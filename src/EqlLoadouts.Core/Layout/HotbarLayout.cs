namespace EqlLoadouts.Core.Layout;

/// <summary>
/// Button positions inside a hot button window. The window keeps the size the game saved.
/// Buttons are 40 pixels with 2 pixels between them across and 1 down. A horizontal bar
/// reserves 11 pixels on the right for the page spinner, and a vertical bar reserves 11 on top.
/// </summary>
public static class HotbarLayout
{
    public const int Buttons = 12;

    public const double Button = 40;

    public const double Spacing = 2;

    public const double SecondarySpacing = 1;

    public const double Edge = 1;

    public const double Spinner = 11;

    // The saved window is taller than the button row. The game paints only the buttons plus this pad.
    public const double FramePad = 2;

    public static bool IsHorizontal(double width, double height) => width >= height;

    /// <summary>
    /// The part of a horizontal bar the game actually paints. The leftover height sits below the buttons
    /// and shows the world, so the editor frame stops at the button row.
    /// </summary>
    public static FrameInset Chrome(double width, double height)
    {
        if (!IsHorizontal(width, height) || height <= 0)
        {
            return FrameInset.None;
        }

        var rows = (int)Math.Ceiling(Buttons / (double)Columns(width, height));
        var used = Edge + (rows * Button) + ((rows - 1) * SecondarySpacing) + FramePad;
        var extra = height - used;
        return extra > 0.5 ? new FrameInset(0, 0, 0, extra) : FrameInset.None;
    }

    public static int Columns(double width, double height)
    {
        var horizontal = IsHorizontal(width, height);
        var inner = width - Edge - (horizontal ? Spinner : Edge);
        var columns = (int)Math.Floor((Math.Max(Button, inner) + Spacing) / (Button + Spacing));
        return Math.Clamp(columns, 1, Buttons);
    }

    public static ScreenRect Slot(int index, double width, double height)
    {
        var columns = Columns(width, height);
        var column = index % columns;
        var row = index / columns;
        var top = IsHorizontal(width, height) ? Edge : Spinner;
        return new ScreenRect(
            Edge + (column * (Button + Spacing)),
            top + (row * (Button + SecondarySpacing)),
            Button,
            Button);
    }
}
