using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Media.Imaging;
using ClipNinjaV2.Models;

namespace ClipNinjaV2.Services;

/// <summary>
/// Manages ClipNinja's rolling history list — captures that have cascaded
/// off the bottom of the recent area (slot 15) and would otherwise be lost.
/// 
/// Storage model:
/// - In-memory: an ObservableCollection&lt;HistoryItem&gt; for binding
/// - Persistence: history.json next to settings.json in %AppData%\ClipNinja
/// - Image content: each history image's full PNG is in %AppData%\ClipNinja\history\images\
///   and the thumbnail is rebuilt from the loaded full image on read (avoids
///   storing both)
/// 
/// Eviction:
/// - When history exceeds MaxItems, oldest items are removed
/// - Removed image entries also have their PNG file deleted from disk
/// </summary>
public class HistoryService
{
    public ObservableCollection<HistoryItem> Items { get; } = new();

    public int MaxItems { get; set; } = 100;

    private static string AppDataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClipNinja");

    private static string HistoryJsonPath => Path.Combine(AppDataDir, "history.json");
    private static string HistoryImagesDir => Path.Combine(AppDataDir, "history", "images");

    /// <summary>
    /// Add a clip content as the newest history item (inserted at index 0).
    /// Image content is saved to disk on the way in; text content is just held
    /// in memory + serialized to JSON on next Save(). Enforces MaxItems by
    /// evicting from the end of the list.
    /// </summary>
    public void Add(ClipContent content)
    {
        // Image content needs a persisted PNG file so it survives restart.
        // We ALWAYS write a fresh hist_*.png here — even if the content
        // already has a SavedFileName. That name points at the MAIN
        // images\ directory (set when the item was a live slot), but
        // history loads from history\images\ — reusing the old name
        // meant the image silently failed to load after restart. Writing
        // our own copy also keeps the two directories independent, so
        // the main dir's orphan sweep can never break a history item.
        // Mutating SavedFileName is safe: the slot that owned this
        // content is being discarded by the cascade.
        if (content is ImageContent ic && ic.FullImage is not null)
        {
            try
            {
                Directory.CreateDirectory(HistoryImagesDir);
                var fileName = $"hist_{Guid.NewGuid():N}.png";
                var path = Path.Combine(HistoryImagesDir, fileName);
                SaveBitmapAsPng(ic.FullImage, path);
                ic.SavedFileName = fileName;
            }
            catch (Exception ex)
            {
                Trace.Log("history", $"Failed to save history image: {ex.GetType().Name}: {ex.Message}");
            }
        }

        var item = new HistoryItem
        {
            Content = content,
            CapturedAt = DateTime.Now,
        };
        Items.Insert(0, item);

        EvictOverflow();
    }

    /// <summary>
    /// Remove a specific history item. Deletes the underlying image file from
    /// disk if it was an image entry.
    /// </summary>
    public void Remove(HistoryItem item)
    {
        if (item.Content is ImageContent ic && !string.IsNullOrEmpty(ic.SavedFileName))
        {
            TryDeleteHistoryImage(ic.SavedFileName);
        }
        Items.Remove(item);
    }

    /// <summary>Clear all history entries. Deletes every persisted image file.</summary>
    public void ClearAll()
    {
        foreach (var it in Items)
        {
            if (it.Content is ImageContent ic && !string.IsNullOrEmpty(ic.SavedFileName))
                TryDeleteHistoryImage(ic.SavedFileName);
        }
        Items.Clear();
    }

    /// <summary>Trim from the end of the list if we exceeded MaxItems.</summary>
    private void EvictOverflow()
    {
        while (Items.Count > MaxItems)
        {
            var last = Items[^1];
            if (last.Content is ImageContent ic && !string.IsNullOrEmpty(ic.SavedFileName))
                TryDeleteHistoryImage(ic.SavedFileName);
            Items.RemoveAt(Items.Count - 1);
        }
    }

    private static void TryDeleteHistoryImage(string fileName)
    {
        try
        {
            var path = Path.Combine(HistoryImagesDir, fileName);
            if (File.Exists(path)) File.Delete(path);
        }
        catch { /* harmless */ }
    }

    // ── Persistence ─────────────────────────────────────────────────────

