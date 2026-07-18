import os
import sys
import json
import shutil
import argparse
import xml.etree.ElementTree as ET
import subprocess
from pathlib import Path

# 将 .tools 目录加入 sys.path 以便导入 config_loader
sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
from config_loader import find_project_root, load_config, get_tools_dir, get_search_paths

TARGET_DIR = find_project_root() / "参考文档"


def resolve_ilspy_command(ilspycmd_raw: str):
    """解析 ilspycmd 的实际启动命令，兼容独立 exe 与 dotnet tool 布局。"""
    if not ilspycmd_raw:
        return None

    configured_path = (get_tools_dir() / ilspycmd_raw).resolve()
    if not configured_path.exists():
        return None

    # 直接配置为 dll 时，显式通过 dotnet 启动。
    if configured_path.suffix.lower() == ".dll":
        return ["dotnet", str(configured_path)]

    # 兼容 dotnet tool shim：exe 旁边若没有实际入口 dll，则回退到 .store 中查找。
    if configured_path.suffix.lower() == ".exe":
        sibling_dll = configured_path.with_suffix(".dll")
        if sibling_dll.exists():
            return [str(configured_path)]

        store_root = configured_path.parent / ".store"
        if store_root.exists():
            dll_candidates = sorted(store_root.glob("**/tools/*/any/ilspycmd.dll"))
            if dll_candidates:
                return ["dotnet", str(dll_candidates[-1])]

        return [str(configured_path)]

    return [str(configured_path)]


def get_child_ci(element, tag_name):
    if element is None: return None
    tag_lower = tag_name.lower()
    for child in element:
        if child.tag.lower() == tag_lower:
            return child
    return None

def get_text_ci(element, tag_name):
    child = get_child_ci(element, tag_name)
    return child.text if child is not None else None

def get_children_ci(element, tag_name):
    if element is None: return []
    tag_lower = tag_name.lower()
    return [child for child in element if child.tag.lower() == tag_lower]

def parse_about_xml(about_file: Path, target_version: str | None = None):
    """解析 About.xml，提取名称、包名和前置依赖"""
    try:
        if not target_version:
            cfg = load_config()
            target_version = cfg.get("game_version", "1.5")

        tree = ET.parse(about_file)
        root = tree.getroot()
        
        name = get_text_ci(root, 'name') or "Unknown_Name"
        package_id = get_text_ci(root, 'packageId') or "Unknown.PackageId"

        deps = []
        # 1. 兼容旧版 <modDependencies>
        mod_deps = get_child_ci(root, 'modDependencies')
        if mod_deps is not None:
            for li in get_children_ci(mod_deps, 'li'):
                dep_pkg = get_text_ci(li, 'packageId')
                dep_name = get_text_ci(li, 'displayName')
                if dep_pkg:
                    deps.append({"packageId": dep_pkg, "displayName": dep_name or dep_pkg})

        # 2. 兼容新版 <modDependenciesByVersion>
        deps_by_version = get_child_ci(root, 'modDependenciesByVersion')
        if deps_by_version is not None:
            # 尝试寻找指定版本 (如 v1.5 或 v1.6)
            target_v_node = get_child_ci(deps_by_version, f'v{target_version}')

            # 如果没找到指定版本，尝试找最高版本(倒序遍历)
            if target_v_node is None:
                versions = list(deps_by_version)
                if versions:
                    target_v_node = versions[-1]  # 通常最新版本写在最下面

            if target_v_node is not None:
                for li in get_children_ci(target_v_node, 'li'):
                    dep_pkg = get_text_ci(li, 'packageId')
                    dep_name = get_text_ci(li, 'displayName')
                    if dep_pkg:
                        if not any(d.get("packageId") == dep_pkg for d in deps):
                            deps.append({"packageId": dep_pkg, "displayName": dep_name or dep_pkg})

        return name, package_id, deps
    except Exception as e:
        return None, None, []


