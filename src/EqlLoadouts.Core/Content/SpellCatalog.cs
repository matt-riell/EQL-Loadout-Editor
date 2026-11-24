using System.Globalization;

namespace EqlLoadouts.Core.Content;

public enum SpellSearchKind
{
    Name,
    Level,
    Type,
    Damage,
    Class,
}

public sealed class SpellInfo
{
    public const int Unusable = 255;

    private readonly byte[]? _levels;

    public SpellInfo(
        int id,
        string name,
        byte[]? levels,
        int category = 0,
        int subcategory = 0,
        int durationFormula = 0,
        int duration = 0,
        int icon = 0,
        SpellCasting? casting = null,
        int descriptionId = 0,
        int spellLineId = 0,
        int goodEffect = 0,
        int castLevel = 0,
        int resistAdjust = 0,
        int bookCategory = 0)
    {
        Id = id;
        Name = name;
        Category = category;
        Subcategory = subcategory;
        DurationFormula = durationFormula;
        Duration = duration;
        Icon = icon;
        Casting = casting ?? SpellCasting.Empty;
        DescriptionId = descriptionId;
        SpellLineId = spellLineId;
        GoodEffect = goodEffect;
        CastLevel = castLevel;
        ResistAdjust = resistAdjust;
        BookCategory = bookCategory;
        _levels = levels is { Length: CharacterClasses.Count } ? levels : null;
    }

    public int Id { get; }

    public string Name { get; }

    public int Category { get; }

    public int Subcategory { get; }

    public int DurationFormula { get; }

    public int Duration { get; }

    /// <summary>Index into the gem icon sheets (gemicons01.tga and on). This is the icon the client draws.</summary>
    public int Icon { get; }

    /// <summary>Mana, times, resist, target, and the raw effect slots from the spell file.</summary>
    public SpellCasting Casting { get; }

    /// <summary>dbstr_us.txt type 6 id. Newer spells do not use the spell id for this.</summary>
    public int DescriptionId { get; }

    /// <summary>dbstr_us.txt type 27 id for the spell line, when the row has one.</summary>
    public int SpellLineId { get; }

    /// <summary>0 is detrimental. Any other value is a beneficial effect.</summary>
    public int GoodEffect { get; }

    /// <summary>Level the spell is cast as when no class can scribe it.</summary>
    public int CastLevel { get; }

    /// <summary>Stored resist modifier. The spell window's parenthetical tier value is not in the file.</summary>
    public int ResistAdjust { get; }

    /// <summary>Spell book filing category from spells_us.txt field 55. A value of -1 means the book does not file this spell.</summary>
    public int BookCategory { get; }

    /// <summary>Spell book category, such as Direct Damage or Utility Detrimental. Empty when the spell file has none.</summary>
    public string TypeName => SpellCategories.Name(Category);

    /// <summary>Spell book subcategory, such as Slow or Fire. Empty when the spell file has none.</summary>
    public string SubcategoryName => SpellCategories.Name(Subcategory);

    /// <summary>Resist element, such as Fire or Cold, when the spell is tagged with one.</summary>
    public string DamageName
    {
        get
        {
            var damage = SpellCategories.DamageName(Subcategory);
            return damage.Length > 0 ? damage : SpellCategories.DamageName(Category);
        }
    }

    /// <summary>Duration formula 50 with no duration value is a buff that does not expire.</summary>
    public bool IsPermanent => DurationFormula == 50 && Duration == 0;

    /// <summary>False when the spell line had no class columns, so usability can't be judged.</summary>
    public bool HasClassLevels => _levels is not null;

    /// <summary>The minimum level for a class, or null when that class can't use the spell.</summary>
    public int? LevelFor(int classIndex)
    {
        if (_levels is null || classIndex is < 0 or >= CharacterClasses.Count)
        {
            return null;
        }

        var level = _levels[classIndex];
        return level is 0 or Unusable ? null : level;
    }

