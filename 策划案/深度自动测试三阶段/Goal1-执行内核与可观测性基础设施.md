# Goal 1：执行内核、结构化证据与运行期可观测性基础设施

## 给实施 AI 的任务说明

这是一份可以直接实施的改造任务，不是只供讨论的设计稿。

实施时必须完成代码、自动化测试、构建验证和兼容性回归。不要只新增接口而不接入真实测试流程，也不要在本 Goal 中重写所有现有测试模块。

本 Goal 完成后，现有 `quick/auto/xml` 命令和现有报告必须继续工作；至少选择一个现有 InfoCard 用例作为垂直切片，完整接入新的 Operation、Wait、Journal 和 Evidence 基础设施。

后续两个 Goal 依赖本文建立的类型和边界，因此不要随意改名或把职责重新塞回 `AutoTestRunner`。

## 一、为什么这是第一优先级

当前 AutoTest 已经能运行大量真实游戏测试，但新的深度测试仍然主要依赖：

- 每类测试自己的 Phase 枚举。
- `TicksGame`、`RemainingUiFrames` 和硬编码倒计时。
- 在各个 Session 内直接修改 `AutoTestResult.Status/Stage/ErrorMessage`。
- 最终依靠 Player.log 的 `COMPLETED` 字符串通知外部 CLI。
- 失败后再从整份 Player.log 和截图中人工推断原因。

相关现状：

- `source/AutoTestProject/AutoTestRunner.cs` 同时持有普通队列、炮塔、远程、近战、技能、三视图和 CombatPawn Session。
- `source/AutoTestProject/AutoTestRunner.cs` 的 `Tick()` 统一驱动所有 Session。
- `source/AutoTestProject/AutoTestModels.cs` 的结果模型主要是 `Status + Stage + Notes`。
- `RimDevTools/Commands/TestCommand.cs` 每 500ms 扫描 Player.log，收到完成信号后才读取最终 JSON。
- `RimDevTools.Tests` 只引用 `RimDevTools`，没有直接测试 AutoTest 的执行逻辑。

如果直接在这个基础上继续增加保存加载、Gizmo、Hediff 生命周期、通知、性能等测试，每个新模块都会再次实现超时、等待、错误捕获、截图和清理。

本 Goal 的收益是：先把所有后续深度测试都需要的公共执行能力建立起来。

## 二、参考实现与明确借鉴点

参考项目：`参考文档/RimBridgeServer-main`

重点参考以下内容：

1. `docs/architecture.md`
   - Main-thread ownership。
   - Explicit async instead of hidden waiting。
   - Operation envelope。
   - Wait conditions。
   - Thin adapters, thick core。
2. `Tests/RimBridgeServer.Core.Tests/OperationRunnerTests.cs`
   - 异常、超时、取消统一转换为操作结果，而不是散落的 catch。
3. `Tests/RimBridgeServer.Core.Tests/ConditionWaiterTests.cs`
   - 超时时保留最后一次 Snapshot、尝试次数和最后探针错误。
4. `Tests/RimBridgeServer.Core.Tests/OperationJournalTests.cs`
   - 操作状态变化发布带序号的生命周期事件。
5. `Tests/RimBridgeServer.Core.Tests/LogJournalTests.cs`
   - 日志折叠、游标查询和 operation 关联。
6. `Tests/RimBridgeServer.Sdk.Tests/RimBridgeEvidenceTests.cs`
   - assertions、captures、errors、logs 和 environment 使用稳定证据结构。

不要直接照搬它的阻塞式 `ConditionWaiter.WaitUntil()` 到 RimWorld 主线程。我们的独立 AutoTest 由 `Tick()` 和 `OnGUI/Repaint` 驱动，必须实现为非阻塞轮询状态机。

### 2.1 参考目录实际包含什么

后续 AI 必须先注意：当前复制到仓库中的 `参考文档/RimBridgeServer-main` **没有主实现的 `Source/` 目录**。不能搜索一个不存在的 `Source/RimBridgeServer.Core/OperationRunner.cs`，然后得出“参考代码缺失、无法实施”的结论。

当前可用证据分三层：

