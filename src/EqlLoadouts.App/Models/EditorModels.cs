using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using EqlLoadouts.Core;
using EqlLoadouts.Core.Content;
using EqlLoadouts.Core.Ini;

namespace EqlLoadouts.App.Models;

public sealed class SocialRow : ObservableObject
{
    private readonly IniSection _section;
    private readonly Action _edited;
    private readonly Func<bool>? _allowEdit;
    private readonly bool _readOnly;
    private bool _mute;
    private string _name = string.Empty;
    private string _color = string.Empty;
    private string _line1 = string.Empty;
    private string _line2 = string.Empty;
    private string _line3 = string.Empty;
    private string _line4 = string.Empty;
    private string _line5 = string.Empty;

    public SocialRow(int page, int slot, IniSection section, Action edited, bool readOnly = false, Func<bool>? allowEdit = null)
    {
        Page = page;
        Slot = slot;
        _section = section;
        _edited = edited;
        _allowEdit = allowEdit;
        _readOnly = readOnly;
        var choices = new MacroColorChoice[SocialColors.Count];
        for (var index = 0; index < choices.Length; index++)
        {
            var (red, green, blue) = SocialColors.At(index);
            choices[index] = new MacroColorChoice(this, index, red, green, blue);
        }

        ColorChoices = choices;
    }

    public IReadOnlyList<MacroColorChoice> ColorChoices { get; }

    public int Page { get; }

    public int Slot { get; }

    public string Caption => string.IsNullOrWhiteSpace(Name) ? Slot.ToString(CultureInfo.InvariantCulture) : Name;

    public string Name
    {
        get => _name;
        set => Update(ref _name, value, nameof(Name), nameof(Caption));
    }

    public string Color
    {
        get => _color;
        set
        {
            Update(ref _color, value, nameof(Color), nameof(NameBrush));
            foreach (var choice in ColorChoices)
            {
                choice.Refresh();
            }
        }
    }

    public Brush NameBrush => HotbarCaptionBrush.For(_color);

    internal void RefreshCaption() => Raise(nameof(NameBrush));

    public string Line1
    {
        get => _line1;
        set => Update(ref _line1, value, nameof(Line1));
    }

    public string Line2
    {
        get => _line2;
        set => Update(ref _line2, value, nameof(Line2));
    }

    public string Line3
    {
        get => _line3;
        set => Update(ref _line3, value, nameof(Line3));
    }

    public string Line4
    {
        get => _line4;
        set => Update(ref _line4, value, nameof(Line4));
    }

    public string Line5
    {
        get => _line5;
        set => Update(ref _line5, value, nameof(Line5));
    }

    public void Load(SocialSlot slot)
    {
        _mute = true;
        Name = slot.Name;
        Color = slot.Color;
        Line1 = slot.Line1;
        Line2 = slot.Line2;
        Line3 = slot.Line3;
        Line4 = slot.Line4;
        Line5 = slot.Line5;
        _mute = false;
    }

    private void Update(ref string field, string value, params string[] names)
    {
        if (field == value)
        {
            return;
        }

        field = value;
        foreach (var name in names)
        {
            Raise(name);
        }

        if (_mute || _readOnly)
        {
            return;
        }

        if (_allowEdit?.Invoke() == false)
        {
            var existing = SocialButtons.ForDisplay(_section).FirstOrDefault(item => item.Page == Page && item.Slot == Slot);
            Load(existing ?? new SocialSlot(Page, Slot));
            return;
        }

        var slot = new SocialSlot(Page, Slot)
        {
            Name = Name,
            Color = Color,
            Line1 = Line1,
            Line2 = Line2,
            Line3 = Line3,
            Line4 = Line4,
            Line5 = Line5,
        };
        if (slot.IsEmpty)
        {
            SocialButtons.ClearSlot(_section, Page, Slot);
        }
        else
        {
            SocialButtons.WriteSlot(_section, slot);
        }

        _edited();
    }

    public bool IsBlank =>
        string.IsNullOrWhiteSpace(Name)
        && string.IsNullOrWhiteSpace(Color)
        && string.IsNullOrWhiteSpace(Line1)
        && string.IsNullOrWhiteSpace(Line2)
        && string.IsNullOrWhiteSpace(Line3)
        && string.IsNullOrWhiteSpace(Line4)
        && string.IsNullOrWhiteSpace(Line5);
}

public enum SocialSide
{
    Source,
    Destination,
}

public sealed class SocialPageGroup
{
    public SocialPageGroup(int page)
    {
        Page = page;
    }

    public int Page { get; }

