# Goal 1 完成报告：执行内核与可观测性基础设施

状态：已完成

实施日期：2026-08-10

对应策划案：[Goal1-执行内核与可观测性基础设施.md](./Goal1-执行内核与可观测性基础设施.md)

## 1. 完成范围

本 Goal 已完成执行内核、非阻塞等待、结构化 Journal、日志关联、Evidence 双写和一个真实 InfoCard 垂直切片。旧 `AutoTestResult`、`*_Result.json`、`*_Report.md`、`*_Manifest.json` 及 `COMPLETED` 完成信号继续保留。

本次没有迁移所有既有 Session，也没有接入 RimBridgeServer SDK、场景语言或第三方运行时 DLL，符合 Goal 1 的非目标约束。

## 2. 新增稳定接口

纯逻辑层位于 `source/AutoTestProject/Contracts` 和 `source/AutoTestProject/Core`，不引用 Verse、RimWorld、UnityEngine 或 Harmony。

```text
AutoTestOperationRunner.Run(string capabilityId, string caseId, string stepId, Action)
AutoTestOperationRunner.Run<T>(string capabilityId, string caseId, string stepId, Func<T>, Func<T, string>)
AutoTestOperationContext.Current
AutoTestOperationContext.PushRun/PushCase/PushStep/PushOperation
AutoTestWaitHandle.Poll(int currentGameTick)
AutoTestOperationJournal.RecordStarted/RecordTerminal/GetOperation/GetRecentOperations/GetEvents
AutoTestLogJournal.Record/GetEntries
AutoTestEvidenceBuilder.Pass/Fail/AreEqual/IsTrue/OperationSucceeded/AddCapture/AddError/Complete
AutoTestEvidenceManifest
AutoTestAssertionEvidence
AutoTestCaptureEvidence
AutoTestRuntimeObservation.Capture(string packageId, Map map)
```

Wait Handle 自身也会创建 Operation envelope，因此等待步骤拥有 `operationId`、开始时间、结束时间、duration、终态及 `wait.satisfied`/`wait.timed_out` 等生命周期事件。

## 3. 游戏运行时接入

- `LogPatches` 增加只读 `LogCaptured` 事件，带 UTC 时间、级别、文本和来源 packageId，并带递归保护及订阅器隔离。
- `AutoTestLogCaptureSubscription` 在 run 开始时订阅、结束时取消，并从 `AutoTestOperationContext` 填充 run/case/step/operation 关联。
- `AutoTestRunner.Initialize/Finish` 创建 Evidence Manifest，发布 `run.started`/`run.completed`，并双写旧报告与 Evidence。
- `AutoTestReportWriter` 新增 `*_Evidence.json`，并向旧 Result 增加 `evidenceSchemaVersion`、`evidencePath`、operation/assertion/log 统计字段。
- `AutoResultValidator` 支持旧结果和 Evidence 结果；Evidence 缺失、损坏、schema 不支持或 run/package 不匹配时分别返回稳定错误码。
- `InfoCardOperationAdapter` 已接入 Building/Item InfoCard 路径。

### InfoCard 垂直切片步骤

```text
case.started
spawn_or_prepare_target       Operation
wait_pre_open_settle          Wait + Operation
open_info_card                Operation
wait_info_card_open           Wait + RequiredStablePolls=3
capture_info_card             Operation + CaptureEvidence
request_close                 Operation
force_close_fallback          Operation（必要时）
wait_info_card_closed         Wait + Operation
case.completed / case.failed
```

每个 Case 还会记录事件和日志游标，仅将游标之后且属于该 Case 的 Operations/Events/Logs 写入 Case Evidence 窗口。

## 4. 测试与验证矩阵

| 验证项 | 结果 |
| --- | --- |
| `dotnet run --project AutoTest.Core.Tests\AutoTest.Core.Tests.csproj -c Debug` | 27 项通过 |
| `dotnet build source\TempProject\TempProject.csproj -c Debug` | 成功 |
| `dotnet build source\AutoTestProject\AutoTestProject.csproj -c Debug` | 成功；仅保留 2 个既有 CS0649 警告 |
| `dotnet build RimDevTools.Tests\RimDevTools.Tests.csproj -c Debug` | 成功 |
| `dotnet RimDevTools.Tests\bin\Debug\net10.0\RimDevTools.Tests.dll` | 274 项通过 |
| 真实 `test run --mode quick` | 通过，完成信号正常 |
| 真实 `test run --mode auto --scope content --defname LogSync_AutoTestPowerGenerator --capture-screenshots` | 通过 |
| 真实 `test run --mode xml --scope content --capture-screenshots` | 通过，完成信号正常；当前 XML 驱动清单为 0 个计划用例 |
| 标准 `.tools\deploy.ps1` 部署 | 成功 |
| 工作区与实际 Mods 目录 DLL SHA256 | `LogSyncMod.dll` 与 `LogSyncMod.AutoTest.dll` 均一致 |
| Core/Contracts 引用检查 | 无 Verse/RimWorld/UnityEngine/Harmony 引用 |
| AutoTest 主线程阻塞检查 | 无 `Thread.Sleep` |

## 5. 最终真实运行证据

最终真实 auto run：`6587fc68e59b4a018175e63fe211c559`

- RimWorld：`1.6.4871`
- packageId：`trigger.logsyncmod`
- Case：`trigger.logsyncmod|BuildingInfoCard|LogSync_AutoTestPowerGenerator`
- Operations：8
- Assertions：8，失败 0
- Errors：0
- Severe logs：0
- Case Operations：8
- Case Events：24
- Case Logs：12
- Evidence schema：1

产物：

- [Result.json](../../.tools/log/AutoTestOutput/trigger.logsyncmod_6587fc68e59b4a018175e63fe211c559_Result.json)
- [Evidence.json](../../.tools/log/AutoTestOutput/trigger.logsyncmod_6587fc68e59b4a018175e63fe211c559_Evidence.json)
- [Report.md](../../.tools/log/AutoTestOutput/trigger.logsyncmod_6587fc68e59b4a018175e63fe211c559_Report.md)
- [InfoCard 截图](../../.tools/log/AutoTestOutput/trigger.logsyncmod_6587fc68e59b4a018175e63fe211c559_Screenshots/UI/BuildingInfoCard/0001_LogSync_AutoTestPowerGenerator.png)

## 6. 发布与兼容性结论

- `LogSyncMod.dll` 和 `LogSyncMod.AutoTest.dll` 已部署至配置中的 `H:\SteamLibrary\steamapps\common\RimWorld\Mods\LogSyncMod`。
- 已生成 `artifacts/RimDevTools-Package`，包含最新 CLI 和 LogSyncMod 工具组件。
- 未安装 RimBridgeServer 时现有测试仍可运行；本 Goal 没有新增运行时第三方依赖。
- 旧 quick/auto/xml 命令、旧 Result 核心字段和旧完成信号保持兼容。

