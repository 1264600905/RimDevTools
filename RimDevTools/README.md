# RimDevTools

RimDevTools 是 Windows x64 的 .NET 10 CLI，可检查和部署 RimWorld 1.6/1.7+ 模组项目，并提供第三方参考模组拉取、反编译和可选的 LogSync 测试集成。随包 LogSyncMod 提供 RimWorld 1.6 与 1.7 对应的程序集和 Defs。

## 运行要求

- `RimDevTools.exe` 是 framework-dependent 单文件，需要 .NET 10 Runtime。
- 源码构建需要 .NET 10 SDK。
- `mod copy` 使用随包 ILSpy，目标机还需要 .NET 8 Runtime。
- RimWorld 1.6 模组编译需要 .NET Framework 4.8 Developer/Targeting Pack；4.7.2 也受支持。
- RimWorld 1.7 及更高版本默认沿用 1.7 布局，模组工程使用 .NET 10；`game_version` 仍须填写实际的精确版本号，以便只解析对应 `LoadFolders.xml` 版本目录。

`doctor` 根据 `game_version` 选择对应的游戏目录、程序集和工程目标框架，并检查 About.xml/Preview.png、配置路径、Steam/RimWorld、Harmony、ILSpy、锁和事务。1.7+ 默认隐藏不适用的 .NET Framework targeting pack 检查，`doctor --verbose` 会显示它。LogSync、AutoTestRunner 与 `min_active_mods` 属于可选测试能力，不会阻止普通项目检查或部署；普通项目无需包含自动测试 Spec XML。缺少 About 文件或 Preview.png 不小于 1 MiB 时只给出 warning。`mods_config_path` 必须指向游戏实际读取的现有 `ModsConfig.xml`；`deploy` 和 `test` 会拒绝在该项无效时继续。它不检查启动 CLI 所需的 Runtime，也不探测 RimSage 网络或进程。

## 构建与发布

```powershell
dotnet build RimDevTools\RimDevTools.csproj -c Release
dotnet publish RimDevTools\RimDevTools.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
powershell -ExecutionPolicy Bypass -File .tools\publish-rimdevtools.ps1
powershell -ExecutionPolicy Bypass -File .tools\publish-rimdevtools.ps1 -CreateZip
```

默认发布会生成 1.6、1.7 两个模板包和一个 Tools 简化包，共用载荷只准备一次。模板包命名为 `RimDevTools-<工具版本>-Template-RimWorld-<游戏版本>`，简化包为 `RimDevTools-<工具版本>-Tools`。可传 `-TemplateGameVersion 1.6`、`1.7` 或 `Tools` 只生成一种包。`-CreateZip` 只输出对应的 ZIP，不再生成兼容包或独立校验附件。Tools 包包含 Codex、Claude 两套各 7 个 skill 的全部文件。发布页只上传三个 ZIP，更新器使用 GitHub 附件 SHA-256 摘要校验；自动更新仍保留非核心 skill。1.5.2 及更早版本的更新器需手动安装一次。

`RimDevTools.exe --version` 同时报告 `rimDevToolsVersion`、`templateGameVersion` 和 `templateSchemaVersion`。模板版本信息来自每个发布包中的 `.tools/Data/project-template.manifest.json`，所以同一个工具版本可随不同游戏版本模板发布。

模板包目录内容（Tools 包仅含正式工具、两套完整 skill、发行清单与人工配置参考，不含模板及 `.gitignore`）：

```text
artifacts/RimDevTools-1.5.6.0-Template-RimWorld-1.6/
artifacts/RimDevTools-1.5.6.0-Template-RimWorld-1.7/
├── mod_config.jsonc
├── 1.6/ or 1.7/
├── About/
├── source/
├── .mcp.json
├── opencode.jsonc
└── .tools/
    ├── RimDevTools.exe
    ├── Data/
    │   ├── LogSyncMod/ (RimWorld 1.6/1.7 assemblies and Defs)
    │   ├── project-template.manifest.json
    │   ├── tool-package.manifest.json
    │   ├── tool-mods.manifest.json
    │   └── licenses/
    ├── Ilspy/
    ├── log/
    │   ├── 精选日志.log
    │   └── AutoTestOutput/
    └── modlist/
```