def execute_rg_search(pattern: str, fixed_strings=False):
    """使用 rg 高速搜索 About.xml 文件"""
    search_paths = get_search_paths()
    if not search_paths:
        return []

    cmd = ["rg", "-i", "-l"]
    if fixed_strings:
        cmd.append("-F")
    cmd.extend([pattern, "--glob", "**/About.xml"])
    cmd.extend(search_paths)

    try:
        result = subprocess.run(cmd, capture_output=True, text=True, encoding='utf-8')
        if result.returncode == 0:
            return result.stdout.strip().splitlines()
        return []
    except FileNotFoundError:
        print("Error: 找不到 'rg' 命令，请确保已安装 ripgrep 并添加到系统 PATH 中。", file=sys.stderr)
        sys.exit(1)


def search_mods(keyword: str, output_json=True, fuzzy=False):
    """方法 1：搜索模组"""
    if fuzzy:
        # 极致模糊匹配：将关键词所有非空字符拆分并以 .* 连接
        clean_keyword = "".join(keyword.split())
        pattern = ".*".join(list(clean_keyword))
        matched_files = execute_rg_search(pattern, fixed_strings=False)
    else:
        matched_files = execute_rg_search(keyword, fixed_strings=True)
        
    results = []
    keyword_lower = keyword.lower()

    for fpath in matched_files:
        about_path = Path(fpath)
        name, pkg_id, deps = parse_about_xml(about_path)
        if name:
            folder_name = about_path.parent.parent.name
            
            is_match = False
            if fuzzy:
                # 模糊搜索只要 rg 命中即可返回（rg 可能会命中描述、作者等）
                is_match = True
            else:
                # 【关键修复】确保关键词是匹配在"名称"、"包名"或"文件夹名称"上
                if (keyword_lower in name.lower() or
                    keyword_lower in (pkg_id or "").lower() or
                    keyword_lower in folder_name.lower()):
                    is_match = True

            if is_match:
                results.append({
                    "path": str(about_path.parent.parent),
                    "name": name,
                    "packageId": pkg_id,
                    "dependencies": deps
                })

    # 按照匹配精准度排序：包名完全一致 > 名称完全一致 > 包含关键词 > 模糊匹配
    def sort_key(mod):
        pkg = (mod.get("packageId") or "").lower()
        nm = (mod.get("name") or "").lower()
        folder = Path(mod.get("path")).name.lower() if mod.get("path") else ""
        
        if keyword_lower == pkg: return 0
        if keyword_lower == nm: return 1
        if keyword_lower == folder: return 2
        if keyword_lower in pkg: return 3
        if keyword_lower in nm: return 4
        if keyword_lower in folder: return 5
        return 6

    results.sort(key=sort_key)

    if output_json:
        print(json.dumps(results, ensure_ascii=False, indent=2))
    return results


def find_mod_path(query: str, fuzzy=False):
    """解析用户输入的是路径还是关键词，返回最佳匹配路径"""
    p = Path(query)
    if p.exists() and p.is_dir() and (p / "About" / "About.xml").exists():
        return str(p)

    # 首先尝试精确查找 packageId
    # rg -i 已经忽略大小写，但由于 XML 标签也可能大小写不同，我们只需匹配值即可
    # 虽然 fixed_strings=True，但 rg -i 能忽略大小写
    exact_matches = execute_rg_search(f"<packageId>{query}</packageId>", fixed_strings=True)
    if exact_matches:
        for fpath in exact_matches:
            about_path = Path(fpath)
            name, pkg_id, deps = parse_about_xml(about_path)
            if pkg_id and pkg_id.lower() == query.lower():
                return str(about_path.parent.parent)

    # 模糊搜索取第一个（已经过排序优化，最匹配的在前面）
    results = search_mods(query, output_json=False, fuzzy=fuzzy)
    if results:
        return results[0]["path"]

    return None


def sanitize_folder_name(name: str):
    """将名字中的非法字符和空格转换为下划线"""
    invalid_chars = '<>:"/\\|?*'
    for char in invalid_chars:
        name = name.replace(char, '')
    return name.replace(' ', '_')


