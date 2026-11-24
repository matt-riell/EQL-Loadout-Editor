using System.Globalization;
using System.Text;
using EqlLoadouts.Core.Ini;
using EqlLoadouts.Core.Loadouts;

namespace EqlLoadouts.Core.Tests;

public class LoadoutBackupRestoreTests
{
    private static readonly Encoding Utf8 = new UTF8Encoding(false);

    private static readonly string[] WindowSections =
    [
        "BagInv1",
        "BagBank1",
        "ChatWindow",
        "HotButtonWnd",
        "CastSpellWnd",
        "PlayerWindow",
    ];

    [Fact]
    public void Save_ChangesEachWindowAndTheCharacterFile_ThenRestoreReturnsTheOriginalBytes()
    {
        using var game = new GameFolder();
        var characterPath = game.Write("Zenator_rivervale_LO1.ini", CharacterIni("Hail"));
        var uiPath = game.Write("UI_Zenator_rivervale_LO1.ini", UiIni(xpos: "10"));
        var otherLoadout = game.Write("Zenator_rivervale_LO2.ini", CharacterIni("Wave"));
        var otherServer = game.Write("Zenator_freeport_LO1.ini", CharacterIni("Port"));
        var characterBytes = File.ReadAllBytes(characterPath);
        var uiBytes = File.ReadAllBytes(uiPath);
        var otherLoadoutBytes = File.ReadAllBytes(otherLoadout);
        var otherServerBytes = File.ReadAllBytes(otherServer);

        var character = File.ReadAllText(characterPath);
        var ui = File.ReadAllText(uiPath);
        var characterEdit = IniDocument.Parse(character);
        characterEdit.Find("Socials")!.Set("Page1Button1Name", "Hello");
        characterEdit.Find("SpellLoadouts")!.Set("SpellLoadout1.name", "Raid");
        var uiEdit = IniDocument.Parse(ui);
        foreach (var section in WindowSections)
        {
            uiEdit.Find(section)!.Set("XPos", "110");
        }

        var characterChanges = SectionSave.ChangedSections(IniDocument.Parse(character), characterEdit);
        var uiChanges = SectionSave.ChangedSections(IniDocument.Parse(ui), uiEdit);
        Assert.Equal(["Socials", "SpellLoadouts"], characterChanges.Select(change => change.Name).ToArray());
        Assert.Equal(WindowSections, uiChanges.Select(change => change.Name).ToArray());

        var savedCharacter = SectionSave.Apply(character, characterChanges.Select(SectionWrite.Replace).ToList());
        var savedUi = SectionSave.Apply(ui, uiChanges.Select(SectionWrite.Replace).ToList());
        var timestamp = Stamp(9, 15);
        var saved = GameFileSaver.Save(
            game.Path,
            [
                new PendingFileWrite(characterPath, savedCharacter, Utf8),
                new PendingFileWrite(uiPath, savedUi, Utf8),
            ],
            timestamp,
            keepModifiedCopies: true);

        Assert.True(saved.Success, saved.Message);
        Assert.Equal(savedCharacter, File.ReadAllText(characterPath));
        Assert.Equal(savedUi, File.ReadAllText(uiPath));
        AssertOnly(
            character,
            File.ReadAllText(characterPath),
            ("Socials", "Page1Button1Name", "Hello"),
            ("SpellLoadouts", "SpellLoadout1.name", "Raid"));
        AssertOnly(
            ui,
            File.ReadAllText(uiPath),
            WindowSections.Select(section => (section, "XPos", "110")).ToArray());
        Assert.Equal(characterBytes, File.ReadAllBytes(game.Backup("MODIFIED", timestamp, 1, "Character.ini")));
        Assert.Equal(uiBytes, File.ReadAllBytes(game.Backup("MODIFIED", timestamp, 1, "UI.ini")));
        Assert.Equal(savedCharacter, File.ReadAllText(game.Backup("MODIFIED", timestamp, 1, "Character.ini", modifiedCopy: true)));
        Assert.Equal(savedUi, File.ReadAllText(game.Backup("MODIFIED", timestamp, 1, "UI.ini", modifiedCopy: true)));
        Assert.Equal(otherLoadoutBytes, File.ReadAllBytes(otherLoadout));
        Assert.Equal(otherServerBytes, File.ReadAllBytes(otherServer));

        var modifiedCopy = game.Backup("MODIFIED", timestamp, 1, "Character.ini", modifiedCopy: true);
        var rejected = GameFileSaver.Restore(game.Path, [modifiedCopy]);
        Assert.False(rejected.Success);
        Assert.Equal(savedCharacter, File.ReadAllText(characterPath));

        var snapshot = GameFileSaver.List(game.Path).Single(item => item.Kind == "MODIFIED");
        var loadout = Assert.Single(snapshot.Loadouts);
        Assert.Equal("Zenator_rivervale_LO1.ini", loadout.CharacterGameFile);
        Assert.Equal("UI_Zenator_rivervale_LO1.ini", loadout.UiGameFile);
        var restored = GameFileSaver.Restore(game.Path, [loadout.CharacterSource!, loadout.UiSource!]);

        Assert.True(restored.Success, restored.Message);
        Assert.Equal(characterBytes, File.ReadAllBytes(characterPath));
        Assert.Equal(uiBytes, File.ReadAllBytes(uiPath));
        Assert.Equal(otherLoadoutBytes, File.ReadAllBytes(otherLoadout));
        Assert.Equal(otherServerBytes, File.ReadAllBytes(otherServer));
    }

