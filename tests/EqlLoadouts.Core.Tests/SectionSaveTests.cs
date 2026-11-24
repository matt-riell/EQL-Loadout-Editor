using System.Text;
using EqlLoadouts.Core.Ini;
using EqlLoadouts.Core.Loadouts;

namespace EqlLoadouts.Core.Tests;

public class SectionSaveTests
{
    [Fact]
    public void CharacterReplace_WritesEditorSectionsAndKeepsTheRestOfTheDiskFile()
    {
        const string disk =
            "[Socials]\r\n" +
            "Page1Button1Name=Keep\r\n" +
            "Page1Button2Name=Old\r\n" +
            "[SpellLoadouts]\r\n" +
            "SpellLoadout1.inuse=1\r\n" +
            "SpellLoadout1.name=Attack\r\n" +
            "SpellLoadout2.inuse=1\r\n" +
            "SpellLoadout2.name=Heal\r\n" +
            "[Other]\r\n" +
            "Keep=disk\r\n";
        var baseline = IniDocument.Parse(
            "[Socials]\r\n" +
            "Page1Button1Name=Keep\r\n" +
            "Page1Button2Name=Old\r\n" +
            "[SpellLoadouts]\r\n" +
            "SpellLoadout1.inuse=1\r\n" +
            "SpellLoadout1.name=Attack\r\n" +
            "SpellLoadout2.inuse=1\r\n" +
            "SpellLoadout2.name=Heal\r\n" +
            "[Other]\r\n" +
            "Keep=editor\r\n");
        var edited = IniDocument.Parse(
            "[Socials]\r\n" +
            "Page1Button2Name=New\r\n" +
            "Page1Button3Name=Added\r\n" +
            "[SpellLoadouts]\r\n" +
            "SpellLoadout1.inuse=1\r\n" +
            "SpellLoadout1.name=Raid\r\n" +
            "[Other]\r\n" +
            "Keep=editor\r\n");

        var changes = SectionSave.ChangedSections(baseline, edited);
        Assert.Equal(["Socials", "SpellLoadouts"], changes.Select(change => change.Name).ToArray());

        var written = IniDocument.Parse(SectionSave.Apply(disk, changes.Select(SectionWrite.Replace).ToList()));
        Assert.Equal("New", written.Find("Socials")!.Get("Page1Button2Name"));
        Assert.Equal("Added", written.Find("Socials")!.Get("Page1Button3Name"));
        Assert.Null(written.Find("Socials")!.Get("Page1Button1Name"));
        Assert.Equal("Raid", written.Find("SpellLoadouts")!.Get("SpellLoadout1.name"));
        Assert.Null(written.Find("SpellLoadouts")!.Get("SpellLoadout2.name"));
        Assert.Equal("disk", written.Find("Other")!.Get("Keep"));
    }

    [Fact]
    public void CharacterMerge_KeepsDiskKeysTheEditorDoesNotHave()
    {
        const string disk =
            "[Socials]\r\n" +
            "Page1Button1Name=Keep\r\n" +
            "Page1Button2Name=Old\r\n" +
            "[SpellLoadouts]\r\n" +
            "SpellLoadout1.inuse=1\r\n" +
            "SpellLoadout1.name=Attack\r\n" +
            "SpellLoadout2.inuse=1\r\n" +
            "SpellLoadout2.name=Heal\r\n" +
            "[Other]\r\n" +
            "Keep=disk\r\n";
        var editor = new IniSection("Socials");
        editor.Set("Page1Button2Name", "New");
        editor.Set("Page1Button3Name", "Added");
        var spells = new IniSection("SpellLoadouts");
        spells.Set("SpellLoadout1.inuse", "1");
        spells.Set("SpellLoadout1.name", "Raid");

        var written = IniDocument.Parse(SectionSave.Apply(disk, [
            new SectionWrite("Socials", editor, SectionWriteMode.MergeNoDeletion),
            new SectionWrite("SpellLoadouts", spells, SectionWriteMode.MergeNoDeletion),
        ]));

        var socials = written.Find("Socials")!;
        Assert.Equal("Keep", socials.Get("Page1Button1Name"));
        Assert.Equal("New", socials.Get("Page1Button2Name"));
        Assert.Equal("Added", socials.Get("Page1Button3Name"));
        Assert.Equal(["Page1Button1Name", "Page1Button2Name", "Page1Button3Name"], socials.Entries.Select(entry => entry.Key).ToArray());
        var loadouts = written.Find("SpellLoadouts")!;
        Assert.Equal("Raid", loadouts.Get("SpellLoadout1.name"));
        Assert.Equal("Heal", loadouts.Get("SpellLoadout2.name"));
        Assert.Equal("disk", written.Find("Other")!.Get("Keep"));
    }

