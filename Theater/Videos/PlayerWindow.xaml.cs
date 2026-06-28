using LibVLCSharp.Shared;
using Theater.Videos.Models;
using Theater.Videos.Services;
using Theater.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;
using ColorConverter = System.Windows.Media.ColorConverter;
using Cursors = System.Windows.Input.Cursors;
using Point = System.Windows.Point;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using WinForms = System.Windows.Forms;

namespace Theater.Videos;

public partial class PlayerWindow : Window
{
    private static readonly float[] SpeedSteps = [0.5f, 0.75f, 1.0f, 1.25f, 1.5f, 2.0f];
    private const int KeyboardSeekStepMs = 5000;
    private const int KeyboardVolumeStep = 5;
    private const int RightHoldThresholdMs = 220;
    private const double DefaultWindowSidePanelWidth = 360.0;
    private const double DefaultBottomBarHeight = 150.0;
    private const double MinWindowSidePanelWidth = 300.0;
    private const double MaxWindowSidePanelWidth = 560.0;
    private const double MinBottomBarHeight = 92.0;
    private const double MaxBottomBarHeight = 260.0;
    private const string SidePanelWidthSettingKey = "player.layout.side_panel_width";
    private const string BottomBarHeightSettingKey = "player.layout.bottom_bar_height";
    private const string PlaylistViewModeSettingKey = "player.playlist.view_mode";
    private const float TemporaryFastForwardRate = 3.0f;
    private const double WindowAspect = 19.0 / 11.0;
    private const double DefaultVideoAspect = 16.0 / 9.0;
    private const double LayoutEpsilon = 0.5;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private const int WM_SIZING = 0x0214;
    private const int WMSZ_LEFT = 1;
    private const int WMSZ_RIGHT = 2;
    private const int WMSZ_TOP = 3;
    private const int WMSZ_TOPLEFT = 4;
    private const int WMSZ_TOPRIGHT = 5;
    private const int WMSZ_BOTTOM = 6;
    private const int WMSZ_BOTTOMLEFT = 7;
    private const int WMSZ_BOTTOMRIGHT = 8;

    private const int WM_ERASEBKGND = 0x0014;
    private const int GCLP_HBRBACKGROUND = -10;
    private const int BLACK_BRUSH = 4;

