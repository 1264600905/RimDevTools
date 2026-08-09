# Goal 3：RimBridgeServer 可选 Companion、Deep 模式与深度场景编排

## 给实施 AI 的任务说明

本 Goal 必须在 Goal 1 和 Goal 2 均完成后实施。

目标是增加一个可选的 RimBridgeServer 深度测试栈，而不是把现有 AutoTest 改成必须安装 RimBridgeServer 才能运行。

完成后应同时存在两条路径：

```text
基础路径：RimDevTools test run --mode auto/xml
  - 只依赖 LogSyncMod + LogSyncMod.AutoTest
  - 继续支持现有自动发现和批量回归

深度路径：RimDevTools test run --mode deep
  - 额外依赖 RimBridgeServer + GABS + LogSyncMod AutoTest Companion
  - 支持存档夹具、语义 UI、精确帧/tick、可组合场景和实时诊断
```

不要复制 RimBridgeServer 的主机、协议栈或全部工具到我们的 Mod。优先通过它公开的 SDK 和工具面扩展。

## 一、依赖条件

开始前确认：

1. Goal 1 的 Operation、Wait、Journal、Evidence 已稳定。
2. Goal 2 的语义 Probe、Assertion、ErrorPolicy 已接入主要测试族。
3. `AutoTestRuntimeApi` 已提供公共 DTO 门面：
   - ListSuites。
   - GetStatus。
   - RequestRun。
   - Cancel。
   - GetEvidence。
4. 现有 auto/xml 模式在没有 RimBridgeServer 时仍能通过。
5. 所有公共 DTO 不包含 Verse/RimWorld/Unity 对象。

如果 `AutoTestRuntimeApi` 尚不存在，先补齐 Goal 2，不要让 Companion 反射内部私有 Runner。

## 二、参考实现和许可证要求

参考项目：`参考文档/RimBridgeServer-main`

重点参考：

1. `README.md` 的 Third-Party Extension Tools。
   - Companion DLL 只引用 `RimBridgeServer.Sdk`。
   - 工具用 `[Tool]`、`[ToolParameter]`、`[ToolResponse]` 标注。
   - 支持 `IRimBridgeContext`、`CancellationToken`、Task/ValueTask。
   - 工具名在全局工具面唯一。
   - Companion 放在 RimWorld Mods 目录的兄弟 `BridgeTools` 根目录。
2. `README.md` 的 Async orchestration example。
   - 使用 `ctx.Tools.CallAsync(...)` 调用已注册能力。
   - 使用 `ctx.Game.NextFrameAsync/FramesAsync/StepTicksAsync/RunForTicksAsync/RunUntilAsync` 让真实游戏时间通过。
3. `README.md` 的 Evidence suites。
   - 使用 `RimBridgeEvidenceManifest` 返回 assertions、captures、errors、logs 和 environment。
4. `Tests/RimBridgeServer.LiveSmoke/SmokeHarness.cs`。
   - 外部 Harness 启动、连接、调用工具、保存报告。
5. `Tests/RimBridgeServer.LiveSmoke/SmokeScenarioContext.cs`。
   - 统一记录每次工具调用、operationId、duration 和 response。
6. `Tests/RimBridgeServer.LiveSmoke/SmokeObservationWindow.cs`。
   - 场景开始记录 log/event sequence，结束后读取增量窗口。
7. `Tests/RimBridgeServer.LiveSmoke/SmokeScenarioCatalog.cs`。
   - 场景目录、稳定名称、描述和独立执行函数。

RimBridgeServer 使用 MIT License。若实质复制参考项目的测试 Harness 或 MCP 客户端代码，必须在发布包 third-party notices 和对应源码文件头中保留许可证/版权说明。优先按其公开协议和设计重新实现，避免大段逐字复制。

### 2.1 参考目录的代码可用性

当前仓库中的参考副本不含 RimBridgeServer 主 `Source/`，但包含完整的公开说明、LiveSmoke Harness 源码、Core/SDK 测试和编译 DLL。

后续 AI 应按以下顺序理解实现：

1. 先读 `参考文档/RimBridgeServer-main/AGENTS.md`，了解 Companion 部署和验证约束。
2. 读 `README.md` 的 `Third-Party Extension Tools` 和 `Tool Surface`。
3. 读 `Tests/RimBridgeServer.LiveSmoke/*.cs`，理解外部 Harness 如何编排。
4. 读 `Tests/RimBridgeServer.Sdk.Tests/*.cs`，理解 SDK 返回和 Evidence 语义。
5. 如需确认 API，以 `1.6/Assemblies/RimBridgeServer.Sdk.dll` 的公开签名为准。
6. 如需确认 Host 实际已实现的类型，可查看 `RimBridgeServer.dll`，但不要复制 Host 内部实现。

已验证 DLL 中存在：

```text
RimBridgeServer.Sdk.ToolAttribute
RimBridgeServer.Sdk.ToolParameterAttribute
RimBridgeServer.Sdk.ToolResponseAttribute
RimBridgeServer.Sdk.IRimBridgeContext
RimBridgeServer.Sdk.IRimBridgeToolClient
RimBridgeServer.Sdk.IRimBridgeGameClock
RimBridgeServer.Sdk.IRimBridgeMainThread
RimBridgeServer.Sdk.RimBridgeToolClientExtensions
RimBridgeServer.Sdk.RimBridgeToolCallResult<T>
RimBridgeServer.Sdk.RimBridgeEvidence
RimBridgeServer.Sdk.RimBridgeEvidenceManifest
RimBridgeServer.AnnotatedExtensionCapabilityProvider
RimBridgeServer.RimBridgeExtensionDiscovery
RimBridgeServer.RimBridgeToolClient
RimBridgeServer.RimBridgeFrameClock
RimBridgeServer.MainThreadDispatcher
```

