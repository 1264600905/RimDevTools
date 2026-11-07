# 外部场景 XML：第二批使用说明

后端为 RimWorld 1.7。本批扩展[第一批基础契约](RimDevTools外部场景XML第一批使用说明.md)，交付 C01—C08、C24—C27 的自定义战斗、技能和功能执行。专用视图、角色暂存和手动弹窗属于第三批；独立群战属于第四批。

## 运行和选择

```powershell
# 只运行外部规则，包括本文件的参数矩阵展开实例
dotnet RimDevTools/bin/Release/net10.0/RimDevTools.dll test run --deploy --mode auto --scenario-only --scenario-path "AutoTest.Core.Tests/Fixtures/ScenarioBatch2AcceptancePositive.autotest.xml" --batch-size 100 --target-package-id ludeon.rimworld --timeout-seconds 600

# 按稳定测试定义名运行；同一定义的矩阵实例全部保留为候选
dotnet RimDevTools/bin/Release/net10.0/RimDevTools.dll test run --deploy --mode auto --scenario-only --scenario-path "AutoTest.Core.Tests/Fixtures/ScenarioBatch2AcceptancePositive.autotest.xml" --test-name Batch2_Axolotl_FloatingCloudLanding --target-package-id hentailoliteam.axolotl --timeout-seconds 600

# 混合普通 UI/战斗与自定义规则
dotnet RimDevTools/bin/Release/net10.0/RimDevTools.dll test run --deploy --mode auto --scenario-path "AutoTest.Core.Tests/Fixtures/ScenarioBatch2Joint.example.xml" --scope ui --scope rangedweapons --defname Gun_Autopistol --defname Gun_Revolver --batch-size 4 --target-package-id ludeon.rimworld --timeout-seconds 600
```

上述路径均为项目内的功能输入，不部署到模组 Defs。单个文件允许使用其他 XML 后缀，目录仅非递归读取 `*.autotest.xml`；示例 `.example.xml` 适合单独点名运行。集中正例需要原版、Biotech、Anomaly、HAR 和 Axolotl；规则自己的 Requirements 和实际 Def 检查会指出缺失依赖。负例文件故意失败，CLI 返回失败属于预期结果，不能当作生产回归全绿。

CMD 和 PowerShell 都可使用双引号路径。`--test-name` 可重复，接受稳定 testDefName 或唯一显示名，未知或重名明确报错。`--defname` 选择普通目标，应重复传入多个值；它不以逗号分隔。本批允许与 `--scope`、`--batch-size` 混用。未提供场景参数的普通测试保留原批量选择策略。

外部场景仍必填唯一 `testDefName`。`Case` 可填 `caseType`；省略为通用 `ExternalScenario`。专用类型验证实际对象与动作，例如自动炮塔、持有远程武器的射手、接敌的近战持有者、受击的穿戴者，以及相应的动物/机械族/异族。只修改类型名称不能冒充完成该类测试。

## 参数矩阵与覆盖

矩阵使用显式 Row，行 ID 稳定且不区分大小写唯一，最多 256 行。Param 替换属性和叶节点里的 `${名称}`，替换值直接写入 XML 节点，不能注入标签、代码或递归参数。未知、重复、未使用参数和替换后非法字段均报错。testDefName 不允许参数化；实例为 `testDefName/行ID/1`，无矩阵为 `testDefName/default/1`。

```xml
<Matrix>
  <Row id="autopistol"><Param name="weapon" value="Gun_Autopistol" /></Row>
  <Row id="revolver"><Param name="weapon" value="Gun_Revolver" /></Row>
</Matrix>
<Coverage mode="replace">
  <Target packageId="ludeon.rimworld" defType="ThingDef"
          defName="${weapon}" caseType="RangedWeaponPocketMap">
    <Evidence step="damage" />
  </Target>
</Coverage>
```

`Coverage` 是 Case 下与 Scene、Actors、Steps 并列的字段。`mode` 为 `supplement` 或 `replace`，省略 Coverage 也可以执行补充场景。Evidence 必须引用真实 Require/Wait 状态或效果条件，不能只引用截图、Checkpoint 或 StepSucceeded。目标还必须实际用于角色或步骤，游戏核对 Def、来源 package 和类型。

精确键为 `packageId + defType + defName + caseType + variant`，未填 package 沿用 Suite，空 variant 只匹配没有变体的普通用例。物品/服装变体采用 `PawnKindDef@BodyTypeDef`，例如 `Colonist@Male`；具体变体以普通 manifest 为准，不使用通配符。不存在的 Def、错误来源、错误类型或变体明确失败。两条 replace 争用同一完整键在启动前拒绝。

一次联合场景允许多个 Target，每个目标提供对应证据步骤。工具另外记录目标实际参与的原生执行/观察：仅配置角色拥有某项技能、只添加目标名称，都不能承接该技能的测试。整个场景、目标使用记录、清理与语义检查全部通过时，所有已声明目标才进入 `verifiedCoverageKeys`；整体失败、未执行、跳过或不支持均不能形成绿色覆盖。普通 UI 键不允许被功能场景替代。

