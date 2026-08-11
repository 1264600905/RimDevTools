# 注意：此脚本需要读写"沙盒外"的 RimWorld 配置目录（例如 ModsConfig.xml、Player.log 等）。
# - 如果你在 Cursor / 其他受限沙盒中运行：请开启"访问外部文件"权限，否则脚本会因为无法访问
#   `%USERPROFILE%\\AppData\\LocalLow\\Ludeon Studios\\RimWorld by Ludeon Studios` 而失败。
# - 这里说的"高权限"是指"允许访问沙盒外文件"，不是指 Windows 管理员权限。
#  注意绝对禁止并发执行此脚本，因为游戏只有一个player.log文件，如果并发执行会导致日志内容混乱，甚至可能导致游戏崩溃。

import json
import subprocess
import time
import argparse
from pathlib import Path

from runner_core.config import (
    AUTO_TEST_READY_SIGNAL,
    DEFAULT_LOG_KEYWORDS,
    MODS_CONFIG_BACKUP_FILE,
    MODS_CONFIG_FILE,
    READY_SIGNAL,
    REPO_ROOT,
    RUNNER_MODE_AUTO,
    RUNNER_MODE_MANUAL,
    RUNNER_MODE_QUICK,
    RUNNER_MODE_XML,
    RUNNER_MODES,
    SYSTEM_LOG_PATH,
    TARGET_SYNC_LOG,
    TIMEOUT,
    WORKSPACE_DIR,
)
from runner_core.mod_config import (
    check_sandbox_access,
    read_current_mod_package_id,
    restore_mod_list,
    set_minimal_mod_list,
)
from runner_core.autotest_spec import (
    disable_existing_autotest_spec_files,
    normalize_scope_value,
    recover_stale_run_test_artifacts,
    remove_temp_autotest_spec,
    restore_disabled_autotest_spec_files,
    write_temp_autotest_spec,
)
from runner_core.settings import get_logsync_keywords, update_mod_settings
from runner_core.log_manager import (
    prune_auto_test_output_runs,
    rotate_target_log,
    summarize_sync_log,
)
from runner_core.game_process import (
    is_rimworld_running,
    kill_rimworld,
    launch_rimworld,
    wait_for_existing_rimworld_to_exit,
)

AUTO_TEST_NON_FAILURE_STATUSES = {
    "passed",
    "passed_after_retry",
    "skipped",
    "unsupported",
    "compatibility_warning",
}


# ==========================================
# 部署集成
# ==========================================

def run_deploy(sync_source: bool = False):
    """调用 deploy.ps1 执行编译部署，返回是否成功。"""
    deploy_script = REPO_ROOT / ".tools" / "deploy.ps1"
    if not deploy_script.exists():
        print(f"[错误] 未找到部署脚本: {deploy_script}")
        return False

    print("======= 开始执行部署 =======")
    cmd = ["powershell", "-ExecutionPolicy", "Bypass", "-File", str(deploy_script)]
    if sync_source:
        cmd.append("-SyncSource")

    result = subprocess.run(cmd, cwd=str(REPO_ROOT))
    if result.returncode != 0:
        print(f"[错误] 部署失败 (exit code {result.returncode})，已跳过测试。")
        return False

    print("[信息] 部署成功。")
    return True


# ==========================================
# 辅助函数
# ==========================================

def resolve_auto_test_output_dir(auto_test_output_dir: str | None, auto_test_package_id: str | None):
    if not auto_test_package_id:
        return None
    if auto_test_output_dir is not None:
        return auto_test_output_dir
    return str(WORKSPACE_DIR / "AutoTestOutput")


def determine_runner_mode(explicit_mode: str | None, auto_test_package_id: str | None, auto_test_mode: str | None):
    if explicit_mode:
        return explicit_mode

    if auto_test_package_id:
        normalized_auto_test_mode = (auto_test_mode or "").strip().lower()
        return RUNNER_MODE_XML if normalized_auto_test_mode in {"xml", "xml-driven", "xmldriven"} else RUNNER_MODE_AUTO

    return RUNNER_MODE_QUICK


def snapshot_result_files(output_dir: str | None):
    if not output_dir:
        return set()
    path = Path(output_dir)
    if not path.is_dir():
        return set()
    return {item.resolve() for item in path.glob("*_Result.json") if item.is_file()}