    [Fact]
    public void CharacterMerge_DoesNotDeleteASectionTheEditorRemoved()
    {
        const string disk = "[Socials]\r\nPage1Button1Name=Keep\r\n[Other]\r\nKeep=1\r\n";
        var written = IniDocument.Parse(SectionSave.Apply(disk, [
            new SectionWrite("Socials", null, SectionWriteMode.MergeNoDeletion),
        ]));

        Assert.Equal("Keep", written.Find("Socials")!.Get("Page1Button1Name"));
        Assert.Equal("1", written.Find("Other")!.Get("Keep"));
    }

    [Fact]
    public void SpellLoadouts_SaveOnlyWhenEditedAndStayOffOtherLoadouts()
    {
        const string disk =
            "[SpellLoadouts]\r\n" +
            "SpellLoadout1.name=Attack\r\n" +
            "SpellLoadout1.slot1=1\r\n" +
            "[CastSpellWnd]\r\n" +
            "XPos=2\r\n";
        var baseline = IniDocument.Parse(disk);
        var movedWindow = IniDocument.Parse(disk);
        movedWindow.Find("CastSpellWnd")!.Set("XPos", "8");

        var windowChanges = SectionSave.ChangedSections(baseline, movedWindow);
        Assert.Equal(["CastSpellWnd"], windowChanges.Select(change => change.Name).ToArray());
        Assert.False(SectionSave.CopiesToEveryLoadout(LoadoutFileKind.Character, "SpellLoadouts"));
        Assert.True(SectionSave.CopiesToEveryLoadout(LoadoutFileKind.Ui, "CastSpellWnd"));
        Assert.True(SectionSave.IncludeSpellLoadouts(destinationIsAll: false, isEditedLoadout: true));
        Assert.True(SectionSave.IncludeSpellLoadouts(destinationIsAll: true, isEditedLoadout: true));
        Assert.False(SectionSave.IncludeSpellLoadouts(destinationIsAll: true, isEditedLoadout: false));

        var written = IniDocument.Parse(SectionSave.Apply(disk, windowChanges.Select(SectionWrite.Replace).ToList()));
        Assert.Equal("Attack", written.Find("SpellLoadouts")!.Get("SpellLoadout1.name"));
        Assert.Equal("1", written.Find("SpellLoadouts")!.Get("SpellLoadout1.slot1"));
        Assert.Equal("8", written.Find("CastSpellWnd")!.Get("XPos"));

        var editedSpells = IniDocument.Parse(disk);
        editedSpells.Find("SpellLoadouts")!.Set("SpellLoadout1.name", "Raid");
        Assert.Equal(["SpellLoadouts"], SectionSave.ChangedSections(baseline, editedSpells).Select(change => change.Name).ToArray());
    }

    [Fact]
    public void UiSave_ReplacesOnlyChangedSections()
    {
        const string disk =
            "[HotButtonWnd]\r\nXPos=1\r\n" +
            "[CastSpellWnd]\r\nXPos=2\r\n" +
            "[BagInv1]\r\nXPos=9\r\n" +
            "[ExtraWnd]\r\nXPos=4\r\n";
        var baseline = IniDocument.Parse(
            "[HotButtonWnd]\r\nXPos=1\r\n" +
            "[CastSpellWnd]\r\nXPos=2\r\n" +
            "[BagInv1]\r\nXPos=3\r\n" +
            "[ChatWindow]\r\nXPos=5\r\n");
        var edited = IniDocument.Parse(
            "[HotButtonWnd]\r\nXPos=1\r\n" +
            "[CastSpellWnd]\r\nXPos=8\r\n" +
            "[BagInv1]\r\nXPos=3\r\n");

        var changes = SectionSave.ChangedSections(baseline, edited);
        Assert.Equal(["CastSpellWnd", "ChatWindow"], changes.Select(change => change.Name).ToArray());
        Assert.Null(changes[1].Edited);

        var written = IniDocument.Parse(SectionSave.Apply(disk, changes.Select(SectionWrite.Replace).ToList()));
        Assert.Equal("1", written.Find("HotButtonWnd")!.Get("XPos"));
        Assert.Equal("8", written.Find("CastSpellWnd")!.Get("XPos"));
        Assert.Equal("9", written.Find("BagInv1")!.Get("XPos"));
        Assert.Equal("4", written.Find("ExtraWnd")!.Get("XPos"));
        Assert.Null(written.Find("ChatWindow"));
    }

