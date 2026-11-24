using EqlLoadouts.Core.Content;
using EqlLoadouts.Core.Ini;

namespace EqlLoadouts.Core.Tests;

public class SpellCatalogTests
{
    private static readonly string[] Sample =
    [
        "3^Summon Corpse^0^^10000^0",
        "673^Discordant Mind^0^^0^0",
        "6877^Kragg's Salve^0^^0^0",
        "bad line",
        "12^^0^0",
    ];

    [Fact]
    public void Parse_ReadsIdAndNameFromEachLine()
    {
        var catalog = SpellCatalog.Parse(Sample);

        Assert.Equal(3, catalog.Count);
        Assert.True(catalog.TryGetName("6877", out var name));
        Assert.Equal("Kragg's Salve", name);
        Assert.True(catalog.TryGetName(" 673 ", out name));
        Assert.Equal("Discordant Mind", name);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("0")]
    [InlineData("")]
    [InlineData(null)]
    public void EmptyGemValues_DoNotResolve(string? value)
    {
        var catalog = SpellCatalog.Parse(Sample);

        Assert.True(SpellCatalog.IsEmptyGem(value));
        Assert.False(catalog.TryGetName(value, out _));
    }

    [Fact]
    public void UnknownId_DoesNotResolve()
    {
        var catalog = SpellCatalog.Parse(Sample);

        Assert.False(SpellCatalog.IsEmptyGem("99999"));
        Assert.False(catalog.TryGetName("99999", out _));
        Assert.False(catalog.TryGetName("12", out _));
    }

    [Fact]
    public void Parse_ReadsClassLevelsFromColumns36To51()
    {
        var catalog = SpellCatalog.Parse([SpellLine(200, "Healing", (1, 10), (2, 20), (3, 28), (5, 19), (9, 19), (14, 24))]);

        Assert.True(catalog.TryGet("200", out var spell));
        Assert.True(spell.HasClassLevels);
        Assert.Equal(10, spell.LevelFor(1));
        Assert.Equal(24, spell.LevelFor(14));
        Assert.Null(spell.LevelFor(0));
        Assert.Equal([1, 2, 3, 5, 9, 14], spell.UsableBy().Select(usable => usable.Class));
    }

    [Fact]
    public void Parse_LastClassColumnMayEndTheLine()
    {
        var line = SpellLine(5, "Frenzy", (15, 7));
        line = line[..line.LastIndexOf('^')];

        var catalog = SpellCatalog.Parse([line]);

        Assert.True(catalog.TryGet("5", out var spell));
        Assert.Equal(7, spell.LevelFor(15));
    }

    [Fact]
    public void Search_PrefersAnExactIdThenNamesThatStartWithTheText()
    {
        var catalog = SpellCatalog.Parse(
        [
            SpellLine(9, "Superior Healing"),
            SpellLine(10, "Healing"),
            SpellLine(11, "Minor Healing"),
            SpellLine(12, "Disease Cloud"),
        ]);

        Assert.Equal(["Healing", "Minor Healing", "Superior Healing"], catalog.Search("heal", 10).Select(spell => spell.Name));
        Assert.Equal("Superior Healing", Assert.Single(catalog.Search("9", 10)).Name);
        Assert.Empty(catalog.Search("h", 10));
        Assert.Empty(catalog.Search("   ", 10));
    }

    [Fact]
    public void ShortLines_HaveNoClassLevels()
    {
        var catalog = SpellCatalog.Parse(Sample);

        Assert.True(catalog.TryGet("3", out var spell));
        Assert.False(spell.HasClassLevels);
        Assert.Empty(spell.UsableBy());
        Assert.Equal(string.Empty, spell.TypeName);
        Assert.False(spell.IsPermanent);
    }

    [Fact]
    public void Search_MatchesTypeDamageLevelAndClass()
    {
        var catalog = SpellCatalog.Parse(
        [
            FullSpellLine(1, "Shock of Fire", category: 25, subcategory: 38, durationFormula: 0, duration: 0, (11, 4)),
            FullSpellLine(2, "Envenomed Bolt", category: 20, subcategory: 75, durationFormula: 1, duration: 6, (9, 8)),
            FullSpellLine(3, "Minor Shielding", category: 45, subcategory: 87, durationFormula: 50, duration: 0, (9, 1)),
            FullSpellLine(4, "Spirit of Wolf", category: 125, subcategory: 65, durationFormula: 3, duration: 360, (9, 9)),
            FullSpellLine(5, "Frost Shock", category: 25, subcategory: 14, durationFormula: 0, duration: 0, (11, 4)),
            FullSpellLine(6, "Healing", category: 42, subcategory: 42, durationFormula: 0, duration: 0, (1, 1)),
        ]);

        Assert.Equal(["Frost Shock", "Shock of Fire"], catalog.Search(SpellSearchKind.Type, "direct damage", null, 10).Select(spell => spell.Name));
        Assert.Equal(["Envenomed Bolt"], catalog.Search(SpellSearchKind.Type, "DOT", null, 10).Select(spell => spell.Name));
        Assert.Equal(["Minor Shielding"], catalog.Search(SpellSearchKind.Type, "permanent", null, 10).Select(spell => spell.Name));
        Assert.Equal(["Shock of Fire"], catalog.Search(SpellSearchKind.Damage, "fire", null, 10).Select(spell => spell.Name));
        Assert.Equal(["Frost Shock"], catalog.Search(SpellSearchKind.Damage, "ice", null, 10).Select(spell => spell.Name));
        Assert.Equal(["Frost Shock", "Shock of Fire"], catalog.Search(SpellSearchKind.Level, "4", null, 10).Select(spell => spell.Name));
        Assert.Equal(
            ["Minor Shielding", "Envenomed Bolt", "Spirit of Wolf"],
            catalog.Search(SpellSearchKind.Class, "", [9], 10).Select(spell => spell.Name));
        Assert.Equal(["Spirit of Wolf"], catalog.Search(SpellSearchKind.Class, "wolf", [9], 10).Select(spell => spell.Name));
        Assert.True(catalog.TryGet("4", out var wolf));
        Assert.False(wolf.IsPermanent);
        Assert.Equal("Utility Beneficial", wolf.TypeName);
        Assert.Equal(string.Empty, wolf.DamageName);
    }

