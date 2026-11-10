# XML 完整字段索引

此表逐项对照当前 `ScenarioLoader` 的属性/子节点白名单、预处理入口、`ArenaLoader` 和 `SquadTemplates`。用于查询支持字段；配装、观察、群战、擂台与混编语义仍读相应参考。仅列作者输入，不将冻结计划/结果 JSON 的内部字段混作 XML 配置。

## 阅读规则与必需结构

- 每行是该节点允许的完整属性和子节点集合，`—` 表示无属性/无子节点；字段名和层级区分大小写。列表不表示每个属性必填，也不允许将不同模式的属性任意组合。同名节点受父节点约束时用完整路径列出。
- TestSuite 必填 schemaVersion=1、id；gameVersion 默认 1.7，packageId 默认 ludeon.rimworld。Case 必填 id 和 testDefName 子节点；普通回归须 Scene/Actors/Steps/Success，群战须 Scene/Teams/Actors/GroupCombat，缺 Steps 会补空块。
- Actors 每项必须 id；Pawn/Corpse/PawnGroup 需 basePawnKind 或有效模板，squad 群组走独立契约。Steps 每个直属步骤必须唯一 id。Def/DefRef 用 defType+defName，Mod/DLC 用 packageId。各动作、配装和条件的必需参数见 [结构与角色](schema-and-actors.md)、[步骤](steps-and-observation.md)。
- 除明确可重复的列表项外单值节点不重复；Not 恰好一个条件，其余条件组合/Require/Wait/Success 不允许空。actor/group/target/focus/relativeTo/instigator/worktable/ref/since 等须符合对应引用范围。
- Case.extends/abstract/Patches 仅群战预处理；PawnTemplate.file 在群战及擂台 Load 导入，file 节点只填 id/file；SquadTemplates 仅群战/擂台。不能将其余源输入预处理字段泛化到普通 auto 或 ArenaSuite。
- 未列的 Class、任意字段路径和 AI 类入口不支持。ActionBinding.type/method 是明确声明、限定所属包的公开静态方法签名，见 [动作绑定](action-bindings.md)，不开放其他节点的反射入口。

## TestSuite：节点、属性和子节点

`li` 内容取决于父节点：apparelTags/weaponTags/abilities 下为纯文本；forcedTraits 下只允许 def/degree，skills 下只允许 skill/range/passion。其他角色叶子如 basePawnKind、race、gender、年龄范围、预算、外观均填文本。Case、Scene 和 PawnTemplate 的源输入扩展也列入本表。

