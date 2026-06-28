using Theater.Models;
using Theater.Videos.Services;

namespace Theater.Services;

public sealed class LibraryScanner
{
    private readonly NaturalPathComparer _pathComparer = new();

    public List<MangaBook> Scan(
        string rootPath,
        Dictionary<string, MangaBook> savedBooks,
        IProgress<LibraryScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var books = new List<MangaBook>();
        if (!Directory.Exists(rootPath))
        {
            return books;
        }

        var folders = new Queue<string>();
        folders.Enqueue(rootPath);
        var discoveredFolders = 1;
        var completedFolders = 0;

        while (folders.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var folder = folders.Dequeue();
            progress?.Report(new LibraryScanProgress(rootPath, completedFolders, discoveredFolders, books.Count, folder));

            foreach (var childFolder in EnumerateChildFolders(folder, cancellationToken))
            {
                folders.Enqueue(childFolder);
                discoveredFolders++;
            }

            var videoFiles = EnumerateVideos(folder, cancellationToken);
            var imageFiles = EnumerateImages(folder, cancellationToken);
            var coverPath = FindCoverImage(folder);
            var imageSetFolders = EnumerateImageSetFolders(folder, cancellationToken);

            // 合并 V/ 标记目录下的视频和 P/ 标记目录下的图集（VideoTapes 合集约定），
            // 使扫描结果与导入流程（BatchImportAnalyzer）一致，避免标记子目录被当作独立书籍重复入库。
            var vDir = Path.Combine(folder, "V");
            if (Directory.Exists(vDir))
            {
                videoFiles.AddRange(Directory.EnumerateFiles(vDir, "*", SearchOption.AllDirectories)
                    .Where(VideoFileDetector.IsSupportedVideo));
                if (string.IsNullOrEmpty(coverPath))
                    coverPath = FindCoverImage(vDir);
            }
            var pDir = Path.Combine(folder, "P");
            if (Directory.Exists(pDir))
            {
                var pSubs = Directory.EnumerateDirectories(pDir).ToList();
                if (pSubs.Count > 0)
                    imageSetFolders.AddRange(pSubs);
                else
                    imageSetFolders.Add(pDir);
                if (string.IsNullOrEmpty(coverPath))
                    coverPath = FindCoverImage(pDir);
            }

            // A video work exists if there are videos, images, or image sub-folders
            if (videoFiles.Count == 0 && imageFiles.Count == 0 && imageSetFolders.Count == 0)
            {
                completedFolders++;
                progress?.Report(new LibraryScanProgress(rootPath, completedFolders, discoveredFolders, books.Count, folder));
                continue;
            }

            var id = BookId.FromFolderPath(folder);
            savedBooks.TryGetValue(folder, out var saved);

            var book = saved ?? new MangaBook
            {
                Id = id,
                Title = Path.GetFileName(folder),
                Author = TryGetAuthorName(rootPath, folder),
                Tags = "",
                FolderPath = folder,
                ImportedAt = DateTimeOffset.Now.ToString("yyyy-MM-dd"),
            };

            book.Id = id;
            book.FolderPath = folder;
            book.Title = Path.GetFileName(folder);
            book.Author = TryGetAuthorName(rootPath, folder);

            // Video files
            book.VideoPathsJson = System.Text.Json.JsonSerializer.Serialize(videoFiles);
            book.DurationMs = book.DurationMs > 0 ? book.DurationMs : 0; // preserve saved duration

            // Image sets from sub-folders
            book.ImageSetPathsJson = System.Text.Json.JsonSerializer.Serialize(imageSetFolders);

            // Cover image
            book.CoverImagePath = coverPath;

            // Page count = number of image files directly in the folder
            book.PageCount = imageFiles.Count;
            book.TotalBytes = ImageLoader.SumFileBytes(imageFiles) +
                              videoFiles.Sum(path => VideoFileDetector.GetFileSize(path));
            book.CoverPageIndex = Math.Clamp(book.CoverPageIndex, 0, Math.Max(imageFiles.Count - 1, 0));
            book.LastReadPageIndex = Math.Clamp(book.LastReadPageIndex, 0, Math.Max(imageFiles.Count - 1, 0));
            book.IsMissing = false;

            // Still populate Pages for backward compat (manga-style browsing of direct images)
            book.Pages.Clear();
            foreach (var page in imageFiles)
            {
                book.Pages.Add(page);
            }

            // 合集判定：多视频 / 视频+图集 / 多图集 时自动打"合集" tag
            // （仅当用户未自定义 tag 时；数据安全规则：不覆盖手工 tag）
            if (string.IsNullOrWhiteSpace(book.Tags))
            {
                var collectionTag = TryGetCollectionTag(book.VideoCount, book.ImageSetCount);
                if (!string.IsNullOrEmpty(collectionTag))
                {
                    book.Tags = collectionTag;
                }
            }

            AppLogger.Info("scanner", $"Book: {book.Title} | videos={book.VideoCount} images={book.PageCount} sets={book.ImageSetCount} cover={!string.IsNullOrEmpty(book.CoverImagePath)}");
            books.Add(book);

            completedFolders++;
            progress?.Report(new LibraryScanProgress(rootPath, completedFolders, discoveredFolders, books.Count, folder));
        }

        return books.OrderBy(book => book.Author).ThenBy(book => book.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private List<string> EnumerateChildFolders(string folder, CancellationToken cancellationToken)
    {
        try
        {
            return Directory.EnumerateDirectories(folder, "*", SearchOption.TopDirectoryOnly)
                .Where(path =>
                {
                    // V/ 和 P/ 是合集标记目录，内容属于父文件夹，不递归扫描
                    var dirName = Path.GetFileName(path);
                    return !string.Equals(dirName, "V", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(dirName, "P", StringComparison.OrdinalIgnoreCase);
                })
                .Select(path =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return path;
                })
                .OrderBy(path => path, _pathComparer)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException or NotSupportedException)
        {
            return [];
        }
    }

    private List<string> EnumerateImages(string folder, CancellationToken cancellationToken)
    {
        try
        {
            return Directory.EnumerateFiles(folder)
                .Where(ImageLoader.IsSupportedImage)
                .OrderBy(path => path, _pathComparer)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException or NotSupportedException)
        {
            return [];
        }
    }

    private List<string> EnumerateVideos(string folder, CancellationToken cancellationToken)
    {
        try
        {
            var allFiles = Directory.EnumerateFiles(folder).ToList();
            var videos = allFiles
                .Where(VideoFileDetector.IsSupportedVideo)
                .OrderBy(path => path, _pathComparer)
                .ToList();
            if (videos.Count > 0)
            {
                AppLogger.Info("scanner", $"Found {videos.Count} videos in {folder}: {string.Join(", ", videos.Select(Path.GetFileName))}");
            }
            return videos;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException or NotSupportedException)
        {
            AppLogger.Warn("scanner", $"EnumerateVideos failed for {folder}: {ex.Message}");
            return [];
        }
    }

    private static string FindCoverImage(string folder)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(folder))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (string.Equals(name, "cover", StringComparison.OrdinalIgnoreCase)
                    && ImageLoader.IsSupportedImage(file))
                {
                    return file;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException or NotSupportedException)
        {
        }
        return "";
    }

