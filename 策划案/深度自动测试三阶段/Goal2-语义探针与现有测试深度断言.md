# Goal 2：语义探针、稳定目标与现有测试深度断言

## 给实施 AI 的任务说明

本 Goal 必须在 Goal 1 已完成并通过验收后实施。

开始前先读取 Goal 1 的完成报告和最终接口签名。如果 Goal 1 实现中的类名与计划文档不同，应以实际代码为准，但必须保持本文描述的职责边界。

本 Goal 的任务不是继续增加更多截图，而是让现有测试能够证明游戏状态真的发生了预期变化。截图作为证据保留，不能再作为主要功能判定。

完成时必须迁移现有主要测试族，不能只写一套 Probe API 而没有真实调用者。

## 一、依赖条件

实施前确认：

- `AutoTestOperationRunner` 已可生成稳定 operationId。
- 非阻塞 `AutoTestWaitHandle` 已可在 Tick/Repaint 中使用。
- Operation/Event/Log Journal 已支持游标窗口。
- Evidence Manifest 已包含 operations/assertions/captures/logs/events/errors。
- 至少一个 InfoCard 用例已完成 Goal 1 垂直切片。
- 新旧报告和 RimDevTools Validator 均保持兼容。

如果以上任一条件不成立，先修复 Goal 1，不要在 Goal 2 内复制替代实现。

## 二、参考实现与借鉴边界

参考项目：`参考文档/RimBridgeServer-main`

### 重点参考

1. `docs/semantic-state-design.md`
   - 从游戏模型读取语义状态，不把 UI 文本截图识别当作第一数据源。
   - Gizmo ID 必须与当前 Selection Fingerprint 绑定。
   - Messages、Letters、Alerts 是可轮询、可 diff 的游戏状态。
2. `docs/architecture.md`
   - Stable references over fuzzy strings。
   - Semantic mode 和 Physical mode 分离。
   - Wait condition 使用命名条件，而不是散落 sleep/tick 倒计时。
3. `Tests/RimBridgeServer.Core.Tests/SelectionGizmoIdsTests.cs`
   - 稳定选择指纹和过期目标拒绝执行。
4. `Tests/RimBridgeServer.Core.Tests/SemanticValueGraphTests.cs`
   - 嵌套对象、集合、循环引用和最大深度控制。
5. `Tests/RimBridgeServer.Core.Tests/AutomationReadinessTests.cs`
   - gameData/mapData/currentMap/playable/visualReady 分层就绪。
6. `README.md` 的 Selection Semantics、Notifications、UI、Camera、Screenshot 工具说明。

### 不直接照搬

参考项目使用 publicized RimWorld assembly，而当前项目引用普通 `Assembly-CSharp.dll`。因此：

- 优先使用现有公开 API。
- 必须访问私有字段时，反射只能封装在 `Game/Probes` 或 `Game/Adapters` 中。
- Core、Contracts、Assertions 禁止出现 `AccessTools`、`BindingFlags` 或 RimWorld 类型。
- 反射失败必须返回“不支持/不可用”的结构化结果，不能让整个测试运行崩溃。

### 2.1 参考证据位置说明

当前 `参考文档/RimBridgeServer-main` 没有主 `Source/` 目录。语义层的实际实现需要通过以下三类证据理解：

```text
设计原因：docs/semantic-state-design.md、docs/architecture.md
行为合同：Tests/RimBridgeServer.Core.Tests/*.cs
实际类型：1.6/Assemblies/RimBridgeServer.Core.dll、RimBridgeServer.dll
```

已从 DLL 验证存在的关键类型：

```text
RimBridgeServer.Core.AutomationReadiness
RimBridgeServer.Core.AutomationReadinessEvaluation
RimBridgeServer.Core.AutomationReadinessTarget
RimBridgeServer.Core.SelectionGizmoIds
RimBridgeServer.Core.SemanticValueGraph
RimBridgeServer.Core.SemanticValueGraphOptions
RimBridgeServer.Core.SemanticValueNode
RimBridgeServer.Core.UiLayoutTargetIds
RimBridgeServer.RimWorldSelectionSemantics
RimBridgeServer.RimWorldNotifications
RimBridgeServer.RimBridgeUiWorkbench
RimBridgeServer.UiLayoutSnapshot
RimBridgeServer.UiLayoutSurfaceSnapshot
RimBridgeServer.UiLayoutElementSnapshot
```

### 2.2 精确参考矩阵