    public IReadOnlyList<(int Class, int Level)> UsableBy()
    {
        var usable = new List<(int, int)>();
        for (var i = 0; i < CharacterClasses.Count; i++)
        {
            if (LevelFor(i) is { } level)
            {
                usable.Add((i, level));
            }
        }

        return usable;
    }

    /// <summary>The listed class at or below <paramref name="maxLevel"/> that learns this spell latest.</summary>
    public (int Class, int Level)? BestWithin(IReadOnlyList<int> classes, int maxLevel)
    {
        var bestClass = -1;
        var bestLevel = -1;
        foreach (var classIndex in classes)
        {
            if (LevelFor(classIndex) is not { } level || level > maxLevel || level <= bestLevel)
            {
                continue;
            }

            bestClass = classIndex;
            bestLevel = level;
        }

        return bestClass >= 0 ? (bestClass, bestLevel) : null;
    }
}

/// <summary>Cast, landing, and fade text from one spells_us_str.txt row. On Other keeps the file's leading space.</summary>
public readonly record struct SpellSpoken(string OnYou, string OnOther, string WearsOff)
{
    public static SpellSpoken Empty { get; } = new(string.Empty, string.Empty, string.Empty);
}

public sealed class SpellCatalog
{
    public const string FileName = "spells_us.txt";

    /// <summary>Spell search only lists spells a selected class can use at this level or below.</summary>
    public const int MaxSearchLevel = 50;

    private const int RangeField = 4;
    private const int AreaRangeField = 5;
    private const int CastTimeField = 8;
    private const int RecoveryTimeField = 9;
    private const int RecastTimeField = 10;
    private const int SkillField = 32;
    private const int FirstClassField = 36;
    /// <summary>Category the spell book files this spell under. -1 is not filed, so pickers do not offer it.</summary>
    private const int BookCategoryField = 55;
    private const int DurationFormulaField = 11;
    private const int DurationField = 12;
    /// <summary>Milliseconds for the %i description token. This is not the area radius.</summary>
    private const int AreaDurationField = 13;

    /// <summary>Creature cap for the %T description token. This is not the area radius. Measured at column 143 on the August 2026 client.</summary>
    private const int MaxTargetsField = 143;
    private const int ManaField = 14;
    private const int GoodEffectField = 28;
    private const int ResistField = 29;
    private const int TargetField = 30;
    /// <summary>
    /// Resist check. Column 79 on the current client. Lifespike's Magic -200 and Elbow Strike's Physical -10 live here.
    /// Column 78 is 0, or 1 on some disciplines and poisons, and is not the check the spell window prints.
    /// </summary>
    private const int ResistAdjustField = 79;
    private const int IconField = 76;
    private const int DescriptionIdField = 86;
    private const int CategoryField = 87;
    private const int SubcategoryField = 88;
    private const int CastLevelField = 115;
    private const int SpellLineField = 166;
    /// <summary>
    /// Enough carets to reach a tail that has grown past the August 2026 effects column.
    /// The effects blob is found by its shape, not by this count.
    /// </summary>
    private const int StoredFieldCount = 400;

    private readonly Dictionary<int, SpellInfo> _spells;
    private readonly Dictionary<int, string> _descriptions;
    private readonly Dictionary<int, string> _spellLines;
    private readonly Dictionary<int, SpellSpoken> _spoken;
    private readonly bool[] _classHasSpells;

    private SpellCatalog(
        Dictionary<int, SpellInfo> spells,
        bool[]? classHasSpells = null,
        Dictionary<int, string>? descriptions = null,
        Dictionary<int, string>? spellLines = null,
        Dictionary<int, SpellSpoken>? spoken = null)
    {
        _spells = spells;
        _classHasSpells = classHasSpells ?? new bool[CharacterClasses.Count];
        _descriptions = descriptions ?? [];
        _spellLines = spellLines ?? [];
        _spoken = spoken ?? [];
    }

    /// <summary>True when this class has a spell-gem spell at level 1 through <see cref="MaxSearchLevel"/>.</summary>
    public bool ClassHasSpells(int classIndex)
        => classIndex is >= 0 and < CharacterClasses.Count && _classHasSpells[classIndex];

