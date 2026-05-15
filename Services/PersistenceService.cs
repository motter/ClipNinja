using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClipNinjaV2.Models;

namespace ClipNinjaV2.Services;

/// <summary>
/// Saves and loads slots + settings to %AppData%\ClipNinja\.
/// Images are stored as PNG files in an images/ subfolder; slot records
/// reference them by filename.
/// </summary>
public class PersistenceService
{
    public string AppDir { get; }
    public string SettingsPath { get; }
    public string SlotsPath { get; }
    public string ImagesDir { get; }

    public PersistenceService()
    {
        AppDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ClipNinja");
        SettingsPath = Path.Combine(AppDir, "settings.json");
        SlotsPath = Path.Combine(AppDir, "slots.json");
        ImagesDir = Path.Combine(AppDir, "images");
        Directory.CreateDirectory(AppDir);
        Directory.CreateDirectory(ImagesDir);
    }

    // ── Settings ────────────────────────────────────────────────────────────

    public AppSettings LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new AppSettings();
            var json = File.ReadAllText(SettingsPath);
            var s = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();

            // Migration: v1 default was 15 slots; v1.1 raised to 30.
            // If the user is still on the old default, bump them automatically.
            if (s.SlotCount == 15) s.SlotCount = 30;

            s.ClampToValidRanges();
            return s;
        }
        catch { return new AppSettings(); }
    }

    public void SaveSettings(AppSettings s)
    {
        try
        {
            var json = JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch { /* best-effort */ }
    }

    // ── Slots ───────────────────────────────────────────────────────────────

    /// <summary>Wire-format DTO for serialization.</summary>
    private class SlotDto
    {
        public int Index { get; set; }
        public bool IsPinned { get; set; }
        public string Nickname { get; set; } = "";
        public string ContentType { get; set; } = "empty";   // empty | text | image
        public string Text { get; set; } = "";
        public bool IsSpreadsheet { get; set; }              // true if text came from a table source
        public string ImageFile { get; set; } = "";          // filename only, lives in images/
        public int ImgWidth { get; set; }
        public int ImgHeight { get; set; }
        public string ImageLabel { get; set; } = "Screenshot";
        public DateTime CapturedAt { get; set; }
    }

    public List<ClipSlot> LoadSlots(int slotCount)
    {
        var result = new List<ClipSlot>();
        try
        {
            if (File.Exists(SlotsPath))
            {
                var json = File.ReadAllText(SlotsPath);
                var dtos = JsonSerializer.Deserialize<List<SlotDto>>(json) ?? new();
                foreach (var dto in dtos.Take(slotCount))
                {
                    result.Add(DtoToSlot(dto));
                }
            }
        }
        catch { /* fall through to fill empties */ }

        // Pad with empty slots if needed
        while (result.Count < slotCount)
        {
            result.Add(new ClipSlot { Index = result.Count + 1 });
        }
        // Re-index so positions are sane
        for (int i = 0; i < result.Count; i++) result[i].Index = i + 1;
        return result;
    }

    public void SaveSlots(IEnumerable<ClipSlot> slots)
    {
        try
        {
            var dtos = slots.Select(SlotToDto).ToList();
            var json = JsonSerializer.Serialize(dtos, new JsonSerializerOptions { WriteIndented = true });
            // Atomic write: tmp → rename
            var tmp = SlotsPath + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(SlotsPath)) File.Delete(SlotsPath);
            File.Move(tmp, SlotsPath);
        }
        catch { /* best-effort */ }
    }

    private SlotDto SlotToDto(ClipSlot s)
    {
        var dto = new SlotDto
        {
            Index = s.Index,
            IsPinned = s.IsPinned,
            Nickname = s.Nickname,
            CapturedAt = s.Content?.CapturedAt ?? DateTime.MinValue,
        };
        switch (s.Content)
        {
            case TextContent tc:
                dto.ContentType = "text";
                dto.Text = tc.Text;
                dto.IsSpreadsheet = tc.IsSpreadsheet;
                break;
            case ImageContent ic:
                dto.ContentType = "image";
                dto.ImgWidth = ic.OriginalWidth;
                dto.ImgHeight = ic.OriginalHeight;
                dto.ImageLabel = ic.DisplayLabel;
                if (ic.FullImage is not null)
                {
                    // Reuse the existing file if we already saved this image.
                    // Otherwise encode to PNG once and remember the filename.
                    string fileName = ic.SavedFileName;
                    var existingPath = string.IsNullOrEmpty(fileName) ? "" : Path.Combine(ImagesDir, fileName);
                    if (string.IsNullOrEmpty(fileName) || !File.Exists(existingPath))
                    {
                        fileName = $"slot{s.Index}_{Guid.NewGuid():N}.png";
                        var path = Path.Combine(ImagesDir, fileName);
                        SaveBitmapAsPng(ic.FullImage, path);
                        ic.SavedFileName = fileName;
                    }
                    dto.ImageFile = fileName;
                }
                break;
            default:
                dto.ContentType = "empty";
                break;
        }
        return dto;
    }

    private ClipSlot DtoToSlot(SlotDto dto)
    {
        var slot = new ClipSlot
        {
            Index = dto.Index,
            IsPinned = dto.IsPinned,
            Nickname = dto.Nickname,
        };
        switch (dto.ContentType)
        {
            case "text":
                slot.Content = new TextContent { Text = dto.Text, CapturedAt = dto.CapturedAt, IsSpreadsheet = dto.IsSpreadsheet };
                break;
            case "image":
                if (!string.IsNullOrEmpty(dto.ImageFile))
                {
                    var path = Path.Combine(ImagesDir, dto.ImageFile);
                    if (File.Exists(path))
                    {
                        var full = LoadBitmapFromPng(path);
                        var thumb = MakeThumbnail(full, 32);
                        slot.Content = new ImageContent
                        {
                            FullImage = full,
                            Thumbnail = thumb,
                            OriginalWidth = dto.ImgWidth > 0 ? dto.ImgWidth : full.PixelWidth,
                            OriginalHeight = dto.ImgHeight > 0 ? dto.ImgHeight : full.PixelHeight,
                            DisplayLabel = string.IsNullOrEmpty(dto.ImageLabel) ? "Screenshot" : dto.ImageLabel,
                            CapturedAt = dto.CapturedAt,
                            // CRITICAL: remember the file we loaded from, so the
                            // next save reuses it instead of writing a fresh GUID.
                            SavedFileName = dto.ImageFile,
                        };
                    }
                }
                break;
        }
        return slot;
    }

    public static void SaveBitmapAsPng(BitmapSource bmp, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = new FileStream(path, FileMode.Create);
        encoder.Save(fs);
    }

    public static BitmapImage LoadBitmapFromPng(string path)
    {
        var bi = new BitmapImage();
        bi.BeginInit();
        bi.CacheOption = BitmapCacheOption.OnLoad;   // close file immediately
        bi.UriSource = new Uri(path);
        bi.EndInit();
        bi.Freeze();
        return bi;
    }

    /// <summary>Generate a thumbnail. Fast path for already-small images;
    /// otherwise scale via TransformedBitmap and bake the pixels.</summary>
    public static BitmapSource MakeThumbnail(BitmapSource src, int targetSize)
    {
        if (src.PixelWidth == 0 || src.PixelHeight == 0) return src;

        // Already small? Just freeze and return.
        int longSide = Math.Max(src.PixelWidth, src.PixelHeight);
        if (longSide <= targetSize * 2)
        {
            try { if (src.CanFreeze && !src.IsFrozen) src.Freeze(); return src; } catch { }
        }

        // Scale via TransformedBitmap, then bake pixels into a fresh
        // WriteableBitmap so render time is constant (no lazy scaling).
        try
        {
            double scale = (double)targetSize / longSide;
            var tb = new TransformedBitmap(src, new ScaleTransform(scale, scale));
            var baked = new WriteableBitmap(tb);
            baked.Freeze();
            return baked;
        }
        catch { return src; }
    }
}
