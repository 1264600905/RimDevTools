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

WORKSPACE_DIR = Path(__file__).resolve().parent
REPO_ROOT = WORKSPACE_DIR.parent

# 环世界主程序路径
GAME_PATH = r"H:\SteamLibrary\steamapps\common\RimWorld\RimWorldWin64.exe"

# Steam 启动协议
STEAM_URL = "steam://rungameid/294100"

RUNNER_MODE_QUICK = "quick"
RUNNER_MODE_MANUAL = "manual"
RUNNER_MODE_AUTO = "auto"
RUNNER_MODE_XML = "xml"
RUNNER_MODES = [RUNNER_MODE_QUICK, RUNNER_MODE_MANUAL, RUNNER_MODE_AUTO, RUNNER_MODE_XML]
TEMP_AUTOTEST_SPEC_FILE_NAME = "LogSync_RunTest_TemporaryAutoTests.xml"
DISABLED_SPEC_SUFFIX = ".logsyncruntest.disabled"

# 模组发出的就绪信号字符串 (检测到此字符串后脚本将自动关闭游戏)
READY_SIGNAL = "[LogSyncMod] RUNTIME_READY_TO_CLOSE"
AUTO_TEST_READY_SIGNAL = "[LogSyncAutoTest] COMPLETED"

# 默认不改动当前配置中的 keywords。
DEFAULT_LOG_KEYWORDS = "LogSyncAutoTest"

# Steam 启动检测超时时间 (秒)：
# 通过 steam:// 协议拉起游戏后，最多等待这么久让 RimWorld 进程出现；否则视为 Steam 启动失败，改为直接执行 exe。
STEAM_LAUNCH_DETECT_TIMEOUT = 120

# 最大等待超时时间 (秒)
TIMEOUT = 600 

# 如果检测到已有 RimWorld 正在运行，最多等待其自行退出的时间（秒）
WAIT_FOR_EXISTING_GAME_EXIT_TIMEOUT = 600

# 检测到已有 RimWorld 已退出后，额外等待一段时间再开始测试（秒）
POST_EXISTING_GAME_EXIT_DELAY = 10

# 测试日志总保留数（包含当前日志文件本身）
MAX_MANAGED_LOG_FILES = 3
# 自动测试输出总保留运行数
MAX_MANAGED_AUTO_TEST_RUNS = 2

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

ABOUT_XML_PATH = REPO_ROOT / "About" / "About.xml"

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
TARGET_SYNC_LOG = str((WORKSPACE_DIR / "精选日志.log"))

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
    root = REPO_ROOT
    for name in ["模组信息_vef.txt", "模组信息.txt"]:
        p = root / name
        if p.exists(): return p
    return None

def resolve_auto_test_output_dir(auto_test_output_dir: str | None, auto_test_package_id: str | None):
    if not auto_test_package_id:
        return None
    if auto_test_output_dir is not None:
        return auto_test_output_dir
    return str(WORKSPACE_DIR / "AutoTestOutput")

def normalize_scope_value(scope: str | None):
    normalized_scope = (scope or "").strip().lower()
    scope_map = {
        "all": "All",
        "content": "Content",
        "recipes": "Recipes",
        "hediffs": "Hediffs",
        "turrets": "Turrets",
    }
    return scope_map.get(normalized_scope, "All")

def determine_runner_mode(explicit_mode: str | None, auto_test_package_id: str | None, auto_test_mode: str | None):
    if explicit_mode:
        return explicit_mode

    if auto_test_package_id:
        normalized_auto_test_mode = (auto_test_mode or "").strip().lower()
        return RUNNER_MODE_XML if normalized_auto_test_mode in {"xml", "xml-driven", "xmldriven"} else RUNNER_MODE_AUTO

    return RUNNER_MODE_QUICK

def build_candidate_mod_roots():
    roots = []
    game_path = Path(GAME_PATH)
    if game_path.exists():
        rimworld_dir = game_path.parent
        roots.append(rimworld_dir / "Mods")
        try:
            steamapps_dir = game_path.parents[2]
            roots.append(steamapps_dir / "workshop" / "content" / "294100")
        except Exception:
            pass
    return [root for root in roots if root.exists() and root.is_dir()]

