using EqlLoadouts.Core.Content;

namespace EqlLoadouts.Core.Tests;

public class SpellAdvisorTests
{
    private const int Shaman = 9;

    [Fact]
    public void Fifteen_attack_beats_five_percent_movement()
    {
        var fast = Buff(1, "Fast", "2|3|25|0|100|0");
        var packed = Buff(2, "Packed", "2|3|20|0|100|0$3|2|15|0|100|0");

        var buffs = SpellAdvisor.Build([fast, packed], [Shaman], 50).Single(set => set.Kind == SpellSetKind.Buffs);

        Assert.Equal([2], buffs.Spells.Select(spell => spell.Spell.Id));
        Assert.True(SpellAdvisor.Score(packed, 50).Points > SpellAdvisor.Score(fast, 50).Points);
        var swap = Assert.Single(SpellAdvisor.Suggest([fast], Scores(fast, packed), 50));
        Assert.Equal(2, swap.Suggested.Id);
        Assert.Equal("+15 attack is worth more than 5% movement", swap.Reason);
    }

    [Fact]
    public void Equal_movement_keeps_the_spell_that_also_grants_attack()
    {
        var sow = Buff(278, "Spirit of Wolf", "1|10|0|0|100|0$2|3|30|0|101|55");
        var wolf = Buff(425, "Wolf Form", "1|58|796|0|100|0$2|3|30|0|101|55$3|2|1|0|102|30");

        Assert.True(SpellAdvisor.Conflicts(sow, wolf, 50));
        var buffs = SpellAdvisor.Build([sow, wolf], [Shaman], 50).Single(set => set.Kind == SpellSetKind.Buffs);
        Assert.Equal([425], buffs.Spells.Select(spell => spell.Spell.Id));
    }

    [Fact]
    public void Blank_charisma_slots_do_not_conflict()
    {
        var attack = Buff(1, "Attack", "1|10|0|0|100|0$2|2|10|0|100|0");
        var armor = Buff(2, "Armor", "1|10|0|0|100|0$4|1|40|0|100|0");

        Assert.False(SpellAdvisor.Conflicts(attack, armor, 50));
        var buffs = SpellAdvisor.Build([attack, armor], [Shaman], 50).Single(set => set.Kind == SpellSetKind.Buffs);
        Assert.Equal(2, buffs.Spells.Count);
    }

    [Fact]
    public void Spells_above_the_max_level_are_left_out()
    {
        var high = Buff(1, "High", "2|2|20|0|100|0", classLevel: 51);
        var low = Buff(2, "Low", "3|2|10|0|100|0", classLevel: 50);

        var buffs = SpellAdvisor.Build([high, low], [Shaman], 50).Single(set => set.Kind == SpellSetKind.Buffs);

        Assert.Equal([2], buffs.Spells.Select(spell => spell.Spell.Id));
    }

    [Fact]
    public void Perms_are_only_formula_50_with_no_duration()
    {
        var perm = Spell(1, "Rage", "1|2|15|0|100|0", good: 1, formula: 50, duration: 0);
        var timed = Spell(2, "Harness", "2|2|30|0|100|0", good: 1, formula: 3, duration: 720);
        var frenzy = Spell(3, "Frenzied Spirit", "3|2|20|0|100|0", good: 1, formula: 0, duration: 0);

        var drafts = SpellAdvisor.Build([perm, timed, frenzy], [Shaman], 50);

        Assert.Equal([1], drafts.Single(set => set.Kind == SpellSetKind.Perms).Spells.Select(spell => spell.Spell.Id));
        Assert.Equal([2], drafts.Single(set => set.Kind == SpellSetKind.Buffs).Spells.Select(spell => spell.Spell.Id));
    }

    [Fact]
    public void A_weaker_nuke_wins_when_its_slow_is_worth_more_than_the_damage()
    {
        var nuke = Spell(1, "Nuke", "1|0|-100|0|100|0", good: 0, formula: 0, duration: 0);
        var mixed = Spell(2, "Mixed", "1|0|-90|0|100|0$2|11|90|0|100|0", good: 0, formula: 0, duration: 0);

        var attacks = SpellAdvisor.Build([nuke, mixed], [Shaman], 50).Single(set => set.Kind == SpellSetKind.Attack);

        Assert.Equal([2], attacks.Spells.Select(spell => spell.Spell.Id));
        var swap = Assert.Single(SpellAdvisor.Suggest([nuke], Scores(nuke, mixed), 50));
        Assert.Equal("+10% haste is worth more than 10 damage", swap.Reason);
    }

    [Fact]
    public void A_buff_is_not_replaced_by_a_stronger_spell_that_does_not_conflict()
    {
        var armor = Buff(1, "Armor", "4|1|40|0|100|0");
        var health = Buff(2, "Health", "1|69|40|0|100|0");

        Assert.Empty(SpellAdvisor.Suggest([armor], Scores(armor, health), 50));
    }

    [Fact]
    public void A_heal_is_compared_with_other_heals()
    {
        var minor = Spell(1, "Minor", "1|0|10|0|100|0", good: 1, formula: 0, duration: 0);
        var major = Spell(2, "Major", "2|0|40|0|100|0", good: 1, formula: 0, duration: 0);

        var swap = Assert.Single(SpellAdvisor.Suggest([minor], Scores(minor, major), 50));

        Assert.Equal(2, swap.Suggested.Id);
        Assert.Equal("+40 hit points is worth more than 10 hit points", swap.Reason);
    }

    [Fact]
    public void Only_fourteen_spells_are_kept()
    {
        var spells = Enumerable.Range(1, 15)
            .Select(index => Buff(index, "Spell " + index.ToString(System.Globalization.CultureInfo.InvariantCulture), index + "|2|" + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|0|100|0"))
            .ToArray();

        var buffs = SpellAdvisor.Build(spells, [Shaman], 50).Single(set => set.Kind == SpellSetKind.Buffs);

        Assert.Equal(14, buffs.Spells.Count);
        Assert.DoesNotContain(buffs.Spells, spell => spell.Spell.Id == 1);
    }

    [Fact]
    public void Duplicate_names_gain_a_windows_style_suffix()
    {
        Assert.Equal("Buffs", SpellAdvisor.UniqueName("Buffs", ["Perma's", "Attack"]));
        Assert.Equal("Buffs (1)", SpellAdvisor.UniqueName("Buffs", ["buffs"]));
        Assert.Equal("Buffs (2)", SpellAdvisor.UniqueName("Buffs", ["Buffs", "Buffs (1)"]));
    }

    private static IReadOnlyList<SpellScore> Scores(params SpellInfo[] spells)
        => spells.Select(spell => SpellAdvisor.Score(spell, 50)).ToArray();

    private static SpellInfo Buff(int id, string name, string effects, int classLevel = 10)
        => Spell(id, name, effects, good: 1, formula: 3, duration: 360, classLevel);

    private static SpellInfo Spell(int id, string name, string effects, int good, int formula, int duration, int classLevel = 10)
    {
        var levels = Enumerable.Repeat((byte)255, 16).ToArray();
        levels[Shaman] = (byte)classLevel;
        return new SpellInfo(
            id,
            name,
            levels,
            durationFormula: formula,
            duration: duration,
            goodEffect: good,
            casting: new SpellCasting(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, effects, 0));
    }
}
