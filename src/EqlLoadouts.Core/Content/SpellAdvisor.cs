using System.Globalization;

namespace EqlLoadouts.Core.Content;

public enum SpellSetKind
{
    Buffs,
    Perms,
    Attack,
}

public enum AdvisorRole
{
    Buff,
    Perm,
    Attack,
    Heal,
}

/// <summary>
/// Points for one effect. Movement and attack are 1 point per point, so 15 attack beats 5% movement.
/// The other weights live here so a later change has to face that case.
/// </summary>
public static class SpellWeights
{
    public const double Movement = 1;
    public const double Attack = 1;
    public const double ArmorClass = 1;
    public const double HitPoints = 1;
    public const double Attribute = 1;
    public const double Resist = 1;
    public const double Haste = 2;
    public const double Mana = 1;
    public const double Damage = 1;
}

public sealed record ScoredPart(int Slot, int Spa, string Label, int Amount, double Points);

public sealed record SpellScore(SpellInfo Spell, double Points, AdvisorRole? Role, IReadOnlyList<ScoredPart> Effects);

public sealed record SpellSetDraft(SpellSetKind Kind, IReadOnlyList<SpellScore> Spells);

public sealed record SpellSwap(int GemNumber, SpellInfo Current, SpellInfo Suggested, string Reason);

/// <summary>
/// Picks spell sets by overall impact. Spells that share an effect in the same slot conflict,
/// and the higher total wins that conflict. Mote rank is not part of the score.
/// </summary>
public static class SpellAdvisor
{
    public const int GemCount = 14;

    public static SpellScore Score(SpellInfo spell, int level)
    {
        var beneficial = spell.GoodEffect != 0;
        var ticks = SpellPresentation.Ticks(level, spell.DurationFormula, spell.Duration);
        var effects = new List<ScoredPart>();
        foreach (var effect in SpellPresentation.EffectsAt(spell, level))
        {
            if (!TryScore(effect, beneficial, ticks, out var part))
            {
                effects.Add(new ScoredPart(effect.Slot, effect.Spa, "effect", Math.Abs(effect.Amount), 0));
                continue;
            }

            effects.Add(part);
        }

        double points = 0;
        foreach (var part in effects)
        {
            points += part.Points;
        }

        return new SpellScore(spell, points, RoleOf(spell, level, effects), effects);
    }

    public static bool Conflicts(SpellInfo left, SpellInfo right, int level)
        => Conflicts(Score(left, level), Score(right, level));

