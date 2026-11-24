using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EqlLoadouts.Core.Loadouts;

namespace EqlLoadouts.App;

public partial class RestoreWindow : Window
{
    private readonly IReadOnlyList<BackupSnapshot> _backups;
    private List<RestoreTreeNode> _fullNodes = [];
    private List<RestoreTreeNode> _modifiedNodes = [];
    private bool _fullAscending;
    private bool _modifiedAscending;

    public RestoreWindow(IReadOnlyList<BackupSnapshot> backups)
    {
        InitializeComponent();
        _backups = backups;
        ShowTree(FullTree, GameFileSaver.FullFolderName, dateAscending: false, out _fullNodes);
        ShowTree(ModifiedTree, GameFileSaver.ModifiedFolderName, dateAscending: false, out _modifiedNodes);
    }

    public IReadOnlyList<string> SelectedSources { get; private set; } = [];

    private void FullSort_Click(object sender, RoutedEventArgs e)
    {
        _fullAscending = !_fullAscending;
        ShowTree(FullTree, GameFileSaver.FullFolderName, _fullAscending, out _fullNodes);
        SetSortButton(FullSort, _fullAscending);
        RefreshPrompt();
    }

    private void ModifiedSort_Click(object sender, RoutedEventArgs e)
    {
        _modifiedAscending = !_modifiedAscending;
        ShowTree(ModifiedTree, GameFileSaver.ModifiedFolderName, _modifiedAscending, out _modifiedNodes);
        SetSortButton(ModifiedSort, _modifiedAscending);
        RefreshPrompt();
    }

    private static void SetSortButton(Button button, bool ascending)
    {
        button.Content = ascending ? "\u2191" : "\u2193";
        button.ToolTip = ascending ? "Sorted ascending. Click to reverse." : "Sorted descending. Click to reverse.";
    }

