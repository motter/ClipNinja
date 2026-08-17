using System;
using System.Collections.Generic;
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
    /// Fired after a captured image had a border baked in, signaling that
    /// the host should write the bordered version BACK onto the live
    /// clipboard. This is how the "auto-border" feature reaches the user's
    /// next paste without requiring them to click a ClipNinja slot first.
    ///
    /// The tuple contains the bordered BitmapSource and the signature we
    /// computed for it; the host should set LastWrittenSignature to that
    /// signature BEFORE writing to prevent re-capture of our own echo.
    /// </summary>
    public event EventHandler<(BitmapSource Bordered, string Signature)>? BorderedImageReadyForClipboardReplace;

    /// <summary>
    /// Ignore all clipboard-update events until this time. Used during paste.
    /// We use a time-based suppression (not bool) because a single SetImage
    /// can fire multiple WM_CLIPBOARDUPDATE events, and a bool only catches one.
    /// </summary>
    public DateTime SuppressUntil { get; set; } = DateTime.MinValue;

    /// <summary>One-shot: when set, the NEXT observed image is added to
    /// the list WITHOUT applying the global effect settings (because the
    /// caller — the annotator — already baked its own per-image effects).
    /// Reset automatically after that image. Unlike SuppressUntil, this
    /// does NOT stop the image from being captured into the list.</summary>
    public bool SkipEffectsOnce { get; set; }

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
    /// <summary>
    /// When true, captured images get a 3px black border baked in before
    /// being stored. Helpful for screenshots pasted into docs that would
    /// otherwise blend into the page. Settable from the ViewModel which
    /// mirrors AppSettings.AddBorderToImages.
    /// </summary>
    public bool AddBorderToImages { get; set; } = true;

    /// <summary>Bake a soft drop shadow onto captured images. Mirrors
    /// AppSettings.AddDropShadowToImages.</summary>
    public bool AddDropShadowToImages { get; set; } = false;

    /// <summary>Torn top edge effect. Mirrors AppSettings.AddTornTopEdge.</summary>
    public bool AddTornTopEdge { get; set; } = false;

    /// <summary>Torn bottom edge effect. Mirrors AppSettings.AddTornBottomEdge.</summary>
    public bool AddTornBottomEdge { get; set; } = false;

    /// <summary>Torn left edge effect. Mirrors AppSettings.AddTornLeftEdge.</summary>
    public bool AddTornLeftEdge { get; set; } = false;

    /// <summary>Torn right edge effect. Mirrors AppSettings.AddTornRightEdge.</summary>
    public bool AddTornRightEdge { get; set; } = false;

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
                    bool wasModified = false;
                    // Bake on the configurable effects BEFORE we compute
                    // signatures and thumbnails — that way the effect-baked
                    // image is what gets cached, hashed, and pasted back
                    // later. Order: torn edges FIRST (they crop pixels
                    // from the source), then border (frames whatever's
                    // left), then drop shadow (surrounds the framed result).
                    // Any effect at all triggers the "replace-live-clipboard"
                    // flow so hot-key paste gets the effect-baked version.
                    // SkipEffectsOnce (set when the annotator already baked
                    // its own effects) makes us add the image to the list
                    // WITHOUT re-applying — but only for this one image.
                    bool skipFx = SkipEffectsOnce;
                    SkipEffectsOnce = false;
                    bool tornAny = AddTornTopEdge || AddTornBottomEdge || AddTornLeftEdge || AddTornRightEdge;
                    if (!skipFx && (tornAny || AddBorderToImages || AddDropShadowToImages))
                    {
                        using (Trace.Time("watcher", "ApplyEffects"))
                        {
                            bmp = ApplyEffects(bmp, tornAll: tornAny,
                                border: AddBorderToImages, shadow: AddDropShadowToImages);
                            wasModified = true;
                        }
                    }

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

                    // If we modified the image (border baked in), the live
                    // clipboard still holds the ORIGINAL un-bordered version
                    // that the user's next paste would receive. Notify the
                    // host so it can replace the live clipboard with our
                    // bordered version. This is what makes the "auto-border"
                    // feature actually work for hot-key pasting — without
                    // this, the user would have to click the ClipNinja slot
                    // first to get the bordered version onto the clipboard.
                    if (wasModified)
                    {
                        BorderedImageReadyForClipboardReplace?.Invoke(this, (bmp, sig));
                    }
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

                    // Pull rich formats if the source provided them. Outlook
                    // selections, web pages, Word, Teams chat etc. all put
                    // CF_HTML and/or CF_RTF on the clipboard alongside the
                    // plain text. We capture them verbatim so they can be
                    // round-tripped back when the user pastes the slot.
                    //
                    // Size cap (2MB) avoids settings.json bloat from giant
                    // rich-text selections; above the cap we keep only the
                    // plain text.
                    string? html = null, rtf = null;
                    IReadOnlyList<HyperLink> links = Array.Empty<HyperLink>();
                    try
                    {
                        var data = Clipboard.GetDataObject();
                        if (data is not null)
                        {
                            const int sizeCap = 2 * 1024 * 1024;
                            if (data.GetDataPresent(DataFormats.Html))
                            {
                                if (data.GetData(DataFormats.Html) is string h && h.Length < sizeCap)
                                {
                                    html = h;
                                    links = ParseHyperlinksFromHtml(h);
                                }
                                else if (data.GetData(DataFormats.Html) is string hbig)
                                {
                                    Trace.Log("watcher", $"CF_HTML present but {hbig.Length} bytes > cap, dropping");
                                }
                            }
                            if (data.GetDataPresent(DataFormats.Rtf))
                            {
                                if (data.GetData(DataFormats.Rtf) is string r && r.Length < sizeCap)
                                    rtf = r;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Trace.Log("watcher", $"Rich format read failed: {ex.GetType().Name}: {ex.Message}");
                    }

                    Trace.Log("watcher", $"CAPTURED text (len={text.Length}, html={html?.Length ?? 0}, rtf={rtf?.Length ?? 0}, links={links.Count})");
                    var content = new TextContent
                    {
                        Text = text,
                        HtmlFormat = html,
                        RtfFormat = rtf,
                        Links = links,
                    };
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
    /// Extract (label, url) pairs from a CF_HTML clipboard payload. Walks
    /// every &lt;a href="..."&gt;...&lt;/a&gt; tag, decodes minimal HTML
    /// entities in the label (&amp;amp;, &amp;quot;, etc.), and strips inner
    /// tags from the label text so links found inside &lt;span&gt; or other
    /// wrappers still extract cleanly.
    /// 
    /// We intentionally use regex rather than a real HTML parser — the CF_HTML
    /// produced by Outlook/Word/Teams is wildly varied and full of corruption
    /// edge cases, but the &lt;a href&gt; pattern is consistent. A regex that
    /// captures 99% of links is more useful here than a strict parser that
    /// chokes on the first malformed tag.
    /// </summary>
    private static IReadOnlyList<HyperLink> ParseHyperlinksFromHtml(string html)
    {
        var results = new List<HyperLink>();
        try
        {
            // Match <a ... href="URL" ...> LABEL </a>, case-insensitive,
            // tolerant of single OR double quotes around the URL, and other
            // attributes before/after href.
            var re = new System.Text.RegularExpressions.Regex(
                @"<a\b[^>]*?\shref\s*=\s*(?:""([^""]*)""|'([^']*)')[^>]*?>(.*?)</a>",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase |
                System.Text.RegularExpressions.RegexOptions.Singleline);
            var matches = re.Matches(html);
            var stripTags = new System.Text.RegularExpressions.Regex(@"<[^>]+>");
            foreach (System.Text.RegularExpressions.Match m in matches)
            {
                string url = !string.IsNullOrEmpty(m.Groups[1].Value)
                    ? m.Groups[1].Value
                    : m.Groups[2].Value;
                string labelHtml = m.Groups[3].Value;
                // Strip nested tags, decode common entities
                string label = stripTags.Replace(labelHtml, "");
                label = System.Net.WebUtility.HtmlDecode(label).Trim();
                if (string.IsNullOrWhiteSpace(label)) label = url;
                if (string.IsNullOrWhiteSpace(url)) continue;
                results.Add(new HyperLink(label, url));
            }
        }
        catch { /* parsing should never throw; safe fallback to empty */ }
        return results;
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

            // GIF detection: when a .gif file is copied from File Explorer
            // (or other file-aware sources), the clipboard carries a file
            // reference via CF_HDROP in addition to a static-frame bitmap.
            // We can't show the animation (WPF doesn't natively animate
            // GIFs in Image controls), but we CAN label the slot so the
            // user knows what they actually have.
            //
            // Note: pasting a GIF from a browser typically does NOT include
            // CF_HDROP — browsers usually rasterize to a single frame. So
            // GIF detection here works for File Explorer copies but not
            // browser image-copies. The static thumbnail is the truth in
            // both cases; we just label the file-source one better.
            try
            {
                if (data.GetDataPresent(System.Windows.DataFormats.FileDrop))
                {
                    if (data.GetData(System.Windows.DataFormats.FileDrop) is string[] files)
                    {
                        foreach (var f in files)
                        {
                            if (f.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
                                return "GIF";
                        }
                    }
                }
            }
            catch { /* FileDrop read failed — fall through */ }

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
    public static int EstimateImageHash(BitmapSource bmp)
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
    /// <summary>
    /// Bake a thin black border around <paramref name="src"/>. Uses direct
    /// pixel copying via WriteableBitmap so it's immune to DPI scaling
    /// issues that plagued earlier DrawingVisual-based attempts (the source
    /// reports DIUs not pixels, and DrawImage sizes by DIU).
    /// </summary>
    public static BitmapSource AddBlackBorder(BitmapSource src, int borderPx)
    {
        if (src.PixelWidth == 0 || src.PixelHeight == 0 || borderPx <= 0) return src;
        try
        {
            int srcW = src.PixelWidth;
            int srcH = src.PixelHeight;
            int outerW = srcW + borderPx * 2;
            int outerH = srcH + borderPx * 2;

            // Convert source to Bgra32 if it isn't already — uniform pixel
            // format makes the byte math below predictable.
            BitmapSource bgra = src.Format == System.Windows.Media.PixelFormats.Bgra32
                ? src
                : new FormatConvertedBitmap(src, System.Windows.Media.PixelFormats.Bgra32, null, 0);

            int srcStride = srcW * 4;
            var srcPixels = new byte[srcStride * srcH];
            bgra.CopyPixels(srcPixels, srcStride, 0);

            int outStride = outerW * 4;
            var outPixels = new byte[outStride * outerH];

            // Fill the entire output buffer with opaque black (BGRA = 0,0,0,255).
            for (int i = 3; i < outPixels.Length; i += 4) outPixels[i] = 255;

            // Copy source pixels into the inset region row by row.
            for (int y = 0; y < srcH; y++)
            {
                int srcRow = y * srcStride;
                int dstRow = (y + borderPx) * outStride + borderPx * 4;
                System.Buffer.BlockCopy(srcPixels, srcRow, outPixels, dstRow, srcStride);
            }

            // Output at 96 DPI regardless of source DPI. This makes the
            // output bitmap's DIU (device-independent unit) dimensions
            // match its pixel dimensions, which is what all downstream
            // consumers assume:
            //  - The slot's Image control uses Uniform stretch within a
            //    32x32 DIU container. If the bitmap reports a smaller DIU
            //    size than its pixel count (high-DPI source), the image
            //    appears tiny with empty space around it.
            //  - When pasted into Word/Outlook, the DIU dimensions drive
            //    the displayed size. Matching DIU to pixels gives us the
            //    same physical size as the un-bordered source on any
            //    DPI scaling.
            // Trade-off: on a 200% scaled display, the bordered image
            // shows 2x larger than the un-bordered source did. Acceptable;
            // most users won't notice and the alternative (DIU-aware DPI)
            // breaks thumbnail rendering and produces inconsistent paste
            // sizes anyway.
            var wb = new WriteableBitmap(outerW, outerH, 96, 96,
                System.Windows.Media.PixelFormats.Bgra32, null);
            wb.WritePixels(new System.Windows.Int32Rect(0, 0, outerW, outerH),
                outPixels, outStride, 0);
            wb.Freeze();
            return wb;
        }
        catch { return src; }
    }

    /// <summary>
    /// Bake a soft drop shadow onto the image. Expands the canvas by
    /// <paramref name="offsetPx"/>+<paramref name="blurPx"/> on the
    /// right and bottom, fills the expansion with a semi-transparent
    /// dark falloff that fades from ~50% opaque at the image edge to
    /// 0% at the outer edge, then blits the source in the top-left.
    ///
    /// Simple two-axis exponential falloff — cheaper than a real
    /// gaussian blur and looks convincing at small blur sizes. The
    /// shadow only shows on the right + bottom (evokes light from
    /// the upper-left), which matches most operating-system UI
    /// conventions and reads naturally.
    ///
    /// Output at 96 DPI to match AddBlackBorder's DIU semantics —
    /// prevents inconsistent paste sizes on high-DPI displays.
    /// </summary>
    /// <summary>
    /// Greenshot-style soft drop shadow: the image gets a transparent
    /// margin on ALL sides, with a soft dark shadow that surrounds it and
    /// sits slightly below-right. Unlike the old offset-only shadow (hard
    /// top-left edge, shadow only on two sides), this pads every side so
    /// the image floats on a soft shadow — the clean look you get from
    /// Greenshot. `blurPx` is the softness radius; `offsetPx` nudges the
    /// shadow down-right so the light reads as coming from top-left.
    /// </summary>
    /// <summary>Apply the presentation effects (torn edges → border →
    /// drop shadow) to an image in the correct order. Shared by the
    /// clipboard watcher (global-setting captures) and the annotator
    /// (per-image toggles) so both produce identical results. Torn runs
    /// first so the shadow casts from the ragged silhouette; shadow
    /// composites everything onto opaque white so it survives the
    /// clipboard.</summary>
    public static BitmapSource ApplyEffects(BitmapSource bmp,
        bool tornAll, bool border, bool shadow)
    {
        if (tornAll)
            bmp = ApplyTornEdges(bmp, true, true, true, true);
        if (border)
            bmp = AddBlackBorder(bmp, borderPx: 3);
        if (shadow)
        {
            // Composites onto opaque white and (if torn) reveals the
            // shadow through the ragged edges.
            bmp = AddDropShadow(bmp, offsetPx: 4, blurPx: 12);
        }
        else if (tornAll)
        {
            // Torn WITHOUT shadow still leaves transparent ragged edges,
            // which the clipboard would blacken. Flatten onto white so
            // the tears become white and survive paste.
            bmp = FlattenOntoWhite(bmp);
        }
        return bmp;
    }

    /// <summary>Composite an image (which may have alpha, e.g. torn
    /// edges) onto an opaque white background at the same size. Used so a
    /// torn image with no drop shadow doesn't paste with black edges
    /// (the clipboard drops the alpha channel).</summary>
    public static BitmapSource FlattenOntoWhite(BitmapSource src)
    {
        if (src.PixelWidth == 0 || src.PixelHeight == 0) return src;
        try
        {
            int w = src.PixelWidth, h = src.PixelHeight;
            var bgra = src.Format == System.Windows.Media.PixelFormats.Bgra32
                ? src
                : new FormatConvertedBitmap(src, System.Windows.Media.PixelFormats.Bgra32, null, 0);
            int stride = w * 4;
            var px = new byte[stride * h];
            bgra.CopyPixels(px, stride, 0);
            for (int i = 0; i < px.Length; i += 4)
            {
                double a = px[i + 3] / 255.0;
                if (a >= 1) { px[i + 3] = 255; continue; }
                px[i + 0] = (byte)Math.Round(px[i + 0] * a + 255 * (1 - a));
                px[i + 1] = (byte)Math.Round(px[i + 1] * a + 255 * (1 - a));
                px[i + 2] = (byte)Math.Round(px[i + 2] * a + 255 * (1 - a));
                px[i + 3] = 255;
            }
            var wb = new WriteableBitmap(w, h, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null);
            wb.WritePixels(new System.Windows.Int32Rect(0, 0, w, h), px, stride, 0);
            wb.Freeze();
            return wb;
        }
        catch { return src; }
    }

    public static BitmapSource AddDropShadow(BitmapSource src, int offsetPx, int blurPx)
    {
        if (src.PixelWidth == 0 || src.PixelHeight == 0 || blurPx <= 0) return src;
        try
        {
            int srcW = src.PixelWidth;
            int srcH = src.PixelHeight;

            // Uniform margin on every side, with extra room down-right for
            // the offset so the shadow can extend all the way around.
            int margin = blurPx + 2;
            int padLeft = margin, padTop = margin;
            int padRight = margin + offsetPx, padBottom = margin + offsetPx;
            int outerW = srcW + padLeft + padRight;
            int outerH = srcH + padTop + padBottom;

            var bgra = src.Format == System.Windows.Media.PixelFormats.Bgra32
                ? src
                : new FormatConvertedBitmap(src, System.Windows.Media.PixelFormats.Bgra32, null, 0);
            int srcStride = srcW * 4;
            var srcPixels = new byte[srcStride * srcH];
            bgra.CopyPixels(srcPixels, srcStride, 0);

            // Silhouette-based shadow so it works for BOTH a plain
            // rectangle AND a torn (ragged-alpha) image: we take the
            // source's alpha as a mask, blur it, and paint that blurred
            // mask as gray onto white. Where the image is torn away near
            // an edge, the blurred shadow shows through — "tears reveal
            // the shadow beneath". Everything is composited onto OPAQUE
            // WHITE so the Windows clipboard (which drops alpha) can't
            // turn it black.

            // 1. Build the shadow mask at output size: source alpha placed
            //    at the shadow position (image pos shifted by offset).
            var mask = new float[outerW * outerH];
            int shX = padLeft + offsetPx, shY = padTop + offsetPx;
            for (int y = 0; y < srcH; y++)
            {
                int sRow = y * srcStride;
                int mRow = (y + shY) * outerW + shX;
                for (int x = 0; x < srcW; x++)
                    mask[mRow + x] = srcPixels[sRow + x * 4 + 3] / 255f;
            }

            // 2. Separable box blur (3 passes ≈ gaussian) of the mask.
            int radius = Math.Max(1, blurPx / 2);
            var tmp = new float[outerW * outerH];
            for (int pass = 0; pass < 3; pass++)
            {
                BoxBlurH(mask, tmp, outerW, outerH, radius);
                BoxBlurV(tmp, mask, outerW, outerH, radius);
            }

            // 3. White canvas, darkened toward black by the blurred mask.
            int outStride = outerW * 4;
            var outPixels = new byte[outStride * outerH];
            const double peak = 0.42;   // max shadow darkness (0..1)
            for (int y = 0; y < outerH; y++)
            {
                int oRow = y * outStride;
                int mRow = y * outerW;
                for (int x = 0; x < outerW; x++)
                {
                    double t = mask[mRow + x] * peak;
                    byte v = (byte)Math.Round(255 * (1 - t));
                    int p = oRow + x * 4;
                    outPixels[p + 0] = v;
                    outPixels[p + 1] = v;
                    outPixels[p + 2] = v;
                    outPixels[p + 3] = 255;   // opaque
                }
            }

            // 4. Composite the source over the shadowed white using the
            //    source's own alpha, so torn areas keep the shadow behind.
            for (int y = 0; y < srcH; y++)
            {
                int sRow = y * srcStride;
                int oRow = (y + padTop) * outStride + padLeft * 4;
                for (int x = 0; x < srcW; x++)
                {
                    int sp = sRow + x * 4;
                    int op = oRow + x * 4;
                    double a = srcPixels[sp + 3] / 255.0;
                    if (a <= 0) continue;            // fully torn — keep shadow
                    if (a >= 1)
                    {
                        outPixels[op + 0] = srcPixels[sp + 0];
                        outPixels[op + 1] = srcPixels[sp + 1];
                        outPixels[op + 2] = srcPixels[sp + 2];
                        continue;
                    }
                    // Partial edge alpha — blend over the backing.
                    for (int c = 0; c < 3; c++)
                        outPixels[op + c] = (byte)Math.Round(srcPixels[sp + c] * a + outPixels[op + c] * (1 - a));
                }
            }

            var wb = new WriteableBitmap(outerW, outerH, 96, 96,
                System.Windows.Media.PixelFormats.Bgra32, null);
            wb.WritePixels(new System.Windows.Int32Rect(0, 0, outerW, outerH),
                outPixels, outStride, 0);
            wb.Freeze();
            return wb;
        }
        catch { return src; }
    }

    // Separable box blur helpers for the shadow mask (float alpha 0..1).
    private static void BoxBlurH(float[] src, float[] dst, int w, int h, int r)
    {
        double norm = 1.0 / (2 * r + 1);
        for (int y = 0; y < h; y++)
        {
            int row = y * w;
            double acc = 0;
            for (int x = -r; x <= r; x++) acc += src[row + Math.Clamp(x, 0, w - 1)];
            for (int x = 0; x < w; x++)
            {
                dst[row + x] = (float)(acc * norm);
                int add = Math.Clamp(x + r + 1, 0, w - 1);
                int sub = Math.Clamp(x - r, 0, w - 1);
                acc += src[row + add] - src[row + sub];
            }
        }
    }
    private static void BoxBlurV(float[] src, float[] dst, int w, int h, int r)
    {
        double norm = 1.0 / (2 * r + 1);
        for (int x = 0; x < w; x++)
        {
            double acc = 0;
            for (int y = -r; y <= r; y++) acc += src[Math.Clamp(y, 0, h - 1) * w + x];
            for (int y = 0; y < h; y++)
            {
                dst[y * w + x] = (float)(acc * norm);
                int add = Math.Clamp(y + r + 1, 0, h - 1);
                int sub = Math.Clamp(y - r, 0, h - 1);
                acc += src[add * w + x] - src[sub * w + x];
            }
        }
    }

    /// <summary>
    /// Apply a torn / ragged edge on any combination of the four sides
    /// by punching out (alpha-zeroing) pixels beyond a jagged
    /// piecewise-linear "tear line" per side. Enable all four for the
    /// full "torn out of a magazine article" look. The tear lines are
    /// generated deterministically from the image dimensions (plus a
    /// per-side salt so opposite sides don't mirror each other), so
    /// re-processing the same source gives the same tear.
    ///
    /// Tear depth ~= 6% of the perpendicular dimension, clamped to
    /// 8-40 pixels. Each tear segment is 6-16px long; segment
    /// endpoints jitter within [0, depth] pixels off the flat edge.
    /// Where two tears meet at a corner, the cutouts union naturally.
    /// </summary>
    public static BitmapSource ApplyTornEdges(BitmapSource src,
        bool tornTop, bool tornBottom, bool tornLeft = false, bool tornRight = false)
    {
        if (src.PixelWidth == 0 || src.PixelHeight == 0) return src;
        if (!tornTop && !tornBottom && !tornLeft && !tornRight) return src;
        try
        {
            int w = src.PixelWidth;
            int h = src.PixelHeight;

            var bgra = src.Format == System.Windows.Media.PixelFormats.Bgra32
                ? src
                : new FormatConvertedBitmap(src, System.Windows.Media.PixelFormats.Bgra32, null, 0);

            int stride = w * 4;
            var pixels = new byte[stride * h];
            bgra.CopyPixels(pixels, stride, 0);

            // Shallow "nibble" tear — a small fixed depth that does NOT
            // grow with image size, so it never chews far into content.
            // (Was h*0.06 up to 40px, which cut into text.) Leave a little
            // margin around content when capturing if you want the tear to
            // stay clear of it.
            int depth = Math.Clamp(Math.Min(w, h) / 90, 3, 8);
            int depthH = depth, depthV = depth;

            // Build a per-position offset array along an edge of the
            // given length: how many pixels are "torn off". Interpolate
            // between random keypoints every 6-16 positions so the tear
            // looks jagged but coherent. Seeded deterministically; the
            // salt keeps the four edges visually distinct.
            int[] BuildTearProfile(int length, int d, int salt)
            {
                var rng = new System.Random(w * 73856093 ^ h * 19349663 ^ salt * 83492791);
                var offsets = new int[length];
                int pos = 0;
                int prevPos = 0;
                int prevOff = rng.Next(0, d + 1);
                offsets[0] = prevOff;
                while (pos < length - 1)
                {
                    int step = 6 + rng.Next(11);  // 6..16
                    int nextPos = Math.Min(length - 1, pos + step);
                    int nextOff = rng.Next(0, d + 1);
                    for (int i = prevPos + 1; i <= nextPos; i++)
                    {
                        double t = (double)(i - prevPos) / (nextPos - prevPos);
                        offsets[i] = (int)(prevOff + (nextOff - prevOff) * t);
                    }
                    prevPos = nextPos;
                    prevOff = nextOff;
                    pos = nextPos;
                }
                return offsets;
            }

            if (tornTop)
            {
                var prof = BuildTearProfile(w, depthH, salt: 1);
                for (int x = 0; x < w; x++)
                    for (int y = 0; y < prof[x]; y++)
                        pixels[y * stride + x * 4 + 3] = 0;
            }
            if (tornBottom)
            {
                var prof = BuildTearProfile(w, depthH, salt: 2);
                for (int x = 0; x < w; x++)
                    for (int y = 0; y < prof[x]; y++)
                        pixels[(h - 1 - y) * stride + x * 4 + 3] = 0;
            }
            if (tornLeft)
            {
                var prof = BuildTearProfile(h, depthV, salt: 3);
                for (int y = 0; y < h; y++)
                {
                    int rowBase = y * stride;
                    for (int x = 0; x < prof[y]; x++)
                        pixels[rowBase + x * 4 + 3] = 0;
                }
            }
            if (tornRight)
            {
                var prof = BuildTearProfile(h, depthV, salt: 4);
                for (int y = 0; y < h; y++)
                {
                    int rowBase = y * stride;
                    for (int x = 0; x < prof[y]; x++)
                        pixels[rowBase + (w - 1 - x) * 4 + 3] = 0;
                }
            }

            var wb = new WriteableBitmap(w, h, 96, 96,
                System.Windows.Media.PixelFormats.Bgra32, null);
            wb.WritePixels(new System.Windows.Int32Rect(0, 0, w, h),
                pixels, stride, 0);
            wb.Freeze();
            return wb;
        }
        catch { return src; }
    }

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
