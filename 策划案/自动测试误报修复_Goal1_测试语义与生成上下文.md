# 自动测试误报修复 Goal 1：测试语义与生成上下文

## 1. Goal 目标

本 Goal 独立修复自动测试的基础语义和对象生成上下文，使本 Goal 涉及的 Pawn、三视图和建筑测试能够正确区分：

1. 模组原生流程失败；
2. 测试框架不支持；
3. 跨阵营、直接挂载等兼容性压力场景；
4. 随机或环境因素导致的不稳定结果。

本 Goal 完成后，应解决以下已知误报：

- DMS PawnKind 被强制以 PlayerColony 生成，导致 backstory 过滤错误；
- 非玩家阵营 Pawn 无法征召时，三视图朝向可能不稳定的担忧；
- DMS_Hound_Base、DMS_TrashBin_Base 等建筑无阵营 Spawn，触发依赖框架 PostSpawnSetup 空引用；
- RK_Skin_PureWhite、RK_Gene_ThinTail 等纯视觉基因被错误要求匹配随机 BodyType。

本 Goal 不修改任何目标模组，只修改本项目自动测试框架、结果语义和测试流程。

## 2. 独立执行与提交边界

本文件定义一个完全独立的开发任务。实现时必须在本 Goal 范围内补齐所需辅助方法、结果处理和测试逻辑，不使用任何外部阶段作为前置条件。

本 Goal 完成并通过本文件第 7 节命令后单独提交。提交不得混入配方、Hediff、战斗距离或其他范围外改动。

## 3. 当前问题与源码位置

### 3.1 PawnInfoCard 强制使用玩家阵营

当前文件：

    source/AutoTestRunner/AutoTestRunner.ExtendedInfoCardModule.cs

`ResolveInfoCardPawnFaction` 对普通 PawnInfoCard 返回 `Faction.OfPlayer`。这会绕过 PawnKind 的原生阵营配置，并改变 backstory、文化、关系和装备生成上下文。

### 3.2 三视图 Humanlike 强制使用玩家阵营

当前文件：

    source/AutoTestRunner/AutoTestThreeViewService.cs

`ResolveFaction` 对所有 Humanlike Presenter 返回玩家阵营。测试因此实际验证的是“以 PlayerColony 生成该 PawnKind”，而不一定是 PawnKind 的正常出现方式。

### 3.3 三视图已经可以直接控制朝向

当前 `StabilizePawnForSettle` 已执行：

- 停止当前 Job；
- 停止寻路；
- 直接设置 `pawn.Rotation`；
- 存在 `drafter` 时才设置 Drafted。

因此非玩家阵营不能征召不是阻碍。本 Goal 不应为了控制朝向而强行把 Pawn 改成玩家阵营。

### 3.4 普通建筑 Spawn 前没有阵营

当前文件：

    source/AutoTestRunner/AutoTestRunner.ContentModule.cs
    source/AutoTestRunner/AutoTestRunner.StaticViewModule.cs

建筑经 `ThingMaker.MakeThing` 后直接 `GenSpawn.Spawn`，其 Comp 会先进入 `PostSpawnSetup`，依赖 `Faction` 的 Comp 因而可能在 `SetFaction` 之前崩溃。

### 3.5 BodyType 断言适用于所有三视图

当前文件：

    source/AutoTestRunner/Integration/AutoTestRunner.SemanticModule.cs

`threeview.body_type_matches_plan` 只要 Spec 中记录了 `PawnBodyTypeDefName` 就严格断言三名 Presenter 的 BodyType。Gene 探针阶段记录的是随机 Pawn 的体型，纯肤色、尾巴等基因并不约束体型，因此会天然产生偶发失败。

## 4. 总体设计决策

### 4.1 原生流程为主测试，兼容场景不混入主结论

PawnKind 主测试默认使用原生生成上下文。

“以 PlayerColony 生成目标 PawnKind”保留为兼容性观察，但不得继续冒充 PawnKind 原生流程。兼容性观察产生 Error 时，应记录为兼容性警告，而不是直接把主测试判定为模组失败。

### 4.2 不为朝向修改阵营

三视图朝向统一通过直接 Rotation、停止 Job 和停止寻路实现。不得通过征召、敌对关系或切换阵营来换取朝向控制。

### 4.3 建筑阵营必须在 Spawn 前设置