```text
设计说明：参考文档/RimBridgeServer-main/docs/*.md
行为测试：参考文档/RimBridgeServer-main/Tests/**/*.cs
实际编译类型：参考文档/RimBridgeServer-main/1.6/Assemblies/*.dll
```

其中编译 DLL 已验证存在以下真实类型：

```text
RimBridgeServer.Contracts.OperationEnvelope
RimBridgeServer.Contracts.OperationError
RimBridgeServer.Contracts.OperationStatus
RimBridgeServer.Core.OperationRunner
RimBridgeServer.Core.OperationExecutionOptions
RimBridgeServer.Core.ConditionWaiter
RimBridgeServer.Core.WaitOptions
RimBridgeServer.Core.WaitOutcome
RimBridgeServer.Core.WaitProbeResult
RimBridgeServer.Core.OperationContext
RimBridgeServer.Core.OperationJournal
RimBridgeServer.Core.OperationEventRecord
RimBridgeServer.Core.LogJournal
RimBridgeServer.Core.IGameThreadDispatcher
RimBridgeServer.Sdk.RimBridgeEvidence
RimBridgeServer.Sdk.RimBridgeEvidenceManifest
RimBridgeServer.Sdk.RimBridgeEvidenceAssertion
RimBridgeServer.Sdk.RimBridgeEvidenceCapture
```

如果实施 AI 需要确认签名，可以用反射或仓库配置的 ILSpy 查看上述 DLL；不要反编译后整段复制。这里借鉴的是边界和行为合同。

### 2.2 精确参考矩阵

