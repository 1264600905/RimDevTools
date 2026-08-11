# 自动测试误报修复 Goal 2：配方与 Hediff 执行流程

## 1. Goal 目标

本 Goal 重做配方与 Hediff 测试中的对象准备和真实执行流程，解决以下问题：

1. 普通配方产物经 `GenSpawn.Spawn` 直接落地，超过 stackLimit 时产生截断 Error；
2. 机械培育、机械复活等特殊配方被当成普通制造配方调用 `GenRecipe.MakeRecipeProducts`；
3. Hediff 安装部位通过随意选择身体部位兜底，可能产生错误安装或掩盖真实问题；
4. 所有 Hediff 都挂到同一个原版 Colonist，种族专属 Hediff 缺少合法上下文；
5. 配方测试复用不稳定工人，导致 `FoodPoisonChance` 等环境错误；
6. 重试策略没有区分瞬态错误和确定性错误。

本文件定义一个完全独立的开发任务。需要的结果状态、Pawn 解析、Recipe Adapter 和清理逻辑必须在本 Goal 范围内直接实现或复用当前已有稳定代码，不使用任何外部阶段作为前置条件。

## 2. 完成后的预期结果

完成后应达到：

- DMS 五个 Mass 配方按正常放置流程拆堆，总数量保持正确；
- DMS 两个 Hediff 使用手术定义中的合法部位，不再以任意部位或 null 安装；
- DMS 机械培育与复活配方不再进入普通 MakeRecipeProducts 路径；
- Axolotl_WaterInvisibility 优先挂载到具备目标模组原生前置环境的 Pawn；
- RK_Recipe_Hardtack 使用干净、合法的标准工人，重试不会掩盖真实错误；
- 特殊配方无法安全执行时明确输出 unsupported，而不是 failed_recipe_produce。

## 3. 当前问题与源码位置

主要文件：

    source/AutoTestRunner/AutoTestRunner.ContentModule.cs
    source/AutoTestRunner/AutoTestRunner.InfoCardModule.cs

### 3.1 普通产物调用原版生成函数后直接 Spawn

当前流程已经调用：

    GenRecipe.MakeRecipeProducts(...)

但对每个产物直接调用：

    GenSpawn.Spawn(producedThing, ...)

真正缺失的是原版放置/拆堆阶段，不是完全没有调用原版配方函数。

### 3.2 特殊配方过滤不足

当前 `IsSupportedRecipeProductDef` 只排除 Corpse、Blueprint 和 Frame。Pawn/Mech 产品以及 gestation/resurrection Recipe 仍会进入普通执行器。

### 3.3 Hediff 部位兜底不安全

当前 `ResolveHediffPart` 在部分 Hediff 上返回 `parts.FirstOrDefault()`。这不能证明该部位符合模组设计。

### 3.4 Hediff 共用原版 Colonist

当前 `EnsureSharedHediffPawn` 始终生成 `PawnKindDefOf.Colonist`，且多个 Hediff 共用同一个 Pawn。种族专属 Comp、startingHediffs、HediffGiverSet 和 RaceProps 都无法满足。

### 3.5 配方工人从现有地图殖民者中随意选择

当前通过 `FreeColonistsSpawned.FirstOrDefault` 选择工人。该 Pawn 可能已被其他测试改变健康、基因、能力、装备或 Stat 可用性。

## 4. 总体设计

### 4.1 配方执行器分层

新增统一配方执行器接口，建议结构：

    interface IAutoTestRecipeExecutionAdapter
    {
        bool CanHandle(RecipeDef recipe, Thing worktable);
        AutoTestRecipeExecutionResult Execute(...);
    }

至少实现：

1. `StandardRecipeExecutionAdapter`；
2. `MechGestationRecipeExecutionAdapter`；
3. `MechResurrectionRecipeExecutionAdapter`；
4. `UnsupportedSpecialRecipeExecutionAdapter` 作为安全兜底。

执行器选择必须发生在创建产品之前。

### 4.2 普通配方使用正常产品放置流程

普通配方仍可调用 `GenRecipe.MakeRecipeProducts`，但放置必须改用原版可拆堆流程，例如 `GenPlace.TryPlaceThing`。

通过 placed callback 收集所有实际放置产物，不得只保存原始大堆 Thing 引用。

### 4.3 Hediff 上下文解析分为 Pawn 和 BodyPart 两部分

新增两个独立解析器：

    AutoTestHediffPawnResolver
    AutoTestHediffPartResolver

不要继续把所有逻辑堆在 `StartHediffSession`。

