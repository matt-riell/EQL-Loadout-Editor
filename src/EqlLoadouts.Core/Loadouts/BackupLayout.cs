using System.Globalization;

namespace EqlLoadouts.Core.Loadouts;

public static class BackupLayout
{
    public const string CharacterCopyName = "Character.ini";

    public const string UiCopyName = "UI.ini";

    public static string StoredPath(LoadoutId id, LoadoutFileKind kind)
        => Path.Combine(ServerFolder(id), LoadoutFolder(id.Number), CopyName(kind));

    public static bool TryStoredPath(string gameFileName, out string storedPath)
    {
        if (!LoadoutDiscovery.TryParseFileName(gameFileName, out var id, out var kind))
        {
            storedPath = string.Empty;
            return false;
        }

        storedPath = StoredPath(id, kind);
        return true;
    }

    public static bool TryMapStoredFile(string relativePath, out BackupStoredFile stored)
    {
        stored = null!;
        var parts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (parts.Length != 3)
        {
            return false;
        }

        var separator = " - ";
        var split = parts[0].IndexOf(separator, StringComparison.Ordinal);
        if (split <= 0 || !parts[1].StartsWith("LO", StringComparison.Ordinal))
        {
            return false;
        }

        if (!int.TryParse(parts[1][2..], NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number <= 0)
        {
            return false;
        }

        var kind = parts[2] switch
        {
            CharacterCopyName => LoadoutFileKind.Character,
            UiCopyName => LoadoutFileKind.Ui,
            _ => (LoadoutFileKind?)null,
        };
        if (kind is null)
        {
            return false;
        }

        var id = new LoadoutId(parts[0][(split + separator.Length)..], parts[0][..split].ToLowerInvariant(), number);
        if (id.Character.Length == 0 || id.Server.Length == 0)
        {
            return false;
        }

        stored = new BackupStoredFile(id, kind.Value, StoredPath(id, kind.Value));
        return string.Equals(stored.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase);
    }

    public static string GameFileName(LoadoutId id, LoadoutFileKind kind)
    {
        var name = string.Create(CultureInfo.InvariantCulture, $"{id.Character}_{id.Server}_LO{id.Number}.ini");
        return kind == LoadoutFileKind.Ui ? "UI_" + name : name;
    }

    public static string ServerLabel(LoadoutId id) => ServerFolder(id);

    public static string LoadoutName(LoadoutId id) => LoadoutFolder(id.Number);

    public static string LoadoutLabel(LoadoutId id)
        => string.Create(CultureInfo.InvariantCulture, $"{ServerFolder(id)}  {LoadoutFolder(id.Number)}");

    private static string ServerFolder(LoadoutId id)
        => $"{DisplayServer(id.Server)} - {id.Character}";

    private static string LoadoutFolder(int number)
        => "LO" + number.ToString("00", CultureInfo.InvariantCulture);

    private static string CopyName(LoadoutFileKind kind)
        => kind == LoadoutFileKind.Character ? CharacterCopyName : UiCopyName;

    private static string DisplayServer(string server)
        => server.Length == 0 ? server : char.ToUpperInvariant(server[0]) + server[1..].ToLowerInvariant();
}

public sealed class BackupStoredFile
{
    public BackupStoredFile(LoadoutId id, LoadoutFileKind kind, string relativePath)
    {
        Id = id;
        Kind = kind;
        RelativePath = relativePath;
    }

    public LoadoutId Id { get; }

    public LoadoutFileKind Kind { get; }

    public string RelativePath { get; }

    public string GameFileName => BackupLayout.GameFileName(Id, Kind);
}

public sealed class StoredBackup
{
    public StoredBackup(LoadoutId id, LoadoutFileKind kind, byte[] bytes)
    {
        Id = id;
        Kind = kind;
        Bytes = bytes;
    }

    public LoadoutId Id { get; }

    public LoadoutFileKind Kind { get; }

    public byte[] Bytes { get; }
}

public sealed class BackupLoadout
{
    public BackupLoadout(
        string label,
        string serverCharacter,
        string loadoutName,
        string? characterSource,
        string? characterGameFile,
        string? uiSource,
        string? uiGameFile)
    {
        Label = label;
        ServerCharacter = serverCharacter;
        LoadoutName = loadoutName;
        CharacterSource = characterSource;
        CharacterGameFile = characterGameFile;
        UiSource = uiSource;
        UiGameFile = uiGameFile;
    }

    public string Label { get; }

    public string ServerCharacter { get; }

    public string LoadoutName { get; }

    public string? CharacterSource { get; }

    public string? CharacterGameFile { get; }

    public string? UiSource { get; }

    public string? UiGameFile { get; }
}