### 2.2 Companion SDK 精确参考矩阵

| 参考位置 | 具体符号 | 必须理解的行为 | 我们的实现 |
| --- | --- | --- | --- |
| README `Minimal example` | public parameterless tool class + `[Tool("mymod/ping")]` | 工具类和方法必须公开，注册来自注解 | `public sealed class LogSyncAutoTestBridgeTools`，公共无参构造器 |
| README `Async orchestration example` | `RunParticleTest(IRimBridgeContext ctx, string pawnId)` | Context 和 CancellationToken 是注入参数，不出现在公共 schema | `RunSuite(IRimBridgeContext ctx, ..., CancellationToken)` |
| README `ctx.Tools.CallAsync(...)` | 调用任意已注册工具 | 不为已有 `rimworld/*` 再写 wrapper | Companion 只补 LogSync 专有工具 |
| README `ctx.Tools.QueueAsync(...)` | 长操作先排队再按 operationId 等待 | save/load/启动等长事件不应假装同步立即完成 | Deep 场景调用 bridge ready/wait 工具 |
| DLL `IRimBridgeToolClient` | `Exists/Get/List/CallAsync/QueueAsync` | 运行前可以动态发现，不硬编码假设工具一定存在 | `BridgeRunCoordinator` 在调用前验证关键 capability |
| DLL `IRimBridgeContext` | `OperationId/CapabilityId/Tools/Game/MainThread` | 当前工具调用已有 operation 关联、工具客户端、游戏时钟和主线程入口 | Companion 不自行建立另一套全局 service locator |
| DLL `IRimBridgeGameClock` | `NextFrameAsync/FramesAsync/StepTicksAsync/RunForTicksAsync/RunUntilAsync` | Frame、tick 和条件等待是 SDK 的正式接口 | combat 和 run_suite 等待全部通过 `ctx.Game` |
| DLL `IRimBridgeMainThread` | `IsMainThread/InvokeAsync(Func<T>)/InvokeAsync(Action)` | Companion 如需直接读取 AutoTest RuntimeApi 游戏状态，可明确切回主线程 | 只有 RuntimeApi 要求主线程时才使用 `ctx.MainThread.InvokeAsync` |
| README `ctx.Game.NextFrameAsync/FramesAsync/StepTicksAsync/RunForTicksAsync/RunUntilAsync` | 让真实 Frame/Tick 通过 | 不使用 `Task.Delay` 代替游戏状态推进 | `run_suite` 等待 RuntimeApi 和 combat 场景 |
| DLL `RimBridgeFrameClock.NextFrameAsync` | 下一 Root Update 完成 Task | Host 确实有 frame clock，而非 README 空接口 | Companion 通过 SDK Context 使用，不直接引用 Host 类型 |
| README Evidence example | `CreateManifest`、`ToolSucceeded`、captures、`Complete` | 结构化返回给 Harness | `BridgeEvidenceAdapter` |
| DLL `RimBridgeEvidence` | `CreateManifest/Pass/Fail/IsTrue/AreEqual/ToolSucceeded/Complete` | Companion 直接使用 SDK helper | 不自行发明第二份 Bridge evidence DTO |
| `RimBridgeEvidenceTests.ToolSucceededUsesToolCallResultSemantics` | operation success + payload success | 不能只检查 MCP call 没报错 | 每个 bridge 工具步骤使用 `Succeeded()`/`ToolSucceeded()` |
| README Companion discovery rules | global BridgeTools、bundle helper isolation、collision rejected | 部署位置和工具命名必须正确 | `BridgeToolDeployment.cs` + `logsync/autotest_*` 前缀 |
| DLL `AnnotatedExtensionCapabilityProvider` | `InvokeMethodAsync/AwaitReturnValueAsync` | Host 支持 Task/ValueTask 和参数注入 | Companion 方法可以真正 async，不需自行阻塞 |
| DLL `RimBridgeExtensionDiscovery` | companion load/discovery diagnostics | 一个 Companion 失败应在 bridge status 可诊断 | `bridge status` 必须读取 discovery errors |

### 2.3 LiveSmoke Harness 精确参考矩阵

