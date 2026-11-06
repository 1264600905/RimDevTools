# 外部场景 XML：第一批使用说明与字段契约

后端：RimWorld 1.7。测试定义放在模组之外，不注册到 DefDatabase。本文描述已经实现的基础场景；第二至四批的类型适配、替代覆盖、手动弹窗与群战仍按实施计划交付。

## 运行

```powershell
dotnet RimDevTools/bin/Release/net10.0/RimDevTools.dll test run --mode auto --scenario-only --scenario-path "AutoTest.Core.Tests/Fixtures/Scenarios" --target-package-id ludeon.rimworld --timeout-seconds 360
```

`--scenario-path` 接受单个 XML 或目录；目录只读取当前层的 `*.autotest.xml`，按文件名排序。第一批必须同时指定 `--scenario-only`，避免误以为已支持混合、补充或替代。没有场景参数时普通测试行为不变。第一批独立执行冻结计划中的全部规则，不对外部规则套用普通随机批量筛选；与普通 `--scope`、`--defname`、`--batch-size` 混用会提前报错。

CLI 在启动前冻结文件清单、原文件 SHA-256、内容和展开后的规则。游戏读取冻结输入并重新校验，不重新读取可能被编辑的源文件。设置交接使用 Base64 包装，避免 Scribe 改写 JSON 内的转义字符。静态错误在启动前返回 `SCENARIO_INVALID`；加载后的 Def、阵营、tracker 和身体部位约束在游戏侧验证。错误保留文件、行和字段位置；原始结果中的 `scenario` 保留定义名、实例、哈希、目标列表及阶段记录。

## Suite 与身份

```xml
<TestSuite schemaVersion="1" id="my.suite" packageId="ludeon.rimworld" gameVersion="1.7">
  <Templates>
    <PawnTemplate id="human">
      <basePawnKind>SpaceRefugee</basePawnKind>
    </PawnTemplate>
  </Templates>
  <Case id="alive" seed="12345" timeoutRealSeconds="60">
    <testDefName>MyMod_PawnIsAlive</testDefName>
    <Targets><Def defType="PawnKindDef" defName="SpaceRefugee" /></Targets>
    <Scene type="pocketMap" sizeX="50" sizeZ="50" revealFog="all" />
    <Actors><Pawn id="pawn" template="human" x="20" z="20" /></Actors>
    <Steps><Require id="ready"><ActorState actor="pawn" state="alive" /></Require></Steps>
    <Success><StepSucceeded ref="ready" /></Success>
  </Case>
</TestSuite>
```

每个 Case 必须填写稳定 `testDefName`，全输入按大小写不敏感检查唯一，格式为字母/下划线开头，后续可有字母、数字、下划线、点、短横线。它与游戏目标 DefName、局部 `id`、显示 `name` 分开。第一批实例为 `testDefName/default/1`；矩阵扩展由第二批提供。同一个游戏目标可以有多个不同规则，`Targets` 可以关联多个 Def，移动文件不改变规则名和实例名。

`Requirements` 可在 Suite/Case 中填写 `Mod`、`DLC`，均以 `packageId` 指定，合并后要求实际加载。模板在同一个 Suite 内引用，`extends` 继承父模板，子模板同名字段整体覆盖，生成后再按对应 patch 修改原生状态；循环和不存在引用报错。未知字段、属性、命名空间、重复单值字段和未交付能力报错。禁止 DTD、实体、任意 `Class` 和代码入口。

## 角色字段

| 字段 | 格式与作用 |
| --- | --- |
| basePawnKind | 必填；引用已加载 PawnKindDef，保留原生初始化、随机和模组生成回调 |
| race | 可选 ThingDef；覆盖临时生成配置中的 race，不修改共享 PawnKind |
| gender | Male、Female、None；省略则继承/随机 |
| minGenerationAge、maxGenerationAge | 非负整数；两者给定时在范围中选择生物年龄，等值表示固定年龄 |
| apparelTags、weaponTags | `<li>标签</li>`；覆盖临时生成配置的标签列表 |
| apparelMoney、weaponMoney | 原生区间写法，如 `350~600` |
| forcedTraits | `<li><def>Tough</def><degree>0</degree></li>` |
| skills | `<li><skill>Shooting</skill><range>10~15</range><passion>Major</passion></li>`；指定技能使用原生生成校验排除完全禁用该技能的背景 |
| abilities | `<li>MetalbloodInjection</li>`，生成后确保拥有此 Ability |
| Apparel | inherit/patch/replace；Add(def/stuff/quality)、Remove(def)、Replace(matchDef 或 matchLayer，withDef/stuff/quality/onMissing) |
| Equipment | inherit/patch/replace；SetPrimary(def/stuff/quality) |
| Traits | inherit/patch/replace；Set(def/degree)、Remove(def) |
| Skills | inherit/patch/replace；Set(def/level/passion)，等级 0–20 |
| Hediffs | inherit/patch/replace；Ensure(def/severity/part/durationTicks)、Remove(def/part) |
| Abilities | inherit/patch/replace；Ensure(def)、Remove(def) |
| Appearance | hair、bodyType、headType；hairColor/skinColor 使用 `#RRGGBB` |

