using System.Diagnostics;
using System.Text.Json;

namespace Theater.Services;

public static class TagCatalog
{
    public static readonly string[] PresetColors =
    [
        "#F4B6C2", "#B7D7A8", "#A9CCE3", "#F7DC6F",
        "#D7BDE2", "#F5CBA7", "#AED6F1", "#A3E4D7"
    ];

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
        var preset = AllPresets.FirstOrDefault(item => string.Equals(item.Name, tag, StringComparison.OrdinalIgnoreCase));
        if (preset is not null)
        {
            return preset.Color;
        }

        return PresetColors[Math.Abs(tag.GetHashCode()) % PresetColors.Length];
    }
}

public sealed record TagPreset(string Name, string Category, string Color, bool IsExclusive);

/// <summary>本地 NSFW 预设 JSON 文件的反序列化模型。</summary>
public sealed class LocalPresetsFile
{
    public TagPreset[] Presets { get; set; } = [];
}
