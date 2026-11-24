using EqlLoadouts.Core.Content;

namespace EqlLoadouts.Core.Tests;

public class SpellUpgradeTests
{
    private const int Shaman = 9;
    private const int Necromancer = 10;

    [Fact]
    public void Greater_healing_at_rank_six_matches_the_spell_window()
    {
        Assert.Equal(101, SpellScale.Mana(115, 6, SpellScale.Kind.Heal));
        Assert.Equal(2280, SpellScale.CastMilliseconds(3000, 6, SpellScale.Kind.Heal));
        Assert.Equal(413, SpellScale.Grow(350, 6, 3));
        Assert.Equal(1000, SpellScale.ReuseMilliseconds(1500, 6));
    }

    [Fact]
    public void A_nuke_at_rank_six_grows_damage_by_six_percent_and_cuts_cast_by_two()
    {
        Assert.Equal(568, SpellScale.Grow(418, 6, 6));
        Assert.Equal(3520, SpellScale.CastMilliseconds(4000, 6, SpellScale.Kind.Nuke));
        Assert.Equal(-90, SpellScale.Resist(0, 6, SpellScale.Kind.Nuke));
    }

    [Fact]
    public void Spirit_tap_at_rank_five_matches_the_siphon_and_the_resist()
    {
        Assert.Equal(273, SpellScale.Grow(210, 5, 6));
        Assert.Equal(-275, SpellScale.Resist(-200, 5, SpellScale.Kind.Nuke));
        Assert.Equal(2250, SpellScale.CastMilliseconds(2500, 5, SpellScale.Kind.Nuke));
    }

    [Fact]
    public void Rank_zero_leaves_the_file_value()
    {
        Assert.Equal(115, SpellScale.Mana(115, 0, SpellScale.Kind.Heal));
        Assert.Equal(350, SpellScale.Grow(350, 0, 3));
        Assert.Equal(270, SpellScale.DurationTicks(270, 0, SpellScale.Kind.Buff));
    }

    [Fact]
    public void A_buff_duration_rounds_the_way_the_window_does()
    {
        Assert.Equal(47, SpellScale.DurationTicks(43, 1, SpellScale.Kind.Buff));
        Assert.Equal(52, SpellScale.DurationTicks(43, 2, SpellScale.Kind.Buff));
        Assert.Equal(65, SpellScale.DurationTicks(43, 5, SpellScale.Kind.Buff));
    }

    [Fact]
    public void The_same_spell_line_is_the_upgrade()
    {
        var spirit = Nuke(525, "Drain Spirit", 314, line: 7, resist: 1, shaman: 255, necro: 39);
        var soul = Nuke(447, "Drain Soul", 406, line: 7, resist: 1, shaman: 255, necro: 48);

        var upgrade = Assert.Single(SpellUpgrades.Find([spirit], [spirit, soul], [Necromancer], 50, _ => 0));

        Assert.Equal(447, upgrade.Spell.Id);
    }

    [Fact]
    public void A_later_line_spell_with_less_damage_per_second_is_not_an_upgrade()
    {
        var spirit = Nuke(525, "Drain Spirit", 314, line: 7, resist: 1, shaman: 255, necro: 39);
        var soul = Nuke(447, "Drain Soul", 200, line: 7, resist: 1, shaman: 255, necro: 48);

        Assert.Empty(SpellUpgrades.Find([spirit], [spirit, soul], [Necromancer], 50, _ => 0));
    }

