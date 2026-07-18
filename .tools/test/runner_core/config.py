# 配置常量与路径定义
# 所有模块共享的常量、路径和默认值集中在此文件中。
# 可变配置统一从项目根目录的 mod_config.jsonc 读取。

import os
import sys
from pathlib import Path

# 将 .tools 目录加入 sys.path 以便导入 config_loader
sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from config_loader import find_project_root, load_config

# ==========================================
# 1. 从 mod_config.jsonc 读取的配置
# ==========================================

_cfg = load_config()

REPO_ROOT = find_project_root()
WORKSPACE_DIR = REPO_ROOT / ".tools" / "test"

# 环世界主程序路径（从 rimworld_dir 推导）
GAME_PATH = str(Path(_cfg["rimworld_dir"]) / "RimWorldWin64.exe")

# Steam 启动协议
STEAM_URL = "steam://rungameid/294100"

RUNNER_MODE_QUICK = "quick"
RUNNER_MODE_MANUAL = "manual"
RUNNER_MODE_AUTO = "auto"
RUNNER_MODE_XML = "xml"
RUNNER_MODES = [RUNNER_MODE_QUICK, RUNNER_MODE_MANUAL, RUNNER_MODE_AUTO, RUNNER_MODE_XML]
TEMP_AUTOTEST_SPEC_FILE_NAME = "LogSync_RunTest_TemporaryAutoTests.xml"
DISABLED_SPEC_SUFFIX = ".logsyncruntest.disabled"

# 模组发出的就绪信号字符串 (检测到此字符串后脚本将自动关闭游戏)，就绪和完成信号由日志同步模组负责禁止修改。
READY_SIGNAL = "[LogSyncMod] RUNTIME_READY_TO_CLOSE"
AUTO_TEST_READY_SIGNAL = "[LogSyncAutoTest] COMPLETED"

# 默认不改动当前配置中的 keywords。
DEFAULT_LOG_KEYWORDS: list[str] = _cfg.get("log_keywords", ["[MyCustomMod]"])

# Steam 启动检测超时时间 (秒)
STEAM_LAUNCH_DETECT_TIMEOUT = _cfg.get("timeouts", {}).get("steam_launch_detect", 120)

# 最大等待超时时间 (秒)
TIMEOUT = _cfg.get("timeouts", {}).get("max_wait", 600)

# 如果检测到已有 RimWorld 正在运行，最多等待其自行退出的时间（秒）
WAIT_FOR_EXISTING_GAME_EXIT_TIMEOUT = _cfg.get("timeouts", {}).get("existing_game_exit", 600)

# 检测到已有 RimWorld 已退出后，额外等待一段时间再开始测试（秒）
POST_EXISTING_GAME_EXIT_DELAY = _cfg.get("timeouts", {}).get("post_exit_delay", 10)

# 测试日志总保留数（包含当前日志文件本身）
MAX_MANAGED_LOG_FILES = _cfg.get("retention", {}).get("max_log_files", 3)
# 自动测试输出总保留运行数
MAX_MANAGED_AUTO_TEST_RUNS = _cfg.get("retention", {}).get("max_auto_test_runs", 2)

# 默认的测试激活模组列表 (当没有预设文件时使用)
MIN_ACTIVE_MODS = _cfg.get("min_active_mods", [
    "brrainz.harmony",
    "ludeon.rimworld",
    "trigger.logsyncmod",
])

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
