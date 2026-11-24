namespace EqlLoadouts.Core.Layout;

public readonly record struct BagGrid(int Slots, int Columns, int Rows)
{
    public const double SlotPitch = 42;
    public const double SlotSize = 40;
    public const double LeftInset = 6;
    public const double TopInset = 17;
    public const double HorizontalChrome = 10;
    public const double VerticalChrome = 21;

    /// <summary>A bag's stone frame is painted this far inside its saved rectangle. The game anchors that frame, so an edge anchor sits this far toward the edge.</summary>
    public const double FrameInsetLeft = 3;

    public const double FrameInsetTop = 3;

    public const double FrameInsetRight = 3;

    public const double FrameInsetBottom = 4;

    private const double Tolerance = 1e-6;

    public double Width => HorizontalChrome + (Columns * SlotPitch);

    public double Height => VerticalChrome + (Rows * SlotPitch);

    public static BagGrid FromWidth(int slots, double width)
    {
        slots = Math.Max(1, slots);
        var columns = (int)Math.Floor(((width - HorizontalChrome) / SlotPitch) + Tolerance);
        return ForColumns(slots, columns);
    }

    public static BagGrid FromHeight(int slots, double height)
    {
        slots = Math.Max(1, slots);
        var rows = Math.Clamp((int)Math.Floor(((height - VerticalChrome) / SlotPitch) + Tolerance), 1, slots);
        return ForColumns(slots, (slots + rows - 1) / rows);
    }

    public static BagGrid ForColumns(int slots, int columns)
    {
        slots = Math.Max(1, slots);
        columns = Math.Clamp(columns, 1, slots);
        return new BagGrid(slots, columns, (slots + columns - 1) / columns);
    }

    public static ScreenRect ToPainted(ScreenRect saved)
        => saved with
        {
            Left = saved.Left + FrameInsetLeft,
            Top = saved.Top + FrameInsetTop,
            Width = saved.Width - FrameInsetLeft - FrameInsetRight,
            Height = saved.Height - FrameInsetTop - FrameInsetBottom,
        };

    public static ScreenRect FromPainted(ScreenRect painted)
        => painted with
        {
            Left = painted.Left - FrameInsetLeft,
            Top = painted.Top - FrameInsetTop,
            Width = painted.Width + FrameInsetLeft + FrameInsetRight,
            Height = painted.Height + FrameInsetTop + FrameInsetBottom,
        };

    /// <summary>
    /// The game anchors the painted frame. An edge anchor therefore sits one inset toward that edge of the percent rectangle.
    /// </summary>
    public static ScreenRect ToScreen(ScreenRect stored, string? xRef, string? yRef)
        => stored with
        {
            Left = stored.Left + EdgeShiftX(xRef),
            Top = stored.Top + EdgeShiftY(yRef),
        };

    public static ScreenRect ToStored(ScreenRect screen, string? xRef, string? yRef)
        => screen with
        {
            Left = screen.Left - EdgeShiftX(xRef),
            Top = screen.Top - EdgeShiftY(yRef),
        };

    private static double EdgeShiftX(string? xRef) => (xRef ?? "left").ToLowerInvariant() switch
    {
        "left" => -FrameInsetLeft,
        "right" => FrameInsetRight,
        _ => 0,
    };

    private static double EdgeShiftY(string? yRef) => (yRef ?? "top").ToLowerInvariant() switch
    {
        "top" => -FrameInsetTop,
        "bottom" => FrameInsetBottom,
        _ => 0,
    };

    public ScreenRect Slot(int index)
    {
        var column = index % Columns;
        var row = index / Columns;
        return new ScreenRect(LeftInset + (column * SlotPitch), TopInset + (row * SlotPitch), SlotSize, SlotSize);
    }
}
