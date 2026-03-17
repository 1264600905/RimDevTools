# AutoTestRunner 重构设计方案

## 背景

当前 [AutoTestRunner.cs](D:/RimworldProject/环世界日志同步mod/source/AutoTestProject/AutoTestRunner.cs) 已经增长到约 `2790` 行，实际上承担了过多职责：

- 自动测试总调度
- 内容枚举与筛选
- InfoCard UI 自动化
- 建筑 / 物品 / 地形 / 配方 / Hediff 测试
- 炮塔口袋地图测试
- 口袋地图生成、接线、敌人生成、炮手生成
- 截图
- 结果收集与状态流转
- 布局分配、辅助工具、日志与注释拼接

这已经不是“一个 runner 类”，而是“整个自动测试系统的临时聚合点”。  
后续你已经明确提出：

- 口袋地图测试以后还会有新的类型
- 信息卡测试本身也是独立模块
- 所有模块都需要统一支持截图

所以当前文件结构已经不适合继续增量堆逻辑，应该尽快拆成模块化架构。

## 当前问题

### 1. 一个类同时承担了 5 层职责

从方法分布上看，`AutoTestRunner` 目前至少包含这些层级：

- 启动和主循环
  - `Initialize`
  - `Tick`
  - `ProcessUi`
  - `Finish`
- 内容测试入口
  - `StartThingSession`
  - `StartTerrainSession`
  - `StartRecipeSession`
  - `StartHediffSession`
- UI / InfoCard 状态机
  - `ProcessWaitingForPreOpenSettle`
  - `ProcessWaitingForInfoCardOpen`
  - `ProcessWaitingBeforeClose`
  - `ProcessWaitingForInfoCardClose`
  - `TryCaptureInfoCardScreenshot`
- 炮塔口袋地图状态机
  - `BeginTurretTestSession`
  - `StartNewTurretSession`
  - 内嵌 `TurretTestSession.Update`
  - `ConnectPower`
  - `SpawnEnemies`
  - `SpawnOperatorPawn`
  - `TryCaptureTurretScreenshot`
- 公共基础设施
  - 输出目录与结果对象
  - 批量限制
  - 截图文件名
  - 布局分配
  - `ResolveStuff`
  - 研究解锁
  - 注释拼接、路径清理

这种结构有一个很明显的问题：任何新功能都会继续向这个类灌入逻辑，而不是落到自己的模块里。

### 2. 炮塔测试被写成“内嵌特例”

当前炮塔测试不是一个独立子系统，而是：

- 顶层 `AutoTestRunner`
- 内嵌 `TurretTestSession`
- 一堆只服务于炮塔测试的私有方法

这会带来两个后果：

- 新的口袋地图测试类型只能继续复制 `TurretTestSession` 风格的内嵌状态机
- 公共能力无法抽象复用，例如：
  - 口袋地图生成
  - 口袋地图清理
  - 目标地图截图
  - 会话状态日志
  - 战斗观察窗口

### 3. InfoCard 测试与口袋地图测试没有共享抽象

InfoCard 测试本质上也是“一个有生命周期的测试会话”：

- 预备
- 打开
- 稳定
- 截图
- 关闭
- 结果回收

口袋地图测试同样是“一个有生命周期的测试会话”：

- 建图
- 生成对象
- 执行
- 截图
- 清理

但现在这两套状态机没有共同接口，也没有统一调度模型。  
这意味着后续如果出现：

- 口袋地图建筑测试
- 口袋地图事件测试
- 口袋地图 AI 行为测试
- 非 InfoCard 的 UI 窗口测试

就只能继续在 `AutoTestRunner` 里添加新的字段、队列、状态和特判。

### 4. 截图能力已经出现重复实现

当前至少有两套截图入口：

- `TryCaptureInfoCardScreenshot`
- `TryCaptureTurretScreenshot`

两者有很多共性：

- 截图是否启用
- 结果对象记录 `ScreenshotPath`
- 输出目录计算
- 文件名清理
- 异常兜底
- 日志记录

但因为没有独立的截图服务，现在逻辑已经开始重复。  
后续如果再加：

- 口袋地图建筑测试结束截图
- 配方产物截图
- Hediff 应用前后截图

重复会继续扩散。

### 5. 数据模型过于松散

当前 `AutoTestWorkItem` 和 `AutoTestResult` 都是“所有测试共用一个平面对象”，导致：

- 炮塔测试需要的字段没有类型化
- InfoCard 测试需要的字段也没有类型化
- `Notes` 被当成通用垃圾桶

这在短期内方便，但长期会导致：

- 状态含义只能靠字符串约定
- 模块间很难建立明确契约
- 新模块只能继续拼接 `Notes`

