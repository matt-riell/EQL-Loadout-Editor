using EqlLoadouts.Core.Content;
using EqlLoadouts.Core.Loadouts;

namespace EqlLoadouts.Core.Tests;

public class GameFolderWatchTests
{
    private const string Root = @"C:\Games\EverQuest";

    [Theory]
    [InlineData(@"C:\Games\EverQuest\Zenator_rivervale_LO1.ini", WatchedGameFile.LoadoutIni)]
    [InlineData(@"C:\Games\EverQuest\UI_Zenator_rivervale_LO1.ini", WatchedGameFile.LoadoutIni)]
    [InlineData(@"C:\Games\EverQuest\eqclient.ini", WatchedGameFile.ClientSettings)]
    [InlineData(@"C:\Games\EverQuest\spells_us.txt", WatchedGameFile.SpellList)]
    [InlineData(@"C:\Games\EverQuest\spells_us_str.txt", WatchedGameFile.SpellMessages)]
    [InlineData(@"C:\Games\EverQuest\dbstr_us.txt", WatchedGameFile.StringTable)]
    [InlineData(@"C:\Games\EverQuest\Zenator_rivervale-Inventory.txt", WatchedGameFile.InventoryExport)]
    public void TopLevelGameFiles_AreClassified(string path, WatchedGameFile expected)
    {
        Assert.Equal(expected, GameFolderWatch.Classify(Root, path));
    }

    [Theory]
    [InlineData(@"C:\Games\EverQuest\Zenator_rivervale_LO1.ini.eql-tmp")]
    [InlineData(@"C:\Games\EverQuest\Zenator_rivervale_LO1_Backup_old.ini")]
    [InlineData(@"C:\Games\EverQuest\2026-11-03-22-12-00\Zenator_rivervale_LO1.ini")]
    [InlineData(@"C:\Games\EverQuest\2026-11-03-22-12-00\modified\Zenator_rivervale_LO1.ini")]
    [InlineData(@"C:\Games\EverQuest\uifiles\UI_Zenator_rivervale_LO1.ini")]
    [InlineData(@"C:\Games\EverQuest\Textures\icon.bmp")]
    [InlineData(@"C:\Games\EverQuest\notes.txt")]
    [InlineData(@"C:\Games\EverQuest\Logs\notes.txt")]
    public void BackupsTempsAndUnrelatedFiles_AreIgnored(string path)
    {
        Assert.Equal(WatchedGameFile.None, GameFolderWatch.Classify(Root, path));
    }

    [Fact]
    public void EveryCharacterLog_IsTailed()
    {
        var active = ActiveLog();
        Assert.Equal(WatchedGameFile.WhoLog, GameFolderWatch.Classify(Root + @"\", active));
        Assert.Equal(WatchedGameFile.WhoLog, GameFolderWatch.Classify(Root, Path.Combine(Root, "Logs", "eqlog_Other_rivervale.txt")));
        Assert.Equal(WatchedGameFile.None, GameFolderWatch.Classify(Root, Path.Combine(Root, "Logs", "notes.txt")));
    }

    private static string ActiveLog() => WhoLog.PathFor(Root, "Zenator", "rivervale");
}