def read_mod_package_id_from_about(about_xml_path: Path):
    if not about_xml_path.exists():
        return None

    try:
        tree = ET.parse(about_xml_path)
        root = tree.getroot()
        node = root.find("./packageId") or root.find("packageId")
        if node is None or not (node.text or "").strip():
            return None
        return (node.text or "").strip().lower()
    except Exception:
        return None

def find_mod_directory_by_package_id(package_id: str | None):
    normalized_package_id = (package_id or "").strip().lower()
    if not normalized_package_id:
        return None

    for root in build_candidate_mod_roots():
        for about_xml_path in root.rglob("About.xml"):
            if read_mod_package_id_from_about(about_xml_path) != normalized_package_id:
                continue
            if about_xml_path.parent.name.lower() == "about":
                return about_xml_path.parent.parent
            return about_xml_path.parent

    return None

def resolve_temp_autotest_spec_path(package_id: str):
    mod_dir = find_mod_directory_by_package_id(package_id)
    if mod_dir is None:
        raise RuntimeError(f"未能根据 packageId 找到目标模组目录: {package_id}")

    candidate_dirs = [
        mod_dir / "1.6" / "Defs" / "LogSync_AutoTests",
        mod_dir / "Defs" / "LogSync_AutoTests",
        mod_dir / "1.6" / "Defs",
        mod_dir / "Defs",
    ]

    for candidate_dir in candidate_dirs:
        if candidate_dir.exists():
            return candidate_dir / TEMP_AUTOTEST_SPEC_FILE_NAME

    target_dir = candidate_dirs[0]
    target_dir.mkdir(parents=True, exist_ok=True)
    return target_dir / TEMP_AUTOTEST_SPEC_FILE_NAME

def write_temp_autotest_spec(package_id: str, scope: str, def_names: list[str], capture_screenshots: bool):
    spec_path = resolve_temp_autotest_spec_path(package_id)
    normalized_def_names = []
    seen = set()
    for value in def_names:
        text = (value or "").strip()
        if not text:
            continue
        lowered = text.lower()
        if lowered in seen:
            continue
        seen.add(lowered)
        normalized_def_names.append(text)

    if not normalized_def_names:
        raise RuntimeError("未提供有效的 defName，无法生成临时自动测试 XML。")

    capture_value = "true" if capture_screenshots else "false"
    lines = [
        '<?xml version="1.0" encoding="utf-8" ?>',
        "<Defs>",
        "  <LogSyncMod.LogSyncAutoTestSpecDef>",
        "    <defName>LogSync_RunTest_TemporarySpec</defName>",
        "    <enabled>true</enabled>",
        f"    <scope>{scope}</scope>",
        "    <defNames>",
    ]
    for def_name in normalized_def_names:
        lines.append(f"      <li>{def_name}</li>")
    lines.extend([
        "    </defNames>",
        f"    <captureScreenshots>{capture_value}</captureScreenshots>",
        "  </LogSyncMod.LogSyncAutoTestSpecDef>",
        "</Defs>",
        "",
    ])

    spec_path.write_text("\n".join(lines), encoding="utf-8")
    return spec_path

def remove_temp_autotest_spec(spec_path: Path | None):
    if spec_path is None or not spec_path.exists():
        return
    try:
        spec_path.unlink()
        print(f"[信息] 已删除临时自动测试 XML: {spec_path}")
    except Exception as e:
        print(f"[警告] 删除临时自动测试 XML 失败: {spec_path} ({e})")

def find_existing_autotest_spec_files(package_id: str, excluded_paths: set[Path] | None = None):
    mod_dir = find_mod_directory_by_package_id(package_id)
    if mod_dir is None:
        raise RuntimeError(f"未能根据 packageId 找到目标模组目录: {package_id}")

    excluded = {path.resolve() for path in (excluded_paths or set())}
    matched_files = []
    for xml_path in mod_dir.rglob("*.xml"):
        try:
            resolved_path = xml_path.resolve()
        except Exception:
            resolved_path = xml_path
        if resolved_path in excluded:
            continue
        try:
            text = xml_path.read_text(encoding="utf-8", errors="ignore")
        except Exception:
            continue
        if "LogSyncAutoTestSpecDef" not in text:
            continue
        matched_files.append(xml_path)

    return matched_files

