# RimDevTools 命令与配置参考

## 调用与输出

从项目根目录或其直属子目录运行 `.\.tools\RimDevTools.exe`。项目命令仅在这些层级发现 `mod_config.jsonc`。`help`、`--version` 和 `mods install-tools --mods-path` 不需要项目。

命令以 stdout 上的一个 JSON 对象结束；进度输出到 stderr。退出码为 `0` 成功、`2` 无效参数/配置、`3` 缺少依赖、`4` 忙、`5` 任务失败、`6` 超时、`7` 游戏/测试失败、`130` 取消。`--version` 必须作为唯一参数使用，仅返回 `ok`、`command` 和 `rimDevToolsVersion`。

## 命令

### `doctor [--verbose]`

检查所选游戏版本对应的路径和构建目标框架、About.xml/Preview.png、Steam/RimWorld/创意工坊发现、项目目录安全性、游戏与 Harmony 程序集、Unity Shader Bundle 版本、Git/工作树状态、ILSpy、可选的 LogSync/AutoTest 测试宿主、锁和事务。测试宿主与 `min_active_mods` 是可选测试前提，不是普通模组工程的必需组件；AutoTest XML 仅在显式 XML 测试时使用。缺少 About 文件或 Preview.png 不小于 1 MiB 时只给出 warning。项目不能位于 RimWorld `Mods` 或 Steam Workshop 内容目录。它是只读的，不探测 RimSage。默认输出包含可操作检查项和关键程序集路径，默认会自动隐藏满足条件的选项；`--verbose` 显示所有检查结果，包括已经满足选项。

### `deploy [--sync-source] [--dry-run]`

构建已配置的 C# 项目，并将 `sync_items` 原子部署到 `rimworld_dir/Mods/mod_name`。

- `--dry-run`：不构建或部署，仅返回不可变计划。
- `--sync-source`：在部署中包含 `source/`；`sync_source: true` 时默认包含。

常见错误：`PROJECT_DEPLOYMENT_LOCATION_INVALID`、`ENVIRONMENT_BUSY`、`GAME_EXIT_TIMEOUT`、`BUILD_FAILED`、`SHADER_BUILD_FAILED`、`SYNC_ITEM_NOT_FOUND`、`DEPLOY_FAILED`。

### 参考模组命令

```text
mod search <keyword> [--fuzzy]
mod copy <query> [--version <value>] [--deps] [--include-ignored] [--fuzzy] [--overwrite-existing] [--dry-run]
mod dependencies <query> [--version <value>] [--fuzzy]
mod dependents <query> [--version <value>] [--fuzzy]
```

搜索会扫描游戏 Mods 目录和 `workshop_paths`。`mod copy` 接受绝对目录、精确的 package ID/名称/文件夹或搜索匹配项。它以净化后的 About 名称命名目标，复制到 `project_paths.reference_docs`，并使用打包的 ILSpy 反编译程序集。`--deps` 不复制官方内容与 Harmony 共享依赖。

- `--deps`：拉取完整的硬依赖闭包。
- `--include-ignored`：禁用 `pull_ignore_folders`。
- `--overwrite-existing`：替换所有预先检查过的冲突。
- `--dry-run`：只输出解析目标、依赖闭包、忽略目录、目标冲突和待反编译程序集，不复制或反编译。
- `--fuzzy`：允许模糊候选匹配。
- `--version`：选择特定版本的依赖元数据。

不要隐式添加这些选项。歧义、依赖循环、目标冲突和净化名称碰撞会在复制前失败。常见错误包括 `MOD_NOT_FOUND`、`MOD_AMBIGUOUS`、`DEPENDENCY_CYCLE`、`TARGET_COLLISION`、`TARGET_CONFLICT` 和 `COPY_FAILED`。

### 测试命令

