using System.Globalization;

namespace EqlLoadouts.Core.Loadouts;

public sealed class SaveResult
{
    private SaveResult(bool success, bool hadChanges, string message, string? backupDirectory, IReadOnlyList<string> writtenFiles)
    {
        Success = success;
        HadChanges = hadChanges;
        Message = message;
        BackupDirectory = backupDirectory;
        WrittenFiles = writtenFiles;
    }

    public bool Success { get; }

    public bool HadChanges { get; }

    public string Message { get; }

    public string? BackupDirectory { get; }

    public IReadOnlyList<string> WrittenFiles { get; }

    public static SaveResult NoChanges()
        => new(true, false, "Nothing changed, so no files were written.", null, []);

    public static SaveResult Failed(string message, string? backupDirectory = null, IReadOnlyList<string>? writtenFiles = null)
        => new(false, true, message, backupDirectory, writtenFiles ?? []);

    public static SaveResult Succeeded(string backupDirectory, IReadOnlyList<string> writtenFiles)
        => new(true, true, $"Saved {writtenFiles.Count} file(s). Backup: {backupDirectory}", backupDirectory, writtenFiles);

    public static SaveResult Copied(string backupDirectory, IReadOnlyList<string> writtenFiles)
        => new(true, true, $"Copied {writtenFiles.Count} file(s) to {backupDirectory}. Live game files were not changed.", backupDirectory, writtenFiles);

    public static SaveResult Restored(string backupDirectory, IReadOnlyList<string> writtenFiles)
        => new(true, true, $"Restored {writtenFiles.Count} file(s) from {backupDirectory}.", backupDirectory, writtenFiles);
}

public sealed class BackupSnapshot
{
    public BackupSnapshot(string kind, string name, string path, IReadOnlyList<BackupLoadout> loadouts)
    {
        Kind = kind;
        Name = name;
        Path = path;
        Loadouts = loadouts;
    }

    public string Kind { get; }

    public string Name { get; }

    public string Path { get; }

    public IReadOnlyList<BackupLoadout> Loadouts { get; }
}

public static class GameFileSaver
{
    public const string BackupRootName = "LoadoutBackups";

    public const string FullFolderName = "FULL";

    public const string ModifiedFolderName = "MODIFIED";

    public const string BackupStampFormat = "yyyy-MM-dd HH-mm-ss";

    public static SaveResult Save(
        string gameDirectory,
        IReadOnlyList<PendingFileWrite> writes,
        DateTimeOffset timestamp,
        bool keepModifiedCopies = false)
    {
        if (writes.Count == 0)
        {
            return SaveResult.NoChanges();
        }

        if (!Directory.Exists(gameDirectory))
        {
            return SaveResult.Failed("The game directory does not exist. Nothing was written.");
        }

        string backupFolder;
        try
        {
            backupFolder = CreateKindFolder(gameDirectory, ModifiedFolderName, timestamp);
        }
        catch (Exception ex)
        {
            return SaveResult.Failed($"Could not create the backup folder. Nothing was written. {ex.Message}");
        }

        try
        {
            foreach (var write in writes)
            {
                if (!File.Exists(write.Path))
                {
                    throw new FileNotFoundException(
                        $"Cannot back up {Path.GetFileName(write.Path)} because the file does not exist.");
                }

                var backupPath = Path.Combine(backupFolder, StoredRelativePath(write.Path));
                Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
                File.Copy(write.Path, backupPath, overwrite: false);
            }
        }
        catch (Exception ex)
        {
            return SaveResult.Failed($"Backup failed. Nothing was written. {ex.Message}");
        }

        if (keepModifiedCopies)
        {
            try
            {
                WriteModifiedCopies(backupFolder, writes);
            }
            catch (Exception ex)
            {
                return SaveResult.Failed(
                    $"Could not write the modified copies in {backupFolder}. Nothing was written to the live files. {ex.Message}",
                    backupFolder);
            }
        }

        var written = new List<string>();
        try
        {
            foreach (var write in writes)
            {
                WriteAtomically(write);
                written.Add(write.Path);
            }
        }
        catch (Exception ex)
        {
            return SaveResult.Failed(
                $"The backup is in {backupFolder}. Writing stopped after {written.Count} file(s). {ex.Message}",
                backupFolder,
                written);
        }

        return SaveResult.Succeeded(backupFolder, written);
    }