def disable_existing_autotest_spec_files(package_id: str, excluded_paths: set[Path] | None = None):
    disabled_pairs = []
    for source_path in find_existing_autotest_spec_files(package_id, excluded_paths=excluded_paths):
        disabled_path = source_path.with_name(source_path.name + DISABLED_SPEC_SUFFIX)
        source_path.replace(disabled_path)
        disabled_pairs.append((source_path, disabled_path))
        print(f"[信息] 已临时禁用目标模组自动测试 XML: {source_path}")
    return disabled_pairs

def restore_disabled_autotest_spec_files(disabled_pairs: list[tuple[Path, Path]] | None):
    for original_path, disabled_path in disabled_pairs or []:
        if not disabled_path.exists():
            continue
        try:
            disabled_path.replace(original_path)
            print(f"[信息] 已恢复目标模组自动测试 XML: {original_path}")
        except Exception as e:
            print(f"[警告] 恢复目标模组自动测试 XML 失败: {original_path} ({e})")

def ensure_text_node(parent, tag: str, value: str):
    node = parent.find(tag)
    if node is None:
        node = ET.SubElement(parent, tag)
    node.text = value

def remove_child_node(parent, tag: str):
    node = parent.find(tag)
    if node is None:
        return False
    parent.remove(node)
    return True

def normalize_log_keywords(log_keywords):
    if log_keywords is None:
        return None
    if isinstance(log_keywords, str):
        return [log_keywords]

    normalized = []
    for kw in log_keywords:
        if kw is None:
            continue
        if isinstance(kw, str):
            normalized.append(kw)
        else:
            normalized.append(str(kw))
    return normalized

def update_mod_settings(
    log_keywords: list[str] | None = None,
    clear_log_keywords: bool = False,
    auto_test_enabled: bool | None = None,
    auto_test_target_package_id: str | None = None,
    auto_test_output_path: str | None = None,
    auto_test_mode: str | None = None,
    auto_test_scope: str | None = None,
    auto_test_capture_screenshots: bool | None = None,
    auto_test_batch_size: int | None = None,
    clear_auto_test_batch_size: bool = False,
):
    """
    在启动游戏前修改 LogSyncMod 的设置文件：
    - 强制 logPath 指向本项目的 精选日志.log
    - 可选：写入/清空 <keywords>（用于跟踪关键字相关日志）
    """
    if not SETTINGS_FILE.exists():
        return

    try:
        log_keywords = normalize_log_keywords(log_keywords)
        tree = ET.parse(SETTINGS_FILE)
        root = tree.getroot()

        changed = False

        mod_settings = root.find(".//ModSettings") or root.find("ModSettings")
        if mod_settings is None:
            raise RuntimeError("未在 LogSyncMod 配置文件中找到 <ModSettings> 节点")

        path_node = mod_settings.find("logPath") or root.find(".//logPath")
        if path_node is None:
            ensure_text_node(mod_settings, "logPath", TARGET_SYNC_LOG)
            changed = True
        elif path_node.text != TARGET_SYNC_LOG:
            path_node.text = TARGET_SYNC_LOG
            changed = True

        if clear_log_keywords or log_keywords is not None:
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

        if auto_test_enabled is not None:
            desired_value = "True" if auto_test_enabled else "False"
            if (mod_settings.findtext("autoTestEnabled") or "").strip() != desired_value:
                ensure_text_node(mod_settings, "autoTestEnabled", desired_value)
                changed = True

        if auto_test_target_package_id is not None:
            normalized_package_id = (auto_test_target_package_id or "").strip().lower()
            if (mod_settings.findtext("autoTestTargetPackageId") or "").strip() != normalized_package_id:
                ensure_text_node(mod_settings, "autoTestTargetPackageId", normalized_package_id)
                changed = True

        if auto_test_output_path is not None:
            normalized_output_path = (auto_test_output_path or "").strip()
            if (mod_settings.findtext("autoTestOutputPath") or "").strip() != normalized_output_path:
                ensure_text_node(mod_settings, "autoTestOutputPath", normalized_output_path)
                changed = True

        if auto_test_mode is not None:
            normalized_mode = (auto_test_mode or "").strip().lower()
            mode_map = {
                "automatic": "Automatic",
                "xml": "XmlDriven",
                "xml-driven": "XmlDriven",
                "xmldriven": "XmlDriven",
            }
            desired_mode = mode_map.get(normalized_mode, "Automatic")
            if (mod_settings.findtext("autoTestMode") or "").strip() != desired_mode:
                ensure_text_node(mod_settings, "autoTestMode", desired_mode)
                changed = True

        if auto_test_scope is not None:
            normalized_scope = (auto_test_scope or "").strip().lower()
            scope_map = {
                "all": "All",
                "content": "Content",
                "recipes": "Recipes",
                "hediffs": "Hediffs",
                "turrets": "Turrets",
            }
            desired_scope = scope_map.get(normalized_scope, "All")
            if (mod_settings.findtext("autoTestScope") or "").strip() != desired_scope:
                ensure_text_node(mod_settings, "autoTestScope", desired_scope)
                changed = True

        if auto_test_capture_screenshots is not None:
            desired_value = "True" if auto_test_capture_screenshots else "False"
            if (mod_settings.findtext("autoTestCaptureScreenshots") or "").strip() != desired_value:
                ensure_text_node(mod_settings, "autoTestCaptureScreenshots", desired_value)
                changed = True

        if clear_auto_test_batch_size:
            if remove_child_node(mod_settings, "autoTestBatchSize"):
                changed = True
        elif auto_test_batch_size is not None:
            normalized_batch_size = str(max(1, int(auto_test_batch_size)))
            if (mod_settings.findtext("autoTestBatchSize") or "").strip() != normalized_batch_size:
                ensure_text_node(mod_settings, "autoTestBatchSize", normalized_batch_size)
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