    public void Load()
    {
        try
        {
            if (!File.Exists(HistoryJsonPath)) return;
            var json = File.ReadAllText(HistoryJsonPath);
            var dtos = JsonSerializer.Deserialize<List<HistoryItemDto>>(json);
            if (dtos is null) return;
            Items.Clear();
            foreach (var d in dtos)
            {
                var item = DtoToItem(d);
                if (item is not null) Items.Add(item);
            }
        }
        catch (Exception ex)
        {
            Trace.Log("history", $"Load failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppDataDir);
            var dtos = Items.Select(ItemToDto).ToList();
            var json = JsonSerializer.Serialize(dtos, new JsonSerializerOptions { WriteIndented = false });
            File.WriteAllText(HistoryJsonPath, json);
        }
        catch (Exception ex)
        {
            Trace.Log("history", $"Save failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // ── DTOs ────────────────────────────────────────────────────────────

    private class HistoryItemDto
    {
        public string ContentType { get; set; } = "";  // text | image
        public DateTime CapturedAt { get; set; }
        public string Text { get; set; } = "";
        public bool IsSpreadsheet { get; set; }
        public string? HtmlFormat { get; set; }
        public string? RtfFormat { get; set; }
        public List<LinkDto>? Links { get; set; }
        public string ImageFile { get; set; } = "";
        public int ImgWidth { get; set; }
        public int ImgHeight { get; set; }
        public string ImageLabel { get; set; } = "Screenshot";
    }

    private class LinkDto
    {
        public string Label { get; set; } = "";
        public string Url { get; set; } = "";
    }

    private static HistoryItemDto ItemToDto(HistoryItem item)
    {
        var dto = new HistoryItemDto { CapturedAt = item.CapturedAt };
        switch (item.Content)
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
                    foreach (var l in tc.Links) dto.Links.Add(new LinkDto { Label = l.Label, Url = l.Url });
                }
                break;
            case ImageContent ic:
                dto.ContentType = "image";
                dto.ImageFile = ic.SavedFileName;
                dto.ImgWidth = ic.OriginalWidth;
                dto.ImgHeight = ic.OriginalHeight;
                dto.ImageLabel = ic.DisplayLabel;
                break;
        }
        return dto;
    }

    private static HistoryItem? DtoToItem(HistoryItemDto dto)
    {
        switch (dto.ContentType)
        {
            case "text":
                IReadOnlyList<HyperLink> links = Array.Empty<HyperLink>();
                if (dto.Links is { Count: > 0 })
                {
                    var l = new List<HyperLink>(dto.Links.Count);
                    foreach (var ld in dto.Links) l.Add(new HyperLink(ld.Label, ld.Url));
                    links = l;
                }
                return new HistoryItem
                {
                    Content = new TextContent
                    {
                        Text = dto.Text,
                        CapturedAt = dto.CapturedAt,
                        IsSpreadsheet = dto.IsSpreadsheet,
                        HtmlFormat = dto.HtmlFormat,
                        RtfFormat = dto.RtfFormat,
                        Links = links,
                    },
                    CapturedAt = dto.CapturedAt,
                };
            case "image":
                if (!string.IsNullOrEmpty(dto.ImageFile))
                {
                    var path = Path.Combine(HistoryImagesDir, dto.ImageFile);
                    if (File.Exists(path))
                    {
                        var full = LoadBitmapFromPng(path);
                        var thumb = MakeThumbnail(full, 32);
                        return new HistoryItem
                        {
                            Content = new ImageContent
                            {
                                FullImage = full,
                                Thumbnail = thumb,
                                OriginalWidth = dto.ImgWidth > 0 ? dto.ImgWidth : full.PixelWidth,
                                OriginalHeight = dto.ImgHeight > 0 ? dto.ImgHeight : full.PixelHeight,
                                DisplayLabel = string.IsNullOrEmpty(dto.ImageLabel) ? "Screenshot" : dto.ImageLabel,
                                SavedFileName = dto.ImageFile,
                                CapturedAt = dto.CapturedAt,
                            },
                            CapturedAt = dto.CapturedAt,
                        };
                    }
                }
                return null;
            default:
                return null;
        }
    }

    private static void SaveBitmapAsPng(BitmapSource bmp, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        encoder.Save(fs);
    }

    private static BitmapSource LoadBitmapFromPng(string path)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.UriSource = new Uri(path, UriKind.Absolute);
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    private static BitmapSource MakeThumbnail(BitmapSource src, int targetSize)
    {
        // Same logic as ClipboardWatcher.MakeThumbnail — kept as a private
        // copy here to avoid taking a dependency on the watcher just for
        // history loading.
        int longSide = Math.Max(src.PixelWidth, src.PixelHeight);
        if (longSide <= targetSize * 2)
        {
            var clone = new WriteableBitmap(src);
            clone.Freeze();
            return clone;
        }
        double scale = (double)targetSize / longSide;
        var tb = new TransformedBitmap(src, new System.Windows.Media.ScaleTransform(scale, scale));
        tb.Freeze();
        return tb;
    }
}
