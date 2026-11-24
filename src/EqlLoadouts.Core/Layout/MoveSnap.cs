namespace EqlLoadouts.Core.Layout;

/// <summary>
/// Pulls a moving rectangle onto nearby edges, centers, and equal gaps.
/// The rectangles are the visible frames. A stored window inset stays outside this.
/// </summary>
public static class MoveSnap
{
    public readonly record struct Line(double X1, double Y1, double X2, double Y2);

    public readonly record struct Adjustment(double Left, double Top, bool SnappedX, bool SnappedY, IReadOnlyList<Line> Guides);

    /// <summary>
    /// <paramref name="moving"/> is where the pointer has the frame. <paramref name="others"/> stay put.
    /// <paramref name="threshold"/> is the farthest pull, in the same units as the rectangles.
    /// </summary>
    public static Adjustment Snap(ScreenRect moving, IReadOnlyList<ScreenRect> others, double threshold)
    {
        if (threshold <= 0 || others.Count == 0)
        {
            return new Adjustment(moving.Left, moving.Top, false, false, []);
        }

        var horizontal = Horizontal(moving);
        var vertical = Vertical(moving);
        var across = new List<Span>(others.Count);
        var down = new List<Span>(others.Count);
        foreach (var other in others)
        {
            if (other.Width <= 0 || other.Height <= 0)
            {
                continue;
            }

            across.Add(Horizontal(other));
            down.Add(Vertical(other));
        }

        var xOptions = new List<Option>();
        var yOptions = new List<Option>();
        AddAlignment(horizontal, across, threshold, alongX: true, xOptions);
        AddAlignment(vertical, down, threshold, alongX: false, yOptions);
        AddSpacing(horizontal, across, threshold, alongX: true, xOptions);
        AddSpacing(vertical, down, threshold, alongX: false, yOptions);

        var x = Best(xOptions);
        var y = Best(yOptions);
        var guides = new List<Line>();
        if (x is not null)
        {
            guides.AddRange(x.Guides);
        }

        if (y is not null)
        {
            guides.AddRange(y.Guides);
        }

        return new Adjustment(moving.Left + (x?.Delta ?? 0), moving.Top + (y?.Delta ?? 0), x is not null, y is not null, guides);
    }

    public readonly record struct ResizeResult(ScreenRect Rect, bool SnappedX, bool SnappedY, IReadOnlyList<Line> Guides);

    /// <summary>
    /// Pulls the edges being resized onto nearby edges, centers, and equal gaps.
    /// The opposite edge stays put, so the size changes. A false edge is left alone.
    /// </summary>
    public static ResizeResult SnapResize(
        ScreenRect moving,
        IReadOnlyList<ScreenRect> others,
        double threshold,
        bool leftEdge,
        bool rightEdge,
        bool topEdge,
        bool bottomEdge,
        double minWidth = 0,
        double minHeight = 0)
    {
        var unchanged = new ResizeResult(moving, false, false, []);
        if (threshold <= 0 || others.Count == 0)
        {
            return unchanged;
        }

        var across = new List<Span>(others.Count);
        var down = new List<Span>(others.Count);
        foreach (var other in others)
        {
            if (other.Width <= 0 || other.Height <= 0)
            {
                continue;
            }

            across.Add(Horizontal(other));
            down.Add(Vertical(other));
        }

        var left = moving.Left;
        var top = moving.Top;
        var width = moving.Width;
        var height = moving.Height;
        var guides = new List<Line>();
        var snappedX = ApplyAxis(
            Horizontal(moving),
            across,
            threshold,
            alongX: true,
            moveStart: leftEdge && !rightEdge,
            moveEnd: rightEdge && !leftEdge,
            minSize: minWidth,
            ref left,
            ref width,
            guides);
        var snappedY = ApplyAxis(
            Vertical(moving),
            down,
            threshold,
            alongX: false,
            moveStart: topEdge && !bottomEdge,
            moveEnd: bottomEdge && !topEdge,
            minSize: minHeight,
            ref top,
            ref height,
            guides);
        return new ResizeResult(new ScreenRect(left, top, width, height), snappedX, snappedY, guides);
    }