def iter_managed_log_files(target_log: Path):
    """返回当前日志文件和归档日志文件。"""
    if not target_log.parent.exists():
        return []

    archive_pattern = f"{target_log.stem}_*{target_log.suffix}"
    files = []
    if target_log.exists():
        files.append(target_log)
    files.extend(path for path in target_log.parent.glob(archive_pattern) if path.is_file())
    return files

def build_archive_log_path(target_log: Path):
    """生成不冲突的归档日志文件名。"""
    stamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    archive_path = target_log.with_name(f"{target_log.stem}_{stamp}{target_log.suffix}")
    index = 1
    while archive_path.exists():
        archive_path = target_log.with_name(f"{target_log.stem}_{stamp}_{index}{target_log.suffix}")
        index += 1
    return archive_path

def prune_managed_log_files(target_log: Path, keep_count: int = MAX_MANAGED_LOG_FILES):
    """按最近修改时间保留日志文件，超出的自动删除。"""
    managed_files = sorted(
        iter_managed_log_files(target_log),
        key=lambda path: path.stat().st_mtime,
        reverse=True,
    )
    for obsolete_path in managed_files[keep_count:]:
        try:
            obsolete_path.unlink()
            print(f"[信息] 已删除旧日志归档: {obsolete_path.name}")
        except Exception as e:
            print(f"[警告] 删除旧日志归档失败: {obsolete_path} ({e})")

def rotate_target_log(target_log: Path):
    """归档当前日志并裁剪到保留上限。"""
    if target_log.exists() and target_log.stat().st_size > 0:
        archive_path = build_archive_log_path(target_log)
        target_log.replace(archive_path)
        print(f"[信息] 已归档日志: {archive_path.name}")

    target_log.write_text("", encoding="utf-8")
    prune_managed_log_files(target_log)

def parse_auto_test_run_key(path: Path):
    name = path.name
    suffixes = ["_Report.md", "_Result.json", "_Screenshots"]
    matched_suffix = None
    for suffix in suffixes:
        if name.endswith(suffix):
            matched_suffix = suffix
            break

    if matched_suffix is None:
        return None

    base_name = name[:-len(matched_suffix)]
    if len(base_name) < 16:
        return None

    stamp = base_name[-15:]
    if len(stamp) != 15 or stamp[8] != "_":
        return None

    date_part = stamp[:8]
    time_part = stamp[9:]
    if not (date_part.isdigit() and time_part.isdigit()):
        return None

    if len(base_name) == 15 or base_name[-16] != "_":
        return None

    return base_name