| 参考位置 | 要阅读的具体符号 | 参考行为 | 在本项目中的实现落点 |
| --- | --- | --- | --- |
| `docs/architecture.md` 的 `Main-thread ownership` | 主线程、frame-bound、long-event-bound 的所有权说明 | 所有 RimWorld 状态访问经过统一执行边界 | `Game/AutoTestRuntimeObservation.cs` 和现有 `AutoTestBootstrap.Tick/OnGui`；Core 不访问游戏对象 |
| `docs/architecture.md` 的 `Operation envelope` | 示例 JSON 中 operationId/status/duration/result/warnings/events | 每个步骤都有同一种终态和关联 ID | `Contracts/AutoTestOperationContracts.cs` |
| `OperationRunnerTests.UsesDispatcherWhenMainThreadMarshallingIsEnabled` | `OperationRunner.Run(... OperationExecutionOptions)` | 是否需要主线程由执行选项决定 | 我们第一阶段不跨线程 dispatch，但保留统一 Runner 边界，为 Goal 3 异步调用预留 |
| `OperationRunnerTests.ConvertsExceptionsIntoFailedOperationEnvelopes` | `InvalidOperationException("boom")` 转 Failed + code/message | 业务调用者不自行拼异常状态 | `Core/AutoTestOperationRunner.cs` |
| `OperationRunnerTests.ConvertsTimeoutExceptionsIntoTimedOutOperationEnvelopes` | `TimeoutException` 转 TimedOut | timeout 与普通 failure 区分 | `AutoTestOperationStatus.TimedOut` 和 `operation.timed_out` |
| `OperationRunnerTests.ConvertsOperationCanceledExceptionsIntoCancelledOperationEnvelopes` | `OperationCanceledException` 转 Cancelled | cancellation 与 failure 区分 | `AutoTestOperationStatus.Cancelled` 和安全清理流程 |
| DLL `RimBridgeServer.Core.OperationRunner.Run/RunAsync` | `Run(Func<object>, OperationExecutionOptions)`、`RunAsync(...)` | 同步和异步使用相同 Envelope | 本 Goal 先实现同步动作 + 非阻塞 Wait；Goal 3 再从外部 async 等待 |
| `ConditionWaiterTests.ReturnsSuccessWhenProbeEventuallySatisfiesCondition` | 第三次 probe ready、Attempts=3、Snapshot=3 | Wait 返回尝试次数和最后快照 | `Core/AutoTestWaitHandle.cs` |
| `ConditionWaiterTests.ReturnsTimeoutWithLastSnapshotWhenConditionNeverSatisfies` | timeout 后仍保留最后 Snapshot | 超时报告不是只有一句“等待超时” | `AutoTestWaitOutcome.LastSnapshot` |
| `ConditionWaiterTests.CanRetryAfterHandledProbeExceptions` | probe 暂时抛 `TimeoutException` 后继续 | 探针短暂失败不一定终止整个 Case | `ProbeFailureCount/LastProbeError` |
| DLL `RimBridgeServer.Core.ConditionWaiter.WaitUntil` | `WaitUntil(Func<WaitProbeResult>, WaitOptions, CancellationToken)` | WaitOptions 集中管理 timeout/poll | 我们改造成 Tick 驱动的 `Poll()`，不能照搬其内部 Sleep |
| `OperationJournalTests.PublishesLifecycleEventsWhenOperationsChangeState` | `RecordStarted` 和 `RecordCompleted` | 状态变化同时进入 Event Journal | `Core/AutoTestOperationJournal.cs` |
| `OperationJournalTests.GetOperationRetainsBoundedResultSnapshots` | 单项查询可带 result，列表默认不展开 | 控制报告大小 | operation 列表只写摘要，Evidence 可按 operationId 查详细项 |
| `OperationJournalTests.FiltersEventsBySequenceAndOperationId` | `afterSequence + operationId` | 场景只读取自己开始后的事件 | `GetEvents(afterSequence, ...)` |
| `LogJournalTests.CollapsesRepeatedEntriesIntoASingleRow` | RepeatCount=2 | 高频相同日志折叠 | `Core/AutoTestLogJournal.cs` |
| `LogJournalTests.CapturesAndFiltersOperationCorrelation` | root operation、child operation、script step 关联 | 日志应知道属于哪个 run/case/step | `AutoTestOperationContext` + `AutoTestLogCaptureSubscription` |
| DLL `RimBridgeServer.Core.LogJournal.Record/GetEntries` | `Record(...)`、`GetEntries(limit, minimumLevel, afterSequence, operationId, rootOperationId, capabilityId)` | 日志查询维度明确 | 我们至少实现 level/afterSequence/runId/caseId/operationId |
| `RimBridgeEvidenceTests.CreateManifestInitializesStableEvidenceShape` | manifest 初始化所有集合且 success 初始为 false | 避免 null collection 和半成品成功 | `AutoTestEvidenceBuilder.CreateManifest` |
| `RimBridgeEvidenceTests.AssertionHelpersReportPassAndFail` | expected/actual 保留 | 断言必须能解释失败 | `AutoTestAssertionEvidence` |
| `RimBridgeEvidenceTests.CompleteMarksManifestFromAssertionsAndErrors` | assertion 全通过且 errors 为空才成功 | success 由证据推导 | `AutoTestEvidenceBuilder.Complete` |
| DLL `RimBridgeServer.Sdk.RimBridgeEvidence` | `CreateManifest/Pass/Fail/IsTrue/AreEqual/ToolSucceeded/Complete` | 小而稳定的 Evidence helper API | 我们实现同职责、不同命名空间的独立版本，不引用 SDK |

### 2.3 参考行为到现有代码的第一批替换点

后续 AI 应从以下现有方法入手，而不是漫无目的全局重构：

| 当前代码 | 当前职责 | 第一阶段替换方式 |
| --- | --- | --- |
| `AutoTestRunner.Initialize()` | 创建 run、发现目标、开始运行 | 创建 Evidence Manifest，发布 `run.started`，订阅 Log Journal |
| `AutoTestRunner.Tick()` | 驱动 Session | 增加统一的 operation/wait poll 入口；旧 Session 暂时保留 |
| `AutoTestRunner.Finish()` | 写报告和完成日志 | Complete Evidence、取消日志订阅、发布 `run.completed` 后再写旧报告 |
| `AutoTestRunner.InfoCardModule.ProcessWaitingForInfoCardOpen()` | 自行计数稳定 Repaint | 用 `AutoTestWaitHandle` 表达 `window.info_card_open` |
| `AutoTestRunner.InfoCardModule.ProcessWaitingForInfoCardClose()` | 自行计数关闭超时 | 用 `AutoTestWaitHandle` 表达 `window.info_card_closed` |
| `AutoTestScreenshotService.TryCapture()` | 截图和路径回填 | 外层用 OperationRunner 包裹，成功后登记 CaptureEvidence |
| `AutoTestReportWriter.Write()` | 旧 Markdown/JSON | 保留旧输出并新增 `*_Evidence.json` |
| `RimDevTools.AutoResultValidator.Validate()` | 验证最终 Result.json | 如声明 Evidence，则验证其存在、run/package/schema 匹配 |
| `LogPatches.MessagePrefix/WarningPrefix/ErrorPrefix` | 已经统一拦截 Verse.Log | 在这里发布只读事件，不再额外 Harmony Patch 同样方法 |