选择和执行遵守替代优先：保留范围内全部替代，移除其承接的普通用例，再按物理 Case 补足其他范围内候选；有余量继续范围外 XML。一个联合 Case 只占一个额度，替代数量超过上限也不裁掉，并记录 `ReplacementLimitExceeded`。执行时先替代/范围内场景，再剩余普通测试，最后范围外场景。

机器结果保存完整 `scenarioSelection`、每条选择/批量原因、实际替代关系、独立场景结果与已验证键。CLI 逐项核对唯一物理实例、执行数量、覆盖证据步骤和精确替代键。`ExternalScenario` 表示只执行外部计划，`MixedScenario` 表示混合计划；Completeness 只描述本次选定计划，不表示全部普通测试均已完成。

## 固定 DebugAction

```xml
<DebugAction id="energy" actionId="axolotl.fillQi" value="1">
  <Select mode="single" actor="caster" />
</DebugAction>
<DebugAction id="prepare-enemies" actionId="native.addHediff"
             def="AlcoholHigh" severity="0.2">
  <Select mode="rectangle" minX="20" minZ="20" maxX="30" maxZ="25"
          filter="enemies" relativeTo="caster" />
</DebugAction>
```

只能使用 single 或 rectangle。single 必须解析为一个本场景拥有、已生成的有效对象；rectangle 仅选择本场景拥有、已生成且活着的 Pawn，倒地但存活的 Pawn 仍可选。框内外坐标按测试地图计算；不命中目标即失败。

筛选为 `allScenePawns`、`enemies`、`allies`。敌我筛选必须给 relativeTo；enemies 使用原生敌对关系，allies 包含同阵营和原生盟友。不会遍历主地图选择对象。动作逐目标保存 before/after/error，并核对同步执行前后源地图角色未被修改。

| actionId | 参数与条件 |
| --- | --- |
| native.fillNeeds | 无参数；目标必须拥有需要条，填至上限并核对 |
| native.fillHemogen | value 为 0—1，默认 1；需要有效 Hemogenic 基因 |
| native.setPsyfocus | value 为 0—1，默认 1；需要原生灵能链接 |
| native.addHediff | def 必填，可填 severity、part、durationTicks |
| native.removeHediff | def 必填，可填 part |
| native.refuel | 无参数；目标需要 CompRefuelable，填满并核对 |
| native.chargeBattery | 无参数；目标需要 CompPowerBattery，充满并核对 |
| native.healInjuries | 无参数；只调用原生伤势治疗，不复制角色或清空所有 Hediff |
| axolotl.fillQi | value 为 0—1，默认 1；需要兼容的 Axolotl 螈力组件和非零经脉容量 |

Axolotl 适配固定访问已核对的公共 Energy/EnergyMax 属性，不接受 XML 提供类名、字段路径或方法。未注册动作和需要互动的原生菜单不直接执行。它不会代替经脉/功法准备；用 PawnTemplate/Hediffs 写出实际前置条件。

调试写入改变被观察的 Hediff/对象身份时，记录 `debug.interference`，拒绝将旧基线之后这项变化归因于技能。准备完成后可创建新的 Checkpoint，再开始实际技能观察。直接调试后的当前状态断言仍可使用。施放不重置冷却或费用，不走 Gizmo 界面检查。

## 地图与环境

Suite 下可写 `ScenePresets`，Case 用 `Scene preset="..."` 引用。Environment 局部属性覆盖保留未填写的预设属性；Layout、Validation 块显式覆盖原块。未知预设、越界布局和错误字段在执行前失败。

```xml
<ScenePresets>
  <Scene id="open" type="pocketMap" sizeX="50" sizeZ="50"
         generatorDef="LogSync_AutoTestOutdoorPocketMap" revealFog="all">
    <Environment weather="Clear" temperature="23" roof="none" naturalSun="true" />
    <Layout>
      <TerrainRect def="Concrete" x="10" z="10" width="8" height="3" />
      <ThingRect def="Sandbags" stuff="Cloth" x="19" z="10" width="1" height="4" />
      <RoofRect def="RoofConstructed" x="23" z="10" width="3" height="3" />
    </Layout>
    <Validation><Reachable fromX="10" fromZ="10" toX="30" toZ="10" /></Validation>
  </Scene>
</ScenePresets>
```

Scene 只引用已加载的 MapGeneratorDef，要求口袋地图属性和 GenStep 依赖有效。可以使用其他模组提供的适用生成器；不导入任意 C# 生成步骤。ThingRect 要求单格的非 Pawn/Corpse 物品，可填 stuff 和 North/East/South/West rotation。多格建筑使用 Actors 中的 Building。RoofRect 的 none 表示无屋顶，省略 def 也表示移除。

场景 Case 的 seed 用于原生地图初始化和角色准备，记录实际采用值；不修改世界种子或共享生成器 Def。测试口袋地图绑定有效源地块，供原生天文和植物初始化读取。Environment 温度使用仅属于该测试地图的固定环境，清理后撤销；天气和屋顶通过原生接口设置。默认不覆盖环境。