def prune_auto_test_output_runs(output_dir: Path, keep_count: int = MAX_MANAGED_AUTO_TEST_RUNS):
    if output_dir is None or not output_dir.exists() or not output_dir.is_dir():
        return

    grouped_paths: dict[str, list[Path]] = {}
    for path in output_dir.iterdir():
        run_key = parse_auto_test_run_key(path)
        if not run_key:
            continue
        grouped_paths.setdefault(run_key, []).append(path)

    if len(grouped_paths) <= keep_count:
        return

    sorted_runs = sorted(
        grouped_paths.items(),
        key=lambda entry: max(item.stat().st_mtime for item in entry[1]),
        reverse=True,
    )

    for run_key, paths in sorted_runs[keep_count:]:
        for path in paths:
            try:
                if path.is_dir():
                    for child in sorted(path.rglob("*"), reverse=True):
                        if child.is_file():
                            child.unlink()
                        elif child.is_dir():
                            child.rmdir()
                    path.rmdir()
                else:
                    path.unlink()
                print(f"[信息] 已删除旧自动测试归档: {path.name}")
            except Exception as e:
                print(f"[警告] 删除旧自动测试归档失败: {path} ({e})")

def summarize_sync_log():
    sync_log_path = Path(TARGET_SYNC_LOG)
    if not sync_log_path.exists():
        print(f"[错误] 未找到同步日志文件: {sync_log_path}")
        return False

    content = sync_log_path.read_text(encoding="utf-8", errors="ignore")
    lines = content.splitlines()
    print(f"[信息] 同步日志文件: {sync_log_path}")
    print(f"[信息] 同步日志大小: {len(content.encode('utf-8'))} 字节")
    print(f"[信息] 同步日志行数: {len(lines)}")
    if not content.strip():
        print("[警告] 同步日志当前为空；脚本不再执行模组专属内容断言。")
    else:
        preview = lines[-3:] if len(lines) >= 3 else lines
        print("[信息] 同步日志末尾预览:")
        for line in preview:
            print(f"  {line}")
    return True

def ensure_mod_enabled(mods: list[str], package_id: str | None, label: str = "包名"):
    if not package_id:
        return mods

    normalized = package_id.strip()
    if not normalized:
        return mods

    lowered = {m.lower() for m in mods}
    if normalized.lower() in lowered:
        return mods

    insert_at = len(mods)
    for i, m in enumerate(mods):
        if m.lower() == "trigger.logsyncmod":
            insert_at = i
            break
    mods.insert(insert_at, normalized)
    print(f"[信息] 模组预设未包含{label}，已自动加入: {normalized}")
    return mods

def set_minimal_mod_list(mod_list_file: Path | None = None, include_package_ids: list[tuple[str | None, str]] | None = None):
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

        desired_mods = list(desired_mods)
        for package_id, label in include_package_ids or []:
            desired_mods = ensure_mod_enabled(desired_mods, package_id, label=label)

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

def launch_rimworld(use_quicktest: bool):
    game_path = Path(GAME_PATH)
    if game_path.exists():
        command = [str(game_path)]
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

