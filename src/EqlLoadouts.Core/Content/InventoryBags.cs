using System.Globalization;

namespace EqlLoadouts.Core.Content;

/// <summary>
/// Inventory bag windows are shared by every loadout. A layout file only contains a bag section after that window has been opened on that layout.
/// </summary>
public static class InventoryBags
{
    public const string Prefix = "BagInv";

    public static bool IsName(string name)
    {
        if (!name.StartsWith(Prefix, StringComparison.Ordinal) || name.Length == Prefix.Length)
        {
            return false;
        }

        for (var index = Prefix.Length; index < name.Length; index++)
        {
            if (!char.IsDigit(name[index]))
            {
                return false;
            }
        }

        return true;
    }

    public static IReadOnlyList<string> Union(IEnumerable<IEnumerable<string>> sectionNames)
    {
        var names = new SortedDictionary<int, string>();
        foreach (var source in sectionNames)
        {
            foreach (var name in source)
            {
                if (!IsName(name)
                    || !int.TryParse(name.AsSpan(Prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var number))
                {
                    continue;
                }

                names.TryAdd(number, name);
            }
        }

        return names.Values.ToArray();
    }

    public static IEnumerable<string> NamesIn(IEnumerable<string> lines)
    {
        foreach (var line in lines)
        {
            var name = HeaderName(line);
            if (name is not null && IsName(name))
            {
                yield return name;
            }
        }
    }

    public static string? HeaderName(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length < 3 || trimmed[0] != '[' || trimmed[^1] != ']')
        {
            return null;
        }

        return trimmed[1..^1];
    }
}
