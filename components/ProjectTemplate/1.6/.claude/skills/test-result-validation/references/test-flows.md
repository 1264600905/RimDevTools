# AutoTest 测试流程与核心函数参考

记录 auto/XML 模式自动测试的各项测试流程与核心调用函数（保留核心部分）。校验测试结果前先在此定位用例所属流程。

## 通用调度流程

所有 auto/XML 测试共用：

- `AutoTestBootstrap.Initialize`：解析运行时请求并初始化。
- `AutoTestBootstrap.Tick` / `AutoTestRunner.Tick`：每 tick 驱动测试调度，分发到各模块。
- `AutoTestOperationRunner`：按序执行用例操作。
- `AutoTestAssertions`：执行断言。
- `AutoTestEvidenceBuilder`：收集操作证据。
- `AutoTestLogCaptureSubscription`：订阅并捕获游戏日志。
- `AutoTestReportWriter.CreateRunArtifacts`：写出结构化 JSON 与 Markdown 产物。

## 三视图（ThreeViewModule）

流程：准备舞台区域 → 解析外观呈现器 → 生成三联画目标 → 摆位/朝向 → 截图 → 清理。

- `AutoTestThreeViewService.PrepareStudioArea`：准备舞台区域。
- `TryResolveAppearancePresenter`：解析外观呈现器，失败给出跳过原因。
- `TryPopulateTriptych`：生成三联画目标上下文。
- `PrepareForSettle`、`TryPrimeDirectionalWalk`、`TryPrimeDirectionalFacingForPausedCapture`：摆位。
- `PrepareForCapture`、`IsReadyForCapture`、`Focus`：截图准备。
- `AutoTestThreeViewRenderSuppression.Register/Unregister`：渲染抑制。
- `Update`（每帧驱动）、`ProcessUiFrame`（UI 帧处理）、`FailFromUi`（UI 失败上报）。

## 静态视图（StaticViewModule）

- `Update`：每帧驱动。
- `ProcessUiFrame`：处理 UI 帧。
- `FailFromUi`：UI 失败上报。

## 信息卡（InfoCardModule / ExtendedInfoCardModule）

流程：打开信息卡 → 断言内容 → 关闭并追踪关闭原因。

- `AutoTestUiAdapters.Open(Thing/Def/Hediff/Bill_Production)`：打开信息卡窗口。
- `InfoCardCloseTrace.Begin/Attach/Matches/AddReason/Describe/Stop`：追踪信息卡关闭原因。
- `InfoCardOperationAdapter.AssertOperation/AssertTrue/AddCapture/CompleteCase`：断言与证据。
- `ProcessUi`：处理 UI。

## 近战武器（MeleeWeaponModule）

- `Update`：逐 tick 驱动近战攻击流程。

## 远程武器（RangedWeaponModule）

- `Update`：逐 tick 驱动射击流程。
- `ProjectileScreenshotTicks`：弹丸截图时刻表。

## 炮塔（TurretModule）

- `Update`：逐 tick 驱动炮塔攻击流程。
- `EnemyPawns`、`EnemyInitialHealthPercent`：敌方生成与血量基线。
- `ProjectileLastPositions`、`ProjectileLastTicks`：弹丸位置/时刻追踪。

## 技能口袋地图（SkillPocketMapModule）

- `Update`：驱动口袋地图技能流程。
- `AutoTestPocketMapService.IsAlive/Destroy`：口袋地图生命周期检查。

## 战斗小人（CombatPawnModule）

- `AutoTestCombatPawnService.TrySpawnConfiguredPawn`：按配置生成敌人。
- `Update`：逐 tick 驱动战斗。
- `BuildDebugStatus`：构建调试状态。

## 结果状态（ResultStatusModule）

- `AutoTestResultStatus`：结果状态判定，输出成功/失败分类。

## XML Spec（SpecModule）

- 从目标模组 XML Spec（如 `RimLane_LogSyncAutoTests.xml`）解析测试目标。
- `AutoTestGenerationContext.Resolve`：解析 pawn/派系生成上下文。

## 截图

- `AutoTestScreenshotService.TryCapture`：请求截图。
- `PrepareCaptureFrame`：准备截图帧。
- `Request`、`DrawOverlay`：截图请求与覆盖层。
- `AutoTestScreenshotProfiles`：截图参数配置。