工具 Mod 清单必须且只能包含 `trigger.logsyncmod`。ILSpy 保留在发布包中，因为 `mod copy` 需要生成第三方程序集的可读源码。

## 命令

```powershell
RimDevTools.exe --version
RimDevTools.exe doctor [--verbose]
RimDevTools.exe deploy [--sync-source] [--xml-hot] [--dry-run] [--verbose]
RimDevTools.exe mod search <keyword> [--fuzzy] [--verbose]
RimDevTools.exe mod copy <query> [--version <value>] [--deps] [--include-ignored] [--fuzzy] [--overwrite-existing] [--dry-run] [--verbose]
RimDevTools.exe mod dependencies <query> [--version <value>] [--fuzzy] [--verbose]
RimDevTools.exe mod dependents <query> [--version <value>] [--fuzzy] [--verbose]
RimDevTools.exe test settings [--verbose]
RimDevTools.exe test run [--deploy] [--mode quick|manual|auto|groupcombat|arena] [--active-mods-file <path>] [--scope <value>]... [--defname <value>]... [--batch-size <n>] [--verbose]
RimDevTools.exe test cleanup [--dry-run] [--status] [--verbose]
RimDevTools.exe mods install-tools [--mods-path <path>] [--dry-run] [--verbose]
```

1.7 独立群战入口：`test run --mode groupcombat --scenario-path <XML 文件或目录> --test-name <testDefName 或完整 caseId>`，多个名称可重复传入。只接受双方普通主地图评测，普通 auto 扫描不会运行群战；不传名称即报错。完整语法、样例和伤害口径见[第四批使用说明](../策划案/RimDevTools外部场景XML第四批使用说明.md)。

1.7 独立擂台入口：`test run --mode arena --scenario-path <ArenaSuite XML 文件或目录> --test-name <testDefName>`，支持固定数量、combatPower 预算、单败淘汰赛，以及 `Tournament.format="bossRoundRobin"` 魔王轮战和 `format="limitChallenge"` 极限挑战。极限挑战优先按人数增长，也支持 CP 预算增长；两者均可使用乘法或固定加法。三种赛制开场均以规则页覆盖角色准备过程，现实时间至少 5 秒。现有群战也可用 `PawnGroup.combatPowerBudget` 按战力配兵。详见[擂台测试使用说明](../策划案/RimDevTools擂台测试使用说明.md)。

擂台容忍伤害采集异常：保留异常记录，伤害交换项取中性分，按实际胜负继续晋级。完整赛程正常完成并展示荣誉榜后自动关闭游戏；报错、取消、未决平局或超时保留游戏。返回 JSON 标明 `gameStillRunning` 和 `transactionRetained`。游戏仍运行时保留配置事务，手动退出游戏后执行 `test cleanup` 或在下一次测试启动时恢复。

项目命令从当前目录或直接父目录发现 `mod_config.jsonc`。最终结果是 stdout 上的单个 JSON 对象，进度与诊断写入 stderr。默认结果只包含摘要和关键字段；`--verbose` 展开检查项、计划、候选元数据和可用的详细结果，并显示构建、Unity、ILSpy 子进程输出。警告和错误始终保留；普通模式会显示子进程识别出的 warning/error 行，失败时最多附带 12 行、每行最多 500 个字符的末尾诊断。游戏自身 stdout/stderr 仍保持丢弃。`--version` 必须作为唯一参数使用，并只返回 RimDevTools 自身版本。

### 版本选择

`game_version` 必须填写当前目标游戏的精确版本，例如 `1.6`、`1.7` 或 `1.8`。1.6 使用 `RimWorldWin64.exe` 与 `RimWorldWin64_Data/Managed`；1.7 及更高版本默认使用 `RimWorld.exe`、游戏根目录程序集和 .NET 10。工具会核对 `Assembly-CSharp.dll` 的版本，并拒绝把配置指向另一游戏版本。Harmony 和其他模组程序集仍按 `game_version` 精确查找，1.8 不会回退到 1.7 DLL。