    [Fact]
    public void Search_MatchesSubcategoryAndTheDetailsPaneNamesIt()
    {
        var catalog = SpellCatalog.Parse(
        [
            FullSpellLine(507, "Togor's Insects", category: 126, subcategory: 88, durationFormula: 6, duration: 50, (9, 38)),
            FullSpellLine(2, "Spirit of Wolf", category: 125, subcategory: 65, durationFormula: 3, duration: 360, (9, 9)),
        ]);

        Assert.Equal(["Togor's Insects"], catalog.SearchForClasses("slow", [9], SpellCatalog.MaxSearchLevel, 10).Select(spell => spell.Name));
        Assert.Equal(["Togor's Insects"], catalog.Search(SpellSearchKind.Type, "slow", null, 10).Select(spell => spell.Name));
        Assert.True(catalog.TryGet("507", out var insects));
        var lines = SpellPresentation.Lines(insects);
        Assert.Contains(lines, line => line is { Label: "Category", Value: "Utility Detrimental" });
        Assert.Contains(lines, line => line is { Label: "Subcategory", Value: "Slow" });
    }

    [Fact]
    public void SearchForClasses_KeepsSpellsAtOrBelowTheLevelCap()
    {
        var catalog = SpellCatalog.Parse(
        [
            WithIcon(FullSpellLine(1, "Minor Shielding", category: 45, subcategory: 87, durationFormula: 50, duration: 0, (9, 1)), 278),
            FullSpellLine(2, "Spirit of Wolf", category: 125, subcategory: 65, durationFormula: 3, duration: 360, (9, 9), (8, 20)),
            FullSpellLine(3, "Ancient Remedy", category: 42, subcategory: 42, durationFormula: 0, duration: 0, (9, 60)),
            FullSpellLine(4, "Shock of Fire", category: 25, subcategory: 38, durationFormula: 0, duration: 0, (11, 4)),
        ]);

        Assert.Equal(278, catalog.SearchForClasses("", [9], SpellCatalog.MaxSearchLevel, 10).Single(spell => spell.Name == "Minor Shielding").Icon);
        Assert.Equal(["Spirit of Wolf", "Minor Shielding"], catalog.SearchForClasses("", [9], SpellCatalog.MaxSearchLevel, 10).Select(spell => spell.Name));
        Assert.Equal(["Minor Shielding", "Spirit of Wolf"], catalog.SearchForClasses("", [9], SpellCatalog.MaxSearchLevel, 10, levelDescending: false).Select(spell => spell.Name));
        Assert.Equal(["Minor Shielding"], catalog.SearchForClasses("1", [9], SpellCatalog.MaxSearchLevel, 10).Select(spell => spell.Name));
        Assert.Equal(["Spirit of Wolf"], catalog.SearchForClasses("20", [9, 8], SpellCatalog.MaxSearchLevel, 10).Select(spell => spell.Name));
        Assert.Equal(["Spirit of Wolf", "Minor Shielding"], catalog.SearchForClasses("s", [9], SpellCatalog.MaxSearchLevel, 10).Select(spell => spell.Name));
        Assert.Empty(catalog.SearchForClasses("ancient", [9], SpellCatalog.MaxSearchLevel, 10));
        Assert.Empty(catalog.SearchForClasses("60", [9], SpellCatalog.MaxSearchLevel, 10));
        Assert.Equal(["Shock of Fire"], catalog.SearchForClasses("fire", [11], SpellCatalog.MaxSearchLevel, 10).Select(spell => spell.Name));
    }

    [Fact]
    public void Search_OmitsSpellsTheSpellBookDoesNotFile()
    {
        var catalog = SpellCatalog.Parse(
        [
            WithBookCategory(FullSpellLine(48421, "Illusion: Dwarven Sentinel", category: 48, subcategory: 48, durationFormula: 0, duration: 0, (13, 1)), -1),
            WithBookCategory(FullSpellLine(2, "Mesmerize", category: 35, subcategory: 35, durationFormula: 0, duration: 0, (13, 1)), 4),
        ]);

        Assert.Equal(["Mesmerize"], catalog.SearchForClasses("", [13], SpellCatalog.MaxSearchLevel, 10).Select(spell => spell.Name));
        Assert.Equal(["Mesmerize"], catalog.SearchForClasses("1", [13], SpellCatalog.MaxSearchLevel, 10).Select(spell => spell.Name));
        Assert.Empty(catalog.SearchForClasses("sentinel", [13], SpellCatalog.MaxSearchLevel, 10));
        Assert.Empty(catalog.Search("sentinel", 10));
        Assert.Equal(["Mesmerize"], catalog.Search("mesmerize", 10).Select(spell => spell.Name));
        Assert.Empty(catalog.Search("48421", 10));
        Assert.True(catalog.TryGet("48421", out var stored));
        Assert.Equal("Illusion: Dwarven Sentinel", stored.Name);
        Assert.Equal(-1, stored.BookCategory);
    }

    [Fact]
    public void Search_OmitsMakeshiftAndEmpoweringEssenceSpells()
    {
        var catalog = SpellCatalog.Parse(
        [
            FullSpellLine(7677, "Focus Makeshift Spellcaster's Empowering Essence", category: 18, subcategory: 59, durationFormula: 0, duration: 0, (12, 20)),
            FullSpellLine(7690, "Focus Mass Makeshift Spellcaster's Empowering Essence", category: 18, subcategory: 59, durationFormula: 0, duration: 0, (12, 24)),
            FullSpellLine(7676, "Focus Crude Spellcaster's Empowering Essence", category: 18, subcategory: 59, durationFormula: 0, duration: 0, (12, 16)),
            FullSpellLine(50, "Summon Food", category: 18, subcategory: 108, durationFormula: 0, duration: 0, (12, 1)),
        ]);

        Assert.Equal(["Summon Food"], catalog.SearchForClasses("", [12], SpellCatalog.MaxSearchLevel, 10).Select(spell => spell.Name));
        Assert.Equal(["Summon Food"], catalog.Search("summon", 10).Select(spell => spell.Name));
        Assert.Empty(catalog.Search("makeshift", 10));
        Assert.Empty(catalog.Search("empowering", 10));
        Assert.Empty(catalog.Search("7676", 10));
        Assert.True(catalog.TryGet("7676", out var stored));
        Assert.Equal("Focus Crude Spellcaster's Empowering Essence", stored.Name);
    }