    private static bool ApplyAxis(
        Span moving,
        List<Span> others,
        double threshold,
        bool alongX,
        bool moveStart,
        bool moveEnd,
        double minSize,
        ref double start,
        ref double size,
        List<Line> guides)
    {
        if (others.Count == 0 || moveStart == moveEnd)
        {
            return false;
        }

        var options = new List<Option>();
        AddEdgeAlignment(moving, others, threshold, alongX, moveEnd, minSize, options);
        AddEdgeSpacing(moving, others, threshold, alongX, moveEnd, minSize, options);
        var best = Best(options);
        if (best is null)
        {
            return false;
        }

        if (moveEnd)
        {
            size += best.Delta;
        }
        else
        {
            start += best.Delta;
            size -= best.Delta;
        }

        guides.AddRange(best.Guides);
        return true;
    }

    private static void AddAlignment(Span moving, List<Span> others, double threshold, bool alongX, List<Option> options)
    {
        foreach (var other in others)
        {
            AddOption(options, other.Start - moving.Start, Guide(other.Start, moving, other, alongX, threshold), threshold);
            AddOption(options, other.End - moving.End, Guide(other.End, moving, other, alongX, threshold), threshold);
            AddOption(options, other.End - moving.Start, Guide(other.End, moving, other, alongX, threshold), threshold);
            AddOption(options, other.Start - moving.End, Guide(other.Start, moving, other, alongX, threshold), threshold);
            AddOption(options, other.Center - moving.Center, Guide(other.Center, moving, other, alongX, threshold), threshold);
        }
    }

    private static void AddEdgeAlignment(Span moving, List<Span> others, double threshold, bool alongX, bool moveEnd, double minSize, List<Option> options)
    {
        var point = moveEnd ? moving.End : moving.Start;
        foreach (var other in others)
        {
            AddEdgeOption(options, other.Start - point, moving, moveEnd, minSize, Guide(other.Start, moving, other, alongX, threshold), threshold);
            AddEdgeOption(options, other.End - point, moving, moveEnd, minSize, Guide(other.End, moving, other, alongX, threshold), threshold);
            AddEdgeOption(options, other.Center - point, moving, moveEnd, minSize, Guide(other.Center, moving, other, alongX, threshold), threshold);
        }
    }

    private static void AddEdgeOption(List<Option> options, double delta, Span moving, bool moveEnd, double minSize, Line guide, double threshold)
    {
        var size = moveEnd ? moving.Size + delta : moving.Size - delta;
        if (size < minSize)
        {
            return;
        }

        AddOption(options, delta, guide, threshold);
    }

