using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ClipNinjaV2.Models;

/// <summary>
/// A single slot in the clipboard manager. Holds optional content,
/// pinned state, and an optional user-supplied nickname.
/// </summary>
public class ClipSlot : INotifyPropertyChanged
{
    private ClipContent? _content;
    private bool _isPinned;
    private string _nickname = "";
    private bool _isActive;
    private bool _isDropTarget;

    /// <summary>1-based position. 1 = newest / next-to-paste by default.</summary>
    public int Index { get; set; }

    public ClipContent? Content
    {
        get => _content;
        set { _content = value; Notify(); Notify(nameof(IsEmpty)); Notify(nameof(HasImage)); Notify(nameof(HasSpreadsheet)); Notify(nameof(HasPlainText)); Notify(nameof(DisplayText)); Notify(nameof(ThumbnailSource)); }
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
    /// <summary>True if this slot holds spreadsheet/table text (gets a special icon).</summary>
    public bool HasSpreadsheet => Content is TextContent tc && tc.IsSpreadsheet;
    /// <summary>True if this slot holds plain text (not a spreadsheet, not an image).</summary>
    public bool HasPlainText => Content is TextContent tc && !tc.IsSpreadsheet;

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