    [Fact]
    public void SearchForClasses_SortsByLevelThenName()
    {
        var catalog = SpellCatalog.Parse(
        [
            FullSpellLine(1, "Minor Shielding", category: 45, subcategory: 87, durationFormula: 50, duration: 0, (9, 1)),
            FullSpellLine(2, "Spirit of Wolf", category: 125, subcategory: 65, durationFormula: 3, duration: 360, (9, 9)),
            FullSpellLine(3, "Scale of Wolf", category: 125, subcategory: 65, durationFormula: 3, duration: 360, (9, 9)),
        ]);

        Assert.Equal(["Scale of Wolf", "Spirit of Wolf", "Minor Shielding"], catalog.SearchForClasses("", [9], SpellCatalog.MaxSearchLevel, 10).Select(spell => spell.Name));
        Assert.Equal(["Spirit of Wolf", "Scale of Wolf", "Minor Shielding"], catalog.SearchForClasses("", [9], SpellCatalog.MaxSearchLevel, 10, nameAscending: false).Select(spell => spell.Name));
        Assert.Equal(["Minor Shielding", "Scale of Wolf", "Spirit of Wolf"], catalog.SearchForClasses("", [9], SpellCatalog.MaxSearchLevel, 10, levelDescending: false).Select(spell => spell.Name));
        Assert.Equal(["Minor Shielding", "Scale of Wolf", "Spirit of Wolf"], catalog.SearchForClasses("", [9], SpellCatalog.MaxSearchLevel, 10, levelFirst: false).Select(spell => spell.Name));
        Assert.Equal(["Spirit of Wolf", "Scale of Wolf", "Minor Shielding"], catalog.SearchForClasses("", [9], SpellCatalog.MaxSearchLevel, 10, nameAscending: false, levelFirst: false).Select(spell => spell.Name));

        var ties = SpellCatalog.Parse(
        [
            FullSpellLine(1, "Spirit of Wolf", category: 125, subcategory: 65, durationFormula: 3, duration: 360, (9, 9)),
            FullSpellLine(2, "spirit of wolf", category: 125, subcategory: 65, durationFormula: 3, duration: 360, (9, 1)),
        ]);
        Assert.Equal([9, 1], ties.SearchForClasses("", [9], SpellCatalog.MaxSearchLevel, 10, levelFirst: false).Select(spell => spell.LevelFor(9)));
        Assert.Equal([1, 9], ties.SearchForClasses("", [9], SpellCatalog.MaxSearchLevel, 10, levelDescending: false, levelFirst: false).Select(spell => spell.LevelFor(9)));
    }

    [Fact]
    public void BestWithin_KeepsTheHighestLevelAtOrBelowTheCap()
    {
        var catalog = SpellCatalog.Parse(
        [
            FullSpellLine(1, "Swift Like the Wind", category: 125, subcategory: 65, durationFormula: 3, duration: 360, (9, 63), (13, 44)),
            FullSpellLine(2, "Spirit of Wolf", category: 125, subcategory: 65, durationFormula: 3, duration: 360, (9, 49), (13, 30)),
            FullSpellLine(3, "Ancient Remedy", category: 42, subcategory: 42, durationFormula: 0, duration: 0, (9, 63)),
        ]);

        Assert.True(catalog.TryGet("1", out var swift));
        Assert.Equal((13, 44), swift.BestWithin([9, 8, 13], SpellCatalog.MaxSearchLevel));
        Assert.True(catalog.TryGet("2", out var wolf));
        Assert.Equal((9, 49), wolf.BestWithin([9, 13], SpellCatalog.MaxSearchLevel));
        Assert.True(catalog.TryGet("3", out var ancient));
        Assert.Null(ancient.BestWithin([9], SpellCatalog.MaxSearchLevel));
    }

    [Fact]
    public void ClassHasSpells_UsesCastingSkillAndTheLevelCap()
    {
        var discipline = FullSpellLine(2, "Throw Stone", category: 15, subcategory: 115, durationFormula: 0, duration: 0, (0, 1)).Split('^');
        discipline[32] = "33";
        var catalog = SpellCatalog.Parse(
        [
            string.Join("^", discipline),
            FullSpellLine(1, "Warrior's Auspice", category: 125, subcategory: 64, durationFormula: 0, duration: 0, (0, 99)),
            FullSpellLine(3, "Minor Healing", category: 42, subcategory: 42, durationFormula: 0, duration: 0, (1, 1)),
        ]);

        Assert.False(catalog.ClassHasSpells(0));
        Assert.True(catalog.ClassHasSpells(1));
        Assert.False(catalog.ClassHasSpells(6));
    }

    [Fact]
    public void KeepSingleGem_ClearsTheSpellFromEveryOtherGem()
    {
        var slots = new[] { "12", "-1", "12", "0", "12", "40" };

        Assert.True(SpellGems.KeepSingleGem(slots, 2));
        Assert.Equal(["-1", "-1", "12", "0", "-1", "40"], slots);
        Assert.False(SpellGems.KeepSingleGem(slots, 2));
        Assert.False(SpellGems.KeepSingleGem(slots, 1));
    }

    [Fact]
    public void SpellIconLayout_MapsTheSheetGrid()
    {
        Assert.True(SpellIconLayout.TryLocate(0, out var sheet, out var column, out var row));
        Assert.Equal(1, sheet);
        Assert.Equal(0, column);
        Assert.Equal(0, row);
        Assert.True(SpellIconLayout.TryLocate(2, out sheet, out column, out row));
        Assert.Equal(1, sheet);
        Assert.Equal(2, column);
        Assert.Equal(0, row);
        Assert.True(SpellIconLayout.TryLocate(161, out sheet, out column, out row));
        Assert.Equal(2, sheet);
        Assert.Equal(1, column);
        Assert.Equal(6, row);
        Assert.False(SpellIconLayout.TryLocate(-1, out _, out _, out _));
    }

