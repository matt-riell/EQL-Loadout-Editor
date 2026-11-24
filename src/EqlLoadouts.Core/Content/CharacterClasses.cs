namespace EqlLoadouts.Core.Content;

/// <summary>The sixteen classes, indexed 0-15 in the game's class order (the order of the level columns in spells_us.txt).</summary>
public static class CharacterClasses
{
    public const int Count = 16;

    public const int MaxPerCharacter = 3;

    public static IReadOnlyList<string> Codes { get; } =
    [
        "WAR", "CLR", "PAL", "RNG", "SHD", "DRU", "MNK", "BRD",
        "ROG", "SHM", "NEC", "WIZ", "MAG", "ENC", "BST", "BER",
    ];

    public static IReadOnlyList<string> Names { get; } =
    [
        "Warrior", "Cleric", "Paladin", "Ranger", "Shadow Knight", "Druid", "Monk", "Bard",
        "Rogue", "Shaman", "Necromancer", "Wizard", "Magician", "Enchanter", "Beastlord", "Berserker",
    ];

    private static readonly Dictionary<string, int> Lookup = BuildLookup();

    public static int IndexOf(string? token)
    {
        return token is not null && Lookup.TryGetValue(token.Trim(), out var index) ? index : -1;
    }

    /// <summary>Reads class codes or names from a label such as "SHM/ROG/ENC", ignoring anything that isn't a class.</summary>
    public static IReadOnlyList<int> Parse(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return [];
        }

        var found = new List<int>();
        var normalized = label.Replace("Shadow Knight", "ShadowKnight", StringComparison.OrdinalIgnoreCase);
        foreach (var token in normalized.Split(['/', ',', ';', '-', '|', '+', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            var index = IndexOf(token);
            if (index >= 0 && !found.Contains(index))
            {
                found.Add(index);
            }
        }

        return found;
    }

    public static string Format(IEnumerable<int> classes)
    {
        return string.Join("/", classes.Where(index => index is >= 0 and < Count).Select(index => Codes[index]));
    }

    private static Dictionary<string, int> BuildLookup()
    {
        var lookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < Count; i++)
        {
            lookup[Codes[i]] = i;
            lookup[Names[i].Replace(" ", string.Empty, StringComparison.Ordinal)] = i;
        }

        lookup["SK"] = 4;
        lookup["BL"] = 14;
        lookup["BZK"] = 15;
        return lookup;
    }
}
