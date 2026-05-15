using System.Windows.Media.Imaging;

namespace ClipNinjaV2.Models;

/// <summary>
/// Base class for any kind of clipboard content stored in a slot.
/// </summary>
public abstract class ClipContent
{
    public DateTime CapturedAt { get; init; } = DateTime.Now;

    /// <summary>What displays in the slot row when no nickname is set.</summary>
    public abstract string PreviewText { get; }
}

/// <summary>Plain text clipboard content.</summary>
public class TextContent : ClipContent
{
    public string Text { get; init; } = "";

    /// <summary>
    /// True if this text came from a spreadsheet/table source. Set by the
    /// clipboard watcher when it detects spreadsheet markers (Excel, CSV, etc).
    /// Determines the slot's icon and how we re-write the clipboard on load.
    /// </summary>
    public bool IsSpreadsheet { get; init; }

    public override string PreviewText
    {
        get
        {
            var t = Text.Replace("\r\n", "↵").Replace("\n", "↵").Replace("\t", "→");
            return t.Length > 35 ? t[..35] + "…" : t;
        }
    }
}

/// <summary>Image clipboard content — bitmap with a pre-rendered thumbnail.</summary>
public class ImageContent : ClipContent
{
    /// <summary>Full-resolution image, used when pasting back to clipboard.</summary>
    public BitmapSource? FullImage { get; init; }

    /// <summary>Pre-scaled thumbnail (~32x32) shown in the slot row.</summary>
    public BitmapSource? Thumbnail { get; init; }

    /// <summary>Default label, e.g. "Screenshot". User can override via nickname.</summary>
    public string DisplayLabel { get; init; } = "Screenshot";

    public int OriginalWidth { get; init; }
    public int OriginalHeight { get; init; }

    /// <summary>
    /// Filename (just the name, not full path) inside the images/ folder.
    /// Set by PersistenceService once the bitmap has been written to disk;
    /// reused on subsequent saves to avoid re-encoding every PNG.
    /// </summary>
    public string SavedFileName { get; set; } = "";

    public override string PreviewText =>
        $"{DisplayLabel} ({OriginalWidth}×{OriginalHeight})";
}
