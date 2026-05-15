using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ClipNinjaV2.Models;
using ClipNinjaV2.Services;
using ClipNinjaV2.ViewModels;

namespace ClipNinjaV2;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly ClipboardWatcher _clipWatcher = new();
    private readonly HotkeyService _hotkeys = new();
    private Views.PreviewPopup? _previewPopup;
    private Animations.NinjaAnimator? _ninjaAnimator;
    /// <summary>Time before which preview popups are suppressed (used after sun menu close).</summary>
    private DateTime _suppressPreviewUntil = DateTime.MinValue;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel();
        DataContext = _vm;

        Loaded += OnWindowLoaded;
        Closing += OnWindowClosing;
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        Services.Trace.Log("startup", "OnWindowLoaded begin");
        _clipWatcher.AttachTo(this);
        _clipWatcher.ContentChanged += (_, content) =>
        {
            Dispatcher.Invoke(() =>
            {
                using (Services.Trace.Time("capture", $"OnNewClipContent {content.GetType().Name}"))
                {
                    _vm.OnNewClipContent(content);
                }
                using (Services.Trace.Time("capture", "PlayRandomMove"))
                {
                    PlayRandomMove();
                }
            });
        };

        _hotkeys.AttachTo(this);

        // Only register the show/hide hotkey. We DELIBERATELY don't intercept
        // Ctrl+V or any direct-slot hotkeys — those caused focus and clipboard
        // race conditions. Instead, the user clicks a slot to load it onto the
        // clipboard, then presses Ctrl+V natively in their target app.
        _hotkeys.Register(HotkeyService.CtrlShift, Key.N, ToggleVisibility);
        _hotkeys.Register(HotkeyService.CtrlShift, Key.B, () => _vm.TogglePause());

        _previewPopup = new Views.PreviewPopup(this);

        // Ninja animator — drives the named transforms in MainWindow.xaml.
        _ninjaAnimator = new Animations.NinjaAnimator(
            NinjaTranslate, NinjaRotate, NinjaScale,
            LeftArmRotate, RightArmRotate,
            LeftLegRotate, RightLegRotate,
            HeadRotate,
            FireballTranslate, FireballScale, FireballRoot);

        _vm.StatusText = "Click a slot to load it onto the clipboard, then Ctrl+V to paste";

        // If the user has auto-start enabled in settings, make sure the
        // registry entry exists and points at the current .exe path. This
        // self-heals after the user moves the .exe to a new location.
        // (We don't touch the registry if the setting is off — they may
        // have explicitly disabled it elsewhere.)
        if (_vm.Settings.LaunchOnStartup)
        {
            Services.StartupService.SyncRegistryIfEnabled();
        }

        // If the system clipboard is empty when the app launches AND we have
        // saved content in slot 1, load slot 1 to the clipboard. This way
        // the user can immediately Ctrl+V their last-known content after
        // a fresh boot without needing to click a slot first.
        TryAutoLoadSlot1OnStartup();
        Services.Trace.Log("startup", "OnWindowLoaded end");
    }

    private void TryAutoLoadSlot1OnStartup()
    {
        try
        {
            // "Empty clipboard" = neither image nor text. (Clipboard might still
            // hold weird formats, but those don't help us.)
            bool clipboardHasUsefulContent = Clipboard.ContainsImage() || Clipboard.ContainsText();
            if (clipboardHasUsefulContent) return;

            // Slot 1 must have content
            if (_vm.Slots.Count == 0) return;
            var slot1 = _vm.Slots[0];
            if (slot1.IsEmpty || slot1.Content is null) return;

            // Load it through the normal path so retry/echo-suppression all work
            LoadSlotToClipboard(slot1);
        }
        catch
        {
            // If reading the clipboard at startup fails (rare), just skip
            // this convenience — the user can click slot 1 manually.
        }
    }

    // ── Hover preview ────────────────────────────────────────────────────────

    private void OnSlotRow_MouseEnter(object sender, MouseEventArgs e)
    {
        // Skip the preview entirely if we're inside the suppression window
        // (set briefly after the sun-menu closes, to avoid the popup flashing
        // up over a slot row immediately after the menu disappears).
        if (DateTime.UtcNow < _suppressPreviewUntil) return;

        var slot = GetSlotFrom(sender);
        if (slot is null) return;
        // Compute the slot row's vertical position in screen pixels so the
        // preview can align next to it.
        double anchorTop = 0;
        if (sender is FrameworkElement fe)
        {
            try
            {
                var topLeft = fe.PointToScreen(new System.Windows.Point(0, 0));
                anchorTop = topLeft.Y;
            }
            catch { /* not yet in visual tree */ }
        }
        _previewPopup?.ScheduleShow(slot, anchorTop);
    }

    private void OnSlotRow_MouseLeave(object sender, MouseEventArgs e)
    {
        _previewPopup?.Hide();
    }

    // ── Slot row click — sets that slot's content as the clipboard ──────────

    private static ClipSlot? GetSlotFrom(object sender)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ClipSlot s)
            return s;
        if (sender is MenuItem mi)
        {
            if (mi.DataContext is ClipSlot s2) return s2;
            if (mi.Parent is ContextMenu cm && cm.PlacementTarget is FrameworkElement pt
                && pt.DataContext is ClipSlot s3) return s3;
        }
        return null;
    }

    /// <summary>
    /// Single-click on a slot: copy its content to the OS clipboard and
    /// show a confirmation. Long press + drag: initiate reorder drag.
    /// We use mouse-down to record the start position; if the user moves
    /// past a threshold while still holding the button, we start a drag.
    /// Otherwise on mouse-up we treat it as a click.
    /// </summary>

    private System.Windows.Point _dragStartPoint;
    private ClipSlot? _dragStartSlot;
    private bool _dragInitiated;

    private void OnSlotRow_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var slot = GetSlotFrom(sender);
        if (slot is null) return;
        // Record where the click started so we can distinguish click from drag
        _dragStartPoint = e.GetPosition(this);
        _dragStartSlot = slot;
        _dragInitiated = false;
    }

    private void OnSlotRow_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (_dragStartSlot is null || _dragInitiated) return;
        // Empty slots ARE draggable — dragging them around is a no-op semantically
        // but blocking it caused weird selection behavior.

        // Has the mouse moved far enough to count as a drag?
        var current = e.GetPosition(this);
        var dx = current.X - _dragStartPoint.X;
        var dy = current.Y - _dragStartPoint.Y;
        if (Math.Abs(dx) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(dy) < SystemParameters.MinimumVerticalDragDistance)
            return;

        // Start the drag-drop operation. The data is the slot index.
        _dragInitiated = true;
        _previewPopup?.HideImmediate();   // hide preview during drag

        var data = new DataObject("ClipNinjaSlotIndex", _dragStartSlot.Index);
        try
        {
            DragDrop.DoDragDrop(
                (DependencyObject)sender,
                data,
                DragDropEffects.Move);
        }
        catch { /* drag canceled */ }
        finally
        {
            // Clean up any lingering visual feedback
            ClearDropIndicator();
        }

        // After DoDragDrop returns, the drag is over (drop or cancel).
        _dragStartSlot = null;
    }

    private void OnSlotRow_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // If we never crossed the drag threshold, treat this as a click.
        if (_dragInitiated)
        {
            _dragInitiated = false;
            _dragStartSlot = null;
            return;
        }
        var slot = GetSlotFrom(sender);
        if (slot is null || slot.IsEmpty || slot.Content is null) { _dragStartSlot = null; return; }
        Services.Trace.Log("click", $"slot {slot.Index} clicked → LoadSlotToClipboard");
        LoadSlotToClipboard(slot);
        _dragStartSlot = null;
    }

    // ── Drop targets ─────────────────────────────────────────────────────────

    private void OnSlotRow_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent("ClipNinjaSlotIndex"))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }
        e.Effects = DragDropEffects.Move;
        e.Handled = true;

        // Visual feedback: highlight the target row briefly
        if (sender is FrameworkElement fe && fe.DataContext is ClipSlot targetSlot)
        {
            ShowDropIndicator(targetSlot.Index);
        }
    }

    private void OnSlotRow_DragLeave(object sender, DragEventArgs e)
    {
        // Don't immediately clear — DragOver fires on neighbors during transition.
        // The next DragOver elsewhere will overwrite it; if drop happens, OnSlotRow_Drop clears it.
    }

    private void OnSlotRow_Drop(object sender, DragEventArgs e)
    {
        ClearDropIndicator();
        if (!e.Data.GetDataPresent("ClipNinjaSlotIndex")) return;

        int srcIdx = (int)e.Data.GetData("ClipNinjaSlotIndex");
        if (sender is not FrameworkElement fe || fe.DataContext is not ClipSlot dropTarget) return;
        int dstIdx = dropTarget.Index;
        if (srcIdx == dstIdx) return;

        ReorderSlot(srcIdx, dstIdx);
        e.Handled = true;
    }

    /// <summary>
    /// Move a slot from srcIdx to dstIdx, shifting intervening slots by one.
    /// 1-based indices. Pin state, nickname, and content all travel together.
    /// </summary>
    private void ReorderSlot(int srcIdx, int dstIdx)
    {
        if (srcIdx == dstIdx) return;
        var slots = _vm.Slots;
        if (srcIdx < 1 || srcIdx > slots.Count || dstIdx < 1 || dstIdx > slots.Count) return;

        // Snapshot the moving payload
        var moving = slots[srcIdx - 1];
        var movingContent = moving.Content;
        var movingPinned = moving.IsPinned;
        var movingNickname = moving.Nickname;

        if (srcIdx < dstIdx)
        {
            // Move down: shift slots [srcIdx+1 .. dstIdx] UP by one
            for (int i = srcIdx; i < dstIdx; i++)
            {
                var from = slots[i];      // 0-based: slots[i] is index i+1
                var to = slots[i - 1];
                to.Content = from.Content;
                to.IsPinned = from.IsPinned;
                to.Nickname = from.Nickname;
            }
        }
        else
        {
            // Move up: shift slots [dstIdx .. srcIdx-1] DOWN by one
            for (int i = srcIdx - 1; i > dstIdx - 1; i--)
            {
                var from = slots[i - 1];
                var to = slots[i];
                to.Content = from.Content;
                to.IsPinned = from.IsPinned;
                to.Nickname = from.Nickname;
            }
        }

        // Place the moving payload at its new position
        var dest = slots[dstIdx - 1];
        dest.Content = movingContent;
        dest.IsPinned = movingPinned;
        dest.Nickname = movingNickname;

        // Highlight follows the moved slot
        _vm.PasteIdx = dstIdx;
        _vm.StatusText = $"Moved slot {srcIdx} → {dstIdx}";
    }

    /// <summary>Visual: highlight a slot row as the drop target.</summary>
    private int _currentDropIndicator = 0;
    private void ShowDropIndicator(int slotIndex)
    {
        if (_currentDropIndicator == slotIndex) return;
        ClearDropIndicator();
        _currentDropIndicator = slotIndex;
        if (slotIndex >= 1 && slotIndex <= _vm.Slots.Count)
            _vm.Slots[slotIndex - 1].IsDropTarget = true;
    }

    private void ClearDropIndicator()
    {
        if (_currentDropIndicator >= 1 && _currentDropIndicator <= _vm.Slots.Count)
            _vm.Slots[_currentDropIndicator - 1].IsDropTarget = false;
        _currentDropIndicator = 0;
    }

    /// <summary>
    /// Monotonic counter used to cancel stale "load slot to clipboard"
    /// retries when the user clicks a different slot mid-retry.
    /// </summary>
    private int _loadToken;

    private void LoadSlotToClipboard(ClipSlot slot)
    {
        if (slot.Content is null) return;
        using var _t = Services.Trace.Time("load", $"LoadSlotToClipboard(slot {slot.Index}, {slot.Content.GetType().Name})");

        // Each load attempt gets a unique token. If the user clicks another
        // slot before our retries finish, the new click bumps the token and
        // any in-flight retry chain self-terminates.
        int myToken = ++_loadToken;

        // Update UI immediately for snappy feedback
        using (Services.Trace.Time("load", "set PasteIdx + StatusText"))
        {
            _vm.PasteIdx = slot.Index;
            _vm.StatusText = $"Loading slot {slot.Index}…";
        }

        // Pre-compute the fingerprint of what we're about to write so the
        // watcher knows to skip the echo when it sees this content come back.
        string signature = "";
        using (Services.Trace.Time("load", "compute signature"))
        {
            switch (slot.Content)
            {
                case TextContent tc:
                    signature = Services.ClipboardWatcher.ComputeTextSignature(tc.Text);
                    break;
                case ImageContent ic when ic.FullImage is not null:
                    signature = Services.ClipboardWatcher.ComputeImageSignature(ic.FullImage);
                    break;
            }
        }
        _clipWatcher.LastWrittenSignature = signature;
        _clipWatcher.SuppressFor(1500);

        // Try once immediately. If clipboard is locked, retry async via
        // dispatcher posts (which let the UI breathe between attempts).
        TryWriteClipboard(slot.Content, slot.Index, attempt: 0, token: myToken);
    }

    /// <summary>
    /// Try to write content to clipboard. Text uses our native Win32 path
    /// (fast retries, fail-fast). Images use WPF's SetImage which works
    /// fine per the trace logs. On failure, schedules another retry on
    /// the dispatcher — clipboard contention sometimes clears seconds later.
    /// </summary>
    private void TryWriteClipboard(ClipContent content, int slotIdx, int attempt, int token)
    {
        // Stale check: user clicked a different slot in the meantime — drop this
        if (token != _loadToken)
        {
            Services.Trace.Log("write", $"slot {slotIdx} attempt {attempt} CANCELLED (stale token)");
            return;
        }

        // Refresh the suppression window every retry so that whenever the
        // write FINALLY succeeds, the watcher won't capture our own echo.
        _clipWatcher.SuppressFor(1500);

        bool succeeded = false;
        try
        {
            switch (content)
            {
                case TextContent tc:
                    {
                        using (Services.Trace.Time("write", $"NativeClipboard.SetText slot {slotIdx} attempt {attempt} (len={tc.Text.Length})"))
                        {
                            // 250ms budget per attempt — much faster than WPF's
                            // internal 1-second retry on SetDataObject failure.
                            succeeded = Services.NativeClipboard.SetText(tc.Text, maxWaitMs: 250);
                        }
                        break;
                    }
                case ImageContent ic when ic.FullImage is not null:
                    using (Services.Trace.Time("write", $"Clipboard.SetImage slot {slotIdx} attempt {attempt} ({ic.FullImage.PixelWidth}x{ic.FullImage.PixelHeight})"))
                    {
                        Clipboard.SetImage(ic.FullImage);
                        succeeded = true;
                    }
                    break;
                default:
                    return;
            }
        }
        catch (Exception ex)
        {
            Services.Trace.Log("write", $"slot {slotIdx} attempt {attempt} threw: {ex.GetType().Name}: {ex.Message}");
            succeeded = false;
        }

        if (succeeded)
        {
            _vm.StatusText = $"✓ Slot {slotIdx} loaded — Ctrl+V to paste";
            Services.Trace.Log("write", $"slot {slotIdx} attempt {attempt} SUCCESS");
            return;
        }

        Services.Trace.Log("write", $"slot {slotIdx} attempt {attempt} FAILED (clipboard busy)");

        // Up to 5 attempts. With native 250ms retries, total worst case is
        // ~1.5s instead of 8s previously.
        if (attempt >= 4)
        {
            _vm.StatusText = $"❌ Slot {slotIdx} clipboard busy — try again";
            Services.Trace.Log("write", $"slot {slotIdx} GAVE UP after {attempt+1} attempts");
            return;
        }
        int delayMs = 50 + 50 * attempt;   // 50, 100, 150, 200
        var t = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(delayMs),
        };
        t.Tick += (_, _) =>
        {
            t.Stop();
            TryWriteClipboard(content, slotIdx, attempt + 1, token);
        };
        t.Start();
    }

    // ── Right-click menu handlers ────────────────────────────────────────────

    private void OnSlotMenu_TogglePin(object sender, RoutedEventArgs e)
    {
        var slot = GetSlotFrom(sender);
        if (slot is null || slot.IsEmpty) return;
        _vm.TogglePin(slot);
    }

    private void OnSlotMenu_SetNickname(object sender, RoutedEventArgs e)
    {
        var slot = GetSlotFrom(sender);
        if (slot is null || slot.IsEmpty) return;

        var input = Views.InputPrompt.Show(
            this,
            $"Enter a nickname for slot {slot.Index} (max 20 characters).\n\nLeave blank to remove the nickname.",
            "Slot Nickname",
            slot.Nickname,
            maxLength: 20);
        if (input is null) return;

        slot.Nickname = input;
        if (!slot.IsPinned && !string.IsNullOrEmpty(input))
            slot.IsPinned = true;
        _vm.StatusText = string.IsNullOrEmpty(input)
            ? $"Nickname removed from slot {slot.Index}"
            : $"Slot {slot.Index} nickname: {input}";
    }

    private void OnSlotMenu_MoveUp(object sender, RoutedEventArgs e)
    {
        var slot = GetSlotFrom(sender);
        if (slot is null) return;
        SwapSlotWithNeighbor(slot, -1);
    }

    private void OnSlotMenu_MoveDown(object sender, RoutedEventArgs e)
    {
        var slot = GetSlotFrom(sender);
        if (slot is null) return;
        SwapSlotWithNeighbor(slot, +1);
    }

    private void SwapSlotWithNeighbor(ClipSlot slot, int direction)
    {
        int from = slot.Index - 1;
        int to = from + direction;
        if (to < 0 || to >= _vm.Slots.Count) return;
        var a = _vm.Slots[from];
        var b = _vm.Slots[to];
        var tmpContent = a.Content; a.Content = b.Content; b.Content = tmpContent;
        var tmpPinned = a.IsPinned; a.IsPinned = b.IsPinned; b.IsPinned = tmpPinned;
        var tmpNick = a.Nickname; a.Nickname = b.Nickname; b.Nickname = tmpNick;
        _vm.PasteIdx = to + 1;   // highlight follows the moved slot
        _vm.StatusText = $"Slot {slot.Index} moved {(direction == -1 ? "up" : "down")}";
    }

    private void OnSlotMenu_EditText(object sender, RoutedEventArgs e)
    {
        var slot = GetSlotFrom(sender);
        if (slot is null || slot.Content is not TextContent tc)
        {
            MessageBox.Show("Edit only works on text slots (not images).",
                            "ClipNinja", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var input = Views.InputPrompt.Show(this,
            $"Edit text for slot {slot.Index}:",
            "Edit Text",
            tc.Text);
        if (input is null) return;
        slot.Content = new TextContent { Text = input, CapturedAt = tc.CapturedAt };
        _vm.StatusText = $"Slot {slot.Index} edited";
    }

    private void OnSlotMenu_Clear(object sender, RoutedEventArgs e)
    {
        var slot = GetSlotFrom(sender);
        if (slot is null) return;
        _vm.ClearSlot(slot);
    }

    // ── Up/Down arrow buttons ────────────────────────────────────────────────

    private void OnArrowUp_Click(object sender, MouseButtonEventArgs e)
    {
        var slot = GetSlotFrom(sender);
        if (slot is null) return;
        SwapSlotWithNeighbor(slot, -1);
        e.Handled = true;
    }

    private void OnArrowDown_Click(object sender, MouseButtonEventArgs e)
    {
        var slot = GetSlotFrom(sender);
        if (slot is null) return;
        SwapSlotWithNeighbor(slot, +1);
        e.Handled = true;
    }

    /// <summary>Click the ✕ on a slot row to clear that slot.</summary>
    private void OnEraser_Click(object sender, MouseButtonEventArgs e)
    {
        var slot = GetSlotFrom(sender);
        if (slot is null || slot.IsEmpty) return;
        // Confirm if the slot is pinned (avoid accidental nuke of important content)
        if (slot.IsPinned)
        {
            var res = MessageBox.Show(
                $"Slot {slot.Index} is pinned. Clear it anyway?",
                "Clear pinned slot",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (res != MessageBoxResult.Yes) { e.Handled = true; return; }
        }
        _vm.ClearSlot(slot);
        e.Handled = true;
    }

    /// <summary>Click the lock icon to toggle pin state on this slot.</summary>
    private void OnLock_Click(object sender, MouseButtonEventArgs e)
    {
        var slot = GetSlotFrom(sender);
        if (slot is null || slot.IsEmpty) return;
        _vm.TogglePin(slot);
        e.Handled = true;
    }

    /// <summary>Eat the matching MouseUp so the slot row's MouseUp doesn't
    /// also load the slot when the user clicks the lock or eraser.</summary>
    private void OnLock_MouseUp(object sender, MouseButtonEventArgs e) { e.Handled = true; }
    private void OnIconBtn_MouseUp(object sender, MouseButtonEventArgs e) { e.Handled = true; }

    // ── Window plumbing ──────────────────────────────────────────────────────

    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e) { }

    public void OnAppExiting()
    {
        _ninjaAnimator?.Stop();
        _previewPopup?.HideImmediate();
        _vm.Flush();
        _hotkeys.Dispose();
        _clipWatcher.Dispose();
    }

    private void OnHeader_DragStart(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void OnMinimize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void OnClose_Click(object sender, RoutedEventArgs e)
    {
        // ClipNinja keeps running in the system tray when you click X — that
        // way it can keep capturing clipboard activity in the background
        // (which is the whole point of a clipboard manager).
        // First time the user clicks X, show a one-time hint explaining this.
        if (_vm.Settings.ShowTrayHint)
        {
            _vm.Settings.ShowTrayHint = false;
            _vm.SaveAll();
            MessageBox.Show(
                "ClipNinja is still running in your system tray (look near the clock 🕐).\n\n" +
                "It needs to stay running so it can capture what you copy.\n\n" +
                "To FULLY EXIT: right-click the ninja in your system tray → Exit.",
                "ClipNinja keeps running",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        Hide();
    }

    // ── Tray menu handlers ───────────────────────────────────────────────────

    private void OnTrayShow_Click(object sender, RoutedEventArgs e) => ToggleVisibility();

    /// <summary>
    /// Sync the "Launch on Windows startup" checkmark with the real registry
    /// state every time the tray context menu opens. Defends against the
    /// user toggling auto-start externally (Task Manager → Startup tab) since
    /// our last view of the world.
    /// </summary>
    private void OnTrayContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        try
        {
            TrayLaunchOnStartupItem.IsChecked = Services.StartupService.IsEnabled();
        }
        catch { /* leave whatever it was */ }
    }

    private void OnTraySettings_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("Settings dialog coming in a later phase 🥷",
                        "ClipNinja", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnTrayClearAll_Click(object sender, RoutedEventArgs e)
    {
        var res = MessageBox.Show("Clear ALL slots, including pinned ones?",
                                  "Clear All", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (res != MessageBoxResult.Yes) return;
        _vm.ClearAllSlots();
    }

    private void OnTrayExit_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }

    /// <summary>
    /// Toggle the "Launch on Windows startup" registry entry. Keeps the
    /// settings.json flag and the registry value in sync.
    /// </summary>
    private void OnTrayLaunchOnStartup_Click(object sender, RoutedEventArgs e)
    {
        // The MenuItem's IsCheckable means WPF flips IsChecked before our
        // handler fires; we read it to know what state the user wants.
        bool want = TrayLaunchOnStartupItem.IsChecked;
        bool ok = Services.StartupService.SetEnabled(want);
        if (ok)
        {
            _vm.Settings.LaunchOnStartup = want;
            _vm.ScheduleSave();
            _vm.StatusText = want
                ? "✓ ClipNinja will launch when Windows starts"
                : "ClipNinja will no longer launch at startup";
        }
        else
        {
            // Couldn't write to the registry — revert the checkmark and tell the user.
            TrayLaunchOnStartupItem.IsChecked = !want;
            MessageBox.Show(
                "Couldn't update the Windows startup registry entry.\n\nMake sure you're not running ClipNinja in a sandbox or restricted account.",
                "ClipNinja", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnTrayOpenTrace_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ClipNinja", "trace.log");
            if (System.IO.File.Exists(path))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true,
                });
            }
            else
            {
                MessageBox.Show("Trace log not found yet — try copying or clicking a slot first.",
                                "ClipNinja", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Couldn't open trace log: {ex.Message}",
                            "ClipNinja", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ── Ninja animation control ──────────────────────────────────────────────

    /// <summary>Pick a random move and play it (used on capture).</summary>
    private void PlayRandomMove()
    {
        if (_ninjaAnimator is null) return;
        if (_ninjaAnimator.IsPlaying) return;   // don't interrupt a current animation
        if (!_vm.Settings.ShowNinja) return;
        _ninjaAnimator.Play(Animations.AnimationLibrary.PickRandom());
    }

    /// <summary>Sun left-click: random move (the easter egg).</summary>
    private void OnSun_LeftClick(object sender, MouseButtonEventArgs e)
    {
        if (_ninjaAnimator is null) return;
        _ninjaAnimator.Stop();
        _ninjaAnimator.Play(Animations.AnimationLibrary.PickRandom());
        e.Handled = true;
    }

    /// <summary>Sun right-click: pop a menu of all available moves.</summary>
    private void OnSun_RightClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_ninjaAnimator is null) return;

        var menu = new ContextMenu();
        if (sender is FrameworkElement sunEl)
        {
            menu.PlacementTarget = sunEl;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        }

        _previewPopup?.HideImmediate();
        menu.Opened += (_, _) => { _suppressPreviewUntil = DateTime.UtcNow.AddSeconds(2); };
        menu.Closed += (_, _) => { _suppressPreviewUntil = DateTime.UtcNow.AddMilliseconds(400); };

        var randomItem = new MenuItem { Header = "🎲 Random Move" };
        randomItem.Click += (_, _) =>
        {
            _ninjaAnimator.Stop();
            _ninjaAnimator.Play(Animations.AnimationLibrary.PickRandom());
        };
        menu.Items.Add(randomItem);
        menu.Items.Add(new Separator());

        foreach (var kvp in Animations.AnimationLibrary.All)
        {
            if (kvp.Key == "IdleBob") continue;
            var item = new MenuItem { Header = kvp.Key };
            var anim = kvp.Value;
            item.Click += (_, _) =>
            {
                _ninjaAnimator.Stop();
                _ninjaAnimator.Play(anim);
            };
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private void ToggleVisibility()
    {
        if (WindowState == WindowState.Minimized || !IsVisible)
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }
        else
        {
            Hide();
        }
    }
}
