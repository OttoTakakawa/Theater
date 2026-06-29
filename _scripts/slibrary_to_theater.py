"""
SLibrary → Theater 数据转译脚本
从 SLibrary 的 _site/data.json 拉取作品元数据，写入 Theater 的 app.db。

用法:
    python slibrary_to_theater.py              # dry-run，只输出报告
    python slibrary_to_theater.py --apply      # 真正写入 app.db

数据安全:
    - SLibrary 端只读，不修改任何文件
    - Theater 端：如果目标 app.db 已存在，先备份再覆盖
    - 不写 library_roots（避免 Theater 重扫出垃圾）
"""
import json, os, sys, hashlib, re, sqlite3, shutil
from pathlib import Path
from datetime import datetime
from collections import Counter, defaultdict

# ==== 配置 ====
SLIB_ROOT = Path(r"G:\Lanweilig\Heimlich\Japan\SLibrary")
SLIB_DATA = SLIB_ROOT / "_site" / "data.json"
SLIB_SITE = SLIB_ROOT / "_site"

THEATER_DATA_DIR = Path(r"G:\Lanweilig\Heimlich\Japan\Theater\Data")
THEATER_DB = THEATER_DATA_DIR / "app.db"

THEATER_SITE_ROOT = Path(r"G:\Lanweilig\Heimlich\Japan\Theater")
THEATER_SITE_IMAGES = THEATER_SITE_ROOT / "Theater_Data" / "imported_covers"

# SLibrary region → Theater 作品互斥 tag
REGION_TO_WORK_TAG = {
    "日本": "日本AV",
    "国产": "国产",
    "欧美": "欧美",
}

# SLibrary actress 分类 → 主角前缀
ACTRESS_CATS = {"女优", "女星", "女主"}

# ==== BookId 复现（与 Theater BookId.cs 一致）====
def book_id_from_path(folder_path: str) -> str:
    # Path.GetFullPath + TrimEnd(separator) + ToLowerInvariant
    normalized = os.path.abspath(folder_path).rstrip("\\/").lower()
    sha = hashlib.sha256(normalized.encode("utf-8")).hexdigest()
    return sha[:16]

# ==== source 路径转换 ====
def file_url_to_path(url):
    if not isinstance(url, str) or not url.startswith("file:///"):
        return None
    p = url[8:].replace("/", "\\")
    return p

# ==== tag 转换 ====
def convert_tags(slib_tags, region, work_id):
    """将 SLibrary [[cat, val], ...] 转成 Theater tag 字符串。
    返回 (theater_tag_string, list_of_(tag_name, category, is_exclusive))
    """
    parts = []          # tag 显示字符串的片段
    managed = []        # managed_tags 记录
    seen = set()

    def add(tag_name, category, is_exclusive=False):
        key = tag_name.lower()
        if key in seen:
            return
        seen.add(key)
        parts.append(tag_name)
        managed.append((tag_name, category, is_exclusive))

    # 1. 地区 → 作品互斥 tag
    work_tag = REGION_TO_WORK_TAG.get(region)
    if work_tag:
        add(work_tag, "作品", is_exclusive=True)

    # 2. 遍历 SLibrary tags
    for entry in slib_tags:
        if not isinstance(entry, list) or len(entry) != 2:
            continue
        cat, val = entry[0], entry[1]
        if not cat or not val:
            continue

        if cat in ACTRESS_CATS:
            # 女优/女星/女主 → 主角/地区/姓名
            tag_name = f"主角/{region}/{val}" if region else f"主角/{val}"
            add(tag_name, "主角", is_exclusive=False)
        elif cat == "作品":
            # 作品分类已在上面通过 region 处理，跳过 SLibrary 的原始值
            continue
        else:
            # 其他分类直通
            add(val, cat, is_exclusive=False)

    return ", ".join(parts), managed

