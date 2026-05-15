using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using ClipNinjaV2.Models;
using ClipNinjaV2.Services;

namespace ClipNinjaV2.ViewModels;

/// <summary>
/// Top-level VM. Manages slots, settings, and persistence.
/// Capture flow:
///   - Watcher fires ContentChanged → OnNewClipContent inserts at slot 1
///   - Pinned slots stay in place; unpinned slots cascade down
/// Paste flow (managed in MainWindow.xaml.cs):
///   - User clicks slot → content goes to system clipboard → user presses Ctrl+V natively
/// </summary>
public class MainViewModel : INotifyPropertyChanged
{
    private readonly PersistenceService _persistence;
    private readonly DispatcherTimer _saveDebouncer;

    public AppSettings Settings { get; private set; }
    public ObservableCollection<ClipSlot> Slots { get; } = new();

    private int _pasteIdx = 1;
    /// <summary>Index of the slot currently visually marked as active (1-based).</summary>
    public int PasteIdx
    {
        get => _pasteIdx;
        set
        {
            _pasteIdx = value;
            for (int i = 0; i < Slots.Count; i++)
                Slots[i].IsActive = (i + 1 == _pasteIdx);
        }
    }

    public bool PasteIdxPinned { get; set; }
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

        var loaded = _persistence.LoadSlots(Settings.SlotCount);
        foreach (var s in loaded)
        {
            s.PropertyChanged += OnSlotChanged;
            Slots.Add(s);
        }
        PasteIdx = 1;   // initial active highlight
    }

    // ── Clipboard insertion ──────────────────────────────────────────────────

    /// <summary>Slot index (1-based) where the Memory Hole begins.
    /// Slots before this are the rolling "recent" area; slots at or after
    /// this index are long-term storage untouched by new captures.</summary>
    public const int MemoryHoleStart = 16;

    public void OnNewClipContent(ClipContent content)
    {
        // Avoid storing duplicate-of-most-recent text
        if (content is TextContent newTc &&
            Slots.Count > 0 &&
            Slots[0].Content is TextContent existTc &&
            existTc.Text == newTc.Text)
        {
            return;
        }

        // Only cascade through the "recent" area — slots 1 through MemoryHoleStart-1
        // (i.e. indices 0..14). Slots 16+ (indices 15+) form the Memory Hole —
        // long-term storage that new captures don't displace.
        var unpinnedPositions = new List<int>();
        int cascadeLimit = Math.Min(Slots.Count, MemoryHoleStart - 1);
        for (int i = 0; i < cascadeLimit; i++)
            if (!Slots[i].IsPinned) unpinnedPositions.Add(i);

        if (unpinnedPositions.Count == 0) return;

        // Cascade unpinned content down by one within the recent area only
        for (int i = unpinnedPositions.Count - 1; i > 0; i--)
        {
            int dst = unpinnedPositions[i];
            int src = unpinnedPositions[i - 1];
            Slots[dst].Content = Slots[src].Content;
        }
        Slots[unpinnedPositions[0]].Content = content;

        PasteIdx = 1;
        PasteIdxPinned = false;

        var preview = content switch
        {
            TextContent tc => tc.Text.Length > 22 ? tc.Text[..22] + "…" : tc.Text,
            ImageContent ic => $"image {ic.OriginalWidth}×{ic.OriginalHeight}",
            _ => "?",
        };
        StatusText = $"📋 Captured: {preview}";

        ScheduleSave();
    }

    public void TogglePause()
    {
        ManuallyPaused = !ManuallyPaused;
        StatusText = ManuallyPaused
            ? "⏸️ Paused — clipboard capture disabled"
            : "▶️ Resumed";
    }

    public void TogglePin(ClipSlot slot)
    {
        slot.IsPinned = !slot.IsPinned;
        if (!slot.IsPinned) slot.Nickname = "";
        StatusText = slot.IsPinned ? $"Slot {slot.Index} pinned 📌" : $"Slot {slot.Index} unpinned";
    }

    public void ClearSlot(ClipSlot slot)
    {
        slot.Content = null;
        slot.IsPinned = false;
        slot.Nickname = "";
        StatusText = $"Slot {slot.Index} cleared";
    }

    public void ClearAllSlots()
    {
        foreach (var s in Slots)
        {
            s.Content = null;
            s.IsPinned = false;
            s.Nickname = "";
        }
        PasteIdx = 1;
        StatusText = "All slots cleared";
    }

    // ── Persistence (async background save) ──────────────────────────────────

    private void OnSlotChanged(object? sender, PropertyChangedEventArgs e)
    {
        // UI-only properties never trigger save
        if (e.PropertyName == nameof(ClipSlot.IsActive)) return;
        if (e.PropertyName == nameof(ClipSlot.IsDropTarget)) return;
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
        var slotsSnapshot = Slots.ToList();
        var settingsSnapshot = Settings;

        lock (_saveLock)
        {
            if (_saveInProgress)
            {
                _saveQueuedAgain = true;
                return;
            }
            _saveInProgress = true;
        }

        _ = Task.Run(() =>
        {
            try
            {
                using (Services.Trace.Time("save", $"SaveSettings + SaveSlots ({slotsSnapshot.Count} slots)"))
                {
                    _persistence.SaveSettings(settingsSnapshot);
                    _persistence.SaveSlots(slotsSnapshot);
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
        _persistence.SaveSlots(Slots);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name ?? ""));
}
