# 自动测试误报修复 Goal 3 实施报告

## 1. 实施结果

Goal 3 已完成战斗目标距离统一、远程与炮塔接入、`onlyManualCast` 手动施放、近战布局收紧、超大射程环副作用规避，以及 DMS/Ratkin 独立重复验收。

最终状态：

- DMS 显式目标 5/5 passed；
- Ratkin 显式目标 2/2 passed；
- 两个包各连续独立运行两次，四轮均为 `Complete`、`manifestMissing=0`、`manifestExecuted=manifestPlanned`、`failedAssertionCount=0`、`severeLogCount=0`；
- 所有显式 Case 均为 `passed`，没有 `unsupported`、`skipped`、`compatibility_warning` 或 `passed_after_retry`；
- 原版中射程枪、原版炮塔和原版近战基准回归 3/3 passed。

## 2. 实际源码改动

### 2.1 统一距离策略与目标布置服务

新增：

- `source/AutoTestRunner/Core/AutoTestCombatDistancePolicy.cs`
- `source/AutoTestRunner/AutoTestCombatPlacementService.cs`

实现内容：

- 合法区间：`max(distanceFloor, minRange + minimumMargin)` 到 `min(range - maximumMargin, mapSafeMaximum)`；
- 短射程远程武器使用 2 格下限，不再被旧 5 格下限排除；
- 炮塔保留 5 格安全下限，同时实际检查 `minRange` 和 `range`；
- 优先保留原固定 5/8 布局，仅在原位置无效或部分无效时重排；
- 重排优先选择离原固定位置最近、再接近期望距离、横向偏移最小的稳定候选；
- 过滤 InBounds、安全区、可行走/占用、合法距离、视线和附加验证条件；
- 输出合法区间、期望距离、实际距离、是否重排、重排原因和候选过滤统计；
- 提供 1～2 格近战/围攻布置，供 MeleeWeapon 与 CombatPawn 共用。

### 2.2 RangedWeaponModule

- 从实际装备后的 `AllEquipmentVerbs` 解析主远程 Verb，失败时回退 `ThingDef.Verbs`；
- 删除“计算 `targetDistance` 但仍使用固定 anchor”的旧逻辑；
- Spawn 前记录 `VerbClass`、`MinRange`、`Range`、LOS、`OnlyManualCast` 和合法区间；
- Spawn 后记录实际距离，并再次验证 TargetingParameters、合法区间和 `Verb.CanHitTarget`；
- 支持 Pawn、Location 和 Building 三类目标准备；
- `onlyManualCast=true` 时不创建 `AttackStatic` Job，直接走 `Verb.TryStartCastOn`，记录 `AttackInitiation=ManualVerbCast`；
- 将合法投射物观测作为战斗通过证据之一，避免强制偏差很大的间接火力因未造成伤害被误判；
- 普通自动远程仍记录 `AttackInitiation=AttackStaticJob`。

### 2.3 TurretModule

- 优先解析当前 `AttackVerb`，其次使用 `GunCompEq.PrimaryVerb`，并保留反射兼容回退；
- 目标布置同时使用真实 `minRange`、有效 `range` 和 `requireLineOfSight`；
- 保留炮塔占地与 5 格安全距离；
- Spawn 后逐目标验证合法距离和 `CombatVerb.CanHitTarget`；
- 结果记录 `CombatMinRange`、`CombatMaxRange`、`EnemyRepositioned`、`RepositionReason` 和过滤统计；
- 操作员、供电、燃料/弹药仍由原准备流程负责，距离验证没有混入资源失败。

### 2.4 MeleeWeapon 与 CombatPawn

- MeleeWeapon 的敌人从旧固定约 12 格改为攻击者周围 1～1.41 格；
- CombatPawn 的围攻敌人复用近距离环带和稳定排序；
- 两者保留安全区、可行走和占用过滤诊断。

### 2.5 超大射程环

源码定位确认原版 `GenDraw.DrawRadiusRing` 在半径超过预计算上限时会输出 Error；测试战斗本身不需要选择 Pawn、武器或炮塔。

处理方式：

- 战斗截图继续直接使用相机服务；
- 切换到战斗截图地图前清理当前 Selection，避免无必要的选择覆盖层触发射程环；
- 未修改 Verb range；
- 未截断 89.9 射程；
- 未增加全局 Error 过滤或 Harmony 抑制。

最终 DMS 截图运行 `severeLogCount=0`。

### 2.6 自动测试

`AutoTest.Core.Tests` 新增 3 个距离策略测试：

- 7.9 短射程合法区间为 2～7.4；
- 19.9/89.9 间接火力合法区间正确受地图容量约束；
- 地图无法容纳合法区间时返回 `no_legal_distance_interval`。

Core 测试总数由 87 增至 90。

## 3. 定向测试命令

### 3.1 DMS

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
  --timeout-seconds 3600