| 参考文件 | 具体类/方法 | 参考行为 | RimDevTools 对应实现 |
| --- | --- | --- | --- |
| `Tests/RimBridgeServer.LiveSmoke/SmokeHarness.cs` | `SmokeHarness.RunAsync` | 建 report、查 games.status、必要时 start、connect、执行 scenario、stop、保存 report | `DeepTestCommand.RunAsync` |
| 同文件 | `ConnectGameAsync` | connect 失败可重试，启动日志出现致命标记时提前终止 | `GabsConnectionCoordinator` |
| 同文件 | `CaptureStartupDiagnostics` | 连接前也检查 Player.log 的 bridge 初始化失败 | `StartupDiagnostics` |
| 同文件 | `SaveReportAsync` | 无论成功失败都保存报告 | Deep Harness finally 中写报告 |
| `SmokeScenarioContext.cs` | `CallServerToolAsync` | 每次工具调用记录 tool、duration、operationId、response | `DeepScenarioContext.CallToolAsync` |
| 同文件 | `CallGameToolAsync` | 统一包一层 `games.call_tool` 和 gameId/timeout | 不允许每个场景自己拼 games.call_tool 参数 |
| 同文件 | `EnsureSucceeded` | 一处解释工具失败 | `DeepScenarioContext.EnsureSucceeded` |
| 同文件 | `WaitForLongEventIdleAsync` | 调 `rimbridge/wait_for_long_event_idle` | 场景启动/存档操作前复用 |
| 同文件 | `WaitForGameLoadedAsync` | 调 `rimbridge/wait_for_game_loaded` | fixture load 和 debug game start 后复用 |
| 同文件 | `WaitForOperationAsync` | 按 operationId 等终态 | queue 型工具统一等待 |
| 同文件 | `EnsurePlayableGameAsync` | 已有游戏、long event、主菜单 quick start 三分支 | `DeepScenarioContext.EnsurePlayableGameAsync` |
| 同文件 | `BeginObservationWindowAsync` | 从 bridge status 读取两个 sequence cursor | `DeepObservationWindow.BeginAsync` |
| `SmokeObservationWindow.cs` | `CaptureAsync` | final status + afterSequence operation events + afterSequence logs | Deep Report 的增量诊断窗口 |
| `SmokeScenarioCatalog.cs` | `SmokeScenarioDefinition` | stable name/description/RunAsync | `DeepScenarioDefinition` |
| 同文件 `RunDebugGameLoadAsync` | queue start → wait operation → wait game loaded → assert colonists | 长事件编排范例 | Deep Harness 的 debug-game/fixture 准备流程 |
| 同文件 `RunUiLayoutRoundTripAsync` | layout → target → click → verify → restore | UI 不是只截图，要动作前后验证 | `ui-layout-language-roundtrip` |
| 同文件 `RunSaveLoadRoundTripAsync` | save → load → verify colony | 存档场景基础顺序 | `save-load-settings-roundtrip` |
| `SmokeReports.cs` | `SmokeStepReport/SmokeRunReport/HumanVerificationArtifact` | 报告分 step、scenario data、events、logs、artifacts | `DeepTestReports.cs` |

### 2.4 必须直接复用的 RimBridge 工具

以下工具已在参考 README 的生成工具面中明确存在。实施 AI 应先通过 `rimbridge/list_capabilities` 或 SDK `ctx.Tools.Exists()` 验证，再调用；不要在 Companion 重新实现：

| 目的 | 工具 |
| --- | --- |
| Bridge/游戏状态和 capability discovery | `rimbridge/get_bridge_status`、`rimbridge/list_capabilities`、`rimbridge/get_capability` |
| 等待 | `rimbridge/wait_for_operation`、`rimbridge/wait_for_game_loaded`、`rimbridge/wait_for_long_event_idle` |
| 日志和事件游标 | `rimbridge/list_logs`、`rimbridge/list_operation_events` |
| 启动/加载 | `rimworld/start_debug_game_ready`、`rimworld/load_game_ready`、`rimworld/save_game` |
| 时间 | `rimworld/pause_game`、`rimworld/set_time_speed`、`rimworld/step_game_ticks`、`rimworld/play_for` |
| 选择/Gizmo | `rimworld/select_pawn`、`rimworld/get_selection_semantics`、`rimworld/list_selected_gizmos`、`rimworld/execute_gizmo` |
| UI | `rimworld/get_ui_state`、`rimworld/get_ui_layout`、`rimworld/click_ui_target`、`rimworld/scroll_ui_target` |
| 通知 | `rimworld/list_messages`、`rimworld/list_letters`、`rimworld/list_alerts` |
| 地图/目标 | `rimworld/get_cell_info`、`rimworld/get_cells_info`、`rimworld/get_map_target_info` |
| 截图 | `rimworld/take_screenshot`、`rimworld/screenshot_cell_rect` |
| 设置/语言 | `rimworld/get_mod_settings`、`rimworld/update_mod_settings`、`rimworld/reload_mod_settings`、`rimworld/list_languages`、`rimworld/switch_language` |
| 脚本 | `rimbridge/run_script`、`rimbridge/run_lua_file`、`rimbridge/compile_lua_file` |
| 性能 | `rimworld/dpa_status`、`rimworld/dpa_patch_methods`、`rimworld/dpa_snapshot`、`rimworld/dpa_reset`、`rimworld/dpa_cleanup` |

### 2.5 当前仓库的明确改动映射

| 当前代码 | Goal 3 改动 |
| --- | --- |
| `source/AutoTestProject` | 只暴露/完善 `AutoTestRuntimeApi`；不引用 SDK |
| `source/AutoTestBridgeTools` | 新 Companion，引用 SDK + AutoTest 公共 DTO |
| `RimDevTools/Commands/TestCommand.cs` | mode parser 增加 `deep`，转交 DeepTestCommand；不把所有 deep 逻辑继续堆入该文件 |
| `RimDevTools/Infrastructure/ProjectConfig.cs` | 增加可选 `BridgeTestingConfig` |
| `RimDevTools/Infrastructure/RimWorldEnvironmentResolver.cs` | 复用 Mods 根解析，派生 sibling BridgeTools 根 |
| `RimDevTools/Infrastructure/BridgeToolDeployment.cs` | 新增 Companion 专用事务部署 |
| `.tools/publish-rimdevtools.ps1` | 构建并打包 Companion/fixtures/notice |
| `RimDevTools.ReleaseTests/Program.cs` | 检查正确布局且无 SDK 私有副本 |
| `mod_config.jsonc` 和模板 | 增加默认关闭的 bridge_testing 示例 |

