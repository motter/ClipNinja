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

        // Preview — scaled to fit a modest box; the actual bitmap is
        // untouched.
        var preview = new Image
        {
            Source = current,
            MaxWidth = 420,
            MaxHeight = 260,
            Stretch = Stretch.Uniform,
            Margin = new Thickness(0, 0, 0, 6),
        };
        root.Children.Add(preview);

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
        Grid.SetColumn(saveAsLink, 0);
        footRow.Children.Add(saveAsLink);
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
        // convert via the window's DPI scale.
        dlg.Loaded += (_, _) =>
        {
            try
            {
                var mons = Services.ScreenCaptureService.GetMonitors();
                var target = mons.FirstOrDefault(m => m.isPrimary);
                if (anchor is { } a)
                {
                    foreach (var m in mons)
                    {
                        if (a.X >= m.x && a.X < m.x + m.width &&
                            a.Y >= m.y && a.Y < m.y + m.height)
                        { target = m; break; }
                    }
                }
                if (target.width > 0)
                {
                    var src = PresentationSource.FromVisual(dlg);
                    double sx = src?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
                    double sy = src?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;
                    if (sx <= 0) sx = 1.0;
                    if (sy <= 0) sy = 1.0;
                    // Monitor rect in DIUs, then center the (already
                    // measured, thanks to SizeToContent) popup in it.
                    double mLeft = target.x / sx, mTop = target.y / sy;
                    double mW = target.width / sx, mH = target.height / sy;
                    dlg.Left = mLeft + (mW - dlg.ActualWidth) / 2;
                    dlg.Top = mTop + (mH - dlg.ActualHeight) / 2;
                }
            }
            catch { /* fall back to wherever WPF put it */ }
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