```text
test settings
test run [--deploy] [--sync-source] [--mode quick|manual|auto|xml]
         [--active-mods-file <path>] [--timeout-seconds <n>]
         [--target-package-id <id>]
         [--scope <value>]... [--defname <value>]... [--batch-size <n>]
         [--isolated-desktop|--no-isolated-desktop]
```

`test settings` 是持久化 LogSync 设置命令，不会启动测试。它会等待已有 RimWorld 退出、取得共享环境锁，然后把日志路径设置为当前项目的 `.tools/log/精选日志.log`，把 `mod_config.jsonc.log_keywords` 写入 LogSync，并更新自动测试输出路径、开关和筛选设置。写入不会像 `test run` 一样在结束后自动恢复，适合准备一次由用户手动启动的 RimWorld 会话；正常自动测试应使用 `test run`，由事务临时应用并恢复设置。

quick 是启动冒烟测试。manual 是用户手动的。auto 和 XML 验证结构化的 AutoTest 结果。auto 模式默认截图；XML 驱动测试按每条测试定义的 `captureScreenshots` 设置截图，不需要截图命令行开关。隔离优先级为显式标志、项目设置，然后是模式默认值。

`--scope` 可在 auto/XML 中重复，多个 scope 取并集。正式值为 `standard`、`ui`、`combat`、`view`、`misc`；旧细分值继续以原窄范围语义兼容。未传时默认为 `Standard`；Standard 横跨四类常规测试，但不隐式启用 CombatPawns 或 SkillPocketMaps。

`--defname` 仅适用于 auto，可重复且按不区分大小写去重。scope 选择测试类型，DefName 选择目标，两者取交集；不传 DefName 时枚举所选范围。任何请求的 DefName 未产生用例时，本轮失败并在 JSON/Markdown 中列出未命中名称。`--batch-size` 适用于 auto/xml，默认 40；包括 Standard 在内的枚举运行都限制最终计划数量，候选超限时从全部候选中全局均匀、无放回随机抽取。显式 DefName 运行继续忽略该限制。游戏进程 stdout/stderr 写入 `.tools/log/RimWorldProcess.log`。

例 1，只测试三视图：

```powershell
.\.tools\RimDevTools.exe test run --mode auto --target-package-id aoba.deadmanswitch.core --scope threeviews
```

例 2，同时精确测试近战和远程武器：

```powershell
.\.tools\RimDevTools.exe test run --mode auto --target-package-id aoba.deadmanswitch.core `
  --scope meleeweapons --scope rangedweapons `
  --defname DMS_StunBaton --defname DMS_TacticalPistol
```

启用模组缺少必要上级依赖时，命令会在 `warnings` 中指出父模组和缺失依赖，但不会阻挡执行。`doctor` 使用 `mods.min_active_dependencies` 对 `min_active_mods` 做相同检查。

`test cleanup --status` 只读报告环境锁、RimWorld 运行状态、遗留事务、部署暂存/备份现场和建议的清理命令；它不取得写入锁，也不修改文件。`test cleanup --dry-run` 预览恢复动作，普通 `test cleanup` 才执行恢复。

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
- `sync_source`：为 `true` 时，部署默认附带 `source/`；默认 `false`。
- `protected_files_regex`：替换时保留的已部署相对路径。
- `pull_ignore_folders`：除非使用 `--include-ignored`，否则参考复制省略的文件夹。
- `project_paths.reference_docs`：第三方模组复制和证据目录，默认 `参考文档`。
- `project_paths.planning_docs`：项目计划文档目录，默认 `策划案`。
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

## 推荐序列

- 设置：`doctor` -> 修复前提条件 -> `doctor`。
- 部署：`doctor` -> `deploy --dry-run` -> 检查 -> `deploy`。
- 冒烟：`test run --deploy --mode quick`。
- 自动化：`test run --deploy --mode auto --target-package-id <id>`。
- 第三方证据：`mod search` -> `mod copy --dry-run` -> 精确 `mod copy` -> 检查本地 XML 和 ILSpy 源码。
