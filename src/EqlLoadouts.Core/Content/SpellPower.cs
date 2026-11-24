namespace EqlLoadouts.Core.Content;

/// <summary>Spell power from "You begin casting" lines. The latest cast wins. A name that is itself a spell stays at power 0.</summary>
public static class SpellPower
{
    private static readonly string[] Romans = ["X", "IX", "VIII", "VII", "VI", "V", "IV", "III", "II", "I"];

    public static string DisplayName(string spellName, int power)
        => power <= 0 ? spellName : spellName + " " + Roman(power);

    public static string Roman(int power) => power switch
    {
        1 => "I",
        2 => "II",
        3 => "III",
        4 => "IV",
        5 => "V",
        6 => "VI",
        7 => "VII",
        8 => "VIII",
        9 => "IX",
        10 => "X",
        _ => string.Empty,
    };

    public static bool TryParseCast(string spoken, IReadOnlyDictionary<string, string> names, out string spellName, out int power)
    {
        spellName = string.Empty;
        power = 0;
        if (spoken.Length == 0)
        {
            return false;
        }

        if (names.TryGetValue(spoken, out var exact))
        {
            spellName = exact;
            return true;
        }

        foreach (var roman in Romans)
        {
            var suffix = " " + roman;
            if (!spoken.EndsWith(suffix, StringComparison.Ordinal))
            {
                continue;
            }

            var stem = spoken[..^suffix.Length];
            if (!names.TryGetValue(stem, out var match))
            {
                continue;
            }

            spellName = match;
            power = RomanValue(roman);
            return true;
        }

        return false;
    }

    public static Dictionary<string, int> Read(IEnumerable<string> lines, IReadOnlyDictionary<string, string> names)
    {
        var powers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines)
        {
            if (!TryTakeCast(line, out var spoken))
            {
                continue;
            }

            if (TryParseCast(spoken, names, out var spellName, out var power))
            {
                powers[spellName] = power;
            }
        }

        return powers;
    }

    public static Dictionary<string, int> Read(Stream stream, IReadOnlyDictionary<string, string> names, IProgress<(long Read, long Length)>? progress, CancellationToken cancellation)
    {
        var powers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var length = stream.CanSeek ? stream.Length : 0;
        using var reader = new StreamReader(stream, leaveOpen: true);
        long reported = 0;
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            cancellation.ThrowIfCancellationRequested();
            if (stream.CanSeek && stream.Position - reported >= 262144)
            {
                reported = stream.Position;
                progress?.Report((reported, length));
            }

            if (!TryTakeCast(line, out var spoken))
            {
                continue;
            }

            if (TryParseCast(spoken, names, out var spellName, out var power))
            {
                powers[spellName] = power;
            }
        }

        progress?.Report((length, length));
        return powers;
    }

    public static Dictionary<string, int>? ReadFile(string path, IReadOnlyDictionary<string, string> names, IProgress<(long Read, long Length)>? progress, CancellationToken cancellation)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return Read(stream, names, progress, cancellation);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool TryTakeCast(string line, out string spoken)
    {
        const string Marker = "You begin casting ";
        var index = line.IndexOf(Marker, StringComparison.Ordinal);
        if (index < 0)
        {
            spoken = string.Empty;
            return false;
        }

        spoken = line[(index + Marker.Length)..].Trim();
        if (spoken.EndsWith('.'))
        {
            spoken = spoken[..^1].TrimEnd();
        }

        return spoken.Length > 0;
    }

    private static int RomanValue(string roman) => roman switch
    {
        "I" => 1,
        "II" => 2,
        "III" => 3,
        "IV" => 4,
        "V" => 5,
        "VI" => 6,
        "VII" => 7,
        "VIII" => 8,
        "IX" => 9,
        "X" => 10,
        _ => 0,
    };
}
