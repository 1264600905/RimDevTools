# 场景结构、角色与环境

## 结构与身份

```xml
<TestSuite schemaVersion="1" id="my.suite" packageId="my.mod" gameVersion="1.7">
  <Requirements><Mod packageId="my.dependency"/></Requirements>
  <Templates>
    <PawnTemplate id="human"><basePawnKind>SpaceRefugee</basePawnKind></PawnTemplate>
  </Templates>
  <Case id="alive" seed="12345" timeoutRealSeconds="60">
    <testDefName>MyMod_PawnAlive</testDefName>
    <Targets><Def defType="PawnKindDef" defName="SpaceRefugee"/></Targets>
    <Scene type="pocketMap" sizeX="50" sizeZ="50" revealFog="all"/>
    <Actors><Pawn id="p" template="human" x="20" z="20"/></Actors>
    <Steps><Require id="ready"><ActorState actor="p" state="alive"/></Require></Steps>
    <Success><StepSucceeded ref="ready"/></Success>
  </Case>
</TestSuite>
```

普通 Case 必须有 `Scene`、`Actors`、`Steps`、`Success`。`caseType` 省略为 `ExternalScenario`，专用类型还核对实际对象和动作；换类型名称不能代替执行。`Targets` 只声明关联目标，Coverage 的证据另行定义。

`testDefName` 在全输入中按大小写不敏感唯一，格式为 `[A-Za-z_][A-Za-z0-9_.-]*`，不能参数化。它与局部 `id`、显示 `name`、游戏目标 DefName 分开；移动文件不改变规则身份。普通实例为 `testDefName/default/1`，矩阵实例为 `testDefName/行ID/1`。

Suite 与 Case 的 `Requirements` 合并，`Mod`/`DLC` 均用 `packageId`，要求实际加载。未知节点、属性、命名空间、重复单值字段、DTD/实体和任意 `Class` 或代码入口被拒绝。不要从设计稿猜语法；静态校验不过时根据本地工具支持修正。

CLI 在启动前冻结清单、原始 SHA-256、内容与展开规则；游戏重验冻结输入，不重读正在编辑的源文件。Def、阵营、tracker、身体树等约束仍须在加载后核对。

## 角色模板与覆盖

`Templates/PawnTemplate` 在本 Suite 中用 `id` 引用，`extends` 继承父模板。未知引用和循环失败。子模板同名字段整体覆盖父字段，然后用对应 tracker 的 patch 修改原生生成结果；不要把它误当作逐项合并父模板列表。

| 字段 | 写法 / 含义 |
| --- | --- |
| `basePawnKind` | 必填，真实 PawnKindDef；保留原生生成及模组回调 |
| `race` | 可选 ThingDef，只覆盖临时生成配置，不修改共享 PawnKind |
| `gender` | Male / Female / None；省略继承或原生随机 |
| `minGenerationAge`、`maxGenerationAge` | 非负整数，等值固定生理年龄 |
| `Age` | `biologicalYears`、`chronologicalYears`，精确年龄配置 |
| `apparelTags`、`weaponTags` | `<li>标签</li>` 列表 |
| `apparelMoney`、`weaponMoney` | 原生区间，如 `350~600` |
| `forcedTraits` | `<li><def>Tough</def><degree>0</degree></li>` |
| `skills` | `<li><skill>Shooting</skill><range>10~15</range><passion>Major</passion></li>`；生成时排除完全禁用该技能的背景 |
| `abilities` | `<li>AbilityDefName</li>`，生成后确保拥有 |
| `Appearance` | 子节点 hair / bodyType / headType / hairColor / skinColor；颜色 `#RRGGBB` |
| `Xenotype` | `def`，使用原生异种设置 |
| `Resources` | `GeneResource def/value`、`Psyfocus value`、`AxolotlQi value`，核对真实资源 |

tracker 块支持 `mode="inherit|patch|replace"`：省略未配置字段保留原生结果；inherit 不允许修改，patch 仅处理列出项，replace 先清空相应 tracker 内容。为避免歧义，修改时显式写 mode。

| tracker | 子节点 |
| --- | --- |
| `Apparel` | Add(def/stuff/quality/color)、Remove(def)、Replace(matchDef **或** matchLayer，withDef/stuff/quality/onMissing) |
| `Equipment` | SetPrimary(def/stuff/quality/color)，明确替换主武器 |
| `Traits` | Set(def/degree)、Remove(def) |
| `Skills` | Set(def/level/passion)，等级 0—20 |
| `Hediffs` | Ensure(def/severity/part/durationTicks)、Remove(def/part) |
| `Abilities` | Ensure(def)、Remove(def) |
| `Genes` | Add(def/xenogene)、Remove(def)，激活、依赖及共存按原生规则 |

