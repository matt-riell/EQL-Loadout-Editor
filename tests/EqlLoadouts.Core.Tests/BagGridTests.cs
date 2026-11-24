using EqlLoadouts.Core.Layout;

namespace EqlLoadouts.Core.Tests;

public class BagGridTests
{
    [Theory]
    [InlineData(10, 52, 1, 10, 52, 441)]
    [InlineData(10, 94, 2, 5, 94, 231)]
    [InlineData(24, 136, 3, 8, 136, 357)]
    [InlineData(50, 430, 10, 5, 430, 231)]
    public void FromWidth_MatchesSizesTheGameSaves(int slots, double width, int columns, int rows, double snappedWidth, double height)
    {
        var grid = BagGrid.FromWidth(slots, width);

        Assert.Equal(columns, grid.Columns);
        Assert.Equal(rows, grid.Rows);
        Assert.Equal(snappedWidth, grid.Width);
        Assert.Equal(height, grid.Height);
    }

    [Fact]
    public void FromWidth_ShrinksToTheLastWholeColumn()
    {
        var grid = BagGrid.FromWidth(10, 135);

        Assert.Equal(2, grid.Columns);
        Assert.Equal(5, grid.Rows);
        Assert.Equal(94, grid.Width);
        Assert.Equal(136, BagGrid.FromWidth(10, 177).Width);
    }

    [Fact]
    public void FromWidth_KeepsAtLeastOneColumnAndNoMoreThanTheSlots()
    {
        Assert.Equal(1, BagGrid.FromWidth(6, 50).Columns);
        Assert.Equal(6, BagGrid.FromWidth(6, 1000).Columns);
        Assert.Equal(1, BagGrid.FromWidth(6, 1000).Rows);
    }

    [Fact]
    public void FromHeight_PicksTheNarrowestBagThatFits()
    {
        var grid = BagGrid.FromHeight(10, 231);

        Assert.Equal(2, grid.Columns);
        Assert.Equal(5, grid.Rows);
        Assert.Equal(3, BagGrid.FromHeight(10, 189).Columns);
        Assert.Equal(4, BagGrid.FromHeight(10, 189).Rows);
    }

    [Fact]
    public void Slot_FlowsLeftToRightThenDown()
    {
        var grid = BagGrid.ForColumns(10, 3);

        Assert.Equal(new ScreenRect(6, 17, 40, 40), grid.Slot(0));
        Assert.Equal(new ScreenRect(90, 17, 40, 40), grid.Slot(2));
        Assert.Equal(new ScreenRect(6, 59, 40, 40), grid.Slot(3));
        Assert.Equal(new ScreenRect(6, 143, 40, 40), grid.Slot(9));
    }

    [Fact]
    public void PaintedFrame_RoundTripsAndTouchesWhenStackedWithNoGap()
    {
        var first = new ScreenRect(100, 50, 52, 273);
        var second = new ScreenRect(0, 0, 136, 357);
        var painted = new[] { BagGrid.ToPainted(first), BagGrid.ToPainted(second) };

        Assert.Equal(first, BagGrid.FromPainted(painted[0]));

        var right = LayoutArranger.Arrange(painted, ArrangeAction.StackRight);
        Assert.Equal(first.Left + first.Width - 6, BagGrid.FromPainted(right[1]).Left);

        var down = LayoutArranger.Arrange(painted, ArrangeAction.StackDown, verticalSpacing: 10);
        Assert.Equal(first.Top + first.Height - 7 + 10, BagGrid.FromPainted(down[1]).Top);
    }

    [Fact]
    public void EdgeAnchor_MovesThePaintedFrameOntoTheAnchor()
    {
        var center = new ScreenRect(1810.016, 240.084, 136, 357);
        var right = new ScreenRect(1940.013, 240.084, 52, 441);
        var top = new ScreenRect(1679.968, 240.048, 136, 357);

        var onScreenRight = BagGrid.ToScreen(right, "right", "center");
        var onScreenTop = BagGrid.ToScreen(top, "center", "top");

        Assert.Equal(right, BagGrid.ToStored(onScreenRight, "right", "center"));
        Assert.Equal(3d, onScreenRight.Left - right.Left, 3);
        Assert.Equal(center.Top, BagGrid.ToScreen(center, "center", "center").Top, 3);

        var centerPainted = BagGrid.ToPainted(center);
        var rightPainted = BagGrid.ToPainted(onScreenRight);
        var topPainted = BagGrid.ToPainted(onScreenTop);
        Assert.Equal(3d, rightPainted.Left - (centerPainted.Left + centerPainted.Width), 1);
        Assert.Equal(3d, centerPainted.Top - topPainted.Top, 1);
    }
}