    public string Label => string.Create(CultureInfo.InvariantCulture, $"Page {Page}");

    public ObservableCollection<SocialButton> Buttons { get; } = [];
}

public sealed class SocialButton : ObservableObject
{
    private string _name = string.Empty;
    private string _color = string.Empty;
    private string _line1 = string.Empty;
    private string _line2 = string.Empty;
    private string _line3 = string.Empty;
    private string _line4 = string.Empty;
    private string _line5 = string.Empty;
    private string _detail = string.Empty;
    private string _toolTip = "Empty";
    private bool _isEmpty = true;
    private bool _isDuplicate;
    private bool _isMismatch;
    private bool _isQueued;
    private bool _isNew;
    private bool _isSelected;
    private bool _isDragPreview;
    private string _previewCaption = string.Empty;
    private Brush _previewBrush = Brushes.Transparent;
    private Brush _previewBorderBrush = Brushes.Transparent;
    private Thickness _previewBorderThickness = new(1);
    private string? _incomingCaption;
    private string _incomingColor = string.Empty;

    public SocialButton(SocialSide side, int page, int slot)
    {
        Side = side;
        Page = page;
        Slot = slot;
    }

    public SocialSide Side { get; }

    public int Page { get; }

    public int Slot { get; }

    public string Name => _name;

    public string Detail => _detail;

    public Brush NameBrush => HotbarCaptionBrush.For(_color);

    public void RefreshCaption() => Raise(nameof(NameBrush));

    public string Caption => string.IsNullOrWhiteSpace(_name)
        ? Slot.ToString(CultureInfo.InvariantCulture)
        : _name.Trim();

    public string ShownCaption => _isDragPreview
        ? _previewCaption
        : !string.IsNullOrWhiteSpace(_incomingCaption) ? _incomingCaption
        : _isNew && string.IsNullOrWhiteSpace(_name) ? "NEW"
        : Caption;

    public bool ShowIncomingName => _isDragPreview || !string.IsNullOrWhiteSpace(_incomingCaption);

    public Brush ShownBrush => _isDragPreview
        ? _previewBrush
        : string.IsNullOrWhiteSpace(_incomingCaption) ? NameBrush : HotbarCaptionBrush.For(_incomingColor);

    public bool IsEmpty => _isEmpty;

    public bool IsDuplicate => _isDuplicate;

    public bool IsMismatch => _isMismatch;

    public bool IsQueued => _isQueued;

    public bool IsNew => _isNew;

    /// <summary>Named new macros keep their name and carry a NEW mark. An unnamed one uses the caption itself.</summary>
    public bool ShowNewMark => _isNew && !string.IsNullOrWhiteSpace(_name);

    public bool IsSelected => _isSelected;

    public bool IsDragPreview => _isDragPreview;

    public string PreviewCaption => _previewCaption;

    public Brush PreviewBrush => _previewBrush;

    public Brush PreviewBorderBrush => _previewBorderBrush;

    public Thickness PreviewBorderThickness => _previewBorderThickness;

    public string ToolTip => _toolTip;

    public SocialSlot ToSlot()
    {
        return new SocialSlot(Page, Slot)
        {
            Name = _name,
            Color = _color,
            Line1 = _line1,
            Line2 = _line2,
            Line3 = _line3,
            Line4 = _line4,
            Line5 = _line5,
        };
    }

    public void Load(SocialSlot? slot)
    {
        var name = slot?.Name ?? string.Empty;
        var color = slot?.Color ?? string.Empty;
        _line1 = slot?.Line1 ?? string.Empty;
        _line2 = slot?.Line2 ?? string.Empty;
        _line3 = slot?.Line3 ?? string.Empty;
        _line4 = slot?.Line4 ?? string.Empty;
        _line5 = slot?.Line5 ?? string.Empty;
        var empty = slot is null || slot.IsEmpty;
        var detail = FirstCommand(slot);
        if (_color != color)
        {
            _color = color;
            Raise(nameof(NameBrush));
            Raise(nameof(ShownBrush));
        }
        if (_name != name)
        {
            _name = name;
            Raise(nameof(Name));
            Raise(nameof(Caption));
            Raise(nameof(ShownCaption));
            Raise(nameof(ShowNewMark));
        }

        if (_isEmpty != empty)
        {
            _isEmpty = empty;
            Raise(nameof(IsEmpty));
            Raise(nameof(Caption));
            Raise(nameof(ShownCaption));
        }

        if (_detail != detail)
        {
            _detail = detail;
            Raise(nameof(Detail));
        }
    }

