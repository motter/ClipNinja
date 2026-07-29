using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
    /// <summary>Pending content for the non-slot (history) entry point. Mutually exclusive with _pendingSlot.</summary>
    private ClipContent? _pendingContent;
    /// <summary>Currently-showing content when invoked via ScheduleShowContent (history hover).</summary>
    private ClipContent? _currentContent;

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
        _pendingContent = null;
        _pendingAnchorTop = anchorTop;
        _showTimer.Stop();
        _showTimer.Start();
    }

    /// <summary>
    /// Show the preview popup for an arbitrary ClipContent (not tied to a
    /// slot). Used by the history panel — history items don't have slot
    /// indices but DO have the same kinds of content (text, image, URL,
    /// etc.) and benefit from the same preview UI.
    /// </summary>
    public void ScheduleShowContent(ClipContent content, double anchorTop = double.NaN)
    {
        if (content is null) { Hide(); return; }

        _hideTimer.Stop();

        // Skip if we're already showing this exact content
        if (_popup is not null && ReferenceEquals(_currentContent, content)) return;

        if (_popup is not null)
        {
            _popup.Close();
            _popup = null;
            _currentSlot = null;
            _currentContent = null;
        }

        _pendingSlot = null;
        _pendingContent = content;
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
        _pendingContent = null;
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
        _pendingContent = null;
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
        _currentContent = null;
    }

    private void ActuallyShow()
    {
        // Resolve the content + a display label from whichever entry point
        // queued this show: slot-based (regular slot hover) or content-based
        // (history hover).
        ClipContent? content = null;
        string label;
        if (_pendingSlot is not null && _pendingSlot.Content is not null)
        {
            content = _pendingSlot.Content;
            label = $"slot {_pendingSlot.Index}";
        }
        else if (_pendingContent is not null)
        {
            content = _pendingContent;
            label = "history";
        }
        else
        {
            return;
        }
        if (content is null) return;

        using var _t = Services.Trace.Time("preview", $"ActuallyShow {label} {content.GetType().Name}");

        var workArea = GetMonitorWorkArea(_ownerWindow);
        // Preview is capped at half the monitor work area (was a third).
        // A larger preview means you rarely need to open the image full
        // size just to read it. Still bounded so it never dominates the
        // screen, and MakePreviewBitmap downsamples to fit — text and
        // image content both get the extra room.
        int maxW = (int)(workArea.Width / 2);
        int maxH = (int)(workArea.Height / 2);
        maxW = Math.Max(maxW, 540);
        maxH = Math.Max(maxH, 360);

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
        switch (content)
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
            case TextContent tc when tc.IsUrl:
            {
                // URL preview — show the link prominently with an explicit
                // "Open in browser" button. Keep it compact so it doesn't
                // dominate the screen for what's just a one-line URL.
                var uri = tc.ToUri();
                var sp = new StackPanel { Orientation = Orientation.Vertical };

                // Chain-link icon (blue) next to a header label
                var headerRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
                headerRow.Children.Add(new TextBlock
                {
                    Text = "🔗 Link",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0xB4, 0xFF)),   // light blue
                    FontWeight = FontWeights.Bold,
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                });
                sp.Children.Add(headerRow);

                // The URL text — wraps if long, monospace so it reads cleanly
                var urlBlock = new TextBlock
                {
                    Text = tc.Text,
                    Foreground = (Brush)Application.Current.FindResource("TextBrush"),
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = maxW - 30,
                    Margin = new Thickness(0, 0, 0, 10),
                };
                sp.Children.Add(urlBlock);

                // "Open in browser" button — shells out to default browser
                if (uri is not null)
                {
                    var openBtn = new Button
                    {
                        Content = "🌐  Open in browser",
                        Padding = new Thickness(10, 4, 10, 4),
                        FontSize = 11,
                        Cursor = Cursors.Hand,
                        HorizontalAlignment = HorizontalAlignment.Left,
                    };
                    openBtn.Click += (_, _) =>
                    {
                        try
                        {
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = uri.AbsoluteUri,
                                UseShellExecute = true,
                            });
                        }
                        catch { /* shell-execute failed; silently no-op */ }
                    };
                    sp.Children.Add(openBtn);
                }
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

                // If this text content carries embedded hyperlinks (parsed
                // from the source's CF_HTML at capture), show them as an
                // explicit list under the text with "Open in browser"
                // buttons. The text itself remains plain — preserving the
                // visible content the user copied — but the user can still
                // act on the links from here.
                FrameworkElement bodyContent;
                if (tc.HasLinks)
                {
                    var stack = new StackPanel();
                    stack.Children.Add(tb);
                    stack.Children.Add(BuildLinksSection(tc.Links, maxW));
                    bodyContent = stack;
                }
                else
                {
                    bodyContent = tb;
                }

                var sv = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    MaxHeight = maxH - 30,
                    Content = bodyContent,
                };
                contentElem = sv;
                break;
            }
            default: return;
        }

        // Wrap the content with a small footer line showing when the slot
        // was captured. Helps when comparing multiple similar slots ("which
        // one is the most recent?") without leaving the popup.
        var rootStack = new StackPanel();
        rootStack.Children.Add(contentElem);
        rootStack.Children.Add(new Border
        {
            Height = 1,
            Background = (Brush)Application.Current.FindResource("AccentBrush"),
            Opacity = 0.3,
            Margin = new Thickness(0, 8, 0, 4),
        });
        rootStack.Children.Add(new TextBlock
        {
            Text = $"📅 Captured: {FormatCapturedTime(content.CapturedAt)}",
            FontSize = 10,
            Foreground = (Brush)Application.Current.FindResource("SubTextBrush"),
        });

        var border = new Border
        {
            CornerRadius = new CornerRadius(8),
            Background = (Brush)Application.Current.FindResource("PanelBrush"),
            BorderBrush = (Brush)Application.Current.FindResource("AccentBrush"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10),
            Child = rootStack,
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

        // Track which entry point created this popup so ScheduleShow/
        // ScheduleShowContent can detect "same thing as last hover" and
        // skip recreating the popup.
        _currentSlot = _pendingSlot;
        _currentContent = _pendingContent;

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
    /// <summary>
    /// Format a capture timestamp for the preview footer. Uses friendly
    /// relative wording for very-recent captures and absolute dates for
    /// older ones. Mirrors HistoryItem.RelativeTime's logic but appends
    /// the absolute clock time for clarity.
    /// </summary>
    private static string FormatCapturedTime(DateTime captured)
    {
        var delta = DateTime.Now - captured;
        string when;
        if (delta.TotalSeconds < 60) when = "just now";
        else if (delta.TotalMinutes < 60) when = $"{(int)delta.TotalMinutes} min ago";
        else if (delta.TotalHours < 24) when = $"{(int)delta.TotalHours} hr ago";
        else if (delta.TotalDays < 2) when = "yesterday";
        else if (delta.TotalDays < 7) when = $"{(int)delta.TotalDays} days ago";
        else when = captured.ToString("MMM d, yyyy");
        return $"{when}  ({captured:h:mm tt})";
    }

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

    /// <summary>
    /// Build the "Links found in source" panel shown beneath the text preview
    /// when the slot's content has embedded hyperlinks. Renders each link as a
    /// label + URL pair with a small "Open" button that launches the user's
    /// default browser. URLs are truncated to 80 chars in the displayed line
    /// to keep the popup width manageable; the full URL is in the tooltip.
    /// </summary>
    private static FrameworkElement BuildLinksSection(IReadOnlyList<HyperLink> links, double maxWidth)
    {
        var accent = (Brush)Application.Current.FindResource("AccentBrush");
        var fg = (Brush)Application.Current.FindResource("TextBrush");
        var subFg = (Brush)Application.Current.FindResource("SubTextBrush");

        var container = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };

        // Section divider line
        container.Children.Add(new Border
        {
            Height = 1,
            Background = accent,
            Opacity = 0.45,
            Margin = new Thickness(0, 0, 0, 6),
        });

        // Section header
        container.Children.Add(new TextBlock
        {
            Text = links.Count == 1
                ? "🔗 1 LINK FOUND IN SOURCE"
                : $"🔗 {links.Count} LINKS FOUND IN SOURCE",
            FontWeight = FontWeights.Bold,
            FontSize = 10,
            Foreground = accent,
            Margin = new Thickness(0, 0, 0, 6),
        });

        // Each link → a row with label, URL, and an Open button
        for (int i = 0; i < links.Count; i++)
        {
            var link = links[i];
            var row = new Border
            {
                Margin = new Thickness(0, 0, 0, 4),
                Padding = new Thickness(6, 4, 6, 4),
                CornerRadius = new CornerRadius(3),
                Background = (Brush)Application.Current.FindResource("BgBrush"),
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var textStack = new StackPanel();
            // Label (visible text from the source <a> tag)
            textStack.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(link.Label) ? "(no label)" : link.Label,
                Foreground = fg,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            // URL (truncated visually, full in tooltip)
            string urlShown = link.Url.Length > 80 ? link.Url[..77] + "…" : link.Url;
            textStack.Children.Add(new TextBlock
            {
                Text = urlShown,
                Foreground = subFg,
                FontSize = 9,
                FontFamily = new FontFamily("Consolas"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = link.Url,
            });
            Grid.SetColumn(textStack, 0);
            grid.Children.Add(textStack);

            // Open-in-browser button
            var btn = new Button
            {
                Content = "🌐 Open",
                FontSize = 10,
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = $"Open: {link.Url}",
            };
            var capturedUrl = link.Url;
            btn.Click += (_, _) =>
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = capturedUrl,
                        UseShellExecute = true,
                    });
                }
                catch (System.Exception ex)
                {
                    MessageBox.Show($"Couldn't open link:\n{ex.Message}",
                        "ClipNinja", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            };
            Grid.SetColumn(btn, 1);
            grid.Children.Add(btn);

            row.Child = grid;
            container.Children.Add(row);
        }

        return container;
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
