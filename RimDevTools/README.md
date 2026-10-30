# RimDevTools

RimDevTools 是 Windows x64 的 .NET 10 CLI，可检查和部署 RimWorld 1.6 与 1.7+ 模组项目，并提供第三方参考模组拉取、反编译和可选的 LogSync 测试集成。

## 运行要求

- `RimDevTools.exe` 是 framework-dependent 单文件，需要 .NET 10 Runtime。
- 源码构建需要 .NET 10 SDK。
- `mod copy` 使用随包 ILSpy，目标机还需要 .NET 8 Runtime。
- RimWorld 1.6 模组编译需要 .NET Framework 4.8 Developer/Targeting Pack；4.7.2 也受支持。
- RimWorld 1.7 及更高版本默认沿用 1.7 布局，模组工程使用 .NET 10；`game_version` 仍须填写实际的精确版本号，以便只解析对应 `LoadFolders.xml` 版本目录。

`doctor` 根据 `game_version` 选择对应的游戏目录、程序集和工程目标框架，并检查 About.xml/Preview.png、配置路径、Steam/RimWorld、Harmony、ILSpy、锁和事务。1.7+ 默认隐藏不适用的 .NET Framework targeting pack 检查，`doctor --verbose` 会显示它。LogSync、AutoTestRunner 与 `min_active_mods` 属于可选测试能力，不会阻止普通项目检查或部署；AutoTest XML 只在显式运行 XML 测试时才由测试流程使用。缺少 About 文件或 Preview.png 不小于 1 MiB 时只给出 warning。`mods_config_path` 必须指向游戏实际读取的现有 `ModsConfig.xml`；`deploy` 和 `test` 会拒绝在该项无效时继续。它不检查启动 CLI 所需的 Runtime，也不探测 RimSage 网络或进程。

## 构建与发布

```powershell
dotnet build RimDevTools\RimDevTools.csproj -c Release
dotnet publish RimDevTools\RimDevTools.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
powershell -ExecutionPolicy Bypass -File .tools\publish-rimdevtools.ps1
powershell -ExecutionPolicy Bypass -File .tools\publish-rimdevtools.ps1 -VersionSuffix v1.0.7 -CreateZip
```

第二条发布命令会生成 `artifacts/RimDevTools-Package-v1.0.7/` 和同名 ZIP。未传入版本后缀时仍使用兼容目录 `artifacts/RimDevTools-Package/`。

发布目录内容：

```text
artifacts/RimDevTools-Package/
├── mod_config.jsonc
├── 1.6/
├── About/
├── source/
├── .mcp.json
├── opencode.jsonc
└── .tools/
    ├── RimDevTools.exe
    ├── Data/
    │   ├── LogSyncMod/ (RimWorld 1.6 and 1.7 assemblies and Defs)
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
RimDevTools.exe test run [--deploy] [--mode quick|manual|auto|xml] [--active-mods-file <path>] [--scope <value>]... [--defname <value>]... [--batch-size <n>] [--verbose]
RimDevTools.exe test cleanup [--dry-run] [--status] [--verbose]
RimDevTools.exe mods install-tools [--mods-path <path>] [--dry-run] [--verbose]
```

项目命令从当前目录或直接父目录发现 `mod_config.jsonc`。最终结果是 stdout 上的单个 JSON 对象，进度与诊断写入 stderr。默认结果只包含摘要和关键字段；`--verbose` 展开检查项、计划、候选元数据和可用的详细结果，并显示构建、Unity、ILSpy 子进程输出。警告和错误始终保留；普通模式会显示子进程识别出的 warning/error 行，失败时最多附带 12 行、每行最多 500 个字符的末尾诊断。游戏自身 stdout/stderr 仍保持丢弃。`--version` 必须作为唯一参数使用，并只返回 RimDevTools 自身版本。

### 版本选择

`game_version` 必须填写当前目标游戏的精确版本，例如 `1.6`、`1.7` 或 `1.8`。1.6 使用 `RimWorldWin64.exe` 与 `RimWorldWin64_Data/Managed`；1.7 及更高版本默认使用 `RimWorld.exe`、游戏根目录程序集和 .NET 10。工具会核对 `Assembly-CSharp.dll` 的版本，并拒绝把配置指向另一游戏版本。Harmony 和其他模组程序集仍按 `game_version` 精确查找，1.8 不会回退到 1.7 DLL。

RimDevTools 的 Unity Shader Bundle 构建仅适用于 1.6。1.7+ 项目使用游戏原生 GLSL/ShaderDef 工作流，不要启用旧的 `shader_bundle`。

### 部署

先运行 `doctor`，再运行 `deploy --dry-run` 检查部署摘要；需要查看完整计划时添加 `--verbose`。项目必须位于独立工作目录，不能位于 RimWorld `Mods` 或 Steam Workshop 内容目录；部署会自动同步到 `RimWorld/Mods`。普通部署会构建配置中的项目并准备暂存目录，然后在目标目录保持原位的情况下逐文件替换和清理；提交前会检查将要修改的文件是否被占用，失败时报告路径及可识别的占用进程，并通过事务备份回滚。空目录可能留在目标中，不影响模组文件。`protected_files_regex` 匹配的顶层文件或目录会保留。`--xml-hot` 会先检查 RimWorld 是否正在运行：运行中时将非 DLL 文件暂存后逐文件事务更新，若更新 Def/Patches XML 则至少等待 3 秒并向 LogSyncMod 本机 HTTP endpoint 请求重载；未运行时回退到普通构建和逐文件事务部署。`--xml-hot` 只适用于独立 `deploy`，`test run --deploy` 始终使用普通部署。纯 XML 模组可以将 `csproj_paths` 配置为空数组，此时 deploy 跳过 C# 构建但仍同步 `sync_items`。配置 `sync_source: true` 可让每次部署默认包含 `source/`，`--sync-source` 仍可单次启用。