    [Fact]
    public void RebaseUnchanged_KeepsEditedSectionsAndTakesDiskForTheRest()
    {
        const string before = "[A]\r\nX=1\r\n[B]\r\nX=1\r\n";
        const string snapshot = "[A]\r\nX=1\r\n[B]\r\nX=2\r\n";
        const string after = "[A]\r\nX=9\r\n[B]\r\nX=3\r\n[C]\r\nX=4\r\n";

        var rebased = IniDocument.Parse(SectionSave.RebaseUnchanged(snapshot, before, after));
        Assert.Equal("9", rebased.Find("A")!.Get("X"));
        Assert.Equal("2", rebased.Find("B")!.Get("X"));
        Assert.Equal("4", rebased.Find("C")!.Get("X"));
    }

    [Fact]
    public void UndoBaseline_RestoresEditedSectionsAndKeepsSavedDiskSections()
    {
        const string original = "[A]\r\nX=1\r\n[B]\r\nX=1\r\n";
        const string after = "[A]\r\nX=2\r\n[B]\r\nX=9\r\n";
        var edited = IniDocument.Parse("[A]\r\nX=2\r\n[B]\r\nX=1\r\n");

        var baseline = IniDocument.Parse(SectionSave.UndoBaseline(original, after, edited));
        Assert.Equal("1", baseline.Find("A")!.Get("X"));
        Assert.Equal("9", baseline.Find("B")!.Get("X"));
    }

    [Fact]
    public void CopyFileName_LabelsSourceDestinationAndFileKind()
    {
        Assert.Equal(
            "Source Character - Zenator_rivervale_LO1.ini",
            SectionSave.CopyFileName("Source", LoadoutFileKind.Character, "Zenator_rivervale_LO1.ini"));
        Assert.Equal(
            "Destination UI - UI_Zenator_rivervale_LO2.ini",
            SectionSave.CopyFileName("Destination", LoadoutFileKind.Ui, "UI_Zenator_rivervale_LO2.ini"));
    }

    [Fact]
    public void Save_WritesTheBackupUnderModified()
    {
        var game = Directory.CreateTempSubdirectory();
        try
        {
            var path = Path.Combine(game.FullName, "Zenator_rivervale_LO1.ini");
            const string original = "[Socials]\r\nA=1\r\n[Other]\r\nKeep=1\r\n";
            const string updated = "[Socials]\r\nA=2\r\n[Other]\r\nKeep=1\r\n";
            File.WriteAllText(path, original);
            var timestamp = new DateTimeOffset(2026, 11, 7, 12, 30, 0, TimeSpan.FromHours(-5));

            var result = GameFileSaver.Save(
                game.FullName,
                [new PendingFileWrite(path, updated, new UTF8Encoding(false))],
                timestamp);

            Assert.True(result.Success);
            var folder = timestamp.LocalDateTime.ToString("yyyy-MM-dd HH-mm-ss", System.Globalization.CultureInfo.InvariantCulture);
            var backupFolder = Path.Combine(game.FullName, "LoadoutBackups", "MODIFIED", folder);
            var backup = Path.Combine(backupFolder, "Rivervale - Zenator", "LO01", "Character.ini");
            Assert.Equal(backupFolder, result.BackupDirectory);
            Assert.Equal(original, File.ReadAllText(backup));
            Assert.Equal(updated, File.ReadAllText(path));
            Assert.False(Directory.Exists(Path.Combine(game.FullName, "LoadoutBackups", "FULL")));
        }
        finally
        {
            game.Delete(recursive: true);
        }
    }

    [Fact]
    public void WriteCopies_DoesNotChangeTheLiveFile()
    {
        var game = Directory.CreateTempSubdirectory();
        try
        {
            var path = Path.Combine(game.FullName, "Zenator_rivervale_LO1.ini");
            const string original = "[Socials]\r\nA=1\r\n";
            File.WriteAllText(path, original);
            var timestamp = new DateTimeOffset(2026, 11, 7, 12, 31, 0, TimeSpan.FromHours(-5));
            var edited = new UTF8Encoding(false).GetBytes("[Socials]\r\nA=9\r\n");

            var id = new LoadoutId("Zenator", "rivervale", 1);
            var result = GameFileSaver.WriteCopies(
                game.FullName,
                [
                    new StoredBackup(id, LoadoutFileKind.Character, edited),
                    new StoredBackup(id, LoadoutFileKind.Ui, File.ReadAllBytes(path)),
                ],
                timestamp);

            Assert.True(result.Success);
            Assert.Equal(original, File.ReadAllText(path));
            var folder = timestamp.LocalDateTime.ToString("yyyy-MM-dd HH-mm-ss", System.Globalization.CultureInfo.InvariantCulture);
            var full = Path.Combine(game.FullName, "LoadoutBackups", "FULL", folder);
            Assert.Equal(full, result.BackupDirectory);
            var loadout = Path.Combine(full, "Rivervale - Zenator", "LO01");
            Assert.Equal(edited, File.ReadAllBytes(Path.Combine(loadout, "Character.ini")));
            Assert.Equal(original, File.ReadAllText(Path.Combine(loadout, "UI.ini")));
            Assert.Contains("Live game files were not changed.", result.Message, StringComparison.Ordinal);
            Assert.False(Directory.Exists(Path.Combine(game.FullName, "LoadoutBackups", "MODIFIED")));
        }
        finally
        {
            game.Delete(recursive: true);
        }
    }

