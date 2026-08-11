# 自动测试误报修复 Goal 2 实施记录

## 实施结果

Goal 2 已完成并通过正式定向验收。最终构建中：

- 普通 Recipe 通过 `StandardRecipeExecutionAdapter` 调用 `GenRecipe.MakeRecipeProducts`，再由 `GenPlace.TryPlaceThing` 正常拆堆并用 callback 汇总实际放置数量；
- 机械培育通过 `MechGestationRecipeExecutionAdapter` 驱动原版 `Bill_Mech`、`Building_MechGestator`、`Notify_BillWorkFinished`、`ForceCompleteAllCycles`、`BillTick` 和 `GenRecipe.FinalizeGestatedPawns`；
- 机械复活通过 `MechResurrectionRecipeExecutionAdapter` 从合法 Race/PawnKind 生成 Pawn，经正常 `Kill` 流程得到 Corpse，再由 `Bill_ResurrectMech` 正式复活；
- 不安全的 Pawn/自定义产品由 `UnsupportedSpecialRecipeExecutionAdapter` 明确分类，不会落入普通 `MakeRecipeProducts`；
- Hediff 使用每 Case 独立 Pawn，Pawn 和 BodyPart 分别由 `AutoTestHediffPawnResolver`、`AutoTestHediffPartResolver` 解析；
- Recipe 使用每 Case 独立、无测试残留的标准 Colonist 工人，并在 Case 结束后销毁；
- 仅 `TransientPawnGeneration`、`TransientUiTiming` 允许一次重试，成功时记录 `passed_after_retry`；本次正式验收未依赖重试。

## 构建与逻辑测试

以下命令均返回退出码 0：

```powershell
dotnet build .\RimDevTools\RimDevTools.csproj -c Release
dotnet run --project .\AutoTest.Core.Tests\AutoTest.Core.Tests.csproj -c Release
dotnet run --project .\RimDevTools.Tests\RimDevTools.Tests.csproj -c Release
dotnet run --project .\RimDevTools.ReleaseTests\RimDevTools.ReleaseTests.csproj -c Release
powershell -ExecutionPolicy Bypass -File .tools\deploy.ps1
```

测试数量：

- AutoTest.Core.Tests：87 passed；
- RimDevTools.Tests：303 passed；
- RimDevTools.ReleaseTests：61 passed。

## 正式端到端验收

| 验收组 | runId | Result | 结果 |
|---|---|---|---|
| DMS 五个 Mass Recipe + 两个 Hediff | `308d9e901f6641f2b40b8f70b716ca94` | `.tools/log/AutoTestOutput/aoba.deadmanswitch.core_308d9e901f6641f2b40b8f70b716ca94_Result.json` | 7/7 passed |
| DMS 29 个机械培育 Recipe | `c0944d3a669c478e8ba07140726f5f6d` | `.tools/log/AutoTestOutput/aoba.deadmanswitch.core_c0944d3a669c478e8ba07140726f5f6d_Result.json` | 29/29 passed |
| DMS 4 个机械复活 Recipe | `8ac71700545047a5be7a2ce59ffa3677` | `.tools/log/AutoTestOutput/aoba.deadmanswitch.core_8ac71700545047a5be7a2ce59ffa3677_Result.json` | 4/4 passed |
| Axolotl_WaterInvisibility | `c417baf61ba0427c9b49e7a879eb2f23` | `.tools/log/AutoTestOutput/hentailoliteam.axolotl_c417baf61ba0427c9b49e7a879eb2f23_Result.json` | 1/1 passed |
| Ratkin Hardtack 第 1 轮 | `79d0ad28d6f34d3395d10350006b9568` | `.tools/log/AutoTestOutput/solaris.ratkinracemod_79d0ad28d6f34d3395d10350006b9568_Result.json` | 2/2 passed |
| Ratkin Hardtack 第 2 轮 | `1f41e01c2fa64e4e970532500b6125c1` | `.tools/log/AutoTestOutput/solaris.ratkinracemod_1f41e01c2fa64e4e970532500b6125c1_Result.json` | 2/2 passed |

所有上述 Result 均满足：

- `completeness = Complete`；
- `manifestMissing = 0`；
- `manifestExecuted = manifestPlanned`；
- `failedAssertionCount = 0`；
- `severeLogCount = 0`；
- 无 `failed_recipe_produce`、`unsupported`、`skipped` 或未命中 DefName。

正式运行命令与策划案第 7 节一致。除统一的 `$Tool`、`$Mods` 初始化外，依次执行：

```powershell
& $Tool test run --mode auto --target-package-id aoba.deadmanswitch.core --active-mods-file $Mods `
  --scope recipes --scope hediffs `
  --defname DMS_Make_ArtifuscleMass --defname DMS_Make_CeramicsMass `
  --defname DMS_Make_PlasfiberMass --defname DMS_Make_PolyesterMass `
  --defname DMS_Make_TungsteelMass --defname DMS_SensoryFilter `
  --defname DMS_WeaponInprint --capture-screenshots --timeout-seconds 2400