```

截图验收 runId：`85a8675b144c4ff8affbc1b87cfa4a3e`，退出码 0。

### 3.2 Ratkin

```powershell
& $Tool test run --mode auto `
  --target-package-id solaris.ratkinracemod `
  --active-mods-file $Mods `
  --scope rangedweapons `
  --defname RK_Weapon_SawedOff `
  --defname RK_StrawberryBeer `
  --timeout-seconds 2400
```

截图验收 runId：`880ef8e224fa4435bca1d7080682d0b6`，退出码 0。

## 4. 四次重复运行

| 包 | 轮次 | runId | 退出码 | Result |
| --- | ---: | --- | ---: | --- |
| DMS | 1 | `7eca0fc6728046a6baa12d896a4d9406` | 0 | `.tools/log/AutoTestOutput/aoba.deadmanswitch.core_7eca0fc6728046a6baa12d896a4d9406_Result.json` |
| DMS | 2 | `e3197668b31f4c1d88660b028b3bbdba` | 0 | `.tools/log/AutoTestOutput/aoba.deadmanswitch.core_e3197668b31f4c1d88660b028b3bbdba_Result.json` |
| Ratkin | 1 | `f1fbd33d893040a7aa826440e9e0b548` | 0 | `.tools/log/AutoTestOutput/solaris.ratkinracemod_f1fbd33d893040a7aa826440e9e0b548_Result.json` |
| Ratkin | 2 | `a57ec90ac4654691988c87b30624f7ce` | 0 | `.tools/log/AutoTestOutput/solaris.ratkinracemod_a57ec90ac4654691988c87b30624f7ce_Result.json` |

四轮共同结果：

- `completeness=Complete`；
- `manifestMissing=0`；
- `manifestExecuted=manifestPlanned`；
- `failedAssertionCount=0`；
- `severeLogCount=0`；
- 所有 Result Case 状态为 `passed`。

## 5. 关键战斗证据

### 5.1 DMS

- `DMS_Turret_Vasilek`：`CombatMinRange=20.40`，实际距离约 21.00～21.10，`requireLineOfSight=false`，passed；
- `DMS_Gun_Vasilek_B`：同样布置在约 21 格，记录合法投射物并 passed；
- `DMS_DoorBreaker`：实际距离约 9.00～9.22，passed；
- `DMS_Flameblaster`：实际距离约 7.00～7.28，passed；
- `DMS_KnuckledusterRevolver`：实际距离约 10.00～10.20，passed。

### 5.2 Ratkin

- `RK_Weapon_SawedOff`：短射程位置自动调整，正常 `AttackStaticJob`，passed；
- `RK_StrawberryBeer`：`OnlyManualCast=True`，`AttackInitiation=ManualVerbCast`，合法投射物证据，passed。

### 5.3 基准回归

runId：`72552cb0e9c5443f8953c746ec59c90d`，退出码 0。

- `Gun_AssaultRifle`：原固定位置合法，`EnemyRepositioned=False`；
- `Turret_MiniTurret`：原固定位置合法，`EnemyRepositioned=False`；
- `MeleeWeapon_LongSword`：目标距离 1.00～1.41；
- 3/3 passed，`failedAssertionCount=0`，`severeLogCount=0`。

## 6. 快速回归

- `dotnet build .\RimDevTools\RimDevTools.csproj -c Release`：通过；
- `dotnet run --project .\AutoTest.Core.Tests\AutoTest.Core.Tests.csproj -c Release`：90 passed；
- `dotnet build .\source\AutoTestRunner\AutoTestRunner.csproj -c Release`：通过；
- `dotnet run --project .\RimDevTools.Tests\RimDevTools.Tests.csproj -c Release`：303 passed；
- `dotnet run --project .\RimDevTools.ReleaseTests\RimDevTools.ReleaseTests.csproj -c Release`：61 passed；
- `.tools/deploy.ps1`：通过。

## 7. 遗留风险

1. 当前五模组启动时间在高负载桌面环境下偶尔贴近 CLI 固定的 60 秒心跳阈值，曾在 Runner 启动前出现 `GAME_HUNG`；该问题没有产生 Result，也未计入重复验收。最终四轮均以独立进程成功完成。未为本 Goal 修改 CLI 心跳策略。
2. 建筑专用和 Location 专用手动 Verb 已有目标准备路径，但本 Goal 的显式验收对象 StrawberryBeer 最终按其有效 TargetingParameters 使用 Pawn target；后续新增纯建筑/纯地点武器时仍应加入专门的显式回归 DefName。
3. 候选搜索在 50×50 口袋地图内采用安全区全量扫描，当前规模开销很小；若未来口袋地图显著增大，可在不改变排序语义的前提下改为分层环带采样。

## 8. 范围确认

- 未修改任何目标模组 Def；
- 未修改武器或炮塔真实射程；
- 未全局吞掉 Error；
- 未测试或修改 Axolotl、Pawn/建筑/Gene、Recipe/Hediff 逻辑；
- 工作区中既有的 Goal 1/Goal 2 和策划案修改未纳入 Goal 3 提交。
