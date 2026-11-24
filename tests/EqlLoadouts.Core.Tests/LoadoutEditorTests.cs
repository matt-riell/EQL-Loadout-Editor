using System.Globalization;
using System.Text;
using EqlLoadouts.Core.Content;
using EqlLoadouts.Core.Ini;
using EqlLoadouts.Core.Layout;
using EqlLoadouts.Core.Loadouts;

namespace EqlLoadouts.Core.Tests;

public class LoadoutEditorTests
{
    [Fact]
    public void RoundTrip_PreservesOrderValuesAndTrailingSpaces()
    {
        const string text =
            "[HotButtons]\r\n" +
            "Page1Button1=B0,@-1,0000000000000000,0,,\r\n" +
            "[BagInv1]\r\n" +
            "XRef=right\r\n" +
            "YPos=-20.416666%\r\n" +
            "Width=50\r\n" +
            "Height=356\r\n" +
            "PetButtonState=attack follow   \r\n";

        var document = IniDocument.Parse(text);

        Assert.Equal(text, document.Write());
    }

    [Fact]
    public void ReplaceSection_KeepsOtherSectionsAndAppendsAMissingSection()
    {
        var document = IniDocument.Parse("[Alpha]\r\nKeep=1\r\n[Beta]\r\nStay=2\r\n");
        var replacement = new IniSection("Alpha");
        replacement.Set("Keep", "9");
        replacement.Set("Added", "3");

        document.ReplaceSection(replacement);

        var added = new IniSection("Gamma");
        added.Set("New", "4");
        document.ReplaceSection(added);

        Assert.Equal(
            "[Alpha]\r\nKeep=9\r\nAdded=3\r\n[Beta]\r\nStay=2\r\n[Gamma]\r\nNew=4\r\n",
            document.Write());
    }

