using Microsoft.Data.Sqlite;
using Theater.Videos.Models;

namespace Theater.Services;

public partial class LibraryDatabase
{
    // ── VideoItem-compatible overloads ──────────────────────────────

    public void SaveVideoProgress(VideoItem video)
    {
        SaveSetting($"video.{video.Id}.last_position_ms", video.LastPositionMs.ToString());
        SaveSetting($"video.{video.Id}.reading_status", video.ReadingStatus);
    }

    public void SaveDuration(VideoItem video)
    {
        if (video.DurationMs <= 0) return;
        SaveSetting($"video.{video.Id}.duration_ms", video.DurationMs.ToString());
    }

    public long LoadVideoDuration(string videoId)
    {
        var raw = LoadSetting($"video.{videoId}.duration_ms", "0");
        return long.TryParse(raw, out var v) ? v : 0;
    }

    public long LoadVideoPosition(string videoId)
    {
        var raw = LoadSetting($"video.{videoId}.last_position_ms", "0");
        return long.TryParse(raw, out var v) ? v : 0;
    }

    // ── Video segment markers (uses Theater.Videos.Models.VideoSegmentMarker directly) ────

    public List<VideoSegmentMarker> LoadSegmentMarkers(string videoId)
    {
        var markers = new List<VideoSegmentMarker>();
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, video_id, time_ms, title, note, thumbnail_path, color, created_at, updated_at
            FROM video_segment_markers
            WHERE video_id = $videoId
            ORDER BY time_ms ASC;
            """;
        command.Parameters.AddWithValue("$videoId", videoId);
        using var reader = command.ExecuteReader();
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
        using var connection = Open();
        using var command = connection.CreateCommand();

        if (marker.Id > 0)
        {
            command.CommandText = """
                UPDATE video_segment_markers
                SET title = $title, note = $note, color = $color, updated_at = $updatedAt
                WHERE id = $id;
                """;
            command.Parameters.AddWithValue("$id", marker.Id);
        }
        else
        {
            command.CommandText = """
                INSERT INTO video_segment_markers(video_id, time_ms, title, note, thumbnail_path, color, created_at, updated_at)
                VALUES ($videoId, $timeMs, $title, $note, $thumbnailPath, $color, $createdAt, $updatedAt);
                """;
            command.Parameters.AddWithValue("$videoId", marker.VideoId);
            command.Parameters.AddWithValue("$timeMs", marker.TimeMs);
            command.Parameters.AddWithValue("$thumbnailPath", marker.ThumbnailPath);
            command.Parameters.AddWithValue("$createdAt", now);
        }

        command.Parameters.AddWithValue("$title", marker.Title);
        command.Parameters.AddWithValue("$note", marker.Note);
        command.Parameters.AddWithValue("$color", marker.Color);
        command.Parameters.AddWithValue("$updatedAt", now);
        command.ExecuteNonQuery();

        if (marker.Id <= 0)
        {
            using var idCmd = connection.CreateCommand();
            idCmd.CommandText = "SELECT last_insert_rowid();";
            marker.Id = (long)idCmd.ExecuteScalar()!;
        }
    }

    public void DeleteSegmentMarker(long id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM video_segment_markers WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }
}