`test run` 在启动前将 `ModsConfig.xml` 的 `<version>` 自动写为已校验的 `game_version`，避免切换游戏版本时启用列表被游戏重置。此更新先于配置事务备份；测试结束、失败、取消或中断恢复时会恢复原模组列表与设置，并保留目标版本号。quick、manual 和 auto 模式均适用。

RimDevTools 的 Unity Shader Bundle 构建仅适用于 1.6。1.7+ 项目使用游戏原生 GLSL/ShaderDef 工作流，不要启用旧的 `shader_bundle`。

### 部署

事务清单更新失败时，`DEPLOY_MANIFEST_REPLACE_FAILED` / `DEPLOY_MANIFEST_WRITE_FAILED` 返回清单路径、尝试状态、磁盘现存状态和原生错误码；Win32 1175 会单独标识为清单替换失败。
成功部署的 `result.fileCommit.manifestWriteRetries` 表示事务清单更新遇到短暂占用后的重试次数。

先运行 `doctor`，再运行 `deploy --dry-run` 检查部署摘要；需要查看完整计划时添加 `--verbose`。项目必须位于独立工作目录，不能位于 RimWorld `Mods` 或 Steam Workshop 内容目录；部署会自动同步到 `RimWorld/Mods`。普通部署会构建配置中的项目并准备暂存目录，然后在目标目录保持原位的情况下逐文件替换和清理；提交前会检查将要修改的文件是否被占用，失败时报告路径及可识别的占用进程，并通过事务备份回滚。空目录可能留在目标中，不影响模组文件。`protected_files_regex` 匹配的顶层文件或目录会保留。`--xml-hot` 会先检查 RimWorld 是否正在运行：运行中时将非 DLL 文件暂存后逐文件事务更新，若更新 Def/Patches XML 则至少等待 3 秒并向 LogSyncMod 本机 HTTP endpoint 请求重载；未运行时回退到普通构建和逐文件事务部署。`--xml-hot` 只适用于独立 `deploy`，`test run --deploy` 始终使用普通部署。纯 XML 模组可以将 `csproj_paths` 配置为空数组，此时 deploy 跳过 C# 构建但仍同步 `sync_items`。配置 `sync_source: true` 可让每次部署默认包含 `source/`，`--sync-source` 仍可单次启用。

### Steam 创意工坊编辑

`steamworkshops upload` 上传上次完整部署，`deploy --upload` 在一条命令中部署并上传，两者都保留已有条目的页面元数据。
三个提交入口（上述两者及 `steamworkshops edit`）支持 `--change-note <文本>` 或 `--change-note-file <UTF-8文件>`；两者互斥。支持中文和多行，未提供、空文本或空文件均不附改动说明，不再发送固定说明。

`steamworkshops edit --title "标题" --description-file <UTF-8文件> --text-language schinese` 编辑目标语言的页面文本，默认语言是 `english`。使用 Steamworks API 语言代码，如 `tchinese`、`japanese`、`koreana`，不接受 `zh-CN`。每次提交写入一种语言，保留其他语言；只提供标题或描述时，先读取目标语言并补传另一个字段，避免 Steam 清空未指定的译文。首次创建也可指定 `--text-language`；已有内容上传不改写任何语言的文本。查询入口不增加语言参数。

主封面继续使用 `edit --preview-file <图片>`。附图使用可重复的 `--add-preview <图片>`、`--update-preview <从零开始的索引>=<图片路径>`、`--remove-preview <索引>`；索引来自 `status --verbose` 的 `additionalPreviews`，同一提交按操作前的索引解释，不接受重复或冲突索引。普通 status 不再输出附图列表。图片须为小于 1 MiB 的 PNG/JPG/GIF。附图增改删不改变主封面。

所有 `edit` 操作均校验 owner、沿用非阻塞超时约定，且不构建、部署或传输模组内容。只改标题、附图等显式字段时保留未指定字段；不带字段选项的 `edit` 仍只同步本地 About 描述。

关键词 `search --details` 清洗官方 255 字节摘要；`status` 及精确 ID/链接搜索清洗官方全文，不另行截断。展示会移除裸露 HTTP/HTTPS 地址、图片块和实际 BBCode，保留链接的说明文字、普通 `[新增]` / `[NEW]`、列表与段落；原始分页缓存及上传文本保持原样。