    [Fact]
    public void Save_MergeKeepsUneditedCharacterKeys_AndRestoreReturnsTheOriginal()
    {
        using var game = new GameFolder();
        var characterPath = game.Write("Zenator_rivervale_LO1.ini", CharacterIni("Hail"));
        var original = File.ReadAllBytes(characterPath);
        var text = File.ReadAllText(characterPath);
        var edited = IniDocument.Parse(text);
        edited.Find("Socials")!.Remove("Page2Button1Name");
        edited.Find("Socials")!.Set("Page1Button1Name", "Hello");
        edited.Find("Options")!.Set("ShowGrid", "2");

        var writes = new List<SectionWrite>();
        foreach (var change in SectionSave.ChangedSections(IniDocument.Parse(text), edited))
        {
            writes.Add(SectionSave.IsCharacterContent(change.Name)
                ? SectionWrite.Merge(change)
                : SectionWrite.Replace(change));
        }

        var savedText = SectionSave.Apply(text, writes);
        var timestamp = Stamp(9, 16);
        var saved = GameFileSaver.Save(
            game.Path,
            [new PendingFileWrite(characterPath, savedText, Utf8)],
            timestamp);

        Assert.True(saved.Success, saved.Message);
        var written = IniDocument.Parse(File.ReadAllText(characterPath));
        Assert.Equal("Hello", written.Find("Socials")!.Get("Page1Button1Name"));
        Assert.Equal("Sit", written.Find("Socials")!.Get("Page2Button1Name"));
        Assert.Equal("/hail", written.Find("Socials")!.Get("Page1Button1Line1"));
        Assert.Equal("2", written.Find("Options")!.Get("ShowGrid"));
        Assert.Equal("Attack", written.Find("SpellLoadouts")!.Get("SpellLoadout1.name"));
        Assert.Equal(original, File.ReadAllBytes(game.Backup("MODIFIED", timestamp, 1, "Character.ini")));

        var source = GameFileSaver.List(game.Path).Single().Loadouts[0].CharacterSource!;
        var restored = GameFileSaver.Restore(game.Path, [source]);
        Assert.True(restored.Success, restored.Message);
        Assert.Equal(original, File.ReadAllBytes(characterPath));
    }

