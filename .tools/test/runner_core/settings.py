# LogSyncMod 设置文件读写
# 负责在启动游戏前修改 LogSyncMod 的配置文件。

import xml.etree.ElementTree as ET

from .config import SETTINGS_FILE, TARGET_SYNC_LOG


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
    log_max_lines: int | None = None,
    auto_test_enabled: bool | None = None,
    auto_test_target_package_id: str | None = None,
    auto_test_output_path: str | None = None,
    auto_test_mode: str | None = None,
    auto_test_scope: str | None = None,
    auto_test_capture_screenshots: bool | None = None,
    auto_test_batch_size: int | None = None,
    auto_test_prevent_pause: bool | None = None,
    auto_test_use_fast_speed: bool | None = None,
    auto_test_pocket_map_max_concurrent: int | None = None,
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

        if log_max_lines is not None:
            normalized_log_max_lines = str(max(1, int(log_max_lines)))
            if (mod_settings.findtext("logMaxLines") or "").strip() != normalized_log_max_lines:
                ensure_text_node(mod_settings, "logMaxLines", normalized_log_max_lines)
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
                "standard": "Standard",
                "all": "Standard",
                "content": "Content",
                "recipes": "Recipes",
                "hediffs": "Hediffs",
                "turrets": "Turrets",
                "rangedweapons": "RangedWeapons",
                "ranged-weapons": "RangedWeapons",
                "ranged_weapons": "RangedWeapons",
                "meleeweapons": "MeleeWeapons",
                "melee-weapons": "MeleeWeapons",
                "melee_weapons": "MeleeWeapons",
                "threeviews": "ThreeViews",
                "three-views": "ThreeViews",
                "three_views": "ThreeViews",
                "combatpawns": "CombatPawns",
                "combat-pawns": "CombatPawns",
                "combat_pawns": "CombatPawns",
                "skillpocketmaps": "SkillPocketMaps",
                "skill-pocket-maps": "SkillPocketMaps",
                "skill_pocket_maps": "SkillPocketMaps",
            }
            desired_scope = scope_map.get(normalized_scope, "Standard")
            if (mod_settings.findtext("autoTestScope") or "").strip() != desired_scope:
                ensure_text_node(mod_settings, "autoTestScope", desired_scope)
                changed = True

        if auto_test_capture_screenshots is not None:
            desired_value = "True" if auto_test_capture_screenshots else "False"
            if (mod_settings.findtext("autoTestCaptureScreenshots") or "").strip() != desired_value:
                ensure_text_node(mod_settings, "autoTestCaptureScreenshots", desired_value)
                changed = True

        if auto_test_prevent_pause is not None:
            desired_value = "True" if auto_test_prevent_pause else "False"
            if (mod_settings.findtext("autoTestPreventPause") or "").strip() != desired_value:
                ensure_text_node(mod_settings, "autoTestPreventPause", desired_value)
                changed = True

        if auto_test_use_fast_speed is not None:
            desired_value = "True" if auto_test_use_fast_speed else "False"
            if (mod_settings.findtext("autoTestUseFastSpeed") or "").strip() != desired_value:
                ensure_text_node(mod_settings, "autoTestUseFastSpeed", desired_value)
                changed = True

        if clear_auto_test_batch_size:
            if remove_child_node(mod_settings, "autoTestBatchSize"):
                changed = True
        elif auto_test_batch_size is not None:
            normalized_batch_size = str(max(1, int(auto_test_batch_size)))
            if (mod_settings.findtext("autoTestBatchSize") or "").strip() != normalized_batch_size:
                ensure_text_node(mod_settings, "autoTestBatchSize", normalized_batch_size)
                changed = True

        if auto_test_pocket_map_max_concurrent is not None:
            normalized_limit = str(max(1, int(auto_test_pocket_map_max_concurrent)))
            if (mod_settings.findtext("autoTestPocketMapMaxConcurrent") or "").strip() != normalized_limit:
                ensure_text_node(mod_settings, "autoTestPocketMapMaxConcurrent", normalized_limit)
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
