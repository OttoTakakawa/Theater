# Plan: Theater 详情页重构

Status: **pending approval**

## RALPLAN-DR Summary

### Principles (5)
1. **保留现有行为（Preserve）** — 编辑模式、元数据保存、Tag编辑、评分收藏继续原样工作
2. **复用优先（Reuse）** — PlayerWindow 播视频，ReaderWindow 看图集，不新建阅读器
3. **数据驱动可见性（Data-driven）** — HasVideo/HasImages 控制标签页显隐
4. **精确修改（Surgical）** — 只改 DetailShell 内部，不影响书库/首页/侧边栏
5. **删除不禁用（Delete）** — "切换卡片样式"菜单项整个删除，不隐藏

### Decision Drivers (Top 3)
1. **1080×820 空间约束** — 封面16:9占~270px，Tab内容区剩~400px，信息栏~60px
2. **标签切换 = Visibility 切换** — 不引入 ViewModel 或 TabControl，两个内容区 StackPanel 互相 toggle
3. **图集兼容路径** — ImageSetPaths → 临时填充 Pages → ReaderWindow，不改 ReaderWindow

### Options
| Option | Approach | Pros | Cons |
|--------|----------|------|------|
| **A (推荐)** | 重构 DetailShell 为 Grid + 标签 + 内容区 | 布局简洁，符合用户需求 | 编辑模式需要额外兼容处理 |
| B | 保持布局不变，内联可点击列表 | 改动最小 | 没有标签页，不符合用户要求 |
| C | 用 WPF TabControl | 标准控件 | 样式难匹配现有 UI |

## ADR

**Decision:** Option A — 重构 DetailShell 内部布局

**Drivers:** 用户明确要求标签页切换形式；现有 16:9 封面代码 (DetailVideoCover) 可直接复用；PlayerWindow 已有图集处理逻辑

**Alternatives considered:**
- Option B: 用户已经确认需要标签页形式，内联列表不够
- Option C: TabControl 的焦点隔离和模板化问题会破坏编辑模式

**Why chosen:** 用户需求来源；XAML Visibility 切换已有成熟先例（SetEditMode）；代码局部化

**Consequences:**
- 编辑模式需要在新布局和 EditFormPanel 之间增加一层 Visibility 切换
- 图集标签双击需要胶水代码 (ImageSetPaths → Pages → ReaderWindow)
- BookStyleIndex 保留在模型中（书库卡片需要），只删除菜单项

**Follow-ups:**
- 后续轮次处理 deferred 组件（侧边栏、首页文案、卡片样式、导入文案）

## Layout

```
┌─ DetailShell (1080×820, centered flyout) ────────────────────┐
│  ← 返回书库                   编辑    更多 ▾                  │  ← 保留现有顶部栏
├──────────────────────────────────────────────────────────────┤
│              [16:9 Cover (固定, cover.jpg/纯色+🎬)]         │  ← Row 0, ~270px
│                       ▶ 播放叠加                             │
├──────────────────────────────────────────────────────────────┤
│              标题 · 作者                                      │  ← Row 1, Auto
├──────────────────────────────────────────────────────────────┤
│  [视频集]   [图集]                        ← Tab Bar, ~40px  │  ← Row 2, Auto
├──────────────────────────────────────────────────────────────┤
│  ┌ 视频集标签激活 ───────────────────────────────────────┐   │
│  │ 🎬 正片.mp4    双击→PlayerWindow                       │   │  ← Row 3, *
│  │ 🎬 花絮.mkv                                            │   │
│  │ 🎬 特典.mkv                                            │   │
│  └───────────────────────────────────────────────────────┘   │
│  ┌ 图集标签激活 ───────────────────────────────────────┐   │
│  │ [封面] [设定集] [特典图片]      双击→ReaderWindow      │   │
│  │ (纯文字列表, 无缩略图预览)                             │   │
│  └───────────────────────────────────────────────────────┘   │
├──────────────────────────────────────────────────────────────┤
│  [▶ 播放全部]  Tag...  作者·视频N·时长·图集N·出品时间      │  ← Row 4, ~60px
└──────────────────────────────────────────────────────────────┘
```