    public void SetFlags(bool duplicate, bool mismatch, bool queued, bool selected, bool isNew, string toolTip)
    {
        var captionChanged = _isNew != isNew;
        SetProperty(ref _isDuplicate, duplicate, nameof(IsDuplicate));
        SetProperty(ref _isMismatch, mismatch, nameof(IsMismatch));
        SetProperty(ref _isQueued, queued, nameof(IsQueued));
        SetProperty(ref _isSelected, selected, nameof(IsSelected));
        SetProperty(ref _isNew, isNew, nameof(IsNew));
        if (captionChanged)
        {
            Raise(nameof(ShownCaption));
            Raise(nameof(ShowNewMark));
        }

        SetProperty(ref _toolTip, toolTip, nameof(ToolTip));
    }

    public void SetIncoming(string? caption, string? color)
    {
        var nextCaption = string.IsNullOrWhiteSpace(caption) ? null : caption.Trim();
        var nextColor = color ?? string.Empty;
        if (_incomingCaption == nextCaption && _incomingColor == nextColor)
        {
            return;
        }

        _incomingCaption = nextCaption;
        _incomingColor = nextColor;
        Raise(nameof(ShownCaption));
        Raise(nameof(ShownBrush));
        Raise(nameof(ShowIncomingName));
    }

    public void SetDragPreview(string caption, Brush foreground, Brush border, double thickness)
    {
        _previewCaption = caption;
        _previewBrush = foreground;
        _previewBorderBrush = border;
        _previewBorderThickness = new Thickness(thickness);
        Raise(nameof(PreviewCaption));
        Raise(nameof(PreviewBrush));
        Raise(nameof(PreviewBorderBrush));
        Raise(nameof(PreviewBorderThickness));
        Raise(nameof(ShownCaption));
        Raise(nameof(ShownBrush));
        if (!_isDragPreview)
        {
            _isDragPreview = true;
            Raise(nameof(IsDragPreview));
            Raise(nameof(ShowIncomingName));
        }
    }

    public void ClearDragPreview()
    {
        if (!_isDragPreview)
        {
            return;
        }

        _isDragPreview = false;
        Raise(nameof(IsDragPreview));
        Raise(nameof(ShowIncomingName));
        Raise(nameof(ShownCaption));
        Raise(nameof(ShownBrush));
    }

    private static string FirstCommand(SocialSlot? slot)
    {
        if (slot is null)
        {
            return string.Empty;
        }

        foreach (var line in new[] { slot.Line1, slot.Line2, slot.Line3, slot.Line4, slot.Line5 })
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                return line.Trim();
            }
        }

        return string.Empty;
    }
}

public enum SocialChangeKind
{
    Copied,
    New,
    Modified,
    Deleted,
}

public sealed class SocialChange
{
    public required SocialChangeKind Kind { get; init; }

    public string KindText => Kind switch
    {
        SocialChangeKind.Copied => "Copied",
        SocialChangeKind.Modified => "Modified",
        SocialChangeKind.Deleted => "Deleted",
        _ => "New",
    };

    public required int Page { get; init; }

    public required int Slot { get; init; }

    public required string Caption { get; init; }

    public required string Detail { get; init; }

    public QueuedSocialCopy? Queue { get; init; }
}

public sealed class QueuedSocialCopy
{
    public required int SourcePage { get; init; }

    public required int SourceSlot { get; init; }

    public required int DestinationPage { get; init; }

    public required int DestinationSlot { get; init; }

    public required string Caption { get; init; }

    public string? DestinationName { get; init; }

    public required SocialSlot Content { get; init; }

    public string Route => string.Create(
        CultureInfo.InvariantCulture,
        $"Page {SourcePage} · {SourceSlot}  →  Page {DestinationPage} · {DestinationSlot}");
}

public sealed class MacroColorChoice : ObservableObject
{
    private readonly SocialRow _row;

    public MacroColorChoice(SocialRow row, int index, byte red, byte green, byte blue)
    {
        _row = row;
        Index = index.ToString(CultureInfo.InvariantCulture);
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(red, green, blue));
        brush.Freeze();
        Brush = brush;
    }

    public string Index { get; }

    public Brush Brush { get; }

    public bool IsSelected => string.IsNullOrEmpty(_row.Color) ? Index == "0" : _row.Color == Index;

    public void Choose() => _row.Color = Index;

    public void Refresh() => Raise(nameof(IsSelected));
}

/// <summary>
/// Default hotbar labels are the game's white-on-dark color. Light mode paints that default black.
/// A stored palette color is kept in both themes.
/// </summary>
internal static class HotbarCaptionBrush
{
    private static readonly Brush LightDefault = Frozen(0x10, 0x12, 0x16);

