using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace EqlLoadouts.App;

public partial class LoadoutClassesWindow : Window
{
    private readonly bool _allowCancel;
    private readonly bool _requireLevel;
    private readonly int[] _selected = [-1, -1, -1];
    private bool _ready;

    public LoadoutClassesWindow(
        string loadoutName,
        IReadOnlyList<ClassOption> options,
        IReadOnlyList<int> current,
        bool allowCancel,
        string? predictionNote = null,
        string? debugReport = null,
        int? level = null,
        bool requireLevel = false)
    {
        InitializeComponent();
        _allowCancel = allowCancel;
        _requireLevel = requireLevel;
        var classesReady = current.Count >= 3 && current.Take(3).All(index => index >= 0) && current.Take(3).Distinct().Count() == 3;
        BodyText.Text = requireLevel
            ? classesReady
                ? $"{loadoutName} needs a character level before its spell loadouts can be opened."
                : $"{loadoutName} needs 3 different classes and a level before its spell loadouts can be opened."
            : $"{loadoutName} needs 3 different classes before its macros or spell loadouts can be changed.";
        if (!string.IsNullOrWhiteSpace(predictionNote))
        {
            PredictionText.Text = predictionNote;
            PredictionText.Visibility = Visibility.Visible;
        }

        if (debugReport is not null)
        {
            DebugText.Text = debugReport;
            DebugButton.Visibility = Visibility.Visible;
        }

        CancelButton.Visibility = allowCancel ? Visibility.Visible : Visibility.Collapsed;
        for (var slot = 0; slot < _selected.Length; slot++)
        {
            _selected[slot] = slot < current.Count && current[slot] >= 0 ? current[slot] : -1;
        }

        if (level is int known)
        {
            LevelBox.Text = known.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        foreach (var box in Boxes)
        {
            box.ItemsSource = options;
        }

        Loaded += (_, _) =>
        {
            ApplySelections();
            if (requireLevel && classesReady)
            {
                LevelBox.Focus();
                LevelBox.SelectAll();
            }
        };
        ApplySelections();
    }

    public IReadOnlyList<int> Classes => [Value(Class1), Value(Class2), Value(Class3)];

    public int? Level => MainViewModel.TryCharacterLevel(LevelBox.Text, out var level) ? level : null;

    private ComboBox[] Boxes => [Class1, Class2, Class3];

    private void ApplySelections()
    {
        Apply(Class1, _selected[0]);
        Apply(Class2, _selected[1]);
        Apply(Class3, _selected[2]);
        UpdateContinue();
    }

    private static void Apply(ComboBox box, int classIndex)
    {
        if (classIndex < 0 || box.ItemsSource is not IEnumerable<ClassOption> options)
        {
            return;
        }

        var match = options.FirstOrDefault(option => option.Index == classIndex);
        if (match is not null)
        {
            box.SelectedItem = match;
        }
    }

    private static int Value(ComboBox box) => box.SelectedValue is int index ? index : -1;

    private void Class_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded)
        {
            UpdateContinue();
        }
    }

    private void Level_Changed(object sender, TextChangedEventArgs e)
    {
        if (IsLoaded)
        {
            UpdateContinue();
        }
    }

    private void UpdateContinue()
    {
        var picks = Classes;
        var classesReady = picks.All(index => index >= 0) && picks.Distinct().Count() == 3;
        var levelText = LevelBox.Text.Trim();
        var levelReady = MainViewModel.TryCharacterLevel(levelText, out _);
        var levelProblem = levelText.Length > 0 && !levelReady;
        _ready = classesReady && (!_requireLevel || levelReady) && !levelProblem;
        ContinueButton.IsEnabled = _ready;
        if (!classesReady && picks.Any(index => index >= 0))
        {
            Note.Text = "Choose 3 different classes.";
            Note.Visibility = Visibility.Visible;
        }
        else if (_requireLevel && !levelReady)
        {
            Note.Text = $"Enter a level from {MainViewModel.MinCharacterLevel} to {MainViewModel.MaxCharacterLevel}.";
            Note.Visibility = Visibility.Visible;
        }
        else if (levelProblem)
        {
            Note.Text = $"Level must be from {MainViewModel.MinCharacterLevel} to {MainViewModel.MaxCharacterLevel}, or left blank.";
            Note.Visibility = Visibility.Visible;
        }
        else
        {
            Note.Visibility = Visibility.Collapsed;
        }
    }

    private void Continue_Click(object sender, RoutedEventArgs e)
    {
        if (_ready)
        {
            DialogResult = true;
        }
    }

    private void Debug_Click(object sender, RoutedEventArgs e)
    {
        var show = DebugText.Visibility != Visibility.Visible;
        DebugText.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        DebugButton.Content = show ? "Hide" : "DEBUG";
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowCancel && DialogResult != true)
        {
            e.Cancel = true;
        }

        base.OnClosing(e);
    }
}