省略字段保留原生结果。`inherit` 不允许携带修改；`patch` 只处理列出的项，`replace` 先清空该 tracker 的同类内容。装备 SetPrimary 明确替换主武器；服装 Add 不隐式剥掉冲突服装，需要显式 Replace/Remove。Replace 只允许一个匹配方式；`onMissing="fail"` 为默认，`add` 允许无旧帽时直接加入。

```xml
<Apparel mode="patch">
  <Replace matchLayer="Overhead" withDef="Apparel_CowboyHat" stuff="Cloth" onMissing="add" />
</Apparel>
<Equipment mode="patch"><SetPrimary def="Gun_Autopistol" quality="Good" /></Equipment>
<Hediffs mode="patch"><Ensure def="Metalblood" severity="1" /></Hediffs>
```

Hediff 按 Def 和指定部位更新已有实例；`durationTicks` 要求原生 `HediffComp_Disappears`。部位可用唯一的 BodyPartDef 名，或从根开始的完整路径，如 `Torso/Neck[0]/Head[0]/Brain[0]`；方括号是该部位在父节点 parts 列表中的索引，应以实际种族身体树为准。歧义、缺失部位及 tracker 不支持均报具体配置错误。

## 场景对象与阵营

`Scene` 第一批仅支持基础 pocketMap，边长 30–250，默认 50；`revealFog="all"` 显式解雾，不依赖生成玩家单位。截图前切地图、定位镜头、等待绘制帧并检查目标可见性；可通过真实 `GlowPod` 建筑照明，不以调试亮度冒充自然日照。

| Actors 子节点 | 参数/引用 |
| --- | --- |
| Pawn | id、template、team、x/z，及角色字段；可 `spawn="false"` 暂不摆放 |
| PawnGroup | 上述字段，另有 count、originX/originZ、spacing、formation="line"；成员 ID 为 `group[0]` 等 |
| Building | id、def、stuff、team、x/z、quality；要求真实建筑 Def |
| Thing | id、def、stuff、x/z、count、quality；不允许绕过 Pawn/Corpse 生命周期 |
| Corpse | 使用角色字段正常生成 Pawn 后死亡；尸体为 id，内层 Pawn 为 `id.innerPawn` |
| DefRef | id、defType、defName，只保存加载后的定义引用 |
| TerrainArea | id、def、x/z、width/height，铺设本场景内的矩形地形 |

`Targets/Def` 与 `DefRef` 支持 ThingDef、PawnKindDef、AbilityDef、RecipeDef、HediffDef、TerrainDef、GeneDef、XenotypeDef、HairDef、HeadTypeDef、BodyTypeDef。第一批建立引用，不宣称已有相应生产、基因或视图专用执行适配。

`Teams/Team` 使用 id、factionDef；`player` 指现有玩家阵营，其他值定位实际存在的 FactionDef 实例。`Relation a/b/value` 核对实际阵营关系，不改变世界共享阵营。临时敌对阵营在群战批次实现。

## 步骤和条件

所有步骤有唯一 `id`。步骤开始前自动建立同名基线，场景准备后有 `prepared` 基线，`Checkpoint` 可显式命名基线。

| 步骤 | 参数/实际行为 |
| --- | --- |
| Require | 内含条件；当前不满足立即失败 |
| Checkpoint | 保存当前对象与健康状态基线 |
| Wait | 条件、timeoutRealSeconds（默认 15）、stableTicks；重复帧不增加稳定 tick |
| Screenshot | focus 可选单体 actor；通过正式截图服务输出原图 |
| CastAbility | actor、def、target、force="false"；原生合法性检查和原生 job/verb，不补次数、不清冷却 |
| Attack | actor、target、melee="true/false"；使用原生攻击行为，是否造成期望结果由后续条件判断 |
| Spawn | actor；将已准备但未摆放的对象放到场景中心；这不是创建新对象 |
| CreateThing | def、stuff、x/z、count；在步骤中真正创建一个新 Thing |
| Equip、DropEquipment | actor；Equip 另有 def/stuff，DropEquipment 正常掉落主武器 |
| AddHediff、RemoveHediff | actor、def；Add 可有 severity/part/durationTicks，Remove 可有 part |
| Destroy、Kill | actor；原生销毁/死亡 |
| Pause | 暂停模拟；用于需要暂停观察或检验实际时间超时，清理后释放暂停绕过 |

CastAbility 分开记录 `cast.accepted`、`cast.started`、`cast.completed`；Hediff/Thing 的变化另有 `effect.observed`，技能完成本身不代替成功条件。原生资源、冷却、站位或目标拒绝会失败。第一批不开放 DebugAction、任意成员或交互菜单。

