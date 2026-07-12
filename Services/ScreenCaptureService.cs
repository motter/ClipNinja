using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace ClipNinjaV2.Services;

/// <summary>
/// Screen capture via GDI BitBlt — zero external dependencies (no
/// System.Drawing.Common package needed). Captures the virtual screen
/// (all monitors) or an arbitrary rectangle of it into a BitmapSource.
///
/// Coordinates are physical device pixels in virtual-screen space:
/// the primary monitor's top-left is (0,0) and monitors left/above it
/// have negative coordinates. Callers get the virtual bounds from
/// <see cref="GetVirtualScreenBounds"/> and pass rectangles in that
/// same space to <see cref="CaptureRegion"/>.
/// </summary>
public static class ScreenCaptureService
{
    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;
    private const int SRCCOPY = 0x00CC0020;
    private const int CAPTUREBLT = 0x40000000;

    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int nIndex);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int width, int height);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr hdcDest, int x, int y, int cx, int cy,
        IntPtr hdcSrc, int x1, int y1, int rop);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr hObject);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);

    /// <summary>Virtual-screen bounds in physical pixels. X/Y can be
    /// negative on multi-monitor layouts.</summary>
    public static (int x, int y, int width, int height) GetVirtualScreenBounds()
        => (GetSystemMetrics(SM_XVIRTUALSCREEN),
            GetSystemMetrics(SM_YVIRTUALSCREEN),
            GetSystemMetrics(SM_CXVIRTUALSCREEN),
            GetSystemMetrics(SM_CYVIRTUALSCREEN));

    // ── Monitor enumeration ───────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;   // MONITORINFOF_PRIMARY = 1
    }

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);
    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO info);

    /// <summary>One entry per physical monitor: bounds in virtual-screen
    /// physical pixels (same space CaptureRegion uses) and whether it's
    /// the primary display. Ordered left-to-right then top-to-bottom so
    /// "Monitor 1 / 2 / 3" matches how people think about a desk layout,
    /// with a stable tiebreak.</summary>
    public static List<(int x, int y, int width, int height, bool isPrimary)> GetMonitors()
    {
        var list = new List<(int, int, int, int, bool)>();
        try
        {
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMon, IntPtr _, ref RECT __, IntPtr ___) =>
            {
                var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                if (GetMonitorInfo(hMon, ref info))
                {
                    var r = info.rcMonitor;
                    list.Add((r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, (info.dwFlags & 1) != 0));
                }
                return true;
            }, IntPtr.Zero);
        }
        catch (Exception ex)
        {
            Trace.Log("capture", $"monitor enumeration failed: {ex.Message}");
        }
        return list.OrderBy(m => m.Item1).ThenBy(m => m.Item2)
                   .Select(m => (m.Item1, m.Item2, m.Item3, m.Item4, m.Item5))
                   .ToList();
    }

    /// <summary>Capture a single monitor by index into the GetMonitors
    /// ordering (0-based). Returns null if the index is stale (monitor
    /// unplugged between menu-open and click) or capture fails.</summary>
    public static BitmapSource? CaptureMonitor(int index)
    {
        var mons = GetMonitors();
        if (index < 0 || index >= mons.Count) return null;
        var m = mons[index];
        return CaptureRegion(m.x, m.y, m.width, m.height);
    }

    /// <summary>Capture the entire virtual screen (all monitors).</summary>
    public static BitmapSource? CaptureFullScreen()
    {
        var (x, y, w, h) = GetVirtualScreenBounds();
        return CaptureRegion(x, y, w, h);
    }

    /// <summary>Capture an arbitrary rectangle in virtual-screen
    /// physical-pixel coordinates. Returns null on failure (locked
    /// desktop, zero-size region, GDI exhaustion…).</summary>
    public static BitmapSource? CaptureRegion(int x, int y, int width, int height)
    {
        if (width <= 0 || height <= 0) return null;
        IntPtr screenDc = IntPtr.Zero, memDc = IntPtr.Zero, hBmp = IntPtr.Zero, oldBmp = IntPtr.Zero;
        try
        {
            screenDc = GetDC(IntPtr.Zero);
            if (screenDc == IntPtr.Zero) return null;
            memDc = CreateCompatibleDC(screenDc);
            hBmp = CreateCompatibleBitmap(screenDc, width, height);
            if (memDc == IntPtr.Zero || hBmp == IntPtr.Zero) return null;
            oldBmp = SelectObject(memDc, hBmp);
            // CAPTUREBLT includes layered windows (tooltips, some
            // overlays) — matches what the user actually sees.
            if (!BitBlt(memDc, 0, 0, width, height, screenDc, x, y, SRCCOPY | CAPTUREBLT))
                return null;

            var source = Imaging.CreateBitmapSourceFromHBitmap(
                hBmp, IntPtr.Zero, Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            // Normalize to 96 DPI like every other ClipNinja bitmap so
            // paste sizes are consistent (see AddBlackBorder's notes).
            var normalized = new WriteableBitmap(new FormatConvertedBitmap(
                source, System.Windows.Media.PixelFormats.Bgra32, null, 0));
            normalized.Freeze();
            return normalized;
        }
        catch (Exception ex)
        {
            Trace.Log("capture", $"screen capture failed: {ex.Message}");
            return null;
        }
        finally
        {
            if (oldBmp != IntPtr.Zero) SelectObject(memDc, oldBmp);
            if (hBmp != IntPtr.Zero) DeleteObject(hBmp);
            if (memDc != IntPtr.Zero) DeleteDC(memDc);
            if (screenDc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screenDc);
        }
    }
}
