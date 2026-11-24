using EqlLoadouts.Core.Content;

namespace EqlLoadouts.Core.Loadouts;

/// <summary>A game-folder change the editor may reload. Backup folders and temp writes are <see cref="None"/>.</summary>
public enum WatchedGameFile
{
    None,
    LoadoutIni,
    InventoryExport,
    ClientSettings,
    SpellList,
    SpellMessages,
    StringTable,
    WhoLog,
}

public static class GameFolderWatch
{
    public const string ClientFileName = "eqclient.ini";

    public const string SpellMessagesFileName = "spells_us_str.txt";

    public static WatchedGameFile Classify(string? gameDirectory, string? changedPath)
    {
        if (string.IsNullOrWhiteSpace(gameDirectory) || string.IsNullOrWhiteSpace(changedPath))
        {
            return WatchedGameFile.None;
        }

        string fullChanged;
        string root;
        try
        {
            fullChanged = Path.GetFullPath(changedPath);
            root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(gameDirectory));
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException)
        {
            return WatchedGameFile.None;
        }

        var name = Path.GetFileName(fullChanged);
        if (name.Length == 0
            || name.EndsWith(".eql-tmp", StringComparison.OrdinalIgnoreCase)
            || name.Contains("_Backup_", StringComparison.OrdinalIgnoreCase))
        {
            return WatchedGameFile.None;
        }

        var directory = Path.GetDirectoryName(fullChanged);
        if (directory is null)
        {
            return WatchedGameFile.None;
        }

        if (string.Equals(directory, root, StringComparison.OrdinalIgnoreCase))
        {
            if (name.Equals(ClientFileName, StringComparison.OrdinalIgnoreCase))
            {
                return WatchedGameFile.ClientSettings;
            }

            if (name.Equals(SpellCatalog.FileName, StringComparison.OrdinalIgnoreCase))
            {
                return WatchedGameFile.SpellList;
            }

            if (name.Equals(SpellMessagesFileName, StringComparison.OrdinalIgnoreCase))
            {
                return WatchedGameFile.SpellMessages;
            }

            if (name.Equals(AbilityCatalog.FileName, StringComparison.OrdinalIgnoreCase))
            {
                return WatchedGameFile.StringTable;
            }

            if (name.EndsWith("-Inventory.txt", StringComparison.OrdinalIgnoreCase))
            {
                return WatchedGameFile.InventoryExport;
            }

            if (LoadoutDiscovery.IsLoadoutFileName(name))
            {
                return WatchedGameFile.LoadoutIni;
            }

            return WatchedGameFile.None;
        }

        if (!string.Equals(directory, Path.Combine(root, "Logs"), StringComparison.OrdinalIgnoreCase))
        {
            return WatchedGameFile.None;
        }

        return name.StartsWith("eqlog_", StringComparison.OrdinalIgnoreCase)
            && name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
            ? WatchedGameFile.WhoLog
            : WatchedGameFile.None;
    }
}
