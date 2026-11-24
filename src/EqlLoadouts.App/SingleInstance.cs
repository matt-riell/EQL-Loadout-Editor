using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace EqlLoadouts.App;

internal static class SingleInstance
{
    private const string MutexName = @"Local\EqlLoadouts.SingleInstance";
    private const string EventName = @"Local\EqlLoadouts.Activate";
    private const string OwnerMapName = @"Local\EqlLoadouts.SingleInstance.Owner";
    private const int SwRestore = 9;

    private static Mutex? _mutex;
    private static EventWaitHandle? _activate;
    private static ManualResetEvent? _stop;
    private static MemoryMappedFile? _owner;
    private static Thread? _listener;

    public static bool Acquire()
    {
        _mutex = new Mutex(false, MutexName);
        try
        {
            if (!_mutex.WaitOne(TimeSpan.Zero))
            {
                _mutex.Dispose();
                _mutex = null;
                return false;
            }
        }
        catch (AbandonedMutexException)
        {
        }

        _owner = MemoryMappedFile.CreateOrOpen(OwnerMapName, sizeof(int));
        using (var view = _owner.CreateViewAccessor())
        {
            view.Write(0, Environment.ProcessId);
        }

        _activate = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
        _stop = new ManualResetEvent(false);
        _listener = new Thread(Listen)
        {
            IsBackground = true,
            Name = "Single instance"
        };
        _listener.Start();
        return true;
    }

    public static void ActivateExisting()
    {
        var deadline = Environment.TickCount64 + 2000;
        var signaled = false;
        do
        {
            if (!signaled)
            {
                signaled = Signal();
            }

            foreach (var process in OtherProcesses())
            {
                using (process)
                {
                    var hwnd = FindTopWindow(process.Id);
                    if (hwnd == IntPtr.Zero)
                    {
                        continue;
                    }

                    signaled = true;
                    AllowSetForegroundWindow(process.Id);
                    Signal();
                    if (IsIconic(hwnd))
                    {
                        ShowWindow(hwnd, SwRestore);
                    }

                    SetForegroundWindow(hwnd);
                    return;
                }
            }

            Thread.Sleep(50);
        }
        while (Environment.TickCount64 < deadline);
    }

    public static void Release()
    {
        _stop?.Set();
        _listener?.Join(TimeSpan.FromMilliseconds(500));
        _listener = null;

        if (_mutex is not null)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }

            _mutex.Dispose();
            _mutex = null;
        }

        _activate?.Dispose();
        _activate = null;
        _stop?.Dispose();
        _stop = null;
        _owner?.Dispose();
        _owner = null;
    }

    private static void Listen()
    {
        var activate = _activate;
        var stop = _stop;
        if (activate is null || stop is null)
        {
            return;
        }

        while (WaitHandle.WaitAny([activate, stop]) == 0)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.HasShutdownStarted)
            {
                continue;
            }

            dispatcher.BeginInvoke(BringToFront);
        }
    }

    private static void BringToFront()
    {
        var app = Application.Current;
        if (app is null || app.Dispatcher.HasShutdownStarted)
        {
            return;
        }

        var window = app.Windows.OfType<Window>().LastOrDefault(candidate => candidate.IsVisible)
            ?? app.MainWindow;
        if (window is null)
        {
            return;
        }

        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd != IntPtr.Zero && IsIconic(hwnd))
        {
            ShowWindow(hwnd, SwRestore);
        }

        window.Activate();
        window.Topmost = true;
        window.Topmost = false;
        window.Focus();
    }

    private static bool Signal()
    {
        try
        {
            using var handle = EventWaitHandle.OpenExisting(EventName);
            handle.Set();
            return true;
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            return false;
        }
    }

    private static IEnumerable<Process> OtherProcesses()
    {
        var ownerId = ReadOwnerProcessId();
        if (ownerId == 0 || ownerId == Environment.ProcessId)
        {
            yield break;
        }

        Process? process;
        try
        {
            process = Process.GetProcessById(ownerId);
        }
        catch (ArgumentException)
        {
            yield break;
        }

        yield return process;
    }

    private static int ReadOwnerProcessId()
    {
        try
        {
            using var map = MemoryMappedFile.OpenExisting(OwnerMapName);
            using var view = map.CreateViewAccessor();
            return view.ReadInt32(0);
        }
        catch (FileNotFoundException)
        {
            return 0;
        }
    }

    private static IntPtr FindTopWindow(int processId)
    {
        IntPtr enabled = IntPtr.Zero;
        IntPtr visible = IntPtr.Zero;
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid != (uint)processId || !IsWindowVisible(hwnd))
            {
                return true;
            }

            if (visible == IntPtr.Zero)
            {
                visible = hwnd;
            }

            if (IsWindowEnabled(hwnd))
            {
                enabled = hwnd;
                return false;
            }

            return true;
        }, IntPtr.Zero);
        return enabled != IntPtr.Zero ? enabled : visible;
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowEnabled(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int dwProcessId);
}