    [Fact]
    public void Save_WindowsOnlyDoesNotChangeTheCharacterFile()
    {
        using var game = new GameFolder();
        var characterPath = game.Write("Zenator_rivervale_LO1.ini", CharacterIni("Hail"));
        var uiPath = game.Write("UI_Zenator_rivervale_LO1.ini", UiIni(xpos: "10"));
        var characterBytes = File.ReadAllBytes(characterPath);
        var uiBytes = File.ReadAllBytes(uiPath);
        var ui = File.ReadAllText(uiPath);
        var edited = IniDocument.Parse(ui);
        edited.Find("CastSpellWnd")!.Set("XPos", "110");
        var changes = SectionSave.ChangedSections(IniDocument.Parse(ui), edited);
        Assert.Equal(["CastSpellWnd"], changes.Select(change => change.Name).ToArray());

        var saved = GameFileSaver.Save(
            game.Path,
            [new PendingFileWrite(uiPath, SectionSave.Apply(ui, changes.Select(SectionWrite.Replace).ToList()), Utf8)],
            Stamp(9, 17));

        Assert.True(saved.Success, saved.Message);
        Assert.Equal(characterBytes, File.ReadAllBytes(characterPath));
        AssertOnly(ui, File.ReadAllText(uiPath), ("CastSpellWnd", "XPos", "110"));

        var source = GameFileSaver.List(game.Path).Single().Loadouts[0].UiSource!;
        Assert.True(GameFileSaver.Restore(game.Path, [source]).Success);
        Assert.Equal(characterBytes, File.ReadAllBytes(characterPath));
        Assert.Equal(uiBytes, File.ReadAllBytes(uiPath));
    }

    [Fact]
    public void Save_CharacterOnlyDoesNotChangeTheUiFile()
    {
        using var game = new GameFolder();
        var characterPath = game.Write("Zenator_rivervale_LO1.ini", CharacterIni("Hail"));
        var uiPath = game.Write("UI_Zenator_rivervale_LO1.ini", UiIni(xpos: "10"));
        var characterBytes = File.ReadAllBytes(characterPath);
        var uiBytes = File.ReadAllBytes(uiPath);
        var character = File.ReadAllText(characterPath);
        var edited = IniDocument.Parse(character);
        edited.Find("SpellLoadouts")!.Set("SpellLoadout2.name", "Travel");
        var changes = SectionSave.ChangedSections(IniDocument.Parse(character), edited);

        var saved = GameFileSaver.Save(
            game.Path,
            [new PendingFileWrite(characterPath, SectionSave.Apply(character, changes.Select(SectionWrite.Replace).ToList()), Utf8)],
            Stamp(9, 18));

        Assert.True(saved.Success, saved.Message);
        Assert.False(GameFileSaver.HasFullBackup(game.Path));
        Assert.Equal(uiBytes, File.ReadAllBytes(uiPath));
        AssertOnly(character, File.ReadAllText(characterPath), ("SpellLoadouts", "SpellLoadout2.name", "Travel"));

        var source = GameFileSaver.List(game.Path).Single().Loadouts[0].CharacterSource!;
        Assert.True(GameFileSaver.Restore(game.Path, [source]).Success);
        Assert.Equal(characterBytes, File.ReadAllBytes(characterPath));
        Assert.Equal(uiBytes, File.ReadAllBytes(uiPath));
    }

