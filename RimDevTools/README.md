# RimDevTools

RimDevTools 是 Windows x64 的 .NET 10 CLI，负责 RimWorld 1.6 模组项目检查、构建部署、第三方参考模组拉取与反编译、LogSync 测试，以及随包 LogSyncMod 安装。

## 运行要求

- `RimDevTools.exe` 是 framework-dependent 单文件，需要 .NET 10 Runtime。
- 源码构建需要 .NET 10 SDK。
- `mod copy` 使用随包 ILSpy，目标机还需要 .NET 8 Runtime。
- 模组编译需要 .NET Framework 4.8 Developer/Targeting Pack；4.7.2 也受支持。

`doctor` 检查项目、About.xml/Preview.png、`mod_config.jsonc` 的路径、Steam/RimWorld、程序集、最小启用模组、ILSpy、LogSync、AutoTest、锁和事务。缺少 About 文件或 Preview.png 不小于 1 MiB 时只给出 warning。`mods_config_path` 必须指向游戏实际读取的现有 `ModsConfig.xml`；`deploy` 和 `test` 会拒绝在该项无效时继续。它不检查启动 CLI 所需的 Runtime，也不探测 RimSage 网络或进程。

## 构建与发布

```powershell
dotnet build RimDevTools\RimDevTools.csproj -c Release
dotnet publish RimDevTools\RimDevTools.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
powershell -ExecutionPolicy Bypass -File .tools\publish-rimdevtools.ps1
```

发布目录：

```text
artifacts/RimDevTools-Package/
├── mod_config.jsonc
├── 1.6/
├── About/
├── source/
├── .codex/config.toml
├── .mcp.json
├── opencode.jsonc
└── .tools/
    ├── RimDevTools.exe
    ├── Data/
    │   ├── LogSyncMod/
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
RimDevTools.exe deploy [--sync-source] [--dry-run]
RimDevTools.exe mod search <keyword> [--fuzzy]
RimDevTools.exe mod copy <query> [--version <value>] [--deps] [--include-ignored] [--fuzzy] [--overwrite-existing] [--dry-run]
RimDevTools.exe mod dependencies <query> [--version <value>] [--fuzzy]
RimDevTools.exe mod dependents <query> [--version <value>] [--fuzzy]
RimDevTools.exe test settings
RimDevTools.exe test run [--deploy] [--mode quick|manual|auto|xml] [--active-mods-file <path>] [--scope <value>]... [--defname <value>]... [--batch-size <n>]
RimDevTools.exe test cleanup [--dry-run] [--status]
RimDevTools.exe mods install-tools [--mods-path <path>] [--dry-run]
```

项目命令从当前目录或直接父目录发现 `mod_config.jsonc`。最终结果是 stdout 上的单个 JSON 对象，进度与诊断写入 stderr。`--version` 必须作为唯一参数使用，并只返回 RimDevTools 自身版本。

### 部署

先运行 `doctor --verbose`，再运行 `deploy --dry-run` 检查不可变计划。项目必须位于独立工作目录，不能位于 RimWorld `Mods` 或 Steam Workshop 内容目录；部署会自动同步到 `RimWorld/Mods`。实际部署会构建配置中的项目，以暂存目录和备份目录原子替换目标，并保留 `protected_files_regex` 匹配的文件。纯 XML 模组可以将 `csproj_paths` 配置为空数组，此时 deploy 跳过 C# 构建但仍同步 `sync_items`。配置 `sync_source: true` 可让每次部署默认包含 `source/`，`--sync-source` 仍可单次启用。

### 第三方参考

`mod search` 消除名称歧义；`mod copy` 将目标复制到 `mod_config.jsonc` 的 `project_paths.reference_docs`（默认 `参考文档`）并调用 ILSpy。`--deps`、`--include-ignored` 和 `--overwrite-existing` 均保持显式语义，不会自动启用；官方本体、DLC 和 Harmony 作为共享依赖不会被闭包复制。`--dry-run` 预览依赖闭包、跳过的共享依赖、目标、冲突和待反编译程序集，不修改文件。

### 测试

`test settings` 用于给用户手动启动的 RimWorld 会话持久化 LogSync 设置。它会等待游戏退出、取得共享环境锁，写入当前项目的日志路径和 `log_keywords`，并保留这些设置；`test run` 则以事务方式应用等价设置并在运行后恢复。

quick 是启动烟雾测试；manual 用于交互；auto/XML 验证结构化 AutoTest 结果。auto/xml 支持可重复的 `--scope`，范围按首次出现顺序去重，未传时默认为 Standard。`--defname` 仅适用于 auto，可重复指定精确目标；任何未命中目标都会使结果不完整并失败。`--batch-size` 是 auto/xml 的正整数 Targeted 计划上限，默认 40；Standard 全量和显式 DefName 运行忽略该限制。XML 模式的具体用例仍由 Spec 决定。测试控制信号和每 5 秒一次的主线程心跳从 `.tools/log/精选日志.log` 读取，不再依赖 Unity `Player.log`；进程存活但连续 60 秒没有当前 runId 的心跳时返回 `GAME_HUNG` 并终止游戏。RimWorld stdout/stderr 会被安静丢弃，不再生成 `RimWorldProcess.log`；自动测试会落盘独立 `*_Evidence.json`，并在 `*_Result.json` 中写入 `evidenceSchemaVersion` 与 `evidencePath`，CLI 会验证其 schema、runId、packageId 和 success。CLI stdout 只保留最终 JSON。测试会检查本轮 activeMods 中每个可解析模组的必要硬依赖；依赖已安装但未启用、或完全找不到时，会在成功/失败 JSON 的 `warnings` 或 `details.dependencyWarnings` 中提示，但不会阻挡执行。`doctor` 通过 `mods.min_active_dependencies` 以非必需 warning 检查 `min_active_mods`。测试通过共享环境锁与配置事务恢复 `mods_config_path` 所在目录中的 RimWorld 配置。无论使用 `min_active_mods` 还是外部 `--active-mods-file`，都必须启用 `min_active_mods` 中的全部模组。`--deploy` 用于测试当前源码改动。事务冲突后，可在 RimWorld 完全退出后运行 `test cleanup`；`--dry-run` 只检查不修改，`--status` 只读报告锁、游戏、事务和部署现场。

正式 scope 值为：`standard`、`ui`、`combat`、`view`、`misc`。Standard 横跨四类常规测试，但不隐式启用 `combatpawns` 与 `skillpocketmaps`；旧细分值继续作为窄范围兼容入口。

`doctor` 还会读取已解析的 `0Harmony.dll` AssemblyVersion。低于工具要求版本时只给出 warning，并提示重新订阅 Harmony。

### 工具 Mod 安装

`mods install-tools --dry-run` 输出单组件计划。实际安装校验清单、About、程序集版本和 SHA-256 内容哈希；相同内容跳过、旧版本升级、新版本不降级、同版本冲突拒绝覆盖。升级时只保留 `PublishedFileId.txt`。

## 回归测试

```powershell
dotnet build RimDevTools.Tests\RimDevTools.Tests.csproj
dotnet RimDevTools.Tests\bin\Debug\net10.0\RimDevTools.Tests.dll

dotnet build RimDevTools.ReleaseTests\RimDevTools.ReleaseTests.csproj
dotnet RimDevTools.ReleaseTests\bin\Debug\net10.0\RimDevTools.ReleaseTests.dll artifacts\RimDevTools-Package
```

单元测试使用临时游戏目录。发布测试验证模板逐文件一致、三种 MCP 配置、两套 RimSage skill、单工具 Mod 清单、ILSpy 和干净日志/参考目录。
