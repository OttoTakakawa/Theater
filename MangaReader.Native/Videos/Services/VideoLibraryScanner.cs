using MangaReader.Native.Videos.Models;
using MangaReader.Native.Videos.Services;

namespace MangaReader.Native.Videos;

/// <summary>
/// Scans directories recursively to find video files and creates VideoItems.
/// </summary>
public sealed class VideoLibraryScanner
{
    public List<VideoItem> ScanDirectory(string rootPath, IProgress<string>? progress = null)
    {
        var videos = new List<VideoItem>();

        if (!Directory.Exists(rootPath))
            return videos;

        ScanRecursive(rootPath, rootPath, videos, progress);
        return videos;
    }

    private void ScanRecursive(string rootPath, string currentDir, List<VideoItem> videos, IProgress<string>? progress)
    {
        try
        {
            var files = Directory.EnumerateFiles(currentDir);
            foreach (var file in files)
            {
                if (!VideoFileDetector.IsSupportedVideo(file)) continue;

                var relativeDir = Path.GetRelativePath(rootPath, currentDir);
                var author = ExtractAuthor(rootPath, currentDir);

                var video = new VideoItem
                {
                    Id = ComputeId(file),
                    Title = Path.GetFileNameWithoutExtension(file),
                    Author = author,
                    FolderPath = file,
                    DurationMs = 0, // Will be populated on first play
                };

                videos.Add(video);
            }

            foreach (var dir in Directory.EnumerateDirectories(currentDir))
            {
                try
                {
                    ScanRecursive(rootPath, dir, videos, progress);
                }
                catch (UnauthorizedAccessException) { }
            }
        }
        catch (UnauthorizedAccessException) { }
    }

    private static string ExtractAuthor(string rootPath, string folderPath)
    {
        var relative = Path.GetRelativePath(rootPath, folderPath);
        if (string.IsNullOrEmpty(relative) || relative == ".") return "";
        var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Length > 0 ? parts[0] : "";
    }

    private static string ComputeId(string filePath)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var normalized = Path.GetFullPath(filePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToLowerInvariant();
        var hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
    }
}
