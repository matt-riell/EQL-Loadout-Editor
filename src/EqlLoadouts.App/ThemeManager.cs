using System.Windows;
using Microsoft.Win32;

namespace EqlLoadouts.App;

public static class ThemeManager
{
    private static string _mode = "Dark";
    private static ResourceDictionary? _dictionary;
    private static bool _watching;
    private static bool _windowsHooked;
    private static bool _dark;

    public static bool IsDark => _dark;

    public static event EventHandler? ThemeChanged;

    public static void Apply(string? mode)
    {
        _mode = mode is "Light" or "Dark" or "System" ? mode : "Dark";
        var dark = _mode == "Dark" || (_mode == "System" && IsSystemDark());
        var changed = _dictionary is null || _dark != dark;
        var source = new Uri(dark ? "Themes/Theme.Dark.xaml" : "Themes/Theme.Light.xaml", UriKind.Relative);
        var dictionary = new ResourceDictionary { Source = source };
        var merged = Application.Current.Resources.MergedDictionaries;
        if (_dictionary is not null)
        {
            merged.Remove(_dictionary);
        }

        merged.Add(dictionary);
        _dictionary = dictionary;
        _dark = dark;
        EnsureWindowHook();
        ApplyOpenCaptions();
        if (changed)
        {
            ThemeChanged?.Invoke(null, EventArgs.Empty);
        }
    }

    public static void WatchSystem()
    {
        if (_watching)
        {
            return;
        }

        _watching = true;
        SystemEvents.UserPreferenceChanged += (_, args) =>
        {
            if (args.Category != UserPreferenceCategory.General || _mode != "System")
            {
                return;
            }

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null)
            {
                return;
            }

            dispatcher.Invoke(() => Apply(_mode));
        };
    }

    private static void EnsureWindowHook()
    {
        if (_windowsHooked)
        {
            return;
        }

        _windowsHooked = true;
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnWindowLoaded));
    }

    private static void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Window window && ReferenceEquals(sender, e.OriginalSource))
        {
            WindowCaption.ApplyTo(window, _dark, refreshFrame: false);
        }
    }

    private static void ApplyOpenCaptions()
    {
        var application = Application.Current;
        if (application is null)
        {
            return;
        }

        foreach (Window window in application.Windows)
        {
            WindowCaption.ApplyTo(window, _dark, refreshFrame: true);
        }
    }

    public static bool IsSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch (System.Security.SecurityException)
        {
            return false;
        }
        catch (System.IO.IOException)
        {
            return false;
        }
    }
}
