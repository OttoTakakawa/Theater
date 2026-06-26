using MangaReader.Native.Services;
using MangaReader.Native.Videos.Models;
using MangaReader.Native.Videos.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;

namespace MangaReader.Native.Videos;

/// <summary>
/// Collection browser — shows image sets and videos from a folder in separate tabs.
/// Completely independent from the manga subsystem.
/// </summary>
public partial class CollectionWindow : Window
{
    private readonly string _folderPath;
    private readonly VideoDatabase _db;
    private readonly AppStorage _storage;
    private readonly ObservableCollection<ImageFileItem> _images = new();
    private readonly ObservableCollection<VideoItem> _videos = new();

    // Supported image extensions (mirrored from ImageLoader, keeping separate)
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif", ".tif", ".tiff"
    };

    public CollectionWindow(string folderPath, VideoDatabase db, AppStorage storage)
    {
        InitializeComponent();
        _folderPath = folderPath;
        _db = db;
        _storage = storage;

        ImageGalleryPanel.ItemsSource = _images;
        VideoListPanel.ItemsSource = _videos;

        FolderTitle.Text = Path.GetFileName(folderPath);
    }

    private async void CollectionWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await ScanFolderAsync();
    }

    private async Task ScanFolderAsync()
    {
        try
        {
            StatusText.Text = "正在扫描合集...";
            var imageFiles = new List<ImageFileItem>();
            var videoFiles = new List<VideoItem>();

            await Task.Run(() =>
            {
                if (!Directory.Exists(_folderPath)) return;

                // Check V/ and P/ subdirectories (VideoTapes convention)
                var vDir = Path.Combine(_folderPath, "V");
                var pDir = Path.Combine(_folderPath, "P");

                if (Directory.Exists(vDir))
                    ScanVideoDir(vDir, videoFiles);
                else
                    ScanVideoDir(_folderPath, videoFiles);

                if (Directory.Exists(pDir))
                {
                    // Each subfolder in P/ is an image set
                    foreach (var subDir in Directory.EnumerateDirectories(pDir))
                    {
                        var images = Directory.EnumerateFiles(subDir)
                            .Where(f => ImageExtensions.Contains(Path.GetExtension(f)))
                            .Select(f => new ImageFileItem(f))
                            .ToList();
                        imageFiles.AddRange(images);
                    }
                    // If P/ itself has images directly (no subfolders)
                    if (!Directory.EnumerateDirectories(pDir).Any())
                    {
                        imageFiles.AddRange(
                            Directory.EnumerateFiles(pDir)
                                .Where(f => ImageExtensions.Contains(Path.GetExtension(f)))
                                .Select(f => new ImageFileItem(f)));
                    }
                }
                else
                {
                    // No P/ dir — look for images in root or image-only subfolders
                    ScanImageDir(_folderPath, imageFiles);
                }
            });

            _images.Clear();
            foreach (var img in imageFiles) _images.Add(img);

            _videos.Clear();
            foreach (var vid in videoFiles) _videos.Add(vid);

            // Update tab labels
            ImageTabButton.Content = $"图库 {_images.Count}";
            VideoTabButton.Content = $"视频集 {_videos.Count}";

            // Show first tab that has content
            if (_images.Count > 0) ShowImageTab();
            else if (_videos.Count > 0) ShowVideoTab();

            ItemCountText.Text = _images.Count > 0 && _videos.Count > 0
                ? $"{_images.Count} 个图片 · {_videos.Count} 个视频"
                : _images.Count > 0
                    ? $"{_images.Count} 个图片"
                    : $"{_videos.Count} 个视频";

            StatusText.Text = "就绪";
        }
        catch (Exception ex)
        {
            AppLogger.Error("collection", ex, $"Failed to scan collection: {_folderPath}");
            StatusText.Text = $"扫描失败：{ex.Message}";
        }
    }

    private static void ScanVideoDir(string dir, List<VideoItem> videos)
    {
        var savedVideos = new Dictionary<string, VideoItem>(); // empty: no persistence lookup here
        foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
        {
            if (!VideoFileDetector.IsSupportedVideo(file)) continue;
            videos.Add(new VideoItem
            {
                Id = ComputeId(file),
                Title = Path.GetFileNameWithoutExtension(file),
                FolderPath = file,
            });
        }
    }

    private static void ScanImageDir(string dir, List<ImageFileItem> images)
    {
        // Look for image-only subfolders (excluding V/ dirs)
        foreach (var subDir in Directory.EnumerateDirectories(dir))
        {
            var dirName = Path.GetFileName(subDir);
            if (string.Equals(dirName, "V", StringComparison.OrdinalIgnoreCase)) continue;
            if (string.Equals(dirName, "P", StringComparison.OrdinalIgnoreCase)) continue;

            var hasImages = Directory.EnumerateFiles(subDir)
                .Any(f => ImageExtensions.Contains(Path.GetExtension(f)));
            if (hasImages)
            {
                // Treat as image set — add each file
                foreach (var file in Directory.EnumerateFiles(subDir))
                {
                    if (ImageExtensions.Contains(Path.GetExtension(file)))
                        images.Add(new ImageFileItem(file));
                }
            }
        }
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

    // ── Tab switching ──────────────────────────────────────────────

    private void ImageTab_Click(object sender, RoutedEventArgs e)
    {
        ShowImageTab();
    }

    private void VideoTab_Click(object sender, RoutedEventArgs e)
    {
        ShowVideoTab();
    }

    private void ShowImageTab()
    {
        ImageGalleryPanel.Visibility = Visibility.Visible;
        VideoListPanel.Visibility = Visibility.Collapsed;
        ImageTabButton.Tag = "active";
        VideoTabButton.Tag = null;
    }

    private void ShowVideoTab()
    {
        ImageGalleryPanel.Visibility = Visibility.Collapsed;
        VideoListPanel.Visibility = Visibility.Visible;
        ImageTabButton.Tag = null;
        VideoTabButton.Tag = "active";
    }

    // ── Double-click handlers ──────────────────────────────────────

    private void ImageGallery_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var item = ImageGalleryPanel.SelectedItem as ImageFileItem;
        if (item == null || !File.Exists(item.FilePath)) return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = item.FilePath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppLogger.Warn("collection", $"Failed to open image: {ex.Message}");
        }
    }

    private void VideoList_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var video = VideoListPanel.SelectedItem as VideoItem;
        if (video == null || string.IsNullOrEmpty(video.FolderPath) || !File.Exists(video.FolderPath))
            return;

        var player = new PlayerWindow(video, _db, _storage);
        player.Show();
    }
}

/// <summary>
/// Lightweight wrapper for image files in a collection.
/// </summary>
public sealed class ImageFileItem
{
    private static readonly HashSet<string> PhotoExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png" };

    public string FilePath { get; }
    public string Name => Path.GetFileName(FilePath);
    public string FileTypeIcon
    {
        get
        {
            var ext = Path.GetExtension(FilePath);
            return PhotoExtensions.Contains(ext) ? "🖼️" : "📄";
        }
    }

    public ImageFileItem(string filePath)
    {
        FilePath = filePath;
    }
}
