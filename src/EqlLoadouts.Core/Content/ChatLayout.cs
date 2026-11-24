using System.Globalization;
using EqlLoadouts.Core.Ini;

namespace EqlLoadouts.Core.Content;

public sealed class NamedChatWindow
{
    public NamedChatWindow(int index, string name, int container, int tabIndex, IReadOnlyList<string> filters)
    {
        Index = index;
        Name = name;
        Container = container;
        TabIndex = tabIndex;
        Filters = filters;
    }

    public int Index { get; }

    public string Name { get; }

    public int Container { get; }

    /// <summary>Order of this tab inside its container. Zero is the tab the game shows open.</summary>
    public int TabIndex { get; }

    public IReadOnlyList<string> Filters { get; }
}

public static class ChatLayout
{
    // ChannelMap index is the filter-menu command id minus 1000.
    private static readonly string[] FilterNames =
    [
        "Say",
        "Tell",
        "Group",
        "Raid",
        "Guild",
        "OOC",
        "Auction",
        "Shout",
        "Emotes",
        "Your Hits",
        "Mine",
        "Skills",
        "Chat Channel 1",
        "Chat Channel 2",
        "Chat Channel 3",
        "Chat Channel 4",
        "Chat Channel 5",
        "Chat Channel 6",
        "Chat Channel 7",
        "Chat Channel 8",
        "Chat Channel 9",
        "Chat Channel 10",
        "Other (Misc)",
        "Your Misses",
        "You Being Hit",
        "You Being Missed",
        "Others Hits",
        "Others Misses",
        "My Death",
        "Other PC Death",
        "Critical Hits",
        "Combat Abilities / Disciplines (You)",
        "Melee Warnings",
        "NPC Rampage",
        "NPC Flurry",
        "NPC Enrage",
        "Others (Spells)",
        "Failures (Yours)",
        "Direct Damage (Your Critical Hits)",
        "Worn Off",
        "Direct Damage (Yours)",
        "Focus Effects",
        "Your Rolls",
        "Pet messages",
        "Pet Rampage/Flurry",
        "Pet Criticals",
        "Damage Shields (You Attacking)",
        "Experience Messages",
        "Filter 48",
        "System Messages",
        "/who",
        "Pet Spells",
        "Pet Responses",
        "Item Speech",
        "Fellowship Messages",
        "Mercenary Messages",
        "PvP Messages",
        "Your Flurry",
        "Filter 58",
        "NPC death",
        "Others Rolls",
        "Group / Raid Rolls",
        "Environmental Damage (Yours)",
        "Environmental Damage (Others)",
        "Damage Shields (You Defending)",
        "Damage Shields (Others)",
        "Event Messages",
        "Overwritten (Detrimental)",
        "Overwritten (Beneficial)",
        "Can't Use Command Warning",
        "Combat Ability Reuse",
        "AA Ability Reuse",
        "Destroyed Items",
        "Auras (You)",
        "Auras (Others)",
        "Heals (Yours)",
        "Heals (Others)",
        "DoTs (Yours)",
        "DoTs (Others)",
        "Songs",
        "Direct Damage (Others)",
        "Spell Emotes",
        "Faction Messages",
        "Loot Messages",
        "Taunt Messages",
        "Combat Abilities / Disciplines (Others)",
        "Achievements (You)",
        "Achievements (Others)",
        "Food and Drink Messages",
        "Raid Victory Messages",
        "Other (Green)",
        "Other (Blue)",
        "Other (Yellow)",
        "Other (Red)",
        "Chat Channel Info",
        "Consider Messages",
        "Direct Damage (You Being Hit)",
        "Direct Damage (Other Critical Hits)",
        "DoTs (Your Critical Hits)",
        "DoTs (Other Critical Hits)",
        "DoTs (You Being Hit)",
        "Heals (Received)",
        "Heals (Your Critical)",
        "Heals (Other Critical)",
        "Others Hits (Critical)",
        "Failures (Others)",
        "Runes (Yours)",
        "Heals (Received Critical)"
    ];
    public static bool IsChatWindow(string sectionName) => TryContainer(sectionName, out _);

