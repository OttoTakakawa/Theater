using System.Diagnostics;
using System.Text.Json;

namespace Theater.Services;

public enum TagColorMode { Dark, Light }

public enum TagPaletteTheme { Classic, Vintage, Cool }

public static class TagCatalog
{
    public static readonly string[] PresetColors =
    [
        "#F4B6C2", "#B7D7A8", "#A9CCE3", "#F7DC6F",
        "#D7BDE2", "#F5CBA7", "#AED6F1", "#A3E4D7"
    ];

    public static TagColorMode CurrentColorMode { get; set; } = TagColorMode.Dark;
    public static TagPaletteTheme CurrentPaletteTheme { get; set; } = TagPaletteTheme.Classic;

    private static readonly string DarkTextOnLight = "#111827";
    private static readonly string LightTextOnDark = "#FFFFFF";

    private static readonly Dictionary<string, string> PaletteClassicDark = new()
    {
        ["作品"] = "#531DAB", ["类型"] = "#1D39C4", ["规格"] = "#389E0D",
        ["公司"] = "#00796B", ["身份"] = "#C41D7F", ["服装"] = "#D4380D",
        ["行为"] = "#08979C", ["体型"] = "#5B8C00", ["玩法"] = "#0050B3",
        ["体位"] = "#722ED1", ["性格"] = "#D48806", ["主题"] = "#13C2C2",
        ["男主"] = "#E65100", ["地区"] = "#475569", ["人种"] = "#614700",
        ["自定义"] = "#6B7280",
    };

    private static readonly Dictionary<string, string> PaletteClassicLight = new()
    {
        ["作品"] = "#F3E8FF", ["类型"] = "#DBEAFE", ["规格"] = "#DCFCE7",
        ["公司"] = "#CCFBF1", ["身份"] = "#FCE7F3", ["服装"] = "#FFE4E1",
        ["行为"] = "#CFFAFE", ["体型"] = "#ECFCCB", ["玩法"] = "#E0E7FF",
        ["体位"] = "#F5D0FE", ["性格"] = "#FEF3C7", ["主题"] = "#D6F5F5",
        ["男主"] = "#FFEDD5", ["地区"] = "#E2E8F0", ["人种"] = "#F5EBD8",
        ["自定义"] = "#E5E7EB",
    };

    private static readonly Dictionary<string, string> PaletteVintageDark = new()
    {
        ["作品"] = "#6B1F47", ["类型"] = "#0F3057", ["规格"] = "#005377",
        ["公司"] = "#1B4332", ["身份"] = "#9D4EDD", ["服装"] = "#BC4749",
        ["行为"] = "#2A9D8F", ["体型"] = "#6A994E", ["玩法"] = "#264653",
        ["体位"] = "#E76F51", ["性格"] = "#B5838D", ["主题"] = "#1A5490",
        ["男主"] = "#9E2A2B", ["地区"] = "#4A4E69", ["人种"] = "#774936",
        ["自定义"] = "#6B7280",
    };

    private static readonly Dictionary<string, string> PaletteVintageLight = new()
    {
        ["作品"] = "#F5DCE5", ["类型"] = "#D6E2EC", ["规格"] = "#CCDFE9",
        ["公司"] = "#CFE0D3", ["身份"] = "#E8D5F5", ["服装"] = "#F2D2D3",
        ["行为"] = "#D2E8E4", ["体型"] = "#DFE8D2", ["玩法"] = "#D2D9DD",
        ["体位"] = "#FAD4C6", ["性格"] = "#E8DADE", ["主题"] = "#D2DDE9",
        ["男主"] = "#F0CECF", ["地区"] = "#DEDFE5", ["人种"] = "#E8D8C8",
        ["自定义"] = "#E5E7EB",
    };

    private static readonly Dictionary<string, string> PaletteCoolDark = new()
    {
        ["作品"] = "#6D28D9", ["类型"] = "#1E40AF", ["规格"] = "#0E7490",
        ["公司"] = "#115E59", ["身份"] = "#7E22CE", ["服装"] = "#BE185D",
        ["行为"] = "#0369A1", ["体型"] = "#047857", ["玩法"] = "#3730A3",
        ["体位"] = "#5B21B6", ["性格"] = "#9333EA", ["主题"] = "#0F766E",
        ["男主"] = "#DB2777", ["地区"] = "#475569", ["人种"] = "#6B7280",
        ["自定义"] = "#64748B",
    };

