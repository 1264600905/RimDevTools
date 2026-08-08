# RimDevTools 命令与配置参考

## 调用与输出

从项目根目录或其直属子目录运行 `.\.tools\RimDevTools.exe`。项目命令仅在这些层级发现 `mod_config.jsonc`。`help`、`--version` 和 `mods install-tools --mods-path` 不需要项目。

命令以 stdout 上的一个 JSON 对象结束；进度输出到 stderr。退出码为 `0` 成功、`2` 无效参数/配置、`3` 缺少依赖、`4` 忙、`5` 任务失败、`6` 超时、`7` 游戏/测试失败、`130` 取消。`--version` 仅返回 `ok`、`command` 和 `rimDevToolsVersion`。

## 命令

### `doctor [--verbose]`

检查构建前提条件、Steam/RimWorld/创意工坊发现、游戏与 Harmony 程序集、Unity、Git/工作树状态、必需模组、ILSpy、LogSync、AutoTest、打包的工具模组、锁和事务。它是只读的，不探测 RimSage。默认输出包含可操作检查项和关键程序集路径，默认会自动隐藏满足条件的选项；`--verbose` 显示所有检查结果，包括已经满足选项。

### `deploy [--sync-source] [--dry-run]`

构建已配置的 C# 项目，并将 `sync_items` 原子部署到 `rimworld_dir/Mods/mod_name`。

- `--dry-run`：不构建或部署，仅返回不可变计划。
- `--sync-source`：在部署中包含 `source/`。

常见错误：`ENVIRONMENT_BUSY`、`GAME_EXIT_TIMEOUT`、`BUILD_FAILED`、`SHADER_BUILD_FAILED`、`SYNC_ITEM_NOT_FOUND`、`DEPLOY_FAILED`。

### 参考命令

```text
ref search <keyword> [--fuzzy]
ref pull <query> [--version <value>] [--deps] [--all] [--fuzzy] [--force]
ref deps <query> [--version <value>] [--fuzzy]
ref related <query> [--version <value>] [--fuzzy]
```

搜索会扫描游戏 Mods 目录和 `workshop_paths`。`ref pull` 接受绝对目录、精确的 package ID/名称/文件夹或搜索匹配项。它以净化后的 About 名称命名目标，复制到 `参考文档`，并使用打包的 ILSpy 反编译程序集。

- `--deps`：拉取完整的硬依赖闭包。
- `--all`：禁用 `pull_ignore_folders`。
- `--force`：替换所有预先检查过的冲突。
- `--fuzzy`：允许模糊候选匹配。
- `--version`：选择特定版本的依赖元数据。

不要隐式添加这些选项。歧义、依赖循环、目标冲突和净化名称碰撞会在复制前失败。常见错误包括 `MOD_NOT_FOUND`、`MOD_AMBIGUOUS`、`DEPENDENCY_CYCLE`、`TARGET_COLLISION`、`TARGET_CONFLICT` 和 `PULL_FAILED`。

### 测试命令

```text
test settings
test run [--deploy] [--sync-source] [--mode quick|manual|auto|xml]
         [--mods-file <path>] [--timeout-seconds <n>]
         [--target-package-id <id>] [--capture-screenshots]
         [--isolated-desktop|--no-isolated-desktop]
```

`test settings` 持久化写入 LogSync 输出和关键字设置,但是一般不建议使用。`test run` 拥有一个事务，并在成功、失败、超时或取消后仍可恢复时恢复全局配置。

quick 是启动冒烟测试。manual 是用户手动的。auto 和 XML 验证结构化的 AutoTest 结果。`--capture-screenshots` 仅适用于 auto/XML。隔离优先级为显式标志、项目设置，然后是模式默认值。

### `mods install-tools [--mods-path <path>] [--dry-run]`

安装打包的 LogSyncMod。清单必须恰好包含 `trigger.logsyncmod` 组件。安装器会验证 package ID、版本、目录哈希、暂存、备份、回滚和降级规则；升级时仅保留 `PublishedFileId.txt`。

不带 `--mods-path` 时，从项目配置解析 RimWorld Mods 目录。使用 `--dry-run` 在不写入的情况下检查单组件计划。

## 配置字段

- `config_version`：必须为 `2`。
- `mod_name`：部署的文件夹名称。
- `game_version`：目标版本，当前为 `1.6`。
- `rimworld_dir`：RimWorld 安装根目录。
- `workshop_paths`：参考命令搜索的创意工坊根目录。
- `csproj_paths`：由部署构建的 C# 项目。
- `sync_items`：部署期间复制的项目文件/目录。
- `protected_files_regex`：替换时保留的已部署相对路径。
- `pull_ignore_folders`：除非使用 `--all`，否则参考拉取省略的文件夹。
- `ilspycmd_path`：相对 `.tools` 的路径，通常为 `Ilspy\ilspycmd.exe`。
- `log_keywords`：应用于 LogSync 设置的字符串。
- `min_active_mods`：始终包含在临时测试模组列表中的 package ID。
- `isolated_desktop`：quick/auto/XML 的默认隔离。
- `timeouts.steam_launch_detect`：Steam/游戏出现超时。
- `timeouts.max_wait`：默认测试完成超时。
- `timeouts.existing_game_exit`：等待现有游戏进程退出。
- `timeouts.post_exit_delay`：退出后的稳定延迟。
- `retention.max_log_files`：精选日志的存档限制。
- `retention.max_auto_test_runs`：自动测试运行的保留数。
- `shader_bundle.*`：可选的 Unity 批处理着色器构建设置。

## 推荐序列

- 设置：`doctor` -> 修复前提条件 -> `doctor`。
- 部署：`doctor` -> `deploy --dry-run` -> 检查 -> `deploy`。
- 冒烟：`test run --deploy --mode quick`。
- 自动化：`test run --deploy --mode auto --target-package-id <id>`。
- 第三方证据：`ref search` -> 精确 `ref pull` -> 检查本地 XML 和 ILSpy 源码。
