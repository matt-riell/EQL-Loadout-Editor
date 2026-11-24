using System.IO;
using System.Text.Json;
using EqlLoadouts.Core.Content;
using EqlLoadouts.Core.Loadouts;

namespace EqlLoadouts.App.Settings;

public sealed class ClassLabelRecord
{
    public string Character { get; set; } = string.Empty;

    public string Server { get; set; } = string.Empty;

    public int Number { get; set; }

    public string Label { get; set; } = string.Empty;

    /// <summary>Character level for this loadout. Missing until the user enters one.</summary>
    public int? Level { get; set; }
}

public sealed class AppSettings
{
    public string? GameDirectory { get; set; }

    public string Theme { get; set; } = "Dark";

    public bool SnapToGrid { get; set; } = true;

    public int SnapGrid { get; set; } = 10;

    public string? SourceCharacter { get; set; }

    public string? SourceServer { get; set; }

    public int? SourceLoadout { get; set; }

    public string? DestinationCharacter { get; set; }

    public string? DestinationServer { get; set; }

    public int? DestinationLoadout { get; set; }

    public bool DestinationAllServers { get; set; }

    public bool DestinationAllLoadouts { get; set; }

    public bool? DestinationServerChosen { get; set; }

    public bool ShowInventoryReminder { get; set; } = true;

    /// <summary>When true, Save does not remind the user to be at the character select screen.</summary>
    public bool HideSaveCharacterSelectWarning { get; set; }

    /// <summary>Debug builds can show the Raw keys tab. Release builds ignore this.</summary>
    public bool ShowRawKeys { get; set; }

    /// <summary>Debug builds can keep the saved text in the backup's modified folder. Release builds ignore this.</summary>
    public bool SaveModifiedCopies { get; set; }

    /// <summary>Last folder the user picked for loadout backups and copies.</summary>
    public string? BackupDirectory { get; set; }

    /// <summary>Debug builds can list HotButtons sections in the left panel. Release builds ignore this.</summary>
    public bool ShowHotButtons { get; set; }

    /// <summary>Debug builds can show suggested spell sets and gem suggestions. Release builds ignore this.</summary>
    public bool ShowSpellSuggestions { get; set; }

    /// <summary>Pixels left between windows when stacking left or right. 0 places the edges together.</summary>
    public int HorizontalStackSpacing { get; set; }

    /// <summary>Pixels left between windows when stacking up or down. 0 places the edges together.</summary>
    public int VerticalStackSpacing { get; set; }

    /// <summary>0 is the Default texture set. 1, 2, or 3 are the Alternate sets. Null is Default.</summary>
    public int? SpellIconSet { get; set; }

    /// <summary>0.5 through 4 in steps of 0.5. Null is x1.5.</summary>
    public double? SpellIconScale { get; set; }

    /// <summary>Find a spell stays open until the user collapses it.</summary>
    public bool SpellSearchExpanded { get; set; } = true;

    /// <summary>Spell details stay open until the user collapses them.</summary>
    public bool SpellDetailsExpanded { get; set; } = true;

    /// <summary>Spell upgrade stays open until the user collapses it.</summary>
    public bool SpellUpgradeExpanded { get; set; } = true;

    public double? EditorWindowLeft { get; set; }

    public double? EditorWindowTop { get; set; }

    public double? EditorWindowWidth { get; set; }

    public double? EditorWindowHeight { get; set; }

    public bool EditorWindowMaximized { get; set; }

    public List<string> ExpandedSectionGroups { get; set; } = [];

    public List<ClassLabelRecord> ClassLabels { get; set; } = [];

    public List<GroupVisibilityRecord> GroupVisibility { get; set; } = [];

    /// <summary>Byte offset and /who sightings already read from each character log.</summary>
    public List<LogCursorRecord> LogCursors { get; set; } = [];
}

public sealed class LogCursorRecord
{
    public string Path { get; set; } = string.Empty;