    private static readonly Dictionary<string, string> PaletteCoolLight = new()
    {
        ["作品"] = "#EDE9FE", ["类型"] = "#DBEAFE", ["规格"] = "#CFFAFE",
        ["公司"] = "#CCFBF1", ["身份"] = "#F3E8FF", ["服装"] = "#FCE7F3",
        ["行为"] = "#E0F2FE", ["体型"] = "#D1FAE5", ["玩法"] = "#E0E7FF",
        ["体位"] = "#F3E8FF", ["性格"] = "#FAE8FF", ["主题"] = "#CCFBF1",
        ["男主"] = "#FBCFE8", ["地区"] = "#E2E8F0", ["人种"] = "#E5E7EB",
        ["自定义"] = "#E5E7EB",
    };

    private static Dictionary<string, string> GetCurrentPalette()
    {
        return (CurrentPaletteTheme, CurrentColorMode) switch
        {
            (TagPaletteTheme.Classic, TagColorMode.Dark) => PaletteClassicDark,
            (TagPaletteTheme.Classic, TagColorMode.Light) => PaletteClassicLight,
            (TagPaletteTheme.Vintage, TagColorMode.Dark) => PaletteVintageDark,
            (TagPaletteTheme.Vintage, TagColorMode.Light) => PaletteVintageLight,
            (TagPaletteTheme.Cool, TagColorMode.Dark) => PaletteCoolDark,
            (TagPaletteTheme.Cool, TagColorMode.Light) => PaletteCoolLight,
            _ => PaletteClassicDark,
        };
    }

    public static readonly TagPreset[] BuiltInPresets =
    [
        // ===== 作品（SFW，互斥）=====
        new("日本AV", "作品", "#888888", true),
        new("国产", "作品", "#888888", true),
        new("欧美", "作品", "#888888", true),

        // ===== 类型（SLibrary 迁移 — SFW）=====
        new("引退作", "类型", "#1d39c4", false),
        new("出道作", "类型", "#1d39c4", false),
        new("记录类", "类型", "#1d39c4", false),
        new("感谢祭", "类型", "#1d39c4", false),
        new("合集", "类型", "#1d39c4", false),
        new("Easy", "类型", "#1d39c4", false),
        new("正规公司", "类型", "#1d39c4", false),
        new("情侣自拍", "类型", "#1d39c4", false),

        // ===== 规格（SFW）=====
        new("中文字幕", "规格", "#389e0d", false),
        new("无码流出", "规格", "#389e0d", false),
        new("主观视角", "规格", "#389e0d", false),

        // ===== 公司（SLibrary 迁移 — SFW）=====
        new("マドンナ", "公司", "#00796b", false),
        new("アリスJAPAN", "公司", "#00796b", false),
        new("FALENO", "公司", "#00796b", false),
        new("IDEA", "公司", "#00796b", false),
        new("Kanbi", "公司", "#00796b", false),
        new("IRIS", "公司", "#00796b", false),
        new("なまなま", "公司", "#00796b", false),
        new("SOD", "公司", "#00796b", false),
        new("S1", "公司", "#00796b", false),
        new("Blacked", "公司", "#00796b", false),
        new("Tushy", "公司", "#00796b", false),
        new("Vixen", "公司", "#00796b", false),

        // ===== 男主（SLibrary 迁移 — SFW）=====
        new("大熊探花", "男主", "#e65100", false),
        new("李寻欢", "男主", "#e65100", false),
        new("王安全", "男主", "#e65100", false),
        new("猫先生", "男主", "#e65100", false),
        new("田伯光", "男主", "#e65100", false),
        new("鬼脚七", "男主", "#e65100", false),
        new("康爱福", "男主", "#e65100", false),

        // ===== 地区（SLibrary 迁移 — SFW）=====
        new("湖南", "地区", "#8D9DB6", false),
        new("河北", "地区", "#8D9DB6", false),
        new("东北", "地区", "#8D9DB6", false),
        new("山东", "地区", "#8D9DB6", false),
        new("杭州", "地区", "#8D9DB6", false),
        new("北京", "地区", "#8D9DB6", false),
        new("上海", "地区", "#8D9DB6", false),
        new("川渝", "地区", "#8D9DB6", false),

        // ===== 人种（SLibrary 迁移 — SFW）=====
        new("黑皮", "人种", "#B8A89A", false),
    ];