| 参考位置 | 具体符号/方法 | 参考行为 | 我们的实现落点 |
| --- | --- | --- | --- |
| `docs/semantic-state-design.md` 的 `Model-backed state, not UI scraping` | `ISelectable.GetInspectString()`、`ISelectable.GetGizmos()`、Letter/Message/Alert 集合 | 优先读取游戏模型，不做截图 OCR | `Game/Probes/*` |
| `docs/semantic-state-design.md` 的 `Selection-scoped gizmos` | selection fingerprint + gizmo ordinal/fingerprint | Gizmo 只在产生它的 Selection 上有效 | `Core/AutoTestSelectionFingerprint.cs`、`Game/Adapters/AutoTestGizmoAdapter.cs` |
| `docs/semantic-state-design.md` 的 `Mirror RimWorld's gizmo grouping behavior` | 多选 Gizmo 分组和代表项 | 不把每个 Pawn 的原始 Gizmo 当成玩家看到的命令集合 | 第一阶段至少复用 RimWorld 当前选择的分组语义；Goal 3 由 bridge 执行真实 grouped gizmo |
| `SelectionGizmoIdsTests.CreateSelectionFingerprint_IsDeterministic` | 相同 token 顺序产生相同 fingerprint | ID 可重复生成 | `AutoTestTargetIdsTests` |
| `SelectionGizmoIdsTests.CreateSelectionFingerprint_DiffersWhenOrderChanges` | 选择顺序变化导致 fingerprint 变化 | 防止多选顺序变化后误执行 | 同上 |
| `SelectionGizmoIdsTests.CreateGizmoId_EmbedsSelectionFingerprint` | Gizmo ID 可读回 selection fingerprint | 执行前验证 stale | `AutoTestGizmoAdapter.Execute` 前置校验 |
| DLL `RimBridgeServer.Core.SelectionGizmoIds` | `CreateSelectionFingerprint/CreateGizmoId/TryReadSelectionFingerprint` | 小型纯 Core ID helper | 我们独立实现同一职责，不引用 Bridge Core |
| DLL `RimBridgeServer.RimWorldSelectionSemantics` | `GetSelectionSemanticsResponse/ListSelectedGizmosResponse/ExecuteGizmoResponse` | 读状态、列命令、执行命令三层分离 | `AutoTestSelectionProbe`、`AutoTestGizmoAdapter` |
| DLL `RimBridgeServer.RimWorldSelectionSemantics` | `DescribeSelectedObject/GetInspectString/GetInspectTabTypes/GetSelectionToken` | 选择对象返回稳定 token + inspect 语义 | Thing/Pawn Probe 和 InfoCard Evidence |
| `AutomationReadinessTests.MarksPlayingNonFadingGameAsAutomationReady` | playing + map + no long event + no fade | AutomationReady 不是单一 ProgramState | `AutoTestReadinessProbe` |
| `AutomationReadinessTests.RejectsAutomationReadyWhileScreenFadeIsActive` | fade active 时 visualReady=false | 截图/UI 测试必须等视觉稳定 | Bootstrap 初始 Wait 和截图前 Wait |
| `AutomationReadinessTests.MapDataCanBeReadyWhileLongEventIsPending` | mapData 与 playable/visual 分层 | 加载过程可诊断卡在哪一层 | Readiness DTO 的各布尔字段 |
| DLL `RimBridgeServer.Core.AutomationReadiness.Evaluate` | 输入 current game/map/program state/long event/fade，输出分层 evaluation | 将判定纯逻辑化并单元测试 | `Core/AutoTestReadinessEvaluator.cs` + Game Probe 采集原始值 |
| `SemanticValueGraphTests.DescribesNestedObjectsCollectionsAndPrivateFields` | object/array/dictionary/private field 都生成节点 | 用受控图描述 Mod 状态 | `Core/AutoTestSemanticValueGraph.cs` |
| `SemanticValueGraphTests.MarksRepeatedReferencesInsteadOfRecursingForever` | 循环对象变 reference 节点 | 防止无限递归 | visited reference set |
| `SemanticValueGraphTests.TruncatesWhenMaxDepthIsReached` | MaxDepth 后 Truncated=true | Evidence 大小受控 | Graph options |
| DLL `RimBridgeServer.Core.SemanticValueGraph` | `Describe/DescribeObject/DescribeEnumerable/DescribeDictionary/TryDescribeScalar` | scalar/container/object 分派明确 | 我们按同样职责拆私有方法，增加危险类型黑名单 |
| `UiLayoutTargetIdsTests.ParsesSurfaceTargetIds` | `ui-surface:capture:surface` | UI target ID 可解析 | 本 Goal 只借鉴 ID 规则；完整 layout 留 Goal 3 |
| `UiLayoutTargetIdsTests.ParsesElementTargetIds` | `ui-element:capture:surface:element` | 元素属于某次 capture，旧 capture 可过期 | Goal 3 的 bridge UI 调用直接使用其返回 ID |
| DLL `RimBridgeServer.RimBridgeUiWorkbench` | `GetUiLayoutResponse/ClickUiTargetResponse/ScrollUiTargetResponse/TryResolveClipArea` | UI 捕获、交互、截图裁剪共享 target ID | 不在本 Goal 重造完整 Workbench；默认 UI 只做语义 open/close |
| README `rimworld/list_messages/list_letters/list_alerts` | 结构化通知而不是 Player.log | 用户可见结果也应成为断言 | `AutoTestNotificationProbe` |
| README `rimworld/get_selection_semantics/list_selected_gizmos/execute_gizmo` | 状态读取和动作分离 | Probe 不应在读取时产生副作用 | 所有 Probe 只读，动作放 Adapter/Operation |

