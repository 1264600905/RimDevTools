# 自动测试误报修复 Goal 1 实施结果

## 结论

Goal 1 已按《自动测试误报修复_Goal1_测试语义与生成上下文》完成实现与验收。

- DMS PawnInfoCard 与 HumanoidThreeView 使用 PawnKind 原生阵营 `DMS_Army`，不再冒充 PlayerColony 原生流程。
- 三视图通过停止 Job/寻路并直接恢复 Rotation 控制朝向，不依赖 Drafter；Player、非玩家、动物、机械族与 HAR 抽检均通过。
- BuildingInfoCard、BuildingView、Recipe Worktable、炮塔及其他交互建筑路径在 Spawn 前准备测试 Faction，并跳过植物、自然岩石、Blueprint、Frame 和显式无阵营对象。
- Gene/Xenotype BodyType 断言改为显式策略；`RK_Skin_PureWhite` 与 `RK_Gene_ThinTail` 只记录实际 BodyType，不再把探针随机体型升级为失败条件。
- `passed`、`passed_after_retry`、`failed`、`skipped`、`unsupported`、`compatibility_warning` 已统一分类；Result、Evidence、Markdown、RimDevTools 和 Python 驱动使用同一失败语义。
- 显式 PlayerCompatibility 会建立独立 Case/Evidence；兼容观察失败会记录为 `compatibility_warning`，不覆盖 Native 主 Case，也不使 Evidence 失败。
- 未修改任何目标模组文件。

## 最终编译与纯逻辑门禁

以下命令均于 2026-08-11 从项目根目录执行并返回 0：

```powershell
dotnet build .\RimDevTools\RimDevTools.csproj -c Release
dotnet run --project .\AutoTest.Core.Tests\AutoTest.Core.Tests.csproj -c Release
dotnet run --project .\RimDevTools.Tests\RimDevTools.Tests.csproj -c Release
powershell -ExecutionPolicy Bypass -File .tools\publish-rimdevtools.ps1
dotnet run --project .\RimDevTools.ReleaseTests\RimDevTools.ReleaseTests.csproj -c Release
python -m py_compile .tools\test\run_test.py
powershell -ExecutionPolicy Bypass -File .tools\deploy.ps1
```

结果：

- AutoTest.Core.Tests：77 项通过；覆盖状态分类、原生阵营决策、BodyType 表驱动决策和兼容观察 Evidence 非失败语义。
- RimDevTools.Tests：303 项通过；覆盖外层对 `passed_after_retry`、`unsupported`、`compatibility_warning` 的接受规则。
- RimDevTools.ReleaseTests：61 项通过。
- AutoTestRunner 与 LogSyncMod Release 编译、发布和部署成功。

## 最终 DMS 定向验收

命令目标：`aoba.deadmanswitch.core`，UI + View，`DMS_Police_Melee`、`DMS_Hound_Base`、`DMS_TrashBin_Base`，启用截图。

- runId：`2773df02f9a14d2590a50bb8f444dd72`
- Result：`D:\RimworldProject\环世界日志同步mod\.tools\log\AutoTestOutput\aoba.deadmanswitch.core_2773df02f9a14d2590a50bb8f444dd72_Result.json`
- Report：`D:\RimworldProject\环世界日志同步mod\.tools\log\AutoTestOutput\aoba.deadmanswitch.core_2773df02f9a14d2590a50bb8f444dd72_Screenshots\aoba.deadmanswitch.core_2773df02f9a14d2590a50bb8f444dd72_Report.md`

严格字段：

- `completeness=Complete`
- `manifestPlanned=6`
- `manifestExecuted=6`
- `manifestMissing=0`
- `failedAssertionCount=0`
- `severeLogCount=0`
- 6 个目标 Case 全部为 `passed`

关键证据：

- `PawnInfoCard|DMS_Police_Melee`：`GenerationFactionMode=Native`、`GenerationFactionDef=DMS_Army`、最终 Pawn Faction 为 `DMS_Army`。
- `HumanoidThreeView|DMS_Police_Melee`：三名 Pawn 均为 `DMS_Army`，Job 为 `None`，Drafter 为 `None`，Rotation 分别为 West/South/North 对应值，方向断言通过。
- 两个 BuildingInfoCard 与两个 BuildingView：`BuildingFactionPrepared=True`、最终为 `PlayerColony`，无 Fortified Faction-null NRE。

最终 DMS 命令第一次尝试在模组初始化阶段、自动测试启动前出现一次 `GAME_HUNG`（runId `968cb31359224d9d890287993205c9c2`，无 Result）；同一命令重试后完整通过。该事件未被计为模组 Case 失败。

## 最终 Ratkin 定向验收

截图轮：

- runId：`41bb1f32abb94878ae75929c877d782f`
- Result：`D:\RimworldProject\环世界日志同步mod\.tools\log\AutoTestOutput\solaris.ratkinracemod_41bb1f32abb94878ae75929c877d782f_Result.json`