def pull_mod(query: str, version: str, all_content: bool, with_deps: bool, fuzzy: bool = False, pulled_set=None):
    """方法 2：拉取模组到当前目录"""
    if pulled_set is None:
        pulled_set = set()

    # 直接忽略 RimWorld 官方内容
    if query.lower().startswith('ludeon.rimworld.') or query.lower() == 'ludeon.rimworld':
        return

    mod_path_str = find_mod_path(query, fuzzy=fuzzy)
    if not mod_path_str:
        print(f"Error: 找不到匹配 '{query}' 的模组。", file=sys.stderr)
        return

    mod_path = Path(mod_path_str)
    about_file = mod_path / "About" / "About.xml"
    name, package_id, deps = parse_about_xml(about_file)

    if not package_id:
        print(f"Error: 无法解析 {about_file} 中的 packageId。", file=sys.stderr)
        return

    pkg_lower = package_id.lower()
    if pkg_lower in pulled_set:
        return
    pulled_set.add(pkg_lower)

    # 【重大更改】文件夹名称使用清理后的模组名称，而不是 packageId
    clean_name = sanitize_folder_name(name or "Unknown_Mod")
    target_mod_dir = TARGET_DIR / clean_name

    if target_mod_dir.exists():
        print(f"目录已存在，正在覆盖: {target_mod_dir}")
        shutil.rmtree(target_mod_dir)

    print(f"\n[{package_id}] 正在拉取: {name} -> 文件夹: {clean_name}")

    def ignore_func(dir_path, files):
        if all_content:
            return []
        cfg = load_config()
        ignore_list = cfg.get("pull_ignore_folders", [])
        return [f for f in files if f in ignore_list or (os.path.isdir(os.path.join(dir_path, f)) and f in ignore_list)]

    shutil.copytree(mod_path, target_mod_dir, ignore=ignore_func)

    # 反编译逻辑
    cfg = load_config()
    ilspycmd_raw = cfg.get("ilspycmd_path", "")
    ilspycmd_cmd = resolve_ilspy_command(ilspycmd_raw)

    assemblies_dirs = [
        target_mod_dir / version / "Assemblies",
        target_mod_dir / "Assemblies"
    ]

    assembly_dir_to_use = None
    for d in assemblies_dirs:
        if d.exists() and d.is_dir():
            assembly_dir_to_use = d
            break

    if assembly_dir_to_use and ilspycmd_cmd:
        dlls = list(assembly_dir_to_use.glob("*.dll"))
        if dlls:
            source_target_dir = target_mod_dir / "Source"
            for dll in dlls:
                dll_source_dir = source_target_dir / dll.stem
                dll_source_dir.mkdir(parents=True, exist_ok=True)
                print(f"  -> 反编译 {dll.name}...")
                cmd = ilspycmd_cmd + ["-p", "-o", str(dll_source_dir), str(dll)]
                try:
                    subprocess.run(cmd, check=True, capture_output=True)
                except subprocess.CalledProcessError as e:
                    print(f"  -> 反编译失败 {dll.name}: {e}", file=sys.stderr)
                except FileNotFoundError as e:
                    print(f"  -> 反编译工具不可用 {dll.name}: {e}", file=sys.stderr)

    print(f"[{package_id}] 拉取完成 -> {target_mod_dir}")

    # 递归拉取前置
    if with_deps and deps:
        print(f"[{package_id}] 发现 {len(deps)} 个前置模组，开始拉取...")
        for dep in deps:
            dep_pkg = dep["packageId"]
            print(f"  -> 需要前置: {dep_pkg}")
            pull_mod(dep_pkg, version, all_content, with_deps, fuzzy=fuzzy, pulled_set=pulled_set)


def search_deps(query: str, version: str | None = None, fuzzy: bool = False):
    """方法 3：搜索模组的前置模组"""
    mod_path_str = find_mod_path(query, fuzzy=fuzzy)
    if not mod_path_str:
        print(json.dumps({"error": f"找不到匹配 '{query}' 的模组。"}))
        return

    about_file = Path(mod_path_str) / "About" / "About.xml"
    name, package_id, deps = parse_about_xml(about_file, target_version=version)

    result = {
        "target": {
            "name": name,
            "packageId": package_id,
            "path": mod_path_str
        },
        "dependencies": deps
    }
    print(json.dumps(result, ensure_ascii=False, indent=2))