    public static Brush For(string? color)
    {
        if (!ThemeManager.IsDark && IsDefault(color))
        {
            return LightDefault;
        }

        var (red, green, blue) = SocialColors.Rgb(color);
        return Frozen(red, green, blue);
    }

    private static bool IsDefault(string? color)
        => string.IsNullOrWhiteSpace(color)
            || !int.TryParse(color, out var index)
            || index <= 0
            || index >= SocialColors.Count;

    private static Brush Frozen(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }
}

public sealed class HotbarRow : ObservableObject
{
    private readonly IniSection _section;
    private readonly Action _edited;
    private bool _mute;
    private string _label = string.Empty;
    private string _raw = string.Empty;
    private string _unnamed = string.Empty;

    public HotbarRow(int page, int slot, IniSection section, Action edited)
    {
        Page = page;
        Slot = slot;
        _section = section;
        _edited = edited;
        Key = string.Create(CultureInfo.InvariantCulture, $"Page{page}Button{slot}");
    }

    public int Page { get; }

    public int Slot { get; }

    public string Key { get; }

    public string Caption => !string.IsNullOrWhiteSpace(Label)
        ? Label
        : !string.IsNullOrWhiteSpace(_unnamed) ? _unnamed : Slot.ToString(CultureInfo.InvariantCulture);

    public string Label
    {
        get => _label;
        set
        {
            if (_label == value)
            {
                return;
            }

            _label = value;
            Raise(nameof(Label));
            Raise(nameof(Caption));
            if (_mute)
            {
                return;
            }

            _mute = true;
            Raw = HotButtonText.WriteLabel(Raw, value);
            _mute = false;
            Commit();
        }
    }

    public string Raw
    {
        get => _raw;
        set
        {
            if (_raw == value)
            {
                return;
            }

            _raw = value;
            Raise(nameof(Raw));
            if (_mute)
            {
                return;
            }

            _mute = true;
            Label = HotButtonText.ReadLabel(value);
            _mute = false;
            Commit();
        }
    }

    public void Load(string raw, string unnamed = "")
    {
        _mute = true;
        _unnamed = unnamed;
        Raw = raw;
        Label = HotButtonText.ReadLabel(raw);
        _mute = false;
    }

    private void Commit()
    {
        if (string.IsNullOrEmpty(Raw))
        {
            _section.Remove(Key);
        }
        else
        {
            _section.Set(Key, Raw);
        }

        _edited();
    }
}

public sealed class SpellChoice
{
    public static SpellChoice Empty { get; } = new(-1, "Empty", "Clear this gem", false);

    public SpellChoice(int id, string name, string detail, bool unusable)
    {
        Id = id;
        Name = name;
        Detail = detail;
        IsUnusable = unusable;
    }

    public int Id { get; }

    public string Name { get; }

    public string Detail { get; }

    public bool IsUnusable { get; }

    public bool IsEmpty => Id < 0;
}

public interface ISpellDetails
{
    string Name { get; }

    string Description { get; }

    /// <summary>Generated effect lines, landing messages, and the triggered spell summary.</summary>
    string EffectText { get; }

    /// <summary>Effect sentences. A power rank keeps the adjusted amount separate so it can be colored.</summary>
    IReadOnlyList<SpellDetailLine> EffectLines { get; }

    IReadOnlyList<SpellDetailLine> Details { get; }

    ImageSource? Icon { get; }

    /// <summary>Class and level on the spell panel, such as "ENC 44". Empty when that line is hidden.</summary>
    string PanelLevel { get; }

    bool IsUnusable { get; }
}

public sealed class SpellSearchHit : ISpellDetails
{
    public const string DragFormat = "EqlLoadoutSpellId";

    public SpellSearchHit(int id, string name, string classLevel, bool unusable, ImageSource? icon, string description, string effectText, IReadOnlyList<SpellDetailLine> details, IReadOnlyList<SpellDetailLine>? effectLines = null)
    {
        Id = id;
        Name = name;
        ClassLevel = classLevel;
        IsUnusable = unusable;
        Icon = icon;
        Description = description;
        EffectText = effectText;
        Details = details;
        EffectLines = effectLines ?? [];
    }

    public int Id { get; }

    public string Name { get; }

    public ImageSource? Icon { get; }

    /// <summary>The selected class at level 50 or below that learns this spell latest, e.g. "ENC 44".</summary>
    public string ClassLevel { get; }

    public string PanelLevel => ClassLevel;

    public bool IsUnusable { get; }

    /// <summary>Spell book text with the damage and heal numbers filled in.</summary>
    public string Description { get; }