### 2.3 参考工具与现有测试族的直接映射

| RimBridge 公开能力 | 我们不复制的通用能力 | 本 Goal 需要在独立 AutoTest 中实现的最小等价语义 |
| --- | --- | --- |
| `rimbridge/wait_for_game_loaded` | 外部可等待加载 | Bootstrap 内的 `automation.ready` 非阻塞 Wait |
| `rimworld/get_selection_semantics` | 通用选择描述 | 当前测试目标的稳定 ID、inspect string、选中状态 |
| `rimworld/list_selected_gizmos` | 通用分组 Gizmo | Skill 测试能证明目标 Ability/Gizmo 存在和未过期 |
| `rimworld/list_messages/list_letters/list_alerts` | 通用通知 API | Case 开始/结束 Snapshot Diff，用于 Message/Letter 断言 |
| `rimworld/get_ui_state` | 完整窗口栈和输入状态 | WindowProbe 输出窗口类型、顺序、目标绑定和 rect |
| `rimworld/get_ui_layout` | 完整控件树 | 本 Goal 不重造，只保证 Goal 3 可以使用 bridge 的布局工具 |
| `rimworld/get_cell_info/get_cells_info` | 通用地图格检查 | Combat/cleanup Probe 最少验证 Thing、位置和 pocket map 是否仍存在 |
| `rimworld/take_screenshot/screenshot_cell_rect` | 通用目标裁剪 | 独立模式继续用现有 ScreenshotService，但 Capture 必须关联 Assertion/Operation |

### 2.4 现有代码的明确修改入口

| 当前文件/类型 | 首要修改内容 |
| --- | --- |
| `AutoTestBootstrap.Tick()` | 使用 Readiness Probe + WaitHandle 替换单纯 `StartupMapSettleTicks` 判断 |
| `AutoTestUiAdapters.AutoTestUiClickSimulator` | 从默认路径移除 `user32/mouse_event/Thread.Sleep` |
| `AutoTestRunner.InfoCardModule` | WindowProbe、InspectString、绑定目标断言 |
| `AutoTestRunner.ContentModule` | Thing/Recipe/BillStack 状态断言 |
| `AutoTestRunner.TurretModule` | target acquired、projectile、damage、cleanup 四类语义断言 |
| `AutoTestRunner.RangedWeaponModule` | 根据 TargetingParameters/Verb 分类 pawn/location/building/beam/projectile |
| `AutoTestRunner.MeleeWeaponModule` | equipment、verb、job/distance、damage 断言 |
| `AutoTestRunner.SkillPocketMapModule` | ability ownership、force bypass、cast、cooldown/charge/Hediff 断言 |
| `AutoTestRunner.CombatPawnModule` | 初始/受击后 Pawn Snapshot 对比 |
| `AutoTestRunner.ThreeViewModule` | 实际 body/race/apparel/weapon 与计划值、相机/速度恢复断言 |
| `LogSyncAutoTestSpecDef` | allowedErrorPatterns/allowedWarningPatterns 和未来领域预期字段 |

## 三、目标和非目标

### 目标

1. 建立稳定的 Map/Thing/Pawn/Window/Selection/Gizmo 引用。
2. 建立自动化就绪探针，替换“地图出现后固定等 10 tick”的粗略判定。
3. 建立 Thing、Pawn、Combat、Ability、Window、Notification 等语义 Probe。
4. 建立统一 Assertion Library。
5. 把 InfoCard、Recipe、Turret、Ranged、Melee、Skill、CombatPawn、ThreeView 接入语义断言。
6. 默认将测试窗口内新增、未允许的 Error/Fatal 视为失败证据。
7. 移除默认 UI 路径对 Win32 鼠标和 `Thread.Sleep` 的依赖。
8. 让失败报告包含 expected、actual、最后状态快照和相关日志。

