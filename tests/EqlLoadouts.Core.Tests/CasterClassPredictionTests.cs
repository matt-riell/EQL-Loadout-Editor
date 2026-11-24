using System.Globalization;
using EqlLoadouts.Core.Content;
using EqlLoadouts.Core.Ini;

namespace EqlLoadouts.Core.Tests;

public class CasterClassPredictionTests
{
    private const int Warrior = 0;
    private const int Cleric = 1;
    private const int Paladin = 2;
    private const int Wizard = 11;
    private const int Magician = 12;
    private const int Druid = 5;
    private const int Enchanter = 13;
    private const int Shaman = 9;
    private const int Necromancer = 10;

    [Fact]
    public void EmptyGems_PredictNothing()
    {
        var catalog = Catalog(Spell(1, "Minor Healing", skill: 5, (Cleric, 1)));

        Assert.Empty(CasterClassPrediction.Predict(catalog, Array.Empty<SpellGemSet>()));
        Assert.Empty(CasterClassPrediction.Predict(catalog, [Set(1, ["-1", "0", ""])]));
        Assert.Empty(CasterClassPrediction.Predict(catalog, (IniSection?)null));
    }

    [Fact]
    public void UnknownAndUnleveledSpells_AreIgnored()
    {
        var catalog = Catalog(
            "3^Summon Corpse^0^^10000^0",
            Spell(1, "Minor Healing", skill: 5, (Cleric, 1)));

        Assert.Empty(CasterClassPrediction.Predict(catalog, [Set(1, ["3", "99999"])]));
    }

    [Fact]
    public void UnusedSpellSet_DoesNotContribute()
    {
        var catalog = Catalog(
            Spell(1, "Minor Healing", skill: 5, (Cleric, 1)),
            Spell(2, "Mesmerize", skill: 14, (Enchanter, 16)));
        var section = new IniSection("SpellLoadouts");
        SpellGems.WriteSet(section, Set(1, ["1", "-1"]));
        SpellGems.WriteSet(section, Set(8, ["2"], "0"));

        Assert.Equal([Cleric], CasterClassPrediction.Predict(catalog, section));
    }

    [Fact]
    public void SharedSpell_DoesNotSelectAClass()
    {
        var catalog = Catalog(Spell(1, "Healing", skill: 5, (Cleric, 1), (Paladin, 30)));

        Assert.Empty(CasterClassPrediction.Predict(catalog, [Set(1, ["1"]), Set(2, ["1"])]));
    }

    [Fact]
    public void EveryListedClass_IsKeptHighestSpellCountFirst()
    {
        var catalog = Catalog(
            Spell(1, "Mesmerize", skill: 14, (Enchanter, 16)),
            Spell(2, "Color Skew", skill: 18, (Enchanter, 12)),
            Spell(3, "Discordant Mind", skill: 24, (Enchanter, 43)),
            Spell(4, "Forest's Renewal", skill: 5, (Druid, 44)),
            Spell(5, "Chloroplast", skill: 5, (Druid, 29)),
            Spell(6, "Minor Shielding", skill: 4, (Shaman, 1)),
            Spell(7, "Burst of Flame", skill: 14, (Wizard, 1)));

        Assert.Equal([Enchanter, Druid, Shaman, Wizard], CasterClassPrediction.Predict(catalog, [1, 2, 3, 4, 5, 6, 7]));
    }

    [Fact]
    public void TiedSpellCounts_StillIncludeEveryClass()
    {
        var catalog = Catalog(
            Spell(1, "Mesmerize", skill: 14, (Enchanter, 16)),
            Spell(2, "Color Skew", skill: 18, (Enchanter, 12)),
            Spell(3, "Discordant Mind", skill: 24, (Enchanter, 43)),
            Spell(4, "Forest's Renewal", skill: 5, (Druid, 44)),
            Spell(5, "Minor Shielding", skill: 4, (Shaman, 1)));

        Assert.Equal([Enchanter, Druid, Shaman], CasterClassPrediction.Predict(catalog, [1, 2, 3, 4, 5]));
    }

    [Fact]
    public void SharedGem_CountsForListedClassesAndDoesNotAddTheEarlierClass()
    {
        var catalog = Catalog(
            Spell(1, "Discordant Mind", skill: 24, (Enchanter, 43)),
            Spell(2, "Forest's Renewal", skill: 5, (Druid, 44)),
            Spell(3, "Dustdevil", skill: 5, (Shaman, 20), (Druid, 39)));

        Assert.Equal([Druid, Enchanter], CasterClassPrediction.Predict(catalog, [1, 2, 3]));
    }

