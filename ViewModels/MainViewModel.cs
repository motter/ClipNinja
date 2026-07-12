using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Threading;
using ClipNinjaV2.Models;
using ClipNinjaV2.Services;

namespace ClipNinjaV2.ViewModels;

/// <summary>
/// Top-level VM for the v2.4+ two-list model.
///
/// Two ObservableCollections of ClipSlot:
///   • PinnedItems — user-curated "shelf" at the top. Never automatically
///     displaced. Items end up here by user action (pin button or right-click).
///   • RecentItems — rolling list of recent captures. Newest copy at
///     position 0 (display index 1). Oldest item at the bottom falls off
///     into history when the list exceeds Settings.RecentMaxItems.
///
/// Capture flow:
///   1. Watcher.ContentChanged → OnNewClipContent
///   2. If the new content matches a PinnedItem (by ContentMatches), no-op
///   3. Else if it matches an existing RecentItem, promote that item to
///      position 0 (so the user doesn't get a duplicate further down)
///   4. Else insert a fresh ClipSlot at RecentItems[0]; if RecentItems now
///      exceeds the cap, push the oldest item into History and remove it.
///
/// Display indices are recomputed on every change via RebuildIndices so
/// the UI always shows 1..N within each section.
/// </summary>
public class MainViewModel : INotifyPropertyChanged
{
    private readonly PersistenceService _persistence;

    /// <summary>Read-only access to the persistence layer — the quick-save
    /// feature uses this to find already-encoded PNGs in the images dir
    /// (cheaper than re-encoding the in-memory bitmap).</summary>
    public PersistenceService Persistence => _persistence;
    private readonly DispatcherTimer _saveDebouncer;

    public AppSettings Settings { get; private set; }

    /// <summary>Pinned items (user-curated shelf).</summary>
    public ObservableCollection<ClipSlot> PinnedItems { get; } = new();

    /// <summary>Recent items (rolling list, newest at index 0).</summary>
    public ObservableCollection<ClipSlot> RecentItems { get; } = new();

    /// <summary>Rolling history of items that have cascaded off the bottom
    /// of RecentItems. Capped at Settings.HistoryMaxItems.</summary>
    public HistoryService History { get; } = new();

    private int _pasteIdx = 1;
    /// <summary>1-based display index of the slot in RecentItems currently
    /// marked as "active" (the next one a hotkey-cycle would target). Pinned
    /// items don't participate in cycling — see SetActivePinned for their
    /// separate visual-active tracking.</summary>
    public int PasteIdx
    {
        get => _pasteIdx;
        set
        {
            _pasteIdx = value;
            // Update IsActive flags so the UI can highlight the active row.
            // Setting PasteIdx implicitly clears any Pinned active highlight —
            // they're mutually exclusive ("the last thing you interacted with"
            // is the active row, whichever list it came from).
            for (int i = 0; i < RecentItems.Count; i++)
                RecentItems[i].IsActive = (i + 1 == _pasteIdx);
            foreach (var p in PinnedItems) p.IsActive = false;
        }
    }

    /// <summary>
    /// Mark a specific Pinned item as the "active" (last-interacted) row,
    /// clearing any Recent active highlight at the same time. Symmetric
    /// counterpart to the Recent path that goes through PasteIdx — visually
    /// the user sees ONE active row across both lists at any time.
    /// </summary>
    public void SetActivePinned(ClipSlot pinned)
    {
        // Clear Recent highlight by zeroing PasteIdx (its setter walks
        // RecentItems clearing IsActive).
        _pasteIdx = 0;
        for (int i = 0; i < RecentItems.Count; i++)
            RecentItems[i].IsActive = false;

        // Mark only the requested Pinned item as active.
        foreach (var p in PinnedItems)
            p.IsActive = ReferenceEquals(p, pinned);
    }

    public bool ManuallyPaused { get; set; }