## 重构目标

这次重构不应该只是“把 3000 行拆成 10 个文件”，而应该顺便建立可以继续扩展的架构。

目标应该是：

1. `AutoTestRunner` 只保留总调度职责
2. 每种测试类型都有独立入口模块
3. 统一抽象“测试会话”
4. 统一抽象“截图服务”
5. 统一抽象“口袋地图会话”
6. 统一抽象“结果与阶段”
7. 让新增测试类型不需要再改动主 Runner 的主体结构

## 建议的目录拆分

建议将 `source/AutoTestProject` 下继续拆子目录，而不是所有类继续平铺。

建议结构：

```text
source/AutoTestProject/
  Core/
    AutoTestRunner.cs
    AutoTestContext.cs
    AutoTestContracts.cs
    AutoTestKinds.cs
    AutoTestResult.cs
    AutoTestWorkItem.cs
    AutoTestSessionScheduler.cs

  Bootstrap/
    AutoTestBootstrap.cs
    AutoTestLogWindowControl.cs

  Reporting/
    AutoTestReportWriter.cs
    AutoTestScreenshotService.cs
    AutoTestScreenshotRequest.cs

  Content/
    ContentAutoTestPlanner.cs
    ThingInfoCardTestSession.cs
    TerrainInfoCardTestSession.cs
    RecipeInfoCardTestSession.cs
    HediffInfoCardTestSession.cs
    SharedHediffPawnService.cs
    RecipeWorktableService.cs
    TestMapLayoutService.cs

  PocketMap/
    PocketMapSessionBase.cs
    PocketMapFactory.cs
    PocketMapCleanupService.cs
    PocketMapScreenshotFocusService.cs
    PocketMapStateLogger.cs

  PocketMap/Turret/
    TurretPocketMapPlanner.cs
    TurretPocketMapSession.cs
    TurretSpawnService.cs
    TurretPowerNetworkBuilder.cs
    TurretEnemySpawner.cs
    TurretOperatorService.cs
    TurretCombatObserver.cs

  Ui/
    AutoTestUiAdapters.cs
    InfoCardCloseTrace.cs
```

## 建议的核心抽象

### 1. `IAutoTestSession`

这是最重要的抽象。  
所有测试类型都应该被统一看成“一个可以被调度器驱动的会话”。

建议接口：

```csharp
internal interface IAutoTestSession
{
    AutoTestResult Result { get; }
    bool Completed { get; }
    void Start();
    void Tick();
    void ProcessUi();
}
```

意义：

- InfoCard 测试可以实现它
- 炮塔口袋地图测试可以实现它
- 以后新的口袋地图测试类型也可以实现它

这样 `AutoTestRunner` 就不再需要：

- `activeSession`
- `activeTurretSessions`
- `pendingItems`
- `pendingTurretDefs`

这种“每种类型各一套字段”的模式。

### 2. `IAutoTestPlanner`

Runner 不应该自己枚举所有 Def 并自己知道怎么转成会话。  
应该由 planner 负责：

- 扫描目标 mod
- 过滤类型
- 生成 `IAutoTestSession`

建议接口：

```csharp
internal interface IAutoTestPlanner
{
    IEnumerable<IAutoTestSession> BuildSessions(AutoTestContext context);
}
```

对应实现：

- `ContentAutoTestPlanner`
- `TurretPocketMapPlanner`
- 未来可以有 `PocketMapBuildingPlanner`

### 3. `AutoTestContext`

现在很多服务都隐式依赖 `AutoTestRunner` 本体。  
这会形成强耦合，例如当前 `TurretTestSession` 持有 `owner`。

建议改成显式上下文对象：

```csharp
internal sealed class AutoTestContext
{
    public LogSyncSettings Settings;
    public Map MainMap;
    public ModContentPack TargetMod;
    public AutoTestRunArtifacts RunArtifacts;
    public List<AutoTestResult> Results;
}
```

这样各模块不再依赖 Runner 本身，而依赖稳定的上下文。

### 4. `IAutoTestScreenshotService`

截图要从“每个模块自己写”改成统一服务。

建议接口：

```csharp
internal interface IAutoTestScreenshotService
{
    bool TryCapture(AutoTestScreenshotRequest request);
}
```

请求对象里应该明确：

- 截图类型
- 截图目标
- 是否强制截图
- 文件名前缀
- 结果对象
- 是否需要先切图 / 对焦

这样：

- InfoCard 截图
- 口袋地图结束截图
- 失败瞬间截图

都走同一条链路。

### 5. `PocketMapSessionBase`

以后口袋地图测试还会扩展，所以不应该让炮塔测试把所有口袋地图能力独占。

