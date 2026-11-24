using EqlLoadouts.Core.Layout;

namespace EqlLoadouts.Core.Tests;

public class MoveSnapTests
{
    [Fact]
    public void Edges_SnapWhenTheyAreClose()
    {
        var moving = new ScreenRect(104, 40, 50, 20);
        var other = new ScreenRect(100, 10, 50, 20);

        var snapped = MoveSnap.Snap(moving, [other], 8);

        Assert.Equal(100, snapped.Left);
        Assert.Equal(40, snapped.Top);
        Assert.True(snapped.SnappedX);
        Assert.False(snapped.SnappedY);
        Assert.Contains(snapped.Guides, guide => guide.X1 == 100 && guide.X2 == 100);
    }

    [Fact]
    public void Centers_SnapOnBothAxes()
    {
        var moving = new ScreenRect(38, 8, 20, 20);
        var other = new ScreenRect(0, 0, 100, 40);

        var snapped = MoveSnap.Snap(moving, [other], 8);

        Assert.Equal(40, snapped.Left);
        Assert.Equal(10, snapped.Top);
        Assert.True(snapped.SnappedX);
        Assert.True(snapped.SnappedY);
    }

    [Fact]
    public void DistantEdges_StayWhereThePointerLeftThem()
    {
        var moving = new ScreenRect(30, 30, 20, 20);
        var other = new ScreenRect(0, 0, 20, 20);

        var snapped = MoveSnap.Snap(moving, [other], 8);

        Assert.Equal(moving.Left, snapped.Left);
        Assert.Equal(moving.Top, snapped.Top);
        Assert.Empty(snapped.Guides);
    }

    [Fact]
    public void CloserEdge_Wins()
    {
        var moving = new ScreenRect(6, 0, 20, 20);
        var farther = new ScreenRect(0, 0, 20, 20);
        var closer = new ScreenRect(10, 0, 20, 20);

        var snapped = MoveSnap.Snap(moving, [farther, closer], 8);

        Assert.Equal(10, snapped.Left);
    }

    [Fact]
    public void Gap_CentersBetweenTheNeighbors()
    {
        var left = new ScreenRect(0, 0, 100, 40);
        var right = new ScreenRect(200, 0, 100, 40);
        var moving = new ScreenRect(136, 0, 40, 40);

        var snapped = MoveSnap.Snap(moving, [left, right], 8);

        Assert.Equal(130, snapped.Left);
        Assert.Equal(0, snapped.Top);
        Assert.Contains(snapped.Guides, guide => guide.Y1 == 20 && guide.X1 == 100 && guide.X2 == 130);
        Assert.Contains(snapped.Guides, guide => guide.Y1 == 20 && guide.X1 == 170 && guide.X2 == 200);
    }

    [Fact]
    public void Gap_MatchesTheGapAlreadyUsed()
    {
        var first = new ScreenRect(0, 0, 40, 20);
        var second = new ScreenRect(60, 0, 40, 20);
        var moving = new ScreenRect(125, 0, 40, 20);

        var snapped = MoveSnap.Snap(moving, [first, second], 8);

        Assert.Equal(120, snapped.Left);
    }

    [Fact]
    public void Gap_IgnoresShapesOutsideTheRow()
    {
        var first = new ScreenRect(0, 0, 40, 20);
        var second = new ScreenRect(60, 200, 40, 20);
        var moving = new ScreenRect(125, 0, 40, 20);

        var snapped = MoveSnap.Snap(moving, [first, second], 8);

        Assert.Equal(125, snapped.Left);
    }

    [Fact]
    public void Resize_BottomEdge_SnapsToAnotherBottomAndKeepsTheTop()
    {
        var moving = new ScreenRect(10, 10, 40, 30);
        var other = new ScreenRect(80, 16, 40, 30);

        var snapped = MoveSnap.SnapResize(moving, [other], 8, leftEdge: false, rightEdge: false, topEdge: false, bottomEdge: true);

        Assert.Equal(new ScreenRect(10, 10, 40, 36), snapped.Rect);
        Assert.False(snapped.SnappedX);
        Assert.True(snapped.SnappedY);
        Assert.Contains(snapped.Guides, guide => guide.Y1 == 46 && guide.Y2 == 46);
    }

