using Theater.Models;
using System.Windows.Media.Imaging;

namespace Theater.Services;

public sealed class CoverThumbnailPipeline
{
    private const int MaxMemoryCovers = 320;
    private const long MaxMemoryBytes = 64L * 1024 * 1024;
    private readonly CoverCache _coverCache;
    private readonly SemaphoreSlim _loaderGate = new(4);
    private readonly object _syncRoot = new();
    private readonly Dictionary<string, LinkedListNode<CacheEntry>> _memoryCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<CacheEntry> _lru = new();
    private readonly Dictionary<string, Task<BitmapSource?>> _inFlight = new(StringComparer.OrdinalIgnoreCase);
    private long _currentMemoryBytes;
    private long _cacheGeneration;

    public CoverThumbnailPipeline(CoverCache coverCache)
    {
        _coverCache = coverCache;
    }

    public async Task<BitmapSource?> LoadAsync(MangaBook book, CancellationToken cancellationToken = default)
    {
        var hasCoverPath = !string.IsNullOrEmpty(book.CoverImagePath) && File.Exists(book.CoverImagePath);
        if (book.Pages.Count == 0 && !hasCoverPath)
        {
            return null;
        }

        var cacheKey = _coverCache.GetCacheKey(book);
        if (TryGet(cacheKey, out var cached))
        {
            return cached;
        }

        Task<BitmapSource?> task;
        lock (_syncRoot)
        {
            if (!_inFlight.TryGetValue(cacheKey, out var existingTask))
            {
                task = LoadCoreAsync(book, cacheKey, _cacheGeneration, cancellationToken);
                _inFlight[cacheKey] = task;
            }
            else
            {
                task = existingTask;
            }
        }

        try
        {
            return await task.ConfigureAwait(true);
        }
        finally
        {
            lock (_syncRoot)
            {
                if (_inFlight.TryGetValue(cacheKey, out var current) && ReferenceEquals(current, task))
                {
                    _inFlight.Remove(cacheKey);
                }
            }
        }
    }

    private async Task<BitmapSource?> LoadCoreAsync(MangaBook book, string cacheKey, long generation, CancellationToken cancellationToken)
    {
        await _loaderGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var image = _coverCache.LoadOrCreate(book);
                cancellationToken.ThrowIfCancellationRequested();
                if (image is not null)
                {
                    Add(cacheKey, image, generation);
                }
                return image;
            }, cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            _loaderGate.Release();
        }
    }

    private bool TryGet(string key, out BitmapSource? image)
    {
        lock (_syncRoot)
        {
            if (_memoryCache.TryGetValue(key, out var node))
            {
                _lru.Remove(node);
                _lru.AddFirst(node);
                image = node.Value.Image;
                return true;
            }
        }

        image = null;
        return false;
    }

    private void Add(string key, BitmapSource image, long generation)
    {
        var byteSize = EstimateBitmapBytes(image);

        lock (_syncRoot)
        {
            if (generation != _cacheGeneration)
            {
                return;
            }

            if (_memoryCache.TryGetValue(key, out var existing))
            {
                _currentMemoryBytes -= existing.Value.ByteSize;
                existing.Value = new CacheEntry(key, image, byteSize);
                _currentMemoryBytes += byteSize;
                _lru.Remove(existing);
                _lru.AddFirst(existing);
            }
            else
            {
                var node = new LinkedListNode<CacheEntry>(new CacheEntry(key, image, byteSize));
                _lru.AddFirst(node);
                _memoryCache[key] = node;
                _currentMemoryBytes += byteSize;
            }

            while ((_memoryCache.Count > MaxMemoryCovers || _currentMemoryBytes > MaxMemoryBytes) && _lru.Last is not null)
            {
                var last = _lru.Last;
                _lru.RemoveLast();
                _memoryCache.Remove(last.Value.Key);
                _currentMemoryBytes -= last.Value.ByteSize;
            }

            if (_currentMemoryBytes < 0)
            {
                _currentMemoryBytes = 0;
            }
        }
    }

    public void ClearMemoryCache()
    {
        lock (_syncRoot)
        {
            _memoryCache.Clear();
            _lru.Clear();
            _inFlight.Clear();
            _currentMemoryBytes = 0;
            _cacheGeneration++;
        }
    }

    private static long EstimateBitmapBytes(BitmapSource source)
    {
        var bitsPerPixel = source.Format.BitsPerPixel > 0 ? source.Format.BitsPerPixel : 32;
        var stride = ((source.PixelWidth * bitsPerPixel + 31) / 32) * 4;
        return (long)stride * source.PixelHeight;
    }

    private readonly record struct CacheEntry(string Key, BitmapSource Image, long ByteSize);
}