def search_related(query: str, version: str | None = None, fuzzy: bool = False):
    """方法 4：分析模组的关系网络（前置依赖 + 依赖它的拓展模组）"""
    mod_path_str = find_mod_path(query, fuzzy=fuzzy)
    if not mod_path_str:
        print(json.dumps({"error": f"找不到匹配 '{query}' 的模组。"}))
        return

    # 1. 提取目标本体及其前置
    about_file = Path(mod_path_str) / "About" / "About.xml"
    target_name, target_pkg, target_deps = parse_about_xml(about_file, target_version=version)

    if not target_pkg:
        print(json.dumps({"error": f"无法解析 {about_file} 的 packageId。"}))
        return

    # 2. 查找依赖它的拓展模组 (反向依赖)
    # 使用 rg 快速找出所有包含该 packageId 的 About.xml
    matched_files = execute_rg_search(target_pkg, fixed_strings=True)
    dependents = []

    target_pkg_lower = target_pkg.lower()

    for fpath in matched_files:
        f_path_obj = Path(fpath)
        # 跳过目标模组自己
        if f_path_obj.parent.parent == Path(mod_path_str):
            continue

        dep_name, dep_pkg, dep_deps = parse_about_xml(f_path_obj, target_version=version)
        if not dep_pkg:
            continue

        # 检查该模组的前置中是否包含我们的目标模组
        is_dependent = any(d.get("packageId", "").lower() == target_pkg_lower for d in dep_deps)

        if is_dependent:
            dependents.append({
                "name": dep_name,
                "packageId": dep_pkg,
                "path": str(f_path_obj.parent.parent)
            })

    # 3. 输出完整的关系网
    result = {
        "target": {
            "name": target_name,
            "packageId": target_pkg,
            "path": mod_path_str
        },
        "prerequisites": target_deps,  # 它需要的前置
        "dependents": dependents       # 依赖它的拓展
    }

    print(json.dumps(result, ensure_ascii=False, indent=2))


def main():
    parser = argparse.ArgumentParser(description="RimWorld Mod Reference Puller (Fast RG Version)")
    subparsers = parser.add_subparsers(dest="command", required=True, help="子命令")

    # 1. search
    search_parser = subparsers.add_parser("search", help="搜索模组 (使用 rg 极速搜索 About.xml)")
    search_parser.add_argument("keyword", help="关键词 (可以是包名、模组名、作者等)")
    search_parser.add_argument("--fuzzy", action="store_true", help="开启模糊搜索匹配")

    # 2. pull
    pull_parser = subparsers.add_parser("pull", help="拉取模组并自动反编译")
    pull_parser.add_argument("query", help="绝对路径 或 模组名/包名关键词")
    pull_parser.add_argument("--version", required=True, help="【必需】要反编译的目标游戏版本 (例如: 1.5, 1.6)")
    pull_parser.add_argument("--all", action="store_true", help="拉取全部内容，不忽略 Textures/Sounds 等大型文件夹")
    pull_parser.add_argument("--deps", action="store_true", help="自动递归拉取所有前置模组")
    pull_parser.add_argument("--fuzzy", action="store_true", help="开启模糊搜索匹配")

    # 3. deps
    deps_parser = subparsers.add_parser("deps", help="获取指定模组的前置依赖")
    deps_parser.add_argument("query", help="绝对路径 或 模组名/包名关键词")
    deps_parser.add_argument("--version", default=None, help="目标游戏版本，例如 1.6")
    deps_parser.add_argument("--fuzzy", action="store_true", help="开启模糊搜索匹配")

    # 4. related
    related_parser = subparsers.add_parser("related", help="获取模组的关系网（包括前置和依赖它的拓展）")
    related_parser.add_argument("query", help="绝对路径 或 模组名/包名关键词")
    related_parser.add_argument("--version", default=None, help="目标游戏版本，例如 1.6")
    related_parser.add_argument("--fuzzy", action="store_true", help="开启模糊搜索匹配")

    args = parser.parse_args()
    TARGET_DIR.mkdir(parents=True, exist_ok=True)

    if args.command == "search":
        search_mods(args.keyword, fuzzy=args.fuzzy)
    elif args.command == "pull":
        pull_mod(args.query, args.version, args.all, args.deps, fuzzy=args.fuzzy)
    elif args.command == "deps":
        search_deps(args.query, args.version, fuzzy=args.fuzzy)
    elif args.command == "related":
        search_related(args.query, args.version, fuzzy=args.fuzzy)


if __name__ == "__main__":
    main()
