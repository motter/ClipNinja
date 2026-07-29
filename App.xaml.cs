using System;
using System.Linq;
using System.Threading;
using System.Windows;

namespace ClipNinjaV2;

public partial class App : Application
{
    private static Mutex? _singleInstanceMutex;
    private static bool _ownsMutex;
    private MainWindow? _mainWindow;

    /// <summary>
    /// True if launched with --hidden (typically auto-start at Windows logon).
    /// When set, MainWindow starts minimized to the tray instead of showing
    /// a window. Read by MainWindow during OnWindowLoaded.
    /// </summary>
    public static bool StartHidden { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        // ── Global exception safety net ──────────────────────────────
        // A tray app dying silently is the worst failure mode: the user
        // just sees "the whole thing crashed" with zero information.
        // These handlers log the FULL exception to the trace file and
        // show it in a dialog. UI-thread exceptions are marked handled
        // so the app survives whenever survival is plausible.
        DispatcherUnhandledException += (_, ex) =>
        {
            Services.Trace.Log("crash", $"UI exception: {ex.Exception}");
            MessageBox.Show(
                $"ClipNinja hit an unexpected error (the app will try to keep running):\n\n" +
                $"{ex.Exception.GetType().Name}: {ex.Exception.Message}\n\n" +
                $"{ex.Exception.StackTrace}\n\n" +
                "Details were written to the trace log (tray menu → Open trace log).",
                "ClipNinja — unexpected error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            ex.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
        {
            // Non-UI thread or truly fatal — can't mark handled, but at
            // least capture the reason before the process dies.
            Services.Trace.Log("crash", $"fatal exception: {ex.ExceptionObject}");
        };
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, ex) =>
        {
            Services.Trace.Log("crash", $"unobserved task exception: {ex.Exception}");
            ex.SetObserved();
        };

        // Parse command-line args. --hidden tells us to start in the tray
        // without showing a window (used when auto-launched at Windows logon).
        StartHidden = e.Args.Any(a =>
            string.Equals(a, "--hidden", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a, "-h",       StringComparison.OrdinalIgnoreCase));

        // Single-instance check
        const string mutexName = "ClipNinjaV2_SingleInstance_F8E2A1";
        _singleInstanceMutex = new Mutex(true, mutexName, out bool isFirst);
        if (!isFirst)
        {
            // Another copy already running. We don't own the mutex.
            _ownsMutex = false;
            Shutdown();
            return;
        }
        _ownsMutex = true;

        base.OnStartup(e);

        _mainWindow = new MainWindow();

        // If launched hidden, keep the window invisible until the user
        // explicitly shows it (via tray icon or Ctrl+Shift+N hotkey).
        // We still need to call Show() at least once so WPF realizes the
        // window's HWND (the clipboard listener needs that HWND), then
        // immediately Hide() it.
        //
        // The Minimized + ShowInTaskbar=false dance avoids any taskbar
        // button flash during the brief Show/Hide. AFTER hiding, we restore
        // both properties so the next time the user reveals the window
        // (via tray menu or Ctrl+Shift+N), it shows up on the taskbar
        // normally — fixing the v2.4.x bug where the window could only be
        // accessed from the "show hidden icons" tray flyout.
        if (StartHidden)
        {
            _mainWindow.WindowState = WindowState.Minimized;
            _mainWindow.ShowInTaskbar = false;
            _mainWindow.Show();
            _mainWindow.Hide();
            // Restore for the normal-use path. These don't take effect
            // until the window is shown again; until then it's just sitting
            // hidden with the right defaults queued.
            _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.ShowInTaskbar = true;
        }
        else
        {
            _mainWindow.Show();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mainWindow?.OnAppExiting();
        try
        {
            // Only release if we actually acquired ownership.
            if (_ownsMutex)
            {
                _singleInstanceMutex?.ReleaseMutex();
            }
        }
        catch
        {
            // Defensive — releasing a mutex you don't own throws
            // ApplicationException. We've already protected against this
            // with _ownsMutex, but extra safety doesn't hurt.
        }
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