连续重复轮：

- 第 1 次 runId：`9eb8cc0762654c08b3189f6a6efaddc3`
- Result：`D:\RimworldProject\环世界日志同步mod\.tools\log\AutoTestOutput\solaris.ratkinracemod_9eb8cc0762654c08b3189f6a6efaddc3_Result.json`
- 第 2 次 runId：`39e5bc95dff54cbc9dcfe924f211c612`
- Result：`D:\RimworldProject\环世界日志同步mod\.tools\log\AutoTestOutput\solaris.ratkinracemod_39e5bc95dff54cbc9dcfe924f211c612_Result.json`

三轮均满足：

- `completeness=Complete`
- `manifestPlanned=2`
- `manifestExecuted=2`
- `manifestMissing=0`
- `failedAssertionCount=0`
- `severeLogCount=0`
- `RK_Skin_PureWhite` 与 `RK_Gene_ThinTail` 全部为 `passed`
- 两项均记录 `RequireBodyTypeMatch=False`、`VisualEffectDetected=True`、`TargetGeneActive=True`
- Evidence 使用信息性 `threeview.body_type_observed`，同时保留 Gene 存在、数量、依赖、激活、唯一集合和视觉签名断言。

Ratkin Presenter 探针仍可能产生模组自身的 backstory 回退 Error。原始数量保留在 `capturedSevereLogCount` 和 Evidence Logs 中；只有关联到 Case 且未获允许的严重日志计入 `severeLogCount`。三轮目标 Case 的 `unexpectedSevereLogCount` 均为 0。

## 回归抽检

- 原版 Colonist、Mechanoid、Muffalo 与相关 Presenter：runId `eeeb14456313408fbc6f6fb3586e8d0b`，114/114 `passed`，失败断言与严重日志均为 0。
- HAR Ratkin PawnInfoCard：runId `ab282596203842a78a52f674876cbf22`，`PawnInfoCard|RatkinColonist` 通过。
- HAR Ratkin 三视图：runId `24705aeefb4e4bc28f877c9c8c49f60d`，`AlienThreeView|RatkinColonist` 通过；Replica、方向和清理断言通过。
- Recipe Worktable：runId `0c921b0b2afc4ed8ace932a572cc53eb`，`RecipeBillUi|CookMealSimple` 通过，Campfire 记录 `BuildingFactionPrepared=True`、最终 `PlayerColony`。

## 改动文件

核心实现：

- `.tools/test/run_test.py`
- `RimDevTools/Commands/TestCommand.cs`
- `RimDevTools/RimDevTools.csproj`
- `source/LogSyncMod/LogSyncAutoTestSpecDef.cs`
- `source/AutoTestRunner/AutoTestGenerationContext.cs`
- `source/AutoTestRunner/AutoTestModels.cs`
- `source/AutoTestRunner/AutoTestReportWriter.cs`
- `source/AutoTestRunner/AutoTestRunner.ResultStatusModule.cs`
- `source/AutoTestRunner/AutoTestRunner.cs`
- `source/AutoTestRunner/AutoTestRunner.ContentModule.cs`
- `source/AutoTestRunner/AutoTestRunner.ExtendedInfoCardModule.cs`
- `source/AutoTestRunner/AutoTestRunner.StaticViewModule.cs`
- `source/AutoTestRunner/AutoTestRunner.ThreeViewModule.cs`
- `source/AutoTestRunner/AutoTestRunner.TurretModule.cs`
- `source/AutoTestRunner/AutoTestThreeViewService.cs`
- `source/AutoTestRunner/Core/AutoTestEvidenceBuilder.cs`
- `source/AutoTestRunner/Core/AutoTestGenerationPolicies.cs`
- `source/AutoTestRunner/Core/AutoTestOperationJournal.cs`
- `source/AutoTestRunner/Core/AutoTestResultStatus.cs`
- `source/AutoTestRunner/Integration/AutoTestRunner.SemanticModule.cs`
- `source/AutoTestRunner/Integration/LegacyResultEvidenceAdapter.cs`

测试：

- `AutoTest.Core.Tests/Program.cs`
- `RimDevTools.Tests/Program.cs`

## 遗留风险

- 五模组环境偶尔会在自动测试启动前出现 RimWorld 初始化心跳中断；本次最终 DMS 验收重试一次后通过。该风险属于启动环境不稳定，不改变已生成 Result 的 Case 结论。
- Ratkin 的 Presenter 探针会产生未关联 Case 的 backstory 回退 Error；目前完整保留原始日志并与“计入失败的意外严重日志”分栏统计。若后续要消除这些原始日志，应在 Ratkin/Presenter 生成策略层另立任务，不能通过吞掉全部 Error 处理。
- 本 Goal 未改动配方拆堆、Hediff、战斗距离等 Goal 2/3 范围。