条件组合为 All/Any/Not；叶子为 HasHediff(actor/def/severityMin/severityMax)、ThingExists(actor 可省略，def 可选)、ActorState(actor/state)、CorpseState(actor/state)、ActorCount(group/equals)、Equipped、Wearing、HasTrait、SkillLevel、HasAbility、StepSucceeded(ref)。引用必须在规则内存在。ActorState 支持 alive/dead/downed/standing/spawned/destroyed；CorpseState 支持 valid/spawned/destroyed。

HasHediff、ThingExists、ActorState 支持 `quantifier="any/all/count"`（默认 any，count 配 equals）。数量按目标归属计算；空的显式目标集合不能因 all/Not 而通过。ThingExists 不写 actor 时检查本场景对象及其持有物；无匹配对象可用 Not 验证不存在。

HasHediff、ThingExists 支持 `transition="added/removed"` 和 `since="步骤或基线ID"`。按 Hediff/Thing 实例身份判断，不把改变旧 Hediff 严重度、移动或掉落旧物品当成新增。观察采用 tick 加原生健康/生成/销毁事件，保留瞬时变化。

Wait/叶子可写 `observation="current/eventually"`；eventually 保留窗口内出现过的匹配，显式 since 则从该基线观察。Wait 的 `observation="always"` 必须写 durationTicks，整个窗口中任一次不满足立即失败。通常用 `Wait + stableTicks` 验证连续稳定状态。

失败分类为 configuration、prepare、probe、execution、timeout、cleanup、cancelled。失败先保留真实结果并清理，再继续下一场；取消不等同失败后继续。模拟暂停时仍按帧检查实际时间期限。主线程完全阻塞时由 CLI 的心跳/总超时处理。口袋地图卸载复用十秒镜头恢复。

## 非 UI 类型交付清单

所有下列类型共享角色、目标、场景、步骤、观察窗口、截图及 Success 契约。第一批可显式组合基础步骤验证对象；“专用适配”表示后续批次提供该类型的缺省策略和专用动作，不能用基础场景通过代替完成。

| 类型 | 适用自定义项与判据 | 专用适配批次 |
| --- | --- | --- |
| TurretPocketMap | 炮塔、敌友、距离、供能；攻击/命中/效果 | 二 |
| RangedWeaponPocketMap | 武器、射手配装/技能、目标、站位；伤害/效果 | 二 |
| MeleeWeaponPocketMap | 武器、攻击者、接敌与目标；伤害/效果 | 二 |
| ApparelCombat | 防具、材质品质、穿戴者、攻击来源；健康变化 | 二 |
| AnimalCombat / MechanoidCombat / AlienCombat | 指定 PawnKind、阵营、编组与攻击/效果 | 二 |
| SkillPocketMap | Ability、角色状态、敌友、步骤与瞬时/持续效果 | 二 |
| HumanoidThreeView / AnimalThreeView / MechanoidThreeView / AlienThreeView | 种族、年龄、姿态、朝向、镜头与实际可见性 | 三 |
| ApparelThreeView / WeaponThreeView | 精确装备、材质品质、载体、朝向与截图 | 三 |
| PlantGrowthView / BuildingView / TerrainPatchView | 生长阶段、建筑状态、地形区域与镜头 | 三 |
| PawnLifeStageThreeView | 生活阶段、年龄与外观渲染 | 三 |
| HairThreeView / HeadTypeThreeView / BodyTypeThreeView | 相应外观 Def、载体和朝向 | 三 |
| GeneThreeView / XenotypeThreeView | 基因/异种人、依赖、外观与截图 | 三 |
| RecipeExecution | 配方、工作台、材料与工作者；真实产物和消耗 | 二 |
| HediffEffect | 部位、严重度、时长；新增/移除/状态效果 | 二 |
| CorpseLifecycle | 内层 Pawn、死亡/复活条件；尸体和角色身份 | 二 |
| ThingBehavior | 创建、持有、掉落、消耗/销毁；对象身份和状态 | 二 |
| LoadoutView | 整套服装武器、姿态和三方向截图 | 三 |
| GroupCombat | 显式指名、主地图、两至三方；独立数值统计 | 四 |

最近自动测试项目上下文由 CLI 在游戏进程启动后写入游戏 Config 下的 `RimDevTools.LastAutoTestProject.json`，包含绝对根目录、package 身份、版本、runId 和开始时间；配置事务恢复不删除它。它不是日志产物，也不从输出路径推断归属。第三批据此定位项目 `.tools/ProjectData`。

所有 CLI 测试会话和游戏内自动测试期间，统一关闭已打开的调试日志窗口并阻止重新添加窗口；退出测试后恢复原自动打开设置。错误日志继续保留在正式产物中。