    [Fact]
    public void FullBackup_CopiesEveryLoadoutAndRestoreCanReturnOneLoadout()
    {
        using var game = new GameFolder();
        var firstCharacter = game.Write("Zenator_rivervale_LO1.ini", CharacterIni("Hail"));
        var firstUi = game.Write("UI_Zenator_rivervale_LO1.ini", UiIni(xpos: "10"));
        var secondCharacter = game.Write("Zenator_rivervale_LO2.ini", CharacterIni("Wave"));
        var secondUi = game.Write("UI_Zenator_rivervale_LO2.ini", UiIni(xpos: "30"));
        var otherServer = game.Write("Zenator_freeport_LO1.ini", CharacterIni("Port"));
        var originals = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
        {
            [firstCharacter] = File.ReadAllBytes(firstCharacter),
            [firstUi] = File.ReadAllBytes(firstUi),
            [secondCharacter] = File.ReadAllBytes(secondCharacter),
            [secondUi] = File.ReadAllBytes(secondUi),
            [otherServer] = File.ReadAllBytes(otherServer),
        };

        Assert.False(GameFileSaver.HasFullBackup(game.Path));
        var timestamp = Stamp(9, 19);
        var copied = GameFileSaver.WriteCopies(
            game.Path,
            [
                Copy("Zenator", "rivervale", 1, LoadoutFileKind.Character, originals[firstCharacter]),
                Copy("Zenator", "rivervale", 1, LoadoutFileKind.Ui, originals[firstUi]),
                Copy("Zenator", "rivervale", 2, LoadoutFileKind.Character, originals[secondCharacter]),
                Copy("Zenator", "rivervale", 2, LoadoutFileKind.Ui, originals[secondUi]),
            ],
            timestamp);

        Assert.True(copied.Success, copied.Message);
        Assert.True(GameFileSaver.HasFullBackup(game.Path));
        Assert.True(GameFileSaver.TryGetLatestFullBackup(game.Path, out var latest));
        Assert.Equal(StampName(timestamp), latest.LocalDateTime.ToString(GameFileSaver.BackupStampFormat, CultureInfo.InvariantCulture));
        Assert.Equal(originals[firstCharacter], File.ReadAllBytes(game.Backup("FULL", timestamp, 1, "Character.ini")));
        Assert.Equal(originals[firstUi], File.ReadAllBytes(game.Backup("FULL", timestamp, 1, "UI.ini")));
        Assert.Equal(originals[secondCharacter], File.ReadAllBytes(game.Backup("FULL", timestamp, 2, "Character.ini")));
        Assert.Equal(originals[secondUi], File.ReadAllBytes(game.Backup("FULL", timestamp, 2, "UI.ini")));
        Assert.False(Directory.Exists(Path.Combine(game.Path, "LoadoutBackups", "FULL", StampName(timestamp), "Freeport - Zenator")));
        foreach (var (path, bytes) in originals)
        {
            Assert.Equal(bytes, File.ReadAllBytes(path));
        }

        foreach (var path in originals.Keys)
        {
            File.WriteAllText(path, "mutated\r\n", Utf8);
        }

        var loadouts = GameFileSaver.List(game.Path).Single().Loadouts;
        Assert.Equal(2, loadouts.Count);
        var first = loadouts.Single(item => item.Label.EndsWith("LO01", StringComparison.Ordinal));
        var second = loadouts.Single(item => item.Label.EndsWith("LO02", StringComparison.Ordinal));
        var oneFile = GameFileSaver.Restore(game.Path, [first.CharacterSource!]);

        Assert.True(oneFile.Success, oneFile.Message);
        Assert.Equal(originals[firstCharacter], File.ReadAllBytes(firstCharacter));
        Assert.Equal("mutated\r\n", File.ReadAllText(firstUi));
        Assert.Equal("mutated\r\n", File.ReadAllText(secondCharacter));
        Assert.Equal("mutated\r\n", File.ReadAllText(secondUi));
        Assert.Equal("mutated\r\n", File.ReadAllText(otherServer));

        var rest = GameFileSaver.Restore(game.Path, [first.UiSource!, second.CharacterSource!, second.UiSource!]);
        Assert.True(rest.Success, rest.Message);
        Assert.Equal(originals[firstCharacter], File.ReadAllBytes(firstCharacter));
        Assert.Equal(originals[firstUi], File.ReadAllBytes(firstUi));
        Assert.Equal(originals[secondCharacter], File.ReadAllBytes(secondCharacter));
        Assert.Equal(originals[secondUi], File.ReadAllBytes(secondUi));
        Assert.Equal("mutated\r\n", File.ReadAllText(otherServer));
    }

    [Fact]
    public void FullBackupIsDue_WaitsFifteenMinutesAfterTheNewestFullSnapshot()
    {
        using var game = new GameFolder();
        var interval = TimeSpan.FromMinutes(15);
        var recent = Local(2026, 11, 8, 9, 45, 1);
        var older = Local(2026, 11, 8, 9, 20, 0);
        var now = Local(2026, 11, 8, 10, 0, 0);

        Assert.True(GameFileSaver.FullBackupIsDue(null, now, interval));
        Assert.True(GameFileSaver.FullBackupIsDue(game.Path, now, interval));

        Directory.CreateDirectory(Path.Combine(game.Path, "LoadoutBackups", "MODIFIED", StampName(older)));
        Directory.CreateDirectory(Path.Combine(game.Path, "LoadoutBackups", "FULL", "not-a-stamp"));
        Assert.True(GameFileSaver.FullBackupIsDue(game.Path, now, interval));

        Directory.CreateDirectory(Path.Combine(game.Path, "LoadoutBackups", "FULL", StampName(older)));
        Directory.CreateDirectory(Path.Combine(game.Path, "LoadoutBackups", "FULL", StampName(recent)));
        Assert.False(GameFileSaver.FullBackupIsDue(game.Path, now, interval));
        Assert.True(GameFileSaver.FullBackupIsDue(game.Path, recent.AddMinutes(15), interval));
        Assert.False(GameFileSaver.FullBackupIsDue(game.Path, recent.AddMinutes(15).AddSeconds(-1), interval));
    }

