using System.Globalization;
using System.Text.RegularExpressions;

namespace EqlLoadouts.Core.Content;

/// <summary>Casting numbers and the raw effect-slot text from one spells_us.txt row.</summary>
public sealed record SpellCasting(
    int Mana,
    int CastTime,
    int RecoveryTime,
    int RecastTime,
    double Range,
    double AreaRange,
    int Resist,
    int Target,
    int Skill,
    /// <summary>Field 13 in milliseconds. The %i token prints this as seconds. Zero leaves %i literal.</summary>
    int AreaDuration,
    string Effects,
    int MaxTargets)
{
    public static SpellCasting Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, string.Empty, 0);
}

/// <summary>How a power-adjusted parenthetical is colored. The game paints a better mana cost blue, a better cast time cyan, and a better effect green.</summary>
public enum SpellValueTone
{
    None,
    Mana,
    Time,
    Benefit,
    Worse,
}

public readonly record struct SpellDetailLine(string Label, string Value, string Adjusted = "", SpellValueTone Tone = SpellValueTone.None)
{
    public string AdjustedText => Adjusted.Length == 0 ? string.Empty : " (" + Adjusted + ")";
}

/// <summary>Turns a spell's file fields and dbstr blurb into the text shown in the details column.</summary>
public static partial class SpellPresentation
{
    /// <summary>One spell effect at a character level, using the same formula as the details column. Blank charisma slots are left out.</summary>
    public readonly record struct SpellEffectAmount(int Slot, int Spa, int Amount);

    /// <summary>Effect amounts at <paramref name="level"/>. Armor class is the number the spell window shows.</summary>
    public static IReadOnlyList<SpellEffectAmount> EffectsAt(SpellInfo spell, int level)
    {
        var effects = new List<SpellEffectAmount>();
        foreach (var slot in Slots(spell.Casting.Effects))
        {
            if (slot.Spa == 10 && slot.Base == 0 && slot.Calc == 100)
            {
                continue;
            }

            var amount = ShownAmount(spell, slot.Spa, EffectValue(slot, level));
            if (amount == 0)
            {
                continue;
            }

            effects.Add(new SpellEffectAmount(slot.Slot, slot.Spa, amount));
        }

        return effects;
    }