    [Fact]
    public void ItemIconLayout_MapsDragItemSheetsFromIcon500()
    {
        Assert.Equal(585, ItemIconLayout.Parse("A585"));
        Assert.Equal(596, ItemIconLayout.Parse("a596"));
        Assert.Null(ItemIconLayout.Parse("@-1"));
        Assert.Null(ItemIconLayout.Parse(string.Empty));
        Assert.True(ItemIconLayout.TryLocate(585, out var sheet, out var column, out var row));
        Assert.Equal(3, sheet);
        Assert.Equal(2, column);
        Assert.Equal(1, row);
        Assert.True(ItemIconLayout.TryLocate(764, out sheet, out column, out row));
        Assert.Equal(8, sheet);
        Assert.Equal(2, column);
        Assert.Equal(0, row);
        Assert.True(ItemIconLayout.TryLocate(806, out sheet, out column, out row));
        Assert.Equal(9, sheet);
        Assert.Equal(3, column);
        Assert.Equal(0, row);
        Assert.False(ItemIconLayout.TryLocate(499, out _, out _, out _));
    }

    [Fact]
    public void SpellDetails_FillTheDescriptionAndListTheCastingNumbers()
    {
        var fields = Enumerable.Repeat("0", 174).ToArray();
        fields[0] = "376";
        fields[1] = "Shock of Fire";
        fields[4] = "200";
        fields[8] = "1500";
        fields[9] = "1500";
        fields[10] = "1500";
        fields[14] = "10";
        fields[29] = "2";
        fields[30] = "5";
        fields[32] = "24";
        fields[36 + 11] = "4";
        fields[87] = "25";
        fields[88] = "38";
        fields[172] = "1|0|-10|0|102|20";
        for (var i = 0; i < CharacterClasses.Count; i++)
        {
            if (fields[36 + i] == "0")
            {
                fields[36 + i] = "255";
            }
        }

        var catalog = SpellCatalog.Parse(
            [string.Join("^", fields)],
            ["376^6^Surrounds your target in fire, causing between #1 and @1 damage.^0^", "1^5^Aegolism^0^"]);
        Assert.True(catalog.TryGet("376", out var spell));
        Assert.Equal("Surrounds your target in fire, causing between 10 and 20 damage.", SpellPresentation.Describe(spell, catalog.Description(376)));
        Assert.Equal(string.Empty, catalog.Description(1));

        var lines = SpellPresentation.Lines(spell);
        Assert.Contains(lines, line => line is { Label: "Mana", Value: "10" });
        Assert.Contains(lines, line => line is { Label: "Cast", Value: "1.5 seconds" });
        Assert.Contains(lines, line => line is { Label: "Recovery", Value: "1.5 seconds" });
        Assert.Contains(lines, line => line is { Label: "Reuse", Value: "1.5 seconds" });
        Assert.Contains(lines, line => line is { Label: "Range", Value: "200" });
        Assert.Contains(lines, line => line is { Label: "Resist", Value: "Fire" });
        Assert.Contains(lines, line => line is { Label: "Target", Value: "Single" });
        Assert.Contains(lines, line => line is { Label: "Skill", Value: "Evocation" });
        Assert.Contains(lines, line => line is { Label: "Classes", Value: "WIZ(4)" });
        Assert.DoesNotContain(lines, line => line.Label == "Duration");
    }

    [Fact]
    public void SpellDetails_SubstituteTargetCapAndIntervalAndKeepUnconfirmedTokens()
    {
        var fields = SpellFields(901, "Target Cap");
        fields[5] = "20";
        fields[13] = "360000";
        fields[143] = "30";
        var catalog = SpellCatalog.Parse(
            [string.Join("^", fields)],
            ["901^6^Blinds up to %T creatures for %i seconds within %J feet. %H %N %L %R %S %O %M %Q %C %Y^0^"]);

        Assert.True(catalog.TryGet("901", out var spell));
        Assert.Equal(30, spell.Casting.MaxTargets);
        Assert.Equal(20, spell.Casting.AreaRange);
        Assert.Equal(360000, spell.Casting.AreaDuration);
        const string expected = "Blinds up to 30 creatures for 360 seconds within %J feet. %H %N %L %R %S %O %M %Q %C %Y";
        Assert.Equal(expected, SpellPresentation.Describe(spell, catalog.Description(901)));
        Assert.Contains(expected, SpellPresentation.DetailText(spell, catalog.Description(901), catalog));

        fields[13] = "7500";
        var fractional = SpellCatalog.Parse(
            [string.Join("^", fields)],
            ["901^6^Holds the target for %i seconds within %J feet.^0^"]);
        Assert.True(fractional.TryGet("901", out var fractionSpell));
        Assert.Equal("Holds the target for 7.5 seconds within %J feet.", SpellPresentation.Describe(fractionSpell, fractional.Description(901)));

        fields[13] = "0";
        var unset = SpellCatalog.Parse(
            [string.Join("^", fields)],
            ["901^6^Holds the target for %i seconds within %J feet.^0^"]);
        Assert.True(unset.TryGet("901", out var unsetSpell));
        Assert.Equal("Holds the target for %i seconds within %J feet.", SpellPresentation.Describe(unsetSpell, unset.Description(901)));
    }