    private string _statusText = "Ready";
    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; Notify(); }
    }

    public MainViewModel()
    {
        _persistence = new PersistenceService();
        Settings = _persistence.LoadSettings();

        _saveDebouncer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _saveDebouncer.Tick += (_, _) => { _saveDebouncer.Stop(); SaveAll(); };

        // Load pinned + recent from persistence (handles v1 → v2 migration internally).
        var (pinned, recent) = _persistence.LoadItems(Settings);
        foreach (var p in pinned)
        {
            p.IsPinned = true;
            p.PropertyChanged += OnItemChanged;
            PinnedItems.Add(p);
        }
        foreach (var r in recent)
        {
            r.IsPinned = false;
            r.PropertyChanged += OnItemChanged;
            RecentItems.Add(r);
        }

        RebuildIndices();

        // Keep indices fresh on any reorder / insert / remove.
        PinnedItems.CollectionChanged += OnCollectionChanged;
        RecentItems.CollectionChanged += OnCollectionChanged;

        PasteIdx = RecentItems.Count > 0 ? 1 : 0;

        History.MaxItems = Settings.HistoryMaxItems;
        History.Load();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildIndices();
        // Re-attach change handlers to newly-added items so we still hear about
        // nickname edits, content changes, etc.
        if (e.NewItems is not null)
        {
            foreach (ClipSlot s in e.NewItems)
            {
                s.PropertyChanged -= OnItemChanged;
                s.PropertyChanged += OnItemChanged;
            }
        }
    }

    /// <summary>
    /// Walk both lists and assign 1-based display indices. Called whenever
    /// either collection changes (insert, remove, reorder) so the UI's
    /// "row N" labels stay in sync with visual position.
    /// </summary>
    public void RebuildIndices()
    {
        for (int i = 0; i < PinnedItems.Count; i++) PinnedItems[i].Index = i + 1;
        for (int i = 0; i < RecentItems.Count; i++) RecentItems[i].Index = i + 1;
    }

    // ── Capture / cascade ────────────────────────────────────────────────

    /// <summary>
    /// Handle a new clipboard capture. Inserts into RecentItems at the top,
    /// handles re-copy promotion, evicts overflow into History.
    /// </summary>
    public void OnNewClipContent(ClipContent content)
    {
        // 1. If the new content matches anything already PINNED, no-op.
        //    Pinned content is "decided" content; we don't disturb it on
        //    re-copy, and we don't add a duplicate to Recent either.
        foreach (var p in PinnedItems)
        {
            if (ContentMatches(p.Content, content))
            {
                StatusText = $"⤴ Already pinned — no change";
                return;
            }
        }

        // 2. If the new content matches something in Recent, PROMOTE that
        //    item to the top instead of creating a duplicate.
        int matchIdx = -1;
        for (int i = 0; i < RecentItems.Count; i++)
        {
            if (ContentMatches(RecentItems[i].Content, content))
            {
                matchIdx = i;
                break;
            }
        }

        if (matchIdx == 0)
        {
            // Already at the top — true no-op, don't even update status
            // (would be noisy if the user re-copies the same thing twice).
            return;
        }

        if (matchIdx > 0)
        {
            var existing = RecentItems[matchIdx];

            // Upgrade: if the new capture has richer text formats (HTML/RTF)
            // and the existing one doesn't, replace the content with the
            // richer version while keeping the item identity.
            if (content is TextContent newTc && newTc.HasRichFormats &&
                existing.Content is TextContent oldTc && !oldTc.HasRichFormats)
            {
                existing.Content = newTc;
            }

            // Move it to position 0 (top of Recent).
            RecentItems.Move(matchIdx, 0);
            PasteIdx = 1;
            StatusText = $"⤴ Promoted re-copied item to top";
            ScheduleSave();
            return;
        }

        // 3. Fresh capture — insert at position 0.
        var slot = new ClipSlot { Content = content };
        slot.PropertyChanged += OnItemChanged;
        RecentItems.Insert(0, slot);

        // 4. Enforce cap. Anything beyond RecentMaxItems falls into history.
        int cap = Math.Max(1, Settings.RecentMaxItems);
        while (RecentItems.Count > cap)
        {
            var displaced = RecentItems[^1];
            if (Settings.HistoryMaxItems > 0 && displaced.Content is not null)
            {
                History.Add(displaced.Content);
            }
            RecentItems.RemoveAt(RecentItems.Count - 1);
        }

        PasteIdx = 1;

        var preview = content switch
        {
            TextContent tc => tc.Text.Length > 22 ? tc.Text[..22] + "…" : tc.Text,
            ImageContent ic => $"image {ic.OriginalWidth}×{ic.OriginalHeight}",
            _ => "?",
        };
        StatusText = $"📋 Captured: {preview}";

        ScheduleSave();
    }

    /// <summary>
    /// Move a history item back to Recent at position 0, removing it from
    /// history. We delete the underlying history-image PNG since the slot
    /// persistence layer will re-save it to images\.
    /// </summary>
    public void PromoteFromHistory(HistoryItem item)
    {
        // Detach from history's image storage so the slot layer can take ownership.
        if (item.Content is ImageContent ic && !string.IsNullOrEmpty(ic.SavedFileName))
        {
            try
            {
                var historyImagesDir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "ClipNinja", "history", "images");
                var path = System.IO.Path.Combine(historyImagesDir, ic.SavedFileName);
                if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
            }
            catch { /* harmless */ }
            ic.SavedFileName = "";
        }

        History.Items.Remove(item);
        OnNewClipContent(item.Content);
    }

    /// <summary>
    /// Two ClipContents represent the same clipboard payload?
    /// Text: exact string equality plus matching spreadsheet flag.
    /// Image: pixel dimensions plus our cheap pixel-sample hash.
    /// </summary>
    private static bool ContentMatches(ClipContent? a, ClipContent? b)
    {
        if (a is null || b is null) return false;
        if (a is TextContent at && b is TextContent bt)
        {
            return at.IsSpreadsheet == bt.IsSpreadsheet && at.Text == bt.Text;
        }
        if (a is ImageContent ai && b is ImageContent bi &&
            ai.FullImage is not null && bi.FullImage is not null)
        {
            if (ai.OriginalWidth != bi.OriginalWidth) return false;
            if (ai.OriginalHeight != bi.OriginalHeight) return false;
            return Services.ClipboardWatcher.EstimateImageHash(ai.FullImage) ==
                   Services.ClipboardWatcher.EstimateImageHash(bi.FullImage);
        }
        return false;
    }

    // ── Pin / unpin / clear ──────────────────────────────────────────────

    /// <summary>
    /// Toggle pin state of an item. Pinning moves it from Recent → top of
    /// Pinned. Unpinning moves it from Pinned → top of Recent. The user's
    /// most-recent decision goes to the top of the destination list.
    /// </summary>
    public void TogglePin(ClipSlot item)
    {
        if (PinnedItems.Contains(item))
        {
            // Unpin: move from PinnedItems → top of RecentItems.
            PinnedItems.Remove(item);
            item.IsPinned = false;
            // Nickname stays — user's effort shouldn't be lost.
            RecentItems.Insert(0, item);
            StatusText = "Unpinned — moved to top of Recent";
        }
        else if (RecentItems.Contains(item))
        {
            // Pin: move from RecentItems → top of PinnedItems.
            RecentItems.Remove(item);
            item.IsPinned = true;
            PinnedItems.Insert(0, item);
            StatusText = "📌 Pinned";
        }

        PasteIdx = RecentItems.Count > 0 ? 1 : 0;
        ScheduleSave();
    }

    /// <summary>Remove an item from whichever list it's in. Image files are kept
    /// (the persistence layer will GC unreferenced files on next save).</summary>
    public void RemoveItem(ClipSlot item)
    {
        if (PinnedItems.Contains(item))
        {
            PinnedItems.Remove(item);
            StatusText = "Pinned item removed";
        }
        else if (RecentItems.Contains(item))
        {
            RecentItems.Remove(item);
            StatusText = "Recent item removed";
        }
        ScheduleSave();
    }

    public void TogglePause()
    {
        ManuallyPaused = !ManuallyPaused;
        StatusText = ManuallyPaused
            ? "⏸️ Paused — clipboard capture disabled"
            : "▶️ Resumed";
    }

    public void ClearAllRecent()
    {
        RecentItems.Clear();
        PasteIdx = 0;
        StatusText = "Recent items cleared";
        ScheduleSave();
    }

    public void ClearAllPinned()
    {
        PinnedItems.Clear();
        StatusText = "Pinned items cleared";
        ScheduleSave();
    }

    public void ClearEverything()
    {
        PinnedItems.Clear();
        RecentItems.Clear();
        History.ClearAll();
        PasteIdx = 0;
        StatusText = "Everything cleared";
        ScheduleSave();
    }

    // ── Persistence ──────────────────────────────────────────────────────

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        // UI-only properties never trigger save
        if (e.PropertyName == nameof(ClipSlot.IsActive)) return;
        if (e.PropertyName == nameof(ClipSlot.IsDropTarget)) return;
        if (e.PropertyName == nameof(ClipSlot.Index)) return;
        ScheduleSave();
    }

    public void ScheduleSave()
    {
        _saveDebouncer.Stop();
        _saveDebouncer.Start();
    }

    private readonly object _saveLock = new();
    private bool _saveInProgress;
    private bool _saveQueuedAgain;

    public void SaveAll()
    {
        var pinnedSnapshot = PinnedItems.ToList();
        var recentSnapshot = RecentItems.ToList();
        var settingsSnapshot = Settings;

        lock (_saveLock)
        {
            if (_saveInProgress) { _saveQueuedAgain = true; return; }
            _saveInProgress = true;
        }

        _ = Task.Run(() =>
        {
            try
            {
                using (Services.Trace.Time("save",
                    $"Save (settings + pinned={pinnedSnapshot.Count}, recent={recentSnapshot.Count}, history={History.Items.Count})"))
                {
                    _persistence.SaveSettings(settingsSnapshot);
                    _persistence.SaveItems(pinnedSnapshot, recentSnapshot);
                    History.Save();
                }
            }
            catch (Exception ex) { Services.Trace.Log("save", $"FAILED: {ex.Message}"); }
            finally
            {
                bool runAgain;
                lock (_saveLock)
                {
                    _saveInProgress = false;
                    runAgain = _saveQueuedAgain;
                    _saveQueuedAgain = false;
                }
                if (runAgain)
                {
                    System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(SaveAll));
                }
            }
        });
    }

    public void Flush()
    {
        _saveDebouncer.Stop();
        _persistence.SaveSettings(Settings);
        _persistence.SaveItems(PinnedItems, RecentItems);
        History.Save();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name ?? ""));
}
