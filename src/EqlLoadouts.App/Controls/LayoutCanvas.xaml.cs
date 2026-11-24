using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using EqlLoadouts.App.Models;
using EqlLoadouts.Core.Content;
using EqlLoadouts.Core.Layout;

namespace EqlLoadouts.App.Controls;

public partial class LayoutCanvas : UserControl
{
    private const double MinimumSize = 20;
    private const double EdgeGrip = 6;
    private const double HandleLong = 18;
    private const double HandleShort = 6;
    private const double HandleCorner = 9;
    private const double CornerSize = 8;
    private const double BadgeSize = 28;
    private const double ScreenBorderThickness = 3;
    private const double SnapScreenPixels = 8;
    private const string SnapGuideTag = "snap-guide";

    private static readonly Color FallbackSelected = Color.FromRgb(56, 189, 248);
    private static readonly Color FallbackAnchor = Color.FromRgb(245, 158, 11);
    private static readonly object SlotsTag = new();
    private static readonly object ButtonsTag = new();
    private static readonly object ChatTabsTag = new();
    private static readonly Brush SlotEmptyFillDark = Frozen(Color.FromArgb(110, 0, 0, 0));
    private static readonly Brush SlotUsedFillDark = Frozen(Color.FromArgb(55, 255, 255, 255));
    private static readonly Brush SlotStrokeDark = Frozen(Color.FromArgb(70, 255, 255, 255));
    private static readonly Brush TitleBarFillDark = Frozen(Color.FromArgb(120, 0, 0, 0));
    private static readonly Brush SlotEmptyFillLight = Frozen(Color.FromRgb(0xD2, 0xD7, 0xDF));
    private static readonly Brush SlotUsedFillLight = Frozen(Color.FromRgb(0x6B, 0x73, 0x80));
    private static readonly Brush SlotStrokeLight = Frozen(Color.FromRgb(0x24, 0x30, 0x40));
    private static readonly Brush TitleBarInventoryLight = Frozen(Color.FromArgb(80, 120, 80, 30));
    private static readonly Brush TitleBarBankLight = Frozen(Color.FromArgb(70, 24, 50, 90));
    private static readonly Brush TitleBarWindowLight = Frozen(Color.FromArgb(70, 44, 53, 66));
    private static readonly Brush GemFilledDark = Frozen(Color.FromArgb(220, 92, 18, 24));
    private static readonly Brush GemEmptyDark = Frozen(Color.FromArgb(140, 40, 12, 16));
    private static readonly Brush GemFilledLight = Frozen(Color.FromRgb(0x8C, 0x1C, 0x24));
    private static readonly Brush GemEmptyLight = Frozen(Color.FromRgb(0x4E, 0x2A, 0x2E));
    private static readonly Brush GemTextLight = Frozen(Color.FromRgb(0xF7, 0xF4, 0xF4));
    private static readonly Brush ButtonFaceLight = Frozen(Color.FromRgb(0xD9, 0xDE, 0xE7));
    private static readonly Brush ButtonEdgeLight = Frozen(Color.FromRgb(0x2C, 0x35, 0x42));
    private static readonly Brush ButtonEdgeDark = Frozen(Color.FromRgb(240, 240, 240));
    private static readonly Brush ChatTabLineDark = Frozen(Color.FromArgb(0xB8, 0xC5, 0xCD, 0xD8));
    private static readonly Brush ChatTabLineLight = Frozen(Color.FromRgb(0x2C, 0x35, 0x42));
    private static readonly Typeface ChatTabTypeface = new("Segoe UI");
    private static readonly Pen GridPenDark = FrozenPen(Color.FromArgb(70, 150, 170, 190));
    private static readonly Pen GridPenLight = FrozenPen(Color.FromArgb(160, 90, 102, 118));
    private static readonly Color ScreenBorderFallbackDark = Color.FromRgb(0xB4, 0xC0, 0xCC);
    private static readonly Color ScreenBorderFallbackLight = Color.FromRgb(0x2A, 0x38, 0x48);
    private static readonly Brush SnapGuideBrush = Frozen(Color.FromRgb(0xE2, 0x3B, 0x8C));

    public static readonly DependencyProperty WindowsProperty = DependencyProperty.Register(
        nameof(Windows), typeof(IReadOnlyList<LayoutWindow>), typeof(LayoutCanvas),
        new PropertyMetadata(null, (d, _) => ((LayoutCanvas)d).Redraw()));

    public static readonly DependencyProperty SelectedNamesProperty = DependencyProperty.Register(
        nameof(SelectedNames), typeof(string), typeof(LayoutCanvas),
        new PropertyMetadata(string.Empty, (d, _) => ((LayoutCanvas)d).Redraw()));

    public static readonly DependencyProperty SnapSizeProperty = DependencyProperty.Register(
        nameof(SnapSize), typeof(double), typeof(LayoutCanvas),
        new PropertyMetadata(0d, (d, _) => ((LayoutCanvas)d).Redraw()));

    public static readonly DependencyProperty LayoutVersionProperty = DependencyProperty.Register(
        nameof(LayoutVersion), typeof(int), typeof(LayoutCanvas),
        new PropertyMetadata(0, (d, _) => ((LayoutCanvas)d).Redraw()));

    public static readonly DependencyProperty ScreenWidthProperty = DependencyProperty.Register(
        nameof(ScreenWidth), typeof(double), typeof(LayoutCanvas), new PropertyMetadata(2560d));

    public static readonly DependencyProperty ScreenHeightProperty = DependencyProperty.Register(
        nameof(ScreenHeight), typeof(double), typeof(LayoutCanvas), new PropertyMetadata(1440d));

    public static readonly DependencyProperty IsFullScreenProperty = DependencyProperty.Register(
        nameof(IsFullScreen), typeof(bool), typeof(LayoutCanvas),
        new PropertyMetadata(false, (d, e) => ((LayoutCanvas)d).UpdateFullScreenButton((bool)e.NewValue)));

    private const double ZoomStep = 1.2;
    private const double MinZoomFactor = 0.5;
    private const double MaxScale = 8;
    private const double FrameMargin = 26;
    private const double ClickSlop = 4;

    private DragState? _drag;
    private List<string> _selected = [];
    private double _fitScale = 1;
    private double _zoomFactor = 1;
    private Point? _marqueeStart;
    private bool _marqueeExtend;
    private Rectangle? _marquee;
    private Border? _screenBorder;
    private Point? _panStart;
    private Vector _panOrigin;
    private MouseButton _panButton;
    private bool _panMoved;
    private bool _suppressMenu;
    private bool _openingItemMenu;

    private Func<int, ImageSource?>? _itemIcon;
    private Func<int, ImageSource?>? _spellIcon;
    private Func<ImageSource?>? _bookIcon;

    /// <summary>Resolves an item icon id, such as 585, to the picture from the game's dragitem sheets.</summary>
    public Func<int, ImageSource?>? ItemIcon
    {
        get => _itemIcon;
        set => SetIconSource(ref _itemIcon, value);
    }

    /// <summary>Resolves a spell gem icon id to the picture from the gemicons sheets.</summary>
    public Func<int, ImageSource?>? SpellIcon
    {
        get => _spellIcon;
        set => SetIconSource(ref _spellIcon, value);
    }

    /// <summary>The spell-bar book button, cropped from the game's window pieces.</summary>
    public Func<ImageSource?>? BookIcon
    {
        get => _bookIcon;
        set => SetIconSource(ref _bookIcon, value);
    }

    private void SetIconSource<T>(ref T field, T value)
    {
        if (Equals(field, value))
        {
            return;
        }

        field = value;
        Redraw();
    }

    public event EventHandler<SpellBookCommandEventArgs>? SpellBookCommandChosen;

    public LayoutCanvas()
    {
        InitializeComponent();
        Scroller.AddHandler(MouseLeftButtonDownEvent, new MouseButtonEventHandler(Scroller_MouseLeftButtonDown), handledEventsToo: true);
        Scroller.AddHandler(MouseMoveEvent, new MouseEventHandler(Scroller_MouseMove), handledEventsToo: true);
        Scroller.AddHandler(MouseLeftButtonUpEvent, new MouseButtonEventHandler(Scroller_MouseLeftButtonUp), handledEventsToo: true);
        Scroller.PreviewMouseDown += Scroller_PreviewMouseDown;
        Scroller.PreviewMouseUp += Scroller_PreviewMouseUp;
        Scroller.LostMouseCapture += (_, _) => CancelPointerGestures();
        ContextMenuOpening += LayoutCanvas_ContextMenuOpening;
        Loaded += OnCanvasLoaded;
        Unloaded += OnCanvasUnloaded;
    }

    private void OnCanvasLoaded(object sender, RoutedEventArgs e)
    {
        ThemeManager.ThemeChanged -= OnThemeChanged;
        ThemeManager.ThemeChanged += OnThemeChanged;
    }

    private void OnCanvasUnloaded(object sender, RoutedEventArgs e)
    {
        ThemeManager.ThemeChanged -= OnThemeChanged;
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.CheckAccess())
        {
            Redraw();
            return;
        }

