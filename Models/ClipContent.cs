using System;
using System.Collections.Generic;
using System.Windows.Media.Imaging;

namespace ClipNinjaV2.Models;

/// <summary>
/// A single hyperlink extracted from a piece of rich-text clipboard content.
/// The label is the visible text (what was between &lt;a&gt; tags) and the
/// URL is the destination. Used in the preview popup so the user can open
/// links from a slot regardless of where they paste the slot.
/// </summary>
public record HyperLink(string Label, string Url);

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

    /// <summary>
    /// Original CF_HTML clipboard format payload, captured when the source app
    /// provided one (typical for selections in Outlook, Word, web pages,
    /// Teams chat, etc). Includes the standard CF_HTML header
    /// ("Version:0.9\r\nStartHTML:..."). Stored verbatim so the byte offsets
    /// inside the header remain valid when we round-trip it back to the
    /// clipboard. Null when the source had no HTML, or when we skipped
    /// capturing it (e.g. for spreadsheet content, which has its own path).
    /// </summary>
    public string? HtmlFormat { get; init; }

    /// <summary>
    /// Original CF_RTF clipboard format payload. Some apps prefer RTF over
    /// HTML during paste (notably Outlook's compose pane when the recipient
    /// is in RTF mode). Captured and re-played alongside HTML.
    /// </summary>
    public string? RtfFormat { get; init; }

    /// <summary>
    /// Hyperlinks parsed from the source's CF_HTML. Empty list when no HTML
    /// was captured or the HTML had no &lt;a href&gt; elements. Exposed in
    /// the preview popup so the user can open links from a slot even if they
    /// only paste it as plain text somewhere else.
    /// </summary>
    public IReadOnlyList<HyperLink> Links { get; init; } = Array.Empty<HyperLink>();

    /// <summary>True when the source had embedded hyperlinks we could parse.</summary>
    public bool HasLinks => Links.Count > 0;

    /// <summary>
    /// True if this text content carries any rich-format payload (HTML or
    /// RTF) that should be replayed alongside the plain text when pasted.
    /// </summary>
    public bool HasRichFormats => HtmlFormat is not null || RtfFormat is not null;

    /// <summary>
    /// True if the entire text content is a single recognizable URL.
    /// Detected lazily on read. Used to show a link icon in the slot row
    /// and offer "Open in browser" in the preview popup.
    /// </summary>
    public bool IsUrl => LooksLikeUrl(Text);

    /// <summary>
    /// Returns true when <paramref name="text"/> is a single URL with a
    /// recognized scheme (http/https/ftp/file) or a www-prefixed host.
    /// Deliberately strict: we only treat text as a URL when the WHOLE
    /// content is one — a document with a URL buried in it is still text.
    /// </summary>
    public static bool LooksLikeUrl(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        var trimmed = text.Trim();

        // Reject multi-line/whitespace-containing text — only a single
        // continuous URL with no internal spaces or line breaks qualifies.
        if (trimmed.Length > 2048) return false;
        foreach (var ch in trimmed)
            if (char.IsWhiteSpace(ch)) return false;

        // Recognized schemes — try a full Uri parse for these
        var schemes = new[] { "http://", "https://", "ftp://", "ftps://", "file://" };
        foreach (var s in schemes)
        {
            if (trimmed.StartsWith(s, StringComparison.OrdinalIgnoreCase))
                return Uri.TryCreate(trimmed, UriKind.Absolute, out _);
        }

        // www. prefix without scheme — assume http and validate
        if (trimmed.StartsWith("www.", StringComparison.OrdinalIgnoreCase) &&
            trimmed.Length > 5)
        {
            return Uri.TryCreate("http://" + trimmed, UriKind.Absolute, out _);
        }

        return false;
    }

    /// <summary>
    /// Returns a launchable Uri if <see cref="IsUrl"/>, with http:// prefixed
    /// to bare www. addresses so they actually open in a browser.
    /// </summary>
    public Uri? ToUri()
    {
        if (!IsUrl) return null;
        var t = Text.Trim();
        if (t.StartsWith("www.", StringComparison.OrdinalIgnoreCase) &&
            !t.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            t = "http://" + t;
        }
        return Uri.TryCreate(t, UriKind.Absolute, out var uri) ? uri : null;
    }

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