    [Fact]
    public void Discover_PairsLoadoutsAndSkipsBackupFiles()
    {
        var root = Directory.CreateTempSubdirectory();
        try
        {
            File.WriteAllText(Path.Combine(root.FullName, "Zenator_rivervale_LO1.ini"), "[A]\r\nX=1\r\n");
            File.WriteAllText(Path.Combine(root.FullName, "UI_Zenator_rivervale_LO1.ini"), "[BagInv1]\r\nWidth=50\r\n");
            File.WriteAllText(Path.Combine(root.FullName, "Zenator_halas_LO2.ini"), "[A]\r\nX=2\r\n");
            File.WriteAllText(Path.Combine(root.FullName, "UI_Zenator_freeport_LO1_Backup_1.ini"), "[A]\r\nX=9\r\n");
            File.WriteAllText(Path.Combine(root.FullName, "eqclient.ini"), "[Defaults]\r\nSound=1\r\n");

            var loadouts = LoadoutDiscovery.Discover(root.FullName);

            Assert.Equal(2, loadouts.Count);
            Assert.Equal("Zenator", loadouts[0].Id.Character);
            Assert.Equal("halas", loadouts[0].Id.Server);
            Assert.Equal(2, loadouts[0].Id.Number);
            Assert.NotNull(loadouts[1].CharacterFile);
            Assert.NotNull(loadouts[1].UiFile);
            Assert.Equal("rivervale", loadouts[1].Id.Server);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void Discover_SelectableCountIsTheChosenCharacterAndServer()
    {
        var root = Directory.CreateTempSubdirectory();
        try
        {
            File.WriteAllText(Path.Combine(root.FullName, "Zenator_rivervale_LO1.ini"), "[A]\r\nX=1\r\n");
            File.WriteAllText(Path.Combine(root.FullName, "UI_Zenator_rivervale_LO1.ini"), "[BagInv1]\r\nWidth=50\r\n");
            File.WriteAllText(Path.Combine(root.FullName, "Zenator_rivervale_LO2.ini"), "[A]\r\nX=2\r\n");
            File.WriteAllText(Path.Combine(root.FullName, "UI_Zenator_rivervale_LO2.ini"), "[BagInv1]\r\nWidth=50\r\n");
            File.WriteAllText(Path.Combine(root.FullName, "Zenator_halas_LO1.ini"), "[A]\r\nX=3\r\n");
            File.WriteAllText(Path.Combine(root.FullName, "UI_Zenator_halas_LO1.ini"), "[BagInv1]\r\nWidth=50\r\n");
            File.WriteAllText(Path.Combine(root.FullName, "Zenator_freeport_LO1_Backup_1.ini"), "[A]\r\nX=9\r\n");
            File.WriteAllText(Path.Combine(root.FullName, "UI_Zenator_rivervale_LO11_1.ini"), "[BagInv1]\r\nWidth=1\r\n");
            File.WriteAllText(Path.Combine(root.FullName, "eqclient.ini"), "[Defaults]\r\nSound=1\r\n");

            var loadouts = LoadoutDiscovery.Discover(root.FullName);
            var listed = LoadoutDiscovery.ForCharacterAndServer(loadouts, "Zenator", "rivervale");

            Assert.Equal(3, loadouts.Count);
            Assert.Equal(2, listed.Count);
            Assert.Equal([1, 2], listed.Select(loadout => loadout.Id.Number).ToArray());
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void CopySection_AllScopes_ExcludeSourceAndPreserveOtherSections()
    {
        var root = Directory.CreateTempSubdirectory();
        try
        {
            var source = WriteLoadout(root.FullName, "Zenator", "rivervale", 1, "A=1", "Stay=source");
            var sameServer = WriteLoadout(root.FullName, "Zenator", "rivervale", 2, "A=2", "Stay=same");
            var otherServer = WriteLoadout(root.FullName, "Zenator", "halas", 1, "A=3", "Stay=other");
            var all = LoadoutDiscovery.Discover(root.FullName);
            var section = IniFile.Load(source.CharacterFile!).Document.Find("Socials")!;

            var sameServerTargets = DestinationSet.Resolve(
                source, DestinationScope.AllForCharacterAndServer, null, all);
            var allTargets = DestinationSet.Resolve(source, DestinationScope.AllLoadouts, null, all);

            Assert.Equal(["Zenator — rivervale — LO2"], sameServerTargets.Select(item => item.Id.Title).ToArray());
            Assert.Equal(
                ["Zenator — halas — LO1", "Zenator — rivervale — LO2"],
                allTargets.Select(item => item.Id.Title).ToArray());

            var samePlan = SectionApplier.Plan(section, LoadoutFileKind.Character, sameServerTargets);
            var allPlan = SectionApplier.Plan(section, LoadoutFileKind.Character, allTargets);
            Assert.Null(samePlan.Error);
            Assert.Single(samePlan.Writes);
            Assert.Equal(2, allPlan.Writes.Count);

            Apply(samePlan);
            Assert.Contains("A=1", File.ReadAllText(sameServer.CharacterFile!), StringComparison.Ordinal);
            Assert.Contains("Stay=same", File.ReadAllText(sameServer.CharacterFile!), StringComparison.Ordinal);
            Assert.Contains("A=3", File.ReadAllText(otherServer.CharacterFile!), StringComparison.Ordinal);
            Assert.Contains("A=1", File.ReadAllText(source.CharacterFile!), StringComparison.Ordinal);

            var windowTargets = DestinationSet.WindowSaveLoadouts(source, DestinationScope.AllLoadouts, all);
            Assert.Equal(3, windowTargets.Count);
            Assert.Contains(windowTargets, loadout => loadout.Id == source.Id);
            var sameCharacterWindows = DestinationSet.WindowSaveLoadouts(
                source, DestinationScope.AllForCharacterAndServer, all);
            Assert.Equal([1, 2], sameCharacterWindows.Select(loadout => loadout.Id.Number).OrderBy(number => number).ToArray());

            Apply(allPlan);
            Assert.Contains("A=1", File.ReadAllText(otherServer.CharacterFile!), StringComparison.Ordinal);
            Assert.Contains("Stay=other", File.ReadAllText(otherServer.CharacterFile!), StringComparison.Ordinal);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void Save_BacksUpTheEntireFileBeforeWriting()
    {
        var root = Directory.CreateTempSubdirectory();
        try
        {
            var path = Path.Combine(root.FullName, "Zenator_rivervale_LO1.ini");
            const string original = "[Socials]\r\nPage1Button1Name=Old\r\n[Other]\r\nKeep=1\r\n";
            File.WriteAllText(path, original);
            var document = IniDocument.Parse(original);
            document.Find("Socials")!.Set("Page1Button1Name", "New");
            var timestamp = new DateTimeOffset(2026, 10, 17, 7, 50, 1, TimeSpan.FromHours(-7));

            var result = GameFileSaver.Save(
                root.FullName,
                [new PendingFileWrite(path, document.Write(), new UTF8Encoding(false))],
                timestamp);

            Assert.True(result.Success);
            var folder = timestamp.LocalDateTime.ToString("yyyy-MM-dd HH-mm-ss", CultureInfo.InvariantCulture);
            var backupFolder = Path.Combine(root.FullName, "LoadoutBackups", "MODIFIED", folder);
            var backup = Path.Combine(backupFolder, "Rivervale - Zenator", "LO01", "Character.ini");
            Assert.Equal(backupFolder, result.BackupDirectory);
            Assert.Contains($"Backup: {result.BackupDirectory}", result.Message, StringComparison.Ordinal);
            Assert.Equal(original, File.ReadAllText(backup));
            Assert.Contains("Page1Button1Name=New", File.ReadAllText(path), StringComparison.Ordinal);
            Assert.Contains("Keep=1", File.ReadAllText(path), StringComparison.Ordinal);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void Save_WritesNewTextIntoTheModifiedFolder()
    {
        var root = Directory.CreateTempSubdirectory();
        try
        {
            var path = Path.Combine(root.FullName, "Zenator_rivervale_LO1.ini");
            const string original = "[Socials]\r\nPage1Button1Name=Old\r\n[Other]\r\nKeep=1\r\n";
            const string updated = "[Socials]\r\nPage1Button1Name=New\r\n[Other]\r\nKeep=1\r\n";
            File.WriteAllText(path, original);
            var timestamp = new DateTimeOffset(2026, 11, 3, 22, 12, 0, TimeSpan.FromHours(-5));

            var result = GameFileSaver.Save(
                root.FullName,
                [new PendingFileWrite(path, updated, new UTF8Encoding(false))],
                timestamp,
                keepModifiedCopies: true);

            Assert.True(result.Success);
            var folder = timestamp.LocalDateTime.ToString("yyyy-MM-dd HH-mm-ss", CultureInfo.InvariantCulture);
            var backupFolder = Path.Combine(root.FullName, "LoadoutBackups", "MODIFIED", folder);
            Assert.Equal(backupFolder, result.BackupDirectory);
            Assert.Contains($"Backup: {backupFolder}", result.Message, StringComparison.Ordinal);
            var stored = Path.Combine("Rivervale - Zenator", "LO01", "Character.ini");
            Assert.Equal(original, File.ReadAllText(Path.Combine(backupFolder, stored)));
            Assert.Equal(updated, File.ReadAllText(Path.Combine(backupFolder, "modified", stored)));
            Assert.Equal(updated, File.ReadAllText(path));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void Save_LeavesNoModifiedFolderWhenTheOptionIsOff()
    {
        var root = Directory.CreateTempSubdirectory();
        try
        {
            var path = Path.Combine(root.FullName, "Zenator_rivervale_LO1.ini");
            const string original = "[Socials]\r\nPage1Button1Name=Old\r\n";
            const string updated = "[Socials]\r\nPage1Button1Name=New\r\n";
            File.WriteAllText(path, original);
            var timestamp = new DateTimeOffset(2026, 11, 3, 22, 12, 0, TimeSpan.FromHours(-5));

            var result = GameFileSaver.Save(
                root.FullName,
                [new PendingFileWrite(path, updated, new UTF8Encoding(false))],
                timestamp,
                keepModifiedCopies: false);

            Assert.True(result.Success);
            Assert.NotNull(result.BackupDirectory);
            Assert.Equal(original, File.ReadAllText(Path.Combine(result.BackupDirectory, "Rivervale - Zenator", "LO01", "Character.ini")));
            Assert.Equal(updated, File.ReadAllText(path));
            Assert.False(Directory.Exists(Path.Combine(result.BackupDirectory, "modified")));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void Save_DoesNotCreateAModifiedFolderWhenNothingIsWritten()
    {
        var root = Directory.CreateTempSubdirectory();
        try
        {
            var timestamp = new DateTimeOffset(2026, 11, 3, 22, 12, 0, TimeSpan.FromHours(-5));
            var result = GameFileSaver.Save(root.FullName, [], timestamp, keepModifiedCopies: true);

            Assert.True(result.Success);
            Assert.False(result.HadChanges);
            Assert.Empty(Directory.EnumerateFileSystemEntries(root.FullName));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void DeletedSpellSet_IsAbsentFromTheSavedFile()
    {
        var root = Directory.CreateTempSubdirectory();
        try
        {
            var path = Path.Combine(root.FullName, "Zenator_rivervale_LO1.ini");
            const string original =
                "[SpellLoadouts]\r\n" +
                "SpellLoadout1.inuse=1\r\n" +
                "SpellLoadout1.name=Attack\r\n" +
                "SpellLoadout1.slot1=6877\r\n" +
                "SpellLoadout2.inuse=1\r\n" +
                "SpellLoadout2.name=Heal\r\n" +
                "SpellLoadout10.inuse=1\r\n" +
                "SpellLoadout10.name=Keep\r\n" +
                "[Other]\r\nKeep=1\r\n";
            File.WriteAllText(path, original);
            var document = IniDocument.Parse(original);
            var section = document.Find("SpellLoadouts")!;
            SpellGems.RemoveSet(section, 1);
            Assert.Null(section.Get("SpellLoadout1.name"));
            Assert.Equal("Keep", section.Get("SpellLoadout10.name"));

            document = IniDocument.Parse(original);
            section = document.Find("SpellLoadouts")!;
            SpellGems.RemoveSet(section, 2);
            var written = document.Write();
            var removed = SpellGems.RemovedIndexes(original, written);
            Assert.Equal([2], removed);

            var result = GameFileSaver.Save(
                root.FullName,
                [new PendingFileWrite(path, written, new UTF8Encoding(false))],
                new DateTimeOffset(2026, 10, 27, 8, 30, 0, TimeSpan.FromHours(-7)));

            Assert.True(result.Success);
            var onDisk = File.ReadAllText(path);
            Assert.Empty(SpellGems.StillPresent(onDisk, removed));
            Assert.Contains("SpellLoadout1.name=Attack", onDisk, StringComparison.Ordinal);
            Assert.Contains("SpellLoadout10.name=Keep", onDisk, StringComparison.Ordinal);
            Assert.DoesNotContain("SpellLoadout2.", onDisk, StringComparison.Ordinal);
            Assert.Contains("Keep=1", onDisk, StringComparison.Ordinal);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void SocialEdit_IsWrittenBackToTheFile()
    {
        var root = Directory.CreateTempSubdirectory();
        try
        {
            var path = Path.Combine(root.FullName, "character.ini");
            const string original = "[Socials]\r\nPage2Button2Name=Gnome\r\nPage2Button2Color=0\r\nPage2Button2Line1=/say new warrior\r\n";
            File.WriteAllText(path, original);
            var document = IniDocument.Parse(original);
            var section = document.Find("Socials")!;
            var slot = Assert.Single(SocialButtons.Read(section));
            slot.Name = "GnomeSaveCheck";
            slot.Line2 = "/say saved";
            SocialButtons.WriteSlot(section, slot);

            var result = GameFileSaver.Save(
                root.FullName,
                [new PendingFileWrite(path, document.Write(), new UTF8Encoding(false))],
                new DateTimeOffset(2026, 10, 23, 6, 30, 0, TimeSpan.FromHours(-7)));

            Assert.True(result.Success);
            var saved = SocialButtons.Read(IniDocument.Parse(File.ReadAllText(path)).Find("Socials")!);
            var again = Assert.Single(saved);
            Assert.Equal("GnomeSaveCheck", again.Name);
            Assert.Equal("0", again.Color);
            Assert.Equal("/say new warrior", again.Line1);
            Assert.Equal("/say saved", again.Line2);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void SocialColors_MatchTheSocialEditorSwatches()
    {
        Assert.Equal(20, SocialColors.Count);
        Assert.Equal((byte)240, SocialColors.Rgb("0").R);
        Assert.Equal(SocialColors.Rgb("13"), SocialColors.At(13));
        Assert.Equal(((byte)240, (byte)0, (byte)0), SocialColors.Rgb("13"));
        Assert.Equal(((byte)0, (byte)240, (byte)0), SocialColors.Rgb("14"));
        Assert.Equal(((byte)240, (byte)240, (byte)0), SocialColors.Rgb("15"));
        Assert.Equal(SocialColors.Rgb("0"), SocialColors.Rgb("nope"));
    }

    [Fact]
    public void Save_WritesNothingWhenBackupFails()
    {
        var root = Directory.CreateTempSubdirectory();
        try
        {
            var real = Path.Combine(root.FullName, "Zenator_rivervale_LO2.ini");
            const string original = "[Socials]\r\nA=2\r\n";
            File.WriteAllText(real, original);
            var missing = Path.Combine(root.FullName, "missing.ini");
            var timestamp = new DateTimeOffset(2026, 10, 17, 8, 0, 0, TimeSpan.FromHours(-7));

            var result = GameFileSaver.Save(
                root.FullName,
                [
                    new PendingFileWrite(missing, "[Socials]\r\nA=1\r\n", Encoding.UTF8),
                    new PendingFileWrite(real, "[Socials]\r\nA=9\r\n", Encoding.UTF8),
                ],
                timestamp);

            Assert.False(result.Success);
            Assert.Equal(original, File.ReadAllText(real));
            Assert.False(File.Exists(missing));
            Assert.DoesNotContain("A=9", File.ReadAllText(real), StringComparison.Ordinal);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void Placement_UsesAnchorsFromTheLoadoutFile()
    {
        var mainChat = WindowPlacement.ReadRect(
            Section("XRef=left", "YRef=bottom", "XPos=0.117187%", "YPos=0.277778%", "Width=908", "Height=303"),
            2560,
            1440,
            100,
            40);
        Assert.Equal(3, mainChat.Left, 3);
        Assert.Equal(1133, mainChat.Top, 3);

        var hotbar = WindowPlacement.ReadRect(
            Section("XRef=center", "YRef=bottom", "XPos=-0.039062%", "YPos=16.319443%", "Width=525", "Height=52"),
            2560,
            1440,
            100,
            40);
        Assert.Equal(1016.5, hotbar.Left, 3);
        Assert.InRange(hotbar.Top, 1140, 1160);

        var rect = WindowPlacement.ReadRect(
            Section("XRef=right", "YRef=center", "XPos=10%", "YPos=0%", "Width=100", "Height=50"),
            1000,
            500,
            10,
            10);
        Assert.Equal(800, rect.Left, 3);
        Assert.Equal(225, rect.Top, 3);

        var version1 = WindowPlacement.ReadRect(
            Section("INIVersion=1", "XRef=center", "YRef=center", "XPos=38.124996%", "YPos=-20.347219%", "Width=136", "Height=357"),
            2560,
            1440,
            10,
            10);
        Assert.Equal(1700, version1.Left, 1);
        Assert.Equal(395, version1.Top, 1);

        var version2 = WindowPlacement.ReadRect(
            Section("INIVersion=2", "XRef=right", "YRef=center", "XPos=0%", "YPos=-16.736111%"),
            2560,
            1440,
            504,
            495);
        Assert.Equal(2056, version2.Left, 1);
        Assert.Equal(231.5, version2.Top, 1);

        var roundTrip = Section("INIVersion=1", "XRef=center", "YRef=center", "XPos=14.296874%", "YPos=-14.513889%", "Width=52", "Height=441");
        var rectAgain = WindowPlacement.ReadRect(roundTrip, 2560, 1440, 10, 10);
        WindowPlacement.WriteRect(roundTrip, rectAgain, 2560, 1440, writeSize: true);
        var written = WindowPlacement.ReadRect(roundTrip, 2560, 1440, 10, 10);
        Assert.Equal(rectAgain.Left, written.Left, 0.05);
        Assert.Equal(rectAgain.Top, written.Top, 0.05);

        var section = Section("XRef=left", "YRef=top", "XPos=0%", "YPos=0%", "Width=10", "Height=10");
        WindowPlacement.WriteRect(section, new ScreenRect(100, 50, 80, 40), 1000, 500, writeSize: true);
        Assert.Equal("10%", section.Get("XPos"));
        Assert.Equal("10%", section.Get("YPos"));
        Assert.Equal("80", section.Get("Width"));
        Assert.Equal("40", section.Get("Height"));

        Assert.Equal("20.625%", WindowPlacement.FormatAxisPercent("right", 20.625, 2560, 1));
        var onePixel = 100d / 2560d;
        var shortened = WindowPlacement.FormatAxisPercent("left", onePixel, 2560, 1);
        Assert.True(WindowPlacement.TryParsePercent(shortened, out var parsed));
        Assert.Equal(1, WindowPlacement.ToPixelsX("left", parsed, 0, 2560), 0.05);
        Assert.DoesNotContain("000000", shortened);

        var kept = Section("XRef=right", "YRef=center", "XPos=10%", "YPos=-25.486111%", "Width=52", "Height=273");
        var before = WindowPlacement.ReadRect(kept, 1920, 1080, 52, 273);
        kept.Set("XRef", "left");
        kept.Set("YRef", "top");
        WindowPlacement.WriteRect(kept, before, 1920, 1080, writeSize: true);
        var after = WindowPlacement.ReadRect(kept, 1920, 1080, 52, 273);
        Assert.Equal(before.Left, after.Left, 0.05);
        Assert.Equal(before.Top, after.Top, 0.05);
        Assert.Equal("left", kept.Get("XRef"));
        Assert.Equal("top", kept.Get("YRef"));
    }

    [Theory]
    [InlineData(2560)]
    [InlineData(1440)]
    [InlineData(1920)]
    [InlineData(1080)]
    [InlineData(1280)]
    [InlineData(720)]
    public void FormatPercent_IntegerPixelsSurviveClientTruncation(double reference)
    {
        for (var pixel = -reference; pixel <= reference; pixel++)
        {
            var text = WindowPlacement.FormatPercent(pixel / reference * 100d, reference);
            Assert.True(WindowPlacement.TryParsePercent(text, out var stored));
            var raw = (float)reference * (float)stored / 100f;
            Assert.Equal((int)pixel, (int)raw);
            Assert.Equal((int)pixel, (int)Math.Round(raw, MidpointRounding.AwayFromZero));
            Assert.InRange(Math.Abs((stored / 100d * reference) - pixel), 0, 0.05);
        }
    }

    [Theory]
    [InlineData("left", 1d, 52)]
    [InlineData("left", 1d, 94)]
    [InlineData("left", 1d, 136)]
    [InlineData("left", 1d, 178)]
    [InlineData("left", 1d, 220)]
    [InlineData("center", 0.5d, 94)]
    [InlineData("center", 0.5d, 136)]
    [InlineData("right", 1d, 94)]
    public void FormatPercent_StackedBagsStayFlushWhenTheClientTruncates(string anchor, double centerScale, double width)
    {
        const double screen = 2560;
        var painted = width - BagGrid.FrameInsetLeft - BagGrid.FrameInsetRight;
        var reference = string.Equals(anchor, "center", StringComparison.Ordinal) ? screen * centerScale : screen;
        var left = 640d;
        for (var index = 0; index < 8; index++)
        {
            var offset = anchor switch
            {
                "right" => screen - (left + width),
                "center" => left + (width / 2d) - (screen / 2d),
                _ => left,
            };
            var text = WindowPlacement.FormatAxisPercent(anchor, offset / reference * 100d, screen, centerScale);
            Assert.True(WindowPlacement.TryParsePercent(text, out var stored));
            var raw = (float)reference * (float)stored / 100f;
            var clientOffset = (int)raw;
            var clientLeft = anchor switch
            {
                "right" => screen - clientOffset - width,
                "center" => (screen / 2d) + clientOffset - Math.Truncate(width / 2d),
                _ => (double)clientOffset,
            };
            Assert.Equal(left, clientLeft);
            left += painted;
        }
    }

    [Fact]
    public void StackRight_CenterAnchoredBagsStayFlushAfterTheIniRoundTrip()
    {
        const double screen = 2560;
        const double height = 1440;
        var saved = new[]
        {
            new ScreenRect(1414.51, 410, 52, 441),
            new ScreenRect(1460.46, 410, 52, 441),
            new ScreenRect(1506.54, 410, 52, 441),
            new ScreenRect(1552.50, 410, 52, 441),
            new ScreenRect(1598.45, 410, 52, 441),
            new ScreenRect(1644.53, 410, 52, 441),
        };
        var painted = saved.Select(BagGrid.ToPainted).ToArray();
        var stacked = LayoutArranger.Arrange(painted, ArrangeAction.StackRight);
        var written = stacked.Select(BagGrid.FromPainted).ToArray();
        int? previous = null;
        foreach (var rect in written)
        {
            var section = Section("INIVersion=1", "XRef=center", "YRef=center", "Width=52", "Height=441");
            WindowPlacement.WriteRect(section, rect, screen, height, writeSize: true);
            var clientLeft = ClientLeft(section, screen);
            if (previous is int prior)
            {
                Assert.Equal(46, clientLeft - prior);
            }

            previous = clientLeft;
        }
    }

    private static int ClientLeft(IniSection section, double screen)
    {
        Assert.True(WindowPlacement.TryParsePercent(section.Get("XPos"), out var percent));
        var scale = WindowPlacement.CenterScale(section);
        var reference = (float)(string.Equals(section.Get("XRef"), "center", StringComparison.OrdinalIgnoreCase) ? screen * scale : screen);
        var stored = (float)percent;
        var raw = reference * stored / 100f;
        var width = int.Parse(section.Get("Width")!, CultureInfo.InvariantCulture);
        var offset = (int)raw;
        return section.Get("XRef") switch
        {
            "right" => (int)screen - offset - width,
            "center" => (int)(screen / 2d) + offset - (width / 2),
            _ => offset,
        };
    }

    [Fact]
    public void SkinCatalog_ReadsDefaultSizeAndSizableFlag()
    {
        var root = Directory.CreateTempSubdirectory();
        try
        {
            var skin = Path.Combine(root.FullName, "uifiles", "default_modern");
            Directory.CreateDirectory(skin);
            File.WriteAllText(
                Path.Combine(skin, "EQUI_BankWnd.xml"),
                """
                <?xml version="1.0" encoding="us-ascii"?>
                <XML>
                  <Screen item="BankWnd">
                    <Size><CX>257</CX><CY>227</CY></Size>
                    <Style_Sizable>false</Style_Sizable>
                  </Screen>
                </XML>
                """);

            var catalog = UiSkinCatalog.Load(root.FullName, "default_modern");
            var chrome = catalog.Find("BankWnd");

            Assert.NotNull(chrome);
            Assert.Equal(257, chrome.Width);
            Assert.Equal(227, chrome.Height);
            Assert.False(chrome.Sizable);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void HotbarAndBagNames_AreRecognized()
    {
        Assert.Equal("Range<BR>Attack", HotButtonText.ReadLabel("B1,@-1,0000000000000000,0,Range<BR>Attack,"));
        Assert.Equal(
            "White Chain Bridle",
            HotButtonText.ReadLabel("U-1,A596,pWy000q0002LRW00,21808,,White Chain Bridle"));
        Assert.True(HotButtonText.IsHotbarSection("HotButtons2"));
        Assert.False(HotButtonText.IsHotbarSection("HotButtonWnd"));
        Assert.Equal("HotButtonWnd", HotButtonText.WindowSection("HotButtons"));
        Assert.Equal("HotButtons", HotButtonText.ButtonsSection("HotButtonWnd"));
        Assert.Equal("HotButtonWnd2", HotButtonText.WindowSection("HotButtons2"));
        Assert.Equal("HotButtons4", HotButtonText.ButtonsSection("HotButtonWnd4"));
        Assert.Equal("HotButtonWnd10", HotButtonText.WindowSection("HotButtons10"));
        Assert.Null(HotButtonText.WindowSection("HotButtonWnd"));
        Assert.Null(HotButtonText.ButtonsSection("HotButtons"));
    }

    [Fact]
    public void SectionTitles_UseReadableNames()
    {
        Assert.Equal("Hot Button Bar 1", SectionTitles.Format("HotButtonWnd"));
        Assert.Equal("Hot Button Bar 2", SectionTitles.Format("HotButtonWnd2"));
        Assert.Equal("Hot Button Bar 10", SectionTitles.Format("HotButtonWnd10"));
        Assert.Equal("Hot Buttons 1", SectionTitles.Format("HotButtons"));
        Assert.Equal("Hot Buttons 11", SectionTitles.Format("HotButtons11"));
        Assert.Equal("Spell Loadouts", SectionTitles.Format("SpellLoadouts"));
        Assert.Equal("Macros", SectionTitles.Format("Socials"));
        Assert.Equal("Buffs", SectionTitles.Format("BuffWindow"));
        Assert.Equal("Buffs 1", SectionTitles.Format("BuffWindow_1"));
        Assert.Equal("Spell Display", SectionTitles.Format("SpellDisplayWindow_99"));
        Assert.Equal("Item Window 1", SectionTitles.Format("ItemDisplay0"));
        Assert.Equal("Item Window 3", SectionTitles.Format("ItemDisplay2"));
        Assert.Equal("Additional Filters", SectionTitles.Format("ADDITIONALFILTERS"));
        Assert.Equal("PvP Stats", SectionTitles.Format("PvPStatsWnd"));
        Assert.Equal("Main Menu", SectionTitles.Format("EQMainWnd"));
        Assert.Equal("Main Menu 2", SectionTitles.Format("EQMainWnd_2"));
        Assert.Equal("Mercenaries", SectionTitles.Format("MMGW_ManageWnd"));
        Assert.Equal("World Bag 3", SectionTitles.Format("BagWorld3"));
        Assert.Equal("Alert", SectionTitles.Format("AlertWnd"));
        Assert.Equal("Book Window", SectionTitles.Format("BookWindow"));
        Assert.Equal("External Target Roles", SectionTitles.Format("ExternalTargetRoles"));
        Assert.Equal("Large Spellgems", SectionTitles.Format("CastSpellWnd"));
        Assert.Equal("Small Spellgems", SectionTitles.Format("CastSpellWnd_1"));
        Assert.Equal("Small Spellgems, Spell Names", SectionTitles.Format("CastSpellWnd_2"));
        Assert.Equal("Small Spellgems, Spell Names and Numbering", SectionTitles.Format("CastSpellWnd_3"));
    }

    [Fact]
    public void ReadPage_ColorsSocialsAndKeepsOtherLabels()
    {
        var hotbar = IniDocument.Parse(
            "[HotButtons2]\r\n" +
            "Page1Button1=E19,@-1,0000000000000000,0,HC1,\r\n" +
            "Page1Button2=B1,@-1,0000000000000000,0,Range<BR>Attack,\r\n" +
            "Page1Button3=U-1,A596,pWy000q0002LRW00,21808,,White Chain Bridle\r\n" +
            "Page1Button4=E0,@-1,0000000000000000,0,,\r\n").Find("HotButtons2")!;
        var socials = IniDocument.Parse(
            "[Socials]\r\n" +
            "Page1Button1Name=Assist Main\r\n" +
            "Page1Button1Color=15\r\n" +
            "Page2Button8Name=HC1\r\n" +
            "Page2Button8Color=13\r\n").Find("Socials")!;

        var faces = HotButtonText.ReadPage(hotbar, socials, 1);

        Assert.Equal(HotButtonText.ButtonsPerBar, faces.Count);
        Assert.Equal("HC1", faces[0].Text);
        Assert.Equal("13", faces[0].Color);
        Assert.Equal("Range\nAttack", faces[1].Text);
        Assert.Equal("0", faces[1].Color);
        Assert.Equal("White Chain Bridle", faces[2].Text);
        Assert.Equal("0", faces[2].Color);
        Assert.Null(faces[0].Icon);
        Assert.Null(faces[1].Icon);
        Assert.Equal(596, faces[2].Icon);
        Assert.Equal("Assist Main", faces[3].Text);
        Assert.Equal("15", faces[3].Color);
        Assert.Equal(string.Empty, faces[4].Text);
        Assert.Equal(string.Empty, faces[4].Color);
    }

    [Fact]
    public void ReadPage_NamesASystemSocialTheFileDoesNotStore()
    {
        var hotbar = IniDocument.Parse(
            "[HotButtons]\r\n" +
            "Page1Button1=E1,@-1,0000000000000000,0,,\r\n").Find("HotButtons")!;

        var faces = HotButtonText.ReadPage(hotbar, socials: null, page: 1);

        Assert.Equal("Auto Fire", faces[0].Text);
        Assert.Equal("0", faces[0].Color);
    }

    [Fact]
    public void ReadPage_NamesUnlabeledAttacksSkillsAndMemorizedGems()
    {
        var hotbar = IniDocument.Parse(
            "[HotButtons]\r\n" +
            "Page1Button1=B0,@-1,0000000000000000,0,,\r\n" +
            "Page1Button2=B1,@-1,0000000000000000,0,,\r\n" +
            "Page1Button3=B1,@-1,0000000000000000,0,Range<BR>Attack,\r\n" +
            "Page1Button4=J30,@-1,0000000000000000,0,,\r\n" +
            "Page1Button5=J8,@-1,0000000000000000,0,Backstab,\r\n" +
            "Page1Button6=J111,@-1,0000000000000000,0,,\r\n" +
            "Page1Button7=H0,@-1,0000000000000000,0,,\r\n" +
            "Page1Button8=H1,@-1,0000000000000000,0,,\r\n" +
            "Page1Button9=H0,@-1,0000000000000000,0,Gem,\r\n" +
            "Page1Button10=J99,@-1,0000000000000000,0,,\r\n" +
            "Page1Button11=G4,@-1,0000000000000000,0,,\r\n" +
            "Page1Button12=G12,@-1,0000000000000000,0,,\r\n").Find("HotButtons")!;
        var gems = new HotbarGem[]
        {
            new("Spirit of Wolf", 12),
            new("Cannibalize", -1),
        };

        var faces = HotButtonText.ReadPage(hotbar, socials: null, page: 1, gems);

        Assert.Equal("Melee Attack", faces[0].Text);
        Assert.Equal("0", faces[0].Color);
        Assert.Null(faces[0].SpellIcon);
        Assert.Equal("Range Attack", faces[1].Text);
        Assert.Equal("Range\nAttack", faces[2].Text);
        Assert.Equal("Kick", faces[3].Text);
        Assert.Equal("Backstab", faces[4].Text);
        Assert.Equal("Slam", faces[5].Text);
        Assert.Equal("Spirit of Wolf", faces[6].Text);
        Assert.Equal(12, faces[6].SpellIcon);
        Assert.Equal("Spirit of Wolf", faces[6].SpellName);
        Assert.Equal("Cannibalize", faces[7].Text);
        Assert.Null(faces[7].SpellIcon);
        Assert.Equal("Cannibalize", faces[7].SpellName);
        Assert.Equal("Gem", faces[8].Text);
        Assert.Equal(12, faces[8].SpellIcon);
        Assert.Equal("Spirit of Wolf", faces[8].SpellName);
        Assert.Equal(string.Empty, faces[9].Text);
        Assert.Equal(string.Empty, faces[10].Text);
        Assert.Equal("Find", faces[11].Text);
    }

    [Fact]
    public void ReadPage_ShowsSpellIconsForGemsAndCombatAbilities()
    {
        var hotbar = IniDocument.Parse(
            "[HotButtons]\r\n" +
            "Page1Button1=P74030,@-1,0000000000000000,0,Stunning Venom,\r\n" +
            "Page1Button2=P74061,@-1,0000000000000000,0,,\r\n" +
            "Page1Button3=P1,@-1,0000000000000000,0,Missing,\r\n").Find("HotButtons")!;
        HotbarGem? Lookup(int id) => id switch
        {
            74030 => new HotbarGem("Stunning Venom", 40),
            74061 => new HotbarGem("Neurotoxic Poison", 41),
            _ => null,
        };

        var faces = HotButtonText.ReadPage(hotbar, socials: null, page: 1, spells: Lookup);

        Assert.Equal("Stunning Venom", faces[0].Text);
        Assert.Equal(40, faces[0].SpellIcon);
        Assert.Equal("Stunning Venom", faces[0].SpellName);
        Assert.Equal("Neurotoxic Poison", faces[1].Text);
        Assert.Equal(41, faces[1].SpellIcon);
        Assert.Equal("Neurotoxic Poison", faces[1].SpellName);
        Assert.Equal("Missing", faces[2].Text);
        Assert.Null(faces[2].SpellIcon);
        Assert.Equal(string.Empty, faces[2].SpellName);
    }

    [Fact]
    public void ReadPage_NamesAlternateAbilitiesFromTheStringTable()
    {
        var abilities = AbilityCatalog.Parse(
        [
            "146^1^Cannibalization^0^",
            "146^2^Canni^0^",
            "146^3^V^0^",
            "14^1^Widened Perception^0^",
            "18^2^Point Blank Fire^0^",
            "18^4^This toggleable ability is not a button line.^0^",
        ]);
        var hotbar = IniDocument.Parse(
            "[HotButtons]\r\n" +
            "Page1Button1=E266,@-1,0000000000000000,0,,\r\n" +
            "Page1Button2=E134,@-1,0000000000000000,0,,\r\n" +
            "Page1Button3=E18,@-1,0000000000000000,0,S&H,\r\n" +
            "Page1Button4=E0,@-1,0000000000000000,0,,\r\n").Find("HotButtons")!;
        var socials = IniDocument.Parse(
            "[Socials]\r\n" +
            "Page1Button1Name=Assist Main\r\n" +
            "Page1Button1Color=15\r\n").Find("Socials")!;

        Assert.Equal("Canni\nV", abilities.ButtonName(266));
        Assert.Equal("Point Blank Fire", abilities.ButtonName(134));
        Assert.Equal("Point Blank Fire", abilities.ButtonName(18));

        var faces = HotButtonText.ReadPage(hotbar, socials, 1, abilities: abilities.ButtonName);
        Assert.Equal("Canni\nV", faces[0].Text);
        Assert.Equal("0", faces[0].Color);
        Assert.Equal("Point Blank Fire", faces[1].Text);
        Assert.Equal("S&H", faces[2].Text);
        Assert.Equal("Assist Main", faces[3].Text);
        Assert.Equal("15", faces[3].Color);
    }

    [Fact]
    public void ButtonName_SubtractsSocialPagesBeforeTheStringId()
    {
        var abilities = AbilityCatalog.Parse(
        [
            "68^1^Innate Metabolism^0^",
            "68^4^This passive ability reduces your food and drink consumption by 110%.^0^",
            "183^1^Life Burn^0^",
            "188^1^Divine Stun^0^",
            "188^2^Divine^0^",
            "188^3^Stun^0^",
            "98^1^Spell Casting Subtlety^0^",
            "23^1^Unbound Boon^0^",
            "23^4^This passive ability grants the Clerics healing spells a #1% chance to score an exceptional heal.^0^",
            "25^4^This passive ability grants your healing spells a #1% chance to score an exceptional heal.^0^",
            "145^1^Dire Charm^0^",
            "145^2^Dire^0^",
            "145^3^Charm^0^",
            "194^1^Holy Steed^0^",
            "194^4^This ability, when activated, summons the bridle of a very fast Holy Steed.^0^",
            "195^4^This passive ability grants you immunity to most fear spells.^0^",
        ]);
        var hotbar = IniDocument.Parse(
            "[HotButtons2]\r\n" +
            "Page1Button10=E188,@-1,0000000000000000,0,,\r\n" +
            "Page1Button11=E145,@-1,0000000000000000,0,,\r\n").Find("HotButtons2")!;

        Assert.Equal("Life Burn", abilities.ButtonName(188));
        Assert.Equal("Spell Casting Subtlety", abilities.ButtonName(145));
        Assert.Null(abilities.ButtonName(315));

        var faces = HotButtonText.ReadPage(hotbar, socials: null, page: 1, abilities: abilities.ButtonName);
        Assert.Equal("Life Burn", faces[9].Text);
        Assert.Equal("Spell Casting Subtlety", faces[10].Text);
    }

    [Fact]
    public void ButtonName_ReadsOriginFromItsStringId()
    {
        var abilities = AbilityCatalog.Parse(
        [
            "331^1^Fury of Magic^0^",
            "331^4^This passive ability increases your chance to land a critical hit with your direct damage spells by 2%.^0^",
            "1000^1^Origin^0^",
            "1000^2^Origin^0^",
            "1000^4^This ability, when activated, transports you back to your starting city.^0^",
        ]);
        var hotbar = IniDocument.Parse(
            "[HotButtons5]\r\n" +
            "Page1Button9=E451,@-1,0000000000000000,0,,\r\n").Find("HotButtons5")!;

        Assert.Equal("Origin", abilities.ButtonName(451));

        var faces = HotButtonText.ReadPage(hotbar, socials: null, page: 1, abilities: abilities.ButtonName);
        Assert.Equal("Origin", faces[8].Text);
    }

    [Fact]
    public void ButtonName_ReadsTrickstersMisdirectionFromItsStringId()
    {
        var abilities = AbilityCatalog.Parse(
        [
            "399^1^Tricksters Misdirection^0^",
            "399^2^Tricksters Misdirection^0^",
        ]);
        var hotbar = IniDocument.Parse(
            "[HotButtons]\r\n" +
            "Page1Button11=E2373,@-1,0000000000000000,0,,\r\n").Find("HotButtons")!;

        Assert.Equal("Tricksters Misdirection", abilities.ButtonName(2373));

        var faces = HotButtonText.ReadPage(hotbar, socials: null, page: 1, abilities: abilities.ButtonName);
        Assert.Equal("Tricksters Misdirection", faces[10].Text);
    }

    [Fact]
    public void ChatWindows_UseNamesAndFiltersFromChatManager()
    {
        var document = IniDocument.Parse("""
            [ChatManager]
            ChatWindow0_Name=Main Chat
            ChatWindow0_ContainerIndex=0
            ChatWindow1_Name=Combat
            ChatWindow1_ContainerIndex=2
            ChannelMap0=0
            ChannelMap9=1
            ChannelMap4=0
            """);

        var windows = ChatLayout.Read(document.Find("ChatManager"));
        Assert.Equal("Main Chat", ChatLayout.Caption(windows, "MainChat"));
        Assert.Equal("Combat", ChatLayout.Caption(windows, "Chat 2"));
        Assert.Equal("", ChatLayout.Caption(windows, "Chat 1"));
        Assert.True(ChatLayout.HasTabs(windows, "MainChat"));
        Assert.True(ChatLayout.HasTabs(windows, "Chat 2"));
        Assert.False(ChatLayout.HasTabs(windows, "Chat 1"));
        Assert.True(ChatLayout.HasTabs(windows, "BagInv1"));

        var main = Assert.Single(windows, window => window.Name == "Main Chat");
        Assert.Equal(["Say", "Guild"], main.Filters);
        Assert.Equal(0, main.TabIndex);
        Assert.Equal("Main Chat", Assert.Single(ChatLayout.Tabs(windows, "MainChat")).Name);
        var combat = Assert.Single(windows, window => window.Name == "Combat");
        Assert.Equal(["Your Hits"], combat.Filters);
    }

    [Fact]
    public void ChatWindows_OrderTabsByContainerTabIndex()
    {
        var document = IniDocument.Parse("""
            [ChatManager]
            ChatWindow0_Name=Main Chat
            ChatWindow0_ContainerIndex=0
            ChatWindow0_ContainerTabIndex=0
            ChatWindow1_Name=Junk
            ChatWindow1_ContainerIndex=0
            ChatWindow1_ContainerTabIndex=1
            ChatWindow2_Name=Crits
            ChatWindow2_ContainerIndex=2
            ChatWindow2_ContainerTabIndex=1
            ChatWindow5_Name=Combat
            ChatWindow5_ContainerIndex=2
            ChatWindow5_ContainerTabIndex=0
            ChatWindow6_Name=Group
            ChatWindow6_ContainerIndex=1
            ChannelMap0=0
            ChannelMap1=1
            ChannelMap2=2
            ChannelMap9=5
            ChannelMap4=6
            """);

        var windows = ChatLayout.Read(document.Find("ChatManager"));
        var main = ChatLayout.Tabs(windows, "MainChat");
        Assert.Equal(["Main Chat", "Junk"], main.Select(tab => tab.Name).ToArray());
        Assert.Equal(0, main[0].TabIndex);
        Assert.Equal(1, main[1].TabIndex);

        var combat = ChatLayout.Tabs(windows, "Chat 2");
        Assert.Equal(["Combat", "Crits"], combat.Select(tab => tab.Name).ToArray());
        Assert.Equal(0, combat[0].TabIndex);

        var group = Assert.Single(ChatLayout.Tabs(windows, "Chat 1"));
        Assert.Equal("Group", group.Name);
        Assert.Equal(0, group.TabIndex);

        Assert.Empty(ChatLayout.Tabs(windows, "Chat 3"));
        Assert.False(ChatLayout.HasTabs(windows, "Chat 3"));
        Assert.Equal("Main Chat, Junk", ChatLayout.Caption(windows, "MainChat"));
        Assert.Equal("Crits, Combat", ChatLayout.Caption(windows, "Chat 2"));
        Assert.Equal("Group", ChatLayout.Caption(windows, "Chat 1"));
    }

    [Fact]
    public void ChatWindows_HideTabsThatHaveNoChannelMap()
    {
        var document = IniDocument.Parse("""
            [ChatManager]
            ChatWindow4_Name=Chat 4
            ChatWindow4_ContainerIndex=4
            ChatWindow4_ContainerTabIndex=0
            ChatWindow5_Name=Combat
            ChatWindow5_ContainerIndex=2
            ChatWindow5_ContainerTabIndex=0
            ChatWindow6_Name=Unused
            ChatWindow6_ContainerIndex=2
            ChatWindow6_ContainerTabIndex=1
            ChannelMap9=5
            [Chat 4]
            XPos=1%
            [Chat 2]
            XPos=1%
            [BuffWindow_5]
            XPos=1%
            Show=1
            """);

        var windows = ChatLayout.Read(document.Find("ChatManager"));
        Assert.Contains(windows, window => window.Name == "Chat 4" && window.Filters.Count == 0);
        Assert.False(ChatLayout.HasTabs(windows, "Chat 4"));
        Assert.Empty(ChatLayout.Tabs(windows, "Chat 4"));
        Assert.Equal(string.Empty, ChatLayout.Caption(windows, "Chat 4"));

        var combat = Assert.Single(ChatLayout.Tabs(windows, "Chat 2"));
        Assert.Equal("Combat", combat.Name);
        Assert.True(ChatLayout.HasTabs(windows, "Chat 2"));
        Assert.Equal("Combat", ChatLayout.Caption(windows, "Chat 2"));
        Assert.True(ChatLayout.HasTabs(windows, "BuffWindow_5"));

        var layouts = ActiveLayout.Read(document, "default_modern");
        var chat4 = document.Find("Chat 4")!;
        var chat2 = document.Find("Chat 2")!;
        Assert.Null(chat4.Get("Show"));
        Assert.False(ActiveLayout.AppearsOnLayout(chat4, layouts, windowCategory: true, ChatLayout.HasTabs(windows, "Chat 4")));
        Assert.True(ActiveLayout.AppearsOnLayout(chat2, layouts, windowCategory: true, ChatLayout.HasTabs(windows, "Chat 2")));
        Assert.True(ActiveLayout.AppearsOnLayout(document.Find("BuffWindow_5")!, layouts, windowCategory: true, ChatLayout.HasTabs(windows, "BuffWindow_5")));
    }

    [Fact]
    public void CopyFilters_ReplacesChannelMapsAndKeepsOtherChatManagerKeys()
    {
        var source = IniDocument.Parse("""
            [ChatManager]
            NumWindows=2
            NumContainers=3
            ChatWindow0_Name=Main Chat
            ChatWindow0_ContainerIndex=0
            ChatWindow0_ContainerTabIndex=0
            ChatWindow0_ContainerName=Guild Chat
            ChatWindow3_Name=Crits
            ChatWindow3_ContainerIndex=2
            ChatWindow3_ContainerTabIndex=0
            ChatWindow3_ContainerName=Crits
            ChannelMap0=0
            ChannelMap9=3
            """).Find("ChatManager")!;
        var destination = IniDocument.Parse("""
            [ChatManager]
            NumWindows=5
            NumContainers=4
            ChatWindow0_Name=Old
            ChatWindow0_ContainerIndex=1
            ChatWindow1_Name=Combat
            ChatWindow1_ContainerIndex=2
            ChatWindow1_ContainerName=Junk
            ChannelMap0=1
            ChannelMap4=1
            FontSize=3
            """).Find("ChatManager")!;

        Assert.True(ChatLayout.CopyFilters(source, destination));
        Assert.False(ChatLayout.CopyFilters(source, destination));

        Assert.Equal("2", destination.Get("NumWindows"));
        Assert.Equal("3", destination.Get("NumContainers"));
        Assert.Equal("Main Chat", destination.Get("ChatWindow0_Name"));
        Assert.Equal("0", destination.Get("ChatWindow0_ContainerIndex"));
        Assert.Equal("0", destination.Get("ChatWindow0_ContainerTabIndex"));
        Assert.Equal("Guild Chat", destination.Get("ChatWindow0_ContainerName"));
        Assert.Equal("Crits", destination.Get("ChatWindow3_Name"));
        Assert.Equal("2", destination.Get("ChatWindow3_ContainerIndex"));
        Assert.Equal("Crits", destination.Get("ChatWindow3_ContainerName"));
        Assert.Equal("0", destination.Get("ChannelMap0"));
        Assert.Equal("3", destination.Get("ChannelMap9"));
        Assert.Null(destination.Get("ChatWindow1_Name"));
        Assert.Null(destination.Get("ChatWindow1_ContainerIndex"));
        Assert.Null(destination.Get("ChatWindow1_ContainerName"));
        Assert.Null(destination.Get("ChannelMap4"));
        Assert.Equal("3", destination.Get("FontSize"));
        Assert.True(ChatLayout.IsChatWindow("MainChat"));
        Assert.True(ChatLayout.IsChatWindow("Chat 2"));
        Assert.False(ChatLayout.IsChatWindow("ChatManager"));
        Assert.False(ChatLayout.IsFilterKey("FontSize"));
    }

    [Fact]
    public void ClientResolution_ReadsTheActiveVideoMode()
    {
        var root = Directory.CreateTempSubdirectory();
        try
        {
            File.WriteAllText(
                Path.Combine(root.FullName, "eqclient.ini"),
                "[VideoMode]\r\nWidth=2560\r\nHeight=1440\r\nWindowedWidth=1920\r\nWindowedHeight=1080\r\nFullscreen=1\r\n");

            Assert.True(ClientResolution.TryRead(root.FullName, out var width, out var height));
            Assert.Equal(2560, width);
            Assert.Equal(1440, height);

            File.WriteAllText(
                Path.Combine(root.FullName, "eqclient.ini"),
                "[VideoMode]\r\nWidth=2560\r\nHeight=1440\r\nWindowedWidth=1920\r\nWindowedHeight=1080\r\nFullscreen=0\r\n");
            Assert.True(ClientResolution.TryRead(root.FullName, out width, out height));
            Assert.Equal(1920, width);
            Assert.Equal(1080, height);
            Assert.False(ClientResolution.TryRead(Path.Combine(root.FullName, "missing"), out _, out _));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void ActiveLayout_UsesTheSkinChoiceOverTheDefault()
    {
        var document = IniDocument.Parse(
            "[Default]\r\nBuffWindow=0\r\nInventoryWindow=1\r\n" +
            "[default_modern]\r\nBuffWindow=5\r\n");
        var selected = ActiveLayout.Read(document, "default_modern");

        Assert.True(ActiveLayout.IsActive("BuffWindow_5", selected));
        Assert.False(ActiveLayout.IsActive("BuffWindow", selected));
        Assert.False(ActiveLayout.IsActive("BuffWindow_4", selected));
        Assert.False(ActiveLayout.IsActive("InventoryWindow", selected));
        Assert.True(ActiveLayout.IsActive("InventoryWindow_1", selected));
        Assert.True(ActiveLayout.IsActive("SpellDisplayWindow_99", selected));

        var hidden = IniDocument.Parse("[BuffWindow_5]\r\nXPos=1%\r\nShow=0\r\n").Find("BuffWindow_5")!;
        var shown = IniDocument.Parse("[BuffWindow_5]\r\nXPos=1%\r\nShow=1\r\n").Find("BuffWindow_5")!;
        var unused = IniDocument.Parse("[BuffWindow]\r\nXPos=1%\r\n").Find("BuffWindow")!;
        Assert.False(ActiveLayout.IsDisplayed(hidden, selected));
        Assert.True(ActiveLayout.IsDisplayed(shown, selected));
        Assert.False(ActiveLayout.IsDisplayed(unused, selected));
        Assert.True(ActiveLayout.IsActive("BagInv1", selected));
        Assert.True(ActiveLayout.IsActive("HotButtonWnd2", selected));
    }

    [Fact]
    public void ActiveLayout_AppearsOnLayoutMatchesWhatTheCanvasDraws()
    {
        var selected = ActiveLayout.Read(
            IniDocument.Parse("[Default]\r\nBuffWindow=0\r\n[default_modern]\r\nBuffWindow=5\r\n"),
            "default_modern");
        var shown = IniDocument.Parse("[BuffWindow_5]\r\nXPos=1%\r\nShow=1\r\n").Find("BuffWindow_5")!;
        var hidden = IniDocument.Parse("[BuffWindow_5]\r\nXPos=1%\r\nShow=0\r\n").Find("BuffWindow_5")!;
        var inactive = IniDocument.Parse("[BuffWindow]\r\nXPos=1%\r\nShow=1\r\n").Find("BuffWindow")!;
        var unplaced = IniDocument.Parse("[SpellBookWnd]\r\nShow=1\r\n").Find("SpellBookWnd")!;
        var bag = IniDocument.Parse("[BagInv1]\r\nShow=1\r\n").Find("BagInv1")!;
        var chat = IniDocument.Parse("[Chat 1]\r\nXPos=1%\r\nShow=1\r\n").Find("Chat 1")!;

        Assert.True(ActiveLayout.AppearsOnLayout(shown, selected, windowCategory: true, hasTabs: true));
        Assert.False(ActiveLayout.AppearsOnLayout(hidden, selected, windowCategory: true, hasTabs: true));
        Assert.False(ActiveLayout.AppearsOnLayout(inactive, selected, windowCategory: true, hasTabs: true));
        Assert.False(ActiveLayout.AppearsOnLayout(unplaced, selected, windowCategory: true, hasTabs: true));
        Assert.True(ActiveLayout.AppearsOnLayout(bag, selected, windowCategory: false, hasTabs: true));
        Assert.False(ActiveLayout.AppearsOnLayout(chat, selected, windowCategory: true, hasTabs: false));
        Assert.True(ChatLayout.HasTabs(Array.Empty<NamedChatWindow>(), "BuffWindow_5"));
        Assert.False(ChatLayout.HasTabs(Array.Empty<NamedChatWindow>(), "Chat 1"));
    }

    private static void Apply(SectionApplyPlan plan)
    {
        var result = GameFileSaver.Save(
            Path.GetDirectoryName(plan.Writes[0].Path)!,
            plan.Writes,
            new DateTimeOffset(2026, 10, 17, 9, 0, 0, TimeSpan.FromHours(-7)));
        Assert.True(result.Success);
    }

    private static DiscoveredLoadout WriteLoadout(
        string root,
        string character,
        string server,
        int number,
        string socialLine,
        string otherLine)
    {
        var characterPath = Path.Combine(root, $"{character}_{server}_LO{number.ToString(CultureInfo.InvariantCulture)}.ini");
        var uiPath = Path.Combine(root, $"UI_{character}_{server}_LO{number.ToString(CultureInfo.InvariantCulture)}.ini");
        File.WriteAllText(characterPath, $"[Socials]\r\n{socialLine}\r\n[Other]\r\n{otherLine}\r\n");
        File.WriteAllText(uiPath, "[BagInv1]\r\nWidth=50\r\nHeight=356\r\n");
        return new DiscoveredLoadout(
            new LoadoutId(character, server, number),
            characterPath,
            uiPath);
    }

    private static IniSection Section(params string[] lines)
    {
        var text = "[Window]\r\n" + string.Join("\r\n", lines) + "\r\n";
        return IniDocument.Parse(text).Find("Window")!;
    }
}
