# RimDevTools

RimDevTools 是本仓库内的 .NET 10 Windows CLI，统一承载模组部署、参考模组处理和 RimWorld 测试准备/运行。

## 构建

```powershell
dotnet build RimDevTools\RimDevTools.csproj -c Release
dotnet publish RimDevTools\RimDevTools.csproj -c Release -r win-x64 --self-contained false
```

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

## 回归测试

```powershell
dotnet build RimDevTools.Tests\RimDevTools.Tests.csproj -c Release
dotnet exec RimDevTools.Tests\bin\Release\net10.0\RimDevTools.Tests.dll
```

测试使用临时游戏目录，不会修改真实 RimWorld 安装或用户配置。
