using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MangaReader.Native.Videos.Models;

/// <summary>
/// Minimal video model for the video player subsystem.
/// Completely separate from MangaBook — no manga code is touched.
/// </summary>
public sealed class VideoItem : INotifyPropertyChanged
{
    private long _lastPositionMs;
    private long _durationMs;
    private string _readingStatus = "unread";
    private int _segmentMarkerCount;

    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Author { get; set; } = "";
    public string FolderPath { get; set; } = "";
    public bool IsMissing { get; set; }

    public long DurationMs
    {
        get => _durationMs;
        set
        {
            _durationMs = Math.Max(0, value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(DurationText));
        }
    }

    public long LastPositionMs
    {
        get => _lastPositionMs;
        set
        {
            _lastPositionMs = Math.Max(0, value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasProgress));
            OnPropertyChanged(nameof(ProgressText));
        }
    }

    public string ReadingStatus
    {
        get => _readingStatus;
        set
        {
            _readingStatus = string.IsNullOrWhiteSpace(value) ? "unread" : value;
            OnPropertyChanged();
        }
    }

    public int SegmentMarkerCount
    {
        get => _segmentMarkerCount;
        set
        {
            _segmentMarkerCount = Math.Max(0, value);
            OnPropertyChanged();
        }
    }

    public string DurationText => FormatDuration(DurationMs);
    public bool HasProgress => LastPositionMs > 0 && DurationMs > 0;
    public string ProgressText => HasProgress
        ? $"{FormatDuration(LastPositionMs)} / {DurationText}"
        : "";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private static readonly PropertyChangedEventArgs AllChangedArgs = new("");

    public void NotifyAll()
    {
        PropertyChanged?.Invoke(this, AllChangedArgs);
    }

    private static string FormatDuration(long ms)
    {
        if (ms <= 0) return "--:--";
        var ts = TimeSpan.FromMilliseconds(ms);
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{ts.Minutes}:{ts.Seconds:D2}";
    }
}
