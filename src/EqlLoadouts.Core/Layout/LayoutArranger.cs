namespace EqlLoadouts.Core.Layout;

public enum ArrangeAction
{
    AlignLeft,
    AlignCenter,
    AlignRight,
    AlignTop,
    AlignMiddle,
    AlignBottom,
    StackRight,
    StackLeft,
    StackDown,
    StackUp,
    CenterOnScreenHorizontally,
    CenterOnScreenVertically,
    MatchWidth,
    MatchHeight,
}

public static class LayoutArranger
{
    // ordered[0] is the anchor and is returned unchanged for Align, Stack, and Size. Align matches every window to the anchor;
    // Stack places each window against the one before it in the list and leaves the other axis alone.
    // MatchWidth and MatchHeight copy that side of the anchor and leave the top-left where it is.
    // Center on screen moves every window, including the anchor, so that axis sits in the middle of the screen.
    public static IReadOnlyList<ScreenRect> Arrange(
        IReadOnlyList<ScreenRect> ordered,
        ArrangeAction action,
        double screenWidth = 0,
        double screenHeight = 0,
        double horizontalSpacing = 0,
        double verticalSpacing = 0)
    {
        ArgumentNullException.ThrowIfNull(ordered);
        var result = new ScreenRect[ordered.Count];
        if (ordered.Count == 0)
        {
            return result;
        }

        var anchor = ordered[0];
        var centersOnScreen = action is ArrangeAction.CenterOnScreenHorizontally or ArrangeAction.CenterOnScreenVertically;
        if (action is ArrangeAction.StackRight or ArrangeAction.StackLeft)
        {
            anchor = anchor with { Left = Math.Round(anchor.Left, MidpointRounding.AwayFromZero) };
        }
        else if (action is ArrangeAction.StackDown or ArrangeAction.StackUp)
        {
            anchor = anchor with { Top = Math.Round(anchor.Top, MidpointRounding.AwayFromZero) };
        }

        if (!centersOnScreen)
        {
            result[0] = anchor;
        }

        for (var index = centersOnScreen ? 0 : 1; index < ordered.Count; index++)
        {
            var rect = ordered[index];
            var previous = index == 0 ? rect : result[index - 1];
            result[index] = action switch
            {
                ArrangeAction.AlignLeft => rect with { Left = anchor.Left },
                ArrangeAction.AlignCenter => rect with { Left = anchor.Left + (anchor.Width - rect.Width) / 2d },
                ArrangeAction.AlignRight => rect with { Left = anchor.Left + anchor.Width - rect.Width },
                ArrangeAction.AlignTop => rect with { Top = anchor.Top },
                ArrangeAction.AlignMiddle => rect with { Top = anchor.Top + (anchor.Height - rect.Height) / 2d },
                ArrangeAction.AlignBottom => rect with { Top = anchor.Top + anchor.Height - rect.Height },
                ArrangeAction.StackRight => rect with { Left = previous.Left + previous.Width + horizontalSpacing },
                ArrangeAction.StackLeft => rect with { Left = previous.Left - rect.Width - horizontalSpacing },
                ArrangeAction.StackDown => rect with { Top = previous.Top + previous.Height + verticalSpacing },
                ArrangeAction.StackUp => rect with { Top = previous.Top - rect.Height - verticalSpacing },
                ArrangeAction.CenterOnScreenHorizontally => rect with { Left = (screenWidth - rect.Width) / 2d },
                ArrangeAction.CenterOnScreenVertically => rect with { Top = (screenHeight - rect.Height) / 2d },
                ArrangeAction.MatchWidth => rect with { Width = anchor.Width },
                ArrangeAction.MatchHeight => rect with { Height = anchor.Height },
                _ => rect,
            };
        }

        return result;
    }

    /// <summary>
    /// Selection order is not a position. Stack Up was reversing a column when the bottom window
    /// was selected first and the others were selected from top to bottom. Keep the anchor first
    /// and arrange the rest by where they already sit, so the stack direction does not swap them.
    /// </summary>
    public static int[] StackOrder(IReadOnlyList<ScreenRect> rects, ArrangeAction action)
    {
        ArgumentNullException.ThrowIfNull(rects);
        var order = new int[rects.Count];
        if (rects.Count == 0)
        {
            return order;
        }

        var vertical = action is ArrangeAction.StackUp or ArrangeAction.StackDown;
        var nearestLast = action is ArrangeAction.StackUp or ArrangeAction.StackLeft;
        var rest = Enumerable.Range(1, rects.Count - 1)
            .OrderBy(index => vertical ? rects[index].Top : rects[index].Left)
            .ThenBy(index => vertical ? rects[index].Left : rects[index].Top)
            .ThenBy(index => index)
            .ToList();
        if (nearestLast)
        {
            rest.Reverse();
        }

        order[0] = 0;
        for (var index = 0; index < rest.Count; index++)
        {
            order[index + 1] = rest[index];
        }

        return order;
    }

    public static bool IsOffScreen(ScreenRect rect, double screenWidth, double screenHeight)
        => rect.Left < 0 || rect.Top < 0 || rect.Left + rect.Width > screenWidth || rect.Top + rect.Height > screenHeight;
}