### 2.6 最小 Companion 代码形状

后续 AI 应先实现下面这个最小工具验证 discovery，再扩展复杂 run_suite：

```csharp
using RimBridgeServer.Sdk;

public sealed class LogSyncAutoTestBridgeTools
{
    [Tool(
        "logsync/autotest_list_suites",
        Description = "List suites exposed by LogSyncMod AutoTest.",
        ResultDescription = "A success flag and the currently available suite descriptors.")]
    public object ListSuites()
    {
        return new
        {
            success = true,
            suites = AutoTestRuntimeApi.ListSuites()
        };
    }
}
```

验证顺序：

```text
构建 Companion
→ 部署到 RimWorldRoot/BridgeTools/LogSyncMod.AutoTest
→ 启动带 RimBridge 的游戏
→ rimbridge/get_bridge_status 检查 discovery diagnostics
→ rimbridge/list_capabilities 搜索 logsync/autotest_list_suites
→ 实际调用工具
```

只有这个垂直切片成功后，才开始实现 async `run_suite`。

## 三、目标和非目标

### 目标

1. 创建 `LogSyncMod.AutoTest.BridgeTools.dll` Companion。
2. 通过 Companion 暴露可发现、可等待、可取消的测试工具。
3. 新增 RimDevTools `deep` 模式，负责 GABS/RimBridge 生命周期和报告。
4. 正确部署 Companion 到全局 BridgeTools sibling root。
5. 使用 RimBridge 通用能力组合深度场景，不重新实现完整 DSL。
6. 首批实现五个有代表性的深度场景。
7. 将 RimBridge 操作、内部 AutoTest Evidence、截图、日志和事件合并成一份 Deep Report。
8. 所有设置、语言、存档和游戏状态修改都有恢复策略。

### 非目标

1. 不让 `trigger.logsyncmod` 的 About.xml 增加 RimBridgeServer 强制依赖。
2. 不在主 Mod 的 `1.6/Assemblies` 中附带 `RimBridgeServer.Sdk.dll`。
3. 不复制 RimBridgeServer.dll、Core.dll 或 GAB 协议实现。
4. 不在本 Goal 发明第二套 Lua 解释器。
5. 不承诺 deep 模式在没有 RimBridgeServer/GABS 时降级执行；应清晰失败并说明安装要求。
6. 不让 Companion 的构造器或静态初始化器查询 Bridge Registry。

## 四、最终架构

```text
RimDevTools deep harness (net10)
        │ games.start/connect/call_tool
        ▼
GABS ───── RimBridgeServer (RimWorld 内)
                 │ capability registry
                 ├── rimworld/* 通用工具
                 ├── rimbridge/* 等待、日志、事件、脚本
                 └── logsync/autotest_* Companion 工具
                              │
                              ▼
                 AutoTestRuntimeApi
                              │
                              ▼
                 现有 AutoTest Runner/Core/Probes/Evidence
```

## 五、Companion 项目

### 5.1 新项目

新增：

```text
source/AutoTestBridgeTools/
  AutoTestBridgeTools.csproj
  LogSyncAutoTestBridgeTools.cs
  BridgeEvidenceAdapter.cs
  BridgeRunCoordinator.cs
  Properties/AssemblyInfo.cs
```

程序集名称：

```text
LogSyncMod.AutoTest.BridgeTools.dll
```

目标框架：`net472`。

编译引用：

- `参考文档/RimBridgeServer-main/1.6/Assemblies/RimBridgeServer.Sdk.dll`
- `1.6/Assemblies/LogSyncMod.AutoTest.dll`
- `1.6/Assemblies/LogSyncMod.dll`

所有运行时引用设置 `<Private>False</Private>`。Companion 输出目录不得是主 Mod 的 `1.6/Assemblies`，建议：

```text
artifacts/BridgeTools/LogSyncMod.AutoTest/LogSyncMod.AutoTest.BridgeTools.dll
```

严禁把开发用 SDK 副本复制进 Companion bundle。运行时由 RimBridgeServer 提供统一 SDK assembly。

### 5.2 Companion 故障隔离

所有工具方法：

- 参数校验失败返回结构化失败，不抛出无上下文异常。
- 调用 `AutoTestRuntimeApi` 失败时返回 stage、code、message。
- SDK ABI 错误交给 RimBridge Operation 包装，但错误信息要建议重新构建/部署 Companion。
- 不缓存 RimWorld Map/Pawn/Thing 实例跨调用。

## 六、公开工具面

工具名全局唯一，统一使用 `logsync/autotest_` 前缀。

### 6.1 list_suites

```csharp
[Tool(
    "logsync/autotest_list_suites",
    Description = "List LogSync AutoTest suites and their supported filters.")]
public object ListSuites()
```

返回：

```json
{
  "success": true,
  "suites": [
    {
      "id": "standard",
      "description": "...",
      "supportsDefNames": true,
      "supportsScreenshots": true,
      "requiresPlayableMap": true
    }
  ]
}
```

