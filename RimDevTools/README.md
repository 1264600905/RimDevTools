# RimDevTools

RimDevTools 是本仓库内的 .NET 10 Windows CLI，统一承载模组部署、参考模组处理和 RimWorld 测试准备/运行。

## 环境要求

RimDevTools、RimSearcher 和 DecompilerServer 均要求 Windows x64 .NET 10 Runtime。仅运行工具不要求 .NET 10 SDK；由于缺少 Runtime 时 RimDevTools 本身无法启动，`doctor` 不检查该前提。编译模组还需要 dotnet SDK，并推荐安装 .NET Framework 4.8 Developer/Targeting Pack，也可使用 4.7.2 作为兼容替代。

## 构建

```powershell
dotnet build RimDevTools\RimDevTools.csproj -c Release
dotnet publish RimDevTools\RimDevTools.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
.\.tools\publish-rimdevtools.ps1
```

发布脚本默认生成隔离目录 `artifacts/RimDevTools-Package`，不会复制项目现有日志、自动测试结果或数据库：

```text
RimDevTools-Package/
├── mod_config.jsonc
├── 1.6/
├── About/
├── source/
└── .tools/
    ├── RimDevTools.exe
    ├── rimsearcher.exe
    ├── Data/
    │   ├── LogSyncMod/
    │   ├── RimSearcher_DataMod/
    │   ├── tool-mods.manifest.json
    │   └── licenses/
    ├── Ilspy/
    ├── log/
    │   ├── 精选日志.log
    │   └── AutoTestOutput/
    ├── modlist/
    │   ├── defaultMods.xml
    │   └── deadmanswitch-core.xml
    └── db/
```

`RimDevTools.exe` 和 `rimsearcher.exe` 都是 framework-dependent 单文件，不内置 .NET；使用机器必须安装 .NET 10 Runtime。DataMod 必须保留 RimWorld 模组目录结构，ILSpy 也保留独立工具目录。

## 命令

```powershell
RimDevTools.exe --version
RimDevTools.exe doctor
RimDevTools.exe doctor --verbose
RimDevTools.exe deploy --dry-run
RimDevTools.exe ref search <keyword>
RimDevTools.exe ref pull <query> --deps
RimDevTools.exe test settings
RimDevTools.exe test run --mode quick
RimDevTools.exe data refresh
RimDevTools.exe data status
RimDevTools.exe mods install-tools --dry-run
RimDevTools.exe mods install-tools
```

项目命令从当前目录或直接父目录发现 `mod_config.jsonc`。正常结果写入 stdout 的单个 JSON 对象，诊断信息写入 stderr。

`doctor` 默认在 `result.checks` 中输出需要处理的 missing/invalid/stale/busy/unsupported 项，并始终输出 .NET Framework Targeting Pack、解析后的 `Assembly-CSharp.dll` 与 `0Harmony.dll` 信息，方便 AI 直接了解真实编译引用；同时提供 `ready`、`issueCount` 和 `warningCount` 摘要。`doctor --verbose` 输出全部有序检查。它将 .NET Framework 4.8（推荐）和 4.7.2 都视为满足编译前提；仅发现 4.7.2 时会提示 AI 可能需要把 `source\MyCustomMod\MyCustomMod.csproj` 的目标改为 `v4.7.2`，但不会阻止后续操作。它还检查 Steam/RimWorld/Workshop、真实游戏与 Harmony DLL、Unity、Git、必需模组和既有工具状态，但不执行 build，也不检查 .NET 10 Runtime 或账号许可证。

发布目录本身就是可复制使用的 1.6 项目模板，不再提供 `init` 命令，也不在 `.tools` 中嵌套第二份模板。`mods install-tools` 会校验随包组件的版本与目录哈希，再将 LogSyncMod 和 RimSearcher DataMod 作为一个批次安装到 RimWorld `Mods`；可用 `--mods-path` 显式指定目标。

quick、auto、xml 和 `data refresh` 默认使用隔离桌面，manual 默认使用当前桌面。`--isolated-desktop` 与 `--no-isolated-desktop` 可覆盖单次运行。quick、auto、xml 会在游戏进程内临时设置 `AudioListener.volume=0`，完成前恢复，不修改 Windows 或玩家持久音量。

精选日志写入 `.tools/log/精选日志.log`，自动测试产物写入 `.tools/log/AutoTestOutput`。`retention.max_auto_test_runs` 控制保留的自动测试运行数，未配置时默认为 3。项目数据库默认建议配置为 `.tools/db/defs.db`。

## 回归测试

```powershell
dotnet build RimDevTools.Tests\RimDevTools.Tests.csproj -c Release
dotnet exec RimDevTools.Tests\bin\Release\net10.0\RimDevTools.Tests.dll
```

测试使用临时游戏目录，不会修改真实 RimWorld 安装或用户配置。
