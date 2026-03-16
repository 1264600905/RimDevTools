# 注意：此脚本需要读写“沙盒外”的 RimWorld 配置目录（例如 ModsConfig.xml、Player.log 等）。
# - 如果你在 Cursor / 其他受限沙盒中运行：请开启“访问外部文件”权限，否则脚本会因为无法访问
#   `%USERPROFILE%\\AppData\\LocalLow\\Ludeon Studios\\RimWorld by Ludeon Studios` 而失败。
# - 这里说的“高权限”是指“允许访问沙盒外文件”，不是指 Windows 管理员权限。

import subprocess
import time
import os
import sys
import xml.etree.ElementTree as ET
import re
import argparse
from pathlib import Path
from datetime import datetime

# ==========================================
# 1. 配置部分 (需要修改的部分写在这里)
# ==========================================

# 环世界主程序路径
GAME_PATH = r"H:\SteamLibrary\steamapps\common\RimWorld\RimWorldWin64.exe"

# Steam 启动协议
STEAM_URL = "steam://rungameid/294100"

# 模组发出的就绪信号字符串 (检测到此字符串后脚本将自动关闭游戏)
READY_SIGNAL = "[LogSyncMod] RUNTIME_READY_TO_CLOSE"

# LogSyncMod 默认写入的 keywords（用于跟踪关键字相关日志；可在此修改）
# - 若命令行未传入 --log-keyword 且未传入 --clear-log-keywords，则会写入该默认值
# - 可通过多次 --log-keyword 覆盖默认值
DEFAULT_LOG_KEYWORDS = ["冒烟测试"]

# Steam 启动检测超时时间 (秒)：
# 通过 steam:// 协议拉起游戏后，最多等待这么久让 RimWorld 进程出现；否则视为 Steam 启动失败，改为直接执行 exe。
STEAM_LAUNCH_DETECT_TIMEOUT = 120

# 最大等待超时时间 (秒)
TIMEOUT = 600 

# 默认的测试激活模组列表 (当没有预设文件时使用)
MIN_ACTIVE_MODS = [
    "brrainz.harmony",
    "brrainz.visualexceptions",
    "ludeon.rimworld",
    "automatic.autolinks",
    "brrainz.cameraplus",
    "rwzh.chinesepack.cameraplus",
    "ludeon.rimworld.royalty",
    "ludeon.rimworld.ideology",
    "ludeon.rimworld.biotech",
    "ludeon.rimworld.anomaly",
    "ludeon.rimworld.odyssey",
    "void.charactereditor",
    "hailuan.translate",
    "erdelf.humanoidalienraces",
    "rwzh.chinesepack.humanoidalienraces",
    "mlie.logafterdeferror",
    "jaxe.rimhud",
    "shouer.chinesepack.rimhud",
    "arandomkiwi.rimsaves",
    "rwzh.chinesepack.rimsaves",
    "trigger.universalblueprints",
    "trigger.logsyncmod",
]

# ==========================================
# 1.5. 从项目元数据读取当前模组信息
# ==========================================

ABOUT_XML_PATH = Path(__file__).resolve().parent / "About" / "About.xml"

# ==========================================
# 2. 内部路径配置 (通常不需要修改)
# ==========================================

# 环世界配置文件目录
RIMWORLD_CONFIG_DIR = Path(os.environ["USERPROFILE"]) / "AppData/LocalLow/Ludeon Studios/RimWorld by Ludeon Studios/Config"
# ModsConfig.xml 路径
MODS_CONFIG_FILE = RIMWORLD_CONFIG_DIR / "ModsConfig.xml"
# ModsConfig 备份路径
MODS_CONFIG_BACKUP_FILE = RIMWORLD_CONFIG_DIR / "ModsConfig.xml.codexbak"
# 系统日志 Player.log 路径
SYSTEM_LOG_PATH = RIMWORLD_CONFIG_DIR.parent / "Player.log"
# LogSyncMod 模组的配置文件路径
SETTINGS_FILE = RIMWORLD_CONFIG_DIR / "Mod_LogSyncMod_LogSyncMod.xml"
# 本项目同步日志输出路径
TARGET_SYNC_LOG = str((Path(__file__).resolve().parent / "精选日志.log"))

