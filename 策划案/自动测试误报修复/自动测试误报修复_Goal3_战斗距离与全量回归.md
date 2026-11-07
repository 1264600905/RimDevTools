# 自动测试误报修复 Goal 3：战斗距离与全量回归

## 1. Goal 目标

本 Goal 独立统一远程武器、炮塔和相关战斗口袋地图的目标布置算法，并完成本 Goal 自己的定向与重复回归。

主要解决：

1. 短射程武器因固定约 12 格刷点无法生成敌人；
2. 间接火力武器因 `minRange` 大于固定刷点距离而无法攻击；
3. 当前虽然计算 `targetDistance`，实际仍使用固定 5/8 地图锚点；
4. 超大 range 在选中/绘制射程环时产生与战斗无关的 Error；
5. onlyManualCast 武器被当成自动战斗武器，Job 不会主动使用；
6. 战斗距离修复完成后缺少独立、可重复的验收流程。

本文件定义一个完全独立的开发任务。实现、测试、报告和 Git 提交全部在本 Goal 内闭环，不使用任何外部阶段作为前置条件或验收内容。

## 2. 当前问题与源码位置

主要文件：

    source/AutoTestRunner/AutoTestRunner.RangedWeaponModule.cs
    source/AutoTestRunner/AutoTestRunner.TurretModule.cs
    source/AutoTestRunner/AutoTestRunner.MeleeWeaponModule.cs
    source/AutoTestRunner/AutoTestRunner.CombatPawnModule.cs

### 2.1 RangedWeapon 计算距离但没有使用

`FindEnemySpawnCells` 当前计算：

    preferredDistance
    targetDistance

但最终 anchor 仍固定为地图 X 轴 5/8 位置。`targetDistance` 没有参与 candidate 坐标生成。

### 2.2 Turret 存在相同问题

TurretModule 同样计算 `targetDistance` 后继续使用固定 anchor，并且只检查 `distance <= range`，没有检查 `minRange`。

### 2.3 onlyManualCast 不能依赖普通 Attack Job

例如可投掷物或手动施放武器即使目标在射程内，也可能因为 `onlyManualCast` 而不会由 Pawn 自动攻击。

### 2.4 超大射程环属于 UI/选择副作用

射程 89.9 本身可以用于战斗，但原版某些 UI 绘制路径只接受较小半径。测试不应修改实际 Verb range，也不应把纯射程环绘制 Error 误判成武器伤害失败。

## 3. 总体设计

### 3.1 抽出统一战斗目标布置服务

新增服务，例如：

    AutoTestCombatPlacementService

由 RangedWeapon、Turret 和需要射程约束的其他测试共用。

输入模型建议包含：

    Map
    IntVec3 attackerCell
    VerbProperties verbProps
    int targetCount
    IntVec3 preferredAnchor
    CellRect safeRect
    bool requireLineOfSight
    bool requireWalkable
    float minimumSeparationMargin
    float maximumRangeMargin

输出包含：

    selectedCells
    minValidDistance
    maxValidDistance
    preferredCellWasValid
    repositionReason
    candidateDiagnostics

### 3.2 仅在原位置无法攻击时调整

保持现有截图布局稳定的原则：

1. 先验证现有固定位置；
2. 如果固定位置满足 minRange、range、视线和安全区，则继续使用；
3. 只有固定位置完全无法攻击时才重新选点；
4. 新点优先距离原固定位置最近；
5. 结果 Notes 必须说明是否移动和原因。

### 3.3 实际射程与展示射程分离

战斗逻辑永远使用 Verb 的真实 minRange/range。

射程环或截图 UI 如果存在上限，只能调整展示行为，不能修改 VerbProperties。

## 4. 实施任务

### 4.1 实现合法距离区间计算

建议公式：

    minDistance = max(2, verbProps.minRange + minMargin)
    maxDistance = min(verbProps.range - maxMargin, mapSafeMaximum)

注意事项：

1. `minRange <= 0` 时按 0 处理；
2. 极短射程不能被固定最小值 5 排除；
3. 近战或接触类 Verb 单独使用 1～2 格规则；
4. `maxDistance < minDistance` 时明确输出地图尺寸不足或 Verb 配置不支持；
5. 不把无法布置目标统一记为模组失败，应根据原因区分 unsupported_harness 和真实无合法射程；
6. 对爆炸、建筑目标、地点目标 Verb 不能强行要求 Pawn target。

### 4.2 实现候选点生成和评分

候选点来源：

