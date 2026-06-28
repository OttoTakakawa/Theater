namespace Theater.Services;

public enum ImportFolderKind
{
    Empty,
    /// <summary>合集模式：整个文件夹作为一个视频作品</summary>
    Collection,
    /// <summary>作者模式：每个子文件夹作为一个视频作品，父文件夹名为作者</summary>
    AuthorFolder
}

public sealed class ImportFolderClassification
{
    public string RootPath { get; init; } = "";
    public ImportFolderKind Kind { get; init; }
    public int DirectContentCount { get; init; }
    public IReadOnlyList<string> ChildContentFolders { get; init; } = [];
}

public sealed class ImportFolderClassifier
{
    private readonly NaturalPathComparer _pathComparer = new();

    public ImportFolderClassification Classify(string rootPath)
    {
        if (!Directory.Exists(rootPath))
        {
            return new ImportFolderClassification
            {
                RootPath = rootPath,
                Kind = ImportFolderKind.Empty
            };
        }

        // V/ and P/ are collection markers (VideoTapes convention)
        var hasCollectionMarker = HasCollectionMarker(rootPath);
        var directContent = CountDirectContent(rootPath);

        var childContentFolders = FindChildContentFolders(rootPath);

        var kind = ImportFolderKind.Empty;
        if (hasCollectionMarker || directContent > 0)
        {
            // Root has V/P marker or direct content → Collection
            kind = ImportFolderKind.Collection;
        }
        else if (childContentFolders.Count > 0)
        {
            // No content in root, subfolders have content → AuthorFolder
            kind = ImportFolderKind.AuthorFolder;
        }

        return new ImportFolderClassification
        {
            RootPath = rootPath,
            Kind = kind,
            DirectContentCount = directContent,
            ChildContentFolders = childContentFolders
        };
    }

    private static bool HasCollectionMarker(string folder)
    {
        try
        {
            return (Directory.Exists(Path.Combine(folder, "V")) && HasAnyContent(Path.Combine(folder, "V")))
                || (Directory.Exists(Path.Combine(folder, "P")) && HasAnyContent(Path.Combine(folder, "P")));
        }
        catch
        {
            return false;
        }
    }

    private int CountDirectContent(string folder)
    {
        try
        {
            return Directory.EnumerateFiles(folder)
                .Count(f => ImageLoader.IsSupportedImage(f) ||
                            Videos.Services.VideoFileDetector.IsSupportedVideo(f));
        }
        catch
        {
            return 0;
        }
    }

    private IReadOnlyList<string> FindChildContentFolders(string rootPath)
    {
        try
        {
            return Directory.EnumerateDirectories(rootPath, "*", SearchOption.TopDirectoryOnly)
                .Where(folder => IsChildWorkFolder(folder))
                .OrderBy(path => path, _pathComparer)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static bool IsChildWorkFolder(string folder)
    {
        var name = Path.GetFileName(folder);
        // V/, P/ are collection markers, not child works
        if (string.Equals(name, "V", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "P", StringComparison.OrdinalIgnoreCase))
            return false;
        return HasAnyContent(folder);
    }

    private static bool HasAnyContent(string folder)
    {
        try
        {
            return Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Any(f => ImageLoader.IsSupportedImage(f) ||
                          Videos.Services.VideoFileDetector.IsSupportedVideo(f));
        }
        catch
        {
            return false;
        }
    }
}