    [Fact]
    public void SpellDetails_UseTheDescriptionNumberAndNameTheLinkedEffect()
    {
        var parent = SpellFields(10, "Feline Spirit");
        parent[4] = "100";
        parent[8] = "3000";
        parent[9] = "1500";
        parent[10] = "1500";
        parent[14] = "212";
        parent[28] = "1";
        parent[30] = "56";
        parent[32] = "5";
        parent[36 + 9] = "50";
        parent[86] = "900";
        parent[87] = "125";
        parent[166] = "100100040";
        parent[172] = "1|10|0|0|100|0$5|85|42|400|100|0";

        var triggered = SpellFields(42, "Savage Maw");
        triggered[4] = "100";
        triggered[29] = "7";
        triggered[30] = "5";
        triggered[79] = "-200";
        triggered[115] = "50";
        triggered[172] = "1|0|-154|0|100|0";

        var catalog = SpellCatalog.Parse(
            [string.Join("^", parent), string.Join("^", triggered)],
            [
                "10^6^This blurb is filed under the spell id and must not be used.^0^",
                "900^6^Infuses your target with the spirit of the puma, allowing them to make a savage attack for extra damage.^0^",
                "100100040^27^Talisman of the Panther^0^",
            ],
            [
                "10^^^You begin to snarl as your features become feline.^ growls with the spirit of the puma.^The spirit of the puma departs.^",
                "42^^^Savage jaws tear into your skin.^ is rent by a savage maw.^",
            ]);

        Assert.True(catalog.TryGet("10", out var spell));
        Assert.Equal(
            "Infuses your target with the spirit of the puma, allowing them to make a savage attack for extra damage.",
            SpellPresentation.Describe(spell, catalog.Description(10)));

        var effects = SpellPresentation.EffectText(spell, catalog);
        Assert.Contains("Grants the following effects to the target:", effects);
        Assert.Contains("Melee attacks may trigger Savage Maw.", effects);
        Assert.Contains("On You: You begin to snarl as your features become feline.", effects);
        Assert.Contains("On Other: Soandso growls with the spirit of the puma.", effects);
        Assert.Contains("Wears Off: The spirit of the puma departs.", effects);
        Assert.Contains("Savage Maw", effects);
        Assert.Contains("No eligible class", effects);
        Assert.Contains("Cast as Level: 50", effects);
        Assert.Contains("Inflicts the following effects to the target:", effects);
        Assert.Contains("Decreases Health by 154", effects);
        Assert.Contains("On You: Savage jaws tear into your skin.", effects);
        Assert.Contains("On Other: Soandso is rent by a savage maw.", effects);
        Assert.Contains("Duration: Instant", effects);
        Assert.Contains("Target: Single", effects);
        Assert.DoesNotContain("(181)", effects);
        Assert.DoesNotContain("VII", effects);

        const string blurb = "Infuses your target with the spirit of the puma, allowing them to make a savage attack for extra damage.";
        var detail = SpellPresentation.DetailText(spell, catalog.Description(10), catalog, catalog.SpellLine(10));
        var typeAt = detail.IndexOf("Category: Utility Beneficial", StringComparison.Ordinal);
        var blurbAt = detail.IndexOf(blurb, StringComparison.Ordinal);
        var onYouAt = detail.IndexOf("On You:", StringComparison.Ordinal);
        Assert.True(typeAt >= 0 && typeAt < blurbAt && blurbAt < onYouAt, detail);
        Assert.True(detail.IndexOf("Grants the following effects to the target:", StringComparison.Ordinal) > blurbAt);
        Assert.True(detail.IndexOf("Grants the following effects to the target:", StringComparison.Ordinal) < onYouAt);
        Assert.Contains("Category: Utility Beneficial\n\n" + blurb, detail);
        Assert.DoesNotContain("\n\n\n", detail);

        var withoutBlurb = SpellPresentation.DetailText(spell, null, catalog, catalog.SpellLine(10));
        Assert.Contains("Category: Utility Beneficial\n\nGrants the following effects to the target:", withoutBlurb);
        Assert.DoesNotContain(blurb, withoutBlurb);
        Assert.DoesNotContain("\n\n\n", withoutBlurb);

        var lines = SpellPresentation.Lines(spell, catalog.SpellLine(10));
        Assert.Contains(lines, line => line is { Label: "Spell line", Value: "Talisman of the Panther" });
        Assert.Contains(lines, line => line is { Label: "Target", Value: "Target Group Member or Self" });
        Assert.Contains(lines, line => line is { Label: "Classes", Value: "SHM(50)" });
        Assert.Contains(lines, line => line is { Label: "Level", Value: "50" });
        Assert.Contains(lines, line => line is { Label: "Skill", Value: "Alteration" });
        Assert.Contains(lines, line => line is { Label: "Mana", Value: "212" });
        Assert.Contains(lines, line => line is { Label: "Cast", Value: "3 seconds" });
        Assert.DoesNotContain(lines, line => line.Label == "Resist");
        Assert.Contains("Resist: Prismatic -200", effects);
    }

    [Fact]
    public void SpellDetails_ShowTheTriggeredMawWhenTheEffectBlobHasMoved()
    {
        var parent = SpellFields(6906, "Spirit of the Puma VII").ToList();
        parent[4] = "100";
        parent[8] = "3000";
        parent[14] = "212";
        parent[28] = "1";
        parent[30] = "56";
        parent[32] = "5";
        parent[36 + 9] = "50";
        parent[172] = "0";
        while (parent.Count < 210)
        {
            parent.Add("0");
        }

        parent.Add("1|10|0|0|100|0$5|85|6908|400|100|0");

        var maw = SpellFields(6908, "Puma Maw III").ToList();
        maw[4] = "100";
        maw[29] = "7";
        maw[30] = "5";
        maw[79] = "-245";
        maw[115] = "50";
        maw[172] = "0";
        while (maw.Count < 210)
        {
            maw.Add("0");
        }

        maw.Add("1|0|-154|0|100|0");

        var catalog = SpellCatalog.Parse(
            [string.Join("^", parent), string.Join("^", maw)],
            messageLines:
            [
                "6908^^^Savage jaws tear into your skin.^ is rent by a savage maw.^",
            ]);
        Assert.True(catalog.TryGet("6906", out var spell));

        var shifted = SpellPresentation.EffectText(spell, catalog);
        Assert.Contains("Grants the following effects to the target:", shifted);
        Assert.Contains("Melee attacks may trigger Puma Maw III.", shifted);
        Assert.Contains("Puma Maw III", shifted);
        Assert.Contains("No eligible class", shifted);
        Assert.Contains("Cast as Level: 50", shifted);
        Assert.Contains("Decreases Health by 154", shifted);
        Assert.Contains("Resist: Prismatic -245", shifted);
        Assert.Contains("On You: Savage jaws tear into your skin.", shifted);
    }

    [Fact]
    public void SpellDetails_FollowTheSpellWindowOrderThroughResist()
    {
        var fields = SpellFields(27, "Pogonip");
        fields[4] = "150";
        fields[5] = "20";
        fields[8] = "3500";
        fields[9] = "1500";
        fields[10] = "12000";
        fields[11] = "3";
        fields[14] = "125";
        fields[29] = "3";
        fields[30] = "8";
        fields[32] = "14";
        fields[36 + 9] = "15";
        fields[78] = "1";
        fields[79] = "-10";
        fields[87] = "2";
        var catalog = SpellCatalog.Parse([string.Join("^", fields)]);
        Assert.True(catalog.TryGet("27", out var spell));

        var lines = SpellPresentation.Lines(spell, "Cloudburst Hail");
        Assert.Contains(lines, line => line is { Label: "Resist", Value: "Cold -10" });
        var labels = lines.Select(line => line.Label).ToArray();
        Assert.Equal(
            [
                "Classes", "Level", "Skill", "Duration", "Mana", "Cast", "Recovery", "Reuse",
                "Spell line", "Resist", "Target", "Range", "Area", "Category",
            ],
            labels);
    }

