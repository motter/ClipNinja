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
        if (StartHidden)
        {
            _mainWindow.WindowState = WindowState.Minimized;
            _mainWindow.ShowInTaskbar = false;
            _mainWindow.Show();
            _mainWindow.Hide();
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