    [Fact]
    public void Call_of_flame_is_not_replaced_by_the_slower_ignite_bones()
    {
        const int Ranger = 3;
        var flame = Timed(691, "Call of Flame", "1|0|-300|0|100|300", line: 1, resist: 2, cast: 500, recovery: 1500, reuse: 10000, ranger: 49, necro: 255);
        var bones = Timed(559, "Ignite Bones", "1|79|-450|0|102|500", line: 2, resist: 2, cast: 6100, recovery: 1500, reuse: 12000, formula: 5, duration: 2, ranger: 255, necro: 42);

        Assert.Empty(SpellUpgrades.Find([flame], [flame, bones], [Ranger, Necromancer], 50, spell => spell.Id == 691 ? 5 : 0));

        var upgrade = Assert.Single(SpellUpgrades.Find([bones], [flame, bones], [Ranger, Necromancer], 50, spell => spell.Id == 691 ? 5 : 0));
        Assert.Equal(691, upgrade.Spell.Id);
    }

    [Fact]
    public void A_harder_hit_later_in_the_line_loses_when_it_is_slower()
    {
        var fast = Timed(691, "Call of Flame", "1|0|-300|0|100|300", line: 9, resist: 2, cast: 500, recovery: 1500, reuse: 10000, ranger: 255, necro: 39);
        var slow = Timed(559, "Ignite Bones", "1|79|-450|0|102|500", line: 9, resist: 2, cast: 6100, recovery: 1500, reuse: 12000, formula: 5, duration: 2, ranger: 255, necro: 48);

        Assert.Empty(SpellUpgrades.Find([fast], [fast, slow], [Necromancer], 50, spell => spell.Id == 691 ? 5 : 0));
    }

    [Fact]
    public void A_lower_spell_in_the_line_is_not_an_upgrade()
    {
        var spirit = Nuke(525, "Drain Spirit", 314, line: 7, resist: 1, shaman: 255, necro: 39);
        var soul = Nuke(447, "Drain Soul", 406, line: 7, resist: 1, shaman: 255, necro: 48);

        Assert.Empty(SpellUpgrades.Find([soul], [spirit, soul], [Necromancer], 50, _ => 0));
    }

    [Fact]
    public void Without_a_line_upgrade_the_higher_powered_damage_wins()
    {
        var venom = Nuke(3573, "Shock of Venom", 100, line: 1, resist: 4, shaman: 47, necro: 255);
        var torbas = Nuke(900, "Torbas's Poison Blast", 150, line: 2, resist: 4, shaman: 255, necro: 49);

        Assert.Empty(SpellUpgrades.Find([venom], [venom, torbas], [Shaman, Necromancer], 50, spell => spell.Id == 3573 ? 10 : 0));

        var unpowered = Nuke(3573, "Shock of Venom", 100, line: 1, resist: 4, shaman: 47, necro: 255);
        var fromVenom = Assert.Single(SpellUpgrades.Find([unpowered], [unpowered, torbas], [Shaman, Necromancer], 50, _ => 0));
        Assert.Equal(900, fromVenom.Spell.Id);

        var fromTorbas = SpellUpgrades.Find([torbas], [venom, torbas], [Shaman, Necromancer], 50, spell => spell.Id == 3573 ? 10 : 0);
        Assert.Equal(3573, Assert.Single(fromTorbas).Spell.Id);
    }

    [Fact]
    public void Pact_of_shadow_is_not_an_upgrade_of_chloroplast()
    {
        var chloroplast = new SpellInfo(
            145,
            "Chloroplast",
            Levels(Shaman, 39),
            category: 79,
            subcategory: 43,
            durationFormula: 10,
            duration: 205,
            spellLineId: 100100030,
            goodEffect: 1,
            casting: new SpellCasting(200, 6000, 1500, 1500, 100, 0, 0, 51, 0, 0, "1|0|6|0|139|19$2|189|3|0|139|10", 0));
        var pact = new SpellInfo(
            694,
            "Pact of Shadow",
            Levels(Necromancer, 44),
            category: 42,
            subcategory: 56,
            durationFormula: 1,
            duration: 4,
            spellLineId: 9056,
            goodEffect: 1,
            casting: new SpellCasting(10, 2000, 1500, 12000, 100, 0, 0, 5, 0, 0, "1|10|0|0|100|0$2|10|0|0|100|0$3|10|0|0|100|0$4|0|80|0|100|0", 0));

        Assert.Empty(SpellUpgrades.Find([chloroplast], [chloroplast, pact], [Shaman, Necromancer], 50, _ => 0));
    }