    public static SaveResult WriteCopies(string gameDirectory, IReadOnlyList<StoredBackup> copies, DateTimeOffset timestamp)
    {
        if (!Directory.Exists(gameDirectory))
        {
            return SaveResult.Failed("The game directory does not exist. Nothing was written.");
        }

        if (copies.Count == 0)
        {
            return SaveResult.Failed("There are no loadout files to copy.");
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var copy in copies)
        {
            if (!names.Add(BackupLayout.StoredPath(copy.Id, copy.Kind)))
            {
                return SaveResult.Failed($"Could not name the copy for {BackupLayout.LoadoutLabel(copy.Id)}. Nothing was written.");
            }
        }

        string folder;
        try
        {
            folder = CreateKindFolder(gameDirectory, FullFolderName, timestamp);
        }
        catch (Exception ex)
        {
            return SaveResult.Failed($"Could not create the backup folder. Nothing was written. {ex.Message}");
        }

        var written = new List<string>();
        try
        {
            foreach (var copy in copies)
            {
                var path = Path.Combine(folder, BackupLayout.StoredPath(copy.Id, copy.Kind));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, copy.Bytes);
                written.Add(path);
            }
        }
        catch (Exception ex)
        {
            return SaveResult.Failed($"Could not write the copies in {folder}. {ex.Message}", folder, written);
        }

