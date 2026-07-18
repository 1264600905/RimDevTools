# ModsConfig.xml 管理与模组信息解析
# 负责模组列表的读取、切换、备份与恢复。

import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

from .config import (
    ABOUT_XML_PATH,
    GAME_PATH,
    MIN_ACTIVE_MODS,
    MODS_CONFIG_BACKUP_FILE,
    MODS_CONFIG_FILE,
    REPO_ROOT,
    RIMWORLD_CONFIG_DIR,
)


def check_sandbox_access():
    """检查是否能够访问沙盒外的关键文件。"""
    if not RIMWORLD_CONFIG_DIR.parent.exists():
        print("=" * 60)
        print("[错误] 无法访问环世界配置目录！脚本可能处于受限沙盒中。")
        print(f"预期路径: {RIMWORLD_CONFIG_DIR.parent}")
        print("=" * 60)
        sys.exit(1)


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


def load_mods_from_file(path: Path):
    """从文件中解析模组列表，支持 XML <li> 格式和纯文本行格式。"""
    if not path.exists():
        return None
    text = path.read_text(encoding="utf-8", errors="ignore")

    # 尝试匹配 <li>packageId</li>
    mods = []
    for m in re.finditer(r"<li>\s*([^<]+?)\s*</li>", text, flags=re.IGNORECASE):
        mod_id = (m.group(1) or "").strip()
        if mod_id:
            mods.append(mod_id)

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
        if p.exists():
            return p
    return None


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

    game_path = Path(GAME_PATH)
    if game_path.exists() and normalized_package_id.startswith("ludeon.rimworld"):
        data_root = game_path.parent / "Data"
        official_map = {
            "ludeon.rimworld": data_root / "Core",
            "ludeon.rimworld.royalty": data_root / "Royalty",
            "ludeon.rimworld.ideology": data_root / "Ideology",
            "ludeon.rimworld.biotech": data_root / "Biotech",
            "ludeon.rimworld.anomaly": data_root / "Anomaly",
            "ludeon.rimworld.odyssey": data_root / "Odyssey",
        }
        official_dir = official_map.get(normalized_package_id)
        if official_dir and official_dir.exists():
            return official_dir

    for root in build_candidate_mod_roots():
        for about_xml_path in root.rglob("About.xml"):
            if read_mod_package_id_from_about(about_xml_path) != normalized_package_id:
                continue
            if about_xml_path.parent.name.lower() == "about":
                return about_xml_path.parent.parent
            return about_xml_path.parent

    return None


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
    if not MODS_CONFIG_FILE.exists():
        return None
    original_text = MODS_CONFIG_FILE.read_text(encoding="utf-8", errors="ignore")
    try:
        MODS_CONFIG_BACKUP_FILE.write_text(original_text, encoding="utf-8")

        # 确定要启用的模组
        if mod_list_file is None:
            mod_list_file = pick_default_mods_file()
        desired_mods = load_mods_from_file(mod_list_file) if mod_list_file else None
        if not desired_mods:
            print("[信息] 未找到有效的模组预设文件，将使用默认最小集。")
            desired_mods = MIN_ACTIVE_MODS
        else:
            file_name = mod_list_file.name if mod_list_file else "未知"
            print(f"[信息] 已加载模组预设: {file_name} (共 {len(desired_mods)} 个)")

        desired_mods = list(desired_mods)
        for package_id, label in include_package_ids or []:
            desired_mods = ensure_mod_enabled(desired_mods, package_id, label=label)

        tree = ET.parse(MODS_CONFIG_FILE)
        root = tree.getroot()
        active_mods = root.find("activeMods")
        if active_mods is None:
            return original_text

        for child in list(active_mods):
            active_mods.remove(child)
        for mod_id in desired_mods:
            li = ET.SubElement(active_mods, "li")
            li.text = mod_id

        tree.write(MODS_CONFIG_FILE, encoding="utf-8", xml_declaration=True)
        return original_text
    except Exception as e:
        print(f"[警告] 修改 ModsConfig 失败: {e}")
        return original_text


def restore_mod_list(original_text):
    if original_text is None:
        return
    try:
        MODS_CONFIG_FILE.write_text(original_text, encoding="utf-8")
        if MODS_CONFIG_BACKUP_FILE.exists():
            MODS_CONFIG_BACKUP_FILE.unlink()
    except:
        pass
