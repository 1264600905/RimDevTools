---
name: rimdevtools
description: 操作项目本地的 RimDevTools CLI，处理 RimWorld 模组配置、健康检查、构建、事务化逐文件部署和非 DLL 文件热同步、参考模组发现与 ILSpy 反编译、由 LogSync 支持的 quick/manual/auto 测试，Steam 创意工坊查询/发布/订阅及官方资源包，以及打包的 LogSyncMod 安装。处理 mod_config.jsonc、.tools/RimDevTools.exe、部署、测试、参考模组或工具模组安装时使用。
---

# RimDevTools 工具

在模组根目录使用项目本地 Windows CLI：

```powershell
.\.tools\RimDevTools.exe <command> [options]
```

将 `mod_config.jsonc` 视为项目契约。应在其所在目录或直属子目录运行命令。修改配置，或使用参数、变更行为或错误契约重要的命令前，阅读 [references/commands-and-config.md](references/commands-and-config.md)。

## 工具更新

正式解压安装支持 `update --check`、`update --dry-run`、`update` 和 `update status`。普通业务命令启动检查使用 24 小时缓存，联网总预算最多 2 秒；失败退避 1 小时，不影响业务结果。帮助、版本、状态查询及内部助手不联网检查。

用户已要求更新时，先检查和预览，再执行 `update`，随后查询 `update status` 直到终态。`scheduled` 只说明助手接收了更新，不能报告已经成功；成功状态为 `succeeded`。不自动重跑原业务命令。首次获得更新能力需手动安装一次新版，源码仓库及缺少可信发行清单的旧安装不自动更新。

`UPDATE_CONFLICT` 会列出本地受管理文件冲突，整次更新停止，保留本地修改。配置仅补充缺失字段，保留注释、未知字段、模式和游戏目标；不覆盖项目源码、资源、`.gitignore`、其他 skill 或工坊身份。`TOOL_UPDATE_BUSY` 表示安装仍在使用，`TOOL_UPDATE_RECOVERY_REQUIRED` 表示更新未完成或需要恢复。使用 `update status` 返回的原始状态、事务路径和 `recoveryCommand`；恢复前让已有更新进程退出。即使主 EXE 无法运行，保留的助手仍可恢复。未知并发修改会阻止恢复并保留备份，先处理对应冲突再重试。

完成后重新读取磁盘上的完整 `rimdevtools` skill。项目私有的工具 Mod 载荷更新不等于游戏中已经安装或启用新版，游戏内组件仍由 `mods install-tools` 管理。缓存、暂存和回滚数据由工具维护；保留最近三份已完成事务，不删除未完成恢复所需的数据。

## 旧项目模式

在现有 `mod_config.jsonc` 中设置 `existing_project_mode: true`，继续使用原来的 `csproj_paths`、`sync_items` 和 `game_version`。缺失或 `false` 沿用模板模式。旧项目模式只放宽模板名称、程序集名和日志关键词等专属门禁；必要元数据、构建、路径安全及测试依赖仍按对应命令校验。无需首次接入命令，不会自动识别工程、生成配置或修改 `.gitignore`。简化 Tools 包仅提供工具及配置参考，已有配置应保留。

## 测试产物与归档

- 日志、结果、截图和测试存档只读取工具生成的原始产物。除用户明确要求导出外，禁止自建 evidence、任务或日期归档目录，包括 `.tools` 和 `.tools/log` 下的额外目录，也不得换到其他目录绕过。
- 禁止通过 `>`、`2>`、`Tee-Object`、`Out-File`、`Set-Content` 或文件复制另存工具 stdout/stderr 和测试产物；禁止为归档编写一次性包装脚本。
- 新版工具以成功 JSON 的 `result.artifacts` 或失败 JSON 的 `error.details.artifacts` 定位本轮产物；test run 的产物对象字段为 `directoryPath`、`syncLogPath`、`autoTestOutputPath`；部署/工坊发布的 artifacts 为实际收据路径数组。quick/auto `test run` 还会在成功结果的 `result.logSummary` 或失败详情的 `error.details.logSummary` 中报告 `errorCount`、`warningCount`、`available`、`countComplete` 和 `truncated`，无需 `--verbose`。这些数量统计本轮精选日志的记录，受模组/关键词筛选和行数上限影响。旧版没有这些字段时读取已有 LogSync 设置和产物定位，不创建替代路径或补造文件。
- 直接在聊天中报告结论并引用原始路径。除用户明确要求外，不另写测试报告或校验文档；保留与清理由工具负责，不修改原始证据。历史测试产物不构成启动新测试的授权。

## 选择工作流