| 节点 / 路径 | 允许属性 | 允许子节点 |
| --- | --- | --- |
| `Abilities` | `mode` | `Ensure`、`Remove` |
| `ActionBinding` | `id`、`packageId`、`type`、`method` | `Parameters`、`Arguments`、`Result`、`ExpectHediff` |
| `ActionBindings` | — | `ActionBinding` |
| `ActorCount` | `group`、`equals` | — |
| `ActorPosition` | `actor`、`x`、`z`、`radius` | — |
| `ActorState` | `actor`、`state`、`quantifier`、`equals`、`observation` | — |
| `Actors` | — | `Pawn`、`PawnGroup`、`Building`、`Thing`、`Corpse`、`DefRef`、`TerrainArea` |
| `AddHediff` | `id`、`actor`、`def`、`severity`、`part`、`durationTicks` | — |
| `AddNode` | `xpath` | `Value` |
| `Age` | `biologicalYears`、`chronologicalYears` | — |
| `All` | — | `All`、`Any`、`Not`、`HasHediff`、`ThingExists`、`ActorState`、`CorpseState`、`ActorCount`、`Equipped`、`Wearing`、`HasTrait`、`SkillLevel`、`HasAbility`、`StepSucceeded`、`HealthPercent`、`NeedLevel`、`ResourceLevel`、`ComponentState`、`EnvironmentState`、`ActorPosition`、`ModResourceLevel`、`ThingHeld`、`ThingQuantity`、`HasController`、`PawnAppearance`、`HasGene`、`HasXenotype`、`PlantGrowth`、`TerrainState`、`ThingStyle` |
| `Any` | — | `All`、`Any`、`Not`、`HasHediff`、`ThingExists`、`ActorState`、`CorpseState`、`ActorCount`、`Equipped`、`Wearing`、`HasTrait`、`SkillLevel`、`HasAbility`、`StepSucceeded`、`HealthPercent`、`NeedLevel`、`ResourceLevel`、`ComponentState`、`EnvironmentState`、`ActorPosition`、`ModResourceLevel`、`ThingHeld`、`ThingQuantity`、`HasController`、`PawnAppearance`、`HasGene`、`HasXenotype`、`PlantGrowth`、`TerrainState`、`ThingStyle` |
| `Apparel` | `mode` | `Add`、`Replace`、`Remove` |
| `Appearance` | — | `hair`、`bodyType`、`headType`、`hairColor`、`skinColor` |
| `ApplyDamage` | `id`、`actor`、`def`、`amount`、`armorPenetration`、`part`、`instigator` | — |
| `Arg` | `name`、`value` | — |
| `Argument` | `type`、`source`、`parameter`、`value`、`def` | — |
| `Arguments` | — | `Argument` |
| `Attack` | `id`、`actor`、`target`、`melee`、`timeoutRealSeconds` | — |
| `AxolotlQi` | `value` | — |
| `BindMech` | `id`、`actor`、`target` | — |
| `Building` | `id`、`def`、`stuff`、`team`、`x`、`z`、`count`、`quality`、`color`、`rotation`、`growth`、`spawn` | — |
| `Case` | `id`、`name`、`seed`、`timeoutRealSeconds`、`caseType`、`kind`、`extends`、`abstract` | `testDefName`、`Requirements`、`ActionBindings`、`Scene`、`Teams`、`Actors`、`Steps`、`Success`、`Targets`、`Coverage`、`Matrix`、`GroupCombat`、`Output`、`Patches` |
| `CastAbility` | `id`、`actor`、`def`、`target`、`targetX`、`targetZ`、`force`、`timeoutRealSeconds` | — |
| `Checkpoint` | `id` | — |
| `ComponentState` | `actor`、`component`、`state`、`min`、`max` | — |
| `Corpse` | `id`、`template`、`team`、`x`、`z`、`spawn` | `basePawnKind`、`race`、`gender`、`minGenerationAge`、`maxGenerationAge`、`apparelTags`、`apparelMoney`、`weaponTags`、`weaponMoney`、`forcedTraits`、`skills`、`abilities`、`Apparel`、`Equipment`、`Traits`、`Skills`、`Hediffs`、`Abilities`、`Appearance`、`Genes`、`Xenotype`、`Age`、`Resources` |
| `CorpseState` | `actor`、`state` | — |
| `Coverage` | `mode` | `Target` |
| `CreateThing` | `id`、`def`、`stuff`、`x`、`z`、`count` | — |
| `DLC` | `packageId` | — |
| `DebugAction` | `id`、`actionId`、`value`、`def`、`severity`、`part`、`durationTicks` | `Select`、`Arg`、`Expect` |
| `Def` | `defType`、`defName` | — |
| `DefRef` | `id`、`defType`、`defName` | — |
| `Destroy` | `id`、`actor` | — |
| `DropEquipment` | `id`、`actor` | — |
| `DropInventory` | `id`、`actor`、`target` | — |
| `End` | `condition`、`onTimeLimit`、`onTie`、`confirmationTicks` | — |
| `Environment` | `weather`、`temperature`、`roof`、`naturalSun` | — |
| `EnvironmentState` | `property`、`def`、`min`、`max` | — |
| `Equip` | `id`、`actor`、`def`、`stuff` | — |
| `Equipment` | `mode` | `SetPrimary` |
| `Equipped` | `actor`、`def` | — |
| `Evidence` | `step` | — |
| `ExecuteRecipe` | `id`、`actor`、`recipe`、`worktable`、`accelerate`、`output` | `Ingredients` |
| `ExpectHediff` | `type`、`def`、`count`、`property`、`equals`、`equalsParameter` | — |
| `Experiments` | `swapSides` | `Seed` |
| `GeneResource` | `def`、`value` | — |
| `Genes` | `mode` | `Add`、`Remove` |
| `GroupCombat` | `start`、`aiPolicy`、`durationSeconds`、`timeSpeed` | `End`、`Metrics`、`Overlay`、`Experiments` |
| `HasAbility` | `actor`、`def` | — |
| `HasController` | `actor`、`target` | — |
| `HasGene` | `actor`、`def`、`active`、`xenogene` | — |
| `HasHediff` | `actor`、`def`、`severityMin`、`severityMax`、`quantifier`、`equals`、`transition`、`since`、`observation` | — |
| `HasTrait` | `actor`、`def`、`degree` | — |
| `HasXenotype` | `actor`、`def` | — |
| `HealthPercent` | `actor`、`min`、`max`、`quantifier`、`equals`、`observation` | — |
| `Hediffs` | `mode` | `Ensure`、`Remove` |
| `Ingredient` | `actor`、`count` | — |
| `Ingredients` | — | `Ingredient` |
| `Kill` | `id`、`actor` | — |
| `Layout` | — | `TerrainRect`、`RoofRect`、`ThingRect` |
| `ManTurret` | `id`、`actor`、`target`、`timeoutRealSeconds` | — |
| `Matrix` | — | `Row` |
| `Metrics` | `damageBasis`、`dpsBasis`、`teamDpsBasis`、`population`、`meanPopulation`、`includeZeroValues`、`distributions`、`detailLimit`、`requireCompleteAttribution` | — |
| `Mod` | `packageId` | — |
| `ModResourceLevel` | `actor`、`resource`、`min`、`max` | — |
| `NeedLevel` | `actor`、`def`、`min`、`max`、`quantifier`、`equals` | — |
| `Not` | — | `All`、`Any`、`Not`、`HasHediff`、`ThingExists`、`ActorState`、`CorpseState`、`ActorCount`、`Equipped`、`Wearing`、`HasTrait`、`SkillLevel`、`HasAbility`、`StepSucceeded`、`HealthPercent`、`NeedLevel`、`ResourceLevel`、`ComponentState`、`EnvironmentState`、`ActorPosition`、`ModResourceLevel`、`ThingHeld`、`ThingQuantity`、`HasController`、`PawnAppearance`、`HasGene`、`HasXenotype`、`PlantGrowth`、`TerrainState`、`ThingStyle` |
| `Output` | `purpose`、`showAllParticipants`、`width`、`height` | — |
| `Overlay` | `enabled`、`layout`、`topUnitsPerMetric`、`topTypesPerTeam`、`humanPortrait`、`refreshRealMilliseconds` | `Rankings`、`Summary` |
| `Param` | `name`、`value` | — |
| `Parameter` | `name`、`type`、`min`、`max` | — |
| `Parameters` | — | `Parameter` |
| `Patches` | — | `SetAttribute`、`SetText`、`AddNode`、`ReplaceNode`、`RemoveNode` |
| `Pause` | `id` | — |
| `Pawn` | `id`、`template`、`team`、`x`、`z`、`spawn` | `basePawnKind`、`race`、`gender`、`minGenerationAge`、`maxGenerationAge`、`apparelTags`、`apparelMoney`、`weaponTags`、`weaponMoney`、`forcedTraits`、`skills`、`abilities`、`Apparel`、`Equipment`、`Traits`、`Skills`、`Hediffs`、`Abilities`、`Appearance`、`Genes`、`Xenotype`、`Age`、`Resources` |
| `PawnAppearance` | `actor`、`property`、`value` | — |
| `PawnGroup` | `id`、`template`、`squad`、`costMode`、`team`、`count`、`combatPowerBudget`、`originX`、`originZ`、`spacing`、`formation`、`columns`、`spawn` | `basePawnKind`、`race`、`gender`、`minGenerationAge`、`maxGenerationAge`、`apparelTags`、`apparelMoney`、`weaponTags`、`weaponMoney`、`forcedTraits`、`skills`、`abilities`、`Apparel`、`Equipment`、`Traits`、`Skills`、`Hediffs`、`Abilities`、`Appearance`、`Genes`、`Xenotype`、`Age`、`Resources`、`SquadTemplate` |
| `PawnTemplate` | `id`、`extends`、`file` | `basePawnKind`、`race`、`gender`、`minGenerationAge`、`maxGenerationAge`、`apparelTags`、`apparelMoney`、`weaponTags`、`weaponMoney`、`forcedTraits`、`skills`、`abilities`、`Apparel`、`Equipment`、`Traits`、`Skills`、`Hediffs`、`Abilities`、`Appearance`、`Genes`、`Xenotype`、`Age`、`Resources` |
| `PlantGrowth` | `actor`、`min`、`max` | — |
| `Psyfocus` | `value` | — |
| `Rankings` | `damageDealt`、`damageTaken` | — |
| `Reachable` | `fromX`、`fromZ`、`toX`、`toZ` | — |
| `Relation` | `a`、`b`、`value` | — |
| `RemoveHediff` | `id`、`actor`、`def`、`part` | — |
| `RemoveNode` | `xpath` | — |
| `Replace` | `matchDef`、`matchLayer`、`withDef`、`stuff`、`quality`、`color`、`onMissing` | — |
| `ReplaceNode` | `xpath` | `Value` |
| `Require` | `id` | `All`、`Any`、`Not`、`HasHediff`、`ThingExists`、`ActorState`、`CorpseState`、`ActorCount`、`Equipped`、`Wearing`、`HasTrait`、`SkillLevel`、`HasAbility`、`StepSucceeded`、`HealthPercent`、`NeedLevel`、`ResourceLevel`、`ComponentState`、`EnvironmentState`、`ActorPosition`、`ModResourceLevel`、`ThingHeld`、`ThingQuantity`、`HasController`、`PawnAppearance`、`HasGene`、`HasXenotype`、`PlantGrowth`、`TerrainState`、`ThingStyle` |
| `Requirements` | — | `Mod`、`DLC` |
| `ResourceLevel` | `actor`、`def`、`min`、`max`、`quantifier`、`equals` | — |
| `Resources` | — | `GeneResource`、`Psyfocus`、`AxolotlQi` |
| `Result` | `equals` | — |
| `Resurrect` | `id`、`actor` | — |
| `RoofRect` | `def`、`x`、`z`、`width`、`height` | — |
| `Row` | `id` | `Param` |
| `Scene` | `id`、`preset`、`type`、`sizeX`、`sizeZ`、`revealFog`、`generatorDef` | `Environment`、`Layout`、`Validation` |
| `ScenePresets` | — | `Scene` |
| `Screenshot` | `id`、`focus`、`cameraSize`、`stableFrames` | — |
| `Seed` | `value` | — |
| `Select` | `mode`、`actor`、`minX`、`minZ`、`maxX`、`maxZ`、`filter`、`relativeTo` | — |
| `SetAttribute` | `xpath`、`name`、`value` | — |
| `SetPlantGrowth` | `id`、`actor`、`value` | — |
| `SetPrimary` | `def`、`stuff`、`quality`、`color` | — |
| `SetRot` | `id`、`actor`、`state` | — |
| `SetText` | `xpath`、`value` | — |
| `SkillLevel` | `actor`、`def`、`min`、`max` | — |
| `Skills` | `mode` | `Set` |
| `Spawn` | `id`、`actor` | — |
| `StepSucceeded` | `ref` | — |
| `Steps` | — | `Require`、`Checkpoint`、`Wait`、`Screenshot`、`View`、`SetPlantGrowth`、`CastAbility`、`Attack`、`Spawn`、`CreateThing`、`DropEquipment`、`Equip`、`AddHediff`、`RemoveHediff`、`Destroy`、`Kill`、`Pause`、`DebugAction`、`ExecuteRecipe`、`TransferThing`、`DropInventory`、`SetRot`、`Resurrect`、`ApplyDamage`、`BindMech`、`ManTurret` |
| `Success` | — | `All`、`Any`、`Not`、`HasHediff`、`ThingExists`、`ActorState`、`CorpseState`、`ActorCount`、`Equipped`、`Wearing`、`HasTrait`、`SkillLevel`、`HasAbility`、`StepSucceeded`、`HealthPercent`、`NeedLevel`、`ResourceLevel`、`ComponentState`、`EnvironmentState`、`ActorPosition`、`ModResourceLevel`、`ThingHeld`、`ThingQuantity`、`HasController`、`PawnAppearance`、`HasGene`、`HasXenotype`、`PlantGrowth`、`TerrainState`、`ThingStyle` |
| `Summary` | `dps`、`damageTaken` | — |
| `Target` | `packageId`、`defType`、`defName`、`caseType`、`variant` | `Evidence` |
| `Targets` | — | `Def` |
| `Team` | `id`、`name`、`factionDef`、`factionSource`、`ai`、`defendX`、`defendZ`、`defendRadius` | — |
| `Teams` | — | `Team`、`Relation` |
| `Templates` | — | `PawnTemplate` |
| `TerrainArea` | `id`、`def`、`x`、`z`、`width`、`height` | — |
| `TerrainRect` | `def`、`x`、`z`、`width`、`height` | — |
| `TerrainState` | `actor`、`def` | — |
| `TestSuite` | `schemaVersion`、`id`、`packageId`、`gameVersion` | `Requirements`、`ActionBindings`、`Templates`、`ScenePresets`、`Case`、`SquadTemplates` |
| `Thing` | `id`、`def`、`stuff`、`team`、`x`、`z`、`count`、`quality`、`color`、`rotation`、`growth`、`spawn` | — |
| `ThingExists` | `actor`、`def`、`quantifier`、`equals`、`transition`、`since`、`observation` | — |
| `ThingHeld` | `actor`、`target` | — |
| `ThingQuantity` | `actor`、`def`、`min`、`max` | — |
| `ThingRect` | `def`、`stuff`、`x`、`z`、`width`、`height`、`rotation` | — |
| `ThingStyle` | `actor`、`stuff`、`quality`、`color`、`rotation` | — |
| `Traits` | `mode` | `Set`、`Remove` |
| `TransferThing` | `id`、`actor`、`target` | — |
| `Validation` | — | `Reachable` |
| `Value` | — | 由目标位置语法决定的 XML 元素 |
| `View` | `id`、`actor`、`directions`、`pose`、`loadout`、`targetDef`、`cameraSize`、`stableFrames`、`composite`、`timeoutRealSeconds` | — |
| `Wait` | `id`、`timeoutRealSeconds`、`stableTicks`、`durationTicks`、`observation` | `All`、`Any`、`Not`、`HasHediff`、`ThingExists`、`ActorState`、`CorpseState`、`ActorCount`、`Equipped`、`Wearing`、`HasTrait`、`SkillLevel`、`HasAbility`、`StepSucceeded`、`HealthPercent`、`NeedLevel`、`ResourceLevel`、`ComponentState`、`EnvironmentState`、`ActorPosition`、`ModResourceLevel`、`ThingHeld`、`ThingQuantity`、`HasController`、`PawnAppearance`、`HasGene`、`HasXenotype`、`PlantGrowth`、`TerrainState`、`ThingStyle` |
| `Wearing` | `actor`、`def` | — |
| `Xenotype` | `def` | — |
| `abilities` | — | `li` |
| `apparelMoney` | — | — |
| `apparelTags` | — | `li` |
| `basePawnKind` | — | — |
| `bodyType` | — | — |
| `def` | — | — |
| `degree` | — | — |
| `forcedTraits` | — | `li` |
| `gender` | — | — |
| `hair` | — | — |
| `hairColor` | — | — |
| `headType` | — | — |
| `li` | — | `def`、`degree`、`skill`、`range`、`passion` |
| `maxGenerationAge` | — | — |
| `minGenerationAge` | — | — |
| `passion` | — | — |
| `race` | — | — |
| `range` | — | — |
| `skill` | — | — |
| `skills` | — | `li` |
| `skinColor` | — | — |
| `testDefName` | — | — |
| `weaponMoney` | — | — |
| `weaponTags` | — | `li` |
| `Apparel/Add` | `def`、`stuff`、`quality`、`color` | — |
| `Genes/Add` | `def`、`xenogene` | — |
| `Apparel/Remove` | `def` | — |
| `Traits/Remove` | `def` | — |
| `Hediffs/Remove` | `def`、`part` | — |
| `Abilities/Remove` | `def` | — |
| `Genes/Remove` | `def` | — |
| `Traits/Set` | `def`、`degree` | — |
| `Skills/Set` | `def`、`level`、`passion` | — |
| `Hediffs/Ensure` | `def`、`severity`、`part`、`durationTicks` | — |
| `Abilities/Ensure` | `def` | — |

