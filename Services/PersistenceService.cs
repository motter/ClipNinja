using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClipNinjaV2.Models;

namespace ClipNinjaV2.Services;

/// <summary>
/// Saves and loads settings + items to %AppData%\ClipNinja\.
///
/// v2 (current) layout:
///   • settings.json          — preferences (incl. SchemaVersion=2)
///   • items.json             — { Pinned: [SlotDto...], Recent: [SlotDto...] }
///   • images\                — full-resolution PNG files for active items
///   • history.json           — rolling history list
///   • history\images\        — full-resolution PNG files for history items
///
/// v1 (pre-2.4.0) layout:
///   • settings.json          — preferences (SlotCount, etc.)
///   • slots.json             — flat list of 30 SlotDto entries (positional)
///   • images\                — full-resolution PNG files
///
/// One-time migration v1 → v2 happens on first load if items.json doesn't
/// exist but slots.json does:
///   • slots that were IsPinned=true in v1 (anywhere in 1-30) become Pinned
///   • slots 16-30 with content (the "Memory Hole") also become Pinned —
///     they were explicit user-stashed content
///   • slots 1-15 unpinned with content stay in Recent
///   • empty slots are dropped
///   • the old slots.json is renamed to slots.v1.bak (kept as a safety net)
/// </summary>
public class PersistenceService
{
    public string AppDir { get; }
    public string SettingsPath { get; }
    public string ItemsPath { get; }       // v2
    public string LegacySlotsPath { get; } // v1
    public string ImagesDir { get; }

    public PersistenceService()
    {
        AppDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ClipNinja");
        SettingsPath = Path.Combine(AppDir, "settings.json");
        ItemsPath = Path.Combine(AppDir, "items.json");
        LegacySlotsPath = Path.Combine(AppDir, "slots.json");
        ImagesDir = Path.Combine(AppDir, "images");
        Directory.CreateDirectory(AppDir);
        Directory.CreateDirectory(ImagesDir);
    }

    // ── Settings ────────────────────────────────────────────────────────

    public AppSettings LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new AppSettings();
            var json = File.ReadAllText(SettingsPath);
            var s = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
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

    // ── Items (v2 model: separate pinned + recent lists) ────────────────

    public (List<ClipSlot> pinned, List<ClipSlot> recent) LoadItems(AppSettings settings)
    {
        // v2 file present? Load it directly.
        if (File.Exists(ItemsPath))
        {
            try
            {
                var json = File.ReadAllText(ItemsPath);
                var dto = JsonSerializer.Deserialize<ItemsFileDto>(json);
                if (dto is not null)
                {
                    var p = (dto.Pinned ?? new()).Select(DtoToSlot).Where(s => s.Content is not null).ToList();
                    var r = (dto.Recent ?? new()).Select(DtoToSlot).Where(s => s.Content is not null).ToList();
                    return (p, r);
                }
            }
            catch (Exception ex)
            {
                Trace.Log("persist", $"items.json load failed: {ex.Message}");
            }
        }

        // v2 not present — fall back to v1 migration if there's a slots.json.
        if (File.Exists(LegacySlotsPath))
        {
            try
            {
                var migrated = MigrateV1ToV2();
                // Bump SchemaVersion and save settings so we don't keep migrating.
                settings.SchemaVersion = 2;
                SaveSettings(settings);
                // Persist the migrated lists as items.json so v1 file becomes
                // ignored from here on. Rename old file as a safety backup.
                SaveItems(migrated.pinned, migrated.recent);
                try { File.Move(LegacySlotsPath, Path.Combine(AppDir, "slots.v1.bak"), overwrite: true); } catch { }
                Trace.Log("persist", $"v1→v2 migration complete: pinned={migrated.pinned.Count}, recent={migrated.recent.Count}");
                return migrated;
            }
            catch (Exception ex)
            {
                Trace.Log("persist", $"v1→v2 migration FAILED: {ex.Message}");
            }
        }

        // No file at all — fresh install.
        return (new List<ClipSlot>(), new List<ClipSlot>());
    }

