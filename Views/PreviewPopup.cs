using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ClipNinjaV2.Models;

namespace ClipNinjaV2.Views;

/// <summary>
/// Hover preview popup. Sized at most 1/3 of screen; positioned to the LEFT
/// of the main window if there's room, otherwise to the RIGHT.
/// 350ms hover delay so brief mouse passes don't trigger it.
/// </summary>
public class PreviewPopup
{
    private readonly Window _ownerWindow;
    private Window? _popup;
    private readonly DispatcherTimer _showTimer;
    private readonly DispatcherTimer _hideTimer;
    private ClipSlot? _pendingSlot;
    private ClipSlot? _currentSlot;   // slot whose preview is currently shown (or about to be)

    public PreviewPopup(Window owner)
    {
        _ownerWindow = owner;
        _showTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _showTimer.Tick += (_, _) => { _showTimer.Stop(); ActuallyShow(); };

        // Grace period after leaving a slot row before we close the popup.
        // Long enough to let the user's cursor reach the popup, short enough
        // that flicking between slots feels responsive.
        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(280) };
        _hideTimer.Tick += (_, _) => { _hideTimer.Stop(); ActuallyHide(); };
    }

    private double _pendingAnchorTop;

    public void ScheduleShow(ClipSlot slot, double anchorTop = double.NaN)
    {
        if (slot.IsEmpty || slot.Content is null) { Hide(); return; }

        // Cancel any pending hide — user has hovered something new
        _hideTimer.Stop();

        // If we're already showing the popup for THIS slot, leave it alone
        if (_popup is not null && ReferenceEquals(_currentSlot, slot)) return;

        // Different slot: snap immediately to a new preview (no fade flash).
        // Close the existing popup synchronously before scheduling the next.
        if (_popup is not null)
        {
            _popup.Close();
            _popup = null;
            _currentSlot = null;
        }

        _pendingSlot = slot;
        _pendingAnchorTop = anchorTop;
        _showTimer.Stop();
        _showTimer.Start();
    }

    /// <summary>
    /// Request to hide the preview. Starts a short grace period; if the
    /// cursor enters the preview window during that window, the hide is
    /// canceled (so the user can interact with the preview, e.g. double-click
    /// to open full-size).
    /// </summary>
    public void Hide()
    {
        // Cancel any pending show
        _showTimer.Stop();
        _pendingSlot = null;
        // Schedule hide with grace period
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    /// <summary>Force-close immediately (e.g. on app shutdown).</summary>
    public void HideImmediate()
    {
        _showTimer.Stop();
        _hideTimer.Stop();
        _pendingSlot = null;
        ActuallyHide();
    }

    private void ActuallyHide()
    {
        if (_popup is not null)
        {
            _popup.Close();
            _popup = null;
        }
        _currentSlot = null;
    }

    private void ActuallyShow()
    {
        var slot = _pendingSlot;
        if (slot is null || slot.Content is null) return;
        using var _t = Services.Trace.Time("preview", $"ActuallyShow slot {slot.Index} {slot.Content.GetType().Name}");

        var workArea = GetMonitorWorkArea(_ownerWindow);
        int maxW = (int)(workArea.Width / 3);
        int maxH = (int)(workArea.Height / 3);
        maxW = Math.Max(maxW, 360);
        maxH = Math.Max(maxH, 240);

        var popup = new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Topmost = true,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            Owner = _ownerWindow,
            Focusable = false,
            ShowActivated = false,
        };

        FrameworkElement contentElem;
        switch (slot.Content)
        {
            case ImageContent ic when ic.FullImage is not null:
            {
                var previewBmp = MakePreviewBitmap(ic.FullImage, maxW - 30, maxH - 60);
                var img = new Image
                {
                    Source = previewBmp,
                    Stretch = Stretch.Uniform,
                    Width = previewBmp.PixelWidth,
                    Height = previewBmp.PixelHeight,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = "Double-click to open full size",
                };
                // Capture the FullImage in the closure so the handler can use it
                var fullImg = ic.FullImage;
                img.MouseLeftButtonDown += (s, e) =>
                {
                    if (e.ClickCount == 2)
                    {
                        OpenFullSize(fullImg);
                        e.Handled = true;
                    }
                };
                var sp = new StackPanel { Orientation = Orientation.Vertical };
                sp.Children.Add(img);
                sp.Children.Add(new TextBlock
                {
                    Text = $"{ic.DisplayLabel} • {ic.OriginalWidth}×{ic.OriginalHeight}  (double-click to open)",
                    Foreground = (Brush)Application.Current.FindResource("SubTextBrush"),
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 10,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 6, 0, 0),
                });
                contentElem = sp;
                break;
            }
            case TextContent tc:
            {
                var displayText = tc.Text.Length > 4000
                    ? tc.Text[..4000] + "\n…[truncated]"
                    : tc.Text;
                var tb = new TextBlock
                {
                    Text = displayText,
                    Foreground = (Brush)Application.Current.FindResource("TextBrush"),
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = maxW - 20,
                };
                var sv = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    MaxHeight = maxH - 30,
                    Content = tb,
                };
                contentElem = sv;
                break;
            }
            default: return;
        }

        var border = new Border
        {
            CornerRadius = new CornerRadius(8),
            Background = (Brush)Application.Current.FindResource("PanelBrush"),
            BorderBrush = (Brush)Application.Current.FindResource("AccentBrush"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10),
            Child = contentElem,
        };

        popup.Content = border;
        popup.SizeToContent = SizeToContent.WidthAndHeight;
        popup.MaxWidth = maxW;
        popup.MaxHeight = maxH;

        // Show off-screen to measure, then reposition
        popup.Left = -10000;
        popup.Top = -10000;
        popup.Show();
        popup.UpdateLayout();
        var popupW = popup.ActualWidth;
        var popupH = popup.ActualHeight;

        var ownerLeft = _ownerWindow.Left;
        var ownerTop = _ownerWindow.Top;
        var ownerWidth = _ownerWindow.ActualWidth;

        double leftOption = ownerLeft - popupW - 8;
        double rightOption = ownerLeft + ownerWidth + 8;
        double targetLeft;
        if (leftOption >= workArea.X)
            targetLeft = leftOption;
        else if (rightOption + popupW <= workArea.Right)
            targetLeft = rightOption;
        else
            targetLeft = Math.Max(workArea.X, ownerLeft - popupW - 8);

        // Vertical: align to the hovered slot if anchor was provided,
        // otherwise fall back to top-of-window. We center the popup
        // vertically on the slot row (which is ~40px tall) — slot
        // mid-line is anchorTop + 20, popup mid is popupH/2.
        double targetTop;
        if (!double.IsNaN(_pendingAnchorTop) && _pendingAnchorTop > 0)
        {
            // WPF Top is in DIPs; PointToScreen returned pixels. Convert if needed.
            // For simplicity, both are typically the same on most monitors;
            // we'll use the raw value and clamp to screen bounds below.
            double dpiScale = VisualTreeHelper.GetDpi(_ownerWindow).DpiScaleY;
            double anchorDip = _pendingAnchorTop / dpiScale;
            // Place popup so its vertical center aligns with the slot row's center
            targetTop = anchorDip + 20 - popupH / 2;
        }
        else
        {
            targetTop = ownerTop;
        }

        if (targetTop + popupH > workArea.Bottom)
            targetTop = workArea.Bottom - popupH - 4;
        if (targetTop < workArea.Top)
            targetTop = workArea.Top + 4;

        popup.Left = targetLeft;
        popup.Top = targetTop;

        // Track which slot this popup is for (so ScheduleShow can detect "same slot")
        _currentSlot = slot;

        // When cursor enters the popup, cancel any pending hide.
        // When it leaves the popup, start the hide grace period.
        popup.MouseEnter += (_, _) => _hideTimer.Stop();
        popup.MouseLeave += (_, _) =>
        {
            _hideTimer.Stop();
            _hideTimer.Start();
        };

        _popup = popup;
    }

    /// <summary>
    /// Save the bitmap to a temp PNG file and open it with whatever app is
    /// registered as the default for PNGs (usually Windows Photos / Photo Viewer).
    /// Reuses a stable filename per session so multiple double-clicks don't
    /// flood Temp with PNGs.
    /// </summary>
    private static void OpenFullSize(System.Windows.Media.Imaging.BitmapSource bmp)
    {
        try
        {
            var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ClipNinja");
            System.IO.Directory.CreateDirectory(dir);
            // Use a hash of the bitmap dimensions so the same image reuses the same file
            string name = $"clipninja_preview_{bmp.PixelWidth}x{bmp.PixelHeight}_{Math.Abs(bmp.GetHashCode()):X}.png";
            var path = System.IO.Path.Combine(dir, name);

            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
            using (var fs = new System.IO.FileStream(path, System.IO.FileMode.Create))
                encoder.Save(fs);

            // ShellExecute the file — Windows opens it with the default app for .png
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            };
            System.Diagnostics.Process.Start(psi);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Couldn't open full-size image: {ex.Message}",
                            "ClipNinja", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Re-decode a source bitmap at preview size for bulletproof display.</summary>
    private static BitmapSource MakePreviewBitmap(BitmapSource src, int maxW, int maxH)
    {
        try
        {
            double scale = Math.Min((double)maxW / src.PixelWidth, (double)maxH / src.PixelHeight);
            scale = Math.Min(scale, 1.0);
            int targetW = Math.Max(1, (int)(src.PixelWidth * scale));

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(src));
            using var ms = new System.IO.MemoryStream();
            encoder.Save(ms);
            ms.Position = 0;

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bmp.DecodePixelWidth = targetW;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return src; }
    }

    private static Rect GetMonitorWorkArea(Window window)
    {
        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return GetPrimaryWorkArea();
            const uint MONITOR_DEFAULTTONEAREST = 2;
            var hMon = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (hMon == IntPtr.Zero) return GetPrimaryWorkArea();
            var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(hMon, ref info)) return GetPrimaryWorkArea();
            return new Rect(info.rcWork.Left, info.rcWork.Top,
                info.rcWork.Right - info.rcWork.Left,
                info.rcWork.Bottom - info.rcWork.Top);
        }
        catch { return GetPrimaryWorkArea(); }
    }

    private static Rect GetPrimaryWorkArea()
    {
        var wa = SystemParameters.WorkArea;
        return new Rect(wa.Left, wa.Top, wa.Width, wa.Height);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
}
