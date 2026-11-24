namespace EqlLoadouts.Core.Content;

/// <summary>One row in the spell-class picker. A loadout row covers every class on the character.</summary>
public sealed class SpellClassChoice
{
    public SpellClassChoice(string label, IReadOnlyList<int> classes, bool isLoadout)
    {
        Label = label;
        Classes = classes;
        IsLoadout = isLoadout;
    }

    public string Label { get; }

    public IReadOnlyList<int> Classes { get; }

    public bool IsLoadout { get; }
}

/// <summary>
/// Class picker order: the loadout label, then its spellcasting classes in that same order,
/// then every other class that has spells, by code.
/// </summary>
public static class SpellClassList
{
    public static IReadOnlyList<SpellClassChoice> Build(IReadOnlyList<int>? loadout, Func<int, bool> hasSpells)
    {
        var trio = new List<int>();
        if (loadout is not null)
        {
            foreach (var index in loadout)
            {
                if (index is >= 0 and < CharacterClasses.Count && !trio.Contains(index) && trio.Count < CharacterClasses.MaxPerCharacter)
                {
                    trio.Add(index);
                }
            }
        }

        var casters = new List<int>();
        foreach (var index in trio)
        {
            if (hasSpells(index))
            {
                casters.Add(index);
            }
        }

        var choices = new List<SpellClassChoice>();
        if (trio.Count >= 2 && casters.Count > 0)
        {
            choices.Add(new SpellClassChoice(CharacterClasses.Format(trio), casters, true));
            foreach (var index in casters)
            {
                choices.Add(ClassChoice(index));
            }
        }
        else if (casters.Count == 1)
        {
            choices.Add(ClassChoice(casters[0], isLoadout: true));
        }

        var rest = new List<int>();
        for (var index = 0; index < CharacterClasses.Count; index++)
        {
            if (hasSpells(index) && !casters.Contains(index))
            {
                rest.Add(index);
            }
        }

        foreach (var index in rest.OrderBy(index => CharacterClasses.Codes[index], StringComparer.Ordinal))
        {
            choices.Add(ClassChoice(index));
        }

        return choices;
    }

    private static SpellClassChoice ClassChoice(int index, bool isLoadout = false)
        => new(CharacterClasses.Codes[index], [index], isLoadout);
}
