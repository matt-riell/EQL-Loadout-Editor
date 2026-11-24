using System.Text;
using EqlLoadouts.Core.Content;

namespace EqlLoadouts.Core.Tests;

public class SpellPowerTests
{
    private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Yaulp"] = "Yaulp",
        ["Yaulp II"] = "Yaulp II",
        ["Spirit of Wolf"] = "Spirit of Wolf",
        ["Frenzied Spirit"] = "Frenzied Spirit",
    };

    [Theory]
    [InlineData("Spirit of Wolf III", "Spirit of Wolf", 3)]
    [InlineData("Frenzied Spirit X", "Frenzied Spirit", 10)]
    [InlineData("Spirit of Wolf", "Spirit of Wolf", 0)]
    [InlineData("Yaulp II", "Yaulp II", 0)]
    public void Cast_names_keep_a_real_spell_and_strip_only_a_power_numeral(string spoken, string spell, int power)
    {
        Assert.True(SpellPower.TryParseCast(spoken, Names, out var name, out var rank));
        Assert.Equal(spell, name);
        Assert.Equal(power, rank);
        Assert.Equal(power == 0 ? spell : spell + " " + SpellPower.Roman(power), SpellPower.DisplayName(spell, power));
    }

    [Fact]
    public void A_name_that_is_not_a_spell_is_ignored()
    {
        Assert.False(SpellPower.TryParseCast("Not A Spell III", Names, out _, out _));
    }

    [Fact]
    public void The_latest_cast_wins()
    {
        var lines = new[]
        {
            "[Fri Nov 21 12:00:00 2026] You begin casting Spirit of Wolf.",
            "[Fri Nov 21 12:01:00 2026] You begin casting Spirit of Wolf III.",
            "[Fri Nov 21 12:02:00 2026] You begin casting Yaulp II.",
            "[Fri Nov 21 12:03:00 2026] Someone else begins casting Frenzied Spirit X.",
            "[Fri Nov 21 12:04:00 2026] You begin casting Spirit of Wolf.",
        };

        var powers = SpellPower.Read(lines, Names);

        Assert.Equal(0, powers["Spirit of Wolf"]);
        Assert.Equal(0, powers["Yaulp II"]);
        Assert.False(powers.ContainsKey("Frenzied Spirit"));
    }

    [Fact]
    public void A_stream_reports_bytes_read()
    {
        var text = Encoding.UTF8.GetBytes("[log] You begin casting Frenzied Spirit X.\n");
        using var stream = new MemoryStream(text);
        long reported = -1;
        var progress = new SyncProgress(pair => reported = pair.Read);

        var powers = SpellPower.Read(stream, Names, progress, CancellationToken.None);

        Assert.Equal(10, powers["Frenzied Spirit"]);
        Assert.Equal(text.Length, reported);
    }

    private sealed class SyncProgress(Action<(long Read, long Length)> report) : IProgress<(long Read, long Length)>
    {
        public void Report((long Read, long Length) value) => report(value);
    }
}
