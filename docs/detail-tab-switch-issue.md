# 详情页标签页切换故障记录

## 现象
详情页中点击「图集」标签页按钮无反应，内容不切换。视频集标签页正常。

## 根因

### 1. 点击事件处理
`DetailTab_Click` 使用 C# 模式匹配 `sender is Button { Tag: string tab }`。理论上该语法在 WPF 中正确，但在项目环境中不确定是否为编译或运行时匹配失败。改为传统 `as` 转换后正常工作：

```csharp
// 修复前 — 模式匹配，点击图集无响应
if (sender is Button { Tag: string tab }) { ... }

// 修复后 — 显式转换，正常触发
var btn = sender as System.Windows.Controls.Button;
if (btn == null) return;
var tag = btn.Tag as string;
```

### 2. 可见性条件阻挡
`UpdateDetailTabVisibility` 中原有 `hasImages && _activeDetailTab == "gallery"` 条件。当 `book.ImageSetPaths` 为空（导入时未正确填充图集路径）时，`hasImages == false`，导致即使点击图集标签，内容区依然 `Collapsed`。修复后改为仅按 `_activeDetailTab` 切换，不再依赖数据存在性：

```csharp
// 修复前 — 数据为空时标签无法显示
DetailGalleryContent.Visibility = hasImages && _activeDetailTab == "gallery"
    ? Visibility.Visible : Visibility.Collapsed;

// 修复后 — 仅按标签切换显示
DetailGalleryContent.Visibility = _activeDetailTab == "gallery"
    ? Visibility.Visible : Visibility.Collapsed;
```

### 3. 图集路径显示
图集列表的 `ItemsControl` 绑定 `ImageSetPaths`（子文件夹全路径），原样显示完整路径如 `G:\...\P\p`。改为 `PathToFileName` 转换器后只显示文件夹名 `p`。

## 涉及文件
| 文件 | 改动 |
|------|------|
| `Theater/MainWindow.xaml.cs` | DetailTab_Click 改用显式转换；UpdateDetailTabVisibility 去除 hasImages 条件 |
| `Theater/MainWindow.xaml` | 图集列表绑定改为 `{Binding Converter={StaticResource PathToFileName}}` |

## 验证
- 打开视频作品（含 V/ P/ 子目录）
- 点击「图集」标签 → 内容区显示图集列表
- 点击「视频集」标签 → 切回视频列表
- 图集条目显示文件夹名（非全路径）
