using EqlLoadouts.Core.Layout;

namespace EqlLoadouts.Core.Tests;

public class LayoutArrangerTests
{
    private static readonly ScreenRect Anchor = new(100, 200, 80, 60);
    private static readonly ScreenRect Small = new(500, 40, 40, 20);
    private static readonly ScreenRect Large = new(10, 900, 120, 100);

    [Theory]
    [InlineData(ArrangeAction.AlignLeft, 100, 100)]
    [InlineData(ArrangeAction.AlignCenter, 120, 80)]
    [InlineData(ArrangeAction.AlignRight, 140, 60)]
    public void Align_Horizontal_MatchesAnchorAndKeepsTop(ArrangeAction action, double smallLeft, double largeLeft)
    {
        var result = LayoutArranger.Arrange([Anchor, Small, Large], action);

        Assert.Equal(Anchor, result[0]);
        Assert.Equal(Small with { Left = smallLeft }, result[1]);
        Assert.Equal(Large with { Left = largeLeft }, result[2]);
    }

    [Theory]
    [InlineData(ArrangeAction.AlignTop, 200, 200)]
    [InlineData(ArrangeAction.AlignMiddle, 220, 180)]
    [InlineData(ArrangeAction.AlignBottom, 240, 160)]
    public void Align_Vertical_MatchesAnchorAndKeepsLeft(ArrangeAction action, double smallTop, double largeTop)
    {
        var result = LayoutArranger.Arrange([Anchor, Small, Large], action);

        Assert.Equal(Anchor, result[0]);
        Assert.Equal(Small with { Top = smallTop }, result[1]);
        Assert.Equal(Large with { Top = largeTop }, result[2]);
    }

    [Fact]
    public void StackRight_ChainsEachWindowAgainstThePreviousOne()
    {
        var result = LayoutArranger.Arrange([Anchor, Small, Large], ArrangeAction.StackRight);

        Assert.Equal(Anchor, result[0]);
        for (var index = 1; index < result.Count; index++)
        {
            Assert.Equal(result[index - 1].Left + result[index - 1].Width, result[index].Left);
        }

        Assert.Equal(180, result[1].Left);
        Assert.Equal(220, result[2].Left);
        Assert.Equal(Small.Top, result[1].Top);
        Assert.Equal(Large.Top, result[2].Top);
        Assert.Equal(Small.Width, result[1].Width);
        Assert.Equal(Large.Height, result[2].Height);
    }

    [Fact]
    public void StackRight_SnapsAFractionalAnchorSoTheChainStaysOnWholePixels()
    {
        var anchor = new ScreenRect(100.6, 10, 46, 20);
        var next = new ScreenRect(0, 10, 46, 20);
        var result = LayoutArranger.Arrange([anchor, next], ArrangeAction.StackRight);

        Assert.Equal(101, result[0].Left);
        Assert.Equal(147, result[1].Left);
    }

    [Fact]
    public void Stack_LeavesTheRequestedGapBetweenEdges()
    {
        var right = LayoutArranger.Arrange([Anchor, Small, Large], ArrangeAction.StackRight, horizontalSpacing: 10);
        Assert.Equal(190, right[1].Left);
        Assert.Equal(240, right[2].Left);
        Assert.Equal(right[0].Left + right[0].Width + 10, right[1].Left);

        var down = LayoutArranger.Arrange([Anchor, Small], ArrangeAction.StackDown, verticalSpacing: 8);
        Assert.Equal(268, down[1].Top);

        var left = LayoutArranger.Arrange([Anchor, Small], ArrangeAction.StackLeft, horizontalSpacing: 5);
        Assert.Equal(55, left[1].Left);

        var up = LayoutArranger.Arrange([Anchor, Small], ArrangeAction.StackUp, verticalSpacing: 4);
        Assert.Equal(176, up[1].Top);
    }

    [Fact]
    public void StackDown_ChainsEachWindowAgainstThePreviousOne()
    {
        var result = LayoutArranger.Arrange([Anchor, Small, Large], ArrangeAction.StackDown);

        Assert.Equal(Anchor, result[0]);
        for (var index = 1; index < result.Count; index++)
        {
            Assert.Equal(result[index - 1].Top + result[index - 1].Height, result[index].Top);
        }

        Assert.Equal(260, result[1].Top);
        Assert.Equal(280, result[2].Top);
        Assert.Equal(Small.Left, result[1].Left);
        Assert.Equal(Large.Left, result[2].Left);
    }

    [Fact]
    public void StackLeft_PlacesEachWindowLeftOfThePreviousOne()
    {
        var result = LayoutArranger.Arrange([Anchor, Small, Large], ArrangeAction.StackLeft);

        Assert.Equal(Anchor, result[0]);
        Assert.Equal(60, result[1].Left);
        Assert.Equal(-60, result[2].Left);
        Assert.Equal(result[0].Left, result[1].Left + result[1].Width);
        Assert.Equal(result[1].Left, result[2].Left + result[2].Width);
        Assert.Equal(Small.Top, result[1].Top);
        Assert.Equal(Large.Top, result[2].Top);
    }

