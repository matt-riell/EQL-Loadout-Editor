using EqlLoadouts.Core.Loadouts;

namespace EqlLoadouts.Core.Tests;

public class ActiveLoadoutTests
{
    private static readonly LoadoutId First = new("Zenator", "rivervale", 1);

    private static readonly LoadoutId Second = new("Zenator", "rivervale", 2);

    [Fact]
    public void Newest_UsesTheLaterFileInALoadoutPair()
    {
        var noon = new DateTime(2026, 11, 24, 12, 0, 0);
        var winner = ActiveLoadout.Newest(
        [
            new LoadoutFileWrite(First, noon),
            new LoadoutFileWrite(First, noon.AddMinutes(5)),
            new LoadoutFileWrite(Second, noon.AddMinutes(1)),
        ]);

        Assert.Equal(First, winner);
    }

    [Fact]
    public void Newest_ATieIsNotAMatch()
    {
        var noon = new DateTime(2026, 11, 24, 12, 0, 0);
        Assert.Null(ActiveLoadout.Newest(
        [
            new LoadoutFileWrite(First, noon),
            new LoadoutFileWrite(Second, noon),
        ]));
        Assert.Null(ActiveLoadout.Newest([]));
    }
}