只对能够持有阵营且测试场景需要正常 Comp 初始化的建筑设置测试阵营。不得对植物、自然岩石、蓝图、Frame 或明确要求无阵营的对象全局赋予玩家阵营。

### 4.4 BodyType 断言改为显式语义

不能再把探针 Pawn 的随机 BodyType 自动升级成测试要求。

新增明确字段，例如：

    bool RequireBodyTypeMatch;
    string ExpectedBodyTypeSource;

仅在 BodyType 本身就是测试目标，或服装、武器、测试 Spec 明确锁定体型时启用严格断言。

## 5. 实施任务

### 5.1 建立统一的测试结果分类辅助方法

检查并统一以下状态的含义：

    passed
    passed_after_retry
    failed
    skipped
    unsupported
    compatibility_warning

具体要求：

1. 保持现有 Result.Status 字符串模型，避免在本 Goal 中进行大规模序列化重构；
2. 在 AutoTestRunner 内提供统一完成辅助方法，禁止各模块随意拼写新状态；
3. `unsupported` 和 `compatibility_warning` 不计入模组失败；
4. Result、Evidence 和 Markdown Report 对这些状态使用一致的成功/失败统计规则；
5. Python 测试驱动和 RimDevTools 汇总逻辑同步更新，避免 C# 认为成功但外层脚本仍返回失败；
6. 所有非普通 passed 状态必须带结构化原因和 Notes。

建议新增或整理的方法：

    CompletePassed(...)
    CompleteFailed(...)
    CompleteUnsupported(...)
    CompleteCompatibilityWarning(...)

### 5.2 新增 Pawn 生成阵营解析器

在 ThreeView 和 PawnInfoCard 共用层新增阵营解析逻辑，不复制两套判断。

建议模型：

    enum AutoTestPawnFactionMode
    {
        Native,
        PlayerCompatibility,
        Mechanoid,
        FactionlessFallback
    }

原生阵营解析顺序：

1. Mechanoid 使用 `Faction.OfMechanoids`；
2. PawnKind 存在 `defaultFactionType` 时，在当前游戏 FactionManager 中查找对应 Faction 实例；
3. 找不到对应实例时使用 null，不要为了截图创建会污染当前游戏的永久 Faction；
4. 测试 Spec 明确指定玩家阵营时才使用 PlayerColony；
5. 结果 Notes 记录 PawnKind、FactionMode、FactionDef 和最终 Faction。

不得把“Humanlike”等同于“玩家阵营”。

### 5.3 修改 PawnInfoCard 生成流程

目标文件：

    source/AutoTestRunner/AutoTestRunner.ExtendedInfoCardModule.cs

改动要求：

1. 删除普通 PawnInfoCard 固定返回 PlayerColony 的规则；
2. 调用统一阵营解析器；
3. 主 Case 使用 Native 模式；
4. 若未来启用 PlayerCompatibility，必须形成单独 CaseId 或单独 compatibility evidence，不能覆盖主 Case；
5. 生成失败时依次尝试原生阵营、无阵营安全回退，但不得自动切到玩家阵营后仍声称是原生测试；
6. 每次 fallback 必须写入 Notes。

### 5.4 修改三视图 Pawn 生成和稳定逻辑

目标文件：

    source/AutoTestRunner/AutoTestThreeViewService.cs

改动要求：

1. `TryGeneratePawn`、`TryGenerateProbePawn` 和 Replica 流程统一使用新阵营解析器；
2. 保持同一 Triptych 的三名 Pawn 使用相同 FactionMode；
3. Spawn 后立即停止 Job、停止寻路并设置 Rotation；
4. 每次 settle/tick 检查继续恢复预定 Rotation；
5. 没有 drafter 时不得判定失败；
6. 不调用 `SetFaction(Faction.OfPlayer)` 来解决朝向问题；
7. 记录三个 Pawn 的 Rotation、Faction 和 Job 状态。

### 5.5 新增测试建筑 Spawn 上下文辅助方法

新增统一辅助方法，例如：

    PrepareBuildingFactionBeforeSpawn(Thing thing, AutoTestBuildingContext context)

接入位置至少包括：

- BuildingInfoCard；
- BuildingStaticView；
- Recipe Worktable 放置；
- 其他直接 `ThingMaker.MakeThing + GenSpawn.Spawn` 的测试建筑路径。

行为规则：