### 4.4 重试只处理明确瞬态问题

确定性异常、严重日志、配方分类错误、缺失部位不能重试。

只有已知 UI 时序、Pawn 随机生成失败等瞬态问题允许一次重试，并必须输出 `passed_after_retry`。

## 5. 实施任务

### 5.1 新增配方分类模型

建议枚举：

    enum AutoTestRecipeExecutionKind
    {
        Standard,
        MechGestation,
        MechResurrection,
        PawnProduct,
        UnsupportedCustom
    }

分类依据至少包含：

- `RecipeDef.gestationCycles`；
- 产品 ThingDef 是否为 Pawn/Race；
- Recipe 是否继承或表现为 `BaseMechanoidRecipe`；
- Recipe 是否需要 Corpse 且属于机械复活；
- Worktable 是否为 Mech Gestator 或自定义培育建筑；
- 已加载 Recipe/Worktable 类型和 Comp 能力；
- XML Spec 是否显式指定适配器。

分类结果写入 Notes：

    RecipeExecutionKind
    RecipeExecutionAdapter
    RecipeClassificationReason

### 5.2 实现 StandardRecipeExecutionAdapter

执行顺序：

1. 验证 Recipe、BillGiver、BillStack 和工人；
2. 创建 Bill；
3. 按过滤器创建合法原料；
4. 调用 `GenRecipe.MakeRecipeProducts`；
5. 对每个产品调用正常放置 API；
6. 通过回调收集拆分后的每个 Thing；
7. 验证每堆 `stackCount <= stackLimit`；
8. 按 ThingDef 汇总实际总数；
9. 与生成前预期产品总数比较；
10. 调用 `Notify_IterationCompleted`；
11. 删除 Bill、销毁测试原料、保留产品到信息卡完成后再统一清理。

如果原版放置 API 返回多个拆分堆，Result 的 Products Notes 必须列出总数量和堆数。

不得自行把配方 XML 数量截断到 stackLimit。

### 5.3 实现机械培育配方适配器

目标是通过真实机械培育仓能力完成测试，不直接 `ThingMaker.MakeThing` 生成 Pawn 产品。

实施步骤：

1. 确认当前 RimWorld 1.6 中 Mech Gestator 的公开类型、Comp 和完成接口；
2. 识别 DMS_MechGestatorSmall 及其他 Recipe User 上的实际 gestator Comp；
3. 创建并放置真实工作台，保证 Faction、Power、Fuel 和必要 Comp 初始化；
4. 创建真实 Bill 并绑定 Recipe；
5. 注入合法原料；
6. 通过正式接受/开始 gestation 接口进入培育状态；
7. 测试环境允许加速进度，但不得绕过 Pawn 初始化接口；
8. 调用正式完成接口或推进到完成；
9. 验证产出 Pawn 的 health、kindDef、Faction、Dead 属性和 Map 状态均可安全读取；
10. 捕获信息卡或产物摘要后清理。

如果某个第三方 gestator 完全没有可安全调用的公开流程，使用本 Goal 自己定义或当前框架已有的 `unsupported` 状态，并记录缺失能力。不得回退到普通 MakeRecipeProducts。

Goal 2 的 DMS 验收中，优先要求 29 个配方进入正确适配器；支持真实流程的配方应实际执行，确实无法泛化的第三方路径才允许 unsupported。

### 5.4 实现机械复活配方适配器

实施步骤：

1. 从 fixedIngredientFilter 或 IngredientCount 中解析允许的 Corpse Def；
2. 使用对应 PawnKind/Race 的合法生成流程创建 Pawn；
3. 通过正常伤害/死亡流程得到 Corpse，不直接 MakeThing 一个空 Corpse；
4. 将 Corpse 与其他原料交给真实复活工作台流程；
5. 通过正式完成接口执行 Recipe；
6. 验证复活 Pawn 非 Dead、health tracker 完整、Faction 合理；
7. 清理 Pawn、Corpse、Bill 和工作台状态。

若 Recipe 的工作台 Comp 具有模组专用逻辑，允许通过反射适配，但必须限定类型和成员名称，并在成员缺失时输出 unsupported，不得静默吞异常。

### 5.5 修复普通配方产物拆堆验收

定向覆盖：

- DMS_Make_ArtifuscleMass；
- DMS_Make_CeramicsMass；
- DMS_Make_PlasfiberMass；
- DMS_Make_PolyesterMass；
- DMS_Make_TungsteelMass。

断言：