    public string EffectText { get; }

    public IReadOnlyList<SpellDetailLine> EffectLines { get; }

    /// <summary>Mana, times, resist, and the other lines shown in the details column.</summary>
    public IReadOnlyList<SpellDetailLine> Details { get; }
}

public sealed class SpellGem : ObservableObject, ISpellDetails
{
    private readonly SpellRow _row;
    private readonly SpellCatalog _catalog;
    private SpellIcons _icons;
    private string _value = string.Empty;

    public SpellGem(SpellRow row, int number, SpellCatalog catalog, SpellIcons icons)
    {
        _row = row;
        _catalog = catalog;
        _icons = icons;
        Number = number;
    }

    public int Number { get; }

    public bool CanClear => true;

    public void Clear() => Choose(SpellChoice.Empty);

    public void UseIcons(SpellIcons icons)
    {
        _icons = icons;
        Raise(nameof(Icon));
    }

    public void Choose(SpellChoice choice)
    {
        var stored = choice.IsEmpty ? "-1" : choice.Id.ToString(CultureInfo.InvariantCulture);
        if (Set(stored))
        {
            _row.GemChanged(this);
        }
    }

    public string Value
    {
        get => _value;
        set
        {
            if (Set(value))
            {
                _row.GemChanged(this);
            }
        }
    }

    public bool IsEmpty => SpellCatalog.IsEmptyGem(_value);

    private ISpellDetails? _upgrade;

    /// <summary>A stronger spell of the same kind, when one is available for this gem.</summary>
    public ISpellDetails? Upgrade => _upgrade;

    public bool HasUpgrade => _upgrade is not null;

    public void SetUpgrade(ISpellDetails? upgrade)
    {
        if (ReferenceEquals(_upgrade, upgrade))
        {
            return;
        }

        _upgrade = upgrade;
        Raise(nameof(Upgrade));
        Raise(nameof(HasUpgrade));
    }

    public string SpellName => IsEmpty
        ? "Empty"
        : _catalog.TryGetName(_value, out var name)
            ? _row.PowerName(name)
            : _catalog.Count == 0 ? "Spell list not found" : "Unknown spell";

    public ImageSource? Icon
    {
        get
        {
            if (!_catalog.TryGet(_value, out var spell) || spell.Icon < 0)
            {
                return null;
            }

            return _icons.For(spell.Icon);
        }
    }

    public string ResolvedName => _catalog.TryGetName(_value, out var name) ? name : string.Empty;

    /// <summary>The loadout class at level 50 or below that learns this spell latest, e.g. "ENC 44".</summary>
    public string LoadoutLevels
    {
        get
        {
            if (Spell?.BestWithin(_row.Classes, SpellCatalog.MaxSearchLevel) is not { } best)
            {
                return string.Empty;
            }

            return $"{CharacterClasses.Codes[best.Class]} {best.Level}";
        }
    }

    public bool HasLevels => LoadoutLevels.Length > 0;

    public string PanelLevel => LoadoutLevels;

    public string Name => Listed is { } spell ? _row.PowerName(spell.Name) : SpellName;

    public string Description => Listed is { } spell ? SpellPresentation.Describe(spell, _catalog.Description(spell.Id), _row.CasterLevel) : string.Empty;

    public string EffectText => Listed is { } spell ? SpellPresentation.EffectText(spell, _catalog, _row.CasterLevel, _row.PowerRank(spell.Name)) : string.Empty;

    public IReadOnlyList<SpellDetailLine> EffectLines => Listed is { } spell ? SpellPresentation.EffectLines(spell, _catalog, _row.CasterLevel, _row.PowerRank(spell.Name)) : [];

    /// <summary>Mana, times, resist, and the other lines shown in the details column.</summary>
    public IReadOnlyList<SpellDetailLine> Details => Listed is { } spell ? SpellPresentation.Lines(spell, _catalog.SpellLine(spell.Id), _row.CasterLevel, _row.PowerRank(spell.Name)) : [];

    /// <summary>True when the loadout's classes are known and none of them can use this spell at level 50 or below.</summary>
    public bool IsUnusable => Spell is { } spell
        && _row.Classes.Count > 0
        && spell.BestWithin(_row.Classes, SpellCatalog.MaxSearchLevel) is null;

    public string UsableTip
    {
        get
        {
            if (Spell is not { } spell)
            {
                return SpellName;
            }

            var usable = spell.UsableBy();
            var lines = new List<string> { _row.PowerName(spell.Name) };
            if (IsUnusable)
            {
                lines.Add($"{CharacterClasses.Format(_row.Classes)} can't use this spell.");
            }

            lines.Add(usable.Count == 0
                ? "No class can use this spell."
                : "Usable by " + string.Join(", ", usable.Select(entry => $"{CharacterClasses.Names[entry.Class]} {entry.Level}")));
            return string.Join(Environment.NewLine, lines);
        }
    }