建议抽一个基类：

```csharp
internal abstract class PocketMapSessionBase : IAutoTestSession
{
    protected Map TestMap;
    protected PocketMapParent Parent;
    protected abstract void TickSession();
    protected abstract bool ShouldCaptureBeforeCleanup();
}
```

公共能力放到基类：

- 创建口袋地图
- 记录父对象
- 地图存活检查
- 清理
- 结束前截图
- 切回主地图

这样未来新类型：

- `TurretPocketMapSession`
- `BuildingPocketMapSession`
- `RaidPocketMapSession`

都能复用。

## 建议的模块边界

### A. 主调度器：`AutoTestRunner`

重构后的 `AutoTestRunner` 应该只做这些事情：

- 构建 `AutoTestContext`
- 选择 planner
- 获取所有 session
- 驱动 session
- 汇总结果
- 收尾写报告

不应该再包含：

- 炮塔生成逻辑
- 配方生产逻辑
- 布局算法
- 截图细节
- 口袋地图清理细节

### B. InfoCard 模块

InfoCard 测试建议单独拆为一套家族：

- `ThingInfoCardTestSession`
- `TerrainInfoCardTestSession`
- `RecipeInfoCardTestSession`
- `HediffInfoCardTestSession`

它们共享：

- `InfoCardSessionBase`
- `IAutoTestUiHandle`
- `InfoCardCloseTrace`
- `AutoTestUiClickSimulator`

这样 UI 状态机会集中在一个模块里，不会散落在 Runner。

### C. 口袋地图模块

口袋地图模块建议拆成公共层和炮塔专用层。

公共层：

- `PocketMapFactory`
  - 创建 `PocketMapParent`
  - 调用 `MapGenerator.GenerateMap`
  - 注入 `PlayerStartSpot`
- `PocketMapCleanupService`
  - 统一销毁
  - 切回主地图
- `PocketMapScreenshotFocusService`
  - 切图
  - 相机定位
- `PocketMapStateLogger`
  - 统一记录地图状态

炮塔专用层：

- `TurretSpawnService`
- `TurretPowerNetworkBuilder`
- `TurretEnemySpawner`
- `TurretOperatorService`
- `TurretCombatObserver`

这样未来如果做“口袋地图建筑测试”，只需要换掉专用层，不用再动公共层。

### D. 共享服务

建议把这些工具类独立出去：

- `ResolveStuff` -> `ThingSpawnHelper`
- 布局分配 -> `TestMapLayoutService`
- 共享 Hediff Pawn -> `SharedHediffPawnService`
- 配方工作台缓存 -> `RecipeWorktableService`

这些现在都藏在 Runner 里，导致读代码时很难看出哪些是“测试流程”，哪些只是“支持服务”。

## 建议的会话模型

### 现状

当前会话模型分裂成两套：

- `ActiveSession` 用于 InfoCard 内容测试
- `TurretTestSession` 用于炮塔口袋地图测试

这本身就说明抽象已经缺失了。

### 重构建议

统一为：

```csharp
internal abstract class AutoTestSessionBase : IAutoTestSession
{
    protected readonly AutoTestContext Context;
    public AutoTestResult Result { get; }
    public bool Completed { get; protected set; }
    public virtual void Start() {}
    public virtual void Tick() {}
    public virtual void ProcessUi() {}
}
```

然后：

- `InfoCardSessionBase : AutoTestSessionBase`
- `PocketMapSessionBase : AutoTestSessionBase`

这样状态机会话不再通过“嵌套类 + owner 回调”来维持。

## 截图重构建议

### 现状问题

当前截图能力存在三类耦合：

- 文件命名逻辑耦合在 Runner
- “是否允许截图”的策略耦合在 Runner
- “截图前先切图/对焦”的逻辑耦合在具体测试实现

### 重构目标

建立一个统一的截图请求：

```csharp
internal sealed class AutoTestScreenshotRequest
{
    public AutoTestResult Result;
    public string Prefix;
    public string Suffix;
    public bool Force;
    public Action Prepare;
}
```

调用方式：

- InfoCard 模块：
  - `Prepare = () => 确认窗口仍打开`
- 口袋地图模块：
  - `Prepare = () => Current.Game.CurrentMap = TestMap; CameraJump(...)`

这样所有截图都走同一服务，不会继续复制。

## 结果模型重构建议

### 当前问题

`AutoTestResult` 现在已经被所有模块共用，但字段仍然偏少，`Notes` 承担了过多动态信息。

### 建议

保留当前结构，但新增一个轻量的扩展字段：

```csharp
public Dictionary<string, string> Diagnostics;
```

意义：

- `Notes` 保留给人类可读摘要
- `Diagnostics` 存放结构化诊断信息

