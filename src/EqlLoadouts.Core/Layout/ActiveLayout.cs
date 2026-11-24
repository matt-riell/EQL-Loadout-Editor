using System.Globalization;
using EqlLoadouts.Core.Ini;

namespace EqlLoadouts.Core.Layout;

public static class ActiveLayout
{
    public static Dictionary<string, int> Read(IniDocument document, string? skin)
    {
        var selected = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        Apply(selected, document.Find("Default"));
        if (!string.IsNullOrWhiteSpace(skin))
        {
            Apply(selected, document.Find(skin));
        }

        return selected;
    }

    public static bool IsActive(string sectionName, IReadOnlyDictionary<string, int> selected)
    {
        if (TryVariant(sectionName, out var baseName, out var index) && selected.ContainsKey(baseName))
        {
            return selected[baseName] == index;
        }

        return !selected.TryGetValue(sectionName, out var active) || active == 0;
    }

    /// <summary>The game is showing this window: it is not turned off, and it is the layout variant currently in use.</summary>
    public static bool IsDisplayed(IniSection section, IReadOnlyDictionary<string, int> selected)
    {
        return section.Get("Show") != "0" && IsActive(section.Name, selected);
    }

    /// <summary>
    /// The layout canvas would draw this section when its show checkbox is on and the section is not selected.
    /// The checkbox itself is ignored, so a section can stay in the list while it is hidden.
    /// A window with no stored position is skipped, and a chat container with no tabs is skipped.
    /// </summary>
    public static bool AppearsOnLayout(
        IniSection section,
        IReadOnlyDictionary<string, int> selected,
        bool windowCategory,
        bool hasTabs)
    {
        if (!IsDisplayed(section, selected) || !hasTabs)
        {
            return false;
        }

        return !windowCategory || WindowPlacement.HasPosition(section);
    }

    private static void Apply(Dictionary<string, int> selected, IniSection? section)
    {
        if (section is null)
        {
            return;
        }

        foreach (var entry in section.Entries)
        {
            if (entry.HasEquals
                && int.TryParse(entry.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
            {
                selected[entry.Key] = index;
            }
        }
    }

    private static bool TryVariant(string name, out string baseName, out int index)
    {
        var split = name.LastIndexOf('_');
        if (split > 0
            && int.TryParse(name[(split + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out index))
        {
            baseName = name[..split];
            return true;
        }

        baseName = name;
        index = 0;
        return false;
    }
}
