# RimDevTools 命令与配置参考

## 工具更新

正式解压安装支持 `update --check`、`update --dry-run`、`update` 和 `update status`。普通业务命令启动检查使用 24 小时缓存，联网总预算最多 2 秒；失败退避 1 小时，不影响业务结果。帮助、版本、状态查询及内部助手不联网检查。

用户已要求更新时，先检查和预览，再执行 `update`，随后查询 `update status` 直到终态。`scheduled` 只说明助手接收了更新，不能报告已经成功；成功状态为 `succeeded`。不自动重跑原业务命令。首次获得更新能力需手动安装一次新版，源码仓库及缺少可信发行清单的旧安装不自动更新。

`UPDATE_CONFLICT` 会列出本地受管理文件冲突，整次更新停止，保留本地修改。配置仅补充缺失字段，保留注释、未知字段、模式和游戏目标；不覆盖项目源码、资源、`.gitignore`、其他 skill 或工坊身份。`TOOL_UPDATE_BUSY` 表示安装仍在使用，`TOOL_UPDATE_RECOVERY_REQUIRED` 表示更新未完成或需要恢复。使用 `update status` 返回的原始状态、事务路径和 `recoveryCommand`；恢复前让已有更新进程退出。即使主 EXE 无法运行，保留的助手仍可恢复。未知并发修改会阻止恢复并保留备份，先处理对应冲突再重试。

完成后重新读取磁盘上的完整 `rimdevtools` skill。项目私有的工具 Mod 载荷更新不等于游戏中已经安装或启用新版，游戏内组件仍由 `mods install-tools` 管理。缓存、暂存和回滚数据由工具维护；保留最近三份已完成事务，不删除未完成恢复所需的数据。

## 旧项目模式

在现有 `mod_config.jsonc` 中设置 `existing_project_mode: true`，继续使用原来的 `csproj_paths`、`sync_items` 和 `game_version`。缺失或 `false` 沿用模板模式。旧项目模式只放宽模板名称、程序集名和日志关键词等专属门禁；必要元数据、构建、路径安全及测试依赖仍按对应命令校验。无需首次接入命令，不会自动识别工程、生成配置或修改 `.gitignore`。简化 Tools 包仅提供工具及配置参考，已有配置应保留。

## 调用与输出

从项目根目录或其直属子目录运行 `.\.tools\RimDevTools.exe`。项目命令仅在这些层级发现 `mod_config.jsonc`。`help`、`--version` 和 `mods install-tools --mods-path` 不需要项目。

命令以 stdout 上的一个 JSON 对象结束；进度输出到 stderr。退出码为 `0` 成功、`2` 无效参数/配置、`3` 缺少依赖、`4` 忙、`5` 任务失败、`6` 超时、`7` 游戏/测试失败、`130` 取消。`--version` 必须作为唯一参数使用，返回 `rimDevToolsVersion`、`templateGameVersion` 和 `templateSchemaVersion`；未打包时模板版本分别显示 `unpackaged` 和 `0`。

测试产物路径：新版 `test run` 和 `test settings` 在分配本轮目录后，成功结果的 `result.artifacts`、失败结果的 `error.details.artifacts` 默认包含 `directoryPath`、`syncLogPath`、`autoTestOutputPath` 三个绝对路径，不需要 `--verbose`。quick/auto `test run` 还会返回 `result.logSummary` 或 `error.details.logSummary`，其中 `errorCount`、`warningCount` 统计本轮精选日志记录；`available`、`countComplete` 和 `truncated` 说明读取状态。计数受 LogSync 的模组/关键词筛选和行数上限影响。目录分配前的参数、环境或部署失败没有本轮产物，不应创建占位目录。路径表示输出位置，quick/manual 等模式可能没有 AutoTest 文件，应检查文件是否实际生成。旧版没有这些字段时读取已有 LogSync 设置和原始产物定位，不创建替代路径。

除用户明确要求导出外，直接读取 stdout 和原始产物并在聊天中报告；禁止自建 evidence、任务或日期目录，禁止重定向、另存、复制日志/截图/存档或编写归档包装脚本。保留与清理由工具负责。

## 命令

### `doctor [--verbose]`

检查所选游戏版本对应的路径和构建目标框架、About.xml/Preview.png、Steam/RimWorld/创意工坊发现、项目目录安全性、游戏与 Harmony 程序集、Unity Shader Bundle 版本、Git/工作树状态、ILSpy、可选的 LogSync/AutoTest 测试宿主、锁和事务。测试宿主与 `min_active_mods` 是可选测试前提，不是普通模组工程的必需组件；AutoTest 用例由 AutoTestRunner 代码生成，无需 Spec XML。缺少 About 文件或 Preview.png 不小于 1 MiB 时只给出 warning。项目不能位于 RimWorld `Mods` 或 Steam Workshop 内容目录。它是只读的，不探测 RimSage。默认输出包含可操作检查项和关键程序集路径，默认会自动隐藏满足条件的选项；`--verbose` 显示所有检查结果，包括已经满足选项。

### `deploy [--sync-source] [--dry-run]`

