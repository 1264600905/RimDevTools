# RimWorld 进程的启动、关闭、检测与等待
# 负责所有与游戏进程生命周期相关的操作。

import os
import subprocess
import sys
import time
from pathlib import Path

from .config import (
    GAME_PATH,
    POST_EXISTING_GAME_EXIT_DELAY,
    STEAM_LAUNCH_DETECT_TIMEOUT,
    STEAM_URL,
    WAIT_FOR_EXISTING_GAME_EXIT_TIMEOUT,
)


BORDERLESS_WINDOW_ARGUMENT = "-popupwindow"


def is_steam_running():
    if sys.platform != "win32":
        return False
    result = subprocess.run(
        ["tasklist", "/FI", "IMAGENAME eq steam.exe"],
        capture_output=True,
        text=True,
        check=False,
    )
    return "steam.exe" in (result.stdout or "").lower()


def ensure_steam_running():
    """在直接启动 RimWorld 前尽力启动 Steam，但不阻断本地 exe 启动。"""
    if is_steam_running():
        return True

    candidates = [
        Path(os.environ.get("PROGRAMFILES(X86)", "")) / "Steam" / "steam.exe",
        Path(os.environ.get("PROGRAMFILES", "")) / "Steam" / "steam.exe",
        Path(os.environ.get("LOCALAPPDATA", "")) / "Steam" / "steam.exe",
    ]
    for steam_path in candidates:
        if not steam_path.is_file():
            continue
        try:
            subprocess.Popen([str(steam_path), "-silent"], shell=False)
            print(f"[信息] Steam 未运行，已尝试启动: {steam_path}")
            return True
        except OSError as e:
            print(f"[警告] 启动 Steam 失败: {e}")
            return False

    try:
        os.startfile("steam://open/main")
        print("[信息] Steam 未运行，已通过协议尝试启动。")
        return True
    except OSError as e:
        print(f"[警告] 未找到 Steam 客户端，继续直接启动 RimWorld: {e}")
        return False


def kill_rimworld():
    if sys.platform == "win32":
        try:
            subprocess.run(["taskkill", "/F", "/IM", "RimWorldWin64.exe", "/T"], capture_output=True, check=False)
            subprocess.run(["taskkill", "/F", "/IM", "RimWorld.exe", "/T"], capture_output=True, check=False)
        except:
            pass


def is_rimworld_running():
    if sys.platform != "win32":
        return False
    result = subprocess.run(
        ["tasklist", "/FI", "IMAGENAME eq RimWorldWin64.exe"],
        capture_output=True,
        text=True,
        check=False,
    )
    if "RimWorldWin64.exe" in (result.stdout or ""):
        return True

    result = subprocess.run(
        ["tasklist", "/FI", "IMAGENAME eq RimWorld.exe"],
        capture_output=True,
        text=True,
        check=False,
    )
    return "RimWorld.exe" in (result.stdout or "")


def launch_rimworld(use_quicktest: bool):
    game_path = Path(GAME_PATH)
    if game_path.exists():
        ensure_steam_running()
        command = [str(game_path), BORDERLESS_WINDOW_ARGUMENT]
        if use_quicktest:
            command.append("quicktest")
        return subprocess.Popen(command, shell=False)

    if use_quicktest:
        raise RuntimeError(f"找不到游戏 exe，无法以 quicktest 模式启动: {GAME_PATH}")

    try:
        subprocess.Popen(["start", STEAM_URL], shell=True)
    except Exception:
        pass

    steam_start = time.time()
    while time.time() - steam_start < STEAM_LAUNCH_DETECT_TIMEOUT:
        if is_rimworld_running():
            return None
        time.sleep(2)

    raise RuntimeError(f"Steam 启动失败且找不到游戏 exe 路径: {GAME_PATH}")


def wait_for_existing_rimworld_to_exit(timeout_seconds: int = WAIT_FOR_EXISTING_GAME_EXIT_TIMEOUT):
    """若检测到已有 RimWorld 进程正在运行，则等待其退出。等待期间不改模组配置。"""
    if not is_rimworld_running():
        return True

    print(f"[信息] 检测到 RimWorld 已在运行，最多等待 {timeout_seconds}s 让其自行关闭。")
    start_time = time.time()
    while time.time() - start_time < timeout_seconds:
        elapsed = int(time.time() - start_time)
        print(f"\r等待现有游戏退出: {elapsed}s/{timeout_seconds}s", end="", flush=True)
        time.sleep(2)
        if not is_rimworld_running():
            print("")
            print(f"[信息] 检测到现有 RimWorld 已关闭，额外等待 {POST_EXISTING_GAME_EXIT_DELAY}s 后开始测试。")
            time.sleep(POST_EXISTING_GAME_EXIT_DELAY)
            return True

    print("")
    print(f"[错误] 超过 {timeout_seconds}s，现有 RimWorld 仍未关闭，已取消本次测试。")
    return False