服装 Add 不自动剥除冲突装备；需要 Replace/Remove。Replace 只允许一种匹配方式，`onMissing="fail"` 为默认，`add` 表示没有匹配物时直接加入。材料、品质、颜色必须为物品真实支持的属性。

```xml
<PawnTemplate id="shooter" extends="human">
  <Apparel mode="patch">
    <Replace matchLayer="Overhead" withDef="Apparel_CowboyHat" stuff="Cloth" onMissing="add"/>
  </Apparel>
  <Equipment mode="patch"><SetPrimary def="Gun_Autopistol" quality="Good"/></Equipment>
  <Skills mode="patch"><Set def="Shooting" level="14" passion="Major"/></Skills>
</PawnTemplate>
```

Hediff Ensure 按 Def 与指定部位更新已有实例；`durationTicks` 要求原生 `HediffComp_Disappears`。part 可用唯一 BodyPartDef 或从身体根开始的完整路径，如 `Torso/Neck[0]/Head[0]/Brain[0]`。索引是父节点 parts 列表中的索引，以实际种族身体树为准；不要把人类路径硬套给其他种族。歧义、缺失部位或缺少 tracker 都是配置问题。

## 场景对象与阵营

| Actors 节点 | 字段与身份 |
| --- | --- |
| Pawn | id/template/team/x/z 与角色字段；`spawn="false"` 先准备不摆放 |
| PawnGroup | 另有 count、originX/originZ、spacing、formation；成员 `group[0]` 等；群战可用 grid/columns |
| Building | id/def/stuff/team/x/z/quality，可有 color/rotation，必须真实建筑 |
| Thing | id/def/stuff/x/z/count/quality，可有 color/rotation/growth；不绕过 Pawn/Corpse 生命周期 |
| Corpse | 正常生成 Pawn 后死亡；尸体是 id，内层角色是 `id.innerPawn` |
| DefRef | id/defType/defName，保存已加载定义引用 |
| TerrainArea | id/def/x/z/width/height，矩形铺地 |

`Targets/Def` 与 DefRef 支持 ThingDef、PawnKindDef、AbilityDef、RecipeDef、HediffDef、TerrainDef、GeneDef、XenotypeDef、HairDef、HeadTypeDef、BodyTypeDef。

普通场景 `Teams/Team id/factionDef`：`player` 使用已有玩家阵营，其他值解析实际存在的阵营实例。`Relation a/b/value` 只核对原生关系，不改变共享阵营。独立群战新建临时敌对阵营，另见群战参考。

## 地图与预设

效果场景一般用 pocketMap，边长 30—250，默认 50；视图用 studio，群战用 mainMap。`revealFog="all"` 显式解雾，不依赖玩家角色生成。

```xml
<ScenePresets>
  <Scene id="open" type="pocketMap" sizeX="50" sizeZ="50"
         generatorDef="LogSync_AutoTestOutdoorPocketMap" revealFog="all">
    <Environment weather="Clear" temperature="23" roof="none" naturalSun="true"/>
    <Layout>
      <TerrainRect def="Concrete" x="10" z="10" width="8" height="3"/>
      <ThingRect def="Sandbags" stuff="Cloth" x="19" z="10" width="1" height="4"/>
      <RoofRect def="RoofConstructed" x="23" z="10" width="3" height="3"/>
    </Layout>
    <Validation><Reachable fromX="10" fromZ="10" toX="30" toZ="10"/></Validation>
  </Scene>
</ScenePresets>
```

Case 用 `<Scene preset="open"/>`。Environment 局部属性覆盖且保留其他预设属性；Layout/Validation 显式覆盖整个原块。ThingRect 只用于单格非 Pawn/Corpse 物品，多格建筑用 Building；RoofRect 的 `def="none"` 或省略 def 表示无屋顶。布局及占地必须完全在边界内。

generatorDef 仅引用已加载且适用于该地图类型的 MapGeneratorDef，不导入任意 C# GenStep。seed 用于原生地图和角色准备，不改世界种子或共享生成器。环境温度限定于测试地图，天气/屋顶走原生接口，省略则不覆盖。

`naturalSun` 不强制 SkyGlow，也不反复重置全局 tick；真实日照应结合 EnvironmentState 的 sunGlow/skyGlow 和太阳能实际 powerOutput 验证。光源照明或明亮截图不能证明太阳机制通过。