1. 遇到环境失败、状态不清或能力疑问，先用只读的 `doctor`。
2. 用 `deploy --dry-run` 检查构建和同步计划。仅当用户要求部署或工作流明确需要时，才运行会修改内容的 `deploy`。
3. 用 `mod search`、`mod dependencies` 或 `mod dependents` 只读发现参考模组。仅在任务范围包含复制/反编译参考模组时使用 `mod copy`。
4. 用 `test run --mode quick` 验证启动和运行就绪；用 `manual` 进行用户观察的游玩；对 LogSync AutoTest 用 `auto`。
5. 用 `mods install-tools --dry-run` 检查打包的 LogSyncMod 安装计划；仅在任务范围包含安装时才去掉 `--dry-run`。
6. 仅在需要持久化 LogSync 设置时使用 `test settings`。正常测试会以事务方式应用并还原设置。

## 输出与详细模式

RimDevTools 默认只返回摘要和关键字段；所有命令仍将最终结果写为一个 JSON 对象。多数子命令支持 `--verbose`，用于查看完整计划、候选列表、模组清单和子进程诊断。日常调用不要默认附加 `--verbose`；只有摘要不足以判断结果或正在排查问题时再使用。警告和错误始终可见。构建/Unity/ILSpy 成功时的常规输出默认隐藏；失败时会显示有限的末尾诊断。游戏进程自身 stdout/stderr 始终丢弃。

## 诊断

运行：

```powershell
.\.tools\RimDevTools.exe doctor          （一般使用这个，已经满足的验证项目不会显示）
.\.tools\RimDevTools.exe doctor --verbose   （只有当doctor命令解决仍存在问题时使用这个，会显示大量的验证项目状态）
.\.tools\RimDevTools.exe --version
```

读取 stdout 上唯一的 JSON 对象。默认的 `checks` 数组包含可操作问题及已解析的 `Assembly-CSharp.dll` 和 `0Harmony.dll` 绝对路径；用 `ready`、`issueCount` 和 `warningCount` 总结，其他成功/已禁用/不适用检查则使用 `--verbose` 查看。对于 1.7+，.NET Framework targeting pack 检查默认隐藏，verbose 才会显示 `not_applicable`；不要建议为 1.7+ 安装 4.8 或 4.7.2 targeting pack。修复范围最小的失败前提条件后重新运行 `doctor`。它不会构建，也不会检查启动工具所需的 .NET 10 Runtime。

## 部署

事务清单更新失败时，`DEPLOY_MANIFEST_REPLACE_FAILED` / `DEPLOY_MANIFEST_WRITE_FAILED` 会报告清单路径、尝试状态、磁盘现存状态和原生错误码；1175 是清单替换失败的专用诊断码。
成功部署结果中的 `result.fileCommit.manifestWriteRetries` 是事务清单因短暂占用而重试的次数。

构建模组项目时禁止生成 `.pdb` 和 `.deps.json`。执行 `deploy` 或 `test run --deploy` 前，确认模组 `.csproj` 在所有构建配置中显式设置 `DebugSymbols=false`、`DebugType=none`、`GenerateDependencyFile=false`；构建后检查当前目标的 `Assemblies/`，不得同步或打包这两类文件。

1. 根据doctor的路径探测，尝试修复 `mod_config.jsonc` 中的 游戏路径，工坊路径，完成后重新执行 `doctor` 验证,检查状态是否更新。
2. 填写正确的项目 `mod_name`、`rimworld_dir`、`csproj_paths` 和 `sync_items`。
3. 运行 `deploy --dry-run` 检查部署摘要；需要核对所有目标路径和替换动作时添加 `--verbose`。
4. 仅在计划正确后运行 `deploy`。
5. 仅当有意分发源代码时启用 `sync_source: true`，或对单次部署添加 `--sync-source`。

`deploy --xml-hot` 会先检查 RimWorld 是否正在运行：运行中时将配置项中的非 DLL 文件暂存后逐文件事务更新，若更新了 `Defs/` 或 `Patches/` XML，则至少等待 3 秒，再向 LogSyncMod 的本机 HTTP endpoint 请求 Def 热重载；游戏还需处于 Playing 且启用开发者模式。游戏未运行时，`--xml-hot` 自动回退到普通构建和逐文件事务部署。热重载请求失败时文件仍已部署，命令会给出 warning；该功能不热更新 C# DLL。`test run` 不接受 `--xml-hot`；`test run --deploy` 使用普通部署。

