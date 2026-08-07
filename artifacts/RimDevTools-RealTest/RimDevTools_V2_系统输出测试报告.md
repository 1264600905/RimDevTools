# RimDevTools V2 系统输出测试报告

测试日期：2026-08-07  
测试目录：`artifacts/RimDevTools-RealTest`  
RimDevTools：`1.0.0.0`  
rimsearcher/DataMod：`3.1.3.0` / `3.1.3.0`

## 结论

| 范围 | 结果 | 说明 |
| --- | --- | --- |
| 主回归 | 通过 | `172/172`。 |
| 发布验证 | 通过 | `84/84`，含 1.6/1.7/1.8 init。 |
| RimSearcher 回归 | 通过 | `15/15`。 |
| ref name 目录 | 通过 | 联合重工输出到 `联合重工-UF_Heavy_Industries`。 |
| quick 默认隔离桌面 | 通过 | 60.9 秒，收到 ready signal。 |
| quick CLI 覆盖 | 通过 | 59.1 秒，`--no-isolated-desktop` 生效。 |
| data refresh | 通过 | 99.8 秒，数据库 343,728,128 字节，integrity `ok`。 |
| auto 静音生命周期 | 通过 | 静音覆盖截图过程，恢复发生在 `COMPLETED` 前一行。 |
| 失能机关本家全量截图 | 600 秒超时 | CLI 流程正确且持续推进；产生 354 张有效截图，但未形成完整 Result。 |
| 最终清理 | 通过 | 无 RimWorld/RimDevTools 进程、事务目录和导出请求残留。 |

本报告将“CLI 流程成功”“业务用例完整通过”“测试宿主是否提前终止”分开记录。本家全量截图没有被写成通过。

## 自动回归

执行：

```powershell
dotnet build RimDevTools.Tests\RimDevTools.Tests.csproj -c Release --no-restore
dotnet exec RimDevTools.Tests\bin\Release\net10.0\RimDevTools.Tests.dll
dotnet build RimDevTools.ReleaseTests\RimDevTools.ReleaseTests.csproj -c Release --no-restore
dotnet exec RimDevTools.ReleaseTests\bin\Release\net10.0\RimDevTools.ReleaseTests.dll artifacts\RimDevTools-Package
dotnet exec RimSearcher\Sources\RimSearcher.Cli.Tests\bin\Release\net10.0\RimSearcher.Cli.Tests.dll
```

结果：

- RimDevTools.Tests：`PASS 172 tests`。
- RimDevTools.ReleaseTests：`PASS 84 release tests`。
- RimSearcher.Cli.Tests：`PASS 15 tests`。
- TempProject、AutoTestProject、RimDevTools.Tests、ReleaseTests 构建成功。
- ReleaseTests 对三个 init 输出逐文件比对模板与共享工具，验证 doctor、版本、JSONC/XML/csproj/sln、空数据库和无历史日志。

## init

发布布局只保留一份共享工具：

```text
.tools/
├── RimDevTools.exe
├── rimsearcher.exe
├── Data/
├── Ilspy/
├── log/
├── modlist/
├── db/
└── components/ProjectTemplate/
    ├── manifest.json
    ├── 1.6/   # 不含 .tools
    ├── 1.7/   # 不含 .tools
    └── 1.8/   # 不含 .tools
```

`init` 原样复制对应版本模板，再从 exe 所在目录复制一份共享 `.tools`，并排除 `components`。只有 manifest 的 `requiredDirectories` 缺失时才创建目录；required file 缺失直接返回 `INIT_TEMPLATE_INVALID`。

发布测试实际执行三个版本，均满足：

- 输出文件严格等于版本模板文件加共享工具文件，内容逐字节一致。
- 项目内不存在 `.tools/components`。
- `doctor` 可发现项目，`.tools/RimDevTools.exe --version` 可运行且组件匹配。
- 非空目标与 1.5 在安装前失败，不修改目标。

## ref pull

命令：

```powershell
.\.tools\RimDevTools.exe ref pull KindSeal.LOL --deps --force
```

墙钟耗时：43.8 秒，退出码 0，runId：`eddf0b69d1f14283b0232df088ebdf2f`。

输出目录：

- `参考文档/Harmony`
- `参考文档/SRALib`
- `参考文档/联合重工-UF_Heavy_Industries`

联合重工 packageId 仍为 `KindSeal.LOL`，但目录明确按 About.xml 的 name 生成。

## quick

### 默认隔离桌面

```powershell
.\.tools\RimDevTools.exe test run --mode quick --timeout-seconds 120
```

外层等待上限 180 秒；墙钟耗时 60.9 秒；退出码 0。