`status` 默认只显示关键信息、发布者昵称和 SteamID、一份带时区的更新时间、权限及下载摘要；`--verbose` 展开原始 flags、权限和绑定依据、预览地址与其他时间格式。未知值及失败原因在普通结果中也可见，绑定冲突会保留完整来源。`dependencies` 仅查询工坊登记的直接前置模组（ID、名称、是否订阅、本机状态）及 DLC（App ID、名称）；接口失败时保留已知 ID，缺失部分标为未知，不把集合成员当作前置。不自动订阅、下载、启用、排序或递归解析依赖；主体尚未下载也可查询，结果不代表包内 About.xml 的全部依赖和版本约束。

### 第三方参考

`mod search` 消除名称歧义；`mod copy` 将目标复制到 `mod_config.jsonc` 的 `project_paths.reference_docs`（默认 `参考文档`）并调用 ILSpy。`--deps`、`--include-ignored` 和 `--overwrite-existing` 均保持显式语义，不会自动启用；官方本体、DLC 和 Harmony 作为共享依赖不会被闭包复制。`--dry-run` 预览依赖闭包、跳过的共享依赖、目标、冲突和待反编译程序集，不修改文件。

### 测试

`test settings` 用于给用户手动启动的 RimWorld 会话持久化 LogSync 设置。它会等待游戏退出、取得共享环境锁，写入当前项目的日志路径和 `log_keywords`，并保留这些设置；`test run` 则以事务方式应用等价设置并在运行后恢复。

`test run` 和 `test settings` 分配本轮目录后，成功 JSON 的 `result.artifacts`、失败 JSON 的 `error.details.artifacts` 默认返回 `directoryPath`、`syncLogPath`、`autoTestOutputPath` 三个绝对路径，无需 `--verbose`。目录分配前的参数、环境或部署失败不返回本轮产物；路径表示输出位置，quick/manual 等模式可能没有 AutoTest 文件。直接读取 stdout 和原始产物，在聊天中报告结论；除用户明确要求导出外，禁止自建 evidence、任务或日期归档目录，禁止重定向、另存或复制日志、截图、存档及编写归档包装脚本。保留与清理由工具负责。

quick/auto `test run` 完成后，成功 JSON 的 `result.logSummary`、失败 JSON 的 `error.details.logSummary` 都包含 `errorCount` 和 `warningCount`，统计本轮 `syncLogPath` 中记录的 Error/Warning 条目。`available`、`countComplete`、`truncated` 标明日志读取是否成功、游戏是否已退出及 LogSync 是否触发行数上限；计数范围受 LogSync 的模组/关键词筛选影响。该摘要不依赖 `--verbose`，顶层 `warnings` 仍表示依赖警告。

quick 是启动烟雾测试；manual 用于交互；auto 验证结构化 AutoTest 结果。测试命令在执行时才要求与 `game_version` 匹配的 LogSync 宿主；auto 还要求同版本 AutoTestRunner。项目无需包含 AutoTest Spec XML。当前随包测试宿主只覆盖其清单中声明的版本，缺少 1.7/1.8 版本程序集时工具会停止测试，不会加载 1.6 宿主。auto 支持可重复的 `--scope`，范围按首次出现顺序去重，未传时默认为 Standard。`--defname` 仅适用于 auto，可重复指定精确目标；任何未命中目标都会使结果不完整并失败。`--batch-size` 是 auto 的正整数计划上限，默认 40；包括 Standard 在内的枚举运行都会受该上限控制，候选超限时从全部候选中全局均匀、无放回随机抽取。显式 DefName 运行继续忽略该限制。测试控制信号和每 5 秒一次的主线程心跳从 `.tools/log/精选日志.log` 读取，不再依赖 Unity `Player.log`；进程存活但连续 60 秒没有当前 runId 的心跳时返回 `GAME_HUNG` 并终止游戏。RimWorld stdout/stderr 会被安静丢弃，不再生成 `RimWorldProcess.log`；自动测试会落盘独立 `*_Evidence.json`，并在 `*_Result.json` 中写入 `evidenceSchemaVersion` 与 `evidencePath`，CLI 会验证其 schema、runId、packageId 和 success。CLI stdout 只保留最终 JSON。测试会检查本轮 activeMods 中每个可解析模组的必要硬依赖；依赖已安装但未启用、或完全找不到时，会在成功/失败 JSON 的 `warnings` 或 `details.dependencyWarnings` 中提示，但不会阻挡执行。`doctor` 通过 `mods.min_active_dependencies` 以非必需 warning 检查 `min_active_mods`。测试通过共享环境锁与配置事务恢复 `mods_config_path` 所在目录中的 RimWorld 配置。无论使用 `min_active_mods` 还是外部 `--active-mods-file`，都必须启用 `min_active_mods` 中的全部模组。`--deploy` 用于测试当前源码改动。事务冲突后，可在 RimWorld 完全退出后运行 `test cleanup`；`--dry-run` 只检查不修改，`--status` 只读报告锁、游戏、事务和部署现场。

