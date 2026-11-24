using EqlLoadouts.Core.Layout;

namespace EqlLoadouts.Core.Tests;

public class HotbarLayoutTests
{
    [Fact]
    public void HorizontalBar_KeepsTheSavedWidthAndLeavesRoomForTheSpinner()
    {
        Assert.Equal(12, HotbarLayout.Columns(525, 52));
        Assert.Equal(new ScreenRect(1, 1, 40, 40), HotbarLayout.Slot(0, 525, 52));
        Assert.Equal(new ScreenRect(463, 1, 40, 40), HotbarLayout.Slot(11, 525, 52));
        Assert.Equal(514, HotbarLayout.Slot(11, 514, 52).Left + HotbarLayout.Button + HotbarLayout.Spinner);
    }

    [Fact]
    public void HorizontalBar_PaintsOnlyTheButtonRow()
    {
        var chrome = HotbarLayout.Chrome(525, 52);

        Assert.Equal(new FrameInset(0, 0, 0, 9), chrome);
        Assert.Equal(43, chrome.ToPainted(new ScreenRect(0, 1149, 525, 52)).Height);
        Assert.True(HotbarLayout.Chrome(98, 271).IsEmpty);
    }

    [Fact]
    public void VerticalBar_StacksUnderThePageButtons()
    {
        Assert.Equal(2, HotbarLayout.Columns(98, 271));
        Assert.Equal(new ScreenRect(1, 11, 40, 40), HotbarLayout.Slot(0, 98, 271));
        Assert.Equal(new ScreenRect(43, 11, 40, 40), HotbarLayout.Slot(1, 98, 271));
        Assert.Equal(new ScreenRect(1, 52, 40, 40), HotbarLayout.Slot(2, 98, 271));
    }
}
