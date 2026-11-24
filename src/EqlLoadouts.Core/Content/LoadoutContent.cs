using System.Globalization;
using System.Text.RegularExpressions;
using EqlLoadouts.Core.Ini;

namespace EqlLoadouts.Core.Content;

public static partial class HotButtonText
{
    public static string ReadLabel(string value)
    {
        var parts = value.Split(',');
        if (parts.Length >= 6 && parts[5].Length > 0)
        {
            return parts[5];
        }

        if (parts.Length >= 5 && parts[4].Length > 0)
        {
            return parts[4];
        }

        return string.Empty;
    }

    public static string WriteLabel(string value, string label)
    {
        var parts = value.Split(',').ToList();
        if (parts.Count >= 6)
        {
            parts[5] = label;
            return string.Join(',', parts);
        }

        if (parts.Count == 5 && parts[4].Length > 0)
        {
            parts[4] = label;
            return string.Join(',', parts);
        }

        while (parts.Count < 5)
        {
            parts.Add(string.Empty);
        }

        if (parts.Count == 5)
        {
            parts.Add(label);
        }

        return string.Join(',', parts);
    }

    public const int ButtonsPerBar = 12;

    public static bool IsHotbarSection(string name)
    {
        return name.Equals("HotButtons", StringComparison.Ordinal)
            || (name.StartsWith("HotButtons", StringComparison.Ordinal)
                && name.Length > "HotButtons".Length
                && name["HotButtons".Length..].All(char.IsDigit));
    }

    public static bool IsHotButtonWindow(string name)
    {
        return name.Equals("HotButtonWnd", StringComparison.Ordinal)
            || (name.StartsWith("HotButtonWnd", StringComparison.Ordinal)
                && name.Length > "HotButtonWnd".Length
                && name["HotButtonWnd".Length..].All(char.IsDigit));
    }

    /// <summary>HotButtons pairs with HotButtonWnd, HotButtons2 with HotButtonWnd2, and so on.</summary>
    public static string? WindowSection(string buttonsName)
    {
        if (!IsHotbarSection(buttonsName))
        {
            return null;
        }

        var suffix = buttonsName.Length == "HotButtons".Length ? string.Empty : buttonsName["HotButtons".Length..];
        return "HotButtonWnd" + suffix;
    }

    /// <summary>HotButtonWnd pairs with HotButtons, HotButtonWnd2 with HotButtons2, and so on.</summary>
    public static string? ButtonsSection(string windowName)
    {
        if (!IsHotButtonWindow(windowName))
        {
            return null;
        }

        var suffix = windowName.Length == "HotButtonWnd".Length ? string.Empty : windowName["HotButtonWnd".Length..];
        return "HotButtons" + suffix;
    }

