using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
// ImplicitUsings imports System.IO globally, which also defines Path —
// alias to the Shapes one (same fix as ImageAnnotator.cs).
using Path = System.Windows.Shapes.Path;

namespace ClipNinjaV2.Views;

/// <summary>
/// Full-virtual-screen region selector, Greenshot-style: the screen is
/// frozen (we capture it FIRST, then display that capture as the
/// window background), dimmed, and the user drags a rectangle. The
/// dragged area shows the un-dimmed image plus a size readout.
///
///  • Drag + release → returns the selected crop of the frozen capture
///  • Enter → capture the full virtual screen
///  • Esc / right-click → cancel (returns null)
///
/// Working from a frozen capture (rather than live-cropping on
/// release) means what you see during selection is EXACTLY what you
/// get — no tooltip flicker or window movement between selection and
/// capture. It also sidesteps the need to hide the overlay before
/// capturing.
///
/// DPI note: the overlay is laid out in WPF DIUs while the capture is
/// physical pixels. We map DIU → pixel via the ratio of capture size
/// to window size, which handles uniform scaling. (Mixed per-monitor
/// scale factors can drift a few px at monitor seams — acceptable for
/// v1; per-monitor DPI awareness is a future refinement.)
/// </summary>
public static class RegionSelectorWindow
{
    public static BitmapSource? SelectAndCapture()
    {
        // Freeze the screen first.
        var frozen = Services.ScreenCaptureService.CaptureFullScreen();
        if (frozen is null) return null;
        var (vx, vy, vw, vh) = Services.ScreenCaptureService.GetVirtualScreenBounds();

        BitmapSource? result = null;

        var win = new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Topmost = true,
            AllowsTransparency = false,
            Background = Brushes.Black,
            Cursor = Cursors.Cross,
            // Position over the whole virtual screen. SystemParameters
            // gives DIUs; combined with WindowStartupLocation.Manual
            // this spans all monitors.
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = SystemParameters.VirtualScreenLeft,
            Top = SystemParameters.VirtualScreenTop,
            Width = SystemParameters.VirtualScreenWidth,
            Height = SystemParameters.VirtualScreenHeight,
        };

        var root = new Grid();

        // Layer 1: the frozen screenshot, stretched to fill (DIU↔pixel
        // scaling handled by the stretch).
        root.Children.Add(new Image { Source = frozen, Stretch = Stretch.Fill });

        // Layer 2: dim veil with a rectangular hole for the selection.
        // Implemented as a Path with EvenOdd fill: outer rect = whole
        // window, inner rect = selection → inner area stays undimmed.
        var outerRect = new RectangleGeometry(new Rect(0, 0, win.Width, win.Height));
        var holeRect = new RectangleGeometry(new Rect(0, 0, 0, 0));
        var veilGroup = new GeometryGroup { FillRule = FillRule.EvenOdd };
        veilGroup.Children.Add(outerRect);
        veilGroup.Children.Add(holeRect);
        root.Children.Add(new Path
        {
            Data = veilGroup,
            Fill = new SolidColorBrush(Color.FromArgb(0x90, 0x00, 0x00, 0x00)),
        });

