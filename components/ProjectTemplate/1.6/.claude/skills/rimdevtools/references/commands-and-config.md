# RimDevTools 命令与配置参考

## 调用与输出

从项目根目录或其直属子目录运行 `.\.tools\RimDevTools.exe`。项目命令仅在这些层级发现 `mod_config.jsonc`。`help`、`--version` 和 `mods install-tools --mods-path` 不需要项目。

命令以 stdout 上的一个 JSON 对象结束；进度输出到 stderr。默认输出摘要和关键字段；大多数子命令接受 `--verbose` 展开计划、列表、检查项和诊断。警告与错误始终可见。构建、Unity 和 ILSpy 成功时的常规输出默认隐藏，warning/error 行仍会显示；失败时显示最多 12 行末尾诊断。游戏自身 stdout/stderr 始终丢弃。退出码为 `0` 成功、`2` 无效参数/配置、`3` 缺少依赖、`4` 忙、`5` 任务失败、`6` 超时、`7` 游戏/测试失败、`130` 取消。`--version` 必须作为唯一参数使用，仅返回 `ok`、`command` 和 `rimDevToolsVersion`。

## 命令

### `doctor [--verbose]`

检查 About.xml/Preview.png、构建前提条件、Steam/RimWorld/创意工坊发现、项目目录安全性、游戏与 Harmony 程序集、Unity、Git/工作树状态、必需模组、ILSpy、LogSync、AutoTest、打包的工具模组、锁和事务。缺少 About 文件或 Preview.png 不小于 1 MiB 时只给出 warning。项目不能位于 RimWorld `Mods` 或 Steam Workshop 内容目录。它是只读的，不探测 RimSage。默认输出包含可操作检查项和关键程序集路径，默认会自动隐藏满足条件的选项；`--verbose` 显示所有检查结果，包括已经满足选项。

### `deploy [--sync-source] [--xml-hot] [--dry-run] [--verbose]`

构建已配置的 C# 项目，并将 `sync_items` 以文件级事务部署到 `rimworld_dir/Mods/mod_name`。目标目录保持原位，提交前检查变更文件占用；失败时使用事务备份回滚。空目录可能保留。

- `--dry-run`：不构建或部署，仅返回不可变计划。
- `--verbose`：展开完整部署计划，并显示构建和 Unity 输出。默认结果保留目标、同步项和动作计数。
- `--sync-source`：在部署中包含 `source/`；`sync_source: true` 时默认包含。
- `--xml-hot`：先检查 RimWorld 是否正在运行。运行中时将配置同步项中的非 DLL 文件暂存后逐文件事务更新，不构建程序集，并保留已有 DLL 和源目录中不存在的目标文件；若更新了 `Defs/` 或 `Patches/` XML，全部更新完成后至少等待 3 秒，再通过本机 HTTP 请求 LogSyncMod 调用内置 Def 热重载。游戏必须处于 Playing 且启用开发者模式；HTTP endpoint 不可用或游戏拒绝请求时，文件仍已部署，命令会报告 warning。游戏未运行时，此选项自动回退到普通构建和逐文件事务部署。此功能不热更新 DLL。

提交前如果文件被占用，`DEPLOY_TARGET_BUSY` 会报告占用路径以及 Restart Manager 能识别的进程；关闭占用程序后重试。提交阶段若发生共享冲突，工具会先回滚，再报告相同错误。回滚或上次中断事务恢复失败时，使用 `test cleanup --dry-run` 检查保留的事务备份。

常见错误：`PROJECT_DEPLOYMENT_LOCATION_INVALID`、`ENVIRONMENT_BUSY`、`GAME_EXIT_TIMEOUT`、`BUILD_FAILED`、`SHADER_BUILD_FAILED`、`SYNC_ITEM_NOT_FOUND`、`DEPLOY_TARGET_BUSY`、`DEPLOY_RECOVERY_REQUIRED`、`DEPLOY_RECOVERY_FAILED`、`DEPLOY_ROLLBACK_FAILED`、`DEPLOY_FAILED`。

### 参考模组命令

```text
mod search <keyword> [--fuzzy] [--verbose]
mod copy <query> [--version <value>] [--deps] [--include-ignored] [--fuzzy] [--overwrite-existing] [--dry-run] [--verbose]
mod dependencies <query> [--version <value>] [--fuzzy] [--verbose]
mod dependents <query> [--version <value>] [--fuzzy] [--verbose]
```

搜索会扫描游戏 Mods 目录和 `workshop_paths`。默认最多列出 10 个搜索候选、20 个依赖项或 20 个依赖方，并始终给出总数。`--verbose` 展开完整候选及元数据。`mod copy` 接受绝对目录、精确的 package ID/名称/文件夹或搜索匹配项。它以净化后的 About 名称命名目标，复制到 `project_paths.reference_docs`，并使用打包的 ILSpy 反编译程序集。`--deps` 不复制官方内容与 Harmony 共享依赖。

