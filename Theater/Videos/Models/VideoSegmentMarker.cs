namespace Theater.Videos.Models;

public sealed class VideoSegmentMarker
{
    public long Id { get; set; }
    public string VideoId { get; set; } = "";
    public long TimeMs { get; set; }
    public string Title { get; set; } = "";
    public string Note { get; set; } = "";
    public string ThumbnailPath { get; set; } = "";
    public string Color { get; set; } = "#F97316";
    public string CreatedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";

    public string TimeText
    {
        get
        {
            var ms = Math.Max(0, TimeMs);
            var ts = TimeSpan.FromMilliseconds(ms);
            return ts.TotalHours >= 1
                ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
                : $"{ts.Minutes}:{ts.Seconds:D2}";
        }
    }

    public string DefaultTitleForTime()
    {
        return $"分段 · {TimeText}";
    }
}