普通部署会先构建并校验暂存目录，然后让目标目录保持原位，逐文件提交变更。提交前会检查将被替换或删除的文件是否占用；失败时输出被占用路径及可识别的进程。事务记录会在提交失败或意外中断时恢复原文件。不要为绕过失败部署而手动将部分输出复制进活动模组目录；若错误为 `DEPLOY_TARGET_BUSY`，关闭报告的占用程序后重试。若出现 `DEPLOY_RECOVERY_REQUIRED`，先停止 RimWorld，再运行 `test cleanup --dry-run` 检查事务；若出现 `DEPLOY_ROLLBACK_FAILED` 或 `DEPLOY_RECOVERY_FAILED`，保留备份并先运行 `test cleanup --dry-run` 查看恢复状态。`protected_files_regex` 匹配的顶层项目保持不变，更新后可能留下空目录。若启用着色器构建，着色器失败即为部署阻塞项。

## 测试

选择范围最小的合适模式：

- `quick`：自动启动并等待运行就绪信号；冒烟测试默认模式。此测试速度最快，只会检查游戏启动报错。
- `manual`：提供人工手动测试的游戏窗口；默认使用当前桌面。
- `auto`：自动发现测试目标，可使用多个 scope 和精确 DefName 白名单。
- `groupcombat` / `arena`：1.7 的独立群战评测 / 擂台淘汰赛，必须提供 `--scenario-path` 和显式 `--test-name`，不使用 `--scenario-only`。外部 XML 场景、混编预算、地图和结果语义读取同模板的 [rimworld-xml-test](../rimworld-xml-test/SKILL.md)。

auto 测试参数遵循以下规则：

- `--scope <value>` 可重复，多个 scope 取并集。正式值为 `standard/ui/combat/view/misc`；旧细分值仍以原窄范围语义兼容。
- `--defname <value>` 可重复，仅适用于 auto。DefName 是精确白名单，与 scope 取交集；不传 DefName 时枚举所选 scope 下的目标。
- `--batch-size <n>` 适用于 auto，默认 40。包括 Standard 在内的枚举运行都受该计划上限控制；候选超限时从全部候选中全局均匀、无放回随机抽取。
- 未传 scope 时默认为 `Standard`；Standard 横跨 UI、Combat、View、Misc，但不隐式启用 `CombatPawns` 和 `SkillPocketMaps`。
- 显式 DefName 测试继续忽略 `autoTestBatchSize`；其他 auto 运行（包括 Standard）都受该限制。
- 启用模组缺少必要上级依赖时只输出 warning，不阻挡测试；应根据 warning 补齐 `min_active_mods` 或 `--active-mods-file`。

例 1，只测试目标模组的三视图：

```powershell
.\.tools\RimDevTools.exe test run --mode auto --target-package-id aoba.deadmanswitch.core --scope threeviews
```

例 2，同时测试指定的近战和远程武器：

```powershell
.\.tools\RimDevTools.exe test run --mode auto --target-package-id aoba.deadmanswitch.core `
  --scope meleeweapons --scope rangedweapons `
  --defname DMS_StunBaton --defname DMS_TacticalPistol
```

测试必须覆盖当前源代码变更时使用 `--deploy`。`--sync-source` 需要 `--deploy`。quick 和 auto 使用配置的桌面隔离默认值；manual 除非明确覆盖，否则使用当前桌面。

运行期间绝不编辑活动 RimWorld 的 `ModsConfig.xml`、LogSync 设置、事务清单或请求/结果文件。RimDevTools 负责事务性还原。失败时报告稳定错误代码并保留恢复产物。

## 参考资料与工具安装

使用 `mod copy --dry-run` 先检查目标摘要和冲突；默认最多列出 10 个目标。需要查看完整依赖闭包、跳过项、程序集计划时添加 `--verbose`。确认计划后使用 `mod copy` 复制到 `mod_config.jsonc` 的 `project_paths.reference_docs`，并通过打包的 ILSpy 反编译程序集。用精确 package ID、名称、目录或绝对路径消除歧义；以 `--deps` 获取除官方内容与 Harmony 外的硬依赖闭包，仅在审查替换目标后使用 `--overwrite-existing`。

使用 `mods install-tools` 安装单个打包的 LogSyncMod 组件。安装器会验证清单、package ID、版本和内容哈希，保留 `PublishedFileId.txt`，阻止降级，并通过暂存/备份回滚。

## 防护规则

- 不要仅从进程退出推断成功；应解析最后的 JSON 和产物。
- 不要重试 `ENVIRONMENT_BUSY`；识别或等待占用操作。
- 不要删除 `.rimdev` 事务来掩盖恢复错误。
- 开始实际开发后，不要以通用模板程序集/模组名称部署。
- 测试进行期间，不要并发执行测试指令，因为环世界是单线程游戏，日志只有一个文件，并发执行后果不可控。
- 结论应关联命令输出、配置值或生成报告。

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