    [Fact]
    public void OnlySpellGemSlots_AreUsed()
    {
        var catalog = Catalog(
            Spell(1, "Minor Healing", skill: 5, (Cleric, 1)),
            Spell(2, "Splurt", skill: 5, (Necromancer, 8)),
            Spell(99, "Ancient Spell", skill: 5, (Necromancer, 1)));
        var section = new IniSection("SpellLoadouts");
        section.Set("SpellLoadout1.inuse", "1");
        section.Set("SpellLoadout1.slot1", "1");
        section.Set("SpellLoadout1.slot2", "-1");
        section.Set("SpellLoadout2.inuse", "1");
        section.Set("SpellLoadout2.slot3", "1");
        section.Set("Spell0", "99");
        section.Set("SpellLoadout1.slot15", "2");

        var report = CasterClassPrediction.Explain(catalog, section);
        var only = Assert.Single(report.Classes);

        Assert.Equal(Cleric, only.Class);
        Assert.Equal(1, only.Count);
        var gem = Assert.Single(only.Gems);
        Assert.Equal("Minor Healing", gem.Name);
        Assert.Equal([new GemSpot(1, 1), new GemSpot(2, 3)], gem.Spots);
        Assert.DoesNotContain(99, report.UnknownSpellIds);
        Assert.DoesNotContain(2, report.UnknownSpellIds);
    }

    [Fact]
    public void Explain_NamesTheGemThatListsAClassAndItsSpellCount()
    {
        var catalog = Catalog(
            Spell(1, "Minor Shielding", skill: 4, (Shaman, 1)),
            Spell(2, "Discordant Mind", skill: 24, (Enchanter, 43)),
            Spell(3, "Splurt", skill: 5, (Necromancer, 8)),
            Spell(4, "Dark Plague", skill: 5, (Shaman, 20), (Necromancer, 29)));

        var report = CasterClassPrediction.Explain(catalog, [1, 2, 3, 4]);
        var necro = Assert.Single(report.Classes, row => row.Class == Necromancer);

        Assert.Equal(2, necro.Count);
        Assert.Equal("Splurt", Assert.Single(necro.Gems, gem => gem.QualifiesClass).Name);
        Assert.Contains(necro.Gems, gem => gem.Name == "Dark Plague" && gem.ClassesAtCap.SequenceEqual([Shaman, Necromancer]));
        Assert.Equal(report.Classes.Select(row => row.Class), CasterClassPrediction.Predict(catalog, [1, 2, 3, 4]));
    }

    [Fact]
    public void SameLevelSharedSpell_StaysUnpicked()
    {
        var catalog = Catalog(Spell(1, "Shock of Fire", skill: 14, (Wizard, 4), (Magician, 4)));

        Assert.Empty(CasterClassPrediction.Predict(catalog, [Set(1, ["1"])]));
    }

    [Fact]
    public void ExclusiveSpell_IsKeptWhenTheOtherGemIsShared()
    {
        var catalog = Catalog(
            Spell(1, "Mesmerize", skill: 14, (Enchanter, 16)),
            Spell(2, "Shock of Fire", skill: 14, (Wizard, 4), (Magician, 4)));

        Assert.Equal([Enchanter], CasterClassPrediction.Predict(catalog, [Set(1, ["1"]), Set(2, ["2"])]));
    }

    [Fact]
    public void SharedHeal_DoesNotAddTheEarlierClassWhenTheLockedClassCanCastIt()
    {
        var catalog = Catalog(
            Spell(1, "Healing", skill: 5, (Cleric, 1), (Paladin, 30)),
            Spell(2, "Yaulp", skill: 5, (Paladin, 39)));

        Assert.Equal([Paladin], CasterClassPrediction.Predict(catalog, [Set(1, ["1", "2"])]));
    }

    [Fact]
    public void ExclusiveClasses_BothStayWhenTheyShareAnotherGem()
    {
        var catalog = Catalog(
            Spell(1, "Mesmerize", skill: 14, (Enchanter, 16), (Wizard, 40)),
            Spell(2, "Discordant Mind", skill: 24, (Enchanter, 43)),
            Spell(3, "Shock of Fire", skill: 14, (Wizard, 4)));

        Assert.Equal([Wizard, Enchanter], CasterClassPrediction.Predict(catalog, [Set(1, ["1", "2", "3"])]));
    }

    [Fact]
    public void EarlierClass_BeatsOneThatLearnsTheSpellAfterTheSearchCap()
    {
        var catalog = Catalog(Spell(1, "Complete Heal", skill: 5, (Cleric, 39), (Paladin, 60)));

        Assert.Equal([Cleric], CasterClassPrediction.Predict(catalog, [Set(1, [" 1 "])]));
    }

    [Fact]
    public void SpellAboveTheSearchCap_DoesNotSelectThatClass()
    {
        var catalog = Catalog(
            Spell(1, "Minor Healing", skill: 5, (Cleric, 1)),
            Spell(2, "Color Skew", skill: 18, (Enchanter, 60)));

        Assert.False(catalog.ClassHasSpells(Enchanter));
        Assert.Equal([Cleric], CasterClassPrediction.Predict(catalog, [Set(1, ["1"]), Set(9, ["2"])]));
    }