# ==========================================
# 3. 内部逻辑部分 (不需要修改)
# ==========================================

def read_current_mod_package_id():
    """
    从 About/About.xml 读取当前项目的 packageId（只取 ModMetaData 下的顶层 packageId）。
    如果读取失败返回 None。
    """
    if not ABOUT_XML_PATH.exists():
        return None
    try:
        tree = ET.parse(ABOUT_XML_PATH)
        root = tree.getroot()
        # 只取顶层 packageId，避免误取到 modDependencies 里的 packageId
        node = root.find("./packageId") or root.find("packageId")
        if node is None or not (node.text or "").strip():
            return None
        return (node.text or "").strip()
    except Exception:
        return None

def check_sandbox_access():
    """检查是否能够访问沙盒外的关键文件。"""
    if not RIMWORLD_CONFIG_DIR.parent.exists():
        print("="*60)
        print("[错误] 无法访问环世界配置目录！脚本可能处于受限沙盒中。")
        print(f"预期路径: {RIMWORLD_CONFIG_DIR.parent}")
        print("="*60)
        sys.exit(1)

def load_mods_from_file(path: Path):
    """从文件中解析模组列表，支持 XML <li> 格式和纯文本行格式。"""
    if not path.exists(): return None
    text = path.read_text(encoding="utf-8", errors="ignore")
    
    # 尝试匹配 <li>packageId</li>
    mods = []
    for m in re.finditer(r"<li>\s*([^<]+?)\s*</li>", text, flags=re.IGNORECASE):
        mod_id = (m.group(1) or "").strip()
        if mod_id: mods.append(mod_id)
    
    # 如果没匹配到 <li>，则尝试按行读取非空行（排除 XML 标签和注释）
    if not mods:
        for line in text.splitlines():
            line = line.strip()
            if line and not line.startswith(("<", "#", "/")):
                mods.append(line)
    
    # 去重并保持顺序
    unique = []
    seen = set()
    for m in mods:
        if m.lower() not in seen:
            seen.add(m.lower())
            unique.append(m)
    return unique if unique else None

def pick_default_mods_file():
    root = Path(__file__).resolve().parent
    for name in ["模组信息_vef.txt", "模组信息.txt"]:
        p = root / name
        if p.exists(): return p
    return None

def update_mod_settings(log_keywords: list[str] | None = None, clear_log_keywords: bool = False):
    """
    在启动游戏前修改 LogSyncMod 的设置文件：
    - 强制 logPath 指向本项目的 精选日志.log
    - 可选：写入/清空 <keywords>（用于跟踪关键字相关日志）
    """
    if not SETTINGS_FILE.exists():
        return

    try:
        tree = ET.parse(SETTINGS_FILE)
        root = tree.getroot()

        changed = False

        path_node = root.find(".//logPath")
        if path_node is not None and path_node.text != TARGET_SYNC_LOG:
            path_node.text = TARGET_SYNC_LOG
            changed = True

        if clear_log_keywords or log_keywords is not None:
            mod_settings = root.find(".//ModSettings") or root.find("ModSettings")
            if mod_settings is None:
                raise RuntimeError("未在 LogSyncMod 配置文件中找到 <ModSettings> 节点")

            keywords_node = mod_settings.find("keywords")
            if keywords_node is None:
                keywords_node = ET.SubElement(mod_settings, "keywords")

            # 清空已有条目
            for child in list(keywords_node):
                keywords_node.remove(child)

            if log_keywords:
                for kw in log_keywords:
                    kw = (kw or "").strip()
                    if not kw:
                        continue
                    li = ET.SubElement(keywords_node, "li")
                    li.text = kw
            changed = True

        if changed:
            try:
                ET.indent(tree, space="\t")
            except Exception:
                pass
            tree.write(SETTINGS_FILE, encoding="utf-8", xml_declaration=True)
    except Exception as e:
        print(f"[错误] 修改模组设置失败: {e}")