## ArenaSuite：独立字段表

Scene 及其 Environment/Layout/Validation 复用上表地图语法，SquadTemplates 见下一表。其余按此表，不从 TestSuite 自动继承能力。ArenaSuite 必填 schemaVersion=1/id；Tournament 必填 id/testDefName/Scene/Roster/Entrants；Roster 必填 mode，combatPower 模式还须 budget；参赛项必填 id 并二选一 pawnKind/squad；Showcase 页的 Entrant 必填 ref。

| 节点 / 路径 | 允许属性 | 允许子节点 |
| --- | --- | --- |
| `ArenaSuite` | `schemaVersion`、`id`、`gameVersion` | `Tournament`、`SquadTemplates`、`Templates`、`Requirements`、`ScenePresets`、`ActionBindings` |
| `Tournament` | `id`、`name`、`seed`、`format` | `testDefName`、`Scene`、`Roster`、`Entrants`、`Battle`、`Matchmaking`、`Growth`、`Cleanup`、`Scoring`、`TieBreak`、`Presentation`、`Output`、`Requirements`、`Metrics`、`Overlay`、`ActionBindings`、`Preparation` |
| `testDefName` | — | — |
| `Roster` | `mode`、`budget`、`count`、`costMode` | — |
| `Entrants` | — | `Entrant` |
| `Entrants/Entrant` | `id`、`pawnKind`、`squad`、`name`、`count`、`budget`、`xenotype`、`role`、`mode`、`costMode` | — |
| `Battle` | `durationSeconds`、`timeoutRealSeconds`、`timeSpeed`、`confirmationTicks`、`aiPolicy` | — |
| `Preparation` | — | `Require`、`DebugAction`、`Equip`、`DropEquipment`、`AddHediff`、`RemoveHediff`、`ApplyDamage`；每步另可用 entrant/role/group 选择范围；引用占位符 entrant |
| `Matchmaking` | `mode` | — |
| `Growth` | `target`、`mode`、`multiplier`、`increment`、`maxValue` | — |
| `Cleanup` | `corpses`、`droppedItems` | — |
| `Scoring` | `survivalWeight`、`suppressionWeight`、`exchangeWeight`、`speedWeight`、`timeReferenceSeconds` | — |
| `TieBreak` | `onDraw`、`maxReplays` | — |
| `Presentation` | `bracketSeconds`、`settlementSeconds`、`advanceStartSeconds` | `Showcase` |
| `Showcase` | `pageSeconds` | `Page` |
| `Page` | `seconds`、`caption` | `Entrant` |
| `Page/Entrant` | `ref` | — |
| `Output` | `width`、`height` | — |

