using System.Globalization;
using System.Windows;
using EqlLoadouts.Core.Loadouts;

namespace EqlLoadouts.App;

public partial class SaveChangesWindow : Window
{
    private readonly SavePrompt _prompt;

    public SaveChangesWindow(SavePrompt prompt)
    {
        InitializeComponent();
        _prompt = prompt;
        BodyText.Text = Describe(prompt);
        CharacterChoice.Content = prompt.CharacterContentChanged
            ? "Character (Macros and Spell Loadouts)"
            : "Character sections";
        var both = prompt.WindowsChanged && prompt.CharacterChanged;
        ScopePanel.Visibility = both ? Visibility.Visible : Visibility.Collapsed;
        if (both)
        {
            BothChoice.IsChecked = true;
        }
        else if (prompt.WindowsChanged)
        {
            WindowsChoice.IsChecked = true;
        }
        else
        {
            CharacterChoice.IsChecked = true;
        }

        UpdateChoices();
    }

    public bool SaveWindows => WindowsChoice.IsChecked == true || BothChoice.IsChecked == true;

    public bool SaveCharacter => CharacterChoice.IsChecked == true || BothChoice.IsChecked == true;

    public SectionWriteMode CharacterMode
        => MergeChoice.IsChecked == true ? SectionWriteMode.MergeNoDeletion : SectionWriteMode.Replace;

    private void Scope_Changed(object sender, RoutedEventArgs e)
    {
        if (IsLoaded)
        {
            UpdateChoices();
        }
    }

    private void UpdateChoices()
    {
        var windows = SaveWindows;
        var character = SaveCharacter;
        WindowsNote.Visibility = windows ? Visibility.Visible : Visibility.Collapsed;
        ModePanel.Visibility = character && _prompt.CharacterContentChanged ? Visibility.Visible : Visibility.Collapsed;
        OtherNote.Visibility = character && _prompt.OtherCharacterChanged ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (SaveWindows || SaveCharacter)
        {
            DialogResult = true;
        }
    }

    private static string Describe(SavePrompt prompt)
    {
        if (prompt.WindowsChanged && prompt.CharacterChanged)
        {
            return "Windows and character files both have changes. Choose what to write."
                + Environment.NewLine + Environment.NewLine
                + "Windows: " + List(prompt.WindowFiles)
                + Environment.NewLine
                + "Character: " + List(prompt.CharacterFiles);
        }

        if (prompt.WindowsChanged)
        {
            return "Only windows have changes. Character files have no changes, so Macros, Spell Loadouts, and other character sections will not be written."
                + Environment.NewLine + Environment.NewLine
                + List(prompt.WindowFiles);
        }

        if (prompt.CharacterContentChanged)
        {
            return "Only character files have changes (Macros and Spell Loadouts). Windows have no changes, so window sections will not be written."
                + Environment.NewLine + Environment.NewLine
                + List(prompt.CharacterFiles);
        }

        return "Only character files have changes. Windows have no changes, so window sections will not be written."
            + Environment.NewLine + Environment.NewLine
            + List(prompt.CharacterFiles);
    }

    private static string List(IReadOnlyList<string> names)
    {
        if (names.Count <= 6)
        {
            return string.Join(", ", names);
        }

        return string.Join(", ", names.Take(5))
            + string.Create(CultureInfo.InvariantCulture, $", and {names.Count - 5} more");
    }
}
