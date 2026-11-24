using EqlLoadouts.Core.Content;

namespace EqlLoadouts.Core.Tests;

public class CharacterClassesTests
{
    [Theory]
    [InlineData("SHM/ROG/ENC", "SHM/ROG/ENC")]
    [InlineData("shm, rog, enc", "SHM/ROG/ENC")]
    [InlineData("Shadow Knight - Paladin", "SHD/PAL")]
    [InlineData("RNG/RNG/BRD", "RNG/BRD")]
    [InlineData("raid tank", "")]
    [InlineData("", "")]
    public void Parse_ReadsKnownClassesInOrder(string label, string expected)
    {
        Assert.Equal(expected, CharacterClasses.Format(CharacterClasses.Parse(label)));
    }
}

public class WhoLogTests
{
    [Fact]
    public void Parse_GroupsSightingsByClassesAndKeepsTheLatestLevel()
    {
        string[] lines =
        [
            "[Thu Aug 13 20:08:06 2026] [47 ROG/SHM/ENC] Zenator (Iksar) <Static> ZONE: The Plane of Hate (hateplane)",
            "[Sat Aug 15 21:13:36 2026] [50 ROG/MAG/ENC] Grazix (Dark Elf) <Fellatio> ZONE: Neriak - Commons (neriakb)",
            "[Sat Oct 03 09:04:40 2026] [50 ROG/SHM/ENC] Zenator (Iksar) <Static> ZONE: New Sebilis (newsebexp)",
            "[Wed Oct 14 17:26:01 2026]  AFK [33 RNG/BRD/SHM] Zenator (Iksar) <Static> ZONE: Lake Rathetear (lakerathe)",
            "[Wed Oct 14 17:27:00 2026] [ANONYMOUS] Zenator (Iksar)",
            "[Wed Oct 14 17:28:00 2026] Zenator tells you, '[50 WAR/CLR/ENC] Zenator is fake'",
        ];

        var sightings = WhoLog.Parse(lines, "Zenator");

        Assert.Equal(["RNG/BRD/SHM", "ROG/SHM/ENC"], sightings.Select(sighting => sighting.ClassText));
        Assert.Equal(33, sightings[0].Level);
        Assert.Equal(50, sightings[1].Level);
        Assert.Equal(2, sightings[1].Count);
        Assert.Equal(new DateTime(2026, 10, 3, 9, 4, 40), sightings[1].LastSeen);
        Assert.Equal(new DateTime(2026, 10, 14, 17, 26, 1), WhoLog.LastSeen(sightings, CharacterClasses.Parse("SHM/RNG/BRD")));
        Assert.Equal(new DateTime(2026, 10, 3, 9, 4, 40), WhoLog.LastSeen(sightings, CharacterClasses.Parse("ENC/SHM/ROG")));
        Assert.Null(WhoLog.LastSeen(sightings, CharacterClasses.Parse("CLR/DRU/BRD")));
        Assert.Equal(33, WhoLog.LevelFor(sightings, CharacterClasses.Parse("SHM/RNG/BRD")));
        Assert.Equal(50, WhoLog.LevelFor(sightings, CharacterClasses.Parse("ENC/SHM/ROG")));
        Assert.Null(WhoLog.LevelFor(sightings, CharacterClasses.Parse("CLR/DRU/BRD")));
    }

    [Fact]
    public void Scan_ReadsOnlyAppendedLinesOnTheNextPass()
    {
        var path = Path.Combine(Path.GetTempPath(), $"eqlog_{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllText(path, "[Thu Aug 13 20:08:06 2026] [47 ROG/SHM/ENC] Zenator (Iksar) ZONE: Hate (hateplane)\n");
            var first = WhoLog.Scan(path, "Zenator");

            File.AppendAllText(path, "[Sat Oct 03 09:04:40 2026] [50 ROG/SHM/ENC] Zenator (Iksar) ZONE: Sebilis (sebilis)\n");
            var second = WhoLog.Scan(path, "Zenator", first);

            Assert.Equal(new FileInfo(path).Length, second.Length);
            var sighting = Assert.Single(second.Sightings);
            Assert.Equal(50, sighting.Level);
            Assert.Equal(2, sighting.Count);
            Assert.Same(second, WhoLog.Scan(path, "Zenator", second));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Scan_ReadsFromTheStartWhenTheFileShrinks()
    {
        var path = Path.Combine(Path.GetTempPath(), $"eqlog_{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllText(path, "[Thu Aug 13 20:08:06 2026] [47 ROG/SHM/ENC] Zenator (Iksar) ZONE: Hate (hateplane)\n");
            var first = WhoLog.Scan(path, "Zenator");

            File.WriteAllText(path, "[Sat Oct 03 09:04:40 2026] [33 RNG/BRD/SHM] Zenator (Iksar)\n");
            var second = WhoLog.Scan(path, "Zenator", first);

            var sighting = Assert.Single(second.Sightings);
            Assert.Equal(33, sighting.Level);
            Assert.Equal("RNG/BRD/SHM", sighting.ClassText);
            Assert.True(second.Length < first.Length);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TryParseLogFileName_KeepsUnderscoresInTheCharacter()
    {
        Assert.True(WhoLog.TryParseLogFileName(@"C:\Games\Logs\eqlog_Zenator_rivervale.txt", out var character, out var server));
        Assert.Equal("Zenator", character);
        Assert.Equal("rivervale", server);

        Assert.True(WhoLog.TryParseLogFileName(@"eqlog_Some_Name_freeport.txt", out character, out server));
        Assert.Equal("Some_Name", character);
        Assert.Equal("freeport", server);
        Assert.False(WhoLog.TryParseLogFileName(@"eqlog_Zenator.txt", out _, out _));
    }
}