1. 没有 `Spawned ... stackCount ... stackLimit` Error；
2. 单堆不超限；
3. 总数分别保持 100、80、80、200、80；
4. Bill 完成通知执行；
5. 信息卡流程仍可正常关闭并清理。

### 5.6 实现 Hediff BodyPart 解析器

解析顺序：

1. `HediffDef.defaultInstallPart`；
2. 搜索 `RecipeDef.addsHediff == 当前 HediffDef`；
3. 从 `appliedOnFixedBodyParts` 选择 Pawn 实际拥有且未缺失的部位；
4. 如果多个 Recipe 给出多个部位，按目标模组 Recipe 优先、DefName 稳定排序；
5. 明确为全身 Hediff 时允许 null；
6. addedPart/injury 类型但无法确定合法部位时输出 unsupported；
7. 禁止返回任意 `parts.FirstOrDefault()` 作为通用兜底。

Notes 至少记录：

    HediffPart
    HediffPartSource
    HediffInstallRecipe

### 5.7 实现 Hediff Pawn Resolver

候选优先级：

1. XML Spec 显式指定的 PawnKind；
2. 目标模组 PawnKind 的 `startingHediffs` 直接包含目标 Hediff；
3. 目标模组 PawnKind 的 startingHediffs/HediffGiverSet 提供目标 Hediff可能依赖的原生环境；
4. 与 Hediff 所属模组相同的 Humanlike PawnKind；
5. 与 Hediff 所属模组相同的 Animal/Mechanoid PawnKind；
6. 原版 Colonist 安全回退。

每个候选必须检查：

- Pawn 可以生成；
- health tracker 存在；
- BodyPart 解析成功或 Hediff 明确允许全身；
- 必要 DLC tracker 存在；
- Spawn 后一个短稳定窗口内没有立即死亡或被销毁。

不要采用“依次挂载并制造严重日志，哪个不崩就用哪个”的探测方式。严重日志一旦产生已经污染测试。

对无法从 Def 静态推断的 C# 前置条件，允许在测试 Spec 中声明：

    presenterPawnKind
    prerequisiteHediffs
    requiredRace
    requiredFactionMode

### 5.8 重构 Hediff Session 生命周期

当前单一 `sharedHediffPawn` 应改为按上下文缓存，或每个 Hediff Case 使用独立 Pawn。

优先建议每 Case 独立 Pawn，原因：

- Hediff Comp 可能修改 Pawn 状态；
- RemoveHediff 可能有移除副作用；
- 不同种族 Hediff 不应共享同一个 Pawn；
- 便于准确归属日志。

如考虑运行耗时而使用缓存，Cache Key 至少包含：

    PawnKindDef
    RaceDef
    FactionMode
    prerequisiteHediffs signature

清理必须验证：

- 目标 Hediff 已移除；
- prerequisite Hediff 已恢复；
- Pawn 已销毁；
- Selector 和 InfoCard 已关闭。

### 5.9 为 Axolotl_WaterInvisibility 准备原生环境

定向逻辑应优先找到萌螈 PawnKind，并保留/补齐其原生 `Axolotl_BodyRepair` 环境，再挂载 `Axolotl_WaterInvisibility`。

主测试验证原生使用路径。

直接给普通人类 AddHediff 的行为可作为 compatibility_warning，但不能继续取代原生测试。

不得对目标模组 DLL 做补丁或 null 保护修改。

### 5.10 建立干净配方工人服务

新增专用工人生成器，例如：

    AutoTestRecipeWorkerService

要求：

1. 使用标准 Colonist/PawnKind；
2. PlayerColony Faction；
3. 非 Downed、非 Dead、无影响工作的测试残留 Hediff；
4. 必要 WorkType 可执行；
5. Cooking/GeneralLabor 等需要技能按 Recipe 初始化；
6. 禁止从地图现有殖民者中随意抓取；
7. 每个 Recipe Case 使用新工人，或完整恢复快照；
8. 工人生成和清理写入 Evidence。

### 5.11 实现有边界的重试策略

新增错误分类：

    DeterministicFailure
    UnsupportedPath
    TransientPawnGeneration
    TransientUiTiming
    UnexpectedSevereLog

只有 `TransientPawnGeneration` 和 `TransientUiTiming` 可重试一次。

`FoodPoisonChance` 一致性 Error 不直接重试，应先换用干净工人。换工人后成功记录 `passed_after_retry`，同时保留第一次失败原因。

配方产品错误、NRE、stackLimit Error、缺失部位等不得通过重试变成 passed。

## 6. 测试计划

### 6.1 单元/逻辑测试