    [Fact]
    public void SpellDetails_ScaleSelfBuffsToTheLevelCap()
    {
        var shaman = SpellFields(337, "Rage");
        shaman[28] = "1";
        shaman[30] = "6";
        shaman[36 + 9] = "45";
        shaman[172] = "1|10|0|0|100|0$3|1|35|0|101|60$5|6|2|0|102|55$6|4|2|0|102|51$7|5|2|0|102|60";

        var wizard = SpellFields(338, "Arcane Armor");
        wizard[28] = "1";
        wizard[30] = "6";
        wizard[36 + 11] = "45";
        wizard[172] = "1|1|35|0|101|60";

        var catalog = SpellCatalog.Parse([string.Join("^", shaman), string.Join("^", wizard)]);
        Assert.True(catalog.TryGet("337", out var rage));
        var effects = SpellPresentation.EffectText(rage, catalog);
        Assert.Contains("Grants the following effects to yourself:", effects);
        Assert.Contains("Increases Armor Class by 15", effects);
        Assert.Contains("Increases Agility by 52", effects);
        Assert.Contains("Increases Strength by 51", effects);
        Assert.Contains("Increases Dexterity by 52", effects);
        Assert.DoesNotContain("Charisma", effects);

        Assert.True(catalog.TryGet("338", out var arcane));
        Assert.Contains("Increases Armor Class by 20", SpellPresentation.EffectText(arcane, catalog));
    }

    [Fact]
    public void SpellDetails_ShowACappedDurationAndAPermanentBuff()
    {
        var wolf = SpellCatalog.Parse([FullSpellLine(278, "Spirit of Wolf", category: 125, subcategory: 65, durationFormula: 3, duration: 360, (9, 50))]);
        Assert.True(wolf.TryGet("278", out var sow));
        Assert.Equal(360, SpellPresentation.Ticks(50, 3, 360));
        Assert.Equal("36:00", SpellPresentation.DurationText(sow));

        var partial = SpellCatalog.Parse([FullSpellLine(1, "Partial", category: 125, subcategory: 65, durationFormula: 201, duration: 0, (9, 50))]);
        Assert.True(partial.TryGet("1", out var leftover));
        Assert.Equal("20:06", SpellPresentation.DurationText(leftover));

        var longBuff = SpellCatalog.Parse([FullSpellLine(2, "Long", category: 125, subcategory: 65, durationFormula: 601, duration: 0, (9, 50))]);
        Assert.True(longBuff.TryGet("2", out var hour));
        Assert.Equal("01:00:06", SpellPresentation.DurationText(hour));

        var shield = SpellCatalog.Parse([FullSpellLine(288, "Minor Shielding", category: 45, subcategory: 87, durationFormula: 50, duration: 0, (9, 1))]);
        Assert.True(shield.TryGet("288", out var minor));
        Assert.Equal("Permanent", SpellPresentation.DurationText(minor));
    }

    [Fact]
    public void SpellDetails_ShowTheLevelCapSlowAndItsEffectMagnitudes()
    {
        var fields = SpellFields(507, "Togor's Insects");
        fields[4] = "200";
        fields[8] = "5000";
        fields[9] = "1500";
        fields[10] = "5000";
        fields[11] = "6";
        fields[12] = "50";
        fields[14] = "175";
        fields[29] = "1";
        fields[30] = "5";
        fields[32] = "5";
        fields[36 + 9] = "38";
        fields[79] = "-150";
        fields[172] = "1|10|0|0|100|0$2|11|90|0|102|30$3|35|12|0|100|0";

        var catalog = SpellCatalog.Parse(
            [string.Join("^", fields)],
            ["507^6^Engulfs your target in a swarm of insects, slowing their attack rate for %z.^0^"]);
        Assert.True(catalog.TryGet("507", out var spell));

        Assert.Equal("02:42", SpellPresentation.DurationText(spell));
        Assert.Equal(
            "Engulfs your target in a swarm of insects, slowing their attack rate for 02:42.",
            SpellPresentation.Describe(spell, catalog.Description(507)));

        var lines = SpellPresentation.Lines(spell);
        Assert.Contains(lines, line => line is { Label: "Mana", Value: "175" });
        Assert.Contains(lines, line => line is { Label: "Cast", Value: "5 seconds" });
        Assert.Contains(lines, line => line is { Label: "Recovery", Value: "1.5 seconds" });
        Assert.Contains(lines, line => line is { Label: "Reuse", Value: "5 seconds" });
        Assert.Contains(lines, line => line is { Label: "Range", Value: "200" });
        Assert.Contains(lines, line => line is { Label: "Resist", Value: "Magic -150" });

        var effects = SpellPresentation.EffectText(spell, catalog);
        Assert.Contains("Decreases Attack Speed by 60%", effects);
        Assert.Contains("Increases Disease Counters by 12", effects);
    }

    [Fact]
    public void SpellDetails_FindEffectSlotsAfterTheAugustColumn()
    {
        var fields = SpellFields(507, "Togor's Insects").ToList();
        fields[4] = "200";
        fields[11] = "6";
        fields[12] = "50";
        fields[14] = "175";
        fields[36 + 9] = "38";
        fields[172] = "0";
        while (fields.Count < 210)
        {
            fields.Add("0");
        }

        fields.Add("1|10|0|0|100|0$2|11|90|0|102|30$3|35|12|0|100|0");

        var catalog = SpellCatalog.Parse([string.Join("^", fields)]);
        Assert.True(catalog.TryGet("507", out var spell));

        var effects = SpellPresentation.EffectText(spell, catalog);
        Assert.Contains("Decreases Attack Speed by 60%", effects);
        Assert.Contains("Increases Disease Counters by 12", effects);
        Assert.DoesNotContain("Charisma", effects);
    }