# ==== cover 路径处理 ====
def resolve_cover(cover_dst, cover_raw):
    """返回 Theater cover_image_path 绝对路径，或 None。
    cover_dst 是 SLibrary _site 内的相对路径（如 images/xxx.png）。
    直接指向 SLibrary 的 _site/images/xxx.png，不复制（Theater 支持绝对路径）。
    """
    if cover_dst:
        p = SLIB_SITE / cover_dst
        if p.exists():
            return str(p)
    return None

# ==== schema 初始化（复制自 LibraryDatabase.cs）====
SCHEMA_SQL = """
PRAGMA journal_mode=WAL;
PRAGMA synchronous=NORMAL;

CREATE TABLE IF NOT EXISTS books (
    id TEXT PRIMARY KEY,
    title TEXT NOT NULL,
    author TEXT NOT NULL DEFAULT '',
    character_name TEXT NOT NULL DEFAULT '',
    foreign_name TEXT NOT NULL DEFAULT '',
    tags TEXT NOT NULL DEFAULT '',
    produced_at TEXT NOT NULL DEFAULT '',
    imported_at TEXT NOT NULL DEFAULT '',
    summary TEXT NOT NULL DEFAULT '',
    folder_path TEXT NOT NULL UNIQUE,
    page_count INTEGER NOT NULL DEFAULT 0,
    total_bytes INTEGER NOT NULL DEFAULT 0,
    cover_page_index INTEGER NOT NULL DEFAULT 0,
    book_style INTEGER NOT NULL DEFAULT -1,
    last_read_page_index INTEGER NOT NULL DEFAULT 0,
    read_count INTEGER NOT NULL DEFAULT 0,
    reading_status TEXT NOT NULL DEFAULT 'unread',
    is_favorite INTEGER NOT NULL DEFAULT 0,
    rating REAL NOT NULL DEFAULT 0,
    is_missing INTEGER NOT NULL DEFAULT 0,
    is_hidden INTEGER NOT NULL DEFAULT 0,
    is_privacy_cover INTEGER NOT NULL DEFAULT 0,
    updated_at TEXT NOT NULL,
    last_opened_at TEXT NOT NULL DEFAULT '',
    duration_ms INTEGER NOT NULL DEFAULT 0,
    last_position_ms INTEGER NOT NULL DEFAULT 0,
    video_paths TEXT NOT NULL DEFAULT '[]',
    image_set_paths TEXT NOT NULL DEFAULT '[]',
    cover_image_path TEXT NOT NULL DEFAULT ''
);

CREATE TABLE IF NOT EXISTS library_roots (
    path TEXT PRIMARY KEY,
    updated_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS shortcuts (
    action_id TEXT PRIMARY KEY,
    keybinding TEXT NOT NULL,
    updated_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS app_settings (
    key TEXT PRIMARY KEY,
    value TEXT NOT NULL,
    updated_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS managed_tags (
    name TEXT PRIMARY KEY,
    category TEXT NOT NULL DEFAULT '自定义',
    is_exclusive INTEGER NOT NULL DEFAULT 0,
    updated_at TEXT NOT NULL,
    color TEXT NOT NULL DEFAULT ''
);

CREATE TABLE IF NOT EXISTS managed_authors (
    name TEXT PRIMARY KEY,
    updated_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS suppressed_tags (
    name TEXT PRIMARY KEY,
    updated_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS book_bookmarks (
    book_id TEXT NOT NULL,
    page_index INTEGER NOT NULL,
    label TEXT NOT NULL DEFAULT '',
    created_at TEXT NOT NULL,
    PRIMARY KEY (book_id, page_index),
    FOREIGN KEY (book_id) REFERENCES books(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS video_segment_markers (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    video_id TEXT NOT NULL,
    time_ms INTEGER NOT NULL DEFAULT 0,
    title TEXT NOT NULL DEFAULT '',
    note TEXT NOT NULL DEFAULT '',
    thumbnail_path TEXT NOT NULL DEFAULT '',
    color TEXT NOT NULL DEFAULT '#F97316',
    created_at TEXT NOT NULL DEFAULT '',
    updated_at TEXT NOT NULL DEFAULT ''
);

CREATE INDEX IF NOT EXISTS idx_books_author ON books(author);
CREATE INDEX IF NOT EXISTS idx_books_reading_status ON books(reading_status);
CREATE INDEX IF NOT EXISTS idx_books_is_favorite ON books(is_favorite);
CREATE INDEX IF NOT EXISTS idx_books_is_hidden ON books(is_hidden);
CREATE INDEX IF NOT EXISTS idx_books_folder_path ON books(folder_path);
CREATE INDEX IF NOT EXISTS idx_books_last_opened_at ON books(last_opened_at);
"""