        Dispatcher.InvokeAsync(Redraw);
    }

    public event EventHandler<LayoutEditedEventArgs>? LayoutEdited;

    public event EventHandler<CanvasSelectionEventArgs>? SelectionRequested;

    public event EventHandler? FullScreenToggled;

    public bool IsFullScreen
    {
        get => (bool)GetValue(IsFullScreenProperty);
        set => SetValue(IsFullScreenProperty, value);
    }

    private double Scale => Zoom.ScaleX;

    public IReadOnlyList<LayoutWindow>? Windows
    {
        get => (IReadOnlyList<LayoutWindow>?)GetValue(WindowsProperty);
        set => SetValue(WindowsProperty, value);
    }

    public string SelectedNames
    {
        get => (string)GetValue(SelectedNamesProperty);
        set => SetValue(SelectedNamesProperty, value);
    }

    public double SnapSize
    {
        get => (double)GetValue(SnapSizeProperty);
        set => SetValue(SnapSizeProperty, value);
    }

    public int LayoutVersion
    {
        get => (int)GetValue(LayoutVersionProperty);
        set => SetValue(LayoutVersionProperty, value);
    }

    public double ScreenWidth
    {
        get => (double)GetValue(ScreenWidthProperty);
        set => SetValue(ScreenWidthProperty, value);
    }

    public double ScreenHeight
    {
        get => (double)GetValue(ScreenHeightProperty);
        set => SetValue(ScreenHeightProperty, value);
    }

    private void Redraw()
    {
        if (Screen is null)
        {
            return;
        }

        Screen.Children.Clear();
        ClearSnapGuides();
        var width = ScreenWidth <= 0 ? 2560 : ScreenWidth;
        var height = ScreenHeight <= 0 ? 1440 : ScreenHeight;
        var resized = Screen.Width != width || Screen.Height != height;
        Screen.Width = width;
        Screen.Height = height;
        if (resized)
        {
            UpdateFit();
        }

        _selected = SelectedNames.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        DrawGrid();
        DrawScreenBorder();
        if (Windows is null)
        {
            return;
        }

        var primary = _selected.FirstOrDefault();
        foreach (var window in Windows
            .OrderBy(window => window.Name == primary)
            .ThenBy(window => _selected.Contains(window.Name, StringComparer.Ordinal))
            .ThenBy(window => window.Category switch
            {
                "inventory" => 1,
                "bank" => 2,
                _ => 0,
            }))
        {
            Screen.Children.Add(CreateWindow(window));
        }
    }

    private void DrawGrid()
    {
        if (SnapSize < 4 || Screen.Width <= 0 || Screen.Height <= 0)
        {
            return;
        }

        if (Screen.Width / SnapSize > 500 || Screen.Height / SnapSize > 500)
        {
            return;
        }

        var pen = ThemeManager.IsDark ? GridPenDark : GridPenLight;
        var geometry = new GeometryGroup();
        geometry.Children.Add(new LineGeometry(new Point(0, 0), new Point(SnapSize, 0)));
        geometry.Children.Add(new LineGeometry(new Point(0, 0), new Point(0, SnapSize)));
        geometry.Freeze();
        var brush = new DrawingBrush
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, SnapSize, SnapSize),
            ViewportUnits = BrushMappingMode.Absolute,
            Drawing = new GeometryDrawing(null, pen, geometry),
        };
        brush.Freeze();
        Screen.Children.Add(new Rectangle
        {
            Width = Screen.Width,
            Height = Screen.Height,
            IsHitTestVisible = false,
            Fill = brush,
        });
    }

    private void DrawScreenBorder()
    {
        if (_screenBorder is not null)
        {
            Surface.Children.Remove(_screenBorder);
        }

        var thickness = ScreenBorderThickness;
        var frame = new Border
        {
            Width = Screen.Width + (thickness * 2),
            Height = Screen.Height + (thickness * 2),
            Margin = new Thickness(-thickness),
            BorderThickness = new Thickness(thickness),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
            SnapsToDevicePixels = true,
        };
        UseBrush(
            frame,
            Border.BorderBrushProperty,
            "CanvasScreenBorderBrush",
            ThemeManager.IsDark ? ScreenBorderFallbackDark : ScreenBorderFallbackLight);
        _screenBorder = frame;
        Surface.Children.Insert(0, frame);
    }

    private FrameworkElement CreateWindow(LayoutWindow window)
    {
        var primary = _selected.FirstOrDefault();
        var selected = _selected.Contains(window.Name, StringComparer.Ordinal);
        var isPrimary = selected && window.Name == primary;
        var root = new Grid
        {
            Width = Math.Max(MinimumSize, window.Width),
            Height = Math.Max(MinimumSize, window.Height),
            Tag = window,
            ToolTip = Tooltip(window, window.Width, window.Height),
        };
        var corner = new CornerRadius(Math.Min(CornerSize, Math.Min(root.Width, root.Height) / 4d));
        var fill = new Border { CornerRadius = corner };
        var stroke = new Border { CornerRadius = corner, IsHitTestVisible = false };
        var inventoryBag = window.IsInventoryBag;
        var showsSpells = window.IsSpellWindow;
        var showsChatTabs = window.ChatTabs.Count > 0;
        var hotbar = window.Buttons.Count > 0;
        var frame = inventoryBag ? FrameInset.Bag
            : hotbar ? HotbarLayout.Chrome(window.Width, window.Height)
            : WindowFrame.Paint(window.Name);
        // Bags, spell gems, the target plate, and hotbars paint their frame off the saved rectangle.
        if (!frame.IsEmpty)
        {
            var inset = new Thickness(frame.Left, frame.Top, frame.Right, frame.Bottom);
            fill.Margin = inset;
            stroke.Margin = inset;
        }

        root.Children.Add(fill);
        root.Children.Add(stroke);
        if (inventoryBag)
        {
            root.Children.Add(new Border
            {
                Height = BagGrid.TopInset - BagGrid.FrameInsetTop,
                Margin = new Thickness(BagGrid.FrameInsetLeft, BagGrid.FrameInsetTop, BagGrid.FrameInsetRight, 0),
                VerticalAlignment = VerticalAlignment.Top,
                CornerRadius = new CornerRadius(corner.TopLeft, corner.TopRight, 0, 0),
                Background = TitleBarBrush(window.Category),
                IsHitTestVisible = false,
            });
        }

        if (!showsSpells && !showsChatTabs && !hotbar)
        {
            var fontSize = inventoryBag ? 11 : window.Width < 80 ? 10 : 12;
            var label = new StackPanel
            {
                Margin = inventoryBag
                    ? new Thickness(BagGrid.LeftInset, BagGrid.FrameInsetTop + 1, BagGrid.LeftInset, 0)
                    : new Thickness(6, 3, 6, 3),
                IsHitTestVisible = false,
            };
            label.Children.Add(new TextBlock
            {
                Text = inventoryBag ? BagTitle(window) : string.IsNullOrEmpty(window.Label) ? window.Name : window.Label,
                FontSize = fontSize,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            if (window.Detail.Length > 0 && window.SlotCount == 0 && window.Height >= fontSize * 3)
            {
                label.Children.Add(new TextBlock
                {
                    Text = window.Detail,
                    FontSize = fontSize - 1,
                    Opacity = ThemeManager.IsDark ? 0.8 : 1,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                });
            }

            UseBrush(label, TextElement.ForegroundProperty, "CanvasLabelBrush", Colors.White);
            root.Children.Add(label);
        }

        UpdateSlots(root, window);
        ApplyStyle(fill, stroke, window, isPrimary, selected);
        if (showsChatTabs)
        {
            DrawChatTabs(root, window);
        }

        if (isPrimary)
        {
            AddAnchorBadge(root);
        }

        if (selected && window.Sizable)
        {
            AddHandles(root, window);
        }

        Canvas.SetLeft(root, window.Left);
        Canvas.SetTop(root, window.Top);
        root.MouseLeftButtonDown += (_, e) =>
        {
            var extend = (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0;
            BeginDrag(root, window, extend ? DragMode.Move : EdgeAt(window, e.GetPosition(root)) ?? DragMode.Move, e);
        };
        root.MouseRightButtonUp += (_, e) => OpenMenuFor(window, e);
        root.MouseMove += (_, e) =>
        {
            if (_drag is null)
            {
                var point = e.GetPosition(root);
                root.Cursor = EdgeAt(window, point) is { } edge
                    ? CursorFor(edge)
                    : inventoryBag && point.Y < BagGrid.TopInset ? Cursors.SizeAll : null;
            }

            ContinueDrag(root, e);
        };
        root.MouseLeftButtonUp += (_, e) => EndDrag(root, e);
        return root;
    }

    private const double ChatTabBand = 18;

    /// <summary>
    /// Draws one equal cell per tab across the full top of the window, under the window's top border.
    /// The open tab has no line under it. Stored width and height stay unchanged.
    /// </summary>
    private void DrawChatTabs(Grid root, LayoutWindow window)
    {
        var index = root.Children.Count;
        for (var i = 0; i < root.Children.Count; i++)
        {
            if (root.Children[i] is FrameworkElement existing && ReferenceEquals(existing.Tag, ChatTabsTag))
            {
                root.Children.RemoveAt(i);
                index = i;
                break;
            }
        }

        var tabs = window.ChatTabs;
        if (tabs.Count == 0 || root.Width < 1 || root.Height < 1)
        {
            return;
        }

        var selected = _selected.Contains(window.Name, StringComparer.Ordinal);
        var primary = selected && window.Name == _selected.FirstOrDefault();
        var frame = FrameThickness(primary, selected);
        var innerWidth = Math.Max(0, root.Width - (frame * 2));
        var band = Math.Min(ChatTabBand, Math.Max(0, root.Height - frame));
        if (innerWidth < 1 || band < 1)
        {
            return;
        }

        var dark = ThemeManager.IsDark;
        var line = dark ? ChatTabLineDark : ChatTabLineLight;
        var row = new Grid
        {
            Width = innerWidth,
            Height = band,
            Margin = new Thickness(frame, frame, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
            ClipToBounds = true,
            SnapsToDevicePixels = true,
            UseLayoutRounding = true,
            Opacity = PaintedOpacity(window.Hidden),
            Tag = ChatTabsTag,
        };
        var cellWidth = innerWidth / tabs.Count;
        var fontSize = band >= 16 ? 11 : Math.Max(8, band - 4);
        for (var i = 0; i < tabs.Count; i++)
        {
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var open = tabs[i].Active;
            var cell = new Border
            {
                BorderBrush = line,
                BorderThickness = new Thickness(0, 0, i < tabs.Count - 1 ? 1 : 0, open ? 0 : 1),
                Background = Brushes.Transparent,
                SnapsToDevicePixels = true,
                IsHitTestVisible = false,
                Child = ChatTabCaption(tabs[i].Name, fontSize, Math.Max(1, cellWidth - 4), dark),
            };
            Grid.SetColumn(cell, i);
            row.Children.Add(cell);
        }

        root.Children.Insert(index, row);
    }

    private TextBlock ChatTabCaption(string text, double fontSize, double maxWidth, bool dark)
    {
        var caption = new TextBlock
        {
            Text = text,
            FontFamily = ChatTabTypeface.FontFamily,
            FontSize = fontSize,
            LineHeight = fontSize,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = maxWidth,
            IsHitTestVisible = false,
        };
        UseBrush(
            caption,
            TextBlock.ForegroundProperty,
            "CanvasLabelBrush",
            dark ? Colors.White : Color.FromRgb(0x10, 0x12, 0x16));
        return caption;
    }

    private void UpdateSlots(Grid root, LayoutWindow window)
    {
        foreach (var old in root.Children.OfType<FrameworkElement>().Where(element => ReferenceEquals(element.Tag, SlotsTag) || ReferenceEquals(element.Tag, ButtonsTag)).ToList())
        {
            root.Children.Remove(old);
        }

        if (window.IsSpellWindow)
        {
            DrawSpellWindow(root, window);
        }

        if (window.SlotCount <= 0)
        {
            return;
        }

        if (window.Buttons.Count > 0)
        {
            DrawHotbar(root, window);
            return;
        }

        var grid = BagGrid.FromWidth(window.SlotCount, window.Width);
        var empty = new GeometryGroup();
        var used = new GeometryGroup();
        for (var index = 0; index < grid.Slots; index++)
        {
            var slot = grid.Slot(index);
            var geometry = new RectangleGeometry(new Rect(slot.Left, slot.Top, slot.Width, slot.Height), 3, 3);
            (window.UsedSlots.Contains(index) ? used : empty).Children.Add(geometry);
        }

        var opacity = PaintedOpacity(window.Hidden);
        var (emptyFill, usedFill, stroke) = SlotBrushes();
        root.Children.Insert(1, SlotPath(empty, emptyFill, stroke, opacity));
        root.Children.Insert(2, SlotPath(used, usedFill, stroke, opacity));
    }

    private const double SpellBookSize = 22;

    private const double SpellBookBand = 28;

    private void DrawSpellWindow(Grid root, LayoutWindow window)
    {
        var vertical = root.Height + 0.5 >= root.Width;
        var layer = new Canvas
        {
            IsHitTestVisible = false,
            Tag = ButtonsTag,
            ClipToBounds = true,
            Opacity = PaintedOpacity(window.Hidden),
            Margin = new Thickness(0),
            Width = root.Width,
            Height = root.Height,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        if (vertical)
        {
            DrawVerticalSpellGems(layer, window, layer.Width, layer.Height);
        }
        else
        {
            DrawHorizontalSpellGems(layer, window, layer.Width, layer.Height);
        }

        root.Children.Insert(1, layer);
        root.Children.Add(CreateSpellBook(window, vertical));
    }

    private void DrawVerticalSpellGems(Canvas layer, LayoutWindow window, double width, double height)
    {
        var gems = window.SpellGems;
        var count = Math.Max(1, gems.Count);
        var row = Math.Clamp((height - SpellBookBand) / count, 14, 32);
        var filled = ThemeManager.IsDark ? GemFilledDark : GemFilledLight;
        var empty = ThemeManager.IsDark ? GemEmptyDark : GemEmptyLight;
        for (var index = 0; index < gems.Count; index++)
        {
            var face = gems[index];
            var bar = new Border
            {
                Width = Math.Max(8, width - 2),
                Height = Math.Max(8, row - 1),
                Background = face.Name.Length > 0 ? filled : empty,
                IsHitTestVisible = false,
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(16, row)) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            AddGemIcon(grid, face, Math.Max(10, row - 6));
            var name = GemCaption(face.Name, Math.Clamp(row * 0.46, 9, 13));
            name.Margin = new Thickness(2, 0, 4, 0);
            Grid.SetColumn(name, 1);
            grid.Children.Add(name);
            var number = GemCaption(face.Number.ToString(CultureInfo.InvariantCulture), Math.Clamp(row * 0.4, 8, 12));
            number.Opacity = 0.85;
            number.Margin = new Thickness(0, 0, 4, 0);
            Grid.SetColumn(number, 2);
            grid.Children.Add(number);
            bar.Child = grid;
            Canvas.SetLeft(bar, 1);
            Canvas.SetTop(bar, 1 + (index * row));
            layer.Children.Add(bar);
        }
    }

    private void DrawHorizontalSpellGems(Canvas layer, LayoutWindow window, double width, double height)
    {
        var gems = window.SpellGems;
        var count = Math.Max(1, gems.Count);
        var gem = Math.Clamp(Math.Min(height - 4, (width - SpellBookBand - 4) / count), 12, 36);
        var top = Math.Max(1, (height - gem) / 2);
        for (var index = 0; index < gems.Count; index++)
        {
            var face = gems[index];
            var box = new Border
            {
                Width = gem,
                Height = gem,
                CornerRadius = new CornerRadius(2),
                BorderThickness = new Thickness(1),
                IsHitTestVisible = false,
            };
            if (ThemeManager.IsDark)
            {
                UseBrush(box, Border.BackgroundProperty, "SurfaceAlt", Color.FromRgb(28, 30, 36));
                UseBrush(box, Border.BorderBrushProperty, "BorderBrush", Color.FromArgb(80, 255, 255, 255));
            }
            else
            {
                box.Background = ButtonFaceLight;
                box.BorderBrush = ButtonEdgeLight;
            }
            if (face.Icon >= 0 && SpellIcon?.Invoke(face.Icon) is { } image)
            {
                box.Child = new Image
                {
                    Source = image,
                    Stretch = Stretch.Uniform,
                    Margin = new Thickness(2),
                    IsHitTestVisible = false,
                };
            }

            Canvas.SetLeft(box, 2 + (index * gem));
            Canvas.SetTop(box, top);
            layer.Children.Add(box);
        }
    }

    private void AddGemIcon(Grid grid, SpellGemFace face, double size)
    {
        if (face.Icon < 0 || SpellIcon?.Invoke(face.Icon) is not { } image)
        {
            return;
        }

        var picture = new Image
        {
            Source = image,
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
        Grid.SetColumn(picture, 0);
        grid.Children.Add(picture);
    }

    private TextBlock GemCaption(string text, double fontSize)
    {
        var caption = new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            IsHitTestVisible = false,
        };
        if (ThemeManager.IsDark)
        {
            UseBrush(caption, TextBlock.ForegroundProperty, "CanvasLabelBrush", Colors.White);
        }
        else
        {
            caption.Foreground = GemTextLight;
        }

        return caption;
    }

    private Border CreateSpellBook(LayoutWindow window, bool vertical)
    {
        var book = new Border
        {
            Width = SpellBookSize,
            Height = SpellBookSize,
            Tag = ButtonsTag,
            Opacity = PaintedOpacity(window.Hidden),
            Cursor = Cursors.Hand,
            ToolTip = "Right-click to load, save, or delete a spell set",
            HorizontalAlignment = vertical ? HorizontalAlignment.Left : HorizontalAlignment.Right,
            VerticalAlignment = vertical ? VerticalAlignment.Bottom : VerticalAlignment.Center,
            Margin = vertical ? new Thickness(13, 0, 0, 2) : new Thickness(0, 0, 2, 0),
        };
        if (BookIcon?.Invoke() is { } image)
        {
            book.Background = new ImageBrush(image) { Stretch = Stretch.Fill };
        }
        else
        {
            book.Background = Frozen(Color.FromRgb(92, 58, 32));
            book.Child = new Border
            {
                Margin = new Thickness(4, 3, 4, 3),
                Background = Brushes.AntiqueWhite,
                IsHitTestVisible = false,
            };
        }

        book.MouseLeftButtonDown += (_, e) => e.Handled = true;
        book.MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            OpenSpellBookMenu(book, window);
        };
        return book;
    }

    private void OpenSpellBookMenu(FrameworkElement book, LayoutWindow window)
    {
        var menu = new ContextMenu { PlacementTarget = book, Placement = PlacementMode.MousePoint };
        var load = new MenuItem { Header = "Load spell set" };
        foreach (var set in window.SpellSets)
        {
            AddSpellBookItem(load, window, set, SpellBookCommand.Load);
        }

        if (window.SpellSets.Count > 0)
        {
            load.Items.Add(new Separator());
        }

        var add = new MenuItem { Header = "Add New Set" };
        add.Click += (_, args) =>
        {
            args.Handled = true;
            SpellBookCommandChosen?.Invoke(this, new SpellBookCommandEventArgs(window.Name, SpellBookCommand.Add, 0, string.Empty));
        };
        load.Items.Add(add);

        var save = new MenuItem { Header = "Save spell set" };
        var delete = new MenuItem { Header = "Delete spell set" };
        if (window.SpellSets.Count == 0)
        {
            save.Items.Add(new MenuItem { Header = "No spell sets", IsEnabled = false });
            delete.Items.Add(new MenuItem { Header = "No spell sets", IsEnabled = false });
        }
        else
        {
            foreach (var set in window.SpellSets)
            {
                AddSpellBookItem(save, window, set, SpellBookCommand.Save);
                AddSpellBookItem(delete, window, set, SpellBookCommand.Delete);
            }
        }

        menu.Items.Add(load);
        menu.Items.Add(save);
        menu.Items.Add(delete);
        menu.IsOpen = true;
    }

    private void AddSpellBookItem(MenuItem parent, LayoutWindow window, SpellSetOption set, SpellBookCommand command)
    {
        var choice = set;
        var caption = SpellSetCaption(choice);
        var item = new MenuItem
        {
            Header = caption,
            IsCheckable = command == SpellBookCommand.Load,
            IsChecked = command == SpellBookCommand.Load && choice.Selected,
        };
        item.Click += (_, args) =>
        {
            args.Handled = true;
            SpellBookCommandChosen?.Invoke(this, new SpellBookCommandEventArgs(window.Name, command, choice.Index, caption));
        };
        parent.Items.Add(item);
    }

    private static string SpellSetCaption(SpellSetOption set)
        => set.Name.Length > 0 ? set.Name : string.Create(CultureInfo.InvariantCulture, $"Set {set.Index}");

    private void DrawHotbar(Grid root, LayoutWindow window)
    {
        var layer = new Canvas
        {
            Tag = ButtonsTag,
            Opacity = PaintedOpacity(window.Hidden),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        var count = Math.Min(HotbarLayout.Buttons, window.Buttons.Count);
        for (var index = 0; index < count; index++)
        {
            var slot = HotbarLayout.Slot(index, window.Width, window.Height);
            var face = window.Buttons[index];
            var button = new Border
            {
                Width = slot.Width,
                Height = slot.Height,
                CornerRadius = new CornerRadius(3),
                ClipToBounds = true,
                IsHitTestVisible = face.SpellName.Length > 0,
                ToolTip = face.SpellName.Length > 0 ? face.SpellName : null,
            };
            if (face.SpellName.Length > 0)
            {
                ToolTipService.SetInitialShowDelay(button, 250);
                ToolTipService.SetShowDuration(button, 60000);
            }
            button.BorderThickness = new Thickness(1);
            if (ThemeManager.IsDark)
            {
                UseBrush(button, Border.BackgroundProperty, "SurfaceAlt", Color.FromRgb(38, 40, 46));
                button.BorderBrush = ButtonEdgeDark;
            }
            else
            {
                button.Background = ButtonFaceLight;
                button.BorderBrush = ButtonEdgeLight;
            }
            ImageSource? picture = face.Icon is int iconId ? ItemIcon?.Invoke(iconId) : null;
            if (picture is null && face.SpellIcon is int spellId)
            {
                picture = SpellIcon?.Invoke(spellId);
            }

            if (picture is not null)
            {
                button.Child = new Image
                {
                    Source = picture,
                    Stretch = Stretch.Uniform,
                    Margin = new Thickness(2),
                    IsHitTestVisible = false,
                };
            }
            else if (face.Text.Length > 0)
            {
                button.Child = new TextBlock
                {
                    Text = face.Text,
                    FontSize = 9,
                    Foreground = HotbarCaptionBrush.For(face.Color),
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(1, 0, 1, 0),
                    IsHitTestVisible = false,
                };
            }

            Canvas.SetLeft(button, slot.Left);
            Canvas.SetTop(button, slot.Top);
            layer.Children.Add(button);
        }

        root.Children.Insert(1, layer);
    }

    private static Path SlotPath(Geometry geometry, Brush fill, Brush stroke, double opacity)
    {
        geometry.Freeze();
        return new Path
        {
            Data = geometry,
            Fill = fill,
            Stroke = stroke,
            StrokeThickness = 1,
            Opacity = opacity,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Tag = SlotsTag,
        };
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Pen FrozenPen(Color color)
    {
        var pen = new Pen(new SolidColorBrush(color), 1);
        pen.Freeze();
        return pen;
    }

    private static double PaintedOpacity(bool hidden)
        => hidden ? (ThemeManager.IsDark ? 0.4 : 0.55) : 1;

    private static (Brush Empty, Brush Used, Brush Stroke) SlotBrushes()
        => ThemeManager.IsDark
            ? (SlotEmptyFillDark, SlotUsedFillDark, SlotStrokeDark)
            : (SlotEmptyFillLight, SlotUsedFillLight, SlotStrokeLight);

    private static Brush TitleBarBrush(string category)
    {
        if (ThemeManager.IsDark)
        {
            return TitleBarFillDark;
        }

        return category switch
        {
            "inventory" => TitleBarInventoryLight,
            "bank" => TitleBarBankLight,
            _ => TitleBarWindowLight,
        };
    }

    private void SelectForMenu(LayoutWindow window)
    {
        if (_drag is not null || _selected.Contains(window.Name, StringComparer.Ordinal))
        {
            return;
        }

        LayoutEdited?.Invoke(this, new LayoutEditedEventArgs(window.Name, default, resized: false, selectingOnly: true, extendSelection: false, []));
    }

    private void OpenMenuFor(LayoutWindow window, MouseButtonEventArgs e)
    {
        if (_drag is not null || ContextMenu is not { } menu)
        {
            return;
        }

        // Selecting redraws the canvas and removes the clicked element, so the built-in menu service would lose its target.
        SelectForMenu(window);
        e.Handled = true;
        menu.PlacementTarget = this;
        menu.Placement = PlacementMode.MousePoint;
        _openingItemMenu = true;
        menu.IsOpen = true;
        _openingItemMenu = false;
    }

    private void AddAnchorBadge(Grid root)
    {
        var size = Math.Clamp(Math.Min(root.Width, root.Height) * 0.5, 12, BadgeSize);
        var badge = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size / 2d),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 12, 12),
            IsHitTestVisible = false,
        };
        UseBrush(badge, Border.BackgroundProperty, "CanvasAnchorBrush", FallbackAnchor);
        var text = new TextBlock
        {
            Text = "1",
            FontSize = size * 0.65,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        UseBrush(text, TextBlock.ForegroundProperty, "AccentTextBrush", Colors.White);
        badge.Child = text;
        root.Children.Add(badge);
    }

    private void UseBrush(FrameworkElement element, DependencyProperty property, string key, Color fallback)
    {
        if (TryFindResource(key) is Brush)
        {
            element.SetResourceReference(property, key);
            return;
        }

        var brush = new SolidColorBrush(fallback);
        brush.Freeze();
        element.SetValue(property, brush);
    }

    private void AddHandles(Grid root, LayoutWindow window)
    {
        AddHandle(root, window, DragMode.Left, HorizontalAlignment.Left, VerticalAlignment.Center);
        AddHandle(root, window, DragMode.Right, HorizontalAlignment.Right, VerticalAlignment.Center);
        if (window.SlotCount == 0)
        {
            AddHandle(root, window, DragMode.Top, HorizontalAlignment.Center, VerticalAlignment.Top);
            AddHandle(root, window, DragMode.Bottom, HorizontalAlignment.Center, VerticalAlignment.Bottom);
            AddHandle(root, window, DragMode.TopLeft, HorizontalAlignment.Left, VerticalAlignment.Top);
            AddHandle(root, window, DragMode.TopRight, HorizontalAlignment.Right, VerticalAlignment.Top);
        }

        AddHandle(root, window, DragMode.BottomLeft, HorizontalAlignment.Left, VerticalAlignment.Bottom);
        AddHandle(root, window, DragMode.BottomRight, HorizontalAlignment.Right, VerticalAlignment.Bottom);
    }

    private void AddHandle(
        Grid root,
        LayoutWindow window,
        DragMode mode,
        HorizontalAlignment horizontal,
        VerticalAlignment vertical)
    {
        var handle = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(20, 20, 20)),
            HorizontalAlignment = horizontal,
            VerticalAlignment = vertical,
            Cursor = CursorFor(mode),
            Tag = mode,
        };
        SizeHandle(handle, mode, window.Width, window.Height);
        handle.MouseLeftButtonDown += (_, e) =>
        {
            if (root.Tag is LayoutWindow target && Window.GetWindow(root) is not null)
            {
                var canvas = FindCanvas(root);
                if (canvas is not null)
                {
                    canvas.BeginDrag(root, target, mode, e);
                }
            }

            e.Handled = true;
        };
        root.Children.Add(handle);
    }

    private void SizeHandle(Border handle, DragMode mode, double windowWidth, double windowHeight)
    {
        var scale = Scale > 0 ? Scale : 1;
        var (width, height) = mode switch
        {
            DragMode.Left or DragMode.Right => (HandleShort, HandleLong),
            DragMode.Top or DragMode.Bottom => (HandleLong, HandleShort),
            _ => (HandleCorner, HandleCorner),
        };
        handle.Width = Math.Min(width / scale, windowWidth / 3);
        handle.Height = Math.Min(height / scale, windowHeight / 3);
        handle.BorderThickness = new Thickness(1 / scale);
    }

    private void UpdateHandleSizes()
    {
        if (Screen is null)
        {
            return;
        }

        foreach (var root in Screen.Children.OfType<Grid>())
        {
            if (root.Tag is not LayoutWindow window)
            {
                continue;
            }

            foreach (var handle in root.Children.OfType<Border>())
            {
                if (handle.Tag is DragMode mode)
                {
                    SizeHandle(handle, mode, window.Width, window.Height);
                }
            }
        }
    }

    private DragMode? EdgeAt(LayoutWindow window, Point point)
    {
        if (!window.Sizable)
        {
            return null;
        }

        var grip = EdgeGrip / (Scale > 0 ? Scale : 1);
        var gripX = Math.Min(grip, window.Width / 4);
        var gripY = Math.Min(grip, window.Height / 4);
        var left = point.X <= gripX;
        var right = point.X >= window.Width - gripX;
        var top = point.Y <= gripY;
        var bottom = point.Y >= window.Height - gripY;
        if (window.IsInventoryBag)
        {
            return (bottom, left, right) switch
            {
                (true, true, _) => DragMode.BottomLeft,
                (true, _, true) => DragMode.BottomRight,
                (_, true, _) => DragMode.Left,
                (_, _, true) => DragMode.Right,
                _ => null,
            };
        }

        return (top, bottom, left, right) switch
        {
            (true, _, true, _) => DragMode.TopLeft,
            (true, _, _, true) => DragMode.TopRight,
            (_, true, true, _) => DragMode.BottomLeft,
            (_, true, _, true) => DragMode.BottomRight,
            (true, _, _, _) => DragMode.Top,
            (_, true, _, _) => DragMode.Bottom,
            (_, _, true, _) => DragMode.Left,
            (_, _, _, true) => DragMode.Right,
            _ => null,
        };
    }

    private static string BagTitle(LayoutWindow window)
    {
        var label = string.IsNullOrEmpty(window.Label) ? window.Name : window.Label;
        return window.ItemName.Length > 0 && label != window.ItemName ? $"{label} {window.ItemName}" : label;
    }

    private static LayoutCanvas? FindCanvas(DependencyObject child)
    {
        var current = child;
        while (current is not null && current is not LayoutCanvas)
        {
            current = VisualTreeHelper.GetParent(current);
        }

        return current as LayoutCanvas;
    }

    private static string TooltipTitle(LayoutWindow window)
    {
        return string.IsNullOrEmpty(window.Label) || window.Label == window.Name
            ? window.Name
            : $"{window.Label} ({window.Name})";
    }

    private static string Tooltip(LayoutWindow window, double width, double height)
    {
        var lines = new List<string> { TooltipTitle(window) };
        if (window.ItemName.Length > 0)
        {
            lines.Add($"{window.ItemName}: {window.SlotSummary}");
        }

        var size = $"{Math.Round(width)} x {Math.Round(height)}";
        if (window.IsInventoryBag)
        {
            var grid = BagGrid.FromWidth(window.SlotCount, width);
            size += $" ({grid.Columns} across, {grid.Rows} down)";
        }

        lines.Add(window.Unplaced ? "No saved position" : size);
        if (window.SpellSets.FirstOrDefault(set => set.Selected) is { } spellSet)
        {
            var name = spellSet.Name.Length > 0 ? spellSet.Name : $"Set {spellSet.Index}";
            lines.Add($"Spell set: {name}. Right-click the book to load, save, or delete a set.");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private void Scroller_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateFit();

    private void UpdateFit()
    {
        var width = Screen.Width > 0 ? Screen.Width : 2560;
        var height = Screen.Height > 0 ? Screen.Height : 1440;
        var fit = Math.Min((Scroller.ActualWidth - FrameMargin) / width, (Scroller.ActualHeight - FrameMargin) / height);
        if (fit <= 0 || double.IsNaN(fit))
        {
            return;
        }

        _fitScale = fit;
        ApplyScale();
    }

    private void ApplyScale()
    {
        _zoomFactor = Math.Clamp(_zoomFactor, MinZoomFactor, Math.Max(MinZoomFactor, MaxScale / _fitScale));
        var scale = _fitScale * _zoomFactor;
        Zoom.ScaleX = scale;
        Zoom.ScaleY = scale;
        ZoomText.Content = $"{Math.Round(scale * 100)}%";
        if (_marquee is not null)
        {
            _marquee.StrokeThickness = 1.5 / scale;
        }

        UpdateHandleSizes();
    }

    private void SetZoom(double factor, Point viewportPoint)
    {
        var contentPoint = Scroller.TranslatePoint(viewportPoint, Screen);
        _zoomFactor = factor;
        ApplyScale();
        Scroller.UpdateLayout();
        var moved = Screen.TranslatePoint(contentPoint, Scroller);
        Scroller.ScrollToHorizontalOffset(Scroller.HorizontalOffset + moved.X - viewportPoint.X);
        Scroller.ScrollToVerticalOffset(Scroller.VerticalOffset + moved.Y - viewportPoint.Y);
    }

    private Point ViewportCenter => new(Scroller.ViewportWidth / 2d, Scroller.ViewportHeight / 2d);

    private void Scroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        if (_drag is not null || _marqueeStart is not null)
        {
            return;
        }

        SetZoom(_zoomFactor * Math.Pow(ZoomStep, e.Delta / 120d), e.GetPosition(Scroller));
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => SetZoom(_zoomFactor * ZoomStep, ViewportCenter);

    private void ZoomOut_Click(object sender, RoutedEventArgs e) => SetZoom(_zoomFactor / ZoomStep, ViewportCenter);

    private void ActualSize_Click(object sender, RoutedEventArgs e) => SetZoom(1 / _fitScale, ViewportCenter);

    private void Fit_Click(object sender, RoutedEventArgs e)
    {
        _zoomFactor = 1;
        ApplyScale();
    }

    private void FullScreen_Click(object sender, RoutedEventArgs e) => FullScreenToggled?.Invoke(this, EventArgs.Empty);

    private void UpdateFullScreenButton(bool fullScreen)
    {
        FullScreenButton.Content = fullScreen ? "\uE73F" : "\uE740";
        FullScreenButton.ToolTip = fullScreen ? "Exit full screen (F12 or Esc)" : "Full screen (F12)";
    }

    private void Scroller_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_drag is not null || _marqueeStart is not null || _panStart is not null)
        {
            return;
        }

        if (e.ChangedButton == MouseButton.Right)
        {
            if (e.OriginalSource is not DependencyObject source || IsInsideWindow(source) || FindAncestor<ScrollBar>(source) is not null)
            {
                return;
            }
        }
        else if (e.ChangedButton != MouseButton.Middle)
        {
            return;
        }

        _panButton = e.ChangedButton;
        _panStart = e.GetPosition(Scroller);
        _panOrigin = new Vector(Scroller.HorizontalOffset, Scroller.VerticalOffset);
        _panMoved = false;
        if (_panButton == MouseButton.Middle)
        {
            Scroller.Cursor = Cursors.SizeAll;
        }

        Scroller.CaptureMouse();
        e.Handled = true;
    }

    private void Scroller_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != _panButton || _panStart is null)
        {
            return;
        }

        var moved = _panMoved;
        _panStart = null;
        _panMoved = false;
        Scroller.Cursor = null;
        Scroller.ReleaseMouseCapture();
        _suppressMenu = _panButton == MouseButton.Right && moved;
        e.Handled = _panButton != MouseButton.Right || moved;
    }

    private void LayoutCanvas_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (_suppressMenu || !_openingItemMenu)
        {
            _suppressMenu = false;
            e.Handled = true;
        }
    }

    private void Scroller_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_drag is not null || _panStart is not null || e.OriginalSource is not DependencyObject source
            || IsInsideWindow(source) || FindAncestor<ScrollBar>(source) is not null)
        {
            return;
        }

        _marqueeStart = e.GetPosition(Screen);
        _marqueeExtend = (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0;
        _marquee = new Rectangle { StrokeThickness = 1.5 / Scale, StrokeDashArray = [4, 2] };
        UseBrush(_marquee, Shape.StrokeProperty, "CanvasSelectedBrush", FallbackSelected);
        UseBrush(_marquee, Shape.FillProperty, "SelectionBrush", Color.FromArgb(60, 56, 189, 248));
        _marquee.Opacity = 0.9;
        PlaceMarquee(_marqueeStart.Value, _marqueeStart.Value);
        Overlay.Children.Add(_marquee);
        Scroller.CaptureMouse();
        e.Handled = true;
    }

    private void Scroller_MouseMove(object sender, MouseEventArgs e)
    {
        if (_panStart is { } panStart)
        {
            var delta = e.GetPosition(Scroller) - panStart;
            if (!_panMoved)
            {
                if (Math.Abs(delta.X) < ClickSlop && Math.Abs(delta.Y) < ClickSlop)
                {
                    return;
                }

                _panMoved = true;
                Scroller.Cursor = Cursors.SizeAll;
            }

            Scroller.ScrollToHorizontalOffset(_panOrigin.X - delta.X);
            Scroller.ScrollToVerticalOffset(_panOrigin.Y - delta.Y);
            return;
        }

        if (_marqueeStart is { } start && e.LeftButton == MouseButtonState.Pressed)
        {
            PlaceMarquee(start, e.GetPosition(Screen));
        }
    }

    private void Scroller_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_marqueeStart is not { } start)
        {
            return;
        }

        var end = e.GetPosition(Screen);
        var extend = _marqueeExtend;
        ClearMarquee();
        Scroller.ReleaseMouseCapture();
        e.Handled = true;
        var area = new Rect(start, end);
        var isClick = area.Width * Scale < ClickSlop && area.Height * Scale < ClickSlop;
        if (isClick && extend)
        {
            return;
        }

        var names = isClick
            ? []
            : Screen.Children.OfType<FrameworkElement>()
                .Select(child => child.Tag)
                .OfType<LayoutWindow>()
                .Where(window => area.IntersectsWith(new Rect(window.Left, window.Top, Math.Max(MinimumSize, window.Width), Math.Max(MinimumSize, window.Height))))
                .OrderBy(window => window.Left)
                .ThenBy(window => window.Top)
                .Select(window => window.Name)
                .ToList();
        SelectionRequested?.Invoke(this, new CanvasSelectionEventArgs(names, extend));
    }

    private void PlaceMarquee(Point start, Point end)
    {
        if (_marquee is null)
        {
            return;
        }

        var area = new Rect(start, end);
        Canvas.SetLeft(_marquee, area.Left);
        Canvas.SetTop(_marquee, area.Top);
        _marquee.Width = area.Width;
        _marquee.Height = area.Height;
    }

    private void ClearMarquee()
    {
        _marqueeStart = null;
        if (_marquee is not null)
        {
            Overlay.Children.Remove(_marquee);
            _marquee = null;
        }
    }

    private void CancelPointerGestures()
    {
        ClearMarquee();
        if (_panStart is not null)
        {
            _panStart = null;
            Scroller.Cursor = null;
        }
    }

    private bool IsInsideWindow(DependencyObject source)
    {
        for (var current = source; current is not null && !ReferenceEquals(current, Screen); current = TreeParent(current))
        {
            if (current is FrameworkElement { Tag: LayoutWindow })
            {
                return true;
            }
        }

        return false;
    }

    private static T? FindAncestor<T>(DependencyObject source) where T : DependencyObject
    {
        for (var current = source; current is not null; current = TreeParent(current))
        {
            if (current is T match)
            {
                return match;
            }
        }

        return null;
    }

    private static DependencyObject? TreeParent(DependencyObject current)
        => current is Visual ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current);

    private void BeginDrag(FrameworkElement element, LayoutWindow window, DragMode mode, MouseButtonEventArgs e)
    {
        var extend = (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0;
        if (extend && mode == DragMode.Move)
        {
            LayoutEdited?.Invoke(this, new LayoutEditedEventArgs(window.Name, default, resized: false, selectingOnly: true, extendSelection: true, []));
            e.Handled = true;
            return;
        }

        var companions = new List<DragCompanion>();
        if (mode == DragMode.Move && _selected.Contains(window.Name, StringComparer.Ordinal))
        {
            foreach (var child in Screen.Children.OfType<FrameworkElement>())
            {
                if (child.Tag is LayoutWindow other
                    && !string.Equals(other.Name, window.Name, StringComparison.Ordinal)
                    && _selected.Contains(other.Name, StringComparer.Ordinal))
                {
                    companions.Add(new DragCompanion(other, child, other.Left, other.Top));
                }
            }
        }

        _drag = new DragState(window, mode, e.GetPosition(Screen), window.Left, window.Top, window.Width, window.Height, companions);
        element.CaptureMouse();
        e.Handled = true;
    }

    private void ContinueDrag(FrameworkElement element, MouseEventArgs e)
    {
        if (_drag is null || e.LeftButton != MouseButtonState.Pressed || !ReferenceEquals(_drag.Window, element.Tag))
        {
            return;
        }

        var point = e.GetPosition(Screen);
        var dx = point.X - _drag.Start.X;
        var dy = point.Y - _drag.Start.Y;
        if (!_drag.Started)
        {
            var scale = Scale > 0 ? Scale : 1;
            if (Math.Abs(dx * scale) < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(dy * scale) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            _drag.Started = true;
        }

        var left = _drag.Left;
        var top = _drag.Top;
        var width = _drag.Width;
        var height = _drag.Height;
        switch (_drag.Mode)
        {
            case DragMode.Move:
                left += dx;
                top += dy;
                break;
            case DragMode.Left:
                left += dx;
                width -= dx;
                break;
            case DragMode.Right:
                width += dx;
                break;
            case DragMode.Top:
                top += dy;
                height -= dy;
                break;
            case DragMode.Bottom:
                height += dy;
                break;
            case DragMode.TopLeft:
                left += dx;
                width -= dx;
                top += dy;
                height -= dy;
                break;
            case DragMode.TopRight:
                width += dx;
                top += dy;
                height -= dy;
                break;
            case DragMode.BottomLeft:
                left += dx;
                width -= dx;
                height += dy;
                break;
            case DragMode.BottomRight:
                width += dx;
                height += dy;
                break;
        }

        var maxWidth = ScreenWidth > MinimumSize ? ScreenWidth : MinimumSize;
        var maxHeight = ScreenHeight > MinimumSize ? ScreenHeight : MinimumSize;
        if (width < MinimumSize)
        {
            if (_drag.Mode is DragMode.Left or DragMode.TopLeft or DragMode.BottomLeft)
            {
                left -= MinimumSize - width;
            }

            width = MinimumSize;
        }
        else if (width > maxWidth)
        {
            if (_drag.Mode is DragMode.Left or DragMode.TopLeft or DragMode.BottomLeft)
            {
                left -= maxWidth - width;
            }

            width = maxWidth;
        }

        if (height < MinimumSize)
        {
            if (_drag.Mode is DragMode.Top or DragMode.TopLeft or DragMode.TopRight)
            {
                top -= MinimumSize - height;
            }

            height = MinimumSize;
        }
        else if (height > maxHeight)
        {
            if (_drag.Mode is DragMode.Top or DragMode.TopLeft or DragMode.TopRight)
            {
                top -= maxHeight - height;
            }

            height = maxHeight;
        }

        var bagResize = _drag.Mode != DragMode.Move && _drag.Window.IsInventoryBag;
        if (bagResize)
        {
            var slots = _drag.Window.SlotCount;
            var narrowest = BagGrid.ForColumns(slots, 1).Width;
            var widest = Math.Max(narrowest, Math.Min(BagGrid.ForColumns(slots, slots).Width, maxWidth));
            width = Math.Clamp(width, narrowest, widest);
            left = AnchorsRight(_drag.Mode) ? _drag.Left + _drag.Width - width : _drag.Left;
            top = _drag.Top;
            height = BagGrid.FromWidth(slots, width).Height;
        }
        else
        {
            SnapEdges(ref left, ref top, ref width, ref height);
        }

        left = Math.Clamp(left, MinimumSize - width, Math.Max(MinimumSize - width, ScreenWidth - MinimumSize));
        top = Math.Clamp(top, MinimumSize - height, Math.Max(MinimumSize - height, ScreenHeight - MinimumSize));

        var deltaX = left - _drag.Left;
        var deltaY = top - _drag.Top;
        _drag.Window.Left = left;
        _drag.Window.Top = top;
        _drag.Window.Width = width;
        _drag.Window.Height = height;
        element.Width = width;
        element.Height = height;
        Canvas.SetLeft(element, left);
        Canvas.SetTop(element, top);
        if (bagResize && element is Grid root)
        {
            UpdateSlots(root, _drag.Window);
        }
        else if (_drag.Mode != DragMode.Move && element is Grid chatRoot && _drag.Window.ChatTabs.Count > 0)
        {
            DrawChatTabs(chatRoot, _drag.Window);
        }

        if (_drag.Mode == DragMode.Move)
        {
            foreach (var companion in _drag.Companions)
            {
                var companionLeft = Math.Clamp(companion.Left + deltaX, MinimumSize - companion.Window.Width, Math.Max(MinimumSize - companion.Window.Width, ScreenWidth - MinimumSize));
                var companionTop = Math.Clamp(companion.Top + deltaY, MinimumSize - companion.Window.Height, Math.Max(MinimumSize - companion.Window.Height, ScreenHeight - MinimumSize));
                companion.Window.Left = companionLeft;
                companion.Window.Top = companionTop;
                companion.Element.Width = companion.Window.Width;
                companion.Element.Height = companion.Window.Height;
                Canvas.SetLeft(companion.Element, companionLeft);
                Canvas.SetTop(companion.Element, companionTop);
            }
        }

        element.ToolTip = Tooltip(_drag.Window, width, height);
    }

    private static bool AnchorsRight(DragMode mode) => mode is DragMode.Left or DragMode.TopLeft or DragMode.BottomLeft;

    private void SettleBag(FrameworkElement element, DragState drag)
    {
        var window = drag.Window;
        var grid = BagGrid.FromWidth(window.SlotCount, window.Width);
        if (AnchorsRight(drag.Mode))
        {
            window.Left += window.Width - grid.Width;
        }

        window.Width = grid.Width;
        window.Height = grid.Height;
        element.Width = grid.Width;
        element.Height = grid.Height;
        Canvas.SetLeft(element, window.Left);
        if (element is Grid root)
        {
            UpdateSlots(root, window);
        }

        element.ToolTip = Tooltip(window, grid.Width, grid.Height);
    }

    private void SnapEdges(ref double left, ref double top, ref double width, ref double height)
    {
        if (_drag is null)
        {
            return;
        }

        var mode = _drag.Mode;
        if (mode == DragMode.Move)
        {
            var rawLeft = left;
            var rawTop = top;
            var aligned = SnapToWindows(ref left, ref top, width, height);
            if (SnapSize >= 1)
            {
                if (!aligned.X)
                {
                    left = Snap(rawLeft);
                }

                if (!aligned.Y)
                {
                    top = Snap(rawTop);
                }
            }

            return;
        }

        var pointerLeft = left;
        var pointerTop = top;
        var pointerWidth = width;
        var pointerHeight = height;
        var resized = SnapResizeToWindows(ref left, ref top, ref width, ref height);
        if (SnapSize < 1)
        {
            return;
        }

        if (!resized.X)
        {
            left = pointerLeft;
            width = pointerWidth;
            if (mode is DragMode.Left or DragMode.TopLeft or DragMode.BottomLeft)
            {
                var right = left + width;
                left = Snap(left);
                width = Math.Max(MinimumSize, right - left);
            }
            else if (mode is DragMode.Right or DragMode.TopRight or DragMode.BottomRight)
            {
                width = Math.Max(MinimumSize, Snap(left + width) - left);
            }
        }

        if (!resized.Y)
        {
            top = pointerTop;
            height = pointerHeight;
            if (mode is DragMode.Top or DragMode.TopLeft or DragMode.TopRight)
            {
                var bottom = top + height;
                top = Snap(top);
                height = Math.Max(MinimumSize, bottom - top);
            }
            else if (mode is DragMode.Bottom or DragMode.BottomLeft or DragMode.BottomRight)
            {
                height = Math.Max(MinimumSize, Snap(top + height) - top);
            }
        }
    }

    private double Snap(double value)
    {
        return Math.Round(value / SnapSize) * SnapSize;
    }

    /// <summary>Pulls the moving frame onto other windows' edges, centers, and equal gaps. The grid is unchanged.</summary>
    private (bool X, bool Y) SnapToWindows(ref double left, ref double top, double width, double height)
    {
        if (_drag is null)
        {
            return (false, false);
        }

        var scale = Scale > 0 ? Scale : 1;
        var moving = new List<ScreenRect> { VisibleFrame(_drag.Window, new ScreenRect(left, top, width, height)) };
        var names = new HashSet<string>(StringComparer.Ordinal) { _drag.Window.Name };
        var deltaX = left - _drag.Left;
        var deltaY = top - _drag.Top;
        foreach (var companion in _drag.Companions)
        {
            names.Add(companion.Window.Name);
            moving.Add(VisibleFrame(
                companion.Window,
                new ScreenRect(companion.Left + deltaX, companion.Top + deltaY, companion.Window.Width, companion.Window.Height)));
        }

        var others = new List<ScreenRect>();
        foreach (var child in Screen.Children.OfType<FrameworkElement>())
        {
            if (child.Tag is not LayoutWindow window || window.Hidden || names.Contains(window.Name))
            {
                continue;
            }

            others.Add(VisibleFrame(window, new ScreenRect(window.Left, window.Top, window.Width, window.Height)));
        }

        var union = Union(moving);
        var snapped = MoveSnap.Snap(union, others, SnapScreenPixels / scale);
        left += snapped.Left - union.Left;
        top += snapped.Top - union.Top;
        DrawSnapGuides(snapped.Guides);
        return (snapped.SnappedX, snapped.SnappedY);
    }

    /// <summary>Pulls the edges being resized onto other windows and draws the same guides as a move.</summary>
    private (bool X, bool Y) SnapResizeToWindows(ref double left, ref double top, ref double width, ref double height)
    {
        if (_drag is null)
        {
            return (false, false);
        }

        var mode = _drag.Mode;
        var scale = Scale > 0 ? Scale : 1;
        var layout = new ScreenRect(left, top, width, height);
        var visible = VisibleFrame(_drag.Window, layout);
        var others = new List<ScreenRect>();
        foreach (var child in Screen.Children.OfType<FrameworkElement>())
        {
            if (child.Tag is not LayoutWindow window || window.Hidden || window.Name == _drag.Window.Name)
            {
                continue;
            }

            others.Add(VisibleFrame(window, new ScreenRect(window.Left, window.Top, window.Width, window.Height)));
        }

        var minWidth = Math.Max(0, MinimumSize - (layout.Width - visible.Width));
        var minHeight = Math.Max(0, MinimumSize - (layout.Height - visible.Height));
        var snapped = MoveSnap.SnapResize(
            visible,
            others,
            SnapScreenPixels / scale,
            leftEdge: mode is DragMode.Left or DragMode.TopLeft or DragMode.BottomLeft,
            rightEdge: mode is DragMode.Right or DragMode.TopRight or DragMode.BottomRight,
            topEdge: mode is DragMode.Top or DragMode.TopLeft or DragMode.TopRight,
            bottomEdge: mode is DragMode.Bottom or DragMode.BottomLeft or DragMode.BottomRight,
            minWidth,
            minHeight);
        left += snapped.Rect.Left - visible.Left;
        top += snapped.Rect.Top - visible.Top;
        width += snapped.Rect.Width - visible.Width;
        height += snapped.Rect.Height - visible.Height;
        DrawSnapGuides(snapped.Guides);
        return (snapped.SnappedX, snapped.SnappedY);
    }

    private static ScreenRect VisibleFrame(LayoutWindow window, ScreenRect layout)
    {
        var frame = window.IsInventoryBag ? FrameInset.Bag
            : window.Buttons.Count > 0 ? HotbarLayout.Chrome(layout.Width, layout.Height)
            : WindowFrame.Paint(window.Name);
        return frame.IsEmpty ? layout : frame.ToPainted(layout);
    }

    private static ScreenRect Union(IReadOnlyList<ScreenRect> rects)
    {
        var left = rects[0].Left;
        var top = rects[0].Top;
        var right = left + rects[0].Width;
        var bottom = top + rects[0].Height;
        for (var index = 1; index < rects.Count; index++)
        {
            var rect = rects[index];
            left = Math.Min(left, rect.Left);
            top = Math.Min(top, rect.Top);
            right = Math.Max(right, rect.Left + rect.Width);
            bottom = Math.Max(bottom, rect.Top + rect.Height);
        }

        return new ScreenRect(left, top, right - left, bottom - top);
    }

    private void DrawSnapGuides(IReadOnlyList<MoveSnap.Line> lines)
    {
        ClearSnapGuides();
        if (lines.Count == 0 || Overlay is null)
        {
            return;
        }

        var thickness = 1 / (Scale > 0 ? Scale : 1);
        foreach (var guide in lines)
        {
            Overlay.Children.Add(new Line
            {
                X1 = guide.X1,
                Y1 = guide.Y1,
                X2 = guide.X2,
                Y2 = guide.Y2,
                Stroke = SnapGuideBrush,
                StrokeThickness = thickness,
                IsHitTestVisible = false,
                SnapsToDevicePixels = true,
                Tag = SnapGuideTag,
            });
        }
    }

    private void ClearSnapGuides()
    {
        if (Overlay is null)
        {
            return;
        }

        for (var index = Overlay.Children.Count - 1; index >= 0; index--)
        {
            if (Overlay.Children[index] is FrameworkElement element && Equals(element.Tag, SnapGuideTag))
            {
                Overlay.Children.RemoveAt(index);
            }
        }
    }

    private void EndDrag(FrameworkElement element, MouseButtonEventArgs e)
    {
        if (_drag is null || !ReferenceEquals(_drag.Window, element.Tag))
        {
            return;
        }

        var drag = _drag;
        _drag = null;
        ClearSnapGuides();
        element.ReleaseMouseCapture();
        if (drag.Mode != DragMode.Move && drag.Window.IsInventoryBag)
        {
            SettleBag(element, drag);
        }

        var moved = Math.Abs(drag.Window.Left - drag.Left) > 0.5
            || Math.Abs(drag.Window.Top - drag.Top) > 0.5
            || Math.Abs(drag.Window.Width - drag.Width) > 0.5
            || Math.Abs(drag.Window.Height - drag.Height) > 0.5;
        var edits = new List<LayoutWindowEdit>
        {
            new(drag.Window.Name, new ScreenRect(drag.Window.Left, drag.Window.Top, drag.Window.Width, drag.Window.Height)),
        };
        if (drag.Mode == DragMode.Move)
        {
            edits.AddRange(drag.Companions.Select(companion => new LayoutWindowEdit(
                companion.Window.Name,
                new ScreenRect(companion.Window.Left, companion.Window.Top, companion.Window.Width, companion.Window.Height))));
        }

        LayoutEdited?.Invoke(
            this,
            new LayoutEditedEventArgs(
                drag.Window.Name,
                edits[0].Rect,
                resized: drag.Mode != DragMode.Move,
                selectingOnly: !moved,
                extendSelection: false,
                edits));
        e.Handled = true;
    }

    private static double FrameThickness(bool primary, bool selected)
        => ThemeManager.IsDark
            ? primary ? 6 : selected ? 4 : 2
            : primary ? 6 : selected ? 5 : 3;

    private void ApplyStyle(Border fill, Border stroke, LayoutWindow window, bool primary, bool selected)
    {
        if (!ThemeManager.IsDark)
        {
            ApplyLightStyle(fill, stroke, window, primary, selected);
            return;
        }

        var (key, fallback) = primary
            ? ("CanvasAnchorBrush", FallbackAnchor)
            : selected
                ? ("CanvasSelectedBrush", FallbackSelected)
                : window.Category switch
                {
                    "inventory" => ("CanvasInventoryBrush", Color.FromRgb(46, 125, 50)),
                    "bank" => ("CanvasBankBrush", Color.FromRgb(239, 108, 0)),
                    _ => ("CanvasWindowBrush", Color.FromRgb(21, 101, 192)),
                };
        UseBrush(fill, Border.BackgroundProperty, key, fallback);
        var hotbarEdge = !primary && !selected && HotButtonText.IsHotButtonWindow(window.Name);
        if (hotbarEdge)
        {
            stroke.BorderBrush = ButtonEdgeDark;
        }
        else
        {
            UseBrush(stroke, Border.BorderBrushProperty, key, fallback);
        }

        stroke.BorderThickness = new Thickness(FrameThickness(primary, selected));
        if (primary || selected)
        {
            fill.Opacity = primary ? 0.55 : 0.45;
            stroke.Opacity = 1;
            return;
        }

        fill.Opacity = window.Hidden ? 0.15 : window.Category == "window" ? 0.5 : 0.35;
        stroke.Opacity = window.Hidden || window.Unplaced ? 0.4 : hotbarEdge ? 1 : 0.9;
    }

    private void ApplyLightStyle(Border fill, Border stroke, LayoutWindow window, bool primary, bool selected)
    {
        var (fillKey, fillFallback) = window.Category switch
        {
            "inventory" => ("CanvasInventoryBrush", Color.FromRgb(0xF8, 0xE8, 0xC4)),
            "bank" => ("CanvasBankBrush", Color.FromRgb(0xD7, 0xE6, 0xF8)),
            _ => ("CanvasWindowBrush", Color.FromRgb(0xFF, 0xFF, 0xFF)),
        };
        var (strokeKey, strokeFallback) = primary
            ? ("CanvasAnchorBrush", FallbackAnchor)
            : selected
                ? ("CanvasSelectedBrush", FallbackSelected)
                : ("CanvasStrokeBrush", Color.FromRgb(0x2C, 0x35, 0x42));
        UseBrush(fill, Border.BackgroundProperty, fillKey, fillFallback);
        UseBrush(stroke, Border.BorderBrushProperty, strokeKey, strokeFallback);
        stroke.BorderThickness = new Thickness(FrameThickness(primary, selected));
        fill.Opacity = window.Hidden ? 0.55 : 1;
        stroke.Opacity = window.Hidden || window.Unplaced ? 0.75 : 1;
    }

    private static Cursor CursorFor(DragMode mode)
    {
        return mode switch
        {
            DragMode.Left or DragMode.Right => Cursors.SizeWE,
            DragMode.Top or DragMode.Bottom => Cursors.SizeNS,
            DragMode.TopLeft or DragMode.BottomRight => Cursors.SizeNWSE,
            DragMode.TopRight or DragMode.BottomLeft => Cursors.SizeNESW,
            _ => Cursors.SizeAll,
        };
    }

    private enum DragMode
    {
        Move,
        Left,
        Right,
        Top,
        Bottom,
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight,
    }

    private sealed class DragState
    {
        public DragState(
            LayoutWindow window,
            DragMode mode,
            Point start,
            double left,
            double top,
            double width,
            double height,
            List<DragCompanion> companions)
        {
            Window = window;
            Mode = mode;
            Start = start;
            Left = left;
            Top = top;
            Width = width;
            Height = height;
            Companions = companions;
        }

        public List<DragCompanion> Companions { get; }

        public bool Started { get; set; }

        public LayoutWindow Window { get; }

        public DragMode Mode { get; }

        public Point Start { get; }

        public double Left { get; }

        public double Top { get; }

        public double Width { get; }

        public double Height { get; }
    }

    private sealed class DragCompanion
    {
        public DragCompanion(LayoutWindow window, FrameworkElement element, double left, double top)
        {
            Window = window;
            Element = element;
            Left = left;
            Top = top;
        }

        public LayoutWindow Window { get; }

        public FrameworkElement Element { get; }

        public double Left { get; }

        public double Top { get; }
    }
}

public readonly record struct LayoutWindowEdit(string Name, ScreenRect Rect);

public sealed class LayoutEditedEventArgs : EventArgs
{
    public LayoutEditedEventArgs(
        string name,
        ScreenRect rect,
        bool resized,
        bool selectingOnly,
        bool extendSelection,
        IReadOnlyList<LayoutWindowEdit> edits)
    {
        Name = name;
        Rect = rect;
        Resized = resized;
        SelectingOnly = selectingOnly;
        ExtendSelection = extendSelection;
        Edits = edits;
    }

    public string Name { get; }

    public ScreenRect Rect { get; }

    public bool Resized { get; }

    public bool SelectingOnly { get; }

    public bool ExtendSelection { get; }

    public IReadOnlyList<LayoutWindowEdit> Edits { get; }
}

public sealed class CanvasSelectionEventArgs : EventArgs
{
    public CanvasSelectionEventArgs(IReadOnlyList<string> names, bool extend)
    {
        Names = names;
        Extend = extend;
    }

    public IReadOnlyList<string> Names { get; }

    public bool Extend { get; }
}

public enum SpellBookCommand
{
    Load,
    Save,
    Delete,
    Add,
}

public sealed class SpellBookCommandEventArgs : EventArgs
{
    public SpellBookCommandEventArgs(string windowName, SpellBookCommand command, int index, string setName)
    {
        WindowName = windowName;
        Command = command;
        Index = index;
        SetName = setName;
    }

    public string WindowName { get; }

    public SpellBookCommand Command { get; }

    public int Index { get; }

    public string SetName { get; }
}