    public static string DisplayText(string label)
        => label.Replace("<BR>", "\n", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The twelve faces on one hotbar page. The window file does not store which page is open, so callers pass the page to draw.
    /// A button whose type is E{n} is social n, counted across pages of <see cref="ButtonsPerBar"/>.
    /// <paramref name="gems"/> supplies the memorized spell for an H button, which the game also saves without a label.
    /// <paramref name="abilities"/> supplies the alternate-ability name for an E button that is not a social.
    /// <paramref name="spells"/> supplies the name and icon for a P button, whose number is a spell id.
    /// </summary>
    public static IReadOnlyList<HotbarFace> ReadPage(
        IniSection? hotbar,
        IniSection? socials,
        int page,
        IReadOnlyList<HotbarGem>? gems = null,
        Func<int, string?>? abilities = null,
        Func<int, HotbarGem?>? spells = null)
    {
        var socialBySlot = SocialButtons.ForDisplay(socials).ToDictionary(slot => (slot.Page, slot.Slot));
        var faces = new HotbarFace[ButtonsPerBar];
        for (var slot = 1; slot <= ButtonsPerBar; slot++)
        {
            var key = string.Create(CultureInfo.InvariantCulture, $"Page{page}Button{slot}");
            var raw = hotbar?.Get(key) ?? string.Empty;
            var text = DisplayText(ReadLabel(raw));
            var color = string.Empty;
            var spellName = string.Empty;
            int? spellIcon = null;
            if (TrySocialRef(raw, out var socialPage, out var socialSlot)
                && socialBySlot.TryGetValue((socialPage, socialSlot), out var social))
            {
                if (text.Length == 0)
                {
                    text = DisplayText(social.Name);
                }

                color = social.Color;
            }

            if (TrySpell(raw, gems, spells, out var referenced, out spellIcon))
            {
                spellName = referenced;
                if (text.Length == 0 && referenced.Length > 0)
                {
                    text = referenced;
                }
            }
            else if (text.Length == 0 && TryUnnamed(raw, abilities, out var unnamed))
            {
                text = unnamed;
            }

            if (text.Length > 0 && color.Length == 0)
            {
                color = "0";
            }

            faces[slot - 1] = new HotbarFace(text, color, ReadIcon(raw), spellIcon, spellName);
        }

        return faces;
    }

    /// <summary>
    /// An H button is a memorized gem and a P button is a spell id. The picture and hover name come from that spell.
    /// A label the player typed still wins as the button text.
    /// </summary>
    private static bool TrySpell(
        string value,
        IReadOnlyList<HotbarGem>? gems,
        Func<int, HotbarGem?>? spells,
        out string name,
        out int? spellIcon)
    {
        name = string.Empty;
        spellIcon = null;
        if (!TryButtonId(value, out var kind, out var id))
        {
            return false;
        }

        HotbarGem? spell = kind switch
        {
            'H' => gems is not null && (uint)id < (uint)gems.Count ? gems[id] : null,
            'P' => spells?.Invoke(id),
            _ => null,
        };
        if (spell is not { } found)
        {
            return false;
        }

        var hasIcon = found.Icon >= 0;
        if (found.Name.Length == 0 && !hasIcon)
        {
            return false;
        }

        name = found.Name;
        spellIcon = hasIcon ? found.Icon : null;
        return true;
    }

    /// <summary>Attack, command, skill, and ability buttons are saved with an empty label. A label the player typed still wins.</summary>
    private static bool TryUnnamed(string value, Func<int, string?>? abilities, out string name)
    {
        name = string.Empty;
        if (!TryButtonId(value, out var kind, out var id) || kind is not ('B' or 'G' or 'J' or 'E'))
        {
            return false;
        }

        var label = kind switch
        {
            'B' => AbilityNames.Weapon(id),
            'G' => AbilityNames.Command(id),
            'J' => AbilityNames.Skill(id),
            'E' => abilities?.Invoke(id),
            _ => null,
        };
        if (label is null)
        {
            return false;
        }

        name = label;
        return true;
    }

    private static bool TryButtonId(string value, out char kind, out int id)
    {
        kind = '\0';
        id = 0;
        if (value.Length < 2 || !char.IsLetter(value[0]))
        {
            return false;
        }

        kind = char.ToUpperInvariant(value[0]);
        var end = 1;
        while (end < value.Length && value[end] != ',')
        {
            end++;
        }

        return int.TryParse(value.AsSpan(1, end - 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out id) && id >= 0;
    }

    /// <summary>The second field is an item icon such as A585. @-1 and anything else mean there is no item picture.</summary>
    public static int? ReadIcon(string value)
    {
        var comma = value.IndexOf(',');
        if (comma < 0 || comma + 1 >= value.Length)
        {
            return null;
        }

        var end = value.IndexOf(',', comma + 1);
        var field = end < 0 ? value[(comma + 1)..] : value[(comma + 1)..end];
        return ItemIconLayout.Parse(field);
    }

    private static bool TrySocialRef(string value, out int page, out int slot)
    {
        page = 0;
        slot = 0;
        if (value.Length < 2 || value[0] is not ('E' or 'e'))
        {
            return false;
        }

        var end = 1;
        while (end < value.Length && value[end] != ',')
        {
            end++;
        }

        if (!int.TryParse(value.AsSpan(1, end - 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) || index < 0)
        {
            return false;
        }

        page = index / ButtonsPerBar + 1;
        slot = index % ButtonsPerBar + 1;
        return true;
    }
}

public readonly record struct HotbarFace(string Text, string Color, int? Icon = null, int? SpellIcon = null, string SpellName = "");

/// <summary>One memorized gem, in gem order, for an H hotbar button. Icon -1 means the spell has no picture.</summary>
public readonly record struct HotbarGem(string Name, int Icon);

public sealed class SocialSlot
{
    public SocialSlot(int page, int slot)
    {
        Page = page;
        Slot = slot;
    }

    public int Page { get; }

    public int Slot { get; }

    public string Name { get; set; } = string.Empty;

    public string Color { get; set; } = string.Empty;

    public string Line1 { get; set; } = string.Empty;

    public string Line2 { get; set; } = string.Empty;

    public string Line3 { get; set; } = string.Empty;

    public string Line4 { get; set; } = string.Empty;

    public string Line5 { get; set; } = string.Empty;

    public bool IsEmpty =>
        Name.Length == 0
        && Color.Length == 0
        && Line1.Length == 0
        && Line2.Length == 0
        && Line3.Length == 0
        && Line4.Length == 0
        && Line5.Length == 0;

    public SocialSlot CopiedTo(int page, int slot)
    {
        return new SocialSlot(page, slot)
        {
            Name = Name,
            Color = Color,
            Line1 = Line1,
            Line2 = Line2,
            Line3 = Line3,
            Line4 = Line4,
            Line5 = Line5,
        };
    }
}

/// <summary>
/// Macros the client puts on every loadout. They are not written into the ini until someone edits that button.
/// Page 1 is the default social page: eqstr names 6652 through 6663 paired with commands 6664 through 6675.
/// The May 2025 client renamed most of them. Bug, Consider, and Time kept their original strings.
/// </summary>
public static class SystemSocials
{
    public static IReadOnlyList<SocialSlot> Slots()
    {
        return
        [
            Button(1, "Assist Main", "/assist main"),
            Button(2, "Auto Fire", "/autofire"),
            Button(3, "Auto Skill", "/autoskill"),
            Button(4, "Bug", "/bug"),
            Button(5, "Consider", "/con"),
            Button(6, "Corpse", "/corpse"),
            Button(7, "Hail", "/hail"),
            Button(8, "Open", "/open"),
            Button(9, "Pick Zone", "/pickzone"),
            Button(10, "Time", "/time"),
            Button(11, "Use Target", "/usetarget"),
            Button(12, "Who", "/who"),
        ];
    }

    private static SocialSlot Button(int slot, string name, string command)
    {
        return new SocialSlot(1, slot)
        {
            Name = name,
            Color = "0",
            Line1 = command,
        };
    }
}

public static partial class SocialButtons
{
    [GeneratedRegex(@"^Page(\d+)Button(\d+)(Name|Color|Line(\d+))$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();

    /// <summary>File slots plus any system macro whose button is not already in the file.</summary>
    public static IReadOnlyList<SocialSlot> ForDisplay(IniSection? section)
    {
        var stored = section is null ? [] : Read(section);
        var present = new HashSet<(int Page, int Slot)>(stored.Select(slot => (slot.Page, slot.Slot)));
        var shown = new List<SocialSlot>(stored);
        foreach (var system in SystemSocials.Slots())
        {
            if (!present.Contains((system.Page, system.Slot)))
            {
                shown.Add(system);
            }
        }

        return shown
            .OrderBy(slot => slot.Page)
            .ThenBy(slot => slot.Slot)
            .ToList();
    }

    public static IReadOnlyList<SocialSlot> Read(IniSection section)
    {
        var slots = new Dictionary<(int Page, int Slot), SocialSlot>();
        foreach (var entry in section.Entries)
        {
            var match = KeyPattern().Match(entry.Key);
            if (!match.Success)
            {
                continue;
            }

            var page = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            var slot = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            if (!slots.TryGetValue((page, slot), out var social))
            {
                social = new SocialSlot(page, slot);
                slots[(page, slot)] = social;
            }

            Assign(social, match.Groups[3].Value, match.Groups[4].Value, entry.Value);
        }

        return slots.Values
            .OrderBy(slot => slot.Page)
            .ThenBy(slot => slot.Slot)
            .ToList();
    }

    public static void WriteSlot(IniSection section, SocialSlot slot)
    {
        var prefix = string.Create(CultureInfo.InvariantCulture, $"Page{slot.Page}Button{slot.Slot}");
        if (slot.IsEmpty)
        {
            section.Remove(prefix + "Name");
            section.Remove(prefix + "Color");
            for (var line = 1; line <= 5; line++)
            {
                section.Remove(prefix + "Line" + line.ToString(CultureInfo.InvariantCulture));
            }

            return;
        }

        section.Set(prefix + "Name", slot.Name);
        section.Set(prefix + "Color", slot.Color);
        section.Set(prefix + "Line1", slot.Line1);
        section.Set(prefix + "Line2", slot.Line2);
        section.Set(prefix + "Line3", slot.Line3);
        section.Set(prefix + "Line4", slot.Line4);
        section.Set(prefix + "Line5", slot.Line5);
    }

    /// <summary>
    /// Leaves the button defined and blank. Removing the keys would bring a page-1 system macro back.
    /// </summary>
    public static void ClearSlot(IniSection section, int page, int slot)
    {
        var prefix = string.Create(CultureInfo.InvariantCulture, $"Page{page}Button{slot}");
        section.Set(prefix + "Name", string.Empty);
        section.Set(prefix + "Color", string.Empty);
        for (var line = 1; line <= 5; line++)
        {
            section.Set(prefix + "Line" + line.ToString(CultureInfo.InvariantCulture), string.Empty);
        }
    }

    private static void Assign(SocialSlot slot, string field, string lineNumber, string value)
    {
        if (field == "Name")
        {
            slot.Name = value;
            return;
        }

        if (field == "Color")
        {
            slot.Color = value;
            return;
        }

        switch (lineNumber)
        {
            case "1":
                slot.Line1 = value;
                break;
            case "2":
                slot.Line2 = value;
                break;
            case "3":
                slot.Line3 = value;
                break;
            case "4":
                slot.Line4 = value;
                break;
            case "5":
                slot.Line5 = value;
                break;
        }
    }
}

public sealed class SpellGemSet
{
    public SpellGemSet(int index)
    {
        Index = index;
        Slots = new string[14];
        Array.Fill(Slots, string.Empty);
    }

    public int Index { get; }

    public string InUse { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string[] Slots { get; }
}

public static partial class SpellGems
{
    [GeneratedRegex(@"^SpellLoadout(\d+)\.(inuse|name|slot(\d+))$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();

    public static IReadOnlyList<SpellGemSet> Read(IniSection section)
    {
        var sets = new Dictionary<int, SpellGemSet>();
        foreach (var entry in section.Entries)
        {
            var match = KeyPattern().Match(entry.Key);
            if (!match.Success)
            {
                continue;
            }

            var index = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            if (!sets.TryGetValue(index, out var set))
            {
                set = new SpellGemSet(index);
                sets[index] = set;
            }

            if (match.Groups[2].Value == "inuse")
            {
                set.InUse = entry.Value;
            }
            else if (match.Groups[2].Value == "name")
            {
                set.Name = entry.Value;
            }
            else if (int.TryParse(match.Groups[3].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var slot)
                && slot is >= 1 and <= 14)
            {
                set.Slots[slot - 1] = entry.Value;
            }
        }

        return sets.Values.OrderBy(set => set.Index).ToList();
    }

    public static void WriteSet(IniSection section, SpellGemSet set)
    {
        var prefix = KeyPrefix(set.Index);
        section.Set(prefix + "inuse", set.InUse);
        section.Set(prefix + "name", set.Name);
        for (var slot = 1; slot <= 14; slot++)
        {
            section.Set(prefix + "slot" + slot.ToString(CultureInfo.InvariantCulture), set.Slots[slot - 1]);
        }
    }

    /// <summary>Removes every SpellLoadout{index}.* key. SpellLoadout1 does not match SpellLoadout10.</summary>
    public static void RemoveSet(IniSection section, int index)
    {
        var prefix = KeyPrefix(index);
        section.Entries.RemoveAll(entry => entry.HasEquals && entry.Key.StartsWith(prefix, StringComparison.Ordinal));
    }

    public static bool HasSet(IniSection? section, int index)
    {
        if (section is null)
        {
            return false;
        }

        var prefix = KeyPrefix(index);
        return section.Entries.Any(entry => entry.HasEquals && entry.Key.StartsWith(prefix, StringComparison.Ordinal));
    }

    /// <summary>Spell set indexes present in <paramref name="beforeText"/> and absent from <paramref name="afterText"/>.</summary>
    public static IReadOnlyList<int> RemovedIndexes(string beforeText, string afterText)
    {
        var after = Indexes(afterText);
        return Indexes(beforeText).Where(index => !after.Contains(index)).Order().ToList();
    }

    /// <summary>Indexes from <paramref name="removed"/> that the file text still contains.</summary>
    public static IReadOnlyList<int> StillPresent(string fileText, IReadOnlyList<int> removed)
    {
        var section = IniDocument.Parse(fileText).Find("SpellLoadouts");
        return removed.Where(index => HasSet(section, index)).ToList();
    }

    private static HashSet<int> Indexes(string text)
    {
        var section = IniDocument.Parse(text).Find("SpellLoadouts");
        return section is null ? [] : Read(section).Select(set => set.Index).ToHashSet();
    }

    private static string KeyPrefix(int index)
        => string.Create(CultureInfo.InvariantCulture, $"SpellLoadout{index}.");

    /// <summary>A spell stays in the gem that was just set. Any earlier copy in the set is cleared.</summary>
    public static bool KeepSingleGem(IList<string> slots, int kept)
    {
        if (kept < 0 || kept >= slots.Count || SpellCatalog.IsEmptyGem(slots[kept]))
        {
            return false;
        }

        if (!int.TryParse(slots[kept].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
        {
            return false;
        }

        var changed = false;
        for (var index = 0; index < slots.Count; index++)
        {
            if (index == kept || SpellCatalog.IsEmptyGem(slots[index]))
            {
                continue;
            }

            if (int.TryParse(slots[index].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var other) && other == id)
            {
                slots[index] = "-1";
                changed = true;
            }
        }

        return changed;
    }
}
