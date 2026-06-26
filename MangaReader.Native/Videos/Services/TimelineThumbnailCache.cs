using LibVLCSharp.Shared;
using MangaReader.Native.Services;
using MangaReader.Native.Videos.Models;

namespace MangaReader.Native.Videos.Services;

public sealed class TimelineThumbnailCache : IDisposable
{
    private const uint SnapshotWidth = 320;
    private static readonly string[] SnapshotOptions =
    [
        "--no-video-title-show",
        "--no-osd",
        "--no-sout-display",
        "--avcodec-hw=any",
        "--file-caching=150"
    ];

    private readonly AppStorage _storage;
    private LibVLC? _libVLC;
    private readonly object _libVLCLock = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    public TimelineThumbnailCache(AppStorage storage)
    {
        _storage = storage;
    }

    public string GetCachePath(VideoItem video, long timeMs)
    {
        long modifiedTicks = 0;
        try
        {
            modifiedTicks = File.GetLastWriteTimeUtc(video.FolderPath).Ticks;
        }
        catch
        {
        }

        var dir = Path.Combine(_storage.Root, "cache", "timeline", video.Id);
        return Path.Combine(dir, $"{timeMs}_{modifiedTicks}.jpg");
    }

    public async Task<string?> GetOrCreateAsync(VideoItem video, long timeMs, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(video.FolderPath) || !File.Exists(video.FolderPath))
        {
            return null;
        }

        var path = GetCachePath(video, timeMs);
        if (File.Exists(path))
        {
            return path;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(path))
            {
                return path;
            }

            await Task.Run(() => CreateThumbnail(video.FolderPath, timeMs, path), cancellationToken).ConfigureAwait(false);
            return File.Exists(path) ? path : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        lock (_libVLCLock)
        {
            _libVLC?.Dispose();
            _libVLC = null;
        }
        _gate.Dispose();
    }

    private void CreateThumbnail(string videoPath, long timeMs, string cachePath)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);

            var libVLC = GetOrCreateLibVLC();
            using var player = new LibVLCSharp.Shared.MediaPlayer(libVLC);
            using var media = new Media(libVLC, videoPath, FromType.FromPath);

            var lengthReady = new ManualResetEventSlim(false);
            var snapshotReady = new ManualResetEventSlim(false);

            player.LengthChanged += (_, _) => lengthReady.Set();
            player.SnapshotTaken += (_, _) => snapshotReady.Set();

            player.Play(media);

            if (!lengthReady.Wait(TimeSpan.FromSeconds(10)))
            {
                player.Stop();
                AppLogger.Warn("timeline-thumb", $"Timeout waiting for length: {videoPath}");
                return;
            }

            var duration = player.Length;
            if (duration <= 0)
            {
                player.Stop();
                return;
            }

            var target = Math.Clamp(timeMs, 0, Math.Max(0, duration - 200));
            player.Time = target;

            Thread.Sleep(300);

            player.TakeSnapshot(0, cachePath, SnapshotWidth, 0);

            if (!snapshotReady.Wait(TimeSpan.FromSeconds(6)))
            {
                AppLogger.Warn("timeline-thumb", $"Timeout waiting for snapshot: {videoPath} @ {timeMs}ms");
            }

            player.Stop();
        }
        catch (Exception ex)
        {
            AppLogger.Warn("timeline-thumb", $"Thumbnail extraction failed for {videoPath} @ {timeMs}ms: {ex.Message}");
        }
    }

    private LibVLC GetOrCreateLibVLC()
    {
        lock (_libVLCLock)
        {
            if (_libVLC != null)
            {
                return _libVLC;
            }

            Core.Initialize();
            _libVLC = new LibVLC(SnapshotOptions);
            return _libVLC;
        }
    }
}