    public void SaveItems(IEnumerable<ClipSlot> pinned, IEnumerable<ClipSlot> recent)
    {
        try
        {
            var dto = new ItemsFileDto
            {
                Pinned = pinned.Select(SlotToDto).ToList(),
                Recent = recent.Select(SlotToDto).ToList(),
            };
            var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true });
            var tmp = ItemsPath + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(ItemsPath)) File.Delete(ItemsPath);
            File.Move(tmp, ItemsPath);

            // Orphan sweep: delete PNGs in images\ that no live slot
            // references. Orphans accumulate when items are removed,
            // replaced by annotation (new SavedFileName), or cascade
            // into history (which owns its own copies in a separate
            // directory — see HistoryService.Add). Running the sweep
            // here is safe because the dto lists we JUST serialized are
            // the complete set of live references: SlotToDto assigns a
            // SavedFileName to every image slot that lacked one.
            SweepOrphanedImages(dto);
        }
        catch (Exception ex)
        {
            Trace.Log("persist", $"items.json save failed: {ex.Message}");
        }
    }

    /// <summary>Delete image files not referenced by any live slot.
    /// Failures are logged and non-fatal (a locked file just gets
    /// swept on a later save).</summary>
    private void SweepOrphanedImages(ItemsFileDto liveItems)
    {
        try
        {
            var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // Pinned/Recent are declared nullable on the DTO (they're
            // deserialization targets); in this path we just built the
            // DTO ourselves so they're always set — but iterate null-
            // tolerantly anyway to keep the analyzer honest.
            foreach (var s in liveItems.Pinned ?? new List<SlotDto>()) if (!string.IsNullOrEmpty(s.ImageFile)) referenced.Add(s.ImageFile);
            foreach (var s in liveItems.Recent ?? new List<SlotDto>()) if (!string.IsNullOrEmpty(s.ImageFile)) referenced.Add(s.ImageFile);

            foreach (var path in Directory.EnumerateFiles(ImagesDir, "*.png"))
            {
                var name = Path.GetFileName(path);
                if (referenced.Contains(name)) continue;
                try
                {
                    File.Delete(path);
                    Trace.Log("persist", $"swept orphaned image {name}");
                }
                catch { /* locked or transient — retry naturally next save */ }
            }
        }
        catch (Exception ex)
        {
            Trace.Log("persist", $"orphan sweep failed: {ex.Message}");
        }
    }

    // ── v1 → v2 migration ───────────────────────────────────────────────

    /// <summary>
    /// Read the legacy slots.json (v1 format: flat list of 30 positional
    /// slots) and split into pinned + recent for the v2 model.
    ///
    /// Migration rules (matches what we promised the user):
    ///   • A slot that was IsPinned=true → ends up in Pinned
    ///   • An unpinned slot in positions 16-30 (Memory Hole) WITH content →
    ///     ends up in Pinned (it was explicit "kept" content)
    ///   • An unpinned slot in positions 1-15 WITH content → ends up in Recent
    ///   • Empty slots are dropped
    ///   • Order preservation: Pinned items appear in their old positional
    ///     order; Recent items keep their old position 1-15 order
    /// </summary>
    private (List<ClipSlot> pinned, List<ClipSlot> recent) MigrateV1ToV2()
    {
        var pinned = new List<ClipSlot>();
        var recent = new List<ClipSlot>();

        var json = File.ReadAllText(LegacySlotsPath);
        var dtos = JsonSerializer.Deserialize<List<SlotDto>>(json) ?? new();

        // Old positional indices were 1-based; cascadeLimit was 15.
        const int oldRecentBoundary = 15;

        foreach (var dto in dtos)
        {
            // Skip empty
            if (dto.ContentType == "empty" ||
                (dto.ContentType == "text" && string.IsNullOrEmpty(dto.Text)) ||
                (dto.ContentType == "image" && string.IsNullOrEmpty(dto.ImageFile)))
            {
                continue;
            }

            var slot = DtoToSlot(dto);
            if (slot.Content is null) continue;

            bool wasPinned = dto.IsPinned;
            bool wasMemoryHole = dto.Index > oldRecentBoundary && !wasPinned;

            if (wasPinned || wasMemoryHole)
            {
                slot.IsPinned = true;
                pinned.Add(slot);
            }
            else
            {
                slot.IsPinned = false;
                recent.Add(slot);
            }
        }

        return (pinned, recent);
    }

    // ── DTOs ────────────────────────────────────────────────────────────

    private class ItemsFileDto
    {
        public List<SlotDto>? Pinned { get; set; }
        public List<SlotDto>? Recent { get; set; }
    }

    /// <summary>Wire-format DTO for a single slot. Same shape in v1 and v2;
    /// only the containing file structure changed.</summary>
    private class SlotDto
    {
        public int Index { get; set; }
        public bool IsPinned { get; set; }
        public string Nickname { get; set; } = "";
        public string ContentType { get; set; } = "empty";
        public string Text { get; set; } = "";
        public bool IsSpreadsheet { get; set; }
        public string? HtmlFormat { get; set; }
        public string? RtfFormat { get; set; }
        public List<LinkDto>? Links { get; set; }
        public string ImageFile { get; set; } = "";
        public int ImgWidth { get; set; }
        public int ImgHeight { get; set; }
        public string ImageLabel { get; set; } = "Screenshot";
        public DateTime CapturedAt { get; set; }
    }

    private class LinkDto
    {
        public string Label { get; set; } = "";
        public string Url { get; set; } = "";
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
                dto.HtmlFormat = string.IsNullOrEmpty(tc.HtmlFormat) ? null : tc.HtmlFormat;
                dto.RtfFormat = string.IsNullOrEmpty(tc.RtfFormat) ? null : tc.RtfFormat;
                if (tc.Links.Count > 0)
                {
                    dto.Links = new List<LinkDto>(tc.Links.Count);
                    foreach (var l in tc.Links)
                        dto.Links.Add(new LinkDto { Label = l.Label, Url = l.Url });
                }
                break;
            case ImageContent ic:
                dto.ContentType = "image";
                dto.ImgWidth = ic.OriginalWidth;
                dto.ImgHeight = ic.OriginalHeight;
                dto.ImageLabel = ic.DisplayLabel;
                if (ic.FullImage is not null)
                {
                    string fileName = ic.SavedFileName;
                    var existingPath = string.IsNullOrEmpty(fileName) ? "" : Path.Combine(ImagesDir, fileName);
                    if (string.IsNullOrEmpty(fileName) || !File.Exists(existingPath))
                    {
                        fileName = $"item_{Guid.NewGuid():N}.png";
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
                {
                    IReadOnlyList<HyperLink> links = Array.Empty<HyperLink>();
                    if (dto.Links is { Count: > 0 })
                    {
                        var l = new List<HyperLink>(dto.Links.Count);
                        foreach (var ld in dto.Links) l.Add(new HyperLink(ld.Label, ld.Url));
                        links = l;
                    }
                    slot.Content = new TextContent
                    {
                        Text = dto.Text,
                        CapturedAt = dto.CapturedAt,
                        IsSpreadsheet = dto.IsSpreadsheet,
                        HtmlFormat = string.IsNullOrEmpty(dto.HtmlFormat) ? null : dto.HtmlFormat,
                        RtfFormat = string.IsNullOrEmpty(dto.RtfFormat) ? null : dto.RtfFormat,
                        Links = links,
                    };
                    break;
                }
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
                            SavedFileName = dto.ImageFile,
                        };
                    }
                }
                break;
        }
        return slot;
    }

    // ── Bitmap helpers ──────────────────────────────────────────────────

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
        bi.CacheOption = BitmapCacheOption.OnLoad;
        bi.UriSource = new Uri(path);
        bi.EndInit();
        bi.Freeze();
        return bi;
    }

    public static BitmapSource MakeThumbnail(BitmapSource src, int targetSize)
    {
        if (src.PixelWidth == 0 || src.PixelHeight == 0) return src;

        int longSide = Math.Max(src.PixelWidth, src.PixelHeight);
        if (longSide <= targetSize * 2)
        {
            try { if (src.CanFreeze && !src.IsFrozen) src.Freeze(); return src; } catch { }
        }

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