    public static bool Conflicts(SpellScore left, SpellScore right)
    {
        foreach (var effect in left.Effects)
        {
            foreach (var other in right.Effects)
            {
                if (effect.Slot == other.Slot && effect.Spa == other.Spa)
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static IReadOnlyList<SpellSetDraft> Build(IEnumerable<SpellInfo> spells, IReadOnlyList<int> classes, int level)
    {
        var pool = new List<SpellScore>();
        foreach (var spell in spells)
        {
            if (!Castable(spell, classes, level))
            {
                continue;
            }

            var score = Score(spell, level);
            if (score.Role is null || score.Points <= 0)
            {
                continue;
            }

            pool.Add(score);
        }

        return Draft(pool);
    }

    public static IReadOnlyList<SpellSetDraft> Draft(IReadOnlyList<SpellScore> pool)
        =>
        [
            new SpellSetDraft(SpellSetKind.Buffs, Select(pool, AdvisorRole.Buff)),
            new SpellSetDraft(SpellSetKind.Perms, Select(pool, AdvisorRole.Perm)),
            new SpellSetDraft(SpellSetKind.Attack, Select(pool, AdvisorRole.Attack)),
        ];

    public static IReadOnlyList<SpellSwap> Suggest(IReadOnlyList<SpellInfo?> gems, IReadOnlyList<SpellScore> pool, int level)
    {
        var memorized = new HashSet<int>();
        foreach (var gem in gems)
        {
            if (gem is not null)
            {
                memorized.Add(gem.Id);
            }
        }

        var swaps = new List<SpellSwap>();
        for (var index = 0; index < gems.Count; index++)
        {
            var current = gems[index];
            if (current is null)
            {
                continue;
            }

            var currentScore = Score(current, level);
            if (currentScore.Role is null)
            {
                continue;
            }

            SpellScore? best = null;
            foreach (var candidate in pool)
            {
                if (candidate.Spell.Id == current.Id || memorized.Contains(candidate.Spell.Id))
                {
                    continue;
                }

                if (candidate.Role != currentScore.Role || candidate.Points <= currentScore.Points)
                {
                    continue;
                }

                if (currentScore.Role != AdvisorRole.Heal && !Conflicts(currentScore, candidate))
                {
                    continue;
                }

                if (best is null || candidate.Points > best.Points
                    || (candidate.Points == best.Points && candidate.Spell.Id < best.Spell.Id))
                {
                    best = candidate;
                }
            }

            if (best is not null)
            {
                swaps.Add(new SpellSwap(index + 1, current, best.Spell, Tradeoff(best, currentScore)));
            }
        }

        return swaps;
    }

    public static string Tradeoff(SpellScore winner, SpellScore loser)
    {
        var gains = Advantages(winner, loser);
        var losses = Advantages(loser, winner);
        if (gains.Count == 0)
        {
            return "Higher overall";
        }

        if (losses.Count == 0)
        {
            return Join(gains, plus: true);
        }

        var verb = gains.Count > 1 ? "are" : "is";
        return string.Create(CultureInfo.InvariantCulture, $"{Join(gains, plus: true)} {verb} worth more than {Join(losses, plus: false)}");
    }

    /// <summary>Buffs, or Buffs (1) when that name is already used. Matching ignores case.</summary>
    public static string UniqueName(string preferred, IEnumerable<string> used)
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in used)
        {
            var trimmed = name.Trim();
            if (trimmed.Length > 0)
            {
                taken.Add(trimmed);
            }
        }

        if (!taken.Contains(preferred))
        {
            return preferred;
        }

        for (var number = 1; number < 1000; number++)
        {
            var candidate = string.Create(CultureInfo.InvariantCulture, $"{preferred} ({number})");
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }

        return preferred;
    }

    private static List<SpellScore> Select(IReadOnlyList<SpellScore> pool, AdvisorRole role)
    {
        var ranked = new List<SpellScore>();
        foreach (var score in pool)
        {
            if (score.Role == role && score.Points > 0)
            {
                ranked.Add(score);
            }
        }

        ranked.Sort(static (left, right) =>
        {
            var byPoints = right.Points.CompareTo(left.Points);
            if (byPoints != 0)
            {
                return byPoints;
            }

            var byEffects = Counted(right).CompareTo(Counted(left));
            return byEffects != 0 ? byEffects : left.Spell.Id.CompareTo(right.Spell.Id);
        });

        var chosen = new List<SpellScore>();
        foreach (var score in ranked)
        {
            if (chosen.Count == GemCount)
            {
                break;
            }

            var blocked = false;
            foreach (var kept in chosen)
            {
                if (Conflicts(kept, score))
                {
                    blocked = true;
                    break;
                }
            }

            if (!blocked)
            {
                chosen.Add(score);
            }
        }

        return chosen;
    }

    private static int Counted(SpellScore score)
    {
        var count = 0;
        foreach (var effect in score.Effects)
        {
            if (effect.Points > 0)
            {
                count++;
            }
        }

        return count;
    }

    private static bool Castable(SpellInfo spell, IReadOnlyList<int> classes, int level)
    {
        foreach (var index in classes)
        {
            if (spell.LevelFor(index) is { } required && required <= level)
            {
                return true;
            }
        }

        return false;
    }

    private static AdvisorRole? RoleOf(SpellInfo spell, int level, IReadOnlyList<ScoredPart> effects)
    {
        if (spell.GoodEffect == 0)
        {
            return AdvisorRole.Attack;
        }

        if (spell.IsPermanent)
        {
            return AdvisorRole.Perm;
        }

        if (SpellPresentation.Ticks(level, spell.DurationFormula, spell.Duration) > 0)
        {
            return AdvisorRole.Buff;
        }

        foreach (var effect in effects)
        {
            if (effect.Spa is 0 or 79 && effect.Points > 0)
            {
                return AdvisorRole.Heal;
            }
        }

        return null;
    }

    private static bool TryScore(SpellPresentation.SpellEffectAmount effect, bool beneficial, int ticks, out ScoredPart part)
    {
        part = default!;
        var amount = effect.Amount;
        if (!Helps(beneficial, effect.Spa, amount))
        {
            return false;
        }

        var magnitude = Math.Abs(amount);
        double points;
        string label;
        var shown = magnitude;
        switch (effect.Spa)
        {
            case 0:
                if (!beneficial)
                {
                    shown = magnitude * Math.Max(ticks, 1);
                    points = shown * SpellWeights.Damage;
                    label = "damage";
                    break;
                }

                points = magnitude * SpellWeights.HitPoints;
                label = ticks > 1 ? "hit points per tick" : "hit points";
                break;
            case 69 or 79:
                if (effect.Spa == 79 && !beneficial)
                {
                    points = magnitude * SpellWeights.Damage;
                    label = "damage";
                    break;
                }

                points = magnitude * SpellWeights.HitPoints;
                label = "hit points";
                break;
            case 1:
                points = magnitude * SpellWeights.ArmorClass;
                label = "armor class";
                break;
            case 2:
                points = magnitude * SpellWeights.Attack;
                label = "attack";
                break;
            case 3:
                points = magnitude * SpellWeights.Movement;
                label = "movement";
                break;
            case 11:
                shown = Math.Abs(amount - 100);
                points = shown * SpellWeights.Haste;
                label = "haste";
                break;
            case 15:
                points = magnitude * SpellWeights.Mana;
                label = "mana";
                break;
            case >= 4 and <= 10:
                points = magnitude * SpellWeights.Attribute;
                label = AttributeLabel(effect.Spa);
                break;
            case >= 46 and <= 50:
                points = magnitude * SpellWeights.Resist;
                label = ResistLabel(effect.Spa);
                break;
            default:
                return false;
        }

        if (points <= 0)
        {
            return false;
        }

        part = new ScoredPart(effect.Slot, effect.Spa, label, shown, points);
        return true;
    }

    private static bool Helps(bool beneficial, int spa, int amount)
    {
        if (spa == 11)
        {
            return beneficial ? amount > 100 : amount is > 0 and < 100;
        }

        return beneficial ? amount > 0 : amount < 0;
    }

    private static List<ScoredPart> Advantages(SpellScore winner, SpellScore loser)
    {
        var list = new List<ScoredPart>();
        foreach (var part in winner.Effects)
        {
            if (part.Points <= 0)
            {
                continue;
            }

            ScoredPart? other = null;
            foreach (var candidate in loser.Effects)
            {
                if (candidate.Slot == part.Slot && candidate.Spa == part.Spa && candidate.Points > 0)
                {
                    other = candidate;
                    break;
                }
            }

            if (other is not { } shared)
            {
                list.Add(part);
                continue;
            }

            var points = part.Points - shared.Points;
            if (points <= 0)
            {
                continue;
            }

            list.Add(part with { Points = points, Amount = part.Amount - shared.Amount });
        }

        list.Sort(static (left, right) => right.Points.CompareTo(left.Points));
        return list;
    }

    private static string Join(IReadOnlyList<ScoredPart> parts, bool plus)
    {
        if (parts.Count == 1)
        {
            return (plus ? "+" : string.Empty) + Format(parts[0]);
        }

        var bits = new string[parts.Count];
        for (var index = 0; index < parts.Count; index++)
        {
            bits[index] = (plus ? "+" : string.Empty) + Format(parts[index]);
        }

        if (parts.Count == 2)
        {
            return bits[0] + " and " + bits[1];
        }

        return string.Join(", ", bits[..^1]) + ", and " + bits[^1];
    }

    private static string Format(ScoredPart part)
    {
        var amount = Math.Abs(part.Amount);
        if (part.Spa is 3 or 11)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{amount}% {part.Label}");
        }

        return string.Create(CultureInfo.InvariantCulture, $"{amount} {part.Label}");
    }

    private static string AttributeLabel(int spa) => spa switch
    {
        4 => "strength",
        5 => "dexterity",
        6 => "agility",
        7 => "stamina",
        8 => "intelligence",
        9 => "wisdom",
        _ => "charisma",
    };

    private static string ResistLabel(int spa) => spa switch
    {
        46 => "fire resist",
        47 => "cold resist",
        48 => "poison resist",
        49 => "disease resist",
        _ => "magic resist",
    };
}