    private SpellInfo? Spell => _catalog.TryGet(_value, out var spell) && spell.HasClassLevels ? spell : null;

    private SpellInfo? Listed => _catalog.TryGet(_value, out var spell) ? spell : null;

    public bool Set(string value)
    {
        if (_value == value)
        {
            return false;
        }

        _value = value;
        Raise(nameof(Value));
        Raise(nameof(IsEmpty));
        Raise(nameof(SpellName));
        Raise(nameof(ResolvedName));
        Raise(nameof(Icon));
        RaiseClasses();
        SetUpgrade(null);
        return true;
    }


    internal void RaiseClasses()
    {
        Raise(nameof(LoadoutLevels));
        Raise(nameof(HasLevels));
        Raise(nameof(PanelLevel));
        Raise(nameof(Name));
        Raise(nameof(Description));
        Raise(nameof(EffectText));
        Raise(nameof(EffectLines));
        Raise(nameof(Details));
        Raise(nameof(IsUnusable));
        Raise(nameof(UsableTip));
    }

    internal void RaisePowerName()
    {
        Raise(nameof(SpellName));
        Raise(nameof(Name));
        Raise(nameof(Description));
        Raise(nameof(EffectText));
        Raise(nameof(EffectLines));
        Raise(nameof(Details));
        Raise(nameof(UsableTip));
    }
}

public sealed class SpellRow : ObservableObject
{
    private readonly IniSection _section;
    private readonly Action _edited;
    private readonly Func<bool>? _allowEdit;
    private bool _mute;
    private string _inUse = string.Empty;
    private string _name = string.Empty;

    private Func<string, string>? _powerName;
    private Func<string, int>? _powerRank;

    public SpellRow(int index, IniSection section, Action edited, SpellCatalog catalog, SpellIcons icons, IReadOnlyList<int> classes, Func<bool>? allowEdit = null)
    {
        Index = index;
        _section = section;
        _edited = edited;
        _allowEdit = allowEdit;
        Classes = classes;
        Gems = Enumerable.Range(1, 14).Select(number => new SpellGem(this, number, catalog, icons)).ToList();
    }

    public void UsePowerNames(Func<string, string> powerName, Func<string, int>? powerRank = null)
    {
        _powerName = powerName;
        _powerRank = powerRank;
        RefreshPowerNames();
    }

    public string PowerName(string name) => _powerName?.Invoke(name) ?? name;

    public int PowerRank(string name) => name.Length == 0 ? 0 : _powerRank?.Invoke(name) ?? 0;

    public void RefreshPowerNames()
    {
        foreach (var gem in Gems)
        {
            gem.RaisePowerName();
        }

        Raise(nameof(Summary));
    }

    public void AssignSpells(string name, IReadOnlyList<string> slots)
    {
        _mute = true;
        Name = name;
        for (var index = 0; index < Gems.Count && index < slots.Count; index++)
        {
            Gems[index].Set(slots[index]);
        }

        _mute = false;
        Raise(nameof(Summary));
        RaiseUsability();
        Commit();
    }

    public void UseIcons(SpellIcons icons)
    {
        foreach (var gem in Gems)
        {
            gem.UseIcons(icons);
        }
    }

    public int Index { get; }

    public IReadOnlyList<SpellGem> Gems { get; }

    /// <summary>The loadout's classes, used to flag gems none of them can use.</summary>
    public IReadOnlyList<int> Classes { get; private set; }

    /// <summary>Character level used to scale spell duration and effects. The level cap when /who has not reported one.</summary>
    public int CasterLevel { get; private set; } = SpellCatalog.MaxSearchLevel;

    public void SetCasterLevel(int level)
    {
        if (level < 1)
        {
            level = SpellCatalog.MaxSearchLevel;
        }

        if (CasterLevel == level)
        {
            return;
        }

        CasterLevel = level;
        foreach (var gem in Gems)
        {
            gem.RaiseClasses();
        }
    }

    public int UnusableCount => Gems.Count(gem => gem.IsUnusable);

    public string UsabilityText => UnusableCount switch
    {
        0 => string.Empty,
        1 => "1 gem unusable",
        var count => $"{count} gems unusable",
    };

    public void SetClasses(IReadOnlyList<int> classes)
    {
        Classes = classes;
        foreach (var gem in Gems)
        {
            gem.RaiseClasses();
        }

        RaiseUsability();
    }