    private List<string> EnumerateImageSetFolders(string folder, CancellationToken cancellationToken)
    {
        var sets = new List<string>();
        try
        {
            foreach (var subDir in Directory.EnumerateDirectories(folder))
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Skip V/ and P/ marker dirs (used by VideoTapes convention, kept for compatibility)
                var dirName = Path.GetFileName(subDir);
                if (string.Equals(dirName, "V", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(dirName, "P", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Check if this subdir contains images
                try
                {
                    var hasImages = Directory.EnumerateFiles(subDir)
                        .Any(ImageLoader.IsSupportedImage);
                    if (hasImages)
                    {
                        sets.Add(subDir);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException or NotSupportedException)
        {
        }
        return sets;
    }

    private static string TryGetAuthorName(string rootPath, string folder)
    {
        var relative = Path.GetRelativePath(rootPath, folder);
        var firstSegment = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).FirstOrDefault();
        return string.IsNullOrWhiteSpace(firstSegment) || firstSegment == "." ? "" : firstSegment;
    }

    /// <summary>
    /// 判定是否合集（多视频 / 视频+图集 / 多图集），是则返回"合集" tag，否则返回空。
    /// 统一用"合集"，对应 TagCatalog 内置预设，避免颜色查询失败。
    /// </summary>
    private static string TryGetCollectionTag(int videoCount, int imageSetCount)
    {
        var hasVideo = videoCount > 0;
        var hasImages = imageSetCount > 0;

        if (hasVideo && hasImages) return "合集";
        if (hasVideo && videoCount > 1) return "合集";
        if (hasImages && imageSetCount > 1) return "合集";
        return "";
    }
}

public readonly record struct LibraryScanProgress(
    string RootPath,
    int CompletedFolders,
    int TotalFolders,
    int BooksFound,
    string CurrentFolder);
