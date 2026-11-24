namespace EqlLoadouts.Core.Content;

/// <summary>
/// A gem upgrade is a later spell in the same line, such as Drain Soul after Drain Spirit,
/// when that spell also deals more damage per second. Otherwise a similar spell is compared.
/// Direct damage, heals, and damage over time use the power-adjusted amount divided by how
/// long the spell takes: its cast, then the longer of recovery and reuse. A spell that ticks
/// also occupies its duration when that outlasts the cast cycle. The category, the resist,
/// and the intended target all have to match, so a regen is not replaced by a heal.
/// </summary>
public static class SpellUpgrades
{
    public readonly record struct GemUpgrade(int GemNumber, SpellInfo Spell);

    public static IReadOnlyList<GemUpgrade> Find(
        IReadOnlyList<SpellInfo?> gems,
        IReadOnlyList<SpellInfo> castable,
        IReadOnlyList<int> classes,
        int level,
        Func<SpellInfo, int> power)
    {
        var memorized = new HashSet<int>();
        foreach (var gem in gems)
        {
            if (gem is not null)
            {
                memorized.Add(gem.Id);
            }
        }

        var found = new List<GemUpgrade>();
        for (var index = 0; index < gems.Count; index++)
        {
            var current = gems[index];
            if (current is null || ScribeLevel(current, classes, level) is not { } currentLevel)
            {
                continue;
            }

            var upgrade = LineUpgrade(current, currentLevel, castable, memorized, classes, level, power)
                ?? Similar(current, castable, memorized, classes, level, power);
            if (upgrade is not null)
            {
                found.Add(new GemUpgrade(index + 1, upgrade));
            }
        }

        return found;
    }

    private static SpellInfo? LineUpgrade(
        SpellInfo current,
        int currentLevel,
        IReadOnlyList<SpellInfo> castable,
        HashSet<int> memorized,
        IReadOnlyList<int> classes,
        int level,
        Func<SpellInfo, int> power)
    {
        if (current.SpellLineId <= 0)
        {
            return null;
        }

        var ranked = CombatKind(current, level);
        var bestRate = ranked ? PerSecond(current, level, power(current)) : 0;
        SpellInfo? best = null;
        var bestLevel = currentLevel;
        foreach (var candidate in castable)
        {
            if (candidate.Id == current.Id
                || candidate.SpellLineId != current.SpellLineId
                || memorized.Contains(candidate.Id)
                || candidate.Category != current.Category
                || !SpellPresentation.SameTarget(current.Casting, candidate.Casting)
                || AddsHarm(current, candidate, level)
                || ScribeLevel(candidate, classes, level) is not { } scribe
                || scribe <= currentLevel)
            {
                continue;
            }

            if (ranked)
            {
                var rate = PerSecond(candidate, level, power(candidate));
                if (rate <= bestRate)
                {
                    continue;
                }

                best = candidate;
                bestRate = rate;
                continue;
            }

            if (scribe <= bestLevel)
            {
                continue;
            }

            best = candidate;
            bestLevel = scribe;
        }

        return best;
    }

    private static SpellInfo? Similar(
        SpellInfo current,
        IReadOnlyList<SpellInfo> castable,
        HashSet<int> memorized,
        IReadOnlyList<int> classes,
        int level,
        Func<SpellInfo, int> power)
    {
        var kind = SpellScale.KindOf(current, level);
        if (kind is not (SpellScale.Kind.Nuke or SpellScale.Kind.Heal or SpellScale.Kind.Dot or SpellScale.Kind.Hot))
        {
            return null;
        }

        var currentRate = PerSecond(current, level, power(current));
        if (currentRate <= 0)
        {
            return null;
        }

        SpellInfo? best = null;
        var bestRate = currentRate;
        foreach (var candidate in castable)
        {
            if (candidate.Id == current.Id
                || memorized.Contains(candidate.Id)
                || candidate.SpellLineId == current.SpellLineId && current.SpellLineId > 0
                || candidate.Category != current.Category
                || SpellScale.KindOf(candidate, level) != kind
                || candidate.Casting.Resist != current.Casting.Resist
                || !SpellPresentation.SameTarget(current.Casting, candidate.Casting)
                || AddsHarm(current, candidate, level)
                || ScribeLevel(candidate, classes, level) is null)
            {
                continue;
            }

            var rate = PerSecond(candidate, level, power(candidate));
            if (rate <= bestRate)
            {
                continue;
            }

            best = candidate;
            bestRate = rate;
        }

        return best;
    }