    [Fact]
    public void MeleeDiscipline_SelectsThatClass()
    {
        var discipline = Spell(2, "Throw Stone", skill: 33, (Warrior, 1));
        var catalog = Catalog(
            discipline,
            Spell(1, "Minor Healing", skill: 5, (Cleric, 1)));

        Assert.False(catalog.ClassHasSpells(Warrior));
        Assert.Equal([Warrior, Cleric], CasterClassPrediction.Predict(catalog, [Set(1, ["1", "2"])]));
    }

    [Fact]
    public void FourExclusiveCasters_AreAllKept()
    {
        var catalog = Catalog(
            Spell(1, "Minor Healing", skill: 5, (Cleric, 1)),
            Spell(2, "Minor Shielding", skill: 4, (Shaman, 1)),
            Spell(3, "Mesmerize", skill: 14, (Enchanter, 16)),
            Spell(4, "Burst of Flame", skill: 14, (Wizard, 1)));

        Assert.Equal([Cleric, Shaman, Wizard, Enchanter], CasterClassPrediction.Predict(catalog, [1, 2, 3, 4]));
    }

    [Fact]
    public void LoadoutL06_SelectsDruidAndEnchanterNotTheEarlierShaman()
    {
        // The spell details panel prints Classes ENC(43) for this row. It sits in L06 spell set 1.
        var catalog = Catalog(
            Spell(673, "Discordant Mind", skill: 24, (Enchanter, 43)),
            Spell(2, "Mesmerization", skill: 18, (Enchanter, 16)),
            Spell(3, "Shiftless D", skill: 18, (Enchanter, 43)),
            Spell(4, "Gasping Embrace", skill: 18, (Enchanter, 29)),
            Spell(5, "Pillage Enchantment", skill: 18, (Enchanter, 20)),
            Spell(6, "Forest's Renewal", skill: 5, (Druid, 44)),
            Spell(7, "Dustdevil", skill: 5, (Shaman, 20), (Druid, 39)),
            Spell(8, "Ice", skill: 24, (Wizard, 12), (Enchanter, 48)));
        var section = new IniSection("SpellLoadouts");
        SpellGems.WriteSet(section, Set(1, ["6", "7", "673", "3", "8", "2", "4", "5"], "1"));
        section.Set("SpellLoadout1.name", "Attack");

        Assert.True(catalog.TryGet(673, out var spell));
        Assert.Equal([(Enchanter, 43)], spell.UsableBy());
        Assert.Equal("ENC(43)", SpellPresentation.Lines(spell).Single(line => line.Label == "Classes").Value);
        Assert.Equal("Evocation", SpellPresentation.Lines(spell).Single(line => line.Label == "Skill").Value);
        Assert.Equal([Enchanter, Druid], CasterClassPrediction.Predict(catalog, section));
        Assert.DoesNotContain(Shaman, CasterClassPrediction.Predict(catalog, section));
    }

    [Fact]
    public void EnchanterOnlyCastingSpell_CountsWhenTheSpellBookDidNotMarkTheClass()
    {
        var line = Spell(673, "Discordant Mind", skill: 24, (Enchanter, 43));
        var fields = line.Split('^');
        fields[55] = "-1";
        var catalog = Catalog(string.Join("^", fields));

        Assert.False(catalog.ClassHasSpells(Enchanter));
        Assert.Equal([Enchanter], CasterClassPrediction.Predict(catalog, [Set(1, ["673"])]));
    }

    private static SpellCatalog Catalog(params string[] lines) => SpellCatalog.Parse(lines);

    private static SpellGemSet Set(int index, string[] slots, string inUse = "1")
    {
        var set = new SpellGemSet(index) { InUse = inUse };
        for (var slot = 0; slot < slots.Length && slot < set.Slots.Length; slot++)
        {
            set.Slots[slot] = slots[slot];
        }

        return set;
    }

    private static string Spell(int id, string name, int skill, params (int Class, int Level)[] levels)
    {
        var fields = Enumerable.Repeat("0", 60).ToArray();
        fields[0] = id.ToString(CultureInfo.InvariantCulture);
        fields[1] = name;
        fields[32] = skill.ToString(CultureInfo.InvariantCulture);
        fields[55] = "0";
        for (var index = 0; index < CharacterClasses.Count; index++)
        {
            fields[36 + index] = "255";
        }

        foreach (var (classIndex, level) in levels)
        {
            fields[36 + classIndex] = level.ToString(CultureInfo.InvariantCulture);
        }

        return string.Join("^", fields);
    }
}