def get_logsync_keywords():
    if not SETTINGS_FILE.exists():
        return None
    try:
        tree = ET.parse(SETTINGS_FILE)
        root = tree.getroot()
        keywords_node = root.find(".//ModSettings/keywords") or root.find(".//keywords")
        if keywords_node is None:
            return []
        out = []
        for li in list(keywords_node):
            if li.tag != "li":
                continue
            text = (li.text or "").strip()
            if text:
                out.append(text)
        return out
    except Exception:
        return None

def ensure_mod_enabled(mods: list[str], package_id: str | None):
    if not package_id:
        return mods

    normalized = package_id.strip()
    if not normalized:
        return mods

    lowered = {m.lower() for m in mods}
    if normalized.lower() in lowered:
        return mods

    # 尽量把当前模组插入在 LogSyncMod 前面（如果存在），避免 logsync 作为最后收尾模组时被打乱。
    insert_at = len(mods)
    for i, m in enumerate(mods):
        if m.lower() == "trigger.logsyncmod":
            insert_at = i
            break
    mods.insert(insert_at, normalized)
    print(f"[信息] 模组预设未包含当前项目包名，已自动加入: {normalized}")
    return mods

def set_minimal_mod_list(mod_list_file: Path | None = None, include_package_id: str | None = None):
    if not MODS_CONFIG_FILE.exists(): return None
    original_text = MODS_CONFIG_FILE.read_text(encoding="utf-8", errors="ignore")
    try:
        MODS_CONFIG_BACKUP_FILE.write_text(original_text, encoding="utf-8")
        
        # 确定要启用的模组
        if mod_list_file is None: mod_list_file = pick_default_mods_file()
        desired_mods = load_mods_from_file(mod_list_file) if mod_list_file else None
        if not desired_mods:
            print("[信息] 未找到有效的模组预设文件，将使用默认最小集。")
            desired_mods = MIN_ACTIVE_MODS
        else:
            print(f"[信息] 已加载模组预设: {mod_list_file.name} (共 {len(desired_mods)} 个)")

        desired_mods = ensure_mod_enabled(list(desired_mods), include_package_id)

        tree = ET.parse(MODS_CONFIG_FILE)
        root = tree.getroot()
        active_mods = root.find("activeMods")
        if active_mods is None: return original_text
        
        for child in list(active_mods): active_mods.remove(child)
        for mod_id in desired_mods:
            li = ET.SubElement(active_mods, "li")
            li.text = mod_id
        
        tree.write(MODS_CONFIG_FILE, encoding="utf-8", xml_declaration=True)
        return original_text
    except Exception as e:
        print(f"[警告] 修改 ModsConfig 失败: {e}")
        return original_text

def restore_mod_list(original_text):
    if original_text is None: return
    try:
        MODS_CONFIG_FILE.write_text(original_text, encoding="utf-8")
        if MODS_CONFIG_BACKUP_FILE.exists(): MODS_CONFIG_BACKUP_FILE.unlink()
    except: pass

def kill_rimworld():
    if sys.platform == "win32":
        try:
            subprocess.run(["taskkill", "/F", "/IM", "RimWorldWin64.exe", "/T"], capture_output=True, check=False)
            subprocess.run(["taskkill", "/F", "/IM", "RimWorld.exe", "/T"], capture_output=True, check=False)
        except: pass

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

