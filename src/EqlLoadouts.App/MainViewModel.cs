using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using EqlLoadouts.Core.Content;
using EqlLoadouts.Core.Ini;
using EqlLoadouts.Core.Layout;
using EqlLoadouts.Core.Loadouts;
using EqlLoadouts.App.Models;
using EqlLoadouts.App.Settings;

namespace EqlLoadouts.App;

public sealed class ServerChoice
{
    public string? Character { get; init; }

    public string? Server { get; init; }

    public bool AllServers { get; init; }

    public required string Text { get; init; }
}

public sealed record SnapOption(int Size, string Text)
{
    public bool IsOff => Size == 0;

    public override string ToString() => Text;
}

public sealed record ClassOption(int Index, string Text)
{
    public override string ToString() => Text;
}

public sealed record ClassSuggestion(string Classes, int Level, string Tip);

public sealed record SpellIconSetOption(int Number, string Text);

public sealed record SpellIconScaleOption(double Scale, string Text);

public sealed class LoadoutChoice : ObservableObject
{
    private string _text;

    public LoadoutChoice(string text, DiscoveredLoadout? loadout = null, bool allLoadouts = false)
    {
        _text = text;
        Loadout = loadout;
        AllLoadouts = allLoadouts;
    }

    public DiscoveredLoadout? Loadout { get; }

    public bool AllLoadouts { get; }

    public string Text
    {
        get => _text;
        set => SetProperty(ref _text, value);
    }
}

public sealed class SectionItem : ObservableObject
{
    private bool _isShown;
    private string _detail = string.Empty;
    private string _bagCaption = string.Empty;

    public required string Name { get; init; }

    public required LoadoutFileKind Kind { get; init; }

    public required string Group { get; init; }

    public required int GroupOrder { get; init; }

    public string KindLabel => Kind == LoadoutFileKind.Character ? "Character" : string.Empty;

    public string Caption { get; init; } = string.Empty;

    public string BagCaption
    {
        get => _bagCaption;
        set
        {
            if (SetProperty(ref _bagCaption, value))
            {
                Raise(nameof(ListText));
            }
        }
    }

    public string ListText => _bagCaption.Length > 0 ? _bagCaption : Caption.Length > 0 ? Caption : SectionTitles.Format(Name);

    public string Detail
    {
        get => _detail;
        set => SetProperty(ref _detail, value);
    }

    public bool CanToggleShown => Name is not ("SpellLoadouts" or "Socials");

    public bool IsShown
    {
        get => _isShown;
        set => SetProperty(ref _isShown, value);
    }

    public void SetShown(bool shown)
    {
        if (_isShown == shown)
        {
            return;
        }

        _isShown = shown;
        Raise(nameof(IsShown));
    }

    public bool IsBlocked { get; private set; }

    public string BlockReason { get; private set; } = string.Empty;

    public void Block(string reason)
    {
        IsBlocked = true;
        BlockReason = reason;
    }

    public string SortName => SortKey(Name);

    internal static string SortKey(string name)
    {
        var index = 0;
        var prefixLength = 0;
        if (name.StartsWith("BagInv", StringComparison.Ordinal))
        {
            prefixLength = "BagInv".Length;
        }
        else if (name.StartsWith("BagBank", StringComparison.Ordinal))
        {
            prefixLength = "BagBank".Length;
        }
        else if (name.StartsWith("BagWorld", StringComparison.Ordinal))
        {
            prefixLength = "BagWorld".Length;
        }
        else if (name.StartsWith("HotButtonWnd", StringComparison.Ordinal))
        {
            prefixLength = "HotButtonWnd".Length;
        }
        else if (name.StartsWith("HotButtons", StringComparison.Ordinal))
        {
            prefixLength = "HotButtons".Length;
        }

        if (prefixLength == 0)
        {
            return name;
        }

        var digits = 0;
        while (prefixLength + digits < name.Length && char.IsDigit(name[prefixLength + digits]))
        {
            digits++;
        }

        if (digits > 0 && int.TryParse(name.AsSpan(prefixLength, digits), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
        {
            index = number;
        }

        var suffix = name[(prefixLength + digits)..];
        return string.Create(CultureInfo.InvariantCulture, $"{name[..prefixLength]}{index:00}{suffix}");
    }
}

public sealed class SectionRow : ObservableObject
{
    private GroupToggle? _group;

    public GroupToggle? Group
    {
        get => _group;
        init
        {
            _group = value;
            if (_group is null)
            {
                return;
            }

            _group.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName is nameof(GroupToggle.ExpandLabel) or nameof(GroupToggle.IsExpanded))
                {
                    Raise(nameof(AutomationName));
                }
            };
        }
    }

    public SectionItem? Item { get; init; }

    public bool IsChild { get; init; }

    public bool IsGroup => Group is not null;

    public string AutomationName => Group is { } group ? group.ExpandLabel : Item?.ListText ?? string.Empty;
}