& $Tool test run --mode auto --target-package-id aoba.deadmanswitch.core --active-mods-file $Mods `
  --scope recipes `
  --defname DMS_Make_Ape --defname DMS_Make_Arquebusier --defname DMS_Make_BattleFrame `
  --defname DMS_Make_Caretta --defname DMS_Make_Dogge --defname DMS_Make_EscortLifter `
  --defname DMS_Make_Falcon --defname DMS_Make_FieldCommand --defname DMS_Make_Gecko `
  --defname DMS_Make_Geochelone --defname DMS_Make_Gladiator --defname DMS_Make_Grenadier `
  --defname DMS_Make_HermitCrab --defname DMS_Make_Hyrax --defname DMS_Make_Iguana `
  --defname DMS_Make_Jaeger --defname DMS_Make_Killdozer --defname DMS_Make_Mammoth `
  --defname DMS_Make_Noctula --defname DMS_Make_Phalanx --defname DMS_Make_Pioneer `
  --defname DMS_Make_Protector --defname DMS_Make_Ronin --defname DMS_Make_Sergeant `
  --defname DMS_Make_ShaftWorker --defname DMS_Make_Siegebreaker --defname DMS_Make_Sniper `
  --defname DMS_Make_Soldat --defname DMS_Make_Tarbosaurus --timeout-seconds 3600

& $Tool test run --mode auto --target-package-id aoba.deadmanswitch.core --active-mods-file $Mods `
  --scope recipes `
  --defname DMS_ResurrectLightMech --defname DMS_ResurrectMediumMech `
  --defname DMS_ResurrectHeavyMech --defname DMS_ResurrectUltraheavyMech `
  --timeout-seconds 2400

& $Tool test run --mode auto --target-package-id hentailoliteam.axolotl --active-mods-file $Mods `
  --scope hediffs --defname Axolotl_WaterInvisibility `
  --capture-screenshots --timeout-seconds 1800

& $Tool test run --mode auto --target-package-id solaris.ratkinracemod --active-mods-file $Mods `
  --scope recipes --defname RK_Recipe_Hardtack --defname RK_Recipe_HardtackBulk `
  --timeout-seconds 1800

# 上一条 Ratkin 命令连续执行第二次。
```

## DMS Mass 拆堆证据

| Recipe | 总数 | 实际堆数 | Bill 完成 |
|---|---:|---:|---|
| DMS_Make_ArtifuscleMass | 100 | 2 | `BillIterationCompleted=True` |
| DMS_Make_CeramicsMass | 80 | 2 | `BillIterationCompleted=True` |
| DMS_Make_PlasfiberMass | 80 | 2 | `BillIterationCompleted=True` |
| DMS_Make_PolyesterMass | 200 | 3 | `BillIterationCompleted=True` |
| DMS_Make_TungsteelMass | 80 | 2 | `BillIterationCompleted=True` |

所有放置堆均通过 `stackCount <= stackLimit` 验证，正式 Result 的 `severeLogCount=0`，未出现 Spawn stackLimit Error。

## Hediff 上下文证据

- `DMS_SensoryFilter`：`DMS_Civilian`，Brain，来源 `targetModInstallRecipe`，安装 Recipe 为 `DMS_InstallSensoryFilter`；
- `DMS_WeaponInprint`：`DMS_Mech_Lady`，Brain，来源 `targetModCompDefinition`，上下文为 `Fortified.CompProperties_WeaponImprint.bodyPart`。目标模组实际 Def 中没有 `addsHediff=DMS_WeaponInprint` 的安装 Recipe，因此使用目标 Race Comp 的明确 bodyPart 定义，而非任意部位或 null；
- `Axolotl_WaterInvisibility`：原生萌螈 PawnKind `AxolotlCombatantSlaves`，保留 `Axolotl_BodyRepair`，全身 Hediff，Case 为 passed；
- 每个 Hediff Case 均记录目标 Hediff 移除、前置环境恢复、Pawn 销毁和 InfoCard 关闭。

## 特殊 Recipe 支持清单

### MechGestationRecipeExecutionAdapter（29/29 passed）

`DMS_Make_Ape`、`DMS_Make_Arquebusier`、`DMS_Make_BattleFrame`、`DMS_Make_Caretta`、`DMS_Make_Dogge`、`DMS_Make_EscortLifter`、`DMS_Make_Falcon`、`DMS_Make_FieldCommand`、`DMS_Make_Gecko`、`DMS_Make_Geochelone`、`DMS_Make_Gladiator`、`DMS_Make_Grenadier`、`DMS_Make_HermitCrab`、`DMS_Make_Hyrax`、`DMS_Make_Iguana`、`DMS_Make_Jaeger`、`DMS_Make_Killdozer`、`DMS_Make_Mammoth`、`DMS_Make_Noctula`、`DMS_Make_Phalanx`、`DMS_Make_Pioneer`、`DMS_Make_Protector`、`DMS_Make_Ronin`、`DMS_Make_Sergeant`、`DMS_Make_ShaftWorker`、`DMS_Make_Siegebreaker`、`DMS_Make_Sniper`、`DMS_Make_Soldat`、`DMS_Make_Tarbosaurus`。

### MechResurrectionRecipeExecutionAdapter（4/4 passed）

`DMS_ResurrectLightMech`、`DMS_ResurrectMediumMech`、`DMS_ResurrectHeavyMech`、`DMS_ResurrectUltraheavyMech`。

## Ratkin 连续执行证据

两轮的 `RK_Recipe_Hardtack`、`RK_Recipe_HardtackBulk` 均为 passed；每个 Case 生成并销毁不同的标准 Colonist 工人。两轮 Result/Evidence 均为 `severeLogCount=0`，搜索不到 `FoodPoisonChance`。

## 文件范围

只修改 LogSyncMod/AutoTestRunner、对应 Core 测试和本实施记录；没有修改 DMS、Axolotl、Ratkin 的 DLL、XML 或配方数据。