def find_successful_auto_test_result(output_dir: str | None, package_id: str | None, existing_files: set[Path]):
    if not output_dir or not package_id:
        return None

    output_path = Path(output_dir)
    if not output_path.is_dir():
        return None

    candidates = sorted(
        (item for item in output_path.glob("*_Result.json") if item.is_file() and item.resolve() not in existing_files),
        key=lambda item: item.stat().st_mtime_ns,
        reverse=True,
    )
    for result_path in candidates:
        try:
            result = json.loads(result_path.read_text(encoding="utf-8-sig"))
        except (OSError, json.JSONDecodeError):
            continue

        results = result.get("results")
        if (
            str(result.get("packageId", "")).lower() != package_id.lower()
            or result.get("completeness") != "Complete"
            or result.get("manifestMissing") != 0
            or result.get("manifestExecuted", -1) < result.get("manifestPlanned", 0)
            or not isinstance(results, list)
            or any(not isinstance(item, dict) or item.get("status") not in AUTO_TEST_NON_FAILURE_STATUSES for item in results)
        ):
            continue
        return result_path, result
    return None


def read_new_system_log(last_pos: int, ready_signal: str | None):
    if not SYSTEM_LOG_PATH.exists():
        return False, last_pos

    current_size = SYSTEM_LOG_PATH.stat().st_size
    if current_size < last_pos:
        last_pos = 0
    if current_size <= last_pos:
        return False, last_pos

    with open(SYSTEM_LOG_PATH, "r", encoding="utf-8", errors="ignore") as log_file:
        log_file.seek(last_pos)
        new_content = log_file.read()
    return bool(ready_signal and ready_signal in new_content), current_size


# ==========================================
# 主编排
# ==========================================