### 非目标

1. 本 Goal 不实现 RimBridgeServer Companion。
2. 本 Goal 不实现通用 UI Layout 捕获器或完整 Widget Hook 系统。
3. 本 Goal 不做 Lua/JSON 通用脚本执行器。
4. 本 Goal 不要求所有第三方 Mod 私有状态都可自动读取。
5. 本 Goal 不以像素级截图比对作为默认通过条件。

## 四、目录和类型设计

新增：

```text
source/AutoTestProject/
  Contracts/
    AutoTestTargetContracts.cs
    AutoTestSemanticContracts.cs
    AutoTestNotificationContracts.cs
  Core/
    AutoTestTargetIds.cs
    AutoTestSelectionFingerprint.cs
    AutoTestReadinessEvaluator.cs
    AutoTestSemanticValueGraph.cs
    AutoTestAssertions.cs
    AutoTestErrorPolicy.cs
  Game/
    Probes/
      AutoTestReadinessProbe.cs
      AutoTestMapProbe.cs
      AutoTestThingProbe.cs
      AutoTestPawnProbe.cs
      AutoTestCombatProbe.cs
      AutoTestAbilityProbe.cs
      AutoTestWindowProbe.cs
      AutoTestSelectionProbe.cs
      AutoTestNotificationProbe.cs
    Adapters/
      AutoTestSemanticUiAdapter.cs
      AutoTestGizmoAdapter.cs
      AutoTestGameTargetResolver.cs
  Integration/
    InfoCardSemanticAssertions.cs
    RecipeSemanticAssertions.cs
    TurretSemanticAssertions.cs
    WeaponSemanticAssertions.cs
    SkillSemanticAssertions.cs
    CombatPawnSemanticAssertions.cs
    ThreeViewSemanticAssertions.cs
```

继续将纯 Contracts/Core 文件链接进 `AutoTest.Core.Tests`。

## 五、稳定 Target ID

### 5.1 ID 格式

统一格式：

```text
map:<uniqueId>
thing:<thingId>
pawn:<thingId>
window:<captureId>:<ordinal>:<typeFingerprint>
selection:<fingerprint>
gizmo:<selectionFingerprint>:<ordinal>:<semanticFingerprint>
case:<caseId>
operation:<operationId>
capture:<captureId>
```

ID 只用于本次运行期，不承诺跨存档永久稳定。Evidence 中必须同时保存人类可读标签和 defName。

### 5.2 Target DTO

```csharp
internal sealed class AutoTestTargetRef
{
    public string Id { get; set; }
    public string Kind { get; set; }
    public string Label { get; set; }
    public string DefName { get; set; }
    public int MapId { get; set; }
    public int ThingId { get; set; }
    public string Fingerprint { get; set; }
}
```

`AutoTestGameTargetResolver` 负责从 ID 重新解析当前对象，并返回：

```text
resolved
missing
stale_map
wrong_kind
fingerprint_mismatch
```

不得长期持有已经销毁的 Thing/Map 引用作为唯一判定依据。

### 5.3 Selection/Gizmo 指纹

Selection Fingerprint 由当前选择对象的稳定 ID 按显示顺序组成并哈希。

Gizmo ID 必须嵌入 Selection Fingerprint。执行前重新计算：

- 指纹一致：允许继续。
- 指纹不一致：返回 `runtime.selection_changed`，禁止执行旧 Gizmo。

本 Goal 至少实现 Gizmo 的列出和验证基础能力；真正通过外部 Bridge 调用 Gizmo 属于 Goal 3，但 Skill 用例可以在游戏内直接使用同一 Adapter。

## 六、Automation Readiness

### 6.1 当前问题

`AutoTestBootstrap` 当前主要判断：

- `ProgramState.Playing`
- 找到 Map
- 再固定等待 `StartupMapSettleTicks`

这不能覆盖：

- `LongEventHandler` 仍有事件。
- 当前地图尚未成为 `Find.CurrentMap`。
- 画面仍在 Fade。
- WindowStack/Selector/TickManager 尚未可用。
- 地图存在但游戏对象尚未稳定。

### 6.2 Readiness DTO