正式 scope 值为：`standard`、`ui`、`combat`、`view`、`misc`。Standard 横跨四类常规测试，但不隐式启用 `combatpawns` 与 `skillpocketmaps`；旧细分值继续作为窄范围兼容入口。

`doctor` 还会读取已解析的 `0Harmony.dll` AssemblyVersion。低于工具要求版本时只给出 warning，并提示重新订阅 Harmony。

### 工具 Mod 安装

`mods install-tools --dry-run` 输出单组件计划。实际安装校验清单、About、程序集版本和 SHA-256 内容哈希；相同内容跳过、旧版本升级、新版本不降级、同版本冲突拒绝覆盖。升级时只保留 `PublishedFileId.txt`。

## 回归测试

```powershell
dotnet build RimDevTools.Tests\RimDevTools.Tests.csproj
dotnet RimDevTools.Tests\bin\Debug\net10.0\RimDevTools.Tests.dll

dotnet build RimDevTools.ReleaseTests\RimDevTools.ReleaseTests.csproj
dotnet RimDevTools.ReleaseTests\bin\Debug\net10.0\RimDevTools.ReleaseTests.dll artifacts\RimDevTools-1.5.6.0-Template-RimWorld-1.7
```

单元测试使用临时游戏目录。发布测试验证模板逐文件一致、三种 MCP 配置、两套 RimSage skill、单工具 Mod 清单、ILSpy 和干净日志/参考目录。

## 工具自动更新

正式解压安装支持 `update --check`、`update --dry-run`、`update` 和 `update status`。普通业务命令启动检查使用 24 小时缓存，联网总预算最多 2 秒；失败退避 1 小时，不影响业务结果。帮助、版本、状态查询及内部助手不联网检查。

用户已要求更新时，先检查和预览，再执行 `update`，随后查询 `update status` 直到终态。`scheduled` 只说明助手接收了更新，不能报告已经成功；成功状态为 `succeeded`。不自动重跑原业务命令。首次获得更新能力需手动安装一次新版，源码仓库及缺少可信发行清单的旧安装不自动更新。

`UPDATE_CONFLICT` 会列出本地受管理文件冲突，整次更新停止，保留本地修改。配置仅补充缺失字段，保留注释、未知字段、模式和游戏目标；不覆盖项目源码、资源、`.gitignore`、其他 skill 或工坊身份。`TOOL_UPDATE_BUSY` 表示安装仍在使用，`TOOL_UPDATE_RECOVERY_REQUIRED` 表示更新未完成或需要恢复。使用 `update status` 返回的原始状态、事务路径和 `recoveryCommand`；恢复前让已有更新进程退出。即使主 EXE 无法运行，保留的助手仍可恢复。未知并发修改会阻止恢复并保留备份，先处理对应冲突再重试。

完成后重新读取磁盘上的完整 `rimdevtools` skill。项目私有的工具 Mod 载荷更新不等于游戏中已经安装或启用新版，游戏内组件仍由 `mods install-tools` 管理。缓存、暂存和回滚数据由工具维护；保留最近三份已完成事务，不删除未完成恢复所需的数据。
