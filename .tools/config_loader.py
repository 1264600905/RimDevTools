# 公共配置加载模块
# 所有 .tools 下的 Python 脚本统一通过此模块读取 mod_config.jsonc。
# 提供:
#   - find_project_root(): 从任意深度向上查找 mod_config.jsonc 定位项目根目录
#   - load_config(): 加载并缓存 JSONC 配置（自动去除 // 注释）

import re
import json
from pathlib import Path

_CONFIG_CACHE = None
_PROJECT_ROOT = None


def find_project_root(start: Path | None = None) -> Path:
    """从 start 开始向上逐级查找 mod_config.jsonc，返回其所在目录作为项目根目录。"""
    global _PROJECT_ROOT
    if _PROJECT_ROOT is not None:
        return _PROJECT_ROOT
    p = (start or Path(__file__)).resolve().parent
    while p != p.parent:
        if (p / "mod_config.jsonc").exists():
            _PROJECT_ROOT = p
            return p
        p = p.parent
    raise FileNotFoundError("找不到 mod_config.jsonc，无法定位项目根目录")


def load_config() -> dict:
    """加载 mod_config.jsonc 并缓存。自动去除 // 行注释。"""
    global _CONFIG_CACHE
    if _CONFIG_CACHE is not None:
        return _CONFIG_CACHE
    config_path = find_project_root() / "mod_config.jsonc"
    text = config_path.read_text(encoding="utf-8")
    # 去除 // 行注释（JSON 值中不会出现独立的 //）
    cleaned = re.sub(r"//.*", "", text)
    _CONFIG_CACHE = json.loads(cleaned)
    return _CONFIG_CACHE


def get_tools_dir() -> Path:
    """返回 .tools 目录的绝对路径。"""
    return find_project_root() / ".tools"


def get_rimworld_dir() -> Path:
    """返回 RimWorld 安装根目录。"""
    return Path(load_config()["rimworld_dir"])


def get_search_paths() -> list[str]:
    """返回所有模组搜索路径（rimworld_dir/Mods + workshop_paths），仅保留存在的路径。"""
    config = load_config()
    paths = [str(get_rimworld_dir() / "Mods")]
    paths.extend(config.get("workshop_paths", []))
    return [p for p in paths if Path(p).exists()]
