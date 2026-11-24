namespace EqlLoadouts.Core.Layout;

/// <summary>Where a window's visible frame sits relative to the rectangle stored in the UI file.</summary>
public readonly record struct FrameInset(double Left, double Top, double Right, double Bottom)
{
    public static FrameInset None { get; } = new(0, 0, 0, 0);

    public static FrameInset Bag { get; } = new(
        BagGrid.FrameInsetLeft,
        BagGrid.FrameInsetTop,
        BagGrid.FrameInsetRight,
        BagGrid.FrameInsetBottom);

    public bool IsEmpty => Left == 0 && Top == 0 && Right == 0 && Bottom == 0;

    public ScreenRect ToPainted(ScreenRect saved)
        => new(
            saved.Left + Left,
            saved.Top + Top,
            saved.Width - Left - Right,
            saved.Height - Top - Bottom);

    public ScreenRect FromPainted(ScreenRect painted)
        => new(
            painted.Left - Left,
            painted.Top - Top,
            painted.Width + Left + Right,
            painted.Height + Top + Bottom);

    /// <summary>The game anchors the painted edge, so the saved rectangle shifts toward that edge by the inset.</summary>
    public ScreenRect ToScreen(ScreenRect stored, string? xRef, string? yRef)
        => stored with { Left = stored.Left + ShiftX(xRef), Top = stored.Top + ShiftY(yRef) };

    public ScreenRect ToStored(ScreenRect screen, string? xRef, string? yRef)
        => screen with { Left = screen.Left - ShiftX(xRef), Top = screen.Top - ShiftY(yRef) };

    private double ShiftX(string? xRef) => (xRef ?? "left").ToLowerInvariant() switch
    {
        "left" => -Left,
        "right" => Right,
        _ => 0,
    };

    private double ShiftY(string? yRef) => (yRef ?? "top").ToLowerInvariant() switch
    {
        "top" => -Top,
        "bottom" => Bottom,
        _ => 0,
    };
}

public static class WindowFrame
{
    // The target plate is 7px inside the 336px window on each side. The window rectangle itself stays put.
    private static readonly FrameInset Target = new(7, 0, 7, 0);

    public static FrameInset Paint(string? name)
    {
        if (IsTarget(name))
        {
            return Target;
        }

        return FrameInset.None;
    }

    /// <summary>Edge-anchor correction. Spell, target, and extended-target windows keep the saved rectangle.</summary>
    public static FrameInset Anchor(string? name) => FrameInset.None;

    private static bool IsTarget(string? name)
        => name == "TargetWindow" || (name?.StartsWith("TargetWindow_", StringComparison.Ordinal) ?? false);
}