    // ==== 本地 NSFW 预设（从 JSON 懒加载）====

    private static TagPreset[]? _localPresets;
    private static readonly object _localPresetsLock = new();

    public static TagPreset[] LocalPresets
    {
        get
        {
            if (_localPresets is null)
            {
                lock (_localPresetsLock)
                {
                    _localPresets ??= LoadLocalPresets();
                }
            }
            return _localPresets;
        }
    }

    public static TagPreset[] AllPresets
    {
        get
        {
            var builtIn = BuiltInPresets;
            var local = LocalPresets;
            var merged = new TagPreset[builtIn.Length + local.Length];
            Array.Copy(builtIn, merged, builtIn.Length);
            Array.Copy(local, 0, merged, builtIn.Length, local.Length);
            return merged;
        }
    }

    private static TagPreset[] LoadLocalPresets()
    {
        try
        {
            var exeDir = AppContext.BaseDirectory;
            var jsonPath = Path.Combine(exeDir, "Theater_Data", "nsfw_tag_presets.json");

            if (!File.Exists(jsonPath))
            {
                Debug.WriteLine($"[TagCatalog] NSFW presets file not found: {jsonPath}");
                return [];
            }

            var json = File.ReadAllText(jsonPath);
            var data = JsonSerializer.Deserialize<LocalPresetsFile>(json);

            if (data?.Presets is null || data.Presets.Length == 0)
            {
                Debug.WriteLine("[TagCatalog] NSFW presets file is empty or invalid.");
                return [];
            }

            Debug.WriteLine($"[TagCatalog] Loaded {data.Presets.Length} local NSFW presets.");
            return data.Presets;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TagCatalog] Failed to load local presets: {ex.Message}");
            return [];
        }
    }

    public static string GetCategory(string tag, IReadOnlyDictionary<string, string>? managedCategories = null)
    {
        if (managedCategories is not null && managedCategories.TryGetValue(tag, out var category))
        {
            return category;
        }

        return AllPresets.FirstOrDefault(item => string.Equals(item.Name, tag, StringComparison.OrdinalIgnoreCase))?.Category
            ?? "自定义";
    }

    public static string GetColor(string tag)
    {
        var category = GetCategory(tag);
        var palette = GetCurrentPalette();
        if (palette.TryGetValue(category, out var color))
        {
            return color;
        }

        return PresetColors[Math.Abs(tag.GetHashCode()) % PresetColors.Length];
    }

    public static string GetTextColor(string tag)
    {
        var category = GetCategory(tag);
        var palette = GetCurrentPalette();
        if (palette.ContainsKey(category))
        {
            return CurrentColorMode == TagColorMode.Dark ? LightTextOnDark : DarkTextOnLight;
        }
        return GetTextColorForBackground(GetColor(tag));
    }

    public static string GetTextColorForBackground(string backgroundColor)
    {
        return ComputeLuminance(backgroundColor) < 0.5 ? LightTextOnDark : DarkTextOnLight;
    }

    private static double ComputeLuminance(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex) || hex.Length < 7 || hex[0] != '#')
        {
            return 0.5;
        }

        try
        {
            var r = Convert.ToInt32(hex.Substring(1, 2), 16) / 255.0;
            var g = Convert.ToInt32(hex.Substring(3, 2), 16) / 255.0;
            var b = Convert.ToInt32(hex.Substring(5, 2), 16) / 255.0;
            return 0.2126 * r + 0.7152 * g + 0.0722 * b;
        }
        catch
        {
            return 0.5;
        }
    }
}

public sealed record TagPreset(string Name, string Category, string Color, bool IsExclusive);

/// <summary>本地 NSFW 预设 JSON 文件的反序列化模型。</summary>
public sealed class LocalPresetsFile
{
    public TagPreset[] Presets { get; set; } = [];
}
