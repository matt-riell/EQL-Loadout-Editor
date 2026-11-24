namespace EqlLoadouts.Core.Content;

/// <summary>
/// Mote-rank adjustments read from the in-game upgrade window.
/// Cast, mana, recovery, reuse, duration, resist, and direct damage follow the rates on
/// https://eqltools.com/learn/spell-upgrades. Instant healing follows the window as well:
/// Greater Healing's 350 at rank 6 reads 413, which is 3% of the base per rank.
/// A parenthetical in the spell window also includes specialization and Spell Casting Mastery.
/// Those are not in the log, so a rank here is the mote rank alone.
/// </summary>
public static class SpellScale
{
    public enum Kind
    {
        Nuke,
        Heal,
        Dot,
        Hot,
        Buff,
        Debuff,
        Utility,
    }

    public static Kind KindOf(SpellInfo spell, int level)
    {
        if (spell.IsPermanent)
        {
            return Kind.Utility;
        }

        var ticks = SpellPresentation.Ticks(level, spell.DurationFormula, spell.Duration);
        var damage = false;
        var heal = false;
        var overTime = false;
        foreach (var effect in SpellPresentation.EffectsAt(spell, level))
        {
            if (effect.Spa == 0 && effect.Amount < 0)
            {
                damage = true;
                overTime = ticks > 0;
            }
            else if (effect.Spa == 79 && effect.Amount < 0)
            {
                damage = true;
            }
            else if (effect.Spa == 100)
            {
                overTime = true;
                if (effect.Amount < 0)
                {
                    damage = true;
                }
                else if (effect.Amount > 0)
                {
                    heal = true;
                }
            }
            else if (effect.Spa is 0 or 79 && effect.Amount > 0)
            {
                heal = true;
                if (effect.Spa == 0 && ticks > 0)
                {
                    overTime = true;
                }
            }
        }

        if (spell.GoodEffect == 0 && damage && overTime)
        {
            return Kind.Dot;
        }

        if (spell.GoodEffect == 0 && damage)
        {
            return Kind.Nuke;
        }

        if (spell.GoodEffect != 0 && heal && overTime)
        {
            return Kind.Hot;
        }

        if (spell.GoodEffect != 0 && heal && ticks <= 0)
        {
            return Kind.Heal;
        }

        if (spell.GoodEffect != 0 && ticks > 0)
        {
            return Kind.Buff;
        }

        if (spell.GoodEffect == 0 && ticks > 0)
        {
            return Kind.Debuff;
        }

        return Kind.Utility;
    }

    /// <summary>Direct damage grows 6% of its base per rank. Healing, and damage or healing per tick, grows 3%.</summary>
    public static int AmountPerRank(Kind kind, bool overTime)
        => kind switch
        {
            Kind.Nuke => 6,
            Kind.Dot when !overTime => 6,
            Kind.Dot => 3,
            Kind.Heal or Kind.Hot => 3,
            _ => 0,
        };

    /// <summary>Truncates toward zero. 418 at rank 6 is 568, and 350 at rank 6 is 413.</summary>
    public static int Grow(int magnitude, int rank, int percentPerRank)
    {
        if (magnitude == 0 || rank <= 0 || percentPerRank == 0)
        {
            return magnitude;
        }

        var percent = 100 + percentPerRank * Math.Clamp(rank, 0, 10);
        return (int)((long)magnitude * percent / 100);
    }

    public static int Mana(int mana, int rank, Kind kind)
    {
        if (mana == 0 || rank <= 0)
        {
            return mana;
        }

        var cut = kind is Kind.Debuff ? 4 : 2;
        return Round(mana * (100 - cut * Math.Clamp(rank, 0, 10)) / 100d);
    }

    public static int CastMilliseconds(int milliseconds, int rank, Kind kind)
    {
        if (milliseconds <= 0 || rank <= 0)
        {
            return milliseconds;
        }

        var cut = kind == Kind.Nuke ? 2 : 4;
        return (int)(milliseconds * (100 - cut * Math.Clamp(rank, 0, 10)) / 100d);
    }

    /// <summary>Nearest tenth of a second, with an exact half rounded down. Rank 0 keeps the file value.</summary>
    public static int RecoveryMilliseconds(int milliseconds, int rank)
    {
        if (milliseconds <= 0 || rank <= 0)
        {
            return milliseconds;
        }

        var adjusted = milliseconds * (100 - 2 * Math.Clamp(rank, 0, 10)) / 100d;
        var tenths = Math.Round(adjusted / 100d, MidpointRounding.ToZero);
        return (int)(tenths * 100);
    }

    /// <summary>Whole seconds, truncated, and at least one second when the spell has a reuse time.</summary>
    public static int ReuseMilliseconds(int milliseconds, int rank)
    {
        if (milliseconds <= 0)
        {
            return 0;
        }

        var adjusted = rank <= 0 ? milliseconds : milliseconds * (100 - 2 * Math.Clamp(rank, 0, 10)) / 100d;
        var seconds = (int)(adjusted / 1000d);
        if (seconds < 1)
        {
            seconds = 1;
        }

        return seconds * 1000;
    }

    public static int DurationTicks(int ticks, int rank, Kind kind)
    {
        if (ticks <= 0 || rank <= 0)
        {
            return ticks;
        }

        var gain = kind is Kind.Buff or Kind.Debuff ? 10 : kind is Kind.Dot or Kind.Hot ? 5 : 0;
        if (gain == 0)
        {
            return ticks;
        }

        return Round(ticks * (100 + gain * Math.Clamp(rank, 0, 10)) / 100d);
    }

    public static int Resist(int adjust, int rank, Kind kind)
    {
        if (rank <= 0 || kind is not (Kind.Nuke or Kind.Dot or Kind.Debuff))
        {
            return adjust;
        }

        return adjust - 15 * Math.Clamp(rank, 0, 10);
    }

    private static int Round(double value)
        => (int)Math.Round(value, MidpointRounding.AwayFromZero);
}