```csharp
internal sealed class AutoTestReadinessSnapshot
{
    public bool HasCurrentGame { get; set; }
    public int MapCount { get; set; }
    public bool HasCurrentMap { get; set; }
    public string ProgramState { get; set; }
    public bool LongEventPending { get; set; }
    public bool TickManagerReady { get; set; }
    public bool WindowStackReady { get; set; }
    public bool SelectorReady { get; set; }
    public bool ScreenFadeClear { get; set; }
    public bool GameDataReady { get; set; }
    public bool MapDataReady { get; set; }
    public bool Playable { get; set; }
    public bool VisualReady { get; set; }
    public bool AutomationReady { get; set; }
    public string Summary { get; set; }
}
```

`AutoTestBootstrap` 使用 WaitHandle 等待 `automation.ready` 连续满足至少 3 次，而不是只等待固定 10 tick。超时必须把最后 Readiness Snapshot 写入 Evidence 和日志。

不要删除合理的最小稳定帧要求；将它表达为 `RequiredStablePolls`，而不是散落常量。

## 七、Semantic Value Graph

参考 RimBridgeServer 的 `SemanticValueGraph`，实现受控的只读对象描述器，主要用于 ModSettings、Comp、HediffComp 和测试专用状态。

### 7.1 输出节点

```csharp
internal sealed class AutoTestSemanticNode
{
    public string Name { get; set; }
    public string Path { get; set; }
    public string ValueKind { get; set; }
    public string TypeName { get; set; }
    public string Value { get; set; }
    public bool Truncated { get; set; }
    public bool IsReference { get; set; }
    public List<AutoTestSemanticNode> Children { get; set; }
}
```

### 7.2 安全限制

默认：

- `MaxDepth = 4`
- `MaxChildrenPerNode = 100`
- `MaxStringLength = 4096`
- `MaxCollectionItems = 100`
- 使用引用相等集合防止循环。
- 跳过委托、线程、Stream、Unity Texture/Material 等危险或巨大对象。
- 属性 getter 抛异常时记录节点错误，不中断整棵图。

Core 中只实现通用反射图；Game 层决定允许描述哪些对象。禁止把整个 `Find`、`Map` 或 `Pawn` 对象图直接丢进去。

## 八、语义 Probe 设计

所有 Probe 返回纯 DTO，并提供一行可读 `Summary`，供 Wait 的 LastSnapshot 和报告使用。

### 8.1 Thing Probe

至少输出：

- target ID、thingID、defName、label。
- spawned/destroyed。
- mapId、position、occupied rect。
- stackCount、hitPoints/maxHitPoints。
- faction。
- forbidden、burning、selected。
- Comp 类型列表。

### 8.2 Pawn Probe

至少输出：

- alive/dead/downed/destroyed。
- drafted。
- position/map。
- current job defName、job target 摘要。
- mental state。
- health percentage。
- Hediff 列表：defName、severity、stage、ticksToDisappear（可获得时）。
- equipment/apparel/inventory defName 列表。
- abilities：defName、是否可用、cooldown/charges（可获得时）。

### 8.3 Combat Probe

必须能在测试开始和结束生成可比较快照：

```csharp
internal sealed class AutoTestCombatSnapshot
{
    public string AttackerId { get; set; }
    public string TargetId { get; set; }
    public int Tick { get; set; }
    public float TargetHealthPercent { get; set; }
    public int TargetHitPoints { get; set; }
    public bool TargetDowned { get; set; }
    public bool TargetDead { get; set; }
    public string CurrentTargetId { get; set; }
    public string CurrentJobDefName { get; set; }
    public int ActiveProjectileCount { get; set; }
    public List<string> ActiveProjectileDefNames { get; set; }
    public string Summary { get; set; }
}
```

不同武器/炮塔内部字段访问封装到 Adapter，失败时返回 `supported=false + reason`。

### 8.4 Ability Probe

至少支持：

- caster 是否拥有目标 AbilityDef。
- gizmo/ability 是否 disabled 及原因。
- cast 前后 cooldown/charges。
- cast 后目标 Hediff/状态变化。
- ability targeting 类型摘要。

不要把“调用 Cast 方法没有抛异常”视为施放成功。

### 8.5 Window Probe

本 Goal 不做完整控件树，但必须输出：

- WindowStack 中窗口类型和顺序。
- topmost window。
- `IsOpen`。
- windowRect。
- closeOnAccept/closeOnCancel/closeOnClickedOutside/doCloseX。
- 对 InfoCard/BillConfig：绑定的 Def/Bill/Recipe 标识。

私有字段只能在 `AutoTestWindowProbe` 内通过缓存的 FieldInfo 获取。

### 8.6 Notification Probe

支持游标或 Snapshot Diff：

- Messages：文本、类型、目标信息、出现 tick/时间。
- Letters：label、text、def、look target、是否可 dismiss。
- Alerts：label、explanation、culprit 目标摘要。

