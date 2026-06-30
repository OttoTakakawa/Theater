using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media.Imaging;
using Theater.Services;

namespace Theater.Models;

public sealed class MangaBook : INotifyPropertyChanged
{
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
    private string _lastOpenedAt = "";
    private long _durationMs;
    private long _lastPositionMs;
    private string _videoPathsJson = "[]";
    private string _imageSetPathsJson = "[]";
    private string _coverImagePath = "";

    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Author { get; set; } = "";
    public string CharacterName { get; set; } = "";
    public string ForeignName { get; set; } = "";
    public string ProducedAt { get; set; } = "";
    public string ImportedAt { get; set; } = "";
    public string LastOpenedAt
    {
        get => _lastOpenedAt;
        set
        {
            _lastOpenedAt = value;
            OnPropertyChanged();
        }
    }
    public string Summary { get; set; } = "";
    public bool IsSummaryLoaded { get; set; }
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
            OnPropertyChanged(nameof(VideoCardMetaText));
        }
    }
    public int CoverPageIndex { get; set; }
    public int BookStyle { get; set; } = -1;
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
            OnPropertyChanged(nameof(VideoRatingBadgeText));
        }
    }

    public bool HasRating => _rating > 0;
    public string RatingText => _rating.ToString("0.#");
    public string VideoRatingBadgeText => HasRating ? $"★ {RatingText}" : "";
    public string RatingStarsText
    {
        get
        {
            var stars = _rating <= 0 ? 0 : Math.Clamp((int)Math.Round(_rating), 0, 5);
            return new string('★', stars) + new string('☆', 5 - stars);
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
    public List<string> Pages { get; } = [];
    public List<string> VideoPaths { get; } = [];
    public List<string> ImageSetPaths { get; } = [];
    public RangeObservableCollection<TagChip> TagItems { get; } = [];
    public RangeObservableCollection<TagChip> CardTagItems { get; } = [];

    // ── Video fields ──────────────────────────────────────────────────

    public string VideoPathsJson
    {
        get => _videoPathsJson;
        set
        {
            _videoPathsJson = value;
            VideoPaths.Clear();
            VideoPaths.AddRange(ParseJsonArray(value));
            OnPropertyChanged();
            OnPropertyChanged(nameof(VideoCount));
            OnPropertyChanged(nameof(HasVideo));
            OnPropertyChanged(nameof(VideoMissingText));
            OnPropertyChanged(nameof(VideoCardVisibility));
            OnPropertyChanged(nameof(MangaCardVisibility));
            OnPropertyChanged(nameof(BookWidth));
            OnPropertyChanged(nameof(BookHeight));
            OnPropertyChanged(nameof(CoverWidth));
            OnPropertyChanged(nameof(CoverHeight));
            OnPropertyChanged(nameof(VideoCountText));
            OnPropertyChanged(nameof(LibraryMetaText));
            OnPropertyChanged(nameof(VideoCardMetaText));
        }
    }

    public string ImageSetPathsJson
    {
        get => _imageSetPathsJson;
        set
        {
            _imageSetPathsJson = value;
            ImageSetPaths.Clear();
            ImageSetPaths.AddRange(ParseJsonArray(value));
            OnPropertyChanged();
            OnPropertyChanged(nameof(ImageSetCount));
            OnPropertyChanged(nameof(HasImages));
        }
    }

    public string CoverImagePath
    {
        get => _coverImagePath;
        set
        {
            _coverImagePath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasCover));
        }
    }

    public long DurationMs
    {
        get => _durationMs;
        set
        {
            _durationMs = Math.Max(0, value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(VideoDurationText));
            OnPropertyChanged(nameof(LibraryMetaText));
            OnPropertyChanged(nameof(VideoCardMetaText));
        }
    }

    public long LastPositionMs
    {
        get => _lastPositionMs;
        set
        {
            _lastPositionMs = Math.Max(0, value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(VideoProgressText));
        }
    }

    public int VideoCount => VideoPaths.Count;
    public int ImageSetCount => ImageSetPaths.Count;
    public bool HasVideo => VideoCount > 0;
    public bool HasImages => ImageSetCount > 0;
    public bool HasCover => !string.IsNullOrEmpty(CoverImagePath);
    public string VideoMissingText => !HasVideo ? "视频暂时不见了" : "";
    public string VideoDurationText => DurationMs > 0 ? FormatVideoDuration(DurationMs) : "";
    public string VideoProgressText => LastPositionMs > 0 && DurationMs > 0
        ? $"{FormatVideoDuration(LastPositionMs)} / {VideoDurationText}"
        : "";
    public string VideoCountText => HasVideo ? $"{VideoCount}个视频" : "";
    public string ImageCountText => HasImages ? $"{ImageSetCount}个图集" : "";
    public bool IsCollection => VideoCount > 1 || (HasVideo && HasImages) || (HasImages && ImageSetCount > 1);

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
            OnPropertyChanged(nameof(WorkCategoryText));
            OnPropertyChanged(nameof(VideoCardMetaText));
        }
    }

    public string ProgressText => PageCount <= 0 ? "0 / 0" : $"{LastReadPageIndex + 1} / {PageCount}";
    public string PageCountText => $"{PageCount}页";
    public string SizeText => FormatSize(TotalBytes);
    public string DisplayTitle => CleanDisplayTitle(Title);

    /// <summary>
    /// 从 tags 中提取"作品"分类下的值（日本AV/国产/欧美），用于卡片信息行。
    /// </summary>
    public string WorkCategoryText
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Tags))
                return "";
            foreach (var part in Tags.Split([',', '，', ';', '；'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var p = part.AsSpan().Trim();
                if (p.Equals("日本AV", StringComparison.OrdinalIgnoreCase) ||
                    p.Equals("国产", StringComparison.OrdinalIgnoreCase) ||
                    p.Equals("欧美", StringComparison.OrdinalIgnoreCase))
                {
                    // 日本AV → 日本，国产 → 国产，欧美 → 欧美
                    return p.EndsWith("AV", StringComparison.OrdinalIgnoreCase) ? p[..^2].ToString() : p.ToString();
                }
            }
            return "";
        }
    }
    public string ReadCountText => ReadCount <= 0
        ? (HasVideo ? "未标记看过" : "未标记读过")
        : (HasVideo ? $"看过 {ReadCount} 次" : $"读过 {ReadCount} 次");
    public string ReadCountBadgeText => ReadCount <= 0 ? "" : ReadCountText;
    public string ReadStateText => ReadCount > 0
        ? (HasVideo ? $"看过 {ReadCount} 次" : $"读过 {ReadCount} 次")
        : ReadingStatusText;
    public string ReadingMetaText => $"{ReadStateText} · {ProgressText}";
    public string ContinueActionText => HasVideo ? "继续观看" : "继续阅读";
    public string VideoCardMetaText
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(SizeText))
            {
                parts.Add(SizeText);
            }

            if (!string.IsNullOrWhiteSpace(WorkCategoryText))
            {
                parts.Add(WorkCategoryText);
            }

            if (!string.IsNullOrWhiteSpace(VideoDurationText))
            {
                parts.Add(VideoDurationText);
            }

            return string.Join(" · ", parts);
        }
    }
    public string LibraryMetaText => HasVideo
        ? BuildVideoLibraryMetaText()
        : $"{ReadStateText} · {PageCountText} · {SizeText}";
    public string LibraryMetaSubText => HasVideo
        ? (DurationMs > 0 ? VideoDurationText : "未播放") + (HasImages ? " · 📷" : "")
        : "";

    /// <summary>
    /// 视频卡片底部元信息：合集时显示"合集 · X个视频 · Y图集 · ZZ GB"，
    /// 单视频时只显示"🎬 ZZ GB"（不显示视频数和图集数）。
    /// </summary>
    private string BuildVideoLibraryMetaText()
    {
        var isCollection = VideoCount > 1 || HasImages;
        if (!isCollection)
        {
            return SizeText;
        }

        var parts = new List<string> { VideoCountText };
        if (HasImages)
        {
            parts.Add(ImageCountText);
        }
        parts.Add(SizeText);
        return "合集 · " + string.Join(" · ", parts);
    }
    public string ReadingStatusText => ReadingStatus switch
    {
        "reading" => HasVideo ? "在看" : "在读",
        _ => HasVideo ? "未看" : "未读"
    };
    public string StatusBadgeText => ReadingStatusText;
    public string FavoriteStarText => IsFavorite ? "★" : "";
    public string MissingText => IsMissing ? "路径失效" : "";
    public string HiddenText => IsHidden ? "已隐藏" : "";
    public static readonly string[] StyleNames = ["书脊卡片", "纯图卡片", "圆角卡片"];
    public string StyleName => StyleNames[BookStyleIndex];
    public int BookStyleIndex => BookStyle >= 0 ? BookStyle % 3 : (Id.GetHashCode() & 0x7FFFFFFF) % 3;
    public double BookWidth => HasVideo ? 322 : BookStyleIndex switch
    {
        0 => 138,
        1 => 154,
        2 => 132,
        _ => 138
    };
    public double BookHeight => HasVideo ? 360 : BookStyleIndex switch
    {
        0 => 214,
        1 => 198,
        2 => 206,
        _ => 214
    };
    public double CoverWidth => HasVideo ? 322 : (BookStyleIndex == 0 ? 150 : BookStyleIndex == 1 ? 154 : 140);
    public double CoverHeight => HasVideo ? 181 : (BookStyleIndex == 0 ? 214 : BookStyleIndex == 1 ? 198 : 204);
    public double SpineWidth => HasVideo ? 0 : BookStyleIndex switch
    {
        0 => 10,
        1 => 28,
        2 => 0,
        _ => 10
    };
    public double BookTilt => 0;
    public string BookAccentColor => HasVideo ? "#1E293B" : BookStyleIndex switch
    {
        0 => "#D7A86E",
        1 => "#8DA7BE",
        2 => "#CDB7A0",
        _ => "#D7A86E"
    };
    public Visibility VideoCardVisibility => HasVideo ? Visibility.Visible : Visibility.Collapsed;
    public Visibility MangaCardVisibility => HasVideo ? Visibility.Collapsed : Visibility.Visible;

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

    public void CycleBookStyle()
    {
        BookStyle = (BookStyleIndex + 1) % 3;
        OnPropertyChanged(nameof(BookStyle));
        OnPropertyChanged(nameof(BookStyleIndex));
        OnPropertyChanged(nameof(BookWidth));
        OnPropertyChanged(nameof(BookHeight));
        OnPropertyChanged(nameof(SpineWidth));
        OnPropertyChanged(nameof(BookTilt));
        OnPropertyChanged(nameof(BookAccentColor));
    }

    private void RefreshTagItems()
    {
        TagItems.Clear();
        CardTagItems.Clear();

        var tags = TagService.ParseTags(_tags).ToList();
        var tagChips = new List<TagChip>(tags.Count);
        foreach (var tag in tags)
        {
            tagChips.Add(new TagChip
            {
                Name = tag,
                Color = TagColor(tag),
                Foreground = TagService.GetTextColor(tag)
            });
        }
        TagItems.AddRange(tagChips);

        RefreshCardTagItems(tags);
        OnPropertyChanged(nameof(CardTagItems));
    }

    private void RefreshCardTagItems(IReadOnlyList<string> tags)
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
                Color = TagColor(tag),
                Foreground = TagService.GetTextColor(tag)
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

        var visibleChips = new List<TagChip>(visible.Count);
        foreach (var item in visible)
        {
            visibleChips.Add(item.Chip);
        }
        CardTagItems.AddRange(visibleChips);

        if (hiddenCount > 0)
        {
            CardTagItems.Add(new TagChip
            {
                Name = $"+{hiddenCount}",
                Color = "#E5E7EB",
                Foreground = "#111827"
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

    private static string TagColor(string tag)
    {
        return TagService.GetColor(tag);
    }

    private static string NormalizeReadingStatus(string status)
    {
        return status switch
        {
            "reading" or "finished" or "paused" => "reading",
            _ => "unread"
        };
    }

    public static string FormatSize(long bytes)
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

    private static string FormatVideoDuration(long ms)
    {
        if (ms <= 0) return "";
        var ts = TimeSpan.FromMilliseconds(ms);
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{ts.Minutes}:{ts.Seconds:D2}";
    }

    private static string CleanDisplayTitle(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var text = value.Trim();
        while (text.StartsWith("[", StringComparison.Ordinal) && text.Contains(']'))
        {
            var end = text.IndexOf(']');
            if (end < 0 || end >= Math.Min(text.Length - 1, 48))
            {
                break;
            }

            var bracketText = text[1..end];
            if (!bracketText.Contains('.', StringComparison.Ordinal) && !bracketText.Contains("www", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            text = text[(end + 1)..].TrimStart('_', '-', ' ', '.');
        }

        var extension = Path.GetExtension(text);
        if (!string.IsNullOrEmpty(extension) && extension.Length <= 6)
        {
            text = text[..^extension.Length];
        }

        text = text.Replace('_', ' ').Replace('.', ' ').Replace('-', ' ');
        var compacted = string.Join(" ", text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return string.IsNullOrWhiteSpace(compacted) ? value.Trim() : compacted;
    }

    private static IEnumerable<string> ParseJsonArray(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "[]") return [];
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<string[]>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public string ToVideoPathsJson() => System.Text.Json.JsonSerializer.Serialize(VideoPaths);
    public string ToImageSetPathsJson() => System.Text.Json.JsonSerializer.Serialize(ImageSetPaths);
}