    [Fact]
    public void Resize_RightEdge_SnapsToAnotherLeftAndKeepsTheLeft()
    {
        var moving = new ScreenRect(0, 0, 50, 20);
        var other = new ScreenRect(58, 0, 40, 20);

        var snapped = MoveSnap.SnapResize(moving, [other], 8, leftEdge: false, rightEdge: true, topEdge: false, bottomEdge: false);

        Assert.Equal(new ScreenRect(0, 0, 58, 20), snapped.Rect);
        Assert.True(snapped.SnappedX);
        Assert.False(snapped.SnappedY);
        Assert.Contains(snapped.Guides, guide => guide.X1 == 58 && guide.X2 == 58);
    }

    [Fact]
    public void Resize_LeftEdge_KeepsTheRightEdge()
    {
        var moving = new ScreenRect(10, 0, 40, 20);
        var other = new ScreenRect(0, 0, 8, 20);

        var snapped = MoveSnap.SnapResize(moving, [other], 8, leftEdge: true, rightEdge: false, topEdge: false, bottomEdge: false);

        Assert.Equal(new ScreenRect(8, 0, 42, 20), snapped.Rect);
        Assert.Equal(50, snapped.Rect.Left + snapped.Rect.Width);
    }

    [Fact]
    public void Resize_TopEdge_KeepsTheBottomEdge()
    {
        var moving = new ScreenRect(0, 10, 20, 40);
        var other = new ScreenRect(0, 4, 20, 40);

        var snapped = MoveSnap.SnapResize(moving, [other], 8, leftEdge: false, rightEdge: false, topEdge: true, bottomEdge: false);

        Assert.Equal(new ScreenRect(0, 4, 20, 46), snapped.Rect);
        Assert.Equal(50, snapped.Rect.Top + snapped.Rect.Height);
    }

    [Fact]
    public void Resize_DistantEdge_StaysWhereThePointerLeftIt()
    {
        var moving = new ScreenRect(0, 0, 40, 40);
        var other = new ScreenRect(0, 100, 40, 20);

        var snapped = MoveSnap.SnapResize(moving, [other], 8, leftEdge: false, rightEdge: false, topEdge: false, bottomEdge: true);

        Assert.Equal(moving, snapped.Rect);
        Assert.Empty(snapped.Guides);
    }

    [Fact]
    public void Resize_BottomDrag_DoesNotMoveTheTopEvenWhenTheTopIsCloser()
    {
        var moving = new ScreenRect(0, 10, 30, 40);
        var topNeighbor = new ScreenRect(0, 0, 20, 12);
        var bottomNeighbor = new ScreenRect(0, 55, 20, 10);

        var snapped = MoveSnap.SnapResize(moving, [topNeighbor, bottomNeighbor], 8, leftEdge: false, rightEdge: false, topEdge: false, bottomEdge: true);

        Assert.Equal(10, snapped.Rect.Top);
        Assert.Equal(45, snapped.Rect.Height);
    }

    [Fact]
    public void Resize_Corner_SnapsBothEdges()
    {
        var moving = new ScreenRect(0, 0, 40, 40);
        var other = new ScreenRect(46, 48, 20, 20);

        var snapped = MoveSnap.SnapResize(moving, [other], 8, leftEdge: false, rightEdge: true, topEdge: false, bottomEdge: true);

        Assert.Equal(new ScreenRect(0, 0, 46, 48), snapped.Rect);
        Assert.True(snapped.SnappedX);
        Assert.True(snapped.SnappedY);
    }

    [Fact]
    public void Resize_RejectsASnapThatWouldShrinkPastTheMinimum()
    {
        var moving = new ScreenRect(0, 0, 30, 20);
        var other = new ScreenRect(22, 0, 40, 20);

        var snapped = MoveSnap.SnapResize(moving, [other], 8, leftEdge: false, rightEdge: true, topEdge: false, bottomEdge: false, minWidth: 25);

        Assert.Equal(moving, snapped.Rect);
        Assert.False(snapped.SnappedX);
    }

    [Fact]
    public void Resize_BottomEdge_MatchesAnExistingGap()
    {
        var above = new ScreenRect(0, 0, 40, 40);
        var spaced = new ScreenRect(0, 50, 40, 40);
        var below = new ScreenRect(0, 160, 40, 40);
        var moving = new ScreenRect(0, 100, 40, 46);

        var snapped = MoveSnap.SnapResize(moving, [above, spaced, below], 12, leftEdge: false, rightEdge: false, topEdge: false, bottomEdge: true);

        Assert.Equal(new ScreenRect(0, 100, 40, 50), snapped.Rect);
        Assert.Contains(snapped.Guides, guide => guide.X1 == 20 && guide.Y1 == 150 && guide.Y2 == 160);
    }
}
