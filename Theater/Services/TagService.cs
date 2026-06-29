namespace Theater.Services;

public static class TagService
{
    private static readonly char[] TagSeparators = [',', '，', ';', '；'];

    public static IEnumerable<string> ParseTags(string tags)
    {
        return tags.Split(TagSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    public static string FormatTags(IEnumerable<string> tags)
    {
        return string.Join(", ", tags
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    public static string GetCategory(string tag, IReadOnlyDictionary<string, string>? managedCategories = null)
    {
        return TagCatalog.GetCategory(tag, managedCategories);
    }

    public static string GetColor(string tag)
    {
        return TagCatalog.GetColor(tag);
    }

    public static string GetTextColor(string tag)
    {
        return TagCatalog.GetTextColor(tag);
    }

    public static bool IsMutuallyExclusiveCategory(string category)
    {
        return string.Equals(category, "作品", StringComparison.OrdinalIgnoreCase);
    }

    public static int CategoryOrder(string category)
    {
        return category switch
        {
            "作品" => 0,
            "类型" => 1,
            "规格" => 2,
            "公司" => 3,
            "身份" => 4,
            "服装" => 5,
            "行为" => 6,
            "体型" => 7,
            "玩法" => 8,
            "体位" => 9,
            "性格" => 10,
            "主题" => 11,
            "男主" => 12,
            "地区" => 13,
            "人种" => 14,
            _ => 99
        };
    }
}