1. 当前固定 anchor；
2. 攻击者正前方合法距离范围；
3. 正前方左右横向偏移；
4. 必要时在攻击者周围合法环带采样；
5. 地图安全边界内的后备点。

过滤条件：

- InBounds；
- safeRect；
- Walkable（Pawn 目标）；
- 距离位于合法区间；
- LineOfSight（Verb 需要时）；
- 不与攻击者、建筑占地或其他目标冲突；
- 可生成 Pawn；
- 不处于不可用地形。

评分顺序：

1. 原固定位置有效优先；
2. 距离原固定位置最近；
3. 距离 preferredDistance 最近；
4. 横向偏移最小；
5. Def/Cell 顺序稳定，保证重复运行结果可复现。

### 4.3 接入 RangedWeaponModule

删除当前模块内未实际使用的 `targetDistance` 计算和固定 anchor 候选逻辑，改为调用统一服务。

要求：

1. 从实际装备后的主 Verb 读取 minRange/range；
2. 如果实际 Verb 不可用，再回退 ThingDef.Verbs；
3. Spawn 前记录合法区间；
4. EnemyCount 不足时说明每个 candidate 被过滤的原因；
5. Enemy Spawn 后验证目标确实在合法射程内；
6. 攻击前再次验证 Verb.CanHitTarget 或等价能力；
7. 不因为短射程而默认失败。

### 4.4 接入 TurretModule

要求：

1. 优先读取 Turret 当前 AttackVerb/GunCompEq PrimaryVerb；
2. 同时检查 minRange 和 range；
3. 保留炮塔自身占地和安全距离；
4. 间接火力不应被 LineOfSight 规则错误排除，是否要求视线应来自 Verb/Projectile 语义；
5. 目标生成后验证炮塔可选中目标；
6. 若炮塔必须操作员、供电或弹药，继续由既有准备流程处理，不把距离失败与资源失败混在一起；
7. 结果 Notes 记录 `CombatMinRange`、`CombatMaxRange` 和 `EnemyRepositioned`。

### 4.5 检查 Melee 与 CombatPawn 布局

MeleeWeapon 不需要 minRange/range 环带，但应检查固定 5/8 布局是否导致攻击者长距离寻路或超时。

建议：

- Melee 目标放在攻击者附近 1～2 格的可达点；
- CombatPawn 围攻仍使用适合包围的近距离点；
- 复用统一服务中的安全区、候选排序和诊断能力，但不强行复用远程距离公式。

### 4.6 处理 onlyManualCast

对 `verbProps.onlyManualCast == true` 的武器：

1. 不依赖普通自动 AttackStatic/AttackMelee Job；
2. 使用与玩家手动指定目标等价的 Verb 启动路径；
3. 在调用前验证目标参数允许 Pawn/Location；
4. 对只允许地点或建筑目标的 Verb 创建正确目标类型；
5. 如果测试框架尚不支持该 TargetingParameters，输出 unsupported，不记 failed_no_damage；
6. Result 记录 `AttackInitiation=ManualVerbCast`。

不得为了让 onlyManualCast 自动触发而修改武器 Def。

### 4.7 处理超大射程环 Error

先定位 Error 的确切调用栈，再按以下优先级处理：

1. 战斗阶段避免无必要地选择 Pawn/武器/炮塔，从而不触发射程环；
2. 截图聚焦直接使用相机服务，不依赖 Selection 绘制；
3. 如果信息覆盖层需要显示射程，使用文字或测试自己的安全图形，不调用原版受限半径环；
4. 只有原版 UI 路径无法避开时，才允许增加严格限定于自动测试活动期间、特定射程环调用的窄范围抑制；
5. 禁止全局过滤同类 Error；
6. 禁止把真实 Verb range 截断为 40。

### 4.8 增加战斗布置 Evidence

每个战斗 Case 至少记录：

    VerbDef/VerbClass
    MinRange
    Range
    PreferredTargetDistance
    ActualTargetDistances
    PreferredCellWasValid
    EnemyRepositioned
    RepositionReason
    RequireLineOfSight
    OnlyManualCast
    AttackInitiation

失败时记录候选点过滤统计，避免只输出“No valid enemy spawn cells”。

## 5. 定向测试矩阵

### 5.0 测试准备

从项目根目录执行：

```powershell
dotnet build .\RimDevTools\RimDevTools.csproj -c Release
dotnet run --project .\AutoTest.Core.Tests\AutoTest.Core.Tests.csproj -c Release
dotnet run --project .\RimDevTools.Tests\RimDevTools.Tests.csproj -c Release
dotnet run --project .\RimDevTools.ReleaseTests\RimDevTools.ReleaseTests.csproj -c Release
powershell -ExecutionPolicy Bypass -File .tools\deploy.ps1

$Tool = (Resolve-Path '.\RimDevTools\bin\Release\net10.0\RimDevTools.exe').Path
$Mods = (Resolve-Path '.\.tools\test\modlist\overnight-five-mods.xml').Path
```