```json
{
  "runId": "38a28d43853a42ffa6f52b2b8ad7c3a8",
  "mode": "quick",
  "isolatedDesktop": true,
  "isolatedDesktopSource": "project_config",
  "signal": "quick ready signal"
}
```

### 命令行关闭隔离桌面

```powershell
.\.tools\RimDevTools.exe test run --mode quick --no-isolated-desktop --timeout-seconds 120
```

外层等待上限 180 秒；墙钟耗时 59.1 秒；退出码 0。

结果为 `isolatedDesktop=false`、`isolatedDesktopSource=command_line`，仍收到 quick ready signal。

## data refresh

命令：

```powershell
.\.tools\RimDevTools.exe data refresh --timeout-seconds 180
```

CLI 内部上限 180 秒；外层等待上限 300 秒；墙钟耗时 99.8 秒；退出码 0。

```json
{
  "runId": "d03b59a2a9254c47a847ee60a7855cd3",
  "isolatedDesktop": true,
  "isolatedDesktopSource": "project_config",
  "databaseBytes": 343728128,
  "integrity": "ok",
  "versionMatches": true
}
```

Player.log 阶段顺序：

```text
request_consumed -> prepare -> enumerating -> records -> indexes -> metadata
-> commit -> atomic_replace -> result_write -> EXPORT_COMPLETE
```

`data status` 再次验证 integrity `ok`、userVersion `30103`、组件版本匹配、`refreshRecommended=false`。`rimsearcher get PassengerShuttle --type ThingDef` 成功，`WorkToBuild=36000`。

旧报告失败的根因是测试编排把 quick/settings/data 放在共享 120 秒外层调用中。本次 data 单独运行只需约 100 秒，证明不是 `DATA_REFRESH_TIMEOUT` 产品缺陷。

## 自动静音

修复发现：初版 quick ready timer 会在 auto 启动 10 秒后提前恢复音量。现已限定该 timer 只服务非 auto/xml，auto/xml 由 AutoTestRunner 在完成日志前恢复。

验证命令：

```powershell
.\.tools\RimDevTools.exe test run --mode auto `
  --target-package-id trigger.dms.mechweapons.autoequip `
  --capture-screenshots `
  --mods-file .\.tools\modlist\dms-full-screenshot.xml `
  --timeout-seconds 600
```

墙钟耗时 43.5 秒，退出码 0，runId：`0b9d6836c7cc4514a1cc7140fda7f0d7`。日志行序：

```text
43  Test audio muted. previousVolume=0.2862407
103 Screenshot requested ...0001...
119 Screenshot requested ...0002...
124 Test audio restored. volume=0.2862407
125 COMPLETED runId=0b9d... total=2 passed=2 failed=0
```

两张截图均非空，总计 3,449,080 字节；结果为 discovered/planned/executed `2/2/2`、failed `0`。

## 失能机关本家

目标解析：

- `Aoba.DeadManSwitch.Core` -> Workshop `3121742525`，name `The Dead Man's Switch`。
- `AOBA.Framework` -> Workshop `3498575851`。

正式命令：

```powershell
.\.tools\RimDevTools.exe test run --mode auto `
  --target-package-id aoba.deadmanswitch.core `
  --capture-screenshots `
  --mods-file .\.tools\modlist\deadmanswitch-core.xml `
  --timeout-seconds 600
```

实际 ModList：Harmony、Core、Biotech、AOBA.Framework、Aoba.DeadManSwitch.Core、trigger.logsyncmod。隔离桌面默认开启。

结果：CLI 内部 600 秒超时，墙钟 611.4 秒，退出码 6，runId：`bb228c51c8d445caad3baca8786ddfb0`。

```json
{
  "ok": false,
  "error": {
    "code": "TEST_TIMEOUT",
    "message": "auto 模式等待超时"
  }
}
```

这不是信号吞失或静止卡死：Player.log 一直推进，超时时刚完成 `DMS_DeposableRocketLauncher` 并开始打开 `DMS_DoorBreaker`。部分产物统计：

- 截图 354 张，总计 564,542,549 字节。
- 零字节文件 0。
- 354/354 均可解码，尺寸全部为 1920x1080。
- 因未到 Finish，没有本轮完整 Manifest/Result/Report，不能声称业务用例全部通过。

结论：CLI 的目标模组、截图开关、隔离桌面、超时和清理路径正确；本家完整截图规模超过本轮规定的 600 秒预算。该业务结果如实保留。

## 清理审计

全部真实运行结束后检查：

- 无 `RimWorldWin64` 或 `RimDevTools` 残留进程。
- `RimDevToolsTransactions` 无待恢复目录。
- 无 `RimDevTools.ExportRequest.json` 或 processing request。
- data refresh 无 `.tmp` 残留，数据库可正常查询。
- 超时与成功路径均恢复 ModsConfig/LogSync 设置事务。
