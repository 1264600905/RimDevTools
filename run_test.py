import subprocess
import time
import os
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

# ================= 配置信息 =================
# 环世界主程序路径
GAME_PATH = r"H:\SteamLibrary\steamapps\common\RimWorld\RimWorldWin64.exe"
# Steam 启动协议
STEAM_URL = "steam://rungameid/294100"
# 环世界配置文件目录
RIMWORLD_CONFIG_DIR = Path(os.environ["USERPROFILE"]) / "AppData/LocalLow/Ludeon Studios/RimWorld by Ludeon Studios/Config"
# 系统日志 Player.log 路径
SYSTEM_LOG_PATH = RIMWORLD_CONFIG_DIR.parent / "Player.log"
# 本模组的配置文件路径
SETTINGS_FILE = RIMWORLD_CONFIG_DIR / "Mod_LogSyncMod_LogSyncMod.xml"
# 我们希望强制指定的同步日志输出路径
TARGET_SYNC_LOG = r"D:\RimworldProject\环世界日志同步mod\精选日志.log"
# 模组发出的就绪信号字符串
READY_SIGNAL = "[LogSyncMod] RUNTIME_READY_TO_CLOSE"
# 最大等待超时时间（10分钟）
TIMEOUT = 600 

def update_mod_settings():
    """
    在启动游戏前直接修改模组的 XML 配置文件。
    确保 logPath 指向我们当前的 '精选日志.log'。
    """
    if not SETTINGS_FILE.exists():
        print(f"[提示] 配置文件不存在，可能模组尚未初始化。将跳过自动设置。")
        return

    try:
        # 解析 XML
        tree = ET.parse(SETTINGS_FILE)
        root = tree.getroot()
        
        # 寻找 logPath 节点并更新其值
        path_node = root.find(".//logPath")
        if path_node is not None:
            if path_node.text != TARGET_SYNC_LOG:
                print(f"[设置] 将日志路径修改为: {TARGET_SYNC_LOG}")
                path_node.text = TARGET_SYNC_LOG
                tree.write(SETTINGS_FILE, encoding="utf-8", xml_declaration=True)
            else:
                print(f"[设置] 日志路径已正确配置。")
        else:
            print("[警告] 在配置文件中未找到 <logPath> 节点。")
    except Exception as e:
        print(f"[错误] 修改配置文件失败: {e}")

def get_sync_log_path():
    """解析模组设置文件获取当前实际生效的日志路径（用于最后信息展示）"""
    if not SETTINGS_FILE.exists():
        return None
    try:
        tree = ET.parse(SETTINGS_FILE)
        root = tree.getroot()
        path_node = root.find(".//logPath")
        if path_node is not None:
            return path_node.text
    except Exception:
        pass
    return None

def kill_rimworld():
    """
    清理环境：强制关闭所有环世界相关的进程。
    使用 taskkill 确保关联的进程树也被终结。
    """
    print("\n[!] 正在终止所有 RimWorld 进程...")
    if sys.platform == "win32":
        try:
            # /F 强制 /IM 进程名 /T 进程树
            subprocess.run(["taskkill", "/F", "/IM", "RimWorldWin64.exe", "/T"], capture_output=True, check=False)
            subprocess.run(["taskkill", "/F", "/IM", "RimWorld.exe", "/T"], capture_output=True, check=False)
        except Exception:
            pass

def monitor():
    """
    自动化监控主流程：修改设置 -> 启动游戏 -> 延迟监控 -> 捕捉信号 -> 清理关闭。
    """
    print("======= 环世界自动化监控启动 =======")
    
    # 1. 启动前强制修改配置路径
    update_mod_settings()

    # 2. 启动游戏 (优先通过 Steam 启动)
    print(f"尝试启动游戏...")
    try:
        # 使用 start 命令拉起 Steam 协议，这样可以带上 Steam 环境启动
        subprocess.Popen(["start", STEAM_URL], shell=True)
    except Exception:
        print(f"Steam 启动失败，尝试直接运行 EXE...")
        subprocess.Popen([GAME_PATH], shell=False)

    # 3. 强制休眠，避开启动时的日志剧烈变动期
    print("启动命令已发送，等待 20 秒让游戏初始化...")
    time.sleep(20)

    start_time = time.time()
    found = False
    
    # 重新获取当前日志的大小作为起始读取偏移量，确保不读到旧数据
    initial_offset = 0
    if SYSTEM_LOG_PATH.exists():
        initial_offset = SYSTEM_LOG_PATH.stat().st_size
    last_pos = initial_offset
    print(f"当前系统日志起始偏移: {initial_offset} bytes")

    print(f"开始监控就绪信号: {READY_SIGNAL}...")

    # 4. 实时轮询 Player.log
    try:
        while time.time() - start_time < TIMEOUT:
            if SYSTEM_LOG_PATH.exists():
                try:
                    current_size = SYSTEM_LOG_PATH.stat().st_size
                    
                    # 检查日志是否被重置（某些启动流程会截断日志文件）
                    if current_size < last_pos:
                        print("\n[系统日志已重置，重置监控位置]")
                        last_pos = 0
                    
                    if current_size > last_pos:
                        # 以共享模式打开文件，防止与 Unity 的写入操作冲突
                        with open(SYSTEM_LOG_PATH, "r", encoding="utf-8", errors="ignore") as f:
                            f.seek(last_pos)
                            new_content = f.read()
                            last_pos = f.tell()
                            
                            if READY_SIGNAL in new_content:
                                print("\n[✔] 检测到就绪信号！所有初始化已完成。")
                                found = True
                                # 找到信号后额外等待 5 秒，确保模组的最后一次文件写入动作完成
                                time.sleep(5) 
                                break
                except Exception:
                    # 忽略读取过程中的临时锁定错误
                    pass
            
            # 打印简单的进度条
            elapsed = int(time.time() - start_time)
            print(f"\r等待中... 已耗时 {elapsed}s (最大 600s)", end="", flush=True)
            time.sleep(2)
            
    except KeyboardInterrupt:
        print("\n[!] 用户中断监控。")
    finally:
        # 5. 测试完成，关闭游戏
        kill_rimworld()

    # 输出结果反馈
    sync_path = get_sync_log_path()
    if sync_path:
        print(f"\n[信息] 当前同步日志路径: {sync_path}")
    
    if found:
        print("测试成功完成！")
    else:
        print("\n[超时] 未能在规定时间内获得信号，请确认模组是否已启用且无报错。")

if __name__ == "__main__":
    monitor()
