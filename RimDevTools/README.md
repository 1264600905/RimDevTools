# RimDevTools

RimDevTools 是本仓库内的 .NET 10 Windows CLI，统一承载模组部署、参考模组处理和 RimWorld 测试准备/运行。

## 构建

```powershell
dotnet build RimDevTools\RimDevTools.csproj -c Release
dotnet publish RimDevTools\RimDevTools.csproj -c Release -r win-x64 --self-contained true
.\.tools\publish-rimdevtools.ps1
```

发布脚本默认生成隔离目录 `artifacts/RimDevTools-Package`，不会复制项目现有日志、自动测试结果或数据库：

```text
RimDevTools-Package/
├── mod_config.jsonc
└── .tools/
    ├── RimDevTools.exe
    ├── rimsearcher.exe
    ├── Data/
    │   ├── RimSearcher_DataMod/
    │   └── licenses/
    ├── Ilspy/
    ├── log/
    │   ├── 精选日志.log
    │   └── AutoTestOutput/
    ├── modlist/
    │   ├── defaultMods.xml
    │   └── deadmanswitch-core.xml
    ├── components/
    │   └── ProjectTemplate/
    │       ├── manifest.json
    │       └── 1.6/
    └── db/
```

`RimDevTools.exe` 和 `rimsearcher.exe` 都是 framework-dependent 单文件，不内置 .NET；使用机器必须安装 .NET 10 Runtime。DataMod 必须保留 RimWorld 模组目录结构，ILSpy 也保留独立工具目录。

## 命令

```powershell
RimDevTools.exe --version
RimDevTools.exe init 1.6 --output D:\RimWorldMods\NewMod
RimDevTools.exe doctor
RimDevTools.exe deploy --dry-run
RimDevTools.exe ref search <keyword>
RimDevTools.exe ref pull <query> --deps
RimDevTools.exe test settings
RimDevTools.exe test run --mode quick
RimDevTools.exe data refresh
RimDevTools.exe data status
```

项目命令从当前目录或直接父目录发现 `mod_config.jsonc`。正常结果写入 stdout 的单个 JSON 对象，诊断信息写入 stderr。

`init` 不需要已有项目。`--output` 是将直接包含 `mod_config.jsonc`、`1.6` 和 `.tools` 的项目根目录，不是配置文件、版本目录、工具目录或 `source` 目录。版本模板来自 `components/ProjectTemplate/<version>`，共享工具目录只复制一次到目标 `.tools` 并排除 `components`；目标必须不存在或为空。项目模板不嵌入 RimDevTools 程序集，修改组件文件无需改动 InitCommand。

quick、auto、xml 和 `data refresh` 默认使用隔离桌面，manual 默认使用当前桌面。`--isolated-desktop` 与 `--no-isolated-desktop` 可覆盖单次运行。quick、auto、xml 会在游戏进程内临时设置 `AudioListener.volume=0`，完成前恢复，不修改 Windows 或玩家持久音量。

精选日志写入 `.tools/log/精选日志.log`，自动测试产物写入 `.tools/log/AutoTestOutput`。`retention.max_auto_test_runs` 控制保留的自动测试运行数，未配置时默认为 3。项目数据库默认建议配置为 `.tools/db/defs.db`。

## 回归测试

```powershell
dotnet build RimDevTools.Tests\RimDevTools.Tests.csproj -c Release
dotnet exec RimDevTools.Tests\bin\Release\net10.0\RimDevTools.Tests.dll
```

测试使用临时游戏目录，不会修改真实 RimWorld 安装或用户配置。
