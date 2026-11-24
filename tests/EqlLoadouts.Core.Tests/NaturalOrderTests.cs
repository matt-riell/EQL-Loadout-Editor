namespace EqlLoadouts.Core.Tests;

public class NaturalOrderTests
{
    [Fact]
    public void Compare_OrdersPageAndLineNumbersByValue()
    {
        var keys = new[]
        {
            "Page10Button7LIne3",
            "Page2Button4LIne1",
            "Page10Button7LIne1",
        };

        Array.Sort(keys, NaturalOrder.Compare);

        Assert.Equal(
            ["Page2Button4LIne1", "Page10Button7LIne1", "Page10Button7LIne3"],
            keys);
    }

    [Fact]
    public void Compare_OrdersButtonNumbersByValue()
    {
        var keys = new[]
        {
            "Page2Button10Line1",
            "Page2Button4Line1",
            "Page2Button4Line2",
        };

        Array.Sort(keys, NaturalOrder.Compare);

        Assert.Equal(
            ["Page2Button4Line1", "Page2Button4Line2", "Page2Button10Line1"],
            keys);
    }
}
