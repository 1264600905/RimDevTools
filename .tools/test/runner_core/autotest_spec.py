# 自动测试 XML 规格文件的生成、禁用、恢复与残留清理
# 负责管理 LogSyncAutoTestSpecDef 的临时 XML 文件。

from pathlib import Path

from .config import DISABLED_SPEC_SUFFIX, TEMP_AUTOTEST_SPEC_FILE_NAME
from .mod_config import build_candidate_mod_roots, find_mod_directory_by_package_id


def normalize_scope_value(scope: str | None):
    normalized_scope = (scope or "").strip().lower()
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
    return scope_map.get(normalized_scope, "Standard")


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
        f"    <captureProjectileScreenshots>{capture_value}</captureProjectileScreenshots>",
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


def recover_stale_run_test_artifacts():
    recovered_count = 0
    cleaned_count = 0
    for root in build_candidate_mod_roots():
        for disabled_path in root.rglob(f"*{DISABLED_SPEC_SUFFIX}"):
            if not disabled_path.is_file():
                continue

            original_name = disabled_path.name[: -len(DISABLED_SPEC_SUFFIX)]
            original_path = disabled_path.with_name(original_name)
            try:
                if original_path.exists():
                    disabled_path.unlink()
                    cleaned_count += 1
                    print(f"[信息] 清理残留禁用 XML: {disabled_path}")
                else:
                    disabled_path.replace(original_path)
                    recovered_count += 1
                    print(f"[信息] 恢复上次中断遗留的 XML: {original_path}")
            except Exception as e:
                print(f"[警告] 恢复残留禁用 XML 失败: {disabled_path} ({e})")

        for temp_spec_path in root.rglob(TEMP_AUTOTEST_SPEC_FILE_NAME):
            if not temp_spec_path.is_file():
                continue
            try:
                temp_spec_path.unlink()
                cleaned_count += 1
                print(f"[信息] 清理残留临时自动测试 XML: {temp_spec_path}")
            except Exception as e:
                print(f"[警告] 清理残留临时自动测试 XML 失败: {temp_spec_path} ({e})")

    return recovered_count, cleaned_count
