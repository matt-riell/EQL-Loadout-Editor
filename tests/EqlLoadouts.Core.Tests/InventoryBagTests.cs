using EqlLoadouts.Core.Content;

namespace EqlLoadouts.Core.Tests;

public class InventoryBagTests
{
    [Fact]
    public void Union_KeepsEachInventoryBagNumber_FromEveryLoadout()
    {
        var first = InventoryBags.NamesIn(
        [
            "[BagInv1]",
            "Location=1,1,10,10",
            "[BagInv10]",
            "[BagBank1]",
            "[ChatWindow]",
        ]);
        var second = InventoryBags.NamesIn(
        [
            "  [BagInv11]  ",
            "[BagInv10]",
            "[BagInv]",
            "[BagInv11a]",
        ]);

        var bags = InventoryBags.Union([first, second]);

        Assert.Equal(["BagInv1", "BagInv10", "BagInv11"], bags);
    }
}
