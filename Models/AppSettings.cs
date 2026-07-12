namespace ClipNinjaV2.Models;

/// <summary>
/// App-wide user preferences. Serialized to settings.json.
///
/// Schema versioning: v1 is the pre-2.4.0 layout (30 fixed slots, with the
/// upper half called "Memory Hole"). v2 is the 2.4.0+ layout: a Pinned list
/// + a Recent list + History. The migration from v1 to v2 happens once on
/// load (see PersistenceService.MigrateV1ToV2) and bumps the SchemaVersion
/// in-place.
/// </summary>
public class AppSettings
{
    /// <summary>Persistence schema version. Bumped when we change the layout.</summary>
    public int SchemaVersion { get; set; } = 2;

    public bool PlainTextMode { get; set; } = false;
    public bool ExcelAwarePaste { get; set; } = true;
    public bool ShowNinja { get; set; } = true;
    public int CycleWindowMs { get; set; } = 1500;
    public bool ShowPlainTextHint { get; set; } = true;
    public bool ShowTrayHint { get; set; } = true;
    public bool LaunchOnStartup { get; set; } = false;

    /// <summary>Bake a thin black border around captured images. Defaults
    /// to true — helpful for screenshots that get pasted into docs where
    /// they'd otherwise blend into the page background.</summary>
    public bool AddBorderToImages { get; set; } = true;

    /// <summary>Bake a soft drop shadow onto captured images. Adds ~15px
    /// of transparent canvas on the right/bottom with a dark gradient
    /// underneath, so the image visually "lifts" off the page it's
    /// pasted into. Off by default — it's polish, not a necessity.</summary>
    public bool AddDropShadowToImages { get; set; } = false;

    /// <summary>Give captured images a torn / ragged top edge. Cosmetic
    /// effect, evokes a piece torn out of an article. Combine all four
    /// sides for the full "ripped from a magazine" look.</summary>
    public bool AddTornTopEdge { get; set; } = false;

    /// <summary>Give captured images a torn / ragged bottom edge.</summary>
    public bool AddTornBottomEdge { get; set; } = false;

    /// <summary>Give captured images a torn / ragged left edge.</summary>
    public bool AddTornLeftEdge { get; set; } = false;

    /// <summary>Give captured images a torn / ragged right edge.</summary>
    public bool AddTornRightEdge { get; set; } = false;

    /// <summary>Folder for the quick-save feature — user can 💾 an image
    /// slot to copy the PNG here with a human-friendly filename. Empty
    /// string means "not configured yet"; the 💾 button and menu item
    /// will prompt on first use rather than silently failing. Defaults
    /// to the user's Documents\ClipNinja Screenshots on first pick.</summary>
    public string QuickSaveFolder { get; set; } = "";

    /// <summary>If true, EVERY captured image is automatically saved to
    /// QuickSaveFolder immediately on capture (in addition to being kept
    /// in the slot). Requires QuickSaveFolder to be set. Off by default —
    /// most users prefer to save selectively.</summary>
    public bool AutoSaveScreenshotsToFolder { get; set; } = false;

    /// <summary>Global hotkey for the region-select screen capture,
    /// stored as a human-readable combo string ("Ctrl+Shift+C").
    /// Laptop-friendly default — PrintScreen often needs the Fn key.
    /// Editable in Settings via a press-the-keys capture box.</summary>
    public string CaptureRegionHotkey { get; set; } = "Ctrl+Shift+C";

    /// <summary>Global hotkey for instant full-screen capture (entire
    /// virtual desktop — all monitors).</summary>
    public string CaptureFullHotkey { get; set; } = "Ctrl+Shift+Z";

    /// <summary>GitHub repo ("owner/name") that in-app updates check
    /// against. Defaults to the official repo so fresh installs are
    /// pre-wired; still editable in Settings for forks. The repo must
    /// be public and its releases must carry a published .exe or .zip
    /// asset (the bundled GitHub Actions workflow does this on every
    /// v* tag push).</summary>
    public string UpdateRepo { get; set; } = "motter/ClipNinja";

    /// <summary>Check for updates in the background shortly after
    /// startup. Never interrupts with dialogs — a newer version just
    /// shows a status-bar hint. Manual checks live in Settings.</summary>
    public bool AutoCheckForUpdates { get; set; } = true;

    /// <summary>
    /// Maximum number of items the Recent list holds before cascading items
    /// off the bottom into History. Default 50 (vs. v1's hardcoded 15).
    /// Pinned items have no cap — pinning is an explicit decision, so we
    /// trust the user.
    /// </summary>
    public int RecentMaxItems { get; set; } = 50;

    /// <summary>
    /// Maximum number of items the rolling history holds. When the Recent
    /// list overflows, the oldest unpinned item lands in history; once this
    /// cap is exceeded the oldest history item (and its image file, if any)
    /// is evicted. 0 disables history.
    /// </summary>
    public int HistoryMaxItems { get; set; } = 100;

    public void ClampToValidRanges()
    {
        if (RecentMaxItems < 5) RecentMaxItems = 5;
        if (RecentMaxItems > 500) RecentMaxItems = 500;
        if (CycleWindowMs < 200) CycleWindowMs = 200;
        if (CycleWindowMs > 10_000) CycleWindowMs = 10_000;
        if (HistoryMaxItems < 0) HistoryMaxItems = 0;
        if (HistoryMaxItems > 1000) HistoryMaxItems = 1000;
    }
}