## 三、目标和非目标

### 目标

1. 为每个测试运行、测试用例和测试步骤建立稳定 ID。
2. 每个步骤返回统一的 Operation Envelope。
3. 建立非阻塞 Wait Handle，统一记录超时、最后状态和探针异常。
4. 建立有界 Operation Journal、Event Journal 和 Log Journal。
5. 为报告增加结构化 Evidence Manifest。
6. 将日志与当前 run/case/step/operation 关联。
7. 建立可在 net10 下直接单元测试的纯逻辑层。
8. 让一个真实 InfoCard 用例完整使用新内核跑通。

### 非目标

1. 本 Goal 不把所有 Session 全部迁移到新执行器。
2. 本 Goal 不接入 RimBridgeServer SDK；那属于 Goal 3。
3. 本 Goal 不实现通用 JSON/Lua 场景语言。
4. 本 Goal 不改变现有测试分类和发现清单语义。
5. 本 Goal 不删除旧 `AutoTestResult` 字段和旧报告字段。
6. 本 Goal 不引入 Newtonsoft.Json、MoonSharp 或其他运行时第三方 DLL。

## 四、总体目录设计

继续只发布 `LogSyncMod.AutoTest.dll`，不要在第一阶段增加多个运行时 DLL。

在现有项目中增加分层目录：

```text
source/AutoTestProject/
  Contracts/
    AutoTestOperationContracts.cs
    AutoTestEvidenceContracts.cs
    AutoTestJournalContracts.cs
    AutoTestWaitContracts.cs
  Core/
    AutoTestOperationContext.cs
    AutoTestOperationRunner.cs
    AutoTestOperationJournal.cs
    AutoTestLogJournal.cs
    AutoTestWaitHandle.cs
    AutoTestEvidenceBuilder.cs
    AutoTestBoundedBuffer.cs
  Game/
    AutoTestGameClock.cs
    AutoTestRuntimeObservation.cs
    AutoTestLogCaptureSubscription.cs
  Integration/
    LegacyResultEvidenceAdapter.cs
    InfoCardOperationAdapter.cs

AutoTest.Core.Tests/
  AutoTest.Core.Tests.csproj
  OperationRunnerTests.cs
  WaitHandleTests.cs
  OperationJournalTests.cs
  LogJournalTests.cs
  EvidenceBuilderTests.cs
```

`AutoTestProject.csproj` 已使用 `Compile Include="**\*.cs"`，新目录会自动进入原程序集。

`AutoTest.Core.Tests.csproj` 使用 net10.0 + xUnit，并以 Link 方式编译 `Contracts/**/*.cs` 和 `Core/**/*.cs`。纯 Core 文件禁止引用 `Verse`、`RimWorld`、`UnityEngine` 或 Harmony；Game 和 Integration 文件不链接进测试项目。

建议测试项目包版本与参考项目保持一致：

```xml
<PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.7.0" />
<PackageReference Include="xunit" Version="2.9.3" />
<PackageReference Include="xunit.runner.visualstudio" Version="3.1.5" PrivateAssets="all" />
```

如果当前环境无法恢复 NuGet，允许改成仓库现有的 console-test 风格，但测试用例和覆盖范围不能省略。

## 五、必须落地的 Contracts

所有 DTO 使用普通属性，不要直接保存 RimWorld 对象。

### 5.1 操作状态和错误

```csharp
internal enum AutoTestOperationStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    TimedOut,
    Cancelled
}

internal sealed class AutoTestOperationError
{
    public string Code { get; set; }
    public string Message { get; set; }
    public string ExceptionType { get; set; }
    public string StackTrace { get; set; }
}
```

错误码必须稳定，不要把整句自然语言当错误码。首批至少定义：

