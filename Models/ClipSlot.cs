using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ClipNinjaV2.Models;

/// <summary>
/// A single clip item — holds optional content, pinned state, and a user
/// nickname. In v2.4+ these live in one of two collections on the view
/// model: PinnedItems (the "shelf" at the top, never automatically displaced)
/// or RecentItems (the rolling list, with oldest items cascading into
/// history). The legacy name "ClipSlot" is preserved to limit churn.
/// </summary>
public class ClipSlot : INotifyPropertyChanged
{
    private ClipContent? _content;
    private bool _isPinned;
    private string _nickname = "";
    private bool _isActive;
    private bool _isDropTarget;

    /// <summary>
    /// 1-based DISPLAY position within its containing list. Updated by the
    /// view model whenever the list changes — not a stable identifier for
    /// the underlying clip content. The newest copy gets Index 1 in
    /// RecentItems; pinned items get their own 1-based Index too.
    /// </summary>
    public int Index { get; set; }

    public ClipContent? Content
    {
        get => _content;
        set { _content = value; Notify(); Notify(nameof(IsEmpty)); Notify(nameof(HasImage)); Notify(nameof(HasGif)); Notify(nameof(HasSpreadsheet)); Notify(nameof(HasUrl)); Notify(nameof(HasPlainText)); Notify(nameof(HasLinks)); Notify(nameof(LinkCount)); Notify(nameof(DisplayText)); Notify(nameof(ThumbnailSource)); }
    }

    public bool IsPinned
    {
        get => _isPinned;
        set { _isPinned = value; Notify(); }
    }

    /// <summary>True when this is the next slot that will be pasted (UI highlight).</summary>
    public bool IsActive
    {
        get => _isActive;
        set { _isActive = value; Notify(); }
    }

    /// <summary>True briefly during drag-drop while this slot is the hover target.</summary>
    public bool IsDropTarget
    {
        get => _isDropTarget;
        set { _isDropTarget = value; Notify(); }
    }

    /// <summary>Up to 20 chars; empty when no nickname.</summary>
    public string Nickname
    {
        get => _nickname;
        set
        {
            var v = (value ?? "").Trim();
            if (v.Length > 20) v = v[..20];
            _nickname = v;
            Notify();
            Notify(nameof(DisplayText));
        }
    }

    public bool IsEmpty => Content is null;
    public bool HasImage => Content is ImageContent;
    /// <summary>True if the slot's image is a GIF (detected at capture time
    /// via CF_HDROP). Used to show a GIF badge on the thumbnail. We don't
    /// animate the thumbnail (WPF doesn't natively support that), but the
    /// label tells the user they're holding an animated image.</summary>
    public bool HasGif => Content is ImageContent ic && string.Equals(ic.DisplayLabel, "GIF", StringComparison.Ordinal);
    /// <summary>True if this slot holds spreadsheet/table text (gets a special icon).</summary>
    public bool HasSpreadsheet => Content is TextContent tc && tc.IsSpreadsheet;
    /// <summary>True if this slot holds a single URL (gets a link icon).</summary>
    public bool HasUrl => Content is TextContent tc && !tc.IsSpreadsheet && tc.IsUrl;
    /// <summary>True if this slot holds plain text (not a URL, not a spreadsheet, not an image).</summary>
    public bool HasPlainText => Content is TextContent tc && !tc.IsSpreadsheet && !tc.IsUrl;

    /// <summary>Text content that carries an HTML clipboard format —
    /// i.e. rich content copied from a browser or editor. Excludes
    /// spreadsheets (Excel copies carry HTML too, but the 📊 category
    /// is the more specific truth). Powers the type filter.</summary>
    public bool HasHtml => Content is TextContent htc && !htc.IsSpreadsheet && !string.IsNullOrEmpty(htc.HtmlFormat);

    /// <summary>
    /// True if the slot's text content carries hyperlinks parsed from the
    /// source's CF_HTML. Shown as a "🔗 N" badge in the slot row so the user
    /// can see which slots preserve email/web hyperlinks.
    /// </summary>
    public bool HasLinks => Content is TextContent tc && tc.HasLinks;

    /// <summary>Number of hyperlinks the slot carries (0 if none / not text).</summary>
    public int LinkCount => Content is TextContent tc ? tc.Links.Count : 0;

    /// <summary>
    /// Direct accessor for the thumbnail bitmap. Bound by the UI Image control.
    /// Using this instead of {Binding Content.Thumbnail} ensures WPF refreshes
    /// the binding properly when Content swaps from null → ImageContent.
    /// </summary>
    public System.Windows.Media.Imaging.BitmapSource? ThumbnailSource
        => (Content as ImageContent)?.Thumbnail;

    /// <summary>What shows in the slot row in the UI.</summary>
    public string DisplayText
    {
        get
        {
            if (IsEmpty) return "— empty —";
            if (!string.IsNullOrEmpty(Nickname)) return $"« {Nickname} »";
            return Content!.PreviewText;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name ?? ""));
}