## 混编模板：独立字段表

source=faction 必须 GroupMaker；source=custom 必须 Composition；source=generator 必须 Generator；source=groups 只接受 PawnGroup 子节点，复用 Templates，不能混填来源块。SquadTemplate 必填 id/source/factionDef；出现 GroupMaker、Composition、Xenotypes 时须 mode；specific maker 须 index，fixed Xenotypes 须 def；Option 的 weight、Member 的 count、Generator 的 id、Param 的 name/value 均必填。细节见 [混编与预算](squads-and-rosters.md)。

| 节点 / 路径 | 允许属性 | 允许子节点 |
| --- | --- | --- |
| `SquadTemplates` | — | `SquadTemplate` |
| `SquadTemplate` | `id`、`source`、`factionDef` | `GroupMaker`、`RaidRules`、`Composition`、`Xenotypes`、`Generator`、`PawnGroup` |
| `SquadTemplate/PawnGroup`（仅 groups） | `id`、`template`、`count` | 单兵配置字段同上表 PawnTemplate；不接受部署、squad 或预算 |
| `GroupMaker` | `mode`、`kindDef`、`index`、`fingerprint`、`referencePoints` | — |
| `RaidRules` | `strategyDef` | — |
| `Composition` | `mode`、`allocator` | `Option`、`Member` |
| `Composition/Option` | `pawnKind`、`weight` | — |
| `Composition/Member` | `pawnKind`、`count` | — |
| `Xenotypes` | `mode`、`def` | `Option` |
| `Xenotypes/Option` | `def`、`weight` | — |
| `Generator` | `id` | `Param` |
| `Generator/Param` | `name`、`value` | — |