```text
operation.failed
operation.timed_out
operation.cancelled
wait.probe_failed
wait.timed_out
runtime.not_ready
runtime.map_changed
runtime.target_missing
```

### 5.2 Operation Envelope

```csharp
internal sealed class AutoTestOperationEnvelope
{
    public string OperationId { get; set; }
    public string RunId { get; set; }
    public string CaseId { get; set; }
    public string StepId { get; set; }
    public string CapabilityId { get; set; }
    public AutoTestOperationStatus Status { get; set; }
    public bool Success { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public long DurationMs { get; set; }
    public string ResultSummary { get; set; }
    public AutoTestOperationError Error { get; set; }
    public List<string> Warnings { get; set; }
}
```

暂时使用 `ResultSummary` 和 Evidence 中的结构化字段，不在第一阶段把任意 RimWorld 对象塞进 `object Result`，避免报告序列化失控。

### 5.3 断言和证据

```csharp
internal sealed class AutoTestAssertionEvidence
{
    public string AssertionId { get; set; }
    public string Name { get; set; }
    public bool Success { get; set; }
    public string Expected { get; set; }
    public string Actual { get; set; }
    public string Details { get; set; }
    public string OperationId { get; set; }
}

internal sealed class AutoTestCaptureEvidence
{
    public string CaptureId { get; set; }
    public string Label { get; set; }
    public string Kind { get; set; }
    public string Path { get; set; }
    public string OperationId { get; set; }
    public string Details { get; set; }
}

internal sealed class AutoTestEvidenceManifest
{
    public int SchemaVersion { get; set; }
    public string RunId { get; set; }
    public string PackageId { get; set; }
    public string Suite { get; set; }
    public bool Success { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public AutoTestEnvironmentEvidence Environment { get; set; }
    public List<AutoTestOperationEnvelope> Operations { get; set; }
    public List<AutoTestAssertionEvidence> Assertions { get; set; }
    public List<AutoTestCaptureEvidence> Captures { get; set; }
    public List<AutoTestLogEvidence> Logs { get; set; }
    public List<AutoTestEventEvidence> Events { get; set; }
    public List<AutoTestOperationError> Errors { get; set; }
}
```

Environment 至少包含：

- RimWorld 版本。
- AutoTest 程序集版本。
- LogSyncMod 程序集版本。
- 当前 packageId。
- 当前地图 ID/名称。
- 当前语言。
- DevMode。
- 当前速度和暂停状态。
- 激活 Mod packageId 列表的稳定摘要或哈希。

### 5.4 Journal 记录

所有 Journal 条目使用单调递增 `long Sequence`。游标语义统一为“返回 `Sequence > afterSequence` 的记录”。

```csharp
internal sealed class AutoTestEventEvidence
{
    public long Sequence { get; set; }
    public DateTime TimestampUtc { get; set; }
    public string EventType { get; set; }
    public string RunId { get; set; }
    public string CaseId { get; set; }
    public string StepId { get; set; }
    public string OperationId { get; set; }
    public string CapabilityId { get; set; }
    public string Message { get; set; }
}
```

事件类型首批固定为：

```text
operation.started
operation.completed
operation.failed
operation.timed_out
operation.cancelled
wait.started
wait.satisfied
wait.timed_out
wait.probe_failed
case.started
case.completed
case.failed
run.started
run.completed
```

## 六、Operation Context 与 Runner 的具体实现

### 6.1 上下文

新增 `AutoTestOperationContext`，内部使用 `AsyncLocal` 或等价的可嵌套作用域实现：

```csharp
using (AutoTestOperationContext.PushRun(runId))
using (AutoTestOperationContext.PushCase(caseId))
using (AutoTestOperationContext.PushStep(stepId, capabilityId))
{
    // 当前日志订阅器可读取 run/case/step/operation 关联信息
}
```

要求：

1. 嵌套调用结束后恢复上层值。
2. 异常退出也必须恢复。
3. Snapshot 是不可变 DTO。
4. 单元测试覆盖嵌套恢复。

### 6.2 Operation Runner

新增 `AutoTestOperationRunner`，由它负责：

