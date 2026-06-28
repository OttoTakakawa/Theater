using Theater.Models;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Theater.Services;

public sealed class BatchImportAnalyzer
{
    private const int AnalysisDecodePixelWidth = 160;
    private const double LandscapeRatioThreshold = 1.2;
    private const long HighQualityBytes = 2L * 1024 * 1024;
    private const long UltraQualityBytes = 5L * 1024 * 1024;
    private readonly NaturalPathComparer _pathComparer = new();

    public List<BatchImportCandidate> AnalyzeAuthorFolder(string authorFolder)
    {
        if (!Directory.Exists(authorFolder))
        {
            return [];
        }

        return FindBookFolders(authorFolder)
            .Select(CreateCandidate)
            .Where(candidate => candidate.PageCount > 0)
            .OrderBy(candidate => candidate.FolderPath, _pathComparer)
            .ToList();
    }

    public BatchImportCandidate? AnalyzeBookFolder(string bookFolder)
    {
        if (!Directory.Exists(bookFolder))
        {
            return null;
        }

        var hasImages = HasDirectImages(bookFolder);
        var hasVideos = HasDirectVideos(bookFolder);
        if (!hasImages && !hasVideos)
        {
            return null;
        }

        var candidate = CreateCandidate(bookFolder);
        // Allow video-only folders (page count can be 0)
        if (candidate.PageCount == 0 && !hasVideos)
        {
            return null;
        }
        return candidate;
    }

    public BatchImportCandidate? AnalyzeCollectionFolder(string bookFolder)
    {
        if (!Directory.Exists(bookFolder))
        {
            return null;
        }
        var candidate = CreateCandidate(bookFolder);
        // For collections, require at least some content
        if (candidate.PageCount == 0 && candidate.VideoCount == 0)
        {
            return null;
        }
        return candidate;
    }

    private static IEnumerable<string> FindBookFolders(string authorFolder)
    {
        var folders = Directory.EnumerateDirectories(authorFolder, "*", SearchOption.AllDirectories)
            .Where(f => HasDirectImages(f) || HasDirectVideos(f))
            .ToList();

        if (folders.Count == 0 && (HasDirectImages(authorFolder) || HasDirectVideos(authorFolder)))
        {
            folders.Add(authorFolder);
        }

        return folders;
    }

    private static bool HasDirectImages(string folder)
    {
        try
        {
            return Directory.EnumerateFiles(folder).Any(ImageLoader.IsSupportedImage);
        }
        catch
        {
            return false;
        }
    }

    private static bool HasDirectVideos(string folder)
    {
        try
        {
            return Directory.EnumerateFiles(folder).Any(Videos.Services.VideoFileDetector.IsSupportedVideo);
        }
        catch
        {
            return false;
        }
    }

    private static string? FindCoverInFolder(string folder)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return null;
        try
        {
            return Directory.EnumerateFiles(folder)
                .FirstOrDefault(f => string.Equals(Path.GetFileNameWithoutExtension(f), "cover", StringComparison.OrdinalIgnoreCase)
                                  && ImageLoader.IsSupportedImage(f));
        }
        catch
        {
            return null;
        }
    }

    private BatchImportCandidate CreateCandidate(string folder)
    {
        var pages = Directory.EnumerateFiles(folder)
            .Where(ImageLoader.IsSupportedImage)
            .OrderBy(path => path, _pathComparer)
            .ToList();

        // Videos: root + V/ subdirectory (VideoTapes convention)
        var videos = Directory.EnumerateFiles(folder)
            .Where(Videos.Services.VideoFileDetector.IsSupportedVideo)
            .OrderBy(path => path, _pathComparer)
            .ToList();
        var vDir = Path.Combine(folder, "V");
        if (Directory.Exists(vDir))
        {
            videos.AddRange(Directory.EnumerateFiles(vDir, "*", SearchOption.AllDirectories)
                .Where(Videos.Services.VideoFileDetector.IsSupportedVideo)
                .OrderBy(path => path, _pathComparer));
        }

        // Cover: root > V/ > P/
        var cover = FindCoverInFolder(folder)
            ?? FindCoverInFolder(vDir)
            ?? FindCoverInFolder(Path.Combine(folder, "P"));

        // Image sets: P/ subdirectories, or root subfolders (excluding V/, P/)
        var imageSets = new List<string>();
        var pDir = Path.Combine(folder, "P");
        if (Directory.Exists(pDir))
        {
            var pSubs = Directory.EnumerateDirectories(pDir).ToList();
            imageSets.AddRange(pSubs.Count > 0 ? pSubs : [pDir]);
        }
        else
        {
            imageSets.AddRange(Directory.EnumerateDirectories(folder)
                .Where(sub =>
                {
                    var name = Path.GetFileName(sub);
                    if (string.Equals(name, "V", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(name, "P", StringComparison.OrdinalIgnoreCase))
                        return false;
                    try { return Directory.EnumerateFiles(sub).Any(ImageLoader.IsSupportedImage); }
                    catch { return false; }
                }));
        }

        var folderName = Path.GetFileName(folder);
        return new BatchImportCandidate
        {
            FolderPath = folder,
            FolderName = folderName,
            Title = folderName,
            PageCount = pages.Count,
            Pages = pages,
            VideoPaths = videos,
            CoverImagePath = cover ?? "",
            ImageSetPaths = imageSets,
            Tags = ""
        };
    }
}