## Implementation Steps

### Step 1: MainWindow.xaml — DetailShell 布局改造

**File:** `Theater/MainWindow.xaml`, lines 2290-2786

Replace the `DockPanel x:Name="DetailPanel"` with new Grid layout:

```xml
<Grid x:Name="DetailPanel" Visibility="Collapsed">
    <Grid.RowDefinitions>
        <RowDefinition Height="Auto"/>  <!-- 顶部栏 — 复用现有 (line 2247-2280) -->
        <RowDefinition Height="Auto"/>  <!-- 封面 (~270px) → 复用 DetailVideoCover + Title/Author -->
        <RowDefinition Height="Auto"/>  <!-- 标签栏 (~40px) -->
        <RowDefinition Height="*"/>     <!-- 内容区 (视频列表/图集列表) -->
        <RowDefinition Height="Auto"/>  <!-- 底部信息栏 (~60px) -->
    </Grid.RowDefinitions>
</Grid>
```

Keep the existing top bar (lines 2247-2280) and the existing `DetailVideoCover` (lines 2301-2334). Move them into the new Grid.

Add tab bar below cover:
- `x:Name="VideoTabButton"` with `Tag="video"`, Content="视频集"
- `x:Name="GalleryTabButton"` with `Tag="gallery"`, Content="图集"
- Click handler: `DetailTab_Click`
- Visibility bound to `HasVideo` / `HasImages`

Add two content panels sharing the same ScrollViewer:
- `x:Name="DetailVideoContent"` — `ItemsControl ItemsSource="{Binding VideoPaths}"`
- `x:Name="DetailGalleryContent"` — `ItemsControl ItemsSource="{Binding ImageSetPaths}"`

Delete from "更多" menu (line 2272): `<MenuItem Header="切换卡片样式" Click="CycleBookStyle_Click"/>`

### Step 2: MainWindow.xaml.cs — 新事件处理

**File:** `Theater/MainWindow.xaml.cs`

**2a. Add `_activeDetailTab` field and `DetailTab_Click` handler:**
```csharp
private string _activeDetailTab = "video";

private void DetailTab_Click(object sender, RoutedEventArgs e)
{
    if (sender is Button btn && btn.Tag is string tab)
    {
        _activeDetailTab = tab;
        UpdateDetailTabVisibility();
    }
}

private void UpdateDetailTabVisibility()
{
    bool hasVideo = _currentBook?.HasVideo ?? false;
    bool hasImages = _currentBook?.HasImages ?? false;
    
    VideoTabButton.Tag = _activeDetailTab == "video" ? "active" : null;
    GalleryTabButton.Tag = _activeDetailTab == "gallery" ? "active" : null;
    
    DetailVideoContent.Visibility = hasVideo && _activeDetailTab == "video"
        ? Visibility.Visible : Visibility.Collapsed;
    DetailGalleryContent.Visibility = hasImages && _activeDetailTab == "gallery"
        ? Visibility.Visible : Visibility.Collapsed;
}
```

**2b. Gallery tab double-click → populates Pages from ImageSetPaths → ReaderWindow:**
```csharp
private void DetailGalleryItem_MouseDoubleClick(object sender, MouseButtonEventArgs e)
{
    if (_currentBook is null) return;
    
    // Populate Pages from ImageSetPaths for ReaderWindow
    if (_currentBook.ImageSetPaths.Count > 0)
    {
        var allImages = _currentBook.ImageSetPaths
            .SelectMany(dir => {
                try { return Directory.EnumerateFiles(dir).Where(ImageLoader.IsSupportedImage); }
                catch { return Enumerable.Empty<string>(); }
            })
            .OrderBy(p => p)
            .ToList();
        _currentBook.Pages.Clear();
        foreach (var img in allImages) _currentBook.Pages.Add(img);
        _currentBook.PageCount = allImages.Count;
    }
    
    if (_currentBook.Pages.Count == 0)
    {
        StatusText.Text = "该作品没有可阅读图片。";
        return;
    }
    
    OpenReader(_currentBook); // extracted helper (see Step 3)
}
```

