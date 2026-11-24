using System.Globalization;

namespace EqlLoadouts.Core.Content;

public sealed record BagContents(string Section, string ItemName, int Slots, int Used)
{
    public IReadOnlySet<int> UsedSlots { get; init; } = new HashSet<int>();
}

public sealed class InventoryExport
{
    public const string Command = "/outputfile inventory";

    private readonly Dictionary<string, BagContents> _bags;

    private InventoryExport(Dictionary<string, BagContents> bags, DateTime writtenAt)
    {
        _bags = bags;
        WrittenAt = writtenAt;
    }

    public DateTime WrittenAt { get; }

    public IReadOnlyCollection<BagContents> Bags => _bags.Values;

    public static string FileName(string character, string server) => $"{character}_{server}-Inventory.txt";

    public static InventoryExport? TryLoad(string? gameDirectory, string character, string server)
    {
        if (string.IsNullOrWhiteSpace(gameDirectory))
        {
            return null;
        }

        var path = Path.Combine(gameDirectory, FileName(character, server));
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return Parse(File.ReadAllLines(path), File.GetLastWriteTime(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static InventoryExport Parse(IEnumerable<string> lines, DateTime writtenAt)
    {
        var containers = new Dictionary<string, (string Name, int Slots)>(StringComparer.Ordinal);
        var used = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            var columns = line.Split('\t');
            if (columns.Length < 5)
            {
                continue;
            }

            var location = columns[0].Trim();
            var dash = location.IndexOf("-Slot", StringComparison.Ordinal);
            var parent = dash < 0 ? location : location[..dash];
            var section = SectionFor(parent);
            if (section is null)
            {
                continue;
            }

            if (dash >= 0)
            {
                if (columns[2].Trim() != "0"
                    && int.TryParse(location.AsSpan(dash + "-Slot".Length), NumberStyles.None, CultureInfo.InvariantCulture, out var slot)
                    && slot > 0)
                {
                    if (!used.TryGetValue(section, out var filled))
                    {
                        filled = [];
                        used[section] = filled;
                    }

                    filled.Add(slot - 1);
                }

                continue;
            }

            if (int.TryParse(columns[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var slots) && slots > 0)
            {
                containers[section] = (columns[1].Trim(), slots);
            }
        }

        var bags = containers.ToDictionary(
            pair => pair.Key,
            pair =>
            {
                var slots = used.TryGetValue(pair.Key, out var filled)
                    ? filled.Where(slot => slot < pair.Value.Slots).ToHashSet()
                    : [];
                return new BagContents(pair.Key, pair.Value.Name, pair.Value.Slots, slots.Count) { UsedSlots = slots };
            },
            StringComparer.Ordinal);
        return new InventoryExport(bags, writtenAt);
    }

    public BagContents? Find(string section) => _bags.GetValueOrDefault(section);

    private static string? SectionFor(string location)
    {
        if (location.StartsWith("General", StringComparison.Ordinal) && TryNumber(location, "General".Length, out var general))
        {
            return string.Create(CultureInfo.InvariantCulture, $"BagInv{general}");
        }

        if (location.StartsWith("Bank", StringComparison.Ordinal) && TryNumber(location, "Bank".Length, out var bank))
        {
            return string.Create(CultureInfo.InvariantCulture, $"BagBank{bank}");
        }

        return null;
    }

    private static bool TryNumber(string location, int start, out int number)
        => int.TryParse(location.AsSpan(start).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out number) && number > 0;
}