    [Fact]
    public void StackUp_KeepsTheCurrentTopToBottomOrder()
    {
        var bottom = new ScreenRect(0, 400, 525, 43);
        var melee = new ScreenRect(0, 100, 525, 43);
        var disarm = new ScreenRect(0, 160, 525, 43);
        var tracking = new ScreenRect(0, 220, 525, 43);
        var gather = new ScreenRect(0, 280, 525, 43);
        var selectedTopToBottom = new[] { bottom, melee, disarm, tracking, gather };
        var order = LayoutArranger.StackOrder(selectedTopToBottom, ArrangeAction.StackUp);
        var arranged = LayoutArranger.Arrange(
            order.Select(index => selectedTopToBottom[index]).ToArray(),
            ArrangeAction.StackUp);

        Assert.Equal([0, 4, 3, 2, 1], order);
        Assert.Equal(400, arranged[0].Top);
        Assert.Equal(357, arranged[1].Top);
        Assert.Equal(314, arranged[2].Top);
        Assert.Equal(271, arranged[3].Top);
        Assert.Equal(228, arranged[4].Top);
    }

    [Fact]
    public void StackUp_PlacesEachWindowAboveThePreviousOne()
    {
        var result = LayoutArranger.Arrange([Anchor, Small, Large], ArrangeAction.StackUp);

        Assert.Equal(Anchor, result[0]);
        Assert.Equal(180, result[1].Top);
        Assert.Equal(80, result[2].Top);
        Assert.Equal(result[0].Top, result[1].Top + result[1].Height);
        Assert.Equal(result[1].Top, result[2].Top + result[2].Height);
        Assert.Equal(Small.Left, result[1].Left);
        Assert.Equal(Large.Left, result[2].Left);
    }

    [Theory]
    [InlineData(ArrangeAction.AlignLeft)]
    [InlineData(ArrangeAction.StackRight)]
    [InlineData(ArrangeAction.StackUp)]
    public void Arrange_OneWindow_ReturnsItUnchanged(ArrangeAction action)
    {
        var result = LayoutArranger.Arrange([Small], action);

        Assert.Equal(Small, Assert.Single(result));
    }

    [Fact]
    public void Arrange_Empty_ReturnsEmpty()
    {
        Assert.Empty(LayoutArranger.Arrange([], ArrangeAction.StackDown));
    }

    [Fact]
    public void CenterHorizontally_MovesEveryWindowIncludingTheAnchor()
    {
        var result = LayoutArranger.Arrange([Anchor, Small, Large], ArrangeAction.CenterOnScreenHorizontally, 1000, 800);

        Assert.Equal(new ScreenRect(460, Anchor.Top, Anchor.Width, Anchor.Height), result[0]);
        Assert.Equal(new ScreenRect(480, Small.Top, Small.Width, Small.Height), result[1]);
        Assert.Equal(new ScreenRect(440, Large.Top, Large.Width, Large.Height), result[2]);
    }

    [Fact]
    public void CenterVertically_MovesEveryWindowIncludingTheAnchor()
    {
        var result = LayoutArranger.Arrange([Anchor, Small, Large], ArrangeAction.CenterOnScreenVertically, 1000, 800);

        Assert.Equal(new ScreenRect(Anchor.Left, 370, Anchor.Width, Anchor.Height), result[0]);
        Assert.Equal(new ScreenRect(Small.Left, 390, Small.Width, Small.Height), result[1]);
        Assert.Equal(new ScreenRect(Large.Left, 350, Large.Width, Large.Height), result[2]);
    }

    [Fact]
    public void CenterHorizontally_OneWindow_CentersIt()
    {
        var result = LayoutArranger.Arrange([Small], ArrangeAction.CenterOnScreenHorizontally, 200, 100);

        Assert.Equal(new ScreenRect(80, Small.Top, Small.Width, Small.Height), Assert.Single(result));
    }

    [Fact]
    public void MatchWidth_CopiesTheAnchorWidthAndKeepsTheTopLeft()
    {
        var result = LayoutArranger.Arrange([Anchor, Small, Large], ArrangeAction.MatchWidth);

        Assert.Equal(Anchor, result[0]);
        Assert.Equal(Small with { Width = Anchor.Width }, result[1]);
        Assert.Equal(Large with { Width = Anchor.Width }, result[2]);
    }

    [Fact]
    public void MatchHeight_CopiesTheAnchorHeightAndKeepsTheTopLeft()
    {
        var result = LayoutArranger.Arrange([Anchor, Small, Large], ArrangeAction.MatchHeight);

        Assert.Equal(Anchor, result[0]);
        Assert.Equal(Small with { Height = Anchor.Height }, result[1]);
        Assert.Equal(Large with { Height = Anchor.Height }, result[2]);
    }

    [Theory]
    [InlineData("left", "top", 500, 40)]
    [InlineData("right", "bottom", 460, 0)]
    [InlineData("center", "center", 480, 20)]
    public void KeepAnchor_HoldsTheNamedEdgeWhenTheSizeChanges(string xRef, string yRef, double left, double top)
    {
        var sized = WindowPlacement.KeepAnchor(Small, Anchor.Width, Anchor.Height, xRef, yRef);

        Assert.Equal(new ScreenRect(left, top, Anchor.Width, Anchor.Height), sized);
    }

    [Fact]
    public void IsOffScreen_DetectsWindowsPastAnyEdge()
    {
        Assert.False(LayoutArranger.IsOffScreen(new ScreenRect(0, 0, 100, 100), 100, 100));
        Assert.True(LayoutArranger.IsOffScreen(new ScreenRect(-1, 0, 10, 10), 100, 100));
        Assert.True(LayoutArranger.IsOffScreen(new ScreenRect(95, 0, 10, 10), 100, 100));
        Assert.True(LayoutArranger.IsOffScreen(new ScreenRect(0, 95, 10, 10), 100, 100));
    }
}