1. 创建 `op_<guid>`。
2. 写入 `operation.started`。
3. 推入 OperationContext。
4. 调用真实动作。
5. 把异常转换成统一错误。
6. 写入终态和持续时间。
7. 更新 Operation Journal 和 Event Journal。

同步入口：

```csharp
AutoTestOperationEnvelope Run(
    string capabilityId,
    string caseId,
    string stepId,
    Action action);

AutoTestOperationEnvelope Run<T>(
    string capabilityId,
    string caseId,
    string stepId,
    Func<T> action,
    Func<T, string> summarize);
```

禁止调用方在 catch 中重新拼另一套 status。调用方只负责添加领域断言或选择是否继续。

### 6.3 时钟抽象

Core 中定义：

```csharp
internal interface IAutoTestClock
{
    DateTime UtcNow { get; }
    long RealtimeMilliseconds { get; }
}
```

Game 层的 `AutoTestGameClock` 使用 `DateTime.UtcNow` 和 Unity realtime；测试使用 FakeClock。不要直接依赖 `Thread.Sleep` 验证超时。

## 七、非阻塞 Wait Handle

### 7.1 设计约束

RimWorld 主线程内禁止实现：

```csharp
while (!condition()) Thread.Sleep(...);
```

也禁止在 `OnGUI` 中阻塞等待下一帧。

### 7.2 合同

```csharp
internal sealed class AutoTestProbeResult
{
    public bool Satisfied { get; set; }
    public string Message { get; set; }
    public string Snapshot { get; set; }
}

internal sealed class AutoTestWaitOptions
{
    public string ConditionId { get; set; }
    public long TimeoutMs { get; set; }
    public int MinimumPollIntervalTicks { get; set; }
    public int RequiredStablePolls { get; set; }
}

internal sealed class AutoTestWaitOutcome
{
    public bool Terminal { get; set; }
    public bool Satisfied { get; set; }
    public bool TimedOut { get; set; }
    public int Attempts { get; set; }
    public int StablePolls { get; set; }
    public int ProbeFailureCount { get; set; }
    public string Message { get; set; }
    public string LastSnapshot { get; set; }
    public string LastProbeError { get; set; }
}
```

`AutoTestWaitHandle.Poll(currentGameTick)` 每次最多调用一次 Probe，并返回 Pending/Satisfied/TimedOut。支持连续 N 次满足才算稳定，替代 InfoCard 现有的 `OpenStableRepaintFrames`。

必须测试：

- 第三次 Poll 满足。
- 永不满足时超时并保留最后 Snapshot。
- Probe 第一次抛异常、后续恢复。
- 连续稳定次数不足时不能提前成功。
- tick 间隔未到时不能重复调用 Probe。

## 八、Operation、Event、Log Journal

### 8.1 有界缓冲区

新增泛型 `AutoTestBoundedBuffer<T>`：

- 默认 Operation 500 条。
- Event 2000 条。
- Log 2000 条。
- 超出容量移除最旧条目。
- 查询时返回复制快照，不能把内部 List 暴露给调用者。

### 8.2 Operation Journal

API：

```csharp
RecordStarted(...)
RecordTerminal(AutoTestOperationEnvelope envelope)
GetOperation(string operationId)
GetRecentOperations(int limit, bool includeDetails)
GetEvents(long afterSequence, int limit, string operationId = null)
```

同一 operation 只能从 Pending/Running 进入一个终态。重复完成必须拒绝或忽略，并生成诊断 warning。

### 8.3 Log Journal 与现有 LogSync 衔接

不要再为 AutoTest 额外 Patch 一遍 `Verse.Log.Message/Warning/Error`。

修改 `source/TempProject/HarmonyPatches.cs`：

1. 为 `LogPatches` 增加公共只读事件，例如：

```csharp
public static event Action<LogSyncCapturedEntry> LogCaptured;
```

2. `LogSyncCapturedEntry` 放在 `LogSyncMod.dll` 中，包含：
   - UTC 时间。
   - level。
   - text。
   - 可选 source packageId。
3. 在现有 Prefix 捕获日志后发布事件。
4. 发布事件必须有递归保护；订阅器处理日志时不得再次通过 `Verse.Log` 记录。
5. 单个订阅器异常不能破坏游戏日志和原有文件同步。

