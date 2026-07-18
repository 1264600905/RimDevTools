# 日志文件的归档、裁剪与摘要输出
# 负责同步日志和自动测试输出的生命周期管理。

from datetime import datetime
from pathlib import Path

from .config import MAX_MANAGED_AUTO_TEST_RUNS, MAX_MANAGED_LOG_FILES, TARGET_SYNC_LOG


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
    suffixes = ["_Report.md", "_Result.json", "_Manifest.json", "_Screenshots"]
    matched_suffix = None
    for suffix in suffixes:
        if name.endswith(suffix):
            matched_suffix = suffix
            break

    if matched_suffix is None:
        return None

    base_name = name[: -len(matched_suffix)]
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

    obsolete_runs = sorted_runs[keep_count:]
    for _, paths in obsolete_runs:
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
