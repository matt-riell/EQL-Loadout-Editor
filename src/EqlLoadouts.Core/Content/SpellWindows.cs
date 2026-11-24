using System.Globalization;
using EqlLoadouts.Core.Ini;

namespace EqlLoadouts.Core.Content;

/// <summary>
/// CastSpellWnd shows one spell set. The book button loads, saves, and deletes sets.
/// The chosen set is stored on the window as SpellSet, a SpellLoadout index.
/// </summary>
public static class SpellWindows
{
    public const string SetKey = "SpellSet";

    public const int MaxSets = 60;

    public static bool IsWindow(string name)
    {
        if (name.Equals("CastSpellWnd", StringComparison.Ordinal))
        {
            return true;
        }

        const string prefix = "CastSpellWnd_";
        return name.StartsWith(prefix, StringComparison.Ordinal)
            && name.Length > prefix.Length
            && name[prefix.Length..].All(char.IsDigit);
    }

    /// <summary>Sets the gem window lists. A set is on the bar only while its inuse value is 1.</summary>
    public static bool IsListed(SpellGemSet set)
        => set.InUse == "1";

    /// <summary>The set this window draws. A missing or unknown SpellSet uses the first listed set.</summary>
    public static int SelectedIndex(IniSection? window, IReadOnlyList<SpellGemSet> sets)
    {
        var listed = sets.Where(IsListed).Select(set => set.Index).Order().ToList();
        if (listed.Count == 0)
        {
            return 0;
        }

        if (window?.Get(SetKey) is string raw
            && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)
            && listed.Contains(index))
        {
            return index;
        }

        return listed[0];
    }

    /// <summary>The first SpellLoadout index that is not on the book menu, or 0 when all 60 are in use.</summary>
    public static int NextFreeIndex(IReadOnlyList<SpellGemSet> sets)
    {
        var used = sets
            .Where(set => IsListed(set) || set.Name.Length > 0 || set.Slots.Any(slot => slot.Length > 0))
            .Select(set => set.Index)
            .ToHashSet();
        for (var index = 1; index <= MaxSets; index++)
        {
            if (!used.Contains(index))
            {
                return index;
            }
        }

        return 0;
    }

    /// <summary>
    /// Editor order: named sets A to Z, then sets with an empty name. Equal names stay in index order.
    /// </summary>
    public static int CompareByName(string? leftName, int leftIndex, string? rightName, int rightIndex)
    {
        var left = leftName ?? string.Empty;
        var right = rightName ?? string.Empty;
        var leftBlank = left.Length == 0;
        var rightBlank = right.Length == 0;
        if (leftBlank != rightBlank)
        {
            return leftBlank ? 1 : -1;
        }

        var byName = string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
        return byName != 0 ? byName : leftIndex.CompareTo(rightIndex);
    }
}