    [Fact]
    public void SpellDetails_ScaleShamanSlowsToTheCasterLevel()
    {
        var drowsy = SpellFields(270, "Drowsy");
        drowsy[11] = "6";
        drowsy[12] = "35";
        drowsy[36 + 9] = "5";
        drowsy[172] = "2|11|90|0|109|75$3|35|3|0|100|0";

        var sleep = SpellFields(505, "Walking Sleep");
        sleep[11] = "6";
        sleep[12] = "35";
        sleep[36 + 9] = "13";
        sleep[172] = "2|11|80|0|109|65$3|35|6|0|100|0";

        var insects = SpellFields(506, "Tagar's Insects");
        insects[11] = "6";
        insects[12] = "35";
        insects[36 + 9] = "27";
        insects[172] = "2|11|80|0|101|50$3|35|9|0|100|0";

        var haste = SpellFields(7, "Quickness");
        haste[172] = "2|11|130|0|100|0";

        var catalog = SpellCatalog.Parse(
        [
            string.Join("^", drowsy),
            string.Join("^", sleep),
            string.Join("^", insects),
            string.Join("^", haste),
        ]);

        Assert.True(catalog.TryGet("270", out var drowsySpell));
        Assert.Equal("02:12", SpellPresentation.DurationText(drowsySpell, 40));
        Assert.Contains("Decreases Attack Speed by 20%", SpellPresentation.EffectText(drowsySpell, catalog, 40));
        Assert.Contains("Increases Disease Counters by 3", SpellPresentation.EffectText(drowsySpell, catalog, 40));
        Assert.Contains("Decreases Attack Speed by 22%", SpellPresentation.EffectText(drowsySpell, catalog, 50));

        Assert.True(catalog.TryGet("505", out var sleepSpell));
        Assert.Contains("Decreases Attack Speed by 30%", SpellPresentation.EffectText(sleepSpell, catalog, 40));
        Assert.Contains("Increases Disease Counters by 6", SpellPresentation.EffectText(sleepSpell, catalog, 40));

        Assert.True(catalog.TryGet("506", out var insectSpell));
        Assert.Contains("Decreases Attack Speed by 40%", SpellPresentation.EffectText(insectSpell, catalog, 40));
        Assert.Contains("Increases Disease Counters by 9", SpellPresentation.EffectText(insectSpell, catalog, 40));
        Assert.Contains("Decreases Attack Speed by 45%", SpellPresentation.EffectText(insectSpell, catalog, 50));
        Assert.Contains("Decreases Attack Speed by 50%", SpellPresentation.EffectText(insectSpell, catalog, 60));

        Assert.True(catalog.TryGet("7", out var quickness));
        Assert.Contains("Increases Attack Speed by 30%", SpellPresentation.EffectText(quickness, catalog, 40));
    }

    [Fact]
    public void SpellDetails_ShowAOneSecondStunAndItsLevelCap()
    {
        var fields = SpellFields(673, "Discordant Mind");
        fields[8] = "4000";
        fields[9] = "1500";
        fields[10] = "1500";
        fields[14] = "126";
        fields[29] = "1";
        fields[30] = "5";
        fields[32] = "24";
        fields[36 + 13] = "43";
        fields[79] = "-120";
        fields[172] = "1|0|-288|0|104|387$2|21|1000|500|100|55";

        var catalog = SpellCatalog.Parse([string.Join("^", fields)]);
        Assert.True(catalog.TryGet("673", out var spell));

        var effects = SpellPresentation.EffectText(spell, catalog, 40);
        Assert.Contains("Decreases Health by 387", effects);
        Assert.Contains("Stuns for 1 second(s) (Max Level 55).", effects);
        Assert.DoesNotContain("Stuns for 55", effects);
    }

    [Fact]
    public void SpellClassList_PutsTheLoadoutTrioFirstThenTheRestByCode()
    {
        var trio = SpellClassList.Build([9, 8, 13], HasSpells);

        Assert.Equal("SHM/ROG/ENC", trio[0].Label);
        Assert.True(trio[0].IsLoadout);
        Assert.Equal([9, 13], trio[0].Classes);
        Assert.Equal(["SHM", "ENC"], trio.Skip(1).Take(2).Select(choice => choice.Label));
        Assert.Equal("BRD", trio[3].Label);
        Assert.Equal(13, trio.Count);
        Assert.DoesNotContain(trio, choice => choice.Label is "WAR" or "ROG" or "MNK" or "BER");
        Assert.Equal(12, trio.SelectMany(choice => choice.Classes).Distinct().Count());

        var shaman = SpellClassList.Build([9], HasSpells);
        Assert.Equal("SHM", shaman[0].Label);
        Assert.True(shaman[0].IsLoadout);
        Assert.Equal(12, shaman.Count);
        Assert.Single(shaman, choice => choice.Label == "SHM");

        var everyone = SpellClassList.Build([], HasSpells);
        Assert.Equal(["SHM/ROG/ENC", "SHM", "ENC"], SpellClassList.Build([9, 8, 13], index => index is 9 or 13).Select(choice => choice.Label));
        Assert.Equal(12, everyone.Count);
        Assert.Equal("BRD", everyone[0].Label);
        Assert.DoesNotContain(everyone, choice => choice.IsLoadout);
    }

    [Fact]
    public void SpellWindows_ShowTheChosenSetOnACastSpellWindow()
    {
        Assert.True(SpellWindows.IsWindow("CastSpellWnd"));
        Assert.True(SpellWindows.IsWindow("CastSpellWnd_3"));
        Assert.False(SpellWindows.IsWindow("CastSpellWnd3"));
        Assert.False(SpellWindows.IsWindow("SpellBookWnd"));

        var sets = new[]
        {
            new SpellGemSet(1) { InUse = "1", Name = "Attack" },
            new SpellGemSet(2) { InUse = "0" },
            new SpellGemSet(3) { InUse = "1", Name = "Buffs" },
        };
        var window = IniDocument.Parse("[CastSpellWnd_3]\r\n").Find("CastSpellWnd_3");
        Assert.Equal(1, SpellWindows.SelectedIndex(window, sets));
        window!.Set(SpellWindows.SetKey, "3");
        Assert.Equal(3, SpellWindows.SelectedIndex(window, sets));
        window.Set(SpellWindows.SetKey, "2");
        Assert.Equal(1, SpellWindows.SelectedIndex(window, sets));
        Assert.False(SpellWindows.IsListed(sets[1]));
        Assert.True(SpellWindows.IsListed(sets[2]));
        Assert.False(SpellWindows.IsListed(new SpellGemSet(4) { InUse = "0", Name = "Voxx" }));
        Assert.Equal(2, SpellWindows.NextFreeIndex(
        [
            new SpellGemSet(1) { InUse = "1", Name = "Attack" },
            new SpellGemSet(4) { InUse = "0", Name = "Voxx" },
        ]));
        Assert.Equal(2, SpellWindows.NextFreeIndex(sets));
        var full = Enumerable.Range(1, SpellWindows.MaxSets)
            .Select(index => new SpellGemSet(index) { InUse = "1", Name = "Set" })
            .ToArray();
        Assert.Equal(0, SpellWindows.NextFreeIndex(full));
    }

