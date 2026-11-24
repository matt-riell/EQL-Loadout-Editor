namespace EqlLoadouts.Core.Layout;

/// <summary>A window that can be highlighted from its section. <see cref="Shown"/> is the window's own checkbox.</summary>
public readonly record struct ShownWindow(string Section, string Name, bool Shown);

public static class WindowSelectAll
{
    /// <summary>
    /// Ctrl+A highlights the shown windows in the selected section.
    /// When every section is selected, it highlights every shown window.
    /// </summary>
    public static IReadOnlyList<string> Names(IReadOnlyList<ShownWindow> windows, bool allSectionsSelected, string? selectedSection)
    {
        return windows
            .Where(window => window.Shown && (allSectionsSelected || selectedSection is not null && window.Section == selectedSection))
            .Select(window => window.Name)
            .ToList();
    }
}