构建已配置的 C# 项目，并将 `sync_items` 原子部署到 `rimworld_dir/Mods/mod_name`。

- `--dry-run`：不构建或部署，仅返回不可变计划。
- `--sync-source`：在部署中包含 `source/`；`sync_source: true` 时默认包含。

常见错误：`PROJECT_DEPLOYMENT_LOCATION_INVALID`、`ENVIRONMENT_BUSY`、`GAME_EXIT_TIMEOUT`、`BUILD_FAILED`、`SHADER_BUILD_FAILED`、`SYNC_ITEM_NOT_FOUND`、`DEPLOY_TARGET_BUSY`、`DEPLOY_MANIFEST_REPLACE_FAILED`、`DEPLOY_MANIFEST_WRITE_FAILED`、`DEPLOY_RECOVERY_REQUIRED`、`DEPLOY_RECOVERY_FAILED`、`DEPLOY_ROLLBACK_FAILED`、`DEPLOY_FAILED`。

成功部署的 `result.fileCommit.manifestWriteRetries` 表示事务清单更新遇到短暂占用后的重试次数。

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
test run [--deploy] [--sync-source] [--mode quick|manual|auto|groupcombat|arena]
         [--active-mods-file <path>] [--timeout-seconds <n>]
         [--target-package-id <id>]
         [--scope <value>]... [--defname <value>]... [--batch-size <n>]
         [--scenario-path <path>] [--validate-only] [--scenario-only] [--test-name <name>]...
         [--isolated-desktop|--no-isolated-desktop]