`LogSyncMod.AutoTest.dll` 中的 `AutoTestLogCaptureSubscription` 在测试运行开始订阅、结束时取消订阅，并从 `AutoTestOperationContext.Current` 填充 run/case/step/operation 关联。

Log Journal 需要：

- level 归一化为 info/warning/error/fatal。
- 5 秒窗口内相同 level + message 折叠，记录 `RepeatCount`。
- `GetEntries(afterSequence, minimumLevel, runId, caseId, operationId)`。
- 不要无限保存完整 StackTrace；设置最大字符数，例如 16KB。

本 Goal 只收集 severe log 并输出证据，不默认改变所有旧用例的通过/失败。Goal 2 再引入可配置的“新 Error/Fatal 自动失败”策略，避免第一轮迁移造成大量未知误报。

## 九、Evidence Builder 和报告兼容

### 9.1 Evidence Builder

新增帮助方法：

```csharp
Pass(name, actual, details = null)
Fail(name, expected, actual, details = null)
AreEqual(name, expected, actual)
IsTrue(name, actual, details = null)
OperationSucceeded(name, envelope)
AddCapture(...)
AddError(...)
Complete(manifest)
```

`Complete()` 的成功规则：

```text
所有 Assertions 成功
AND Errors 为空
AND 所有必须操作处于 Completed
```

### 9.2 输出文件

保留现有：

```text
*_Manifest.json
*_Result.json
*_Report.md
*_Screenshots/
```

新增：

```text
*_Evidence.json
```

第一阶段不要立刻把旧 Result.json 改成完全不同的 schema。只允许向旧 JSON 增加以下兼容字段：

```json
{
  "evidenceSchemaVersion": 1,
  "evidencePath": "..._Evidence.json",
  "operationCount": 12,
  "assertionCount": 4,
  "failedAssertionCount": 0,
  "capturedLogCount": 8,
  "severeLogCount": 0
}
```

Evidence JSON 建议使用 `DataContractJsonSerializer`，在 `AutoTestProject.csproj` 增加 `System.Runtime.Serialization` 引用。DTO 使用 DataContract/DataMember 或验证过的公共属性序列化方式。不要新增 Newtonsoft.Json 运行时副本。

### 9.3 RimDevTools Validator

修改 `RimDevTools/Commands/TestCommand.cs` 中的 `AutoResultValidator`：

1. 旧结果没有 Evidence 字段时仍按旧规则校验。
2. 新结果声明了 Evidence 路径时，必须验证文件存在、runId/packageId 匹配、schemaVersion 支持。
3. Evidence 解析失败返回稳定错误码：
   - `AUTO_EVIDENCE_MISSING`
   - `AUTO_EVIDENCE_INVALID`
   - `AUTO_EVIDENCE_MISMATCH`
4. Goal 1 只验证证据完整性，不因 severe log 自动失败旧用例。

## 十、真实垂直切片：InfoCard

不能只新增基础类而不接入实际流程。

选择 `BuildingInfoCard` 或 `ItemInfoCard` 的一个通用路径，按以下步骤迁移：

1. 开始 Case 时发布 `case.started`。
2. `spawn_or_prepare_target` 使用 OperationRunner。
3. `wait_pre_open_settle` 使用 WaitHandle。
4. `open_info_card` 使用 OperationRunner。
5. `wait_info_card_open` 使用 WaitHandle，并设置 `RequiredStablePolls = 3`。
6. `capture_info_card` 使用 OperationRunner，并把截图登记为 CaptureEvidence。
7. `request_close` 使用 OperationRunner。
8. `wait_info_card_closed` 使用 WaitHandle。
9. 添加至少三个断言：
   - 目标准备成功。
   - InfoCard 稳定打开。
   - InfoCard 最终关闭。
10. Case 结束时截取本 Case 的 operation/event/log 窗口并写入 Evidence。
11. 继续生成现有 `AutoTestResult`，状态与迁移前保持一致。

允许暂时保留 `SessionPhase` 外壳，但阶段内部必须调用新内核，不能复制两套等待计数。

## 十一、测试要求

### 11.1 Core 单元测试

至少覆盖：