## 默认值、枚举和模式限制

下表补充省略属性时的行为，以及容易被字段白名单误导的限制；原生 Def、tracker、身体部位与加载依赖仍要运行时校验。

| 字段 / 场合 | 默认、合法值或约束 |
| --- | --- |
| Case.seed / name / caseType | seed=12345；name 缺省用 testDefName；普通 caseType=ExternalScenario，群战冻结为 GroupCombat |
| Case.timeoutRealSeconds | 普通回归=60，群战=600；擂台单战由 Battle 配置，默认 300；CLI 超时另算 |
| 角色位置与生成 | x/z 或 originX/originZ 各默认 20；spawn=true，Corpse 禁止 false；PawnGroup.count=1；普通场景 count 上限 100，群战每队 200 |
| PawnGroup 阵型 | formation=line，或 grid；spacing=2 且至少 1；grid columns=5，line 按 count；战斗沿 Z 优先，普通回归沿 X 优先 |
| Thing/Building | Thing.count=1，Building 生成一个建筑，不能靠 count 生成建筑组；未写材质时可材质化物品用原生默认材质，品质/颜色省略不覆盖；growth 只适用植物且 0—1 |
| TerrainArea / Layout | TerrainArea 作者宜显式给 x/z/width/height，运行时缺尺寸按 1；TerrainRect/ThingRect/RoofRect 必须给矩形位置和正尺寸；占地必须在界内 |
| Scene / Environment | type 由场景类别限定；sizeX/sizeZ 普通=50、擂台=100，范围 30—250；revealFog 仅 all。weather/temperature/roof 省略不覆盖，naturalSun=false；RoofRect 缺 def 等同 none；详见地图参考 |
| tracker.mode / 基因 | mode=patch，可 inherit/replace；inherit 禁止修改子项，replace 清原内容。Genes/Add.xenogene=false；HasGene 的 active/xenogene 省略表示不筛选对应属性 |
| Replace / 物品样式 | matchDef/matchLayer 恰好一个；onMissing=fail 或 add；withDef 必填，支持 color。color 可 #RRGGBB 或 #RRGGBBAA；rotation 为 North/East/South/West；quality 使用原生 QualityCategory |
| 角色属性 | gender=Male/Female/None，passion=None/Minor/Major；Traits/Set、HasTrait 的 degree=0；Skills/Set 未写 level 为 0，显式等级应 0—20；Age 至少一项非负；min/maxGenerationAge 是整数 |
| Hediff | severity 省略保留原生初值；part 省略不限定部位；durationTicks 必须正数且目标有原生消失组件 |
| Wait / 步骤超时 | 普通异步步骤 timeoutRealSeconds=15；Wait.stableTicks=1、observation=current；eventually 保留已观察匹配；always 必须 durationTicks>0。durationTicks 不是普通 current/eventually 的通用延迟 |
| 条件量词 | 仅声明 quantifier 的节点接受 any/all/count，默认 any；count 使用整数 equals（省略为 0）。HasHediff 的 count 计命中角色数，不是该角色的 Hediff 实例数 |
| 条件观察 | 仅声明 observation 的叶子可覆盖 current/eventually，或继承 Wait；always 连续窗口由 Wait 实现。transition 仅 added/removed；since 通常默认 prepared，Wait eventually 默认用该 Wait 同名基线 |
| 数值条件 | HealthPercent/NeedLevel/ResourceLevel/ModResourceLevel/PlantGrowth 默认 min=0/max=1；SkillLevel 默认 0/20；ThingQuantity、severityMax、powerOutput 上界默认 double.MaxValue；ActorPosition.radius=0 |
| ComponentState | component=fuel/battery 查比例，power 要布尔 state，powerOutput 查实际功率，rot 用 Fresh/Rotting/Dessicated；不支持任意组件路径 |
| EnvironmentState | property=weather 配 def；temperature/sunGlow/skyGlow 配 min/max（缺省范围 0—1，温度应显式给边界）；unfogged 核对解雾 |
| ActorState / CorpseState | ActorState: alive/dead/downed/standing/spawned/destroyed；CorpseState: valid/spawned/destroyed |
| CastAbility / Attack | CastAbility.target 与 targetX+targetZ 二选一，force 仅 false；Attack.melee=false；动作接受不保证命中或效果 |
| Screenshot / View | Screenshot.cameraSize=12/stableFrames=3；View.cameraSize=11/stableFrames=5、pose=standing、loadout=preserve、composite=true、directions=South,East,North；静态视图专用 caseType 默认 North；cameraSize 11—100，stableFrames 3—120 |
| View | timeoutRealSeconds 默认 15（整个步骤）；directions 是 1—4 个唯一原生方向；pose=standing/weaponReady/auto；isolate 必须 targetDef |
| CreateThing / ApplyDamage | CreateThing.count=1，省略 x/z 在场景中心；ApplyDamage.amount>0，armorPenetration=0 且非负，不绕过原生受击 |
| ExecuteRecipe / DebugAction | ExecuteRecipe 只接受 accelerate=true，Ingredient.count 必填；固定 DebugAction 用对应属性，绑定动作使用 Arg，不能互换 |
| Coverage / Matrix | Coverage.mode=supplement 或 replace；Target.packageId 默认 Suite，variant 默认空；Evidence.step 必填；Matrix 最多 256 个显式 Row |
| 群战 AI / 时间 | aiPolicy=assaultColony；Team.ai 优先；battle/seekAndAttack/native 为同控制器别名，另可 assaultThings/defendPoint；timeSpeed 默认 normal，可 fast/superfast；durationSeconds=180，范围 (0,86400] |
| Team 防御配置 | 仅 ai=defendPoint 可填 defendX/defendZ/defendRadius；未写位置用该队成员均值，radius=28 且须有限正数；普通 auto 的 Team 只引用已有阵营，不运行群战 AI 配置 |
| 群战 End | start 仅 simultaneous；condition 仅 oneSideAllDowned，onTimeLimit 仅 fewestAliveLoses，onTie 仅 draw；confirmationTicks=300，范围 0—36000；固定字段不是可自由扩展的策略枚举 |
| 群战 Metrics 固定项 | damageBasis=engineSettled，dpsBasis=unitLifetime，teamDpsBasis=battleWindow，population=registeredRoster（旧 initialRoster 归一化），meanPopulation=aliveOnMap，includeZeroValues=true，distributions=min,max,mean,median |
| 群战 Metrics 可调项 | detailLimit=10000，范围 0—1000000；requireCompleteAttribution=false；裁明细不裁累计；要求完整归因不等于后端天然具备完整归因 |
| 群战 Overlay | enabled=true 可调；其余固定为 layout=teamsLeftRight、topUnitsPerMetric=10、topTypesPerTeam=5、humanPortrait=renderedPawn、refreshRealMilliseconds=250；Rankings.damageDealt/damageTaken 仅 true；Summary.dps 仅 livingUnitLifetimeMean，damageTaken 仅 totalAndMeanPerLivingUnit |
| 群战 Experiments / Output | swapSides=false，无 Seed 时用 Case.seed；最多 128 个显式种子/1024 轮；purpose 仅 benchmark，showAllParticipants 仅 true；width/height 须成对且范围为 800—3840/600—2160 |
| 擂台 format / role | format 默认 knockout，另可 bossRoundRobin/limitChallenge；新赛制每队 role 必填 boss/challenger，至少各一队，允许重复 PawnKind；淘汰赛禁止 role/Growth，直接 PawnKind 不可重复 |
| 擂台 Growth | 仅 limitChallenge 且必填；target 默认 count（匹配每个挑战者实际 mode=count），或 budget（匹配其 combatPower）；mode 默认 multiply，仅 multiplier>1；add 仅正 increment（人数为整数）；两参数互斥。maxValue 必填、不低于挑战者初始值，人数为 1—200 整数，CP ≤10000000；人数乘法逐档向上取整，CP 可小数。平局不增长，换魔王重置；固定混编禁止预算增长，详见 [擂台参考](arena.md) |
| 擂台 Roster / Entrants | mode=count/combatPower，人数默认 20，CP 模式 budget 必填；costMode=pawnKind 或 native；Entrant 可独立覆写 mode/costMode，并按自身模式覆写 count/budget，squad 与 pawnKind 二选一；groups 来源的人数模式由组内求和 |
| 擂台 Battle / Scoring / TieBreak | Battle 默认 180 秒/300 实时秒/fast/300 确认 ticks；评分默认 45/25/20/10，权重总和 100，时间参考 180 秒；onDraw 仅 replay，maxReplays=2（0—10） |
| 擂台 Matchmaking / Cleanup | Matchmaking 仅 knockout，可省略，mode 默认 closestCount，仅 closestCount/random；corpses、droppedItems 独立，均默认 false，只接受小写 true/false；两节点均在 Tournament 下。清理每 300 ticks 分批执行；正午重设、主地图 Pawn 清理、生命条和每秒轮播为自动行为，无额外 XML 字段，详见 [擂台参考](arena.md) |
| 擂台 Presentation / Showcase | bracketSeconds=3、settlementSeconds=3（1—60）；advanceStartSeconds=3（0—60）；pageSeconds=5，Page.seconds 继承它（1—60）；caption 默认空且最多 80 个单行字符；每页 1—5 个唯一 ref；新赛制按 boss/challenger 拆页。三赛制规则页首绘后准备，至少现实 5 秒且包含准备耗时；新赛制赛后页 5.5 秒、最终每页最多 8 项/至少 10 秒；固定行为，无新增 XML 时长属性 |
| 混编 GroupMaker / RaidRules | mode 必填 random/specific，kindDef 仅 Combat，referencePoints=5000（0,10000000]；specific.index 必填非负，fingerprint 可选小写 SHA256；strategyDef 默认 ImmediateAttack，若写 RaidRules 块则须显式给该属性 |
| 混编 Composition / Xenotypes | Composition.mode 必填 weighted/fixed，allocator 仅 native；weight>0，fixed 总人数 ≤200；Xenotypes 省略等于 inherit，出现时 mode 必填 inherit/fixed/weighted；预算上限 10000000 |
| 动作绑定 | 参数全部显式传 Arg；min/max 省略表示不加声明界限，仍须符合原生标量范围；ExpectHediff.count=1，为精确匹配数量；Result/ExpectHediff.equals 按声明类型解析，不统一按整数处理。Argument/Arg.value 按声明标量类型校验，可使用布尔、字符串和具名枚举；枚举成员及原生签名仍需游戏验证，详见动作绑定参考 |