- `--deps`：拉取完整的硬依赖闭包。
- `--include-ignored`：禁用 `pull_ignore_folders`。
- `--overwrite-existing`：替换所有预先检查过的冲突。
- `--dry-run`：只预览，不复制或反编译。默认输出摘要与前 10 个目标；需要完整依赖闭包、忽略目录和程序集清单时添加 `--verbose`。
- `--verbose`：显示完整 dry-run 依赖闭包和 ILSpy 输出。默认最多列出 10 个目标及摘要计数。
- `--fuzzy`：允许模糊候选匹配。
- `--version`：选择特定版本的依赖元数据。

不要隐式添加这些选项。歧义、依赖循环、目标冲突和净化名称碰撞会在复制前失败。常见错误包括 `MOD_NOT_FOUND`、`MOD_AMBIGUOUS`、`DEPENDENCY_CYCLE`、`TARGET_COLLISION`、`TARGET_CONFLICT` 和 `COPY_FAILED`。

### 测试命令

```text
test settings [--verbose]
test run [--deploy] [--sync-source] [--mode quick|manual|auto|xml]
         [--active-mods-file <path>] [--timeout-seconds <n>]
         [--target-package-id <id>] [--capture-screenshots]
         [--scope <value>]... [--defname <value>]... [--batch-size <n>]
         [--isolated-desktop|--no-isolated-desktop] [--verbose]
test cleanup [--dry-run] [--status] [--verbose]
```

`test settings` 是持久化 LogSync 设置命令，不会启动测试。它会等待已有 RimWorld 退出、取得共享环境锁，然后把日志路径设置为当前项目的 `.tools/log/精选日志.log`，把 `mod_config.jsonc.log_keywords` 写入 LogSync，并更新自动测试输出路径、开关和筛选设置。写入不会像 `test run` 一样在结束后自动恢复，适合准备一次由用户手动启动的 RimWorld 会话；正常自动测试应使用 `test run`，由事务临时应用并恢复设置。

quick 是启动冒烟测试。manual 是用户手动的。auto 和 XML 验证结构化的 AutoTest 结果。`--capture-screenshots` 仅适用于 auto/XML。隔离优先级为显式标志、项目设置，然后是模式默认值。`test run` 不接受 `--xml-hot`；`test run --deploy` 会先关闭已有 RimWorld，再在启动测试进程前执行普通构建和逐文件事务部署。

`test run` 默认输出运行结果、测试信号、模组数量和依赖 warning；`--verbose` 附加完整启用模组列表、设置时新增的模组以及部署子进程诊断。

`--scope` 可在 auto/XML 中重复，多个 scope 取并集。正式值为 `standard`、`ui`、`combat`、`view`、`misc`；旧细分值继续以原窄范围语义兼容。未传时默认为 `Standard`；Standard 横跨四类常规测试，但不隐式启用 CombatPawns 或 SkillPocketMaps。

`--defname` 仅适用于 auto，可重复且按不区分大小写去重。scope 选择测试类型，DefName 选择目标，两者取交集；不传 DefName 时枚举所选范围。任何请求的 DefName 未产生用例时，本轮失败并在 JSON/Markdown 中列出未命中名称。`--batch-size` 适用于 auto/xml，默认 40；包括 Standard 在内的枚举运行都限制最终计划数量，候选超限时从全部候选中全局均匀、无放回随机抽取。显式 DefName 运行继续忽略该限制。游戏进程 stdout/stderr 会被安静丢弃，不生成独立进程日志；自动测试不落盘独立 Evidence JSON。

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

`test cleanup --status` 只读报告环境锁、RimWorld 运行状态、遗留事务、部署暂存/备份现场和建议的清理命令；它不取得写入锁，也不修改文件。默认输出数量和动作摘要，`--verbose` 展开锁信息及完整路径。`test cleanup --dry-run` 预览恢复动作，普通 `test cleanup` 才执行恢复。

### `mods install-tools [--mods-path <path>] [--dry-run] [--verbose]`

安装打包的 LogSyncMod。清单必须恰好包含 `trigger.logsyncmod` 组件。安装器会验证 package ID、版本、目录哈希、暂存、备份、回滚和降级规则；升级时仅保留 `PublishedFileId.txt`。

不带 `--mods-path` 时，从项目配置解析 RimWorld Mods 目录。使用 `--dry-run` 在不写入的情况下检查单组件计划；`--verbose` 查看组件源路径和内容哈希。

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
