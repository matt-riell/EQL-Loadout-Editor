using System.Globalization;
using System.Text.RegularExpressions;

namespace EqlLoadouts.Core.Content;

/// <summary>
/// Alternate-ability names from dbstr_us.txt. A hotbar stores these as E{index} with an empty label.
/// Indexes 0 through 119 are socials. Index 120 and above is 120 plus the activation id
/// shown in the ability window. That id is not the dbstr id: activation 331 is Origin,
/// while string 331 is Fury of Magic.
/// Type 2 and type 3 are the two lines the game draws. Type 1 is the full name.
/// A later rank often has only a type 4 description, and the button keeps the first rank's name.
/// </summary>
public sealed partial class AbilityCatalog
{
    public const string FileName = "dbstr_us.txt";

    public const int SocialSlots = 120;

    /// <summary>
    /// Activation id to the dbstr id that holds the name. The pairs are abilities whose
    /// description text matches the activation id but whose string id is different.
    /// </summary>
    private static readonly Dictionary<int, int> ActivationString = new()
    {
        [4] = 1202,
        [14] = 18,
        [5] = 1201,
        [25] = 98,
        [35] = 128,
        [37] = 130,
        [39] = 132,
        [41] = 136,
        [43] = 140,
        [47] = 146,
        [58] = 163,
        [60] = 11470,
        [61] = 168,
        [68] = 183,
        [70] = 185,
        [71] = 186,
        [73] = 188,
        [76] = 193,
        [77] = 194,
        [80] = 197,
        [85] = 206,
        [98] = 233,
        [102] = 243,
        [107] = 254,
        [109] = 258,
        [110] = 259,
        [111] = 260,
        [116] = 273,
        [117] = 274,
        [126] = 289,
        [127] = 290,
        [128] = 291,
        [227] = 665,
        [331] = 1000,
        [372] = 1146,
        [558] = 1383,
        [565] = 1638,
        [570] = 1645,
        [571] = 1646,
        [903] = 184,
        [1061] = 6146,
        [2016] = 13964,
        [2253] = 399,
        [6000] = 7800,
        [6001] = 7850,
    };

    private readonly Dictionary<int, string> _full;
    private readonly Dictionary<int, string> _short;
    private readonly Dictionary<int, string> _second;
    private readonly Dictionary<int, string> _descriptions;

    private AbilityCatalog(
        Dictionary<int, string> full,
        Dictionary<int, string> shortNames,
        Dictionary<int, string> second,
        Dictionary<int, string> descriptions)
    {
        _full = full;
        _short = shortNames;
        _second = second;
        _descriptions = descriptions;
    }

    public static AbilityCatalog Empty { get; } = new([], [], [], []);

    public static AbilityCatalog Load(string? gameDirectory)
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
            return Parse(File.ReadLines(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Empty;
        }
    }

    public static AbilityCatalog Parse(IEnumerable<string> lines)
    {
        var full = new Dictionary<int, string>();
        var shortNames = new Dictionary<int, string>();
        var second = new Dictionary<int, string>();
        var descriptions = new Dictionary<int, string>();
        foreach (var line in lines)
        {
            if (!TryPart(line, out var id, out var type, out var text))
            {
                continue;
            }

            var target = type switch
            {
                1 => full,
                2 => shortNames,
                3 => second,
                _ => descriptions,
            };
            target.TryAdd(id, text);
        }

        return new AbilityCatalog(full, shortNames, second, descriptions);
    }

    public string? ButtonName(int id)
    {
        if (id >= SocialSlots)
        {
            id -= SocialSlots;
        }

        if (ActivationString.TryGetValue(id, out var stringId))
        {
            var named = Title(stringId);
            if (named is not null)
            {
                return named;
            }
        }

        return Title(id);
    }

    /// <summary>
    /// Higher ranks omit the title and keep a description that matches the first rank,
    /// aside from the class name on that first rank and the numbers in the blurb.
    /// </summary>
    private string? Title(int id)
    {
        var named = Label(id);
        if (named is not null || !_descriptions.TryGetValue(id, out var description))
        {
            return named;
        }

        var norm = Normalize(description);
        for (var previous = id - 1; previous >= 0 && id - previous <= 10; previous--)
        {
            if (!_descriptions.TryGetValue(previous, out var earlier))
            {
                continue;
            }

            if (!string.Equals(norm, Normalize(earlier), StringComparison.Ordinal))
            {
                break;
            }

            named = Label(previous);
            if (named is not null)
            {
                return named;
            }
        }

        return null;
    }

    private static string Normalize(string text)
    {
        var lower = text.ToLowerInvariant();
        lower = TagPattern().Replace(lower, " ");
        lower = DruidSubjectPattern().Replace(lower, "you will");
        lower = ClassPossessivePattern().Replace(lower, "your");
        lower = NumberPattern().Replace(lower, "#");
        return SpacePattern().Replace(lower, " ").Trim();
    }

    [GeneratedRegex("<[^>]+>", RegexOptions.CultureInvariant)]
    private static partial Regex TagPattern();

    [GeneratedRegex(@"\bthe druid will\b", RegexOptions.CultureInvariant)]
    private static partial Regex DruidSubjectPattern();

    [GeneratedRegex(
        @"\bthe (magicians|enchanters|clerics|shamans|warriors|necromancers|druids|rangers|beastlords|bards|monks|rogues|wizards|paladins|berserkers)\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex ClassPossessivePattern();

    [GeneratedRegex(@"\d+%?", RegexOptions.CultureInvariant)]
    private static partial Regex NumberPattern();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex SpacePattern();

    private string? Label(int id)
    {
        _short.TryGetValue(id, out var shortName);
        _second.TryGetValue(id, out var second);
        if (!string.IsNullOrEmpty(shortName) && !string.IsNullOrEmpty(second))
        {
            return shortName + "\n" + second;
        }

        if (!string.IsNullOrEmpty(shortName))
        {
            return shortName;
        }

        return _full.TryGetValue(id, out var name) && name.Length > 0 ? name : null;
    }

    private static bool TryPart(string line, out int id, out int type, out string text)
    {
        id = 0;
        type = 0;
        text = string.Empty;
        var first = line.IndexOf('^');
        if (first <= 0)
        {
            return false;
        }

        var second = line.IndexOf('^', first + 1);
        if (second <= first + 1)
        {
            return false;
        }

        var typeSpan = line.AsSpan(first + 1, second - first - 1);
        if (!typeSpan.SequenceEqual("1")
            && !typeSpan.SequenceEqual("2")
            && !typeSpan.SequenceEqual("3")
            && !typeSpan.SequenceEqual("4"))
        {
            return false;
        }

        if (!int.TryParse(line.AsSpan(0, first), NumberStyles.None, CultureInfo.InvariantCulture, out id))
        {
            return false;
        }

        type = typeSpan[0] - '0';
        var textStart = second + 1;
        var textEnd = line.EndsWith("^0^", StringComparison.Ordinal) ? line.Length - 3
            : line.EndsWith("^0", StringComparison.Ordinal) ? line.Length - 2
            : line.Length;
        if (textEnd <= textStart)
        {
            return false;
        }

        text = line.Substring(textStart, textEnd - textStart);
        return text.Length > 0;
    }
}