    public string Summary
    {
        get
        {
            var names = Gems.Select(gem => gem.ResolvedName).Where(name => name.Length > 0).ToList();
            var unknown = Gems.Count(gem => !gem.IsEmpty && gem.ResolvedName.Length == 0);
            if (unknown > 0)
            {
                names.Add(unknown == 1 ? "1 unknown" : $"{unknown} unknown");
            }

            return names.Count == 0 ? "No gems" : string.Join(", ", names);
        }
    }

    public bool IsActive => _inUse == "1";

    public string InUse
    {
        get => _inUse;
        set => UpdateField(ref _inUse, value, nameof(InUse));
    }

    public string InUseText
    {
        get => _inUse == "1" ? "True" : "False";
        set
        {
            var stored = value.Trim();
            var next = stored.Equals("True", StringComparison.OrdinalIgnoreCase) || stored == "1" ? "1"
                : stored.Equals("False", StringComparison.OrdinalIgnoreCase) || stored == "0" ? "0"
                : null;
            if (next is null || _inUse == next)
            {
                Raise(nameof(InUseText));
                return;
            }

            InUse = next;
        }
    }

    public string Name
    {
        get => _name;
        set => UpdateField(ref _name, value, nameof(Name));
    }

    public void Load(SpellGemSet set)
    {
        _mute = true;
        InUse = set.InUse;
        Name = set.Name;
        for (var i = 0; i < Gems.Count; i++)
        {
            Gems[i].Set(set.Slots[i] ?? string.Empty);
        }

        _mute = false;
        Raise(nameof(IsActive));
        Raise(nameof(Summary));
        RaiseUsability();
    }

    internal void GemChanged(SpellGem changed)
    {
        var slots = new string[Gems.Count];
        var kept = -1;
        for (var index = 0; index < Gems.Count; index++)
        {
            slots[index] = Gems[index].Value;
            if (ReferenceEquals(Gems[index], changed))
            {
                kept = index;
            }
        }

        if (SpellGems.KeepSingleGem(slots, kept))
        {
            for (var index = 0; index < Gems.Count; index++)
            {
                if (!string.Equals(Gems[index].Value, slots[index], StringComparison.Ordinal))
                {
                    Gems[index].Set(slots[index]);
                }
            }
        }

        Raise(nameof(Summary));
        RaiseUsability();
        Commit();
    }

    public void Swap(SpellGem source, SpellGem target)
    {
        if (ReferenceEquals(source, target) || !Gems.Contains(source) || !Gems.Contains(target) || source.IsEmpty)
        {
            return;
        }

        var sourceValue = source.Value;
        var targetValue = target.Value;
        if (string.Equals(sourceValue, targetValue, StringComparison.Ordinal))
        {
            return;
        }

        source.Set(targetValue);
        target.Set(sourceValue);
        Raise(nameof(Summary));
        RaiseUsability();
        Commit();
    }

    private void RaiseUsability()
    {
        Raise(nameof(UnusableCount));
        Raise(nameof(UsabilityText));
    }

    private void UpdateField(ref string field, string value, string name)
    {
        if (field == value)
        {
            return;
        }

        field = value;
        Raise(name);
        if (name == nameof(InUse))
        {
            Raise(nameof(InUseText));
        }

        Raise(nameof(IsActive));
        Commit();
    }

    private void Commit()
    {
        if (_mute)
        {
            return;
        }

        if (_allowEdit?.Invoke() == false)
        {
            var existing = SpellGems.Read(_section).FirstOrDefault(set => set.Index == Index);
            if (existing is not null)
            {
                Load(existing);
            }

            return;
        }

        var set = new SpellGemSet(Index)
        {
            InUse = InUse,
            Name = Name,
        };
        for (var i = 0; i < Gems.Count; i++)
        {
            set.Slots[i] = Gems[i].Value;
        }

        SpellGems.WriteSet(_section, set);
        _edited();
    }
}

/// <summary>Raw keys stay in natural order: letters alphabetically, numbers by value.</summary>
internal sealed class RawKeyOrder : System.Collections.IComparer
{
    public static RawKeyOrder ByKeyAscending { get; } = new(byValue: false, ascending: true);

    private readonly bool _byValue;
    private readonly int _sign;

    public RawKeyOrder(bool byValue, bool ascending)
    {
        _byValue = byValue;
        _sign = ascending ? 1 : -1;
    }

    public int Compare(object? x, object? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is not RawRow left)
        {
            return y is RawRow ? -1 : 0;
        }

