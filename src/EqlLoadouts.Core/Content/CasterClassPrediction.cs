using System.Globalization;
using EqlLoadouts.Core.Ini;

namespace EqlLoadouts.Core.Content;

/// <summary>Where a spell sits in a spell set. Slot is 1 through 14.</summary>
public readonly record struct GemSpot(int Set, int Slot);

/// <summary>One spell from a spell-set gem, with every class who can cast it at level 50 or below.</summary>
public sealed record PredictedGem(int Id, string Name, IReadOnlyList<int> ClassesAtCap, IReadOnlyList<GemSpot> Spots)
{
    public bool QualifiesClass => ClassesAtCap.Count == 1;
}

/// <summary>A class placed on the preselect list, the number of gems it can cast, and those gems.</summary>
public sealed record PredictedClass(int Class, int Count, IReadOnlyList<PredictedGem> Gems);

/// <summary>The classes preselected from spell gems, plus spell ids the spell file did not contain.</summary>
public sealed record ClassPrediction(IReadOnlyList<PredictedClass> Classes, IReadOnlyList<int> UnknownSpellIds);

/// <summary>
/// Picks classes from the spell ids stored in in-use spell-set gems only.
/// A set counts while SpellLoadout{n}.inuse is 1. Unused sets are skipped.
/// Keys that are not SpellLoadout{n}.slot1 through slot14 are ignored.
/// A gem only one class can use, at level 50 or below, puts that class on the list. Melee disciplines count.
/// The class's count is every gem in the loadout that class can cast, including gems other classes can cast.
/// Who learns a shared gem sooner does not add a class. Every class on the list is returned, highest count first.
/// </summary>
public static class CasterClassPrediction
{
    public static IReadOnlyList<int> Predict(SpellCatalog catalog, IniSection? spellLoadouts)
        => Explain(catalog, spellLoadouts).Classes.Select(row => row.Class).ToList();

    public static IReadOnlyList<int> Predict(SpellCatalog catalog, IEnumerable<SpellGemSet> sets)
        => Explain(catalog, sets).Classes.Select(row => row.Class).ToList();

    public static IReadOnlyList<int> Predict(SpellCatalog catalog, IEnumerable<int> spellIds)
        => Explain(catalog, spellIds).Classes.Select(row => row.Class).ToList();

    public static ClassPrediction Explain(SpellCatalog catalog, IniSection? spellLoadouts)
    {
        if (spellLoadouts is null)
        {
            return new ClassPrediction([], []);
        }

        return Explain(catalog, SpellGems.Read(spellLoadouts));
    }

    public static ClassPrediction Explain(SpellCatalog catalog, IEnumerable<SpellGemSet> sets)
        => Explain(catalog, GemIds(sets));

    public static ClassPrediction Explain(SpellCatalog catalog, IEnumerable<int> spellIds)
        => Explain(catalog, spellIds.Select(id => new GemRef(0, 0, id)));

    private static ClassPrediction Explain(SpellCatalog catalog, IEnumerable<GemRef> refs)
    {
        var gems = new List<GemUse>();
        var unknown = new List<int>();
        var seen = new Dictionary<int, GemUse>();
        foreach (var gemRef in refs)
        {
            if (seen.TryGetValue(gemRef.SpellId, out var earlier))
            {
                earlier.Spots.Add(new GemSpot(gemRef.Set, gemRef.Slot));
                continue;
            }

            if (!catalog.TryGet(gemRef.SpellId, out var spell) || !spell.HasClassLevels)
            {
                unknown.Add(gemRef.SpellId);
                continue;
            }

            var users = CastersAtCap(spell);
            if (users.Count == 0)
            {
                continue;
            }

            var use = new GemUse(gemRef.SpellId, spell.Name, users, [new GemSpot(gemRef.Set, gemRef.Slot)]);
            seen.Add(gemRef.SpellId, use);
            gems.Add(use);
        }

        var listed = new HashSet<int>();
        foreach (var gem in gems)
        {
            if (gem.ClassesAtCap.Count == 1)
            {
                listed.Add(gem.ClassesAtCap[0]);
            }
        }

        var classes = new List<PredictedClass>();
        foreach (var classIndex in listed)
        {
            var counted = gems
                .Where(gem => gem.ClassesAtCap.Contains(classIndex))
                .OrderByDescending(gem => gem.ClassesAtCap.Count == 1)
                .ThenBy(gem => gem.Name, StringComparer.OrdinalIgnoreCase)
                .Select(gem => new PredictedGem(gem.Id, gem.Name, gem.ClassesAtCap, gem.Spots))
                .ToList();
            classes.Add(new PredictedClass(classIndex, counted.Count, counted));
        }

        return new ClassPrediction(
            classes.OrderByDescending(row => row.Count).ThenBy(row => row.Class).ToList(),
            unknown);
    }

    private static List<int> CastersAtCap(SpellInfo spell)
    {
        var users = new List<int>();
        for (var index = 0; index < CharacterClasses.Count; index++)
        {
            // Level 50 is the spell-book cap. A later level does not make that class a user.
            if (spell.LevelFor(index) is not { } level || level > SpellCatalog.MaxSearchLevel)
            {
                continue;
            }

            users.Add(index);
        }

        return users;
    }

    /// <summary>Spell ids in gem slots 1 through 14 of sets that are in use. The same spell in another set is one spell, with each gem noted.</summary>
    private static IEnumerable<GemRef> GemIds(IEnumerable<SpellGemSet> sets)
    {
        foreach (var set in sets)
        {
            if (!SpellWindows.IsListed(set))
            {
                continue;
            }

            for (var index = 0; index < set.Slots.Length && index < 14; index++)
            {
                var slot = set.Slots[index];
                if (!SpellCatalog.IsEmptyGem(slot)
                    && int.TryParse(slot.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
                    && id > 0)
                {
                    yield return new GemRef(set.Index, index + 1, id);
                }
            }
        }
    }

    private readonly record struct GemRef(int Set, int Slot, int SpellId);

    private sealed class GemUse(int id, string name, IReadOnlyList<int> classesAtCap, List<GemSpot> spots)
    {
        public int Id { get; } = id;

        public string Name { get; } = name;

        public IReadOnlyList<int> ClassesAtCap { get; } = classesAtCap;

        public List<GemSpot> Spots { get; } = spots;
    }
}