- Recipe 分类表驱动测试；
- 标准产品拆堆计数测试；
- Hediff BodyPart 来源优先级测试；
- Hediff Pawn 候选排序测试；
- 重试错误分类测试；
- 本 Goal 自身的 unsupported/failed 结果分类测试。

### 6.2 DMS 定向测试

覆盖：

- 五个 Mass Recipe；
- DMS_SensoryFilter；
- DMS_WeaponInprint；
- 全部 gestation/resurrection Recipe，重点抽检 DMS_Make_Ape、DMS_Make_Hyrax 和复活配方。

### 6.3 Axolotl 定向测试

覆盖：

- Axolotl_WaterInvisibility；
- 一个普通全身 Hediff；
- 一个需要具体 BodyPart 的 Hediff。

验证原生 Pawn 选择和 prerequisite Notes。

### 6.4 Ratkin 定向测试

覆盖：

- RK_Recipe_Hardtack；
- RK_Recipe_HardtackBulk。

连续执行至少两次，确认不再因为复用工人产生 FoodPoisonChance Error。

## 7. 最终验收测试方法与命令

### 7.1 统一测试准备

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

任一步骤失败都不得继续验收。

### 7.2 DMS 普通配方拆堆与 Hediff 部位验收

```powershell
& $Tool test run --mode auto `
  --target-package-id aoba.deadmanswitch.core `
  --active-mods-file $Mods `
  --scope recipes --scope hediffs `
  --defname DMS_Make_ArtifuscleMass `
  --defname DMS_Make_CeramicsMass `
  --defname DMS_Make_PlasfiberMass `
  --defname DMS_Make_PolyesterMass `
  --defname DMS_Make_TungsteelMass `
  --defname DMS_SensoryFilter `
  --defname DMS_WeaponInprint `
  --capture-screenshots `
  --timeout-seconds 2400

if ($LASTEXITCODE -ne 0) { throw 'Goal 2 DMS 普通配方/Hediff 验收失败' }
```

必须验证五个配方总数量正确、单堆不超 stackLimit，两个 Hediff 的 `HediffPartSource` 来自合法 Def/安装 Recipe。

### 7.3 DMS 29 个机械培育配方验收

以下 DefName 已从现有失败 Result 中核对。29 个配方必须全部被发现、执行并通过：

```powershell
& $Tool test run --mode auto `
  --target-package-id aoba.deadmanswitch.core `
  --active-mods-file $Mods `
  --scope recipes `
  --defname DMS_Make_Ape `
  --defname DMS_Make_Arquebusier `
  --defname DMS_Make_BattleFrame `
  --defname DMS_Make_Caretta `
  --defname DMS_Make_Dogge `
  --defname DMS_Make_EscortLifter `
  --defname DMS_Make_Falcon `
  --defname DMS_Make_FieldCommand `
  --defname DMS_Make_Gecko `
  --defname DMS_Make_Geochelone `
  --defname DMS_Make_Gladiator `
  --defname DMS_Make_Grenadier `
  --defname DMS_Make_HermitCrab `
  --defname DMS_Make_Hyrax `
  --defname DMS_Make_Iguana `
  --defname DMS_Make_Jaeger `
  --defname DMS_Make_Killdozer `
  --defname DMS_Make_Mammoth `
  --defname DMS_Make_Noctula `
  --defname DMS_Make_Phalanx `
  --defname DMS_Make_Pioneer `
  --defname DMS_Make_Protector `
  --defname DMS_Make_Ronin `
  --defname DMS_Make_Sergeant `
  --defname DMS_Make_ShaftWorker `
  --defname DMS_Make_Siegebreaker `
  --defname DMS_Make_Sniper `
  --defname DMS_Make_Soldat `
  --defname DMS_Make_Tarbosaurus `
  --timeout-seconds 3600

if ($LASTEXITCODE -ne 0) { throw 'Goal 2 DMS 机械培育配方验收失败' }
```

验收时不接受把这些配方统一改成 `unsupported` 或 `skipped`。它们必须走 MechGestation Adapter，产出完整 Pawn，并以 `passed` 结束。

### 7.4 DMS 四个机械复活配方验收

```powershell
& $Tool test run --mode auto `
  --target-package-id aoba.deadmanswitch.core `
  --active-mods-file $Mods `
  --scope recipes `
  --defname DMS_ResurrectLightMech `
  --defname DMS_ResurrectMediumMech `
  --defname DMS_ResurrectHeavyMech `
  --defname DMS_ResurrectUltraheavyMech `
  --timeout-seconds 2400