def monitor(
    runner_mode: str = RUNNER_MODE_QUICK,
    mod_list_file: Path | None = None,
    log_keywords: list[str] | None = None,
    clear_log_keywords: bool = False,
    log_max_lines: int | None = None,
    auto_test_package_id: str | None = None,
    auto_test_output_dir: str | None = None,
    auto_test_mode: str | None = "automatic",
    auto_test_scope: str | None = "all",
    auto_test_capture_screenshots: bool | None = None,
    auto_test_batch_size: int | None = None,
    auto_test_prevent_pause: bool | None = True,
    auto_test_use_fast_speed: bool | None = True,
    auto_test_pocket_map_max_concurrent: int | None = None,
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

    recovered_specs, cleaned_specs = recover_stale_run_test_artifacts()
    if recovered_specs > 0 or cleaned_specs > 0:
        print(f"[信息] 已恢复/清理上次中断遗留的自动测试文件: recovered={recovered_specs}, cleaned={cleaned_specs}")

    # 恢复上次意外中断
    if MODS_CONFIG_BACKUP_FILE.exists():
        try:
            backup_text = MODS_CONFIG_BACKUP_FILE.read_text(encoding="utf-8", errors="ignore")
            MODS_CONFIG_FILE.write_text(backup_text, encoding="utf-8")
            MODS_CONFIG_BACKUP_FILE.unlink()
        except:
            pass

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
        log_max_lines=log_max_lines,
        auto_test_enabled=True if is_auto_test_mode else False,
        auto_test_target_package_id=auto_test_package_id if is_auto_test_mode else "",
        auto_test_output_path=resolved_auto_test_output_dir if is_auto_test_mode else None,
        auto_test_mode=effective_auto_test_mode if is_auto_test_mode else "automatic",
        auto_test_scope=auto_test_scope if is_auto_test_mode else "all",
        auto_test_capture_screenshots=auto_test_capture_screenshots if is_auto_test_mode else False,
        auto_test_batch_size=auto_test_batch_size if is_auto_test_mode else None,
        auto_test_prevent_pause=auto_test_prevent_pause if is_auto_test_mode else None,
        auto_test_use_fast_speed=auto_test_use_fast_speed if is_auto_test_mode else None,
        auto_test_pocket_map_max_concurrent=auto_test_pocket_map_max_concurrent if is_auto_test_mode else None,
        clear_auto_test_batch_size=(is_auto_test_mode and auto_test_batch_size is None) or (not is_auto_test_mode),
    )
    current_package_id = read_current_mod_package_id()
    if current_package_id:
        print(f"[信息] 当前项目 packageId: {current_package_id}")
    else:
        print(f"[警告] 未能从 About.xml 读取 packageId")

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
    existing_result_files = snapshot_result_files(resolved_auto_test_output_dir)

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
    if SYSTEM_LOG_PATH.exists():
        last_pos = SYSTEM_LOG_PATH.stat().st_size

    found = False
    completion_source = None
    try:
        while time.time() - start_time < timeout_seconds:
            if runner_mode == RUNNER_MODE_MANUAL:
                process_exited = game_process.poll() is not None if game_process is not None else not is_rimworld_running()
                if process_exited:
                    print("\n[OK] 检测到用户已关闭 RimWorld。")
                    found = True
                    completion_source = "manual"
                    break
                print(f"\r耗时: {int(time.time() - start_time)}s", end="", flush=True)
                time.sleep(2)
                continue

            found, last_pos = read_new_system_log(last_pos, ready_signal)
            if found:
                completion_source = "signal"
                print("\n[OK] 检测到就绪信号！")
                time.sleep(3)
                break

            if runner_mode in {RUNNER_MODE_AUTO, RUNNER_MODE_XML} and game_process is not None:
                exit_code = game_process.poll()
                if exit_code is not None:
                    for _ in range(3):
                        time.sleep(1)
                        found, last_pos = read_new_system_log(last_pos, ready_signal)
                        if found:
                            completion_source = "signal"
                            print("\n[OK] 游戏退出后检测到就绪信号！")
                            break

                    if not found and exit_code == 0:
                        result_match = find_successful_auto_test_result(
                            resolved_auto_test_output_dir,
                            auto_test_package_id,
                            existing_result_files,
                        )
                        if result_match:
                            result_path, result = result_match
                            found = True
                            completion_source = "result"
                            print(
                                "\n[OK] 完成信号缺失，但已验证本轮结果文件: "
                                f"{result_path.name} (passed={len(result['results'])})"
                            )
                    if not found:
                        print(f"\n[错误] RimWorld 进程已提前退出，退出码: {exit_code}")
                    break
            print(f"\r耗时: {int(time.time() - start_time)}s", end="", flush=True)
            time.sleep(2)
    except KeyboardInterrupt:
        print("\n[!] 中断。")
    finally:
        kill_rimworld()
        if is_auto_test_mode:
            update_mod_settings(
                auto_test_enabled=False,
                auto_test_target_package_id="",
                auto_test_mode="automatic",
                auto_test_scope="all",
                auto_test_capture_screenshots=False,
                auto_test_prevent_pause=True,
                auto_test_use_fast_speed=True,
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
            print(f"[结果] 测试成功（{completion_source}），日志摘要已输出。")
        else:
            print(f"[结果] 测试成功（{completion_source}），但未找到同步日志。")
    else:
        print("[结果] 未检测到就绪信号，测试失败。")
    return found


# ==========================================
# CLI 入口
# ==========================================

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="环世界自动化监控测试脚本")

    # 部署集成参数
    parser.add_argument("--deploy", action="store_true", help="在测试前先执行 deploy.ps1 部署，部署失败则跳过测试")
    parser.add_argument("--sync-source", action="store_true", help="部署时同步 source 文件夹（仅在 --deploy 时生效）")

    # 原有参数
    parser.add_argument("--mode", choices=RUNNER_MODES, default=None, help="启动模式：quick/manual/auto/xml；默认会根据旧参数自动推断")
    parser.add_argument("--mods-file", default=None, help="自定义模组预设路径")
    parser.add_argument("--log-keyword", action="append", default=None, help="写入 LogSyncMod 配置 <keywords> 的条目，可重复传入")
    parser.add_argument("--clear-log-keywords", action="store_true", help="清空 LogSyncMod 配置中的 <keywords> 列表")
    parser.add_argument("--log-max-lines", type=int, default=None, help="写入 LogSyncMod 配置 <logMaxLines>；普通日志达到上限后仅继续保留 Error")
    parser.add_argument("--auto-test-package-id", default=None, help="auto/xml 模式下的目标模组 packageId")
    parser.add_argument("--auto-test-output-dir", default=None, help="自动测试报告输出目录")
    parser.add_argument("--auto-test-mode", choices=["automatic", "xml-driven"], default="automatic", help="兼容旧参数；未传 --mode 时用于自动推断 auto/xml")
    parser.add_argument("--auto-test-scope", choices=["standard", "all", "content", "recipes", "hediffs", "turrets", "rangedweapons", "meleeweapons", "threeviews", "combatpawns", "skillpocketmaps"], default="standard", help="auto/xml 模式下的测试范围")
    parser.add_argument("--auto-test-capture-screenshots", action="store_true", help="auto/xml 模式下允许截图")
    parser.add_argument("--auto-test-batch-size", type=int, default=None, help="auto 模式下按范围限制测试数量；xml 模式下也可作为上限")
    parser.add_argument("--auto-test-pocket-map-max-concurrent", type=int, default=None, help="auto/xml 模式下口袋地图统一最大并发数")
    parser.add_argument("--auto-test-prevent-pause", dest="auto_test_prevent_pause", action="store_true", help="auto/xml 模式下阻止游戏暂停")
    parser.add_argument("--auto-test-no-prevent-pause", dest="auto_test_prevent_pause", action="store_false", help="auto/xml 模式下不阻止游戏暂停")
    parser.add_argument("--auto-test-use-fast-speed", dest="auto_test_use_fast_speed", action="store_true", help="auto/xml 模式下强制二倍速")
    parser.add_argument("--auto-test-no-fast-speed", dest="auto_test_use_fast_speed", action="store_false", help="auto/xml 模式下不强制二倍速")
    parser.add_argument("--auto-test-defname", action="append", default=None, help="auto 模式下指定要测试的 defName，可重复传入")
    parser.add_argument("--timeout-seconds", type=int, default=TIMEOUT, help="等待测试完成的最长秒数")
    parser.add_argument("--settings-only", action="store_true", help="只更新 LogSyncMod 配置（logPath/keywords），不启动游戏")
    parser.set_defaults(auto_test_prevent_pause=None, auto_test_use_fast_speed=None)
    args = parser.parse_args()

    # 部署阶段：如果指定了 --deploy，先执行部署
    if args.deploy:
        if not run_deploy(sync_source=args.sync_source):
            raise SystemExit(1)

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
        resolved_auto_test_output_dir_val = resolve_auto_test_output_dir(args.auto_test_output_dir, args.auto_test_package_id if is_auto_test_mode else None)
        effective_auto_test_mode = None
        if runner_mode == RUNNER_MODE_XML:
            effective_auto_test_mode = "xml-driven"
        elif runner_mode == RUNNER_MODE_AUTO:
            effective_auto_test_mode = "xml-driven" if args.auto_test_defname else "automatic"

        update_mod_settings(
            log_keywords=keywords_to_write,
            clear_log_keywords=args.clear_log_keywords,
            log_max_lines=args.log_max_lines,
            auto_test_enabled=True if is_auto_test_mode else False,
            auto_test_target_package_id=args.auto_test_package_id if is_auto_test_mode else "",
            auto_test_output_path=resolved_auto_test_output_dir_val,
            auto_test_mode=effective_auto_test_mode if is_auto_test_mode else "automatic",
            auto_test_scope=args.auto_test_scope if is_auto_test_mode else "all",
            auto_test_capture_screenshots=True if args.auto_test_capture_screenshots and is_auto_test_mode else False,
            auto_test_batch_size=args.auto_test_batch_size if is_auto_test_mode else None,
            auto_test_prevent_pause=(True if is_auto_test_mode and args.auto_test_prevent_pause is None else args.auto_test_prevent_pause),
            auto_test_use_fast_speed=(True if is_auto_test_mode and args.auto_test_use_fast_speed is None else args.auto_test_use_fast_speed),
            auto_test_pocket_map_max_concurrent=args.auto_test_pocket_map_max_concurrent if is_auto_test_mode else None,
            clear_auto_test_batch_size=(is_auto_test_mode and args.auto_test_batch_size is None) or (not is_auto_test_mode),
        )
        if resolved_auto_test_output_dir_val:
            prune_auto_test_output_runs(Path(resolved_auto_test_output_dir_val))
        kws = get_logsync_keywords()
        if kws is None:
            print(f"[警告] 读取 LogSyncMod keywords 失败")
        else:
            print(f"[信息] LogSyncMod keywords 当前条目数: {len(kws)}")
            for kw in kws:
                print(f"- {kw}")
        raise SystemExit(0)

    success = monitor(
        runner_mode=runner_mode,
        mod_list_file=Path(args.mods_file) if args.mods_file else None,
        log_keywords=keywords_to_write,
        clear_log_keywords=args.clear_log_keywords,
        log_max_lines=args.log_max_lines,
        auto_test_package_id=args.auto_test_package_id,
        auto_test_output_dir=args.auto_test_output_dir,
        auto_test_mode=args.auto_test_mode,
        auto_test_scope=args.auto_test_scope,
        auto_test_capture_screenshots=True if args.auto_test_capture_screenshots and is_auto_test_mode else False,
        auto_test_batch_size=args.auto_test_batch_size,
        auto_test_prevent_pause=(True if is_auto_test_mode and args.auto_test_prevent_pause is None else args.auto_test_prevent_pause),
        auto_test_use_fast_speed=(True if is_auto_test_mode and args.auto_test_use_fast_speed is None else args.auto_test_use_fast_speed),
        auto_test_pocket_map_max_concurrent=args.auto_test_pocket_map_max_concurrent,
        auto_test_defnames=args.auto_test_defname,
        timeout_seconds=max(1, int(args.timeout_seconds)),
    )
    raise SystemExit(0 if success else 1)