优先公开 API；Messages 等私有列表允许在单独 Adapter 内使用 Harmony `AccessTools`。不可用时标记 capability unavailable，不能让其他断言失效。

## 九、统一 Assertion Library

在 Goal 1 Evidence Builder 基础上增加领域断言：

```text
TargetExists
ThingSpawned
ThingOnExpectedMap
WindowOpenForTarget
WindowClosed
PawnHasHediff
PawnLacksHediff
PawnJobIs
PawnDraftedIs
HealthDecreased
HitPointsDecreased
ProjectileObserved
TargetAcquired
AbilityCooldownStarted
MessageAppeared
LetterAppeared
NoUnexpectedSevereLogs
SemanticPathEquals
SemanticPathContains
```

每个断言必须写入：

- 稳定 assertionId。
- expected。
- actual。
- success。
- 关联 operationId。
- 失败 Details。

禁止只写 `success=false, details=failed`。

## 十、Error/Fatal 自动失败策略

Goal 1 已经收集日志；本 Goal 引入 `AutoTestErrorPolicy`。

### 10.1 默认规则

在单个 Case 的观察窗口中：

- 新 `error/fatal` 默认导致 `NoUnexpectedSevereLogs` 断言失败。
- AutoTest 自己用于说明已知用例失败的日志不重复制造第二个根因，但仍保留在 Evidence。
- warning 默认不失败，只计数。
- 日志必须按 afterSequence 限定，不能把启动期旧红字算到当前 Case。

### 10.2 Allowlist

在 `LogSyncAutoTestSpecDef` 增加可选字段：

```csharp
public List<string> allowedErrorPatterns;
public List<string> allowedWarningPatterns;
```

匹配默认使用大小写不敏感的简单 substring；如增加 regex，必须显式字段区分，避免用户输入意外成为正则。

报告中必须列出被 allow 的日志和命中的规则，不能静默丢弃。

## 十一、现有测试族的具体迁移

### 11.1 InfoCard 和 Recipe

修改：

- `AutoTestRunner.InfoCardModule.cs`
- `AutoTestUiAdapters.cs`
- `AutoTestRunner.ContentModule.cs`

必须增加：

1. 打开前 Window Snapshot。
2. 打开后 `WindowOpenForTarget`。
3. 目标 Thing/Def/Hediff 的稳定 ID。
4. 对 ISelectable 读取 `GetInspectString()`，记录非空、长度和摘要；不把本地化全文作为跨语言固定值。
5. RecipeBill：断言 Bill 已加入正确 BillStack、recipeDef 正确、关闭清理后 Bill 被移除。
6. 截图登记 Capture，但截图失败只在要求截图时才使 Capture 操作失败。

### 11.2 Turret

修改 `AutoTestRunner.TurretModule.cs`。

将原来的“最终有人受伤即可”拆成至少以下步骤和断言：

1. pocket map 创建成功且注册。
2. turret spawned，位置有效。
3. power comp 可用且供电状态为 true；不适用时记录 skipped assertion。
4. reload/ammo 准备成功；CE/第三方路径记录使用的 Adapter。
5. enemy targets spawned 且存活。
6. `target_acquired`：观察到有效目标或等价攻击状态。
7. `projectile_observed`：投射物炮塔必须观察到投射物；beam/instant 类型允许声明不适用。
8. `damage_observed`：至少一个目标 HP/HealthPercent 降低，或死亡/倒地。
9. 战斗窗口无意外 Error/Fatal。
10. cleanup 后 pocket map 不再存在于 `Find.Maps`/pocket map 集合。

每个 Wait 超时都要带最后 CombatSnapshot。

### 11.3 Ranged Weapon

修改 `AutoTestRunner.RangedWeaponModule.cs`。

在现有普通直射流程之外，先通过 Verb/TargetingParameters 分类：

```text
pawn_target
location_target
building_target
projectile
beam_or_instant
unsupported
```

不要再强迫 `canTargetPawns=false` 的地点武器攻击 Pawn。

至少覆盖：

- 正确装备。
- 可用攻击 Verb。
- Job/Verb 进入攻击状态。
- 按类型观察 projectile 或直接伤害/爆炸结果。
- 目标状态发生预期变化。
- unsupported 必须给出明确原因，不可伪装为 passed。

### 11.4 Melee Weapon

修改 `AutoTestRunner.MeleeWeaponModule.cs`。

至少断言：

