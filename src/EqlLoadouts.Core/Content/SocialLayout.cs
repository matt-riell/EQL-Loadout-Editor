namespace EqlLoadouts.Core.Content;

public static class SocialLayout
{
    public const int ButtonsPerPage = 12;

    public static IReadOnlyList<int> Pages(IEnumerable<SocialSlot> source, IEnumerable<SocialSlot> destination)
    {
        var max = 0;
        foreach (var slot in source)
        {
            if (slot.Page > max)
            {
                max = slot.Page;
            }
        }

        foreach (var slot in destination)
        {
            if (slot.Page > max)
            {
                max = slot.Page;
            }
        }

        if (max < 1)
        {
            max = 1;
        }

        return Enumerable.Range(1, max).ToList();
    }

    public static int ButtonsOnPage(IEnumerable<SocialSlot> source, IEnumerable<SocialSlot> destination, int page)
    {
        var max = ButtonsPerPage;
        foreach (var slot in source)
        {
            if (slot.Page == page && slot.Slot > max)
            {
                max = slot.Slot;
            }
        }

        foreach (var slot in destination)
        {
            if (slot.Page == page && slot.Slot > max)
            {
                max = slot.Slot;
            }
        }

        return max;
    }

    public static HashSet<string> DuplicateNames(
        IEnumerable<SocialSlot> source,
        IEnumerable<SocialSlot> destination,
        bool sameDocument)
    {
        var duplicates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (sameDocument)
        {
            return duplicates;
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var slot in source)
        {
            var name = slot.Name.Trim();
            if (name.Length > 0)
            {
                names.Add(name);
            }
        }

        foreach (var slot in destination)
        {
            var name = slot.Name.Trim();
            if (name.Length > 0 && names.Contains(name))
            {
                duplicates.Add(name);
            }
        }

        return duplicates;
    }

    public static bool IsDuplicate(string name, IReadOnlySet<string> duplicates)
    {
        var trimmed = name.Trim();
        return trimmed.Length > 0 && duplicates.Contains(trimmed);
    }

    public static bool SameName(string left, string right)
    {
        var a = left.Trim();
        var b = right.Trim();
        return a.Length > 0 && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    public static bool SameCommands(SocialSlot left, SocialSlot right)
    {
        return Line(left.Line1) == Line(right.Line1)
            && Line(left.Line2) == Line(right.Line2)
            && Line(left.Line3) == Line(right.Line3)
            && Line(left.Line4) == Line(right.Line4)
            && Line(left.Line5) == Line(right.Line5);
    }

    /// <summary>Name, color, and every command line. A shared name alone is not the same macro.</summary>
    public static bool SameMacro(SocialSlot left, SocialSlot right)
    {
        return SameName(left.Name, right.Name)
            && SameColor(left.Color, right.Color)
            && SameCommands(left, right);
    }

    /// <summary>
    /// An exact macro on the other loadout, including when both sides are the same loadout.
    /// A button that only shares a name is not a match.
    /// </summary>
    public static SocialNameMatch Match(SocialSlot slot, IEnumerable<SocialSlot> otherSide)
    {
        if (slot.IsEmpty)
        {
            return SocialNameMatch.None;
        }

        foreach (var other in otherSide)
        {
            if (!other.IsEmpty && SameMacro(slot, other))
            {
                return SocialNameMatch.SameCommands;
            }
        }

        return SocialNameMatch.None;
    }

    /// <summary>A filled button whose name, color, and commands are not already on the other loadout.</summary>
    public static bool IsNew(SocialSlot slot, IEnumerable<SocialSlot> otherSide)
        => Match(slot, otherSide) == SocialNameMatch.None && !slot.IsEmpty;

    private static bool SameColor(string left, string right)
    {
        return string.Equals(NormalizeColor(left), NormalizeColor(right), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeColor(string value)
    {
        var color = value.Trim();
        return color.Length == 0 ? "0" : color;
    }

    private static string Line(string value) => value.Trim();
}

public enum SocialNameMatch
{
    None,
    SameCommands,
    DifferentCommands,
}
