using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using ClipNinjaV2.Models;
using ClipNinjaV2.ViewModels;

namespace ClipNinjaV2.Views;

/// <summary>
/// Settings & About dialog. Exposes the same toggles as the tray menu but
/// in a centralized place where they're easier to discover, plus an "About"
/// section with version info, data-folder path, and a couple of utility
/// actions (open data folder, clear all slots, reset settings to defaults).
///
/// Modal owned by the main window. Returns nothing — settings are written
/// directly to the AppSettings instance passed in, and the caller saves.
/// </summary>
public static class SettingsDialog
{
    public static void Show(Window owner, AppSettings settings, MainViewModel vm,
                            Action onSettingsChanged)
    {
        var dlg = new Window
        {
            Owner = owner,
            Title = "ClipNinja Settings",
            Width = 480,
            Height = 620,
            MinWidth = 420,
            MinHeight = 400,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanResize,
            ShowInTaskbar = false,
            Background = (Brush)Application.Current.FindResource("PanelBrush"),
        };

        // Try to use the same panel/text brushes as the main window for visual cohesion
        var fg = (Brush)Application.Current.FindResource("TextBrush");
        var subFg = (Brush)Application.Current.FindResource("SubTextBrush");
        var accent = (Brush)Application.Current.FindResource("AccentBrush");

        var root = new StackPanel { Margin = new Thickness(18) };

        // ── Header ─────────────────────────────────────────────────────
        var headerRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 14),
        };
        headerRow.Children.Add(new TextBlock
        {
            Text = "🥷",
            FontSize = 24,
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        var headerText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        headerText.Children.Add(new TextBlock
        {
            Text = "ClipNinja",
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = fg,
        });
        headerText.Children.Add(new TextBlock
        {
            Text = $"v{GetVersionString()}",
            FontSize = 11,
            Foreground = subFg,
        });
        headerRow.Children.Add(headerText);
        root.Children.Add(headerRow);

        // ── Behavior section ──────────────────────────────────────────
        root.Children.Add(SectionHeader("Behavior", accent));

        var showNinja = MakeCheck("Show the chibi ninja stage",
            "Animate the ninja on every clipboard capture. Uncheck to hide the stage entirely.",
            settings.ShowNinja, fg, subFg);
        showNinja.Click += (_, _) =>
        {
            settings.ShowNinja = showNinja.IsChecked == true;
            onSettingsChanged();
        };
        root.Children.Add(showNinja);

        var borderCheck = MakeCheck("Add black border to captured images",
            "When ClipNinja captures a screenshot, bake a thin black border into the saved image. Helps screenshots stand out when pasted into documents.",
            settings.AddBorderToImages, fg, subFg);
        borderCheck.Click += (_, _) =>
        {
            settings.AddBorderToImages = borderCheck.IsChecked == true;
            onSettingsChanged();
        };
        root.Children.Add(borderCheck);

        var shadowCheck = MakeCheck("Add drop shadow to captured images",
            "Bake a soft shadow on the right and bottom edges so screenshots visually lift off the page when pasted. Applies to NEW captures only.",
            settings.AddDropShadowToImages, fg, subFg);
        shadowCheck.Click += (_, _) =>
        {
            settings.AddDropShadowToImages = shadowCheck.IsChecked == true;
            onSettingsChanged();
        };
        root.Children.Add(shadowCheck);

        // Torn edges: compact single row with 4 side checkboxes. All
        // four ON gives the full "torn out of a magazine" look; top+
        // bottom only reads as a strip torn from a page.
        root.Children.Add(new TextBlock
        {
            Text = "Torn edges on captured images",
            Foreground = fg,
            FontSize = 12,
            Margin = new Thickness(0, 6, 0, 2),
        });
        root.Children.Add(new TextBlock
        {
            Text = "Ragged torn-paper edges baked into new captures. Check all four for the 'ripped out of an article' look.",
            Foreground = subFg,
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4),
        });
        var tornRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(12, 0, 0, 8),
        };
        CheckBox MakeTornSide(string label, bool initial, Action<bool> apply)
        {
            var cb = new CheckBox
            {
                Content = label,
                IsChecked = initial,
                Foreground = fg,
                FontSize = 11,
                Margin = new Thickness(0, 0, 14, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            cb.Click += (_, _) =>
            {
                apply(cb.IsChecked == true);
                onSettingsChanged();
            };
            tornRow.Children.Add(cb);
            return cb;
        }
        MakeTornSide("Top", settings.AddTornTopEdge, v => settings.AddTornTopEdge = v);
        MakeTornSide("Bottom", settings.AddTornBottomEdge, v => settings.AddTornBottomEdge = v);
        MakeTornSide("Left", settings.AddTornLeftEdge, v => settings.AddTornLeftEdge = v);
        MakeTornSide("Right", settings.AddTornRightEdge, v => settings.AddTornRightEdge = v);
        root.Children.Add(tornRow);

        // ── Quick-save section ───────────────────────────────────────
        root.Children.Add(SectionHeader("Quick-save screenshots", accent));

        root.Children.Add(new TextBlock
        {
            Text = "Right-click any image slot → '💾 Save image to folder' to copy the PNG into your quick-save folder with a friendly name. Configure the folder here (or you'll be prompted on first use).",
            Foreground = subFg,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        });

        // Folder row: current path (or "not set") + Browse button.
        var folderRow = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        folderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        folderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var folderText = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(settings.QuickSaveFolder)
                ? "(no folder chosen yet)"
                : settings.QuickSaveFolder,
            Foreground = string.IsNullOrWhiteSpace(settings.QuickSaveFolder) ? subFg : fg,
            FontSize = 11,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = settings.QuickSaveFolder,
        };
        Grid.SetColumn(folderText, 0);
        folderRow.Children.Add(folderText);
        var browseBtn = new Button
        {
            Content = "Browse…",
            Padding = new Thickness(10, 3, 10, 3),
            Margin = new Thickness(8, 0, 0, 0),
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        browseBtn.Click += (_, _) =>
        {
            var picker = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Choose a folder for quick-saved screenshots",
                InitialDirectory = string.IsNullOrWhiteSpace(settings.QuickSaveFolder)
                    ? System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments)
                    : settings.QuickSaveFolder,
            };
            if (picker.ShowDialog(dlg) == true)
            {
                settings.QuickSaveFolder = picker.FolderName;
                folderText.Text = picker.FolderName;
                folderText.Foreground = fg;
                folderText.ToolTip = picker.FolderName;
                onSettingsChanged();
            }
        };
        Grid.SetColumn(browseBtn, 1);
        folderRow.Children.Add(browseBtn);
        root.Children.Add(folderRow);

        var autoSaveCheck = MakeCheck("Auto-save every screenshot to the folder",
            "Every image ClipNinja captures is ALSO written to your quick-save folder immediately, named by timestamp. Requires the folder above to be set.",
            settings.AutoSaveScreenshotsToFolder, fg, subFg);
        autoSaveCheck.Click += (_, _) =>
        {
            settings.AutoSaveScreenshotsToFolder = autoSaveCheck.IsChecked == true;
            onSettingsChanged();
        };
        root.Children.Add(autoSaveCheck);

        // ── Screen capture hotkeys ───────────────────────────────────
        root.Children.Add(SectionHeader("Screen capture hotkeys", accent));
        root.Children.Add(new TextBlock
        {
            Text = "Click a box and press your key combo (e.g. Ctrl+Shift+C). Esc cancels. Takes effect immediately.",
            Foreground = subFg,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        });

        // Press-the-keys capture box: read-only, records the combo on
        // KeyDown. Pure-modifier presses (Ctrl alone…) are ignored so
        // the user can build up a chord; Esc restores the old value.
        void AddHotkeyRow(string label, Func<string> get, Action<string> set)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var lbl = new TextBlock
            {
                Text = label,
                Foreground = fg,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(lbl, 0);
            row.Children.Add(lbl);

            var box = new TextBox
            {
                Text = get(),
                IsReadOnly = true,
                IsReadOnlyCaretVisible = false,
                Cursor = System.Windows.Input.Cursors.Hand,
                FontSize = 12,
                Padding = new Thickness(6, 3, 6, 3),
                ToolTip = "Click, then press the key combo you want",
            };
            box.GotKeyboardFocus += (_, _) => { box.Text = "Press keys…"; };
            box.LostKeyboardFocus += (_, _) => { box.Text = get(); };
            box.PreviewKeyDown += (_, ke) =>
            {
                ke.Handled = true;
                // Alt combos report the real key in SystemKey.
                var key = ke.Key == System.Windows.Input.Key.System ? ke.SystemKey : ke.Key;
                if (key == System.Windows.Input.Key.Escape)
                {
                    System.Windows.Input.Keyboard.ClearFocus();
                    box.Text = get();
                    return;
                }
                // Ignore bare modifiers — wait for the real key.
                if (key is System.Windows.Input.Key.LeftCtrl or System.Windows.Input.Key.RightCtrl
                    or System.Windows.Input.Key.LeftShift or System.Windows.Input.Key.RightShift
                    or System.Windows.Input.Key.LeftAlt or System.Windows.Input.Key.RightAlt
                    or System.Windows.Input.Key.LWin or System.Windows.Input.Key.RWin)
                    return;
                var mods = Services.HotkeyService.FromModifierKeys(System.Windows.Input.Keyboard.Modifiers);
                var combo = Services.HotkeyService.Format(mods, key);
                set(combo);
                box.Text = combo;
                System.Windows.Input.Keyboard.ClearFocus();
                onSettingsChanged();  // re-registers immediately
            };
            Grid.SetColumn(box, 1);
            row.Children.Add(box);
            root.Children.Add(row);
        }

        AddHotkeyRow("Capture region",
            () => settings.CaptureRegionHotkey,
            v => settings.CaptureRegionHotkey = v);
        AddHotkeyRow("Capture full screen",
            () => settings.CaptureFullHotkey,
            v => settings.CaptureFullHotkey = v);

        // ── Updates ──────────────────────────────────────────────────
        root.Children.Add(SectionHeader("Updates", accent));
        root.Children.Add(new TextBlock
        {
            Text = $"You're running v{Services.UpdateService.CurrentVersion.ToString(3)}. Updates come from GitHub Releases of the repo below (owner/name).",
            Foreground = subFg,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6),
        });

        var repoRow = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        repoRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        repoRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var repoLbl = new TextBlock
        {
            Text = "GitHub repo",
            Foreground = fg,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(repoLbl, 0);
        repoRow.Children.Add(repoLbl);
        var repoBox = new TextBox
        {
            Text = settings.UpdateRepo,
            FontSize = 12,
            Padding = new Thickness(6, 3, 6, 3),
            ToolTip = "owner/name — e.g. yourname/ClipNinjaV2",
        };
        repoBox.TextChanged += (_, _) =>
        {
            settings.UpdateRepo = repoBox.Text.Trim();
            onSettingsChanged();
        };
        Grid.SetColumn(repoBox, 1);
        repoRow.Children.Add(repoBox);
        root.Children.Add(repoRow);

        var autoUpdateCheck = MakeCheck("Check for updates at startup",
            "Silent background check a few seconds after launch. A newer version shows a status-bar hint — never a popup.",
            settings.AutoCheckForUpdates, fg, subFg);
        autoUpdateCheck.Click += (_, _) =>
        {
            settings.AutoCheckForUpdates = autoUpdateCheck.IsChecked == true;
            onSettingsChanged();
        };
        root.Children.Add(autoUpdateCheck);

        var checkRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 4, 0, 4),
        };
        var checkBtn = new Button
        {
            Content = "Check for updates now",
            Padding = new Thickness(12, 4, 12, 4),
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        var checkResult = new TextBlock
        {
            Foreground = subFg,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 250,
        };
        checkBtn.Click += async (_, _) =>
        {
            checkBtn.IsEnabled = false;
            checkResult.Text = "Checking…";
            var (update, error) = await Services.UpdateService.CheckAsync(settings.UpdateRepo);
            checkBtn.IsEnabled = true;
            if (update is null)
            {
                checkResult.Text = error ?? "✓ You're up to date.";
                return;
            }
            checkResult.Text = $"⬆ {update.TagName} available";
            var notes = string.IsNullOrWhiteSpace(update.Notes)
                ? ""
                : "\n\nRelease notes:\n" + (update.Notes.Length > 600 ? update.Notes[..600] + "…" : update.Notes);
            var answer = MessageBox.Show(dlg,
                $"ClipNinja {update.TagName} is available (you have v{Services.UpdateService.CurrentVersion.ToString(3)}).{notes}\n\n" +
                "Update now? The app will restart.",
                "ClipNinja update",
                MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (answer != MessageBoxResult.Yes) return;
            checkBtn.IsEnabled = false;
            checkResult.Text = "Downloading update…";
            var applyError = await Services.UpdateService.DownloadAndStageAsync(update);
            if (applyError is not null)
            {
                checkBtn.IsEnabled = true;
                checkResult.Text = applyError;
                return;
            }
            // Swap script is waiting for our file lock to release —
            // graceful shutdown with a hard-exit fallback so the lock
            // is guaranteed to drop even from inside this modal dialog.
            Services.UpdateService.ShutdownForUpdate();
        };
        checkRow.Children.Add(checkBtn);
        checkRow.Children.Add(checkResult);
        root.Children.Add(checkRow);

        var startupCheck = MakeCheck("Launch ClipNinja when Windows starts",
            "Add a registry entry under HKCU\\…\\Run so ClipNinja starts minimized to the tray each time you sign in.",
            Services.StartupService.IsEnabled(), fg, subFg);
        startupCheck.Click += (_, _) =>
        {
            bool want = startupCheck.IsChecked == true;
            bool ok = Services.StartupService.SetEnabled(want);
            if (ok)
            {
                settings.LaunchOnStartup = want;
                onSettingsChanged();
            }
            else
            {
                // Revert UI if registry write failed
                startupCheck.IsChecked = !want;
                MessageBox.Show(dlg,
                    "Couldn't update the Windows startup entry. Check that ClipNinja isn't running in a restricted account.",
                    "Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };
        root.Children.Add(startupCheck);

        // ── Recent list section ──────────────────────────────────────
        root.Children.Add(SectionHeader("Recent list", accent));

        root.Children.Add(new TextBlock
        {
            Text = "The Recent list is the rolling list of your latest captures (newest at top). When this list gets full, the oldest item moves into History (unless History is disabled).",
            Foreground = subFg,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
            MaxWidth = 420,
        });

        var recentRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 4, 0, 4),
        };
        recentRow.Children.Add(new TextBlock
        {
            Text = "Max items:",
            Foreground = fg,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Width = 100,
        });
        var recentCombo = new ComboBox { Width = 100, FontSize = 12 };
        int[] recentSizes = { 25, 50, 100, 200, 500 };
        foreach (var n in recentSizes) recentCombo.Items.Add(n.ToString());
        int recentIdx = Array.IndexOf(recentSizes, settings.RecentMaxItems);
        if (recentIdx < 0) recentIdx = 1; // default 50
        recentCombo.SelectedIndex = recentIdx;
        recentCombo.SelectionChanged += (_, _) =>
        {
            int chosen = recentSizes[recentCombo.SelectedIndex];
            settings.RecentMaxItems = chosen;
            onSettingsChanged();
        };
        recentRow.Children.Add(recentCombo);
        root.Children.Add(recentRow);

        // ── History section ───────────────────────────────────────────
        root.Children.Add(SectionHeader("History", accent));

        // Brief explanation of what history is
        root.Children.Add(new TextBlock
        {
            Text = "Items that cascade out of the Recent list (when it overflows) are kept here until you clear them or they exceed the cap. Open from the 📜 History button at the bottom of the main window.",
            Foreground = subFg,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
            MaxWidth = 420,
        });

        // History size dropdown
        var sizeRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 4, 0, 4),
        };
        sizeRow.Children.Add(new TextBlock
        {
            Text = "Max items:",
            Foreground = fg,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Width = 100,
        });
        var sizeCombo = new ComboBox
        {
            Width = 100,
            FontSize = 12,
        };
        int[] sizes = { 0, 50, 100, 200, 500, 1000 };
        foreach (var n in sizes)
        {
            sizeCombo.Items.Add(n == 0 ? "Disabled" : n.ToString());
        }
        // Select the current value (or closest match)
        int currentIdx = Array.IndexOf(sizes, settings.HistoryMaxItems);
        if (currentIdx < 0) currentIdx = 2; // default to 100
        sizeCombo.SelectedIndex = currentIdx;
        sizeCombo.SelectionChanged += (_, _) =>
        {
            int chosen = sizes[sizeCombo.SelectedIndex];
            settings.HistoryMaxItems = chosen;
            vm.History.MaxItems = chosen;
            onSettingsChanged();
        };
        sizeRow.Children.Add(sizeCombo);
        root.Children.Add(sizeRow);

        // Clear history button
        var clearHistoryBtn = new Button
        {
            Content = "🗑️ Clear all history items",
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 6, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        // Hoisted up here because the closure below (RefreshAboutRows) needs
        // it BEFORE the About section creates its rows in source order.
        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ClipNinja");

        // Pre-declare these so the clear-history button (defined below in
        // source order) can refresh their displayed values after a clear.
        // Each is assigned its actual TextBlock in the AddRow calls below.
        TextBlock? historyRowBlock = null;
        TextBlock? diskRowBlock = null;

        void RefreshAboutRows()
        {
            try
            {
                var (_, _, bytesNow) = ComputeStats(vm, dataDir);
                if (historyRowBlock is not null)
                    historyRowBlock.Text = $"{vm.History.Items.Count} / {settings.HistoryMaxItems}";
                if (diskRowBlock is not null)
                    diskRowBlock.Text = FormatBytes(bytesNow);
            }
            catch { /* harmless cosmetic refresh */ }
        }

        clearHistoryBtn.Click += (_, _) =>
        {
            if (vm.History.Items.Count == 0)
            {
                MessageBox.Show(dlg, "History is already empty.", "Settings", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var res = MessageBox.Show(dlg,
                $"Delete all {vm.History.Items.Count} history items? This can't be undone.",
                "Clear history",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (res != MessageBoxResult.Yes) return;
            vm.History.ClearAll();
            onSettingsChanged();
            RefreshAboutRows();
        };
        root.Children.Add(clearHistoryBtn);

        // ── About / data section ──────────────────────────────────────
        root.Children.Add(SectionHeader("About", accent));

        var (pinnedCount, recentCount, diskBytes) = ComputeStats(vm, dataDir);

        AddRow(root, "Data folder", dataDir, fg, subFg, monospaceValue: true);
        AddRow(root, "Pinned items", $"{pinnedCount}", fg, subFg);
        AddRow(root, "Recent items", $"{recentCount} / {settings.RecentMaxItems}", fg, subFg);
        historyRowBlock = AddRow(root, "History items", $"{vm.History.Items.Count} / {settings.HistoryMaxItems}", fg, subFg);
        diskRowBlock = AddRow(root, "Disk usage", FormatBytes(diskBytes), fg, subFg);
        AddRow(root, "Built on", ".NET 8 + WPF", fg, subFg);

        // ── Buttons ────────────────────────────────────────────────────
        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0),
        };

        var openFolderBtn = new Button
        {
            Content = "📁 Open data folder",
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 8, 0),
        };
        openFolderBtn.Click += (_, _) =>
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = dataDir,
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(dlg, $"Couldn't open folder:\n{ex.Message}",
                    "Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };
        btnRow.Children.Add(openFolderBtn);

        var closeBtn = new Button
        {
            Content = "Close",
            Padding = new Thickness(16, 4, 16, 4),
            IsDefault = true,
            IsCancel = true,
        };
        closeBtn.Click += (_, _) => dlg.Close();
        btnRow.Children.Add(closeBtn);

        root.Children.Add(btnRow);

        // Wrap in a ScrollViewer in case content overflows on small displays
        dlg.Content = new ScrollViewer
        {
            Content = root,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };

        dlg.ShowDialog();
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private static FrameworkElement SectionHeader(string text, Brush accent)
    {
        var sp = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(0, 8, 0, 6),
        };
        sp.Children.Add(new TextBlock
        {
            Text = text.ToUpperInvariant(),
            FontWeight = FontWeights.Bold,
            FontSize = 11,
            Foreground = accent,
            Margin = new Thickness(0, 0, 0, 4),
        });
        sp.Children.Add(new Border
        {
            Height = 1,
            Background = accent,
            Opacity = 0.4,
        });
        return sp;
    }

    private static CheckBox MakeCheck(string label, string sublabel, bool initial, Brush fg, Brush subFg)
    {
        var cb = new CheckBox
        {
            IsChecked = initial,
            Margin = new Thickness(0, 6, 0, 6),
            Foreground = fg,
        };
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = fg,
            FontSize = 13,
        });
        sp.Children.Add(new TextBlock
        {
            Text = sublabel,
            Foreground = subFg,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0),
            MaxWidth = 380,
        });
        cb.Content = sp;
        return cb;
    }

    private static TextBlock AddRow(Panel parent, string label, string value, Brush fg, Brush subFg, bool monospaceValue = false)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 4, 0, 4),
        };
        row.Children.Add(new TextBlock
        {
            Text = label,
            Width = 100,
            Foreground = subFg,
            FontSize = 12,
        });
        var valueBlock = new TextBlock
        {
            Text = value,
            Foreground = fg,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 320,
        };
        // Only SET FontFamily when we want monospace. Assigning null to
        // the FontFamily dependency property throws ArgumentException
        // ("'' is not a valid value") — it's not a nullable DP, and the
        // ternary `cond ? new FontFamily(...) : null` in the initializer
        // was crashing the entire Settings dialog. Leaving the property
        // untouched inherits the default font, which is what null was
        // trying (and failing) to express.
        if (monospaceValue) valueBlock.FontFamily = new FontFamily("Consolas");
        row.Children.Add(valueBlock);
        parent.Children.Add(row);
        return valueBlock;
    }

    private static string GetVersionString()
    {
        try
        {
            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            var v = asm.GetName().Version;
            return v is null ? "?" : $"{v.Major}.{v.Minor}.{v.Build}";
        }
        catch { return "?"; }
    }

    /// <summary>
    /// Compute item counts (pinned + recent) and total bytes occupied by
    /// cached PNGs and JSON state. Best-effort — returns zeros on any error.
    /// </summary>
    private static (int pinned, int recent, long bytes) ComputeStats(MainViewModel vm, string dataDir)
    {
        int pinned = 0, recent = 0;
        try { pinned = vm.PinnedItems.Count; } catch { }
        try { recent = vm.RecentItems.Count; } catch { }
        long bytes = 0;
        try
        {
            var imgDir = Path.Combine(dataDir, "images");
            if (Directory.Exists(imgDir))
            {
                foreach (var f in Directory.EnumerateFiles(imgDir, "*", SearchOption.TopDirectoryOnly))
                {
                    try { bytes += new FileInfo(f).Length; } catch { }
                }
            }
            var historyImgDir = Path.Combine(dataDir, "history", "images");
            if (Directory.Exists(historyImgDir))
            {
                foreach (var f in Directory.EnumerateFiles(historyImgDir, "*", SearchOption.TopDirectoryOnly))
                {
                    try { bytes += new FileInfo(f).Length; } catch { }
                }
            }
            try { bytes += new FileInfo(Path.Combine(dataDir, "settings.json")).Length; } catch { }
            try { bytes += new FileInfo(Path.Combine(dataDir, "history.json")).Length; } catch { }
            try { bytes += new FileInfo(Path.Combine(dataDir, "items.json")).Length; } catch { }
        }
        catch { }
        return (pinned, recent, bytes);
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
        return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
    }
}
