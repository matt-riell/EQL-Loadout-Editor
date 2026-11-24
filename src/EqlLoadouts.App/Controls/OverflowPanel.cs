using System.Windows;
using System.Windows.Controls;

namespace EqlLoadouts.App.Controls;

/// <summary>
/// Lays children out in a row and hides those that do not fit, raising <see cref="OverflowChangedEvent"/>
/// so the host can offer the hidden ones elsewhere.
/// </summary>
public sealed class OverflowPanel : Panel
{
    public static readonly RoutedEvent OverflowChangedEvent = EventManager.RegisterRoutedEvent(
        "OverflowChanged", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(OverflowPanel));

    private int _fitCount;
    private int _reportedCount = -1;

    public int FitCount => _fitCount;

    public static void AddOverflowChangedHandler(DependencyObject element, RoutedEventHandler handler)
    {
        ((UIElement)element).AddHandler(OverflowChangedEvent, handler);
    }

    public static void RemoveOverflowChangedHandler(DependencyObject element, RoutedEventHandler handler)
    {
        ((UIElement)element).RemoveHandler(OverflowChangedEvent, handler);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = 0d;
        var height = 0d;
        var fit = 0;
        var full = false;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            var desired = child.DesiredSize;
            if (!full && width + desired.Width <= availableSize.Width + 0.5)
            {
                width += desired.Width;
                height = Math.Max(height, desired.Height);
                fit++;
            }
            else
            {
                full = true;
            }
        }

        _fitCount = fit;
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var x = 0d;
        for (var index = 0; index < InternalChildren.Count; index++)
        {
            var child = InternalChildren[index];
            if (index < _fitCount)
            {
                child.Arrange(new Rect(x, 0, child.DesiredSize.Width, finalSize.Height));
                x += child.DesiredSize.Width;
                child.IsHitTestVisible = true;
            }
            else
            {
                child.Arrange(new Rect(0, 0, 0, 0));
                child.IsHitTestVisible = false;
            }
        }

        if (_reportedCount != _fitCount)
        {
            _reportedCount = _fitCount;
            RaiseEvent(new RoutedEventArgs(OverflowChangedEvent, this));
        }

        return finalSize;
    }
}
