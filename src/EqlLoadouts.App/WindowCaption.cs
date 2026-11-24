using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace EqlLoadouts.App;

/// <summary>
/// Paints the native caption with the active app theme. Immersive dark mode switches the
/// caption buttons; the caption, text, and border colors come from the theme dictionary.
/// </summary>
public sealed class WindowCaption
{
    // DWMWA_USE_IMMERSIVE_DARK_MODE is 20 on Windows 10 20H1+ and Windows 11. 19 is the earlier value.
    private const int UseImmersiveDarkMode = 20;
    private const int UseImmersiveDarkModeBefore20H1 = 19;
    private const int BorderColor = 34;
    private const int CaptionColor = 35;
    private const int TextColor = 36;

    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;

    public static readonly DependencyProperty TrackThemeProperty = DependencyProperty.RegisterAttached(
        "TrackTheme",
        typeof(bool),
        typeof(WindowCaption),
        new PropertyMetadata(false, OnTrackThemeChanged));

    private WindowCaption()
    {
    }

    public static bool GetTrackTheme(DependencyObject element) => (bool)element.GetValue(TrackThemeProperty);

    public static void SetTrackTheme(DependencyObject element, bool value) => element.SetValue(TrackThemeProperty, value);

    public static void ApplyTo(Window window, bool dark, bool refreshFrame)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        try
        {
            var enabled = dark ? 1 : 0;
            if (DwmSetWindowAttribute(handle, UseImmersiveDarkMode, ref enabled, sizeof(int)) != 0)
            {
                DwmSetWindowAttribute(handle, UseImmersiveDarkModeBefore20H1, ref enabled, sizeof(int));
            }

            if (TryThemeColor("WindowBackground", out var caption))
            {
                DwmSetWindowAttribute(handle, CaptionColor, ref caption, sizeof(int));
            }

            if (TryThemeColor("TextBrush", out var text))
            {
                DwmSetWindowAttribute(handle, TextColor, ref text, sizeof(int));
            }

            if (TryThemeColor("BorderBrush", out var border))
            {
                DwmSetWindowAttribute(handle, BorderColor, ref border, sizeof(int));
            }

            if (refreshFrame && window.IsLoaded)
            {
                SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0, SwpNoSize | SwpNoMove | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
            }
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }
    }

    private static void OnTrackThemeChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (element is not Window window || args.NewValue is not true || args.OldValue is true)
        {
            return;
        }

        window.SourceInitialized += (_, _) => ApplyTo(window, ThemeManager.IsDark, refreshFrame: false);
        var style = DependencyPropertyDescriptor.FromProperty(Window.WindowStyleProperty, typeof(Window));
        EventHandler styleChanged = (_, _) => ApplyTo(window, ThemeManager.IsDark, refreshFrame: false);
        style.AddValueChanged(window, styleChanged);
        window.Closed += (_, _) => style.RemoveValueChanged(window, styleChanged);
        if (new WindowInteropHelper(window).Handle != IntPtr.Zero)
        {
            ApplyTo(window, ThemeManager.IsDark, refreshFrame: false);
        }
    }

    private static bool TryThemeColor(string key, out int colorRef)
    {
        colorRef = 0;
        if (Application.Current?.TryFindResource(key) is not SolidColorBrush brush)
        {
            return false;
        }

        var color = brush.Color;
        colorRef = color.R | (color.G << 8) | (color.B << 16);
        return true;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr hwndInsertAfter, int x, int y, int cx, int cy, uint flags);
}
