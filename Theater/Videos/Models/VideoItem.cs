using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;
using Theater.Services;
using Theater.Models;

namespace Theater.Videos.Models;

public enum ItemType
{
    Video,
    ImageSet,
    Collection
}

public sealed class VideoItem : INotifyPropertyChanged
{
    private const string FallbackTagCategory = "自定义";
    private const string FallbackTagColor = "#E4E6EA";
    private const int MaxCardTagRows = 2;
    private const int MaxCardTagRowUnits = 30;
    private const int MaxCardTagTextUnits = 26;
    private const int CardTagChromeUnits = 4;

    private int _lastReadPageIndex;
    private int _readCount;
    private BitmapSource? _coverImage;
    private string _tags = "";
    private string _readingStatus = "unread";
    private bool _isFavorite;
    private bool _isPrivacyCover;
    private bool _isSelectedForBatch;
    private long _totalBytes;
    private double _rating;
    private int _segmentMarkerCount;
    private int _collectionVideoCount;
    private int _collectionImageSetCount;

    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Author { get; set; } = "";
    public string CharacterName { get; set; } = "";
    public string ForeignName { get; set; } = "";
    public string ProducedAt { get; set; } = "";
    public string ImportedAt { get; set; } = "";
    public string Summary { get; set; } = "";
    public string Tags
    {
        get => _tags;
        set
        {
            _tags = TagService.FormatTags(TagService.ParseTags(value));
            RefreshTagItems();
            OnPropertyChanged();
        }
    }
    public string FolderPath { get; set; } = "";
    public int PageCount { get; set; }
    public long TotalBytes
    {
        get => _totalBytes;
        set
        {
            _totalBytes = Math.Max(0, value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(SizeText));
            OnPropertyChanged(nameof(LibraryMetaText));
        }
    }
    public int CoverPageIndex { get; set; }
    public int VideoStyle { get; set; } = -1;
    public bool IsMissing { get; set; }
    public bool IsHidden { get; set; }
    public bool IsPrivacyCover
    {
        get => _isPrivacyCover;
        set
        {
            if (_isPrivacyCover == value)
            {
                return;
            }

            _isPrivacyCover = value;
            OnPropertyChanged();
        }
    }