public sealed record SavePrompt(
    bool WindowsChanged,
    bool CharacterChanged,
    bool CharacterContentChanged,
    bool OtherCharacterChanged,
    IReadOnlyList<string> WindowFiles,
    IReadOnlyList<string> CharacterFiles)
{
    public bool HasSavableChanges => WindowsChanged || CharacterChanged;
}

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly AppSettingsStore _store;
    private readonly ObservableCollection<SectionItem> _sections = [];
    private readonly ObservableCollection<SectionRow> _sectionRows = [];
    private bool _hasEditorRows;
    private List<DiscoveredLoadout> _catalog = [];
    private IReadOnlyList<string>? _sharedInventoryBags;
    private LoadedIniFile? _sourceCharacter;
    private LoadedIniFile? _sourceUi;
    private LoadedIniFile? _destCharacter;
    private LoadedIniFile? _destUi;
    private IniSection? _pending;
    private UiSkinCatalog _skins = UiSkinCatalog.Empty;
    private string _skinName = string.Empty;
    private InventoryExport? _inventory;
    private FileSystemWatcher? _inventoryWatcher;
    private FileSystemWatcher? _gameWatcher;
    private FileSystemWatcher? _logWatcher;
    private readonly DispatcherTimer _inventoryReload = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly DispatcherTimer _externalReload = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly DispatcherTimer _logTail = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly HashSet<string> _pendingGameChanges = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _dirtyLogs = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _logsReading = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _suppressedWrites = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _ownWriteStamps = new(StringComparer.OrdinalIgnoreCase);
    private int _externalRetries;
    private bool _disposed;
    private string _bagSlotsText = string.Empty;
    private string _bagSlotsHelp = string.Empty;
    private string _inventoryReminderText = string.Empty;
    private bool _bagSlotsStale;
    private int MutedDepth;
    private int _selectionDepth;

    private bool Muted => MutedDepth > 0;
    private readonly Dictionary<string, LoadedIniFile> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _fileStamps = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _baselines = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, Dictionary<string, IniSection>> _pendingByContext = new(StringComparer.Ordinal);
    private readonly List<(LoadoutFileKind Kind, IniSection Section)> _touchedPending = [];
    private readonly List<EditState> _undo = [EditState.Empty];
    private readonly List<EditState> _redo = [];
    private int _unsavedFileCount;
    private ServerChoice? _sourceServer;
    private LoadoutChoice? _sourceLoadout;
    private ServerChoice? _destinationServer;
    private LoadoutChoice? _destinationLoadout;
    private SectionItem? _selectedSection;
    private string _gameDirectory = LoadoutDiscovery.DefaultGameDirectory;
    private string _classLabel = string.Empty;
    private string _characterLevelText = string.Empty;
    private bool _syncingCharacterLevel;
    private int? _shownLogLevel;
    private int _suppressClassPrompt;
    private int _classOfferTicket;
    private bool _offerClassesAfterLoad;
    private LoadoutId? _classOfferLoadout;
    private string _status = "Choose a loadout.";
    private string _sectionFilter = string.Empty;
    private bool _showSectionFilter;
    private string _viewedTitle = "No loadout selected";
    private string _editorNote = string.Empty;
    private string _structuredTabTitle = string.Empty;
    private string _xRef = "left";
    private string _yRef = "top";
    private string _xPosText = string.Empty;
    private string _yPosText = string.Empty;
    private string _widthText = string.Empty;
    private string _heightText = string.Empty;
    private bool _resolutionFromGame;
    private bool _isDarkTheme;
    private SnapOption _selectedSnap;
    private int _snapSize;
    private readonly List<string> _layoutSelection = [];
    private List<string> _layoutReturn = [];
    private readonly HashSet<string> _shownKeys = new(StringComparer.Ordinal);
    private bool _showMissing;
    private bool _showSocials;
    private bool _showHotbar;
    private bool _showSpells;
    private bool _showLayout = true;
    private bool _showPlacement;
    private bool _showChatFilters;
    private bool _showRaw;
    private bool _showEditorUnavailable;
    private string _editorUnavailableMessage = string.Empty;
    private bool _canResize = true;
    private bool _canCopy;
    private bool _hasUnsavedChanges;
    private bool _showAllSpells;
    private SpellRow? _selectedSpellSet;
    private SpellGem? _selectedGem;
    private SuggestedSpellPage? _selectedSuggestion;
    private SuggestedGemFace? _selectedSuggestionGem;
    private SpellSearchHit? _selectedSpellHit;
    private ISpellDetails? _hoveredSpellDetail;
    private ISpellDetails? _hoveredUpgrade;
    private int _upgradeGeneration;
    private string _upgradeKey = string.Empty;
    private bool _selectingSpell;
    private string _spellListNote = string.Empty;
    private string _spellSearchText = string.Empty;
    private string _spellSearchNote = string.Empty;
    private bool _spellLevelDescending = true;
    private bool _spellNameAscending = true;
    private bool _spellSortByLevel = true;
    private SpellClassChoice? _spellClassChoice;
    private bool _rebuildingSpellClasses;
    private SpellCatalog? _spellCatalog;
    private readonly Dictionary<string, Dictionary<string, int>> _powerCache = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, int> _spellPowers = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _powerScan;
    private bool _powerScanReady;
    private bool _spellPowerScanning;
    private double _spellPowerProgress;
    private string _spellPowerTip = "Reading the character log";
    private string _suggestionLevelText = "50";
    private int _suggestionLevel = SpellCatalog.MaxSearchLevel;
    private bool _suggestionLevelEdited;
    private bool _syncingLevel;
    private string _advisorKey = string.Empty;
    private int _advisorGeneration;
    private string _suggestionNote = string.Empty;
    private bool _hasGemSuggestions;
    private IReadOnlyList<SpellScore> _advisorPool = [];
    private IReadOnlyList<SpellSetDraft> _advisorDrafts = [];
    private AbilityCatalog? _abilityCatalog;
    private string? _abilityCatalogDirectory;
    private SpellIcons? _spellIcons;
    private ItemIcons? _itemIcons;
    private string? _itemIconDirectory;
    private string? _spellIconDirectory;
    private int _loadedSpellIconSet;
    private bool _applyingSpellIcons;
    private SpellIconSetOption? _selectedSpellIconSet;
    private SpellIconScaleOption? _selectedSpellIconScale;
    private string? _spellCatalogDirectory;
    private IReadOnlyList<int> _loadoutClasses = [];
    private readonly Dictionary<string, WhoScan> _whoScans = new(StringComparer.OrdinalIgnoreCase);
    private string? _whoPath;
    private string _classSuggestionNote = string.Empty;
    private int _layoutVersion;
    private int _selectedHotbarPage = 1;

    public MainViewModel(AppSettingsStore store)
    {
        _store = store;
        _isDarkTheme = store.Current.Theme switch
        {
            "Light" => false,
            "System" => ThemeManager.IsSystemDark(),
            _ => true,
        };
        _snapSize = store.Current.SnapGrid is >= 1 and <= 512 ? store.Current.SnapGrid : 10;
        SnapOptions = BuildSnapOptions(_snapSize);
        _selectedSnap = store.Current.SnapToGrid ? SnapOptions.First(option => option.Size == _snapSize) : SnapOptions[0];
        SectionRowsView = CollectionViewSource.GetDefaultView(_sectionRows);
        SectionRowsView.Filter = FilterSectionRow;
        EditorRowsView = new ListCollectionView(_sectionRows) { Filter = FilterEditorRow };
        BarGroups = new ListCollectionView(Groups) { Filter = FilterBarGroup };
        SpellView = CollectionViewSource.GetDefaultView(SpellRows);
        SpellView.Filter = FilterSpell;
        if (SpellView is ListCollectionView spellSets)
        {
            spellSets.CustomSort = SpellSetOrder.Instance;
        }

        if (CollectionViewSource.GetDefaultView(RawRows) is ListCollectionView rawKeys)
        {
            rawKeys.CustomSort = RawKeyOrder.ByKeyAscending;
        }

        RawRows.CollectionChanged += (_, _) => SyncRaw();
        _inventoryReload.Tick += (_, _) =>
        {
            _inventoryReload.Stop();
            ReloadInventory(announce: true);
        };
        _externalReload.Tick += (_, _) =>
        {
            _externalReload.Stop();
            ApplyExternalChanges();
        };
        _logTail.Tick += (_, _) => ReadDirtyLogs();
        foreach (var scan in _store.LogScans())
        {
            _whoScans[scan.Path] = scan;
        }
        if (!string.IsNullOrWhiteSpace(store.Current.GameDirectory))
        {
            _gameDirectory = store.Current.GameDirectory;
        }

        ThemeManager.ThemeChanged += (_, _) => RefreshSocialCaptions();
        Discover();
        LoadSpellIconSelection();
        if (SpellClassChoices.Count == 0)
        {
            RebuildSpellClasses(preferLoadout: true);
        }
    }

    public ObservableCollection<ServerChoice> SourceServers { get; } = [];

    public ObservableCollection<LoadoutChoice> SourceLoadouts { get; } = [];

    public ObservableCollection<ServerChoice> DestinationServers { get; } = [];

    public ObservableCollection<LoadoutChoice> DestinationLoadouts { get; } = [];

    public ObservableCollection<HotbarRow> HotbarSlots { get; } = [];

    public ObservableCollection<int> HotbarPages { get; } = [];

    public ObservableCollection<SpellRow> SpellRows { get; } = [];

    public ObservableCollection<SpellSearchHit> SpellSearchResults { get; } = [];

    public ObservableCollection<SpellClassChoice> SpellClassChoices { get; } = [];

    public ObservableCollection<RawRow> RawRows { get; } = [];

    public ObservableCollection<ChatFilterGroup> ChatFilters { get; } = [];

    public IReadOnlyList<LayoutWindow> Windows { get; private set; } = [];

    public ICollectionView SectionRowsView { get; }

    /// <summary>Macros and Spell Loadouts, listed above the window groups.</summary>
    public ListCollectionView EditorRowsView { get; }

    public bool HasEditorRows
    {
        get => _hasEditorRows;
        private set => SetProperty(ref _hasEditorRows, value);
    }

    public SectionRow? SelectedSectionRow => _sectionRows.FirstOrDefault(row => ReferenceEquals(row.Item, _selectedSection));

    public ICollectionView SpellView { get; }

    public string[] XRefs { get; } = ["left", "center", "right"];

    public string[] YRefs { get; } = ["top", "center", "bottom"];

    public bool IsDarkTheme
    {
        get => _isDarkTheme;
        set
        {
            if (!SetProperty(ref _isDarkTheme, value))
            {
                return;
            }

            var theme = value ? "Dark" : "Light";
            _store.SetTheme(theme);
            ThemeManager.Apply(theme);
        }
    }

    public string LayoutHint =>
        "Drag on empty space to select several windows; hold Ctrl or Shift to add to the selection. Ctrl+A selects the shown windows in the selected section, or every shown window when all sections are selected. Scroll to zoom; right-drag empty space (or middle-drag) to pan. Right-click to align or stack, starting from the first one selected. A fixed window keeps its size. Press F12 for full screen.";

    public IReadOnlyList<SnapOption> SnapOptions { get; }

    public SnapOption SelectedSnap
    {
        get => _selectedSnap;
        set
        {
            if (value is null || !SetProperty(ref _selectedSnap, value))
            {
                return;
            }

            if (!value.IsOff)
            {
                _snapSize = value.Size;
            }

            Raise(nameof(SnapGridSize));
            _store.SetSnap(!value.IsOff, _snapSize);
        }
    }

    public double SnapGridSize => _selectedSnap.Size;

    private static IReadOnlyList<SnapOption> BuildSnapOptions(int saved)
    {
        var sizes = new SortedSet<int> { 1, 2, 5, 10, 20, 25, 50, saved };
        var options = new List<SnapOption> { new(0, "Off") };
        options.AddRange(sizes.Select(size => new SnapOption(size, $"{size} px")));
        return options;
    }

    public ObservableCollection<GroupToggle> Groups { get; } = [];

    /// <summary>Group buttons on the top bar. Macros is edited from the Character pane, so it has no show/hide button.</summary>
    public ListCollectionView BarGroups { get; }

    private const string InventoryGroup = "Inventory bags";

    private static readonly string[] DefaultVisibleGroups = [InventoryGroup];

    private const string SpellGemsGroup = "Spell Gems";
    private const string MiscWindowsGroup = "Misc Windows";

    public event Action? OptionsChanged;

    public event Action? SectionSelectionChanged;

    public string GameDirectory
    {
        get => _gameDirectory;
        private set => SetProperty(ref _gameDirectory, value);
    }

    public ServerChoice? SelectedSourceServer => _sourceServer;

    public LoadoutChoice? SelectedSourceLoadout => _sourceLoadout;

    public ServerChoice? SelectedDestinationServer => _destinationServer;

    public LoadoutChoice? SelectedDestinationLoadout => _destinationLoadout;

    public bool DestinationLoadoutsEnabled => _destinationServer?.AllServers != true;

    public bool SelectionLocked => _selectionDepth > 0;

    private DiscoveredLoadout? SourceLoadout => _sourceLoadout?.Loadout;

    private DiscoveredLoadout? DestinationLoadout => _destinationLoadout?.Loadout;

    private DestinationScope CurrentDestinationScope =>
        _destinationServer?.AllServers == true
            ? DestinationScope.AllLoadouts
            : _destinationLoadout?.AllLoadouts == true
                ? DestinationScope.AllForCharacterAndServer
                : DestinationScope.Single;

    public SectionItem? SelectedSection => _selectedSection;

    public string ClassLabel
    {
        get => _classLabel;
        set
        {
            if (!SetProperty(ref _classLabel, value))
            {
                return;
            }

            if (Muted)
            {
                return;
            }

            var viewed = ViewedLoadout;
            if (viewed is null)
            {
                return;
            }

            _store.SetLabel(viewed.Id, value);
            RefreshOptionText();
            Raise(nameof(ClassSummary));
            ApplyClasses();
        }
    }

    public const int MinCharacterLevel = AppSettingsStore.MinCharacterLevel;

    public const int MaxCharacterLevel = AppSettingsStore.MaxCharacterLevel;

    /// <summary>Level and classes shown on the class button, such as "24 SHM/MAG/RNG".</summary>
    public string ClassSummary
    {
        get
        {
            var classes = _classLabel.Trim();
            var level = _characterLevelText.Trim();
            if (level.Length > 0 && classes.Length > 0)
            {
                return $"{level} {classes}";
            }

            return level.Length > 0 ? level : classes;
        }
    }

    public string CharacterLevelText
    {
        get => _characterLevelText;
        set
        {
            if (!SetProperty(ref _characterLevelText, value ?? string.Empty))
            {
                return;
            }

            Raise(nameof(ClassSummary));
            if (_syncingCharacterLevel || Muted)
            {
                return;
            }

            var viewed = ViewedLoadout;
            if (viewed is null)
            {
                return;
            }

            var text = _characterLevelText.Trim();
            if (text.Length > 0 && !TryCharacterLevel(text, out _))
            {
                return;
            }

            _store.SetLevel(viewed.Id, text.Length == 0 ? null : int.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture));
            _shownLogLevel = null;
            RefreshOptionText();
            ApplyCasterLevel();
            if (text.Length == 0)
            {
                SyncCharacterLevelText();
            }
        }
    }

    /// <summary>The newest /who for this loadout's classes once that log read is caught up, otherwise a saved level.</summary>
    private int? LevelFor(LoadoutId id)
    {
        if (WhoScanIsCurrent(id) && WhoLevel(id) is int who)
        {
            return who;
        }

        return _store.GetLevel(id) ?? WhoLevel(id);
    }

    /// <summary>True when the saved cursor has read through the end of this character's log.</summary>
    private bool WhoScanIsCurrent(LoadoutId id)
    {
        if (string.IsNullOrWhiteSpace(GameDirectory))
        {
            return false;
        }

        var path = WhoLog.PathFor(GameDirectory, id.Character, id.Server);
        return _whoScans.TryGetValue(path, out var scan) && scan.Length > 0 && !GrewPast(path, scan.Length);
    }

    private int? WhoLevel(LoadoutId id)
    {
        var classes = CharacterClasses.Parse(_store.GetLabel(id));
        if (classes.Count == 0 || string.IsNullOrWhiteSpace(GameDirectory))
        {
            return null;
        }

        var path = WhoLog.PathFor(GameDirectory, id.Character, id.Server);
        if (!_whoScans.TryGetValue(path, out var scan))
        {
            return null;
        }

        var level = WhoLog.LevelFor(scan.Sightings, classes);
        return level is >= MinCharacterLevel and <= MaxCharacterLevel ? level : null;
    }

    public static bool TryCharacterLevel(string? text, out int level)
    {
        return int.TryParse(text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out level)
            && level is >= MinCharacterLevel and <= MaxCharacterLevel;
    }

    public bool HasCharacterLevel(LoadoutId id) => LevelFor(id) is not null;

    public int? CharacterLevelOf(LoadoutId id) => LevelFor(id);

    public void SetCharacterLevel(DiscoveredLoadout loadout, int? level)
    {
        if (ViewedLoadout?.Id == loadout.Id)
        {
            var text = level is int known ? known.ToString(CultureInfo.InvariantCulture) : string.Empty;
            if (string.Equals(_characterLevelText, text, StringComparison.Ordinal))
            {
                _store.SetLevel(loadout.Id, level);
                _shownLogLevel = null;
                RefreshOptionText();
                ApplyCasterLevel();
                return;
            }

            CharacterLevelText = text;
            return;
        }

        _store.SetLevel(loadout.Id, level);
        RefreshOptionText();
    }

    private bool EnsureSpellLevel()
    {
        if (ViewedLoadout is not { } loadout)
        {
            return false;
        }

        RememberWhoScan(loadout);
        SyncCharacterLevelText();
        RefreshOptionText();
        if (HasCharacterLevel(loadout.Id))
        {
            return true;
        }

        if (ConfirmCharacterLevel?.Invoke() == true && HasCharacterLevel(loadout.Id))
        {
            return true;
        }

        Status = HasThreeClasses(loadout.Id)
            ? "Enter this loadout's level before opening spell loadouts."
            : "Choose 3 classes and a level for this loadout before opening spell loadouts.";
        return false;
    }

    private void RememberWhoScan(DiscoveredLoadout loadout)
    {
        if (string.IsNullOrWhiteSpace(GameDirectory))
        {
            return;
        }

        var path = WhoLog.PathFor(GameDirectory, loadout.Id.Character, loadout.Id.Server);
        if (_whoScans.ContainsKey(path))
        {
            return;
        }

        var scan = WhoLog.Scan(path, loadout.Id.Character);
        _whoScans[path] = scan;
        if (scan.Length > 0)
        {
            _store.SaveLogCursor(scan);
        }
    }

    private void SyncCharacterLevelText()
    {
        if (ViewedLoadout is { } viewed && WhoScanIsCurrent(viewed.Id) && WhoLevel(viewed.Id) is int logLevel)
        {
            if (_store.GetLevel(viewed.Id) != logLevel)
            {
                _store.SetLevel(viewed.Id, logLevel);
            }

            _shownLogLevel = null;
            SetLevelText(logLevel.ToString(CultureInfo.InvariantCulture));
            return;
        }

        if (ViewedLoadout is { } storedLoadout && _store.GetLevel(storedLoadout.Id) is int stored)
        {
            _shownLogLevel = null;
            SetLevelText(stored.ToString(CultureInfo.InvariantCulture));
            return;
        }

        int? pending = ViewedLoadout is { } loadout ? WhoLevel(loadout.Id) : null;
        var current = _characterLevelText.Trim();
        var showingLog = _shownLogLevel?.ToString(CultureInfo.InvariantCulture);
        if (current.Length > 0 && current != showingLog)
        {
            return;
        }

        _shownLogLevel = pending;
        SetLevelText(pending?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
    }

    private void SetLevelText(string text)
    {
        _syncingCharacterLevel = true;
        CharacterLevelText = text;
        _syncingCharacterLevel = false;
        Raise(nameof(ClassSummary));
    }

    public IReadOnlyList<ClassOption> ClassOptions { get; } =
    [
        new(-1, "None"),
        .. Enumerable.Range(0, CharacterClasses.Count)
            .Select(index => new ClassOption(index, $"{CharacterClasses.Codes[index]} - {CharacterClasses.Names[index]}"))
            .OrderBy(option => CharacterClasses.Names[option.Index], StringComparer.OrdinalIgnoreCase),
    ];

    public int ClassPick1
    {
        get => ClassAt(0);
        set => PickClass(0, value);
    }

    public int ClassPick2
    {
        get => ClassAt(1);
        set => PickClass(1, value);
    }

    public int ClassPick3
    {
        get => ClassAt(2);
        set => PickClass(2, value);
    }

    public bool CanPickClass => ViewedLoadout is not null;

    public DiscoveredLoadout? EditedLoadout => ViewedLoadout;

    public bool EditedLoadoutHasThreeClasses => EditedLoadout is { } loadout && HasThreeClasses(loadout.Id);

    public bool HasThreeClasses(LoadoutId id)
        => CharacterClasses.Parse(_store.GetLabel(id)).Count == CharacterClasses.MaxPerCharacter;

    /// <summary>Shows the class dialog when macros or spell loadouts are about to change. Returns true when three classes are stored.</summary>
    public Func<bool>? ConfirmContentClasses { get; set; }

    /// <summary>Asks for the character level, and for classes when those are still missing. True when a level is stored.</summary>
    public Func<bool>? ConfirmCharacterLevel { get; set; }

    public IReadOnlyList<int> ClassesOf(LoadoutId id)
        => CharacterClasses.Parse(_store.GetLabel(id)).Take(CharacterClasses.MaxPerCharacter).ToList();

    /// <summary>
    /// Classes already stored for the loadout, with caster classes predicted from the in-use spell sets filled into the open slots.
    /// </summary>
    public IReadOnlyList<int> ClassesForPrompt(LoadoutId id)
    {
        var chosen = new List<int>();
        foreach (var index in ClassesOf(id))
        {
            if (index is >= 0 and < CharacterClasses.Count && !chosen.Contains(index))
            {
                chosen.Add(index);
            }
        }

        if (ViewedLoadout is { } viewed && viewed.Id == id)
        {
            foreach (var index in CasterClassPrediction.Predict(Spells(), SpellLoadoutsForPrediction()))
            {
                if (chosen.Count >= CharacterClasses.MaxPerCharacter)
                {
                    break;
                }

                if (!chosen.Contains(index))
                {
                    chosen.Add(index);
                }
            }
        }

        return chosen;
    }

    /// <summary>Explains which prompt slots were filled from spell gems. Empty when nothing was predicted.</summary>
    public string ClassPromptNote(LoadoutId id)
    {
        var stored = ClassesOf(id);
        var filled = ClassesForPrompt(id).Where(index => !stored.Contains(index)).ToList();
        if (filled.Count == 0)
        {
            return string.Empty;
        }

        var names = JoinClassNames(filled);
        var verb = filled.Count == 1 ? "was" : "were";
        var remaining = filled.Count + stored.Count < CharacterClasses.MaxPerCharacter
            ? " Choose the remaining classes."
            : string.Empty;
        return $"{names} {verb} filled in from the spell gems in the in-use spell sets.{remaining}";
    }

    /// <summary>Spell name, class, and count behind the preselected classes. Empty when this loadout's gems are not loaded.</summary>
    public string ClassPredictionDebug(LoadoutId id)
    {
        if (ViewedLoadout is not { } viewed || viewed.Id != id)
        {
            return "The spell gems for this loadout are not loaded.";
        }

        var report = CasterClassPrediction.Explain(Spells(), SpellLoadoutsForPrediction());
        if (report.Classes.Count == 0 && report.UnknownSpellIds.Count == 0)
        {
            return "No class was preselected. No gem is limited to one class at level 50 or below.";
        }

        var lines = new List<string>();
        foreach (var row in report.Classes)
        {
            lines.Add($"{CharacterClasses.Names[row.Class]} ({CharacterClasses.Codes[row.Class]}) — {row.Count} in the in-use spell gems");
            foreach (var gem in row.Gems)
            {
                var classes = string.Join(", ", gem.ClassesAtCap.Select(index => CharacterClasses.Codes[index]));
                var places = gem.Spots.Any(spot => spot.Set > 0)
                    ? string.Join(", ", gem.Spots.Where(spot => spot.Set > 0).Select(spot => $"set {spot.Set} gem {spot.Slot}"))
                    : "spell gem";
                var qualifier = gem.QualifiesClass ? " — only this class" : string.Empty;
                lines.Add($"  {gem.Name} — {places} — {classes}{qualifier}");
            }

            lines.Add(string.Empty);
        }

        if (report.UnknownSpellIds.Count > 0)
        {
            lines.Add("Not in the spell file, so not counted: " + string.Join(", ", report.UnknownSpellIds));
        }

        return string.Join(Environment.NewLine, lines).TrimEnd();
    }

    private static string JoinClassNames(IReadOnlyList<int> classes)
    {
        var names = classes.Select(index => CharacterClasses.Names[index]).ToList();
        return names.Count switch
        {
            1 => names[0],
            2 => $"{names[0]} and {names[1]}",
            _ => $"{string.Join(", ", names.Take(names.Count - 1))}, and {names[^1]}",
        };
    }

    private IniSection? SpellLoadoutsForPrediction()
        => PendingFor(LoadoutFileKind.Character, "SpellLoadouts")
            ?? _sourceCharacter?.Document.Find("SpellLoadouts");

    public string LoadoutCaption(DiscoveredLoadout loadout) => Describe(loadout);

    public void AssignClasses(DiscoveredLoadout loadout, IReadOnlyList<int> classes)
    {
        var label = CharacterClasses.Format(classes.Take(CharacterClasses.MaxPerCharacter));
        if (ViewedLoadout?.Id == loadout.Id)
        {
            ClassLabel = label;
            return;
        }

        _store.SetLabel(loadout.Id, label);
        RefreshOptionText();
    }

    public LoadoutChoice? FirstCompleteSourceLoadout(DiscoveredLoadout? except = null)
    {
        foreach (var choice in SourceLoadouts)
        {
            if (choice.Loadout is { } loadout
                && (except is null || loadout.Id != except.Id)
                && HasThreeClasses(loadout.Id))
            {
                return choice;
            }
        }

        return null;
    }

    public ObservableCollection<ClassSuggestion> ClassSuggestions { get; } = [];

    public string ClassSuggestionNote
    {
        get => _classSuggestionNote;
        private set => SetProperty(ref _classSuggestionNote, value);
    }

    public void ApplyClassSuggestion(ClassSuggestion suggestion)
    {
        ClassLabel = suggestion.Classes;
        if (suggestion.Level is >= MinCharacterLevel and <= MaxCharacterLevel)
        {
            CharacterLevelText = suggestion.Level.ToString(CultureInfo.InvariantCulture);
        }
    }

    public void ClearClasses()
    {
        ClassLabel = string.Empty;
    }

    /// <summary>Clears the source loadout's saved classes so Choose Classes can preselect them again.</summary>
    public void ResetViewedLoadoutClasses()
    {
        if (!IsDebugBuild || ViewedLoadout is not { } viewed)
        {
            return;
        }

        ClearClasses();
        Status = $"Cleared classes for {Describe(viewed)}.";
    }

    /// <summary>Clears every saved class label so each loadout can be preselected again.</summary>
    public void ResetAllLoadoutClasses()
    {
        if (!IsDebugBuild)
        {
            return;
        }

        _store.ClearAllLabels();
        SetProperty(ref _classLabel, string.Empty, nameof(ClassLabel));
        SyncCharacterLevelText();
        RefreshOptionText();
        ApplyClasses();
        Raise(nameof(EditedLoadoutHasThreeClasses));
        Status = "Cleared classes for every loadout.";
    }

    /// <summary>Reads /who results for the viewed character; later calls only read what the game has appended since.</summary>
    public void RefreshClassSuggestions()
    {
        var viewed = ViewedLoadout;
        if (viewed is null || string.IsNullOrWhiteSpace(GameDirectory))
        {
            _whoPath = null;
            ClassSuggestions.Clear();
            ClassSuggestionNote = string.Empty;
            ApplyCasterLevel();
            SyncCharacterLevelText();
            return;
        }

        var path = WhoLog.PathFor(GameDirectory, viewed.Id.Character, viewed.Id.Server);
        var changed = !string.Equals(_whoPath, path, StringComparison.OrdinalIgnoreCase);
        _whoPath = path;
        if (_powerScanReady)
        {
            ScanSpellPowers(force: false);
        }

        _whoScans.TryGetValue(path, out var previous);
        var behind = previous is not null && GrewPast(path, previous.Length);
        if (changed && !behind)
        {
            ShowSuggestions(previous);
        }
        else if (previous is null || behind)
        {
            ClassSuggestionNote = "Reading /who results from your log...";
        }

        if (!_logsReading.Contains(path))
        {
            _ = ReadLogTail(path);
        }
    }

    private void MarkLogDirty(string path)
    {
        _dirtyLogs.Add(path);
        if (!_logTail.IsEnabled)
        {
            _logTail.Start();
        }
    }

    private void ReadDirtyLogs()
    {
        NoteViewedLogGrowth();
        foreach (var path in _dirtyLogs.ToArray())
        {
            if (!_logsReading.Contains(path))
            {
                _ = ReadLogTail(path);
            }
        }
    }

    /// <summary>
    /// A busy log can overflow the watcher buffer, so the open character's file length is checked on the timer too.
    /// </summary>
    private void NoteViewedLogGrowth()
    {
        var path = ActiveWhoLogPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var cursor = _whoScans.TryGetValue(path, out var scan) ? scan.Length : 0;
        if (GrewPast(path, cursor))
        {
            _dirtyLogs.Add(path);
        }
    }

    private async Task ReadLogTail(string path)
    {
        if (_disposed || !_logsReading.Add(path))
        {
            return;
        }

        _dirtyLogs.Remove(path);
        var follow = false;
        var handedOff = false;
        try
        {
            if (!WhoLog.TryParseLogFileName(path, out var character, out _))
            {
                return;
            }

            _whoScans.TryGetValue(path, out var previous);
            if (!File.Exists(path))
            {
                return;
            }

            follow = true;
            var scan = await Task.Run(() => WhoLog.Scan(path, character, previous));
            void Finish()
            {
                try
                {
                    if (_disposed)
                    {
                        return;
                    }

                    _whoScans[path] = scan;
                    if (!ReferenceEquals(scan, previous))
                    {
                        _store.SaveLogCursor(scan);
                        ApplyWhoSighting(scan, previous);
                    }

                    if (string.Equals(_whoPath, path, StringComparison.OrdinalIgnoreCase))
                    {
                        ShowSuggestions(scan);
                    }
                }
                finally
                {
                    ReleaseLogRead(path, follow);
                }
            }

            handedOff = true;
            if (_logTail.Dispatcher.CheckAccess())
            {
                Finish();
            }
            else
            {
                _logTail.Dispatcher.Invoke(Finish);
            }
        }
        finally
        {
            if (!handedOff)
            {
                ReleaseLogRead(path, follow);
            }
        }
    }

    private void ReleaseLogRead(string path, bool follow)
    {
        _logsReading.Remove(path);
        if (follow && !_disposed && GrewPast(path, _whoScans.TryGetValue(path, out var cursor) ? cursor.Length : 0))
        {
            _dirtyLogs.Add(path);
        }

        if (!_disposed && (_dirtyLogs.Count > 0 || _logsReading.Count > 0) && !_logTail.IsEnabled)
        {
            _logTail.Start();
        }
    }

    private static bool GrewPast(string path, long cursor)
    {
        try
        {
            return File.Exists(path) && new FileInfo(path).Length > cursor;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void ApplyWhoSighting(WhoScan scan, WhoScan? previous)
    {
        var latest = scan.Sightings.FirstOrDefault();
        if (latest is null || SameLatest(latest, previous?.Sightings.FirstOrDefault()))
        {
            return;
        }

        if (!WhoLog.TryParseLogFileName(scan.Path, out var character, out var server))
        {
            return;
        }

        var targets = new List<DiscoveredLoadout>();
        if (NewestGameLoadout(character, server) is { } active)
        {
            targets.Add(active);
        }

        if (ViewedLoadout is { } viewed
            && string.Equals(viewed.Id.Character, character, StringComparison.OrdinalIgnoreCase)
            && string.Equals(viewed.Id.Server, server, StringComparison.OrdinalIgnoreCase)
            && targets.TrueForAll(item => item.Id != viewed.Id))
        {
            targets.Add(viewed);
        }

        if (targets.Count == 0)
        {
            RefreshOptionText();
            return;
        }

        foreach (var loadout in targets)
        {
            if (string.IsNullOrWhiteSpace(_store.GetLabel(loadout.Id)))
            {
                if (ViewedLoadout?.Id == loadout.Id)
                {
                    ClassLabel = latest.ClassText;
                }
                else
                {
                    _store.SetLabel(loadout.Id, latest.ClassText);
                }
            }

            ApplyWhoLevel(loadout, scan, latest);
        }

        RefreshOptionText();
    }

    /// <summary>The newest /who for this loadout's classes becomes its level.</summary>
    private void ApplyWhoLevel(DiscoveredLoadout loadout, WhoScan scan, WhoSighting? latest)
    {
        var classes = CharacterClasses.Parse(_store.GetLabel(loadout.Id));
        var level = classes.Count == 0 ? latest?.Level : WhoLog.LevelFor(scan.Sightings, classes);
        if (level is not (>= MinCharacterLevel and <= MaxCharacterLevel))
        {
            return;
        }

        var text = level.Value.ToString(CultureInfo.InvariantCulture);
        if (_store.GetLevel(loadout.Id) == level
            && (ViewedLoadout?.Id != loadout.Id || _characterLevelText.Trim() == text))
        {
            return;
        }

        _store.SetLevel(loadout.Id, level);
        if (ViewedLoadout?.Id == loadout.Id)
        {
            _shownLogLevel = null;
            SetLevelText(text);
            ApplyCasterLevel();
        }

        RefreshOptionText();
    }

    private static bool SameLatest(WhoSighting latest, WhoSighting? previous)
        => previous is not null
            && previous.Level == latest.Level
            && previous.LastSeen == latest.LastSeen
            && previous.ClassText == latest.ClassText;

    private DiscoveredLoadout? NewestGameLoadout(string character, string server)
    {
        var stamps = new List<LoadoutFileWrite>();
        foreach (var loadout in _catalog)
        {
            if (!string.Equals(loadout.Id.Character, character, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(loadout.Id.Server, server, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            DateTime? newest = null;
            foreach (var file in new[] { loadout.CharacterFile, loadout.UiFile })
            {
                var written = GameWriteTime(file);
                if (written is DateTime time && (newest is null || time > newest))
                {
                    newest = time;
                }
            }

            if (newest is DateTime stamp)
            {
                stamps.Add(new LoadoutFileWrite(loadout.Id, stamp));
            }
        }

        var id = ActiveLoadout.Newest(stamps);
        return id is null ? null : _catalog.FirstOrDefault(loadout => loadout.Id == id);
    }

    private DateTime? GameWriteTime(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var written = File.GetLastWriteTimeUtc(path);
            return _ownWriteStamps.TryGetValue(path, out var own) && written <= own ? null : written;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void ShowSuggestions(WhoScan? scan)
    {
        if (scan is not null && ViewedLoadout is { } viewed && WhoScanIsCurrent(viewed.Id))
        {
            ApplyWhoLevel(viewed, scan, scan.Sightings.FirstOrDefault());
        }

        ClassSuggestions.Clear();
        if (scan is null)
        {
            ClassSuggestionNote = string.Empty;
            ApplyCasterLevel();
            SyncCharacterLevelText();
            return;
        }

        foreach (var sighting in scan.Sightings)
        {
            var times = sighting.Count == 1 ? "once" : $"{sighting.Count} times";
            ClassSuggestions.Add(new ClassSuggestion(
                sighting.ClassText,
                sighting.Level,
                $"Level {sighting.Level} when last seen in /who on {sighting.LastSeen.ToString("MMM d, yyyy h:mm tt", CultureInfo.CurrentCulture)} (seen {times})"));
        }

        ClassSuggestionNote = !scan.Exists
            ? $"No log found at {Path.GetFileName(scan.Path)}. Turn on logging in game (/log on) to get suggestions."
            : ClassSuggestions.Count == 0
                ? "No /who results for this character in the log yet. Type /who in game after swapping loadouts."
                : "From /who results in your log. Each source loadout shows the level and time of the last /who for its classes.";
        ApplyCasterLevel();
        SyncCharacterLevelText();
        RefreshOptionText();
    }

    private int ClassAt(int slot)
    {
        return slot < _loadoutClasses.Count ? _loadoutClasses[slot] : -1;
    }

    private void PickClass(int slot, int value)
    {
        if (ClassAt(slot) == value)
        {
            return;
        }

        var picks = Enumerable.Range(0, CharacterClasses.MaxPerCharacter).Select(ClassAt).ToArray();
        for (var i = 0; i < picks.Length; i++)
        {
            if (i != slot && picks[i] == value)
            {
                picks[i] = -1;
            }
        }

        picks[slot] = value;
        ClassLabel = CharacterClasses.Format(picks);
    }

    private void ApplyClasses()
    {
        _loadoutClasses = CharacterClasses.Parse(_classLabel).Take(CharacterClasses.MaxPerCharacter).ToList();
        Raise(nameof(ClassPick1));
        Raise(nameof(ClassPick2));
        Raise(nameof(ClassPick3));
        Raise(nameof(CanPickClass));
        RebuildSpellClasses(preferLoadout: true);
        foreach (var row in SpellRows)
        {
            row.SetClasses(_loadoutClasses);
            row.SetCasterLevel(CasterLevel());
        }

        SyncSuggestionLevel();
        QueueGemUpgrades();
        SyncCharacterLevelText();
    }

    public string ViewedTitle
    {
        get => _viewedTitle;
        private set => SetProperty(ref _viewedTitle, value);
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public string InventoryCommand => InventoryExport.Command;

    public string BagSlotsText
    {
        get => _bagSlotsText;
        private set => SetProperty(ref _bagSlotsText, value);
    }

    public string BagSlotsHelp
    {
        get => _bagSlotsHelp;
        private set => SetProperty(ref _bagSlotsHelp, value);
    }

    public bool BagSlotsStale
    {
        get => _bagSlotsStale;
        private set => SetProperty(ref _bagSlotsStale, value);
    }

    public string InventoryReminderText
    {
        get => _inventoryReminderText;
        private set => SetProperty(ref _inventoryReminderText, value);
    }

    public bool ShowInventoryReminder
    {
        get => _store.Current.ShowInventoryReminder;
        set
        {
            if (value == _store.Current.ShowInventoryReminder)
            {
                return;
            }

            _store.SetShowInventoryReminder(value);
            Raise();
        }
    }

    public bool ShowSaveCharacterSelectWarning
    {
        get => !_store.Current.HideSaveCharacterSelectWarning;
        set
        {
            var hide = !value;
            if (hide == _store.Current.HideSaveCharacterSelectWarning)
            {
                return;
            }

            _store.SetHideSaveCharacterSelectWarning(hide);
            Raise();
        }
    }

    public bool IsDebugBuild
    {
        get
        {
#if DEBUG
            return true;
#else
            return false;
#endif
        }
    }

    public bool ShowRawKeys
    {
        get => _store.Current.ShowRawKeys;
        set
        {
            if (!IsDebugBuild || value == _store.Current.ShowRawKeys)
            {
                return;
            }

            _store.SetShowRawKeys(value);
            Raise();
            ShowRaw = value && _selectedSection is not null && EditableSection() is not null;
        }
    }

    public bool SaveModifiedCopies
    {
        get => _store.Current.SaveModifiedCopies;
        set
        {
            if (!IsDebugBuild || value == _store.Current.SaveModifiedCopies)
            {
                return;
            }

            _store.SetSaveModifiedCopies(value);
            Raise();
        }
    }

    public bool ShowHotButtons
    {
        get => _store.Current.ShowHotButtons;
        set
        {
            if (!IsDebugBuild || value == _store.Current.ShowHotButtons)
            {
                return;
            }

            _store.SetShowHotButtons(value);
            Raise();
            SectionRowsView.Refresh();
        }
    }

    /// <summary>Suggested spell sets and gem suggestions. Debug builds only, and off until the settings toggle is checked.</summary>
    public bool ShowSpellSuggestions
    {
        get => IsDebugBuild && _store.Current.ShowSpellSuggestions;
        set
        {
            if (!IsDebugBuild || value == _store.Current.ShowSpellSuggestions)
            {
                return;
            }

            _store.SetShowSpellSuggestions(value);
            Raise();
            if (value)
            {
                _advisorKey = string.Empty;
                QueueAdvisor();
                return;
            }

            _advisorGeneration++;
            _advisorKey = string.Empty;
            _advisorPool = [];
            _advisorDrafts = [];
            SuggestedSets.Clear();
            SuggestionNote = string.Empty;
            GemSuggestions.Clear();
            HasGemSuggestions = false;
            if (_selectedSuggestion is not null)
            {
                SelectedSuggestion = null;
            }
        }
    }

    public int HorizontalStackSpacing => ClampStackSpacing(_store.Current.HorizontalStackSpacing);

    public string HorizontalStackSpacingText
    {
        get => HorizontalStackSpacing.ToString(CultureInfo.InvariantCulture);
        set => SetStackSpacingText(value, _store.Current.HorizontalStackSpacing, _store.SetHorizontalStackSpacing);
    }

    public int VerticalStackSpacing => ClampStackSpacing(_store.Current.VerticalStackSpacing);

    public string VerticalStackSpacingText
    {
        get => VerticalStackSpacing.ToString(CultureInfo.InvariantCulture);
        set => SetStackSpacingText(value, _store.Current.VerticalStackSpacing, _store.SetVerticalStackSpacing);
    }

    public string SectionFilter
    {
        get => _sectionFilter;
        set
        {
            if (SetProperty(ref _sectionFilter, value))
            {
                SectionRowsView.Refresh();
            }
        }
    }

    /// <summary>The Windows find box. A hidden box keeps its text but does not filter the list.</summary>
    public bool ShowSectionFilter
    {
        get => _showSectionFilter;
        set
        {
            if (SetProperty(ref _showSectionFilter, value))
            {
                SectionRowsView.Refresh();
            }
        }
    }

    private bool SectionFilterActive => ShowSectionFilter && !string.IsNullOrWhiteSpace(SectionFilter);

    public string EditorNote
    {
        get => _editorNote;
        private set => SetProperty(ref _editorNote, value);
    }

    public string StructuredTabTitle
    {
        get => _structuredTabTitle;
        private set => SetProperty(ref _structuredTabTitle, value);
    }

    public string SaveButtonText => _unsavedFileCount switch
    {
        0 => "Save",
        1 => "Save (1 file)",
        _ => string.Create(CultureInfo.InvariantCulture, $"Save ({_unsavedFileCount} files)"),
    };

    public string CopyButtonText => CopyItems().Count is var count and > 1
        ? string.Create(CultureInfo.InvariantCulture, $"Copy {count} sections to destination")
        : "Copy section to destination";

    /// <summary>The all-destinations copy button is gone. Highlighted windows are saved instead.</summary>
    public bool ShowCopySection => !IsAll;

    public bool CanSave => HasUnsavedChanges || HighlightedWindowNames().Count > 0;

    public bool CanSaveLoadoutCopies
        => !string.IsNullOrWhiteSpace(GameDirectory) && Directory.Exists(GameDirectory) && _catalog.Any(HasCopyFile);

    public string? BackupDirectory
    {
        get
        {
            var path = _store.Current.BackupDirectory;
            return string.IsNullOrWhiteSpace(path) ? null : path;
        }
    }

    public void RememberBackupDirectory(string path) => _store.SetBackupDirectory(path);

    public int UnsavedFileCount => _unsavedFileCount;

    public bool CanUndo => _undo.Count > 1;

    public bool CanRedo => _redo.Count > 0;

    public string UndoTip => CanUndo ? $"Undo {_undo[^1].Label} (Ctrl+Z)" : "Nothing to undo";

    public string RedoTip => CanRedo ? $"Redo {_redo[^1].Label} (Ctrl+Y)" : "Nothing to redo";

    public string XRef
    {
        get => _xRef;
        set
        {
            if (!SetProperty(ref _xRef, value) || Muted)
            {
                return;
            }

            CommitPositionFields();
        }
    }

    public string YRef
    {
        get => _yRef;
        set
        {
            if (!SetProperty(ref _yRef, value) || Muted)
            {
                return;
            }

            CommitPositionFields();
        }
    }

    public string XPosText
    {
        get => _xPosText;
        set => SetProperty(ref _xPosText, value);
    }

    public string YPosText
    {
        get => _yPosText;
        set => SetProperty(ref _yPosText, value);
    }

    public string WidthText
    {
        get => _widthText;
        set => SetProperty(ref _widthText, value);
    }

    public string HeightText
    {
        get => _heightText;
        set => SetProperty(ref _heightText, value);
    }

    public bool HasResolution => _resolutionFromGame;

    public string ScreenSizeText => _resolutionFromGame
        ? string.Create(CultureInfo.CurrentCulture, $"{ScreenWidth:0} × {ScreenHeight:0}")
        : "Not found";

    public string ScreenSizeHelp => _resolutionFromGame
        ? "The game's screen resolution, read from eqclient.ini in the game folder."
        : "The screen resolution could not be read from eqclient.ini in the game folder. Click to locate the EverQuest Legends folder.";

    public double ScreenWidth { get; private set; } = 2560;

    public double ScreenHeight { get; private set; } = 1440;

    public string SelectedWindow => _layoutSelection.FirstOrDefault() ?? string.Empty;

    public string SelectedWindowList => string.Join('\n', _layoutSelection);

    public IReadOnlyList<string> LayoutSelection => _layoutSelection;

    public bool CanArrange => _resolutionFromGame && ArrangeTargets().Count >= 2;

    public bool CanCenterOnScreen => _resolutionFromGame && ArrangeTargets().Count >= 1;

    public string ArrangeHeader
    {
        get
        {
            var targets = ArrangeTargets();
            if (targets.Count == 0)
            {
                return "No windows selected";
            }

            var anchor = _sections.FirstOrDefault(item => item.Kind == LoadoutFileKind.Ui && item.Name == targets[0]);
            return string.Create(CultureInfo.InvariantCulture, $"Anchor: {anchor?.ListText ?? targets[0]} ({targets.Count} selected)");
        }
    }

    public string PlacementScopeText
    {
        get
        {
            var targets = ArrangeTargets();
            if (targets.Count < 2)
            {
                return string.Empty;
            }

            var anchor = _sections.FirstOrDefault(item => item.Kind == LoadoutFileKind.Ui && item.Name == targets[0]);
            var name = anchor?.ListText ?? targets[0];
            return string.Create(
                CultureInfo.InvariantCulture,
                $"Changing the anchor applies to all {targets.Count} selected. The percentages are for {name}.");
        }
    }

    public int LayoutVersion
    {
        get => _layoutVersion;
        private set => SetProperty(ref _layoutVersion, value);
    }

    public bool ShowMissing
    {
        get => _showMissing;
        private set => SetProperty(ref _showMissing, value);
    }

    public bool ShowSocials
    {
        get => _showSocials;
        private set => SetProperty(ref _showSocials, value);
    }

    public bool ShowHotbar
    {
        get => _showHotbar;
        private set => SetProperty(ref _showHotbar, value);
    }

    public bool ShowSpells
    {
        get => _showSpells;
        private set => SetProperty(ref _showSpells, value);
    }

    /// <summary>The layout tab stays available from macros and spell loadouts so the window layout can be opened again.</summary>
    public bool ShowLayout
    {
        get => _showLayout;
        private set => SetProperty(ref _showLayout, value);
    }

    public bool ShowPlacement
    {
        get => _showPlacement;
        private set => SetProperty(ref _showPlacement, value);
    }

    public bool ShowChatFilters
    {
        get => _showChatFilters;
        private set => SetProperty(ref _showChatFilters, value);
    }

    public bool ShowRaw
    {
        get => _showRaw;
        private set => SetProperty(ref _showRaw, value);
    }

    public bool ShowEditorUnavailable
    {
        get => _showEditorUnavailable;
        private set => SetProperty(ref _showEditorUnavailable, value);
    }

    public string EditorUnavailableMessage
    {
        get => _editorUnavailableMessage;
        private set => SetProperty(ref _editorUnavailableMessage, value);
    }

    public bool ShowStructured => ShowSocials || ShowHotbar || ShowSpells;

    public bool CanResize
    {
        get => _canResize;
        private set
        {
            if (SetProperty(ref _canResize, value))
            {
                Raise(nameof(LayoutHint));
            }
        }
    }

    public bool CanCopy
    {
        get => _canCopy;
        private set => SetProperty(ref _canCopy, value);
    }

    public bool HasUnsavedChanges
    {
        get => _hasUnsavedChanges;
        private set => SetProperty(ref _hasUnsavedChanges, value);
    }

    public bool ShowAllSpells
    {
        get => _showAllSpells;
        set
        {
            if (SetProperty(ref _showAllSpells, value))
            {
                SpellView.Refresh();
                if (_selectedSpellSet is null || !FilterSpell(_selectedSpellSet))
                {
                    SelectedSpellSet = SpellView.Cast<SpellRow>().FirstOrDefault();
                }
            }
        }
    }

    public SpellRow? SelectedSpellSet
    {
        get => _selectedSpellSet;
        set
        {
            if (!SetProperty(ref _selectedSpellSet, value))
            {
                return;
            }

            Raise(nameof(CanDeleteSpellSet));
            if (value is not null && _selectedSuggestion is not null)
            {
                _selectedSuggestion = null;
                _selectedSuggestionGem = null;
                Raise(nameof(SelectedSuggestion));
                Raise(nameof(ShowingSuggestion));
            }

            if (_selectedGem is not null && (value is null || !value.Gems.Contains(_selectedGem)))
            {
                SelectedGem = null;
            }

            RaiseGemPage();
            RebuildGemSuggestions();
        }
    }

    public ObservableCollection<SuggestedSpellPage> SuggestedSets { get; } = [];

    public SuggestedSpellPage? SelectedSuggestion
    {
        get => _selectedSuggestion;
        set
        {
            if (ReferenceEquals(_selectedSuggestion, value))
            {
                return;
            }

            _selectedSuggestion = value;
            _selectedSuggestionGem = null;
            Raise(nameof(SelectedSuggestion));
            Raise(nameof(ShowingSuggestion));
            if (value is not null && _selectedSpellSet is not null)
            {
                _selectedSpellSet = null;
                Raise(nameof(SelectedSpellSet));
                Raise(nameof(CanDeleteSpellSet));
                if (_selectedGem is not null)
                {
                    SelectedGem = null;
                }
            }

            if (value is not null)
            {
                GemSuggestions.Clear();
                HasGemSuggestions = false;
            }
            else
            {
                RebuildGemSuggestions();
            }

            RaiseGemPage();
        }
    }

    public bool ShowingSuggestion => _selectedSuggestion is not null;

    public System.Collections.IEnumerable ShownGems
    {
        get
        {
            if (_selectedSuggestion is { } suggestion)
            {
                return suggestion.Gems;
            }

            if (_selectedSpellSet is { } set)
            {
                return set.Gems;
            }

            return Array.Empty<object>();
        }
    }

    public string ShownGemTitle => _selectedSuggestion?.Name ?? _selectedSpellSet?.Name ?? string.Empty;

    public object? SelectedShownGem
    {
        get => _selectedSuggestion is not null ? _selectedSuggestionGem : _selectedGem;
        set
        {
            if (value is SuggestedGemFace face)
            {
                if (ReferenceEquals(_selectedSuggestionGem, face))
                {
                    return;
                }

                _selectedSuggestionGem = face;
                Raise(nameof(SelectedShownGem));
                Raise(nameof(SelectedSpellDetail));
                Raise(nameof(DisplayedSpellDetail));
                return;
            }

            if (value is SpellGem gem)
            {
                if (ReferenceEquals(_selectedGem, gem) && _selectedSuggestionGem is null)
                {
                    return;
                }

                _selectedSuggestionGem = null;
                SelectedGem = gem;
                return;
            }

            if (value is null && _selectedSuggestionGem is null && _selectedGem is null)
            {
                return;
            }

            if (_selectedSuggestion is not null)
            {
                _selectedSuggestionGem = null;
            }
            else if (_selectedGem is not null)
            {
                SelectedGem = null;
            }

            Raise(nameof(SelectedShownGem));
            Raise(nameof(SelectedSpellDetail));
            Raise(nameof(DisplayedSpellDetail));
        }
    }

    public string SuggestionLevelText
    {
        get => _suggestionLevelText;
        set
        {
            if (!SetProperty(ref _suggestionLevelText, value ?? string.Empty))
            {
                return;
            }

            if (_syncingLevel)
            {
                return;
            }

            _suggestionLevelEdited = true;
            if (int.TryParse(_suggestionLevelText.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var level) && level is >= 1 and <= 125)
            {
                _suggestionLevel = level;
                QueueAdvisor();
            }
        }
    }

    public string SuggestionNote
    {
        get => _suggestionNote;
        private set => SetProperty(ref _suggestionNote, value);
    }

    public ObservableCollection<GemSuggestion> GemSuggestions { get; } = [];

    public bool HasGemSuggestions
    {
        get => _hasGemSuggestions;
        private set => SetProperty(ref _hasGemSuggestions, value);
    }

    public bool SpellPowerScanning
    {
        get => _spellPowerScanning;
        private set => SetProperty(ref _spellPowerScanning, value);
    }

    public double SpellPowerProgress
    {
        get => _spellPowerProgress;
        private set => SetProperty(ref _spellPowerProgress, value);
    }

    public string SpellPowerTip
    {
        get => _spellPowerTip;
        private set => SetProperty(ref _spellPowerTip, value);
    }

    public SpellGem? SelectedGem
    {
        get => _selectedGem;
        set
        {
            if (ReferenceEquals(_selectedGem, value))
            {
                return;
            }

            if (_selectedGem is not null)
            {
                _selectedGem.PropertyChanged -= SelectedGemChanged;
            }

            _selectedGem = value;
            if (_selectedGem is not null)
            {
                _selectedSuggestionGem = null;
                _selectedGem.PropertyChanged += SelectedGemChanged;
            }

            Raise(nameof(SelectedGem));
            Raise(nameof(SelectedShownGem));
            if (value is not null && !_selectingSpell)
            {
                _selectingSpell = true;
                SelectedSpellHit = null;
                _selectingSpell = false;
            }

            Raise(nameof(SelectedSpellDetail));
            Raise(nameof(DisplayedSpellDetail));
            Raise(nameof(DisplayedSpellUpgrade));
        }
    }

    public SpellSearchHit? SelectedSpellHit
    {
        get => _selectedSpellHit;
        set
        {
            if (ReferenceEquals(_selectedSpellHit, value))
            {
                return;
            }

            _selectedSpellHit = value;
            Raise(nameof(SelectedSpellHit));
            if (value is not null && !_selectingSpell)
            {
                _selectingSpell = true;
                _selectedSuggestionGem = null;
                SelectedGem = null;
                _selectingSpell = false;
            }

            Raise(nameof(SelectedSpellDetail));
            Raise(nameof(DisplayedSpellDetail));
        }
    }

    /// <summary>The one selected spell, from the gems list or the find list. Empty when nothing, or an empty gem, is selected.</summary>
    public ISpellDetails? SelectedSpellDetail => _selectedSuggestionGem is { IsEmpty: false } face
        ? face
        : _selectedGem is { IsEmpty: false } gem ? gem : _selectedSpellHit;

    /// <summary>The gem or search panel under the pointer. Empty when the pointer is not over one. Hover does not change the selection.</summary>
    public ISpellDetails? HoveredSpellDetail => _hoveredSpellDetail;

    /// <summary>The spell under the pointer, or the selected spell when the pointer is not over a panel.</summary>
    public ISpellDetails? DisplayedSpellDetail => _hoveredSpellDetail ?? SelectedSpellDetail;

    /// <summary>The upgrade for the selected gem. The capsule under the pointer replaces it while the pointer is there.</summary>
    public ISpellDetails? DisplayedSpellUpgrade => _hoveredUpgrade ?? _selectedGem?.Upgrade;

    public void HoverSpell(ISpellDetails spell)
    {
        if (ReferenceEquals(_hoveredSpellDetail, spell))
        {
            return;
        }

        _hoveredSpellDetail = spell;
        Raise(nameof(HoveredSpellDetail));
        Raise(nameof(DisplayedSpellDetail));
    }

    public void ClearSpellHover(ISpellDetails spell)
    {
        if (!ReferenceEquals(_hoveredSpellDetail, spell))
        {
            return;
        }

        _hoveredSpellDetail = null;
        Raise(nameof(HoveredSpellDetail));
        Raise(nameof(DisplayedSpellDetail));
    }

    public void HoverUpgrade(ISpellDetails spell)
    {
        if (ReferenceEquals(_hoveredUpgrade, spell))
        {
            return;
        }

        _hoveredUpgrade = spell;
        Raise(nameof(DisplayedSpellUpgrade));
    }

    public void ClearUpgradeHover(ISpellDetails spell)
    {
        if (!ReferenceEquals(_hoveredUpgrade, spell))
        {
            return;
        }

        _hoveredUpgrade = null;
        Raise(nameof(DisplayedSpellUpgrade));
    }

    public bool CanDeleteSpellSet => _selectedSpellSet is not null;

    public void DeleteSelectedSpellSet()
    {
        if (SpellSetsStayOnTheEditedLoadout() || !EnsureContentClasses())
        {
            return;
        }

        var row = _selectedSpellSet;
        var section = EditableSection();
        if (row is null || section is null || section.Name != "SpellLoadouts")
        {
            return;
        }

        var index = row.Index;
        SpellGems.RemoveSet(section, index);
        SpellRows.Remove(row);
        SpellView.Refresh();
        var visible = SpellView.Cast<SpellRow>().ToList();
        SelectedSpellSet = visible.FirstOrDefault(set => set.Index > index) ?? visible.LastOrDefault();
        if (!MarkEdited(string.Create(CultureInfo.InvariantCulture, $"delete spell set {index}")))
        {
            return;
        }
    }

    /// <summary>Lists a new empty spell set at the next free index. Returns false when every index is taken.</summary>
    public bool AddEmptySpellSet()
    {
        if (SpellSetsStayOnTheEditedLoadout() || !EnsureContentClasses())
        {
            return false;
        }

        var section = EditableSection();
        if (section is null || section.Name != "SpellLoadouts")
        {
            return false;
        }

        var index = SpellWindows.NextFreeIndex(SpellGems.Read(section));
        if (index == 0)
        {
            Status = "All 60 spell sets are already listed, named, or have gems.";
            return false;
        }

        var created = new SpellGemSet(index) { InUse = "1" };
        SpellGems.WriteSet(section, created);
        var row = SpellRows.FirstOrDefault(set => set.Index == index);
        if (row is null)
        {
            row = new SpellRow(index, section, SpellRowEdited, Spells(), Icons(), _loadoutClasses, EnsureContentClasses);
            row.UsePowerNames(PowerName, PowerRank);
            row.Load(created);
            var insertAt = 0;
            while (insertAt < SpellRows.Count && SpellRows[insertAt].Index < index)
            {
                insertAt++;
            }

            SpellRows.Insert(insertAt, row);
        }
        else
        {
            row.Load(created);
        }

        SpellView.Refresh();
        SelectedSpellSet = row;
        if (!MarkEdited(string.Create(CultureInfo.InvariantCulture, $"add spell set {index}")))
        {
            return false;
        }

        Status = string.Create(CultureInfo.InvariantCulture, $"Added spell set {index}.");
        return true;
    }

    public string SpellListNote
    {
        get => _spellListNote;
        private set => SetProperty(ref _spellListNote, value);
    }

    public string SpellSearchText
    {
        get => _spellSearchText;
        set
        {
            if (SetProperty(ref _spellSearchText, value))
            {
                RefreshSpellSearch();
            }
        }
    }

    public string SpellSearchNote
    {
        get => _spellSearchNote;
        private set => SetProperty(ref _spellSearchNote, value);
    }

    public string SpellSearchPlaceholder => "Name or level";

    public bool SpellSortByLevel
    {
        get => _spellSortByLevel;
        private set
        {
            if (SetProperty(ref _spellSortByLevel, value))
            {
                Raise(nameof(SpellNameSortActive));
                Raise(nameof(SpellLevelSortTip));
                Raise(nameof(SpellNameSortTip));
                RefreshSpellSearch();
            }
        }
    }

    public bool SpellNameSortActive => !_spellSortByLevel;

    public bool SpellLevelDescending
    {
        get => _spellLevelDescending;
        set
        {
            if (SetProperty(ref _spellLevelDescending, value))
            {
                Raise(nameof(SpellLevelSortText));
                Raise(nameof(SpellLevelSortTip));
                RefreshSpellSearch();
            }
        }
    }

    public bool SpellNameAscending
    {
        get => _spellNameAscending;
        set
        {
            if (SetProperty(ref _spellNameAscending, value))
            {
                Raise(nameof(SpellNameSortText));
                Raise(nameof(SpellNameSortTip));
                RefreshSpellSearch();
            }
        }
    }

    public void ActivateSpellLevelSort()
    {
        if (_spellSortByLevel)
        {
            SpellLevelDescending = !_spellLevelDescending;
            return;
        }

        SpellSortByLevel = true;
    }

    public void ActivateSpellNameSort()
    {
        if (!_spellSortByLevel)
        {
            SpellNameAscending = !_spellNameAscending;
            return;
        }

        SpellSortByLevel = false;
    }

    public string SpellLevelSortText => _spellLevelDescending ? "Level 9–1" : "Level 1–9";

    public string SpellLevelSortTip => _spellSortByLevel
        ? _spellLevelDescending ? "Sorting by level, high to low" : "Sorting by level, low to high"
        : _spellLevelDescending ? "Then by level, high to low" : "Then by level, low to high";

    public string SpellNameSortText => _spellNameAscending ? "A–Z" : "Z–A";

    public string SpellNameSortTip => _spellSortByLevel
        ? _spellNameAscending ? "Then by name, A to Z" : "Then by name, Z to A"
        : _spellNameAscending ? "Sorting by name, A to Z" : "Sorting by name, Z to A";

    public IReadOnlyList<SpellIconSetOption> SpellIconSets { get; } =
    [
        new(0, "Default"),
        new(1, "Alternate 1"),
        new(2, "Alternate 2"),
        new(3, "Alternate 3"),
    ];

    public IReadOnlyList<SpellIconScaleOption> SpellIconScales { get; } =
    [
        new(0.5, "x0.5"),
        new(1, "x1"),
        new(1.5, "x1.5"),
        new(2, "x2"),
        new(2.5, "x2.5"),
        new(3, "x3"),
        new(3.5, "x3.5"),
        new(4, "x4"),
    ];

    public SpellIconSetOption? SelectedSpellIconSet
    {
        get => _selectedSpellIconSet;
        set
        {
            if (_applyingSpellIcons)
            {
                SetProperty(ref _selectedSpellIconSet, value);
                return;
            }

            // The combo box writes this while it is still changing the selection. Notifying it
            // immediately makes every choice after the first stick on the first value.
            if (value is null || value.Number == _selectedSpellIconSet?.Number)
            {
                return;
            }

            _selectedSpellIconSet = value;
            var number = value.Number;
            Dispatcher.CurrentDispatcher.BeginInvoke(() => ApplySpellIconSet(number));
        }
    }

    public SpellIconScaleOption? SelectedSpellIconScale
    {
        get => _selectedSpellIconScale;
        set
        {
            if (!SetProperty(ref _selectedSpellIconScale, value) || value is null || _applyingSpellIcons)
            {
                return;
            }

            _store.Current.SpellIconScale = value.Scale;
            _store.Save();
            Raise(nameof(SpellIconPixels));
        }
    }

    private const double DefaultSpellIconScale = 1.5;

    private const double SpellSidePanelWidth = 360;

    private const double SpellSidePanelCollapsedWidth = 36;

    public int SpellIconPixels => (int)Math.Round((_selectedSpellIconScale?.Scale ?? DefaultSpellIconScale) * SpellIconLayout.Size);

    public bool SpellSearchExpanded
    {
        get => _store.Current.SpellSearchExpanded;
        set => SetSpellPanel(value, () => _store.Current.SpellSearchExpanded, expanded => _store.Current.SpellSearchExpanded = expanded, nameof(SpellSearchExpanded), nameof(SpellSearchPanelWidth), nameof(SpellSearchChevron), nameof(SpellSearchExpandLabel));
    }

    public double SpellSearchPanelWidth => SpellSearchExpanded ? SpellSidePanelWidth : SpellSidePanelCollapsedWidth;

    public string SpellSearchChevron => SpellSearchExpanded ? "\uE70D" : "\uE76C";

    public string SpellSearchExpandLabel => SpellSearchExpanded ? "Collapse Find a spell" : "Show Find a spell";

    public bool SpellDetailsExpanded
    {
        get => _store.Current.SpellDetailsExpanded;
        set => SetSpellPanel(value, () => _store.Current.SpellDetailsExpanded, expanded => _store.Current.SpellDetailsExpanded = expanded, nameof(SpellDetailsExpanded), nameof(SpellDetailsPanelWidth), nameof(SpellDetailsChevron), nameof(SpellDetailsExpandLabel));
    }

    public double SpellDetailsPanelWidth => SpellDetailsExpanded ? SpellSidePanelWidth : SpellSidePanelCollapsedWidth;

    public string SpellDetailsChevron => SpellDetailsExpanded ? "\uE70D" : "\uE76C";

    public string SpellDetailsExpandLabel => SpellDetailsExpanded ? "Collapse Details" : "Show Details";

    public bool SpellUpgradeExpanded
    {
        get => _store.Current.SpellUpgradeExpanded;
        set => SetSpellPanel(value, () => _store.Current.SpellUpgradeExpanded, expanded => _store.Current.SpellUpgradeExpanded = expanded, nameof(SpellUpgradeExpanded), nameof(SpellUpgradePanelWidth), nameof(SpellUpgradeChevron), nameof(SpellUpgradeExpandLabel));
    }

    public double SpellUpgradePanelWidth => SpellUpgradeExpanded ? SpellSidePanelWidth : SpellSidePanelCollapsedWidth;

    public string SpellUpgradeChevron => SpellUpgradeExpanded ? "\uE70D" : "\uE76C";

    public string SpellUpgradeExpandLabel => SpellUpgradeExpanded ? "Collapse Spell Upgrade" : "Show Spell Upgrade";

    private void SetSpellPanel(bool value, Func<bool> current, Action<bool> assign, params string[] names)
    {
        if (value == current())
        {
            return;
        }

        assign(value);
        _store.Save();
        foreach (var name in names)
        {
            Raise(name);
        }
    }

    public SpellClassChoice? SpellClassChoice
    {
        get => _spellClassChoice;
        set
        {
            if (SetProperty(ref _spellClassChoice, value) && !_rebuildingSpellClasses)
            {
                RefreshSpellSearch();
            }
        }
    }

    public int SelectedHotbarPage
    {
        get => _selectedHotbarPage;
        set
        {
            if (!SetProperty(ref _selectedHotbarPage, value))
            {
                return;
            }

            FillHotbarPage();
        }
    }

    public HotbarRow? SelectedHotbar { get; set; }

    public RawRow? SelectedRaw { get; set; }

    public bool HasSelection => _selectedSection is not null;

    public bool IsAll => CurrentDestinationScope != DestinationScope.Single;

    public bool TrySetGameDirectory(string path)
    {
        GameDirectory = path;
        _store.Current.GameDirectory = path;
        _store.Save();
        Discover();
        LoadSpellIconSelection();
        ReloadSpellIcons();
        return true;
    }

    public bool TrySetSourceServer(ServerChoice? choice)
    {
        if (choice is null || choice.AllServers)
        {
            return false;
        }

        if (SameServer(choice, _sourceServer))
        {
            return true;
        }

        var number = _sourceLoadout?.Loadout?.Id.Number;
        var refreshCount = IsDiscoveryStatus(Status);
        ApplySelection(() =>
        {
            _sourceServer = choice;
            FillSourceLoadouts();
            _sourceLoadout = SourceLoadouts.FirstOrDefault(item => item.Loadout?.Id.Number == number)
                ?? SourceLoadouts.FirstOrDefault();
            RememberSource();
            if (_store.Current.DestinationServerChosen != true
                && DestinationServers.FirstOrDefault(item => SameServer(item, choice)) is { } follow)
            {
                SwitchDestinationServer(follow);
            }
        });
        if (refreshCount)
        {
            ReportFoundLoadouts();
        }

        return true;
    }

    public bool TrySetSourceLoadout(LoadoutChoice? choice)
    {
        if (choice?.Loadout is null)
        {
            return false;
        }

        if (ReferenceEquals(choice, _sourceLoadout))
        {
            return true;
        }

        ApplySelection(() =>
        {
            _sourceLoadout = choice;
            RememberSource();
        });
        return true;
    }

    public bool TrySetDestinationServer(ServerChoice? choice)
    {
        if (choice is null)
        {
            return false;
        }

        if (SameServer(choice, _destinationServer))
        {
            return true;
        }

        _store.SetDestinationServerChosen(!SameServer(choice, _sourceServer));
        ApplySelection(() => SwitchDestinationServer(choice));
        return true;
    }

    private void SwitchDestinationServer(ServerChoice choice)
    {
        if (SameServer(choice, _destinationServer))
        {
            return;
        }

        var keepAll = _destinationLoadout?.AllLoadouts == true || choice.AllServers;
        var number = _destinationLoadout?.Loadout?.Id.Number;
        _destinationServer = choice;
        FillDestinationLoadouts();
        _destinationLoadout = keepAll
            ? DestinationLoadouts.FirstOrDefault(item => item.AllLoadouts)
            : DestinationLoadouts.FirstOrDefault(item => item.Loadout?.Id.Number == number)
                ?? DestinationLoadouts.FirstOrDefault(item => item.Loadout is not null);
        _destinationLoadout ??= DestinationLoadouts.FirstOrDefault();
        RememberDestination();
    }

    public bool TrySetDestinationLoadout(LoadoutChoice? choice)
    {
        if (choice is null || _destinationServer?.AllServers == true)
        {
            return choice is not null;
        }

        if (ReferenceEquals(choice, _destinationLoadout))
        {
            return true;
        }

        ApplySelection(() =>
        {
            _destinationLoadout = choice;
            RememberDestination();
        });
        return true;
    }

    public bool TrySelectSection(SectionItem? item)
    {
        if (item is { IsBlocked: true } && !IsEditorPick(item))
        {
            Status = item.BlockReason;
            return false;
        }

        if (item?.Name == "SpellLoadouts" && _suppressClassPrompt == 0 && !EnsureSpellLevel())
        {
            return false;
        }

        if (ReferenceEquals(item, _selectedSection))
        {
            ReplaceLayoutSelection(item);
            SectionSelectionChanged?.Invoke();
            return true;
        }

        SelectSection(item);
        return true;
    }

    public void CommitPositionFields()
    {
        var section = EditableSection();
        if (section is null)
        {
            return;
        }

        var oldX = section.Get("XRef") ?? "left";
        var oldY = section.Get("YRef") ?? "top";
        var anchorChanged = !oldX.Equals(XRef, StringComparison.OrdinalIgnoreCase)
            || !oldY.Equals(YRef, StringComparison.OrdinalIgnoreCase);
        if (anchorChanged)
        {
            var xChanged = !oldX.Equals(XRef, StringComparison.OrdinalIgnoreCase);
            var yChanged = !oldY.Equals(YRef, StringComparison.OrdinalIgnoreCase);
            var names = ArrangeTargets();
            if (!names.Contains(section.Name, StringComparer.Ordinal))
            {
                names.Insert(0, section.Name);
            }

            var wrote = false;
            foreach (var name in names)
            {
                var target = string.Equals(name, section.Name, StringComparison.Ordinal)
                    ? section
                    : MutableLayoutSection(name);
                if (target is null)
                {
                    continue;
                }

                var current = ReadRect(target);
                if (xChanged)
                {
                    target.Set("XRef", XRef);
                }

                if (yChanged)
                {
                    target.Set("YRef", YRef);
                }

                WriteLayoutRect(target, current, WindowPlacement.HasStoredSize(target));
                wrote = true;
            }

            if (!wrote)
            {
                return;
            }

            MarkEdited(names.Count == 1 ? $"position of {section.Name}" : $"anchor of {names.Count} windows");
            LoadPositionFields(section);
            RebuildCanvas();
            return;
        }
        else
        {
            var changed = SetPercent(section, "XPos", XPosText, section.Get("XRef") ?? XRef, ScreenWidth);
            changed |= SetPercent(section, "YPos", YPosText, section.Get("YRef") ?? YRef, ScreenHeight);
            if (CanResize)
            {
                var widthChanged = SetSize(section, "Width", WidthText);
                var heightChanged = SetSize(section, "Height", HeightText);
                changed |= widthChanged || heightChanged;
                if ((widthChanged || heightChanged) && _inventory?.Find(section.Name) is { } bag)
                {
                    var size = ReadRect(section);
                    var grid = widthChanged ? BagGrid.FromWidth(bag.Slots, size.Width) : BagGrid.FromHeight(bag.Slots, size.Height);
                    SetSize(section, "Width", grid.Width.ToString(CultureInfo.InvariantCulture));
                    SetSize(section, "Height", grid.Height.ToString(CultureInfo.InvariantCulture));
                }
            }

            if (!changed)
            {
                return;
            }
        }

        MarkEdited($"position of {section.Name}");
        LoadPositionFields(section);
        RebuildCanvas();
    }

    public void SelectCanvasWindow(string name, bool extend)
    {
        var item = _sections.FirstOrDefault(section => section.Name == name && section.Kind == LoadoutFileKind.Ui);
        if (item is null)
        {
            return;
        }

        if (!extend)
        {
            TrySelectSection(item);
            return;
        }

        var names = _layoutSelection.ToList();
        if (!names.Remove(name))
        {
            names.Add(name);
        }

        ApplyLayoutOrder(names);
    }

    public void SelectCanvasWindows(IReadOnlyList<string> names, bool extend)
    {
        var chosen = extend ? _layoutSelection.ToList() : [];
        foreach (var name in names)
        {
            if (!chosen.Contains(name, StringComparer.Ordinal)
                && _sections.Any(section => section.Kind == LoadoutFileKind.Ui && section.Name == name))
            {
                chosen.Add(name);
            }
        }

        ApplyLayoutOrder(chosen);
    }

    public void ExtendLayoutSelection(IReadOnlyList<SectionItem> selected)
    {
        var chosen = selected.Where(item => item.Kind == LoadoutFileKind.Ui).Select(item => item.Name).ToList();
        var names = new List<string>();
        foreach (var existing in _layoutSelection)
        {
            if (chosen.Contains(existing, StringComparer.Ordinal))
            {
                names.Add(existing);
            }
        }

        foreach (var name in chosen)
        {
            if (!names.Contains(name, StringComparer.Ordinal))
            {
                names.Add(name);
            }
        }

        ApplyLayoutOrder(names);
    }

    public void SelectGroupWindows(GroupToggle group) => SelectGroupWindows(group.Name);

    /// <summary>
    /// Ctrl+A. One selected section contributes its shown windows.
    /// When every section is selected, every shown window is highlighted.
    /// </summary>
    public void SelectShownWindows()
    {
        var sections = Groups
            .Where(group => _sections.Any(item => item.Group == group.Name && IsLayoutWindow(item)))
            .ToList();
        var allSectionsSelected = sections.Count > 0 && sections.All(group => group.CheckState == true);
        var selectedSection = allSectionsSelected ? null : GroupOfLayoutSelection();
        if (!allSectionsSelected && selectedSection is null)
        {
            return;
        }

        var windows = _sections
            .Where(item => IsLayoutWindow(item))
            .OrderBy(item => item.GroupOrder)
            .ThenBy(item => SectionListRank(item.Group, item.Name))
            .ThenBy(item => item.SortName, StringComparer.Ordinal)
            .Select(item => new ShownWindow(item.Group, item.Name, item.IsShown && Windows.Any(window => window.Name == item.Name)))
            .ToList();
        var names = WindowSelectAll.Names(windows, allSectionsSelected, selectedSection).ToList();
        if (names.Count == 0)
        {
            return;
        }

        ApplyLayoutOrder(names);
    }

    private string? GroupOfLayoutSelection()
    {
        var name = _layoutSelection.FirstOrDefault();
        if (name is null && _selectedSection is { } selected && IsLayoutWindow(selected))
        {
            name = selected.Name;
        }
        return name is null
            ? null
            : _sections.FirstOrDefault(item => item.Kind == LoadoutFileKind.Ui && item.Name == name)?.Group;
    }

    public void SelectGroupWindows(string groupName)
    {
        var blocked = _sections.Where(item => item.Group == groupName && item.IsBlocked).ToList();
        if (blocked.Count > 0 && _sections.All(item => item.Group != groupName || item.IsBlocked))
        {
            Status = blocked[0].BlockReason;
            return;
        }

        ExpandSectionGroup(groupName);
        if (!SelectGroupLayout(groupName, save: true))
        {
            TrySelectSection(_sections
                .Where(item => item.Group == groupName)
                .OrderBy(item => item.SortName, StringComparer.Ordinal)
                .FirstOrDefault());
        }
    }

    public void Arrange(ArrangeAction action)
    {
        if (!_resolutionFromGame)
        {
            Status = "The screen resolution is unknown. Locate the EverQuest Legends folder from Settings first.";
            return;
        }

        var names = ArrangeTargets();
        var onScreen = action is ArrangeAction.CenterOnScreenHorizontally or ArrangeAction.CenterOnScreenVertically;
        var matchesSize = action is ArrangeAction.MatchWidth or ArrangeAction.MatchHeight;
        if (names.Count < (onScreen ? 1 : 2))
        {
            Status = onScreen
                ? "Select a window to center it on the screen."
                : matchesSize
                    ? "Select two or more windows. Size starts from the first one you selected."
                    : "Select two or more windows. Align and Stack start from the first one you selected.";
            return;
        }

        if (matchesSize)
        {
            MatchAnchorSize(action, names);
            return;
        }

        var framed = PlacesPaintedFrame(action);
        if (StacksHorizontally(action) || StacksVertically(action))
        {
            var current = names.Select(name => framed ? PaintedRect(name) : LayoutRect(name)).ToList();
            names = LayoutArranger.StackOrder(current, action).Select(index => names[index]).ToList();
        }

        var arranged = LayoutArranger.Arrange(
            names.Select(name => framed ? PaintedRect(name) : LayoutRect(name)).ToList(),
            action,
            ScreenWidth,
            ScreenHeight,
            StacksHorizontally(action) ? HorizontalStackSpacing : 0,
            StacksVertically(action) ? VerticalStackSpacing : 0);
        if (framed)
        {
            arranged = names.Zip(arranged, (name, rect) => StoredRect(name, rect)).ToList();
        }
        var paired = onScreen ? names.Zip(arranged) : names.Skip(1).Zip(arranged.Skip(1));
        var edits = paired.Select(pair => (pair.First, pair.Second)).ToList();
        var offScreen = edits.Count(edit => LayoutArranger.IsOffScreen(edit.Second, ScreenWidth, ScreenHeight));
        var verb = action is ArrangeAction.StackRight or ArrangeAction.StackLeft or ArrangeAction.StackDown or ArrangeAction.StackUp
            ? "Stacked"
            : onScreen ? "Centered" : "Aligned";
        if (!ApplyCanvasEdits(edits, resized: false, $"{verb.ToLowerInvariant()} {edits.Count} windows"))
        {
            return;
        }

        if (onScreen)
        {
            var axis = action == ArrangeAction.CenterOnScreenHorizontally ? "horizontally" : "vertically";
            Status = offScreen == 0
                ? string.Create(CultureInfo.InvariantCulture, $"Centered {edits.Count} window(s) {axis} on the screen.")
                : string.Create(CultureInfo.InvariantCulture, $"Centered {edits.Count} window(s) {axis} on the screen. {offScreen} now extend past the screen edge.");
            return;
        }

        var gap = StacksHorizontally(action) ? HorizontalStackSpacing : StacksVertically(action) ? VerticalStackSpacing : 0;
        var spacing = gap == 0 ? "" : string.Create(CultureInfo.InvariantCulture, $" with {gap} pixels between them");
        Status = offScreen == 0
            ? string.Create(CultureInfo.InvariantCulture, $"{verb} {edits.Count} window(s) against {names[0]}{spacing}.")
            : string.Create(CultureInfo.InvariantCulture, $"{verb} {edits.Count} window(s) against {names[0]}{spacing}. {offScreen} now extend past the screen edge.");
    }

    private static bool StacksHorizontally(ArrangeAction action)
        => action is ArrangeAction.StackRight or ArrangeAction.StackLeft;

    private static bool StacksVertically(ArrangeAction action)
        => action is ArrangeAction.StackDown or ArrangeAction.StackUp;

    private static bool PlacesPaintedFrame(ArrangeAction action)
        => StacksHorizontally(action) || StacksVertically(action)
            || action is ArrangeAction.AlignLeft or ArrangeAction.AlignCenter or ArrangeAction.AlignRight
                or ArrangeAction.AlignTop or ArrangeAction.AlignMiddle or ArrangeAction.AlignBottom;

    private static int ClampStackSpacing(int pixels) => Math.Clamp(pixels, 0, 100);

    private void SetStackSpacingText(string value, int current, Action<int> save)
    {
        var text = value?.Trim() ?? "";
        var next = int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var parsed)
            || int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)
            ? ClampStackSpacing(parsed)
            : ClampStackSpacing(current);
        if (next != current)
        {
            save(next);
        }

        Raise(nameof(HorizontalStackSpacing));
        Raise(nameof(HorizontalStackSpacingText));
        Raise(nameof(VerticalStackSpacing));
        Raise(nameof(VerticalStackSpacingText));
    }

    private ScreenRect PaintedRect(string name)
    {
        var rect = LayoutRect(name);
        var frame = PaintFrame(name);
        return frame.IsEmpty ? rect : frame.ToPainted(rect);
    }

    private ScreenRect StoredRect(string name, ScreenRect arranged)
    {
        var frame = PaintFrame(name);
        return frame.IsEmpty ? arranged : frame.FromPainted(arranged);
    }

    private bool UsesPaintedFrame(string name) => !PaintFrame(name).IsEmpty;

    private FrameInset PaintFrame(string name)
    {
        var frame = WindowFrame.Paint(name);
        if (!frame.IsEmpty)
        {
            return frame;
        }

        var window = Windows.FirstOrDefault(item => item.Name == name);
        if (window is null)
        {
            return FrameInset.None;
        }

        if (window.Buttons.Count > 0)
        {
            return HotbarLayout.Chrome(window.Width, window.Height);
        }

        return window.IsInventoryBag ? FrameInset.Bag : FrameInset.None;
    }

    private void MatchAnchorSize(ArrangeAction action, List<string> names)
    {
        var horizontal = action == ArrangeAction.MatchWidth;
        var anchorRect = LayoutRect(names[0]);
        var edits = new List<(string Name, ScreenRect Rect)>();
        var skipped = 0;
        for (var index = 1; index < names.Count; index++)
        {
            var name = names[index];
            if (!(_skins.Find(name)?.Sizable ?? true))
            {
                skipped++;
                continue;
            }

            var current = LayoutRect(name);
            var matched = LayoutArranger.Arrange([anchorRect, current], action)[1];
            if (_inventory?.Find(name) is { Slots: > 0 } bag && CategoryFor(name) != "window")
            {
                var grid = horizontal
                    ? BagGrid.FromWidth(bag.Slots, matched.Width)
                    : BagGrid.FromHeight(bag.Slots, matched.Height);
                matched = matched with { Width = grid.Width, Height = grid.Height };
            }

            var section = DisplayLayoutSection(name);
            matched = WindowPlacement.KeepAnchor(current, matched.Width, matched.Height, section?.Get("XRef"), section?.Get("YRef"));
            if (matched == current)
            {
                continue;
            }

            edits.Add((name, matched));
        }

        if (edits.Count == 0)
        {
            Status = skipped > 0
                ? "Those windows cannot be resized."
                : "The selected windows already match the anchor.";
            return;
        }

        var axis = horizontal ? "width" : "height";
        var anchor = _sections.FirstOrDefault(item => item.Kind == LoadoutFileKind.Ui && item.Name == names[0]);
        var anchorName = anchor?.ListText ?? names[0];
        if (!ApplyCanvasEdits(edits, resized: false, $"match {axis} of {edits.Count} windows", resizeAll: true))
        {
            return;
        }

        var offScreen = edits.Count(edit => LayoutArranger.IsOffScreen(edit.Rect, ScreenWidth, ScreenHeight));
        Status = string.Create(
            CultureInfo.InvariantCulture,
            $"Matched the {axis} of {edits.Count} window(s) to {anchorName}.");
        if (offScreen > 0)
        {
            Status += string.Create(CultureInfo.InvariantCulture, $" {offScreen} now extend past the screen edge.");
        }

        if (skipped > 0)
        {
            Status += string.Create(CultureInfo.InvariantCulture, $" {skipped} window(s) cannot be resized.");
        }
    }

    private List<string> ArrangeTargets()
        => _layoutSelection.Where(name => Windows.Any(window => window.Name == name)).ToList();

    private bool SelectGroupLayout(string groupName, bool save)
    {
        if (!_sections.Any(item => item.Group == groupName && item.Kind == LoadoutFileKind.Ui))
        {
            return false;
        }

        SetGroupShown(groupName, shown: true, save);
        var names = _sections
            .Where(item => item.Group == groupName && item.Kind == LoadoutFileKind.Ui)
            .OrderBy(item => SectionListRank(item.Group, item.Name))
            .ThenBy(item => item.SortName, StringComparer.Ordinal)
            .Select(item => item.Name)
            .Where(name => Windows.Any(window => window.Name == name))
            .ToList();
        if (names.Count == 0)
        {
            return false;
        }

        ApplyLayoutOrder(names);
        return true;
    }

    public bool ApplyCanvasEdits(IReadOnlyList<(string Name, ScreenRect Rect)> edits, bool resized, string? label = null, bool resizeAll = false)
    {
        if (edits.Count == 0)
        {
            RebuildCanvas();
            return false;
        }

        var changed = false;
        var skipped = false;
        for (var index = 0; index < edits.Count; index++)
        {
            var edit = edits[index];
            var section = MutableLayoutSection(edit.Name);
            if (section is null)
            {
                skipped = true;
                continue;
            }

            var sizable = _skins.Find(edit.Name)?.Sizable ?? true;
            var thisResized = resizeAll || (resized && index == 0);
            if (thisResized && !sizable)
            {
                continue;
            }

            var writeSize = (thisResized && sizable) || WindowPlacement.HasStoredSize(section) || !WindowPlacement.HasPosition(section);
            WriteLayoutRect(section, edit.Rect, writeSize);
            changed = true;
        }

        if (skipped)
        {
            Status = "Some windows are not in the source loadout, so they were not moved.";
        }

        if (!changed)
        {
            RebuildCanvas();
            return false;
        }

        label ??= resized
            ? $"resize {edits[0].Name}"
            : edits.Count == 1 ? $"move {edits[0].Name}" : string.Create(CultureInfo.InvariantCulture, $"move {edits.Count} windows");
        MarkEdited(label, selectedSection: false);
        if (EditableSection() is { } current)
        {
            LoadPositionFields(current);
        }

        RebuildCanvas();
        return !skipped;
    }

    private void ApplyLayoutOrder(List<string> names)
    {
        names = OneCastSpell(names);
        if (names.Count == 0)
        {
            ClearWindowSelection();
            return;
        }

        var primary = _sections.FirstOrDefault(section => section.Kind == LoadoutFileKind.Ui && section.Name == names[0]);
        _layoutSelection.Clear();
        _layoutSelection.AddRange(names);
        RaiseLayoutSelection();
        if (primary is null || ReferenceEquals(primary, _selectedSection))
        {
            SectionSelectionChanged?.Invoke();
            return;
        }

        SelectSection(primary, resetLayout: false);
    }

    /// <summary>Drops the highlighted windows and the note for the window that was selected.</summary>
    public void ClearWindowSelection()
    {
        var clearSection = IsLayoutWindow(_selectedSection);
        if (_layoutSelection.Count == 0 && !clearSection)
        {
            return;
        }

        _layoutSelection.Clear();
        RaiseLayoutSelection();
        if (clearSection)
        {
            SelectSection(null, resetLayout: false);
            return;
        }

        SectionSelectionChanged?.Invoke();
    }

    private static bool IsLayoutWindow(SectionItem? item)
        => item is { Kind: LoadoutFileKind.Ui } && !IsEditorPick(item);

    private void ReplaceLayoutSelection(SectionItem? item)
    {
        _layoutSelection.Clear();
        if (item?.Kind == LoadoutFileKind.Ui)
        {
            _layoutSelection.Add(item.Name);
        }

        RaiseLayoutSelection();
    }

    private void RaiseLayoutSelection()
    {
        Raise(nameof(SelectedWindow));
        Raise(nameof(SelectedWindowList));
        Raise(nameof(CanArrange));
        Raise(nameof(CanCenterOnScreen));
        Raise(nameof(ArrangeHeader));
        Raise(nameof(PlacementScopeText));
        Raise(nameof(CopyButtonText));
        Raise(nameof(ShowCopySection));
        Raise(nameof(CanSave));
    }

    private ScreenRect LayoutRect(string name)
    {
        var shown = Windows.FirstOrDefault(window => window.Name == name);
        if (shown is not null)
        {
            return new ScreenRect(shown.Left, shown.Top, shown.Width, shown.Height);
        }

        var section = DisplayLayoutSection(name);
        return section is null ? new ScreenRect(0, 0, 160, 48) : ReadRect(section);
    }

    private IniSection? MutableLayoutSection(string name)
    {
        if (!IsAll)
        {
            var found = _sourceUi?.Document.Find(name);
            if (found is not null || _sourceUi is null || !IsSharedInventoryBag(name))
            {
                return found;
            }

            found = new IniSection(name);
            _sourceUi.Document.Sections.Add(found);
            SaveVisibility();
            return found;
        }

        var section = PendingFor(LoadoutFileKind.Ui, name);
        if (section is null)
        {
            var source = _sourceUi?.Document.Find(name);
            if (source is null)
            {
                if (!IsSharedInventoryBag(name))
                {
                    return null;
                }

                section = new IniSection(name);
                SaveVisibility();
            }
            else
            {
                section = source.Clone();
            }
        }

        CurrentPending[ItemKey(LoadoutFileKind.Ui, name)] = section;
        _touchedPending.Add((LoadoutFileKind.Ui, section));
        return section;
    }

    private (SpellGemFace[] Gems, SpellSetOption[] Sets) SpellWindowFace(IniSection window)
    {
        var spells = PendingFor(LoadoutFileKind.Character, "SpellLoadouts") ?? _sourceCharacter?.Document.Find("SpellLoadouts");
        var sets = spells is null ? [] : SpellGems.Read(spells);
        var listed = sets.Where(SpellWindows.IsListed).OrderBy(set => set.Index).ToList();
        if (listed.Count == 0)
        {
            return (EmptySpellGems(), []);
        }

        var selected = SpellWindows.SelectedIndex(window, sets);
        var current = listed.First(set => set.Index == selected);
        var options = listed
            .Select(set => new SpellSetOption(set.Index, set.Name, set.Index == selected))
            .ToArray();
        return (SpellFaces(current), options);
    }

    /// <summary>Gems on the hotbars. An H button is a memorized gem, so it follows the set on the main spell window.</summary>
    private IReadOnlyList<HotbarGem> MemorizedGems()
    {
        var spells = PendingFor(LoadoutFileKind.Character, "SpellLoadouts") ?? _sourceCharacter?.Document.Find("SpellLoadouts");
        if (spells is null)
        {
            return [];
        }

        var sets = SpellGems.Read(spells);
        var index = SpellWindows.SelectedIndex(DisplayLayoutSection("CastSpellWnd"), sets);
        var current = sets.FirstOrDefault(set => set.Index == index);
        return current is null
            ? []
            : SpellFaces(current).Select(face => new HotbarGem(face.Name, face.Icon)).ToArray();
    }

    /// <summary>A P hotbar button stores a spell id. The icon and hover name come from the spell list.</summary>
    private HotbarGem? SpellButton(int spellId)
    {
        if (!Spells().TryGet(spellId.ToString(CultureInfo.InvariantCulture), out var spell))
        {
            return null;
        }

        return new HotbarGem(spell.Name, spell.Icon >= 0 ? spell.Icon : -1);
    }

    private SpellGemFace[] SpellFaces(SpellGemSet set)
    {
        var catalog = Spells();
        var faces = new SpellGemFace[set.Slots.Length];
        for (var slot = 0; slot < set.Slots.Length; slot++)
        {
            var value = set.Slots[slot];
            var name = string.Empty;
            var icon = -1;
            if (!SpellCatalog.IsEmptyGem(value) && catalog.TryGet(value, out var spell))
            {
                name = spell.Name;
                icon = spell.Icon >= 0 ? spell.Icon : -1;
            }

            faces[slot] = new SpellGemFace(slot + 1, icon, name);
        }

        return faces;
    }

    private static SpellGemFace[] EmptySpellGems()
        => Enumerable.Range(1, 14).Select(number => new SpellGemFace(number, -1, string.Empty)).ToArray();

    private IniSection? DisplayLayoutSection(string name)
    {
        if (IsAll && PendingFor(LoadoutFileKind.Ui, name) is { } pending)
        {
            return pending;
        }

        return _sourceUi?.Document.Find(name);
    }

    private string PendingContext
    {
        get
        {
            var source = SourceLoadout;
            if (!IsAll || source is null)
            {
                return string.Empty;
            }

            return string.Join('|', source.UiFile, source.CharacterFile, CurrentDestinationScope, _destinationServer?.Character, _destinationServer?.Server);
        }
    }

    private Dictionary<string, IniSection> CurrentPending
    {
        get
        {
            var context = PendingContext;
            if (!_pendingByContext.TryGetValue(context, out var pending))
            {
                pending = new Dictionary<string, IniSection>(StringComparer.Ordinal);
                _pendingByContext[context] = pending;
            }

            return pending;
        }
    }

    private IniSection? PendingFor(LoadoutFileKind kind, string name)
    {
        if (!IsAll)
        {
            return null;
        }

        if (_pending is not null && _selectedSection is { } selected && selected.Kind == kind && selected.Name == name)
        {
            return _pending;
        }

        return _pendingByContext.TryGetValue(PendingContext, out var pending) && pending.TryGetValue(ItemKey(kind, name), out var section)
            ? section
            : null;
    }

    public void ReloadRawKeys()
    {
        ReloadRaw();
    }

    /// <summary>Sorts the raw-key grid. Letters are alphabetical and numbers are by value.</summary>
    public void SortRawKeys(bool byValue, bool ascending)
    {
        if (CollectionViewSource.GetDefaultView(RawRows) is ListCollectionView rawKeys)
        {
            rawKeys.CustomSort = byValue || !ascending
                ? new RawKeyOrder(byValue, ascending)
                : RawKeyOrder.ByKeyAscending;
        }
    }

    public void ShowStructuredEditor()
    {
        MutedDepth++;
        try
        {
            if (ShowSocials)
            {
                BuildSocialBoard();
            }

            FillHotbarPage();
            ReloadSpells();
        }
        finally
        {
            if (MutedDepth > 0) MutedDepth--;
        }
    }

    public void DeleteSelectedRawKey()
    {
        if (SelectedRaw is null)
        {
            return;
        }

        MutedDepth++;
        RawRows.Remove(SelectedRaw);
        if (MutedDepth > 0) MutedDepth--;
        SyncRaw();
    }

    public void CopySelectedSection()
    {
        if (IsAll)
        {
            CopySectionToAll();
            return;
        }

        var destination = DestinationLoadout;
        if (!CanCopy || SourceLoadout is null || destination is null || _selectedSection is null)
        {
            return;
        }

        var copied = new List<string>();
        foreach (var item in CopyItems())
        {
            var section = Document(editingDestination: false, item.Kind)?.Find(item.Name);
            var destinationDocument = Document(editingDestination: true, item.Kind);
            if (section is null || destinationDocument is null)
            {
                continue;
            }

            destinationDocument.ReplaceSection(section);
            copied.Add(section.Name);
        }

        if (copied.Any(ChatLayout.IsChatWindow))
        {
            CopyChatLayout(Document(editingDestination: true, LoadoutFileKind.Ui));
        }

        if (copied.Count == 0)
        {
            Status = "The source loadout does not have that section.";
            return;
        }

        MarkEdited(copied.Count == 1 ? $"copy {copied[0]}" : string.Create(CultureInfo.InvariantCulture, $"copy {copied.Count} sections"), selectedSection: false);
        Status = copied.Count == 1
            ? $"Copied [{copied[0]}] into {Describe(destination)}. Save to write the file."
            : string.Create(CultureInfo.InvariantCulture, $"Copied {copied.Count} sections into {Describe(destination)}. Save to write the files.");
    }

    private void CopySectionToAll()
    {
        if (_selectedSection is null || _pending is null)
        {
            return;
        }

        if (!SectionSave.CopiesToEveryLoadout(_selectedSection.Kind, _selectedSection.Name))
        {
            Status = _selectedSection.Name == "SpellLoadouts"
                ? BlockReasonFor("SpellLoadouts") ?? "Spell sets stay on the loadout that was edited."
                : "That section is not copied when the destination is every loadout.";
            return;
        }

        var name = _selectedSection.Name;
        var count = AllTargets().Count;
        if (ChatLayout.IsChatWindow(name))
        {
            foreach (var loadout in AllTargets())
            {
                var file = Open(loadout.UiFile);
                if (file is null || _sourceUi is not null && SamePath(file.Path, _sourceUi.Path))
                {
                    continue;
                }

                CopyChatLayout(file.Document);
            }
        }

        MarkEdited($"copy {name} to {count} loadouts");
        BuildEditor();
        Status = string.Create(CultureInfo.InvariantCulture, $"Copied [{name}] to {count} loadout(s). Save to write the files.");
    }

    public const string FullBackupNotice =
        "No full backup exists. A full backup of every character's loadouts is being created before the save.";

    public const string NoFolderBackupNotice =
        "No backup was performed. The EverQuest Legends folder does not exist. Correct the folder, then use Backup Files.";

    public const string NoCharactersBackupNotice =
        "No backup was performed. No characters were found in the EverQuest Legends folder. Correct the folder, then use Backup Files.";

    public bool NeedsFullBackup
        => !string.IsNullOrWhiteSpace(GameDirectory) && Directory.Exists(GameDirectory) && !GameFileSaver.HasFullBackup(GameDirectory);

    /// <summary>
    /// Question for copying chat channel filters onto the other loadouts of this character.
    /// Null when this save does not include a chat window, or there is no other loadout.
    /// </summary>
    public string? ChatFilterCopyQuestion()
    {
        if (!ChatWindowsAreBeingSaved())
        {
            return null;
        }

        var others = OtherLoadouts().Select(FormatLoadout).ToList();
        if (others.Count == 0)
        {
            return null;
        }

        if (others.Count == 1)
        {
            return $"Save the chat tabs to {others[0]} too? This includes which window each tab is on and the channel filters. The window positions are saved either way.";
        }

        return "Save the chat tabs to the other loadouts too? This includes which window each tab is on and the channel filters."
            + Environment.NewLine + Environment.NewLine
            + string.Join(", ", others)
            + Environment.NewLine + Environment.NewLine
            + "The window positions are saved either way.";
    }

    /// <summary>Copies the open loadout's chat filters onto the other loadouts for this character. Returns how many files changed.</summary>
    public int CopyChatFiltersToOtherLoadouts()
    {
        var source = ViewedChatManager();
        if (source is null)
        {
            return 0;
        }

        var changed = 0;
        foreach (var loadout in OtherLoadouts())
        {
            var file = Open(loadout.UiFile);
            if (file is not null && CopyChatLayout(file.Document))
            {
                changed++;
            }
        }

        if (changed > 0)
        {
            PushState("chat filters for other loadouts");
        }

        return changed;
    }

    /// <summary>Copies the open loadout's chat windows, tabs, and channel filters onto one UI document.</summary>
    private bool CopyChatLayout(IniDocument? destination)
    {
        var source = ViewedChatManager();
        if (source is null || destination is null)
        {
            return false;
        }

        var section = destination.Find("ChatManager");
        if (section is null)
        {
            section = new IniSection("ChatManager");
            destination.Sections.Add(section);
        }

        return ChatLayout.CopyFilters(source, section);
    }

    private bool ChatWindowsAreBeingSaved()
    {
        foreach (var file in _files.Values)
        {
            if (KindOf(file.Path) != LoadoutFileKind.Ui || !file.HasChanges)
            {
                continue;
            }

            var changes = SectionSave.ChangedSections(IniDocument.Parse(file.OriginalText), file.Document);
            if (changes.Any(change => change.Name == "ChatManager" || ChatLayout.IsChatWindow(change.Name)))
            {
                return true;
            }
        }

        return false;
    }

    private IEnumerable<DiscoveredLoadout> OtherLoadouts()
    {
        if (SourceLoadout is not { } source)
        {
            return [];
        }

        return _catalog
            .Where(loadout => !DestinationSet.SameLoadout(loadout, source)
                && string.Equals(loadout.Id.Character, source.Id.Character, StringComparison.OrdinalIgnoreCase)
                && string.Equals(loadout.Id.Server, source.Id.Server, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(loadout.UiFile))
            .OrderBy(loadout => loadout.Id.Number);
    }

    public SavePrompt DescribeSave()
    {
        StageHighlightedWindows();
        StageWindowEditsForEveryLoadout();
        var windows = false;
        var character = false;
        var content = false;
        var other = false;
        var windowFiles = new List<string>();
        var characterFiles = new List<string>();
        foreach (var file in _files.Values.Where(item => item.HasChanges).OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase))
        {
            var changes = SectionSave.ChangedSections(IniDocument.Parse(file.OriginalText), file.Document);
            if (changes.Count == 0)
            {
                continue;
            }

            var name = Path.GetFileName(file.Path);
            if (KindOf(file.Path) == LoadoutFileKind.Ui)
            {
                windows = true;
                windowFiles.Add(name);
                continue;
            }

            character = true;
            characterFiles.Add(name);
            if (changes.Any(change => SectionSave.IsCharacterContent(change.Name)))
            {
                content = true;
            }

            if (changes.Any(change => !SectionSave.IsCharacterContent(change.Name)))
            {
                other = true;
            }
        }

        return new SavePrompt(windows, character, content, other, windowFiles, characterFiles);
    }

    public SaveResult SaveChanges(bool saveWindows, bool saveCharacter, SectionWriteMode characterMode)
    {
        if (!saveWindows && !saveCharacter)
        {
            Status = "Nothing was saved.";
            return SaveResult.Failed("Choose windows, character changes, or both.");
        }

        if (string.IsNullOrWhiteSpace(GameDirectory) || !Directory.Exists(GameDirectory))
        {
            Status = "The game directory does not exist. Nothing was written.";
            return SaveResult.Failed(Status);
        }

        if (saveWindows)
        {
            StageHighlightedWindows();
            StageWindowEditsForEveryLoadout();
        }

        var prepared = new List<PreparedSave>();
        var reloadSocials = false;
        foreach (var file in _files.Values.Where(item => item.HasChanges).OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase))
        {
            var changes = SectionSave.ChangedSections(IniDocument.Parse(file.OriginalText), file.Document);
            if (changes.Count == 0)
            {
                continue;
            }

            var kind = KindOf(file.Path);
            if (kind == LoadoutFileKind.Ui ? !saveWindows : !saveCharacter)
            {
                continue;
            }

            var writes = new List<SectionWrite>();
            var editedLoadout = IsSourceFile(file.Path, kind);
            foreach (var change in changes)
            {
                if (change.Name == "SpellLoadouts"
                    && !SectionSave.IncludeSpellLoadouts(IsAll, editedLoadout))
                {
                    continue;
                }

                if (kind == LoadoutFileKind.Character
                    && SectionSave.IsCharacterContent(change.Name)
                    && characterMode == SectionWriteMode.MergeNoDeletion)
                {
                    if (change.Edited is not null)
                    {
                        writes.Add(SectionWrite.Merge(change));
                    }

                    continue;
                }

                writes.Add(SectionWrite.Replace(change));
            }

            if (writes.Count == 0)
            {
                continue;
            }

            if (writes.Any(write => write.Name == "Socials") && IsOpenCharacterFile(file.Path))
            {
                reloadSocials = true;
            }

            LoadedIniFile disk;
            try
            {
                disk = IniFile.Load(file.Path);
            }
            catch (Exception ex)
            {
                Status = $"Could not read {Path.GetFileName(file.Path)} to save. Nothing was written. {ex.Message}";
                return SaveResult.Failed(Status);
            }

            var fileUsesMerge = writes.Any(write => write.Mode == SectionWriteMode.MergeNoDeletion);
            prepared.Add(new PreparedSave(
                file,
                new PendingFileWrite(file.Path, SectionSave.Apply(disk.OriginalText, writes), disk.Encoding),
                fileUsesMerge));
        }

        if (prepared.Count == 0)
        {
            Status = "Nothing to save.";
            UpdateDirtyState();
            return SaveResult.NoChanges();
        }

        SaveResult? last = null;
        var written = 0;
        var checkedSets = new List<string>();
        var usedMerge = false;
        foreach (var group in prepared.GroupBy(item => Path.GetDirectoryName(item.Write.Path) ?? GameDirectory, StringComparer.OrdinalIgnoreCase))
        {
            var batch = group.ToList();
            SuppressOwnWrites(batch.Select(item => item.Write));
            try
            {
                last = GameFileSaver.Save(
                    group.Key,
                    batch.Select(item => item.Write).ToList(),
                    DateTimeOffset.Now,
                    keepModifiedCopies: IsDebugBuild && _store.Current.SaveModifiedCopies);
                RememberOwnStamps(last.WrittenFiles);
                if (!last.Success)
                {
                    break;
                }

                var problems = new List<string>();
                foreach (var item in batch)
                {
                    var problem = ConfirmRemovedSpellSets(item.Write, checkedSets);
                    if (problem is not null)
                    {
                        problems.Add(problem);
                        continue;
                    }

                    RememberSavedFile(item);
                    usedMerge |= item.UsedMerge;
                    written++;
                }

                if (problems.Count > 0)
                {
                    UpdateDirtyState();
                    Status = string.Join(" ", problems);
                    return SaveResult.Failed(Status);
                }
            }
            finally
            {
                ReleaseOwnWrites(batch.Select(item => item.Write));
            }
        }

        UpdateDirtyState();
        if (last is null || !last.Success)
        {
            Status = last?.Message ?? "Nothing was saved.";
            return last ?? SaveResult.Failed("Nothing was saved.");
        }

        Status = string.Create(CultureInfo.InvariantCulture, $"Saved {written} file(s). Backup: {last.BackupDirectory}")
            + (checkedSets.Count > 0 ? " " + string.Join(" ", checkedSets) : string.Empty)
            + (System.Diagnostics.Process.GetProcessesByName("eqgame").Length > 0
                ? " EverQuest is running and can overwrite these files when it exits."
                : string.Empty);
        if (reloadSocials)
        {
            ResetSocialComparison();
        }

        if (usedMerge)
        {
            BuildEditor();
        }
        else if (reloadSocials && ShowSocials)
        {
            BuildSocialBoard();
        }

        return last;
    }

    public SaveResult SaveLoadoutCopies()
    {
        if (string.IsNullOrWhiteSpace(GameDirectory) || !Directory.Exists(GameDirectory))
        {
            Status = NoFolderBackupNotice;
            return SaveResult.Failed(Status);
        }

        var copies = new List<StoredBackup>();
        foreach (var loadout in _catalog)
        {
            AddStoredCopy(copies, loadout, LoadoutFileKind.Character);
            AddStoredCopy(copies, loadout, LoadoutFileKind.Ui);
        }

        if (copies.Count == 0)
        {
            Status = NoCharactersBackupNotice;
            return SaveResult.Failed(Status);
        }

        var result = GameFileSaver.WriteCopies(GameDirectory, copies, DateTimeOffset.Now);
        Status = result.Message;
        return result;
    }

    public IReadOnlyList<BackupSnapshot> ListBackups()
        => string.IsNullOrWhiteSpace(GameDirectory) ? [] : GameFileSaver.List(GameDirectory);

    public SaveResult RestoreBackup(IReadOnlyList<string> sourcePaths)
    {
        var result = GameFileSaver.Restore(GameDirectory, sourcePaths);
        if (!result.Success)
        {
            Status = result.Message;
            return result;
        }

        var reloaded = new List<string>();
        foreach (var path in result.WrittenFiles)
        {
            _fileStamps.Remove(path);
            if (TryReloadLoadout(path) == ExternalRead.Reloaded)
            {
                reloaded.Add(path);
            }
        }

        if (reloaded.Count > 0)
        {
            ResetHistoryAfterReload();
            ResetSocialComparison();
            RefreshView();
        }

        Status = result.Message
            + (System.Diagnostics.Process.GetProcessesByName("eqgame").Length > 0
                ? " EverQuest is running and can overwrite these files when it exits."
                : string.Empty);
        return result;
    }

    private void AddStoredCopy(List<StoredBackup> copies, DiscoveredLoadout loadout, LoadoutFileKind kind)
    {
        var path = loadout.FileFor(kind);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        var bytes = _files.TryGetValue(path, out var file) && file.HasChanges
            ? file.Encoding.GetBytes(file.Document.Write())
            : File.ReadAllBytes(path);
        copies.Add(new StoredBackup(loadout.Id, kind, bytes));
    }

    private void RememberSavedFile(PreparedSave item)
    {
        var path = item.File.Path;
        var origin = _baselines.TryGetValue(path, out var recorded) ? recorded : item.File.OriginalText;
        var after = item.Write.NewText;
        for (var index = 0; index < _undo.Count; index++)
        {
            _undo[index] = RebaseState(_undo[index], path, origin, after);
        }

        for (var index = 0; index < _redo.Count; index++)
        {
            _redo[index] = RebaseState(_redo[index], path, origin, after);
        }

        _baselines[path] = SectionSave.UndoBaseline(origin, after, item.File.Document);
        AdoptSavedDocument(item.File, after);
        if (_undo.Count > 0)
        {
            _undo[^1] = ReplaceFileText(_undo[^1], path, item.File.Document.Write());
        }
    }

    private static EditState RebaseState(EditState state, string path, string origin, string after)
    {
        if (!state.Files.TryGetValue(path, out var text))
        {
            return state;
        }

        return ReplaceFileText(state, path, SectionSave.RebaseUnchanged(text, origin, after));
    }

    private static EditState ReplaceFileText(EditState state, string path, string text)
    {
        var files = new Dictionary<string, string>(state.Files, StringComparer.OrdinalIgnoreCase);
        files[path] = text;
        return state with { Files = files };
    }

    private static void AdoptSavedDocument(LoadedIniFile file, string writtenText)
    {
        var written = IniDocument.Parse(writtenText);
        var current = new Dictionary<string, IniSection>(StringComparer.Ordinal);
        foreach (var section in file.Document.Sections)
        {
            current.TryAdd(section.Name, section);
        }

        var merged = new List<IniSection>();
        foreach (var section in written.Sections)
        {
            if (current.TryGetValue(section.Name, out var existing))
            {
                existing.ReplaceEntries(section.Entries);
                merged.Add(existing);
            }
            else
            {
                merged.Add(section.Clone());
            }
        }

        file.Document.NewLine = written.NewLine;
        file.Document.Sections.Clear();
        file.Document.Sections.AddRange(merged);
        file.MarkSaved(writtenText);
    }

    private LoadoutFileKind KindOf(string path)
    {
        if ((_sourceUi is not null && SamePath(_sourceUi.Path, path))
            || (_destUi is not null && SamePath(_destUi.Path, path))
            || Path.GetFileName(path).StartsWith("UI_", StringComparison.Ordinal))
        {
            return LoadoutFileKind.Ui;
        }

        return LoadoutFileKind.Character;
    }

    private static bool HasCopyFile(DiscoveredLoadout? loadout)
        => loadout is not null && (FileExists(loadout.CharacterFile) || FileExists(loadout.UiFile));

    private static bool FileExists(string? path)
        => !string.IsNullOrWhiteSpace(path) && File.Exists(path);

    private sealed record PreparedSave(LoadedIniFile File, PendingFileWrite Write, bool UsedMerge);

    private string? ConfirmRemovedSpellSets(PendingFileWrite write, List<string> notes)
    {
        if (!_files.TryGetValue(write.Path, out var file))
        {
            return null;
        }

        var removed = SpellGems.RemovedIndexes(file.OriginalText, write.NewText);
        if (removed.Count == 0)
        {
            return null;
        }

        string onDisk;
        try
        {
            onDisk = File.ReadAllText(write.Path, write.Encoding);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"Could not re-read {DescribeSavedFile(write.Path)} to confirm the deleted spell set. {ex.Message}";
        }

        var still = SpellGems.StillPresent(onDisk, removed);
        if (still.Count > 0)
        {
            var sets = still.Count == 1 ? $"Spell set {still[0]} is" : $"Spell sets {string.Join(", ", still)} are";
            return $"{sets} still in {DescribeSavedFile(write.Path)}.";
        }

        var label = removed.Count == 1 ? $"Spell set {removed[0]} is" : $"Spell sets {string.Join(", ", removed)} are";
        notes.Add($"{label} no longer in {DescribeSavedFile(write.Path)}.");
        return null;
    }

    private string DescribeSavedFile(string path)
    {
        var name = Path.GetFileName(path);
        return _destCharacter is not null && string.Equals(path, _destCharacter.Path, StringComparison.OrdinalIgnoreCase)
            ? $"the destination file {name}"
            : name;
    }

    public void Undo()
    {
        if (_undo.Count < 2)
        {
            return;
        }

        var current = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(current);
        Restore(_undo[^1]);
        Status = $"Undid {current.Label}.";
    }

    public void Redo()
    {
        if (_redo.Count == 0)
        {
            return;
        }

        var next = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(next);
        Restore(next);
        Status = $"Redid {next.Label}.";
    }

    private void Restore(EditState state)
    {
        foreach (var path in state.Files.Keys.Where(path => !_files.ContainsKey(path)).ToList())
        {
            Open(path);
        }

        foreach (var (path, baseline) in _baselines)
        {
            if (!_files.TryGetValue(path, out var file))
            {
                continue;
            }

            var text = state.Files.TryGetValue(path, out var saved) ? saved : baseline;
            if (file.Document.Write() != text)
            {
                file.Document.ReplaceAll(IniDocument.Parse(text));
            }
        }

        _pendingByContext = ClonePending(state.Pending);
        _touchedPending.Clear();
        UpdateDirtyState();
        RefreshView();
    }

    private void RefreshView()
    {
        var selected = _selectedSection is { } item ? (item.Name, item.Kind) : default((string Name, LoadoutFileKind Kind)?);
        var layout = _layoutSelection.ToList();
        MutedDepth++;
        try
        {
            RebuildSections();
            foreach (var section in _sections)
            {
                section.SetShown(_shownKeys.Contains(ItemKey(section)));
            }

            SyncShownKeys();
            UpdateGroupToggles();
        }
        finally
        {
            if (MutedDepth > 0) MutedDepth--;
        }

        ReloadInventory(announce: false);
        _selectedSection = selected is { } key
            ? _sections.FirstOrDefault(section => section.Name == key.Name && section.Kind == key.Kind)
            : null;
        if (_selectedSection is { IsBlocked: true } blocked && !IsEditorPick(blocked))
        {
            _selectedSection = null;
        }
        Raise(nameof(SelectedSection));
        Raise(nameof(HasSelection));
        _pending = null;
        BuildEditor();
        UpdateCopyState();
        _layoutSelection.Clear();
        _layoutSelection.AddRange(layout.Where(name => _sections.Any(section => section.Kind == LoadoutFileKind.Ui && section.Name == name)));
        RaiseLayoutSelection();
        RebuildCanvas();
        SectionSelectionChanged?.Invoke();
    }

    private void ApplyClientResolution()
    {
        _resolutionFromGame = ClientResolution.TryRead(GameDirectory, out var width, out var height);
        if (_resolutionFromGame)
        {
            ScreenWidth = width;
            ScreenHeight = height;
        }

        Raise(nameof(ScreenWidth));
        Raise(nameof(ScreenHeight));
        Raise(nameof(HasResolution));
        Raise(nameof(ScreenSizeText));
        Raise(nameof(ScreenSizeHelp));
        Raise(nameof(CanArrange));
        Raise(nameof(CanCenterOnScreen));
    }

    private void Discover()
    {
        ApplyClientResolution();
        WatchInventoryExports();
        WatchGameFiles();
        _sharedInventoryBags = null;
        _catalog = LoadoutDiscovery.Discover(GameDirectory).ToList();
        RebuildOptions();
        ReportFoundLoadouts();
    }

    private void ReportFoundLoadouts()
    {
        // The source dropdown lists one character on one server. The catalog also
        // holds every other character and server in the folder.
        Status = _catalog.Count == 0
            ? "No loadout files were found in that folder."
            : $"Found {SourceLoadouts.Count} loadouts.";
    }

    private static bool IsDiscoveryStatus(string status)
    {
        return status == "No loadout files were found in that folder."
            || (status.StartsWith("Found ", StringComparison.Ordinal) && status.EndsWith(" loadouts.", StringComparison.Ordinal));
    }

    private void RebuildOptions()
    {
        SourceServers.Clear();
        SourceLoadouts.Clear();
        DestinationServers.Clear();
        DestinationLoadouts.Clear();

        foreach (var server in _catalog
            .GroupBy(loadout => $"{loadout.Id.Character}\n{loadout.Id.Server}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(loadout => loadout.Id.Server, StringComparer.OrdinalIgnoreCase)
            .ThenBy(loadout => loadout.Id.Character, StringComparer.OrdinalIgnoreCase))
        {
            var text = FormatServer(server.Id.Server, server.Id.Character);
            SourceServers.Add(new ServerChoice { Character = server.Id.Character, Server = server.Id.Server, Text = text });
            DestinationServers.Add(new ServerChoice { Character = server.Id.Character, Server = server.Id.Server, Text = text });
        }

        DestinationServers.Add(new ServerChoice { AllServers = true, Text = "All servers" });

        var saved = _store.Current;
        _sourceServer = SourceServers.FirstOrDefault(choice => MatchesServer(choice, saved.SourceCharacter, saved.SourceServer))
            ?? SourceServers.FirstOrDefault();
        FillSourceLoadouts();
        _sourceLoadout = SourceLoadouts.FirstOrDefault(choice => choice.Loadout?.Id.Number == saved.SourceLoadout)
            ?? SourceLoadouts.FirstOrDefault();

        if (saved.DestinationServerChosen is null)
        {
            var differs = saved.DestinationAllServers
                || (saved.DestinationServer is not null && _sourceServer is not null
                    && !MatchesServer(_sourceServer, saved.DestinationCharacter, saved.DestinationServer));
            _store.SetDestinationServerChosen(differs);
        }

        _destinationServer = saved.DestinationServerChosen != true
            ? DestinationServers.FirstOrDefault(choice => SameServer(choice, _sourceServer))
            : saved.DestinationAllServers
                ? DestinationServers.FirstOrDefault(choice => choice.AllServers)
                : DestinationServers.FirstOrDefault(choice => MatchesServer(choice, saved.DestinationCharacter, saved.DestinationServer));
        _destinationServer ??= DestinationServers.FirstOrDefault(choice => !choice.AllServers);
        FillDestinationLoadouts();
        _destinationLoadout = _destinationServer?.AllServers == true || saved.DestinationAllLoadouts
            ? DestinationLoadouts.FirstOrDefault(choice => choice.AllLoadouts)
            : DestinationLoadouts.FirstOrDefault(choice => choice.Loadout?.Id.Number == saved.DestinationLoadout)
                ?? DestinationLoadouts.FirstOrDefault(choice => choice.Loadout is not null);
        _destinationLoadout ??= DestinationLoadouts.FirstOrDefault();

        RememberSource();
        RememberDestination();
        RaiseSelection();
        LoadPair();
    }

    private void RefreshOptionText()
    {
        foreach (var choice in SourceLoadouts)
        {
            if (choice.Loadout is not null)
            {
                choice.Text = FormatSourceLoadout(choice.Loadout);
            }
        }

        foreach (var choice in DestinationLoadouts)
        {
            if (choice.Loadout is not null)
            {
                choice.Text = FormatLoadout(choice.Loadout);
            }
        }

        OptionsChanged?.Invoke();
        UpdateViewedTitle();
    }

    private static string FormatServer(string server, string character) => $"{FormatServerName(server)} - {character}";

    private string FormatLoadout(DiscoveredLoadout loadout) => FormatLoadoutLine(loadout, includeLevel: false);

    private string FormatSourceLoadout(DiscoveredLoadout loadout)
    {
        var text = FormatLoadoutLine(loadout, includeLevel: true);
        var seen = LastWho(loadout);
        return seen is null
            ? text
            : $"{text} Last Updated: {seen.Value.ToString("MMM d, yyyy h:mm tt", CultureInfo.CurrentCulture)}";
    }

    private string FormatLoadoutLine(DiscoveredLoadout loadout, bool includeLevel)
    {
        var number = loadout.Id.Number.ToString("00", CultureInfo.InvariantCulture);
        var classes = _store.GetLabel(loadout.Id).Trim();
        var level = includeLevel ? LevelFor(loadout.Id) : null;
        var body = level is int known
            ? classes.Length == 0 ? known.ToString(CultureInfo.InvariantCulture) : $"{known.ToString(CultureInfo.InvariantCulture)} {classes}"
            : classes;
        return body.Length == 0 ? $"L{number}" : $"L{number} - {body}";
    }

    private DateTime? LastWho(DiscoveredLoadout loadout)
    {
        var path = WhoLog.PathFor(GameDirectory, loadout.Id.Character, loadout.Id.Server);
        return _whoScans.TryGetValue(path, out var scan)
            ? WhoLog.LastSeen(scan.Sightings, CharacterClasses.Parse(_store.GetLabel(loadout.Id)))
            : null;
    }

    private string Describe(DiscoveredLoadout loadout)
    {
        return $"{FormatServer(loadout.Id.Server, loadout.Id.Character)} {FormatLoadout(loadout)}";
    }

    private void ApplySelection(Action change)
    {
        _selectionDepth++;
        try
        {
            change();
            RaiseSelection();
            LoadPair();
        }
        finally
        {
            if (_selectionDepth > 0)
            {
                _selectionDepth--;
            }
        }
    }

    private void RaiseSelection()
    {
        Raise(nameof(SelectedSourceServer));
        Raise(nameof(SelectedSourceLoadout));
        Raise(nameof(SelectedDestinationServer));
        Raise(nameof(SelectedDestinationLoadout));
        Raise(nameof(DestinationLoadoutsEnabled));
        Raise(nameof(CanSaveLoadoutCopies));
        OptionsChanged?.Invoke();
    }

    private void FillSourceLoadouts()
    {
        SourceLoadouts.Clear();
        if (_sourceServer is null)
        {
            return;
        }

        foreach (var loadout in LoadoutsFor(_sourceServer))
        {
            SourceLoadouts.Add(new LoadoutChoice(FormatSourceLoadout(loadout), loadout));
        }
    }

    private void FillDestinationLoadouts()
    {
        DestinationLoadouts.Clear();
        if (_destinationServer is null)
        {
            return;
        }

        if (_destinationServer.AllServers)
        {
            DestinationLoadouts.Add(new LoadoutChoice("All loadouts", allLoadouts: true));
            return;
        }

        foreach (var loadout in LoadoutsFor(_destinationServer))
        {
            DestinationLoadouts.Add(new LoadoutChoice(FormatLoadout(loadout), loadout));
        }

        DestinationLoadouts.Add(new LoadoutChoice("All loadouts", allLoadouts: true));
    }

    private IEnumerable<DiscoveredLoadout> LoadoutsFor(ServerChoice server)
        => LoadoutDiscovery.ForCharacterAndServer(_catalog, server.Character, server.Server);

    private void RememberSource()
    {
        if (_sourceLoadout?.Loadout is not DiscoveredLoadout loadout)
        {
            return;
        }

        _store.RememberSource(loadout.Id.Character, loadout.Id.Server, loadout.Id.Number);
    }

    private void RememberDestination()
    {
        if (_destinationServer is null)
        {
            return;
        }

        if (_destinationServer.AllServers)
        {
            _store.RememberDestination(null, null, null, allServers: true, allLoadouts: true);
            return;
        }

        if (_destinationLoadout?.AllLoadouts == true)
        {
            _store.RememberDestination(_destinationServer.Character, _destinationServer.Server, null, false, true);
            return;
        }

        if (_destinationLoadout?.Loadout is not DiscoveredLoadout loadout)
        {
            return;
        }

        _store.RememberDestination(loadout.Id.Character, loadout.Id.Server, loadout.Id.Number, false, false);
    }

    private static bool SameServer(ServerChoice? left, ServerChoice? right)
    {
        if (ReferenceEquals(left, right))
        {
            return left is not null;
        }

        if (left is null || right is null)
        {
            return false;
        }

        if (left.AllServers || right.AllServers)
        {
            return left.AllServers && right.AllServers;
        }

        return string.Equals(left.Character, right.Character, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.Server, right.Server, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesServer(ServerChoice choice, string? character, string? server)
    {
        return !choice.AllServers
            && string.Equals(choice.Character, character, StringComparison.OrdinalIgnoreCase)
            && string.Equals(choice.Server, server, StringComparison.OrdinalIgnoreCase);
    }

    private void LoadPair()
    {
        // Switching loadouts rebuilds the macro and spell editors. Their text boxes write the
        // current values back while that happens. That is not the user editing, so it must not
        // ask for classes. The hold lasts until those binding updates have run, then a missing
        // trio is asked for once for this source loadout.
        var sourceId = SourceLoadout?.Id;
        if (sourceId != _classOfferLoadout)
        {
            _classOfferLoadout = sourceId;
            _offerClassesAfterLoad = sourceId is not null;
        }

        _suppressClassPrompt++;
        var ticket = ++_classOfferTicket;
        Dispatcher.CurrentDispatcher.BeginInvoke(
            () => FinishLoadClassPrompt(ticket),
            DispatcherPriority.ApplicationIdle);
        ResetSocialComparison();
        var previous = _selectedSection is { } current && !_layoutSelection.Contains(current.Name, StringComparer.Ordinal)
            ? (current.Kind, current.Name)
            : ((LoadoutFileKind Kind, string Name)?)null;
        MutedDepth++;
        _sourceCharacter = Open(SourceLoadout?.CharacterFile);
        _sourceUi = Open(SourceLoadout?.UiFile);
        if (!IsAll && DestinationLoadout is not null && SourceLoadout is not null
            && DestinationSet.SameLoadout(SourceLoadout, DestinationLoadout))
        {
            _destCharacter = _sourceCharacter;
            _destUi = _sourceUi;
        }
        else if (!IsAll)
        {
            _destCharacter = Open(DestinationLoadout?.CharacterFile);
            _destUi = Open(DestinationLoadout?.UiFile);
        }
        else
        {
            _destCharacter = null;
            _destUi = null;
        }

        _pending = null;
        Raise(nameof(CopyButtonText));
        Raise(nameof(ShowCopySection));
        RefreshSkin();
        RebuildSections();
        ApplyItemVisibility();
        ReloadInventory(announce: false);
        UpdateViewedTitle();
        _classLabel = ViewedLoadout is null ? string.Empty : _store.GetLabel(ViewedLoadout.Id);
        Raise(nameof(ClassLabel));
        SyncCharacterLevelText();
        ApplyClasses();
        RefreshClassSuggestions();
        if (MutedDepth > 0) MutedDepth--;
        // Macros and spell loadouts cannot be edited for every destination loadout, and they have no window layout.
        if (previous is { } kept
            && _sections.FirstOrDefault(section => section.Kind == kept.Kind && section.Name == kept.Name) is { } same
            && (!same.IsBlocked || IsEditorPick(same)))
        {
            SelectSection(same);
            return;
        }

        if (!SelectGroupLayout(InventoryGroup, save: false))
        {
            SelectSection(_sections.FirstOrDefault(item => !item.IsBlocked));
        }
    }

    private void FinishLoadClassPrompt(int ticket)
    {
        var current = ticket == _classOfferTicket;
        try
        {
            if (_disposed || !current || !_offerClassesAfterLoad)
            {
                return;
            }

            _offerClassesAfterLoad = false;
            var onSpells = _selectedSection?.Name == "SpellLoadouts";
            var needLevel = onSpells && ViewedLoadout is { } viewed && !HasCharacterLevel(viewed.Id);
            if (needLevel)
            {
                if (!EnsureSpellLevel())
                {
                    ReturnToLayout();
                }

                return;
            }

            if (EditedLoadoutHasThreeClasses || ConfirmContentClasses is null)
            {
                return;
            }

            // Stay suppressed while the dialog is open so a binding update cannot open a second one.
            ConfirmContentClasses.Invoke();
        }
        finally
        {
            if (_suppressClassPrompt > 0)
            {
                _suppressClassPrompt--;
            }
        }
    }

    private void UpdateViewedTitle()
    {
        var viewed = ViewedLoadout;
        ViewedTitle = viewed is null
            ? "No loadout selected"
            : $"Editing {Describe(viewed)}";
    }

    private DiscoveredLoadout? ViewedLoadout => SourceLoadout;

    private void WatchInventoryExports()
    {
        _inventoryWatcher?.Dispose();
        _inventoryWatcher = null;
        if (!Directory.Exists(GameDirectory))
        {
            return;
        }

        try
        {
            var watcher = new FileSystemWatcher(GameDirectory, "*-Inventory.txt")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            };
            FileSystemEventHandler changed = (_, _) => _inventoryReload.Dispatcher.BeginInvoke(() =>
            {
                _inventoryReload.Stop();
                _inventoryReload.Start();
            });
            watcher.Created += changed;
            watcher.Changed += changed;
            watcher.Renamed += (sender, args) => changed(sender, args);
            watcher.EnableRaisingEvents = true;
            _inventoryWatcher = watcher;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            _inventoryWatcher = null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _inventoryReload.Stop();
        _externalReload.Stop();
        _logTail.Stop();
        _inventoryWatcher?.Dispose();
        _gameWatcher?.Dispose();
        _logWatcher?.Dispose();
        _inventoryWatcher = null;
        _gameWatcher = null;
        _logWatcher = null;
    }

    private void WatchGameFiles()
    {
        _externalReload.Stop();
        _logTail.Stop();
        _pendingGameChanges.Clear();
        _dirtyLogs.Clear();
        _externalRetries = 0;
        _gameWatcher?.Dispose();
        _logWatcher?.Dispose();
        _gameWatcher = null;
        _logWatcher = null;
        if (_disposed || !Directory.Exists(GameDirectory))
        {
            return;
        }

        try
        {
            var watcher = new FileSystemWatcher(GameDirectory)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
                InternalBufferSize = 64 * 1024,
            };
            watcher.Changed += OnGamePathChanged;
            watcher.Created += OnGamePathChanged;
            watcher.Renamed += (_, args) => QueueGameChange(args.FullPath);
            watcher.EnableRaisingEvents = true;
            _gameWatcher = watcher;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            _gameWatcher = null;
        }

        WatchLogs();
    }

    private void WatchLogs()
    {
        _logWatcher?.Dispose();
        _logWatcher = null;
        if (_disposed)
        {
            return;
        }

        var logs = Path.Combine(GameDirectory, "Logs");
        if (!Directory.Exists(logs))
        {
            return;
        }

        try
        {
            var watcher = new FileSystemWatcher(logs, "eqlog_*.txt")
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
                InternalBufferSize = 64 * 1024,
            };
            watcher.Changed += OnGamePathChanged;
            watcher.Created += OnGamePathChanged;
            watcher.Renamed += (_, args) => QueueGameChange(args.FullPath);
            watcher.EnableRaisingEvents = true;
            _logWatcher = watcher;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            _logWatcher = null;
        }

        if (!_disposed && Directory.Exists(GameDirectory) && !_logTail.IsEnabled)
        {
            _logTail.Start();
        }
    }

    private void OnGamePathChanged(object sender, FileSystemEventArgs args) => QueueGameChange(args.FullPath);

    private void QueueGameChange(string fullPath)
    {
        if (_disposed)
        {
            return;
        }

        var dispatcher = _externalReload.Dispatcher;
        if (dispatcher.HasShutdownStarted)
        {
            return;
        }

        if (dispatcher.CheckAccess())
        {
            NoteGameChange(fullPath);
            return;
        }

        dispatcher.BeginInvoke(() => NoteGameChange(fullPath));
    }

    private void NoteGameChange(string fullPath)
    {
        if (_disposed)
        {
            return;
        }

        if (IsLogsDirectory(fullPath))
        {
            WatchLogs();
            return;
        }

        var kind = GameFolderWatch.Classify(GameDirectory, fullPath);
        if (kind == WatchedGameFile.WhoLog)
        {
            MarkLogDirty(fullPath);
            return;
        }

        if (kind is WatchedGameFile.None or WatchedGameFile.InventoryExport)
        {
            return;
        }

        if (kind == WatchedGameFile.LoadoutIni)
        {
            var loaded = MatchLoadedPath(fullPath);
            if (loaded is null)
            {
                return;
            }

            fullPath = loaded;
        }

        if (_suppressedWrites.Contains(fullPath))
        {
            return;
        }

        _pendingGameChanges.Add(fullPath);
        _externalReload.Stop();
        _externalReload.Start();
    }

    private bool IsLogsDirectory(string fullPath)
    {
        try
        {
            var logs = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(GameDirectory, "Logs")));
            return string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(fullPath)),
                logs,
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private string? ActiveWhoLogPath()
    {
        if (!string.IsNullOrWhiteSpace(_whoPath))
        {
            return _whoPath;
        }

        var viewed = ViewedLoadout;
        if (viewed is null || string.IsNullOrWhiteSpace(GameDirectory))
        {
            return null;
        }

        return WhoLog.PathFor(GameDirectory, viewed.Id.Character, viewed.Id.Server);
    }

    private void ApplyExternalChanges()
    {
        if (_disposed)
        {
            return;
        }

        var paths = _pendingGameChanges.ToList();
        _pendingGameChanges.Clear();
        var retry = new List<string>();
        var loadouts = new List<string>();
        var spells = false;
        var abilities = false;
        var resolution = false;
        var sharedBagsChanged = false;
        foreach (var path in paths)
        {
            if (_suppressedWrites.Contains(path))
            {
                continue;
            }

            switch (GameFolderWatch.Classify(GameDirectory, path))
            {
                case WatchedGameFile.LoadoutIni:
                    _sharedInventoryBags = null;
                    if (MatchLoadedPath(path) is { } loaded)
                    {
                        loadouts.Add(loaded);
                    }
                    else if (Path.GetFileName(path).StartsWith("UI_", StringComparison.Ordinal))
                    {
                        sharedBagsChanged = true;
                    }

                    break;
                case WatchedGameFile.ClientSettings:
                    resolution = true;
                    break;
                case WatchedGameFile.SpellList:
                case WatchedGameFile.SpellMessages:
                    if (_spellCatalog is not null)
                    {
                        spells = true;
                    }

                    break;
                case WatchedGameFile.StringTable:
                    if (_spellCatalog is not null)
                    {
                        spells = true;
                    }

                    if (_abilityCatalog is not null)
                    {
                        abilities = true;
                    }

                    break;
                case WatchedGameFile.WhoLog:
                    MarkLogDirty(path);
                    break;
            }
        }

        if (spells)
        {
            _spellCatalog = null;
            _spellCatalogDirectory = null;
        }

        if (abilities)
        {
            _abilityCatalog = null;
            _abilityCatalogDirectory = null;
        }

        if (resolution)
        {
            var previousWidth = ScreenWidth;
            var previousHeight = ScreenHeight;
            var previousResolution = _resolutionFromGame;
            ApplyClientResolution();
            resolution = previousResolution != _resolutionFromGame
                || previousWidth != ScreenWidth
                || previousHeight != ScreenHeight;
        }

        var reloaded = new List<string>();
        foreach (var path in loadouts)
        {
            switch (TryReloadLoadout(path))
            {
                case ExternalRead.Reloaded:
                    reloaded.Add(path);
                    break;
                case ExternalRead.Retry:
                    retry.Add(path);
                    break;
            }
        }

        if (reloaded.Count > 0)
        {
            ResetHistoryAfterReload();
            ResetSocialComparison();
            RefreshView();
            Status = ReloadedStatus(reloaded);
        }
        else if (sharedBagsChanged)
        {
            RefreshView();
        }
        else if (resolution || spells || abilities)
        {
            if (spells && ShowSpells)
            {
                ReloadSpells();
                RefreshSpellSearch();
            }

            if (abilities && ShowHotbar)
            {
                FillHotbarPage();
            }

            if (resolution && ShowPlacement && EditableSection() is { } section)
            {
                LoadPositionFields(section);
            }

            RebuildCanvas();
        }

        if (retry.Count > 0 && _externalRetries < 5)
        {
            _externalRetries++;
            foreach (var path in retry)
            {
                _pendingGameChanges.Add(path);
            }

            _externalReload.Start();
            return;
        }

        _externalRetries = 0;
    }

    private string? MatchLoadedPath(string changedPath)
    {
        if (_files.ContainsKey(changedPath))
        {
            foreach (var path in _files.Keys)
            {
                if (path.Equals(changedPath, StringComparison.OrdinalIgnoreCase))
                {
                    return path;
                }
            }
        }

        string full;
        try
        {
            full = Path.GetFullPath(changedPath);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException)
        {
            return null;
        }

        foreach (var path in _files.Keys)
        {
            try
            {
                if (string.Equals(Path.GetFullPath(path), full, StringComparison.OrdinalIgnoreCase))
                {
                    return path;
                }
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException)
            {
            }
        }

        return null;
    }

    private ExternalRead TryReloadLoadout(string path)
    {
        if (_suppressedWrites.Contains(path) || !_files.ContainsKey(path))
        {
            return ExternalRead.Unchanged;
        }

        if (!File.Exists(path))
        {
            return ExternalRead.Retry;
        }

        DateTime stamp;
        try
        {
            stamp = File.GetLastWriteTimeUtc(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ExternalRead.Retry;
        }

        if (_fileStamps.TryGetValue(path, out var known) && known == stamp)
        {
            return ExternalRead.Unchanged;
        }

        LoadedIniFile loaded;
        try
        {
            loaded = IniFile.Load(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ExternalRead.Retry;
        }

        _files[path] = loaded;
        _fileStamps[path] = stamp;
        _baselines.Remove(path);
        if (_sourceCharacter is not null && SamePath(_sourceCharacter.Path, path))
        {
            _sourceCharacter = loaded;
        }

        if (_sourceUi is not null && SamePath(_sourceUi.Path, path))
        {
            _sourceUi = loaded;
        }

        if (_destCharacter is not null && SamePath(_destCharacter.Path, path))
        {
            _destCharacter = loaded;
        }

        if (_destUi is not null && SamePath(_destUi.Path, path))
        {
            _destUi = loaded;
        }

        return ExternalRead.Reloaded;
    }

    private void ResetHistoryAfterReload()
    {
        _pending = null;
        _pendingByContext = new Dictionary<string, Dictionary<string, IniSection>>(StringComparer.Ordinal);
        _touchedPending.Clear();
        _redo.Clear();
        _undo.Clear();
        _undo.Add(CaptureState("reloaded"));
        UpdateDirtyState();
    }

    private static string ReloadedStatus(IReadOnlyList<string> paths)
    {
        var names = paths.Select(Path.GetFileName).Where(name => !string.IsNullOrEmpty(name)).ToList();
        var subject = names.Count switch
        {
            0 => "The file was",
            1 => $"{names[0]} was",
            2 => $"{names[0]} and {names[1]} were",
            _ => string.Create(CultureInfo.InvariantCulture, $"{names.Count} files were"),
        };
        return subject + " reloaded from disk. Undo was cleared.";
    }

    private void SuppressOwnWrites(IEnumerable<PendingFileWrite> writes)
    {
        foreach (var write in writes)
        {
            _suppressedWrites.Add(write.Path);
        }
    }

    private void ReleaseOwnWrites(IEnumerable<PendingFileWrite> writes)
    {
        foreach (var write in writes)
        {
            _suppressedWrites.Remove(write.Path);
            try
            {
                if (File.Exists(write.Path))
                {
                    _ownWriteStamps[write.Path] = File.GetLastWriteTimeUtc(write.Path);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private void RememberOwnStamps(IReadOnlyList<string> paths)
    {
        foreach (var path in paths)
        {
            try
            {
                if (File.Exists(path))
                {
                    _fileStamps[path] = File.GetLastWriteTimeUtc(path);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static bool SamePath(string left, string right)
        => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private bool IsOpenCharacterFile(string path)
        => (_sourceCharacter is not null && SamePath(_sourceCharacter.Path, path))
            || (_destCharacter is not null && SamePath(_destCharacter.Path, path));

    private bool IsSourceFile(string path, LoadoutFileKind kind)
    {
        var source = kind == LoadoutFileKind.Character ? SourceLoadout?.CharacterFile : SourceLoadout?.UiFile;
        return source is not null && SamePath(source, path);
    }

    private enum ExternalRead
    {
        Unchanged,
        Reloaded,
        Retry,
    }

    private void ReloadInventory(bool announce)
    {
        var viewed = ViewedLoadout;
        _inventory = viewed is null ? null : InventoryExport.TryLoad(GameDirectory, viewed.Id.Character, viewed.Id.Server);
        foreach (var item in _sections)
        {
            item.Detail = item.Kind == LoadoutFileKind.Ui ? BagDetail(item.Name) : string.Empty;
            item.BagCaption = item.Kind == LoadoutFileKind.Ui ? BagCaption(item.Name) : string.Empty;
        }

        UpdateBagSlotsNote(viewed);
        RebuildCanvas();
        if (announce && _inventory is not null)
        {
            Status = $"Bag slots updated from {InventoryExport.FileName(viewed!.Id.Character, viewed.Id.Server)}.";
        }
    }

    private void UpdateBagSlotsNote(DiscoveredLoadout? viewed)
    {
        if (viewed is null)
        {
            BagSlotsText = string.Empty;
            BagSlotsHelp = string.Empty;
            BagSlotsStale = false;
            InventoryReminderText = string.Empty;
            return;
        }

        var who = $"{viewed.Id.Character} on {FormatServerName(viewed.Id.Server)}";
        var file = InventoryExport.FileName(viewed.Id.Character, viewed.Id.Server);
        var source = $"Bag names and slot counts come from {file}, which the game writes to its folder when you type the command below "
            + $"in the EverQuest chat box while logged in to {who}.";
        const string refresh = "Run it again whenever you swap bags. This window picks up the new file automatically.";
        if (_inventory is null)
        {
            BagSlotsText = "No inventory export";
            BagSlotsHelp = $"No inventory export was found for {who}, so bag slots can't be shown. Click for how to create one.";
            BagSlotsStale = true;
            InventoryReminderText = $"{source}\n\nNo export has been found yet. {refresh}";
            return;
        }

        var age = DateTime.Now - _inventory.WrittenAt;
        BagSlotsText = _inventory.WrittenAt.ToString("g", CultureInfo.CurrentCulture);
        BagSlotsHelp = string.Create(CultureInfo.CurrentCulture, $"Inventory export last updated {_inventory.WrittenAt:g}. ")
            + "If you have swapped bags since then, the slot counts are out of date. Click for how to update it.";
        BagSlotsStale = age > TimeSpan.FromDays(7);
        InventoryReminderText = string.Create(CultureInfo.CurrentCulture, $"{source}\n\nLast updated {_inventory.WrittenAt:f}. {refresh}");
    }

    private string BagDetail(string section)
    {
        if (_inventory?.Find(section) is not { } bag)
        {
            return string.Empty;
        }

        return string.Create(CultureInfo.InvariantCulture, $"{bag.Slots} slots, {bag.Used} used");
    }

    private string BagCaption(string section)
    {
        if (!section.StartsWith("BagInv", StringComparison.Ordinal) && !section.StartsWith("BagBank", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        var number = ShortBagLabel(section);
        return _inventory?.Find(section) is { ItemName.Length: > 0 } bag ? $"{number} {bag.ItemName}" : $"Bag {number}";
    }

    private static string FormatServerName(string server)
        => server.Length == 0 ? server : char.ToUpperInvariant(server[0]) + server[1..].ToLowerInvariant();

    private void RebuildSections()
    {
        var seen = new HashSet<(string Name, LoadoutFileKind Kind)>();
        var items = new List<SectionItem>();
        var chatWindows = ChatLayout.Read(ViewedChatManager());
        var layouts = _sourceUi is null
            ? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            : ActiveLayout.Read(_sourceUi.Document, _skinName);
        AddSections(items, seen, _sourceCharacter?.Document, LoadoutFileKind.Character, chatWindows, layouts);
        AddSections(items, seen, _sourceUi?.Document, LoadoutFileKind.Ui, chatWindows, layouts);
        if (_sourceUi is not null)
        {
            foreach (var name in SharedInventoryBags())
            {
                if (!seen.Add((name, LoadoutFileKind.Ui)))
                {
                    continue;
                }

                items.Add(new SectionItem
                {
                    Name = name,
                    Kind = LoadoutFileKind.Ui,
                    Group = InventoryGroup,
                    GroupOrder = 0,
                    Caption = SectionCaption(name, LoadoutFileKind.Ui, chatWindows),
                });
            }
        }

        _sections.Clear();
        foreach (var item in items)
        {
            if (IsAll && BlockReasonFor(item.Name) is { } reason)
            {
                item.Block(reason);
            }

            _sections.Add(item);
        }

        RebuildGroupToggles();
        RebuildSectionRows();
        Raise(nameof(ShowMacrosTab));
        Raise(nameof(ShowSpellLoadoutsTab));
    }

    public bool ShowMacrosTab => _sections.Any(section => section.Name == "Socials");

    public bool ShowSpellLoadoutsTab => _sections.Any(section => section.Name == "SpellLoadouts");

    public bool OpenCharacterEditor(string name)
    {
        var section = _sections.FirstOrDefault(item => item.Name == name && IsEditorPick(item));
        return section is not null && TrySelectSection(section);
    }

    public void ToggleGroup(GroupToggle group) => SetGroupShown(group.Name, group.CheckState != true);

    public void ToggleSectionGroupExpanded(GroupToggle group)
    {
        group.IsExpanded = !group.IsExpanded;
        SaveExpandedSectionGroups();
        SectionRowsView.Refresh();
    }

    private void ExpandSectionGroup(string groupName)
    {
        var group = Groups.FirstOrDefault(item => item.Name == groupName);
        if (group is not { IsExpanded: false })
        {
            return;
        }

        group.IsExpanded = true;
        SaveExpandedSectionGroups();
        SectionRowsView.Refresh();
    }

    private void SaveExpandedSectionGroups()
        => _store.SetExpandedSectionGroups(Groups.Where(item => item.IsExpanded).Select(item => item.Name));

    public EditorWindowPlacement EditorWindowPlacement => _store.EditorWindow;

    public void RememberEditorWindow(double left, double top, double width, double height, bool maximized)
        => _store.SetEditorWindow(left, top, width, height, maximized);

    public void ResetApplicationToDefaults()
    {
        _store.ResetToDefaults();
        _isDarkTheme = true;
        ThemeManager.Apply("Dark");
        Raise(nameof(IsDarkTheme));

        var snap = SnapOptions.First(option => option.Size == 10);
        if (!ReferenceEquals(_selectedSnap, snap))
        {
            SelectedSnap = snap;
        }
        else
        {
            Raise(nameof(SelectedSnap));
            Raise(nameof(SnapGridSize));
        }

        ShowRaw = false;
        Raise(nameof(ShowInventoryReminder));
        Raise(nameof(ShowSaveCharacterSelectWarning));
        Raise(nameof(ShowRawKeys));
        Raise(nameof(SaveModifiedCopies));
        Raise(nameof(ShowHotButtons));
        Raise(nameof(HorizontalStackSpacing));
        Raise(nameof(HorizontalStackSpacingText));
        Raise(nameof(VerticalStackSpacing));
        Raise(nameof(VerticalStackSpacingText));
        SectionRowsView.Refresh();

        if (!string.Equals(_gameDirectory, LoadoutDiscovery.DefaultGameDirectory, StringComparison.OrdinalIgnoreCase))
        {
            GameDirectory = LoadoutDiscovery.DefaultGameDirectory;
        }

        _files.Clear();
        _fileStamps.Clear();
        _baselines.Clear();
        Discover();
        ResetHistoryAfterReload();
        LoadSpellIconSelection();
        ReloadSpellIcons();
        Status = "Application settings were reset to their defaults.";
    }

    public void SetItemShown(SectionItem item, bool shown)
    {
        if (!item.CanToggleShown)
        {
            return;
        }

        if (shown && SpellWindows.IsWindow(item.Name))
        {
            foreach (var other in _sections)
            {
                if (SpellWindows.IsWindow(other.Name) && !ReferenceEquals(other, item))
                {
                    other.SetShown(false);
                }
            }
        }

        item.SetShown(shown);
        SyncShownKeys();
        UpdateGroupToggles();
        RebuildCanvas();
        SaveVisibility();
    }

    private void ApplyItemVisibility()
    {
        IReadOnlyList<string>? savedItems = null;
        IReadOnlyList<string> savedGroups = [];
        IReadOnlyList<string> savedHidden = [];
        var hasRecord = ViewedLoadout is { } loadout
            && _store.TryGetVisibility(loadout.Id, out savedItems, out savedGroups, out savedHidden);
        var items = savedItems is null ? null : new HashSet<string>(savedItems, StringComparer.Ordinal);
        var groups = new HashSet<string>(savedGroups, StringComparer.Ordinal);
        var hidden = new HashSet<string>(savedHidden, StringComparer.Ordinal);
        var inventoryShown = items?.Any(key => key.StartsWith("Ui:BagInv", StringComparison.Ordinal)) == true;
        foreach (var item in _sections)
        {
            var key = ItemKey(item);
            var shown = !hasRecord
                ? DefaultVisibleGroups.Contains(item.Group)
                : items is not null
                    ? items.Contains(key)
                    : groups.Contains(item.Group)
                        || (item.Group == "Macros" && groups.Contains("Socials"))
                        || (item.Group == SpellGemsGroup && groups.Contains("Spell gems"))
                        || (item.Group == MiscWindowsGroup && groups.Contains("Windows"));
            if (!shown
                && inventoryShown
                && !hidden.Contains(key)
                && IsMissingSharedBag(item))
            {
                shown = true;
            }

            item.SetShown(shown && item.CanToggleShown);
        }

        KeepOneShownCastSpell();
        SyncShownKeys();
        UpdateGroupToggles();
    }

    private void RebuildGroupToggles()
    {
        var groups = _sections
            .GroupBy(item => item.Group, StringComparer.Ordinal)
            .Select(group => (Name: group.Key, Order: group.Min(item => item.GroupOrder)))
            .OrderBy(group => group.Order)
            .ThenBy(group => group.Name, StringComparer.Ordinal);
        Groups.Clear();
        foreach (var group in groups)
        {
            Groups.Add(new GroupToggle(group.Name)
            {
                IsExpanded = _store.IsSectionGroupExpanded(group.Name),
            });
        }
    }

    private void UpdateGroupToggles()
    {
        foreach (var toggle in Groups)
        {
            var items = _sections.Where(item => item.Group == toggle.Name && item.CanToggleShown).ToList();
            var shown = items.Count(item => item.IsShown);
            if (!IsSpellGemsGroup(toggle.Name))
            {
                toggle.SetState(shown, items.Count);
                continue;
            }

            // One CastSpell window is the only allowed selection, so that state fills the group toggle.
            var spells = items.Where(item => SpellWindows.IsWindow(item.Name)).ToList();
            var spellShown = spells.Count(item => item.IsShown);
            bool? state = false;
            if (spellShown == 1 || (spellShown > 1 && spellShown == spells.Count))
            {
                state = true;
            }
            else if (spellShown > 1)
            {
                state = null;
            }

            toggle.SetState(shown, items.Count, state);
        }
    }

    private static bool IsSpellGemsGroup(string name)
        => string.Equals(name, SpellGemsGroup, StringComparison.Ordinal)
            || string.Equals(name, "Spell gems", StringComparison.Ordinal);

    private void RebuildSectionRows()
    {
        _sectionRows.Clear();
        foreach (var group in Groups)
        {
            _sectionRows.Add(new SectionRow { Group = group });
            var items = _sections
                .Where(section => section.Group == group.Name)
                .OrderBy(section => SectionListRank(group.Name, section.Name))
                .ThenBy(section => group.Name == MiscWindowsGroup ? WindowListKey(section) : section.SortName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var nested = new HashSet<SectionItem>();
            foreach (var item in items)
            {
                if (ChildOf(item) is { } child && items.Contains(child))
                {
                    nested.Add(child);
                }
            }

            foreach (var item in items)
            {
                if (nested.Contains(item))
                {
                    continue;
                }

                _sectionRows.Add(new SectionRow { Item = item });
                if (ChildOf(item) is { } child && nested.Contains(child))
                {
                    _sectionRows.Add(new SectionRow { Item = child, IsChild = true });
                }
            }
        }

        HasEditorRows = _sections.Any(IsEditorPick);
    }

    /// <summary>The character sections edited from the top of the section list.</summary>
    public static bool IsEditorPick(SectionItem? section)
        => section?.Name is "Socials" or "SpellLoadouts";

    /// <summary>Spell loadouts lead their group, and the Bank window leads the bank bags.</summary>
    private static int SectionListRank(string group, string name)
    {
        if (group == SpellGemsGroup && name == "SpellLoadouts")
        {
            return 0;
        }

        if (group == "Bank bags" && name == "BankWnd")
        {
            return 0;
        }

        return 1;
    }

    /// <summary>Leaves macros or spell loadouts and opens the window layout that was showing before them.</summary>
    public void ReturnToLayout()
    {
        var names = _layoutReturn
            .Where(name => _sections.Any(section => section.Kind == LoadoutFileKind.Ui && section.Name == name))
            .ToList();
        if (names.Count > 0)
        {
            ApplyLayoutOrder(names);
            return;
        }

        if (!SelectGroupLayout(InventoryGroup, save: false))
        {
            SelectSection(_sections.FirstOrDefault(item => item.Kind == LoadoutFileKind.Ui && !item.IsBlocked));
        }
    }

    /// <summary>Friendly window title, with a trailing number padded so 2 sorts before 10.</summary>
    private static string WindowListKey(SectionItem item)
    {
        var text = item.ListText;
        var index = text.Length;
        while (index > 0 && char.IsDigit(text[index - 1]))
        {
            index--;
        }

        if (index < text.Length
            && index > 0
            && text[index - 1] == ' '
            && int.TryParse(text.AsSpan(index), NumberStyles.None, CultureInfo.InvariantCulture, out var number))
        {
            return string.Create(CultureInfo.InvariantCulture, $"{text[..index]}{number:0000}");
        }

        return text;
    }

    private SectionItem? ChildOf(SectionItem item)
    {
        var name = HotButtonText.ButtonsSection(item.Name);
        if (name is null)
        {
            return null;
        }

        return _sections.FirstOrDefault(section =>
            section.Group == item.Group
            && section.Name == name
            && HotButtonText.IsHotbarSection(section.Name));
    }

    private SectionItem? ParentOf(SectionItem item)
    {
        var name = HotButtonText.WindowSection(item.Name);
        if (name is null)
        {
            return null;
        }

        return _sections.FirstOrDefault(section =>
            section.Group == item.Group
            && section.Name == name
            && HotButtonText.IsHotButtonWindow(section.Name));
    }

    private IReadOnlyDictionary<string, int> LayoutVariants()
        => _sourceUi is null
            ? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            : ActiveLayout.Read(_sourceUi.Document, _skinName);

    /// <summary>A saved selection of several gem windows keeps the display type named by CastSpellWnd.</summary>
    private void KeepOneShownCastSpell()
    {
        var spells = _sections.Where(item => SpellWindows.IsWindow(item.Name)).ToList();
        if (spells.Count(item => item.IsShown) <= 1)
        {
            return;
        }

        var layouts = LayoutVariants();
        foreach (var item in spells)
        {
            item.SetShown(ActiveLayout.IsActive(item.Name, layouts));
        }
    }

    /// <summary>The last gem window in the selection is the one that stays selected.</summary>
    private static List<string> OneCastSpell(List<string> names)
    {
        string? keep = null;
        var count = 0;
        foreach (var name in names)
        {
            if (!SpellWindows.IsWindow(name))
            {
                continue;
            }

            keep = name;
            count++;
        }

        if (keep is null || count <= 1)
        {
            return names;
        }

        return names.Where(name => !SpellWindows.IsWindow(name) || name == keep).ToList();
    }

    private void SetGroupShown(string name, bool shown, bool save = true)
    {
        var changed = false;
        var layouts = LayoutVariants();
        foreach (var item in _sections)
        {
            if (item.Group != name || !item.CanToggleShown)
            {
                continue;
            }

            var next = shown && (name != SpellGemsGroup || ActiveLayout.IsActive(item.Name, layouts));
            if (item.IsShown == next)
            {
                continue;
            }

            item.SetShown(next);
            changed = true;
        }

        if (!changed)
        {
            return;
        }

        SyncShownKeys();
        UpdateGroupToggles();
        RebuildCanvas();
        if (save)
        {
            SaveVisibility();
        }
    }

    private void SyncShownKeys()
    {
        _shownKeys.Clear();
        foreach (var item in _sections)
        {
            if (item.IsShown)
            {
                _shownKeys.Add(ItemKey(item));
            }
        }
    }

    private void SaveVisibility()
    {
        if (ViewedLoadout is not { } loadout)
        {
            return;
        }

        _store.SetVisibleItems(
            loadout.Id,
            _sections.Where(item => item.IsShown).Select(ItemKey),
            _sections.Where(item => !item.IsShown && IsMissingSharedBag(item)).Select(ItemKey));
    }

    private bool IsMissingSharedBag(SectionItem item)
        => item.Kind == LoadoutFileKind.Ui
            && InventoryBags.IsName(item.Name)
            && _sourceUi?.Document.Find(item.Name) is null;

    private bool IsSharedInventoryBag(string name)
        => InventoryBags.IsName(name) && SharedInventoryBags().Contains(name, StringComparer.Ordinal);

    private IReadOnlyList<string> SharedInventoryBags()
    {
        if (_sharedInventoryBags is not null)
        {
            return _sharedInventoryBags;
        }

        var names = _catalog
            .Select(loadout => loadout.UiFile)
            .Where(path => !string.IsNullOrEmpty(path))
            .Select(path => InventoryBagNames(path!));
        _sharedInventoryBags = InventoryBags.Union(names);
        return _sharedInventoryBags;
    }

    private IReadOnlyList<string> InventoryBagNames(string path)
    {
        if (_files.TryGetValue(path, out var loaded))
        {
            return loaded.Document.Sections.Select(section => section.Name).Where(InventoryBags.IsName).ToArray();
        }

        try
        {
            return InventoryBags.NamesIn(File.ReadLines(path)).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string ItemKey(SectionItem item) => ItemKey(item.Kind, item.Name);

    private static string ItemKey(LoadoutFileKind kind, string name)
        => string.Create(CultureInfo.InvariantCulture, $"{kind}:{name}");

    private static void AddSections(
        List<SectionItem> items,
        HashSet<(string Name, LoadoutFileKind Kind)> seen,
        IniDocument? document,
        LoadoutFileKind kind,
        IReadOnlyList<NamedChatWindow> chatWindows,
        IReadOnlyDictionary<string, int> layouts)
    {
        if (document is null)
        {
            return;
        }

        foreach (var section in document.Sections)
        {
            if (IsCombineSection(section.Name) || section.Name == "ChatManager" || !seen.Add((section.Name, kind)))
            {
                continue;
            }

            var (group, order) = GroupFor(section);
            var isChatContainer = group == "Chat" && ChatLayout.TryContainer(section.Name, out _);
            if ((group is MiscWindowsGroup or "Other" || isChatContainer)
                && !ActiveLayout.AppearsOnLayout(
                    section,
                    layouts,
                    CategoryFor(section.Name) == "window",
                    ChatLayout.HasTabs(chatWindows, section.Name)))
            {
                continue;
            }

            items.Add(new SectionItem
            {
                Name = section.Name,
                Kind = kind,
                Group = group,
                GroupOrder = order,
                Caption = SectionCaption(section.Name, kind, chatWindows),
            });
        }
    }

    private static (string Group, int Order) GroupFor(IniSection section)
    {
        var name = section.Name;
        if (name.StartsWith("BagInv", StringComparison.Ordinal))
        {
            return ("Inventory bags", 0);
        }

        if (name == "BankWnd" || name.StartsWith("BagBank", StringComparison.Ordinal) || name.StartsWith("BagWorld", StringComparison.Ordinal))
        {
            return ("Bank bags", 1);
        }

        if (name is "ChatManager" or "MainChat" || name.StartsWith("Chat", StringComparison.Ordinal))
        {
            return ("Chat", 2);
        }

        if (name.StartsWith("HotButton", StringComparison.Ordinal))
        {
            return ("Hotbars", 3);
        }

        if (name == "Socials")
        {
            return ("Macros", 4);
        }

        if (name == "SpellLoadouts" || SpellWindows.IsWindow(name))
        {
            return (SpellGemsGroup, 5);
        }

        if (WindowPlacement.HasPosition(section))
        {
            return (MiscWindowsGroup, 6);
        }

        return ("Other", 7);
    }

    private bool FilterSectionRow(object item)
    {
        if (item is not SectionRow row)
        {
            return false;
        }

        if (row.Item is { } section)
        {
            if (IsEditorPick(section) || !IncludeSectionRow(section))
            {
                return false;
            }

            // A search should still reveal matches inside a collapsed group.
            return SectionFilterActive || IsGroupExpanded(section.Group);
        }

        return row.Group is { } group
            && _sections.Any(section => section.Group == group.Name && !IsEditorPick(section) && IncludeSectionRow(section));
    }

    private bool FilterEditorRow(object item)
        => item is SectionRow { Item: { } section } && IsEditorPick(section);

    private static bool FilterBarGroup(object item)
        => item is GroupToggle toggle && toggle.Name != "Macros";

    private bool IncludeSectionRow(SectionItem section)
        => ShowsHotButtonRow(section) && SectionMatchesFilter(section);

    /// <summary>HotButtons rows are hidden unless a debug build opts in. HotButtonWnd rows stay listed.</summary>
    private bool ShowsHotButtonRow(SectionItem section)
        => !HotButtonText.IsHotbarSection(section.Name) || (IsDebugBuild && _store.Current.ShowHotButtons);

    private bool IsGroupExpanded(string name)
        => Groups.FirstOrDefault(group => group.Name == name)?.IsExpanded == true;

    private bool SectionMatchesFilter(SectionItem section)
    {
        if (!SectionFilterActive)
        {
            return true;
        }

        if (NameMatches(section))
        {
            return true;
        }

        return ParentOf(section) is { } parent && NameMatches(parent)
            || ChildOf(section) is { } child && NameMatches(child);
    }

    private bool NameMatches(SectionItem section)
    {
        var filter = SectionFilter.Trim();
        return section.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || section.ListText.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    public SectionRow? RowForLayout(string name)
        => _sectionRows.FirstOrDefault(row => row.Item is { Kind: LoadoutFileKind.Ui } item && item.Name == name);

    private void SelectSection(SectionItem? item, bool resetLayout = true)
    {
        if (item is { IsBlocked: true } && !IsEditorPick(item))
        {
            Status = item.BlockReason;
            return;
        }

        if (IsEditorPick(item) && !IsEditorPick(_selectedSection) && _layoutSelection.Count > 0)
        {
            _layoutReturn = _layoutSelection.ToList();
        }

        _selectedSection = item;
        Raise(nameof(SelectedSection));
        Raise(nameof(HasSelection));
        if (resetLayout)
        {
            ReplaceLayoutSelection(item);
        }

        _pending = null;
        BuildEditor();
        UpdateCopyState();
        SectionSelectionChanged?.Invoke();
    }

    private void BuildEditor()
    {
        MutedDepth++;
        try
        {
            ShowMissing = false;
            ShowSocials = false;
            ShowHotbar = false;
            ShowSpells = false;
            ShowLayout = true;
            ShowPlacement = false;
            ShowChatFilters = false;
            ShowRaw = false;
            ShowEditorUnavailable = false;
            EditorUnavailableMessage = string.Empty;
            EditorNote = string.Empty;
            ClearSocialBoard();
            HotbarSlots.Clear();
            HotbarPages.Clear();
            SpellRows.Clear();
            RawRows.Clear();

            if (_selectedSection is null)
            {
                ClearPositionFields();
                RaiseEditorVisibility();
                return;
            }

            if (IsEditorPick(_selectedSection) && _selectedSection.IsBlocked)
            {
                ShowEditorUnavailable = true;
                EditorUnavailableMessage = "This functionality isn't available when the destination is for ALL loadouts.";
                RaiseEditorVisibility();
                return;
            }

            var source = Document(editingDestination: false, _selectedSection.Kind)?.Find(_selectedSection.Name);
            if (IsAll)
            {
                _pending = _pendingByContext.TryGetValue(PendingContext, out var saved)
                    && saved.TryGetValue(ItemKey(_selectedSection), out var existing)
                        ? existing
                        : source?.Clone();
            }

            var section = EditableSection();
            if (section is null && _selectedSection.Name == "Socials")
            {
                ShowMissing = false;
                ShowSocials = true;
                StructuredTabTitle = "Macros";
                EditorNote = "The destination loadout has no macros yet. Drag the ones you want across, then apply. Save writes the file.";
                BuildSocialBoard();
                RaiseEditorVisibility();
                return;
            }

            ShowMissing = section is null;
            if (section is null)
            {
                EditorNote = "The source loadout does not have this section.";
                RaiseEditorVisibility();
                return;
            }

            ShowRaw = IsDebugBuild && _store.Current.ShowRawKeys;
            ShowSocials = section.Name == "Socials";
            ShowHotbar = HotButtonText.IsHotbarSection(section.Name);
            ShowSpells = section.Name == "SpellLoadouts";
            ShowLayout = true;
            ShowPlacement = ShowLayout && (WindowPlacement.HasPosition(section) || CategoryFor(section.Name) != "window");
            StructuredTabTitle = ShowSocials ? "Macros" : ShowHotbar ? "Hotbar" : ShowSpells ? SpellGemsGroup : string.Empty;
            EditorNote = NoteFor(section.Name);
            var modeNote = IsAll
                ? "Window changes are written to every loadout in the destination, including this one, when you save."
                : "Edits change the source loadout. Use Save to send sections to the destination.";
            EditorNote = string.IsNullOrEmpty(EditorNote) ? modeNote : EditorNote + " " + modeNote;

            if (ShowPlacement)
            {
                LoadPositionFields(section);
            }

            if (ShowSocials)
            {
                BuildSocialBoard();
            }

            if (ShowHotbar)
            {
                BuildHotbarPages(section);
            }

            if (ShowSpells)
            {
                ReloadSpells();
            }

            LoadChatFilters(section);
            ReloadRaw();
            RaiseEditorVisibility();
        }
        finally
        {
            if (MutedDepth > 0) MutedDepth--;
        }
    }

    private void RaiseEditorVisibility()
    {
        Raise(nameof(ShowStructured));
        Raise(nameof(ShowLayout));
        Raise(nameof(ShowChatFilters));
    }

    private void LoadChatFilters(IniSection section)
    {
        ChatFilters.Clear();
        if (!ChatLayout.TryContainer(section.Name, out _))
        {
            ShowChatFilters = false;
            return;
        }

        foreach (var window in ChatLayout.Tabs(ChatLayout.Read(ViewedChatManager()), section.Name))
        {
            ChatFilters.Add(new ChatFilterGroup
            {
                Name = window.Name,
                Filters = string.Join(", ", window.Filters),
            });
        }

        ShowChatFilters = ChatFilters.Count > 0;
    }

    private IniSection? ViewedChatManager()
    {
        if (PendingFor(LoadoutFileKind.Ui, "ChatManager") is { } pending)
        {
            return pending;
        }

        return _sourceUi?.Document.Find("ChatManager");
    }

    private static string NoteFor(string name)
    {
        if (name.StartsWith("BagInv", StringComparison.Ordinal))
        {
            return "Inventory bag location and size. Drag the bag to move it, or drag an edge to resize it.";
        }

        if (name.StartsWith("BagBank", StringComparison.Ordinal) || name.StartsWith("BagWorld", StringComparison.Ordinal))
        {
            return "Bank bag location and size. Drag the bag to move it, or drag an edge to resize it.";
        }

        if (name == "ChatManager")
        {
            return "Chat window names and filters are stored in this section. Select a chat window to see the name and the filters currently sent to it.";
        }

        if (name == "MainChat" || name.StartsWith("Chat ", StringComparison.Ordinal))
        {
            return "The tabs and filters come from ChatManager. Copying this window also copies which tabs it shows.";
        }

        if (name == "Socials")
        {
            return "Drag a macro from the source onto a destination button, or into Changes. Green means the same macro, blue means a new one, and Changes lists copied, new, modified, and deleted destination macros.";
        }

        return string.Empty;
    }

    private static string SectionCaption(string name, LoadoutFileKind kind, IReadOnlyList<NamedChatWindow> chatWindows)
    {
        return kind == LoadoutFileKind.Ui ? ChatLayout.Caption(chatWindows, name) : string.Empty;
    }

    private void RefreshSocialCaptions()
    {
        SelectedSocial?.RefreshCaption();
        foreach (var button in SourceSocialPages.SelectMany(group => group.Buttons)
                     .Concat(DestinationSocialPages.SelectMany(group => group.Buttons)))
        {
            button.RefreshCaption();
        }
    }

    private void BuildHotbarPages(IniSection section)
    {
        var pages = section.Entries
            .Select(entry => PageNumber(entry.Key))
            .Where(page => page > 0)
            .Distinct()
            .Order()
            .ToList();
        if (pages.Count == 0)
        {
            pages.Add(1);
        }

        foreach (var page in pages)
        {
            HotbarPages.Add(page);
        }

        _selectedHotbarPage = HotbarPages[0];
        Raise(nameof(SelectedHotbarPage));
        FillHotbarPage();
    }

    private void FillHotbarPage()
    {
        var section = EditableSection();
        if (section is null || !HotButtonText.IsHotbarSection(section.Name))
        {
            return;
        }

        MutedDepth++;
        HotbarSlots.Clear();
        var faces = HotButtonText.ReadPage(
            section,
            PendingFor(LoadoutFileKind.Character, "Socials") ?? _sourceCharacter?.Document.Find("Socials"),
            SelectedHotbarPage,
            MemorizedGems(),
            Abilities().ButtonName,
            SpellButton);
        for (var slot = 1; slot <= 12; slot++)
        {
            var row = new HotbarRow(SelectedHotbarPage, slot, section, MarkEdited);
            var raw = section.Get(row.Key) ?? string.Empty;
            var unnamed = HotButtonText.ReadLabel(raw).Length == 0 ? faces[slot - 1].Text : string.Empty;
            row.Load(raw, unnamed);
            HotbarSlots.Add(row);
        }

        if (MutedDepth > 0) MutedDepth--;
    }

    private void SpellRowEdited()
    {
        MarkEdited();
        QueueGemUpgrades();
        // The name cell commits inside the grid's edit transaction. Refresh throws until that ends.
        ResortSpellSets(_selectedSpellSet);
    }

    private void ResortSpellSets(SpellRow? edited)
    {
        if (SpellView is IEditableCollectionView view && (view.IsAddingNew || view.IsEditingItem))
        {
            Dispatcher.CurrentDispatcher.BeginInvoke(() => ResortSpellSets(edited), DispatcherPriority.Background);
            return;
        }

        var gem = _selectedGem;
        SpellView.Refresh();
        if (edited is not null && SpellView.Cast<SpellRow>().Contains(edited))
        {
            if (!ReferenceEquals(_selectedSpellSet, edited))
            {
                SelectedSpellSet = edited;
            }

            if (gem is not null && edited.Gems.Contains(gem) && !ReferenceEquals(_selectedGem, gem))
            {
                SelectedGem = gem;
            }
        }
        else if (_selectedSpellSet is null || !FilterSpell(_selectedSpellSet))
        {
            SelectedSpellSet = SpellView.Cast<SpellRow>().FirstOrDefault();
        }
    }

    private void ReloadSpells()
    {
        var section = EditableSection();
        if (section is null || section.Name != "SpellLoadouts")
        {
            return;
        }

        var catalog = Spells();
        var icons = Icons();
        var selectedIndex = _selectedSpellSet?.Index;
        MutedDepth++;
        SpellRows.Clear();
        foreach (var set in SpellGems.Read(section))
        {
            var row = new SpellRow(set.Index, section, SpellRowEdited, catalog, icons, _loadoutClasses, EnsureContentClasses);
            row.UsePowerNames(PowerName, PowerRank);
            row.SetCasterLevel(CasterLevel());
            row.Load(set);
            SpellRows.Add(row);
        }

        if (MutedDepth > 0) MutedDepth--;
        SpellView.Refresh();
        var visible = SpellView.Cast<SpellRow>().ToList();
        SelectedSpellSet = visible.FirstOrDefault(row => row.Index == selectedIndex) ?? visible.FirstOrDefault();
        SpellListNote = catalog.Count == 0
            ? $"Spell names come from {SpellCatalog.FileName} in the game folder, which wasn't found. Gems show their spell IDs only."
            : string.Empty;
        RebuildSpellClasses(preferLoadout: true);
        _advisorKey = string.Empty;
        QueueAdvisor();
        _upgradeKey = string.Empty;
        QueueGemUpgrades();
    }

    public void ApplyGemUpgrade(SpellGem gem)
    {
        if (gem.Upgrade is not SuggestedGemFace face || face.SpellId < 0)
        {
            return;
        }

        var spellId = face.SpellId;
        ClearUpgradeHover(face);
        PlaceSpellOnGem(gem, spellId);
    }

    public void PlaceSpellOnGem(SpellGem gem, int spellId)
    {
        if (SpellSetsStayOnTheEditedLoadout() || !EnsureContentClasses())
        {
            return;
        }

        var catalog = Spells();
        if (!catalog.TryGet(spellId.ToString(CultureInfo.InvariantCulture), out var spell))
        {
            return;
        }

        gem.Choose(new SpellChoice(spell.Id, spell.Name, string.Empty, SpellUnusable(spell)));
    }

    public void SwapGems(SpellGem source, SpellGem target)
    {
        if (SpellSetsStayOnTheEditedLoadout() || !EnsureContentClasses())
        {
            return;
        }

        foreach (var row in SpellRows)
        {
            if (row.Gems.Contains(source) && row.Gems.Contains(target))
            {
                row.Swap(source, target);
                return;
            }
        }
    }

    private void RebuildSpellClasses(bool preferLoadout)
    {
        var previous = _spellClassChoice?.Classes;
        _rebuildingSpellClasses = true;
        SpellClassChoices.Clear();
        SpellClassChoice? match = null;
        foreach (var choice in SpellClassList.Build(_loadoutClasses, ClassHasSpells))
        {
            SpellClassChoices.Add(choice);
            if (match is null && previous is not null && previous.SequenceEqual(choice.Classes))
            {
                match = choice;
            }
        }

        var loadoutChoice = SpellClassChoices.FirstOrDefault(choice => choice.IsLoadout);
        _rebuildingSpellClasses = false;
        var next = (preferLoadout ? loadoutChoice : null) ?? match ?? loadoutChoice ?? SpellClassChoices.FirstOrDefault();
        ApplySpellClassChoice(next);
    }

    private void ApplySpellClassChoice(SpellClassChoice? next)
    {
        if (!ReferenceEquals(next, _spellClassChoice))
        {
            SpellClassChoice = next;
        }
        else
        {
            RefreshSpellSearch();
        }

        // Clearing the item list makes the combo box clear its selection after this returns.
        var chosen = next;
        Dispatcher.CurrentDispatcher.BeginInvoke(() =>
        {
            if (chosen is not null && SpellClassChoices.Contains(chosen) && !ReferenceEquals(SpellClassChoice, chosen))
            {
                SpellClassChoice = chosen;
            }
        });
    }

    private bool ClassHasSpells(int classIndex) => _spellCatalog?.ClassHasSpells(classIndex) == true;

    private void RefreshSpellSearch()
    {
        var selectedId = _selectedSpellHit?.Id;
        SpellSearchResults.Clear();
        var text = _spellSearchText.Trim();
        if (_spellCatalog is null)
        {
            SpellSearchNote = string.Empty;
            KeepSpellHit(selectedId, null);
            return;
        }

        if (_spellCatalog.Count == 0)
        {
            SpellSearchNote = $"Spell names come from {SpellCatalog.FileName} in the game folder, which wasn't found.";
            KeepSpellHit(selectedId, null);
            return;
        }

        if (_spellClassChoice is null)
        {
            SpellSearchNote = "Pick a class.";
            KeepSpellHit(selectedId, null);
            return;
        }

        if (text.Length > 0 && text.All(char.IsDigit)
            && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var level)
            && level is < 1 or > SpellCatalog.MaxSearchLevel)
        {
            SpellSearchNote = $"Type a level from 1 to {SpellCatalog.MaxSearchLevel}.";
            KeepSpellHit(selectedId, null);
            return;
        }

        var classes = _spellClassChoice.Classes;
        var matches = _spellCatalog.SearchForClasses(text, classes, SpellCatalog.MaxSearchLevel, int.MaxValue, _spellLevelDescending, _spellNameAscending, _spellSortByLevel);
        SpellSearchHit? restored = null;
        foreach (var spell in matches)
        {
            var hit = new SpellSearchHit(
                spell.Id,
                PowerName(spell.Name),
                ClassLevel(spell, classes),
                SpellUnusable(spell),
                Icons().For(spell.Icon),
                SpellPresentation.Describe(spell, _spellCatalog.Description(spell.Id), CasterLevel()),
                SpellPresentation.EffectText(spell, _spellCatalog, CasterLevel(), PowerRank(spell.Name)),
                SpellPresentation.Lines(spell, _spellCatalog.SpellLine(spell.Id), CasterLevel(), PowerRank(spell.Name)),
                SpellPresentation.EffectLines(spell, _spellCatalog, CasterLevel(), PowerRank(spell.Name)));
            SpellSearchResults.Add(hit);
            if (selectedId == hit.Id)
            {
                restored = hit;
            }
        }

        KeepSpellHit(selectedId, restored);
        SpellSearchNote = matches.Count == 0
            ? text.Length == 0 ? "No spells for this class." : "No spells match."
            : string.Empty;
    }

    private void KeepSpellHit(int? selectedId, SpellSearchHit? restored)
    {
        if (selectedId is null)
        {
            return;
        }

        SelectedSpellHit = restored;
        var hit = restored;
        Dispatcher.CurrentDispatcher.BeginInvoke(() =>
        {
            if (_selectedGem is not null || hit is null || !SpellSearchResults.Contains(hit) || ReferenceEquals(_selectedSpellHit, hit))
            {
                return;
            }

            SelectedSpellHit = hit;
        });
    }

    private void SelectedGemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SpellGem.IsEmpty) or nameof(SpellGem.Name) or nameof(SpellGem.Description) or nameof(SpellGem.Details))
        {
            Raise(nameof(SelectedSpellDetail));
            Raise(nameof(DisplayedSpellDetail));
        }

        if (e.PropertyName is nameof(SpellGem.Upgrade) or nameof(SpellGem.HasUpgrade))
        {
            Raise(nameof(DisplayedSpellUpgrade));
        }
    }

    /// <summary>
    /// Level the spell window uses. The newest /who for these classes, otherwise the newest /who, otherwise the level cap.
    /// </summary>
    private int CasterLevel()
    {
        if (ViewedLoadout is { } current && WhoScanIsCurrent(current.Id) && WhoLevel(current.Id) is int who)
        {
            return who;
        }

        if (ViewedLoadout is { } viewed && _store.GetLevel(viewed.Id) is int stored)
        {
            return stored;
        }

        if (_whoPath is null || !_whoScans.TryGetValue(_whoPath, out var scan) || scan.Sightings.Count == 0)
        {
            return SpellCatalog.MaxSearchLevel;
        }

        WhoSighting? match = null;
        if (_loadoutClasses.Count > 0)
        {
            foreach (var sighting in scan.Sightings)
            {
                if (SameClasses(sighting.Classes, _loadoutClasses))
                {
                    match = sighting;
                    break;
                }
            }
        }

        var level = (match ?? scan.Sightings[0]).Level;
        return level is >= 1 and <= 125 ? level : SpellCatalog.MaxSearchLevel;
    }

    private void ApplyCasterLevel()
    {
        var level = CasterLevel();
        foreach (var row in SpellRows)
        {
            row.SetCasterLevel(level);
        }

        if (_spellCatalog is not null && _spellClassChoice is not null)
        {
            RefreshSpellSearch();
        }

        SyncSuggestionLevel();
        QueueGemUpgrades();
    }

    private static bool SameClasses(IReadOnlyList<int> left, IReadOnlyList<int> right)
    {
        if (left.Count != right.Count || left.Count == 0)
        {
            return false;
        }

        foreach (var index in left)
        {
            if (!right.Contains(index))
            {
                return false;
            }
        }

        return true;
    }

    private static string ClassLevel(SpellInfo spell, IReadOnlyList<int> classes)
    {
        if (spell.BestWithin(classes, SpellCatalog.MaxSearchLevel) is not { } best)
        {
            return string.Empty;
        }

        return $"{CharacterClasses.Codes[best.Class]} {best.Level}";
    }

    private bool SpellUnusable(SpellInfo spell)
        => _loadoutClasses.Count > 0 && spell.HasClassLevels && _loadoutClasses.All(index => spell.LevelFor(index) is null);

    private SpellCatalog Spells()
    {
        if (_spellCatalog is null || !string.Equals(_spellCatalogDirectory, GameDirectory, StringComparison.OrdinalIgnoreCase))
        {
            _spellCatalog = SpellCatalog.Load(GameDirectory);
            _spellCatalogDirectory = GameDirectory;
        }

        return _spellCatalog;
    }

    private AbilityCatalog Abilities()
    {
        if (_abilityCatalog is null || !string.Equals(_abilityCatalogDirectory, GameDirectory, StringComparison.OrdinalIgnoreCase))
        {
            _abilityCatalog = AbilityCatalog.Load(GameDirectory);
            _abilityCatalogDirectory = GameDirectory;
        }

        return _abilityCatalog;
    }

    public ImageSource? ItemIcon(int icon)
    {
        if (_itemIcons is null || !string.Equals(_itemIconDirectory, GameDirectory, StringComparison.OrdinalIgnoreCase))
        {
            _itemIcons = ItemIcons.Load(GameDirectory);
            _itemIconDirectory = GameDirectory;
        }

        return _itemIcons.For(icon);
    }

    public ImageSource? SpellIcon(int icon) => Icons().For(icon);

    public ImageSource? BookIcon() => UiPieces.Book(GameDirectory);

    public void SelectSpellSet(string windowName, int index)
    {
        if (!SpellWindows.IsWindow(windowName))
        {
            return;
        }

        var section = MutableLayoutSection(windowName);
        if (section is null)
        {
            return;
        }

        var spells = PendingFor(LoadoutFileKind.Character, "SpellLoadouts") ?? _sourceCharacter?.Document.Find("SpellLoadouts");
        var chosen = spells is null
            ? null
            : SpellGems.Read(spells).FirstOrDefault(set => set.Index == index && SpellWindows.IsListed(set));
        if (chosen is null)
        {
            return;
        }

        section.Set(SpellWindows.SetKey, index.ToString(CultureInfo.InvariantCulture));
        MarkEdited(string.Create(CultureInfo.InvariantCulture, $"spell set {index} on {windowName}"), selectedSection: false);
        if (ShowSpells)
        {
            var row = SpellRows.FirstOrDefault(set => set.Index == index);
            if (row is not null)
            {
                SelectedSpellSet = row;
            }
        }

        RebuildCanvas();
        var name = chosen.Name.Length > 0 ? chosen.Name : string.Create(CultureInfo.InvariantCulture, $"Set {index}");
        Status = $"{SectionTitles.Format(windowName)} is showing {name}.";
    }

    public void AddSpellSet(string windowName, string name)
    {
        if (SpellSetsStayOnTheEditedLoadout() || !EnsureContentClasses())
        {
            return;
        }

        var title = name.Trim();
        if (!SpellWindows.IsWindow(windowName) || title.Length == 0)
        {
            return;
        }

        if (title.Length > 40)
        {
            title = title[..40];
        }

        var existing = PendingFor(LoadoutFileKind.Character, "SpellLoadouts") ?? _sourceCharacter?.Document.Find("SpellLoadouts");
        var windowPreview = DisplayLayoutSection(windowName);
        if (existing is null || windowPreview is null)
        {
            Status = "This loadout has no spell sets to change.";
            return;
        }

        var sets = SpellGems.Read(existing);
        var index = SpellWindows.NextFreeIndex(sets);
        if (index == 0)
        {
            Status = "All 60 spell sets are already in use.";
            return;
        }

        var spells = MutableSpellSection();
        var window = MutableLayoutSection(windowName);
        if (spells is null || window is null)
        {
            Status = "This loadout has no spell sets to change.";
            return;
        }

        var current = sets.FirstOrDefault(set => set.Index == SpellWindows.SelectedIndex(windowPreview, sets));
        var created = new SpellGemSet(index) { InUse = "1", Name = title };
        for (var slot = 0; slot < created.Slots.Length; slot++)
        {
            var value = current is null || slot >= current.Slots.Length ? string.Empty : current.Slots[slot];
            created.Slots[slot] = SpellCatalog.IsEmptyGem(value) ? "-1" : value.Trim();
        }

        SpellGems.WriteSet(spells, created);
        window.Set(SpellWindows.SetKey, index.ToString(CultureInfo.InvariantCulture));
        if (!FinishSpellBookEdit(string.Create(CultureInfo.InvariantCulture, $"add spell set {index}")))
        {
            return;
        }
        if (ShowSpells)
        {
            SelectedSpellSet = SpellRows.FirstOrDefault(row => row.Index == index) ?? SelectedSpellSet;
        }

        Status = $"Added spell set {title}.";
    }

    public void SaveSpellSet(string windowName, int targetIndex)
    {
        if (SpellSetsStayOnTheEditedLoadout() || !EnsureContentClasses())
        {
            return;
        }

        if (!SpellWindows.IsWindow(windowName))
        {
            return;
        }

        var window = DisplayLayoutSection(windowName);
        var existing = PendingFor(LoadoutFileKind.Character, "SpellLoadouts") ?? _sourceCharacter?.Document.Find("SpellLoadouts");
        if (window is null || existing is null)
        {
            Status = "This loadout has no spell sets to change.";
            return;
        }

        var sets = SpellGems.Read(existing);
        var current = sets.FirstOrDefault(set => set.Index == SpellWindows.SelectedIndex(window, sets));
        var target = sets.FirstOrDefault(set => set.Index == targetIndex && SpellWindows.IsListed(set));
        if (current is null || target is null)
        {
            return;
        }

        var spells = MutableSpellSection();
        if (spells is null)
        {
            Status = "This loadout has no spell sets to change.";
            return;
        }

        for (var slot = 0; slot < target.Slots.Length; slot++)
        {
            var value = slot < current.Slots.Length ? current.Slots[slot] : string.Empty;
            target.Slots[slot] = SpellCatalog.IsEmptyGem(value) ? "-1" : value.Trim();
        }

        SpellGems.WriteSet(spells, target);
        var targetName = target.Name.Length > 0 ? target.Name : string.Create(CultureInfo.InvariantCulture, $"Set {target.Index}");
        if (!FinishSpellBookEdit(string.Create(CultureInfo.InvariantCulture, $"save spell set {target.Index}")))
        {
            return;
        }
        Status = $"Saved the current gems over {targetName}.";
    }

    public void DeleteSpellSetFromWindow(string windowName, int index)
    {
        if (SpellSetsStayOnTheEditedLoadout() || !EnsureContentClasses())
        {
            return;
        }

        if (!SpellWindows.IsWindow(windowName))
        {
            return;
        }

        var existing = PendingFor(LoadoutFileKind.Character, "SpellLoadouts") ?? _sourceCharacter?.Document.Find("SpellLoadouts");
        if (existing is null || !SpellGems.HasSet(existing, index))
        {
            return;
        }

        var spells = MutableSpellSection();
        if (spells is null)
        {
            return;
        }

        var name = SpellGems.Read(existing).FirstOrDefault(set => set.Index == index)?.Name ?? string.Empty;
        SpellGems.RemoveSet(spells, index);
        var window = DisplayLayoutSection(windowName);
        if (window?.Get(SpellWindows.SetKey) == index.ToString(CultureInfo.InvariantCulture))
        {
            MutableLayoutSection(windowName)?.Remove(SpellWindows.SetKey);
        }

        if (!FinishSpellBookEdit(string.Create(CultureInfo.InvariantCulture, $"delete spell set {index}")))
        {
            return;
        }
        var caption = name.Length > 0 ? name : string.Create(CultureInfo.InvariantCulture, $"Set {index}");
        Status = $"Deleted spell set {caption}.";
    }

    private bool FinishSpellBookEdit(string label)
    {
        if (!MarkEdited(label, selectedSection: false))
        {
            return false;
        }
        if (ShowSpells)
        {
            MutedDepth++;
            ReloadSpells();
            if (MutedDepth > 0)
            {
                MutedDepth--;
            }
        }

        RebuildCanvas();
        return true;
    }

    /// <summary>Spell sets are edited on one loadout. An all-destination choice does not copy them.</summary>
    private bool SpellSetsStayOnTheEditedLoadout()
    {
        if (!IsAll)
        {
            return false;
        }

        Status = BlockReasonFor("SpellLoadouts")
            ?? "Spell loadouts can't be edited while ALL is selected as the destination. Choose a single destination loadout.";
        return true;
    }

    private IniSection? MutableSpellSection()
    {
        if (EditableSection() is { Name: "SpellLoadouts" } editing)
        {
            return editing;
        }

        return _sourceCharacter?.Document.Find("SpellLoadouts");
    }

    private SpellIcons Icons()
    {
        var set = _selectedSpellIconSet?.Number ?? 0;
        if (_spellIcons is null
            || _loadedSpellIconSet != set
            || !string.Equals(_spellIconDirectory, GameDirectory, StringComparison.OrdinalIgnoreCase))
        {
            _spellIcons = SpellIcons.Load(GameDirectory, set);
            _spellIconDirectory = GameDirectory;
            _loadedSpellIconSet = set;
        }

        return _spellIcons;
    }

    private void LoadSpellIconSelection()
    {
        var set = _store.Current.SpellIconSet is >= 0 and <= 3
            ? _store.Current.SpellIconSet.Value
            : 0;
        var scale = SpellIconScales.FirstOrDefault(option => option.Scale == _store.Current.SpellIconScale)?.Scale ?? DefaultSpellIconScale;
        _applyingSpellIcons = true;
        SelectedSpellIconSet = SpellIconSets.First(option => option.Number == set);
        SelectedSpellIconScale = SpellIconScales.First(option => option.Scale == scale);
        _applyingSpellIcons = false;
        Raise(nameof(SpellIconPixels));
    }

    private void ApplySpellIconSet(int number)
    {
        if (_selectedSpellIconSet?.Number != number)
        {
            return;
        }

        _store.Current.SpellIconSet = number;
        _store.Save();
        ReloadSpellIcons();
    }

    private void ReloadSpellIcons()
    {
        _spellIcons = null;
        var icons = Icons();
        foreach (var row in SpellRows)
        {
            row.UseIcons(icons);
        }

        RefreshSpellSearch();
        LayoutVersion++;
    }

    private bool FilterSpell(object item)
    {
        return item is SpellRow row && (ShowAllSpells || row.IsActive);
    }

    private void ReloadRaw()
    {
        var section = EditableSection();
        if (section is null)
        {
            return;
        }

        MutedDepth++;
        RawRows.Clear();
        foreach (var entry in section.Entries)
        {
            var row = new RawRow(SyncRaw)
            {
                Key = entry.Key,
                Value = entry.HasEquals ? entry.Value : string.Empty,
            };
            RawRows.Add(row);
        }

        if (MutedDepth > 0) MutedDepth--;
    }

    private void SyncRaw()
    {
        if (Muted)
        {
            return;
        }

        var section = EditableSection();
        if (section is null)
        {
            return;
        }

        section.ReplaceEntries(RawRows
            .Where(row => row.Key.Length > 0)
            .Select(row => new IniEntry(row.Key, row.Value)));
        MarkEdited();
    }

    private static ChatTabFace[] ChatFaces(IReadOnlyList<NamedChatWindow> windows, string sectionName)
    {
        var tabs = ChatLayout.Tabs(windows, sectionName);
        if (tabs.Count == 0)
        {
            return [];
        }

        var faces = new ChatTabFace[tabs.Count];
        for (var i = 0; i < tabs.Count; i++)
        {
            faces[i] = new ChatTabFace(tabs[i].Name, i == 0);
        }

        return faces;
    }

    private void RebuildCanvas()
    {
        var document = _sourceUi?.Document;
        if (document is null)
        {
            Windows = [];
            Raise(nameof(Windows));
            Raise(nameof(CanArrange));
            Raise(nameof(CanCenterOnScreen));
            Raise(nameof(ArrangeHeader));
            Raise(nameof(PlacementScopeText));
            LayoutVersion++;
            return;
        }

        var chatWindows = ChatLayout.Read(document.Find("ChatManager"));
        var layouts = ActiveLayout.Read(document, _skinName);
        var windows = new List<LayoutWindow>();
        var unplacedIndex = 0;
        var sections = new List<IniSection>(document.Sections);
        var present = new HashSet<string>(document.Sections.Select(section => section.Name), StringComparer.Ordinal);
        foreach (var name in SharedInventoryBags())
        {
            if (present.Add(name))
            {
                sections.Add(new IniSection(name));
            }
        }

        foreach (var section in sections)
        {
            if (IsCombineSection(section.Name))
            {
                continue;
            }

            var effective = PendingFor(LoadoutFileKind.Ui, section.Name) ?? section;
            var category = CategoryFor(section.Name);
            if (!_shownKeys.Contains(ItemKey(LoadoutFileKind.Ui, section.Name)))
            {
                continue;
            }

            var hasPosition = WindowPlacement.HasPosition(effective);
            if (!hasPosition && category == "window")
            {
                continue;
            }

            var chrome = _skins.Find(section.Name);
            ScreenRect rect;
            var unplaced = false;
            if (!hasPosition)
            {
                unplaced = true;
                var width = chrome?.Width ?? 140;
                var height = chrome?.Height ?? 180;
                rect = new ScreenRect(12, 12 + (unplacedIndex * 36), width, height);
                unplacedIndex++;
            }
            else
            {
                rect = ReadRect(effective, chrome);
            }

            var caption = ChatLayout.Caption(chatWindows, section.Name);
            var buttonsName = HotButtonText.ButtonsSection(section.Name);
            IReadOnlyList<HotbarFace> buttons = [];
            var bag = category == "window" ? null : _inventory?.Find(section.Name);
            var slotCount = bag is not null && (chrome?.Sizable ?? true) ? bag.Slots : 0;
            if (buttonsName is not null)
            {
                slotCount = HotButtonText.ButtonsPerBar;
                buttons = HotButtonText.ReadPage(
                    PendingFor(LoadoutFileKind.Character, buttonsName) ?? _sourceCharacter?.Document.Find(buttonsName),
                    PendingFor(LoadoutFileKind.Character, "Socials") ?? _sourceCharacter?.Document.Find("Socials"),
                    page: 1,
                    MemorizedGems(),
                    Abilities().ButtonName,
                    SpellButton);
            }
            else if (StanceBars.IsWindow(section.Name))
            {
                slotCount = StanceBars.Buttons.Count;
                buttons = StanceBars.Buttons;
            }

            var spellWindow = SpellWindows.IsWindow(section.Name);
            var spellGems = Array.Empty<SpellGemFace>();
            var spellSets = Array.Empty<SpellSetOption>();
            if (spellWindow)
            {
                (spellGems, spellSets) = SpellWindowFace(effective);
            }

            if (bag is not null && slotCount > 0)
            {
                var grid = BagGrid.FromWidth(slotCount, rect.Width);
                rect = new ScreenRect(rect.Left, rect.Top, grid.Width, grid.Height);
            }

            windows.Add(new LayoutWindow
            {
                Name = section.Name,
                Label = caption.Length > 0
                    ? caption
                    : category == "window" ? SectionTitles.Format(section.Name) : ShortBagLabel(section.Name),
                ChatTabs = ChatFaces(chatWindows, section.Name),
                Detail = bag is null ? string.Empty : string.Create(CultureInfo.InvariantCulture, $"{bag.Slots} slots"),
                ItemName = bag?.ItemName ?? string.Empty,
                SlotSummary = BagDetail(section.Name),
                SlotCount = slotCount,
                Buttons = buttons,
                IsSpellWindow = spellWindow,
                SpellGems = spellGems,
                SpellSets = spellSets,
                UsedSlots = bag?.UsedSlots ?? new HashSet<int>(),
                Left = rect.Left,
                Top = rect.Top,
                Width = rect.Width,
                Height = rect.Height,
                Sizable = chrome?.Sizable ?? true,
                Hidden = !ActiveLayout.IsDisplayed(effective, layouts)
                    || !ChatLayout.HasTabs(chatWindows, section.Name),
                HasStoredSize = WindowPlacement.HasStoredSize(effective),
                Unplaced = unplaced,
                Category = category,
            });
        }

        Windows = windows;
        Raise(nameof(Windows));
        Raise(nameof(CanArrange));
        Raise(nameof(CanCenterOnScreen));
        Raise(nameof(ArrangeHeader));
        Raise(nameof(PlacementScopeText));
        LayoutVersion++;
    }

    private static string AnchorOrDefault(string? value, params string[] allowed)
        => allowed.FirstOrDefault(item => item.Equals(value, StringComparison.OrdinalIgnoreCase)) ?? allowed[0];

    private ScreenRect ReadRect(IniSection section, WindowChrome? chrome = null)
    {
        chrome ??= _skins.Find(section.Name);
        var width = chrome?.Width ?? 160;
        var height = chrome?.Height ?? 48;
        var rect = WindowPlacement.ReadRect(section, ScreenWidth, ScreenHeight, width, height);
        var anchor = AnchorFrame(section.Name);
        if (!anchor.IsEmpty)
        {
            rect = anchor.ToScreen(rect, section.Get("XRef"), section.Get("YRef"));
        }

        return rect;
    }

    private void WriteLayoutRect(IniSection section, ScreenRect rect, bool writeSize)
    {
        var anchor = AnchorFrame(section.Name);
        if (!anchor.IsEmpty)
        {
            rect = anchor.ToStored(rect, section.Get("XRef"), section.Get("YRef"));
        }

        WindowPlacement.WriteRect(section, rect, ScreenWidth, ScreenHeight, writeSize);
    }

    private bool StoresBagFrame(string name) => _inventory?.Find(name) is not null;

    private FrameInset AnchorFrame(string name)
        => StoresBagFrame(name) ? FrameInset.Bag : WindowFrame.Anchor(name);

    private void ClearPositionFields()
    {
        MutedDepth++;
        XPosText = string.Empty;
        YPosText = string.Empty;
        WidthText = string.Empty;
        HeightText = string.Empty;
        if (MutedDepth > 0)
        {
            MutedDepth--;
        }
    }

    private void LoadPositionFields(IniSection section)
    {
        var chrome = _skins.Find(section.Name);
        MutedDepth++;
        XRef = AnchorOrDefault(section.Get("XRef"), "left", "center", "right");
        YRef = AnchorOrDefault(section.Get("YRef"), "top", "center", "bottom");
        var scale = WindowPlacement.CenterScale(section);
        XPosText = DisplayPercent(section.Get("XPos"), section.Get("XRef"), ScreenWidth, scale);
        YPosText = DisplayPercent(section.Get("YPos"), section.Get("YRef"), ScreenHeight, scale);
        WidthText = section.Get("Width") ?? chrome?.Width?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        HeightText = section.Get("Height") ?? chrome?.Height?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        CanResize = chrome?.Sizable ?? true;
        if (MutedDepth > 0) MutedDepth--;
    }

    private void RefreshSkin()
    {
        var skin = _sourceUi?.Document.Find("Main")?.Get("UISkin");
        if (string.IsNullOrWhiteSpace(skin))
        {
            skin = "default_modern";
        }

        if (skin == _skinName)
        {
            return;
        }

        _skinName = skin;
        _skins = UiSkinCatalog.Load(GameDirectory, skin);
    }

    private IniSection? EditableSection()
    {
        if (_selectedSection is null)
        {
            return null;
        }

        if (IsAll)
        {
            return _pending;
        }

        return Document(editingDestination: false, _selectedSection.Kind)?.Find(_selectedSection.Name);
    }

    private IniDocument? Document(bool editingDestination, LoadoutFileKind kind)
    {
        var file = editingDestination && !IsAll
            ? (kind == LoadoutFileKind.Character ? _destCharacter : _destUi)
            : (kind == LoadoutFileKind.Character ? _sourceCharacter : _sourceUi);
        return file?.Document;
    }

    private List<SectionItem> CopyItems()
    {
        if (_selectedSection is null)
        {
            return [];
        }

        if (_selectedSection.Kind != LoadoutFileKind.Ui || !_layoutSelection.Contains(_selectedSection.Name, StringComparer.Ordinal))
        {
            return [_selectedSection];
        }

        return _layoutSelection
            .Select(name => _sections.FirstOrDefault(section => section.Kind == LoadoutFileKind.Ui && section.Name == name))
            .OfType<SectionItem>()
            .ToList();
    }

    private void UpdateCopyState()
    {
        Raise(nameof(CopyButtonText));
        Raise(nameof(ShowCopySection));
        if (IsAll)
        {
            CanCopy = _pending is not null && AllTargets().Count > 0;
            return;
        }

        CanCopy = _selectedSection is not null
            && SourceLoadout is not null
            && DestinationLoadout is not null
            && !DestinationSet.SameLoadout(SourceLoadout, DestinationLoadout)
            && Document(editingDestination: false, _selectedSection.Kind)?.Find(_selectedSection.Name) is not null;
    }

    private void MarkEdited()
    {
        MarkEdited(_selectedSection is { } item ? $"edit {item.ListText}" : "edit", selectedSection: true);
    }

    private bool MarkEdited(string label, bool selectedSection = true)
    {
        if (Muted)
        {
            _touchedPending.Clear();
            return true;
        }

        if (IsAll && selectedSection && _pending is not null && _selectedSection is { } item)
        {
            CurrentPending[ItemKey(item)] = _pending;
            _touchedPending.Add((item.Kind, _pending));
        }

        if (EditNeedsClasses(selectedSection))
        {
            if (_suppressClassPrompt > 0)
            {
                _touchedPending.Clear();
                return false;
            }

            if (!EnsureContentClasses())
            {
                _touchedPending.Clear();
                RevertUncommittedEdit();
                return false;
            }
        }

        if (IsAll)
        {
            MirrorPendingToTargets();
        }

        _touchedPending.Clear();
        PushState(label);
        return true;
    }

    private bool EditNeedsClasses(bool selectedSection)
    {
        if (_touchedPending.Any(item => item.Section.Name is "Socials" or "SpellLoadouts"))
        {
            return true;
        }

        return selectedSection && _selectedSection?.Name is "Socials" or "SpellLoadouts";
    }

    private bool EnsureContentClasses()
    {
        if (EditedLoadoutHasThreeClasses)
        {
            return true;
        }

        if (_suppressClassPrompt > 0)
        {
            return false;
        }

        if (ConfirmContentClasses?.Invoke() == true && EditedLoadoutHasThreeClasses)
        {
            return true;
        }

        Status = "Choose 3 classes for this loadout before changing macros or spell loadouts.";
        return false;
    }

    private void RevertUncommittedEdit()
    {
        var state = _undo.Count > 0 ? _undo[^1] : EditState.Empty;
        foreach (var file in _files.Values.ToList())
        {
            var text = state.Files.TryGetValue(file.Path, out var saved)
                ? saved
                : _baselines.TryGetValue(file.Path, out var baseline)
                    ? baseline
                    : file.OriginalText;
            if (file.Document.Write() != text)
            {
                file.Document.ReplaceAll(IniDocument.Parse(text));
            }
        }

        _pendingByContext = ClonePending(state.Pending);
        _pending = null;
        _touchedPending.Clear();
        UpdateDirtyState();
        RefreshView();
    }

    /// <summary>Window names highlighted on the layout. Spell sets are not included.</summary>
    private List<string> HighlightedWindowNames()
        => _layoutSelection
            .Where(name => _sections.Any(section => section.Kind == LoadoutFileKind.Ui && section.Name == name)
                && SectionSave.CopiesToEveryLoadout(LoadoutFileKind.Ui, name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// Writes the highlighted windows' current positions onto the destination loadouts.
    /// The source file already has those positions, so it is left as it is.
    /// </summary>
    private void StageHighlightedWindows()
    {
        if (SourceLoadout is not { } source || _sourceUi is null)
        {
            return;
        }

        var names = HighlightedWindowNames();
        if (names.Count == 0)
        {
            return;
        }

        var targets = IsAll
            ? DestinationSet.WindowSaveLoadouts(source, CurrentDestinationScope, _catalog, _destinationServer?.Character, _destinationServer?.Server)
            : DestinationLoadout is { } destination && !DestinationSet.SameLoadout(destination, source)
                ? [destination]
                : Array.Empty<DiscoveredLoadout>();
        var changed = false;
        foreach (var loadout in targets)
        {
            var file = Open(loadout.UiFile);
            if (file is null || SamePath(file.Path, _sourceUi.Path))
            {
                continue;
            }

            if (SectionSave.CopySections(_sourceUi.Document, file.Document, names))
            {
                changed = true;
            }
        }

        if (changed)
        {
            PushState("highlighted window positions");
        }
    }

    /// <summary>
    /// Copies changed window sections onto every loadout in an all-destination save.
    /// Class labels are ignored: a loadout with no classes still receives the windows.
    /// </summary>
    private void StageWindowEditsForEveryLoadout()
    {
        if (!IsAll || SourceLoadout is not { } source || _destinationServer is null)
        {
            return;
        }

        var sections = new Dictionary<string, IniSection?>(StringComparer.OrdinalIgnoreCase);
        if (_sourceUi is not null)
        {
            foreach (var change in SectionSave.ChangedSections(IniDocument.Parse(_sourceUi.OriginalText), _sourceUi.Document))
            {
                if (SectionSave.CopiesToEveryLoadout(LoadoutFileKind.Ui, change.Name))
                {
                    sections[change.Name] = change.Edited;
                }
            }
        }

        if (_pendingByContext.TryGetValue(PendingContext, out var pending))
        {
            var prefix = ItemKey(LoadoutFileKind.Ui, string.Empty);
            foreach (var (key, section) in pending)
            {
                if (key.StartsWith(prefix, StringComparison.Ordinal)
                    && SectionSave.CopiesToEveryLoadout(LoadoutFileKind.Ui, section.Name))
                {
                    sections[section.Name] = section;
                }
            }
        }

        if (sections.Count == 0)
        {
            return;
        }

        var loadouts = DestinationSet.WindowSaveLoadouts(
            source,
            CurrentDestinationScope,
            _catalog,
            _destinationServer.Character,
            _destinationServer.Server);
        foreach (var loadout in loadouts)
        {
            var file = Open(loadout.UiFile);
            if (file is null)
            {
                continue;
            }

            foreach (var (name, section) in sections)
            {
                if (section is null)
                {
                    file.Document.Sections.RemoveAll(item => item.Name == name);
                }
                else
                {
                    file.Document.ReplaceSection(section);
                }
            }
        }

        PushState("window changes for every loadout");
    }

    private List<DiscoveredLoadout> AllTargets()
    {
        if (!IsAll || SourceLoadout is not { } source || _destinationServer is null)
        {
            return [];
        }

        return DestinationSet.Resolve(
            source,
            CurrentDestinationScope,
            null,
            _catalog,
            _destinationServer.Character,
            _destinationServer.Server).ToList();
    }

    private void MirrorPendingToTargets()
    {
        if (_touchedPending.Count == 0)
        {
            return;
        }

        var targets = AllTargets();
        foreach (var (kind, section) in _touchedPending.DistinctBy(touched => (touched.Kind, touched.Section.Name)))
        {
            if (!SectionSave.CopiesToEveryLoadout(kind, section.Name))
            {
                continue;
            }

            foreach (var target in targets)
            {
                Open(target.FileFor(kind))?.Document.ReplaceSection(section);
            }
        }
    }

    private EditState CaptureState(string label)
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in _baselines.Keys)
        {
            if (_files.TryGetValue(path, out var file))
            {
                files[path] = file.Document.Write();
            }
        }

        return new EditState(label, DateTime.UtcNow, files, ClonePending(_pendingByContext));
    }

    private void PushState(string label)
    {
        foreach (var file in _files.Values)
        {
            if (!_baselines.ContainsKey(file.Path) && file.HasChanges)
            {
                _baselines[file.Path] = file.OriginalText;
            }
        }

        var top = _undo[^1];
        var state = CaptureState(label);
        if (state.SameContent(top))
        {
            return;
        }

        var merge = _undo.Count > 1
            && label.StartsWith("edit", StringComparison.Ordinal)
            && top.Label == label
            && state.At - top.At < TimeSpan.FromSeconds(1.5);
        if (merge)
        {
            _undo[^1] = state;
        }
        else
        {
            _undo.Add(state);
            if (_undo.Count > MaxUndo)
            {
                _undo.RemoveAt(0);
            }
        }

        _redo.Clear();
        UpdateDirtyState();
    }

    private const int MaxUndo = 200;

    private void UpdateDirtyState()
    {
        _unsavedFileCount = _files.Values.Count(file => file.HasChanges);
        HasUnsavedChanges = _unsavedFileCount > 0;
        Raise(nameof(UnsavedFileCount));
        Raise(nameof(SaveButtonText));
        Raise(nameof(CanSave));
        Raise(nameof(CanUndo));
        Raise(nameof(CanRedo));
        Raise(nameof(UndoTip));
        Raise(nameof(RedoTip));
    }

    private static Dictionary<string, Dictionary<string, IniSection>> ClonePending(
        IReadOnlyDictionary<string, Dictionary<string, IniSection>> pending)
    {
        var copy = new Dictionary<string, Dictionary<string, IniSection>>(StringComparer.Ordinal);
        foreach (var (context, sections) in pending)
        {
            var inner = new Dictionary<string, IniSection>(StringComparer.Ordinal);
            foreach (var (key, section) in sections)
            {
                inner[key] = section.Clone();
            }

            copy[context] = inner;
        }

        return copy;
    }

    private LoadedIniFile? Open(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        var stamp = File.GetLastWriteTimeUtc(path);
        if (_files.TryGetValue(path, out var cached))
        {
            var untouched = !cached.HasChanges && !_baselines.ContainsKey(path);
            if (!untouched || (_fileStamps.TryGetValue(path, out var known) && known == stamp))
            {
                return cached;
            }
        }

        var loaded = IniFile.Load(path);
        _files[path] = loaded;
        _fileStamps[path] = stamp;
        return loaded;
    }

    private sealed record EditState(
        string Label,
        DateTime At,
        IReadOnlyDictionary<string, string> Files,
        IReadOnlyDictionary<string, Dictionary<string, IniSection>> Pending)
    {
        public static EditState Empty { get; } = new(
            "open",
            DateTime.MinValue,
            new Dictionary<string, string>(),
            new Dictionary<string, Dictionary<string, IniSection>>());

        public bool SameContent(EditState other)
        {
            if (Files.Count != other.Files.Count || Files.Any(file => !other.Files.TryGetValue(file.Key, out var text) || text != file.Value))
            {
                return false;
            }

            return PendingText(Pending) == PendingText(other.Pending);
        }

        private static string PendingText(IReadOnlyDictionary<string, Dictionary<string, IniSection>> pending)
        {
            var builder = new StringBuilder();
            foreach (var (context, sections) in pending.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            {
                builder.Append(context).Append('\n');
                foreach (var (key, section) in sections.OrderBy(entry => entry.Key, StringComparer.Ordinal))
                {
                    builder.Append(key).Append('\n');
                    foreach (var entry in section.Entries)
                    {
                        builder.Append(entry.Key).Append('=').Append(entry.Value).Append('\n');
                    }
                }
            }

            return builder.ToString();
        }
    }

    private static int PageNumber(string key)
    {
        if (!key.StartsWith("Page", StringComparison.Ordinal))
        {
            return 0;
        }

        var end = key.IndexOf("Button", StringComparison.Ordinal);
        if (end < 5)
        {
            return 0;
        }

        return int.TryParse(key[4..end], NumberStyles.Integer, CultureInfo.InvariantCulture, out var page) ? page : 0;
    }

    private static string DisplayPercent(string? stored, string? anchor, double screen, double centerScale)
    {
        if (!WindowPlacement.TryParsePercent(stored, out var percent))
        {
            return string.IsNullOrWhiteSpace(stored) ? "0%" : stored;
        }

        return WindowPlacement.FormatAxisPercent(anchor, percent, screen, centerScale);
    }

    private static bool SetPercent(IniSection section, string key, string text, string? anchor, double screen)
    {
        if (!WindowPlacement.TryParsePercent(text, out var percent))
        {
            return false;
        }

        var scale = WindowPlacement.CenterScale(section);
        if (WindowPlacement.TryParsePercent(section.Get(key), out var existing)
            && WindowPlacement.SameOffset(anchor, existing, percent, screen, scale))
        {
            return false;
        }

        var formatted = WindowPlacement.FormatAxisPercent(anchor, percent, screen, scale);
        if (section.Get(key) == formatted)
        {
            return false;
        }

        section.Set(key, formatted);
        return true;
    }

    private static bool SetSize(IniSection section, string key, string text)
    {
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var size) || size <= 0)
        {
            return false;
        }

        var formatted = Math.Round(size).ToString(CultureInfo.InvariantCulture);
        if (section.Get(key) == formatted)
        {
            return false;
        }

        section.Set(key, formatted);
        return true;
    }

    private static string CategoryFor(string name)
    {
        if (name.StartsWith("BagInv", StringComparison.Ordinal))
        {
            return "inventory";
        }

        if (name == "BankWnd" || name.StartsWith("BagBank", StringComparison.Ordinal) || name.StartsWith("BagWorld", StringComparison.Ordinal))
        {
            return "bank";
        }

        return "window";
    }

    private static bool IsCombineSection(string name) => name.EndsWith("Combine", StringComparison.Ordinal);

    private static string ShortBagLabel(string name)
    {
        var prefixLength = name.StartsWith("BagInv", StringComparison.Ordinal) ? "BagInv".Length
            : name.StartsWith("BagBank", StringComparison.Ordinal) ? "BagBank".Length
            : name.StartsWith("BagWorld", StringComparison.Ordinal) ? "BagWorld".Length
            : 0;
        if (prefixLength == 0)
        {
            return name;
        }

        return name[prefixLength..];
    }

    public void StartSpellPowerScan()
    {
        _powerScanReady = true;
        ScanSpellPowers(force: false);
    }

    public void RescanSpellPowers() => ScanSpellPowers(force: true);

    public int PowerRank(string name)
        => name.Length == 0 || !_spellPowers.TryGetValue(name, out var power) ? 0 : power;

    public string PowerName(string name)
    {
        if (name.Length == 0 || !_spellPowers.TryGetValue(name, out var power) || power <= 0)
        {
            return name;
        }

        return SpellPower.DisplayName(name, power);
    }

    public bool TryBeginSuggestedSave(out string proposed)
    {
        proposed = string.Empty;
        if (_loadoutClasses.Count == 0)
        {
            Status = "Set this loadout's classes before saving a suggested spell set.";
            return false;
        }

        if (_selectedSuggestion is not { } page || page.Gems.All(gem => gem.IsEmpty))
        {
            Status = "This suggestion has no spells to save.";
            return false;
        }

        proposed = SpellAdvisor.UniqueName(page.Name, SpellRows.Select(row => row.Name));
        return true;
    }

    public void SaveSuggestedSet(string name)
    {
        if (_loadoutClasses.Count == 0)
        {
            Status = "Set this loadout's classes before saving a suggested spell set.";
            return;
        }

        var page = _selectedSuggestion;
        if (page is null || page.Gems.All(gem => gem.IsEmpty))
        {
            Status = "This suggestion has no spells to save.";
            return;
        }

        var title = name.Trim();
        if (title.Length == 0)
        {
            return;
        }

        if (title.Length > 40)
        {
            title = title[..40];
        }

        title = SpellAdvisor.UniqueName(title, SpellRows.Select(row => row.Name));
        var slots = page.Gems.Select(gem => gem.IsEmpty ? "-1" : gem.SpellId.ToString(CultureInfo.InvariantCulture)).ToArray();
        if (!AddEmptySpellSet())
        {
            return;
        }

        SelectedSpellSet?.AssignSpells(title, slots);
        Status = $"Added spell set {title}.";
    }

    public void ApplyGemSuggestion(GemSuggestion suggestion)
    {
        if (_loadoutClasses.Count == 0)
        {
            Status = "Set this loadout's classes before changing gems.";
            return;
        }

        var row = _selectedSpellSet;
        var gem = row?.Gems.FirstOrDefault(item => item.Number == suggestion.GemNumber);
        if (gem is null)
        {
            return;
        }

        gem.Value = suggestion.SpellId.ToString(CultureInfo.InvariantCulture);
        RebuildGemSuggestions();
    }

    private void SyncSuggestionLevel()
    {
        if (!_suggestionLevelEdited)
        {
            var level = CasterLevel();
            _suggestionLevel = level;
            _syncingLevel = true;
            SuggestionLevelText = level.ToString(CultureInfo.InvariantCulture);
            _syncingLevel = false;
        }

        QueueAdvisor();
    }

    private void QueueAdvisor()
    {
        if (!ShowSpellSuggestions)
        {
            return;
        }

        if (_loadoutClasses.Count == 0)
        {
            SuggestionNote = "Set this loadout's classes to suggest spell sets.";
            _advisorPool = [];
            _advisorDrafts = SpellAdvisor.Draft([]);
            _advisorKey = "none";
            ShowDrafts();
            return;
        }

        SuggestionNote = string.Empty;
        var key = string.Create(CultureInfo.InvariantCulture, $"{_suggestionLevel}|{string.Join(",", _loadoutClasses)}");
        if (key == _advisorKey && _advisorDrafts.Count > 0)
        {
            ShowDrafts();
            return;
        }

        _advisorKey = key;
        var generation = ++_advisorGeneration;
        var classes = _loadoutClasses.ToArray();
        var level = _suggestionLevel;
        var catalog = Spells();
        var context = SynchronizationContext.Current;
        Task.Run(() =>
        {
            var pool = new List<SpellScore>();
            foreach (var spell in catalog.UsableBy(classes, level))
            {
                var score = SpellAdvisor.Score(spell, level);
                if (score.Role is null || score.Points <= 0)
                {
                    continue;
                }

                pool.Add(score);
            }

            return (Pool: (IReadOnlyList<SpellScore>)pool, Drafts: SpellAdvisor.Draft(pool));
        }).ContinueWith(task =>
        {
            if (generation != _advisorGeneration || task.IsFaulted || task.IsCanceled || _disposed || !ShowSpellSuggestions)
            {
                return;
            }

            void Apply()
            {
                if (generation != _advisorGeneration || _disposed || !ShowSpellSuggestions)
                {
                    return;
                }

                _advisorPool = task.Result.Pool;
                _advisorDrafts = task.Result.Drafts;
                ShowDrafts();
            }

            if (context is null)
            {
                Apply();
            }
            else
            {
                context.Post(_ => Apply(), null);
            }
        });
    }

    private void ShowDrafts()
    {
        if (SuggestedSets.Count == _advisorDrafts.Count && SuggestedSets.Count > 0
            && SuggestedSets.Select(page => page.Kind).SequenceEqual(_advisorDrafts.Select(draft => draft.Kind)))
        {
            for (var index = 0; index < _advisorDrafts.Count; index++)
            {
                SuggestedSets[index].Replace(MakePage(_advisorDrafts[index]).Gems);
            }

            _selectedSuggestionGem = null;
            RaiseGemPage();
            if (_selectedSuggestion is null)
            {
                RebuildGemSuggestions();
            }
            else
            {
                GemSuggestions.Clear();
                HasGemSuggestions = false;
            }

            return;
        }

        var kind = _selectedSuggestion?.Kind;
        SuggestedSets.Clear();
        foreach (var draft in _advisorDrafts)
        {
            SuggestedSets.Add(MakePage(draft));
        }

        _selectedSuggestion = kind is { } kept ? SuggestedSets.FirstOrDefault(page => page.Kind == kept) : null;
        _selectedSuggestionGem = null;
        Raise(nameof(SelectedSuggestion));
        Raise(nameof(ShowingSuggestion));
        RaiseGemPage();
        if (_selectedSuggestion is null)
        {
            RebuildGemSuggestions();
        }
        else
        {
            GemSuggestions.Clear();
            HasGemSuggestions = false;
        }
    }

    private SuggestedSpellPage MakePage(SpellSetDraft draft)
    {
        var catalog = Spells();
        var icons = Icons();
        var faces = new List<SuggestedGemFace>(SpellAdvisor.GemCount);
        for (var index = 0; index < SpellAdvisor.GemCount; index++)
        {
            faces.Add(index < draft.Spells.Count
                ? MakeFace(draft.Spells[index].Spell, index + 1, catalog, icons, _suggestionLevel)
                : SuggestedGemFace.Empty(index + 1));
        }

        return new SuggestedSpellPage(draft.Kind, KindName(draft.Kind), faces);
    }

    private SuggestedGemFace MakeFace(SpellInfo spell, int number, SpellCatalog catalog, SpellIcons icons, int level)
    {
        var levels = string.Empty;
        if (spell.BestWithin(_loadoutClasses, level) is { } best)
        {
            levels = $"{CharacterClasses.Codes[best.Class]} {best.Level}";
        }

        var name = PowerName(spell.Name);
        var usable = spell.UsableBy();
        var tip = usable.Count == 0
            ? name
            : name + Environment.NewLine + "Usable by " + string.Join(", ", usable.Select(entry => $"{CharacterClasses.Names[entry.Class]} {entry.Level}"));
        return new SuggestedGemFace
        {
            Number = number,
            SpellId = spell.Id,
            SpellName = name,
            Name = name,
            Icon = spell.Icon < 0 ? null : icons.For(spell.Icon),
            LoadoutLevels = levels,
            IsUnusable = SpellUnusable(spell),
            UsableTip = tip,
            Description = SpellPresentation.Describe(spell, catalog.Description(spell.Id), level),
            EffectText = SpellPresentation.EffectText(spell, catalog, level, PowerRank(spell.Name)),
            EffectLines = SpellPresentation.EffectLines(spell, catalog, level, PowerRank(spell.Name)),
            Details = SpellPresentation.Lines(spell, catalog.SpellLine(spell.Id), level, PowerRank(spell.Name)),
        };
    }

    private void RebuildGemSuggestions()
    {
        GemSuggestions.Clear();
        if (!ShowSpellSuggestions || _selectedSuggestion is not null || _selectedSpellSet is null || _loadoutClasses.Count == 0)
        {
            HasGemSuggestions = false;
            return;
        }

        var gems = new SpellInfo?[_selectedSpellSet.Gems.Count];
        var catalog = Spells();
        for (var index = 0; index < gems.Length; index++)
        {
            gems[index] = catalog.TryGet(_selectedSpellSet.Gems[index].Value, out var spell) ? spell : null;
        }

        foreach (var swap in SpellAdvisor.Suggest(gems, _advisorPool, _suggestionLevel))
        {
            var text = string.Create(
                CultureInfo.InvariantCulture,
                $"Gem {swap.GemNumber}: {PowerName(swap.Current.Name)} → {PowerName(swap.Suggested.Name)}. {swap.Reason}");
            GemSuggestions.Add(new GemSuggestion(swap.GemNumber, swap.Suggested.Id, text));
        }

        HasGemSuggestions = GemSuggestions.Count > 0;
    }

    private void RaiseGemPage()
    {
        Raise(nameof(ShownGems));
        Raise(nameof(ShownGemTitle));
        Raise(nameof(ShowingSuggestion));
        Raise(nameof(SelectedShownGem));
        Raise(nameof(SelectedSpellDetail));
        Raise(nameof(DisplayedSpellDetail));
    }

    private static string KindName(SpellSetKind kind) => kind switch
    {
        SpellSetKind.Buffs => "Buffs",
        SpellSetKind.Perms => "Perms",
        SpellSetKind.Attack => "Attack",
        _ => kind.ToString(),
    };

    private async void ScanSpellPowers(bool force)
    {
        if (!_powerScanReady && !force)
        {
            return;
        }

        var path = ActiveWhoLogPath();
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return;
        }

        if (!force && _powerCache.TryGetValue(path, out var cached))
        {
            if (!ReferenceEquals(cached, _spellPowers))
            {
                ApplySpellPowers(cached);
            }

            return;
        }

        _powerScan?.Cancel();
        var scan = new CancellationTokenSource();
        _powerScan = scan;
        var token = scan.Token;
        SpellPowerProgress = 0;
        SpellPowerTip = "Reading the character log";
        SpellPowerScanning = true;
        var names = Spells().CastNames();
        var progress = new Progress<(long Read, long Length)>(pair =>
        {
            if (token.IsCancellationRequested)
            {
                return;
            }

            SpellPowerProgress = pair.Length <= 0 ? 0 : (double)pair.Read / pair.Length;
            SpellPowerTip = string.Create(CultureInfo.InvariantCulture, $"{pair.Read:N0} bytes read");
        });

        try
        {
            var powers = await Task.Run(() => SpellPower.ReadFile(path, names, progress, token), token);
            if (token.IsCancellationRequested || !ReferenceEquals(_powerScan, scan) || _disposed)
            {
                return;
            }

            if (powers is null)
            {
                return;
            }

            _powerCache[path] = powers;
            ApplySpellPowers(powers);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_powerScan, scan))
            {
                SpellPowerScanning = false;
            }
        }
    }

    private void ApplySpellPowers(Dictionary<string, int> powers)
    {
        _spellPowers = powers;
        foreach (var row in SpellRows)
        {
            row.RefreshPowerNames();
        }

        if (_spellCatalog is not null && _spellClassChoice is not null)
        {
            RefreshSpellSearch();
        }

        if (ShowSpellSuggestions && _advisorDrafts.Count > 0)
        {
            ShowDrafts();
        }

        if (ShowSpells)
        {
            _upgradeKey = string.Empty;
            QueueGemUpgrades();
        }
    }

    private void QueueGemUpgrades()
    {
        if (!ShowSpells)
        {
            return;
        }

        var classes = _loadoutClasses.ToArray();
        var level = CasterLevel();
        var key = string.Create(
            CultureInfo.InvariantCulture,
            $"{level}|{string.Join(",", classes)}|{string.Join(";", SpellRows.Select(row => row.Index + ":" + string.Join(",", row.Gems.Select(gem => gem.Value))))}");
        if (key == _upgradeKey)
        {
            return;
        }

        _upgradeKey = key;
        var generation = ++_upgradeGeneration;
        if (classes.Length == 0)
        {
            ClearGemUpgrades();
            return;
        }

        var catalog = Spells();
        var powers = _spellPowers;
        var sets = SpellRows.Select(row => (row.Index, Ids: row.Gems.Select(gem => gem.Value).ToArray())).ToArray();
        var context = SynchronizationContext.Current;
        int PowerOf(SpellInfo spell) => powers.TryGetValue(spell.Name, out var rank) ? rank : 0;
        Task.Run(() =>
        {
            var castable = catalog.UsableBy(classes, level).ToList();
            var found = new Dictionary<int, Dictionary<int, int>>();
            foreach (var (index, ids) in sets)
            {
                var gems = new SpellInfo?[ids.Length];
                for (var slot = 0; slot < ids.Length; slot++)
                {
                    gems[slot] = catalog.TryGet(ids[slot], out var spell) ? spell : null;
                }

                foreach (var upgrade in SpellUpgrades.Find(gems, castable, classes, level, PowerOf))
                {
                    if (!found.TryGetValue(index, out var upgrades))
                    {
                        upgrades = [];
                        found[index] = upgrades;
                    }

                    upgrades[upgrade.GemNumber] = upgrade.Spell.Id;
                }
            }

            return found;
        }).ContinueWith(task =>
        {
            if (generation != _upgradeGeneration || task.IsFaulted || task.IsCanceled || _disposed)
            {
                return;
            }

            void Apply()
            {
                if (generation != _upgradeGeneration || _disposed)
                {
                    return;
                }

                ApplyGemUpgrades(task.Result, catalog, level);
            }

            if (context is null)
            {
                Apply();
            }
            else
            {
                context.Post(_ => Apply(), null);
            }
        });
    }

    private void ApplyGemUpgrades(Dictionary<int, Dictionary<int, int>> found, SpellCatalog catalog, int level)
    {
        var icons = Icons();
        foreach (var row in SpellRows)
        {
            found.TryGetValue(row.Index, out var upgrades);
            foreach (var gem in row.Gems)
            {
                ISpellDetails? face = null;
                if (upgrades is not null
                    && upgrades.TryGetValue(gem.Number, out var spellId)
                    && catalog.TryGet(spellId.ToString(CultureInfo.InvariantCulture), out var spell))
                {
                    face = MakeFace(spell, gem.Number, catalog, icons, level);
                }

                var previous = gem.Upgrade;
                gem.SetUpgrade(face);
                if (previous is not null && !ReferenceEquals(previous, face) && ReferenceEquals(_hoveredUpgrade, previous))
                {
                    ClearUpgradeHover(previous);
                }
            }
        }
    }

    private void ClearGemUpgrades()
    {
        foreach (var row in SpellRows)
        {
            foreach (var gem in row.Gems)
            {
                var previous = gem.Upgrade;
                gem.SetUpgrade(null);
                if (previous is not null && ReferenceEquals(_hoveredUpgrade, previous))
                {
                    ClearUpgradeHover(previous);
                }
            }
        }
    }
}