if ($LASTEXITCODE -ne 0) { throw 'Goal 2 DMS 机械复活配方验收失败' }
```

当前框架可能尚未发现无普通 products 的复活 Recipe。本 Goal 必须补齐发现逻辑；任何 DefName 未命中都会使本命令失败，不能以“当前未发现”为由跳过。

### 7.5 Axolotl Hediff 原生 Pawn 上下文验收

```powershell
& $Tool test run --mode auto `
  --target-package-id hentailoliteam.axolotl `
  --active-mods-file $Mods `
  --scope hediffs `
  --defname Axolotl_WaterInvisibility `
  --capture-screenshots `
  --timeout-seconds 1800

if ($LASTEXITCODE -ne 0) { throw 'Goal 2 Axolotl Hediff 验收失败' }
```

该 Case 必须使用可证明的萌螈原生 Pawn/前置 Hediff 环境，并以 `passed` 结束。不接受给普通 Colonist 直接挂载后将异常改成 warning。

### 7.6 Ratkin 干净工人与重试验收

```powershell
& $Tool test run --mode auto `
  --target-package-id solaris.ratkinracemod `
  --active-mods-file $Mods `
  --scope recipes `
  --defname RK_Recipe_Hardtack `
  --defname RK_Recipe_HardtackBulk `
  --timeout-seconds 1800

if ($LASTEXITCODE -ne 0) { throw 'Goal 2 Ratkin 配方验收失败' }
```

该命令连续执行两次。两次均不得出现 `FoodPoisonChance` Error。正常目标是 `passed`；如果确实发生一次已分类的瞬态生成失败，允许 `passed_after_retry`，但必须保留第一次失败证据，且第二轮不能再次依靠重试通过。

### 7.7 Goal 2 严格通过标准

本节所有命令必须返回退出码 0。每个最新 Result.json 必须满足：

- `completeness = Complete`；
- `manifestMissing = 0`；
- 所有显式 DefName 均被发现；
- `manifestExecuted = manifestPlanned`；
- `failedAssertionCount = 0`；
- `severeLogCount = 0`；
- DMS Mass、Hediff、29 个培育和 4 个复活 Case 全部为 `passed`；
- Axolotl_WaterInvisibility 为 `passed`；
- Ratkin 两个 Recipe 为 `passed`，仅按 7.6 条件允许一次 `passed_after_retry`；
- 不接受 `failed_recipe_produce`、`unsupported`、`skipped` 或未命中 DefName。

如果特殊 Recipe 仍只能被标记为 unsupported，说明安全兜底已经完成，但 Goal 2 的“全部实现”尚未达到最终验收标准。

## 8. 完成标准

以下条件全部满足后才可完成 Goal 2：

1. 第 7 节所有定向验收命令通过；
2. 五个 DMS Mass Recipe 没有 stackLimit Error，且总产物数量正确；
3. 两个 DMS Hediff 的部位来自 defaultInstallPart 或安装 Recipe，而不是任意部位；
4. DMS 29 个机械培育和 4 个机械复活 Recipe 全部通过专用 Adapter；
5. 特殊 Recipe 可以完成并产出/恢复完整 Pawn；
6. Axolotl_WaterInvisibility 主测试使用原生兼容 Pawn并通过；
7. RK_Recipe_Hardtack 使用干净工人并连续两轮通过；
8. 重试成功按规则显示 `passed_after_retry`，且没有被滥用；
9. 目标模组文件无改动；
10. 输出 Goal 2 实施记录，包含每条命令、runId、Result 路径和特殊 Recipe 支持清单；
11. 测试通过后创建独立提交，例如 `git commit -m "fix: execute recipe and hediff autotests safely"`，提交内容只能属于 Goal 2。

## 9. 独立提交方法

只有第 7 节全部通过后才能提交：

```powershell
git status --short
git add <仅本 Goal 修改的源码、测试和实施报告>
git commit -m "fix: execute recipe and hediff autotests safely"
```

提交前必须检查暂存区，只允许包含 Recipe、Hediff、相关测试及本 Goal 实施报告。提交完成即结束本 Goal，不等待其他任务。

## 10. 本 Goal 明确不做

- 不修改与本 Goal 无关的 PawnKind 阵营和战斗距离逻辑；
- 不修改远程武器和炮塔距离算法；
- 不通过忽略所有严重日志来通过配方；
- 不把特殊 Recipe 重新伪装成普通 Recipe；
- 不修改 Axolotl DLL 或 XML；
- 不修改 DMS、Ratkin 配方数据。