    private static void AssertOnly(string original, string actual, params (string Section, string Key, string Value)[] expected)
    {
        var before = IniDocument.Parse(original);
        var after = IniDocument.Parse(actual);
        Assert.Equal(before.Sections.Select(section => section.Name).ToArray(), after.Sections.Select(section => section.Name).ToArray());
        var pending = expected.ToDictionary(change => (change.Section, change.Key), change => change.Value);
        foreach (var section in before.Sections)
        {
            var written = after.Find(section.Name);
            Assert.NotNull(written);
            Assert.Equal(section.Entries.Count, written.Entries.Count);
            for (var index = 0; index < section.Entries.Count; index++)
            {
                var left = section.Entries[index];
                var right = written.Entries[index];
                Assert.Equal(left.Key, right.Key);
                Assert.Equal(left.HasEquals, right.HasEquals);
                if (pending.Remove((section.Name, left.Key), out var value))
                {
                    Assert.NotEqual(left.Value, value);
                    Assert.Equal(value, right.Value);
                }
                else
                {
                    Assert.Equal(left.Value, right.Value);
                }
            }
        }

        Assert.Empty(pending);
    }

    private static string CharacterIni(string socialName)
        => "[Socials]\r\n" +
            $"Page1Button1Name={socialName}\r\n" +
            "Page1Button1Line1=/hail\r\n" +
            "Page2Button1Name=Sit\r\n" +
            "[SpellLoadouts]\r\n" +
            "SpellLoadout1.inuse=1\r\n" +
            "SpellLoadout1.name=Attack\r\n" +
            "SpellLoadout2.inuse=1\r\n" +
            "SpellLoadout2.name=Heal\r\n" +
            "[Options]\r\n" +
            "ShowGrid=1\r\n";

    private static string UiIni(string xpos)
    {
        var builder = new StringBuilder();
        foreach (var section in WindowSections.Append("TargetWindow"))
        {
            builder.Append('[').Append(section).Append("]\r\n");
            builder.Append("XPos=").Append(xpos).Append("\r\n");
            builder.Append("YPos=20\r\nWidth=100\r\nHeight=40\r\n");
        }

        return builder.ToString();
    }

    private static StoredBackup Copy(string character, string server, int number, LoadoutFileKind kind, byte[] bytes)
        => new(new LoadoutId(character, server, number), kind, bytes);

    private static DateTimeOffset Stamp(int hour, int minute)
        => new(2026, 11, 8, hour, minute, 0, TimeSpan.FromHours(-5));

    private static DateTimeOffset Local(int year, int month, int day, int hour, int minute, int second)
    {
        var local = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }

    private static string StampName(DateTimeOffset timestamp)
        => timestamp.LocalDateTime.ToString(GameFileSaver.BackupStampFormat, CultureInfo.InvariantCulture);

    private sealed class GameFolder : IDisposable
    {
        private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory();

        public string Path => _directory.FullName;

        public string Write(string fileName, string text)
        {
            var path = System.IO.Path.Combine(Path, fileName);
            File.WriteAllText(path, text, Utf8);
            return path;
        }

        public string Backup(string kind, DateTimeOffset timestamp, int loadout, string fileName, bool modifiedCopy = false)
        {
            var parts = new List<string> { Path, "LoadoutBackups", kind, StampName(timestamp) };
            if (modifiedCopy)
            {
                parts.Add("modified");
            }

            parts.Add("Rivervale - Zenator");
            parts.Add("LO" + loadout.ToString("00", CultureInfo.InvariantCulture));
            parts.Add(fileName);
            return System.IO.Path.Combine(parts.ToArray());
        }

        public void Dispose() => _directory.Delete(recursive: true);
    }
}