## caseType 的完整非 UI 名称

除 ExternalScenario 外，当前允许：

`TurretPocketMap`、`RangedWeaponPocketMap`、`MeleeWeaponPocketMap`、`ApparelCombat`、`AnimalCombat`、`MechanoidCombat`、`AlienCombat`、`SkillPocketMap`、`HumanoidThreeView`、`AnimalThreeView`、`MechanoidThreeView`、`AlienThreeView`、`ApparelThreeView`、`WeaponThreeView`、`PlantGrowthView`、`BuildingView`、`TerrainPatchView`、`PawnLifeStageThreeView`、`HairThreeView`、`HeadTypeThreeView`、`BodyTypeThreeView`、`GeneThreeView`、`XenotypeThreeView`、`RecipeExecution`、`HediffEffect`、`CorpseLifecycle`、`ThingBehavior`、`LoadoutView`、`GroupCombat`。

类型名不会自动创建所需对象或动作；GroupCombat 只能走独立群战入口，UI 用例不能靠填写这些类型名替代。完整效果和覆盖判据见各模式参考。

Preparation 的作用域属性只属于擂台步骤源节点，展开时移除；不是普通 Steps 或 Select 的通用属性。DebugAction/Expect 使用与 Require 相同的条件子节点，绑定方法自身的 Result/ExpectHediff 继续保留。普通群战 PawnGroup 的 costMode 与 squad 同义，非群战禁止此字段。
