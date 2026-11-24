using System.Globalization;
using System.Text.RegularExpressions;

namespace EqlLoadouts.Core.Content;

/// <summary>A class combination the character was seen with in /who output.</summary>
public sealed record WhoSighting(IReadOnlyList<int> Classes, int Level, DateTime LastSeen, int Count)
{
    public string ClassText => CharacterClasses.Format(Classes);
}

/// <summary>The result of reading a log up to <see cref="Length"/> bytes, so a later scan can read only what was appended.</summary>
public sealed record WhoScan(string Path, string Character, long Length, IReadOnlyList<WhoSighting> Sightings)
{
    public bool Exists => Length > 0;
}

public static class WhoLog
{
    private const string TimeFormat = "ddd MMM dd HH:mm:ss yyyy";

    public static string PathFor(string gameDirectory, string character, string server)
    {
        return Path.Combine(gameDirectory, "Logs", $"eqlog_{character}_{server}.txt");
    }

    /// <summary>Reads <c>eqlog_{character}_{server}.txt</c>. The server is the last segment, so a character name may contain underscores.</summary>
    public static bool TryParseLogFileName(string? path, out string character, out string server)
    {
        character = string.Empty;
        server = string.Empty;
        var name = Path.GetFileName(path);
        if (string.IsNullOrEmpty(name)
            || name.Length < "eqlog_a_b.txt".Length
            || !name.StartsWith("eqlog_", StringComparison.OrdinalIgnoreCase)
            || !name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var body = name[6..^4];
        var split = body.LastIndexOf('_');
        if (split <= 0 || split >= body.Length - 1)
        {
            return false;
        }

        character = body[..split];
        server = body[(split + 1)..];
        return true;
    }

    /// <summary>Reads the log, continuing from <paramref name="previous"/> when it covers the same file and the file has only grown.</summary>
    public static WhoScan Scan(string path, string character, WhoScan? previous = null, CancellationToken cancellation = default)
    {
        var empty = new WhoScan(path, character, 0, []);
        if (!File.Exists(path))
        {
            return empty;
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var resume = previous is not null
                && string.Equals(previous.Path, path, StringComparison.OrdinalIgnoreCase)
                && string.Equals(previous.Character, character, StringComparison.OrdinalIgnoreCase)
                && previous.Length <= stream.Length;
            if (resume && previous!.Length == stream.Length)
            {
                return previous;
            }

            stream.Position = resume ? previous!.Length : 0;
            using var reader = new StreamReader(stream);
            var found = Parse(ReadLines(reader, cancellation), character);
            return new WhoScan(path, character, stream.Position, resume ? Merge(previous!.Sightings, found) : found);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return previous ?? empty;
        }
    }

    /// <summary>The newest /who whose classes are the same set as <paramref name="classes"/>, in any order.</summary>
    public static DateTime? LastSeen(IEnumerable<WhoSighting> sightings, IReadOnlyList<int> classes)
    {
        if (classes.Count == 0)
        {
            return null;
        }

        DateTime? latest = null;
        foreach (var sighting in sightings)
        {
            if (!SameClasses(sighting.Classes, classes))
            {
                continue;
            }

            if (latest is null || sighting.LastSeen > latest)
            {
                latest = sighting.LastSeen;
            }
        }

        return latest;
    }

    /// <summary>The level from the newest /who whose classes are the same set as <paramref name="classes"/>, in any order.</summary>
    public static int? LevelFor(IEnumerable<WhoSighting> sightings, IReadOnlyList<int> classes)
    {
        if (classes.Count == 0)
        {
            return null;
        }

        DateTime? latest = null;
        int? level = null;
        foreach (var sighting in sightings)
        {
            if (!SameClasses(sighting.Classes, classes))
            {
                continue;
            }

            if (latest is null || sighting.LastSeen > latest)
            {
                latest = sighting.LastSeen;
                level = sighting.Level;
            }
        }

        return level;
    }

    public static IReadOnlyList<WhoSighting> Merge(IReadOnlyList<WhoSighting> earlier, IReadOnlyList<WhoSighting> later)
    {
        var merged = earlier.ToDictionary(sighting => sighting.ClassText, StringComparer.Ordinal);
        foreach (var sighting in later)
        {
            merged[sighting.ClassText] = merged.TryGetValue(sighting.ClassText, out var known)
                ? (sighting.LastSeen >= known.LastSeen ? sighting : known) with { Count = known.Count + sighting.Count }
                : sighting;
        }

        return merged.Values.OrderByDescending(sighting => sighting.LastSeen).ToList();
    }

    /// <summary>Finds lines like "[Sat Oct 03 09:04:40 2026] [50 ROG/SHM/ENC] Zenator (Iksar) ...", newest combination first.</summary>
    public static IReadOnlyList<WhoSighting> Parse(IEnumerable<string> lines, string character)
    {
        var needle = $"] {character} ";
        var pattern = new Regex(
            $@"^\[(?<time>[^\]]+)\]\s+(?:\S+\s+)?\[(?<level>\d+) (?<classes>[A-Z]{{3}}(?:/[A-Z]{{3}}){{0,2}})\] {Regex.Escape(character)} ",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var sightings = new Dictionary<string, WhoSighting>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            if (!line.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var match = pattern.Match(line);
            if (!match.Success
                || !DateTime.TryParseExact(match.Groups["time"].Value, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var seen)
                || !int.TryParse(match.Groups["level"].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out var level))
            {
                continue;
            }

            var classes = CharacterClasses.Parse(match.Groups["classes"].Value);
            if (classes.Count == 0)
            {
                continue;
            }

            var key = CharacterClasses.Format(classes);
            sightings[key] = sightings.TryGetValue(key, out var known)
                ? seen >= known.LastSeen
                    ? new WhoSighting(classes, level, seen, known.Count + 1)
                    : known with { Count = known.Count + 1 }
                : new WhoSighting(classes, level, seen, 1);
        }

        return sightings.Values.OrderByDescending(sighting => sighting.LastSeen).ToList();
    }

    private static bool SameClasses(IReadOnlyList<int> left, IReadOnlyList<int> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (var item in left)
        {
            if (!right.Contains(item))
            {
                return false;
            }
        }

        return true;
    }

    private static IEnumerable<string> ReadLines(StreamReader reader, CancellationToken cancellation)
    {
        while (reader.ReadLine() is { } line)
        {
            cancellation.ThrowIfCancellationRequested();
            yield return line;
        }
    }
}