    public static string Describe(SpellInfo spell, string? description, int casterLevel = 0)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return string.Empty;
        }

        var text = description.Replace("<br>", "\n", StringComparison.OrdinalIgnoreCase).Replace("<br/>", "\n", StringComparison.OrdinalIgnoreCase);
        var slots = Slots(spell.Casting.Effects);
        text = SlotToken().Replace(text, match =>
        {
            if (!int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var slot))
            {
                return match.Value;
            }

            var found = slots.Find(entry => entry.Slot == slot);
            if (found is null)
            {
                return match.Value;
            }

            var number = match.Groups[1].Value[0] == '#' ? found.Base : found.Max;
            return Math.Abs(number).ToString(CultureInfo.InvariantCulture);
        });

        var duration = DurationText(spell, casterLevel);
        text = text.Replace("%z", duration.Length > 0 ? duration : "a while", StringComparison.OrdinalIgnoreCase);
        // Field 144 is the creature cap. The area radius is field 5 and is not %T.
        // Field 13 is the %i duration in milliseconds. Sentinel (spell 299) shows 360000 as 360.
        // Zero leaves %i literal. %J, %H, %N, %L, %R, %S, %O, %M, %Q, %C, and %Y stay literal.
        text = text.Replace("%T", spell.Casting.MaxTargets.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        if (spell.Casting.AreaDuration > 0)
        {
            text = text.Replace("%i", FormatIntervalSeconds(spell.Casting.AreaDuration), StringComparison.Ordinal);
        }

        return text.Trim();
    }

    public static IReadOnlyList<SpellDetailLine> Lines(SpellInfo spell, string? spellLine = null, int casterLevel = 0, int power = 0)
    {
        var casting = spell.Casting;
        var lines = new List<SpellDetailLine>();
        var level = casterLevel > 0 ? casterLevel : SpellCatalog.MaxSearchLevel;
        var kind = SpellScale.KindOf(spell, level);
        power = Math.Clamp(power, 0, 10);
        var usable = spell.UsableBy();
        if (usable.Count == 0)
        {
            if (spell.HasClassLevels)
            {
                Add(lines, "Classes", "No eligible class");
            }

            if (spell.CastLevel > 0)
            {
                Add(lines, "Cast as level", spell.CastLevel.ToString(CultureInfo.InvariantCulture));
            }
        }
        else
        {
            Add(lines, "Classes", Classes(spell));
        }

        // Level and skill, then duration directly above mana. Resist follows reuse, then target and range.
        if (usable.Count == 1)
        {
            Add(lines, "Level", usable[0].Level.ToString(CultureInfo.InvariantCulture));
        }

        Add(lines, "Skill", SkillName(casting.Skill));
        var duration = DurationText(spell, casterLevel);
        var scaledTicks = SpellScale.DurationTicks(Ticks(level, spell.DurationFormula, spell.Duration), power, kind);
        var scaledDuration = scaledTicks == Ticks(level, spell.DurationFormula, spell.Duration) ? string.Empty : TimeFromTicks(scaledTicks);
        Add(lines, "Duration", duration, scaledDuration, SpellValueTone.Benefit);
        var mana = SpellScale.Mana(casting.Mana, power, kind);
        lines.Add(Figure("Mana", casting.Mana.ToString(CultureInfo.InvariantCulture), mana == casting.Mana ? string.Empty : mana.ToString(CultureInfo.InvariantCulture), mana < casting.Mana ? SpellValueTone.Mana : SpellValueTone.Worse));
        var cast = FormatMilliseconds(casting.CastTime);
        var scaledCast = FormatPowerTime(SpellScale.CastMilliseconds(casting.CastTime, power, kind));
        lines.Add(Figure("Cast", cast, Same(cast, scaledCast) ? string.Empty : scaledCast, SpellValueTone.Time));
        Add(lines, "Recovery", casting.RecoveryTime > 0 ? FormatMilliseconds(casting.RecoveryTime) : string.Empty, RecoveryAdjusted(casting.RecoveryTime, power), SpellValueTone.Time);
        Add(lines, "Reuse", casting.RecastTime > 0 ? FormatMilliseconds(casting.RecastTime) : string.Empty, ReuseAdjusted(casting.RecastTime, power), SpellValueTone.Time);
        Add(lines, "Spell line", spellLine ?? string.Empty);
        Add(lines, "Resist", ResistText(casting.Resist, spell.ResistAdjust), ResistAdjusted(casting.Resist, spell.ResistAdjust, power, kind), SpellScale.Resist(spell.ResistAdjust, power, kind) < spell.ResistAdjust ? SpellValueTone.Benefit : SpellValueTone.Worse);
        Add(lines, "Target", TargetName(casting.Target));
        Add(lines, "Range", casting.Range > 0 ? Number(casting.Range) : string.Empty);
        Add(lines, "Area", casting.AreaRange > 0 ? Number(casting.AreaRange) : string.Empty);
        Add(lines, "Category", spell.TypeName);
        if (!spell.SubcategoryName.Equals(spell.TypeName, StringComparison.Ordinal))
        {
            Add(lines, "Subcategory", spell.SubcategoryName);
        }

        AddRate(lines, spell, kind, level, power);

        if (!spell.DamageName.Equals(spell.SubcategoryName, StringComparison.Ordinal))
        {
            Add(lines, "Damage", spell.DamageName);
        }
        return lines;
    }

    /// <summary>
    /// Details column under the spell panel: stat lines, the book blurb, then effect text.
    /// A blank blurb is left out so it does not open a gap before the effect text.
    /// </summary>
    public static string DetailText(SpellInfo spell, string? description, SpellCatalog catalog, string? spellLine = null, int casterLevel = 0)
    {
        var sections = new List<string>();
        var stats = Lines(spell, spellLine, casterLevel);
        if (stats.Count > 0)
        {
            sections.Add(string.Join("\n", stats.Select(line => line.Label + ": " + line.Value + line.AdjustedText)));
        }

        var blurb = Describe(spell, description, casterLevel);
        if (blurb.Length > 0)
        {
            sections.Add(blurb);
        }

        var effects = EffectText(spell, catalog, casterLevel);
        if (effects.Length > 0)
        {
            sections.Add(effects);
        }

        return string.Join("\n\n", sections);
    }

    /// <summary>
    /// Generated effect sentences, landing messages, and one level of triggered-spell summary.
    /// Amounts use <paramref name="casterLevel"/>, or the level cap when that is 0.
    /// A mote rank above 0 puts the adjusted amount in parentheses, the way the spell window does.
    /// </summary>
    public static string EffectText(SpellInfo spell, SpellCatalog catalog, int casterLevel = 0, int power = 0)
        => string.Join("\n", EffectLines(spell, catalog, casterLevel, power).Select(line => line.Value + line.AdjustedText));

    /// <summary>Effect sentences with the power-adjusted amount kept separate so the window can color the parentheses.</summary>
    public static IReadOnlyList<SpellDetailLine> EffectLines(SpellInfo spell, SpellCatalog catalog, int casterLevel = 0, int power = 0)
    {
        var seen = new HashSet<int>();
        if (!seen.Add(spell.Id))
        {
            return [];
        }

        var lines = new List<SpellDetailLine>();
        var sentences = new List<SpellDetailLine>();
        var links = new List<int>();
        var level = casterLevel > 0 ? casterLevel : SpellCatalog.MaxSearchLevel;
        power = Math.Clamp(power, 0, 10);
        foreach (var slot in Slots(spell.Casting.Effects))
        {
            if (slot.Spa == WeaponProc && slot.Base > 0)
            {
                links.Add(slot.Base);
            }

            if (ReadEffect(slot, spell, catalog, level, power) is { } piece)
            {
                sentences.Add(piece.Line);
            }
        }

        if (sentences.Count > 0)
        {
            var who = spell.Casting.Target == 6 ? "yourself" : "the target";
            var header = spell.GoodEffect == 0
                ? "Inflicts the following effects to " + who + ":"
                : "Grants the following effects to " + who + ":";
            lines.Add(new SpellDetailLine(string.Empty, header));
            lines.AddRange(sentences);
        }

        var spoken = SpokenLines(catalog.Spoken(spell.Id));
        if (spoken.Length > 0)
        {
            foreach (var line in spoken.Split('\n'))
            {
                if (line.Length > 0)
                {
                    lines.Add(new SpellDetailLine(string.Empty, line));
                }
            }
        }

        foreach (var link in links.Distinct())
        {
            if (seen.Contains(link) || !catalog.TryGet(link, out var linked))
            {
                continue;
            }

            var summary = LinkedSummary(linked, catalog, level, seen);
            if (summary.Length == 0)
            {
                continue;
            }

            foreach (var line in summary.Split('\n'))
            {
                lines.Add(new SpellDetailLine(string.Empty, line));
            }
        }

        return lines;
    }

    private static string EffectText(SpellInfo spell, SpellCatalog catalog, int casterLevel, int depth, HashSet<int> seen)
    {
        if (!seen.Add(spell.Id))
        {
            return string.Empty;
        }

        var blocks = new List<string>();
        var sentences = new List<string>();
        var links = new List<int>();
        var level = casterLevel > 0 ? casterLevel : SpellCatalog.MaxSearchLevel;
        foreach (var slot in Slots(spell.Casting.Effects))
        {
            if (slot.Spa == WeaponProc && slot.Base > 0)
            {
                links.Add(slot.Base);
            }

            if (ReadEffect(slot, spell, catalog, level, 0) is { } piece)
            {
                sentences.Add(piece.Display);
            }
        }

        if (sentences.Count > 0)
        {
            var who = spell.Casting.Target == 6 ? "yourself" : "the target";
            var header = spell.GoodEffect == 0
                ? "Inflicts the following effects to " + who + ":"
                : "Grants the following effects to " + who + ":";
            blocks.Add(header + "\n" + string.Join("\n", sentences));
        }

        if (depth == 0)
        {
            var spoken = SpokenLines(catalog.Spoken(spell.Id));
            if (spoken.Length > 0)
            {
                blocks.Add(spoken);
            }

            foreach (var link in links.Distinct())
            {
                if (seen.Contains(link) || !catalog.TryGet(link, out var linked))
                {
                    continue;
                }

                var summary = LinkedSummary(linked, catalog, level, seen);
                if (summary.Length > 0)
                {
                    blocks.Add(summary);
                }
            }
        }

        return string.Join("\n\n", blocks);
    }

    private static string LinkedSummary(SpellInfo spell, SpellCatalog catalog, int level, HashSet<int> seen)
    {
        var lines = new List<string> { spell.Name };
        if (spell.HasClassLevels && spell.UsableBy().Count == 0)
        {
            lines.Add("No eligible class");
            if (spell.CastLevel > 0)
            {
                lines.Add(string.Create(CultureInfo.InvariantCulture, $"Cast as Level: {spell.CastLevel}"));
            }
        }
        else
        {
            var classes = Classes(spell);
            if (classes.Length > 0)
            {
                lines.Add(classes);
            }
        }

        var duration = DurationText(spell, level);
        lines.Add("Duration: " + (duration.Length > 0 ? duration : "Instant"));
        lines.Add("Cast: " + FormatMilliseconds(spell.Casting.CastTime));
        var resist = ResistName(spell.Casting.Resist);
        if (resist.Length > 0)
        {
            lines.Add(spell.ResistAdjust != 0
                ? string.Create(CultureInfo.InvariantCulture, $"Resist: {resist} {spell.ResistAdjust}")
                : "Resist: " + resist);
        }

        var target = TargetName(spell.Casting.Target);
        if (target.Length > 0)
        {
            lines.Add("Target: " + target);
        }

        if (spell.Casting.Range > 0)
        {
            lines.Add("Range: " + Number(spell.Casting.Range));
        }

        var effects = EffectText(spell, catalog, level, 1, seen);
        if (effects.Length > 0)
        {
            lines.Add(effects);
        }

        var spoken = SpokenLines(catalog.Spoken(spell.Id));
        if (spoken.Length > 0)
        {
            lines.Add(spoken);
        }

        return string.Join("\n", lines);
    }

    private readonly record struct EffectRow(string Text, string Adjusted, SpellValueTone Tone)
    {
        public SpellDetailLine Line => new(string.Empty, Text, Adjusted, Adjusted.Length == 0 ? SpellValueTone.None : Tone);

        public string Display => Adjusted.Length == 0 ? Text : Text + " (" + Adjusted + ")";
    }

    private static EffectRow? ReadEffect(EffectSlot slot, SpellInfo spell, SpellCatalog catalog, int level, int power)
    {
        switch (slot.Spa)
        {
            case 0 or 79 or 100:
                var health = EffectValue(slot, level);
                if (health == 0)
                {
                    return null;
                }

                var shown = Math.Abs(health);
                var overTime = slot.Spa == 100 || slot.Spa == 0 && Ticks(level, spell.DurationFormula, spell.Duration) > 0;
                var perRank = health < 0 && !overTime ? 6 : 3;
                var scaled = SpellScale.Grow(shown, power, perRank);
                var healthVerb = health < 0 ? "Decreases" : "Increases";
                return new EffectRow(
                    string.Create(CultureInfo.InvariantCulture, $"{healthVerb} Health by {shown}"),
                    scaled == shown ? string.Empty : scaled.ToString(CultureInfo.InvariantCulture),
                    SpellValueTone.Benefit);
            case 11:
                return Plain(AttackSpeedSentence(EffectValue(slot, level)));
            case 21:
                return Plain(StunSentence(slot, level));
            case WeaponProc when slot.Base > 0 && catalog.TryGet(slot.Base, out var triggered):
                return new EffectRow("Melee attacks may trigger " + triggered.Name + ".", string.Empty, SpellValueTone.None);
            default:
                return Plain(AttributeSentence(slot.Spa, ShownAmount(spell, slot.Spa, EffectValue(slot, level))));
        }
    }

    private static EffectRow? Plain(string? text)
        => text is null ? null : new EffectRow(text, string.Empty, SpellValueTone.None);

    /// <summary>
    /// Attack speed is a haste percent. 100 is unchanged, 80 is a 20% slow, and 130 is 30% haste.
    /// </summary>
    private static string? AttackSpeedSentence(int haste)
    {
        if (haste == 0)
        {
            return null;
        }

        var delta = haste < 0 ? haste : haste - 100;
        if (delta == 0)
        {
            return null;
        }

        var verb = delta < 0 ? "Decreases" : "Increases";
        return string.Create(CultureInfo.InvariantCulture, $"{verb} Attack Speed by {Math.Abs(delta)}%");
    }

    /// <summary>
    /// Stun base is the duration in milliseconds. The max is the highest level that can be stunned,
    /// not a cap on that duration. Discordant Mind is 1000 ms up to level 55, which the window prints as 1 second.
    /// </summary>
    private static string? StunSentence(EffectSlot slot, int level)
    {
        var milliseconds = EffectValue(slot with { Max = 0 }, level);
        if (milliseconds <= 0)
        {
            return null;
        }

        var seconds = milliseconds / 1000d;
        var length = seconds == Math.Truncate(seconds)
            ? ((int)seconds).ToString(CultureInfo.InvariantCulture)
            : seconds.ToString("0.##", CultureInfo.InvariantCulture);
        var line = string.Create(CultureInfo.InvariantCulture, $"Stuns for {length} second(s)");
        if (slot.Max > 0)
        {
            line += string.Create(CultureInfo.InvariantCulture, $" (Max Level {slot.Max})");
        }

        return line + ".";
    }

    /// <summary>
    /// The spell window evaluates a slot at the character's level. Formula 100 keeps the stored base.
    /// A positive base with a lower max shrinks toward that max as level rises. Drowsy is base 90,
    /// max 75, formula 109, so the haste percent is 90 minus level / 4 and does not fall below 75.
    /// Every other slot adds the level term and a max other than 0 caps it.
    /// </summary>
    private static int EffectValue(EffectSlot slot, int level)
    {
        var calc = slot.Calc;
        var basis = slot.Base;
        var max = slot.Max;
        if (calc is 0 or 100)
        {
            return max > 0 && basis > max ? max : basis;
        }

        var change = calc switch
        {
            101 => level / 2,
            102 => level,
            103 => level * 2,
            104 => level * 3,
            105 => level * 4,
            109 => level / 4,
            110 => level / 6,
            111 => level > 16 ? (level - 16) * 6 : 0,
            112 => level > 24 ? (level - 24) * 8 : 0,
            113 => level > 34 ? (level - 34) * 10 : 0,
            114 => level > 44 ? (level - 44) * 15 : 0,
            115 => level > 15 ? (level - 15) * 7 : 0,
            116 => level > 24 ? (level - 24) * 10 : 0,
            117 => level > 34 ? (level - 34) * 13 : 0,
            118 => level > 44 ? (level - 44) * 20 : 0,
            119 => level / 8,
            121 => level / 3,
            124 => level > 50 ? level - 50 : 0,
            125 => level > 50 ? (level - 50) * 2 : 0,
            126 => level > 50 ? (level - 50) * 3 : 0,
            127 => level > 50 ? (level - 50) * 4 : 0,
            128 => level > 50 ? (level - 50) * 5 : 0,
            129 => level > 50 ? (level - 50) * 10 : 0,
            130 => level > 50 ? (level - 50) * 15 : 0,
            131 => level > 50 ? (level - 50) * 20 : 0,
            132 => level > 50 ? (level - 50) * 25 : 0,
            139 => level > 30 ? (level - 30) / 2 : 0,
            140 => level > 30 ? level - 30 : 0,
            141 => level > 30 ? 3 * (level - 30) / 2 : 0,
            143 => 3 * level / 4,
            _ when calc is > 0 and < 100 => level * calc,
            _ when calc is >= 2000 and < 3000 => level * (calc - 2000),
            _ => int.MinValue,
        };
        if (change == int.MinValue)
        {
            return basis;
        }

        // Slows such as Drowsy store haste under 100. The max is the strongest (lowest) haste,
        // so the level term is subtracted and the result does not fall below that max.
        if (basis > 0 && max > 0 && max < basis)
        {
            var shrunk = basis - change;
            return shrunk < max ? max : shrunk;
        }

        var value = Math.Abs(basis) + change;
        if (max != 0 && value > Math.Abs(max))
        {
            value = Math.Abs(max);
        }

        return basis < 0 ? -value : value;
    }

    /// <summary>
    /// Before the armor soft cap, the spell window shows raw armor class divided by 3 for an intelligence
    /// caster and by 4 for every other class. A spell that any other class can use keeps the larger divisor.
    /// </summary>
    private static int ShownAmount(SpellInfo spell, int spa, int value)
    {
        if (spa != 1 || value == 0)
        {
            return value;
        }

        return value / (IntelligenceOnly(spell) ? 3 : 4);
    }

    private static bool IntelligenceOnly(SpellInfo spell)
    {
        var usable = spell.UsableBy();
        if (usable.Count == 0)
        {
            return false;
        }

        foreach (var (classIndex, _) in usable)
        {
            if (classIndex is not (10 or 11 or 12 or 13))
            {
                return false;
            }
        }

        return true;
    }

    private static string? AttributeSentence(int spa, int amount)
    {
        if (!EffectNames.TryGetValue(spa, out var effect) || amount == 0)
        {
            return null;
        }

        var verb = amount < 0 ? "Decreases" : "Increases";
        var suffix = effect.Percent ? "%" : string.Empty;
        return string.Create(CultureInfo.InvariantCulture, $"{verb} {effect.Name} by {Math.Abs(amount)}{suffix}");
    }

    private static string SpokenLines(SpellSpoken spoken)
    {
        var lines = new List<string>();
        if (spoken.OnYou.Length > 0)
        {
            lines.Add("On You: " + spoken.OnYou);
        }

        var other = OtherListener(spoken.OnOther);
        if (other.Length > 0)
        {
            lines.Add("On Other: " + other);
        }

        if (spoken.WearsOff.Length > 0)
        {
            lines.Add("Wears Off: " + spoken.WearsOff);
        }

        return string.Join("\n", lines);
    }

    private static string OtherListener(string text)
    {
        if (text.Length == 0)
        {
            return string.Empty;
        }

        if (text[0] is ' ' or '\'')
        {
            return "Soandso" + text;
        }

        return char.IsLower(text[0]) ? "Soandso " + text : text;
    }

    public static string DurationText(SpellInfo spell, int casterLevel = 0)
    {
        // The spell window prints the duration at the character's level. A loadout does not store
        // that level, so an unknown level uses the cap. The scribe level (38 for a shaman slow)
        // prints 02:06 where a level 50 window shows 02:42 and a level 40 window shows 02:12.
        var level = casterLevel > 0 ? casterLevel : SpellCatalog.MaxSearchLevel;
        return TimeFromTicks(Ticks(level, spell.DurationFormula, spell.Duration));
    }

    /// <summary>Buff length in 6-second ticks. Negative means the buff does not expire.</summary>
    public static int Ticks(int level, int formula, int duration)
    {
        int temp;
        switch (formula)
        {
            case 1:
                temp = level > 3 ? level / 2 : 1;
                break;
            case 2:
                temp = level > 3 ? level / 2 + 5 : 6;
                break;
            case 3:
                temp = 30 * level;
                break;
            case 4:
                temp = 50;
                break;
            case 5:
                temp = 2;
                break;
            case 6:
                temp = level / 2 + 2;
                break;
            case 7:
                temp = level;
                break;
            case 8:
                temp = level + 10;
                break;
            case 9:
                temp = 2 * level + 10;
                break;
            case 10:
                temp = 3 * level + 10;
                break;
            case 11:
                temp = 30 * (level + 3);
                break;
            case 12:
                temp = level > 7 ? level / 4 : 1;
                break;
            case 13:
                temp = 4 * level + 10;
                break;
            case 14:
                temp = 5 * (level + 2);
                break;
            case 15:
                temp = 10 * (level + 10);
                break;
            case 50:
            case 51:
                return -1;
            default:
                return formula < 200 ? 0 : formula;
        }

        if (duration > 0 && duration < temp)
        {
            temp = duration;
        }

        return temp;
    }

    private static void AddRate(List<SpellDetailLine> lines, SpellInfo spell, SpellScale.Kind kind, int level, int power)
    {
        var label = kind switch
        {
            SpellScale.Kind.Nuke or SpellScale.Kind.Dot => "DPS",
            SpellScale.Kind.Heal or SpellScale.Kind.Hot => "HPS",
            _ => string.Empty,
        };
        if (label.Length == 0)
        {
            return;
        }

        var rate = SpellUpgrades.PerSecond(spell, level, 0);
        if (rate <= 0)
        {
            return;
        }

        var scaled = power > 0 ? SpellUpgrades.PerSecond(spell, level, power) : rate;
        var tone = scaled > rate ? SpellValueTone.Benefit : scaled < rate ? SpellValueTone.Worse : SpellValueTone.None;
        Add(lines, label, FormatRate(rate), scaled == rate ? string.Empty : FormatRate(scaled), tone);
    }

    private static string FormatRate(double rate)
        => rate.ToString("0.##", CultureInfo.InvariantCulture);

    private static void Add(List<SpellDetailLine> lines, string label, string value)
    {
        if (value.Length > 0)
        {
            lines.Add(new SpellDetailLine(label, value));
        }
    }

    private static void Add(List<SpellDetailLine> lines, string label, string value, string adjusted, SpellValueTone tone)
    {
        if (value.Length == 0)
        {
            return;
        }

        lines.Add(adjusted.Length == 0 || adjusted == value
            ? new SpellDetailLine(label, value)
            : new SpellDetailLine(label, value, adjusted, tone));
    }

    private static SpellDetailLine Figure(string label, string value, string adjusted, SpellValueTone tone)
        => adjusted.Length == 0 || adjusted == value
            ? new SpellDetailLine(label, value)
            : new SpellDetailLine(label, value, adjusted, tone);

    private static bool Same(string left, string right)
        => string.Equals(left, right, StringComparison.Ordinal);

    /// <summary>Cast time keeps hundredths. A whole second or a single tenth drops the trailing zeros.</summary>
    private static string FormatPowerTime(int milliseconds)
    {
        if (milliseconds <= 0)
        {
            return "Instant";
        }

        if (milliseconds % 1000 == 0)
        {
            return FormatSeconds(milliseconds / 1000);
        }

        var text = (milliseconds / 1000d).ToString("0.00", CultureInfo.InvariantCulture).TrimEnd('0').TrimEnd('.');
        return text + " seconds";
    }

    private static string RecoveryAdjusted(int milliseconds, int power)
    {
        if (milliseconds <= 0 || power <= 0)
        {
            return string.Empty;
        }

        var adjusted = FormatPowerTime(SpellScale.RecoveryMilliseconds(milliseconds, power));
        return Same(adjusted, FormatMilliseconds(milliseconds)) ? string.Empty : adjusted;
    }

    private static string ReuseAdjusted(int milliseconds, int power)
    {
        if (milliseconds <= 0 || power <= 0)
        {
            return string.Empty;
        }

        var adjusted = FormatPowerTime(SpellScale.ReuseMilliseconds(milliseconds, power));
        return Same(adjusted, FormatMilliseconds(milliseconds)) ? string.Empty : adjusted;
    }

    private static string ResistText(int resist, int adjust)
    {
        var name = ResistName(resist);
        if (name.Length == 0)
        {
            return string.Empty;
        }

        return adjust == 0 ? name : string.Create(CultureInfo.InvariantCulture, $"{name} {adjust}");
    }

    private static string ResistAdjusted(int resist, int adjust, int power, SpellScale.Kind kind)
    {
        var name = ResistName(resist);
        if (name.Length == 0 || power <= 0)
        {
            return string.Empty;
        }

        var scaled = SpellScale.Resist(adjust, power, kind);
        if (scaled == adjust)
        {
            return string.Empty;
        }

        return scaled == 0 ? name : string.Create(CultureInfo.InvariantCulture, $"{name} {scaled}");
    }

    private static string Classes(SpellInfo spell)
    {
        var usable = spell.UsableBy();
        return usable.Count == 0
            ? string.Empty
            : string.Join(", ", usable.Select(entry => string.Create(CultureInfo.InvariantCulture, $"{CharacterClasses.Codes[entry.Class]}({entry.Level})")));
    }

    private static string TimeFromTicks(int ticks)
    {
        if (ticks < 0)
        {
            return "Permanent";
        }

        return ticks == 0 ? string.Empty : FormatClock(ticks * 6);
    }

    /// <summary>Exact length. Under an hour is MM:SS. An hour or longer is HH:MM:SS.</summary>
    private static string FormatClock(int seconds)
    {
        var hours = seconds / 3600;
        var minutes = seconds % 3600 / 60;
        var remain = seconds % 60;
        return hours > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{hours:00}:{minutes:00}:{remain:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{minutes:00}:{remain:00}");
    }

    /// <summary>Whole seconds have no decimal. A fractional second keeps one place and drops a trailing .0.</summary>
    private static string FormatIntervalSeconds(int milliseconds)
        => (milliseconds / 1000d).ToString("0.#", CultureInfo.InvariantCulture);

    private static string FormatMilliseconds(int milliseconds)
    {
        if (milliseconds <= 0)
        {
            return "Instant";
        }

        if (milliseconds % 1000 == 0)
        {
            return FormatSeconds(milliseconds / 1000);
        }

        var seconds = milliseconds / 1000d;
        return seconds.ToString("0.#", CultureInfo.InvariantCulture) + " seconds";
    }

    private static string FormatSeconds(int seconds)
    {
        if (seconds <= 0)
        {
            return "Instant";
        }

        if (seconds % 3600 == 0)
        {
            var hours = seconds / 3600;
            return hours == 1 ? "1 hour" : string.Create(CultureInfo.InvariantCulture, $"{hours} hours");
        }

        if (seconds % 60 == 0)
        {
            var minutes = seconds / 60;
            return minutes == 1 ? "1 minute" : string.Create(CultureInfo.InvariantCulture, $"{minutes} minutes");
        }

        if (seconds > 60)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{seconds / 60} minutes {seconds % 60} seconds");
        }

        return seconds == 1 ? "1 second" : string.Create(CultureInfo.InvariantCulture, $"{seconds} seconds");
    }

    private static string Number(double value)
        => value == Math.Truncate(value)
            ? ((int)value).ToString(CultureInfo.InvariantCulture)
            : value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string ResistName(int resist) => resist switch
    {
        1 => "Magic",
        2 => "Fire",
        3 => "Cold",
        4 => "Poison",
        5 => "Disease",
        6 => "Chromatic",
        7 => "Prismatic",
        8 => "Physical",
        9 => "Corruption",
        _ => string.Empty,
    };

    /// <summary>Two spells share an intended target when the spell window would print the same target line.</summary>
    public static bool SameTarget(SpellCasting left, SpellCasting right)
    {
        if (left.Target == right.Target)
        {
            return true;
        }

        var first = TargetName(left.Target);
        var second = TargetName(right.Target);
        return first.Length > 0 && first == second;
    }

    private static string TargetName(int target) => target switch
    {
        1 or 5 or 44 or 46 or 51 or 52 => "Single",
        56 => "Target Group Member or Self",
        3 or 41 => "Group",
        4 => "Point blank area",
        6 => "Self",
        8 => "Targeted area",
        9 => "Animal",
        10 or 21 => "Undead",
        11 => "Summoned",
        13 => "Life tap",
        14 => "Pet",
        15 => "Corpse",
        16 => "Plant",
        17 => "Giant",
        18 => "Dragon",
        32 or 33 or 36 or 40 => "Area",
        34 => "Chest",
        38 => "Pet owner",
        _ => string.Empty,
    };

    private static string SkillName(int skill)
        => (uint)skill < (uint)SkillNames.Length ? SkillNames[skill] : string.Empty;

    private static List<EffectSlot> Slots(string effects)
    {
        var slots = new List<EffectSlot>();
        if (effects.Length == 0)
        {
            return slots;
        }

        foreach (var part in effects.Split('$'))
        {
            var bits = part.Split('|');
            if (bits.Length < 6
                || !int.TryParse(bits[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var slot)
                || !int.TryParse(bits[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var basis)
                || !int.TryParse(bits[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out var max))
            {
                continue;
            }

            var spa = int.TryParse(bits[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var effect) ? effect : 0;
            var calc = int.TryParse(bits[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var formula) ? formula : 100;
            slots.Add(new EffectSlot(slot, spa, basis, max, calc));
        }

        return slots;
    }

    private static readonly Dictionary<int, (string Name, bool Percent)> EffectNames = new()
    {
        [1] = ("Armor Class", false),
        [2] = ("Attack", false),
        [3] = ("Movement Speed", true),
        [4] = ("Strength", false),
        [5] = ("Dexterity", false),
        [6] = ("Agility", false),
        [7] = ("Stamina", false),
        [8] = ("Intelligence", false),
        [9] = ("Wisdom", false),
        [10] = ("Charisma", false),
        [11] = ("Attack Speed", true),
        [15] = ("Mana", false),
        [35] = ("Disease Counters", false),
        [36] = ("Poison Counters", false),
        [46] = ("Fire Resist", false),
        [47] = ("Cold Resist", false),
        [48] = ("Poison Resist", false),
        [49] = ("Disease Resist", false),
        [50] = ("Magic Resist", false),
    };

    private static readonly string[] SkillNames =
    [
        "1H Blunt", "1H Slashing", "2H Blunt", "2H Slashing", "Abjuration", "Alteration", "Apply Poison", "Archery",
        "Backstab", "Bind Wound", "Bash", "Block", "Brass Instruments", "Channeling", "Conjuration", "Defense",
        "Disarm", "Disarm Traps", "Divination", "Dodge", "Double Attack", "Dragon Punch", "Dual Wield", "Eagle Strike",
        "Evocation", "Feign Death", "Flying Kick", "Forage", "Hand to Hand", "Hide", "Kick", "Meditate", "Mend",
        "Offense", "Parry", "Pick Lock", "1H Piercing", "Riposte", "Round Kick", "Safe Fall", "Sense Heading", "Singing",
        "Sneak", "Specialize Abjuration", "Specialize Alteration", "Specialize Conjuration", "Specialize Divination",
        "Specialize Evocation", "Pick Pockets", "Stringed Instruments", "Swimming", "Throwing", "Tiger Claw", "Tracking",
        "Wind Instruments", "Fishing", "Make Poison", "Tinkering", "Research", "Alchemy", "Baking", "Tailoring",
        "Sense Traps", "Blacksmithing", "Fletching", "Brewing", "Alcohol Tolerance", "Begging", "Jewelry Making",
        "Pottery", "Percussion Instruments",
    ];

    [GeneratedRegex(@"([#@])(\d+)")]
    private static partial Regex SlotToken();

    private const int WeaponProc = 85;

    private sealed record EffectSlot(int Slot, int Spa, int Base, int Max, int Calc);
}
