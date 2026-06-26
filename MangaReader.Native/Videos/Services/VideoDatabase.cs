using MangaReader.Native.Videos.Models;
using Microsoft.Data.Sqlite;

namespace MangaReader.Native.Videos.Services;

/// <summary>
/// Separate SQLite database for video metadata (progress, markers, settings).
/// Completely independent from the manga LibraryDatabase — "图和视频分开揍".
/// Database file: {Root}/video.db
/// </summary>
public sealed class VideoDatabase : IDisposable
{
    private readonly string _connectionString;
    private readonly string _dbPath;
    private SqliteConnection? _sharedConnection;
    private readonly object _writeLock = new();

    public VideoDatabase(string dataRoot)
    {
        var dir = Path.Combine(dataRoot, "video");
        Directory.CreateDirectory(dir);
        _dbPath = Path.Combine(dir, "video.db");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath
        }.ToString();
    }

    public void Initialize()
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();

        // Videos table — tracks playback progress
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS videos (
                id TEXT PRIMARY KEY,
                title TEXT NOT NULL DEFAULT '',
                author TEXT NOT NULL DEFAULT '',
                folder_path TEXT NOT NULL DEFAULT '',
                duration_ms INTEGER NOT NULL DEFAULT 0,
                last_position_ms INTEGER NOT NULL DEFAULT 0,
                reading_status TEXT NOT NULL DEFAULT 'unread',
                segment_marker_count INTEGER NOT NULL DEFAULT 0,
                updated_at TEXT NOT NULL DEFAULT ''
            );
            """;
        cmd.ExecuteNonQuery();

        // Segment markers table — timestamped bookmarks
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS video_segment_markers (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                video_id TEXT NOT NULL,
                time_ms INTEGER NOT NULL DEFAULT 0,
                title TEXT NOT NULL DEFAULT '',
                note TEXT NOT NULL DEFAULT '',
                thumbnail_path TEXT NOT NULL DEFAULT '',
                color TEXT NOT NULL DEFAULT '#F97316',
                created_at TEXT NOT NULL DEFAULT '',
                updated_at TEXT NOT NULL DEFAULT ''
            );
            """;
        cmd.ExecuteNonQuery();

        // Player settings key-value store
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS player_settings (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL DEFAULT ''
            );
            """;
        cmd.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var pragma = conn.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
        pragma.ExecuteNonQuery();
        return conn;
    }

    // ── Shared connection for perf (reader lifetime) ────────────────

    public SqliteConnection GetSharedConnection()
    {
        if (_sharedConnection == null)
        {
            _sharedConnection = Open();
        }
        return _sharedConnection;
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _sharedConnection, null)?.Dispose();
    }

    // ── Progress ────────────────────────────────────────────────────

    public void SaveVideoProgress(VideoItem video)
    {
        var now = DateTimeOffset.Now.ToString("O");
        lock (_writeLock)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO videos(id, title, author, folder_path, duration_ms, last_position_ms, reading_status, updated_at)
                VALUES ($id, $title, $author, $folderPath, $durationMs, $lastPositionMs, $readingStatus, $updatedAt)
                ON CONFLICT(id) DO UPDATE SET
                    last_position_ms = excluded.last_position_ms,
                    reading_status = excluded.reading_status,
                    updated_at = excluded.updated_at;
                """;
            cmd.Parameters.AddWithValue("$id", video.Id);
            cmd.Parameters.AddWithValue("$title", video.Title);
            cmd.Parameters.AddWithValue("$author", video.Author);
            cmd.Parameters.AddWithValue("$folderPath", video.FolderPath);
            cmd.Parameters.AddWithValue("$durationMs", video.DurationMs);
            cmd.Parameters.AddWithValue("$lastPositionMs", video.LastPositionMs);
            cmd.Parameters.AddWithValue("$readingStatus", video.ReadingStatus);
            cmd.Parameters.AddWithValue("$updatedAt", now);
            cmd.ExecuteNonQuery();
        }
    }

    public void SaveDuration(VideoItem video)
    {
        if (video.DurationMs <= 0) return;
        lock (_writeLock)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE videos SET duration_ms = $durationMs WHERE id = $id;";
            cmd.Parameters.AddWithValue("$id", video.Id);
            cmd.Parameters.AddWithValue("$durationMs", video.DurationMs);
            cmd.ExecuteNonQuery();
        }
    }

    public VideoItem? LoadVideo(string id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, title, author, folder_path, duration_ms, last_position_ms, reading_status FROM videos WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;

        return new VideoItem
        {
            Id = reader.GetString(0),
            Title = reader.IsDBNull(1) ? "" : reader.GetString(1),
            Author = reader.IsDBNull(2) ? "" : reader.GetString(2),
            FolderPath = reader.IsDBNull(3) ? "" : reader.GetString(3),
            DurationMs = reader.GetInt64(4),
            LastPositionMs = reader.GetInt64(5),
            ReadingStatus = reader.IsDBNull(6) ? "unread" : reader.GetString(6),
        };
    }

    // ── Segment Markers ─────────────────────────────────────────────

    public List<VideoSegmentMarker> LoadSegmentMarkers(string videoId)
    {
        var markers = new List<VideoSegmentMarker>();
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, video_id, time_ms, title, note, thumbnail_path, color, created_at, updated_at
            FROM video_segment_markers
            WHERE video_id = $videoId
            ORDER BY time_ms ASC;
            """;
        cmd.Parameters.AddWithValue("$videoId", videoId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            markers.Add(new VideoSegmentMarker
            {
                Id = reader.GetInt64(0),
                VideoId = reader.GetString(1),
                TimeMs = reader.GetInt64(2),
                Title = reader.IsDBNull(3) ? "" : reader.GetString(3),
                Note = reader.IsDBNull(4) ? "" : reader.GetString(4),
                ThumbnailPath = reader.IsDBNull(5) ? "" : reader.GetString(5),
                Color = reader.IsDBNull(6) ? "#F97316" : reader.GetString(6),
                CreatedAt = reader.IsDBNull(7) ? "" : reader.GetString(7),
                UpdatedAt = reader.IsDBNull(8) ? "" : reader.GetString(8),
            });
        }
        return markers;
    }

    public void UpsertSegmentMarker(VideoSegmentMarker marker)
    {
        var now = DateTimeOffset.Now.ToString("O");
        lock (_writeLock)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO video_segment_markers(video_id, time_ms, title, note, thumbnail_path, color, created_at, updated_at)
                VALUES ($videoId, $timeMs, $title, $note, $thumbnailPath, $color, $createdAt, $updatedAt)
                ON CONFLICT(id) DO UPDATE SET
                    title = excluded.title,
                    note = excluded.note,
                    color = excluded.color,
                    updated_at = excluded.updated_at;
                """;

            if (marker.Id > 0)
            {
                cmd.CommandText = """
                    UPDATE video_segment_markers
                    SET title = $title, note = $note, color = $color, updated_at = $updatedAt
                    WHERE id = $id;
                    """;
                cmd.Parameters.AddWithValue("$id", marker.Id);
            }

            cmd.Parameters.AddWithValue("$videoId", marker.VideoId);
            cmd.Parameters.AddWithValue("$timeMs", marker.TimeMs);
            cmd.Parameters.AddWithValue("$title", marker.Title);
            cmd.Parameters.AddWithValue("$note", marker.Note);
            cmd.Parameters.AddWithValue("$thumbnailPath", marker.ThumbnailPath);
            cmd.Parameters.AddWithValue("$color", marker.Color);
            cmd.Parameters.AddWithValue("$createdAt", now);
            cmd.Parameters.AddWithValue("$updatedAt", now);

            if (marker.Id > 0 && cmd.CommandText.StartsWith("UPDATE", StringComparison.Ordinal))
            {
                // no created_at in update
            }

            cmd.ExecuteNonQuery();

            // For new markers, get the auto-generated ID back
            if (marker.Id <= 0)
            {
                using var idCmd = conn.CreateCommand();
                idCmd.CommandText = "SELECT last_insert_rowid();";
                var result = idCmd.ExecuteScalar();
                if (result is long id && id > 0)
                {
                    marker.Id = id;
                }
            }
        }
    }

    public void DeleteSegmentMarker(long id)
    {
        lock (_writeLock)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM video_segment_markers WHERE id = $id;";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
    }

    // ── Player Settings ─────────────────────────────────────────────

    public string LoadSetting(string key, string defaultValue = "")
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM player_settings WHERE key = $key;";
        cmd.Parameters.AddWithValue("$key", key);
        var result = cmd.ExecuteScalar();
        return result is string s ? s : defaultValue;
    }

    public void SaveSetting(string key, string value)
    {
        lock (_writeLock)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO player_settings(key, value) VALUES ($key, $value)
                ON CONFLICT(key) DO UPDATE SET value = excluded.value;
                """;
            cmd.Parameters.AddWithValue("$key", key);
            cmd.Parameters.AddWithValue("$value", value);
            cmd.ExecuteNonQuery();
        }
    }

    // ── List videos (for future library integration) ────────────────

    public List<VideoItem> ListAllVideos()
    {
        var videos = new List<VideoItem>();
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, title, author, folder_path, duration_ms, last_position_ms, reading_status FROM videos ORDER BY title;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            videos.Add(new VideoItem
            {
                Id = reader.GetString(0),
                Title = reader.IsDBNull(1) ? "" : reader.GetString(1),
                Author = reader.IsDBNull(2) ? "" : reader.GetString(2),
                FolderPath = reader.IsDBNull(3) ? "" : reader.GetString(3),
                DurationMs = reader.GetInt64(4),
                LastPositionMs = reader.GetInt64(5),
                ReadingStatus = reader.IsDBNull(6) ? "unread" : reader.GetString(6),
            });
        }
        return videos;
    }
}
