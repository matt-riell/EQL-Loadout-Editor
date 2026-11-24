using EqlLoadouts.Core.Layout;

namespace EqlLoadouts.Core.Tests;

public class WindowFrameTests
{
    [Fact]
    public void TargetPlate_SitsSevenPixelsInsideTheWindow()
    {
        var window = new ScreenRect(1909, 935, 336, 193);
        var chat = new ScreenRect(1909, 1133, 648, 302);

        var plate = WindowFrame.Paint("TargetWindow").ToPainted(window);

        Assert.Equal(chat.Left + 7, plate.Left);
        Assert.Equal(322, plate.Width);
        Assert.Equal(window, WindowFrame.Paint("TargetWindow_1").FromPainted(plate));
        Assert.True(WindowFrame.Anchor("TargetWindow").IsEmpty);
    }

    [Fact]
    public void ExtendedTarget_UsesTheSavedRectangle()
    {
        Assert.True(WindowFrame.Paint("ExtendedTargetWnd").IsEmpty);
        Assert.True(WindowFrame.Anchor("ExtendedTargetWnd").IsEmpty);
    }

    [Fact]
    public void SpellWindow_UsesTheSavedRectangle()
    {
        Assert.True(WindowFrame.Paint("CastSpellWnd").IsEmpty);
        Assert.True(WindowFrame.Paint("CastSpellWnd_3").IsEmpty);
        Assert.True(WindowFrame.Anchor("CastSpellWnd_3").IsEmpty);
    }

    [Fact]
    public void OtherWindows_UseTheSavedRectangle()
    {
        Assert.True(WindowFrame.Paint("Chat 2").IsEmpty);
        Assert.True(WindowFrame.Paint("TargetOfTargetWindow").IsEmpty);
        Assert.True(WindowFrame.Anchor("HotButtonWnd").IsEmpty);
    }
}