    private static int ScaledAmount(SpellInfo spell, SpellScale.Kind kind, int level, int power)
    {
        var overTime = kind is SpellScale.Kind.Dot or SpellScale.Kind.Hot;
        var ticks = Math.Max(SpellScale.DurationTicks(SpellPresentation.Ticks(level, spell.DurationFormula, spell.Duration), power, kind), 1);
        var total = 0;
        foreach (var effect in SpellPresentation.EffectsAt(spell, level))
        {
            var overTimeHit = effect.Spa is 0 or 100 && overTime;
            var hit = effect.Spa is 0 or 100 || effect.Spa == 79 && !overTime;
            if (!hit || effect.Amount == 0)
            {
                continue;
            }

            if (kind is SpellScale.Kind.Heal or SpellScale.Kind.Hot)
            {
                if (effect.Amount < 0)
                {
                    continue;
                }
            }
            else if (effect.Amount > 0)
            {
                continue;
            }

            var grown = SpellScale.Grow(Math.Abs(effect.Amount), power, SpellScale.AmountPerRank(kind, overTimeHit));
            total += overTimeHit ? grown * ticks : grown;
        }

        return total;
    }

    /// <summary>Power-adjusted damage or healing divided by the seconds until that spell can land again. Zero when the spell is not damage or healing.</summary>
    public static double PerSecond(SpellInfo spell, int level, int power)
    {
        var kind = SpellScale.KindOf(spell, level);
        var amount = ScaledAmount(spell, kind, level, power);
        if (amount <= 0)
        {
            return 0;
        }

        var overTime = kind is SpellScale.Kind.Dot or SpellScale.Kind.Hot;
        var castCut = kind == SpellScale.Kind.Nuke ? 2 : 4;
        var cast = Seconds(spell.Casting.CastTime, power, castCut);
        var recovery = Seconds(spell.Casting.RecoveryTime, power, 2);
        var reuse = Seconds(spell.Casting.RecastTime, power, 2);
        var cycle = cast + Math.Max(recovery, reuse);
        var ticks = SpellScale.DurationTicks(SpellPresentation.Ticks(level, spell.DurationFormula, spell.Duration), power, kind);
        var duration = ticks > 0 ? ticks * 6d : 0;
        var seconds = overTime ? Math.Max(duration, cycle) : cycle;
        return seconds <= 0 ? amount : amount / seconds;
    }

    /// <summary>A new slow, snare, or other penalty is a tradeoff the caster has to weigh, so it is not offered as an upgrade.</summary>
    private static bool AddsHarm(SpellInfo current, SpellInfo candidate, int level)
    {
        var present = Harms(current, level);
        foreach (var spa in Harms(candidate, level))
        {
            if (!present.Contains(spa))
            {
                return true;
            }
        }

        return false;
    }

    private static HashSet<int> Harms(SpellInfo spell, int level)
    {
        var harms = new HashSet<int>();
        var beneficial = spell.GoodEffect != 0;
        foreach (var effect in SpellPresentation.EffectsAt(spell, level))
        {
            var harmful = effect.Spa switch
            {
                0 or 79 or 100 => beneficial ? effect.Amount < 0 : effect.Amount > 0,
                11 => effect.Amount is > 0 and < 100,
                21 => effect.Amount > 0,
                _ => effect.Amount < 0,
            };
            if (harmful)
            {
                harms.Add(effect.Spa);
            }
        }

        return harms;
    }

    private static bool CombatKind(SpellInfo spell, int level)
        => SpellScale.KindOf(spell, level) is SpellScale.Kind.Nuke or SpellScale.Kind.Heal or SpellScale.Kind.Dot or SpellScale.Kind.Hot;

    private static double Seconds(int milliseconds, int rank, int cutPerRank)
    {
        if (milliseconds <= 0)
        {
            return 0;
        }

        if (rank <= 0)
        {
            return milliseconds / 1000d;
        }

        var percent = 100 - cutPerRank * Math.Clamp(rank, 0, 10);
        return milliseconds * percent / 100d / 1000d;
    }

    private static int? ScribeLevel(SpellInfo spell, IReadOnlyList<int> classes, int level)
    {
        int? scribe = null;
        foreach (var index in classes)
        {
            if (spell.LevelFor(index) is { } required && required <= level && (scribe is null || required < scribe))
            {
                scribe = required;
            }
        }

        return scribe;
    }
}