    private static void AddEdgeSpacing(Span moving, List<Span> others, double threshold, bool alongX, bool moveEnd, double minSize, List<Option> options)
    {
        var band = new List<Span>();
        foreach (var other in others)
        {
            if (other.CrossEnd > moving.CrossStart && other.CrossStart < moving.CrossEnd)
            {
                band.Add(other);
            }
        }

        if (band.Count == 0)
        {
            return;
        }

        band.Sort((left, right) => left.Start.CompareTo(right.Start));
        var cross = (moving.CrossStart + moving.CrossEnd) / 2;
        if (moveEnd)
        {
            Span? after = null;
            foreach (var other in band)
            {
                if (other.Start >= moving.End - threshold && (after is null || other.Start < after.Value.Start))
                {
                    after = other;
                }
            }

            if (after is not { } next)
            {
                return;
            }

            Span? later = null;
            foreach (var other in band)
            {
                if (other.Start >= next.End - 0.01 && (later is null || other.Start < later.Value.Start))
                {
                    later = other;
                }
            }

            if (later is { } beyond)
            {
                var gap = beyond.Start - next.End;
                AddMatchedGap(options, moving, true, minSize, next.Start - gap, next.Start, next.End, beyond.Start, cross, alongX, threshold);
            }

            Span? before = null;
            foreach (var other in band)
            {
                if (other.End <= moving.Start + threshold && (before is null || other.End > before.Value.End))
                {
                    before = other;
                }
            }

            if (before is { } anchored)
            {
                var gap = moving.Start - anchored.End;
                AddMatchedGap(options, moving, true, minSize, next.Start - gap, next.Start, anchored.End, moving.Start, cross, alongX, threshold);
            }

            return;
        }

        Span? earlier = null;
        foreach (var other in band)
        {
            if (other.End <= moving.Start + threshold && (earlier is null || other.End > earlier.Value.End))
            {
                earlier = other;
            }
        }

        if (earlier is not { } neighbor)
        {
            return;
        }

        Span? further = null;
        foreach (var other in band)
        {
            if (other.End <= neighbor.Start + 0.01 && (further is null || other.End > further.Value.End))
            {
                further = other;
            }
        }

        if (further is { } earlierNeighbor)
        {
            var gap = neighbor.Start - earlierNeighbor.End;
            AddMatchedGap(options, moving, false, minSize, neighbor.End + gap, neighbor.End, earlierNeighbor.End, neighbor.Start, cross, alongX, threshold);
        }

        Span? afterStart = null;
        foreach (var other in band)
        {
            if (other.Start >= moving.End - threshold && (afterStart is null || other.Start < afterStart.Value.Start))
            {
                afterStart = other;
            }
        }

        if (afterStart is { } following)
        {
            var gap = following.Start - moving.End;
            AddMatchedGap(options, moving, false, minSize, neighbor.End + gap, neighbor.End, moving.End, following.Start, cross, alongX, threshold);
        }
    }

    private static void AddMatchedGap(
        List<Option> options,
        Span moving,
        bool moveEnd,
        double minSize,
        double target,
        double targetTo,
        double referenceFrom,
        double referenceTo,
        double cross,
        bool alongX,
        double threshold)
    {
        if (referenceTo < referenceFrom)
        {
            return;
        }

        var delta = moveEnd ? target - moving.End : target - moving.Start;
        var size = moveEnd ? moving.Size + delta : moving.Size - delta;
        if (size < minSize)
        {
            return;
        }

        var guides = new List<Line>();
        AddGap(guides, moveEnd ? target : targetTo, moveEnd ? targetTo : target, cross, alongX, threshold);
        AddGap(guides, referenceFrom, referenceTo, cross, alongX, threshold);
        AddOption(options, delta, guides, threshold);
    }

