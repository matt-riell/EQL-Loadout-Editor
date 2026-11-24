using System.Globalization;
using System.Text.RegularExpressions;

namespace EqlLoadouts.Core.Loadouts;

public enum LoadoutFileKind
{
    Character,
    Ui,
}

public sealed record LoadoutId(string Character, string Server, int Number)
{
    public string Title => string.Create(CultureInfo.InvariantCulture, $"{Character} — {Server} — LO{Number}");
}

public sealed class DiscoveredLoadout
{
    public DiscoveredLoadout(LoadoutId id, string? characterFile, string? uiFile)
    {
        Id = id;
        CharacterFile = characterFile;
        UiFile = uiFile;
    }

    public LoadoutId Id { get; }

    public string? CharacterFile { get; }

    public string? UiFile { get; }

    public string? FileFor(LoadoutFileKind kind)
    {
        return kind == LoadoutFileKind.Character ? CharacterFile : UiFile;
    }
}

public static class LoadoutDiscovery
{
    public const string DefaultGameDirectory =
        @"C:\Users\Public\Daybreak Game Company\Installed Games\EverQuest Legends";

    private static readonly Regex NamePattern = new(
        @"^(?<character>.+)_(?<server>[^_]+)_LO(?<number>\d+)\.ini$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool TryParseFileName(string? fileName, out LoadoutId id, out LoadoutFileKind kind)
    {
        id = new LoadoutId(string.Empty, string.Empty, 0);
        kind = LoadoutFileKind.Character;
        if (!IsLoadoutFileName(fileName))
        {
            return false;
        }

        var isUi = fileName!.StartsWith("UI_", StringComparison.Ordinal);
        var logicalName = isUi ? fileName[3..] : fileName;
        var match = NamePattern.Match(logicalName);
        if (!match.Success)
        {
            return false;
        }

        id = new LoadoutId(
            match.Groups["character"].Value,
            match.Groups["server"].Value,
            int.Parse(match.Groups["number"].Value, CultureInfo.InvariantCulture));
        kind = isUi ? LoadoutFileKind.Ui : LoadoutFileKind.Character;
        return true;
    }

    public static bool IsLoadoutFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)
            || fileName.Contains("_Backup_", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var logicalName = fileName.StartsWith("UI_", StringComparison.Ordinal) ? fileName[3..] : fileName;
        return NamePattern.IsMatch(logicalName);
    }

    public static IReadOnlyList<DiscoveredLoadout> Discover(string gameDirectory)
    {
        if (!Directory.Exists(gameDirectory))
        {
            return [];
        }

        var found = new Dictionary<LoadoutId, (string? Character, string? Ui)>();
        foreach (var path in Directory.EnumerateFiles(gameDirectory, "*.ini", SearchOption.TopDirectoryOnly))
        {
            var fileName = Path.GetFileName(path);
            if (!IsLoadoutFileName(fileName))
            {
                continue;
            }

            var isUi = fileName.StartsWith("UI_", StringComparison.Ordinal);
            var logicalName = isUi ? fileName[3..] : fileName;
            var match = NamePattern.Match(logicalName);
            if (!match.Success)
            {
                continue;
            }

            var id = new LoadoutId(
                match.Groups["character"].Value,
                match.Groups["server"].Value,
                int.Parse(match.Groups["number"].Value, CultureInfo.InvariantCulture));

            found.TryGetValue(id, out var pair);
            if (isUi)
            {
                pair.Ui = path;
            }
            else
            {
                pair.Character = path;
            }

            found[id] = pair;
        }

        return found
            .OrderBy(item => item.Key.Character, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Key.Server, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Key.Number)
            .Select(item => new DiscoveredLoadout(item.Key, item.Value.Character, item.Value.Ui))
            .ToList();
    }

    /// <summary>
    /// Loadouts the source dropdown can offer for one character on one server.
    /// <see cref="Discover"/> counts every character and server in the folder.
    /// </summary>
    public static IReadOnlyList<DiscoveredLoadout> ForCharacterAndServer(
        IEnumerable<DiscoveredLoadout> loadouts,
        string? character,
        string? server)
    {
        if (string.IsNullOrEmpty(character) || string.IsNullOrEmpty(server))
        {
            return [];
        }

        return loadouts
            .Where(loadout => string.Equals(loadout.Id.Character, character, StringComparison.OrdinalIgnoreCase)
                && string.Equals(loadout.Id.Server, server, StringComparison.OrdinalIgnoreCase))
            .OrderBy(loadout => loadout.Id.Number)
            .ToList();
    }
}