    private delegate IntPtr WndProcDelegate(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "EnumChildWindows")]
    private static extern bool EnumChildWindows(IntPtr hwndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "SetClassLongPtrW")]
    private static extern IntPtr SetClassLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);

    [DllImport("gdi32.dll")]
    private static extern IntPtr GetStockObject(int fnObject);

    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr hWnd);

    private static readonly IntPtr BlackBrush = GetStockObject(BLACK_BRUSH);
    private bool _videoHwndBgFixed;
    private static readonly string[] PlaybackOptions =
    [
        "--no-video-title-show",
        "--no-osd",
        "--avcodec-hw=any",
        "--drop-late-frames",
        "--skip-frames",
        "--file-caching=1000"
    ];
    private static readonly Lazy<LibVLC> SharedLibVLC = new(() =>
    {
        Core.Initialize();
        return new LibVLC(PlaybackOptions);
    });

    public static Task PrewarmAsync()
        => Task.Run(() =>
        {
            _ = SharedLibVLC.Value;
        });

    private VideoItem _video;
    private readonly LibraryDatabase _database;
    private readonly AppStorage _storage;
    private readonly TimelineThumbnailCache _thumbnailCache;
    private readonly List<Key> _nextKeys;
    private readonly List<Key> _prevKeys;
    private readonly List<VideoItem> _playbackQueue;
    private readonly Func<VideoItem, VideoItem?>? _nextVideoResolver;
    private readonly Action<VideoItem>? _openVideoRequest;
    private readonly Key _fullscreenKey;
    private readonly Key _sidePanelKey;
    private readonly Key _markerKey;

    private LibVLCSharp.Shared.MediaPlayer? _mediaPlayer;

    private readonly DispatcherTimer _progressSyncTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer _progressSaveTimer = new() { Interval = TimeSpan.FromSeconds(8) };
    private readonly DispatcherTimer _controlsHideTimer = new() { Interval = TimeSpan.FromSeconds(2.5) };
    private readonly DispatcherTimer _rightHoldTimer = new() { Interval = TimeSpan.FromMilliseconds(RightHoldThresholdMs) };
    private readonly DispatcherTimer _statusHintTimer = new() { Interval = TimeSpan.FromSeconds(1.4) };
    private readonly object _progressSaveSync = new();

    private bool _isDragging;
    private bool _isFullscreen;
    private long _currentPositionMs;
    private long _totalDurationMs;
    private Rect _restoreBounds;
    private WindowStyle _restoreWindowStyle;
    private WindowState _restoreWindowState;
    private ResizeMode _restoreResizeMode;
    private bool _restoreTopmost;
    private int _speedIndex = 2; // 1.0× by default
    private bool _isRightKeyHeld;
    private bool _isTemporaryFastForwardActive;
    private bool _isSyncingVolume;
    private Task _pendingProgressSaveTask = Task.CompletedTask;
    private bool _isClosing;
    private bool _closeCleanupCompleted;
    private bool _isApplyingVideoLayout;
    private bool _videoViewLayoutReady;
    private bool _videoViewRevealAllowed;
    private bool _isPanelResizeDragging;
    private bool _panelResizeLayoutPending;
    private bool _windowSizingHooked;
    private bool _videoHiddenForPanelResize;
    private bool _hasStartedPlayback;
    private CancellationTokenSource? _loadCancellation;
    private DateTime _lastPauseSaveAt = DateTime.MinValue;
    private float _rateBeforeTemporaryFastForward = SpeedSteps[2];
    private string? _lastCropGeometry;
    private double _videoAspectRatio = DefaultVideoAspect;
    private double _sidePanelWidth = DefaultWindowSidePanelWidth;
    private double _bottomBarHeight = DefaultBottomBarHeight;
    private bool _sidePanelPinned;
    private bool _sidePanelVisibleBeforeFullscreen;
    private string _playlistViewMode = "list";

    private readonly List<VideoSegmentMarker> _markers = new();
    private readonly ObservableCollection<MarkerRow> _markerRows = new();
    private readonly ObservableCollection<PlaylistRow> _playlistRows = new();

    public bool IsInternalNavigationClose { get; private set; }

    /// <summary>
    /// 当前正在播放的视频。热切换后会指向最新加载的 <see cref="VideoItem"/>，
    /// 供宿主在窗口关闭时按"最后播放"写回进度，而非构造时传入的首个视频。
    /// </summary>
    public VideoItem CurrentVideo => _video;

    public PlayerWindow(
        VideoItem video,
        LibraryDatabase database,
        AppStorage storage,
        List<Key> nextKeys,
        List<Key> prevKeys,
        IReadOnlyList<VideoItem>? playbackQueue = null,
        Func<VideoItem, VideoItem?>? nextVideoResolver = null,
        Action<VideoItem>? openVideoRequest = null,
        Key fullscreenKey = Key.W,
        Key sidePanelKey = Key.F6,
        Key markerKey = Key.M)
    {
        InitializeComponent();
        _video = video;
        _database = database;
        _storage = storage;
        _thumbnailCache = new TimelineThumbnailCache(storage);
        _nextKeys = nextKeys;
        _prevKeys = prevKeys;
        _playbackQueue = playbackQueue?
            .Where(item => !item.IsMissing && !string.IsNullOrWhiteSpace(item.FolderPath))
            .ToList() ?? [];
        _nextVideoResolver = nextVideoResolver;
        _openVideoRequest = openVideoRequest;
        _fullscreenKey = fullscreenKey;
        _sidePanelKey = sidePanelKey;
        _markerKey = markerKey;

        Title = video.Title;
        LoadPlayerLayoutSettings();
        SidePanel.Visibility = Visibility.Visible;
        _sidePanelPinned = false;

        SidePanelMarkerList.ItemsSource = _markerRows;
        SidePanelPlaylistList.ItemsSource = _playlistRows;
        SidePanelPlaylistGrid.ItemsSource = _playlistRows;
        LoadPlaylistViewMode();
        RefreshPlaylistRows();

        _progressSyncTimer.Tick += ProgressSyncTimer_Tick;
        _progressSaveTimer.Tick += ProgressSaveTimer_Tick;
        _controlsHideTimer.Tick += ControlsHideTimer_Tick;
        _rightHoldTimer.Tick += RightHoldTimer_Tick;
        _statusHintTimer.Tick += StatusHintTimer_Tick;

        KeyDown += PlayerWindow_KeyDown;
        KeyUp += PlayerWindow_KeyUp;
        Closing += PlayerWindow_Closing;
        Loaded += PlayerWindow_Loaded;
        SourceInitialized += (_, _) => HookWindowSizing();
    }

    private async void PlayerWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _loadCancellation = new CancellationTokenSource();
        var loadToken = _loadCancellation.Token;
        ShowLoadingState("正在加载视频...", "播放器正在准备解码器和视频流");
        _ = ShowSlowLoadingHintsAsync(loadToken);

        try
        {
            var libVLC = await Task.Run(() => SharedLibVLC.Value, loadToken);
            if (IsClosingOrCanceled(loadToken))
            {
                return;
            }

            _mediaPlayer = new LibVLCSharp.Shared.MediaPlayer(libVLC);

            VideoViewControl.MediaPlayer = _mediaPlayer;

            _mediaPlayer.Playing += (_, _) => DispatchPlayerEvent(OnPlaying, "playing");
            _mediaPlayer.Paused += (_, _) => DispatchPlayerEvent(OnPaused, "paused");
            _mediaPlayer.Stopped += (_, _) => DispatchPlayerEvent(OnStopped, "stopped");
            _mediaPlayer.EndReached += (_, _) => DispatchPlayerEventAsync(OnEndReachedAsync, "end-reached");
            _mediaPlayer.LengthChanged += (_, args) => DispatchPlayerEvent(() => OnLengthChanged(args.Length), "length-changed");
            _mediaPlayer.VolumeChanged += (_, args) => DispatchPlayerEvent(() => SyncVolumeUI(args.Volume), "volume-changed");

            var filePath = _video.FolderPath;
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                AppLogger.Warn("player-open", $"File not found: {filePath}");
                System.Windows.MessageBox.Show($"视频文件不存在：{filePath}", "无法播放", MessageBoxButton.OK, MessageBoxImage.Warning);
                Close();
                return;
            }

            AppLogger.Info("player-open", $"Opening: {_video.Title} → {filePath}");

            ApplyWindowModeVideoLayout();
            // SidePanel 初始化会使 SidePanelColumn.Width 从 0 变为 _sidePanelWidth，
            // 但 VideoColumn.ActualWidth 需要一次布局 pass 才会更新。
            // 不强制 UpdateLayout 的话，ApplyVideoViewAspectLayout 会用旧值（全屏宽度）
            // 计算 VideoViewControl.Width，导致首帧按全屏宽度渲染、SidePanel 出现后尺寸跳变闪黑。
            StageRoot.UpdateLayout();
            ApplyVideoViewAspectLayout();

            ShowLoadingState("正在加载视频...", "正在打开文件并等待首帧");
            using var media = new Media(libVLC, filePath, FromType.FromPath);
            _mediaPlayer.Play(media);

            _progressSyncTimer.Start();
            _progressSaveTimer.Start();

            SyncVolumeUI(_mediaPlayer.Volume);

            if (IsClosingOrCanceled(loadToken))
            {
                return;
            }

            await LoadMarkersAsync();
            if (IsClosingOrCanceled(loadToken))
            {
                return;
            }

            RebuildMarkerLayer();
            RefreshSidePanelRows();
        }
        catch (OperationCanceledException)
        {
            // Window was closed while the player was still preparing.
        }
        catch (Exception ex)
        {
            if (_isClosing)
            {
                return;
            }

            AppLogger.Error("player-open", ex, "Failed to open player");
            System.Windows.MessageBox.Show($"播放器打开失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
        }
    }

    private void PlayerWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_closeCleanupCompleted)
        {
            return;
        }

        e.Cancel = true;
        if (_isClosing)
        {
            return;
        }

        _isClosing = true;
        _loadCancellation?.Cancel();
        VideoViewControl.Visibility = Visibility.Hidden;
        LoadingOverlay.Visibility = Visibility.Collapsed;
        Hide();
        _progressSyncTimer.Stop();
        _progressSaveTimer.Stop();
        _controlsHideTimer.Stop();
        _rightHoldTimer.Stop();
        _statusHintTimer.Stop();
        StopTemporaryFastForward();

        _thumbnailCache.Dispose();

        _ = CloseAfterCleanupAsync();
    }

    private async Task CloseAfterCleanupAsync()
    {
        _loadCancellation?.Dispose();
        _loadCancellation = null;
        var mediaPlayer = _mediaPlayer;
        _mediaPlayer = null;

        try
        {
            VideoViewControl.MediaPlayer = null;
        }
        catch (Exception ex)
        {
            AppLogger.Warn("player-close", $"Failed to detach VideoView: {ex.Message}");
        }

        var finalSaveTask = SaveFinalProgressOnCloseAsync(mediaPlayer);
        var releaseTask = ReleaseMediaPlayerAsync(mediaPlayer);

        await Task.WhenAny(Task.WhenAll(finalSaveTask, releaseTask), Task.Delay(TimeSpan.FromSeconds(2.5)));

        _closeCleanupCompleted = true;
        _ = Dispatcher.BeginInvoke(new Action(Close), DispatcherPriority.ApplicationIdle);
    }

    private async Task SaveFinalProgressOnCloseAsync(LibVLCSharp.Shared.MediaPlayer? mediaPlayer)
    {
        try
        {
            if (mediaPlayer != null)
            {
                var time = mediaPlayer.Time;
                if (time > 0)
                {
                    await QueueSaveVideoProgressAsync(time, _video.ReadingStatus);
                    return;
                }
            }

            await GetPendingProgressSaveTask();
        }
        catch (Exception ex)
        {
            AppLogger.Warn("player-save-progress", $"Failed to save final progress for {_video.Id}: {ex.Message}");
        }
    }

    private Task GetPendingProgressSaveTask()
    {
        lock (_progressSaveSync)
        {
            return _pendingProgressSaveTask;
        }
    }

    private static Task ReleaseMediaPlayerAsync(LibVLCSharp.Shared.MediaPlayer? mediaPlayer)
    {
        if (mediaPlayer == null)
        {
            return Task.CompletedTask;
        }

        return Task.Run(() =>
        {
            try
            {
                mediaPlayer.Stop();
            }
            catch
            {
                // LibVLC may throw during teardown after the HWND has been detached.
            }

            try
            {
                mediaPlayer.Dispose();
            }
            catch
            {
                // Closing must never block or crash the WPF UI thread.
            }
        });
    }

    private bool IsClosingOrCanceled(CancellationToken token)
    {
        return _isClosing || token.IsCancellationRequested;
    }

    private void LoadPlayerLayoutSettings()
    {
        _sidePanelWidth = LoadDoubleSetting(SidePanelWidthSettingKey, DefaultWindowSidePanelWidth, MinWindowSidePanelWidth, MaxWindowSidePanelWidth);
        _bottomBarHeight = LoadDoubleSetting(BottomBarHeightSettingKey, DefaultBottomBarHeight, MinBottomBarHeight, MaxBottomBarHeight);
        BottomBarRow.Height = new GridLength(_bottomBarHeight);
    }

    private double LoadDoubleSetting(string key, double fallback, double min, double max)
    {
        var raw = _database.LoadSetting(key, fallback.ToString("F0", System.Globalization.CultureInfo.InvariantCulture));
        return double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? Math.Clamp(value, min, max)
            : fallback;
    }

    private void PlayerPanelSplitter_DragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e)
    {
        if (_isFullscreen)
        {
            return;
        }

        _isPanelResizeDragging = true;
        HideVideoViewForPanelResize();
    }

    private void PlayerPanelSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        if (_isFullscreen)
        {
            _isPanelResizeDragging = false;
            RestoreVideoViewAfterPanelResize();
            return;
        }

        if (SidePanel.Visibility == Visibility.Visible && SidePanelColumn.ActualWidth >= MinWindowSidePanelWidth - 1)
        {
            _sidePanelWidth = Math.Clamp(SidePanelColumn.ActualWidth, MinWindowSidePanelWidth, MaxWindowSidePanelWidth);
        }

        _bottomBarHeight = Math.Clamp(BottomBarRow.ActualHeight, MinBottomBarHeight, MaxBottomBarHeight);
        SidePanelColumn.Width = SidePanel.Visibility == Visibility.Visible
            ? new GridLength(_sidePanelWidth)
            : new GridLength(0);
        BottomBarRow.Height = new GridLength(_bottomBarHeight);
        StageRoot.UpdateLayout();
        _isPanelResizeDragging = false;
        ApplyVideoViewAspectLayout();
        RestoreVideoViewAfterPanelResize();
        SavePlayerLayoutSettingsAsync();
    }

    private void PlayerPanelSplitter_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        if (_isFullscreen)
        {
            return;
        }

        if (ReferenceEquals(sender, SidePanelWidthSplitter) && SidePanel.Visibility == Visibility.Visible)
        {
            _sidePanelWidth = Math.Clamp(_sidePanelWidth - e.HorizontalChange, MinWindowSidePanelWidth, MaxWindowSidePanelWidth);
            SidePanelColumn.Width = new GridLength(_sidePanelWidth);
        }
        else if (ReferenceEquals(sender, BottomBarHeightSplitter))
        {
            _bottomBarHeight = Math.Clamp(_bottomBarHeight - e.VerticalChange, MinBottomBarHeight, MaxBottomBarHeight);
            BottomBarRow.Height = new GridLength(_bottomBarHeight);
        }
    }

    private void HideVideoViewForPanelResize()
    {
        if (_isClosing || _videoHiddenForPanelResize)
        {
            return;
        }

        _videoHiddenForPanelResize = true;
        _videoViewLayoutReady = false;
        VideoViewControl.Visibility = Visibility.Hidden;
    }

    private void RestoreVideoViewAfterPanelResize()
    {
        if (!_videoHiddenForPanelResize)
        {
            return;
        }

        _videoHiddenForPanelResize = false;
        if (_videoViewRevealAllowed && !_isClosing)
        {
            VideoViewControl.Visibility = Visibility.Visible;
        }
    }

    private void SchedulePanelResizeVideoLayout()
    {
        if (_panelResizeLayoutPending)
        {
            return;
        }

        _panelResizeLayoutPending = true;
        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            _panelResizeLayoutPending = false;
            ApplyVideoViewAspectLayout(lightweight: true);
        }), DispatcherPriority.Render);
    }

    private void SavePlayerLayoutSettingsAsync()
    {
        var sidePanelWidth = _sidePanelWidth.ToString("F0", System.Globalization.CultureInfo.InvariantCulture);
        var bottomBarHeight = _bottomBarHeight.ToString("F0", System.Globalization.CultureInfo.InvariantCulture);
        _ = Task.Run(() =>
        {
            try
            {
                _database.SaveSetting(SidePanelWidthSettingKey, sidePanelWidth);
                _database.SaveSetting(BottomBarHeightSettingKey, bottomBarHeight);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("player-layout", $"Failed to save player panel layout: {ex.Message}");
            }
        });
    }

    private void ShowLoadingState(string title, string detail)
    {
        if (_isClosing)
        {
            return;
        }

        LoadingTitleText.Text = title;
        LoadingDetailText.Text = detail;
        LoadingOverlay.Visibility = Visibility.Visible;
        VideoBackdrop.Visibility = Visibility.Visible;
        _videoViewRevealAllowed = false;
        VideoViewControl.Visibility = Visibility.Hidden;
    }

    private async Task ShowSlowLoadingHintsAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3), token);
            if (!_hasStartedPlayback && !IsClosingOrCanceled(token))
            {
                ShowLoadingState("视频加载较慢", "仍在等待播放器返回首帧，可以直接关闭窗口取消");
            }

            await Task.Delay(TimeSpan.FromSeconds(7), token);
            if (!_hasStartedPlayback && !IsClosingOrCanceled(token))
            {
                ShowLoadingState("视频加载仍未完成", "可能是文件过大、编码初始化较慢或磁盘读取较慢");
            }
        }
        catch (OperationCanceledException)
        {
            // Loading was canceled by closing the window.
        }
    }

    private void DispatchPlayerEvent(Action action, string scope)
    {
        _ = Dispatcher.InvokeAsync(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                HandlePlayerEventException(scope, ex);
            }
        });
    }

    private void DispatchPlayerEventAsync(Func<Task> action, string scope)
    {
        _ = Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                HandlePlayerEventException(scope, ex);
            }
        });
    }

    private void HandlePlayerEventException(string scope, Exception ex)
    {
        if (_isClosing)
        {
            return;
        }

        AppLogger.Error("player-event", ex, $"Unhandled player event exception: {scope}");
        ShowLoadingState("播放器状态同步失败", ex.Message);
    }

    // ── 播放状态回调 ──────────────────────────────────────────────────────

    private void OnPlaying()
    {
        if (_isClosing)
        {
            return;
        }

        _hasStartedPlayback = true;
        PlayPauseButton.Content = "⏸ 暂停";
        OverlayPlayPauseButton.Content = "▶ 暂停";
        SetVideoViewHwndBlackBackground();
        _ = UpdateVideoAspectFromMedia();
        ApplyVideoViewAspectLayout();
        ApplyFillCrop();
        _ = RevealVideoViewAfterFirstFrameAsync(_loadCancellation?.Token ?? CancellationToken.None);
    }

    private async Task RevealVideoViewAfterFirstFrameAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(220, token);
            if (_isClosing || token.IsCancellationRequested || _mediaPlayer?.IsPlaying != true)
            {
                return;
            }

            SetVideoViewHwndBlackBackground();
            _videoViewRevealAllowed = true;
            ApplyVideoViewAspectLayout();

            await Dispatcher.Yield(DispatcherPriority.Render);
            await Task.Delay(180, token);
            if (_isClosing || token.IsCancellationRequested || _mediaPlayer?.IsPlaying != true)
            {
                return;
            }

            // 分两步隐藏遮罩：先收起 LoadingOverlay（加载文字消失，留下纯黑 VideoBackdrop），
            // 等一帧渲染后再收起 VideoBackdrop（露出 VLC 已渲染的首帧）。
            // 同步隐藏两层会让"加载文字 → 视频画面"在同一帧内跳变，视觉上像闪了一下。
            LoadingOverlay.Visibility = Visibility.Collapsed;
            await Dispatcher.Yield(DispatcherPriority.Render);
            VideoBackdrop.Visibility = Visibility.Collapsed;
        }
        catch (OperationCanceledException)
        {
            // Loading was canceled by closing the window or switching video.
        }
    }

    private void OnPaused()
    {
        if (_isClosing)
        {
            return;
        }

        PlayPauseButton.Content = "▶ 播放";
        OverlayPlayPauseButton.Content = "▶ 播放";
        if ((DateTime.UtcNow - _lastPauseSaveAt).TotalSeconds >= 2)
        {
            _lastPauseSaveAt = DateTime.UtcNow;
            ForceSaveProgress();
        }
    }

    private void OnStopped()
    {
        if (_isClosing)
        {
            return;
        }

        PlayPauseButton.Content = "▶ 播放";
        OverlayPlayPauseButton.Content = "▶ 播放";
        _videoViewRevealAllowed = false;
        VideoBackdrop.Visibility = Visibility.Visible;
    }

    private async Task OnEndReachedAsync()
    {
        if (_isClosing)
        {
            return;
        }

        VideoBackdrop.Visibility = Visibility.Visible;
        _video.LastPositionMs = 0;
        await QueueSaveVideoProgressAsync(_video.LastPositionMs, _video.ReadingStatus);
        var nextVideo = ResolveAdjacentVideo(1) ?? _nextVideoResolver?.Invoke(_video);
        if (nextVideo != null)
        {
            ShowStatusHint($"下一条：{nextVideo.Title}");
            OpenQueuedVideo(nextVideo, saveCurrentProgress: false);
        }
        else
        {
            ShowStatusHint("播放完成");
        }
    }

    private void OnLengthChanged(long lengthMs)
    {
        if (_isClosing)
        {
            return;
        }

        if (lengthMs <= 0)
        {
            return;
        }

        DurationText.Text = FormatMs(lengthMs);
        OverlayDurationText.Text = FormatMs(lengthMs);

        if (_video.DurationMs != lengthMs)
        {
            _video.DurationMs = lengthMs;
            Task.Run(() => _database.SaveDuration(_video));
        }

        _totalDurationMs = lengthMs;
        _ = UpdateVideoAspectFromMedia();
        ApplyVideoViewAspectLayout();
        UpdateProgressVisual();
        UpdateOverlayProgressVisual();
        RebuildMarkerLayer();

        // Seek to last saved position (only once when length becomes known)
        if (_video.LastPositionMs > 0 && _video.LastPositionMs < lengthMs - 3000)
        {
            _mediaPlayer!.Time = _video.LastPositionMs;
        }
    }

    // ── 进度同步计时器 ────────────────────────────────────────────────────

    private void ProgressSyncTimer_Tick(object? sender, EventArgs e)
    {
        if (_isClosing || _mediaPlayer == null || _isDragging)
        {
            return;
        }

        _currentPositionMs = _mediaPlayer.Time;
        _totalDurationMs = _mediaPlayer.Length;
        var txt = FormatMs(_currentPositionMs);
        PositionText.Text = txt;
        OverlayPositionText.Text = txt;
        UpdateProgressVisual();
        UpdateOverlayProgressVisual();
    }

    private void ProgressSaveTimer_Tick(object? sender, EventArgs e)
    {
        QueueCurrentProgressSave();
    }

    private void ForceSaveProgress()
    {
        if (_mediaPlayer == null)
        {
            return;
        }

        var time = _mediaPlayer.Time;
        if (time > 0)
        {
            QueueSaveVideoProgressAsync(time, _video.ReadingStatus).GetAwaiter().GetResult();
            return;
        }

        FlushPendingProgressSaves();
    }

    private void QueueCurrentProgressSave()
    {
        if (_mediaPlayer == null)
        {
            return;
        }

        var time = _mediaPlayer.Time;
        if (time > 0)
        {
            _ = QueueSaveVideoProgressAsync(time, _video.ReadingStatus);
        }
    }

    private Task QueueSaveVideoProgressAsync(long positionMs, string readingStatus)
    {
        _video.LastPositionMs = positionMs;
        var snapshot = new VideoItem
        {
            Id = _video.Id,
            LastPositionMs = positionMs,
            ReadingStatus = readingStatus
        };

        lock (_progressSaveSync)
        {
            _pendingProgressSaveTask = _pendingProgressSaveTask.ContinueWith(_ =>
            {
                try
                {
                    _database.SaveVideoProgress(snapshot);
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("player-save-progress", $"Failed to save progress for {_video.Id}: {ex.Message}");
                }
            }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);

            return _pendingProgressSaveTask;
        }
    }

    private void FlushPendingProgressSaves()
    {
        Task pendingTask;
        lock (_progressSaveSync)
        {
            pendingTask = _pendingProgressSaveTask;
        }

        pendingTask.GetAwaiter().GetResult();
    }

    // ── HUD/Overlay 显隐 ──────────────────────────────────────────────
    // 简化为两态：
    //   窗口：BottomBar 常显（始终 Visible）
    //   全屏：BottomBar Collapsed；OverlayBar 由鼠标近底唤起 → 2.5s 静止 → 隐藏

    private void ShowOverlay()
    {
        if (!_isFullscreen) return;
        OverlayBar.Visibility = Visibility.Visible;
        Cursor = Cursors.Arrow;
    }

    private void HideOverlay()
    {
        OverlayBar.Visibility = Visibility.Collapsed;
        if (_isFullscreen) Cursor = Cursors.None;
    }

    private void Window_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isFullscreen) return;

        var p = e.GetPosition(this);
        var nearBottom = p.Y > ActualHeight - 120;
        var nearSidePanel = p.X > ActualWidth - Math.Max(_sidePanelWidth, 96);

        if (nearBottom)
        {
            ShowOverlay();
            _controlsHideTimer.Stop();
            _controlsHideTimer.Start();
        }

        if (SidePanel.Visibility == Visibility.Visible && !nearSidePanel && !_sidePanelPinned)
        {
            SidePanel.Visibility = Visibility.Collapsed;
            Cursor = Cursors.None;
        }
        else if (SidePanel.Visibility != Visibility.Visible && nearSidePanel)
        {
            SidePanel.Visibility = Visibility.Visible;
            _sidePanelPinned = false;
            RefreshVisibleSidePanelPage();
            Cursor = Cursors.Arrow;
        }
    }

    private void SetVideoViewHwndBlackBackground()
    {
        if (_videoHwndBgFixed) return;

        var mainHwnd = new WindowInteropHelper(this).Handle;
        EnumChildWindows(mainHwnd, (child, param) =>
        {
            SetClassLongPtr(child, GCLP_HBRBACKGROUND, BlackBrush);
            InvalidateRect(child, IntPtr.Zero, true);
            return true;
        }, IntPtr.Zero);

        _videoHwndBgFixed = true;
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyVideoViewAspectLayout();
    }

    private void FlashBackdropDuringLayout()
    {
        HideVideoViewUntilNextLayout();
        if (VideoBackdrop.Visibility != Visibility.Collapsed) return;
        VideoBackdrop.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_mediaPlayer?.IsPlaying == true)
                VideoBackdrop.Visibility = Visibility.Collapsed;
        }), DispatcherPriority.ContextIdle);
    }

    private void HideVideoViewUntilNextLayout()
    {
        if (_isClosing)
        {
            return;
        }

        _videoViewLayoutReady = false;
        VideoViewControl.Visibility = Visibility.Hidden;
    }

    private void HookWindowSizing()
    {
        if (_windowSizingHooked)
        {
            return;
        }

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var source = HwndSource.FromHwnd(hwnd);
        source?.AddHook(WndProc);
        _windowSizingHooked = source is not null;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_SIZING && !_isFullscreen)
        {
            var rc = Marshal.PtrToStructure<RECT>(lParam);
            var w = (double)(rc.Right - rc.Left);
            var h = (double)(rc.Bottom - rc.Top);
            var edge = wParam.ToInt32();

            switch (edge)
            {
                case WMSZ_LEFT:
                case WMSZ_RIGHT:
                    rc.Bottom = rc.Top + (int)Math.Round(w / WindowAspect);
                    break;
                case WMSZ_TOP:
                case WMSZ_BOTTOM:
                    rc.Right = rc.Left + (int)Math.Round(h * WindowAspect);
                    break;
                case WMSZ_TOPLEFT:
                case WMSZ_BOTTOMLEFT:
                    rc.Left = rc.Right - (int)Math.Round(h * WindowAspect);
                    break;
                case WMSZ_TOPRIGHT:
                case WMSZ_BOTTOMRIGHT:
                    rc.Right = rc.Left + (int)Math.Round(h * WindowAspect);
                    break;
            }

            Marshal.StructureToPtr(rc, lParam, false);
            handled = true;
            return new IntPtr(1);
        }

        return IntPtr.Zero;
    }

    private void ApplyWindowModeVideoLayout()
    {
        HideVideoViewUntilNextLayout();

        if (_isFullscreen)
        {
            StageContentRow.Height = new GridLength(1, GridUnitType.Star);
            BottomBarRow.Height = new GridLength(0);
            BottomBarRow.MinHeight = 0;
            VideoColumn.Width = new GridLength(1, GridUnitType.Star);
            SidePanelColumn.Width = new GridLength(0);
            SidePanelWidthSplitter.Visibility = Visibility.Collapsed;
            BottomBarHeightSplitter.Visibility = Visibility.Collapsed;
            Grid.SetColumn(VideoViewControl, 0);
            Grid.SetColumnSpan(VideoViewControl, 2);
            Grid.SetColumn(SidePanel, 0);
            Grid.SetColumnSpan(SidePanel, 2);
            Grid.SetColumn(SidePanelHoverZone, 0);
            Grid.SetColumnSpan(SidePanelHoverZone, 2);
            SidePanel.Width = _sidePanelWidth;
            SidePanel.Height = double.NaN;
            SidePanel.Margin = new Thickness(0);
            SidePanel.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
            SidePanel.VerticalAlignment = System.Windows.VerticalAlignment.Stretch;
            SidePanelHoverZone.Margin = new Thickness(0);
            SidePanelHoverZone.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
            ApplyVideoViewAspectLayout();
            return;
        }

        var panelOpen = SidePanel.Visibility == Visibility.Visible;
        StageContentRow.Height = new GridLength(1, GridUnitType.Star);
        BottomBarRow.Height = new GridLength(_bottomBarHeight);
        VideoColumn.Width = new GridLength(1, GridUnitType.Star);
        SidePanelColumn.Width = new GridLength(panelOpen ? _sidePanelWidth : 0);
        BottomBarHeightSplitter.Visibility = Visibility.Visible;
        Grid.SetColumn(VideoViewControl, 0);
        Grid.SetColumnSpan(VideoViewControl, 1);
        Grid.SetColumn(SidePanel, 1);
        Grid.SetColumnSpan(SidePanel, 1);
        Grid.SetColumn(SidePanelHoverZone, 0);
        Grid.SetColumnSpan(SidePanelHoverZone, 2);
        SidePanel.Width = double.NaN;
        SidePanel.Height = double.NaN;
        SidePanel.Margin = new Thickness(0);
        SidePanel.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
        SidePanel.VerticalAlignment = System.Windows.VerticalAlignment.Stretch;
        SidePanelHoverZone.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
        SidePanelHoverZone.Margin = new Thickness(0);
        ApplyVideoViewAspectLayout();
    }

    private bool UpdateVideoAspectFromMedia()
    {
        if (_mediaPlayer == null)
        {
            return false;
        }

        try
        {
            uint pixelWidth = 0;
            uint pixelHeight = 0;
            if (!_mediaPlayer.Size(0, ref pixelWidth, ref pixelHeight) || pixelWidth == 0 || pixelHeight == 0)
            {
                return false;
            }

            var nextAspect = (double)pixelWidth / pixelHeight;
            if (double.IsFinite(nextAspect) && nextAspect > 0.05 && Math.Abs(nextAspect - _videoAspectRatio) > 0.001)
            {
                _videoAspectRatio = nextAspect;
                return true;
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn("player-layout", $"Failed to read video size: {ex.Message}");
        }

        return false;
    }

    private void ApplyVideoViewAspectLayout(bool lightweight = false)
    {
        if (_isClosing || _isApplyingVideoLayout)
        {
            return;
        }

        var availableWidth = ResolveVideoStageWidth();
        var availableHeight = ResolveVideoStageHeight();
        if (availableWidth <= 0 || availableHeight <= 0)
        {
            return;
        }

        var targetWidth = availableWidth;
        var targetHeight = targetWidth / _videoAspectRatio;
        if (targetHeight > availableHeight)
        {
            targetHeight = availableHeight;
            targetWidth = targetHeight * _videoAspectRatio;
        }

        targetWidth = Math.Max(1, targetWidth);
        targetHeight = Math.Max(1, targetHeight);

        if (_videoViewLayoutReady
            && Math.Abs(VideoViewControl.Width - targetWidth) < LayoutEpsilon
            && Math.Abs(VideoViewControl.Height - targetHeight) < LayoutEpsilon
            && Math.Abs(VideoViewControl.MaxWidth - availableWidth) < LayoutEpsilon
            && Math.Abs(VideoViewControl.MaxHeight - availableHeight) < LayoutEpsilon
            && VideoViewControl.HorizontalAlignment == System.Windows.HorizontalAlignment.Center
            && VideoViewControl.VerticalAlignment == System.Windows.VerticalAlignment.Center
            && VideoViewControl.Visibility == Visibility.Visible)
        {
            return;
        }

        _isApplyingVideoLayout = true;
        try
        {
            if (!lightweight && !_isPanelResizeDragging)
            {
                // 只在尺寸变化时才先隐藏，避免不必要的 Hidden→Visible 切换触发 HWND 重建导致首帧闪烁
                if (!_videoViewLayoutReady
                    || Math.Abs(VideoViewControl.Width - targetWidth) >= LayoutEpsilon
                    || Math.Abs(VideoViewControl.Height - targetHeight) >= LayoutEpsilon)
                {
                    VideoViewControl.Visibility = Visibility.Hidden;
                }
            }

            VideoViewControl.Width = targetWidth;
            VideoViewControl.Height = targetHeight;
            VideoViewControl.MaxWidth = availableWidth;
            VideoViewControl.MaxHeight = availableHeight;
            VideoViewControl.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
            VideoViewControl.VerticalAlignment = System.Windows.VerticalAlignment.Center;
            _videoViewLayoutReady = true;
            VideoViewControl.Visibility = _videoViewRevealAllowed && !_videoHiddenForPanelResize
                ? Visibility.Visible
                : Visibility.Hidden;
        }
        finally
        {
            _isApplyingVideoLayout = false;
        }
    }

    private double ResolveVideoStageWidth()
    {
        if (StageRoot.ActualWidth <= 0)
        {
            return 0;
        }

        if (_isFullscreen)
        {
            return StageRoot.ActualWidth;
        }

        return VideoColumn.ActualWidth > 0 ? VideoColumn.ActualWidth : StageRoot.ActualWidth;
    }

    private double ResolveVideoStageHeight()
    {
        if (StageRoot.ActualHeight <= 0)
        {
            return 0;
        }

        return _isFullscreen ? StageRoot.ActualHeight : StageContentRow.ActualHeight;
    }

    private void ApplyFillCrop()
    {
        if (_mediaPlayer == null)
        {
            return;
        }

        // letterbox/contain：视频按原比例在容器内居中，不裁切。
        // F6 打开时容器 16:9，16:9 视频完美贴合；竖屏视频高度对齐、两侧黑边。
        // F6 关闭时容器 19:9，16:9 视频居中、左右黑边。
        if (_lastCropGeometry == null)
        {
            return;
        }

        _mediaPlayer.Scale = 0;
        _mediaPlayer.AspectRatio = null;
        _mediaPlayer.CropGeometry = null;
        _lastCropGeometry = null;
    }

    private void SidePanelHoverZone_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isFullscreen)
        {
            return;
        }

        SidePanel.Visibility = Visibility.Visible;
        _sidePanelPinned = false;
        RefreshVisibleSidePanelPage();
        Cursor = Cursors.Arrow;
    }

    private void SidePanel_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isFullscreen)
        {
            return;
        }

        if (!_sidePanelPinned)
        {
            SidePanel.Visibility = Visibility.Collapsed;
            Cursor = Cursors.None;
        }
    }

    private void ControlsHideTimer_Tick(object? sender, EventArgs e)
    {
        if (_isFullscreen) HideOverlay();
    }

    private void ShowStatusHint(string message)
    {
        StatusHintText.Text = message;
        StatusHint.Visibility = Visibility.Visible;
        _statusHintTimer.Stop();
        _statusHintTimer.Start();
    }

    private void StatusHintTimer_Tick(object? sender, EventArgs e)
    {
        _statusHintTimer.Stop();
        StatusHint.Visibility = Visibility.Collapsed;
    }

    // ── 进度条交互（纯自定义 visuals，所有事件挂在 TimelineHost） ────────
    // 子元素 IsHitTestVisible=False；TimelineHost Background=Transparent 是唯一命中入口。
    // marker 子 Border 自身命中并 e.Handled=true 阻止冒泡到 TimelineHost。

    private void UpdateProgressVisual()
    {
        if (TimelineHost == null) return;
        if (_totalDurationMs <= 0 || TimelineHost.ActualWidth <= 0)
        {
            TimelineTrackFill.Width = 0;
            TimelineThumb.Margin = new Thickness(-7, 0, 0, 0);
            return;
        }

        var ratio = Math.Clamp((double)_currentPositionMs / _totalDurationMs, 0, 1);
        var w = TimelineHost.ActualWidth * ratio;
        TimelineTrackFill.Width = w;
        TimelineThumb.Margin = new Thickness(w - 7, 0, 0, 0);
    }

    private long TimeFromMouseX(double x)
    {
        if (TimelineHost.ActualWidth <= 0 || _totalDurationMs <= 0)
        {
            return 0;
        }
        var ratio = Math.Clamp(x / TimelineHost.ActualWidth, 0, 1);
        return (long)(_totalDurationMs * ratio);
    }

    private long TimeFromMouseXOn(double x, double width)
    {
        if (width <= 0 || _totalDurationMs <= 0)
        {
            return 0;
        }
        var ratio = Math.Clamp(x / width, 0, 1);
        return (long)(_totalDurationMs * ratio);
    }

    private string GetTimelineHoverText(double x, double width, long fallbackMs)
    {
        var marker = FindMarkerNearTimelineX(x, width);
        if (marker is null)
        {
            return FormatMs(fallbackMs);
        }

        return string.IsNullOrWhiteSpace(marker.Title)
            ? marker.TimeText
            : marker.Title.Trim();
    }

    private VideoSegmentMarker? FindMarkerNearTimelineX(double x, double width)
    {
        if (width <= 0 || _totalDurationMs <= 0 || _markers.Count == 0)
        {
            return null;
        }

        const double hitPadding = 4.0;
        var hitRadius = MarkerDotSize / 2.0 + hitPadding;

        return _markers
            .Select(marker => new
            {
                Marker = marker,
                Distance = Math.Abs((Math.Clamp((double)marker.TimeMs / _totalDurationMs, 0.0, 1.0) * width) - x)
            })
            .Where(item => item.Distance <= hitRadius)
            .OrderBy(item => item.Distance)
            .Select(item => item.Marker)
            .FirstOrDefault();
    }

    private void TimelineHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateProgressVisual();
    }

    // ── Overlay 进度条（全屏鼠标近底唤起） ─────────────────────────────────

    private void UpdateOverlayProgressVisual()
    {
        if (OverlayTimelineHost == null) return;
        if (_totalDurationMs <= 0 || OverlayTimelineHost.ActualWidth <= 0)
        {
            OverlayTrackFill.Width = 0;
            OverlayThumb.Margin = new Thickness(-7, 0, 0, 0);
            return;
        }
        var ratio = Math.Clamp((double)_currentPositionMs / _totalDurationMs, 0, 1);
        var w = OverlayTimelineHost.ActualWidth * ratio;
        OverlayTrackFill.Width = w;
        OverlayThumb.Margin = new Thickness(w - 7, 0, 0, 0);
    }

    private void OverlayTimelineHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateOverlayProgressVisual();
    }

    private void OverlayTimelineHost_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_totalDurationMs > 0)
        {
            OverlayTimelineHoverTip.IsOpen = true;
        }
    }

    private void OverlayTimelineHost_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        OverlayTimelineHoverTip.IsOpen = false;
    }

    private void OverlayTimelineHost_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_isDragging && e.LeftButton == MouseButtonState.Pressed && OverlayTimelineHost.ActualWidth > 0 && _totalDurationMs > 0)
        {
            var x = e.GetPosition(OverlayTimelineHost).X;
            var ratio = Math.Clamp(x / OverlayTimelineHost.ActualWidth, 0, 1);
            _currentPositionMs = (long)(_totalDurationMs * ratio);
            UpdateOverlayProgressVisual();
            UpdateProgressVisual();
            var txt = FormatMs(_currentPositionMs);
            OverlayPositionText.Text = txt;
            PositionText.Text = txt;
        }

        if (_totalDurationMs > 0 && OverlayTimelineHost.ActualWidth > 0)
        {
            var x = e.GetPosition(OverlayTimelineHost).X;
            var ms = TimeFromMouseXOn(x, OverlayTimelineHost.ActualWidth);
            if (!OverlayTimelineHoverTip.IsOpen) OverlayTimelineHoverTip.IsOpen = true;
            OverlayTimelineHoverTipText.Text = GetTimelineHoverText(x, OverlayTimelineHost.ActualWidth, ms);
            OverlayTimelineHoverTip.HorizontalOffset = x - 20;
            OverlayTimelineHoverTip.VerticalOffset = -28;
        }

        // overlay 鼠标活动也要重启 hide timer，避免在 overlay 上交互时被吞掉
        _controlsHideTimer.Stop();
        _controlsHideTimer.Start();
    }

    private void OverlayTimelineHost_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_totalDurationMs <= 0 || OverlayTimelineHost.ActualWidth <= 0) return;
        _isDragging = true;
        OverlayTimelineHost.CaptureMouse();
        var x = e.GetPosition(OverlayTimelineHost).X;
        var ratio = Math.Clamp(x / OverlayTimelineHost.ActualWidth, 0, 1);
        _currentPositionMs = (long)(_totalDurationMs * ratio);
        UpdateOverlayProgressVisual();
        UpdateProgressVisual();
        var txt = FormatMs(_currentPositionMs);
        OverlayPositionText.Text = txt;
        PositionText.Text = txt;
        e.Handled = true;
    }

    private void OverlayTimelineHost_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        var x = e.GetPosition(OverlayTimelineHost).X;
        var ratio = Math.Clamp(x / Math.Max(1, OverlayTimelineHost.ActualWidth), 0, 1);
        _currentPositionMs = (long)(_totalDurationMs * ratio);
        OverlayTimelineHost.ReleaseMouseCapture();
        if (_mediaPlayer != null && _totalDurationMs > 0)
        {
            _mediaPlayer.Time = _currentPositionMs;
        }
        UpdateOverlayProgressVisual();
        UpdateProgressVisual();
        e.Handled = true;
    }

    private void TimelineHost_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_totalDurationMs > 0)
        {
            TimelineHoverTip.IsOpen = true;
        }
    }

    private void TimelineHost_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        TimelineHoverTip.IsOpen = false;
    }

    private void TimelineHost_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        var x = e.GetPosition(TimelineHost).X;
        var ms = TimeFromMouseX(x);

        if (_totalDurationMs > 0)
        {
            if (!TimelineHoverTip.IsOpen) TimelineHoverTip.IsOpen = true;
            TimelineHoverTipText.Text = GetTimelineHoverText(x, TimelineHost.ActualWidth, ms);
            TimelineHoverTip.HorizontalOffset = x - 20;
            TimelineHoverTip.VerticalOffset = -28;
        }

        if (_isDragging && e.LeftButton == MouseButtonState.Pressed)
        {
            _currentPositionMs = ms;
            UpdateProgressVisual();
            UpdateOverlayProgressVisual();
            var txt = FormatMs(ms);
            PositionText.Text = txt;
            OverlayPositionText.Text = txt;
        }
    }

    private void TimelineHost_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_totalDurationMs <= 0) return;

        _isDragging = true;
        TimelineHost.CaptureMouse();
        _currentPositionMs = TimeFromMouseX(e.GetPosition(TimelineHost).X);
        UpdateProgressVisual();
        UpdateOverlayProgressVisual();
        var txt = FormatMs(_currentPositionMs);
        PositionText.Text = txt;
        OverlayPositionText.Text = txt;
        e.Handled = true;
    }

    private void TimelineHost_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging) return;

        _isDragging = false;
        _currentPositionMs = TimeFromMouseX(e.GetPosition(TimelineHost).X);
        TimelineHost.ReleaseMouseCapture();
        if (_mediaPlayer != null && _totalDurationMs > 0)
        {
            _mediaPlayer.Time = _currentPositionMs;
        }
        UpdateProgressVisual();
        UpdateOverlayProgressVisual();
        e.Handled = true;
    }

    // ── 播放/暂停 ─────────────────────────────────────────────────────────

    private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
    {
        TogglePlayPause();
    }

    private void TogglePlayPause()
    {
        if (_mediaPlayer == null)
        {
            return;
        }

        if (_mediaPlayer.IsPlaying)
        {
            _mediaPlayer.Pause();
        }
        else
        {
            _mediaPlayer.Play();
        }
    }

    private void PreviousVideoButton_Click(object sender, RoutedEventArgs e)
    {
        OpenAdjacentVideo(-1);
    }

    private void NextVideoButton_Click(object sender, RoutedEventArgs e)
    {
        OpenAdjacentVideo(1);
    }

    private void OpenAdjacentVideo(int direction)
    {
        var target = ResolveAdjacentVideo(direction);
        if (target == null)
        {
            ShowStatusHint(direction < 0 ? "没有上一条" : "没有下一条");
            return;
        }

        OpenQueuedVideo(target);
    }

    private VideoItem? ResolveAdjacentVideo(int direction)
    {
        if (_playbackQueue.Count == 0)
        {
            return direction > 0 ? _nextVideoResolver?.Invoke(_video) : null;
        }

        var currentIndex = _playbackQueue.FindIndex(item => ReferenceEquals(item, _video) || item.Id == _video.Id);
        if (currentIndex < 0)
        {
            return direction > 0 ? _nextVideoResolver?.Invoke(_video) : null;
        }

        var targetIndex = currentIndex + Math.Sign(direction);
        return targetIndex >= 0 && targetIndex < _playbackQueue.Count ? _playbackQueue[targetIndex] : null;
    }

    private void OpenQueuedVideo(VideoItem video, bool saveCurrentProgress = true)
    {
        if (video == null)
        {
            return;
        }

        if (ReferenceEquals(video, _video) || video.Id == _video.Id)
        {
            ShowStatusHint("已经在播放此视频");
            return;
        }

        _ = SwitchToVideoAsync(video, saveCurrentProgress);
    }

    private async Task SwitchToVideoAsync(VideoItem newVideo, bool saveCurrentProgress = true)
    {
        if (_isClosing || newVideo == null)
        {
            return;
        }

        // 1. 取消正在进行的加载，建立新的取消令牌
        _loadCancellation?.Cancel();
        _loadCancellation = new CancellationTokenSource();
        var token = _loadCancellation.Token;

        // 2. 保存当前视频进度（仍指向旧 _video）
        if (saveCurrentProgress)
        {
            try { ForceSaveProgress(); }
            catch (Exception ex) { AppLogger.Warn("player-switch", $"Save progress before switch failed: {ex.Message}"); }
        }

        // 3. 停止当前播放
        try { _mediaPlayer?.Stop(); }
        catch (Exception ex) { AppLogger.Warn("player-switch", $"Stop before switch failed: {ex.Message}"); }

        // 4. 重置播放/标记状态
        _hasStartedPlayback = false;
        _currentPositionMs = 0;
        _totalDurationMs = 0;
        _videoAspectRatio = DefaultVideoAspect;
        _lastCropGeometry = null;
        _markers.Clear();
        _markerRows.Clear();
        SyncVideoMarkerCount();
        RebuildMarkerLayer();

        // 5. 切换到新视频
        _video = newVideo;
        Title = newVideo.Title;
        PositionText.Text = "00:00";
        OverlayPositionText.Text = "00:00";
        DurationText.Text = "--:--";
        OverlayDurationText.Text = "--:--";
        UpdateProgressVisual();
        UpdateOverlayProgressVisual();

        // 6. 刷新侧边栏面板（播放列表当前项 / 标记）
        RefreshPlaylistRows();
        RefreshSidePanelRows();

        // 7. 加载并播放新文件
        var filePath = newVideo.FolderPath;
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            AppLogger.Warn("player-switch", $"File not found: {filePath}");
            System.Windows.MessageBox.Show($"视频文件不存在：{filePath}", "无法播放", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ShowLoadingState("正在加载视频...", $"正在切换到：{newVideo.Title}");
        _ = ShowSlowLoadingHintsAsync(token);

        try
        {
            if (_mediaPlayer == null)
            {
                AppLogger.Warn("player-switch", "MediaPlayer unavailable during switch");
                return;
            }

            AppLogger.Info("player-switch", $"Switching to: {newVideo.Title} → {filePath}");
            var libVLC = SharedLibVLC.Value;
            using var media = new Media(libVLC, filePath, FromType.FromPath);
            _mediaPlayer.Play(media);

            await LoadMarkersAsync();
            if (token.IsCancellationRequested) return;
            RebuildMarkerLayer();
            RefreshSidePanelRows();
        }
        catch (OperationCanceledException)
        {
            // 切换被下一次切换或关闭取消
        }
        catch (Exception ex)
        {
            if (_isClosing) return;
            AppLogger.Error("player-switch", ex, "Failed to switch video");
            ShowStatusHint("切换视频失败");
        }
    }

    // ── 音量 ──────────────────────────────────────────────────────────────

    private void MuteButton_Click(object sender, RoutedEventArgs e)
    {
        _mediaPlayer?.ToggleMute();
        UpdateMuteButton();
    }

    private void VolumeBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_mediaPlayer == null || _isSyncingVolume)
        {
            return;
        }

        var volume = ClampUiVolume(VolumeBar.Value);
        _mediaPlayer.Volume = volume;
        VolumeText.Text = $"{volume}%";
        UpdateMuteButton();
    }

    private void SyncVolumeUI(double vlcVolume)
    {
        if (_isClosing)
        {
            return;
        }

        var volume = NormalizeVlcVolumeToUi(vlcVolume);
        _isSyncingVolume = true;
        VolumeBar.Value = volume;
        _isSyncingVolume = false;
        VolumeText.Text = $"{volume}%";
        UpdateMuteButton();
    }

    private static int NormalizeVlcVolumeToUi(double vlcVolume)
    {
        // LibVLC 的回调在部分版本会给 0-1 浮点值，属性读取则通常是 0-100 整数。
        var normalized = vlcVolume > 0 && vlcVolume <= 1.0
            ? vlcVolume * 100.0
            : vlcVolume;

        return ClampUiVolume(normalized);
    }

    private static int ClampUiVolume(double volume)
    {
        return (int)Math.Round(Math.Clamp(volume, 0, 100));
    }

    private void UpdateMuteButton()
    {
        var content = (_mediaPlayer?.Mute == true || _mediaPlayer?.Volume == 0) ? "🔇" : "🔊";
        MuteButton.Content = content;
        OverlayMuteButton.Content = content;
    }

    // ── 倍速 ──────────────────────────────────────────────────────────────

    private void SpeedButton_Click(object sender, RoutedEventArgs e)
    {
        _speedIndex = (_speedIndex + 1) % SpeedSteps.Length;
        ApplySpeed();
    }

    private void ApplySpeed()
    {
        var rate = GetSelectedSpeedRate();
        _mediaPlayer?.SetRate(rate);
        var content = $"{rate:0.##}×";
        SpeedButton.Content = content;
        OverlaySpeedButton.Content = content;
    }

    private float GetSelectedSpeedRate()
    {
        return SpeedSteps[Math.Clamp(_speedIndex, 0, SpeedSteps.Length - 1)];
    }

    // ── 全屏 ──────────────────────────────────────────────────────────────

    private void FullscreenButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleFullscreen();
    }

    private void ToggleFullscreen()
    {
        if (_isFullscreen) ExitFullscreen();
        else EnterFullscreen();
    }

    private void EnterFullscreen()
    {
        _restoreWindowStyle = WindowStyle;
        _restoreWindowState = WindowState;
        _restoreResizeMode = ResizeMode;
        _restoreTopmost = Topmost;
        _restoreBounds = new Rect(Left, Top, Width, Height);

        // 顺序优化：先 Topmost（避免任务栏短暂浮上来）+ 立刻 Collapse BottomBar
        // 再 bounds → 最后 WindowStyle=None；ResizeMode 不影响视觉
        Topmost = true;
        BottomBar.Visibility = Visibility.Collapsed;
        _sidePanelVisibleBeforeFullscreen = SidePanel.Visibility == Visibility.Visible;
        SidePanel.Visibility = Visibility.Collapsed;
        _sidePanelPinned = false;
        SidePanelHoverZone.Visibility = Visibility.Visible;

        if (WindowState == WindowState.Maximized) WindowState = WindowState.Normal;

        var hwnd = new WindowInteropHelper(this).Handle;
        var screen = WinForms.Screen.FromHandle(hwnd);
        var source = PresentationSource.FromVisual(this);
        var px2dip = source!.CompositionTarget!.TransformFromDevice;
        var topLeft = px2dip.Transform(new Point(screen.Bounds.Left, screen.Bounds.Top));
        var botRight = px2dip.Transform(new Point(screen.Bounds.Right, screen.Bounds.Bottom));

        Left = topLeft.X;
        Top = topLeft.Y;
        Width = botRight.X - topLeft.X;
        Height = botRight.Y - topLeft.Y;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None;

        _isFullscreen = true;
        FullscreenButton.Content = "⊡ 退出全屏";
        OverlayFullscreenButton.Content = "⊡ 退出";
        Cursor = Cursors.None;
        OverlayBar.Visibility = Visibility.Collapsed;
        _controlsHideTimer.Stop();
        FlashBackdropDuringLayout();
        ApplyWindowModeVideoLayout();
        ApplyFillCrop();
        UpdateOverlayProgressVisual();
        RebuildMarkerLayer();
    }

    private void ExitFullscreen()
    {
        WindowStyle = _restoreWindowStyle;
        ResizeMode = _restoreResizeMode;
        Left = _restoreBounds.X;
        Top = _restoreBounds.Y;
        Width = _restoreBounds.Width;
        Height = _restoreBounds.Height;
        Topmost = _restoreTopmost;
        if (_restoreWindowState == WindowState.Maximized) WindowState = WindowState.Maximized;

        BottomBar.Visibility = Visibility.Visible;
        OverlayBar.Visibility = Visibility.Collapsed;
        SidePanelHoverZone.Visibility = Visibility.Collapsed;
        SidePanel.Visibility = _sidePanelVisibleBeforeFullscreen ? Visibility.Visible : Visibility.Collapsed;
        _sidePanelPinned = false;

        _isFullscreen = false;
        FullscreenButton.Content = "⛶ 全屏";
        OverlayFullscreenButton.Content = "⛶ 全屏";
        Cursor = Cursors.Arrow;
        _controlsHideTimer.Stop();
        BottomBarRow.MinHeight = MinBottomBarHeight;
        FlashBackdropDuringLayout();
        ApplyWindowModeVideoLayout();
        ApplyFillCrop();
        UpdateProgressVisual();
        RebuildMarkerLayer();
    }

    // ── 返回 ──────────────────────────────────────────────────────────────

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    // ── 键盘快捷键 ────────────────────────────────────────────────────────

    private void PlayerWindow_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (_isFullscreen)
            {
                ToggleFullscreen();
            }
            else
            {
                Close();
            }

            e.Handled = true;
            return;
        }

        if (e.Key == _fullscreenKey)
        {
            if (!e.IsRepeat)
            {
                ToggleFullscreen();
            }
            e.Handled = true;
            return;
        }

        if (e.Key == _sidePanelKey)
        {
            if (!e.IsRepeat)
            {
                ToggleSidePanel();
            }
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Space)
        {
            TogglePlayPause();
            e.Handled = true;
            return;
        }

        if (e.Key == _markerKey)
        {
            if (!e.IsRepeat)
            {
                CreateMarkerAtCurrentTime(openEditor: true);
            }
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Right || _nextKeys.Contains(e.Key))
        {
            HandleRightKeyPressed(e);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Left || _prevKeys.Contains(e.Key))
        {
            if (!e.IsRepeat)
            {
                HandleLeftSeek();
            }

            e.Handled = true;
            return;
        }

        if (e.Key == Key.Up)
        {
            AdjustVolume(KeyboardVolumeStep);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Down)
        {
            AdjustVolume(-KeyboardVolumeStep);
            e.Handled = true;
        }

        if (e.Key == Key.M)
        {
            if (!e.IsRepeat)
                ToggleMuteFromKeyboard();
            e.Handled = true;
        }

        if (e.Key == Key.OemComma)
        {
            if (!e.IsRepeat)
                GoToPreviousMarker();
            e.Handled = true;
        }

        if (e.Key == Key.OemPeriod)
        {
            if (!e.IsRepeat)
                GoToNextMarker();
            e.Handled = true;
        }
    }

    private void PlayerWindow_KeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Right || _nextKeys.Contains(e.Key))
        {
            HandleRightKeyReleased();
            e.Handled = true;
        }
    }

    private void RightHoldTimer_Tick(object? sender, EventArgs e)
    {
        _rightHoldTimer.Stop();
        if (_isRightKeyHeld)
        {
            StartTemporaryFastForward();
        }
    }

    private void HandleRightKeyPressed(System.Windows.Input.KeyEventArgs e)
    {
        if (e.IsRepeat)
        {
            return;
        }

        _isRightKeyHeld = true;
        _rightHoldTimer.Stop();
        _rightHoldTimer.Start();
    }

    private void HandleRightKeyReleased()
    {
        if (!_isRightKeyHeld && !_isTemporaryFastForwardActive)
        {
            return;
        }

        _isRightKeyHeld = false;
        _rightHoldTimer.Stop();

        if (_isTemporaryFastForwardActive)
        {
            StopTemporaryFastForward();
            return;
        }

        if (_mediaPlayer != null && _mediaPlayer.Length > 0)
        {
            _mediaPlayer.Time = Math.Min(_mediaPlayer.Length, _mediaPlayer.Time + KeyboardSeekStepMs);
        }
    }

    private void HandleLeftSeek()
    {
        if (_mediaPlayer == null)
        {
            return;
        }

        _mediaPlayer.Time = Math.Max(0, _mediaPlayer.Time - KeyboardSeekStepMs);
    }

    private void AdjustVolume(int delta)
    {
        VolumeBar.Value = Math.Clamp(VolumeBar.Value + delta, VolumeBar.Minimum, VolumeBar.Maximum);
    }

    private void ToggleMuteFromKeyboard()
    {
        _mediaPlayer?.ToggleMute();
        UpdateMuteButton();
    }

    private void StageRoot_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // 鼠标在底部栏（含音量条）或侧边栏上时不拦截，让它们自行处理滚轮
        var pos = e.GetPosition(StageRoot);
        if (pos.Y >= StageRoot.ActualHeight - BottomBarRow.ActualHeight)
            return;
        if (pos.X >= StageRoot.ActualWidth - SidePanelColumn.ActualWidth)
            return;

        AdjustVolume(e.Delta > 0 ? KeyboardVolumeStep : -KeyboardVolumeStep);
        e.Handled = true;
    }

    private void StartTemporaryFastForward()
    {
        if (_mediaPlayer == null || _isTemporaryFastForwardActive)
        {
            return;
        }

        _rateBeforeTemporaryFastForward = GetSelectedSpeedRate();
        _mediaPlayer.SetRate(TemporaryFastForwardRate);
        var content = $"{TemporaryFastForwardRate:0.##}×";
        SpeedButton.Content = content;
        OverlaySpeedButton.Content = content;
        _isTemporaryFastForwardActive = true;
    }

    private void StopTemporaryFastForward()
    {
        if (_mediaPlayer == null || !_isTemporaryFastForwardActive)
        {
            return;
        }

        _mediaPlayer.SetRate(_rateBeforeTemporaryFastForward);
        var content = $"{_rateBeforeTemporaryFastForward:0.##}×";
        SpeedButton.Content = content;
        OverlaySpeedButton.Content = content;
        _isTemporaryFastForwardActive = false;
    }

    // ── 分段标记 ──────────────────────────────────────────────────────────

    private const double MarkerDotSize = 12.0;

    private async Task LoadMarkersAsync()
    {
        try
        {
            var markers = await Task.Run(() => _database.LoadSegmentMarkers(_video.Id));
            _markers.Clear();
            _markers.AddRange(markers);
            SyncVideoMarkerCount();
        }
        catch (Exception ex)
        {
            AppLogger.Warn("segment-load", $"Failed to load markers for {_video.Id}: {ex.Message}");
            ShowStatusHint("分段标记加载失败");
        }
    }

    private void SegmentMarkerLayer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        RebuildMarkerLayer();
    }

    private void OverlaySegmentMarkerLayer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        RebuildMarkerLayer();
    }

    private void RebuildMarkerLayer()
    {
        if (SegmentMarkerLayer.ActualWidth > 0) BuildMarkersOn(SegmentMarkerLayer);
        if (OverlaySegmentMarkerLayer.ActualWidth > 0) BuildMarkersOn(OverlaySegmentMarkerLayer);
    }

    private void BuildMarkersOn(System.Windows.Controls.Canvas? layer)
    {
        if (layer == null) return;
        layer.Children.Clear();

        var duration = GetCurrentDuration();
        var width = layer.ActualWidth;
        if (duration <= 0 || width <= 0 || _markers.Count == 0) return;

        var top = (layer.Height - MarkerDotSize) / 2.0;
        foreach (var marker in _markers)
        {
            var element = CreateMarkerDot(marker, duration, width, top);
            layer.Children.Add(element);
        }
    }

    private System.Windows.Shapes.Ellipse CreateMarkerDot(VideoSegmentMarker marker, long duration, double layerWidth, double top)
    {
        var ratio = Math.Clamp((double)marker.TimeMs / duration, 0.0, 1.0);
        var left = ratio * layerWidth - MarkerDotSize / 2.0;
        left = Math.Clamp(left, 0, Math.Max(0, layerWidth - MarkerDotSize));

        var dot = new System.Windows.Shapes.Ellipse
        {
            Width = MarkerDotSize,
            Height = MarkerDotSize,
            Fill = HexToBrush(marker.Color),
            Stroke = System.Windows.Media.Brushes.White,
            StrokeThickness = 1.5,
            Cursor = Cursors.Hand,
            Tag = marker,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new System.Windows.Media.ScaleTransform(1.0, 1.0),
            ToolTip = string.IsNullOrWhiteSpace(marker.Title)
                ? marker.TimeText
                : marker.Title.Trim(),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 4,
                ShadowDepth = 0,
                Opacity = 0.55,
                Color = Colors.Black
            }
        };

        System.Windows.Controls.Canvas.SetLeft(dot, left);
        System.Windows.Controls.Canvas.SetTop(dot, top);

        dot.MouseLeftButtonDown += Marker_MouseLeftButtonDown;
        dot.MouseEnter += Marker_MouseEnter;
        dot.MouseLeave += Marker_MouseLeave;
        dot.MouseRightButtonUp += Marker_MouseRightButtonUp;
        dot.ContextMenu = BuildMarkerContextMenu(marker);

        return dot;
    }

    private void Marker_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is System.Windows.Shapes.Ellipse dot)
        {
            AnimateMarkerDot(dot, 1.55, 2.4);
        }
    }

    private void Marker_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is System.Windows.Shapes.Ellipse dot)
        {
            AnimateMarkerDot(dot, 1.0, 1.5);
        }
    }

    private static void AnimateMarkerDot(System.Windows.Shapes.Ellipse dot, double scale, double strokeThickness)
    {
        var duration = TimeSpan.FromMilliseconds(110);
        if (dot.RenderTransform is System.Windows.Media.ScaleTransform transform)
        {
            transform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, new DoubleAnimation(scale, duration));
            transform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, new DoubleAnimation(scale, duration));
        }

        dot.BeginAnimation(System.Windows.Shapes.Shape.StrokeThicknessProperty, new DoubleAnimation(strokeThickness, duration));
    }

    private ContextMenu BuildMarkerContextMenu(VideoSegmentMarker marker)
    {
        var menu = new ContextMenu();
        var jump = new MenuItem { Header = "跳转到此处" };
        jump.Click += (_, _) => JumpToMarker(marker);
        var edit = new MenuItem { Header = "编辑..." };
        edit.Click += (_, _) => EditMarker(marker);
        var del = new MenuItem { Header = "删除" };
        del.Click += (_, _) => DeleteMarker(marker);
        menu.Items.Add(jump);
        menu.Items.Add(edit);
        menu.Items.Add(new Separator());
        menu.Items.Add(del);
        return menu;
    }

    private void Marker_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: VideoSegmentMarker marker })
        {
            JumpToMarker(marker);
            e.Handled = true;
        }
    }

    private void Marker_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.ContextMenu != null)
        {
            fe.ContextMenu.PlacementTarget = fe;
            fe.ContextMenu.IsOpen = true;
            e.Handled = true;
        }
    }

    private void AddMarkerButton_Click(object sender, RoutedEventArgs e)
    {
        CreateMarkerAtCurrentTime(openEditor: true);
    }

    private void MarkerListButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleSidePanel();
    }

    private async void CreateMarkerAtCurrentTime(bool openEditor)
    {
        var duration = GetCurrentDuration();
        if (_mediaPlayer == null || duration <= 0)
        {
            ShowStatusHint("当前视频暂不能创建分段标记");
            return;
        }

        var timeMs = Math.Clamp(_mediaPlayer.Time, 0, duration);
        var marker = new VideoSegmentMarker
        {
            VideoId = _video.Id,
            TimeMs = timeMs,
            Color = "#F97316"
        };
        marker.Title = marker.DefaultTitleForTime();

        if (openEditor)
        {
            var dialog = new SegmentMarkerEditDialog(marker) { Owner = this };
            if (dialog.ShowDialog() != true)
            {
                ShowStatusHint("已取消创建分段标记");
                return;
            }

            marker.Title = string.IsNullOrWhiteSpace(dialog.ResultTitle) ? marker.DefaultTitleForTime() : dialog.ResultTitle;
            marker.Note = dialog.ResultNote;
            marker.Color = dialog.ResultColor;
        }

        try
        {
            await Task.Run(() => _database.UpsertSegmentMarker(marker));
        }
        catch (Exception ex)
        {
            AppLogger.Warn("segment-create", $"Upsert failed: {ex.Message}");
            ShowStatusHint("分段标记保存失败");
            return;
        }

        _markers.Add(marker);
        SortMarkers();
        SyncVideoMarkerCount();
        RebuildMarkerLayer();
        RefreshSidePanelRows();
    }

    private void SortMarkers()
    {
        _markers.Sort((a, b) =>
        {
            var cmp = a.TimeMs.CompareTo(b.TimeMs);
            return cmp != 0 ? cmp : a.Id.CompareTo(b.Id);
        });
    }

    private void JumpToMarker(VideoSegmentMarker marker)
    {
        if (_mediaPlayer == null || _mediaPlayer.Length <= 0)
        {
            return;
        }

        _mediaPlayer.Time = Math.Clamp(marker.TimeMs, 0, _mediaPlayer.Length);
        _currentPositionMs = _mediaPlayer.Time;
        var txt = FormatMs(_mediaPlayer.Time);
        PositionText.Text = txt;
        OverlayPositionText.Text = txt;
        UpdateProgressVisual();
        UpdateOverlayProgressVisual();
    }

    private void GoToPreviousMarker()
    {
        if (_markers.Count == 0)
        {
            ShowStatusHint("无分段标记");
            return;
        }

        var currentTime = _mediaPlayer?.Time ?? 0;
        var prev = _markers.Where(m => m.TimeMs < currentTime - 50).OrderByDescending(m => m.TimeMs).FirstOrDefault();
        if (prev is null)
        {
            ShowStatusHint("已到第一个分段标记");
            return;
        }

        JumpToMarker(prev);
    }

    private void GoToNextMarker()
    {
        if (_markers.Count == 0)
        {
            ShowStatusHint("无分段标记");
            return;
        }

        var currentTime = _mediaPlayer?.Time ?? 0;
        var next = _markers.Where(m => m.TimeMs > currentTime + 50).OrderBy(m => m.TimeMs).FirstOrDefault();
        if (next is null)
        {
            ShowStatusHint("已到最后一个分段标记");
            return;
        }

        JumpToMarker(next);
    }

    private async void EditMarker(VideoSegmentMarker marker)
    {
        var dialog = new SegmentMarkerEditDialog(marker) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var oldTitle = marker.Title;
        var oldNote = marker.Note;
        var oldColor = marker.Color;
        marker.Title = string.IsNullOrWhiteSpace(dialog.ResultTitle) ? marker.DefaultTitleForTime() : dialog.ResultTitle;
        marker.Note = dialog.ResultNote;
        marker.Color = dialog.ResultColor;

        try
        {
            await Task.Run(() => _database.UpsertSegmentMarker(marker));
        }
        catch (Exception ex)
        {
            marker.Title = oldTitle;
            marker.Note = oldNote;
            marker.Color = oldColor;
            AppLogger.Warn("segment-update", $"Upsert failed: {ex.Message}");
            ShowStatusHint("分段标记保存失败");
            return;
        }

        RebuildMarkerLayer();
        RefreshSidePanelRows();
    }

    private async void DeleteMarker(VideoSegmentMarker marker)
    {
        try
        {
            await Task.Run(() => _database.DeleteSegmentMarker(marker.Id));
        }
        catch (Exception ex)
        {
            AppLogger.Warn("segment-delete", $"Delete failed: {ex.Message}");
            ShowStatusHint("分段标记删除失败");
            return;
        }

        _markers.RemoveAll(m => m.Id == marker.Id);
        SyncVideoMarkerCount();
        RebuildMarkerLayer();
        RefreshSidePanelRows();
    }

    private void SyncVideoMarkerCount()
    {
        _video.SegmentMarkerCount = _markers.Count;
        _video.NotifyAll();
    }

    // ── 侧边栏（播放列表 / 分段 / 设置） ────────────────────────────────

    public async void ToggleSidePanel()
    {
        HideVideoViewForPanelResize();
        try
        {
            if (SidePanel.Visibility == Visibility.Visible)
            {
                SidePanel.Visibility = Visibility.Collapsed;
                _sidePanelPinned = false;
            }
            else
            {
                SidePanel.Visibility = Visibility.Visible;
                _sidePanelPinned = _isFullscreen;
                RefreshVisibleSidePanelPage();
            }

            ApplyWindowModeVideoLayout();
            StageRoot.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.Render);
            ApplyFillCrop();
            ApplyVideoViewAspectLayout();
        }
        catch (Exception ex)
        {
            AppLogger.Warn("player-side-panel", $"Toggle side panel layout failed: {ex.Message}");
        }
        finally
        {
            RestoreVideoViewAfterPanelResize();
        }
    }

    private void RefreshVisibleSidePanelPage()
    {
        RefreshPlaylistRows();
        if (SegmentPanel.Visibility == Visibility.Visible)
        {
            RefreshSidePanelRows();
        }
    }

    private void RefreshPlaylistRows()
    {
        _playlistRows.Clear();

        var source = _playbackQueue.Count > 0 ? _playbackQueue : [_video];
        for (var i = 0; i < source.Count; i++)
        {
            var item = source[i];
            _playlistRows.Add(new PlaylistRow(
                i + 1,
                item,
                ReferenceEquals(item, _video) || item.Id == _video.Id,
                FormatMs(item.DurationMs),
                string.IsNullOrWhiteSpace(item.ReadingStatus) ? "unread" : item.ReadingStatus));
        }

        PlaylistSummaryText.Text = _playlistRows.Count > 0
            ? $"当前队列 {_playlistRows.Count} 条 / 双击切换"
            : "当前队列为空";

        _ = LoadPlaylistThumbnailsAsync();
    }

    private async Task LoadPlaylistThumbnailsAsync()
    {
        // 等待主播放器稳定后再生成缩略图，避免与主播放器抢占 CPU/GPU 解码资源导致播放卡顿
        while (!_hasStartedPlayback && !_isClosing)
        {
            await Task.Delay(200).ConfigureAwait(false);
        }
        if (_isClosing) return;

        // 拷贝快照避免在 await 期间 _playlistRows 被 RefreshPlaylistRows 修改导致枚举异常
        var snapshot = _playlistRows.ToList();
        foreach (var row in snapshot)
        {
            if (row.Thumbnail != null) continue;
            if (_isClosing) return;
            try
            {
                var path = await _thumbnailCache.GetOrCreateAsync(row.Video, 5000).ConfigureAwait(false);
                if (path == null) continue;
                var bmp = await Task.Run(() => ImageLoader.LoadBitmap(path, 160)).ConfigureAwait(false);
                await Dispatcher.InvokeAsync(() => row.Thumbnail = bmp);
            }
            catch (ObjectDisposedException)
            {
                // 窗口已关闭，_thumbnailCache 被释放
                return;
            }
            catch (Exception ex)
            {
                AppLogger.Warn("playlist-thumb", $"Load thumb failed for {row.Video.Id}: {ex.Message}");
            }
        }
    }

    private void LoadPlaylistViewMode()
    {
        var saved = _database.LoadSetting(PlaylistViewModeSettingKey, "list");
        _playlistViewMode = saved == "grid" ? "grid" : "list";
        ApplyPlaylistViewMode();
    }

    private void ApplyPlaylistViewMode()
    {
        var isGrid = _playlistViewMode == "grid";
        SidePanelPlaylistListContainer.Visibility = isGrid ? Visibility.Collapsed : Visibility.Visible;
        SidePanelPlaylistGridContainer.Visibility = isGrid ? Visibility.Visible : Visibility.Collapsed;
        PlaylistViewToggle.Content = isGrid ? "▤" : "▦";
    }

    private void PlaylistViewToggle_Click(object sender, RoutedEventArgs e)
    {
        _playlistViewMode = _playlistViewMode == "grid" ? "list" : "grid";
        ApplyPlaylistViewMode();
        try { _database.SaveSetting(PlaylistViewModeSettingKey, _playlistViewMode); }
        catch (Exception ex) { AppLogger.Warn("player-layout", $"Failed to save playlist view mode: {ex.Message}"); }
    }

    private void RefreshSidePanelRows()
    {
        UpdateMarkerPanelSummary();

        // 侧边栏关闭时不主动构建行（也不触发缩略图生成），等用户打开再说。
        if (SidePanel.Visibility != Visibility.Visible)
        {
            return;
        }

        _markerRows.Clear();
        foreach (var marker in _markers)
        {
            var row = new MarkerRow
            {
                Marker = marker,
                ColorBrush = HexToBrush(marker.Color)
            };
            _markerRows.Add(row);
            _ = LoadThumbnailAsync(row);
        }
    }

    private void UpdateMarkerPanelSummary()
    {
        if (MarkerPanelSummaryText is null)
        {
            return;
        }

        MarkerPanelSummaryText.Text = _markers.Count == 0
            ? "0 个标记"
            : $"{_markers.Count} 个标记";
    }

    private async Task LoadThumbnailAsync(MarkerRow row)
    {
        // 等待主播放器稳定后再生成缩略图，避免与主播放器抢占解码资源
        while (!_hasStartedPlayback && !_isClosing)
        {
            await Task.Delay(200).ConfigureAwait(false);
        }
        if (_isClosing) return;

        try
        {
            var path = await _thumbnailCache.GetOrCreateAsync(_video, row.Marker.TimeMs).ConfigureAwait(false);
            if (path == null)
            {
                return;
            }

            var bmp = await Task.Run(() => ImageLoader.LoadBitmap(path, 160)).ConfigureAwait(false);
            await Dispatcher.InvokeAsync(() => row.Thumbnail = bmp);
        }
        catch (ObjectDisposedException)
        {
            // 窗口已关闭，_thumbnailCache 被释放
            return;
        }
        catch (Exception ex)
        {
            AppLogger.Warn("timeline-thumb", $"Load thumb failed for marker {row.Marker.Id}: {ex.Message}");
        }
    }

    private VideoSegmentMarker? GetSelectedSidePanelMarker()
    {
        return (SidePanelMarkerList.SelectedItem as MarkerRow)?.Marker;
    }

    private static VideoSegmentMarker? GetMarkerFromCommandSource(object sender)
    {
        return sender is MenuItem { CommandParameter: MarkerRow row }
            ? row.Marker
            : null;
    }

    private void SidePanelMarkerList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var item = FindAncestor<ListBoxItem>((DependencyObject)e.OriginalSource);
        if (item == null)
        {
            return;
        }

        item.IsSelected = true;
        item.Focus();
        e.Handled = false;
    }

    private void MarkerRow_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MarkerRow row })
        {
            SidePanelMarkerList.SelectedItem = row;
        }
    }

    private static T? FindAncestor<T>(DependencyObject? current)
        where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T target)
            {
                return target;
            }

            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private void SidePanelMarkerList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var marker = GetSelectedSidePanelMarker();
        if (marker != null)
        {
            JumpToMarker(marker);
        }
    }

    private void SidePanelMarkerList_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        var marker = GetSelectedSidePanelMarker();
        if (marker == null)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            JumpToMarker(marker);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            DeleteMarker(marker);
            e.Handled = true;
        }
    }

    private void SidePanelJumpMenu_Click(object sender, RoutedEventArgs e)
    {
        var marker = GetMarkerFromCommandSource(sender) ?? GetSelectedSidePanelMarker();
        if (marker != null)
        {
            JumpToMarker(marker);
        }
    }

    private void SidePanelEditMenu_Click(object sender, RoutedEventArgs e)
    {
        var marker = GetMarkerFromCommandSource(sender) ?? GetSelectedSidePanelMarker();
        if (marker != null)
        {
            EditMarker(marker);
        }
    }

    private void SidePanelDeleteMenu_Click(object sender, RoutedEventArgs e)
    {
        var marker = GetMarkerFromCommandSource(sender) ?? GetSelectedSidePanelMarker();
        if (marker != null)
        {
            DeleteMarker(marker);
        }
    }

    private void SidePanelCloseButton_Click(object sender, RoutedEventArgs e)
    {
        SidePanel.Visibility = Visibility.Collapsed;
        _sidePanelPinned = false;
        FlashBackdropDuringLayout();
        ApplyWindowModeVideoLayout();
        ApplyFillCrop();
    }

    private void TabSegment_Click(object sender, RoutedEventArgs e)
    {
        SelectSidePanelPage(SegmentPanel, TabSegment);
        RefreshSidePanelRows();
    }

    private void TabPlaylist_Click(object sender, RoutedEventArgs e)
    {
        SelectSidePanelPage(PlaylistPanel, TabPlaylist);
        RefreshPlaylistRows();
    }

    private void TabSettings_Click(object sender, RoutedEventArgs e)
    {
        SelectSidePanelPage(SettingsPanel, TabSettings);
    }

    private void SelectSidePanelPage(UIElement panel, System.Windows.Controls.Button activeTab)
    {
        PlaylistPanel.Visibility = panel == PlaylistPanel ? Visibility.Visible : Visibility.Collapsed;
        SegmentPanel.Visibility = panel == SegmentPanel ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanel.Visibility = panel == SettingsPanel ? Visibility.Visible : Visibility.Collapsed;

        TabPlaylist.Tag = activeTab == TabPlaylist ? "active" : null;
        TabSegment.Tag = activeTab == TabSegment ? "active" : null;
        TabSettings.Tag = activeTab == TabSettings ? "active" : null;
    }

    private PlaylistRow? GetSelectedPlaylistRow()
    {
        return (SidePanelPlaylistList.SelectedItem as PlaylistRow)
            ?? (SidePanelPlaylistGrid.SelectedItem as PlaylistRow);
    }

    private void SidePanelPlaylistList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        OpenSelectedPlaylistItem();
    }

    private void SidePanelPlaylistList_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OpenSelectedPlaylistItem();
            e.Handled = true;
        }
    }

    private void OpenSelectedPlaylistItem()
    {
        OpenPlaylistItem(GetSelectedPlaylistRow());
    }

    private void OpenPlaylistItem(PlaylistRow? row)
    {
        if (row == null)
        {
            return;
        }

        if (row.IsCurrent)
        {
            ShowStatusHint("已经在播放此视频");
            return;
        }

        OpenQueuedVideo(row.Video);
    }

    private void OpenPlaylistItemMenu_Click(object sender, RoutedEventArgs e)
    {
        OpenPlaylistItem((sender as MenuItem)?.CommandParameter as PlaylistRow);
    }

    private void SettingsToggleFullscreen_Click(object sender, RoutedEventArgs e)
    {
        ToggleFullscreen();
    }

    private void SettingsCreateMarker_Click(object sender, RoutedEventArgs e)
    {
        CreateMarkerAtCurrentTime(openEditor: true);
    }

    private void SettingsOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        OpenCurrentVideoFolder();
    }

    private void OpenCurrentVideoFolder()
    {
        var path = _video.FolderPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            ShowStatusHint("文件不存在");
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{path}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppLogger.Warn("player-open-folder", $"Open folder failed: {ex.Message}");
            ShowStatusHint("打开文件位置失败");
        }
    }

    public sealed class MarkerRow : INotifyPropertyChanged
    {
        public required VideoSegmentMarker Marker { get; init; }
        public required SolidColorBrush ColorBrush { get; init; }

        private BitmapSource? _thumbnail;
        public BitmapSource? Thumbnail
        {
            get => _thumbnail;
            set
            {
                _thumbnail = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ThumbnailPlaceholderVisibility));
            }
        }

        public Visibility ThumbnailPlaceholderVisibility => _thumbnail == null ? Visibility.Visible : Visibility.Collapsed;
        public Visibility NoteVisibility => string.IsNullOrWhiteSpace(Marker.Note) ? Visibility.Collapsed : Visibility.Visible;

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public sealed class PlaylistRow(int index, VideoItem video, bool isCurrent, string durationText, string statusText) : INotifyPropertyChanged
    {
        private static readonly SolidColorBrush AccentCurrentBrush = HexToBrush("#FBBF24");
        private static readonly SolidColorBrush AccentOtherBrush = HexToBrush("#64748B");

        public int Index { get; } = index;
        public VideoItem Video { get; } = video;
        public bool IsCurrent { get; } = isCurrent;
        public string Title => Video.Title;
        public string DurationText { get; } = durationText;
        public string StatusText { get; } = statusText;
        public FontWeight FontWeight => IsCurrent ? FontWeights.SemiBold : FontWeights.Normal;
        public SolidColorBrush AccentBrush => IsCurrent ? AccentCurrentBrush : AccentOtherBrush;

        private BitmapSource? _thumbnail;
        public BitmapSource? Thumbnail
        {
            get => _thumbnail;
            set
            {
                _thumbnail = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ThumbnailPlaceholderVisibility));
            }
        }

        public Visibility ThumbnailPlaceholderVisibility => _thumbnail == null ? Visibility.Visible : Visibility.Collapsed;

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private static SolidColorBrush HexToBrush(string hex)
    {
        try
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }
        catch
        {
            var fallback = new SolidColorBrush(Colors.Orange);
            fallback.Freeze();
            return fallback;
        }
    }

    // ── 工具 ──────────────────────────────────────────────────────────────

    private long GetCurrentDuration() => _totalDurationMs > 0 ? _totalDurationMs : _video.DurationMs;

    private static string FormatMs(long ms)
    {
        if (ms < 0)
        {
            ms = 0;
        }

        var ts = TimeSpan.FromMilliseconds(ms);
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{ts.Minutes}:{ts.Seconds:D2}";
    }
}