    [Fact]
    public void A_stronger_regen_in_the_same_category_is_an_upgrade()
    {
        var chloroplast = new SpellInfo(
            145,
            "Chloroplast",
            Levels(Shaman, 39),
            category: 79,
            durationFormula: 10,
            duration: 205,
            spellLineId: 100100030,
            goodEffect: 1,
            casting: new SpellCasting(200, 6000, 1500, 1500, 100, 0, 0, 51, 0, 0, "1|0|6|0|100|19", 0));
        var regrowth = new SpellInfo(
            2,
            "Regrowth",
            Levels(Shaman, 44),
            category: 79,
            durationFormula: 10,
            duration: 205,
            spellLineId: 88,
            goodEffect: 1,
            casting: new SpellCasting(200, 6000, 1500, 1500, 100, 0, 0, 51, 0, 0, "1|0|40|0|100|0", 0));

        var upgrade = Assert.Single(SpellUpgrades.Find([chloroplast], [chloroplast, regrowth], [Shaman], 50, _ => 0));
        Assert.Equal(2, upgrade.Spell.Id);
    }

    [Fact]
    public void Stoicism_is_not_an_upgrade_of_slugs_healing()
    {
        var slugs = Hot(74067, "Slugs Healing", 200, 42, "3|3|-100|0|100|0");
        var stoicism = Hot(3694, "Stoicism", 200, 44, "11|11|70|0|100|0");

        Assert.Empty(SpellUpgrades.Find([slugs], [slugs, stoicism], [Shaman], 50, _ => 0));
    }

    [Fact]
    public void A_stronger_heal_with_the_same_drawback_is_still_an_upgrade()
    {
        var slugs = Hot(74067, "Slugs Healing", 200, 42, "3|3|-100|0|100|0");
        var stronger = Hot(2, "Greater Slugs", 400, 44, "3|3|-100|0|100|0");

        var upgrade = Assert.Single(SpellUpgrades.Find([slugs], [slugs, stronger], [Shaman], 50, _ => 0));
        Assert.Equal(2, upgrade.Spell.Id);
    }

    [Fact]
    public void Details_show_hps_for_healing_and_dps_for_damage()
    {
        var heal = Hot(74067, "Slugs Healing", 200, 42, "3|3|-100|0|100|0");
        var lines = SpellPresentation.Lines(heal, null, 50);
        Assert.Equal("33.33", lines.Single(line => line.Label == "HPS").Value);
        Assert.DoesNotContain(lines, line => line.Label == "DPS");

        var nuke = Nuke(2, "Bolt", 300, line: 1, resist: 2, shaman: 20, necro: 255);
        var nukeLines = SpellPresentation.Lines(nuke, null, 50);
        Assert.Equal("300", nukeLines.Single(line => line.Label == "DPS").Value);
        Assert.DoesNotContain(nukeLines, line => line.Label == "HPS");
    }

    [Fact]
    public void Banish_undead_is_not_an_upgrade_of_drain_soul()
    {
        var soul = Targeted(447, "Drain Soul", 406, target: 13, line: 1);
        var banish = Targeted(118, "Banish Undead", 800, target: 10, line: 2);

        Assert.Empty(SpellUpgrades.Find([soul], [soul, banish], [Necromancer], 50, _ => 0));
    }

    [Fact]
    public void Single_target_codes_still_match()
    {
        var current = Targeted(1, "Bolt", 100, target: 5, line: 1);
        var harder = Targeted(2, "Blast", 400, target: 1, line: 2);

        var upgrade = Assert.Single(SpellUpgrades.Find([current], [current, harder], [Necromancer], 50, _ => 0));
        Assert.Equal(2, upgrade.Spell.Id);
    }