def monitor(
    runner_mode: str = RUNNER_MODE_QUICK,
    mod_list_file: Path | None = None,
    log_keywords: list[str] | None = None,
    clear_log_keywords: bool = False,
    auto_test_package_id: str | None = None,
    auto_test_output_dir: str | None = None,
    auto_test_mode: str | None = "automatic",
    auto_test_scope: str | None = "all",
    auto_test_capture_screenshots: bool | None = None,
    auto_test_batch_size: int | None = None,
    auto_test_defnames: list[str] | None = None,
    timeout_seconds: int = TIMEOUT,
):
    check_sandbox_access()
    print("======= 环世界自动化监控测试启动 =======")
    game_process = None
    generated_spec_path = None
    disabled_spec_pairs = []
    is_auto_test_mode = runner_mode in {RUNNER_MODE_AUTO, RUNNER_MODE_XML}
    resolved_auto_test_output_dir = resolve_auto_test_output_dir(auto_test_output_dir, auto_test_package_id if is_auto_test_mode else None)

    if not wait_for_existing_rimworld_to_exit():
        return

    # 恢复上次意外中断
    if MODS_CONFIG_BACKUP_FILE.exists():
        try:
            backup_text = MODS_CONFIG_BACKUP_FILE.read_text(encoding="utf-8", errors="ignore")
            MODS_CONFIG_FILE.write_text(backup_text, encoding="utf-8")
            MODS_CONFIG_BACKUP_FILE.unlink()
        except: pass

    effective_auto_test_mode = None
    if runner_mode == RUNNER_MODE_XML:
        if not auto_test_package_id:
            print("[错误] xml 模式必须提供 --auto-test-package-id")
            return
        effective_auto_test_mode = "xml-driven"
    elif runner_mode == RUNNER_MODE_AUTO:
        if not auto_test_package_id:
            print("[错误] auto 模式必须提供 --auto-test-package-id")
            return
        if auto_test_defnames:
            try:
                normalized_scope = normalize_scope_value(auto_test_scope)
                generated_spec_path = write_temp_autotest_spec(
                    auto_test_package_id,
                    normalized_scope,
                    auto_test_defnames,
                    bool(auto_test_capture_screenshots),
                )
                disabled_spec_pairs = disable_existing_autotest_spec_files(
                    auto_test_package_id,
                    excluded_paths={generated_spec_path},
                )
                print(f"[信息] 已生成临时自动测试 XML: {generated_spec_path}")
                effective_auto_test_mode = "xml-driven"
            except Exception as e:
                restore_disabled_autotest_spec_files(disabled_spec_pairs)
                remove_temp_autotest_spec(generated_spec_path)
                print(f"[错误] 生成临时自动测试 XML 失败: {e}")
                return
        else:
            effective_auto_test_mode = "automatic"

    update_mod_settings(
        log_keywords=log_keywords,
        clear_log_keywords=clear_log_keywords,
        auto_test_enabled=True if is_auto_test_mode else False,
        auto_test_target_package_id=auto_test_package_id if is_auto_test_mode else "",
        auto_test_output_path=resolved_auto_test_output_dir if is_auto_test_mode else None,
        auto_test_mode=effective_auto_test_mode if is_auto_test_mode else "automatic",
        auto_test_scope=auto_test_scope if is_auto_test_mode else "all",
        auto_test_capture_screenshots=auto_test_capture_screenshots if is_auto_test_mode else False,
        auto_test_batch_size=auto_test_batch_size if is_auto_test_mode else None,
        clear_auto_test_batch_size=(is_auto_test_mode and auto_test_batch_size is None) or (not is_auto_test_mode),
    )
    current_package_id = read_current_mod_package_id()
    if current_package_id:
        print(f"[信息] 当前项目 packageId: {current_package_id}")
    else:
        print(f"[警告] 未能从 About.xml 读取 packageId: {ABOUT_XML_PATH}")

    original_mods_config = set_minimal_mod_list(
        mod_list_file=mod_list_file,
        include_package_ids=[
            (current_package_id, "当前项目包名"),
            (auto_test_package_id if is_auto_test_mode else None, "被测模组包名"),
        ],
    )
    kill_rimworld()

    # 日志归档
    target_log = Path(TARGET_SYNC_LOG)
    rotate_target_log(target_log)
    if resolved_auto_test_output_dir:
        prune_auto_test_output_runs(Path(resolved_auto_test_output_dir))

    # 启动
    try:
        if runner_mode in {RUNNER_MODE_AUTO, RUNNER_MODE_XML}:
            print("[信息] 自动测试模式：直接以 quicktest 参数启动游戏。")
            game_process = launch_rimworld(use_quicktest=True)
        elif runner_mode == RUNNER_MODE_MANUAL:
            print("[信息] 手动测试模式：启动游戏并等待用户手动关闭。")
            game_process = launch_rimworld(use_quicktest=False)
        else:
            print("[信息] 快速测试模式：启动游戏并等待运行就绪信号。")
            game_process = launch_rimworld(use_quicktest=False)
    except Exception as e:
        restore_mod_list(original_mods_config)
        restore_disabled_autotest_spec_files(disabled_spec_pairs)
        remove_temp_autotest_spec(generated_spec_path)
        print(f"[错误] 启动游戏失败: {e}")
        return

    ready_signal = None
    if runner_mode in {RUNNER_MODE_AUTO, RUNNER_MODE_XML}:
        ready_signal = AUTO_TEST_READY_SIGNAL
    elif runner_mode == RUNNER_MODE_QUICK:
        ready_signal = READY_SIGNAL

    if ready_signal:
        print(f"等待信号: {ready_signal}...")
    else:
        print("等待用户关闭游戏...")

    start_time = time.time()
    last_pos = 0
    if SYSTEM_LOG_PATH.exists(): last_pos = SYSTEM_LOG_PATH.stat().st_size
    
    found = False
    try:
        while time.time() - start_time < timeout_seconds:
            if runner_mode in {RUNNER_MODE_AUTO, RUNNER_MODE_XML} and game_process is not None:
                exit_code = game_process.poll()
                if exit_code is not None:
                    print(f"\n[错误] RimWorld 进程已提前退出，退出码: {exit_code}")
                    break

            if runner_mode == RUNNER_MODE_MANUAL:
                process_exited = game_process.poll() is not None if game_process is not None else not is_rimworld_running()
                if process_exited:
                    print("\n[✔] 检测到用户已关闭 RimWorld。")
                    found = True
                    break
                print(f"\r耗时: {int(time.time() - start_time)}s", end="", flush=True)
                time.sleep(2)
                continue

            if SYSTEM_LOG_PATH.exists():
                current_size = SYSTEM_LOG_PATH.stat().st_size
                if current_size < last_pos: last_pos = 0
                if current_size > last_pos:
                    with open(SYSTEM_LOG_PATH, "r", encoding="utf-8", errors="ignore") as f:
                        f.seek(last_pos)
                        if ready_signal in f.read():
                            print("\n[✔] 检测到就绪信号！")
                            found = True
                            time.sleep(3)
                            break
            print(f"\r耗时: {int(time.time() - start_time)}s", end="", flush=True)
            time.sleep(2)
    except KeyboardInterrupt: print("\n[!] 中断。")
    finally:
        kill_rimworld()
        if is_auto_test_mode:
            update_mod_settings(
                auto_test_enabled=False,
                auto_test_target_package_id="",
                auto_test_mode="automatic",
                auto_test_scope="all",
                auto_test_capture_screenshots=False,
                clear_auto_test_batch_size=True,
            )
            if resolved_auto_test_output_dir:
                prune_auto_test_output_runs(Path(resolved_auto_test_output_dir))
        restore_mod_list(original_mods_config)
        restore_disabled_autotest_spec_files(disabled_spec_pairs)
        remove_temp_autotest_spec(generated_spec_path)
        print("\n======= 测试结束 =======")

    if found:
        if summarize_sync_log():
            print("[结果] 检测到就绪信号，日志摘要已输出。")
        else:
            print("[结果] 检测到就绪信号，但未找到同步日志。")
    else:
        print("[结果] 未检测到就绪信号，测试失败。")