**2c. Modify `FillMetadataEditors` — update labels and populate tab content:**
- `MetaPageCountLabelText.Text = book.HasVideo ? "视频" : "页数"`
- `MetaCoverPageLabelText.Text = book.HasVideo ? "时长" : "封面页"`
- Set bottom info bar text
- Call `UpdateDetailTabVisibility()`

**2d. Modify `SetDetailVisible` — reset active tab on open:**
```csharp
if (visible && _currentBook != null)
{
    _activeDetailTab = _currentBook.HasVideo ? "video" : "gallery";
    UpdateDetailTabVisibility();
}
```

### Step 3: Extract `OpenReader` helper

**File:** `Theater/MainWindow.xaml.cs`

Extract the ReaderWindow-opening code from `OpenBook` (lines 5997-6015) into:
```csharp
private void OpenReader(MangaBook book)
{
    book.LastOpenedAt = DateTimeOffset.Now.ToString("O");
    var reader = new ReaderWindow(
        book, _database, _nextKeys, _prevKeys,
        ResolveNextBookRecommendations,
        nextBook => Dispatcher.InvokeAsync(() => OpenBookFromRecommendation(nextBook), ...),
        _coverPipeline,
        openDetailRequest: OpenBookDetailFromReader)
    { Owner = this };
    reader.Closed += (_, _) => { ... };
    reader.Show();
}
```

Modify `OpenBook` to call `OpenReader` instead of inline code.

### Step 4: Delete "切换卡片样式" menuitem

**File:** `Theater/MainWindow.xaml` line 2272 — delete the MenuItem
**File:** `Theater/MainWindow.xaml.cs` — delete `CycleBookStyle_Click` method (but keep `CycleBookStyle()` on MangaBook model)

### Step 5: Bottom info bar

Add new Border at bottom of DetailShell:
- Left: `Button "▶ 播放全部"` → `DetailPlayAll_Click` → calls `OpenVideoPlayer(_currentBook)`
- Center: reuse existing Tag chip display (`BookTagChips`)
- Right: TextBlock showing: `"{book.Author} · {book.VideoCountText} · {book.VideoDurationText} · {book.ImageCountText} · {book.ProducedAt}"`

## Acceptance Criteria

- [ ] Cover 16:9: 有 cover.jpg 显示 cover，无 cover 显示纯色+🎬
- [ ] ▶ 播放叠加按钮始终显示在有视频的作品上
- [ ] 标签栏：[视频集] [图集]，仅有一种内容时隐藏对应标签
- [ ] 双击视频条目 → PlayerWindow
- [ ] 双击图集条目 → ReaderWindow（ImageSetPaths → Pages 胶水代码）
- [ ] 底部信息栏：作者 · 视频N · 时长 · 图集N · 出品时间
- [ ] "▶ 播放全部" → PlayerWindow 播放当前选中视频
- [ ] 编辑模式完整保留，布局不变
- [ ] "切换卡片样式" 从菜单中消失，且不报错
- [ ] 页数 → 视频 / 封面页 → 时长（视频作品时）
- [ ] 0 警告 0 错误构建

## Risks & Mitigations

| Risk | Mitigation |
|------|-----------|
| 图集标签双击 → ImageSetPaths 到 Pages 转换抛出异常 | try/catch, StatusText 反馈 |
| 编辑模式在新 Grid 布局中被遮盖 | EditFormPanel 保持原有顶层位置 |
| BookStyleIndex 删除导致书库卡片崩 | 只删菜单项，不删模型属性 |
| 视频作品无 Pages 时 ReaderWindow 崩溃 | 构建 Pages 失败时降级提示 |

## Verification

1. `dotnet build Theater/Theater.csproj` — 0 errors 0 warnings
2. 打开视频作品 → 封面16:9 + ▶按钮 + 视频集标签 + 图集标签
3. 双击视频 → PlayerWindow 启动
4. 双击图集 → ReaderWindow 启动（展示子文件夹图片）
5. 编辑模式 → 切换正常，保存正常
6. "更多"菜单 → 没有"切换卡片样式"
7. 信息栏 → 显示视频数/时长/图集数
8. 纯漫画作品 → 保持旧布局（只修改标签文字）