naturalSun 验证有效地块、原生天空、太阳阴影及天气条件；不会强制 SkyGlow。时刻沿用世界正常推进，不反复重置全局 tick。可用 EnvironmentState 的 sunGlow、skyGlow 及太阳能组件实际 powerOutput 共同验证当前日照。阳光不足时应等待原生条件或失败；明亮截图不代表自然太阳机制通过。

## 新增角色字段、动作与判据

原有继承和精确覆盖继续有效。新增 `Xenotype def="..."` 使用原生异种设置；`Genes mode="patch|replace|inherit"` 内使用 Add/Remove，Add 可填 `xenogene="true|false"`。replace 清除当前基因再按配置加入；Gene 依赖、共存和激活状态按原生规则处理，技能资源检查要求其实际有效。这些字段为技能准备提供共享配置，专用基因/异种三视图在第三批交付。

| 步骤 | 主要参数与行为 |
| --- | --- |
| CastAbility | actor、def；target 与 targetX/targetZ 二选一；保留原生合法性、费用、施放阶段和冷却 |
| Attack | actor、target；melee=true 用原生近战任务；其他 Pawn 使用原生攻击；自动炮塔要求合法敌对 Pawn 和原生索敌，不把不能维持的强制墙体目标当作成功 |
| ManTurret | actor 为操作者，target 为可操作炮塔；等待原生操作关系成立 |
| BindMech | actor 为殖民机械族，target 为已配置 Mechlink 的玩家机械师；建立并核对原生监管关系 |
| ApplyDamage | actor 为受击对象，def 为 DamageDef，amount 正数；可填 armorPenetration、part、instigator；走原生 TakeDamage，适用于被动效果刺激 |
| ExecuteRecipe | actor 工作角色、recipe、worktable、output、accelerate=true；Ingredients/Ingredient 指定 actor 和 count；校验原生配方/固定筛选及精确用量，执行原生产品/机械培育或复活路径，记录消耗和输出 |
| TransferThing | actor 为地上物品，target 为携带角色；整体转移并保留对象身份 |
| DropInventory | actor 为角色，target 为其背包物品；选择可保留身份的位置原生掉落 |
| SetRot | actor 与 Fresh/Rotting/Dessicated state；修改并核对原生腐烂阶段 |
| Resurrect | actor 为 Corpse，保留同一 innerPawn，调用原生复活 |

ExecuteRecipe 是显式加速的功能执行，覆盖原生产品、资源消耗、机械培育/复活结果；不宣称验证了工人的长时间工作 AI 或操作 UI。它要求真实工作台/工作角色、精确材料和适用的机械师条件，不开放任意产品 Pawn 生成。output 是本 Case 内新对象组的引用，可用于后续条件、控制绑定与截图。

普通配方测试已将 RecipeExecution 从 RecipeBillUi 分离。Recipes 范围包含两者；Combat 纳入实际功能，UI 保留窗口/账单检查。替代功能键不能移除账单窗口或信息卡结果。原生石块特殊产物也可独立发现和执行。

普通技能的 Gizmo 检查拆为独立的 `AbilityGizmoUi`。技能范围同时保留该 UI 检查，UI 范围也可直接选择它；`SkillPocketMap` 替代只承接效果部分。UI 检查在原生 Repaint 中等待选中状态稳定，记录按钮列表及原生禁用状态，不施放技能、不启用技能强制旁路；它的通过不能代表技能效果通过。普通批量额度可能分别选择这些用例，未选中的 UI 项会记录批量原因，不能算已覆盖。

| 判据 | 用法 |
| --- | --- |
| HealthPercent | actor、min/max；Pawn 使用原生总体健康，普通物件使用 HP 比例 |
| NeedLevel | actor、NeedDef 的 def、min/max，需要条比例 |
| ResourceLevel | actor、GeneDef 的 def、min/max，实际有效资源基因的比例 |
| ModResourceLevel | actor、resource="axolotl.qi"、min/max |
| ComponentState | actor、component；fuel/battery 为 min/max 比例，power 为 true/false state，powerOutput 为实际输出 min/max，rot 为腐烂 state |
| EnvironmentState | property=weather 时填 def；temperature/sunGlow/skyGlow 填 min/max；unfogged 核对全场解雾 |
| ActorPosition | actor、x/z，radius 默认 0；要求目标仍在本测试地图实际生成 |
| ThingHeld | actor 物品、target 携带 Pawn |
| ThingQuantity | 可填 actor 或 def，min/max 比较真实堆叠总量 |
| HasController | actor 机械族、target 控制 Pawn |

第一批 All/Any/Not、Hediff 身份变化、稳定 tick、实际时间超时与清理规则继续有效。每个完成的 Require/Wait 保存当前观测；eventually 的短暂效果同时保留之前捕获的效果事件，不把最后取样时已消失误写为未发生。

## 原始产物

日志、结果、截图仍由正式工具分配，按 stdout JSON 的 result.artifacts 或 error.details.artifacts 定位。机器结果保留全部物理实例，Markdown 的 Results、Screenshot Gallery 和失败明细只列失败；重复日志合并正文、计数和关联用例。工具自身的保留策略可能清理旧产物，不另建副本或任务证据目录。
