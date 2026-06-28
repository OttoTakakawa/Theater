# 标签体系文档

## 概述

Theater 的标签系统由两部分组成：

- **内置预设（SFW）** — 写在 `Services/TagCatalog.cs` 的 `BuiltInPresets` 数组中，提交到 git
- **本地预设（NSFW）** — 存储在 `Theater_Data/nsfw_tag_presets.json`，由 `.gitignore` 保护，不进入仓库

两部分在运行时会合并为 `AllPresets`，对标签查询、颜色、分类等接口透明。

## 分类总览（15 个类别，123 个预设标签）

> SFW = 55 个（git 追踪），NSFW = 68 个（本地 JSON）。

### SFW 类别（进 git）

| 排序 | 类别 | 互斥 | 标签 |
|------|------|:--:|------|
| 0 | 作品 | ✅ | 日本AV、国产、欧美 |
| 1 | 类型 |   | 引退作、出道作、记录类、感谢祭、合集、Easy、正规公司、情侣自拍 |
| 2 | 规格 |   | 中文字幕、无码流出、主观视角 |
| 3 | 公司 |   | マドンナ、アリスJAPAN、FALENO、IDEA、Kanbi、IRIS、なまなま、SOD、S1、Blacked、Tushy、Vixen |
| 12 | 男主 |   | 大熊探花、李寻欢、王安全、猫先生、田伯光、鬼脚七、康爱福 |
| 13 | 地区 |   | 湖南、河北、东北、山东、杭州、北京、上海、川渝 |
| 14 | 人种 |   | 黑皮 |

### NSFW 类别（本地 JSON）

| 排序 | 类别 | 标签 |
|------|------|------|
| 4 | 身份 | OL、护士、人妻、女教师、少妇、空姐、义姐妹、家事代、妓女 |
| 5 | 服装 | 黑丝、白丝、情趣内衣、和服、学生服、包臀裙、高跟鞋、浴衣、眼镜、体操服、连衣裙 |
| 6 | 行为 | 舔肛、内射、肛交、按摩、颜射、尻射、口爆、乳交、对白、足交、骚话、阴射 |
| 7 | 体型 | 大屁股、漂亮屁眼、巨臀、丰腴、白嫩、巨乳、高身长、脚底、苗条、黑逼 |
| 8 | 玩法 | 轮奸、自己掰开、多女一男、小马拉大车、拘束、骑乘、无套 |
| 9 | 体位 | 骑乘位、种付位、一字马、精品后入、火车便当、压着操 |
| 10 | 性格 | 表情丰富、欲求不满、M系、肉便器、清纯、反差 |
| 11 | 主题 | 淫趴、不伦、胁迫、上门服务、NTR、艳遇、媚黑、欲女、探花、淫妻 |

## NSFW 标签的本地管理

### 文件位置

```
Theater_Data/nsfw_tag_presets.json
```

运行时从可执行文件同级的 `Theater_Data/` 目录加载。源文件位于 `Theater/Data/nsfw_tag_presets.json`，**不会**自动复制到 `bin/.../Theater_Data/`，开发调试时如需 NSFW 标签需手动复制。

### JSON 格式

```json
{
  "presets": [
    {"name": "OL", "category": "身份", "color": "#096dd9", "isExclusive": false},
    {"name": "护士", "category": "身份", "color": "#096dd9", "isExclusive": false}
  ]
}
```

### 新环境部署

新 clone 仓库后，NSFW 标签文件**不会自动存在**（已被 gitignore）。需要从其他机器复制 `nsfw_tag_presets.json` 到 `Theater_Data/` 目录，或手动创建。

如果文件不存在，`LoadLocalPresets()` 返回空数组，应用正常运行，只是 NSFW 标签不可用。`Debug.WriteLine` 会输出日志提示。

### 自定义 NSFW 标签

可以直接编辑 JSON 文件添加/删除标签，下次启动生效。格式遵从 `TagPreset` 结构：

| 字段 | 类型 | 说明 |
|------|------|------|
| `name` | string | 标签名称（唯一） |
| `category` | string | 所属分类 |
| `color` | string | 十六进制颜色（如 `#cf1322`） |
| `isExclusive` | bool | 分类内是否互斥 |

## 代码架构

```
TagCatalog.cs
├── BuiltInPresets      ← SFW 标签（git 追踪）
├── LocalPresets        ← NSFW 标签（从 JSON 懒加载，线程安全）
├── AllPresets          ← 合并后的完整列表
├── GetCategory()       → 在 AllPresets 中查找
└── GetColor()          → 在 AllPresets 中查找

TagService.cs
├── IsMutuallyExclusiveCategory()  → 仅"作品"为互斥
└── CategoryOrder()                → 0-14 排序映射，其他=99

BatchImportAnalyzer.cs
└── CreateCandidate()  → 不再自动推断标签，Tags 恒为 ""
```

## 排除的类别

以下 SLibrary 类别**故意不迁移**：

| 类别 | 数量 | 原因 |
|------|------|------|
| 女优 | 52 | 人名数据，量大且属于旧数据 |
| 女星 | 10 | 同上 |
| 女主 | 2 | 同上 |

需要时可通过用户自定义标签功能手动添加。

## 已删除的漫画向类别

以下类别在视频化改造中删除（视频作品不适用）：

| 原排序 | 类别 | 标签 |
|------|------|------|
| 0 | 内容形态 | 单行本、同人志、CG、杂图合集、画集、短篇 |
| 1 | 色彩规格 | 全彩、黑白、部分彩色 |
| 2 | 画质规格 | 高清、超清、扫图、修图版 |

`BatchImportAnalyzer.InferTags()` 及其辅助方法（`AnalyzeImage` / `InferQualityTag` / `PickSamplePages` 等）已一并删除，导入时不再为漫画作品自动打标签。