### 6.2 get_status

```csharp
[Tool("logsync/autotest_get_status")]
public object GetStatus()
```

返回 active runId、suite、current case/step、progress、terminal status、last error、evidenceAvailable。

### 6.3 run_suite

```csharp
[Tool("logsync/autotest_run_suite")]
public async Task<object> RunSuite(
    IRimBridgeContext ctx,
    string suite,
    string runId = null,
    string defNames = null,
    bool captureScreenshots = true,
    int timeoutMs = 600000,
    CancellationToken cancellationToken = default)
```

行为顺序必须固定：

1. 校验 suite 和 defNames。
2. 调用 `rimbridge/get_bridge_status`，确认 SDK、游戏状态和当前日志/事件游标。
3. 如要求可玩地图：
   - 已有游戏时调用 `rimbridge/wait_for_game_loaded`，readiness=`visual`。
   - 主菜单时由上层场景决定调用 `rimworld/start_debug_game_ready` 或 `rimworld/load_game_ready`。
   - Companion 不应擅自覆盖明确的外部存档选择。
4. 调用 `AutoTestRuntimeApi.RequestRun()` 入队。
5. 使用 `ctx.Game.RunUntilAsync()` 或逐帧 Wait API 等待 RuntimeApi 终态，不能在主线程 busy loop。
6. timeout/cancellation 时调用 RuntimeApi.Cancel(runId)。
7. 获取内部 Evidence Snapshot。
8. 读取 bridge observation window 内的新 logs/events。
9. 转换为 `RimBridgeEvidenceManifest` 或稳定等价返回。

### 6.4 cancel_run

```csharp
[Tool("logsync/autotest_cancel_run")]
public object CancelRun(string runId)
```

取消是协作式：

- 设置 cancellation requested。
- 当前步骤进入安全清理。
- 地图、窗口、速度、相机、选择恢复后才进入 Cancelled 终态。
- 超过清理超时返回 `autotest.cleanup_timed_out`，不能直接遗留活动 pocket map。

### 6.5 get_evidence

```csharp
[Tool("logsync/autotest_get_evidence")]
public object GetEvidence(string runId, bool includeOperations = true, bool includeLogs = true)
```

只返回不可变快照。大日志和事件列表支持 limit/afterSequence，避免超过工具响应上限。

### 6.6 probe_target

```csharp
[Tool("logsync/autotest_probe_target")]
public object ProbeTarget(string targetId, int maxDepth = 3)
```

通过 Goal 2 的稳定 TargetResolver 和 Probe 返回受控语义状态，用于外部场景自定义断言。必须应用 SemanticValueGraph 限制。

## 七、Bridge Evidence 适配

新增 `BridgeEvidenceAdapter`：

- 将内部 `AutoTestEvidenceManifest` 映射到 `RimBridgeEvidenceManifest`。
- 内部 assertionId/name/expected/actual/details 不丢失。
- Screenshot 转换为 `RimBridgeEvidenceCapture`。
- 内部 errors 与 Bridge operation errors 分开标明 source。
- Environment 同时记录：
  - AutoTest/LogSyncMod 版本。
  - RimBridge host SDK 版本。
  - Companion assembly 版本。
  - active mod root。
  - gameId/saveName/scenario。

不要只返回 `success=true/false` 和一个 reportPath。

## 八、Companion 部署

### 8.1 正确目标路径

参考 RimBridgeServer 的正常 paired deployment 规则：

```text
<RimWorldRoot>/Mods/LogSyncMod/1.6/Assemblies/LogSyncMod.AutoTest.dll
<RimWorldRoot>/BridgeTools/LogSyncMod.AutoTest/LogSyncMod.AutoTest.BridgeTools.dll
```

BridgeTools 根目录必须从当前选择的 Mods 目录派生：

```csharp
bridgeToolsRoot = Path.GetFullPath(Path.Combine(modsPath, "..", "BridgeTools"));
```

不要新增独立、任意的 BridgeTools override 作为默认配置，也不要默认部署到：

```text
Mods/LogSyncMod/1.6/BridgeTools
```

### 8.2 新部署服务

在 RimDevTools 增加：

```text
Infrastructure/BridgeToolDeployment.cs
```

职责：

- 解析 Mods 根和 sibling BridgeTools 根。
- 路径安全检查。
- 构建 Companion。
- 暂存、哈希验证、备份、替换和失败回滚。
- `--dry-run` 输出 source、target、hash、action。
- 不删除其他 Companion bundle。

不要复用 `ToolModInstaller` 的 packageId 逻辑硬套 BridgeTools；它管理的是 Mods 目录中的完整 Mod，语义不同。

### 8.3 配置模型

在 `ProjectConfig` 增加可选：

```jsonc
"bridge_testing": {
  "enabled": false,
  "rimbridge_package_id": "brrainz.rimbridgeserver",
  "companion_project": "source\\AutoTestBridgeTools\\AutoTestBridgeTools.csproj",
  "companion_bundle": "LogSyncMod.AutoTest",
  "gabs_binary_path": "",
  "game_id": "rimworld",
  "wait_timeout_seconds": 600,
  "report_directory": ".tools\\log\\DeepTestOutput"
}
```

`enabled=false` 时普通 doctor/deploy 不应因为未安装 GABS/RimBridge 而失败。执行 `--mode deep` 时这些配置成为必需项。