```

`test settings` 是持久化 LogSync 设置命令，不会启动测试。它会等待已有 RimWorld 退出、取得共享环境锁，然后把日志路径设置为当前项目本轮目录中的精选日志（以返回的 `artifacts.syncLogPath` 为准），把 `mod_config.jsonc.log_keywords` 写入 LogSync，并更新自动测试输出路径、开关和筛选设置。写入不会像 `test run` 一样在结束后自动恢复，适合准备一次由用户手动启动的 RimWorld 会话；正常自动测试应使用 `test run`，由事务临时应用并恢复设置。

quick 是启动冒烟测试。manual 是用户手动的。auto 验证结构化的 AutoTest 结果并默认截图。隔离优先级为显式标志、项目设置，然后是模式默认值。

外部 XML 场景仅支持 game_version=1.7 的 auto/groupcombat/arena。auto 用 TestSuite，可加 `--scenario-only` 仅运行外部回归；groupcombat 用 TestSuite 中的 kind=groupCombat，arena 用独立 ArenaSuite，两者必须同时给 `--scenario-path` 和显式 `--test-name`，禁止 `--scenario-only`。擂台按稳定 testDefName 选择完整 Tournament；format 默认为 knockout，另支持 bossRoundRobin 与 limitChallenge。新赛制要求 boss/challenger 角色，极限挑战按人数（优先）或 CP 预算做加法/乘法增长。CLI 超时涵盖全部配对、增长档位、重赛和展示，省略时工具估算默认预算。三赛制先显示至少现实 5 秒的规则页，准备耗时计入其中；新赛制巡礼按角色分页，成绩核对 Challenges/Summary，而非冠军。新赛制平局重赛耗尽只结束当前组，完整赛程仍可正常结束；运行错误或现实超时不能记作战败。群战/擂台使用独立结果文件和验证器，胜负、测量质量与执行状态分开判断，不并入普通回归通过率。XML 语法、AI、地图、混编配兵及评分见 [rimworld-xml-test](../../rimworld-xml-test/SKILL.md)。

外部 XML 运行会先复用实际执行加载器做静态预校验，失败在部署、游戏配置修改和启动前终止，并在 `error.details.validation.diagnostics` 中给出错误、可用的文件/行号/节点和修改建议。可在相同的 mode/scenario-path/test-name 命令上加 `--validate-only` 独立校验：成功返回 `result.validation`（含选中规则与数量、warning 和 `runtimeChecks`），不创建运行产物。只校验需要项目 config_version=2、game_version=1.7，不要求游戏、Steam、ModsConfig 或测试宿主；不接受 deploy、timeout、batch-size 等执行参数。编译器按首条错误停止，修正后重试。静态通过不代表 Def、反射签名、定制 AI、地图/部队生成或战斗断言已通过游戏验证。

外部回归在游戏中断言失败或等待超时时，CLI 的 `error.details.details.scenarioFailures[].diagnostics` 会携带具体条件的 `file`、可用的 `line`、`element`、`stepId`、`xml` 与 `suggestion`；原始结果保存在 `results[].scenario.Diagnostics`。此信息在 AUTO_EVIDENCE_FAILED 与 AUTO_RESULT_FAILED 两种失败中都会保留，不要只用 Case 的 Source 定位。默认预览至多 20 个带诊断的用例，`scenarioFailuresTruncated` 标记截断；完整内容使用 `--verbose` 或读取工具原始结果。

所有 `test run` 模式都会在启动前将 `ModsConfig.xml` 的 `<version>` 写为已校验的 `game_version`，防止切换版本时游戏重置启用列表。版本号先于事务备份更新；测试结束、失败、取消或中断恢复后保留目标版本号，原模组列表与设置照常恢复。

`--scope` 可在 auto 中重复，多个 scope 取并集。正式值为 `standard`、`ui`、`combat`、`view`、`misc`；未传时默认为 `Standard`；Standard 横跨四类常规测试，但不隐式启用 CombatPawns 或 SkillPocketMaps。

`--defname` 仅适用于 auto，可重复且按不区分大小写去重。scope 选择测试类型，DefName 选择目标，两者取交集；不传 DefName 时枚举所选范围。任何请求的 DefName 未产生用例时，本轮失败并在 JSON/Markdown 中列出未命中名称。`--batch-size` 适用于 auto，默认 40；包括 Standard 在内的枚举运行都限制最终计划数量，候选超限时从全部候选中全局均匀、无放回随机抽取。显式 DefName 运行继续忽略该限制。新版游戏进程 stdout/stderr 始终丢弃，不生成 `RimWorldProcess.log`；不要另存进程输出。

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
- `game_version`：目标版本，必须明确为 `1.6` 或 `1.7`。
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
- `isolated_desktop`：quick/auto 的默认隔离。
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

## Steam 创意工坊

```powershell
.\.tools\RimDevTools.exe steamworkshops status
.\.tools\RimDevTools.exe steamworkshops search "Harmony"
.\.tools\RimDevTools.exe steamworkshops search next
.\.tools\RimDevTools.exe steamworkshops search "Harmony" --details --page 5
.\.tools\RimDevTools.exe deploy
.\.tools\RimDevTools.exe steamworkshops upload --dry-run
.\.tools\RimDevTools.exe steamworkshops upload
.\.tools\RimDevTools.exe deploy --upload
.\.tools\RimDevTools.exe steamworkshops edit
.\.tools\RimDevTools.exe steamworkshops edit --visibility private
.\.tools\RimDevTools.exe steamworkshops subscribe <精确ID或工坊链接> --wait
.\.tools\RimDevTools.exe steamworkshops status <精确ID或工坊链接> --wait
```

- 五个子命令为 upload、edit、search、subscribe、status。当前身份优先使用项目 About/PublishedFileId.txt；它保存数字工坊 ID，packageId 另读 About.xml。所有写入只对已获用户授权的具体条目执行；已有会话授权可以沿用。
- upload 只使用成功普通 deploy 的目录，逐项复核新增/缺失/修改文件及未部署项目输入；不自动构建，不提供 --create 或哈希绕过。首次自动新建 private，更新保留远端人工元数据。deploy --upload 在同一锁下串联两阶段；--dry-run 只显示计划，与 --xml-hot 互斥。
- 1.7 普通部署调用本机官方库生成加载目录内的 Assets.zip，默认保留源资源；1.6 跳过烘焙。正常部署、烘焙和发布不启动游戏。官方库不随工具分发。
- edit 不上传内容：无字段参数时同步项目 About 简介；--description-file、--preview-file、--visibility 只改指定字段。可见性为 private/public/friends-only/unlisted；本版仅放行已核验 owner，贡献者检测延期。
- 简易搜索只显示名称、ID、订阅人数，每页 25 条，最多 3 页；--details 额外返回官方 255 字节摘要，每页 10 条，最多 5 页。--page 从 1 开始；基于官方 50 条页依序切片，超限要求更准确的关键词。精确 ID/链接和 status 返回完整描述。
- search next 沿用本项目最近成功关键词、模式和页码，不与 --page/--details 混用。缓存从首次搜索起有效 10 分钟；缓存命中标记 source=cache 和原查询时间，不能作为当前在线、权限或发布成功证据。无项目时单次搜索可用，next 需进入项目或 --project。
- subscribe 只订阅指定 ID，并按需请求下载；--wait 核对匹配 App ID/条目的完成回调和最新安装状态。status --wait 只观察已有下载，不订阅、不请求下载。不启用/排序模组、不递归依赖、不自动退订。Installed 与 NeedsUpdate 并存时不能判定完成；进度不可得为 null。
- 收据 deployment-receipt.json、分页 steamworkshops-search-cache.json、绑定及新增运行暂存文件统一在项目 .tools/ProjectData，必须忽略于 Git，并排除部署和发行。不能把清理缓存变成删除收据或绑定。部署/上传的 artifacts 返回实际收据路径；测试日志仍按 test run 的 artifacts 对象定位，不另存 stdout 或复制证据。
- 发布超时返回非零、timedOut=true、remoteOutcome=unknown、当前时间、目标 Steam 最近更新时间（或 null 与原因）、operationId 和提交阶段。超时不等于远端取消；不自动重试，不保存跨调用超时阻塞，不使用 --recover。由 AI 根据证据决定是否再次显式提交；已知 ID 保存失败和真实绑定冲突仍须修复。
- doctor 每次在 stderr 显示 Steam 运行、RimWorld 库可用性、Workshop 在线摘要，stdout 仍只有最终 JSON。Steam 时间来自条目 m_rtimeUpdated，可能不随元数据编辑改变；不提供历史更新次数。