def monitor(mod_list_file: Path | None = None, log_keywords: list[str] | None = None, clear_log_keywords: bool = False):
    check_sandbox_access()
    print("======= 环世界自动化监控测试启动 =======")
    
    # 恢复上次意外中断
    if MODS_CONFIG_BACKUP_FILE.exists():
        try:
            backup_text = MODS_CONFIG_BACKUP_FILE.read_text(encoding="utf-8", errors="ignore")
            MODS_CONFIG_FILE.write_text(backup_text, encoding="utf-8")
            MODS_CONFIG_BACKUP_FILE.unlink()
        except: pass

    update_mod_settings(log_keywords=log_keywords, clear_log_keywords=clear_log_keywords)
    current_package_id = read_current_mod_package_id()
    if current_package_id:
        print(f"[信息] 当前项目 packageId: {current_package_id}")
    else:
        print(f"[警告] 未能从 About.xml 读取 packageId: {ABOUT_XML_PATH}")

    original_mods_config = set_minimal_mod_list(mod_list_file=mod_list_file, include_package_id=current_package_id)
    kill_rimworld()

    # 日志归档
    target_log = Path(TARGET_SYNC_LOG)
    if target_log.exists() and target_log.stat().st_size > 0:
        stamp = datetime.now().strftime("%Y%m%d_%H%M%S")
        target_log.replace(target_log.with_name(f"{target_log.stem}_{stamp}{target_log.suffix}"))
    target_log.write_text("", encoding="utf-8")

    # 启动
    try:
        subprocess.Popen(["start", STEAM_URL], shell=True)
    except:
        pass

    steam_start = time.time()
    while time.time() - steam_start < STEAM_LAUNCH_DETECT_TIMEOUT:
        if is_rimworld_running():
            break
        time.sleep(2)

    if not is_rimworld_running():
        if not os.path.exists(GAME_PATH):
            print(f"[警告] Steam 启动失败且找不到游戏 exe 路径: {GAME_PATH}")
        else:
            subprocess.Popen([GAME_PATH], shell=False)

    print(f"等待信号: {READY_SIGNAL}...")
    start_time = time.time()
    last_pos = 0
    if SYSTEM_LOG_PATH.exists(): last_pos = SYSTEM_LOG_PATH.stat().st_size
    
    found = False
    try:
        while time.time() - start_time < TIMEOUT:
            if SYSTEM_LOG_PATH.exists():
                current_size = SYSTEM_LOG_PATH.stat().st_size
                if current_size < last_pos: last_pos = 0
                if current_size > last_pos:
                    with open(SYSTEM_LOG_PATH, "r", encoding="utf-8", errors="ignore") as f:
                        f.seek(last_pos)
                        if READY_SIGNAL in f.read():
                            print("\n[✔] 检测到就绪信号！")
                            found = True
                            time.sleep(3)
                            break
            print(f"\r耗时: {int(time.time() - start_time)}s", end="", flush=True)
            time.sleep(2)
    except KeyboardInterrupt: print("\n[!] 中断。")
    finally:
        kill_rimworld()
        restore_mod_list(original_mods_config)
        print("\n======= 测试结束 =======")

if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--mods-file", default=None, help="自定义模组预设路径")
    parser.add_argument("--log-keyword", action="append", default=None, help="写入 LogSyncMod 配置 <keywords> 的条目，可重复传入")
    parser.add_argument("--clear-log-keywords", action="store_true", help="清空 LogSyncMod 配置中的 <keywords> 列表")
    parser.add_argument("--settings-only", action="store_true", help="只更新 LogSyncMod 配置（logPath/keywords），不启动游戏")
    args = parser.parse_args()

    keywords_to_write = DEFAULT_LOG_KEYWORDS
    if args.clear_log_keywords:
        keywords_to_write = []
    elif args.log_keyword is not None:
        keywords_to_write = args.log_keyword

    if args.settings_only:
        check_sandbox_access()
        update_mod_settings(log_keywords=keywords_to_write, clear_log_keywords=args.clear_log_keywords)
        kws = get_logsync_keywords()
        if kws is None:
            print(f"[警告] 读取 LogSyncMod keywords 失败: {SETTINGS_FILE}")
        else:
            print(f"[信息] LogSyncMod keywords 当前条目数: {len(kws)}")
            for kw in kws:
                print(f"- {kw}")
        raise SystemExit(0)

    monitor(
        mod_list_file=Path(args.mods_file) if args.mods_file else None,
        log_keywords=keywords_to_write,
        clear_log_keywords=args.clear_log_keywords,
    )
