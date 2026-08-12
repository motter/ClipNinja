using System;
using Microsoft.Win32;

namespace ClipNinjaV2.Services;

/// <summary>
/// Manages the auto-start registry entry under
/// HKCU\Software\Microsoft\Windows\CurrentVersion\Run.
///
/// This is the user-scoped, no-admin-required way to launch an app when
/// the current user signs into Windows. It's what virtually every desktop
/// app with an auto-launch option uses (Slack, Discord, Spotify, etc.).
///
/// When auto-start is enabled, the registry value includes the `--hidden`
/// flag so that the app starts minimized to the system tray, rather than
/// popping a visible window every time the user logs in.
/// </summary>
public static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ClipNinja";
    private const string HiddenArg = "--hidden";

    /// <summary>
    /// True if the Run-key entry exists and points to OUR executable.
    /// (We check the path so that a stale entry from a moved .exe doesn't
    /// register as "enabled".)
    /// </summary>
    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            if (key?.GetValue(ValueName) is not string existing) return false;
            if (string.IsNullOrWhiteSpace(existing)) return false;
            // The value should reference our current .exe (anywhere in the string,
            // since it's quoted + has args).
            var thisExe = CurrentExePath();
            if (string.IsNullOrEmpty(thisExe)) return true;   // can't verify, trust the entry
            return existing.IndexOf(thisExe, StringComparison.OrdinalIgnoreCase) >= 0;
        }
        catch { return false; }
    }

    /// <summary>Register or unregister the current executable for auto-start.</summary>
    public static bool SetEnabled(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key is null) return false;

            if (enable)
            {
                var exe = CurrentExePath();
                if (string.IsNullOrEmpty(exe)) return false;
                // Quoted path + --hidden arg so the app starts in the tray
                key.SetValue(ValueName, $"\"{exe}\" {HiddenArg}");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// If auto-start is enabled, rewrite the registry value with our CURRENT
    /// executable path. Handles the case where the user moved or renamed the
    /// .exe since auto-start was first enabled — the registry self-heals on
    /// the next launch.
    /// </summary>
    public static void SyncRegistryIfEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key?.GetValue(ValueName) is not string existing) return;
            var exe = CurrentExePath();
            if (string.IsNullOrEmpty(exe)) return;
            var expected = $"\"{exe}\" {HiddenArg}";
            if (!string.Equals(existing, expected, StringComparison.OrdinalIgnoreCase))
                key.SetValue(ValueName, expected);
        }
        catch { /* best-effort */ }
    }

    private static string CurrentExePath()
    {
        try
        {
            // Use ProcessPath which works correctly for single-file published apps
            // (Assembly.Location returns the .dll inside the bundle on those builds).
            return Environment.ProcessPath ?? "";
        }
        catch { return ""; }
    }
}