任何准备命令失败都不得进入游戏内验收。

### 5.1 DMS 远程与炮塔

至少覆盖：

- DMS_Turret_Vasilek；
- DMS_Gun_Vasilek_B；
- DoorBreaker；
- Flameblaster；
- KnuckledusterRevolver。

验证：

1. Vasilek 目标距离大于 minRange 且小于 range；
2. 短射程武器目标进入实际射程；
3. 原固定位置本来合法的武器不移动目标；
4. 超大 range 不产生纯射程环 Error；
5. 至少检测到一次合法攻击、投射物或伤害证据。

实际命令：

```powershell
& $Tool test run --mode auto `
  --target-package-id aoba.deadmanswitch.core `
  --active-mods-file $Mods `
  --scope turrets --scope rangedweapons `
  --defname DMS_Turret_Vasilek `
  --defname DMS_Gun_Vasilek_B `
  --defname DMS_DoorBreaker `
  --defname DMS_Flameblaster `
  --defname DMS_KnuckledusterRevolver `
  --capture-screenshots `
  --timeout-seconds 3600

if ($LASTEXITCODE -ne 0) { throw 'Goal 3 DMS 战斗距离验收失败' }
```

五个显式 DefName 对应的 Turret/Ranged Case 必须全部为 `passed`，不能以 unsupported 代替。

### 5.2 Ratkin 短射程抽检

至少覆盖：

- RK_Weapon_SawedOff；
- RK_StrawberryBeer。

SawedOff 应通过正常远程流程。

StrawberryBeer 若 `onlyManualCast` 且支持 Pawn target，应通过手动 Verb cast；如果 TargetingParameters 只支持其他目标，应明确 unsupported，而不是 failed_enemy_spawn 或 failed_no_damage。

本 Goal 的最终验收要求实现正确的手动施放目标类型，使以下命令退出码为 0：

```powershell
& $Tool test run --mode auto `
  --target-package-id solaris.ratkinracemod `
  --active-mods-file $Mods `
  --scope rangedweapons `
  --defname RK_Weapon_SawedOff `
  --defname RK_StrawberryBeer `
  --capture-screenshots `
  --timeout-seconds 2400

