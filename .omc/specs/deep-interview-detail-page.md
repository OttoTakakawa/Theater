# Deep Interview Spec: Theater 详情页重构

## Metadata
- Interview ID: theater-ui-rebuild
- Rounds: 2
- Final Ambiguity Score: 11%
- Type: brownfield
- Generated: 2026-06-27
- Threshold: 0.2 (20%)
- Threshold Source: default
- Status: PASSED

## Clarity Breakdown
| Dimension | Score | Weight | Weighted |
|-----------|-------|--------|----------|
| Goal Clarity | 0.95 | 35% | 0.3325 |
| Constraint Clarity | 0.90 | 25% | 0.2250 |
| Success Criteria | 0.80 | 25% | 0.2000 |
| Context Clarity | 0.90 | 15% | 0.1350 |
| **Total Clarity** | | | **0.8925** |
| **Ambiguity** | | | **~11%** |

## Topology
| Component | Status | Description | Coverage / Deferral Note |
|-----------|--------|-------------|--------------------------|
| detail-page | active | 以视频作品为中心的详情页重构 | 本规格文档全部覆盖 |
| sidebar-nav | deferred | 侧边栏导航 | 用户确认优先详情页 |
| home-empty-state | deferred | 首页/空状态文案 | 用户确认优先详情页 |
| card-style | deferred | 瀑布流卡片样式 | 用户确认优先详情页 |
| import-text | deferred | 导入/状态栏文案 | 用户确认优先详情页 |

## Goal
将 Theater 的详情页从漫画时代的竖构图（320×480 左封面 + 右信息）重构为以视频作品为中心的居中 flyout 布局：固定 16:9 cover 区在上、标签页切换（视频集/图集）、信息区在底部。复用已有 PlayerWindow 和 ReaderWindow，不新建阅读器。

## Constraints
1. ✅ 详情页保持居中 flyout 形态（`DetailShell`: `Grid.Column=1 ColumnSpan=2`, `HorizontalAlignment=Center`, `Width=1080`, `MaxHeight=820`）
2. ✅ 封面区域固定尺寸，始终显示 cover（无 cover 时纯色背景 + 🎬 图标）
3. ✅ 标签页：[视频集] [图集]，非激活标签不显示内容
4. ✅ 仅有视频无图集 → 隐藏图集标签；仅有图集无视频（视频不见）→ 隐藏视频集标签
5. ✅ 不使用 lightbox 看图——图集双击走 ReaderWindow（旧漫画翻页器）
6. ✅ 编辑模式在保留当前 UI 更新的前提下保留现有按钮

## Acceptance Criteria
- [ ] 视频作品打开详情页时，封面区显示 16:9 比例 cover（或纯色+🎬 图标），带 ▶ 播放按钮叠加
- [ ] 封面区下方：标题 · 作者
- [ ] 标签栏：[视频集] [图集]，点击切换
- [ ] 视频集标签激活：显示视频文件列表，双击→PlayerWindow
- [ ] 图集标签激活：显示图集缩略图网格，双击→ReaderWindow
- [ ] "▶ 播放全部" 只播放当前选中的视频
- [ ] F6 面板中播放列表优先只放当前合集内的列表
- [ ] 编辑模式保留：标题/作者/Tag 编辑、评分 ☆1-5、收藏、观看状态
- [ ] 切换卡片样式 → 整个删除，不显示
- [ ] 页面书签和目录预览 → 仅在视频有图集时有效
- [ ] 信息卡字段：作者·视频数·时长·图集数·出品时间
- [ ] "▶ 播放" 按钮（有视频时）/ "开始阅读" 按钮（无视频仅图集时）
- [ ] 返回书库按钮、编辑、更多 → 保留现有位置

## Non-Goals
- ❌ 不重构侧边栏导航（deferred）
- ❌ 不改首页/空状态文案（deferred）
- ❌ 不改瀑布流卡片样式（deferred）
- ❌ 不改导入流程文案（deferred）
- ❌ 不新建 lightbox 看图
- ❌ 不新建独立窗口做详情页

## Assumptions Exposed & Resolved
| Assumption | Challenge | Resolution |
|------------|-----------|------------|
| 详情页是右侧面板 | 用户纠正为居中 flyout | 确认是 `Grid.Column=1 ColumnSpan=2, HorizontalAlignment=Center` |
| 图片用 lightbox 预览 | 用户要求复用 ReaderWindow | ✅ 明确用旧漫画翻页器 |
| 切换样式对视频有效 | 用户要求全删 | ✅ 不存在了 |
| 书签/目录对所有作品有效 | 用户限制为有图集时才显示 | ✅ 仅在图片集合有效 |
| 播放全部=播放所有 | 用户选"只播当前" | ✅ 只播放选中的视频 |

## Technical Context
**关键词修改清单：**（MainWindow.xaml + MainWindow.xaml.cs）

| 旧文本 | 新文本 |
|--------|--------|
| "开始阅读" | "▶ 播放"（有视频）/ 保留"开始阅读"（无视频仅图集） |
| "页数"标签 → "{Binding VideoCountText}" | "视频" |
| "封面页"标签 → "{Binding VideoDurationText}" | "时长" |
| 信息卡 MetaPageCountValueText | 视频作品显示 VideoCountText |
| 信息卡 MetaCoverPageValueText | 视频作品显示 VideoDurationText |
| 切换到 ReaderWindow | 仅在图集标签双击时，复用现有代码 |
| 切换到 PlayerWindow | 视频标签双击 / "▶ 播放" 按钮 |

**关键文件：**
- `Theater/MainWindow.xaml` — DetailShell 内部布局（cover + 标签 + 列表）
- `Theater/MainWindow.xaml.cs` — FillMetadataEditors, OpenBook, 标签切换逻辑
- `Theater/Models/MangaBook.cs` — 已有 HasVideo/HasImages/VideoCountText/DurationText 等属性
- 图集双击复用现有 ReaderWindow 代码

## Ontology (Key Entities)
| Entity | Type | Fields | Relationships |
|--------|------|--------|---------------|
| 视频作品 | core domain | title, author, cover, videos[], imageSets[], tags | has many videos, has many image sets |
| 视频文件 | supporting | path, filename | belongs to 视频作品, plays in PlayerWindow |
| 图集/图片集 | supporting | folderPath, name, images[] | belongs to 视频作品, opens in ReaderWindow |
| 封面 | supporting | path (cover.jpg) | belongs to 视频作品 |
| DetailShell | UI | Width=1080, MaxHeight=820, centered | hosts cover, tabs, info |
| PlayerWindow | UI | VLC player | plays video files |
| ReaderWindow | UI | manga-style reader | browses image sets |

## Interview Transcript
<details>
<summary>Full Q&A (2 rounds)</summary>

### Round 0
**Q:** 拓扑确认（5个大块）
**A:** 详情页是最重要的

### Round 1
**Q:** 详情页布局？横图+标签页+分区
**A:** 提供 ASCII mockup: 固定cover+tab切换[视频集/图集]，视频→PlayerWindow，图集→ReaderWindow

### Round 2
**Q:** 详情页是右面板还是居中？约束+边界
**A:** 用户纠正为居中 flyout。确认代码结构 DetailShell 在 Center

### Final Details
**Q:** 6个细节问题
**A:** 全部一次回答：纯色背景+🎬；只播当前；编辑页保留按钮；切换样式全删；书签仅在图片时有效；信息正常更新
</details>