    private void Check_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not CheckBox { DataContext: RestoreTreeNode node })
        {
            return;
        }

        ApplyCheck(node);
        e.Handled = true;
    }

    private void Check_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space && e.Key != Key.Enter)
        {
            return;
        }

        if (sender is not CheckBox { DataContext: RestoreTreeNode node })
        {
            return;
        }

        ApplyCheck(node);
        e.Handled = true;
    }

    private void ApplyCheck(RestoreTreeNode node)
    {
        node.Toggle();
        if (node.IsChecked == true)
        {
            ClearOtherBackup(node.BackupKind);
        }

        RefreshPrompt();
    }

    private void ClearOtherBackup(string kind)
    {
        var other = kind == GameFileSaver.FullFolderName ? _modifiedNodes : _fullNodes;
        foreach (var node in Enumerate(other))
        {
            if (node.CanCheck)
            {
                node.SetChecked(false, notifyParent: false);
            }
        }
    }

    private void ShowTree(TreeView tree, string kind, bool dateAscending, out List<RestoreTreeNode> nodes)
    {
        nodes = Build(_backups, kind, dateAscending);
        tree.ItemsSource = nodes;
    }

    private static List<RestoreTreeNode> Build(IReadOnlyList<BackupSnapshot> backups, string kind, bool dateAscending)
    {
        var sign = dateAscending ? 1 : -1;
        var dates = backups.Where(backup => backup.Kind == kind).ToList();
        dates.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase) * sign);

        var roots = new List<RestoreTreeNode>();
        foreach (var date in dates)
        {
            if (date.Loadouts.Count == 0)
            {
                continue;
            }

            var dateNode = new RestoreTreeNode(date.Name) { IsExpanded = false, BackupKind = kind };
            foreach (var server in date.Loadouts
                .GroupBy(loadout => loadout.ServerCharacter, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
            {
                var serverNode = new RestoreTreeNode(server.Key, RestoreNodeRole.Server) { IsExpanded = true, BackupKind = kind };
                foreach (var loadout in server.OrderBy(item => item.LoadoutName, StringComparer.OrdinalIgnoreCase))
                {
                    var loadoutNode = new RestoreTreeNode(loadout.LoadoutName, date.Name, server.Key, loadout)
                    {
                        Parent = serverNode,
                        BackupKind = kind,
                    };
                    serverNode.Children.Add(loadoutNode);
                }

                dateNode.Children.Add(serverNode);
            }

            roots.Add(dateNode);
        }

        var latest = roots.OrderByDescending(node => node.Label, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        if (latest is not null)
        {
            latest.IsExpanded = true;
        }

        return roots;
    }

    private void RefreshPrompt()
    {
        var count = CheckedLoadouts().Count();
        ChoicePrompt.Text = count switch
        {
            0 => "Check the loadouts to restore.",
            1 => "1 loadout checked.",
            _ => $"{count} loadouts checked.",
        };
    }

    private IEnumerable<RestoreTreeNode> CheckedLoadouts()
        => Enumerate(_fullNodes).Concat(Enumerate(_modifiedNodes))
            .Where(node => node.Role == RestoreNodeRole.Loadout && node.IsChecked == true && node.Loadout is not null);

    private static IEnumerable<RestoreTreeNode> Enumerate(IEnumerable<RestoreTreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Enumerate(node.Children))
            {
                yield return child;
            }
        }
    }

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        var loadouts = CheckedLoadouts().ToList();
        if (loadouts.Count == 0)
        {
            MessageBox.Show(this, "Check a loadout.", "Restore", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var kinds = loadouts.Select(node => node.BackupKind).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (kinds.Count > 1)
        {
            MessageBox.Show(this, "Choose FULL or MODIFIED, not both.", "Restore", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var character = CharacterBox.IsChecked == true;
        var windows = WindowsBox.IsChecked == true;
        if (!character && !windows)
        {
            MessageBox.Show(this, "Select Character, Windows, or both.", "Restore", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var selected = new List<string>();
        var lines = new List<string>();
        var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in loadouts)
        {
            var loadout = node.Loadout!;
            var summary = node.Date + "    " + node.ServerCharacter + "    " + loadout.LoadoutName;
            if (character && loadout.CharacterSource is not null)
            {
                if (!TryAddDestination(destinations, loadout.CharacterGameFile, loadout.CharacterSource))
                {
                    return;
                }

                selected.Add(loadout.CharacterSource);
                lines.Add(summary + "  Character");
            }

            if (windows && loadout.UiSource is not null)
            {
                if (!TryAddDestination(destinations, loadout.UiGameFile, loadout.UiSource))
                {
                    return;
                }

                selected.Add(loadout.UiSource);
                lines.Add(summary + "  Windows");
            }
        }

        if (selected.Count == 0)
        {
            MessageBox.Show(this, "None of the checked loadouts include the selected files.", "Restore", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var choice = MessageBox.Show(
            this,
            "Copy these files back into the EverQuest Legends folder?" + Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, lines),
            "Restore",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (choice != MessageBoxResult.Yes)
        {
            return;
        }

        SelectedSources = selected;
        DialogResult = true;
    }

    private bool TryAddDestination(HashSet<string> destinations, string? gameFile, string source)
    {
        var name = string.IsNullOrWhiteSpace(gameFile) ? Path.GetFileName(source) : gameFile;
        if (destinations.Add(name))
        {
            return true;
        }

        MessageBox.Show(
            this,
            $"More than one backup file restores {name}. Nothing was restored.",
            "Restore",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        return false;
    }
}

public enum RestoreNodeRole
{
    Date,
    Server,
    Loadout,
}

public sealed class RestoreTreeNode : INotifyPropertyChanged
{
    public RestoreTreeNode(string label, RestoreNodeRole role = RestoreNodeRole.Date)
    {
        Label = label;
        Role = role;
    }

    public RestoreTreeNode(string label, string date, string serverCharacter, BackupLoadout loadout)
    {
        Label = label;
        Date = date;
        ServerCharacter = serverCharacter;
        Loadout = loadout;
        Role = RestoreNodeRole.Loadout;
    }

    public string Label { get; }

    public string Date { get; } = string.Empty;

    public string ServerCharacter { get; } = string.Empty;

    public BackupLoadout? Loadout { get; }

    public RestoreNodeRole Role { get; }

    public string BackupKind { get; set; } = string.Empty;

    public RestoreTreeNode? Parent { get; set; }

    public bool CanCheck => Role is RestoreNodeRole.Server or RestoreNodeRole.Loadout;

    public Visibility CheckVisibility => CanCheck ? Visibility.Visible : Visibility.Collapsed;

    public string CheckName => Role == RestoreNodeRole.Server
        ? "Include every loadout for " + Label
        : "Include " + Label;

    public ObservableCollection<RestoreTreeNode> Children { get; } = [];

    private bool _isExpanded = true;
    private bool _isSelected;
    private bool? _isChecked = false;

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value)
            {
                return;
            }

            _isExpanded = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public bool? IsChecked
    {
        get => _isChecked;
        private set
        {
            if (_isChecked == value)
            {
                return;
            }

            _isChecked = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
        }
    }

    public void Toggle()
    {
        if (Role == RestoreNodeRole.Loadout)
        {
            SetChecked(IsChecked != true, notifyParent: true);
            return;
        }

        if (Role != RestoreNodeRole.Server)
        {
            return;
        }

        var checkAll = IsChecked != true;
        foreach (var child in Children)
        {
            child.SetChecked(checkAll, notifyParent: false);
        }

        IsChecked = checkAll;
    }

    public void SetChecked(bool value, bool notifyParent)
    {
        IsChecked = value;
        if (notifyParent)
        {
            Parent?.RefreshFromChildren();
        }
    }

    public void RefreshFromChildren()
    {
        if (Role != RestoreNodeRole.Server || Children.Count == 0)
        {
            return;
        }

        var checkedCount = Children.Count(child => child.IsChecked == true);
        IsChecked = checkedCount == Children.Count
            ? true
            : checkedCount == 0 ? false : null;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