if ($LASTEXITCODE -ne 0) { throw 'Goal 3 Ratkin 短射程/手动施放验收失败' }
```

如果 StrawberryBeer 的设计确实不允许当前测试目标类型，则必须为它实现对应 Location/Building 测试场景；只标记 unsupported 不满足本 Goal 的最终通过标准。

### 5.3 基准武器回归

选取至少：

- 一个普通中射程枪械；
- 一个原版炮塔；
- 一个近战武器；
- 一个需要操作员的炮塔；
- 一个无视线或间接投射物武器。

确认统一服务没有破坏原本通过的测试。

## 6. Goal 3 独立回归与重复验收

### 6.1 编译和快速测试

依次执行：

1. AutoTest.Core.Tests；
2. AutoTestRunner 编译；
3. RimDevTools.Tests；
4. RimDevTools.ReleaseTests；
5. 本 Goal 的 DMS/Ratkin 定向测试。

所有失败必须在本 Goal 的提交前解决，不能等待范围外修改后再验证。

### 6.2 独立重复验收命令

第 5.1 节 DMS 命令和第 5.2 节 Ratkin 命令必须分别连续执行两次：

```powershell
1..2 | ForEach-Object {
  & $Tool test run --mode auto `
    --target-package-id aoba.deadmanswitch.core `
    --active-mods-file $Mods `
    --scope turrets --scope rangedweapons `
    --defname DMS_Turret_Vasilek `
    --defname DMS_Gun_Vasilek_B `
    --defname DMS_DoorBreaker `
    --defname DMS_Flameblaster `
    --defname DMS_KnuckledusterRevolver `
    --timeout-seconds 3600

  if ($LASTEXITCODE -ne 0) { throw "Goal 3 DMS 第 $_ 次验收失败" }
}

1..2 | ForEach-Object {
  & $Tool test run --mode auto `
    --target-package-id solaris.ratkinracemod `
    --active-mods-file $Mods `
    --scope rangedweapons `
    --defname RK_Weapon_SawedOff `
    --defname RK_StrawberryBeer `
    --timeout-seconds 2400

  if ($LASTEXITCODE -ne 0) { throw "Goal 3 Ratkin 第 $_ 次验收失败" }
}
```

两轮均必须满足：

- `completeness = Complete`；
- `manifestMissing = 0`；
- `manifestExecuted = manifestPlanned`；
- `failedAssertionCount = 0`；
- `severeLogCount = 0`；
- 所有显式目标 Case 状态为 `passed`；
- 不接受 `unsupported`、`skipped`、`compatibility_warning` 或 `passed_after_retry`。

### 6.3 Result 检查函数

```powershell
function Assert-Goal3LatestResultPassed {
  param([Parameter(Mandatory=$true)][string]$PackageId)

  $resultFile = Get-ChildItem '.\.tools\log\AutoTestOutput' -File -Filter "${PackageId}_*_Result.json" |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

  if ($null -eq $resultFile) { throw "找不到 $PackageId 的 Result.json" }

  $result = Get-Content -LiteralPath $resultFile.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
  $bad = @($result.results | Where-Object { $_.status -ne 'passed' })

  if ($result.completeness -ne 'Complete') { throw "Result 不完整: $($resultFile.FullName)" }
  if ([int]$result.manifestMissing -ne 0) { throw "manifestMissing != 0: $($resultFile.FullName)" }
  if ([int]$result.manifestExecuted -ne [int]$result.manifestPlanned) { throw "Executed != Planned: $($resultFile.FullName)" }
  if ([int]$result.failedAssertionCount -ne 0) { throw "存在失败断言: $($resultFile.FullName)" }
  if ([int]$result.severeLogCount -ne 0) { throw "存在严重日志: $($resultFile.FullName)" }
  if ($bad.Count -gt 0) {
    $bad | Select-Object caseType,defName,status,errorMessage | Format-Table -AutoSize
    throw "存在未通过 Case: $($resultFile.FullName)"
  }

  Write-Host "PASS Goal3 package=$PackageId runId=$($result.runId) result=$($resultFile.FullName)"
}
```

### 6.4 前后对比

只比较 Goal 3 涉及的战斗 Case：

- 原 `failed_enemy_spawn` 和 `failed_no_damage` 已消失；
- minRange、range 和实际目标距离证据完整；
- Case 数量没有下降；
- 两轮结果一致；
- 清理后无残留口袋地图、Pawn 或窗口。

## 7. Goal 3 独立文档与提交

Goal 3 完成时新增独立实施报告：

    策划案/自动测试误报修复_Goal3_实施报告.md

报告必须包含：

1. Goal 3 实际源码改动；
2. DMS 和 Ratkin 定向测试命令；
3. 四次重复运行的 runId、退出码和 Result 路径；
4. 战斗距离、onlyManualCast 和射程环问题的最终结果；
5. 遗留风险。

所有测试通过后创建独立提交，例如：

```powershell
git add <仅 Goal 3 相关文件>
git commit -m "fix: adapt autotest combat target distances"
```

提交不得包含 Pawn/建筑/Gene、Recipe/Hediff 或其他无关改动。

## 8. 完成标准

以下条件全部满足后才可完成 Goal 3：

1. 第 5.1、5.2 和 6.2 节命令全部返回退出码 0；
2. 所有 Goal 3 目标 Case 状态为 passed；
3. RangedWeapon 和 Turret 共用统一目标布置算法；
4. minRange 和 range 都进入实际位置计算；
5. 当前代码中“计算 targetDistance 但仍使用固定 anchor”的问题消失；
6. DMS 长射程、最小射程和短射程目标均能合理布置并通过测试；
7. onlyManualCast 不再被普通自动攻击语义误判，Ratkin 两个目标通过；
8. 超大射程环不再产生与战斗无关的严重日志；
9. Result、Manifest、Evidence 和 Report 数量完全一致；
10. 没有通过修改目标模组、全局吞 Error 或篡改武器射程获得通过；
11. Goal 3 独立实施报告已生成；
12. Goal 3 已单独提交，提交中没有其他 Goal 的改动。

## 9. 本 Goal 明确不做

- 不测试或验收 Axolotl；
- 不测试 Pawn/建筑/Gene 项目；
- 不测试 Recipe/Hediff 项目；
- 不修改目标武器和炮塔 Def；
- 不把所有敌人固定移动到同一个距离；
- 不忽略 minRange；
- 不把 range 强制截断到 40；
- 不全局屏蔽射程环或战斗 Error；
- 不把缺弹药、缺操作员、缺供电等问题伪装成距离问题。
