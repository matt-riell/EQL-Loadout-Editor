namespace EqlLoadouts.Core.Content;

/// <summary>
/// The stance window is a bar of stance and invocation buttons. The game fills it at runtime; the loadout file only stores the frame.
/// </summary>
public static class StanceBars
{
    public static bool IsWindow(string name)
    {
        if (name.Equals("StanceWnd", StringComparison.Ordinal))
        {
            return true;
        }

        const string prefix = "StanceWnd_";
        return name.StartsWith(prefix, StringComparison.Ordinal)
            && name.Length > prefix.Length
            && name[prefix.Length..].All(char.IsDigit);
    }

    private static readonly string[] Names =
    [
        "Offense",
        "Mage Hunter",
        "Evasive",
        "Balanced",
        "Defense",
        "Striker",
        "Berserker",
        "Ranged",
        "Channeler",
        "Recover",
        "Empower",
        "Inversion",
        "Spell Blade",
        "Over Channel",
        "Inviolable",
        "Divine",
        "Chained",
        "Arcane Mastery",
        "Unyielding",
    ];

    public static IReadOnlyList<HotbarFace> Buttons { get; } = Names
        .Select(name => new HotbarFace(name, "0"))
        .ToArray();
}
