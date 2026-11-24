using EqlLoadouts.Core.Layout;

namespace EqlLoadouts.Core.Tests;

public class WindowSelectAllTests
{
    private static readonly ShownWindow[] Windows =
    [
        new("Inventory bags", "BagInv1", Shown: true),
        new("Inventory bags", "BagInv2", Shown: false),
        new("Chat", "MainChat", Shown: true),
        new("Chat", "Chat 1", Shown: true),
        new("Spell Gems", "CastSpellWnd", Shown: false),
    ];

    [Fact]
    public void OneSection_SelectsOnlyItsShownWindows()
    {
        var names = WindowSelectAll.Names(Windows, allSectionsSelected: false, "Inventory bags");

        Assert.Equal(["BagInv1"], names);
    }

    [Fact]
    public void AllSections_SelectsEveryShownWindow()
    {
        var names = WindowSelectAll.Names(Windows, allSectionsSelected: true, "Inventory bags");

        Assert.Equal(["BagInv1", "MainChat", "Chat 1"], names);
    }

    [Fact]
    public void NoSection_SelectsNothing()
    {
        Assert.Empty(WindowSelectAll.Names(Windows, allSectionsSelected: false, selectedSection: null));
    }
}