    public string Character { get; set; } = string.Empty;

    public long Length { get; set; }

    public List<LogSightingRecord> Sightings { get; set; } = [];
}

public sealed class LogSightingRecord
{
    public string Classes { get; set; } = string.Empty;

    public int Level { get; set; }

    public DateTime LastSeen { get; set; }

    public int Count { get; set; }
}

public sealed class GroupVisibilityRecord
{
    public string Character { get; set; } = string.Empty;

    public string Server { get; set; } = string.Empty;

    public int Number { get; set; }

    public List<string> VisibleGroups { get; set; } = [];

    public List<string>? VisibleItems { get; set; }

    /// <summary>Shared inventory bags that are not in this loadout file and that the user hid.</summary>
    public List<string>? HiddenItems { get; set; }
}

public sealed class AppSettingsStore
{
    public const int MinCharacterLevel = 1;

    public const int MaxCharacterLevel = 125;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path;

    public AppSettingsStore()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "EqlLoadouts");
        Directory.CreateDirectory(folder);
        _path = Path.Combine(folder, "settings.json");
        Current = Load();
        Current.ClassLabels ??= [];
        Current.GroupVisibility ??= [];
        Current.ExpandedSectionGroups ??= [];
        Current.LogCursors ??= [];
    }

    public AppSettings Current { get; private set; }

    public string GetLabel(LoadoutId id)
    {
        return Current.ClassLabels.FirstOrDefault(record => Matches(record, id))?.Label ?? string.Empty;
    }

    public void SetLabel(LoadoutId id, string label)
    {
        var existing = Current.ClassLabels.FirstOrDefault(record => Matches(record, id));
        var text = label.Trim();
        if (text.Length == 0)
        {
            if (existing is null)
            {
                return;
            }

            if (existing.Level is null)
            {
                Current.ClassLabels.Remove(existing);
            }
            else
            {
                existing.Label = string.Empty;
            }
        }
        else if (existing is null)
        {
            Current.ClassLabels.Add(new ClassLabelRecord
            {
                Character = id.Character,
                Server = id.Server,
                Number = id.Number,
                Label = text,
            });
        }
        else
        {
            existing.Label = text;
        }

        Save();
    }

    public int? GetLevel(LoadoutId id)
    {
        var level = Current.ClassLabels.FirstOrDefault(record => Matches(record, id))?.Level;
        return level is >= MinCharacterLevel and <= MaxCharacterLevel ? level : null;
    }

    public void SetLevel(LoadoutId id, int? level)
    {
        var valid = level is >= MinCharacterLevel and <= MaxCharacterLevel ? level : null;
        var existing = Current.ClassLabels.FirstOrDefault(record => Matches(record, id));
        if (existing is null)
        {
            if (valid is null)
            {
                return;
            }

            Current.ClassLabels.Add(new ClassLabelRecord
            {
                Character = id.Character,
                Server = id.Server,
                Number = id.Number,
                Level = valid,
            });
        }
        else if (valid is null && string.IsNullOrWhiteSpace(existing.Label))
        {
            Current.ClassLabels.Remove(existing);
        }
        else if (existing.Level == valid)
        {
            return;
        }
        else
        {
            existing.Level = valid;
        }

        Save();
    }

    public IReadOnlyList<WhoScan> LogScans()
    {
        var scans = new List<WhoScan>();
        foreach (var cursor in Current.LogCursors)
        {
            if (string.IsNullOrWhiteSpace(cursor.Path) || string.IsNullOrWhiteSpace(cursor.Character) || cursor.Length < 0)
            {
                continue;
            }

            var sightings = new List<WhoSighting>();
            foreach (var sighting in cursor.Sightings ?? [])
            {
                var classes = CharacterClasses.Parse(sighting.Classes);
                if (classes.Count == 0 || sighting.Level < MinCharacterLevel)
                {
                    continue;
                }

                sightings.Add(new WhoSighting(classes, sighting.Level, sighting.LastSeen, Math.Max(1, sighting.Count)));
            }

            scans.Add(new WhoScan(cursor.Path, cursor.Character, cursor.Length, sightings));
        }

        return scans;
    }

    public void SaveLogCursor(WhoScan scan)
    {
        var existing = Current.LogCursors.FirstOrDefault(record => string.Equals(record.Path, scan.Path, StringComparison.OrdinalIgnoreCase));
        var sightings = scan.Sightings
            .Select(sighting => new LogSightingRecord
            {
                Classes = sighting.ClassText,
                Level = sighting.Level,
                LastSeen = sighting.LastSeen,
                Count = sighting.Count,
            })
            .ToList();
        if (existing is null)
        {
            Current.LogCursors.Add(new LogCursorRecord
            {
                Path = scan.Path,
                Character = scan.Character,
                Length = scan.Length,
                Sightings = sightings,
            });
        }
        else
        {
            existing.Path = scan.Path;
            existing.Character = scan.Character;
            existing.Length = scan.Length;
            existing.Sightings = sightings;
        }

        Save();
    }

    public void ClearAllLabels()
    {
        if (Current.ClassLabels.Count == 0)
        {
            return;
        }

        Current.ClassLabels.Clear();
        Save();
    }

    public bool TryGetVisibility(
        LoadoutId id,
        out IReadOnlyList<string>? items,
        out IReadOnlyList<string> groups,
        out IReadOnlyList<string> hidden)
    {
        var existing = Current.GroupVisibility.FirstOrDefault(record => Matches(record, id));
        if (existing is null)
        {
            items = null;
            groups = [];
            hidden = [];
            return false;
        }

        items = existing.VisibleItems;
        groups = existing.VisibleGroups;
        hidden = existing.HiddenItems ?? [];
        return true;
    }

    public void SetVisibleItems(LoadoutId id, IEnumerable<string> items, IEnumerable<string> hidden)
    {
        var list = items.OrderBy(item => item, StringComparer.Ordinal).ToList();
        var hiddenList = hidden.OrderBy(item => item, StringComparer.Ordinal).ToList();
        var existing = Current.GroupVisibility.FirstOrDefault(record => Matches(record, id));
        if (existing is null)
        {
            Current.GroupVisibility.Add(new GroupVisibilityRecord
            {
                Character = id.Character,
                Server = id.Server,
                Number = id.Number,
                VisibleItems = list,
                HiddenItems = hiddenList.Count == 0 ? null : hiddenList,
            });
        }
        else
        {
            existing.VisibleItems = list;
            existing.HiddenItems = hiddenList.Count == 0 ? null : hiddenList;
        }

        Save();
    }

    public void SetSnap(bool enabled, int size)
    {
        Current.SnapToGrid = enabled;
        Current.SnapGrid = size is >= 1 and <= 512 ? size : 10;
        Save();
    }

    public void SetTheme(string theme)
    {
        Current.Theme = theme is "Light" or "Dark" or "System" ? theme : "Dark";
        Save();
    }

    public void RememberSource(string character, string server, int number)
    {
        Current.SourceCharacter = character;
        Current.SourceServer = server;
        Current.SourceLoadout = number;
        Save();
    }

    public void RememberDestination(string? character, string? server, int? number, bool allServers, bool allLoadouts)
    {
        Current.DestinationAllServers = allServers;
        Current.DestinationAllLoadouts = allLoadouts || allServers;
        Current.DestinationCharacter = allServers ? null : character;
        Current.DestinationServer = allServers ? null : server;
        Current.DestinationLoadout = allServers || allLoadouts ? null : number;
        Save();
    }

    public void SetDestinationServerChosen(bool chosen)
    {
        Current.DestinationServerChosen = chosen;
        Save();
    }

    public void SetShowInventoryReminder(bool show)
    {
        Current.ShowInventoryReminder = show;
        Save();
    }

    public void SetHideSaveCharacterSelectWarning(bool hide)
    {
        Current.HideSaveCharacterSelectWarning = hide;
        Save();
    }

    public void SetShowRawKeys(bool show)
    {
        Current.ShowRawKeys = show;
        Save();
    }

    public void SetSaveModifiedCopies(bool save)
    {
        Current.SaveModifiedCopies = save;
        Save();
    }

    public void SetBackupDirectory(string path)
    {
        if (string.Equals(Current.BackupDirectory, path, StringComparison.Ordinal))
        {
            return;
        }

        Current.BackupDirectory = path;
        Save();
    }

    public void SetShowHotButtons(bool show)
    {
        Current.ShowHotButtons = show;
        Save();
    }

    public void SetShowSpellSuggestions(bool show)
    {
        Current.ShowSpellSuggestions = show;
        Save();
    }

    public void SetHorizontalStackSpacing(int pixels)
    {
        Current.HorizontalStackSpacing = pixels;
        Save();
    }

    public void SetVerticalStackSpacing(int pixels)
    {
        Current.VerticalStackSpacing = pixels;
        Save();
    }

    public bool IsSectionGroupExpanded(string name)
        => Current.ExpandedSectionGroups.Contains(name, StringComparer.Ordinal)
            || (name == "Macros" && Current.ExpandedSectionGroups.Contains("Socials", StringComparer.Ordinal))
            || (name == "Spell Gems" && Current.ExpandedSectionGroups.Contains("Spell gems", StringComparer.Ordinal))
            || (name == "Misc Windows" && Current.ExpandedSectionGroups.Contains("Windows", StringComparer.Ordinal));

    public void SetExpandedSectionGroups(IEnumerable<string> names)
    {
        var list = names.Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal).ToList();
        if (list.SequenceEqual(Current.ExpandedSectionGroups, StringComparer.Ordinal))
        {
            return;
        }

        Current.ExpandedSectionGroups = list;
        Save();
    }

    public EditorWindowPlacement EditorWindow => new(
        Current.EditorWindowLeft,
        Current.EditorWindowTop,
        Current.EditorWindowWidth,
        Current.EditorWindowHeight,
        Current.EditorWindowMaximized);

    public void SetEditorWindow(double left, double top, double width, double height, bool maximized)
    {
        if (Current.EditorWindowLeft == left
            && Current.EditorWindowTop == top
            && Current.EditorWindowWidth == width
            && Current.EditorWindowHeight == height
            && Current.EditorWindowMaximized == maximized)
        {
            return;
        }

        Current.EditorWindowLeft = left;
        Current.EditorWindowTop = top;
        Current.EditorWindowWidth = width;
        Current.EditorWindowHeight = height;
        Current.EditorWindowMaximized = maximized;
        Save();
    }

    public void ResetToDefaults()
    {
        Current = new AppSettings();
        Save();
    }

    public void Save()
    {
        File.WriteAllText(_path, JsonSerializer.Serialize(Current, JsonOptions));
    }

    private AppSettings Load()
    {
        if (!File.Exists(_path))
        {
            return new AppSettings();
        }

        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path)) ?? new AppSettings();
        }
        catch (Exception)
        {
            return new AppSettings();
        }
    }

    private static bool Matches(GroupVisibilityRecord record, LoadoutId id)
    {
        return string.Equals(record.Character, id.Character, StringComparison.OrdinalIgnoreCase)
            && string.Equals(record.Server, id.Server, StringComparison.OrdinalIgnoreCase)
            && record.Number == id.Number;
    }

    private static bool Matches(ClassLabelRecord record, LoadoutId id)
    {
        return string.Equals(record.Character, id.Character, StringComparison.OrdinalIgnoreCase)
            && string.Equals(record.Server, id.Server, StringComparison.OrdinalIgnoreCase)
            && record.Number == id.Number;
    }
}

public readonly record struct EditorWindowPlacement(
    double? Left,
    double? Top,
    double? Width,
    double? Height,
    bool Maximized);
