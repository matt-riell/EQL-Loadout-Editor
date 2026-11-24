using EqlLoadouts.Core.Content;

namespace EqlLoadouts.Core.Tests;

public class InventoryExportTests
{
    private static readonly string[] Sample =
    [
        "Location\tName\tID\tCount\tSlots",
        "Head\tLustrous Russet Helm +6\t1234\t1\t10",
        "General 1\tFleeting Quiver\t17001\t1\t6",
        "General 1-Slot1\tArrow\t8005\t20\t0",
        "General 1-Slot2\tEmpty\t0\t0\t0",
        "General 3\tSpacious Rucksack\t177751\t1\t24",
        "General 3-Slot1\tPhosphorous Powder\t24082\t48\t10",
        "General 3-Slot2\tEmpty\t0\t0\t0",
        "General 3-Slot3\tBone Chips\t13073\t5\t0",
        "General 11\tEmpty\t0\t0\t0",
        "Bank16\tStorage Trunk\t17150\t1\t50",
        "Bank16-Slot1\tGem\t10010\t1\t0",
        "SharedBank1\tLarge Bag\t17005\t1\t8",
    ];

    [Fact]
    public void Parse_MapsGeneralAndBankSlotsToBagSections()
    {
        var export = InventoryExport.Parse(Sample, DateTime.MinValue);

        AssertBag(export.Find("BagInv1"), "Fleeting Quiver", 6, [0]);
        AssertBag(export.Find("BagInv3"), "Spacious Rucksack", 24, [0, 2]);
        AssertBag(export.Find("BagBank16"), "Storage Trunk", 50, [0]);
    }

    private static void AssertBag(BagContents? bag, string itemName, int slots, int[] usedSlots)
    {
        Assert.NotNull(bag);
        Assert.Equal(itemName, bag.ItemName);
        Assert.Equal(slots, bag.Slots);
        Assert.Equal(usedSlots.Length, bag.Used);
        Assert.Equal(usedSlots, bag.UsedSlots.Order());
    }

    [Fact]
    public void Parse_IgnoresEquipmentEmptySlotsAndSharedBank()
    {
        var export = InventoryExport.Parse(Sample, DateTime.MinValue);

        Assert.Equal(3, export.Bags.Count);
        Assert.Null(export.Find("BagInv11"));
    }

    [Fact]
    public void FileName_MatchesTheGameOutput()
    {
        Assert.Equal("Zenator_rivervale-Inventory.txt", InventoryExport.FileName("Zenator", "rivervale"));
    }
}
