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
        if (ResolveCoverSourcePath(book) is not { } sourcePath)
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
        return ResolveCoverSourcePath(book) is { } sourcePath
            ? GetCachePath(book, sourcePath, ResolveProfile())
            : book.Id;
    }

    private static string? ResolveCoverSourcePath(MangaBook book)
    {
        if (!string.IsNullOrEmpty(book.CoverImagePath) && File.Exists(book.CoverImagePath))
        {
            return book.CoverImagePath;
        }

        if (book.Pages.Count == 0)
        {
            return null;
        }

        var coverIndex = Math.Clamp(book.CoverPageIndex, 0, book.Pages.Count - 1);
        return book.Pages[coverIndex];
    }

    private string GetCachePath(MangaBook book, string coverPage, CoverQualityProfile profile)
    {
        if (!_coverTimestampCache.TryGetValue(coverPage, out var modifiedTicks))
        {
            modifiedTicks = File.GetLastWriteTimeUtc(coverPage).Ticks;
            _coverTimestampCache[coverPage] = modifiedTicks;
        }
        var sourceHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(coverPage))).ToLowerInvariant()[..16];
        var fileName = $"{book.Id}_{sourceHash}_{profile.SettingValue}_{modifiedTicks}.png";
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
            books.Select(GetCacheKey),
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