### 8.4 命令

增加：

```powershell
RimDevTools.exe bridge deploy --dry-run
RimDevTools.exe bridge deploy
RimDevTools.exe bridge status
RimDevTools.exe test run --mode deep --scenario <scenario-name>
```

`bridge status` 至少检查：

- RimBridgeServer 是否安装并启用。
- active mod root。
- SDK 版本。
- Companion bundle 是否存在。
- Companion 是否错误携带本地 SDK 副本。
- Companion assembly version/hash。
- GABS 路径和 gameId。

## 九、RimDevTools Deep Harness

### 9.1 新目录

```text
RimDevTools/
  DeepTesting/
    DeepTestCommand.cs
    GabsMcpClient.cs
    DeepScenarioCatalog.cs
    DeepScenarioContext.cs
    DeepObservationWindow.cs
    DeepTestReports.cs
    StartupDiagnostics.cs
```

如果可复用当前 `GameProcessLauncher`、环境解析和日志工具，应复用，不要复制第二套 Steam/RimWorld 路径解析。

### 9.2 GABS/MCP 客户端职责

实现最小必要 MCP stdio 客户端：

- 启动 GABS 子进程。
- JSON-RPC request id 关联。
- initialize/list tools/call tool。
- stderr 有界 tail。
- cancellation/timeout。
- 进程退出诊断。

不要把 MCP 客户端和场景业务写在一个类里。

### 9.3 运行流程

```text
validate config
→ build/deploy mod and companion
→ transactional ModsConfig update
→ games.status
→ games.start（必要时）
→ games.connect
→ confirm bridge/companion discovery
→ execute selected scenario
→ collect final status/log/event window
→ save report
→ stop game（仅当本 Harness 启动）
→ restore ModsConfig/settings
```

### 9.4 Attention

RimBridgeServer severe log 或异步 operation failure 可能触发 blocking attention。

Deep Harness 必须支持：

1. 调用普通工具被 attention 阻挡时，读取 `games.get_attention`。
2. 报告 attention 摘要。
3. 调用允许的诊断工具收集 operation/log。
4. 仅在已记录证据后调用 `games.ack_attention`。
5. 默认把 error/fatal/operation failed attention 视为场景失败，不要自动忽略后继续。

## 十、不要重新发明脚本引擎

RimBridgeServer 已提供：

```text
rimbridge/run_script
rimbridge/run_lua
rimbridge/run_lua_file
rimbridge/compile_lua
```

因此 Deep 场景采用两层：

1. C# `DeepScenarioCatalog` 负责高层生命周期、恢复和报告。
2. 重复的游戏动作序列使用 RimBridge JSON script 或 lowered Lua fixture。

Companion 只补充 LogSync AutoTest 专有能力，不复制 `pause_game/spawn_thing/take_screenshot/list_logs` 等已有通用工具。

场景 fixture 放在：

```text
RimDevTools/DeepTesting/Fixtures/
  save-load-settings-roundtrip.json
  ability-gizmo-roundtrip.lua
  ui-layout-language-roundtrip.json
  combat-projectile-evidence.lua
  dpa-budget.json
```

公开发布时同步到 RimDevTools 包内，不放进 RimWorld Mod 的 Assemblies 目录。

## 十一、首批五个深度场景

### 11.1 save-load-settings-roundtrip

目标：验证 ModSettings 和存档数据真实持久化。

流程：

1. `rimworld/get_mod_settings` 读取初始值。
2. 创建测试 save 或加载专用 fixture save。
3. 通过 Companion/Mod API 写入一个明确测试值。
4. `rimworld/save_game`。
5. 改成另一个内存值。
6. `rimworld/load_game_ready` 重载存档。
7. 再次读取语义状态。
8. 断言存档字段恢复为保存值。
9. 恢复原始设置并持久化。

必须记录 saveName、原值、测试值、加载后值和恢复结果。

### 11.2 ability-gizmo-roundtrip

目标：通过玩家真实可见的 Gizmo 路径验证技能。

流程：

1. 准备包含技能 Pawn 的 fixture。
2. `rimworld/select_pawn`。
3. `rimworld/list_selected_gizmos`。
4. 通过 label/类型找到目标 Gizmo，保存稳定 gizmoId。
5. `rimworld/execute_gizmo` 或进入目标选择流程。
6. 必要时使用 `click_cell/right_click_cell` 完成目标。
7. 精确推进 ticks/frames。
8. 调用 Companion `probe_target` 和 Goal 2 AbilityProbe。
9. 断言 cooldown/charge/Hediff/Message。
10. 截取目标区域和 UI 证据。

该场景必须验证旧 gizmoId 在 Selection 改变后会被拒绝。

### 11.3 ui-layout-language-roundtrip

目标：测试真正 UI 控件、布局、滚动和多语言稳定性。

流程：

1. 记录原语言。
2. 打开目标 ModSettings 或主 Tab。
3. `rimworld/get_ui_layout` 获取 surface/element ID。
4. 断言关键按钮、标签行或 scroll view 存在。
5. 使用 `click_ui_target/scroll_ui_target`。
6. 使用 `clipTargetId` 截取控件或窗口截图。
7. 切换 English/ChineseSimplified，重复布局捕获。
8. 不断言本地化全文完全一致；断言控件角色、数量和可操作性。
9. 恢复原语言和窗口状态。

