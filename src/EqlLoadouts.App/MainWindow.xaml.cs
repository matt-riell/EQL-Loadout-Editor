using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using EqlLoadouts.App.Controls;
using EqlLoadouts.App.Models;
using EqlLoadouts.App.Settings;
using EqlLoadouts.Core.Layout;
using EqlLoadouts.Core.Loadouts;

namespace EqlLoadouts.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private bool _syncing;
    private bool _deferredClassPrompt;
    private FullScreenRestore? _fullScreen;
    private bool _placingWindow;
    private bool _spellDragArmed;
    private Point _spellDragOrigin;
    private int? _draggedSpellId;
    private bool _gemDragArmed;
    private Point _gemDragOrigin;
    private SpellGem? _draggedGem;
    private readonly System.Collections.ObjectModel.ObservableCollection<GroupToggle> _overflowGroups = [];
    private OverflowPanel? _groupPanel;

    public MainWindow()
    {
        _viewModel = new MainViewModel(new AppSettingsStore());
        _viewModel.ConfirmContentClasses = ConfirmContentClasses;
        _viewModel.ConfirmCharacterLevel = ConfirmCharacterLevel;
        _viewModel.OptionsChanged += SyncCombos;
        _viewModel.SectionSelectionChanged += SyncSection;
        DataContext = _viewModel;
        InitializeComponent();
        Canvas.ItemIcon = _viewModel.ItemIcon;
        Canvas.SpellIcon = _viewModel.SpellIcon;
        Canvas.BookIcon = _viewModel.BookIcon;
        Canvas.SpellBookCommandChosen += (_, args) => OnSpellBookCommand(args);
        StateChanged += (_, _) => SaveEditorWindow();
        MoreGroupsList.ItemsSource = _overflowGroups;
        SourceSocialScroll.ScrollChanged += (_, _) => UpdateSocialLinkLine();
        DestinationSocialScroll.ScrollChanged += (_, _) => UpdateSocialLinkLine();
        SocialBoard.SizeChanged += (_, _) => UpdateSocialLinkLine();
        _viewModel.SocialLinkChanged += (_, _) => RevealSocialLink();
        _viewModel.Groups.CollectionChanged += (_, _) =>
            Dispatcher.BeginInvoke(UpdateOverflowGroups, System.Windows.Threading.DispatcherPriority.Loaded);
        Loaded += (_, _) =>
        {
            SyncCombos();
            SyncSection();
            _viewModel.StartSpellPowerScan();
            Dispatcher.BeginInvoke(() =>
            {
                PromptForGameFolder();
                ShowStartupBackup();
                if (_viewModel.ShowInventoryReminder)
                {
                    ShowInventoryReminder();
                }
            }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        };
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        CommitFocusedField();
        if (_viewModel.HasUnsavedChanges)
        {
            var count = _viewModel.UnsavedFileCount;
            var choice = MessageBox.Show(
                this,
                count == 1 ? "Save changes to 1 file before closing?" : $"Save changes to {count} files before closing?",
                "Unsaved changes",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning);
            if (choice == MessageBoxResult.Cancel || (choice == MessageBoxResult.Yes && !SaveCore()))
            {
                e.Cancel = true;
            }
        }

        if (!e.Cancel)
        {
            SaveEditorWindow();
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.Dispose();
        base.OnClosed(e);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        RestoreEditorWindow();
    }

    private void SyncCombos()
    {
        _syncing = true;
        Reselect(SourceServerCombo, _viewModel.SelectedSourceServer);
        Reselect(SourceLoadoutCombo, _viewModel.SelectedSourceLoadout);
        Reselect(DestinationServerCombo, _viewModel.SelectedDestinationServer);
        Reselect(DestinationLoadoutCombo, _viewModel.SelectedDestinationLoadout);
        _syncing = false;
    }

    /// <summary>
    /// The closed combo keeps the text it first showed. Clear the selection, re-read the item text, then select again.
    /// </summary>
    private static void Reselect(ComboBox combo, object? selected)
    {
        combo.SelectedItem = null;
        if (combo.ItemsSource is not null)
        {
            CollectionViewSource.GetDefaultView(combo.ItemsSource).Refresh();
        }

        combo.SelectedItem = selected;
    }

    private void SyncSection()
    {
        Dispatcher.BeginInvoke(ApplySectionSync);
    }

    private void ApplySectionSync()
    {
        _syncing = true;
        SectionList.SelectedItems.Clear();
        EditorList.SelectedItem = null;
        var laidOut = false;
        foreach (var name in _viewModel.LayoutSelection)
        {
            var row = _viewModel.RowForLayout(name);
            if (row is null)
            {
                continue;
            }

            SectionList.SelectedItems.Add(row);
            laidOut = true;
        }

        if (!laidOut && _viewModel.SelectedSectionRow is { } selected)
        {
            if (MainViewModel.IsEditorPick(selected.Item))
            {
                EditorList.SelectedItem = selected;
            }
            else
            {
                SectionList.SelectedItems.Add(selected);
            }
        }

        if (EditorList.SelectedItem is not null)
        {
            EditorList.ScrollIntoView(EditorList.SelectedItem);
        }

        if (SectionList.SelectedItem is not null)
        {
            SectionList.ScrollIntoView(SectionList.SelectedItem);
        }

        _syncing = false;
        SyncEditorTab();
    }

    private void SyncEditorTab()
    {
        var name = _viewModel.SelectedSection?.Name;
        var current = EditorTabs.SelectedItem as TabItem;
        TabItem desired;
        if (current == RawTab && _viewModel.ShowRaw)
        {
            desired = RawTab;
        }
        else if (name == "Socials" && MacrosTab.Visibility == Visibility.Visible)
        {
            desired = MacrosTab;
        }
        else if (name == "SpellLoadouts" && SpellLoadoutsTab.Visibility == Visibility.Visible)
        {
            desired = SpellLoadoutsTab;
        }
        else if (_viewModel.ShowHotbar && StructuredTab.Visibility == Visibility.Visible)
        {
            desired = StructuredTab;
        }
        else
        {
            desired = LayoutTab;
        }

        if (ReferenceEquals(current, desired))
        {
            return;
        }

        _syncing = true;
        try
        {
            EditorTabs.SelectedItem = desired;
        }
        finally
        {
            _syncing = false;
        }
    }

    private void SourceServerCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || _viewModel.SelectionLocked || SourceServerCombo.SelectedItem is not ServerChoice choice)
        {
            return;
        }

        if (!_viewModel.TrySetSourceServer(choice))
        {
            _syncing = true;
            SourceServerCombo.SelectedItem = _viewModel.SelectedSourceServer;
            _syncing = false;
        }
    }

    private void SourceLoadoutCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || _viewModel.SelectionLocked || SourceLoadoutCombo.SelectedItem is not LoadoutChoice choice)
        {
            return;
        }

        if (!_viewModel.TrySetSourceLoadout(choice))
        {
            _syncing = true;
            SourceLoadoutCombo.SelectedItem = _viewModel.SelectedSourceLoadout;
            _syncing = false;
        }
    }

    private void DestinationServerCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || _viewModel.SelectionLocked || DestinationServerCombo.SelectedItem is not ServerChoice choice)
        {
            return;
        }

        if (!_viewModel.TrySetDestinationServer(choice))
        {
            _syncing = true;
            DestinationServerCombo.SelectedItem = _viewModel.SelectedDestinationServer;
            _syncing = false;
        }
    }

    private void DestinationLoadoutCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || _viewModel.SelectionLocked || DestinationLoadoutCombo.SelectedItem is not LoadoutChoice choice)
        {
            return;
        }

        if (!_viewModel.TrySetDestinationLoadout(choice))
        {
            _syncing = true;
            DestinationLoadoutCombo.SelectedItem = _viewModel.SelectedDestinationLoadout;
            _syncing = false;
        }
    }

    private void ArrangeMenu_Click(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is MenuItem { Tag: ArrangeAction action })
        {
            _viewModel.Arrange(action);
        }
    }

    private void GroupToggle_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ToggleButton { DataContext: GroupToggle group } button)
        {
            return;
        }

        button.Focus();
        _viewModel.ToggleGroup(group);
        e.Handled = true;
    }

    private void GroupToggle_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Space or Key.Enter) || sender is not ToggleButton { DataContext: GroupToggle group })
        {
            return;
        }

        _viewModel.ToggleGroup(group);
        e.Handled = true;
    }

    private void SectionFilter_Checked(object sender, RoutedEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            SectionFilterBox.Focus();
            SectionFilterBox.SelectAll();
        }, DispatcherPriority.Input);
    }

    private void GroupBar_OverflowChanged(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is OverflowPanel panel)
        {
            _groupPanel = panel;
            UpdateOverflowGroups();
        }

        e.Handled = true;
    }

    private void UpdateOverflowGroups()
    {
        var barGroups = _viewModel.BarGroups.OfType<GroupToggle>().ToList();
        var fit = _groupPanel?.FitCount ?? barGroups.Count;
        _overflowGroups.Clear();
        foreach (var group in barGroups.Skip(fit))
        {
            _overflowGroups.Add(group);
        }

        MoreGroupsCount.Text = $"+{_overflowGroups.Count}";
        MoreGroupsButton.Visibility = _overflowGroups.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_overflowGroups.Count == 0)
        {
            MoreGroupsPopup.IsOpen = false;
        }
    }

    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.IsDarkTheme = !_viewModel.IsDarkTheme;
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        var about = new SplashWindow { Owner = this };
        about.ShowDialog();
    }

    private void SectionShown_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox box && box.DataContext is SectionRow { Item: { } item })
        {
            _viewModel.SetItemShown(item, box.IsChecked == true);
        }
    }

    private void SectionList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (GroupUnderMouse(e) is not { } group)
        {
            return;
        }

        if (e.OriginalSource is DependencyObject source && FindAncestor<Button>(source) is not null)
        {
            _viewModel.ToggleSectionGroupExpanded(group);
        }
        else
        {
            _viewModel.SelectGroupWindows(group);
        }

        e.Handled = true;
    }

    private void SectionList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Item rows rely on the Extended-mode ListBox: an unselected row becomes the only selection, a selected row keeps the multi-selection.
        // Group headers select their windows. Alignment is offered from the layout, not from this list.
        if (GroupUnderMouse(e) is not { } group)
        {
            return;
        }

        _viewModel.SelectGroupWindows(group);
        e.Handled = true;
    }

    private GroupToggle? GroupUnderMouse(MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source || FindAncestor<CheckBox>(source) is not null)
        {
            return null;
        }

        return ItemsControl.ContainerFromElement(SectionList, source) is ListBoxItem { DataContext: SectionRow { Group: { } group } }
            ? group
            : null;
    }

    private void SectionList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space || SectionList.SelectedItem is not SectionRow row)
        {
            return;
        }

        if (row.Group is { } group)
        {
            _viewModel.SelectGroupWindows(group);
        }
        else if (row.Item is { } item)
        {
            _viewModel.SetItemShown(item, !item.IsShown);
        }

        e.Handled = true;
    }

    private void EditorList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || EditorList.SelectedItem is not SectionRow { Item: { } item })
        {
            return;
        }

        if (!_viewModel.TrySelectSection(item))
        {
            SyncSection();
        }
    }

    private void EditorList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space || EditorList.SelectedItem is not SectionRow { Item: { } item })
        {
            return;
        }

        _viewModel.SetItemShown(item, !item.IsShown);
        e.Handled = true;
    }

    private void SectionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing)
        {
            return;
        }

        var items = SectionList.SelectedItems.OfType<SectionRow>().Select(row => row.Item).OfType<SectionItem>().ToList();
        if (items.Count == 0)
        {
            // A rebuild empties the list. A click that unselects the last window leaves that row in the list.
            var deselected = e.RemovedItems
                .OfType<SectionRow>()
                .Any(row => row.Item is { } item && !MainViewModel.IsEditorPick(item) && SectionList.Items.Contains(row));
            if (deselected)
            {
                _viewModel.ClearWindowSelection();
            }

            return;
        }

        var blocked = e.AddedItems.OfType<SectionRow>().Select(row => row.Item).OfType<SectionItem>().FirstOrDefault(item => item.IsBlocked);
        if (blocked is not null)
        {
            _viewModel.TrySelectSection(blocked);
            SyncSection();
            return;
        }

        var extend = Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (extend)
        {
            _viewModel.ExtendLayoutSelection(items);
            return;
        }

        var clicked = e.AddedItems.OfType<SectionRow>().Select(row => row.Item).OfType<SectionItem>().LastOrDefault() ?? items[^1];
        if (!_viewModel.TrySelectSection(clicked))
        {
            SyncSection();
        }
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = current is Visual ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current);
        }

        return null;
    }

    private void RawKeysSetting_Click(object sender, RoutedEventArgs e)
    {
        SyncEditorTab();
    }

    private void SettingsPopup_Opened(object sender, EventArgs e)
    {
        GameDirectoryBox.Text = _viewModel.GameDirectory;
        GameDirectoryBox.Focus();
        GameDirectoryBox.SelectAll();
    }

    private void SettingsPopup_Closed(object sender, EventArgs e)
    {
        CommitGameDirectory();
    }

    private void ResetWindowSettings_Click(object sender, RoutedEventArgs e)
    {
        var unsaved = _viewModel.HasUnsavedChanges
            ? "\n\nUnsaved edits will be discarded and will not be written to the game files."
            : string.Empty;
        var choice = MessageBox.Show(
            this,
            "Reset the application to its default settings?\n\nThe window, theme, snap, spell icons, stack spacing, reminders, and game folder return to their defaults. Class labels and saved window visibility are cleared. The source and destination loadouts return to the first ones found. The save reminder is shown again." + unsaved,
            "Reset application to defaults",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);
        if (choice != MessageBoxResult.OK)
        {
            return;
        }

        if (_fullScreen is not null)
        {
            ExitFullScreen();
        }

        _viewModel.ResetApplicationToDefaults();
        ApplyDefaultEditorWindow();
        SaveEditorWindow();
        SyncCombos();
        SyncSection();
        SyncEditorTab();
        SettingsPopup.IsOpen = false;
        if (!_viewModel.HasResolution)
        {
            Dispatcher.BeginInvoke(PromptForGameFolder);
        }
    }

    private void ResetViewedClasses_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.IsDebugBuild || _viewModel.EditedLoadout is not { } loadout)
        {
            return;
        }

        var choice = MessageBox.Show(
            this,
            $"Clear the saved classes for {_viewModel.LoadoutCaption(loadout)}?\n\nThe Choose Classes dialog will open so the spell gems can preselect them again.",
            "Reset loadout classes",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);
        if (choice != MessageBoxResult.OK)
        {
            return;
        }

        CloseSettingsAndClassPopups();
        _viewModel.ResetViewedLoadoutClasses();
        ConfirmContentClasses();
    }

    private void ResetAllClasses_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.IsDebugBuild)
        {
            return;
        }

        var current = _viewModel.EditedLoadout is { } loadout
            ? $" Choose Classes will then open for {_viewModel.LoadoutCaption(loadout)}."
            : string.Empty;
        var choice = MessageBox.Show(
            this,
            "Clear the saved classes for every loadout?\n\nEach loadout can be preselected from its spell gems again." + current,
            "Reset all loadout classes",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);
        if (choice != MessageBoxResult.OK)
        {
            return;
        }

        CloseSettingsAndClassPopups();
        _viewModel.ResetAllLoadoutClasses();
        ConfirmContentClasses();
    }

    private void CloseSettingsAndClassPopups()
    {
        SettingsPopup.IsOpen = false;
        ClassPopup.IsOpen = false;
    }

    private const double DefaultEditorWidth = 1440;
    private const double DefaultEditorHeight = 900;

    private void RestoreEditorWindow()
    {
        var placement = _viewModel.EditorWindowPlacement;
        _placingWindow = true;
        try
        {
            if (placement.Width is double width && placement.Height is double height && width >= MinWidth && height >= MinHeight)
            {
                Width = width;
                Height = height;
            }

            if (placement.Left is double left
                && placement.Top is double top
                && IsOnScreen(left, top, Width, Height))
            {
                Left = left;
                Top = top;
            }

            if (placement.Maximized)
            {
                WindowState = WindowState.Maximized;
            }
        }
        finally
        {
            _placingWindow = false;
        }
    }

    private void ApplyDefaultEditorWindow()
    {
        _placingWindow = true;
        try
        {
            if (WindowState != WindowState.Normal)
            {
                WindowState = WindowState.Normal;
            }

            Width = DefaultEditorWidth;
            Height = DefaultEditorHeight;
            var area = SystemParameters.WorkArea;
            Left = Math.Max(area.Left, area.Left + (area.Width - Width) / 2);
            Top = Math.Max(area.Top, area.Top + (area.Height - Height) / 2);
        }
        finally
        {
            _placingWindow = false;
        }
    }

    private void SaveEditorWindow()
    {
        if (_placingWindow || _fullScreen is not null || WindowState == WindowState.Minimized)
        {
            return;
        }

        var maximized = WindowState == WindowState.Maximized;
        var bounds = maximized ? RestoreBounds : new Rect(Left, Top, Width, Height);
        if (bounds.Width < MinWidth
            || bounds.Height < MinHeight
            || double.IsNaN(bounds.Left)
            || double.IsNaN(bounds.Top)
            || double.IsInfinity(bounds.Width)
            || double.IsInfinity(bounds.Height))
        {
            return;
        }

        _viewModel.RememberEditorWindow(bounds.Left, bounds.Top, bounds.Width, bounds.Height, maximized);
    }

    private static bool IsOnScreen(double left, double top, double width, double height)
    {
        var right = left + Math.Max(width, 1);
        var bottom = top + Math.Max(height, 1);
        var screenLeft = SystemParameters.VirtualScreenLeft;
        var screenTop = SystemParameters.VirtualScreenTop;
        var screenRight = screenLeft + SystemParameters.VirtualScreenWidth;
        var screenBottom = screenTop + SystemParameters.VirtualScreenHeight;
        var overlapWidth = Math.Min(right, screenRight) - Math.Max(left, screenLeft);
        var overlapHeight = Math.Min(bottom, screenBottom) - Math.Max(top, screenTop);
        return overlapWidth >= 200 && overlapHeight >= 120;
    }

    private void ClassPopup_Opened(object sender, EventArgs e)
    {
        // Right-align with the button; the panel's template adds a 6px shadow margin on each side.
        ClassPopup.HorizontalOffset = ClassButton.ActualWidth - ClassPanel.Width + 6;
        _viewModel.RefreshClassSuggestions();
    }

    private void ClassSuggestion_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ClassSuggestion suggestion })
        {
            _viewModel.ApplyClassSuggestion(suggestion);
        }
    }

    private void ClassDone_Click(object sender, RoutedEventArgs e)
    {
        ClassPopup.IsOpen = false;
    }

    private bool ConfirmContentClasses() => ConfirmLoadoutProfile(requireLevel: false);

    private bool ConfirmCharacterLevel() => ConfirmLoadoutProfile(requireLevel: true);

    private bool ConfirmLoadoutProfile(bool requireLevel)
    {
        if (!IsVisible)
        {
            if (!_deferredClassPrompt)
            {
                _deferredClassPrompt = true;
                Dispatcher.BeginInvoke(
                    () =>
                    {
                        _deferredClassPrompt = false;
                        if (IsVisible && _viewModel.EditedLoadout is { } pending
                            && (!_viewModel.HasThreeClasses(pending.Id)
                                || (requireLevel && !_viewModel.HasCharacterLevel(pending.Id))))
                        {
                            ConfirmLoadoutProfile(requireLevel);
                        }
                    },
                    DispatcherPriority.ApplicationIdle);
            }

            return false;
        }

        var loadout = _viewModel.EditedLoadout;
        if (loadout is null)
        {
            return false;
        }

        if (_viewModel.HasThreeClasses(loadout.Id) && (!requireLevel || _viewModel.HasCharacterLevel(loadout.Id)))
        {
            return true;
        }

        var options = _viewModel.ClassOptions.Where(option => option.Index >= 0).ToList();
        var dialog = new LoadoutClassesWindow(
            _viewModel.LoadoutCaption(loadout),
            options,
            _viewModel.ClassesForPrompt(loadout.Id),
            allowCancel: true,
            _viewModel.ClassPromptNote(loadout.Id),
            _viewModel.IsDebugBuild ? _viewModel.ClassPredictionDebug(loadout.Id) : null,
            _viewModel.CharacterLevelOf(loadout.Id),
            requireLevel)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true)
        {
            return false;
        }

        _viewModel.AssignClasses(loadout, dialog.Classes);
        _viewModel.SetCharacterLevel(loadout, dialog.Level);
        return _viewModel.HasThreeClasses(loadout.Id)
            && (!requireLevel || _viewModel.HasCharacterLevel(loadout.Id));
    }

    private void DeleteSpellSet_Click(object sender, RoutedEventArgs e)
    {
        var set = _viewModel.SelectedSpellSet;
        if (set is null)
        {
            return;
        }

        var name = set.Name.Length > 0 ? $" \"{set.Name}\"" : string.Empty;
        var choice = MessageBox.Show(
            this,
            $"Delete spell set {set.Index}{name}? Save afterward to remove it from the loadout file.",
            "Delete spell set",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);
        if (choice != MessageBoxResult.OK)
        {
            return;
        }

        _viewModel.DeleteSelectedSpellSet();
    }

    private void AddSpellSet_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.AddEmptySpellSet() && _viewModel.SelectedSpellSet is { } set)
        {
            SpellSetsGrid.ScrollIntoView(set);
        }
    }

    private void OnSpellBookCommand(SpellBookCommandEventArgs args)
    {
        switch (args.Command)
        {
            case SpellBookCommand.Load:
                _viewModel.SelectSpellSet(args.WindowName, args.Index);
                break;
            case SpellBookCommand.Add:
                if (AskSpellSetName() is { } name)
                {
                    _viewModel.AddSpellSet(args.WindowName, name);
                }

                break;
            case SpellBookCommand.Save:
                if (MessageBox.Show(this, $"Do you want to save over set {args.SetName}?", "Save spell set", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    _viewModel.SaveSpellSet(args.WindowName, args.Index);
                }

                break;
            case SpellBookCommand.Delete:
                if (MessageBox.Show(
                        this,
                        $"Delete spell set {args.Index} \"{args.SetName}\"? Save afterward to remove it from the loadout file.",
                        "Delete spell set",
                        MessageBoxButton.OKCancel,
                        MessageBoxImage.Warning) == MessageBoxResult.OK)
                {
                    _viewModel.DeleteSpellSetFromWindow(args.WindowName, args.Index);
                }

                break;
        }
    }

    private string? AskSpellSetName(string? initial = null, string title = "Add New Set")
    {
        var box = new TextBox { MinWidth = 260, MaxLength = 40, Margin = new Thickness(0, 8, 0, 0), Text = initial ?? string.Empty };
        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 88, IsEnabled = box.Text.Trim().Length > 0, HorizontalAlignment = HorizontalAlignment.Right };
        if (TryFindResource("AccentButton") is Style accent)
        {
            ok.Style = accent;
        }

        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 88, Margin = new Thickness(0, 0, 8, 0) };
        box.TextChanged += (_, _) => ok.IsEnabled = box.Text.Trim().Length > 0;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        var dialog = new Window
        {
            Title = title,
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Content = new StackPanel
            {
                Margin = new Thickness(20, 16, 20, 16),
                Children =
                {
                    new TextBlock { Text = "Enter spell set name:" },
                    box,
                    buttons,
                },
            },
        };
        if (TryFindResource("WindowBackground") is System.Windows.Media.Brush background)
        {
            dialog.Background = background;
        }

        if (TryFindResource("TextBrush") is System.Windows.Media.Brush foreground)
        {
            dialog.Foreground = foreground;
        }

        ok.Click += (_, _) => dialog.DialogResult = true;
        dialog.Loaded += (_, _) =>
        {
            box.Focus();
            box.SelectAll();
        };
        return dialog.ShowDialog() == true ? box.Text.Trim() : null;
    }

    private void SaveSuggestion_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.TryBeginSuggestedSave(out var proposed))
        {
            return;
        }

        if (AskSpellSetName(proposed, "Save spell set") is { } name)
        {
            _viewModel.SaveSuggestedSet(name);
        }
    }

    private void ApplyGemSuggestion_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Models.GemSuggestion suggestion })
        {
            _viewModel.ApplyGemSuggestion(suggestion);
        }
    }

    private void RescanPowers_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.RescanSpellPowers();
    }

    private void ToggleSpellSearch_Click(object sender, RoutedEventArgs e) => _viewModel.SpellSearchExpanded = !_viewModel.SpellSearchExpanded;

    private void ToggleSpellDetails_Click(object sender, RoutedEventArgs e) => _viewModel.SpellDetailsExpanded = !_viewModel.SpellDetailsExpanded;

    private void ToggleSpellUpgrade_Click(object sender, RoutedEventArgs e) => _viewModel.SpellUpgradeExpanded = !_viewModel.SpellUpgradeExpanded;

    private void SpellLevelSort_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ActivateSpellLevelSort();
    }

    private void SpellNameSort_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ActivateSpellNameSort();
    }

    private void SpellUpgrade_Enter(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SpellGem { Upgrade: { } upgrade } })
        {
            _viewModel.HoverUpgrade(upgrade);
        }
    }

    private void SpellUpgrade_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SpellGem gem })
        {
            _viewModel.ApplyGemUpgrade(gem);
            e.Handled = true;
        }
    }

    private void SpellUpgrade_Leave(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SpellGem { Upgrade: { } upgrade } })
        {
            _viewModel.ClearUpgradeHover(upgrade);
        }
    }

    private static bool IsSpellUpgrade(DependencyObject? current)
    {
        while (current is not null)
        {
            if (current is FrameworkElement { Tag: "SpellUpgrade" })
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private void SpellPanel_Enter(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ISpellDetails spell })
        {
            _viewModel.HoverSpell(spell);
        }
    }

    private void SpellPanel_Leave(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ISpellDetails spell })
        {
            _viewModel.ClearSpellHover(spell);
        }
    }

    private void SpellList_Move(object sender, MouseEventArgs e)
    {
        if (sender is ListBox list)
        {
            RefreshSpellHover(list);
        }
    }

    private void SpellList_Leave(object sender, MouseEventArgs e)
    {
        if (sender is ListBox list)
        {
            ClearSpellHoverFrom(list);
        }
    }

    private void SpellList_Wheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ListBox list)
        {
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            if (list.IsLoaded)
            {
                RefreshSpellHover(list);
            }
        }, DispatcherPriority.Background);
    }

    private void RefreshSpellHover(ListBox list)
    {
        if (!list.IsMouseOver)
        {
            ClearSpellHoverFrom(list);
            return;
        }

        var spell = SpellPanelAt(list.InputHitTest(Mouse.GetPosition(list)) as DependencyObject, list);
        if (spell is null)
        {
            ClearSpellHoverFrom(list);
            return;
        }

        _viewModel.HoverSpell(spell);
    }

    private void ClearSpellHoverFrom(ListBox list)
    {
        if (_viewModel.HoveredSpellDetail is { } spell && list.Items.Contains(spell))
        {
            _viewModel.ClearSpellHover(spell);
        }
    }

    private static ISpellDetails? SpellPanelAt(DependencyObject? current, ListBox list)
    {
        while (current is not null && !ReferenceEquals(current, list))
        {
            if (current is FrameworkElement { Tag: "SpellPanel", DataContext: ISpellDetails spell })
            {
                return spell;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private void SpellSearch_Press(object sender, MouseButtonEventArgs e)
    {
        _spellDragArmed = sender is ListBox list
            && e.OriginalSource is DependencyObject source
            && FindAncestor<Button>(source) is null
            && ItemsControl.ContainerFromElement(list, source) is ListBoxItem;
        _spellDragOrigin = e.GetPosition(null);
    }

    private void SpellSearch_Drag(object sender, MouseEventArgs e)
    {
        if (!_spellDragArmed || e.LeftButton != MouseButtonState.Pressed || sender is not ListBox list)
        {
            return;
        }

        var position = e.GetPosition(null);
        if (Math.Abs(position.X - _spellDragOrigin.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(position.Y - _spellDragOrigin.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        if (e.OriginalSource is not DependencyObject source
            || ItemsControl.ContainerFromElement(list, source) is not ListBoxItem { DataContext: SpellSearchHit hit })
        {
            _spellDragArmed = false;
            return;
        }

        _spellDragArmed = false;
        _draggedSpellId = hit.Id;
        try
        {
            DragDrop.DoDragDrop(list, new DataObject(SpellSearchHit.DragFormat, hit.Id.ToString(CultureInfo.InvariantCulture)), DragDropEffects.Copy);
        }
        finally
        {
            _draggedSpellId = null;
        }
    }

    private void Gem_Press(object sender, MouseButtonEventArgs e)
    {
        _gemDragArmed = sender is FrameworkElement { DataContext: SpellGem gem }
            && !gem.IsEmpty
            && e.OriginalSource is DependencyObject source
            && FindAncestor<Button>(source) is null
            && !IsSpellUpgrade(source);
        _gemDragOrigin = e.GetPosition(null);
    }

    private void Gem_Drag(object sender, MouseEventArgs e)
    {
        if (!_gemDragArmed || e.LeftButton != MouseButtonState.Pressed || sender is not FrameworkElement { DataContext: SpellGem gem } || gem.IsEmpty)
        {
            return;
        }

        var position = e.GetPosition(null);
        if (Math.Abs(position.X - _gemDragOrigin.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(position.Y - _gemDragOrigin.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _gemDragArmed = false;
        _draggedGem = gem;
        try
        {
            DragDrop.DoDragDrop((DependencyObject)sender, new DataObject("EqlLoadoutGem", gem.Number.ToString(CultureInfo.InvariantCulture)), DragDropEffects.Move);
        }
        finally
        {
            _draggedGem = null;
        }
    }

    private void Gem_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = sender is FrameworkElement { DataContext: SpellGem target } && CanDropOnGem(target, e)
            ? _draggedGem is not null ? DragDropEffects.Move : DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void Gem_Drop(object sender, DragEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SpellGem gem })
        {
            if (_draggedGem is { } source)
            {
                _viewModel.SwapGems(source, gem);
            }
            else if (DraggedSpellId(e) is int spellId)
            {
                _viewModel.PlaceSpellOnGem(gem, spellId);
            }
        }

        e.Handled = true;
    }

    private bool CanDropOnGem(SpellGem target, DragEventArgs e)
    {
        if (_draggedGem is { } source)
        {
            return !ReferenceEquals(source, target) && !source.IsEmpty;
        }

        return HasDraggedSpell(e);
    }

    private void Gem_Clear(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SpellGem gem })
        {
            gem.Clear();
        }
    }

    private bool HasDraggedSpell(DragEventArgs e) => DraggedSpellId(e) is not null;

    private int? DraggedSpellId(DragEventArgs e)
    {
        if (_draggedSpellId is int id)
        {
            return id;
        }

        return e.Data.GetData(SpellSearchHit.DragFormat) is string raw
            && int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private void GameDirectoryBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitGameDirectory();
            SettingsPopup.IsOpen = false;
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            GameDirectoryBox.Text = _viewModel.GameDirectory;
            SettingsPopup.IsOpen = false;
            e.Handled = true;
        }
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        SettingsPopup.IsOpen = false;
        if (BrowseForGameFolder())
        {
            PromptForGameFolder();
        }
    }

    private void LocateGameFolder_Click(object sender, RoutedEventArgs e)
    {
        if (BrowseForGameFolder())
        {
            PromptForGameFolder();
        }
    }

    private bool BrowseForGameFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select the EverQuest Legends folder",
            InitialDirectory = System.IO.Directory.Exists(_viewModel.GameDirectory) ? _viewModel.GameDirectory : null,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return false;
        }

        _viewModel.TrySetGameDirectory(dialog.FolderName);
        return true;
    }

    private void PromptForGameFolder()
    {
        while (!_viewModel.HasResolution)
        {
            var where = string.IsNullOrWhiteSpace(_viewModel.GameDirectory)
                ? "No EverQuest Legends folder is set."
                : $"The screen resolution could not be read from eqclient.ini in:\n{_viewModel.GameDirectory}";
            var choice = MessageBox.Show(
                this,
                $"{where}\n\nThe layout can't be shown or edited until the screen resolution is known. Locate the EverQuest Legends folder now?",
                "Game folder needed",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);
            if (choice != MessageBoxResult.OK || !BrowseForGameFolder())
            {
                return;
            }
        }
    }

    private void GameDirectoryBox_LostFocus(object sender, RoutedEventArgs e)
    {
        CommitGameDirectory();
    }

    private void CommitGameDirectory()
    {
        var path = GameDirectoryBox.Text.Trim();
        if (string.Equals(path, _viewModel.GameDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!_viewModel.TrySetGameDirectory(path))
        {
            GameDirectoryBox.Text = _viewModel.GameDirectory;
        }
        else if (!_viewModel.HasResolution)
        {
            Dispatcher.BeginInvoke(PromptForGameFolder);
        }
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.CopySelectedSection();
    }

    private void InventoryExport_Click(object sender, RoutedEventArgs e)
    {
        ShowInventoryReminder();
    }

    private void ShowInventoryReminder()
    {
        if (_viewModel.InventoryReminderText.Length == 0)
        {
            return;
        }

        var dialog = new InventoryReminderWindow(_viewModel.InventoryReminderText, _viewModel.InventoryCommand, !_viewModel.ShowInventoryReminder)
        {
            Owner = this,
        };
        dialog.ShowDialog();
        _viewModel.ShowInventoryReminder = !dialog.DontShowAgain;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        SaveCore();
    }

    private void SaveCopies_Click(object sender, RoutedEventArgs e)
    {
        ShowBackupResult(_viewModel.SaveLoadoutCopies());
    }

    private static readonly TimeSpan StartupFullBackupInterval = TimeSpan.FromMinutes(15);

    private void ShowStartupBackup()
    {
        if (!GameFileSaver.FullBackupIsDue(_viewModel.GameDirectory, DateTimeOffset.Now, StartupFullBackupInterval))
        {
            return;
        }

        ShowBackupResult(_viewModel.SaveLoadoutCopies());
    }

    private void ShowBackupResult(SaveResult result)
    {
        if (result.Success)
        {
            new BackupResultWindow(result) { Owner = this }.ShowDialog();
            return;
        }

        var skipped = result.Message == MainViewModel.NoFolderBackupNotice
            || result.Message == MainViewModel.NoCharactersBackupNotice;
        MessageBox.Show(
            this,
            result.Message,
            skipped ? "Backup" : "Backup failed",
            MessageBoxButton.OK,
            skipped ? MessageBoxImage.Information : MessageBoxImage.Error);
    }

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_viewModel.GameDirectory) || !Directory.Exists(_viewModel.GameDirectory))
        {
            MessageBox.Show(this, "Choose the EverQuest Legends folder before restoring a backup.", "Restore", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new RestoreWindow(_viewModel.ListBackups()) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.SelectedSources.Count == 0)
        {
            return;
        }

        if (_viewModel.HasUnsavedChanges)
        {
            var choice = MessageBox.Show(
                this,
                "You have unsaved edits. Restoring replaces the game files and reloads them. Continue?",
                "Restore",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (choice != MessageBoxResult.Yes)
            {
                return;
            }
        }

        var result = _viewModel.RestoreBackup(dialog.SelectedSources);
        if (!result.Success)
        {
            MessageBox.Show(this, result.Message, "Restore failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Position_LostFocus(object sender, RoutedEventArgs e)
    {
        _viewModel.CommitPositionFields();
    }

    private void Canvas_SelectionRequested(object sender, CanvasSelectionEventArgs e)
    {
        _viewModel.SelectCanvasWindows(e.Names, e.Extend);
    }

    private void Canvas_LayoutEdited(object sender, LayoutEditedEventArgs e)
    {
        if (e.SelectingOnly)
        {
            _viewModel.SelectCanvasWindow(e.Name, e.ExtendSelection);
            return;
        }

        _viewModel.ApplyCanvasEdits(e.Edits.Select(edit => (edit.Name, edit.Rect)).ToList(), e.Resized);
        if (!_viewModel.LayoutSelection.Contains(e.Name, StringComparer.Ordinal))
        {
            _viewModel.SelectCanvasWindow(e.Name, extend: false);
        }
    }

    private void EditorTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, EditorTabs) || _syncing)
        {
            return;
        }

        if (EditorTabs.SelectedItem == RawTab)
        {
            _viewModel.ReloadRawKeys();
        }
        else if (EditorTabs.SelectedItem == MacrosTab)
        {
            OpenEditorTab("Socials");
        }
        else if (EditorTabs.SelectedItem == SpellLoadoutsTab)
        {
            OpenEditorTab("SpellLoadouts");
        }
        else if (EditorTabs.SelectedItem == LayoutTab && MainViewModel.IsEditorPick(_viewModel.SelectedSection))
        {
            _viewModel.ReturnToLayout();
        }
        else if (EditorTabs.SelectedItem == StructuredTab)
        {
            _viewModel.ShowStructuredEditor();
        }
    }

    private void OpenEditorTab(string name)
    {
        if (_viewModel.SelectedSection?.Name == name)
        {
            return;
        }

        if (!_viewModel.OpenCharacterEditor(name))
        {
            SyncEditorTab();
        }
    }

    private void MacroColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: MacroColorChoice choice })
        {
            choice.Choose();
        }
    }

    private void DeleteRaw_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.DeleteSelectedRawKey();
    }

    private void RawGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;
        var byValue = ReferenceEquals(e.Column, RawValueColumn);
        var ascending = e.Column.SortDirection != ListSortDirection.Ascending;
        _viewModel.SortRawKeys(byValue, ascending);
        var direction = ascending ? ListSortDirection.Ascending : ListSortDirection.Descending;
        RawKeyColumn.SortDirection = byValue ? null : direction;
        RawValueColumn.SortDirection = byValue ? direction : null;
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        CommitFocusedField();
        _viewModel.Undo();
    }

    private void Redo_Click(object sender, RoutedEventArgs e)
    {
        CommitFocusedField();
        _viewModel.Redo();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.None && (e.Key == Key.F12 || (e.Key == Key.Escape && _fullScreen is not null)))
        {
            ToggleFullScreen();
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) || !Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            return;
        }

        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (e.Key == Key.S)
        {
            SaveCore();
            e.Handled = true;
            return;
        }

        if (Keyboard.FocusedElement is TextBox)
        {
            return;
        }

        if (e.Key == Key.A && !shift)
        {
            _viewModel.SelectShownWindows();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Z && !shift)
        {
            _viewModel.Undo();
            e.Handled = true;
        }
        else if (e.Key == Key.Y || (e.Key == Key.Z && shift))
        {
            _viewModel.Redo();
            e.Handled = true;
        }
    }

    private void Canvas_FullScreenToggled(object? sender, EventArgs e) => ToggleFullScreen();

    private void ToggleFullScreen()
    {
        if (_fullScreen is null)
        {
            EnterFullScreen();
        }
        else
        {
            ExitFullScreen();
        }
    }

    private void EnterFullScreen()
    {
        if (!_viewModel.HasResolution || Canvas.Parent is not Panel parent)
        {
            return;
        }

        CommitFocusedField();
        EditorTabs.SelectedItem = LayoutTab;
        _fullScreen = new FullScreenRestore(WindowState, WindowStyle, ResizeMode, parent, parent.Children.IndexOf(Canvas));
        parent.Children.Remove(Canvas);
        FullScreenHost.Child = Canvas;
        FullScreenHost.Visibility = Visibility.Visible;
        MainLayout.Visibility = Visibility.Collapsed;
        Canvas.IsFullScreen = true;

        // A borderless window only covers the taskbar if it is maximized after the style changes.
        _placingWindow = true;
        try
        {
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
        }
        finally
        {
            _placingWindow = false;
        }

        Focus();
    }

    private void ExitFullScreen()
    {
        if (_fullScreen is not { } restore)
        {
            return;
        }

        _fullScreen = null;
        FullScreenHost.Child = null;
        restore.Parent.Children.Insert(Math.Clamp(restore.Index, 0, restore.Parent.Children.Count), Canvas);
        FullScreenHost.Visibility = Visibility.Collapsed;
        MainLayout.Visibility = Visibility.Visible;
        Canvas.IsFullScreen = false;

        _placingWindow = true;
        try
        {
            WindowState = WindowState.Normal;
            WindowStyle = restore.Style;
            ResizeMode = restore.Resize;
            WindowState = restore.State;
        }
        finally
        {
            _placingWindow = false;
        }

        Focus();
    }

    private sealed record FullScreenRestore(WindowState State, WindowStyle Style, ResizeMode Resize, Panel Parent, int Index);

    private sealed record SocialDrag(int Page, int Slot)
    {
        public const string Format = "EqlSocialCopy";
    }

    private void CommitFocusedField()
    {
        if (Keyboard.FocusedElement is TextBox)
        {
            SaveButton.Focus();
        }
    }

    private Point? _socialDragStart;
    private SocialButton? _socialDragButton;
    private SocialButton? _socialPreviewButton;
    private Popup? _socialDragPopup;
    private bool _socialDragStarted;
    private (int SourcePage, int SourceSlot, int DestinationPage, int DestinationSlot)? _pendingSocialDrop;

    private void SocialButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: SocialButton button })
        {
            return;
        }

        _socialDragStart = e.GetPosition(null);
        _socialDragButton = button;
        _socialDragStarted = false;
    }

    private void SocialButton_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed
            || _socialDragStart is not Point origin
            || _socialDragButton is not { Side: SocialSide.Source, IsEmpty: false } button
            || sender is not FrameworkElement element
            || !ReferenceEquals(element.DataContext, button))
        {
            return;
        }

        var position = e.GetPosition(null);
        if (Math.Abs(position.X - origin.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(position.Y - origin.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _socialDragStarted = true;
        GiveFeedbackEventHandler feedback = SocialDrag_GiveFeedback;
        element.GiveFeedback += feedback;
        try
        {
            OpenSocialDragPopup(button);
            DragDrop.DoDragDrop(element, new DataObject(SocialDrag.Format, new SocialDrag(button.Page, button.Slot)), DragDropEffects.Copy);
        }
        finally
        {
            element.GiveFeedback -= feedback;
            CloseSocialDragPopup();
            ClearDestinationPreview();
            _socialDragStarted = false;
            _socialDragStart = null;
            _socialDragButton = null;
            var pending = _pendingSocialDrop;
            _pendingSocialDrop = null;
            if (pending is { } drop)
            {
                Dispatcher.BeginInvoke(() => _viewModel.QueueSocialDrop(
                    drop.SourcePage,
                    drop.SourceSlot,
                    drop.DestinationPage,
                    drop.DestinationSlot,
                    AskSocialConflict,
                    AskSocialName));
            }
        }
    }

    private void SocialDrag_GiveFeedback(object sender, GiveFeedbackEventArgs e)
    {
        MoveSocialDragPopup();
        var hit = VisualTreeHelper.HitTest(this, Mouse.GetPosition(this))?.VisualHit as DependencyObject;
        var button = SocialButtonFromHit(hit);
        if (button is { Side: SocialSide.Destination } && _socialDragButton is not null)
        {
            ShowDestinationPreview(button);
        }
        else
        {
            ClearDestinationPreview();
        }
    }

    private void SocialButton_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_socialDragStarted || _socialDragButton is null || sender is not FrameworkElement { DataContext: SocialButton button })
        {
            _socialDragStart = null;
            _socialDragButton = null;
            return;
        }

        if (ReferenceEquals(button, _socialDragButton))
        {
            _viewModel.SelectSocialButton(button);
        }

        _socialDragStart = null;
        _socialDragButton = null;
    }

    private void SocialButton_DragOver(object sender, DragEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SocialButton { Side: SocialSide.Destination } target }
            && e.Data.GetDataPresent(SocialDrag.Format))
        {
            e.Effects = DragDropEffects.Copy;
            ShowDestinationPreview(target);
            MoveSocialDragPopup();
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }

        e.Handled = true;
    }

    private void SocialButton_Drop(object sender, DragEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: SocialButton { Side: SocialSide.Destination } target }
            || e.Data.GetData(SocialDrag.Format) is not SocialDrag drag)
        {
            return;
        }

        _pendingSocialDrop = (drag.Page, drag.Slot, target.Page, target.Slot);
        e.Handled = true;
    }

    private void SocialTray_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(SocialDrag.Format) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void SocialTray_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(SocialDrag.Format) is not SocialDrag drag)
        {
            return;
        }

        _pendingSocialDrop = (drag.Page, drag.Slot, drag.Page, drag.Slot);
        e.Handled = true;
    }

    private void ClearSocialCopies_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ClearSocialChanges();
    }

    private void SocialButton_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: SocialButton { Side: SocialSide.Destination } button })
        {
            return;
        }

        if (button.IsEmpty && !button.ShowIncomingName)
        {
            return;
        }

        var clear = new MenuItem { Header = "Clear macro" };
        clear.Click += (_, _) => _viewModel.ClearDestinationSocial(button.Page, button.Slot);
        var menu = new ContextMenu { PlacementTarget = (UIElement)sender };
        menu.Items.Add(clear);
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void RemoveSocialChange_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SocialChange change })
        {
            _viewModel.RemoveSocialChange(change);
        }
    }

    private void SocialChange_Click(object sender, MouseButtonEventArgs e)
    {
        var origin = e.OriginalSource as DependencyObject;
        while (origin is not null)
        {
            if (origin is Button)
            {
                return;
            }

            origin = VisualTreeHelper.GetParent(origin) ?? LogicalTreeHelper.GetParent(origin);
        }

        if (sender is FrameworkElement { DataContext: SocialChange change })
        {
            _viewModel.SelectSocialChange(change);
        }
    }

    private void RemoveQueuedSocial_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: QueuedSocialCopy copy })
        {
            _viewModel.RemoveQueuedSocial(copy);
        }
    }

    private void QueuedSocial_Click(object sender, MouseButtonEventArgs e)
    {
        var origin = e.OriginalSource as DependencyObject;
        while (origin is not null)
        {
            if (origin is Button)
            {
                return;
            }

            origin = VisualTreeHelper.GetParent(origin) ?? LogicalTreeHelper.GetParent(origin);
        }

        if (sender is FrameworkElement { DataContext: QueuedSocialCopy copy })
        {
            _viewModel.SelectQueuedSocial(copy);
        }
    }

    private void OpenSocialDragPopup(SocialButton source)
    {
        CloseSocialDragPopup();
        var (borderBrush, thickness) = SocialChrome(source);
        var preview = new Border
        {
            MinWidth = 72,
            MinHeight = 28,
            Padding = new Thickness(6, 4, 6, 4),
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(thickness),
            BorderBrush = borderBrush,
            Background = BrushOr("SurfaceAlt", Brushes.White),
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = source.Caption,
                FontSize = 11,
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 140,
                Foreground = source.NameBrush,
            },
        };
        _socialDragPopup = new Popup
        {
            AllowsTransparency = true,
            IsHitTestVisible = false,
            Placement = PlacementMode.Relative,
            PlacementTarget = this,
            StaysOpen = true,
            Child = preview,
        };
        MoveSocialDragPopup();
        _socialDragPopup.IsOpen = true;
    }

    private void MoveSocialDragPopup()
    {
        if (_socialDragPopup is null)
        {
            return;
        }

        var point = Mouse.GetPosition(this);
        _socialDragPopup.HorizontalOffset = point.X + 16;
        _socialDragPopup.VerticalOffset = point.Y + 16;
    }

    private void CloseSocialDragPopup()
    {
        if (_socialDragPopup is null)
        {
            return;
        }

        _socialDragPopup.IsOpen = false;
        _socialDragPopup.Child = null;
        _socialDragPopup = null;
    }

    private void ShowDestinationPreview(SocialButton target)
    {
        if (_socialDragButton is not { } source || ReferenceEquals(_socialPreviewButton, target))
        {
            return;
        }

        ClearDestinationPreview();
        var (borderBrush, thickness) = SocialChrome(source);
        target.SetDragPreview(source.Caption, source.NameBrush, borderBrush, thickness);
        _socialPreviewButton = target;
    }

    private void ClearDestinationPreview()
    {
        _socialPreviewButton?.ClearDragPreview();
        _socialPreviewButton = null;
    }

    private (Brush Brush, double Thickness) SocialChrome(SocialButton button)
    {
        if (button.IsQueued)
        {
            return (BrushOr("SocialQueuedBrush", Brushes.White), 2);
        }

        if (button.IsNew)
        {
            return (BrushOr("SocialNewBrush", Brushes.DodgerBlue), 2);
        }

        if (button.IsMismatch)
        {
            return (BrushOr("SocialMismatchBrush", Brushes.Goldenrod), 2);
        }

        if (button.IsDuplicate)
        {
            return (BrushOr("SocialMatchBrush", Brushes.SeaGreen), 2);
        }

        return (BrushOr("BorderBrush", Brushes.Gray), 1);
    }

    private Brush BrushOr(string key, Brush fallback)
    {
        return TryFindResource(key) as Brush ?? fallback;
    }

    private static SocialButton? SocialButtonFromHit(DependencyObject? node)
    {
        while (node is not null)
        {
            if (node is FrameworkElement { DataContext: SocialButton button })
            {
                return button;
            }

            node = VisualTreeHelper.GetParent(node) ?? LogicalTreeHelper.GetParent(node);
        }

        return null;
    }

    private SocialConflictChoice AskSocialConflict(IReadOnlyList<SocialButton> clashes)
    {
        var name = clashes[0].Name.Trim();
        var where = string.Join(", ", clashes.Select(button => string.Create(CultureInfo.InvariantCulture, $"page {button.Page} button {button.Slot}")));
        var overwrite = new Button { Content = "Overwrite", MinWidth = 96, Margin = new Thickness(0, 0, 8, 0) };
        var create = new Button { Content = "New button", MinWidth = 96, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 88 };
        var choice = SocialConflictChoice.Cancel;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        buttons.Children.Add(overwrite);
        buttons.Children.Add(create);
        buttons.Children.Add(cancel);
        var dialog = new Window
        {
            Title = "Same name, different commands",
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Content = new StackPanel
            {
                Margin = new Thickness(20, 16, 20, 16),
                MaxWidth = 460,
                Children =
                {
                    new TextBlock
                    {
                        Text = $"\"{name}\" is on the destination with different commands ({where}).",
                        TextWrapping = TextWrapping.Wrap,
                    },
                    new TextBlock
                    {
                        Text = "Overwrite replaces those destination buttons with the source commands. New button copies it onto an empty destination button under a name you choose.",
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 8, 0, 0),
                    },
                    buttons,
                },
            },
        };
        StylePrompt(dialog);
        overwrite.Click += (_, _) =>
        {
            choice = SocialConflictChoice.Overwrite;
            dialog.DialogResult = true;
        };
        create.Click += (_, _) =>
        {
            choice = SocialConflictChoice.NewButton;
            dialog.DialogResult = true;
        };
        dialog.ShowDialog();
        return choice;
    }

    private string? AskSocialName(string suggested, string? error)
    {
        var box = new TextBox { MinWidth = 280, MaxLength = 40, Margin = new Thickness(0, 8, 0, 0), Text = suggested };
        var ok = new Button { Content = "Create", IsDefault = true, MinWidth = 88, IsEnabled = suggested.Trim().Length > 0, HorizontalAlignment = HorizontalAlignment.Right };
        if (TryFindResource("AccentButton") is Style accent)
        {
            ok.Style = accent;
        }

        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 88, Margin = new Thickness(0, 0, 8, 0) };
        box.TextChanged += (_, _) => ok.IsEnabled = box.Text.Trim().Length > 0;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        var stack = new StackPanel
        {
            Margin = new Thickness(20, 16, 20, 16),
            Children =
            {
                new TextBlock { Text = "Name for the new destination button:", TextWrapping = TextWrapping.Wrap },
                box,
            },
        };
        if (!string.IsNullOrWhiteSpace(error))
        {
            stack.Children.Add(new TextBlock
            {
                Text = error,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0),
                Foreground = BrushOr("WarningBrush", Brushes.DarkOrange),
            });
        }

        stack.Children.Add(buttons);
        var dialog = new Window
        {
            Title = "New macro button",
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Content = stack,
        };
        StylePrompt(dialog);
        ok.Click += (_, _) => dialog.DialogResult = true;
        dialog.Loaded += (_, _) =>
        {
            box.Focus();
            box.SelectAll();
        };
        return dialog.ShowDialog() == true ? box.Text.Trim() : null;
    }

    private void StylePrompt(Window dialog)
    {
        if (TryFindResource("WindowBackground") is Brush background)
        {
            dialog.Background = background;
        }

        if (TryFindResource("TextBrush") is Brush foreground)
        {
            dialog.Foreground = foreground;
        }
    }

    private void RevealSocialLink()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_viewModel.ActiveSocialLink is { } link)
            {
                FindSocialBorder(SocialBoard, SocialSide.Source, link.SourcePage, link.SourceSlot)?.BringIntoView();
                FindSocialBorder(SocialBoard, SocialSide.Destination, link.DestinationPage, link.DestinationSlot)?.BringIntoView();
            }

            Dispatcher.BeginInvoke(UpdateSocialLinkLine, DispatcherPriority.Loaded);
        }, DispatcherPriority.Loaded);
    }

    private void UpdateSocialLinkLine()
    {
        if (SocialLinkCanvas is null)
        {
            return;
        }

        SocialLinkCanvas.Children.Clear();
        if (_viewModel.ActiveSocialLink is not { } link)
        {
            return;
        }

        var source = FindSocialBorder(SocialBoard, SocialSide.Source, link.SourcePage, link.SourceSlot);
        var destination = FindSocialBorder(SocialBoard, SocialSide.Destination, link.DestinationPage, link.DestinationSlot);
        if (source is null || destination is null || source.ActualWidth <= 0 || destination.ActualWidth <= 0)
        {
            return;
        }

        var start = source.TranslatePoint(new Point(source.ActualWidth, source.ActualHeight / 2), SocialLinkCanvas);
        var end = destination.TranslatePoint(new Point(0, destination.ActualHeight / 2), SocialLinkCanvas);
        if (!double.IsFinite(start.X) || !double.IsFinite(start.Y) || !double.IsFinite(end.X) || !double.IsFinite(end.Y))
        {
            return;
        }

        var brush = BrushOr("AccentBrush", Brushes.Goldenrod);
        SocialLinkCanvas.Children.Add(new Line
        {
            X1 = start.X,
            Y1 = start.Y,
            X2 = end.X,
            Y2 = end.Y,
            Stroke = brush,
            StrokeThickness = 2,
            IsHitTestVisible = false,
        });
        var angle = Math.Atan2(end.Y - start.Y, end.X - start.X);
        const double head = 9;
        SocialLinkCanvas.Children.Add(new Polygon
        {
            Fill = brush,
            IsHitTestVisible = false,
            Points = new PointCollection
            {
                end,
                new Point(end.X - head * Math.Cos(angle - Math.PI / 6), end.Y - head * Math.Sin(angle - Math.PI / 6)),
                new Point(end.X - head * Math.Cos(angle + Math.PI / 6), end.Y - head * Math.Sin(angle + Math.PI / 6)),
            },
        });
    }

    private static Border? FindSocialBorder(DependencyObject root, SocialSide side, int page, int slot)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is Border border
                && border.DataContext is SocialButton button
                && button.Side == side
                && button.Page == page
                && button.Slot == slot)
            {
                return border;
            }

            var nested = FindSocialBorder(child, side, page, slot);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private bool SaveCore()
    {
        CommitFocusedField();
        if (!_viewModel.CanSave)
        {
            return true;
        }

        if (_viewModel.ShowSaveCharacterSelectWarning)
        {
            var warning = new SaveWarningWindow { Owner = this };
            if (warning.ShowDialog() != true)
            {
                return false;
            }

            if (warning.DontShowAgain)
            {
                _viewModel.ShowSaveCharacterSelectWarning = false;
            }
        }

        var prompt = _viewModel.DescribeSave();
        if (!prompt.HasSavableChanges)
        {
            MessageBox.Show(
                this,
                _viewModel.HasUnsavedChanges
                    ? "The open files differ from the last save, but no window or character section changed. Nothing was written."
                    : "The highlighted windows already match the destination. Nothing was written.",
                "Save",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return false;
        }

        var dialog = new SaveChangesWindow(prompt) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return false;
        }

        if (_viewModel.NeedsFullBackup)
        {
            MessageBox.Show(
                this,
                MainViewModel.FullBackupNotice,
                "Full backup",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            var backup = _viewModel.SaveLoadoutCopies();
            if (!backup.Success)
            {
                MessageBox.Show(this, backup.Message, "Full backup failed", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        if (dialog.SaveWindows && _viewModel.ChatFilterCopyQuestion() is { } question)
        {
            var copyFilters = MessageBox.Show(
                this,
                question,
                "Chat tabs",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (copyFilters == MessageBoxResult.Yes)
            {
                _viewModel.CopyChatFiltersToOtherLoadouts();
            }
        }

        var result = _viewModel.SaveChanges(dialog.SaveWindows, dialog.SaveCharacter, dialog.CharacterMode);
        if (!result.Success)
        {
            MessageBox.Show(this, result.Message, "Save failed", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }

        return true;
    }
}

public sealed class SectionRowTemplateSelector : DataTemplateSelector
{
    public DataTemplate? GroupTemplate { get; set; }

    public DataTemplate? ItemTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        return item is SectionRow { IsGroup: true } ? GroupTemplate : ItemTemplate;
    }
}
