# -*- coding: utf-8 -*-
"""G3 smoke UI (engineering plan stage G3): page navigation smoke.

Launches the deployed PChabit, walks the sidebar to every main page,
and asserts no new ERR/FTL lines appear in the app log while navigating.

Pages: 仪表盘 / 时间线 / 应用使用 / 网页访问 / 硬件监控 / 分析 / 便签 / 分类管理 / 数据管理
Acceptance (plan): every page opens, log stays clean, whole run < 2 minutes.

Usage:
    python scripts/smoke_ui.py            # uses running instance (restarts it)
"""
import io
import glob
import os
import sys
import time
import ctypes
import subprocess

import uiautomation as auto

EXE = r"D:\Tool\PChabit\PChabit.exe"
LOG_DIR = os.path.expandvars(r"%LOCALAPPDATA%\PChabit\Logs")
PAGES = ["仪表盘", "时间线", "应用使用", "网页访问", "硬件监控", "分析", "便签", "分类管理", "数据管理"]
SETTLE = 2.0          # seconds to wait after each navigation
LAUNCH_WAIT = 25      # seconds to wait for main window


def log_path():
    L = sorted(glob.glob(os.path.join(LOG_DIR, "*.log")), key=os.path.getmtime)
    return L[-1]


def read_lines(path):
    with io.open(path, encoding="utf-8", errors="replace") as f:
        return f.read().splitlines()


def count_err(lines):
    return sum(1 for ln in lines if "[ERR]" in ln or "[FTL]" in ln)


def find_win(timeout=30):
    deadline = time.time() + timeout
    while time.time() < deadline:
        for c in auto.GetRootControl().GetChildren():
            if c.ClassName == "WinUIDesktopWin32WindowClass":
                return c
        time.sleep(2)
    return None


def walk(x, d=0, out=None):
    if out is None:
        out = []
    out.append(x)
    if d > 10:
        return out
    try:
        for c in x.GetChildren():
            walk(c, d + 1, out)
    except Exception:
        pass
    return out


def nm(c):
    try:
        return (c.Name or "").strip()
    except Exception:
        return ""


def click_label(w, label):
    for c in walk(w):
        if nm(c) == label:
            try:
                c.Invoke()
            except Exception:
                try:
                    c.Click(simulateMove=False)
                except Exception:
                    return False
            return True
    return False


def main():
    # stop any running instance (single-instance mutex would bounce us)
    subprocess.run(["taskkill", "/IM", "PChabit.exe", "/F"], capture_output=True)
    time.sleep(2)

    started = time.time()
    subprocess.Popen([EXE], cwd=os.path.dirname(EXE))
    w = find_win(LAUNCH_WAIT)
    if w is None:
        print("FAIL: main window not found")
        return 2
    try:
        ctypes.windll.user32.SetWindowPos(w.NativeWindowHandle, -1, 40, 40, 1500, 980, 0x0040)
        w.SetActive()
    except Exception:
        pass
    time.sleep(2)

    log = log_path()
    baseline = count_err(read_lines(log))
    failures = []

    for page in PAGES:
        ok = click_label(w, page)
        if not ok:
            failures.append(f"{page}: menu item not found")
            continue
        time.sleep(SETTLE)
        errs = count_err(read_lines(log))
        if errs > baseline:
            # give transient async errors a beat, then re-check once
            time.sleep(1.5)
            errs = count_err(read_lines(log))
        if errs > baseline:
            failures.append(f"{page}: new ERR/FTL detected")
            baseline = errs  # don't cascade the same lines to later pages
        print(f"  nav ok: {page}")

    elapsed = time.time() - started
    final_errs = count_err(read_lines(log))

    print(f"pages navigated: {len(PAGES) - len(failures)}/{len(PAGES)}, elapsed {elapsed:.0f}s")
    if failures:
        for f in failures:
            print(f"FAIL: {f}")
        print("SMOKE UI RESULT: FAIL")
        return 1
    if elapsed > 120:
        print(f"FAIL: run exceeded 2 minutes ({elapsed:.0f}s)")
        print("SMOKE UI RESULT: FAIL")
        return 1
    print("SMOKE UI RESULT: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