### 11.4 combat-projectile-evidence

目标：把现有炮塔/远程测试与精确时间控制、地图探针和截图结合。

流程：

1. 调用 `logsync/autotest_run_suite` 定向启动一个测试目标，或调用专用 fixture 工具准备场景。
2. 通过 `ctx.Game.StepTicksAsync/RunUntilAsync` 精确等待锁敌。
3. 读取攻击者/目标状态。
4. 等待 projectile 出现。
5. `rimworld/screenshot_cell_rect` 捕获投射物路径区域。
6. 等待伤害。
7. 断言 target acquired、projectile observed、damage observed、no severe logs。
8. 验证 cleanup 后 pocket map 消失。

对 beam/instant 或地点攻击类型使用明确分支，不能把“没有 projectile”统一视为失败。

### 11.5 dpa-budget

目标：可选性能回归。

流程：

1. `rimworld/dpa_status`。
2. DPA 未安装或未启用时返回 skipped，不影响非性能场景。
3. `rimworld/dpa_patch_methods` 选择明确方法或 preset。
4. reset。
5. 运行固定 ticks/real-time duration。
6. snapshot。
7. 断言平均耗时、最大耗时或总耗时不超过配置预算。
8. cleanup。

预算放在 scenario 配置中，不硬编码在 Companion。

## 十二、场景目录和报告

### 12.1 Catalog

每个场景定义：

```csharp
internal sealed class DeepScenarioDefinition
{
    public string Name { get; init; }
    public string Description { get; init; }
    public bool RequiresPlayableGame { get; init; }
    public bool RequiresFixtureSave { get; init; }
    public bool RequiresDpa { get; init; }
    public Func<DeepScenarioContext, CancellationToken, Task> RunAsync { get; init; }
}
```

CLI 支持：

```powershell
RimDevTools.exe test run --mode deep --list-scenarios
RimDevTools.exe test run --mode deep --scenario ability-gizmo-roundtrip
RimDevTools.exe test run --mode deep --scenario combat-projectile-evidence --defname MyWeapon
```

### 12.2 Observation Window

场景开始：

- `rimbridge/get_bridge_status`
- 记录 `latestLogSequence`
- 记录 `latestOperationEventSequence`

场景结束：

- 再读 bridge status。
- `rimbridge/list_operation_events afterSequence=...`
- `rimbridge/list_logs afterSequence=...`
- 合并 Companion Evidence。

### 12.3 Deep Report

输出：

```text
<scenario>_<timestamp>_DeepReport.json
<scenario>_<timestamp>_DeepReport.md
<scenario>_<timestamp>_Artifacts/
```

JSON 至少包含：

- scenario、description、gameId、runId、saveName。
- started/completed/duration/success。
- 每次工具调用：step、tool、duration、operationId、success、response 摘要。
- internal AutoTest Evidence。
- bridge operation events。
- bridge logs。
- attention items。
- screenshots/artifacts。
- initial/final game state。
- restore actions 和结果。

## 十三、事务与恢复

Deep 场景可能修改全局状态，必须使用恢复栈。

`DeepScenarioContext` 提供：

```csharp
RegisterRestore(string name, Func<CancellationToken, Task> restore)
RunRestoreStackAsync()
```

恢复顺序后进先出。无论成功、失败、timeout、attention 或 cancellation 都执行。

至少恢复：

- 当前语言。
- ModSettings 测试字段。
- god mode。
- time speed/pause。
- camera extension/zoom。
- selection/window。
- 临时 save 和 fixture 副本。
- 测试创建的 zone/area/thing。
- ModsConfig transaction。

恢复失败必须进入报告 Errors，不能覆盖原始失败原因。

## 十四、配置、发布和测试

### 14.1 `mod_config.jsonc`

增加 `bridge_testing` 示例，但默认 `enabled=false`，模板中使用空路径和注释说明。

### 14.2 发布脚本

修改 `.tools/publish-rimdevtools.ps1`：

- 构建 Companion。
- 将 Companion bundle 放入 RimDevTools 发布包的数据区，而不是 LogSyncMod/1.6/Assemblies。
- 打包 deep fixtures。
- 打包 RimBridgeServer MIT notice；不打包 RimBridgeServer 本体，除非未来获得明确发布要求。

### 14.3 ReleaseTests

修改 `RimDevTools.ReleaseTests/Program.cs` 验证：

- Companion DLL 存在于正确发布数据目录。
- 主 Mod Assemblies 中不存在 `RimBridgeServer.Sdk.dll`。
- Companion bundle 中也不存在 SDK 私有副本。
- deep fixtures 存在。
- 默认配置不会把 deep 模式强制打开。
- third-party notices 包含 RimBridgeServer 许可说明。

### 14.4 Unit/Integration 测试

至少测试：

- Tool 参数解析和 suite validation。
- Bridge Evidence 映射。
- Companion run timeout/cancel 状态。
- BridgeTools 路径派生和路径安全。
- 部署 dry-run、安装、更新、回滚。
- GABS JSON-RPC request/response 关联。
- Observation afterSequence。
- Attention 处理。
- RestoreStack 失败不覆盖原始错误。
- Deep Report 序列化。

## 十五、真实验收流程

### 15.1 无 Bridge 兼容

先在未启用 RimBridgeServer 的配置下验证：

