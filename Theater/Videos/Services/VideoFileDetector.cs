namespace Theater.Videos.Services;

public static class VideoFileDetector
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm",
        ".flv", ".ts", ".m2ts", ".mpg", ".mpeg", ".m4v"
    };

    public static bool IsSupportedVideo(string path)
    {
        var ext = Path.GetExtension(path);
        if (!SupportedExtensions.Contains(ext)) return false;
        // .ts 扩展名既可以是视频 (MPEG-TS) 也可以是 TypeScript，
        // 通过文件大小区分：小于 1MB 的 .ts 文件大概率不是视频
        if (ext.Equals(".ts", StringComparison.OrdinalIgnoreCase))
        {
            return GetFileSize(path) > 1_000_000;
        }
        return true;
    }

    public static long GetFileSize(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch
        {
            return 0;
        }
    }
}
