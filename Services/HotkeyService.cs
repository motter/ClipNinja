using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace ClipNinjaV2.Services;

/// <summary>
/// Registers global system-wide hotkeys via Win32 RegisterHotKey.
/// Hotkeys fire even when ClipNinja isn't focused.
/// </summary>
public class HotkeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private IntPtr _hwnd;
    private HwndSource? _src;
    private readonly Dictionary<int, Action> _handlers = new();
    private int _nextId = 1;

    public void AttachTo(Window window)
    {
        var helper = new WindowInteropHelper(window);
        _hwnd = helper.EnsureHandle();
        _src = HwndSource.FromHwnd(_hwnd);
        _src?.AddHook(WndProc);
    }

    /// <summary>
    /// Register a hotkey. Modifiers: Ctrl=2, Shift=4, Alt=1, Win=8 (combine with |).
    /// Returns the hotkey ID; pass to Unregister to remove later.
    /// </summary>
    public int Register(uint modifiers, Key key, Action handler)
    {
        int id = _nextId++;
        uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        if (!RegisterHotKey(_hwnd, id, modifiers, vk))
        {
            // Most likely cause: another app already grabbed this combo.
            return -1;
        }
        _handlers[id] = handler;
        return id;
    }

    public void Unregister(int id)
    {
        if (id < 0) return;
        UnregisterHotKey(_hwnd, id);
        _handlers.Remove(id);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            if (_handlers.TryGetValue(id, out var act))
            {
                act();
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        foreach (var id in _handlers.Keys.ToList())
            UnregisterHotKey(_hwnd, id);
        _handlers.Clear();
        _src?.RemoveHook(WndProc);
    }

    // Convenience constants
    public const uint Ctrl = MOD_CONTROL;
    public const uint CtrlShift = MOD_CONTROL | MOD_SHIFT;
    public const uint CtrlAlt = MOD_CONTROL | MOD_ALT;
}