- 武器装备在主手。
- 攻击者获得有效 melee verb。
- 攻击者实际进入攻击 Job 或到达可攻击距离。
- 目标 HP/HealthPercent 下降。
- 测试期间攻击者和目标没有意外消失或换地图。

### 11.5 Skill/Ability

修改 `AutoTestRunner.SkillPocketMapModule.cs`。

至少断言：

1. caster 确实拥有 AbilityDef。
2. 施放前可用或记录被强制绕过的原因。
3. 目标类型和实际目标匹配。
4. 施放动作被接受。
5. cooldown/charge/目标状态至少一种发生预期变化。
6. 如果 Ability 定义预期 Hediff，则目标出现对应 Hediff。
7. 产生的 Message/Letter 按 Spec 选择是否断言。

对现有 `ShouldForceAbilityForAutoTest` 路径，Evidence 中必须显式记录 `forceBypass=true`，避免把被测试框架强制通过误认为原生可用。

### 11.6 CombatPawn

修改 `AutoTestRunner.CombatPawnModule.cs`。

至少记录：

- 初始装备、服装、bodyType、race、health。
- 第一次受击前后快照。
- 累计受击次数或可观测 Damage 变化。
- apparel/graphic 刷新成功与否。
- 目标在完整观察期内是否仍有效。

### 11.7 ThreeView

修改 `AutoTestRunner.ThreeViewModule.cs` 和 `AutoTestThreeViewService.cs`。

ThreeView 主要是视觉证据，但仍要增加功能断言：

- 目标 Pawn/Thing 成功生成。
- bodyType/race/apparel/weapon 与计划值一致。
- 三个方向切换后对象仍在同一地图和位置。
- 每个方向 Capture 操作完成。
- 相机和速度在结束后恢复到进入测试前的状态。

不要尝试在本 Goal 实现像素相似度评分。

## 十二、UI 路径改造

当前 `AutoTestUiAdapters.cs` 使用 `user32.dll`、`SetCursorPos`、`mouse_event` 和 `Thread.Sleep(16)`。

本 Goal 要求：

1. 默认功能测试路径改成进程内语义操作：
   - 直接持有 Window Handle。
   - `Window.Close(false)` 或 `WindowStack.TryRemove`。
   - 在下一次 Repaint 通过 WaitHandle 确认关闭。
2. 禁止默认路径移动真实用户鼠标。
3. 禁止 OnGUI/主线程 `Thread.Sleep`。
4. 如果必须保留旧物理点击代码，只允许放入显式 `PhysicalUiCompatibility` Adapter：
   - 默认关闭。
   - isolated desktop 下禁止调用。
   - 报告必须记录使用了兼容路径。
5. 真正测试控件点击和 UI 布局的路径留到 Goal 3，由 RimBridgeServer 的 background-safe UI 能力完成。

## 十三、结果和报告升级

旧 Markdown 表格继续保留，但每个失败 Case 增加：

```markdown
### Failed Assertions

- target_acquired
  - Expected: valid target within 3600 ticks
  - Actual: currentTarget=Invalid, job=Wait_Combat, projectiles=0
  - LastSnapshot: ...

### Related Logs

- [warning] ...
- [error] ...

### Operation Timeline

| Step | Status | DurationMs | Summary |
```

`Result.json` 的每个 result 增加：

```json
{
  "assertionCount": 8,
  "failedAssertionIds": [],
  "operationIds": [],
  "evidenceCaseKey": "...",
  "unexpectedSevereLogCount": 0
}
```

Evidence.json 保持 Goal 1 schemaVersion；如果必须扩字段，只增加可选字段，不破坏现有字段含义。

## 十四、测试要求

### 14.1 Core 测试

至少增加：

- Target ID 创建和解析。
- Selection Fingerprint 确定性与顺序变化。
- Gizmo ID 读取选择指纹。
- SemanticValueGraph 循环引用、深度截断、集合上限、getter 异常。
- ErrorPolicy level、游标、allowlist。
- Assertion expected/actual 输出。
- Readiness 纯逻辑评估。

### 14.2 Game Adapter 定向测试

无法脱离 RimWorld 的 Probe 使用真实游戏定向回归验证。至少分别跑：

```powershell
.\.tools\RimDevTools.exe test run --mode auto --scope content --defname <稳定InfoCard目标> --capture-screenshots
.\.tools\RimDevTools.exe test run --mode auto --scope turrets --defname <稳定炮塔DefName> --capture-screenshots
.\.tools\RimDevTools.exe test run --mode auto --scope rangedweapons --defname <稳定远程武器DefName> --capture-screenshots
.\.tools\RimDevTools.exe test run --mode auto --scope meleeweapons --defname <稳定近战武器DefName> --capture-screenshots
.\.tools\RimDevTools.exe test run --mode auto --scope skillpocketmaps --defname <稳定技能DefName> --capture-screenshots
.\.tools\RimDevTools.exe test run --mode auto --scope combatpawns --defname <稳定目标DefName> --capture-screenshots
.\.tools\RimDevTools.exe test run --mode auto --scope threeviews --defname <稳定目标DefName> --capture-screenshots
```