例如炮塔测试可以写：

- `FirstDamageTick`
- `FirstKillTick`
- `GeneratorPowerOn`
- `OperatorPawnSpawned`

比现在全塞进 `Notes` 更适合后续分析和 UI 展示。

## 建议的重构顺序

为了降低一次性改动风险，不建议直接“全部推倒重写”，建议分四步走。

### 第一步：先抽公共数据与服务

先拆出：

- `AutoTestContext`
- `AutoTestResult`
- `AutoTestWorkItem`
- `AutoTestScreenshotService`
- `ThingSpawnHelper`

这一阶段不改业务行为，只是把工具和数据模型从 Runner 挪出去。

### 第二步：拆 InfoCard 模块

先把“内容测试”整体从 Runner 抽走，因为这部分边界已经最清楚：

- InfoCard 会话
- UI 状态机
- 共享 Hediff Pawn
- 配方工作台
- 布局服务

拆完之后，`AutoTestRunner` 里只保留：

- planner
- session 调度
- finish

### 第三步：拆口袋地图公共层

把以下能力从炮塔逻辑里抽出去：

- `PocketMapFactory`
- `PocketMapCleanupService`
- `PocketMapStateLogger`
- `PocketMapScreenshotFocusService`

这一步做完之后，未来新口袋地图类型才有落脚点。

### 第四步：拆炮塔测试

最后把当前内嵌的 `TurretTestSession` 拆成：

- `TurretPocketMapSession`
- `TurretPowerNetworkBuilder`
- `TurretEnemySpawner`
- `TurretOperatorService`
- `TurretCombatObserver`

这一阶段是收益最大的，因为可以把一大坨内嵌状态机拆平。

## 不建议的重构方式

### 1. 不建议只按“文件大小”机械拆分

例如把 `AutoTestRunner.cs` 生硬拆成：

- `AutoTestRunner.Part1.cs`
- `AutoTestRunner.Part2.cs`

这种只会让逻辑继续耦合，只是换了几个文件名，没有真正解决问题。

### 2. 不建议把所有测试类型都塞进继承树

可以有基类，但不要把每个测试类型都做成很深的继承层级。  
推荐是：

- 小量基类
- 更多组合式 service

### 3. 不建议继续使用字符串驱动复杂状态

例如现在：

- `Result.Stage = "post_kill_10s"`
- `Result.Stage = "fighting_60s"`

人类可读是好事，但内部流程判断不应该依赖这些字符串。  
建议内部状态仍用 enum，字符串只负责报表显示。

## 推荐落地方案

如果只允许做一次中等规模重构，我建议优先做下面这个版本：

### 第一批必须拆

- `AutoTestRunner`
  - 只保留总调度
- `InfoCardSessionBase` + 各类型内容 session
- `PocketMapSessionBase`
- `AutoTestScreenshotService`
- `PocketMapFactory`
- `TurretPocketMapSession`

### 第二批再拆

- `TurretEnemySpawner`
- `TurretPowerNetworkBuilder`
- `TurretOperatorService`
- `RecipeWorktableService`
- `TestMapLayoutService`

## 我对当前文件的判断

当前 [AutoTestRunner.cs](D:/RimworldProject/环世界日志同步mod/source/AutoTestProject/AutoTestRunner.cs) 已经不是“需要整理一下”，而是到了“再继续加功能会持续恶化”的阶段。

尤其是以下两个事实说明必须拆：

- InfoCard 测试和口袋地图测试已经是两套完全不同的生命周期
- 你已经明确后续还会新增新的口袋地图测试类型

在这种前提下，继续把逻辑放进 `AutoTestRunner`，后面会出现：

- `BuildingPocketMapSession`
- `RaidPocketMapSession`
- `UiPanelSession`

然后 Runner 会变成 4000~5000 行的“自动测试大熔炉”。

## 结论

推荐的重构方向不是“继续维护一个更大的 Runner”，而是把系统改成：

- `Runner = 调度器`
- `Planner = 构建测试会话`
- `Session = 一种测试流程`
- `Service = 公共能力`

其中最关键的三个落点是：

1. 用 `IAutoTestSession` 统一 InfoCard 与口袋地图测试
2. 用 `PocketMapSessionBase` 承载未来所有口袋地图类测试
3. 用 `AutoTestScreenshotService` 统一所有截图能力

如果你下一步让我开始实际重构，我建议严格按下面顺序执行：

1. 先抽 `AutoTestContext`、`AutoTestScreenshotService`
2. 再拆 InfoCard 模块
3. 再拆口袋地图公共层
4. 最后拆炮塔测试

这样风险最低，也最容易每一步都保持可测试。
