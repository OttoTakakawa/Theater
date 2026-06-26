using MangaReader.Native.Services;
using MangaReader.Native.Videos.Models;
using MangaReader.Native.Videos.Services;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

namespace MangaReader.Native.Videos;

public partial class VideoLibraryWindow : Window
{
    private readonly VideoDatabase _db;
    private readonly AppStorage _storage;
    private readonly VideoLibraryScanner _scanner = new();
    private readonly ObservableCollection<VideoItem> _videos = new();
    private List<VideoItem> _allVideos = [];
    private readonly CancellationTokenSource _cts = new();

    private const string RootsSettingKey = "library.video_roots";

    public VideoLibraryWindow(AppStorage storage)
    {
        InitializeComponent();
        _storage = storage;
        _db = new VideoDatabase(storage.Root);
        _db.Initialize();

        VideoListBox.ItemsSource = _videos;
    }

    private async void VideoLibraryWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadVideosAsync();
    }

    private async Task LoadVideosAsync()
    {
        try
        {
            var roots = GetVideoRoots();
            if (roots.Count == 0)
            {
                EmptyState.Visibility = Visibility.Visible;
                VideoListBox.Visibility = Visibility.Collapsed;
                UpdateVideoCount(0);
                return;
            }

            EmptyState.Visibility = Visibility.Collapsed;
            VideoListBox.Visibility = Visibility.Visible;
            StatusText.Text = "正在扫描...";

            // Load previously saved progress from DB
            var savedVideos = _db.ListAllVideos().ToDictionary(v => v.Id);

            // Scan all roots
            var allVideos = new List<VideoItem>();
            await Task.Run(() =>
            {
                foreach (var root in roots)
                {
                    var found = _scanner.ScanDirectory(root);
                    allVideos.AddRange(found);
                }
            }, _cts.Token);

            // Merge saved progress
            foreach (var video in allVideos)
            {
                if (savedVideos.TryGetValue(video.Id, out var saved))
                {
                    video.DurationMs = saved.DurationMs;
                    video.LastPositionMs = saved.LastPositionMs;
                    video.ReadingStatus = saved.ReadingStatus;
                }
            }

            _allVideos = allVideos;
            ApplySearchFilter();
            StatusText.Text = "就绪";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "已取消";
        }
        catch (Exception ex)
        {
            AppLogger.Error("video-lib", ex, "Failed to load video library");
            StatusText.Text = $"加载失败：{ex.Message}";
        }
    }

    private void ApplySearchFilter()
    {
        var query = SearchBox.Text?.Trim().ToLowerInvariant() ?? "";

        var filtered = string.IsNullOrEmpty(query)
            ? _allVideos
            : _allVideos.Where(v =>
                v.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                v.Author.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

        _videos.Clear();

        // Sort: by author then title
        filtered.Sort((a, b) =>
        {
            var cmp = string.Compare(a.Author, b.Author, StringComparison.OrdinalIgnoreCase);
            return cmp != 0 ? cmp : string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase);
        });

        foreach (var video in filtered)
        {
            _videos.Add(video);
        }

        UpdateVideoCount(filtered.Count);
    }

    private void UpdateVideoCount(int count)
    {
        VideoCountText.Text = $"{count} 个视频";
    }

    private List<string> GetVideoRoots()
    {
        var raw = _db.LoadSetting(RootsSettingKey, "[]");
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<string>>(raw) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private void SaveVideoRoots(List<string> roots)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(roots);
        _db.SaveSetting(RootsSettingKey, json);
    }

    // ── Button handlers ────────────────────────────────────────────

    private async void AddFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "选择包含视频文件的文件夹"
        };

        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

        var newRoot = dialog.SelectedPath;
        if (string.IsNullOrEmpty(newRoot) || !Directory.Exists(newRoot)) return;

        var roots = GetVideoRoots();
        if (roots.Contains(newRoot, StringComparer.OrdinalIgnoreCase))
        {
            StatusText.Text = "该目录已在视频库中";
            return;
        }

        roots.Add(newRoot);
        SaveVideoRoots(roots);
        StatusText.Text = $"已添加：{newRoot}";

        await LoadVideosAsync();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await LoadVideosAsync();
    }

    private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        ApplySearchFilter();
    }

    private void ManageRootsButton_Click(object sender, RoutedEventArgs e)
    {
        var roots = GetVideoRoots();
        var message = "当前视频目录：\n\n" + (roots.Count == 0
            ? "(无)"
            : string.Join("\n", roots.Select(r => $"  • {r}")));

        message += "\n\n要清除所有目录并重新添加吗？";

        var result = System.Windows.MessageBox.Show(
            message, "管理视频目录",
            MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            SaveVideoRoots([]);
            _ = LoadVideosAsync();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    // ── Video playback ──────────────────────────────────────────────

    private void VideoListBox_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var video = VideoListBox.SelectedItem as VideoItem;
        if (video != null)
        {
            OpenPlayer(video);
        }
    }

    private void VideoListBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            var video = VideoListBox.SelectedItem as VideoItem;
            if (video != null)
            {
                OpenPlayer(video);
                e.Handled = true;
            }
        }
    }

    private void OpenPlayer(VideoItem video)
    {
        if (string.IsNullOrEmpty(video.FolderPath) || !File.Exists(video.FolderPath))
        {
            StatusText.Text = $"文件不存在：{video.Title}";
            return;
        }

        var player = new PlayerWindow(
            video,
            _db,
            _storage,
            playbackQueue: _videos.ToList(),
            nextVideoResolver: ResolveNextVideo,
            openVideoRequest: OpenQueuedVideo);

        player.Show();
    }

    private VideoItem? ResolveNextVideo(VideoItem current)
    {
        var index = _videos.IndexOf(current);
        if (index < 0 || index >= _videos.Count - 1) return null;
        return _videos[index + 1];
    }

    private void OpenQueuedVideo(VideoItem video)
    {
        // PlayerWindow calls this to navigate to the next video.
        // We open a new PlayerWindow for that video.
        OpenPlayer(video);
    }

    protected override void OnClosed(EventArgs e)
    {
        _cts.Cancel();
        _db.Dispose();
        base.OnClosed(e);
    }
}