1. 非 Building 不处理；
2. 不能持有 Faction 的 Thing 不处理；
3. 已经有 Faction 的 Thing 不覆盖；
4. 测试用工作台和可交互建筑默认使用 PlayerColony；
5. 明确要求敌对或中立阵营的 Spec 优先；
6. 必须在 `GenSpawn.Spawn` 前调用；
7. Notes 记录是否设置、设置原因和最终阵营；
8. 清理阶段继续正常销毁建筑，不留下 Faction 状态。

不要用 try/catch 吞掉 Fortified NRE 作为修复。

### 5.6 修改 BodyType 计划与断言

目标文件：

    source/AutoTestRunner/AutoTestThreeViewService.cs
    source/AutoTestRunner/Integration/AutoTestRunner.SemanticModule.cs

改动要求：

1. `AutoTestThreeViewTargetSpec` 增加是否严格要求 BodyType 的字段；
2. BodyTypeThreeView 必须启用；
3. 服装/武器为贴图兼容性明确选择 Presenter BodyType 时启用；
4. 普通 PawnKind 三视图可以按 PawnKind/生成计划启用；
5. GeneThreeView 默认不启用；
6. 只有能静态确认改变 BodyType 的 Gene 才启用；
7. XenotypeThreeView 只在 Xenotype 明确要求/产生特定 BodyType 时启用，否则只记录实际体型；
8. SemanticModule 在 `RequireBodyTypeMatch == false` 时记录 informational evidence，不创建失败断言。

### 5.7 更新报告和日志说明

Result Notes 至少增加：

    GenerationFactionMode
    GenerationFactionDef
    RotationControlledDirectly
    BuildingFactionPrepared
    RequireBodyTypeMatch
    ExpectedBodyTypeSource

Markdown Report 对 `compatibility_warning` 和 `unsupported` 单独分组，不得混入 Failed。

## 6. 测试计划

### 6.1 编译与纯逻辑测试

1. 编译 AutoTestRunner；
2. 为阵营解析器增加不依赖实际地图的逻辑测试；
3. 为 `RequireBodyTypeMatch` 决策增加表驱动测试；
4. 为结果状态汇总增加测试，确认 unsupported/warning 不计入失败。

### 6.2 定向游戏内测试

至少覆盖：

- `DMS_Police_Melee` PawnInfoCard；
- `DMS_Police_Melee` HumanoidThreeView；
- `DMS_Hound_Base` BuildingInfoCard；
- `DMS_TrashBin_Base` BuildingInfoCard；
- `RK_Skin_PureWhite` GeneThreeView；
- `RK_Gene_ThinTail` GeneThreeView。

验证点：

1. DMS Pawn 主测试不再因 PlayerColony backstory 产生严重日志；
2. 三视图三个 Pawn 朝向正确且不要求 drafter；
3. 建筑 PostSpawnSetup 不再因 Faction null 崩溃；
4. 纯视觉 Gene 不再执行严格 BodyType 断言；
5. Gene 存在、激活和视觉签名等原有断言仍保留。

### 6.3 回归抽检

抽检原版 Colonist、Mechanoid、Animal、HAR PawnKind，确认阵营变化没有破坏：

- Pawn 生成；
- 三视图 Replica；
- 信息卡 Character/Health 页；
- 清理阶段；
- Manifest 和 Evidence 完整性。

## 7. 最终验收测试方法与命令

### 7.1 统一测试准备

以下命令均从项目根目录执行。任何一步返回非 0，都必须先修复，不能继续验收：

```powershell
dotnet build .\RimDevTools\RimDevTools.csproj -c Release
dotnet run --project .\AutoTest.Core.Tests\AutoTest.Core.Tests.csproj -c Release
dotnet run --project .\RimDevTools.Tests\RimDevTools.Tests.csproj -c Release
dotnet run --project .\RimDevTools.ReleaseTests\RimDevTools.ReleaseTests.csproj -c Release
powershell -ExecutionPolicy Bypass -File .tools\deploy.ps1

$Tool = (Resolve-Path '.\RimDevTools\bin\Release\net10.0\RimDevTools.exe').Path
$Mods = (Resolve-Path '.\.tools\test\modlist\overnight-five-mods.xml').Path
```

测试时必须使用刚刚编译和部署的 AutoTestRunner，不得使用旧 DLL 或旧发布包中的 RimDevTools。