    private static void AddSpacing(Span moving, List<Span> others, double threshold, bool alongX, List<Option> options)
    {
        var band = new List<Span>();
        foreach (var other in others)
        {
            if (other.CrossEnd > moving.CrossStart && other.CrossStart < moving.CrossEnd)
            {
                band.Add(other);
            }
        }

        if (band.Count == 0)
        {
            return;
        }

        band.Sort((left, right) => left.Start.CompareTo(right.Start));
        Span? before = null;
        Span? after = null;
        foreach (var other in band)
        {
            if (other.End <= moving.Start + threshold && (before is null || other.End > before.Value.End))
            {
                before = other;
            }

            if (other.Start >= moving.End - threshold && (after is null || other.Start < after.Value.Start))
            {
                after = other;
            }
        }

        var cross = (moving.CrossStart + moving.CrossEnd) / 2;
        if (before is { } left && after is { } right && left.End <= right.Start)
        {
            var inner = right.Start - left.End - moving.Size;
            if (inner >= -threshold)
            {
                var target = left.End + (inner / 2);
                var guides = new List<Line>();
                AddGap(guides, left.End, target, cross, alongX, threshold);
                AddGap(guides, target + moving.Size, right.Start, cross, alongX, threshold);
                AddOption(options, target - moving.Start, guides, threshold);
            }
        }

        if (before is { } neighbor)
        {
            Span? earlier = null;
            foreach (var other in band)
            {
                if (other.End <= neighbor.Start + 0.01 && (earlier is null || other.End > earlier.Value.End))
                {
                    earlier = other;
                }
            }

            if (earlier is { } prior)
            {
                var gap = neighbor.Start - prior.End;
                if (gap >= 0)
                {
                    var target = neighbor.End + gap;
                    var guides = new List<Line>();
                    AddGap(guides, prior.End, neighbor.Start, cross, alongX, threshold);
                    AddGap(guides, neighbor.End, target, cross, alongX, threshold);
                    AddOption(options, target - moving.Start, guides, threshold);
                }
            }
        }

        if (after is { } next)
        {
            Span? later = null;
            foreach (var other in band)
            {
                if (other.Start >= next.End - 0.01 && (later is null || other.Start < later.Value.Start))
                {
                    later = other;
                }
            }

            if (later is { } beyond)
            {
                var gap = beyond.Start - next.End;
                if (gap >= 0)
                {
                    var target = next.Start - gap - moving.Size;
                    var guides = new List<Line>();
                    AddGap(guides, next.End, beyond.Start, cross, alongX, threshold);
                    AddGap(guides, target + moving.Size, next.Start, cross, alongX, threshold);
                    AddOption(options, target - moving.Start, guides, threshold);
                }
            }
        }
    }

    private static Line Guide(double position, Span moving, Span other, bool alongX, double pad)
    {
        var start = Math.Min(moving.CrossStart, other.CrossStart) - pad;
        var end = Math.Max(moving.CrossEnd, other.CrossEnd) + pad;
        return alongX ? new Line(position, start, position, end) : new Line(start, position, end, position);
    }

    private static void AddGap(List<Line> lines, double start, double end, double cross, bool alongX, double tick)
    {
        if (end < start)
        {
            (start, end) = (end, start);
        }

        if (end - start < 0.5)
        {
            return;
        }

        var half = Math.Min(tick, (end - start) / 2);
        if (alongX)
        {
            lines.Add(new Line(start, cross, end, cross));
            lines.Add(new Line(start, cross - half, start, cross + half));
            lines.Add(new Line(end, cross - half, end, cross + half));
            return;
        }

        lines.Add(new Line(cross, start, cross, end));
        lines.Add(new Line(cross - half, start, cross + half, start));
        lines.Add(new Line(cross - half, end, cross + half, end));
    }

    private static void AddOption(List<Option> options, double delta, Line guide, double threshold)
        => AddOption(options, delta, [guide], threshold);

    private static void AddOption(List<Option> options, double delta, List<Line> guides, double threshold)
    {
        if (guides.Count == 0 || Math.Abs(delta) > threshold)
        {
            return;
        }

        foreach (var option in options)
        {
            if (Math.Abs(option.Delta - delta) < 0.01)
            {
                option.Guides.AddRange(guides);
                return;
            }
        }

        options.Add(new Option(delta, guides));
    }

    private static Option? Best(List<Option> options)
    {
        Option? best = null;
        foreach (var option in options)
        {
            if (best is null || Math.Abs(option.Delta) < Math.Abs(best.Delta) - 0.001)
            {
                best = option;
            }
        }

        return best;
    }

    private static Span Horizontal(ScreenRect rect) => new(rect.Left, rect.Left + rect.Width, rect.Top, rect.Top + rect.Height);

    private static Span Vertical(ScreenRect rect) => new(rect.Top, rect.Top + rect.Height, rect.Left, rect.Left + rect.Width);

    private readonly record struct Span(double Start, double End, double CrossStart, double CrossEnd)
    {
        public double Size => End - Start;

        public double Center => Start + (Size / 2);
    }

    private sealed class Option
    {
        public Option(double delta, List<Line> guides)
        {
            Delta = delta;
            Guides = guides;
        }

        public double Delta { get; }

        public List<Line> Guides { get; }
    }
}
