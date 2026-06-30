using Theater.Models;
using System.Windows.Media.Imaging;

namespace Theater.Services;

public sealed class CoverCache
{
    private readonly AppStorage _storage;
    private readonly LibraryDatabase _database;
    private readonly Dictionary<string, long> _coverTimestampCache = new(StringComparer.OrdinalIgnoreCase);
    private CoverQualityProfile? _cachedProfile;

    public CoverCache(AppStorage storage, LibraryDatabase database)
    {
        _storage = storage;
        _database = database;
    }

    private CoverQualityProfile ResolveProfile()
    {
        return _cachedProfile ??= CoverQualitySettings.Resolve(_database);
    }

    public BitmapSource? LoadOrCreate(MangaBook book)
    {
        string? sourcePath;

        // Video works: use cover image path (cover.jpg convention)
        if (!string.IsNullOrEmpty(book.CoverImagePath) && File.Exists(book.CoverImagePath))
        {
            sourcePath = book.CoverImagePath;
        }
        // Traditional manga works: use Pages list
        else if (book.Pages.Count > 0)
        {
            var coverIndex = Math.Clamp(book.CoverPageIndex, 0, book.Pages.Count - 1);
            sourcePath = book.Pages[coverIndex];
        }
        else
        {
            return null;
        }

        var profile = ResolveProfile();
        var cachePath = GetCachePath(book, sourcePath, profile);

        if (!File.Exists(cachePath))
        {
            CreateCover(sourcePath, cachePath, profile.CacheDecodeWidth);
        }

        return File.Exists(cachePath) ? ImageLoader.LoadBitmap(cachePath, profile.DisplayDecodeWidth) : null;
    }

    public string GetCacheKey(MangaBook book)
    {
        if (!string.IsNullOrEmpty(book.CoverImagePath) && book.Pages.Count == 0)
        {
            return book.Id + "|" + book.CoverImagePath;
        }

        if (book.Pages.Count == 0)
        {
            return book.Id;
        }

        var coverIndex = Math.Clamp(book.CoverPageIndex, 0, book.Pages.Count - 1);
        var coverPage = book.Pages[coverIndex];
        return GetCachePath(book, coverPage, ResolveProfile());
    }

    private string GetCachePath(MangaBook book, string coverPage, CoverQualityProfile profile)
    {
        if (!_coverTimestampCache.TryGetValue(coverPage, out var modifiedTicks))
        {
            modifiedTicks = File.GetLastWriteTimeUtc(coverPage).Ticks;
            _coverTimestampCache[coverPage] = modifiedTicks;
        }
        var fileName = $"{book.Id}_{book.CoverPageIndex}_{profile.SettingValue}_{modifiedTicks}.png";
        return Path.Combine(_storage.CoverCachePath, fileName);
    }

    private static void CreateCover(string sourcePath, string cachePath, int decodeWidth)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);

        var source = ImageLoader.LoadBitmap(sourcePath, decodeWidth);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));

        using var stream = File.Create(cachePath);
        encoder.Save(stream);
    }

    public void SweepStaleCovers(IEnumerable<MangaBook> books)
    {
        var validSet = new HashSet<string>(
            books.Where(book => book.Pages.Count > 0).Select(GetCacheKey),
            StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var file in Directory.EnumerateFiles(_storage.CoverCachePath, "*.png"))
            {
                if (!validSet.Contains(file))
                {
                    try { File.Delete(file); } catch { }
                }
            }
        }
        catch { }
    }

    public void ClearAll()
    {
        _coverTimestampCache.Clear();
        _cachedProfile = null;
        try
        {
            foreach (var file in Directory.EnumerateFiles(_storage.CoverCachePath, "*.png"))
            {
                try { File.Delete(file); } catch { }
            }
        }
        catch
        {
        }
    }
}