    /// <summary>
    /// Channel routes and the chat-window definition the game uses to decide which windows and tabs exist.
    /// A key that is not part of that definition stays on the destination.
    /// </summary>
    public static bool IsFilterKey(string key)
    {
        if (key is "NumWindows" or "NumContainers" or "LockedActiveWindow")
        {
            return true;
        }

        if (HasIndex(key, "ChannelMap") || HasIndex(key, "HitMode"))
        {
            return true;
        }

        const string windowPrefix = "ChatWindow";
        if (!key.StartsWith(windowPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var split = key.IndexOf('_', windowPrefix.Length);
        return split > windowPrefix.Length
            && int.TryParse(key.AsSpan(windowPrefix.Length, split - windowPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out _);
    }

    /// <summary>
    /// Makes <paramref name="destination"/> use the chat windows, tabs, and channel filters from <paramref name="source"/>.
    /// Keys that are not part of that layout stay on the destination. Returns whether the destination changed.
    /// </summary>
    public static bool CopyFilters(IniSection source, IniSection destination)
    {
        var changed = false;
        var kept = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in source.Entries)
        {
            if (!entry.HasEquals || !IsFilterKey(entry.Key))
            {
                continue;
            }

            kept.Add(entry.Key);
            if (!string.Equals(destination.Get(entry.Key), entry.Value, StringComparison.Ordinal))
            {
                destination.Set(entry.Key, entry.Value);
                changed = true;
            }
        }

        var remove = destination.Entries
            .Where(entry => entry.HasEquals && IsFilterKey(entry.Key) && !kept.Contains(entry.Key))
            .Select(entry => entry.Key)
            .ToList();
        foreach (var key in remove)
        {
            destination.Remove(key);
            changed = true;
        }

        return changed;
    }

    public static bool TryContainer(string sectionName, out int container)
    {
        if (sectionName == "MainChat")
        {
            container = 0;
            return true;
        }

        const string prefix = "Chat ";
        if (sectionName.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(sectionName[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out container))
        {
            return true;
        }

        container = 0;
        return false;
    }

    public static IReadOnlyList<NamedChatWindow> Read(IniSection? manager)
    {
        if (manager is null)
        {
            return [];
        }

        var names = new Dictionary<int, string>();
        var containers = new Dictionary<int, int>();
        var tabIndexes = new Dictionary<int, int>();
        var routes = new List<(int Channel, int Window)>();
        foreach (var entry in manager.Entries)
        {
            if (!entry.HasEquals)
            {
                continue;
            }

            if (TrySuffix(entry.Key, "ChatWindow", "_Name", out var nameIndex) && !names.ContainsKey(nameIndex))
            {
                var name = string.Join(' ', entry.Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
                if (name.Length > 0)
                {
                    names[nameIndex] = name;
                }
            }
            else if (TrySuffix(entry.Key, "ChatWindow", "_ContainerIndex", out var containerIndex)
                && !containers.ContainsKey(containerIndex)
                && int.TryParse(entry.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var container))
            {
                containers[containerIndex] = container;
            }
            else if (TrySuffix(entry.Key, "ChatWindow", "_ContainerTabIndex", out var tabWindow)
                && !tabIndexes.ContainsKey(tabWindow)
                && int.TryParse(entry.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var tab))
            {
                tabIndexes[tabWindow] = tab;
            }
            else if (entry.Key.StartsWith("ChannelMap", StringComparison.Ordinal)
                && int.TryParse(entry.Key["ChannelMap".Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var channel)
                && int.TryParse(entry.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var window)
                && routes.All(route => route.Channel != channel))
            {
                routes.Add((channel, window));
            }
        }

        var filters = routes
            .GroupBy(route => route.Window)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group
                    .OrderBy(route => route.Channel)
                    .Select(route => FilterName(route.Channel))
                    .ToArray());

        return names
            .Where(pair => containers.ContainsKey(pair.Key))
            .OrderBy(pair => pair.Key)
            .Select(pair => new NamedChatWindow(
                pair.Key,
                pair.Value,
                containers[pair.Key],
                tabIndexes.TryGetValue(pair.Key, out var tabIndex) ? tabIndex : 0,
                filters.TryGetValue(pair.Key, out var windowFilters) ? windowFilters : []))
            .ToArray();
    }

    /// <summary>
    /// Tabs that share a chat container and have at least one channel filter, lowest ContainerTabIndex first.
    /// A tab with no ChannelMap entries is omitted. The first remaining tab is the one drawn open.
    /// </summary>
    public static IReadOnlyList<NamedChatWindow> Tabs(IReadOnlyList<NamedChatWindow> windows, string sectionName)
    {
        if (!TryContainer(sectionName, out var container))
        {
            return [];
        }

        return windows
            .Where(window => window.Container == container && window.Filters.Count > 0)
            .OrderBy(window => window.TabIndex)
            .ThenBy(window => window.Index)
            .ToArray();
    }

    /// <summary>
    /// Sections that are not chat containers stay visible. A chat container counts only when
    /// at least one of its tabs has a channel filter; a container whose tabs are all unfiltered
    /// is treated as a container with no tabs.
    /// </summary>
    public static bool HasTabs(IReadOnlyList<NamedChatWindow> windows, string sectionName)
    {
        return !TryContainer(sectionName, out _) || Tabs(windows, sectionName).Count > 0;
    }

    public static string Caption(IReadOnlyList<NamedChatWindow> windows, string sectionName)
    {
        if (!TryContainer(sectionName, out var container))
        {
            return string.Empty;
        }

        var names = windows
            .Where(window => window.Container == container && window.Filters.Count > 0)
            .OrderBy(window => window.Index)
            .Select(window => window.Name)
            .ToArray();
        return names.Length == 0 ? string.Empty : string.Join(", ", names);
    }

    public static string FilterName(int channel)
    {
        return channel >= 0 && channel < FilterNames.Length
            ? FilterNames[channel]
            : string.Create(CultureInfo.InvariantCulture, $"Filter {channel}");
    }

    private static bool HasIndex(string key, string prefix)
        => key.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(key.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out _);

    private static bool TrySuffix(string key, string prefix, string suffix, out int index)
    {
        if (key.StartsWith(prefix, StringComparison.Ordinal)
            && key.EndsWith(suffix, StringComparison.Ordinal)
            && key.Length > prefix.Length + suffix.Length
            && int.TryParse(key[prefix.Length..^suffix.Length], NumberStyles.None, CultureInfo.InvariantCulture, out index))
        {
            return true;
        }

        index = 0;
        return false;
    }
}