    public bool IsFavorite
    {
        get => _isFavorite;
        set
        {
            if (_isFavorite == value)
            {
                return;
            }

            _isFavorite = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FavoriteStarText));
            OnPropertyChanged(nameof(StatusBadgeText));
        }
    }
    public bool IsSelectedForBatch
    {
        get => _isSelectedForBatch;
        set
        {
            if (_isSelectedForBatch == value)
            {
                return;
            }

            _isSelectedForBatch = value;
            OnPropertyChanged();
        }
    }
    public double Rating
    {
        get => _rating;
        set
        {
            var clamped = Math.Clamp(value, 0, 5);
            var quantized = Math.Round(clamped * 2, MidpointRounding.AwayFromZero) / 2;
            if (Math.Abs(_rating - quantized) < 0.0001)
            {
                return;
            }

            _rating = quantized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasRating));
            OnPropertyChanged(nameof(RatingText));
        }
    }

    public bool HasRating => _rating > 0;
    public string RatingText => _rating.ToString("0.#");
    public long DurationMs { get; set; }
    public long LastPositionMs { get; set; }
    public ItemType ItemType { get; set; } = ItemType.Video;
    public string ImagePathsJson { get; set; } = "";
    public string ParentItemId { get; set; } = "";
    public bool IsSigned { get; set; }
    public string Region { get; set; } = "";
    public int SegmentMarkerCount
    {
        get => _segmentMarkerCount;
        set
        {
            var normalized = Math.Max(0, value);
            if (_segmentMarkerCount == normalized)
            {
                return;
            }

            _segmentMarkerCount = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SegmentMarkerCountText));
            OnPropertyChanged(nameof(SegmentMarkerBadgeText));
            OnPropertyChanged(nameof(LibraryMetaText));
        }
    }

    public int CollectionVideoCount
    {
        get => _collectionVideoCount;
        set
        {
            var normalized = Math.Max(0, value);
            if (_collectionVideoCount == normalized)
            {
                return;
            }

            _collectionVideoCount = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasCollectionVideos));
            OnPropertyChanged(nameof(IsMixedCollection));
            OnPropertyChanged(nameof(IsVideoCollection));
            OnPropertyChanged(nameof(IsImageCollection));
            OnPropertyChanged(nameof(CollectionKindText));
            OnPropertyChanged(nameof(CollectionCountsText));
            OnPropertyChanged(nameof(LibraryMetaText));
        }
    }

    public int CollectionImageSetCount
    {
        get => _collectionImageSetCount;
        set
        {
            var normalized = Math.Max(0, value);
            if (_collectionImageSetCount == normalized)
            {
                return;
            }

            _collectionImageSetCount = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasCollectionImageSets));
            OnPropertyChanged(nameof(IsMixedCollection));
            OnPropertyChanged(nameof(IsVideoCollection));
            OnPropertyChanged(nameof(IsImageCollection));
            OnPropertyChanged(nameof(CollectionKindText));
            OnPropertyChanged(nameof(CollectionCountsText));
            OnPropertyChanged(nameof(LibraryMetaText));
        }
    }
    public List<string> Images { get; } = [];
    public List<string> Pages { get; } = [];
    public List<string> ImageSetPaths { get; } = []; // folder paths for image galleries
    public List<string> VideoPaths { get; } = [];     // all video file paths in this work
    public ObservableCollection<TagChip> TagItems { get; } = [];
    public ObservableCollection<TagChip> CardTagItems { get; } = [];

    public bool IsVideo => ItemType == ItemType.Video;
    public bool IsImageSet => ItemType == ItemType.ImageSet;
    public bool IsCollection => ItemType == ItemType.Collection;
    public bool IsCollectionChild => !string.IsNullOrEmpty(ParentItemId);
    public bool HasCollectionVideos => CollectionVideoCount > 0;
    public bool HasCollectionImageSets => CollectionImageSetCount > 0;
    public bool IsMixedCollection => IsCollection && HasCollectionVideos && HasCollectionImageSets;
    public bool IsVideoCollection => IsCollection && HasCollectionVideos && !HasCollectionImageSets;
    public bool IsImageCollection => IsCollection && !HasCollectionVideos && HasCollectionImageSets;

    public int LastReadPageIndex
    {
        get => _lastReadPageIndex;
        set
        {
            _lastReadPageIndex = Math.Clamp(value, 0, Math.Max(PageCount - 1, 0));
            OnPropertyChanged();
            OnPropertyChanged(nameof(ProgressText));
            OnPropertyChanged(nameof(ReadingMetaText));
        }
    }

    public int ReadCount
    {
        get => _readCount;
        set
        {
            _readCount = Math.Max(0, value);
            if (_readCount > 0 && _readingStatus == "unread")
            {
                _readingStatus = "reading";
                OnPropertyChanged(nameof(ReadingStatus));
                OnPropertyChanged(nameof(ReadingStatusText));
                OnPropertyChanged(nameof(StatusBadgeText));
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(ReadCountText));
            OnPropertyChanged(nameof(ReadCountBadgeText));
            OnPropertyChanged(nameof(ReadStateText));
            OnPropertyChanged(nameof(ReadingMetaText));
        }
    }

    public string ReadingStatus
    {
        get => _readingStatus;
        set
        {
            var normalized = NormalizeReadingStatus(value);
            _readingStatus = _readCount > 0 && normalized == "unread" ? "reading" : normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ReadingStatusText));
            OnPropertyChanged(nameof(StatusBadgeText));
            OnPropertyChanged(nameof(ReadStateText));
            OnPropertyChanged(nameof(ReadingMetaText));
        }
    }

    public BitmapSource? CoverImage
    {
        get => _coverImage;
        set
        {
            _coverImage = value;
            OnPropertyChanged();
        }
    }

    public string ProgressText => DurationMs > 0
        ? $"{FormatMs(LastPositionMs)} / {FormatMs(DurationMs)}"
        : PageCount <= 0 ? "0 / 0" : $"{LastReadPageIndex + 1} / {PageCount}";
    public string PageCountText => ItemType switch
    {
        ItemType.Video => DurationMs > 0 ? FormatMs(DurationMs) : "0",
        ItemType.ImageSet => $"{PageCount}张",
        ItemType.Collection => $"{PageCount}个分集",
        _ => "0"
    };
    public string SizeText => FormatSize(TotalBytes);
    public string ReadCountText => ReadCount <= 0 ? "未播放记录" : $"播放 {ReadCount} 次";
    public string ReadCountBadgeText => ReadCount <= 0 ? "" : ReadCountText;
    public string SegmentMarkerCountText => SegmentMarkerCount <= 0 ? "无分段标记" : $"分段标记 {SegmentMarkerCount} 个";
    public string SegmentMarkerBadgeText => SegmentMarkerCount <= 0 ? "" : $"标记 {SegmentMarkerCount}";
    public string ReadStateText => ReadCount > 0 ? $"播放 {ReadCount} 次" : ReadingStatusText;
    public string ReadingMetaText => $"{ReadStateText} · {ProgressText}";
    public string LibraryMetaText => IsCollection
        ? $"{CollectionKindText} · {CollectionCountsText} · {SizeText}"
        : SegmentMarkerCount > 0
            ? $"{ReadStateText} · {PageCountText} · {SizeText} · 标记 {SegmentMarkerCount}"
            : $"{ReadStateText} · {PageCountText} · {SizeText}";
    public string CollectionKindText => IsCollection switch
    {
        false => "",
        true when IsMixedCollection => "视频+图片合集",
        true when IsVideoCollection => "视频合集",
        true when IsImageCollection => "图片合集",
        _ => "合集"
    };
    public string CollectionCountsText
    {
        get
        {
            if (!IsCollection)
            {
                return "";
            }

            var parts = new List<string>();
            if (CollectionVideoCount > 0)
            {
                parts.Add($"{CollectionVideoCount} 个视频");
            }
            if (CollectionImageSetCount > 0)
            {
                parts.Add($"{CollectionImageSetCount} 个图片集");
            }

            return parts.Count == 0 ? $"{PageCount} 个分集" : string.Join(" · ", parts);
        }
    }

    public string ReadingStatusText => ReadingStatus switch
    {
        "reading" => "播放中",
        _ => "未播放"
    };
    public string StatusBadgeText => ReadingStatusText;
    public string FavoriteStarText => IsFavorite ? "★" : "";
    public string MissingText => IsMissing ? "路径失效" : "";
    public string HiddenText => IsHidden ? "已隐藏" : "";
    public static readonly string[] StyleNames = ["统一横幅"];
    public string StyleName => StyleNames[0];
    public int VideoStyleIndex => 0;
    public double VideoWidth => VideoStyleIndex switch
    {
        1 => 138,
        2 => 154,
        3 => 132,
        4 => 150,
        _ => 146
    };
    public double VideoHeight => VideoStyleIndex switch
    {
        1 => 214,
        2 => 198,
        3 => 206,
        4 => 218,
        _ => 206
    };
    public double SpineWidth => VideoStyleIndex switch
    {
        1 => 10,
        2 => 28,
        3 => 0,
        4 => 14,
        _ => 18
    };
    public double VideoTilt => VideoStyleIndex switch
    {
        _ => 0
    };
    public string VideoAccentColor => VideoStyleIndex switch
    {
        1 => "#D7A86E",
        2 => "#8DA7BE",
        3 => "#CDB7A0",
        4 => "#B7A0CD",
        _ => "#D8CABA"
    };

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

    public void AddTag(string tag)
    {
        var names = TagService.ParseTags(Tags).ToList();
        if (names.Any(name => string.Equals(name, tag, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }
        names.Add(tag);
        Tags = TagService.FormatTags(names);
    }

    public void CycleVideoStyle()
    {
        VideoStyle = 0;
        OnPropertyChanged(nameof(VideoStyle));
        OnPropertyChanged(nameof(VideoStyleIndex));
        OnPropertyChanged(nameof(VideoWidth));
        OnPropertyChanged(nameof(VideoHeight));
        OnPropertyChanged(nameof(SpineWidth));
        OnPropertyChanged(nameof(VideoTilt));
        OnPropertyChanged(nameof(VideoAccentColor));
    }

    private void RefreshTagItems()
    {
        RefreshTagItems(_ => FallbackTagCategory, _ => FallbackTagColor);
    }

    public void RefreshTagItems(
        Func<string, string> categoryResolver,
        Func<string, string> colorResolver,
        Func<string, bool>? includeTag = null)
    {
        TagItems.Clear();
        CardTagItems.Clear();

        var tags = TagService.ParseTags(_tags)
            .Where(tag => includeTag?.Invoke(tag) ?? true)
            .ToList();
        foreach (var tag in tags)
        {
            TagItems.Add(new TagChip
            {
                Name = tag,
                Category = categoryResolver(tag),
                Color = colorResolver(tag)
            });
        }

        RefreshCardTagItems(tags, categoryResolver, colorResolver);
        OnPropertyChanged(nameof(CardTagItems));
    }

    private void RefreshCardTagItems(
        IReadOnlyList<string> tags,
        Func<string, string> categoryResolver,
        Func<string, string> colorResolver)
    {
        if (tags.Count == 0)
        {
            return;
        }

        var rows = new int[MaxCardTagRows];
        var visible = new List<(TagChip Chip, int Row, int Units)>();

        foreach (var tag in tags)
        {
            if (!TryPlaceCardTag(tag, rows, out var row, out var units))
            {
                continue;
            }

            visible.Add((new TagChip
            {
                Name = tag,
                Category = categoryResolver(tag),
                Color = colorResolver(tag)
            }, row, units));
        }

        var hiddenCount = tags.Count - visible.Count;
        if (hiddenCount > 0)
        {
            while (visible.Count > 0 && !CanPlaceCardSummary(rows))
            {
                RemoveCardTagPlacement(rows, visible[^1].Row, visible[^1].Units);
                visible.RemoveAt(visible.Count - 1);
                hiddenCount++;
            }
        }

        foreach (var item in visible)
        {
            CardTagItems.Add(item.Chip);
        }

        if (hiddenCount > 0)
        {
            CardTagItems.Add(new TagChip
            {
                Name = $"+{hiddenCount}",
                Category = FallbackTagCategory,
                Color = FallbackTagColor
            });
        }
    }

    private static bool TryPlaceCardTag(string tag, int[] rows, out int row, out int units)
    {
        row = -1;
        var textUnits = CountCardTagTextUnits(tag);
        units = textUnits + CardTagChromeUnits;

        if (textUnits > MaxCardTagTextUnits)
        {
            return false;
        }

        if (units <= MaxCardTagRowUnits)
        {
            for (var i = 0; i < rows.Length; i++)
            {
                if (rows[i] + units <= MaxCardTagRowUnits)
                {
                    rows[i] += units;
                    row = i;
                    return true;
                }
            }

            return false;
        }

        if (rows.Any(value => value > 0))
        {
            return false;
        }

        rows[0] = MaxCardTagRowUnits;
        rows[1] = Math.Min(MaxCardTagRowUnits, units - MaxCardTagRowUnits);
        return true;
    }

    private static bool CanPlaceCardSummary(int[] rows)
    {
        const int summaryUnits = 6;
        return rows.Any(rowUnits => rowUnits + summaryUnits <= MaxCardTagRowUnits);
    }

    private static void RemoveCardTagPlacement(int[] rows, int row, int units)
    {
        if (row < 0)
        {
            Array.Clear(rows);
            return;
        }

        rows[row] = Math.Max(0, rows[row] - units);
    }

    private static int CountCardTagTextUnits(string text)
    {
        var units = 0;
        foreach (var c in text)
        {
            units += c <= 0x007F ? 1 : 2;
        }

        return units;
    }

    private static string NormalizeReadingStatus(string status)
    {
        return status switch
        {
            "reading" or "finished" or "paused" => "reading",
            _ => "unread"
        };
    }

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0)
        {
            return "0MB";
        }

        const double mb = 1024d * 1024d;
        const double gb = 1024d * 1024d * 1024d;
        return bytes >= gb
            ? $"{bytes / gb:0.##}G"
            : $"{Math.Max(1, bytes / mb):0.#}MB";
    }

    private static string FormatMs(long ms)
    {
        if (ms < 0)
        {
            ms = 0;
        }

        var ts = TimeSpan.FromMilliseconds(ms);
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{ts.Minutes}:{ts.Seconds:D2}";
    }
}