if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--mode", choices=RUNNER_MODES, default=None, help="启动模式：quick/manual/auto/xml；默认会根据旧参数自动推断")
    parser.add_argument("--mods-file", default=None, help="自定义模组预设路径")
    parser.add_argument("--log-keyword", action="append", default=None, help="写入 LogSyncMod 配置 <keywords> 的条目，可重复传入")
    parser.add_argument("--clear-log-keywords", action="store_true", help="清空 LogSyncMod 配置中的 <keywords> 列表")
    parser.add_argument("--auto-test-package-id", default=None, help="auto/xml 模式下的目标模组 packageId")
    parser.add_argument("--auto-test-output-dir", default=None, help="自动测试报告输出目录")
    parser.add_argument("--auto-test-mode", choices=["automatic", "xml-driven"], default="automatic", help="兼容旧参数；未传 --mode 时用于自动推断 auto/xml")
    parser.add_argument("--auto-test-scope", choices=["all", "content", "recipes", "hediffs", "turrets"], default="all", help="auto/xml 模式下的测试范围：all/content/recipes/hediffs/turrets")
    parser.add_argument("--auto-test-capture-screenshots", action="store_true", help="auto/xml 模式下允许截图")
    parser.add_argument("--auto-test-batch-size", type=int, default=None, help="auto 模式下按范围限制测试数量；xml 模式下也可作为上限")
    parser.add_argument("--auto-test-defname", action="append", default=None, help="auto 模式下指定要测试的 defName，可重复传入；脚本会自动生成临时 XML 规则")
    parser.add_argument("--timeout-seconds", type=int, default=TIMEOUT, help="等待测试完成的最长秒数")
    parser.add_argument("--settings-only", action="store_true", help="只更新 LogSyncMod 配置（logPath/keywords），不启动游戏")
    args = parser.parse_args()

    runner_mode = determine_runner_mode(args.mode, args.auto_test_package_id, args.auto_test_mode)
    is_auto_test_mode = runner_mode in {RUNNER_MODE_AUTO, RUNNER_MODE_XML}

    keywords_to_write = DEFAULT_LOG_KEYWORDS
    if args.clear_log_keywords:
        keywords_to_write = []
    elif args.log_keyword is not None:
        keywords_to_write = args.log_keyword

    if runner_mode in {RUNNER_MODE_AUTO, RUNNER_MODE_XML} and not args.auto_test_package_id:
        raise SystemExit("[错误] auto/xml 模式必须提供 --auto-test-package-id")

    if runner_mode != RUNNER_MODE_AUTO and args.auto_test_defname:
        raise SystemExit("[错误] --auto-test-defname 仅支持在 auto 模式下使用")

    if args.settings_only:
        check_sandbox_access()
        resolved_auto_test_output_dir = resolve_auto_test_output_dir(args.auto_test_output_dir, args.auto_test_package_id if is_auto_test_mode else None)
        effective_auto_test_mode = None
        if runner_mode == RUNNER_MODE_XML:
            effective_auto_test_mode = "xml-driven"
        elif runner_mode == RUNNER_MODE_AUTO:
            effective_auto_test_mode = "xml-driven" if args.auto_test_defname else "automatic"

        update_mod_settings(
            log_keywords=keywords_to_write,
            clear_log_keywords=args.clear_log_keywords,
            auto_test_enabled=True if is_auto_test_mode else False,
            auto_test_target_package_id=args.auto_test_package_id if is_auto_test_mode else "",
            auto_test_output_path=resolved_auto_test_output_dir,
            auto_test_mode=effective_auto_test_mode if is_auto_test_mode else "automatic",
            auto_test_scope=args.auto_test_scope if is_auto_test_mode else "all",
            auto_test_capture_screenshots=True if args.auto_test_capture_screenshots and is_auto_test_mode else False,
            auto_test_batch_size=args.auto_test_batch_size if is_auto_test_mode else None,
            clear_auto_test_batch_size=(is_auto_test_mode and args.auto_test_batch_size is None) or (not is_auto_test_mode),
        )
        if resolved_auto_test_output_dir:
            prune_auto_test_output_runs(Path(resolved_auto_test_output_dir))
        kws = get_logsync_keywords()
        if kws is None:
            print(f"[警告] 读取 LogSyncMod keywords 失败: {SETTINGS_FILE}")
        else:
            print(f"[信息] LogSyncMod keywords 当前条目数: {len(kws)}")
            for kw in kws:
                print(f"- {kw}")
        raise SystemExit(0)

    monitor(
        runner_mode=runner_mode,
        mod_list_file=Path(args.mods_file) if args.mods_file else None,
        log_keywords=keywords_to_write,
        clear_log_keywords=args.clear_log_keywords,
        auto_test_package_id=args.auto_test_package_id,
        auto_test_output_dir=args.auto_test_output_dir,
        auto_test_mode=args.auto_test_mode,
        auto_test_scope=args.auto_test_scope,
        auto_test_capture_screenshots=True if args.auto_test_capture_screenshots and is_auto_test_mode else False,
        auto_test_batch_size=args.auto_test_batch_size,
        auto_test_defnames=args.auto_test_defname,
        timeout_seconds=max(1, int(args.timeout_seconds)),
    )
