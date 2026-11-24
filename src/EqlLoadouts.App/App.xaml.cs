using System.Windows;
using System.Windows.Threading;

namespace EqlLoadouts.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        if (!SingleInstance.Acquire())
        {
            SingleInstance.ActivateExisting();
            Shutdown();
            return;
        }

        var store = new Settings.AppSettingsStore();
        ThemeManager.Apply(store.Current.Theme);
        ThemeManager.WatchSystem();
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        base.OnStartup(e);

        var splash = new SplashWindow(timed: true);
        splash.Show();

        MainWindow? editor = null;
        var elapsed = false;
        var ready = false;
        var revealed = false;

        void Reveal()
        {
            if (revealed || !elapsed || !ready || editor is null)
            {
                return;
            }

            revealed = true;
            splash.AllowClose();
            splash.Close();
            MainWindow = editor;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            editor.Show();
        }

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            elapsed = true;
            Reveal();
        };
        timer.Start();

        Dispatcher.BeginInvoke(() =>
        {
            editor = new MainWindow();
            ready = true;
            Reveal();
        }, DispatcherPriority.ApplicationIdle);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SingleInstance.Release();
        base.OnExit(e);
    }
}
