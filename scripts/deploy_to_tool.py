# -*- coding: utf-8 -*-
"""把 publish 产物同步到用户实际运行目录 D:\\Tool\\PChabit。

只做覆盖式增量同步 + 清理已知的旧污染目录，不做全量镜像删除（避免误删）。
"""
import os
import shutil
import sys

SRC = r"E:\SYNC\My VS\PChabit\publish"
DST = r"D:\Tool\PChabit"
EXCLUDE_DIRS = {"WebView2"}

copied = 0
skipped = 0

for root, dirs, files in os.walk(SRC):
    dirs[:] = [d for d in dirs if d not in EXCLUDE_DIRS]
    rel = os.path.relpath(root, SRC)
    target_dir = DST if rel == "." else os.path.join(DST, rel)
    os.makedirs(target_dir, exist_ok=True)

    for f in files:
        s = os.path.join(root, f)
        d = os.path.join(target_dir, f)
        try:
            if os.path.exists(d) and os.path.getmtime(d) >= os.path.getmtime(s) and os.path.getsize(d) == os.path.getsize(s):
                skipped += 1
                continue
        except OSError:
            pass
        shutil.copy2(s, d)
        copied += 1

# 清理目标目录中源目录已不存在的 .xbf（页面被删除/改名后的编译残留）
# 自动比对而非维护死页清单：新增被删页面时忘记加清单会留下陈旧 xbf
views_dir = os.path.join(DST, "Views")
src_views = os.path.join(SRC, "Views")
removed = []
if os.path.isdir(views_dir):
    live = set(os.listdir(src_views)) if os.path.isdir(src_views) else set()
    for f in os.listdir(views_dir):
        # 只清理 .xbf；非 xbf 文件（Assets 等）不动
        if f.endswith(".xbf") and f not in live:
            os.remove(os.path.join(views_dir, f))
            removed.append(f)

# 清理 3.26.2 之前 sln 级 publish 混入的测试宿主残留
removed_dirs = []
for d in ("BuildHost-net472", "BuildHost-netcore"):
    p = os.path.join(DST, d)
    if os.path.isdir(p):
        shutil.rmtree(p)
        removed_dirs.append(d)

dll = os.path.join(DST, "PChabit.dll")
print("复制 %d 个文件，跳过 %d 个（已是最新）" % (copied, skipped))
print("删除旧页面 xbf: %s" % (removed or "无"))
print("删除测试宿主残留: %s" % (removed_dirs or "无"))
print("目标 PChabit.dll 存在: %s" % os.path.exists(dll))
print("目标目录文件总数: %d" % sum(len(f) for _, _, f in os.walk(DST)))