        return SaveResult.Copied(folder, written);
    }

    public static bool HasFullBackup(string? gameDirectory)
    {
        if (string.IsNullOrWhiteSpace(gameDirectory))
        {
            return false;
        }

        var root = Path.Combine(gameDirectory, BackupRootName, FullFolderName);
        return Directory.Exists(root) && Directory.EnumerateDirectories(root).Any();
    }

    /// <summary>
    /// True when a full backup should run. A missing folder or an unreadable snapshot is due,
    /// so the caller can still report why nothing was copied. Otherwise the newest FULL stamp
    /// must be at least <paramref name="minimumAge"/> old.
    /// </summary>
    public static bool FullBackupIsDue(string? gameDirectory, DateTimeOffset now, TimeSpan minimumAge)
    {
        if (!TryGetLatestFullBackup(gameDirectory, out var latest))
        {
            return true;
        }

        return now - latest >= minimumAge;
    }

    public static bool TryGetLatestFullBackup(string? gameDirectory, out DateTimeOffset timestamp)
    {
        timestamp = default;
        if (string.IsNullOrWhiteSpace(gameDirectory))
        {
            return false;
        }

        var root = Path.Combine(gameDirectory, BackupRootName, FullFolderName);
        if (!Directory.Exists(root))
        {
            return false;
        }

        DateTimeOffset? latest = null;
        foreach (var folder in Directory.EnumerateDirectories(root))
        {
            if (!TryParseBackupStamp(Path.GetFileName(folder), out var stamp))
            {
                continue;
            }

            if (latest is null || stamp > latest.Value)
            {
                latest = stamp;
            }
        }

        if (latest is null)
        {
            return false;
        }

        timestamp = latest.Value;
        return true;
    }

    private static bool TryParseBackupStamp(string name, out DateTimeOffset timestamp)
    {
        if (!DateTime.TryParseExact(name, BackupStampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
        {
            timestamp = default;
            return false;
        }

        timestamp = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
        return true;
    }

    public static IReadOnlyList<BackupSnapshot> List(string gameDirectory)
    {
        var snapshots = new List<BackupSnapshot>();
        AddSnapshots(snapshots, gameDirectory, FullFolderName);
        AddSnapshots(snapshots, gameDirectory, ModifiedFolderName);
        return snapshots;
    }

    public static SaveResult Restore(string gameDirectory, IReadOnlyList<string> sourcePaths)
    {
        if (!Directory.Exists(gameDirectory))
        {
            return SaveResult.Failed("The game directory does not exist. Nothing was restored.");
        }

        if (sourcePaths.Count == 0)
        {
            return SaveResult.Failed("Choose a loadout file to restore.");
        }

        var planned = new List<(string Source, string Destination)>();
        var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sourcePath in sourcePaths)
        {
            if (!TryBackupSource(gameDirectory, sourcePath, out var source, out var gameFileName))
            {
                return SaveResult.Failed("Choose files from a FULL or MODIFIED backup. Nothing was restored.");
            }

            var destination = Path.Combine(gameDirectory, gameFileName);
            if (!destinations.Add(destination))
            {
                return SaveResult.Failed($"More than one backup file restores {gameFileName}. Nothing was restored.");
            }

            planned.Add((source, destination));
        }

        var written = new List<string>();
        try
        {
            foreach (var (source, destination) in planned)
            {
                File.Copy(source, destination, overwrite: true);
                written.Add(destination);
            }
        }
        catch (Exception ex)
        {
            return SaveResult.Failed($"Restore stopped after {written.Count} file(s). {ex.Message}", null, written);
        }

        return SaveResult.Restored(Path.GetDirectoryName(planned[0].Source) ?? gameDirectory, written);
    }

    private static void AddSnapshots(List<BackupSnapshot> snapshots, string gameDirectory, string kind)
    {
        var root = Path.Combine(gameDirectory, BackupRootName, kind);
        if (!Directory.Exists(root))
        {
            return;
        }

        foreach (var folder in Directory.EnumerateDirectories(root).OrderByDescending(path => Path.GetFileName(path), StringComparer.Ordinal))
        {
            snapshots.Add(new BackupSnapshot(kind, Path.GetFileName(folder), folder, ReadLoadouts(folder)));
        }
    }

    private static IReadOnlyList<BackupLoadout> ReadLoadouts(string stampFolder)
    {
        var found = new Dictionary<string, (BackupStoredFile? Character, string? CharacterPath, BackupStoredFile? Ui, string? UiPath)>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(stampFolder, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(stampFolder, file);
            if (relative.StartsWith("modified" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || relative.StartsWith("modified" + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!BackupLayout.TryMapStoredFile(relative, out var stored))
            {
                continue;
            }

            var key = Path.GetDirectoryName(stored.RelativePath) ?? stored.RelativePath;
            found.TryGetValue(key, out var pair);
            if (stored.Kind == LoadoutFileKind.Character)
            {
                pair.Character = stored;
                pair.CharacterPath = file;
            }
            else
            {
                pair.Ui = stored;
                pair.UiPath = file;
            }

            found[key] = pair;
        }

        return found
            .Select(item =>
            {
                var id = item.Value.Character?.Id ?? item.Value.Ui!.Id;
                return new BackupLoadout(
                    BackupLayout.LoadoutLabel(id),
                    BackupLayout.ServerLabel(id),
                    BackupLayout.LoadoutName(id),
                    item.Value.CharacterPath,
                    item.Value.Character?.GameFileName,
                    item.Value.UiPath,
                    item.Value.Ui?.GameFileName);
            })
            .OrderBy(item => item.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool TryBackupSource(string gameDirectory, string sourcePath, out string source, out string gameFileName)
    {
        source = string.Empty;
        gameFileName = string.Empty;
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(sourcePath);
        }
        catch (Exception)
        {
            return false;
        }

        if (!File.Exists(fullPath))
        {
            return false;
        }

        foreach (var kind in new[] { FullFolderName, ModifiedFolderName })
        {
            var root = Path.GetFullPath(Path.Combine(gameDirectory, BackupRootName, kind)) + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relativeToKind = fullPath[root.Length..];
            var split = relativeToKind.IndexOf(Path.DirectorySeparatorChar);
            if (split <= 0)
            {
                return false;
            }

            var relative = relativeToKind[(split + 1)..];
            if (relative.StartsWith("modified" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!BackupLayout.TryMapStoredFile(relative, out var stored))
            {
                return false;
            }

            source = fullPath;
            gameFileName = stored.GameFileName;
            return true;
        }

        return false;
    }

    private static string StoredRelativePath(string gameFilePath)
    {
        var fileName = Path.GetFileName(gameFilePath);
        return BackupLayout.TryStoredPath(fileName, out var stored) ? stored : fileName;
    }

    private static string CreateKindFolder(string gameDirectory, string kind, DateTimeOffset timestamp)
        => CreateTimestampedFolder(Path.Combine(gameDirectory, BackupRootName, kind), timestamp);

    private static string CreateTimestampedFolder(string root, DateTimeOffset timestamp)
    {
        var local = timestamp.LocalDateTime;
        for (var attempt = 0; attempt < 120; attempt++)
        {
            var name = local.ToString(BackupStampFormat, CultureInfo.InvariantCulture);
            var path = Path.Combine(root, name);
            if (!Directory.Exists(path) && !File.Exists(path))
            {
                Directory.CreateDirectory(path);
                return path;
            }

            local = local.AddSeconds(1);
        }

        throw new IOException("A free backup folder name was not available.");
    }

    private static void WriteModifiedCopies(string backupFolder, IReadOnlyList<PendingFileWrite> writes)
    {
        foreach (var write in writes)
        {
            var path = Path.Combine(backupFolder, "modified", StoredRelativePath(write.Path));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, write.NewText, write.Encoding);
        }
    }

    private static void WriteAtomically(PendingFileWrite write)
    {
        var temp = write.Path + ".eql-tmp";
        try
        {
            File.WriteAllText(temp, write.NewText, write.Encoding);
            File.Move(temp, write.Path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }
}
