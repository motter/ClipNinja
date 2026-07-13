using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
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
                // Auto-save: if enabled AND the capture is an image AND a
                // quick-save folder is configured (we do NOT prompt here —
                // interrupting a capture with a folder picker would be
                // hostile), write the PNG immediately. Failures are logged
                // + surfaced in status but never block the capture.
                if (_vm.Settings.AutoSaveScreenshotsToFolder
                    && content is Models.ImageContent autoIc
                    && autoIc.FullImage is not null)
                {
                    AutoSaveCapturedImage(autoIc);
                }
                using (Services.Trace.Time("capture", "PlayRandomMove"))
                {
                    PlayRandomMove();
                }
                ScrollSlotListToTop();
            });
        };

        // Auto-replace live clipboard with the bordered version when the
        // watcher signals it just baked a border. Without this, the user
        // would have to click the ClipNinja slot to paste with border —
        // defeating the purpose of "automatic border on screenshots."
        _clipWatcher.BorderedImageReadyForClipboardReplace += (_, payload) =>
        {
            Dispatcher.Invoke(() => ReplaceLiveClipboardWithBordered(payload.Bordered, payload.Signature));
        };

        _hotkeys.AttachTo(this);

        // Only register the show/hide hotkey. We DELIBERATELY don't intercept
        // Ctrl+V or any direct-slot hotkeys — those caused focus and clipboard
        // race conditions. Instead, the user clicks a slot to load it onto the
        // clipboard, then presses Ctrl+V natively in their target app.
        _hotkeys.Register(HotkeyService.CtrlShift, Key.N, ToggleVisibility);
        _hotkeys.Register(HotkeyService.CtrlShift, Key.B, () => _vm.TogglePause());
        // Screen capture hotkeys — user-configurable in Settings
        // (defaults: Ctrl+Shift+C region, Ctrl+Shift+Z full screen).
        RegisterCaptureHotkeys();
        // Fire-and-forget background update check (respects the
        // AutoCheckForUpdates setting; needs UpdateRepo configured).
        CheckForUpdatesSilently();

        _previewPopup = new Views.PreviewPopup(this);

        // Ninja animator — drives the named transforms in MainWindow.xaml.
        _ninjaAnimator = new Animations.NinjaAnimator(
            NinjaTranslate, NinjaRotate, NinjaScale,
            LeftArmRotate, RightArmRotate,
            LeftLegRotate, RightLegRotate,
            HeadRotate,
            FireballTranslate, FireballScale, FireballRoot);

        _vm.StatusText = "Click a slot to load it onto the clipboard, then Ctrl+V to paste";

        // Mirror image-effect preferences into the watcher so newly captured
        // images get processed (or not) according to user settings.
        _clipWatcher.AddBorderToImages = _vm.Settings.AddBorderToImages;
        _clipWatcher.AddDropShadowToImages = _vm.Settings.AddDropShadowToImages;
        _clipWatcher.AddTornTopEdge = _vm.Settings.AddTornTopEdge;
        _clipWatcher.AddTornBottomEdge = _vm.Settings.AddTornBottomEdge;
        _clipWatcher.AddTornLeftEdge = _vm.Settings.AddTornLeftEdge;
        _clipWatcher.AddTornRightEdge = _vm.Settings.AddTornRightEdge;
        UpdateBorderToggleVisual();

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

            // First Recent item must have content
            if (_vm.RecentItems.Count == 0) return;
            var slot1 = _vm.RecentItems[0];
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

    /// <summary>
    /// Hovering a history row shows the preview popup the same way slot
    /// hovers do — same UI, same image-popup-on-double-click, same hyperlinks
    /// section. The only difference is the entry point: we feed content
    /// directly into the popup rather than going via a ClipSlot.
    /// </summary>
    private void OnHistoryRow_MouseEnter(object sender, MouseEventArgs e)
    {
        if (DateTime.UtcNow < _suppressPreviewUntil) return;

        if (sender is FrameworkElement fe && fe.DataContext is Models.HistoryItem item)
        {
            double anchorTop = 0;
            try
            {
                var topLeft = fe.PointToScreen(new System.Windows.Point(0, 0));
                anchorTop = topLeft.Y;
            }
            catch { /* not yet in visual tree */ }
            _previewPopup?.ScheduleShowContent(item.Content, anchorTop);
        }
    }

    private void OnHistoryRow_MouseLeave(object sender, MouseEventArgs e)
    {
        _previewPopup?.Hide();
    }

    // ── History drag-out ─────────────────────────────────────────────────
    private Models.HistoryItem? _historyDragStart;
    private System.Windows.Point _historyDragStartPoint;
    private bool _historyDragInitiated;

    /// <summary>
    /// Remember the click position when the user presses on a history row,
    /// so MouseMove can detect when motion exceeds the drag threshold.
    /// History drag is OUTPUT-ONLY (no reorder within history), so we just
    /// need the standard formats for external drop targets.
    /// </summary>
    private void OnHistoryRow_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is Models.HistoryItem item)
        {
            _historyDragStart = item;
            _historyDragStartPoint = e.GetPosition(this);
            _historyDragInitiated = false;
        }
    }

    private void OnHistoryRow_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (_historyDragStart is null || _historyDragInitiated) return;

        var current = e.GetPosition(this);
        var dx = current.X - _historyDragStartPoint.X;
        var dy = current.Y - _historyDragStartPoint.Y;
        if (Math.Abs(dx) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(dy) < SystemParameters.MinimumVerticalDragDistance)
            return;

        _historyDragInitiated = true;
        _previewPopup?.HideImmediate();

        var data = new DataObject();
        PopulateStandardDragFormats(data, _historyDragStart.Content);

        try
        {
            DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Copy);
        }
        catch { /* canceled */ }

        _historyDragStart = null;
        _historyDragInitiated = false;
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

        // Double-click → edit nickname. Note the WPF event sequence for
        // a double-click is Down(ClickCount=1), Up, Down(ClickCount=2),
        // Up — so the FIRST click has already loaded the slot to the
        // clipboard by the time we get here. That's harmless (loading is
        // non-destructive), and the nickname prompt opening on top is
        // exactly what the user asked for. We set _suppressNextClickLoad
        // so the SECOND MouseUp doesn't re-load and stomp the status
        // text that the prompt flow sets.
        if (e.ClickCount == 2 && !slot.IsEmpty)
        {
            _suppressNextClickLoad = true;
            e.Handled = true;
            PromptForNickname(slot);
            return;
        }

        // Record where the click started so we can distinguish click from drag
        _dragStartPoint = e.GetPosition(this);
        _dragStartSlot = slot;
        _dragInitiated = false;
    }

    /// <summary>Set when a double-click was detected on MouseDown, so the
    /// trailing MouseUp of the second click doesn't ALSO fire the
    /// single-click load action.</summary>
    private bool _suppressNextClickLoad;

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

        // Start the drag-drop operation.
        _dragInitiated = true;
        _previewPopup?.HideImmediate();   // hide preview during drag

        // Build a multi-format DataObject:
        //
        //   • "ClipNinjaSlotIndex" (custom)      — used by our internal drop
        //     handlers to recognize a same-app reorder and react with Move.
        //   • Standard text/image formats        — let external apps (Word,
        //     Outlook, browser, image viewer, etc.) accept the drop and
        //     paste the content as if from the clipboard.
        //
        // Receivers pick whichever format they understand. ClipNinja's drop
        // handler looks for the custom format first; external apps look for
        // standard formats. Drop effect AllowedEffects = Copy|Move lets the
        // receiver choose: external apps default to Copy (slot stays in
        // ClipNinja), internal drops keep using Move.
        var data = new DataObject();
        data.SetData("ClipNinjaSlotIndex", _dragStartSlot.Index);
        if (_dragStartSlot.Content is not null)
        {
            PopulateStandardDragFormats(data, _dragStartSlot.Content);
        }

        try
        {
            DragDrop.DoDragDrop(
                (DependencyObject)sender,
                data,
                DragDropEffects.Copy | DragDropEffects.Move);
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

    /// <summary>
    /// Add standard clipboard formats to a DataObject so external apps can
    /// accept a drop FROM ClipNinja. Mirrors the formats we write during a
    /// slot click (text: UnicodeText + optional HTML/RTF; image: Bitmap +
    /// PNG byte stream). Failure is silent — the internal reorder format
    /// stays valid regardless.
    /// </summary>
    private static void PopulateStandardDragFormats(DataObject data, ClipContent content)
    {
        try
        {
            switch (content)
            {
                case TextContent tc:
                    data.SetData(DataFormats.UnicodeText, tc.Text);
                    data.SetData(DataFormats.Text, tc.Text);
                    if (!string.IsNullOrEmpty(tc.HtmlFormat))
                        data.SetData(DataFormats.Html, tc.HtmlFormat);
                    if (!string.IsNullOrEmpty(tc.RtfFormat))
                        data.SetData(DataFormats.Rtf, tc.RtfFormat);
                    break;

                case ImageContent ic when ic.FullImage is not null:
                    // Standard bitmap format (CF_BITMAP / DIB)
                    data.SetImage(ic.FullImage);
                    // Plus PNG byte stream (Office apps prefer this when present)
                    try
                    {
                        var pngStream = new System.IO.MemoryStream();
                        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(ic.FullImage));
                        encoder.Save(pngStream);
                        pngStream.Position = 0;
                        data.SetData("PNG", pngStream);
                    }
                    catch { /* PNG encode failed; bitmap format is still valid */ }
                    break;
            }
        }
        catch (Exception ex)
        {
            Services.Trace.Log("drag", $"PopulateStandardDragFormats threw: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void OnSlotRow_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // A double-click already handled this interaction (nickname edit);
        // swallow the trailing MouseUp so it doesn't re-load the slot.
        if (_suppressNextClickLoad)
        {
            _suppressNextClickLoad = false;
            _dragInitiated = false;
            _dragStartSlot = null;
            return;
        }
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
            ShowDropIndicator(targetSlot);
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

        // The "ClipNinjaSlotIndex" payload is the source ClipSlot's Index
        // value as an int. To resolve back to the actual ClipSlot reference,
        // we use the cached _dragStartSlot field set when the drag began.
        if (_dragStartSlot is null) return;
        if (sender is not FrameworkElement fe || fe.DataContext is not ClipSlot dropTarget) return;
        if (ReferenceEquals(_dragStartSlot, dropTarget)) return;

        ReorderOrMove(_dragStartSlot, dropTarget);
        e.Handled = true;
    }

    /// <summary>
    /// v2.4 reorder/move logic. Four cases based on which list each item
    /// belongs to:
    ///   • Both in Pinned   → reorder within Pinned
    ///   • Both in Recent   → reorder within Recent
    ///   • Pinned → Recent  → unpin and insert at the target position in Recent
    ///   • Recent → Pinned  → pin and insert at the target position in Pinned
    ///
    /// Compared to the v2.3 model, there's no Memory Hole, no "hole is full"
    /// rejection, no positional pinning. Pin/unpin is a list move; reorder
    /// is a list move. One unified mechanism.
    /// </summary>
    private void ReorderOrMove(ClipSlot src, ClipSlot dst)
    {
        bool srcInPinned = _vm.PinnedItems.Contains(src);
        bool srcInRecent = _vm.RecentItems.Contains(src);
        bool dstInPinned = _vm.PinnedItems.Contains(dst);
        bool dstInRecent = _vm.RecentItems.Contains(dst);
        if (!(srcInPinned || srcInRecent)) return;
        if (!(dstInPinned || dstInRecent)) return;

        if (srcInPinned && dstInPinned)
        {
            int from = _vm.PinnedItems.IndexOf(src);
            int to = _vm.PinnedItems.IndexOf(dst);
            if (from >= 0 && to >= 0 && from != to)
            {
                _vm.PinnedItems.Move(from, to);
                _vm.StatusText = "Pinned reordered";
            }
        }
        else if (srcInRecent && dstInRecent)
        {
            int from = _vm.RecentItems.IndexOf(src);
            int to = _vm.RecentItems.IndexOf(dst);
            if (from >= 0 && to >= 0 && from != to)
            {
                _vm.RecentItems.Move(from, to);
                _vm.PasteIdx = to + 1;
                _vm.StatusText = "Recent reordered";
            }
        }
        else if (srcInPinned && dstInRecent)
        {
            int toIdx = _vm.RecentItems.IndexOf(dst);
            _vm.PinnedItems.Remove(src);
            src.IsPinned = false;
            if (toIdx < 0) toIdx = 0;
            _vm.RecentItems.Insert(toIdx, src);
            _vm.PasteIdx = toIdx + 1;
            _vm.StatusText = "Unpinned — moved to Recent";
        }
        else if (srcInRecent && dstInPinned)
        {
            int toIdx = _vm.PinnedItems.IndexOf(dst);
            _vm.RecentItems.Remove(src);
            src.IsPinned = true;
            if (toIdx < 0) toIdx = 0;
            _vm.PinnedItems.Insert(toIdx, src);
            _vm.StatusText = "📌 Pinned";
        }

        _vm.ScheduleSave();
    }

    /// <summary>Visual: highlight a slot row as the drop target.</summary>
    private ClipSlot? _currentDropIndicatorItem;
    private void ShowDropIndicator(ClipSlot? item)
    {
        if (ReferenceEquals(_currentDropIndicatorItem, item)) return;
        ClearDropIndicator();
        if (item is not null)
        {
            _currentDropIndicatorItem = item;
            item.IsDropTarget = true;
        }
    }

    private void ClearDropIndicator()
    {
        if (_currentDropIndicatorItem is not null)
        {
            _currentDropIndicatorItem.IsDropTarget = false;
            _currentDropIndicatorItem = null;
        }
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

        // If the user just clicked one of the top few items, scroll the
        // Recent list back to the top so the active item is visible.
        // (Only meaningful for Recent clicks — Pinned is bounded above
        // and always visible.)
        if (slot.Index <= 3) ScrollSlotListToTop();

        // Each load attempt gets a unique token. If the user clicks another
        // slot before our retries finish, the new click bumps the token and
        // any in-flight retry chain self-terminates.
        int myToken = ++_loadToken;

        // Update UI immediately for snappy feedback.
        //
        // The "active" highlight is a single visual focus across BOTH lists:
        //   • Recent click   → PasteIdx = slot.Index (highlights this Recent
        //                      row; cycle hotkeys target it).
        //   • Pinned click   → SetActivePinned(slot) (highlights this Pinned
        //                      row only; clears Recent highlight and PasteIdx
        //                      so the cycle doesn't drift to a same-numbered
        //                      Recent row — the original v2.4.0 herky-jerky
        //                      bug).
        // Either way, exactly one row across the two lists shows the amber
        // active style.
        using (Services.Trace.Time("load", "set active highlight + StatusText"))
        {
            bool inRecent = _vm.RecentItems.Contains(slot);
            if (inRecent)
            {
                _vm.PasteIdx = slot.Index;
                _vm.StatusText = $"Loading Recent item {slot.Index}…";
            }
            else
            {
                _vm.SetActivePinned(slot);
                _vm.StatusText = "Loading pinned item…";
            }
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
    /// <summary>
    /// Replace the live system clipboard with the bordered version of an
    /// image that was just captured. Critical pre-step: extend the watcher's
    /// echo-suppression window AND set its LastWrittenSignature to OUR new
    /// bitmap's signature — otherwise the WM_CLIPBOARDUPDATE event our own
    /// write triggers would loop back as a new capture.
    ///
    /// Uses plain Clipboard.SetImage which we've confirmed works reliably
    /// when called from the dispatcher thread shortly after the source
    /// app's copy completes. Wrap in try/catch since clipboard access can
    /// briefly fail with COM exceptions when another app holds the lock.
    /// </summary>
    private void ReplaceLiveClipboardWithBordered(System.Windows.Media.Imaging.BitmapSource bordered, string signature)
    {
        try
        {
            // Pre-set the signature + extend suppression BEFORE writing.
            // Order matters: if we wrote first then set, the WM_CLIPBOARDUPDATE
            // for our write could fire before LastWrittenSignature is updated,
            // triggering a re-capture loop.
            _clipWatcher.LastWrittenSignature = signature;
            _clipWatcher.SuppressFor(1500);

            using (Services.Trace.Time("autoborder", $"Clipboard.SetImage ({bordered.PixelWidth}x{bordered.PixelHeight})"))
            {
                System.Windows.Clipboard.SetImage(bordered);
            }
            Services.Trace.Log("autoborder", "live clipboard replaced with bordered version");
        }
        catch (System.Exception ex)
        {
            // Don't bother the user — the bordered version is still safely
            // in the slot; they can click it to paste with border. Just log.
            Services.Trace.Log("autoborder", $"FAILED to replace live clipboard: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Write a TextContent that has rich formats (HTML and/or RTF) to the
    /// clipboard, populating EVERY format the source provided. Use
    /// SetDataObject(copy:true) so it atomically replaces the current
    /// clipboard contents — any leftover formats from a previous owner are
    /// wiped, and the data survives this process exiting.
    ///
    /// The hyperlinks the user originally copied from Outlook/Word/web are
    /// preserved in the HTML+RTF payloads. When they paste this slot into
    /// Word, Word picks the HTML and renders the clickable links.
    /// </summary>
    private bool WriteRichTextRobust(TextContent tc)
    {
        try
        {
            var data = new System.Windows.DataObject();
            // Always include plain text — receivers that don't understand
            // HTML or RTF (Notepad, code editors, web text inputs) get the
            // text fallback.
            data.SetText(tc.Text);
            if (!string.IsNullOrEmpty(tc.HtmlFormat))
                data.SetData(System.Windows.DataFormats.Html, tc.HtmlFormat);
            if (!string.IsNullOrEmpty(tc.RtfFormat))
                data.SetData(System.Windows.DataFormats.Rtf, tc.RtfFormat);
            System.Windows.Clipboard.SetDataObject(data, copy: true);
            return true;
        }
        catch (Exception ex)
        {
            Services.Trace.Log("write", $"WriteRichTextRobust threw: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Write an image to the clipboard such that target apps (Word, Outlook,
    /// browsers, etc.) all paste OUR (bordered) version — not leftover
    /// formats from the original copying app (Greenshot, etc).
    ///
    /// Approach:
    ///  1. Clear the clipboard first to wipe leftover formats.
    ///  2. Build a fresh DataObject. Add the BORDERED bitmap as multiple
    ///     formats: Bitmap (CF_BITMAP), DeviceIndependentBitmap, AND a
    ///     PNG-encoded byte stream (which Office apps prefer when present).
    ///  3. Commit via SetDataObject(data, copy:true) — copy:true makes the
    ///     data persist after our process exits AND replaces the clipboard
    ///     atomically.
    ///  4. Always-on logging to %AppData%\ClipNinja\image-debug.log so we
    ///     can verify what was actually sent (bordered dimensions, formats
    ///     written, any exceptions).
    /// </summary>
    private bool WriteImageRobust(System.Windows.Media.Imaging.BitmapSource bordered, int slotIdx)
    {
        LogImageDebug($"[slot {slotIdx}] WriteImageRobust BEGIN; bitmap is {bordered.PixelWidth}x{bordered.PixelHeight}, format={bordered.Format}");

        try
        {
            // Step 1: Clear leftover formats. Some apps (Greenshot, browsers)
            // populate multiple clipboard formats on copy. If we only call
            // SetImage, the OTHER formats remain in place — and target apps
            // may pick one of those instead of ours during paste.
            try { System.Windows.Clipboard.Clear(); LogImageDebug("  Clipboard.Clear() succeeded"); }
            catch (Exception ex) { LogImageDebug($"  Clipboard.Clear() threw: {ex.Message}"); }

            // Step 2: encode the bordered bitmap to PNG. Office apps prefer
            // PNG over CF_DIB when both are present. We need to KEEP this
            // stream alive — DataObject only stores a reference.
            var pngStream = new System.IO.MemoryStream();
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bordered));
            encoder.Save(pngStream);
            pngStream.Position = 0;
            LogImageDebug($"  PNG encoded: {pngStream.Length} bytes");

            // Step 3: build the DataObject with multiple formats — all
            // describing the SAME bordered bitmap.
            var data = new System.Windows.DataObject();
            data.SetImage(bordered);            // CF_BITMAP / DIB
            data.SetData("PNG", pngStream);     // The PNG byte stream
            LogImageDebug("  DataObject populated with Image + PNG");

            // Step 4: commit. copy:true → data persists after our process,
            // and the clipboard is replaced atomically (no race with
            // background processes asserting their own formats).
            System.Windows.Clipboard.SetDataObject(data, copy: true);
            LogImageDebug("  SetDataObject(copy:true) succeeded");

            // Step 5: verify by reading back what formats are now present.
            try
            {
                var present = System.Windows.Clipboard.GetDataObject()?.GetFormats() ?? Array.Empty<string>();
                LogImageDebug($"  After write, clipboard reports formats: {string.Join(", ", present)}");
            }
            catch (Exception ex) { LogImageDebug($"  Format-read after write threw: {ex.Message}"); }

            LogImageDebug($"[slot {slotIdx}] WriteImageRobust END (success)");
            return true;
        }
        catch (Exception ex)
        {
            LogImageDebug($"[slot {slotIdx}] WriteImageRobust THREW: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Append a single line to %AppData%\ClipNinja\image-debug.log. Only
    /// active when Trace.Enabled is on — was always-on during border-paste
    /// diagnosis but now lives behind the normal tracer flag. File rotates
    /// at 500KB to keep disk usage bounded.
    /// </summary>
    private static void LogImageDebug(string line)
    {
        if (!Services.Trace.Enabled) return;
        try
        {
            var dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ClipNinja");
            System.IO.Directory.CreateDirectory(dir);
            var path = System.IO.Path.Combine(dir, "image-debug.log");
            // Rotate when over 500KB
            try
            {
                var fi = new System.IO.FileInfo(path);
                if (fi.Exists && fi.Length > 500_000)
                {
                    var prev = System.IO.Path.Combine(dir, "image-debug.log.prev");
                    if (System.IO.File.Exists(prev)) System.IO.File.Delete(prev);
                    System.IO.File.Move(path, prev);
                }
            }
            catch { }
            System.IO.File.AppendAllText(path,
                $"[{DateTime.Now:HH:mm:ss.fff}] {line}{Environment.NewLine}");
        }
        catch { /* best-effort */ }
    }

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
                        if (tc.HasRichFormats)
                        {
                            // Rich text path: use a DataObject with plain text
                            // PLUS HTML/RTF if present. SetDataObject(copy:true)
                            // replaces the whole clipboard atomically. Target
                            // apps pick whichever format they understand — Word
                            // and Outlook prefer HTML/RTF (preserving the
                            // hyperlinks the user copied from email), Notepad
                            // and code editors take the plain text. Best of
                            // both worlds.
                            using (Services.Trace.Time("write", $"DataObject multi-format slot {slotIdx} attempt {attempt} (text={tc.Text.Length}, html={tc.HtmlFormat?.Length ?? 0}, rtf={tc.RtfFormat?.Length ?? 0})"))
                            {
                                succeeded = WriteRichTextRobust(tc);
                            }
                        }
                        else
                        {
                            // Plain text path: NativeClipboard's Win32 SetText
                            // is fast and reliable; no reason to involve WPF.
                            using (Services.Trace.Time("write", $"NativeClipboard.SetText slot {slotIdx} attempt {attempt} (len={tc.Text.Length})"))
                            {
                                succeeded = Services.NativeClipboard.SetText(tc.Text, maxWaitMs: 250);
                            }
                        }
                        break;
                    }
                case ImageContent ic when ic.FullImage is not null:
                    using (Services.Trace.Time("write", $"Clipboard.SetImage slot {slotIdx} attempt {attempt} ({ic.FullImage.PixelWidth}x{ic.FullImage.PixelHeight})"))
                    {
                        succeeded = WriteImageRobust(ic.FullImage, slotIdx);
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
        if (slot is null) return;
        // Empty items shouldn't exist in v2.4+ (collections only hold items
        // with content), but be defensive anyway.
        if (slot.IsEmpty) return;
        _vm.TogglePin(slot);
    }

    private void OnSlotMenu_SetNickname(object sender, RoutedEventArgs e)
    {
        var slot = GetSlotFrom(sender);
        if (slot is null || slot.IsEmpty) return;
        PromptForNickname(slot);
    }

    /// <summary>Shared nickname-edit flow — used by the context menu item
    /// AND double-clicking a slot row. Shows the input prompt pre-filled
    /// with the current nickname; empty input removes the nickname.</summary>
    private void PromptForNickname(Models.ClipSlot slot)
    {
        var input = Views.InputPrompt.Show(
            this,
            $"Enter a nickname for this item (max 50 characters).\n\nLeave blank to remove the nickname.",
            "Item Nickname",
            slot.Nickname,
            maxLength: 50);
        if (input is null) return;

        slot.Nickname = input;
        _vm.StatusText = string.IsNullOrEmpty(input)
            ? "Nickname removed"
            : $"Nickname: {input}";
        _vm.ScheduleSave();
    }

    /// <summary>
    /// Open the annotation editor for an image slot. On save, the slot's
    /// content is REPLACED with the annotated bitmap: fresh ImageContent
    /// (init-only props prevent in-place mutation), fresh thumbnail,
    /// empty SavedFileName so persistence writes a new PNG. The
    /// annotated version is also loaded onto the live clipboard so an
    /// immediate paste gets the marked-up image — that's almost always
    /// why the user annotated it in the first place.
    /// </summary>
    private void OnSlotMenu_AnnotateImage(object sender, RoutedEventArgs e)
    {
        var slot = GetSlotFrom(sender);
        if (slot is not null) AnnotateSlot(slot);
    }

    /// <summary>Pencil badge on the slot thumbnail — same flow as the
    /// context-menu item, one click closer. Handled=true so the click
    /// doesn't bubble up and ALSO fire the row's load-to-clipboard.</summary>
    private void OnSlotPencil_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true;
        var slot = GetSlotFrom(sender);
        if (slot is not null) AnnotateSlot(slot);
    }

    private void AnnotateSlot(Models.ClipSlot slot)
    {
        if (slot.IsEmpty) return;
        if (slot.Content is not Models.ImageContent ic || ic.FullImage is null)
        {
            _vm.StatusText = "That item isn't an image";
            return;
        }

        var annotated = Views.ImageAnnotator.Show(this, ic.FullImage, _vm.Settings, () => _vm.ScheduleSave());
        if (annotated is null) return;  // canceled or nothing drawn

        BitmapSource thumb;
        using (Services.Trace.Time("annotate", "MakeThumbnail"))
        {
            thumb = Services.ClipboardWatcher.MakeThumbnail(annotated, 32);
        }

        slot.Content = new Models.ImageContent
        {
            FullImage = annotated,
            Thumbnail = thumb,
            OriginalWidth = annotated.PixelWidth,
            OriginalHeight = annotated.PixelHeight,
            DisplayLabel = ic.DisplayLabel,
            // SavedFileName intentionally left empty — persistence will
            // encode + write the annotated PNG on next save. The old
            // PNG becomes orphaned; persistence's stale-file cleanup
            // reclaims it.
        };
        _vm.ScheduleSave();

        // Put the annotated version on the live clipboard for instant paste.
        LoadSlotToClipboard(slot);
        _vm.StatusText = "✓ Annotations saved — paste to use the marked-up image";
    }

    /// <summary>
    /// Save the slot's image PNG to the user's quick-save folder with a
    /// human-friendly filename. Prompts for a folder on first use (or if
    /// the configured folder no longer exists). Filename preference:
    /// nickname if set (sanitized), else "Screenshot", suffixed with a
    /// timestamp to guarantee uniqueness.
    /// </summary>
    private void OnSlotMenu_SaveImage(object sender, RoutedEventArgs e)
    {
        var slot = GetSlotFrom(sender);
        if (slot is null || slot.IsEmpty) return;
        if (slot.Content is not Models.ImageContent ic || ic.FullImage is null)
        {
            _vm.StatusText = "That item isn't an image";
            return;
        }

        // Ensure we have a usable target folder — configured AND existing.
        var folder = EnsureQuickSaveFolder();
        if (folder is null) return;  // user canceled the picker

        try
        {
            // Friendly filename: nickname (sanitized) or "Screenshot",
            // plus timestamp so repeated saves never collide.
            string baseName = string.IsNullOrWhiteSpace(slot.Nickname)
                ? "Screenshot"
                : SanitizeFileName(slot.Nickname);
            string fileName = $"{baseName} {DateTime.Now:yyyy-MM-dd HHmmss}.png";
            string destPath = System.IO.Path.Combine(folder, fileName);

            // Prefer copying the already-encoded PNG from the images dir
            // (fast, no re-encode). Fall back to encoding the in-memory
            // bitmap if the file doesn't exist yet (persistence saves on
            // a debounce, so a fresh capture may not be on disk yet).
            string existingPng = string.IsNullOrEmpty(ic.SavedFileName)
                ? ""
                : System.IO.Path.Combine(_vm.Persistence.ImagesDir, ic.SavedFileName);
            if (!string.IsNullOrEmpty(existingPng) && System.IO.File.Exists(existingPng))
            {
                System.IO.File.Copy(existingPng, destPath, overwrite: false);
            }
            else
            {
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(ic.FullImage));
                using var fs = new System.IO.FileStream(destPath, System.IO.FileMode.CreateNew);
                encoder.Save(fs);
            }

            _vm.StatusText = $"✓ Saved: {fileName}";
            Services.Trace.Log("save", $"quick-saved image to {destPath}");
        }
        catch (Exception ex)
        {
            _vm.StatusText = $"Save failed: {ex.Message}";
            Services.Trace.Log("save", $"quick-save FAILED: {ex}");
        }
    }

    /// <summary>
    /// Returns the quick-save folder path, prompting the user to pick one
    /// if it's not configured or the configured path no longer exists.
    /// Returns null if the user cancels the picker. The picked folder is
    /// persisted to settings so subsequent saves are one-click.
    /// </summary>
    private string? EnsureQuickSaveFolder()
    {
        var configured = _vm.Settings.QuickSaveFolder;
        if (!string.IsNullOrWhiteSpace(configured) && System.IO.Directory.Exists(configured))
            return configured;

        // Not configured (or folder was deleted/moved). Ask the user.
        // OpenFolderDialog is WPF-native as of .NET 8 — no WinForms
        // reference needed.
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Choose a folder for quick-saved screenshots",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dlg.ShowDialog(this) != true) return null;

        var chosen = dlg.FolderName;
        _vm.Settings.QuickSaveFolder = chosen;
        _vm.ScheduleSave();
        _vm.StatusText = $"Quick-save folder set: {chosen}";
        return chosen;
    }

    /// <summary>Strip characters Windows filenames can't contain, collapse
    /// runs of whitespace, and cap length so nickname-derived filenames
    /// are always valid.</summary>
    private static string SanitizeFileName(string raw)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder(raw.Length);
        foreach (var ch in raw)
        {
            sb.Append(Array.IndexOf(invalid, ch) >= 0 ? ' ' : ch);
        }
        var cleaned = System.Text.RegularExpressions.Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
        if (cleaned.Length > 60) cleaned = cleaned.Substring(0, 60).Trim();
        return string.IsNullOrEmpty(cleaned) ? "Screenshot" : cleaned;
    }

    /// <summary>
    /// Auto-save path: silently write a freshly captured image to the
    /// quick-save folder. Called from the capture handler when the
    /// AutoSaveScreenshotsToFolder setting is on. Never prompts —
    /// if the folder isn't configured or doesn't exist, we log + status
    /// and skip (prompting mid-capture would be hostile).
    /// </summary>
    private void AutoSaveCapturedImage(Models.ImageContent ic)
    {
        try
        {
            var folder = _vm.Settings.QuickSaveFolder;
            if (string.IsNullOrWhiteSpace(folder) || !System.IO.Directory.Exists(folder))
            {
                _vm.StatusText = "Auto-save skipped: quick-save folder not set (see Settings)";
                return;
            }

            string fileName = $"Screenshot {DateTime.Now:yyyy-MM-dd HHmmss-fff}.png";
            string destPath = System.IO.Path.Combine(folder, fileName);

            // Fresh captures aren't on disk yet (persistence debounces), so
            // encode the in-memory bitmap directly.
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(ic.FullImage));
            using var fs = new System.IO.FileStream(destPath, System.IO.FileMode.CreateNew);
            encoder.Save(fs);

            Services.Trace.Log("save", $"auto-saved capture to {destPath}");
        }
        catch (Exception ex)
        {
            _vm.StatusText = $"Auto-save failed: {ex.Message}";
            Services.Trace.Log("save", $"auto-save FAILED: {ex}");
        }
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
        // Find which list the slot lives in and move within that list.
        System.Collections.ObjectModel.ObservableCollection<ClipSlot>? list = null;
        if (_vm.PinnedItems.Contains(slot)) list = _vm.PinnedItems;
        else if (_vm.RecentItems.Contains(slot)) list = _vm.RecentItems;
        if (list is null) return;

        int from = list.IndexOf(slot);
        int to = from + direction;
        if (from < 0 || to < 0 || to >= list.Count) return;

        list.Move(from, to);
        if (ReferenceEquals(list, _vm.RecentItems)) _vm.PasteIdx = to + 1;
        _vm.StatusText = $"Moved {(direction == -1 ? "up" : "down")}";
        _vm.ScheduleSave();
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
        _vm.RemoveItem(slot);
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

    /// <summary>Click the ✕ on a slot row to remove that item.</summary>
    private void OnEraser_Click(object sender, MouseButtonEventArgs e)
    {
        var slot = GetSlotFrom(sender);
        if (slot is null || slot.IsEmpty) return;
        // Confirm if pinned (avoid accidental nuke of important content)
        if (slot.IsPinned)
        {
            var res = MessageBox.Show(
                "Remove this pinned item?",
                "Remove pinned item",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (res != MessageBoxResult.Yes) { e.Handled = true; return; }
        }
        _vm.RemoveItem(slot);
        e.Handled = true;
    }

    /// <summary>Click the lock icon to toggle pin state on this slot.</summary>
    private void OnLock_Click(object sender, MouseButtonEventArgs e)
    {
        var slot = GetSlotFrom(sender);
        if (slot is null) return;
        // Allow unpin on empty pinned slot; block PIN on empty (nothing to pin).
        if (slot.IsEmpty && !slot.IsPinned) return;
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
            // Show current border state in the menu label rather than via a
            // checkmark — easier to read and less prone to inverted-state bugs.
            TrayImageBorderItem.Header = _vm.Settings.AddBorderToImages
                ? "Black border on screenshots: ON"
                : "Black border on screenshots: OFF";
            RebuildMonitorSubmenu();
        }
        catch { /* leave whatever it was */ }
    }

    /// <summary>Populate the "Capture full screen" submenu: All monitors
    /// (the configured hotkey's action), then one entry per display with
    /// its resolution. Rebuilt on every tray-menu open so hot-plugged or
    /// removed displays are always current — monitors are enumerated
    /// fresh at click time too, so a stale index can't capture the
    /// wrong screen.</summary>
    private void RebuildMonitorSubmenu()
    {
        TrayCaptureFullItem.Items.Clear();

        var all = new MenuItem
        {
            Header = "All monitors",
            InputGestureText = _vm.Settings.CaptureFullHotkey,
        };
        all.Click += OnTrayCaptureFull_Click;
        TrayCaptureFullItem.Items.Add(all);
        TrayCaptureFullItem.Items.Add(new Separator());

        var mons = Services.ScreenCaptureService.GetMonitors();
        for (int i = 0; i < mons.Count; i++)
        {
            var m = mons[i];
            int index = i;  // capture loop variable for the closure
            var item = new MenuItem
            {
                Header = $"Monitor {i + 1}{(m.isPrimary ? " (primary)" : "")} — {m.width}×{m.height}",
            };
            item.Click += (_, _) => CaptureMonitorNow(index);
            TrayCaptureFullItem.Items.Add(item);
        }
    }

    /// <summary>Instant capture of a single monitor (tray submenu).
    /// Same hide-window + publish pipeline as the other capture paths.</summary>
    private void CaptureMonitorNow(int index)
    {
        try
        {
            bool wasVisible = IsVisible;
            if (wasVisible) Hide();
            Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
            System.Threading.Thread.Sleep(120);

            var shot = Services.ScreenCaptureService.CaptureMonitor(index);

            if (wasVisible) { Show(); Activate(); }
            if (shot is null) { _vm.StatusText = "Monitor capture failed (display changed?)"; return; }
            ShowCaptureChooser(shot, () => CaptureMonitorNow(index));
        }
        catch (Exception ex)
        {
            Services.Trace.Log("capture", $"monitor capture failed: {ex}");
            _vm.StatusText = $"Capture failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Toggle baking a black border onto captured images. Only affects images
    /// captured AFTER the toggle — existing slots keep whatever state they
    /// were captured with.
    /// </summary>
    private void OnTrayImageBorder_Click(object sender, RoutedEventArgs e)
    {
        ToggleImageBorder();
    }

    /// <summary>Header-bar quick toggle — same behavior as the tray item.</summary>
    private void OnBorderToggle_Click(object sender, RoutedEventArgs e)
    {
        ToggleImageBorder();
    }

    /// <summary>Single source of truth for flipping the border setting.
    /// Updates the watcher, tray label, header button, persists, and
    /// statuses. Both the tray menu item and header button route here so
    /// they can't drift apart.</summary>
    private void ToggleImageBorder()
    {
        // Invert the source-of-truth setting (not the menu state) so this
        // can't get out of sync from event-timing surprises.
        bool want = !_vm.Settings.AddBorderToImages;
        _vm.Settings.AddBorderToImages = want;
        _clipWatcher.AddBorderToImages = want;
        TrayImageBorderItem.Header = want
            ? "Black border on screenshots: ON"
            : "Black border on screenshots: OFF";
        UpdateBorderToggleVisual();
        _vm.ScheduleSave();
        _vm.StatusText = want
            ? "✓ Screenshots will get a black border"
            : "Screenshots will be captured raw (no border)";
    }

    /// <summary>Sync the header toggle button's look with the current
    /// border setting: full opacity when ON, dimmed when OFF, tooltip
    /// describing the CURRENT state + what a click does.</summary>
    private void UpdateBorderToggleVisual()
    {
        try
        {
            bool on = _vm.Settings.AddBorderToImages;
            BorderToggleButton.Opacity = on ? 1.0 : 0.35;
            BorderToggleButton.ToolTip = on
                ? "Black border on new screenshots: ON (click to turn off)"
                : "Black border on new screenshots: OFF (click to turn on)";
        }
        catch { /* button may not exist yet during XAML parse */ }
    }

    private void OnTraySettings_Click(object sender, RoutedEventArgs e)
    {
        // Make sure the main window is visible so the modal dialog has a
        // sensible owner location. If it's hidden in the tray, briefly
        // restore it before opening the dialog.
        bool wasHidden = !IsVisible;
        if (wasHidden) Show();

        OpenSettingsSafely();

        if (wasHidden) Hide();
    }

    /// <summary>Header ⚙ button — window is already visible in this path.</summary>
    private void OnSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        OpenSettingsSafely();
    }

    // ── In-app updates ───────────────────────────────────────────────

    /// <summary>Tray "Check for updates…" — full interactive flow:
    /// check, offer, download, restart. Mirrors the Settings button.</summary>
    private async void OnTrayCheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // The main window is Topmost — an OWNERLESS MessageBox opens
            // UNDERNEATH it and appears to "flash and vanish" (it's not
            // closing, it's being buried). Fix: make the window visible
            // and OWN every dialog in this flow; owned dialogs render
            // above their topmost owner.
            bool wasHidden = !IsVisible;
            if (wasHidden) Show();
            Activate();

            var (update, error) = await Services.UpdateService.CheckAsync(_vm.Settings.UpdateRepo);
            if (update is null)
            {
                MessageBox.Show(this,
                    error ?? $"✓ You're up to date (v{Services.UpdateService.CurrentVersion.ToString(3)}).",
                    "ClipNinja updates", MessageBoxButton.OK,
                    error is null ? MessageBoxImage.Information : MessageBoxImage.Warning);
                if (wasHidden) Hide();
                return;
            }
            var notes = string.IsNullOrWhiteSpace(update.Notes)
                ? ""
                : "\n\nRelease notes:\n" + (update.Notes.Length > 600 ? update.Notes[..600] + "…" : update.Notes);
            var answer = MessageBox.Show(this,
                $"ClipNinja {update.TagName} is available (you have v{Services.UpdateService.CurrentVersion.ToString(3)}).{notes}\n\nUpdate now? The app will restart.",
                "ClipNinja update", MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (answer != MessageBoxResult.Yes)
            {
                if (wasHidden) Hide();
                return;
            }
            _vm.StatusText = "Downloading update…";
            var applyError = await Services.UpdateService.DownloadAndStageAsync(update);
            if (applyError is not null)
            {
                _vm.StatusText = applyError;
                MessageBox.Show(this, applyError, "ClipNinja update",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            // Guaranteed exit: graceful shutdown, hard-exit fallback if
            // anything holds the process (and its file lock) open.
            Services.UpdateService.ShutdownForUpdate();
        }
        catch (Exception ex)
        {
            Services.Trace.Log("update", $"tray update flow failed: {ex}");
            _vm.StatusText = $"Update failed: {ex.Message}";
        }
    }

    /// <summary>Silent background check ~5s after startup (if enabled
    /// and a repo is configured). Success = a status-bar hint only —
    /// never a dialog. Errors stay in the trace log; a background
    /// check has no business popping message boxes.</summary>
    private async void CheckForUpdatesSilently()
    {
        try
        {
            // If the last self-update's swap script left a log, surface
            // it — a FAILED swap otherwise looks like "clicked yes,
            // nothing happened". Success logs just go to trace.
            var swapLog = Services.UpdateService.ConsumeSwapLog();
            if (swapLog is not null)
            {
                Services.Trace.Log("update", $"previous swap result: {swapLog.Trim()}");
                if (swapLog.Contains("FAILED", StringComparison.OrdinalIgnoreCase))
                    _vm.StatusText = "⚠ Last update failed to apply — see trace log (tray → Open trace log)";
            }

            if (!_vm.Settings.AutoCheckForUpdates) return;
            if (string.IsNullOrWhiteSpace(_vm.Settings.UpdateRepo)) return;
            await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(5));
            var (update, error) = await Services.UpdateService.CheckAsync(_vm.Settings.UpdateRepo);
            if (update is not null)
            {
                _vm.StatusText = $"⬆ ClipNinja {update.TagName} available — tray menu → Check for updates";
                Services.Trace.Log("update", $"startup check: {update.TagName} available");
            }
            else if (error is not null)
            {
                Services.Trace.Log("update", $"startup check: {error}");
            }
        }
        catch (Exception ex)
        {
            Services.Trace.Log("update", $"silent check failed: {ex.Message}");
        }
    }

    // ── Built-in screen capture ──────────────────────────────────────

    /// <summary>Registered hotkey IDs for the two capture actions, so
    /// changing them in Settings can unregister the old combos first.
    /// -1 = not currently registered.</summary>
    private int _regionHotkeyId = -1;
    private int _fullHotkeyId = -1;

    /// <summary>(Re-)register the capture hotkeys from settings. Called
    /// at startup and whenever settings change. Unregisters previous
    /// registrations first so stale combos don't linger. Registration
    /// can fail if another app owns the combo — we surface that in the
    /// status bar instead of failing silently, and the 📷 button + tray
    /// items always work regardless.</summary>
    private void RegisterCaptureHotkeys()
    {
        _hotkeys.Unregister(_regionHotkeyId);
        _hotkeys.Unregister(_fullHotkeyId);
        _regionHotkeyId = -1;
        _fullHotkeyId = -1;

        var failures = new List<string>();

        if (HotkeyService.TryParse(_vm.Settings.CaptureRegionHotkey, out var rMods, out var rKey))
        {
            _regionHotkeyId = _hotkeys.Register(rMods, rKey, () => StartRegionCapture());
            if (_regionHotkeyId < 0) failures.Add(_vm.Settings.CaptureRegionHotkey);
        }
        else if (!string.IsNullOrWhiteSpace(_vm.Settings.CaptureRegionHotkey))
        {
            failures.Add($"{_vm.Settings.CaptureRegionHotkey} (unrecognized)");
        }

        if (HotkeyService.TryParse(_vm.Settings.CaptureFullHotkey, out var fMods, out var fKey))
        {
            _fullHotkeyId = _hotkeys.Register(fMods, fKey, () => CaptureFullScreenNow());
            if (_fullHotkeyId < 0) failures.Add(_vm.Settings.CaptureFullHotkey);
        }
        else if (!string.IsNullOrWhiteSpace(_vm.Settings.CaptureFullHotkey))
        {
            failures.Add($"{_vm.Settings.CaptureFullHotkey} (unrecognized)");
        }

        if (failures.Count > 0)
        {
            var msg = $"Capture hotkey unavailable: {string.Join(", ", failures)} — owned by another app?";
            Services.Trace.Log("capture", msg);
            _vm.StatusText = msg;
        }

        // Keep the visible hints honest — tray gesture text and the 📷
        // tooltip show whatever the user actually configured.
        try
        {
            TrayCaptureRegionItem.InputGestureText = _vm.Settings.CaptureRegionHotkey;
            // TrayCaptureFullItem is a submenu parent now — its "All
            // monitors" child gets the gesture text when the submenu is
            // rebuilt on tray open (RebuildMonitorSubmenu).
            CaptureButton.ToolTip =
                $"Capture a screen region ({_vm.Settings.CaptureRegionHotkey}) • " +
                $"{_vm.Settings.CaptureFullHotkey} = full screen";
        }
        catch { /* elements may not exist during early startup */ }
    }

    /// <summary>Header 📷 button → region capture.</summary>
    private void OnCaptureButton_Click(object sender, RoutedEventArgs e) => StartRegionCapture();

    private void OnTrayCaptureRegion_Click(object sender, RoutedEventArgs e) => StartRegionCapture();
    private void OnTrayCaptureFull_Click(object sender, RoutedEventArgs e) => CaptureFullScreenNow();

    /// <summary>Open the frozen-screen region selector; on selection,
    /// route the crop through the clipboard so the watcher ingests it
    /// exactly like any other screenshot — border / torn edges / drop
    /// shadow settings apply automatically, it lands in the top slot,
    /// and it's immediately pasteable. One pipeline, no special cases.
    ///
    /// If ClipNinja's window is visible we hide it for the capture so
    /// it doesn't photobomb the screenshot, and restore after.</summary>
    private void StartRegionCapture()
    {
        try
        {
            bool wasVisible = IsVisible;
            if (wasVisible) Hide();
            // Give the window a beat to actually leave the screen before
            // the selector freezes it.
            Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
            System.Threading.Thread.Sleep(120);

            var shot = Views.RegionSelectorWindow.SelectAndCapture();

            if (wasVisible) { Show(); Activate(); }
            if (shot is null) return;  // canceled
            ShowCaptureChooser(shot, StartRegionCapture);
        }
        catch (Exception ex)
        {
            Services.Trace.Log("capture", $"region capture failed: {ex}");
            _vm.StatusText = $"Capture failed: {ex.Message}";
        }
    }

    /// <summary>Instant full-virtual-screen grab (Ctrl+PrintScreen or
    /// tray item) — no selector UI.</summary>
    private void CaptureFullScreenNow()
    {
        try
        {
            bool wasVisible = IsVisible;
            if (wasVisible) Hide();
            Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
            System.Threading.Thread.Sleep(120);

            var shot = Services.ScreenCaptureService.CaptureFullScreen();

            if (wasVisible) { Show(); Activate(); }
            if (shot is null) { _vm.StatusText = "Capture failed"; return; }
            ShowCaptureChooser(shot, CaptureFullScreenNow);
        }
        catch (Exception ex)
        {
            Services.Trace.Log("capture", $"full capture failed: {ex}");
            _vm.StatusText = $"Capture failed: {ex.Message}";
        }
    }

    /// <summary>Route a fresh capture through the post-capture chooser
    /// (Redo / Annotate / Send / Quick save). "Redo" re-runs whatever
    /// capture flow produced this shot. Annotator preferences persist
    /// through the settings passthrough.</summary>
    private void ShowCaptureChooser(System.Windows.Media.Imaging.BitmapSource shot, Action redo)
    {
        Views.CaptureChooser.Show(
            this, shot, _vm.Settings,
            sendToClipNinja: PublishCapture,
            status: s => _vm.StatusText = s,
            redoCapture: redo,
            persistSettings: () => _vm.ScheduleSave());
    }

    /// <summary>Put a fresh capture onto the live clipboard. The
    /// ClipboardWatcher picks it up like any external screenshot:
    /// effects bake in, it lands in the top slot, auto-save fires if
    /// enabled. Single ingestion path = consistent behavior.</summary>
    private void PublishCapture(System.Windows.Media.Imaging.BitmapSource shot)
    {
        try
        {
            System.Windows.Clipboard.SetImage(shot);
            _vm.StatusText = $"📷 Captured {shot.PixelWidth}×{shot.PixelHeight}";
            Services.Trace.Log("capture", $"published {shot.PixelWidth}x{shot.PixelHeight} to clipboard");
        }
        catch (Exception ex)
        {
            Services.Trace.Log("capture", $"clipboard publish failed: {ex.Message}");
            _vm.StatusText = "Capture succeeded but the clipboard is locked — try again";
        }
    }

    /// <summary>Open the settings dialog with a hard failure boundary.
    /// If dialog construction throws for any reason, the exception is
    /// logged in full and reported in a message box instead of taking
    /// down the app. (Added after a report of the app crashing when
    /// opening settings from the tray — the global handler in App.xaml.cs
    /// is the outer net; this is the local one with better context.)</summary>
    private void OpenSettingsSafely()
    {
        try
        {
            Views.SettingsDialog.Show(this, _vm.Settings, _vm, OnSettingsChangedFromDialog);
        }
        catch (Exception ex)
        {
            Services.Trace.Log("settings", $"settings dialog failed: {ex}");
            MessageBox.Show(this,
                $"Couldn't open Settings:\n\n{ex.GetType().Name}: {ex.Message}\n\n" +
                "Details were written to the trace log (tray menu → Open trace log). " +
                "Please share that log so this can be fixed properly.",
                "ClipNinja — Settings error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Called whenever the Settings dialog changes a toggle. Syncs the
    /// runtime state (watcher flags, ninja visibility) and persists.
    /// </summary>
    private void OnSettingsChangedFromDialog()
    {
        _clipWatcher.AddBorderToImages = _vm.Settings.AddBorderToImages;
        _clipWatcher.AddDropShadowToImages = _vm.Settings.AddDropShadowToImages;
        _clipWatcher.AddTornTopEdge = _vm.Settings.AddTornTopEdge;
        _clipWatcher.AddTornBottomEdge = _vm.Settings.AddTornBottomEdge;
        _clipWatcher.AddTornLeftEdge = _vm.Settings.AddTornLeftEdge;
        _clipWatcher.AddTornRightEdge = _vm.Settings.AddTornRightEdge;
        // Refresh the tray menu label so it matches the new state next time
        // the user opens the tray menu.
        try
        {
            TrayImageBorderItem.Header = _vm.Settings.AddBorderToImages
                ? "Black border on screenshots: ON"
                : "Black border on screenshots: OFF";
        }
        catch { }
        // Keep the header toggle button visual in sync too.
        UpdateBorderToggleVisual();
        // Capture hotkeys may have changed — re-register from settings.
        RegisterCaptureHotkeys();
        _vm.ScheduleSave();
    }

    private void OnTrayClearAll_Click(object sender, RoutedEventArgs e)
    {
        var res = MessageBox.Show(
            "Clear ALL items — Pinned, Recent, AND History?\n\nThis can't be undone.",
            "Clear everything",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (res != MessageBoxResult.Yes) return;
        _vm.ClearEverything();
    }

    // ── Slot list search ─────────────────────────────────────────────────

    /// <summary>
    /// Window-level Ctrl+F shortcut to open the slot search box (and Esc to
    /// close it when focused). Doesn't preempt typing INTO the box.
    /// </summary>
    private void OnWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Ctrl+F → toggle the slot search bar
        if (e.Key == Key.F && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            // Don't trap Ctrl+F if focus is inside a TextBox that wants it
            // (e.g. the user is typing in the rename dialog).
            if (Keyboard.FocusedElement is TextBox tb && !ReferenceEquals(tb, SlotSearchBox))
                return;

            ShowSlotSearch();
            e.Handled = true;
        }
    }

    private void ShowSlotSearch()
    {
        SlotSearchBar.Visibility = Visibility.Visible;
        SlotSearchBox.Focus();
        SlotSearchBox.SelectAll();
    }

    private void OnSlotSearchClose_Click(object sender, RoutedEventArgs e)
    {
        CloseSlotSearch();
    }

    private void CloseSlotSearch()
    {
        SlotSearchBox.Text = "";
        // Reset the type filter too — a hidden bar silently filtering
        // the list to "Excel only" would look like data loss.
        if (TypeFilterCombo is not null) TypeFilterCombo.SelectedIndex = 0;
        SlotSearchBar.Visibility = Visibility.Collapsed;
        ApplySlotFilter();   // restores full list
        Keyboard.ClearFocus();
    }

    private void OnSlotSearch_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { CloseSlotSearch(); e.Handled = true; }
    }

    private void OnSlotSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplySlotFilter();
    }

    /// <summary>
    /// Apply the current search-box filter. With empty filter, all items are
    /// shown. With a non-empty filter, only items whose display text or
    /// nickname contains the term are shown.
    ///
    /// In v2.4 the filter applies to BOTH Pinned and Recent collections using
    /// WPF CollectionView's Filter predicate. The underlying
    /// ObservableCollections stay intact (cascade, persistence, drag-and-drop
    /// still behave normally). Clearing the search restores both lists.
    /// </summary>
    private void ApplySlotFilter()
    {
        // Parse-time guard: the type-filter ComboBox's SelectedIndex="0"
        // fires SelectionChanged during InitializeComponent(), BEFORE
        // the constructor assigns _vm. (Guarding SlotSearchBox wasn't
        // enough — it's created earlier in the same XAML parse, so it
        // already exists by the time the combo fires. The thing that
        // doesn't exist yet is the view model.)
        if (_vm is null) return;

        var pinnedView = System.Windows.Data.CollectionViewSource.GetDefaultView(_vm.PinnedItems);
        var recentView = System.Windows.Data.CollectionViewSource.GetDefaultView(_vm.RecentItems);

        string filter = (SlotSearchBox?.Text ?? "").Trim();
        string typeKey = (TypeFilterCombo?.SelectedItem as ComboBoxItem)?.Tag as string ?? "All";

        if (string.IsNullOrEmpty(filter) && typeKey == "All")
        {
            if (pinnedView is not null) pinnedView.Filter = null;
            if (recentView is not null) recentView.Filter = null;
            return;
        }

        var lower = filter.ToLowerInvariant();
        Predicate<object> pred = o =>
        {
            if (o is not ClipSlot s) return false;
            if (s.IsEmpty) return false;
            // Type filter first — cheap boolean checks. "Text" means
            // PLAIN text specifically: not a URL, not a spreadsheet,
            // and carrying no HTML format. Excel wins over HTML for
            // spreadsheet copies (they carry both formats).
            bool typeOk = typeKey switch
            {
                "Image" => s.HasImage,
                "Url" => s.HasUrl,
                "Text" => s.HasPlainText && !s.HasHtml,
                "Html" => s.HasHtml,
                "Excel" => s.HasSpreadsheet,
                _ => true,
            };
            if (!typeOk) return false;
            // Then the text search (if any).
            if (string.IsNullOrEmpty(lower)) return true;
            if (!string.IsNullOrEmpty(s.Nickname) &&
                s.Nickname.ToLowerInvariant().Contains(lower)) return true;
            return (s.DisplayText ?? "").ToLowerInvariant().Contains(lower);
        };
        if (pinnedView is not null) pinnedView.Filter = pred;
        if (recentView is not null) recentView.Filter = pred;
    }

    /// <summary>Type-filter combo changed — reapply the combined filter.
    /// Null-guarded on _vm because SelectionChanged fires during XAML
    /// parse (SelectedIndex="0") before the constructor has assigned
    /// the view model.</summary>
    private void OnTypeFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_vm is null) return;  // XAML parse-time fire
        ApplySlotFilter();
    }

    // ── History panel handlers ───────────────────────────────────────────

    /// <summary>
    /// Toggle the history panel visibility. When opening, populate the
    /// ListBox from the VM's history items (applying current search filter).
    /// </summary>
    private void OnHistoryToggle_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryPanel.Visibility == Visibility.Visible)
        {
            HistoryPanel.Visibility = Visibility.Collapsed;
        }
        else
        {
            RefreshHistoryView();
            HistoryPanel.Visibility = Visibility.Visible;
            HistorySearchBox.Text = "";
            HistorySearchBox.Focus();
        }
    }

    private void OnHistoryClose_Click(object sender, RoutedEventArgs e)
    {
        HistoryPanel.Visibility = Visibility.Collapsed;
    }

    private void OnHistoryClearAll_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.History.Items.Count == 0) return;
        var res = MessageBox.Show(
            $"Delete all {_vm.History.Items.Count} history items? This can't be undone.",
            "Clear history",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (res != MessageBoxResult.Yes) return;
        _vm.History.ClearAll();
        RefreshHistoryView();
        _vm.ScheduleSave();
    }

    /// <summary>
    /// Apply the search filter from the search box and refresh the visible
    /// list. We use the ListBox's ItemsSource (set to a filtered snapshot)
    /// rather than CollectionView's Filter because we want predictable
    /// behavior with virtualization and a simple substring match.
    /// </summary>
    private void OnHistorySearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        RefreshHistoryView();
    }

    private void RefreshHistoryView()
    {
        if (HistoryListBox is null) return;
        string filter = (HistorySearchBox?.Text ?? "").Trim();
        if (string.IsNullOrEmpty(filter))
        {
            HistoryListBox.ItemsSource = _vm.History.Items;
        }
        else
        {
            var lower = filter.ToLowerInvariant();
            var filtered = new System.Collections.Generic.List<Models.HistoryItem>();
            foreach (var it in _vm.History.Items)
            {
                if (it.DisplayText.ToLowerInvariant().Contains(lower))
                    filtered.Add(it);
            }
            HistoryListBox.ItemsSource = filtered;
        }
    }

    /// <summary>Double-click a history row → promote it back to slot 1.</summary>
    private void OnHistoryItem_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (HistoryListBox.SelectedItem is Models.HistoryItem item)
        {
            PromoteHistoryAndClose(item);
        }
    }

    private void OnHistoryItem_Promote(object sender, RoutedEventArgs e)
    {
        var item = (sender as FrameworkElement)?.DataContext as Models.HistoryItem;
        if (item is not null) PromoteHistoryAndClose(item);
    }

    private void OnHistoryItem_Delete(object sender, RoutedEventArgs e)
    {
        var item = (sender as FrameworkElement)?.DataContext as Models.HistoryItem;
        if (item is null) return;
        _vm.History.Remove(item);
        RefreshHistoryView();
        _vm.ScheduleSave();
    }

    private void PromoteHistoryAndClose(Models.HistoryItem item)
    {
        _vm.PromoteFromHistory(item);
        HistoryPanel.Visibility = Visibility.Collapsed;
        _vm.ScheduleSave();
        ScrollSlotListToTop();
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
        // Invert the source-of-truth (current registry state) rather than
        // reading MenuItem.IsChecked, whose timing relative to Click can be
        // surprising. We then push the new state back to the menu item.
        bool want = !Services.StartupService.IsEnabled();
        bool ok = Services.StartupService.SetEnabled(want);
        if (ok)
        {
            _vm.Settings.LaunchOnStartup = want;
            TrayLaunchOnStartupItem.IsChecked = want;
            _vm.ScheduleSave();
            _vm.StatusText = want
                ? "✓ ClipNinja will launch when Windows starts"
                : "ClipNinja will no longer launch at startup";
        }
        else
        {
            // Couldn't write to the registry — sync checkmark to actual state.
            TrayLaunchOnStartupItem.IsChecked = Services.StartupService.IsEnabled();
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

    private void OnTrayOpenImageDebug_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ClipNinja", "image-debug.log");
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
                MessageBox.Show("Image debug log not found yet — click an image slot first to generate one.",
                                "ClipNinja", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Couldn't open image-debug log: {ex.Message}",
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
        var anim = Animations.AnimationLibrary.PickRandom();
        _ninjaAnimator.Play(anim);
        ShowShout(anim.Shout, anim.Duration);
    }

    /// <summary>Sun left-click: random move (the easter egg).</summary>
    private void OnSun_LeftClick(object sender, MouseButtonEventArgs e)
    {
        if (_ninjaAnimator is null) return;
        _ninjaAnimator.Stop();
        var anim = Animations.AnimationLibrary.PickRandom();
        _ninjaAnimator.Play(anim);
        ShowShout(anim.Shout, anim.Duration);
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
            var a = Animations.AnimationLibrary.PickRandom();
            _ninjaAnimator.Play(a);
            ShowShout(a.Shout, a.Duration);
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
                ShowShout(anim.Shout, anim.Duration);
            };
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    /// <summary>
    /// Scroll the slot list so slot 1 is visible. Called after a new capture
    /// (so the user sees what just landed at the top) and when slot 1 is
    /// clicked from a scrolled-down position.
    /// </summary>
    private void ScrollSlotListToTop()
    {
        try
        {
            if (SlotList is null || _vm.RecentItems.Count == 0) return;
            SlotList.ScrollIntoView(_vm.RecentItems[0]);
        }
        catch { /* harmless if list isn't ready yet */ }
    }

    private System.Windows.Threading.DispatcherTimer? _shoutFadeTimer;

    /// <summary>
    /// Pop the speech bubble with the given shout text, hold for most of the
    /// animation, then fade out. Empty shout text hides the bubble (used by
    /// IdleBob and any move that didn't get a shout assigned).
    /// </summary>
    private void ShowShout(string text, double animDurationSec)
    {
        if (ShoutBubble is null) return;

        _shoutFadeTimer?.Stop();
        _shoutFadeTimer = null;

        if (string.IsNullOrEmpty(text))
        {
            ShoutBubble.Opacity = 0;
            return;
        }

        ShoutText.Text = text;

        // Pop-in: fast opacity fade and a slight scale bounce
        var fadeIn = new System.Windows.Media.Animation.DoubleAnimation
        {
            From = 0, To = 1,
            Duration = TimeSpan.FromMilliseconds(120),
            EasingFunction = new System.Windows.Media.Animation.QuadraticEase
            {
                EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut,
            },
        };
        ShoutBubble.BeginAnimation(System.Windows.UIElement.OpacityProperty, fadeIn);

        var bounceIn = new System.Windows.Media.Animation.DoubleAnimation
        {
            From = 0.6, To = 1.0,
            Duration = TimeSpan.FromMilliseconds(180),
            EasingFunction = new System.Windows.Media.Animation.BackEase
            {
                Amplitude = 0.4,
                EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut,
            },
        };
        ShoutBubbleScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, bounceIn);
        ShoutBubbleScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, bounceIn);

        // Hold for most of the animation, then fade out. Floor at 0.6s so
        // the user can always read short moves.
        double holdSec = Math.Max(0.6, animDurationSec - 0.30);
        _shoutFadeTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(holdSec),
        };
        _shoutFadeTimer.Tick += (_, _) =>
        {
            _shoutFadeTimer?.Stop();
            _shoutFadeTimer = null;
            var fadeOut = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 1, To = 0,
                Duration = TimeSpan.FromMilliseconds(250),
                EasingFunction = new System.Windows.Media.Animation.QuadraticEase
                {
                    EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn,
                },
            };
            ShoutBubble.BeginAnimation(System.Windows.UIElement.OpacityProperty, fadeOut);
        };
        _shoutFadeTimer.Start();
    }

    private void ToggleVisibility()
    {
        if (WindowState == WindowState.Minimized || !IsVisible)
        {
            // Defensive: ensure the window always appears on the taskbar
            // when shown. v2.4.x had a bug where a --hidden startup could
            // leave ShowInTaskbar=false, making the minimize button send
            // the window into the void (only accessible from the tray
            // hidden-icons flyout).
            ShowInTaskbar = true;
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
