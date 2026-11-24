namespace EqlLoadouts.Core.Content;

/// <summary>
/// Names for hotbar buttons the game saves without a label.
/// Basic attacks are type B. Command buttons are type G. Skills and innate abilities are type J, with innate ids stored as 100 plus the innate index.
/// </summary>
public static class AbilityNames
{
    public static string? Weapon(int id) => id switch
    {
        0 => "Melee Attack",
        1 => "Range Attack",
        _ => null,
    };

    public static string? Command(int id) => id switch
    {
        12 => "Find",
        _ => null,
    };

    public static string? Skill(int id)
    {
        if ((uint)id < (uint)Skills.Length && Skills[id] is { Length: > 0 } skill)
        {
            return skill;
        }

        var innate = id - 100;
        if (id >= 101 && (uint)innate < (uint)Innates.Length && Innates[innate] is { Length: > 0 } name)
        {
            return name;
        }

        return null;
    }

    private static readonly string?[] Skills =
    [
        "1H Blunt",
        "1H Slashing",
        "2H Blunt",
        "2H Slashing",
        "Abjuration",
        "Alteration",
        "Apply Poison",
        "Archery",
        "Backstab",
        "Bind Wound",
        "Bash",
        "Block",
        "Brass Instruments",
        "Channeling",
        "Conjuration",
        "Defense",
        "Disarm",
        "Disarm Traps",
        "Divination",
        "Dodge",
        "Double Attack",
        "Dragon Punch",
        "Dual Wield",
        "Eagle Strike",
        "Evocation",
        "Feign Death",
        "Flying Kick",
        "Forage",
        "Hand to Hand",
        "Hide",
        "Kick",
        "Meditate",
        "Mend",
        "Offense",
        "Parry",
        "Pick Lock",
        "1H Piercing",
        "Riposte",
        "Round Kick",
        "Safe Fall",
        "Sense Heading",
        "Singing",
        "Sneak",
        "Specialize Abjure",
        "Specialize Alteration",
        "Specialize Conjuration",
        "Specialize Divination",
        "Specialize Evocation",
        "Pick Pockets",
        "Stringed Instruments",
        "Swimming",
        "Throwing",
        "Tiger Claw",
        "Tracking",
        "Wind Instruments",
        "Fishing",
        "Make Poison",
        "Tinkering",
        "Research",
        "Alchemy",
        "Baking",
        "Tailoring",
        "Sense Traps",
        "Blacksmithing",
        "Fletching",
        "Brewing",
        "Alcohol Tolerance",
        "Begging",
        "Jewelry Making",
        "Pottery",
        "Percussion Instruments",
        "Intimidation",
        "Berserking",
        "Taunt",
        "Frenzy",
        "Remove Trap",
        "Triple Attack",
        "2H Piercing",
        "Cleave",
        null,
        null,
        "Smite",
        "Reave",
    ];

    private static readonly string?[] Innates =
    [
        null,
        "Awareness",
        "Bash Door",
        "Breathe Fire",
        "Harmony",
        "Harm Touch",
        "Infravision",
        "Lay Hands",
        "Lore",
        "No Bash",
        "Regeneration",
        "Slam",
        "Surprise",
        "Ultravision",
        "Inspect",
        "Open",
        "Reveal Trap",
    ];
}