### 第三方参考

`mod search` 消除名称歧义；`mod copy` 将目标复制到 `mod_config.jsonc` 的 `project_paths.reference_docs`（默认 `参考文档`）并调用 ILSpy。`--deps`、`--include-ignored` 和 `--overwrite-existing` 均保持显式语义，不会自动启用；官方本体、DLC 和 Harmony 作为共享依赖不会被闭包复制。`--dry-run` 预览依赖闭包、跳过的共享依赖、目标、冲突和待反编译程序集，不修改文件。

### 测试

`test settings` 用于给用户手动启动的 RimWorld 会话持久化 LogSync 设置。它会等待游戏退出、取得共享环境锁，写入当前项目的日志路径和 `log_keywords`，并保留这些设置；`test run` 则以事务方式应用等价设置并在运行后恢复。

`test run` 和 `test settings` 分配本轮目录后，成功 JSON 的 `result.artifacts`、失败 JSON 的 `error.details.artifacts` 默认返回 `directoryPath`、`syncLogPath`、`autoTestOutputPath` 三个绝对路径，无需 `--verbose`。目录分配前的参数、环境或部署失败不返回本轮产物；路径表示输出位置，quick/manual 等模式可能没有 AutoTest 文件。直接读取 stdout 和原始产物，在聊天中报告结论；除用户明确要求导出外，禁止自建 evidence、任务或日期归档目录，禁止重定向、另存或复制日志、截图、存档及编写归档包装脚本。保留与清理由工具负责。

quick 是启动烟雾测试；manual 用于交互；auto/XML 验证结构化 AutoTest 结果。测试命令在执行时才要求与 `game_version` 匹配的 LogSync 宿主；auto/XML 还要求同版本 AutoTestRunner。项目无需包含 AutoTest XML；Spec 文件只用于显式的 `--mode xml`。当前随包测试宿主只覆盖其清单中声明的版本，缺少 1.7/1.8 版本程序集时工具会停止测试，不会加载 1.6 宿主。auto/xml 支持可重复的 `--scope`，范围按首次出现顺序去重，未传时默认为 Standard。`--defname` 仅适用于 auto，可重复指定精确目标；任何未命中目标都会使结果不完整并失败。`--batch-size` 是 auto/xml 的正整数计划上限，默认 40；包括 Standard 在内的枚举运行都会受该上限控制，候选超限时从全部候选中全局均匀、无放回随机抽取。显式 DefName 运行继续忽略该限制。XML 模式的具体用例仍由 Spec 决定。测试控制信号和每 5 秒一次的主线程心跳从 `.tools/log/精选日志.log` 读取，不再依赖 Unity `Player.log`；进程存活但连续 60 秒没有当前 runId 的心跳时返回 `GAME_HUNG` 并终止游戏。RimWorld stdout/stderr 会被安静丢弃，不再生成 `RimWorldProcess.log`；自动测试会落盘独立 `*_Evidence.json`，并在 `*_Result.json` 中写入 `evidenceSchemaVersion` 与 `evidencePath`，CLI 会验证其 schema、runId、packageId 和 success。CLI stdout 只保留最终 JSON。测试会检查本轮 activeMods 中每个可解析模组的必要硬依赖；依赖已安装但未启用、或完全找不到时，会在成功/失败 JSON 的 `warnings` 或 `details.dependencyWarnings` 中提示，但不会阻挡执行。`doctor` 通过 `mods.min_active_dependencies` 以非必需 warning 检查 `min_active_mods`。测试通过共享环境锁与配置事务恢复 `mods_config_path` 所在目录中的 RimWorld 配置。无论使用 `min_active_mods` 还是外部 `--active-mods-file`，都必须启用 `min_active_mods` 中的全部模组。`--deploy` 用于测试当前源码改动。事务冲突后，可在 RimWorld 完全退出后运行 `test cleanup`；`--dry-run` 只检查不修改，`--status` 只读报告锁、游戏、事务和部署现场。

正式 scope 值为：`standard`、`ui`、`combat`、`view`、`misc`。Standard 横跨四类常规测试，但不隐式启用 `combatpawns` 与 `skillpocketmaps`；旧细分值继续作为窄范围兼容入口。

`doctor` 还会读取已解析的 `0Harmony.dll` AssemblyVersion。低于工具要求版本时只给出 warning，并提示重新订阅 Harmony。

### 工具 Mod 安装

`mods install-tools --dry-run` 输出单组件计划。实际安装校验清单、About、程序集版本和 SHA-256 内容哈希；相同内容跳过、旧版本升级、新版本不降级、同版本冲突拒绝覆盖。升级时只保留 `PublishedFileId.txt`。

## 回归测试

```powershell
dotnet build RimDevTools.Tests\RimDevTools.Tests.csproj
dotnet RimDevTools.Tests\bin\Debug\net10.0\RimDevTools.Tests.dll

dotnet build RimDevTools.ReleaseTests\RimDevTools.ReleaseTests.csproj
dotnet RimDevTools.ReleaseTests\bin\Debug\net10.0\RimDevTools.ReleaseTests.dll artifacts\RimDevTools-Package-v1.0.7
```

单元测试使用临时游戏目录。发布测试验证模板逐文件一致、三种 MCP 配置、两套 RimSage skill、单工具 Mod 清单、ILSpy 和干净日志/参考目录。