        // Layer 3: selection border + size label.
        var selBorder = new Rectangle
        {
            Stroke = new SolidColorBrush(Color.FromRgb(0xE5, 0x9A, 0x2A)),  // amber accent
            StrokeThickness = 1.5,
            Visibility = Visibility.Collapsed,
        };
        var selCanvas = new Canvas();
        selCanvas.Children.Add(selBorder);
        var sizeLabel = new TextBlock
        {
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromArgb(0xB0, 0x00, 0x00, 0x00)),
            Padding = new Thickness(6, 2, 6, 2),
            FontSize = 12,
            FontFamily = new FontFamily("Consolas"),
            Visibility = Visibility.Collapsed,
        };
        selCanvas.Children.Add(sizeLabel);
        root.Children.Add(selCanvas);

        // Hint bar (top-center) so first-time use is self-explanatory.
        root.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xB0, 0x00, 0x00, 0x00)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12, 5, 12, 5),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 12, 0, 0),
            Child = new TextBlock
            {
                Text = "Drag to select a region  •  1-4 = that monitor  •  Enter = all monitors  •  Esc = cancel",
                Foreground = Brushes.White,
                FontSize = 12,
            },
        });

        win.Content = root;

        // DIU → physical pixel scale factors for mapping the selection
        // onto the capture.
        double scaleX = frozen.PixelWidth / win.Width;
        double scaleY = frozen.PixelHeight / win.Height;

        Point dragStart = default;
        bool dragging = false;

        void UpdateSelectionVisual(Point a, Point b)
        {
            double x = Math.Min(a.X, b.X), y = Math.Min(a.Y, b.Y);
            double w = Math.Abs(b.X - a.X), h = Math.Abs(b.Y - a.Y);
            holeRect.Rect = new Rect(x, y, w, h);
            Canvas.SetLeft(selBorder, x);
            Canvas.SetTop(selBorder, y);
            selBorder.Width = w;
            selBorder.Height = h;
            selBorder.Visibility = Visibility.Visible;
            sizeLabel.Text = $"{(int)(w * scaleX)} × {(int)(h * scaleY)} px";
            // Keep the label just below the selection, flipping above if
            // there's no room.
            double labelY = y + h + 6;
            if (labelY > win.Height - 30) labelY = Math.Max(0, y - 26);
            Canvas.SetLeft(sizeLabel, Math.Clamp(x, 0, win.Width - 120));
            Canvas.SetTop(sizeLabel, labelY);
            sizeLabel.Visibility = Visibility.Visible;
        }

        win.MouseLeftButtonDown += (_, e) =>
        {
            dragStart = e.GetPosition(root);
            dragging = true;
            win.CaptureMouse();
        };
        win.MouseMove += (_, e) =>
        {
            if (dragging) UpdateSelectionVisual(dragStart, e.GetPosition(root));
        };
        win.MouseLeftButtonUp += (_, e) =>
        {
            if (!dragging) return;
            dragging = false;
            win.ReleaseMouseCapture();
            var end = e.GetPosition(root);
            int px = (int)(Math.Min(dragStart.X, end.X) * scaleX);
            int py = (int)(Math.Min(dragStart.Y, end.Y) * scaleY);
            int pw = (int)(Math.Abs(end.X - dragStart.X) * scaleX);
            int ph = (int)(Math.Abs(end.Y - dragStart.Y) * scaleY);
            // Tiny selections are almost always accidental clicks.
            if (pw < 5 || ph < 5) { selBorder.Visibility = Visibility.Collapsed; sizeLabel.Visibility = Visibility.Collapsed; holeRect.Rect = new Rect(0, 0, 0, 0); return; }
            px = Math.Clamp(px, 0, frozen.PixelWidth - 1);
            py = Math.Clamp(py, 0, frozen.PixelHeight - 1);
            pw = Math.Clamp(pw, 1, frozen.PixelWidth - px);
            ph = Math.Clamp(ph, 1, frozen.PixelHeight - py);
            try
            {
                var crop = new CroppedBitmap(frozen, new Int32Rect(px, py, pw, ph));
                var flat = new WriteableBitmap(crop);
                flat.Freeze();
                result = flat;
            }
            catch { result = null; }
            win.Close();
        };
        win.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { result = null; win.Close(); }
            else if (e.Key == Key.Enter) { result = frozen; win.Close(); }
            else
            {
                // Number keys 1-9 (top row or numpad) capture that
                // monitor — cropped from the SAME frozen capture, so
                // what was on screen when the selector opened is what
                // you get. Monitor order matches the tray submenu
                // (left-to-right, top-to-bottom).
                int idx = e.Key switch
                {
                    >= Key.D1 and <= Key.D9 => e.Key - Key.D1,
                    >= Key.NumPad1 and <= Key.NumPad9 => e.Key - Key.NumPad1,
                    _ => -1,
                };
                if (idx < 0) return;
                var mons = Services.ScreenCaptureService.GetMonitors();
                if (idx >= mons.Count) return;
                var m = mons[idx];
                // Monitor rect is in virtual-screen coords; the frozen
                // bitmap's (0,0) is the virtual origin (vx, vy).
                int cx = Math.Clamp(m.x - vx, 0, frozen.PixelWidth - 1);
                int cy = Math.Clamp(m.y - vy, 0, frozen.PixelHeight - 1);
                int cw = Math.Clamp(m.width, 1, frozen.PixelWidth - cx);
                int ch = Math.Clamp(m.height, 1, frozen.PixelHeight - cy);
                try
                {
                    var crop = new CroppedBitmap(frozen, new Int32Rect(cx, cy, cw, ch));
                    var flat = new WriteableBitmap(crop);
                    flat.Freeze();
                    result = flat;
                }
                catch { result = null; }
                win.Close();
            }
        };
        win.MouseRightButtonDown += (_, _) => { result = null; win.Close(); };

        win.ShowDialog();
        return result;
    }
}