```powershell
.\.tools\RimDevTools.exe test run --mode quick
.\.tools\RimDevTools.exe test run --mode auto --scope content --defname <稳定目标>
```

必须继续成功。

### 15.2 Bridge 部署诊断

```powershell
.\.tools\RimDevTools.exe bridge deploy --dry-run
.\.tools\RimDevTools.exe bridge deploy
.\.tools\RimDevTools.exe bridge status
```

确认 RimBridge `get_bridge_status` 中能看到 Companion discovery 和工具注册结果。

### 15.3 Deep 场景

至少真实通过：

```powershell
.\.tools\RimDevTools.exe test run --mode deep --scenario save-load-settings-roundtrip
.\.tools\RimDevTools.exe test run --mode deep --scenario ability-gizmo-roundtrip
.\.tools\RimDevTools.exe test run --mode deep --scenario ui-layout-language-roundtrip
.\.tools\RimDevTools.exe test run --mode deep --scenario combat-projectile-evidence --defname <稳定武器或炮塔>
```

如果本机已安装并启用 DPA，再执行：

```powershell
.\.tools\RimDevTools.exe test run --mode deep --scenario dpa-budget
```

## 十六、实施顺序

1. 建立 Companion 项目并完成最小 `autotest_list_suites`。
2. 实现 Bridge Evidence Adapter。
3. 实现 `run_suite/get_status/cancel/get_evidence/probe_target`。
4. 实现 BridgeTools 部署服务和 status 诊断。
5. 实现 GABS MCP 客户端和最小连接测试。
6. 实现 DeepScenarioContext、ObservationWindow、RestoreStack、Report。
7. 实现 save-load 场景。
8. 实现 ability-gizmo 场景。
9. 实现 UI language/layout 场景。
10. 实现 combat projectile 场景。
11. 实现可选 DPA 场景。
12. 修改发布脚本、配置模板和 ReleaseTests。
13. 跑无 Bridge 兼容回归和真实 Deep 回归。

不要一开始同时实现五个场景；先把 list/run/evidence 和一个 save-load 垂直切片跑通。

## 十七、验收标准

- [ ] 未安装/未启用 RimBridgeServer 时，quick/auto/xml 不受影响。
- [ ] Companion 只引用 SDK，不私带 SDK DLL。
- [ ] Companion 正确部署到 RimWorldRoot/BridgeTools/LogSyncMod.AutoTest。
- [ ] RimBridge 能发现全部 `logsync/autotest_*` 工具。
- [ ] `run_suite` 使用 RuntimeApi 和非阻塞等待，不反射私有 Runner。
- [ ] Deep Harness 能启动/连接游戏、处理 attention、调用工具并保存报告。
- [ ] Observation Window 只收集本场景开始后的日志和事件。
- [ ] 五个首批场景至少四个为必跑通过，DPA 场景在缺少 DPA 时明确 skipped。
- [ ] 场景失败、超时或取消后仍执行恢复栈。
- [ ] 报告同时包含 Bridge operations、内部 Evidence、logs、events、attention 和 captures。
- [ ] ReleaseTests 验证 Companion、fixtures、notice 和无 SDK 私有副本。
- [ ] 发布包和源代码包含必要 MIT notice。

## 十八、风险和禁止事项

1. 禁止让 Companion 成为 `trigger.logsyncmod` 强制加载依赖。
2. 禁止把 RimBridgeServer SDK/Core/Host DLL 复制进主 Mod Assemblies。
3. 禁止 Companion 构造器、静态构造器在注册期间查询工具列表。
4. 禁止写前台桌面鼠标自动化代替 RimBridge background-safe UI。
5. 禁止在 Deep Harness 自动 ack attention 后不记录证据。
6. 禁止为已有 `rimworld/*` 工具再写一套重复 Companion wrapper。
7. 禁止深度场景修改语言、设置、存档、ModsConfig 后不恢复。
8. 禁止把 GABS/RimBridge 缺失报告成普通测试失败；必须返回明确环境错误码和修复建议。

建议环境错误码：

```text
DEEP_GABS_MISSING
DEEP_RIMBRIDGE_NOT_INSTALLED
DEEP_RIMBRIDGE_NOT_ACTIVE
DEEP_COMPANION_NOT_DISCOVERED
DEEP_SDK_VERSION_MISMATCH
DEEP_BRIDGE_CONNECT_TIMEOUT
DEEP_ATTENTION_BLOCKED
DEEP_RESTORE_FAILED
```

## 十九、最终完成定义

三个 Goal 全部完成后，自动测试系统应形成以下分层：

```text
自动发现层
  找出目标 Mod 的 Def 和测试清单

执行内核层
  Operation / Wait / Journal / Cancellation / Evidence

语义验证层
  Probe / Stable ID / Assertion / Error Policy

现有测试模块层
  InfoCard / Recipe / Combat / Ability / ThreeView

可选外部编排层
  RimBridge Companion / GABS / Deep Scenarios / UI / SaveLoad / DPA
```

此时新增一种深度测试时，正常工作应是：

1. 复用已有 Probe 或增加一个薄 Game Adapter。
2. 声明步骤和命名 Wait 条件。
3. 添加 expected/actual 断言。
4. 选择是否提供截图证据。
5. 在独立 AutoTest 或 Deep Scenario 中编排。

不应再复制一整套 Session 超时、日志扫描、截图落盘和报告拼接逻辑。