1. Operation 成功、异常、超时、取消。
2. OperationContext 嵌套恢复。
3. OperationJournal 生命周期事件。
4. Event afterSequence 查询。
5. Wait 满足、超时、稳定次数、探针异常。
6. Log 折叠、级别过滤、游标过滤、operation 关联。
7. Evidence 全部通过时 success=true。
8. 任意断言失败或 Errors 非空时 success=false。
9. 有界缓冲区移除最旧记录。
10. Evidence JSON 往返序列化。

### 11.2 RimDevTools 回归测试

在 `RimDevTools.Tests/Program.cs` 增加：

- 新旧 Result.json 都能校验。
- Evidence 缺失、损坏、runId 不匹配均返回正确错误码。
- 新增字段不会破坏旧客户端读取。

### 11.3 游戏内回归

至少执行：

```powershell
dotnet build source\TempProject\TempProject.csproj -c Debug
dotnet build source\AutoTestProject\AutoTestProject.csproj -c Debug
dotnet test AutoTest.Core.Tests\AutoTest.Core.Tests.csproj
dotnet build RimDevTools.Tests\RimDevTools.Tests.csproj
dotnet RimDevTools.Tests\bin\Debug\net10.0\RimDevTools.Tests.dll
.\.tools\RimDevTools.exe test run --mode auto --scope content --defname <一个稳定的建筑或物品DefName> --capture-screenshots
```

游戏内结果必须同时生成旧报告和 Evidence.json。

## 十二、实施顺序

严格按以下顺序执行，避免大爆炸改造：

1. 新增 Contracts/Core 和单元测试项目。
2. 实现 Clock、Context、OperationRunner。
3. 实现 WaitHandle。
4. 实现 Operation/Event/Log Journal。
5. 修改 `LogPatches` 发布捕获事件并增加订阅器。
6. 实现 Evidence Builder 和 Evidence Writer。
7. 接入一个 InfoCard 垂直切片。
8. 扩展旧 Result/Report 输出。
9. 扩展 RimDevTools Validator。
10. 跑单元测试、构建和真实游戏定向回归。

不要先把所有 Session 改一半再补测试。

## 十三、验收标准

以下条件全部满足才算 Goal 1 完成：

- [ ] 现有 `LogSyncMod.dll` 和 `LogSyncMod.AutoTest.dll` 构建成功。
- [ ] 新增 Core 单元测试全部通过。
- [ ] `RimDevTools.Tests` 全部通过。
- [ ] 至少一个真实 InfoCard 用例完整使用新 Operation/Wait/Evidence。
- [ ] 每个步骤有 operationId、开始时间、结束时间、duration 和终态。
- [ ] 超时报告包含 Attempts、LastSnapshot、LastProbeError。
- [ ] Case 报告只包含游标窗口内的新增日志和事件。
- [ ] Evidence.json schemaVersion 固定且可往返解析。
- [ ] 旧 Result.json 的核心字段和完成信号保持兼容。
- [ ] 未安装 RimBridgeServer 时所有现有测试仍可运行。
- [ ] 未引入新的运行时第三方 DLL。

## 十四、风险和禁止事项

1. 禁止在 RimWorld 主线程或 OnGUI 中 `Thread.Sleep` 等待条件。
2. 禁止把 `Pawn/Thing/Map/Window` 直接序列化到 Evidence。
3. 禁止 Journal 无限增长。
4. 禁止在日志事件订阅器里再次调用 `Verse.Log` 导致递归。
5. 禁止一次性删除旧结果模型；Goal 1 必须双写兼容。
6. 禁止用截图是否存在替代功能断言。
7. 禁止让 AutoTest.Core 引用 RimWorld/Unity/Harmony。

## 十五、完成后给下一 Goal 的稳定接口

Goal 2 将直接依赖以下接口，完成 Goal 1 后必须在实现报告中列出它们的最终签名：

```text
AutoTestOperationRunner
AutoTestOperationContext
AutoTestWaitHandle
AutoTestOperationJournal
AutoTestLogJournal
AutoTestEvidenceBuilder
AutoTestEvidenceManifest
AutoTestAssertionEvidence
AutoTestCaptureEvidence
AutoTestRuntimeObservation
```

如果实施中必须调整命名，需要同步更新 Goal 2 文档，不能只改代码不改后续计划。
