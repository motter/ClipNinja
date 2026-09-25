using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ClipNinjaV2.Views
{
    /// <summary>
    /// Full-screen dimmed overlay for picking which monitor to capture.
    /// Spans the whole virtual desktop, dims everything, and paints a big
    /// number badge centered on each monitor. The user presses the number
    /// key for the screen they want, "A" for all monitors, or Esc to
    /// cancel. Returns the chosen monitor index (0-based), -1 for "all",
    /// or null if cancelled.
    /// </summary>
    public static class MonitorPickerWindow
    {
        /// <summary>Result of the picker.</summary>
        public enum Choice { Cancel, Monitor, All }

        /// <summary>Show the picker. Returns (choice, monitorIndex).
        /// monitorIndex is valid only when choice == Monitor.</summary>
        public static (Choice choice, int index) Pick()
        {
            var mons = Services.ScreenCaptureService.GetMonitors();
            if (mons.Count == 0) return (Choice.Cancel, -1);

            // WPF window coords are DIUs; monitor rects are physical px.
            // Convert using the primary DPI scale (good enough for
            // positioning the badges; the overlay itself uses the WPF
            // virtual-screen DIUs).
            var result = (Choice.Cancel, -1);

            var canvas = new Canvas();
            var root = new Grid
            {
                Background = new SolidColorBrush(Color.FromArgb(0x8C, 0x10, 0x0E, 0x0B)), // dim
            };
            root.Children.Add(canvas);

            var win = new Window
            {
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                AllowsTransparency = true,
                ShowInTaskbar = false,
                Topmost = true,
                Background = System.Windows.Media.Brushes.Transparent,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = SystemParameters.VirtualScreenLeft,
                Top = SystemParameters.VirtualScreenTop,
                Width = SystemParameters.VirtualScreenWidth,
                Height = SystemParameters.VirtualScreenHeight,
                Content = root,
                Cursor = Cursors.Hand,
            };

            // Monitor rects come back in physical pixels; the overlay
            // window is laid out in DIUs. Convert with the virtual-screen
            // scale (DIU width / physical width). Positions are relative to
            // the virtual-screen origin.
            var (vxPx, vyPx, vwPx, vhPx) = Services.ScreenCaptureService.GetVirtualScreenBounds();
            double sx = vwPx > 0 ? SystemParameters.VirtualScreenWidth / vwPx : 1.0;
            double sy = vhPx > 0 ? SystemParameters.VirtualScreenHeight / vhPx : 1.0;

            // A number badge + border per monitor, positioned at the
            // monitor's center in overlay (DIU) coordinates.
            for (int i = 0; i < mons.Count; i++)
            {
                var m = mons[i];
                // Monitor rect in overlay DIUs (relative to virtual origin).
                double mx = (m.x - vxPx) * sx;
                double my = (m.y - vyPx) * sy;
                double mw = m.width * sx;
                double mh = m.height * sy;

                // Subtle outline around each monitor so its extent is clear.
                var outline = new System.Windows.Shapes.Rectangle
                {
                    Width = Math.Max(1, mw - 8),
                    Height = Math.Max(1, mh - 8),
                    Stroke = new SolidColorBrush(Color.FromArgb(0x66, 0xE5, 0x9A, 0x2A)),
                    StrokeThickness = 2,
                    RadiusX = 10,
                    RadiusY = 10,
                    IsHitTestVisible = false,
                };
                Canvas.SetLeft(outline, mx + 4);
                Canvas.SetTop(outline, my + 4);
                canvas.Children.Add(outline);

                // Big number badge in the middle of the monitor.
                var badge = new Border
                {
                    Width = 120,
                    Height = 120,
                    CornerRadius = new CornerRadius(60),
                    Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x2A, 0x26, 0x20)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0x9A, 0x2A)),
                    BorderThickness = new Thickness(3),
                    Child = new TextBlock
                    {
                        Text = (i + 1).ToString(),
                        FontSize = 64,
                        FontWeight = FontWeights.Bold,
                        Foreground = System.Windows.Media.Brushes.White,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                };
                Canvas.SetLeft(badge, mx + mw / 2 - 60);
                Canvas.SetTop(badge, my + mh / 2 - 70);
                canvas.Children.Add(badge);

                // Per-monitor label under the badge.
                var label = new TextBlock
                {
                    Text = m.isPrimary ? $"Monitor {i + 1} (primary)" : $"Monitor {i + 1}",
                    FontSize = 16,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xE5, 0x9A, 0x2A)),
                    Width = mw,
                    TextAlignment = TextAlignment.Center,
                    IsHitTestVisible = false,
                };
                Canvas.SetLeft(label, mx);
                Canvas.SetTop(label, my + mh / 2 + 58);
                canvas.Children.Add(label);

                // Click the badge as an alternative to pressing the number.
                int captured = i;
                badge.Cursor = Cursors.Hand;
                badge.MouseLeftButtonUp += (_, e) =>
                {
                    e.Handled = true;
                    result = (Choice.Monitor, captured);
                    win.Close();
                };
            }

            // Central hint banner (on the primary monitor).
            var prim = mons.FindIndex(m => m.isPrimary);
            if (prim < 0) prim = 0;
            var pm = mons[prim];
            double pmx = (pm.x - vxPx) * sx;
            double pmy = (pm.y - vyPx) * sy;
            var hint = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x1A, 0x17, 0x12)),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(18, 10, 18, 10),
                Child = new TextBlock
                {
                    Text = mons.Count > 1
                        ? $"Press 1–{mons.Count} to capture that screen  •  A = all screens  •  Esc = cancel"
                        : "Press 1 or A to capture  •  Esc = cancel",
                    FontSize = 18,
                    Foreground = System.Windows.Media.Brushes.White,
                    FontWeight = FontWeights.SemiBold,
                },
                IsHitTestVisible = false,
            };
            hint.Loaded += (_, _) =>
            {
                hint.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(hint, pmx + (pm.width * sx) / 2 - hint.DesiredSize.Width / 2);
                Canvas.SetTop(hint, pmy + 60);
            };
            canvas.Children.Add(hint);

            win.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape) { result = (Choice.Cancel, -1); win.Close(); return; }
                if (e.Key == Key.A) { result = (Choice.All, -1); win.Close(); return; }
                // Number keys 1..9 (top row and numpad).
                int num = -1;
                if (e.Key >= Key.D1 && e.Key <= Key.D9) num = e.Key - Key.D1;
                else if (e.Key >= Key.NumPad1 && e.Key <= Key.NumPad9) num = e.Key - Key.NumPad1;
                if (num >= 0 && num < mons.Count)
                {
                    result = (Choice.Monitor, num);
                    win.Close();
                }
            };
            // Clicking empty (dimmed) space cancels.
            root.MouseLeftButtonUp += (_, _) => { win.Close(); };

            win.Loaded += (_, _) => { win.Activate(); win.Focus(); Keyboard.Focus(win); };
            win.ShowDialog();
            return result;
        }
    }
}
