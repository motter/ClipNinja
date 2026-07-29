using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClipNinjaV2.Models;

namespace ClipNinjaV2.Views;

/// <summary>
/// Shown right after a screen capture:
///   🔄 Redo             discard this shot and relaunch the capture
///   🖍 Annotate          edit the shot, then return HERE with the
///                        edited version (so you can still save/send)
///   📋 Send to ClipNinja  clipboard → top slot, capture effects
///                        applied, immediately pasteable (Enter)
///   💾 Quick save        instant timestamped PNG into the quick-save
///                        folder — no dialog (falls back to Save as…
///                        if no folder is configured)
///   Save as…             small secondary action: pick name + folder
///
/// Esc = discard. Saving writes the capture as-is (plus annotations);
/// border / torn-edge / shadow effects only bake in on the Send path,
/// since they're paste presentation, not archival.
/// </summary>
public static class CaptureChooser
{
    /// <param name="anchor">Point in VIRTUAL-SCREEN physical pixels that
    /// the popup should appear near — the center of the captured region
    /// or monitor. The popup opens centered on whichever monitor
    /// contains that point, so it shows up where you were working
    /// rather than wherever ClipNinja's main window happens to live.
    /// Null → primary monitor.</param>
    public static void Show(Window owner, BitmapSource shot, AppSettings settings,
        Action<BitmapSource> sendToClipNinja, Action<string> status, Action redoCapture,
        Action? persistSettings = null, Point? anchor = null)
    {
        var current = shot;  // may be replaced by an annotated version

        var dlg = new Window
        {
            Title = "Screenshot captured",
            Owner = owner,
            // Manual placement: positioned on the capture's monitor once
            // we know the popup's measured size (see Loaded below).
            WindowStartupLocation = WindowStartupLocation.Manual,
            WindowStyle = WindowStyle.ToolWindow,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Topmost = true,
            SizeToContent = SizeToContent.WidthAndHeight,
            Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x26, 0x20)),
        };

        var root = new StackPanel { Margin = new Thickness(14) };

        // Forward declaration: the "View / hide full size" link is built
        // in the footer below, but the expand toggle (defined earlier)
        // needs to update its text. Null until the footer creates it.
        TextBlock? viewFullLink = null;

        // Resolve which monitor the popup belongs on and return its work
        // area in DIUs (device-independent units), matching WPF's Left/
        // Top/ActualWidth. anchor is in physical px (capture center);
        // falls back to the primary monitor.
        (double leftDiu, double topDiu, double wDiu, double hDiu) MonitorFor(Point? anchorPt)
        {
            try
            {
                var mons = Services.ScreenCaptureService.GetMonitors();
                var target = mons.FirstOrDefault(m => m.isPrimary);
                if (anchorPt is { } a)
                {
                    foreach (var m in mons)
                    {
                        if (a.X >= m.x && a.X < m.x + m.width &&
                            a.Y >= m.y && a.Y < m.y + m.height)
                        { target = m; break; }
                    }
                }
                if (target.width <= 0) return (0, 0, 0, 0);
                var src = PresentationSource.FromVisual(dlg);
                double sx = src?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
                double sy = src?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;
                if (sx <= 0) sx = 1.0;
                if (sy <= 0) sy = 1.0;
                return (target.x / sx, target.y / sy, target.width / sx, target.height / sy);
            }
            catch { return (0, 0, 0, 0); }
        }

        // Preview — scaled to fit a modest box; the actual bitmap is
        // untouched.
        var preview = new Image
        {
            Source = current,
            MaxWidth = 420,
            MaxHeight = 260,
            Stretch = Stretch.Uniform,
            Margin = new Thickness(0, 0, 0, 6),
            Cursor = Cursors.Hand,
            ToolTip = "Click to view full size",
        };
        root.Children.Add(preview);

        // Toggle the preview between the small thumbnail and a large,
        // in-place view — NO second window. Clicking the preview (or the
        // "View / hide full size" link) grows the same chooser to show
        // the image big, with the action buttons still right there so
        // you can Send / Save / Annotate straight after inspecting.
        // Bounded to the capture's monitor; the window re-centers itself
        // via the Loaded/size-changed placement below.
        bool expanded = false;
        void SetPreviewExpanded(bool big)
        {
            expanded = big;
            var mon = MonitorFor(anchor);
            if (big)
            {
                // Fit within ~85% of the monitor work area (DIU), keeping
                // aspect. Uniform stretch means small captures don't get
                // upscaled past their pixels.
                double capW = mon.wDiu * 0.85, capH = mon.hDiu * 0.85 - 160; // leave room for buttons
                preview.MaxWidth = Math.Max(420, capW);
                preview.MaxHeight = Math.Max(260, capH);
                preview.ToolTip = "Click to shrink back";
            }
            else
            {
                preview.MaxWidth = 420;
                preview.MaxHeight = 260;
                preview.ToolTip = "Click to view full size";
            }
            if (viewFullLink is not null)
                viewFullLink.Text = big ? "🔍 Hide full size" : "🔍 View full size";
            // SizeToContent re-measures; re-center on the next layout pass.
            dlg.Dispatcher.BeginInvoke(new Action(RecenterOnMonitor),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }
        void ShowFullImage() => SetPreviewExpanded(!expanded);
        preview.MouseLeftButtonUp += (_, _) => ShowFullImage();

        var sizeInfo = new TextBlock
        {
            Text = $"{current.PixelWidth} × {current.PixelHeight} px",
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x82, 0x74)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 10),
        };
        root.Children.Add(sizeInfo);

        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        Button MakeButton(string label, string tip, bool isDefault = false)
        {
            var b = new Button
            {
                Content = label,
                Padding = new Thickness(12, 7, 12, 7),
                Margin = new Thickness(0, 0, 8, 0),
                FontSize = 13,
                Cursor = Cursors.Hand,
                ToolTip = tip,
                IsDefault = isDefault,
            };
            btnRow.Children.Add(b);
            return b;
        }

        var redoBtn = MakeButton("🔄 Redo", "Discard this shot and capture again");
        var annotateBtn = MakeButton("🖍 Annotate", "Draw on the shot, then come back here");
        var sendBtn = MakeButton("📋 Send to ClipNinja", "Top slot + clipboard, capture effects applied (Enter)", isDefault: true);
        var quickSaveBtn = MakeButton("💾 Quick save", "Instant timestamped PNG into your quick-save folder");

        // Writes `current` to the given path as PNG. Shared by quick
        // save and Save as….
        bool WritePng(string path)
        {
            try
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(current));
                using var fs = new System.IO.FileStream(path, System.IO.FileMode.Create);
                encoder.Save(fs);
                return true;
            }
            catch (Exception ex)
            {
                status($"Save failed: {ex.Message}");
                Services.Trace.Log("capture", $"chooser save failed: {ex}");
                return false;
            }
        }

        void SaveAsDialog()
        {
            var initialDir = !string.IsNullOrWhiteSpace(settings.QuickSaveFolder)
                             && System.IO.Directory.Exists(settings.QuickSaveFolder)
                ? settings.QuickSaveFolder
                : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            var picker = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Save screenshot",
                Filter = "PNG image (*.png)|*.png",
                DefaultExt = ".png",
                FileName = $"Screenshot {DateTime.Now:yyyy-MM-dd HHmmss}.png",
                InitialDirectory = initialDir,
            };
            if (picker.ShowDialog(dlg) != true) return;  // canceled → stay open
            if (WritePng(picker.FileName))
            {
                status($"✓ Saved: {System.IO.Path.GetFileName(picker.FileName)}");
                dlg.Close();
            }
        }

        redoBtn.Click += (_, _) =>
        {
            dlg.Close();
            // Relaunch AFTER this dialog is fully gone — the capture
            // flow hides windows and freezes the screen, and a dying
            // chooser must not photobomb the new shot.
            owner.Dispatcher.BeginInvoke(new Action(redoCapture),
                System.Windows.Threading.DispatcherPriority.Background);
        };

        annotateBtn.Click += (_, _) =>
        {
            // The annotator opens modally ON TOP of this chooser (it's
            // the owner). In send mode its primary button is "Save &
            // send to ClipNinja" — which sends straight to the tray and
            // closes BOTH windows (no bounce back here). Its secondary
            // "Back to options" returns the edited image to this chooser
            // (for save-as / quick-save on an annotated shot).
            bool sent = false;
            var edited = ImageAnnotator.Show(dlg, current, settings, persistSettings,
                onSendToTray: img =>
                {
                    sendToClipNinja(img);
                    sent = true;
                });
            if (sent) { dlg.Close(); return; }
            if (edited is not null)
            {
                current = edited;
                preview.Source = current;
                sizeInfo.Text = $"{current.PixelWidth} × {current.PixelHeight} px";
            }
        };

        sendBtn.Click += (_, _) =>
        {
            sendToClipNinja(current);
            dlg.Close();
        };

        quickSaveBtn.Click += (_, _) =>
        {
            // No folder configured → graceful fall-through to the
            // named-save dialog rather than an error.
            if (string.IsNullOrWhiteSpace(settings.QuickSaveFolder)
                || !System.IO.Directory.Exists(settings.QuickSaveFolder))
            {
                SaveAsDialog();
                return;
            }
            var path = System.IO.Path.Combine(
                settings.QuickSaveFolder,
                $"Screenshot {DateTime.Now:yyyy-MM-dd HHmmss}.png");
            if (WritePng(path))
            {
                status($"✓ Quick-saved: {System.IO.Path.GetFileName(path)}");
                dlg.Close();
            }
        };

        root.Children.Add(btnRow);

        // Secondary row: Save as… (named save with destination picker)
        // + the key hints.
        var footRow = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        footRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        // Left cluster: "🔍 View full size" + "Save as…".
        var leftLinks = new StackPanel { Orientation = Orientation.Horizontal };
        viewFullLink = new TextBlock
        {
            Text = "🔍 View full size",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0xE5, 0x9A, 0x2A)),
            Cursor = Cursors.Hand,
            TextDecorations = TextDecorations.Underline,
            Margin = new Thickness(0, 0, 14, 0),
            ToolTip = "Show the screenshot large, right here (click again to shrink)",
        };
        viewFullLink.MouseLeftButtonDown += (_, _) => ShowFullImage();
        leftLinks.Children.Add(viewFullLink);
        var saveAsLink = new TextBlock
        {
            Text = "Save as…",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0xE5, 0x9A, 0x2A)),
            Cursor = Cursors.Hand,
            TextDecorations = TextDecorations.Underline,
            ToolTip = "Pick a name and folder",
        };
        saveAsLink.MouseLeftButtonDown += (_, _) => SaveAsDialog();
        leftLinks.Children.Add(saveAsLink);
        Grid.SetColumn(leftLinks, 0);
        footRow.Children.Add(leftLinks);
        var hints = new TextBlock
        {
            Text = "Enter = send  •  Esc = discard",
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x82, 0x74)),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        Grid.SetColumn(hints, 1);
        footRow.Children.Add(hints);
        root.Children.Add(footRow);

        // Place the popup on the monitor the capture came from, centered.
        // WPF window coords are DIUs while monitor rects are physical px;
        // convert via the window's DPI scale. RecenterOnMonitor keeps the
        // popup centered on the capture's monitor after any size change
        // (initial show, and expand/collapse of the preview).
        void RecenterOnMonitor()
        {
            try
            {
                var m = MonitorFor(anchor);
                if (m.wDiu > 0)
                {
                    dlg.Left = m.leftDiu + (m.wDiu - dlg.ActualWidth) / 2;
                    dlg.Top = m.topDiu + (m.hDiu - dlg.ActualHeight) / 2;
                    // Clamp so a tall expanded preview never runs off the
                    // top of the monitor.
                    if (dlg.Top < m.topDiu) dlg.Top = m.topDiu;
                    if (dlg.Left < m.leftDiu) dlg.Left = m.leftDiu;
                }
            }
            catch { /* fall back to wherever WPF put it */ }
        }
        dlg.Loaded += (_, _) =>
        {
            RecenterOnMonitor();
            dlg.Activate();
            dlg.Focus();
        };

        // PreviewKeyDown (tunneling): fires at the window before any
        // focused button can swallow the key, so Esc always discards.
        dlg.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { e.Handled = true; dlg.Close(); }
        };

        dlg.Content = root;
        dlg.ShowDialog();
    }
}
