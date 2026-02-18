import subprocess
import time
import os
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

# 配置信息
GAME_PATH = r"H:\SteamLibrary\steamapps\common\RimWorld\RimWorldWin64.exe"
STEAM_URL = "steam://rungameid/294100"
RIMWORLD_CONFIG_DIR = Path(os.environ["USERPROFILE"]) / "AppData/LocalLow/Ludeon Studios/RimWorld by Ludeon Studios/Config"
SYSTEM_LOG_PATH = RIMWORLD_CONFIG_DIR.parent / "Player.log"
SETTINGS_FILE = RIMWORLD_CONFIG_DIR / "Mod_LogSyncMod_LogSyncMod.xml"
READY_SIGNAL = "[LogSyncMod] RUNTIME_READY_TO_CLOSE"
TIMEOUT = 600  # 10分钟

def get_sync_log_path():
    """解析模组设置文件获取当前日志路径"""
    if not SETTINGS_FILE.exists():
        return None
    try:
        tree = ET.parse(SETTINGS_FILE)
        root = tree.getroot()
        path_node = root.find("logPath")
        if path_node is not None:
            return path_node.text
    except Exception:
        pass
    return None

def kill_rimworld():
    """清理所有环世界相关进程"""
    print("\n[!] 正在终止所有 RimWorld 进程...")
    if sys.platform == "win32":
        try:
            # 终止主程序
            subprocess.run(["taskkill", "/F", "/IM", "RimWorldWin64.exe", "/T"], capture_output=True, check=False)
            # 终止可能存在的 Steam 初始化挂起
            subprocess.run(["taskkill", "/F", "/IM", "RimWorld.exe", "/T"], capture_output=True, check=False)
        except Exception:
            pass

def monitor():
    print("======= 环世界自动化监控启动 =======")
    
    # 0. 记录起始偏移量，防止抓到旧信号
    initial_offset = 0
    if SYSTEM_LOG_PATH.exists():
        initial_offset = SYSTEM_LOG_PATH.stat().st_size
    print(f"当前系统日志起始偏移: {initial_offset} bytes")

    # 1. 启动游戏 (优先通过 Steam 启动)
    print(f"尝试启动游戏...")
    try:
        subprocess.Popen(["start", STEAM_URL], shell=True)
    except Exception:
        print(f"Steam 启动失败，尝试直接运行 EXE...")
        subprocess.Popen([GAME_PATH], shell=False)

    print("启动命令已发送，等待 20 秒让游戏初始化...")
    time.sleep(20)

    start_time = time.time()
    found = False
    
    # 在休眠 20 秒后重新获取初始偏移量，确保此时读到的是最新产生的日志
    initial_offset = 0
    if SYSTEM_LOG_PATH.exists():
        initial_offset = SYSTEM_LOG_PATH.stat().st_size
    last_pos = initial_offset
    print(f"当前系统日志起始偏移 (20s 后): {initial_offset} bytes")

    print(f"开始监控就绪信号: {READY_SIGNAL}...")

    # 2. 监控循环
    try:
        while time.time() - start_time < TIMEOUT:
            if SYSTEM_LOG_PATH.exists():
                try:
                    current_size = SYSTEM_LOG_PATH.stat().st_size
                    
                    # 如果日志被重置（游戏刚开始写新日志）
                    if current_size < last_pos:
                        print("\n[系统日志已重置，开始从头监控]")
                        last_pos = 0
                    
                    if current_size > last_pos:
                        with open(SYSTEM_LOG_PATH, "r", encoding="utf-8", errors="ignore") as f:
                            f.seek(last_pos)
                            new_content = f.read()
                            last_pos = f.tell()
                            
                            if READY_SIGNAL in new_content:
                                print("\n[✔] 检测到就绪信号！所有初始化已完成。")
                                found = True
                                time.sleep(5) # 缓冲
                                break
                except Exception:
                    pass
            
            elapsed = int(time.time() - start_time)
            print(f"\r等待中... 已耗时 {elapsed}s", end="", flush=True)
            time.sleep(2)
            
    except KeyboardInterrupt:
        print("\n[!] 用户中断。")
    finally:
        kill_rimworld()

    sync_path = get_sync_log_path()
    if sync_path:
        print(f"\n[信息] 当前同步日志路径: {sync_path}")
    
    if found:
        print("测试成功完成！")
    else:
        print("\n[超时] 未能检测到信号，请检查模组是否已激活。")

if __name__ == "__main__":
    monitor()