    [Fact]
    public void A_different_resist_is_not_a_similar_spell()
    {
        var poison = Nuke(1, "Poison", 100, line: 1, resist: 4, shaman: 47, necro: 255);
        var fire = Nuke(2, "Fire", 400, line: 2, resist: 2, shaman: 255, necro: 49);

        Assert.Empty(SpellUpgrades.Find([poison], [poison, fire], [Shaman, Necromancer], 50, _ => 0));
    }

    [Fact]
    public void Healing_details_show_the_adjusted_amount()
    {
        var heal = new SpellInfo(
            15,
            "Greater Healing",
            Levels(Shaman, 29),
            durationFormula: 0,
            duration: 0,
            goodEffect: 1,
            casting: new SpellCasting(115, 3000, 1500, 1500, 100, 0, 0, 5, 0, 0, "1|0|350|0|100|0", 0));

        var health = SpellPresentation.EffectLines(heal, SpellCatalog.Parse([]), 50, 6).Single(line => line.Value.Contains("Health", StringComparison.Ordinal));

        Assert.Equal("Increases Health by 350", health.Value);
        Assert.Equal("413", health.Adjusted);
        Assert.Equal(SpellValueTone.Benefit, health.Tone);
        Assert.Equal("101", SpellPresentation.Lines(heal, null, 50, 6).Single(line => line.Label == "Mana").Adjusted);
    }

    private static SpellInfo Hot(int id, string name, int heal, int level, string extra)
    {
        var levels = Enumerable.Repeat((byte)255, 16).ToArray();
        levels[Shaman] = (byte)level;
        var effects = "1|100|" + heal.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|0|100|0";
        if (extra.Length > 0)
        {
            effects += "$" + extra;
        }

        return new SpellInfo(
            id,
            name,
            levels,
            durationFormula: 10,
            duration: 4,
            spellLineId: 14102,
            goodEffect: 1,
            casting: new SpellCasting(180, 6000, 1500, 1500, 100, 0, 0, 56, 0, 0, effects, 0));
    }

    private static SpellInfo Timed(
        int id,
        string name,
        string effects,
        int line,
        int resist,
        int cast,
        int recovery,
        int reuse,
        int ranger,
        int necro,
        int formula = 0,
        int duration = 0)
    {
        var levels = Enumerable.Repeat((byte)255, 16).ToArray();
        levels[3] = (byte)ranger;
        levels[Necromancer] = (byte)necro;
        return new SpellInfo(
            id,
            name,
            levels,
            durationFormula: formula,
            duration: duration,
            spellLineId: line,
            goodEffect: 0,
            casting: new SpellCasting(10, cast, recovery, reuse, 200, 0, resist, 5, 0, 0, effects, 0));
    }

    private static SpellInfo Targeted(int id, string name, int damage, int target, int line)
    {
        var levels = Enumerable.Repeat((byte)255, 16).ToArray();
        levels[Necromancer] = 40;
        return new SpellInfo(
            id,
            name,
            levels,
            spellLineId: line,
            goodEffect: 0,
            casting: new SpellCasting(10, 1000, 0, 0, 200, 0, 1, target, 0, 0, "1|0|-" + damage.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|0|100|0", 0));
    }

    private static SpellInfo Nuke(int id, string name, int damage, int line, int resist, int shaman, int necro)
    {
        var levels = Enumerable.Repeat((byte)255, 16).ToArray();
        levels[Shaman] = (byte)shaman;
        levels[Necromancer] = (byte)necro;
        return new SpellInfo(
            id,
            name,
            levels,
            spellLineId: line,
            goodEffect: 0,
            casting: new SpellCasting(10, 1000, 0, 0, 0, 0, resist, 1, 0, 0, "1|0|-" + damage.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|0|100|0", 0));
    }

    private static byte[] Levels(int classIndex, int level)
    {
        var levels = Enumerable.Repeat((byte)255, 16).ToArray();
        levels[classIndex] = (byte)level;
        return levels;
    }
}
