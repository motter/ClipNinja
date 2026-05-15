namespace ClipNinjaV2.Models;

/// <summary>App-wide user preferences. Serialized to settings.json.</summary>
public class AppSettings
{
    public int SlotCount { get; set; } = 30;
    public bool PlainTextMode { get; set; } = false;
    public bool ExcelAwarePaste { get; set; } = true;
    public bool ShowNinja { get; set; } = true;
    public int CycleWindowMs { get; set; } = 1500;
    public bool ShowPlainTextHint { get; set; } = true;
    public bool ShowTrayHint { get; set; } = true;
    /// <summary>Launch ClipNinja automatically when Windows starts.</summary>
    public bool LaunchOnStartup { get; set; } = false;

    public void ClampToValidRanges()
    {
        if (SlotCount < 5) SlotCount = 5;
        if (SlotCount > 30) SlotCount = 30;
        if (CycleWindowMs < 200) CycleWindowMs = 200;
        if (CycleWindowMs > 10_000) CycleWindowMs = 10_000;
    }
}