### 7.2 DMS 原生阵营、朝向和建筑 Faction 定向验收

目标模组：

    aoba.deadmanswitch.core

命令：

```powershell
& $Tool test run --mode auto `
  --target-package-id aoba.deadmanswitch.core `
  --active-mods-file $Mods `
  --scope ui --scope view `
  --defname DMS_Police_Melee `
  --defname DMS_Hound_Base `
  --defname DMS_TrashBin_Base `
  --capture-screenshots `
  --timeout-seconds 1800

if ($LASTEXITCODE -ne 0) { throw 'Goal 1 DMS 定向验收失败' }
```

必须验证：

- `PawnInfoCard|DMS_Police_Melee` 通过；
- `HumanoidThreeView|DMS_Police_Melee` 通过；
- `BuildingInfoCard` 和 `BuildingView` 下两个建筑全部通过；
- 没有 PlayerColony backstory 误报；
- 没有 Fortified Faction null NRE；
- 三视图方向断言通过。

### 7.3 Ratkin 纯视觉 Gene BodyType 定向验收

目标模组：

    solaris.ratkinracemod

命令：

```powershell
& $Tool test run --mode auto `
  --target-package-id solaris.ratkinracemod `
  --active-mods-file $Mods `
  --scope view `
  --defname RK_Skin_PureWhite `
  --defname RK_Gene_ThinTail `
  --capture-screenshots `
  --timeout-seconds 1800

if ($LASTEXITCODE -ne 0) { throw 'Goal 1 Ratkin 定向验收失败' }
```

该命令必须连续执行两次并全部通过，用于排除随机 BodyType 波动：

```powershell
1..2 | ForEach-Object {
  & $Tool test run --mode auto `
    --target-package-id solaris.ratkinracemod `
    --active-mods-file $Mods `
    --scope view `
    --defname RK_Skin_PureWhite `
    --defname RK_Gene_ThinTail `
    --timeout-seconds 1800

  if ($LASTEXITCODE -ne 0) { throw "Goal 1 Ratkin 第 $_ 次重复验收失败" }
}
```

### 7.4 Goal 1 严格通过标准

Goal 1 只有在上述命令全部成功时才允许验收。最新 Result.json 必须满足：

- `completeness = Complete`；
- `manifestMissing = 0`；
- `manifestExecuted = manifestPlanned`；
- `failedAssertionCount = 0`；
- `severeLogCount = 0`；
- 本节列出的所有目标 Case 状态只能是 `passed`；
- 不接受 `unsupported`、`skipped`、`compatibility_warning` 或 `passed_after_retry` 代替本 Goal 的定向通过。

如果定向命令未通过，即使代码能够编译，也不能视为 Goal 1 完成。

## 8. 完成标准

以下条件全部满足后才可完成 Goal 1：

1. 第 7 节所有定向验收命令通过；
2. PawnInfoCard 和 ThreeView 不再把所有 Humanlike 固定为玩家阵营；
3. 非玩家 Pawn 的三视图朝向稳定，且无 drafter 也可通过；
4. DMS 两个已知建筑不再因 Faction null 产生 Fortified NRE；
5. RK 两个纯视觉 Gene 连续运行至少两次，不再出现 BodyType 偶发失败；
6. compatibility_warning、unsupported 与 failed 在所有输出层统计一致；
7. 未修改任何目标模组文件；
8. 提交一份 Goal 1 实施结果记录，包含命令、runId、Result 路径、改动文件和遗留风险；
9. 测试通过后创建独立提交，例如 `git commit -m "fix: stabilize autotest generation context"`，提交内容只能属于 Goal 1。

## 9. 独立提交方法

只有第 7 节全部通过后才能提交：

```powershell
git status --short
git add <仅本 Goal 修改的源码、测试和实施报告>
git commit -m "fix: stabilize autotest generation context"
```

提交前必须检查暂存区，不得包含配方、Hediff、战斗距离或其他无关文件。提交完成即结束本 Goal，不等待其他任务。

## 10. 本 Goal 明确不做

- 不修复普通配方产物拆堆；
- 不实现机械培育/复活配方；
- 不重做 Hediff 适用 Pawn 和安装部位；
- 不修改远程武器和炮塔敌人距离；
- 不通过屏蔽全部 Error 让测试通过；
- 不修改 DMS、Axolotl、Ratkin 等目标模组。
