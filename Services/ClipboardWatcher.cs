using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClipNinjaV2.Models;

namespace ClipNinjaV2.Services;

/// <summary>
/// Watches the Windows clipboard for changes via the native
/// AddClipboardFormatListener API.
/// 
/// Two important guards:
///   1) DEBOUNCING — apps often write multiple formats (bitmap + DIB + PNG)
///      to the clipboard in rapid succession. We coalesce these into one event.
///   2) SUPPRESSION — when WE write to the clipboard during our own paste,
///      we set SuppressUntil to a future time so we ignore the resulting
///      WM_CLIPBOARDUPDATE messages (could be 1, could be several).
/// </summary>
public class ClipboardWatcher : IDisposable
{
    public event EventHandler<ClipContent>? ContentChanged;

    /// <summary>
    /// Ignore all clipboard-update events until this time. Used during paste.
    /// We use a time-based suppression (not bool) because a single SetImage
    /// can fire multiple WM_CLIPBOARDUPDATE events, and a bool only catches one.
    /// </summary>
    public DateTime SuppressUntil { get; set; } = DateTime.MinValue;

    /// <summary>Helper to set suppression for a fixed window.</summary>
    public void SuppressFor(int milliseconds)
    {
        SuppressUntil = DateTime.UtcNow.AddMilliseconds(milliseconds);
    }

    /// <summary>
    /// Fingerprint of the last content WE deliberately put on the clipboard.
    /// When a clipboard-update event fires with the same fingerprint, we
    /// know it's our own write echoing back (possibly via Windows Clipboard
    /// History) and we skip it. Critical for avoiding duplicate captures
    /// when the user clicks a slot and pastes.
    /// </summary>
    public string LastWrittenSignature { get; set; } = "";

    private HwndSource? _hwndSource;
    private IntPtr _hwnd;

    // Debounce timer — gives apps time to finish writing all clipboard formats
    private System.Windows.Threading.DispatcherTimer? _debounceTimer;
    private const int DebounceMs = 250;

    // Track last captured content signature to avoid storing duplicates that
    // arrive across multiple WM_CLIPBOARDUPDATE events for the same content
    private string _lastSignature = "";
    private DateTime _lastCaptureAt = DateTime.MinValue;