    public static SpellCatalog Empty { get; } = new([]);

    public int Count => _spells.Count;

    /// <summary>Spell names keyed for a cast-line match. The value is the catalog spelling.</summary>
    public Dictionary<string, string> CastNames()
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var spell in _spells.Values)
        {
            if (spell.Name.Length > 0)
            {
                names.TryAdd(spell.Name, spell.Name);
            }
        }

        return names;
    }

    /// <summary>Spells one of <paramref name="classes"/> can cast at <paramref name="maxLevel"/> or below. The spell book leaves category -1 out.</summary>
    public IEnumerable<SpellInfo> UsableBy(IReadOnlyList<int> classes, int maxLevel)
    {
        var indexes = ClassIndexes(classes);
        if (indexes.Count == 0 || maxLevel < 1)
        {
            yield break;
        }

        foreach (var spell in _spells.Values)
        {
            if (IsOffered(spell) && UsableWithinCap(spell, indexes, maxLevel))
            {
                yield return spell;
            }
        }
    }

    public static SpellCatalog Load(string? gameDirectory)
    {
        if (string.IsNullOrWhiteSpace(gameDirectory))
        {
            return Empty;
        }

        var path = Path.Combine(gameDirectory, FileName);
        if (!File.Exists(path))
        {
            return Empty;
        }

        try
        {
            var descriptions = Path.Combine(gameDirectory, "dbstr_us.txt");
            var messages = Path.Combine(gameDirectory, "spells_us_str.txt");
            return Parse(
                File.ReadLines(path),
                File.Exists(descriptions) ? File.ReadLines(descriptions) : null,
                File.Exists(messages) ? File.ReadLines(messages) : null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Empty;
        }
    }

    /// <summary>
    /// The spell book blurb from dbstr_us.txt (string type 6).
    /// The key is the spell's description number, which matches the spell id only on older rows.
    /// </summary>
    public string Description(int spellId)
    {
        if (_spells.TryGetValue(spellId, out var spell) && spell.DescriptionId > 0)
        {
            return _descriptions.TryGetValue(spell.DescriptionId, out var text) ? text : string.Empty;
        }

        return _descriptions.TryGetValue(spellId, out var byId) ? byId : string.Empty;
    }

    /// <summary>Spell line name from dbstr_us.txt type 27, or empty when this row has none.</summary>
    public string SpellLine(int spellId)
    {
        if (!_spells.TryGetValue(spellId, out var spell) || spell.SpellLineId <= 0)
        {
            return string.Empty;
        }

        return _spellLines.TryGetValue(spell.SpellLineId, out var name) ? name : string.Empty;
    }

    /// <summary>On you, on other, and wears off text from spells_us_str.txt.</summary>
    public SpellSpoken Spoken(int spellId)
        => _spoken.TryGetValue(spellId, out var spoken) ? spoken : SpellSpoken.Empty;

    public bool TryGet(int id, out SpellInfo spell)
        => _spells.TryGetValue(id, out spell!);

    public static SpellCatalog Parse(IEnumerable<string> lines, IEnumerable<string>? descriptionLines = null, IEnumerable<string>? messageLines = null)
    {
        var spells = new Dictionary<int, SpellInfo>();
        var classHasSpells = new bool[CharacterClasses.Count];
        Span<int> separators = stackalloc int[StoredFieldCount];
        foreach (var line in lines)
        {
            var found = 0;
            for (var i = 0; i < line.Length && found < separators.Length; i++)
            {
                if (line[i] == '^')
                {
                    separators[found++] = i;
                }
            }

            if (found < 2
                || separators[0] == 0
                || !int.TryParse(line.AsSpan(0, separators[0]), NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            {
                continue;
            }

            var name = line.Substring(separators[0] + 1, separators[1] - separators[0] - 1).Trim();
            if (name.Length > 0)
            {
                var span = separators[..found];
                var levels = ReadLevels(line, span);
                var bookCategory = ReadInt(line, span, BookCategoryField);
                if (bookCategory != -1)
                {
                    MarkClassSpells(classHasSpells, levels, ReadInt(line, span, SkillField));
                }

                spells[id] = new SpellInfo(
                    id,
                    name,
                    levels,
                    ReadInt(line, span, CategoryField),
                    ReadInt(line, span, SubcategoryField),
                    ReadInt(line, span, DurationFormulaField),
                    ReadInt(line, span, DurationField),
                    ReadInt(line, span, IconField),
                    new SpellCasting(
                        ReadInt(line, span, ManaField),
                        ReadInt(line, span, CastTimeField),
                        ReadInt(line, span, RecoveryTimeField),
                        ReadInt(line, span, RecastTimeField),
                        ReadDouble(line, span, RangeField),
                        ReadDouble(line, span, AreaRangeField),
                        ReadInt(line, span, ResistField),
                        ReadInt(line, span, TargetField),
                        ReadInt(line, span, SkillField),
                        ReadInt(line, span, AreaDurationField),
                        ReadEffects(line, span),
                        ReadInt(line, span, MaxTargetsField)),
                    ReadInt(line, span, DescriptionIdField),
                    ReadInt(line, span, SpellLineField),
                    ReadInt(line, span, GoodEffectField),
                    ReadInt(line, span, CastLevelField),
                    ReadInt(line, span, ResistAdjustField),
                    bookCategory);
            }
        }

        var descriptions = new Dictionary<int, string>();
        var spellLines = new Dictionary<int, string>();
        ParseDbStrings(descriptionLines, descriptions, spellLines);
        return new SpellCatalog(spells, classHasSpells, descriptions, spellLines, ParseSpoken(messageLines));
    }

    public static Dictionary<int, string> ParseDescriptions(IEnumerable<string>? lines)
    {
        var descriptions = new Dictionary<int, string>();
        ParseDbStrings(lines, descriptions, []);
        return descriptions;
    }

    /// <summary>Type 6 is the spell book blurb. Type 27 is the spell line name.</summary>
    private static void ParseDbStrings(IEnumerable<string>? lines, Dictionary<int, string> descriptions, Dictionary<int, string> spellLines)
    {
        if (lines is null)
        {
            return;
        }

        foreach (var line in lines)
        {
            var first = line.IndexOf('^');
            if (first <= 0)
            {
                continue;
            }

            var second = line.IndexOf('^', first + 1);
            if (second <= first
                || !int.TryParse(line.AsSpan(0, first), NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            {
                continue;
            }

            var type = line.AsSpan(first + 1, second - first - 1);
            Dictionary<int, string>? target = type switch
            {
                "6" => descriptions,
                "27" => spellLines,
                _ => null,
            };
            if (target is null)
            {
                continue;
            }

            var textStart = second + 1;
            var textEnd = line.EndsWith("^0^", StringComparison.Ordinal) ? line.Length - 3
                : line.EndsWith("^0", StringComparison.Ordinal) ? line.Length - 2
                : line.Length;
            if (textEnd > textStart)
            {
                target[id] = line.Substring(textStart, textEnd - textStart);
            }
        }
    }

    private static Dictionary<int, SpellSpoken> ParseSpoken(IEnumerable<string>? lines)
    {
        var spoken = new Dictionary<int, SpellSpoken>();
        if (lines is null)
        {
            return spoken;
        }

        foreach (var line in lines)
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            var parts = line.Split('^');
            if (parts.Length < 6
                || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            {
                continue;
            }

            var onYou = parts[3];
            var onOther = parts[4];
            var wearsOff = parts[5];
            if (onYou.Length == 0 && onOther.Length == 0 && wearsOff.Length == 0)
            {
                continue;
            }

            spoken[id] = new SpellSpoken(onYou, onOther, wearsOff);
        }

        return spoken;
    }

    // Abjuration, alteration, conjuration, divination, evocation, channeling, and the bard song skills.
    // Combat skills such as offense, defense, and backstab are disciplines, not spell gems.
    public static bool IsCastingSkill(int skill)
        => skill is 4 or 5 or 12 or 13 or 14 or 18 or 24 or 41 or 43 or 44 or 45 or 46 or 47 or 49 or 54 or 70;

    private static void MarkClassSpells(bool[] flags, byte[]? levels, int skill)
    {
        if (levels is null || !IsCastingSkill(skill))
        {
            return;
        }

        for (var index = 0; index < levels.Length; index++)
        {
            if (levels[index] is >= 1 and <= MaxSearchLevel)
            {
                flags[index] = true;
            }
        }
    }

    public static bool IsEmptyGem(string? value)
    {
        return string.IsNullOrWhiteSpace(value) || value.Trim() is "-1" or "0";
    }

    public bool TryGetName(string? value, out string name)
    {
        name = TryGet(value, out var spell) ? spell.Name : string.Empty;
        return spell is not null;
    }

    public bool TryGet(string? value, out SpellInfo spell)
    {
        spell = null!;
        return !IsEmptyGem(value)
            && int.TryParse(value!.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
            && _spells.TryGetValue(id, out spell!);
    }

    /// <summary>
    /// Names that start with the text come first, then names that contain it. A numeric text also matches that spell ID.
    /// Fewer than two characters matches an ID only.
    /// Spells the spell book does not file (category -1), and Makeshift or Empowering Essence spells, are left out. Lookup by id still returns them.
    /// </summary>
    public IReadOnlyList<SpellInfo> Search(string? query, int limit)
    {
        if (limit <= 0 || string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var text = query.Trim();
        SpellInfo? byId = null;
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
            && _spells.TryGetValue(id, out var found)
            && IsOffered(found))
        {
            byId = found;
        }

        if (text.Length < 2 && byId is null)
        {
            return [];
        }

        var prefix = new List<SpellInfo>();
        var contains = new List<SpellInfo>();
        if (text.Length >= 2)
        {
            foreach (var spell in _spells.Values)
            {
                if (!IsOffered(spell) || ReferenceEquals(spell, byId))
                {
                    continue;
                }

                var index = spell.Name.IndexOf(text, StringComparison.OrdinalIgnoreCase);
                if (index < 0)
                {
                    continue;
                }

                (index == 0 ? prefix : contains).Add(spell);
            }

            prefix.Sort(CompareNames);
            contains.Sort(CompareNames);
        }

        var results = new List<SpellInfo>(Math.Min(limit, prefix.Count + contains.Count + 1));
        if (byId is not null)
        {
            results.Add(byId);
        }

        foreach (var spell in prefix.Concat(contains))
        {
            if (results.Count >= limit)
            {
                break;
            }

            results.Add(spell);
        }

        return results;
    }

    /// <summary>
    /// Spells the selected classes can use at <paramref name="maxLevel"/> or below.
    /// A number such as "1" matches that level. Any other text matches the name, and two or more characters also match the category, subcategory, or damage.
    /// Results follow the active sort. Level uses the other order as the tiebreaker, and name does the reverse.
    /// Spells the spell book does not file (category -1) are left out.
    /// </summary>
    public IReadOnlyList<SpellInfo> SearchForClasses(string? text, IReadOnlyList<int>? classes, int maxLevel, int limit, bool levelDescending = true, bool nameAscending = true, bool levelFirst = true)
    {
        if (limit <= 0 || maxLevel < 1)
        {
            return [];
        }

        var indexes = ClassIndexes(classes);
        var query = text?.Trim() ?? string.Empty;
        if (indexes.Count == 0)
        {
            return [];
        }

        Comparison<SpellInfo> compare = (left, right) => CompareForClasses(left, right, indexes, maxLevel, levelDescending, nameAscending, levelFirst);
        if (query.Length == 0)
        {
            return Collect(spell => UsableWithinCap(spell, indexes, maxLevel), limit, compare);
        }

        if (IsLevelQuery(query, out var level))
        {
            if (level > maxLevel)
            {
                return [];
            }

            return Collect(spell => HasClassLevel(spell, indexes, level), limit, compare);
        }

        var prefix = new List<SpellInfo>();
        var contains = new List<SpellInfo>();
        var described = new List<SpellInfo>();
        foreach (var spell in _spells.Values)
        {
            if (!IsOffered(spell) || !UsableWithinCap(spell, indexes, maxLevel))
            {
                continue;
            }

            var nameIndex = spell.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase);
            if (nameIndex == 0)
            {
                prefix.Add(spell);
            }
            else if (nameIndex > 0)
            {
                contains.Add(spell);
            }
            else if (query.Length >= 2 && (MatchesType(spell, query) || MatchesDamage(spell, query)))
            {
                described.Add(spell);
            }
        }

        var results = new List<SpellInfo>(prefix.Count + contains.Count + described.Count);
        results.AddRange(prefix);
        results.AddRange(contains);
        results.AddRange(described);
        results.Sort(compare);
        if (results.Count > limit)
        {
            results.RemoveRange(limit, results.Count - limit);
        }

        return results;
    }

    private static int CompareForClasses(SpellInfo left, SpellInfo right, IReadOnlyList<int> classes, int maxLevel, bool levelDescending, bool nameAscending, bool levelFirst)
    {
        var byLevel = HighestLevel(left, classes, maxLevel).CompareTo(HighestLevel(right, classes, maxLevel));
        if (levelDescending)
        {
            byLevel = -byLevel;
        }

        var byName = CompareNames(left, right);
        if (!nameAscending)
        {
            byName = -byName;
        }

        if (levelFirst)
        {
            return byLevel != 0 ? byLevel : byName;
        }

        return byName != 0 ? byName : byLevel;
    }

    private static int HighestLevel(SpellInfo spell, IReadOnlyList<int> classes, int maxLevel)
    {
        var best = 0;
        foreach (var index in classes)
        {
            if (spell.LevelFor(index) is { } level && level <= maxLevel && level > best)
            {
                best = level;
            }
        }

        return best;
    }

    /// <summary>
    /// Name matches the spell list. Level matches a class's required level. Type matches the spell book category
    /// (DOT and permanent are accepted names). Damage matches the element. Class matches spells that class can use,
    /// and a text of two or more characters also has to appear in the name.
    /// Spells the spell book does not file (category -1) are left out.
    /// </summary>
    public IReadOnlyList<SpellInfo> Search(SpellSearchKind kind, string? text, IReadOnlyList<int>? classes, int limit)
    {
        if (limit <= 0)
        {
            return [];
        }

        if (kind == SpellSearchKind.Name)
        {
            return Search(text, limit);
        }

        var query = text?.Trim() ?? string.Empty;
        if (kind == SpellSearchKind.Class)
        {
            return SearchByClass(query, classes, limit);
        }

        if (kind == SpellSearchKind.Level)
        {
            if (!int.TryParse(query, NumberStyles.None, CultureInfo.InvariantCulture, out var level) || level is < 1 or > 254)
            {
                return [];
            }

            return Collect(spell => HasLevel(spell, level), limit, CompareNames);
        }

        if (query.Length < 2)
        {
            return [];
        }

        return kind switch
        {
            SpellSearchKind.Type => Collect(spell => MatchesType(spell, query), limit, CompareNames),
            SpellSearchKind.Damage => Collect(spell => MatchesDamage(spell, query), limit, CompareNames),
            _ => [],
        };
    }

    private IReadOnlyList<SpellInfo> SearchByClass(string query, IReadOnlyList<int>? classes, int limit)
    {
        var indexes = ClassIndexes(classes);
        if (indexes.Count == 0)
        {
            return [];
        }

        var name = query.Length >= 2 ? query : null;
        return Collect(
            spell => Rank(spell, indexes) < int.MaxValue && (name is null || spell.Name.Contains(name, StringComparison.OrdinalIgnoreCase)),
            limit,
            (left, right) =>
            {
                var byLevel = Rank(left, indexes).CompareTo(Rank(right, indexes));
                return byLevel != 0 ? byLevel : CompareNames(left, right);
            });
    }

    private IReadOnlyList<SpellInfo> Collect(Func<SpellInfo, bool> match, int limit, Comparison<SpellInfo> compare)
    {
        var matches = new List<SpellInfo>();
        foreach (var spell in _spells.Values)
        {
            if (IsOffered(spell) && match(spell))
            {
                matches.Add(spell);
            }
        }

        matches.Sort(compare);
        if (matches.Count > limit)
        {
            matches.RemoveRange(limit, matches.Count - limit);
        }

        return matches;
    }

    /// <summary>
    /// A spell book category of -1 is not filed, so search does not offer it.
    /// Makeshift and Empowering Essence spells are in the file but not in the game, so search skips those names too.
    /// Stored gems still resolve by id.
    /// </summary>
    private static bool IsOffered(SpellInfo spell)
        => spell.BookCategory != -1
            && !spell.Name.Contains("Makeshift", StringComparison.OrdinalIgnoreCase)
            && !spell.Name.Contains("Empowering Essence", StringComparison.OrdinalIgnoreCase);

    private static bool IsLevelQuery(string text, out int level)
    {
        level = 0;
        if (text.Length == 0 || !text.All(char.IsDigit))
        {
            return false;
        }

        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out level) && level >= 1;
    }

    private static bool HasClassLevel(SpellInfo spell, IReadOnlyList<int> classes, int level)
    {
        foreach (var index in classes)
        {
            if (spell.LevelFor(index) == level)
            {
                return true;
            }
        }

        return false;
    }

    private static int LowestLevel(SpellInfo spell, IReadOnlyList<int> classes, int maxLevel)
    {
        var best = int.MaxValue;
        foreach (var index in classes)
        {
            if (spell.LevelFor(index) is { } level && level <= maxLevel && level < best)
            {
                best = level;
            }
        }

        return best;
    }

    private static bool UsableWithinCap(SpellInfo spell, IReadOnlyList<int> classes, int maxLevel)
    {
        foreach (var index in classes)
        {
            if (spell.LevelFor(index) is { } level && level <= maxLevel)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasLevel(SpellInfo spell, int level)
    {
        if (!spell.HasClassLevels)
        {
            return false;
        }

        for (var index = 0; index < CharacterClasses.Count; index++)
        {
            if (spell.LevelFor(index) == level)
            {
                return true;
            }
        }

        return false;
    }

    private static int Rank(SpellInfo spell, IReadOnlyList<int> classes)
    {
        var best = int.MaxValue;
        foreach (var index in classes)
        {
            if (spell.LevelFor(index) is { } level && level < best)
            {
                best = level;
            }
        }

        return best;
    }

    private static List<int> ClassIndexes(IReadOnlyList<int>? classes)
    {
        var indexes = new List<int>();
        if (classes is null)
        {
            return indexes;
        }

        foreach (var index in classes)
        {
            if (index is >= 0 and < CharacterClasses.Count && !indexes.Contains(index))
            {
                indexes.Add(index);
            }
        }

        return indexes;
    }

    private static bool MatchesType(SpellInfo spell, string text)
    {
        if (IsPermanentQuery(text))
        {
            return spell.IsPermanent;
        }

        if (IsDotQuery(text))
        {
            return spell.TypeName.Equals("Damage Over Time", StringComparison.OrdinalIgnoreCase);
        }

        if (text.Equals("dd", StringComparison.OrdinalIgnoreCase))
        {
            return spell.TypeName.Equals("Direct Damage", StringComparison.OrdinalIgnoreCase);
        }

        return spell.TypeName.Contains(text, StringComparison.OrdinalIgnoreCase)
            || spell.SubcategoryName.Contains(text, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesDamage(SpellInfo spell, string text)
    {
        if (spell.DamageName.Length == 0)
        {
            return false;
        }

        if (text.Equals("ice", StringComparison.OrdinalIgnoreCase))
        {
            text = "cold";
        }

        return spell.DamageName.Contains(text, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPermanentQuery(string text)
        => text.Equals("permanent", StringComparison.OrdinalIgnoreCase) || text.Equals("perm", StringComparison.OrdinalIgnoreCase);

    private static bool IsDotQuery(string text)
        => text.Equals("dot", StringComparison.OrdinalIgnoreCase)
            || text.Equals("dots", StringComparison.OrdinalIgnoreCase)
            || text.Equals("d.o.t", StringComparison.OrdinalIgnoreCase)
            || text.Equals("d.o.t.", StringComparison.OrdinalIgnoreCase);

    private static int CompareNames(SpellInfo left, SpellInfo right)
        => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);

    private static int ReadInt(string line, ReadOnlySpan<int> separators, int field)
    {
        if (field <= 0 || separators.Length < field)
        {
            return 0;
        }

        var start = separators[field - 1] + 1;
        var end = separators.Length > field ? separators[field] : line.Length;
        if (end <= start
            || !int.TryParse(line.AsSpan(start, end - start), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            return 0;
        }

        return value;
    }

    private static double ReadDouble(string line, ReadOnlySpan<int> separators, int field)
    {
        if (field <= 0 || separators.Length < field)
        {
            return 0;
        }

        var start = separators[field - 1] + 1;
        var end = separators.Length > field ? separators[field] : line.Length;
        if (end <= start
            || !double.TryParse(line.AsSpan(start, end - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return 0;
        }

        return value;
    }

    private static string ReadText(string line, ReadOnlySpan<int> separators, int field)
    {
        if (field <= 0 || separators.Length < field)
        {
            return string.Empty;
        }

        var start = separators[field - 1] + 1;
        var end = separators.Length > field ? separators[field] : line.Length;
        return end > start ? line.Substring(start, end - start) : string.Empty;
    }

    /// <summary>
    /// The effect slots are one caret field of <c>slot|effect|base|limit|calc|max</c> groups.
    /// That field sat at column 172 in August 2026 and has moved since, so the row is scanned
    /// for the group instead of trusting a column number.
    /// </summary>
    private static string ReadEffects(string line, ReadOnlySpan<int> separators)
    {
        for (var field = separators.Length; field >= 1; field--)
        {
            var text = ReadText(line, separators, field);
            if (IsEffectBlob(text))
            {
                return text;
            }
        }

        return string.Empty;
    }

    private static bool IsEffectBlob(string value)
    {
        if (value.Length < 11 || value.IndexOf('|') < 0)
        {
            return false;
        }

        foreach (var part in value.Split('$'))
        {
            if (part.Length == 0)
            {
                continue;
            }

            var bits = part.Split('|');
            if (bits.Length < 6)
            {
                continue;
            }

            var slot = true;
            for (var index = 0; index < 6; index++)
            {
                if (!int.TryParse(bits[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                {
                    slot = false;
                    break;
                }
            }

            if (slot)
            {
                return true;
            }
        }

        return false;
    }

    // Field n sits between separator n-1 and separator n; the last class column may run to the end of the line.
    private static byte[]? ReadLevels(string line, ReadOnlySpan<int> separators)
    {
        if (separators.Length < FirstClassField + CharacterClasses.Count - 1)
        {
            return null;
        }

        var levels = new byte[CharacterClasses.Count];
        for (var i = 0; i < CharacterClasses.Count; i++)
        {
            var field = FirstClassField + i;
            var start = separators[field - 1] + 1;
            var end = field < separators.Length ? separators[field] : line.Length;
            if (!int.TryParse(line.AsSpan(start, end - start), NumberStyles.None, CultureInfo.InvariantCulture, out var level))
            {
                return null;
            }

            levels[i] = (byte)Math.Clamp(level, 0, SpellInfo.Unusable);
        }

        return levels;
    }
}