    [Fact]
    public void Restore_CopiesFullAndModifiedBackupsIntoTheGameFolder()
    {
        var game = Directory.CreateTempSubdirectory();
        try
        {
            var character = Path.Combine(game.FullName, "Zenator_rivervale_LO1.ini");
            var ui = Path.Combine(game.FullName, "UI_Zenator_rivervale_LO1.ini");
            File.WriteAllText(character, "live-character");
            File.WriteAllText(ui, "live-ui");
            var timestamp = new DateTimeOffset(2026, 11, 7, 12, 40, 0, TimeSpan.FromHours(-5));
            var saved = GameFileSaver.Save(
                game.FullName,
                [new PendingFileWrite(character, "saved-character", new UTF8Encoding(false))],
                timestamp);
            Assert.True(saved.Success);

            var full = GameFileSaver.WriteCopies(
                game.FullName,
                [new StoredBackup(new LoadoutId("Zenator", "rivervale", 1), LoadoutFileKind.Character, Encoding.UTF8.GetBytes("full-character"))],
                timestamp.AddMinutes(1));
            Assert.True(full.Success);

            File.WriteAllText(character, "changed-after");
            var snapshots = GameFileSaver.List(game.FullName);
            var fullSnapshot = snapshots.Single(item => item.Kind == "FULL");
            var modifiedSnapshot = snapshots.Single(item => item.Kind == "MODIFIED");
            Assert.Equal("Rivervale - Zenator  LO01", fullSnapshot.Loadouts[0].Label);
            Assert.NotNull(fullSnapshot.Loadouts[0].CharacterSource);
            Assert.NotNull(modifiedSnapshot.Loadouts[0].CharacterSource);

            var restored = GameFileSaver.Restore(game.FullName, [fullSnapshot.Loadouts[0].CharacterSource!]);
            Assert.True(restored.Success);
            Assert.Equal("full-character", File.ReadAllText(character));
            Assert.Equal("live-ui", File.ReadAllText(ui));

            var rolled = GameFileSaver.Restore(game.FullName, [modifiedSnapshot.Loadouts[0].CharacterSource!]);
            Assert.True(rolled.Success);
            Assert.Equal("live-character", File.ReadAllText(character));
            Assert.False(GameFileSaver.Restore(game.FullName, [game.FullName]).Success);
        }
        finally
        {
            game.Delete(recursive: true);
        }
    }

    [Fact]
    public void CopySections_ReplacesHighlightedWindowsAndLeavesOtherSections()
    {
        var source = IniDocument.Parse(
            "[Inventory]\r\nX=10\r\nY=20\r\n" +
            "[Chat]\r\nX=4\r\n" +
            "[SpellLoadouts]\r\nSpellLoadout1.name=Attack\r\n");
        var destination = IniDocument.Parse(
            "[Inventory]\r\nX=1\r\nY=2\r\n" +
            "[Chat]\r\nX=4\r\n" +
            "[SpellLoadouts]\r\nSpellLoadout1.name=Heal\r\n" +
            "[BuffWindow]\r\nX=9\r\n");

        var highlighted = new[] { "Inventory", "Chat", "SpellLoadouts" }
            .Where(name => SectionSave.CopiesToEveryLoadout(LoadoutFileKind.Ui, name))
            .ToList();

        Assert.True(SectionSave.CopySections(source, destination, highlighted));
        Assert.Equal("10", destination.Find("Inventory")!.Get("X"));
        Assert.Equal("20", destination.Find("Inventory")!.Get("Y"));
        Assert.Equal("4", destination.Find("Chat")!.Get("X"));
        Assert.Equal("Heal", destination.Find("SpellLoadouts")!.Get("SpellLoadout1.name"));
        Assert.Equal("9", destination.Find("BuffWindow")!.Get("X"));
        Assert.False(SectionSave.CopySections(source, destination, highlighted));

        Assert.True(SectionSave.CopySections(source, destination, ["BuffWindow"]));
        Assert.Null(destination.Find("BuffWindow"));
        Assert.Equal("10", destination.Find("Inventory")!.Get("X"));
        Assert.Equal("Heal", destination.Find("SpellLoadouts")!.Get("SpellLoadout1.name"));
    }
}
