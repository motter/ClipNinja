using System;

namespace ClipNinjaV2.Models;

/// <summary>
/// An entry in the rolling history list. Wraps a piece of ClipContent (text,
/// image, etc.) with the timestamp it was captured. History items are
/// transient archive — they don't have pin states or slot positions; their
/// only operation is "promote back to slot 1" via a click.
///
/// New items are added at index 0 (most recent at the top). The history
/// list is capped at AppSettings.HistoryMaxItems — older entries are
/// evicted when the cap is exceeded. Image content's full PNG is kept
/// alongside the thumbnail (so promoted items still paste correctly).
/// </summary>
public class HistoryItem
{
    /// <summary>The captured payload — TextContent or ImageContent.</summary>
    public required ClipContent Content { get; init; }

    /// <summary>Original capture moment. Shown as relative time in the UI ("5 min ago").</summary>
    public DateTime CapturedAt { get; init; } = DateTime.Now;

    /// <summary>
    /// Returns a short single-line preview of this item for the history row.
    /// Text items show the first ~60 chars; images show "image WxH (label)".
    /// </summary>
    public string DisplayText
    {
        get
        {
            return Content switch
            {
                TextContent tc => tc.Text.Length > 60
                    ? tc.Text[..60].Replace('\n', ' ').Replace('\r', ' ') + "…"
                    : tc.Text.Replace('\n', ' ').Replace('\r', ' '),
                ImageContent ic => $"image {ic.OriginalWidth}×{ic.OriginalHeight} ({ic.DisplayLabel})",
                _ => "(empty)",
            };
        }
    }

    /// <summary>Human-friendly "5 min ago" / "yesterday" / "Jan 12" style timestamp.</summary>
    public string RelativeTime
    {
        get
        {
            var delta = DateTime.Now - CapturedAt;
            if (delta.TotalSeconds < 60) return "just now";
            if (delta.TotalMinutes < 60) return $"{(int)delta.TotalMinutes} min ago";
            if (delta.TotalHours < 24) return $"{(int)delta.TotalHours} hr ago";
            if (delta.TotalDays < 2) return "yesterday";
            if (delta.TotalDays < 7) return $"{(int)delta.TotalDays} days ago";
            return CapturedAt.ToString("MMM d");
        }
    }

    /// <summary>Convenience: true if this is an image item (used for thumbnail binding visibility).</summary>
    public bool HasImage => Content is ImageContent;

    /// <summary>True if the captured image was sourced from a .gif file.</summary>
    public bool HasGif => Content is ImageContent ic && string.Equals(ic.DisplayLabel, "GIF", System.StringComparison.Ordinal);

    /// <summary>True if this slot's text came from a spreadsheet/table source (gets the green grid icon).</summary>
    public bool HasSpreadsheet => Content is TextContent tc && tc.IsSpreadsheet;

    /// <summary>True if this slot holds a single URL (gets the link-chain icon).</summary>
    public bool HasUrl => Content is TextContent tc && !tc.IsSpreadsheet && tc.IsUrl;

    /// <summary>True if this slot holds plain text (not a URL, not a spreadsheet, not an image).</summary>
    public bool HasPlainText => Content is TextContent tc && !tc.IsSpreadsheet && !tc.IsUrl;

    /// <summary>Convenience: true if a text item carries hyperlinks (shown as a 🔗 badge).</summary>
    public bool HasLinks => Content is TextContent tc && tc.HasLinks;

    /// <summary>Number of hyperlinks (0 if none / not text).</summary>
    public int LinkCount => Content is TextContent tc ? tc.Links.Count : 0;

    /// <summary>Thumbnail for image items; null for non-images.</summary>
    public System.Windows.Media.Imaging.BitmapSource? ThumbnailSource
        => (Content as ImageContent)?.Thumbnail;
}