        if (y is not RawRow right)
        {
            return 1;
        }

        var compared = _byValue
            ? string.Compare(left.Value, right.Value, StringComparison.OrdinalIgnoreCase)
            : NaturalOrder.Compare(left.Key, right.Key);
        if (compared == 0)
        {
            compared = _byValue
                ? NaturalOrder.Compare(left.Key, right.Key)
                : string.Compare(left.Value, right.Value, StringComparison.Ordinal);
        }

        return compared * _sign;
    }
}

/// <summary>Keeps the spell-set grid alphabetical. Blank names follow named sets.</summary>
internal sealed class SpellSetOrder : System.Collections.IComparer
{
    public static SpellSetOrder Instance { get; } = new();

    private SpellSetOrder()
    {
    }

    public int Compare(object? x, object? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is not SpellRow left)
        {
            return y is SpellRow ? -1 : 0;
        }

        if (y is not SpellRow right)
        {
            return 1;
        }

        return SpellWindows.CompareByName(left.Name, left.Index, right.Name, right.Index);
    }
}

public sealed class RawRow : ObservableObject
{
    private readonly Action _edited;
    private string _key = string.Empty;
    private string _value = string.Empty;

    public RawRow(Action edited)
    {
        _edited = edited;
    }

    public string Key
    {
        get => _key;
        set
        {
            if (!SetProperty(ref _key, value))
            {
                return;
            }

            _edited();
        }
    }

    public string Value
    {
        get => _value;
        set
        {
            if (!SetProperty(ref _value, value))
            {
                return;
            }

            _edited();
        }
    }
}

public readonly record struct ChatTabFace(string Name, bool Active);

public sealed class LayoutWindow
{
    public string Name { get; init; } = string.Empty;

    public string Label { get; init; } = string.Empty;

    /// <summary>Equal cells drawn across the top of a chat container, in ContainerTabIndex order. Empty for every other window.</summary>
    public IReadOnlyList<ChatTabFace> ChatTabs { get; init; } = [];

    public string Detail { get; init; } = string.Empty;

    public string ItemName { get; init; } = string.Empty;

    public string SlotSummary { get; init; } = string.Empty;

    public int SlotCount { get; init; }

    public IReadOnlyList<HotbarFace> Buttons { get; init; } = [];

    /// <summary>An inventory bag. Hot button bars also have slots, and they keep the size the game saved.</summary>
    public bool IsInventoryBag => SlotCount > 0 && !IsSpellWindow && Buttons.Count == 0;

    /// <summary>A CastSpell window. The book button edits its spell sets.</summary>
    public bool IsSpellWindow { get; init; }

    public IReadOnlyList<SpellGemFace> SpellGems { get; init; } = [];

    public IReadOnlyList<SpellSetOption> SpellSets { get; init; } = [];

    public IReadOnlySet<int> UsedSlots { get; init; } = new HashSet<int>();

    public double Left { get; set; }

    public double Top { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }

    public bool Sizable { get; init; } = true;

    public bool Hidden { get; init; }

    public bool HasStoredSize { get; init; }

    public bool Unplaced { get; init; }

    public string Category { get; init; } = "window";
}

public sealed record SpellGemFace(int Number, int Icon, string Name);

public sealed record SpellSetOption(int Index, string Name, bool Selected);

public sealed class GroupToggle : ObservableObject
{
    private bool? _checkState;
    private string _countText = "0 of 0";
    private bool _isExpanded;

    public GroupToggle(string name)
    {
        Name = name;
    }

    public string Name { get; }

    public bool? CheckState => _checkState;

    public string CountText => _countText;

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (!SetProperty(ref _isExpanded, value))
            {
                return;
            }

            Raise(nameof(Chevron));
            Raise(nameof(ExpandLabel));
        }
    }

    public string Chevron => _isExpanded ? "\uE70D" : "\uE76C";

    public string ExpandLabel => _isExpanded ? $"Collapse {Name}" : $"Expand {Name}";

    public void SetState(int shown, int total)
        => SetState(shown, total, total > 0 && shown == total ? true : shown > 0 ? (bool?)null : false);

    public void SetState(int shown, int total, bool? checkState)
    {
        var count = string.Create(CultureInfo.InvariantCulture, $"{shown} of {total}");
        if (_checkState != checkState)
        {
            _checkState = checkState;
            Raise(nameof(CheckState));
        }

        if (_countText != count)
        {
            _countText = count;
            Raise(nameof(CountText));
        }
    }
}

public sealed class ChatFilterGroup
{
    public required string Name { get; init; }

    public required string Filters { get; init; }
}
