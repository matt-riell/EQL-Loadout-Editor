using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;

namespace EqlLoadouts.App;

public partial class SplashWindow : Window
{
    private readonly bool _timed;
    private bool _allowClose = true;

    public SplashWindow(bool timed = false)
    {
        InitializeComponent();
        VersionText.Text = "Version " + ProductVersion();
        RuntimeText.Text = RuntimeVersion();
        _timed = timed;
        if (!timed)
        {
            return;
        }

        _allowClose = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        CloseButton.Visibility = Visibility.Collapsed;
    }

    public void AllowClose() => _allowClose = true;

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_timed && !_allowClose)
        {
            e.Cancel = true;
        }

        base.OnClosing(e);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    internal static string ProductVersion()
    {
        var assemblyVersion = typeof(SplashWindow).Assembly.GetName().Version;
        // 1.0.* stores the generated build and revision on the assembly version.
        if (assemblyVersion is { Build: > 0, Revision: >= 0 })
        {
            return FormatVersion(assemblyVersion, assemblyVersion);
        }

        var informational = typeof(SplashWindow).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plus = informational.IndexOf('+', StringComparison.Ordinal);
            var version = plus >= 0 ? informational[..plus] : informational;
            if (Version.TryParse(version, out var parsed))
            {
                return FormatVersion(parsed, assemblyVersion);
            }
        }

        return FormatVersion(assemblyVersion, assemblyVersion);
    }

    internal static string RuntimeVersion()
        => RuntimeInformation.FrameworkDescription;

    private static string FormatVersion(Version? version, Version? assemblyVersion)
    {
        var major = version?.Major ?? 1;
        var minor = version?.Minor ?? 0;
        var patch = version is { Build: >= 0 } ? version.Build : 0;
        var build = version is { Revision: >= 0 }
            ? version.Revision
            : assemblyVersion is { Revision: >= 0 } ? assemblyVersion.Revision : 0;
        return $"{major}.{minor}.{patch}b{build}";
    }

    private static string? LicensePath()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in LicenseDirectories())
        {
            if (string.IsNullOrWhiteSpace(directory) || !seen.Add(Path.GetFullPath(directory)))
            {
                continue;
            }

            var path = Path.Combine(directory, "LICENSE");
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    private static IEnumerable<string> LicenseDirectories()
    {
        yield return AppContext.BaseDirectory;

        var process = Environment.ProcessPath;
        if (string.IsNullOrEmpty(process))
        {
            yield break;
        }

        var directory = Path.GetDirectoryName(process);
        if (!string.IsNullOrEmpty(directory))
        {
            yield return directory;
        }
    }

    private void SiteLink_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://genx-tech.com",
                UseShellExecute = true,
            });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(this, "The website could not be opened in a browser.", "EQL Loadout Editor", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ViewLicense_Click(object sender, RoutedEventArgs e)
    {
        var path = LicensePath();
        if (path is null)
        {
            MessageBox.Show(this, "The license file was not found.", "License", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = new Uri(path).AbsoluteUri,
                UseShellExecute = true,
            });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(this, "The license could not be opened in a browser.", "License", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