    [Fact]
    public void NextFreeSpellSet_SkipsNamedUnusedSets()
    {
        const string text =
            "[SpellLoadouts]\r\n" +
            "SpellLoadout1.inuse=1\r\n" +
            "SpellLoadout1.name=Attack\r\n" +
            "SpellLoadout1.slot1=6877\r\n" +
            "SpellLoadout2.inuse=0\r\n" +
            "SpellLoadout4.inuse=0\r\n" +
            "SpellLoadout4.name=Voxx\r\n" +
            "SpellLoadout7.inuse=0\r\n" +
            "SpellLoadout7.slot2=55\r\n";
        var document = IniDocument.Parse(text);
        var section = document.Find("SpellLoadouts")!;
        var index = SpellWindows.NextFreeIndex(SpellGems.Read(section));
        Assert.Equal(2, index);

        var created = new SpellGemSet(index) { InUse = "1" };
        SpellGems.WriteSet(section, created);

        var added = Assert.Single(SpellGems.Read(section), set => set.Index == index);
        Assert.True(SpellWindows.IsListed(added));
        Assert.Equal("1", added.InUse);
        Assert.Equal(string.Empty, added.Name);
        Assert.All(added.Slots, slot => Assert.Equal(string.Empty, slot));
        Assert.Equal("1", section.Get("SpellLoadout2.inuse"));
        Assert.Equal(string.Empty, section.Get("SpellLoadout2.name"));
        for (var slot = 1; slot <= 14; slot++)
        {
            Assert.Equal(string.Empty, section.Get($"SpellLoadout2.slot{slot}"));
        }

        Assert.Equal("Attack", section.Get("SpellLoadout1.name"));
        Assert.Equal("6877", section.Get("SpellLoadout1.slot1"));
        Assert.Equal("0", section.Get("SpellLoadout4.inuse"));
        Assert.Equal("Voxx", section.Get("SpellLoadout4.name"));
        Assert.Null(section.Get("SpellLoadout4.slot1"));
        Assert.Equal("55", section.Get("SpellLoadout7.slot2"));
        Assert.Null(section.Get("SpellLoadout7.name"));
        Assert.Equal(3, SpellWindows.NextFreeIndex(SpellGems.Read(section)));

        var named = Enumerable.Range(1, SpellWindows.MaxSets)
            .Select(set => new SpellGemSet(set) { InUse = "0", Name = "Voxx" })
            .ToArray();
        Assert.Equal(0, SpellWindows.NextFreeIndex(named));
    }

    [Fact]
    public void SpellSets_SortByNameThenIndex()
    {
        var sets = new[]
        {
            ("", 4),
            ("Buffs", 8),
            ("attack", 3),
            ("Voxx", 1),
            ("", 2),
            ("Attack", 9),
        };
        Array.Sort(sets, (left, right) => SpellWindows.CompareByName(left.Item1, left.Item2, right.Item1, right.Item2));

        Assert.Equal(
        [
            ("attack", 3),
            ("Attack", 9),
            ("Buffs", 8),
            ("Voxx", 1),
            ("", 2),
            ("", 4),
        ], sets);
    }

    [Fact]
    public void StanceBars_AreTheStanceAndInvocationButtons()
    {
        Assert.True(StanceBars.IsWindow("StanceWnd"));
        Assert.True(StanceBars.IsWindow("StanceWnd_2"));
        Assert.False(StanceBars.IsWindow("StanceWnd2"));
        Assert.Equal("Offense", StanceBars.Buttons[0].Text);
        Assert.Equal("0", StanceBars.Buttons[0].Color);
        Assert.Equal("Arcane Mastery", StanceBars.Buttons[^2].Text);
        Assert.Equal("Unyielding", StanceBars.Buttons[^1].Text);
        Assert.Equal(19, StanceBars.Buttons.Count);
    }

    private static bool HasSpells(int index) => index is not (0 or 6 or 8 or 15);

    private static string WithIcon(string line, int icon)
    {
        var fields = line.Split('^');
        fields[76] = icon.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return string.Join("^", fields);
    }

    private static string WithBookCategory(string line, int bookCategory)
    {
        var fields = line.Split('^');
        fields[55] = bookCategory.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return string.Join("^", fields);
    }

    private static string[] SpellFields(int id, string name)
    {
        var fields = Enumerable.Repeat("0", 174).ToArray();
        fields[0] = id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        fields[1] = name;
        for (var i = 0; i < CharacterClasses.Count; i++)
        {
            fields[36 + i] = "255";
        }

        return fields;
    }

    private static string SpellLine(int id, string name, params (int Class, int Level)[] levels)
    {
        var fields = Enumerable.Repeat("0", 60).ToArray();
        fields[0] = id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        fields[1] = name;
        for (var i = 0; i < CharacterClasses.Count; i++)
        {
            fields[36 + i] = "255";
        }

        foreach (var (classIndex, level) in levels)
        {
            fields[36 + classIndex] = level.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return string.Join("^", fields);
    }

    private static string FullSpellLine(
        int id,
        string name,
        int category,
        int subcategory,
        int durationFormula,
        int duration,
        params (int Class, int Level)[] levels)
    {
        var fields = Enumerable.Repeat("0", 89).ToArray();
        fields[0] = id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        fields[1] = name;
        fields[11] = durationFormula.ToString(System.Globalization.CultureInfo.InvariantCulture);
        fields[12] = duration.ToString(System.Globalization.CultureInfo.InvariantCulture);
        fields[32] = "5";
        fields[87] = category.ToString(System.Globalization.CultureInfo.InvariantCulture);
        fields[88] = subcategory.ToString(System.Globalization.CultureInfo.InvariantCulture);
        for (var i = 0; i < CharacterClasses.Count; i++)
        {
            fields[36 + i] = "255";
        }

        foreach (var (classIndex, level) in levels)
        {
            fields[36 + classIndex] = level.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return string.Join("^", fields);
    }
}