# ==== 主逻辑 ====
def main():
    apply_mode = "--apply" in sys.argv

    if not SLIB_DATA.exists():
        print(f"FATAL: SLibrary data.json 不存在: {SLIB_DATA}")
        sys.exit(1)

    raw = json.loads(SLIB_DATA.read_text(encoding="utf-8"))
    works = raw.get("works", [])
    print(f"读取 SLibrary: {len(works)} 作品")

    now = datetime.now().strftime("%Y-%m-%dT%H:%M:%S")
    today = datetime.now().strftime("%Y-%m-%d")

    stats = {
        "total": len(works),
        "imported": 0,
        "skipped_no_source": 0,
        "skipped_missing_file": 0,
        "chapters": 0,
        "tags_total": 0,
        "tags_unique": set(),
        "regions": Counter(),
        "sample": [],
    }

    books_rows = []        # 待插入 books 的参数列表
    managed_tags_set = {}  # name → (category, is_exclusive)
    segment_markers = []   # (video_id, time_ms, title, created_at)

    for w in works:
        wid = w.get("id", "")
        title = w.get("title", wid)
        region = w.get("region", "")
        stats["regions"][region] += 1

        # === folder_path + video_paths ===
        sources = w.get("sources", [])
        is_collection = w.get("is_collection", False)
        folder_v = w.get("Folder_V", "")
        folder_p = w.get("Folder_P", "")

        if is_collection and sources:
            # 合集：folder_path = 第一个 source 的父目录
            first_path = file_url_to_path(sources[0].get("path", ""))
            if first_path:
                folder_path = os.path.dirname(first_path)
            else:
                folder_path = folder_v or title
            video_paths = [file_url_to_path(s.get("path", "")) for s in sources]
            video_paths = [p for p in video_paths if p]
        elif is_collection and folder_v:
            folder_path = folder_v
            video_paths = []
        else:
            # 单视频
            src = w.get("source", "")
            fp = file_url_to_path(src)
            if not fp:
                stats["skipped_no_source"] += 1
                continue
            folder_path = fp
            video_paths = [fp]

        # 检查视频文件存在性（统计用，不阻塞导入）
        existing = sum(1 for p in video_paths if p and os.path.exists(p))
        if existing == 0 and not is_collection:
            stats["skipped_missing_file"] += 1
            # 仍然导入，但标记
        elif existing < len(video_paths):
            pass  # 部分缺失

        bid = book_id_from_path(folder_path)

        # === tags ===
        slib_tags = w.get("tags", [])
        tag_str, tag_managed = convert_tags(slib_tags, region, wid)
        for name, cat, excl in tag_managed:
            if name not in managed_tags_set:
                managed_tags_set[name] = (cat, excl)
            stats["tags_unique"].add(name)
        stats["tags_total"] += len(tag_managed)

        # === cover ===
        cover_path = resolve_cover(w.get("cover_dst", ""), w.get("cover", ""))

        # === rating ===
        rating = w.get("rating", 0) or 0

        # === favorite ===
        is_fav = 1 if w.get("favorite", False) else 0

        # === 其他字段 ===
        produced_at = w.get("date", "") or ""
        summary = w.get("comment", "") or ""
        read_count = int(w.get("watch", 0) or 0)
        aliases = w.get("aliases", [])

        # author：单视频用 region，合集用文件夹名
        author = region or ""

        # === video_paths JSON ===
        vp_json = json.dumps(video_paths, ensure_ascii=False)

        # === total_bytes：计算所有视频文件实际大小 ===
        total_bytes = 0
        for vp in video_paths:
            try:
                if vp and os.path.exists(vp):
                    total_bytes += os.path.getsize(vp)
            except OSError:
                pass
        # 合集也加上 Folder_P 的图集大小
        if folder_p and os.path.exists(folder_p):
            for root, dirs, files in os.walk(folder_p):
                for fn in files:
                    try:
                        total_bytes += os.path.getsize(os.path.join(root, fn))
                    except OSError:
                        pass

        # image_set_paths：Folder_P 的图集
        image_set_paths = []
        if folder_p:
            image_set_paths.append(folder_p)
        isp_json = json.dumps(image_set_paths, ensure_ascii=False)

        # foreign_name：aliases 里的外文名（日文/英文标题）
        foreign_name = ""
        if aliases and len(aliases) > 0:
            foreign_name = aliases[0]

        books_rows.append({
            "id": bid,
            "title": title,
            "author": author,
            "tags": tag_str,
            "folder_path": folder_path,
            "produced_at": produced_at,
            "imported_at": today,
            "summary": summary,
            "read_count": read_count,
            "rating": rating,
            "is_favorite": is_fav,
            "video_paths": vp_json,
            "image_set_paths": isp_json,
            "cover_image_path": cover_path or "",
            "foreign_name": foreign_name,
            "total_bytes": total_bytes,
            "updated_at": now,
        })

        # === chapters → video_segment_markers ===
        for ch in w.get("chapters", []):
            t = ch.get("time", 0)
            try:
                t = float(t)
            except (ValueError, TypeError):
                continue
            time_ms = int(t * 1000)
            label = ch.get("label", "")
            segment_markers.append((bid, time_ms, label, now))
            stats["chapters"] += 1

        stats["imported"] += 1
        if len(stats["sample"]) < 3:
            stats["sample"].append({
                "id": bid, "title": title, "region": region,
                "folder_path": folder_path,
                "video_count": len(video_paths),
                "tag_str": tag_str[:80],
                "rating": rating, "fav": is_fav,
                "cover": "yes" if cover_path else "no",
            })

    # ==== 报告 ====
    print("\n" + "=" * 60)
    print("转译报告（DRY-RUN）" if not apply_mode else "转译报告（APPLY）")
    print("=" * 60)
    print(f"  总作品:        {stats['total']}")
    print(f"  成功导入:      {stats['imported']}")
    print(f"  跳过(无source): {stats['skipped_no_source']}")
    print(f"  文件缺失(仍导入): {stats['skipped_missing_file']}")
    print(f"  章节标记:      {stats['chapters']}")
    print(f"  地区分布:      {dict(stats['regions'])}")
    print(f"  不同 tag 数:   {len(stats['tags_unique'])}")
    print(f"  tag 总引用:    {stats['tags_total']}")

    print("\n  样本:")
    for s in stats["sample"]:
        print(f"    {s['id']}  {s['title']}")
        print(f"      region={s['region']}, videos={s['video_count']}, rating={s['rating']}, fav={s['fav']}, cover={s['cover']}")
        print(f"      tags: {s['tag_str']}")
        print(f"      path: {s['folder_path']}")

    # tag 分类预览
    cat_count = Counter()
    for name, (cat, _) in managed_tags_set.items():
        cat_count[cat] += 1
    print(f"\n  managed_tags 分类:")
    for cat, n in cat_count.most_common():
        print(f"    [{cat}] {n} 个")

    if not apply_mode:
        print("\n  >>> dry-run 完成，未写入。加 --apply 真正写入。")
        return

    # ==== APPLY ====
    print(f"\n  写入: {THEATER_DB}")
    THEATER_DATA_DIR.mkdir(parents=True, exist_ok=True)

    # 备份现有
    if THEATER_DB.exists():
        bak = THEATER_DB.with_suffix(f".db.bak.{datetime.now().strftime('%Y%m%d_%H%M%S')}")
        shutil.copy2(THEATER_DB, bak)
        print(f"  备份现有: {bak.name}")

    # 删除旧的 WAL/SHM
    for suffix in ("-wal", "-shm"):
        f = THEATER_DB.with_suffix(THEATER_DB.suffix + suffix)
        if f.exists():
            f.unlink()

    con = sqlite3.connect(str(THEATER_DB))
    cur = con.cursor()
    cur.executescript(SCHEMA_SQL)

    # 清空已有数据（全新库）
    cur.execute("DELETE FROM books")
    cur.execute("DELETE FROM managed_tags")
    cur.execute("DELETE FROM video_segment_markers")

    # 插入 books
    inserted = 0
    for row in books_rows:
        try:
            cur.execute("""
                INSERT INTO books (
                    id, title, author, tags, folder_path,
                    produced_at, imported_at, summary,
                    read_count, rating, is_favorite,
                    video_paths, image_set_paths, cover_image_path,
                    foreign_name, total_bytes, updated_at, reading_status
                ) VALUES (
                    ?, ?, ?, ?, ?,
                    ?, ?, ?,
                    ?, ?, ?,
                    ?, ?, ?,
                    ?, ?, ?, 'unread'
                )
            """, (
                row["id"], row["title"], row["author"], row["tags"], row["folder_path"],
                row["produced_at"], row["imported_at"], row["summary"],
                row["read_count"], row["rating"], row["is_favorite"],
                row["video_paths"], row["image_set_paths"], row["cover_image_path"],
                row["foreign_name"], row["total_bytes"], row["updated_at"],
            ))
            inserted += 1
        except sqlite3.IntegrityError as e:
            print(f"    跳过重复: {row['id']} {row['title']} ({e})")

    # 插入 managed_tags
    for name, (cat, excl) in managed_tags_set.items():
        cur.execute("""
            INSERT OR REPLACE INTO managed_tags (name, category, is_exclusive, updated_at, color)
            VALUES (?, ?, ?, ?, '')
        """, (name, cat, 1 if excl else 0, now))

    # 插入 video_segment_markers
    for (vid, tms, label, ts) in segment_markers:
        cur.execute("""
            INSERT INTO video_segment_markers (video_id, time_ms, title, created_at, updated_at)
            VALUES (?, ?, ?, ?, ?)
        """, (vid, tms, label, ts, ts))

    con.commit()

    # 验证
    cur.execute("SELECT COUNT(*) FROM books")
    db_books = cur.fetchone()[0]
    cur.execute("SELECT COUNT(*) FROM managed_tags")
    db_tags = cur.fetchone()[0]
    cur.execute("SELECT COUNT(*) FROM video_segment_markers")
    db_markers = cur.fetchone()[0]
    cur.execute("SELECT COUNT(*) FROM books WHERE rating > 0")
    db_rated = cur.fetchone()[0]
    cur.execute("SELECT COUNT(*) FROM books WHERE is_favorite = 1")
    db_fav = cur.fetchone()[0]
    cur.execute("SELECT COUNT(*) FROM books WHERE cover_image_path != ''")
    db_cover = cur.fetchone()[0]
    con.close()

    print(f"\n  数据库验证:")
    print(f"    books:              {db_books}")
    print(f"    managed_tags:       {db_tags}")
    print(f"    video_segment_markers: {db_markers}")
    print(f"    有评分的 books:     {db_rated}")
    print(f"    有收藏的 books:     {db_fav}")
    print(f"    有封面的 books:     {db_cover}")
    print(f"\n  >>> 完成。app.db 位置: {THEATER_DB}")


if __name__ == "__main__":
    main()