如果 CLI 当前 Scope 名称与示例不同，以 `AutoTestSelectionParser` 的实际支持值为准，并在完成报告中写出实际命令。

### 14.3 全量兼容回归

至少对一个已知内容量较大的目标 Mod 跑一次现有 Full/Standard 组合，确认：

- manifestMissing=0。
- 原有发现数量没有无理由下降。
- unsupported 有明确分类，不被计为 passed。
- Evidence 文件大小受控。
- 无 Win32 鼠标干扰。

## 十五、实施顺序

1. 实现 Target ID、Readiness 纯逻辑和单元测试。
2. 实现 SemanticValueGraph 和安全限制。
3. 实现 Game Target Resolver 与基础 Probe。
4. 实现 Assertion Library 和 ErrorPolicy。
5. 迁移 InfoCard/Recipe。
6. 迁移 Turret/Ranged/Melee。
7. 迁移 Skill/Ability。
8. 迁移 CombatPawn/ThreeView。
9. 改造默认 UI 关闭路径，移除 `Thread.Sleep`。
10. 更新 Report/Validator。
11. 定向游戏回归。
12. 全量兼容回归。

每迁移一类测试后先跑该 Scope，不要全部修改后才首次启动游戏。

## 十六、验收标准

- [ ] AutomationReady 使用结构化 Probe 和稳定轮询，不再只依赖固定 10 tick。
- [ ] 所有主要测试族至少有三个真实语义断言。
- [ ] 炮塔/武器失败时能区分未装备、无 Verb、未锁敌、无投射物、无伤害、目标丢失。
- [ ] Skill 能报告 force bypass、cooldown/charge/Hediff 等真实结果。
- [ ] Case 窗口内意外 Error/Fatal 默认导致断言失败，并支持显式 allowlist。
- [ ] 默认 UI 路径不调用 Win32 鼠标，不使用 `Thread.Sleep`。
- [ ] 所有 Target 使用稳定 ID；旧 Gizmo 在 Selection 变化后不能执行。
- [ ] SemanticValueGraph 有深度、数量和循环引用保护。
- [ ] 报告包含 expected、actual、last snapshot、相关日志和操作时间线。
- [ ] Core 和 RimDevTools 自动化测试通过。
- [ ] 至少完成一轮有代表性的全量游戏回归。

## 十七、风险和禁止事项

1. 禁止为了“更深”而无边界反射整个 Pawn/Map 对象图。
2. 禁止依赖本地化文本全文作为跨语言唯一断言。
3. 禁止把 unsupported 当 passed；应明确 skipped/unsupported，并由 suite 策略决定是否允许。
4. 禁止在每个 Session 再写一套新的 Probe DTO。
5. 禁止继续新增 Win32 前台输入作为默认自动化路径。
6. 禁止让截图成功掩盖功能断言失败。
7. 禁止忽略清理后的地图、窗口、选择、速度和相机恢复断言。

## 十八、提供给 Goal 3 的公共运行时门面

Goal 2 末尾新增一个很薄的公共只读门面，为 Goal 3 Companion 做准备；不要在本 Goal 引用 RimBridge SDK。

建议：

```csharp
public static class AutoTestRuntimeApi
{
    public static AutoTestRuntimeStatusDto GetStatus();
    public static IReadOnlyList<AutoTestSuiteDescriptorDto> ListSuites();
    public static AutoTestRunRequestResultDto RequestRun(AutoTestRunRequestDto request);
    public static AutoTestCancelResultDto Cancel(string runId);
    public static AutoTestEvidenceSnapshotDto GetEvidence(string runId);
}
```

要求：

- DTO 全部是公共、无 RimWorld 类型、可序列化对象。
- `RequestRun` 只入队，不在调用线程同步跑完整测试。
- `GetStatus/GetEvidence` 返回快照，不暴露内部可变集合。
- 同一时间只允许一个活动运行；冲突返回稳定错误码 `autotest.run_already_active`。
- 未找到 runId 返回 `autotest.run_not_found`。

Goal 3 将只通过这个门面控制现有 AutoTest，不允许 Companion 直接操作 `AutoTestRunner` 私有字段。
