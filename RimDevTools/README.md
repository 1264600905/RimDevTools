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
    │   └── defaultMods.xml
    └── db/
```

`RimDevTools.exe` 和 `rimsearcher.exe` 都是 framework-dependent 单文件，不内置 .NET；使用机器必须安装 .NET 10 Runtime。DataMod 必须保留 RimWorld 模组目录结构，ILSpy 也保留独立工具目录。

## 命令

```powershell
RimDevTools.exe --version
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

精选日志写入 `.tools/log/精选日志.log`，自动测试产物写入 `.tools/log/AutoTestOutput`。`retention.max_auto_test_runs` 控制保留的自动测试运行数，未配置时默认为 3。项目数据库默认建议配置为 `.tools/db/defs.db`。

## 回归测试

```powershell
dotnet build RimDevTools.Tests\RimDevTools.Tests.csproj -c Release
dotnet exec RimDevTools.Tests\bin\Release\net10.0\RimDevTools.Tests.dll
```

测试使用临时游戏目录，不会修改真实 RimWorld 安装或用户配置。
