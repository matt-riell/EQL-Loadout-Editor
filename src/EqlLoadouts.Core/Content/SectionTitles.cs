using System.Globalization;

namespace EqlLoadouts.Core.Content;

/// <summary>
/// Names shown for loadout sections. The files store ids such as HotButtonWnd and SpellLoadouts.
/// </summary>
public static class SectionTitles
{
    public static string Format(string name)
    {
        if (name.Length == 0)
        {
            return name;
        }

        if (Names.TryGetValue(name, out var exact))
        {
            return exact;
        }

        if (TryHotbar(name, "HotButtonWnd", "Hot Button Bar", out var bar))
        {
            return bar;
        }

        if (TryHotbar(name, "HotButtons", "Hot Buttons", out var buttons))
        {
            return buttons;
        }

        var stem = name;
        int? index = null;
        var split = name.LastIndexOf('_');
        if (split > 0
            && name[(split + 1)..].All(char.IsDigit)
            && int.TryParse(name.AsSpan(split + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
        {
            stem = name[..split];
            // SpellDisplayWindow_99 and the other _99 sections are the base window, not window 99.
            if (parsed != 99)
            {
                index = parsed;
            }
        }
        else
        {
            var digit = stem.Length;
            while (digit > 0 && char.IsDigit(stem[digit - 1]))
            {
                digit--;
            }

            if (digit > 0
                && digit < stem.Length
                && char.IsLetter(stem[digit - 1])
                && int.TryParse(stem.AsSpan(digit), NumberStyles.None, CultureInfo.InvariantCulture, out var trailing))
            {
                stem = stem[..digit];
                index = ZeroBased.Contains(stem) ? trailing + 1 : trailing;
            }
        }

        var title = Names.TryGetValue(stem, out var known) ? known : Split(stem);
        return index is null ? title : string.Create(CultureInfo.InvariantCulture, $"{title} {index}");
    }

    private static bool TryHotbar(string name, string prefix, string title, out string formatted)
    {
        formatted = string.Empty;
        if (!name.StartsWith(prefix, StringComparison.Ordinal) || name.Length < prefix.Length)
        {
            return false;
        }

        var rest = name[prefix.Length..];
        var number = 1;
        if (rest.Length > 0)
        {
            if (!rest.All(char.IsDigit)
                || !int.TryParse(rest, NumberStyles.None, CultureInfo.InvariantCulture, out number))
            {
                return false;
            }
        }

        formatted = string.Create(CultureInfo.InvariantCulture, $"{title} {number}");
        return true;
    }

    private static string Split(string name)
    {
        var words = new List<string>();
        var current = new System.Text.StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            var letter = name[i];
            if (letter is '_' or ' ')
            {
                Push();
                continue;
            }

            if (current.Length > 0 && char.IsUpper(letter))
            {
                var previous = current[^1];
                var nextIsLower = i + 1 < name.Length && char.IsLower(name[i + 1]);
                if (char.IsLower(previous) || (char.IsUpper(previous) && nextIsLower && current.Length > 1))
                {
                    Push();
                }
            }

            current.Append(letter);
        }

        Push();
        return string.Join(' ', words.Select(Present));

        void Push()
        {
            if (current.Length == 0)
            {
                return;
            }

            var word = current.ToString();
            current.Clear();
            if (word.Equals("Wnd", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            words.Add(word);
        }
    }

    private static string Present(string word)
    {
        if (word is "EQ" or "AA" or "NPC" or "PvP" or "UI")
        {
            return word;
        }

        if (word.All(char.IsLetter) && word.All(char.IsLower) && word.Length > 1)
        {
            return char.ToUpperInvariant(word[0]) + word[1..];
        }

        if (word.Length > 3 && word.All(char.IsLetter) && word.All(char.IsUpper))
        {
            return char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant();
        }

        return word;
    }

    /// <summary>These item windows are numbered from 0 in the file. The list shows them starting at 1.</summary>
    private static readonly HashSet<string> ZeroBased = new(StringComparer.Ordinal)
    {
        "AchievementDisplay",
        "ItemDisplay",
    };

    private static readonly Dictionary<string, string> Names = new(StringComparer.Ordinal)
    {
        ["AAWindow"] = "AA Window",
        ["ADDITIONALFILTERS"] = "Additional Filters",
        ["AbilityDisplayWindow"] = "Ability Display",
        ["AchievementDisplay"] = "Achievement",
        ["AchievementsWnd"] = "Achievements",
        ["ActionsWindow"] = "Actions",
        ["AdvancedLootWnd"] = "Advanced Loot",
        ["AdventureLeaderboardWnd"] = "Adventure Leaderboard",
        ["AdventureRequestWnd"] = "Adventure Request",
        ["AdventureStatsWnd"] = "Adventure Stats",
        ["AggroMeterWnd"] = "Aggro Meter",
        ["AlertStackWnd"] = "Alert Stack",
        ["AlertsWindow"] = "Alerts",
        ["AltStorageWnd"] = "Alt Storage",
        ["AudioTriggersWindow"] = "Audio Triggers",
        ["AuraWindow"] = "Auras",
        ["BagBank"] = "Bank Bag",
        ["BagInv"] = "Bag",
        ["BagWorld"] = "World Bag",
        ["BandolierWnd"] = "Bandolier",
        ["BankWnd"] = "Bank",
        ["BarterMerchantWnd"] = "Barter Merchant",
        ["BarterSearchWnd"] = "Barter Search",
        ["BarterWnd"] = "Barter",
        ["BlockedBuffWnd"] = "Blocked Buffs",
        ["BlockedPetBuffWnd"] = "Blocked Pet Buffs",
        ["BlockedPetSpells"] = "Blocked Pet Spells",
        ["BlockedSpells"] = "Blocked Spells",
        ["BreathWindow"] = "Breath",
        ["BuffWindow"] = "Buffs",
        ["CascadeSubWnd"] = "Submenu",
        ["CascadeTopWnd"] = "Menu",
        ["CastSpellWnd"] = "Large Spellgems",
        ["CastSpellWnd_1"] = "Small Spellgems",
        ["CastSpellWnd_2"] = "Small Spellgems, Spell Names",
        ["CastSpellWnd_3"] = "Small Spellgems, Spell Names and Numbering",
        ["CastingWindow"] = "Casting Bar",
        ["ChatManager"] = "Chat Manager",
        ["ClaimWnd"] = "Claim",
        ["ColorPickerWnd"] = "Color Picker",
        ["Combat"] = "Combat",
        ["CompassWindow"] = "Compass",
        ["ConfirmationDialogBox"] = "Confirmation",
        ["CursorAttachment"] = "Cursor",
        ["Default"] = "Default",
        ["Defaults"] = "Defaults",
        ["DragonHoardWnd"] = "Dragon's Hoard",
        ["DynamicZoneWnd"] = "Dynamic Zone",
        ["EQMainWnd"] = "Main Menu",
        ["EditLabelWnd"] = "Edit Label",
        ["ExtendedTargetRoles"] = "Extended Target Roles",
        ["ExtendedTargetWnd"] = "Extended Target",
        ["FactionWnd"] = "Faction",
        ["FindItemWnd"] = "Find Item",
        ["FindLocationWnd"] = "Find Location",
        ["Friends"] = "Friends List",
        ["FriendsWindow"] = "Friends",
        ["GemsGameWnd"] = "Gems",
        ["GiveWnd"] = "Give",
        ["GroupSearchFiltersWnd"] = "Group Search Filters",
        ["GroupSearchWnd"] = "Group Search",
        ["GroupWindow"] = "Group",
        ["GuildManagementWnd"] = "Guild Management",
        ["HelpWindow"] = "Help",
        ["InspectWnd"] = "Inspect",
        ["InventoryWindow"] = "Inventory",
        ["ItemDisplay"] = "Item Window",
        ["ItemDisplayWindow"] = "Item Display",
        ["ItemOverflowWnd"] = "Item Overflow",
        ["JournalNPCWnd"] = "Journal",
        ["LFGuildWnd"] = "Looking for Guild",
        ["LargeDialogWindow"] = "Dialog",
        ["LoadoutWnd"] = "Loadouts",
        ["LoadskinWnd"] = "Load Skin",
        ["LootFiltersWnd"] = "Loot Filters",
        ["LootWnd"] = "Loot",
        ["MIZoneSelectWnd"] = "Zone Select",
        ["MMGW_ManageWnd"] = "Mercenaries",
        ["MailAddressBookWindow"] = "Address Book",
        ["MailCompositionWindow"] = "Compose Mail",
        ["MailIgnoreListWindow"] = "Ignore List",
        ["MailWindow"] = "Mail",
        ["Main"] = "Main",
        ["MainChat"] = "Main Chat",
        ["MapToolbarWnd"] = "Map Toolbar",
        ["MapViewMap"] = "Map View",
        ["MapViewWnd"] = "Map",
        ["MarketplaceWnd"] = "Marketplace",
        ["MerchantWnd"] = "Merchant",
        ["ObjectPreviewWnd"] = "Object Preview",
        ["OptionsWindow"] = "Options",
        ["OverseerWnd"] = "Overseer",
        ["PersonalInstanceWnd"] = "Personal Instance",
        ["PetInfoWindow"] = "Pet Info",
        ["PlayerCustomizationWnd"] = "Character Customization",
        ["PlayerWindow"] = "Player",
        ["PurchaseWnd"] = "Purchase",
        ["PvPStatsWnd"] = "PvP Stats",
        ["PvpLeaderboardWnd"] = "PvP Leaderboard",
        ["QuantityWnd"] = "Quantity",
        ["RaidRequestWnd"] = "Raid Request",
        ["RaidWindow"] = "Raid",
        ["RespawnWnd"] = "Respawn",
        ["RewardSelectionWnd"] = "Reward",
        ["SelectorWindow"] = "Selector",
        ["SendMoneyWnd"] = "Send Money",
        ["ShortDurationBuffWindow"] = "Short Buffs",
        ["SkillsWindow"] = "Skills",
        ["SocialEditWnd"] = "Edit Social",
        ["Socials"] = "Macros",
        ["SpellBookWnd"] = "Spell Book",
        ["SpellDisplayWindow"] = "Spell Display",
        ["SpellLoadouts"] = "Spell Loadouts",
        ["StanceWnd"] = "Stance",
        ["TargetOfTargetWindow"] = "Target of Target",
        ["TargetWindow"] = "Target",
        ["TaskOverlay"] = "Task Overlay",
        ["TaskOverlayWnd"] = "Task Overlay",
        ["TaskWnd"] = "Tasks",
        ["TextEntryWnd"] = "Text Entry",
        ["TipWindow"] = "Tip",
        ["TitleWnd"] = "Titles",
        ["TrackingWnd"] = "Tracking",
        ["TradeWnd"] = "Trade",
        ["TradeskillDepotWnd"] = "Tradeskill Depot",
        ["TradeskillWnd"] = "Tradeskills",
        ["TrainWindow"] = "Train",
        ["VideoModesWindow"] = "Video Modes",
        ["VoiceMacroWnd"] = "Voice Macros",
        ["ZoneGuideWnd"] = "Zone Guide",
        ["ZonePathWnd"] = "Zone Path",
        ["default_modern"] = "Modern Skin",
    };
}