    private const int WM_CLIPBOARDUPDATE = 0x031D;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    public void AttachTo(Window window)
    {
        var helper = new WindowInteropHelper(window);
        _hwnd = helper.EnsureHandle();
        _hwndSource = HwndSource.FromHwnd(_hwnd);
        _hwndSource?.AddHook(WndProc);
        AddClipboardFormatListener(_hwnd);

        _debounceTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(DebounceMs),
        };
        _debounceTimer.Tick += (_, _) => { _debounceTimer.Stop(); ReadClipboard(); };
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_CLIPBOARDUPDATE)
        {
            if (DateTime.UtcNow < SuppressUntil)
            {
                Trace.Log("watcher", "WM_CLIPBOARDUPDATE suppressed (within echo-suppression window)");
                return IntPtr.Zero;
            }
            Trace.Log("watcher", "WM_CLIPBOARDUPDATE → restart debounce");
            _debounceTimer?.Stop();
            _debounceTimer?.Start();
        }
        return IntPtr.Zero;
    }

    private void ReadClipboard()
    {
        using var _t = Trace.Time("watcher", "ReadClipboard()");
        try
        {
            // ── PRIORITY 1: Spreadsheet / table copies ───────────────────────
            // Excel and similar apps put BOTH an image AND structured text
            // on the clipboard. The user almost always wants the actual cell
            // text (which they can paste into Word as a table, into Notes as
            // text, etc.) — not Excel's rendered picture of the cells.
            // So we check for spreadsheet markers FIRST and capture as text.
            string? tableText;
            using (Trace.Time("watcher", "TryReadSpreadsheetText"))
            {
                tableText = TryReadSpreadsheetText();
            }
            if (tableText is not null)
            {
                var sig = ComputeTextSignature(tableText);
                if (sig == LastWrittenSignature) { Trace.Log("watcher", "spreadsheet capture skipped (matches own write)"); return; }
                if (IsDuplicate(sig)) { Trace.Log("watcher", "spreadsheet capture skipped (duplicate)"); return; }

                Trace.Log("watcher", $"CAPTURED spreadsheet text (len={tableText.Length})");
                var content = new TextContent { Text = tableText, IsSpreadsheet = true };
                ContentChanged?.Invoke(this, content);
                return;
            }

            // ── PRIORITY 2: Plain images ─────────────────────────────────────
            // Screenshot tools, browser image-copies, etc. — store as image.
            if (Clipboard.ContainsImage())
            {
                BitmapSource? bmp;
                using (Trace.Time("watcher", "ReadImageRobust"))
                {
                    bmp = ReadImageRobust();
                }
                if (bmp is not null)
                {
                    var sig = $"img:{bmp.PixelWidth}x{bmp.PixelHeight}:{EstimateImageHash(bmp)}";

                    if (sig == LastWrittenSignature) { Trace.Log("watcher", "image capture skipped (matches own write)"); return; }
                    if (IsDuplicate(sig)) { Trace.Log("watcher", "image capture skipped (duplicate)"); return; }

                    BitmapSource thumb;
                    using (Trace.Time("watcher", $"MakeThumbnail {bmp.PixelWidth}x{bmp.PixelHeight}→32"))
                    {
                        thumb = MakeThumbnail(bmp, 32);
                    }
                    string label = DetectImageContext();

                    Trace.Log("watcher", $"CAPTURED image {bmp.PixelWidth}x{bmp.PixelHeight} ({label})");
                    var content = new ImageContent
                    {
                        FullImage = bmp,
                        Thumbnail = thumb,
                        OriginalWidth = bmp.PixelWidth,
                        OriginalHeight = bmp.PixelHeight,
                        DisplayLabel = label,
                    };
                    ContentChanged?.Invoke(this, content);
                    return;
                }
            }

            // ── PRIORITY 3: Plain text ───────────────────────────────────────
            if (Clipboard.ContainsText())
            {
                string text;
                using (Trace.Time("watcher", "Clipboard.GetText"))
                {
                    text = Clipboard.GetText();
                }
                if (!string.IsNullOrWhiteSpace(text))
                {
                    var sig = ComputeTextSignature(text);
                    if (sig == LastWrittenSignature) { Trace.Log("watcher", "text capture skipped (matches own write)"); return; }
                    if (IsDuplicate(sig)) { Trace.Log("watcher", "text capture skipped (duplicate)"); return; }

                    Trace.Log("watcher", $"CAPTURED text (len={text.Length})");
                    var content = new TextContent { Text = text };
                    ContentChanged?.Invoke(this, content);
                }
            }
        }
        catch
        {
            // Clipboard can be locked by other apps; ignore and try again next time.
        }
    }

    /// <summary>
    /// Read an image from the clipboard reliably. Clipboard.GetImage() can
    /// return InteropBitmap with weird format issues; we re-encode it as a
    /// frozen PNG-decoded BitmapSource so it's safe for cross-thread use,
    /// thumbnailing, and persistence.
    /// </summary>
    /// <summary>
    /// Read the clipboard image with format-aware logic.
    /// 
    /// Priority:
    ///  1. PNG bytes (most reliable — screenshot tools, browsers, modern apps)
    ///  2. CF_DIBV5 raw DIB bytes (handles alpha-channel images)
    ///  3. Clipboard.GetImage() fallback (right-click "Copy Image" sources)
    /// <summary>
    /// Read a robust image from the clipboard. Tries multiple format strategies.
    /// Does NOT do retries — those would block the UI thread. If the read fails,
    /// the clipboard will fire another update event soon enough (or the next
    /// debounce tick will pick it up), and we'll try again then.
    /// </summary>
    private static BitmapSource? ReadImageRobust()
    {
        try
        {
            var data = Clipboard.GetDataObject();
            if (data is null) return null;

            BitmapSource? result = null;

            // ── Strategy 1: PNG bytes (screenshot tools, browsers) ─────
            try
            {
                if (data.GetDataPresent("PNG"))
                {
                    if (data.GetData("PNG") is System.IO.MemoryStream pngStream)
                    {
                        pngStream.Position = 0;
                        var bi = new BitmapImage();
                        bi.BeginInit();
                        bi.CacheOption = BitmapCacheOption.OnLoad;
                        bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                        bi.StreamSource = pngStream;
                        bi.EndInit();
                        bi.Freeze();
                        result = bi;
                    }
                }
            }
            catch { /* fall through */ }

            // ── Strategy 2: CF_DIBV5 (handles alpha) ────────────────────
            if (result is null)
            {
                try
                {
                    if (data.GetDataPresent(System.Windows.DataFormats.Bitmap))
                    {
                        var src = data.GetData(System.Windows.DataFormats.Bitmap) as BitmapSource
                               ?? Clipboard.GetImage();
                        if (src is not null && src.PixelWidth > 0 && src.PixelHeight > 0)
                            result = MaterializeBitmap(src);
                    }
                }
                catch { /* fall through */ }
            }

            // ── Strategy 3: Plain GetImage fallback ─────────────────────
            if (result is null)
            {
                try
                {
                    var src = Clipboard.GetImage();
                    if (src is not null && src.PixelWidth > 0 && src.PixelHeight > 0)
                        result = MaterializeBitmap(src);
                }
                catch { /* fall through */ }
            }

            if (result is null) return null;

            // Sanity check: pure-zero pixels indicate a hollow read. Discard.
            if (HasZeroPixels(result)) return null;

            return result;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Force a BitmapSource (which might be a hollow InteropBitmap) to
    /// materialize as a real WriteableBitmap with real pixel bytes.
    /// </summary>
    private static BitmapSource MaterializeBitmap(BitmapSource src)
    {
        int w = src.PixelWidth;
        int h = src.PixelHeight;
        var converted = new FormatConvertedBitmap(src, System.Windows.Media.PixelFormats.Bgra32, null, 0);
        converted.Freeze();
        int stride = (w * 32 + 7) / 8;
        var pixels = new byte[stride * h];
        converted.CopyPixels(pixels, stride, 0);
        var wb = new WriteableBitmap(w, h, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null);
        wb.WritePixels(new System.Windows.Int32Rect(0, 0, w, h), pixels, stride, 0);
        wb.Freeze();
        return wb;
    }

    /// <summary>Sample some pixels from an image to detect hollow/blank reads.</summary>
    private static bool HasZeroPixels(BitmapSource bmp)
    {
        try
        {
            int w = bmp.PixelWidth, h = bmp.PixelHeight;
            // Sample 5 spots: 4 quarter-points + center
            var spots = new (int x, int y)[]
            {
                (w / 4, h / 4),
                (3 * w / 4, h / 4),
                (w / 4, 3 * h / 4),
                (3 * w / 4, 3 * h / 4),
                (w / 2, h / 2),
            };
            int nonZero = 0;
            foreach (var (x, y) in spots)
            {
                var pix = new byte[4];
                try
                {
                    bmp.CopyPixels(new System.Windows.Int32Rect(x, y, 1, 1), pix, 4, 0);
                    if (pix[0] != 0 || pix[1] != 0 || pix[2] != 0) nonZero++;
                }
                catch { }
            }
            // If ALL 5 sample points are zero, the image is hollow.
            return nonZero == 0;
        }
        catch { return false; }
    }

    /// <summary>
    /// Inspect the clipboard's data formats to figure out what KIND of image
    /// this likely is. Spreadsheet apps (Excel, Sheets, Numbers) put both
    /// an image AND structured text on the clipboard; web pages put HTML;
    /// screenshot tools put just the image.
    /// </summary>
    /// <summary>
    /// If the clipboard looks like it came from a spreadsheet (Excel, Sheets,
    /// <summary>
    /// If the clipboard is from Excel (or another BIFF-format spreadsheet),
    /// return the tab-separated text representation. Otherwise return null
    /// and we fall through to the normal text/image capture path.
    /// 
    /// IMPORTANT: This must be FAST. It runs on the UI thread for every
    /// clipboard event. We deliberately only check for BIFF format presence
    /// (an O(1) format-list lookup) and then read just the text formats.
    /// We do NOT inspect HTML content here — that was costing 100+ms per
    /// clipboard event because Word puts giant HTML on the clipboard for
    /// every copy.
    /// </summary>
    private static string? TryReadSpreadsheetText()
    {
        try
        {
            var data = Clipboard.GetDataObject();
            if (data is null) return null;

            // BIFF formats are the ONLY guaranteed Excel/Calc/Sheets markers.
            // (We previously also checked HTML for <table> markup to catch
            // Google Sheets, but reading HTML is expensive and Word also
            // includes HTML on every copy. Better to miss Sheets-as-spreadsheet
            // detection than to lag every copy.)
            bool isSpreadsheet =
                data.GetDataPresent("Biff12") ||
                data.GetDataPresent("Biff8") ||
                data.GetDataPresent("Biff5") ||
                data.GetDataPresent("XML Spreadsheet");

            if (!isSpreadsheet) return null;

            // Excel writes its cells as tab-separated text via the standard
            // Text/UnicodeText formats. Just pull that.
            try
            {
                if (data.GetDataPresent(System.Windows.DataFormats.UnicodeText))
                {
                    if (data.GetData(System.Windows.DataFormats.UnicodeText) is string utext &&
                        !string.IsNullOrWhiteSpace(utext))
                        return utext;
                }
            }
            catch { }
            try
            {
                if (data.GetDataPresent(System.Windows.DataFormats.Text))
                {
                    if (data.GetData(System.Windows.DataFormats.Text) is string text &&
                        !string.IsNullOrWhiteSpace(text))
                        return text;
                }
            }
            catch { }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Crude HTML → text: extract table cells preserving rows/columns.</summary>
    private static string StripHtmlToText(string html)
    {
        try
        {
            // Locate just the body fragment (Windows clipboard HTML has a header)
            int frag = html.IndexOf("<!--StartFragment-->", StringComparison.OrdinalIgnoreCase);
            int endFrag = html.IndexOf("<!--EndFragment-->", StringComparison.OrdinalIgnoreCase);
            string body = (frag >= 0 && endFrag > frag)
                ? html.Substring(frag + "<!--StartFragment-->".Length,
                                 endFrag - frag - "<!--StartFragment-->".Length)
                : html;

            // Replace cell separators
            body = System.Text.RegularExpressions.Regex.Replace(body, @"</td\s*>", "\t",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            body = System.Text.RegularExpressions.Regex.Replace(body, @"</tr\s*>", "\n",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            body = System.Text.RegularExpressions.Regex.Replace(body, @"<br\s*/?>", "\n",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            // Strip remaining tags
            body = System.Text.RegularExpressions.Regex.Replace(body, @"<[^>]+>", "");
            // Decode HTML entities
            body = System.Net.WebUtility.HtmlDecode(body);
            // Trim trailing tabs and excessive blank lines
            body = System.Text.RegularExpressions.Regex.Replace(body, @"\t+\n", "\n");
            body = System.Text.RegularExpressions.Regex.Replace(body, @"\n{3,}", "\n\n");
            return body.Trim();
        }
        catch
        {
            return html;
        }
    }

    private static string DetectImageContext()
    {
        try
        {
            var data = Clipboard.GetDataObject();
            if (data is null) return "Image";

            // Excel-specific formats — fast format-list checks only.
            if (data.GetDataPresent("Biff12") ||
                data.GetDataPresent("Biff8") ||
                data.GetDataPresent("Biff5") ||
                data.GetDataPresent("XML Spreadsheet"))
            {
                return "Spreadsheet copy";
            }
            return "Screenshot";
        }
        catch
        {
            return "Image";
        }
    }

    /// <summary>
    /// Compute the signature WE will use to fingerprint this image. Call this
    /// from the slot-click code BEFORE writing to clipboard, so the resulting
    /// echo capture can be skipped via LastWrittenSignature.
    /// </summary>
    public static string ComputeImageSignature(BitmapSource bmp)
    {
        return $"img:{bmp.PixelWidth}x{bmp.PixelHeight}:{EstimateImageHash(bmp)}";
    }

    public static string ComputeTextSignature(string text)
    {
        // Normalize line endings so our pre-write signature matches the
        // one computed after Windows reads it back (Windows often converts
        // \n to \r\n inside the clipboard layer, which would otherwise
        // make our signature mismatch and re-capture our own write).
        var normalized = text.Replace("\r\n", "\n").Replace("\r", "\n");
        return $"txt:{normalized.GetHashCode()}:{normalized.Length}";
    }

    /// <summary>Cheap hash by reading a few corner pixels — good enough to detect duplicates.</summary>
    private static int EstimateImageHash(BitmapSource bmp)
    {
        try
        {
            int w = bmp.PixelWidth, h = bmp.PixelHeight;
            int hash = w * 31 + h;
            // Sample 4 corner pixels
            var pixels = new byte[4];
            try
            {
                bmp.CopyPixels(new Int32Rect(0, 0, 1, 1), pixels, 4, 0);
                hash = hash * 31 + BitConverter.ToInt32(pixels, 0);
            }
            catch { }
            try
            {
                bmp.CopyPixels(new Int32Rect(w - 1, 0, 1, 1), pixels, 4, 0);
                hash = hash * 31 + BitConverter.ToInt32(pixels, 0);
            }
            catch { }
            try
            {
                bmp.CopyPixels(new Int32Rect(0, h - 1, 1, 1), pixels, 4, 0);
                hash = hash * 31 + BitConverter.ToInt32(pixels, 0);
            }
            catch { }
            return hash;
        }
        catch { return 0; }
    }

    /// <summary>
    /// True if the given content signature matches the last one we captured
    /// within the last 1.5 seconds. Prevents the "screenshot fills 3 slots"
    /// behavior caused by multiple format updates.
    /// </summary>
    private bool IsDuplicate(string signature)
    {
        var now = DateTime.UtcNow;
        if (signature == _lastSignature && (now - _lastCaptureAt).TotalMilliseconds < 1500)
        {
            return true;
        }
        _lastSignature = signature;
        _lastCaptureAt = now;
        return false;
    }

    /// <summary>
    /// Produces a thumbnail by encoding the source as PNG bytes and decoding
    /// back as a BitmapImage with DecodePixelWidth set. This is the most
    /// reliable approach in WPF: it produces a fully-baked bitmap with
    /// explicit pixel dimensions, decoded fresh from a memory stream so it
    /// has no thread/DPI/handle dependencies on the source.
    /// </summary>
    public static BitmapSource MakeThumbnail(BitmapSource src, int targetSize)
    {
        if (src.PixelWidth == 0 || src.PixelHeight == 0) return src;

        // Fast path: image is already at-or-below thumbnail size. Skip work.
        int longSide = Math.Max(src.PixelWidth, src.PixelHeight);
        if (longSide <= targetSize * 2)
        {
            try
            {
                if (src.CanFreeze && !src.IsFrozen) src.Freeze();
                return src;
            }
            catch { /* fall through to slow path */ }
        }

        // Use TransformedBitmap to scale, but then BAKE the result by copying
        // pixels into a fresh frozen WriteableBitmap. TransformedBitmap is lazy
        // — it defers scaling to render time, which would block the UI thread
        // every time the row repaints. Baking it once eliminates that.
        try
        {
            double scale = (double)targetSize / longSide;
            var tb = new TransformedBitmap(src, new ScaleTransform(scale, scale));
            // Force bake: copy pixels into a fresh WriteableBitmap
            var baked = new WriteableBitmap(tb);
            baked.Freeze();
            return baked;
        }
        catch { /* fall through to safe path */ }

        try
        {
            // Safe path (last resort): encode source to PNG bytes, decode at target size.
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(src));
            using var ms = new System.IO.MemoryStream();
            encoder.Save(ms);
            ms.Position = 0;

            int decodeW = 0, decodeH = 0;
            if (src.PixelWidth >= src.PixelHeight)
                decodeW = targetSize;
            else
                decodeH = targetSize;

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            if (decodeW > 0) bmp.DecodePixelWidth = decodeW;
            if (decodeH > 0) bmp.DecodePixelHeight = decodeH;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return src;
        }
    }

    public void Dispose()
    {
        if (_hwnd != IntPtr.Zero)
            RemoveClipboardFormatListener(_hwnd);
        _hwndSource?.RemoveHook(WndProc);
        _debounceTimer?.Stop();
    }
}
