# 战力预算与混编队伍

## 选择单位配置方式

- 精确技能、装备、Hediff 等机制对比：群战用 PawnTemplate + Pawn/PawnGroup；群战混编和三种擂台也可用下述 source=groups 组合模板，在真实 basePawnKind 上覆盖。
- 同一 PawnKind 按战力凑数：群战 PawnGroup 的 combatPowerBudget，或擂台 Roster mode=combatPower。
- 复现派系袭击兵种池、混合兵种/异种：使用 Suite 级 SquadTemplates；群战 PawnGroup 引用 squad，擂台 Entrant 引用 squad。普通 auto 回归不接受 SquadTemplates。

普通单兵群战示例：

```xml
<PawnGroup id="wolves" team="A" combatPowerBudget="1200"
           originX="18" originZ="30" formation="grid" columns="10" spacing="2">
  <basePawnKind>Wolf_Timber</basePawnKind>
</PawnGroup>
```

count 与 combatPowerBudget 互斥。普通群战 PawnGroup 也接受 costMode=pawnKind/native，默认 pawnKind。加载游戏后逐成员解析真实基础 CP 和实际异种倍率；成本相同时人数等于预算向下取整。native 继承异种时，按成员种子依次生成并核算，下一成员超出余额时停止，不跳过高成本成员重新抽取；NextMemberCost 记录停止边界。报告成员成本、平均 UnitCombatPower、ResolvedCount、ActualCombatPower、UnusedBudget；不足一人或超过 200 人直接失败，不补员或截断。native 不按武器、盔甲或技能重新估算战力。

## Suite 级 SquadTemplates

```xml
<SquadTemplates>
  <SquadTemplate id="nativeRaid" source="faction" factionDef="Pirate">
    <GroupMaker mode="random" kindDef="Combat" referencePoints="1500"/>
    <RaidRules strategyDef="ImmediateAttack"/>
    <Xenotypes mode="inherit"/>
  </SquadTemplate>
  <SquadTemplate id="mixedRaid" source="custom" factionDef="Pirate">
    <Composition mode="weighted" allocator="native">
      <Option pawnKind="Mercenary_Gunner" weight="3"/>
      <Option pawnKind="Mercenary_Heavy" weight="1"/>
    </Composition>
    <Xenotypes mode="inherit"/>
  </SquadTemplate>
</SquadTemplates>
```

每个模板都必填 id/source/factionDef；该 factionDef 用于生成规则，不是在 Teams 上引用已有派系。运行时仍创建隔离的临时阵营；Teams/Team 禁止 factionDef。同一 Team 下的多个 squad 必须使用同一生成 factionDef，不能混合不同派系来源；不同队可以不同。原生生成保留装备、背景、traits、skills 和相关模组回调。

| source | 必需配置与语义 |
| --- | --- |
| faction | 必须有 GroupMaker，禁止 Composition/Generator。kindDef 目前仅 Combat；mode=random 按合格原生 maker 权重选择，specific 必须给非负 index，可附 64 位小写 SHA256 fingerprint 锁定定义 |
| custom | 必须有 Composition，禁止 GroupMaker/Generator。weighted 用 Option(pawnKind/正 weight)，以原生选择器分配；权重不是固定人数。fixed 用 Member(pawnKind/count) 精确名单 |
| generator | 必须有 Generator，禁止 GroupMaker/Composition；通过已注册扩展 ID 获取名单，不从 XML 反射任意 CLR 类型 |
| groups | 一个或多个 PawnGroup(id/template/count)，引用单角色模板，组成固定人数队伍；禁止 GroupMaker/Composition/Xenotypes/Generator，单兵异种写在角色模板中 |

GroupMaker 的 referencePoints 默认为 5000，范围 (0,10000000]；人数模式用它评估原生编队/袭击规则，CP 模式以实际 budget 为 points。specific 的 index 依赖实际加载的 FactionDef.pawnGroupMakers；先读本轮 GroupMakers 中的 Eligible、Options、Fingerprint，再锁定，不能照抄其他模组列表下的 index。`RaidRules strategyDef` 默认 ImmediateAttack，影响编队和生成可用性，不是切换战场 AI 的配置。

固定名单写法：

```xml
<Composition mode="fixed">
  <Member pawnKind="Mercenary_Gunner" count="4"/>
  <Member pawnKind="Mercenary_Heavy" count="2"/>
</Composition>
```

同一 Composition 内 PawnKind 不重复，选项最多 200，fixed 总人数最多 200。人数模式必须指定 count=6 才与此名单一致；CP 模式须容纳整个名单，剩余预算可不花完。fixed 不自动截断、重复或补员。

## 异种与成本

Xenotypes 默认为 inherit，沿用原生兵种/派系分布。也可用 `<Xenotypes mode="fixed" def="Yttakin"/>`，或 weighted 下列 `<Option def="Baseliner" weight="1"/>` 等唯一异种项。明确配置异种须核对 Biotech、Humanlike 和 canGenerateAsCombatant；不要给动物/机械族强套异种。

costMode 默认 pawnKind，只按成员基础 combatPower；native 为基础 combatPower × 该成员实际异种的 combatPowerFactor，没有异种时倍率为 1。它是预算核算方式，不会改共享 Def 或抹掉实际基因。相同 budget 在不同成本口径下未必人数相同，报告预算时必须说明 costMode。

群战引用：

```xml
<PawnGroup id="raiders" team="B" squad="mixedRaid" combatPowerBudget="1500" costMode="native"
           originX="70" originZ="30" formation="grid" columns="10" spacing="2"/>
```

squad PawnGroup 必须且只能给 count/combatPowerBudget 之一，禁止 template、basePawnKind、Equipment、Xenotype 等单兵覆盖子节点；精确配装放在 source=groups 所引用的 PawnTemplate。SquadTemplate 内联节点由工具冻结，不是额外单兵覆盖接口。混编预算上限 10000000，解析后的名单须 1—200 人，不超预算；原生兵种池、袭击规则和出生占地仍可能拒绝配置。

擂台用 `<Entrant id="raiders" squad="mixedRaid"/>`，继承 Roster 的 count/budget/costMode，也可显式设置 mode=count/combatPower、costMode，并按自己的模式覆盖 count 或 budget。模板仅限当前 Suite，squad 项不再另设 xenotype。

## PawnGroup 引用 PawnTemplate（1.7 新增）

```xml
<Templates>
  <PawnTemplate id="gunner">
    <basePawnKind>Colonist</basePawnKind>
    <Traits mode="replace"/>
    <Skills><Set def="Shooting" level="10"/></Skills>
    <Equipment mode="replace"><SetPrimary def="Gun_Minigun" quality="Normal"/></Equipment>
  </PawnTemplate>
  <PawnTemplate id="swordsman" extends="gunner">
    <Equipment mode="replace"><SetPrimary def="MeleeWeapon_LongSword" stuff="Steel" quality="Normal"/></Equipment>
  </PawnTemplate>
</Templates>
<SquadTemplates>
  <SquadTemplate id="mixed" source="groups" factionDef="Pirate">
    <PawnGroup id="gunners" template="gunner" count="15"/>
    <PawnGroup id="swordsmen" template="swordsman" count="5"/>
  </SquadTemplate>
</SquadTemplates>
```

上述两个块可放在 TestSuite（仅群战）或 ArenaSuite 下。模板继承、file 导入与单兵覆盖使用现有群战编译器；同名字段块整体替换。内层 PawnGroup 的属性仅支持 id/template/count，可在其内使用同样的单兵覆盖字段；不得另给 team、站位、squad 或预算。不同 group 可以使用相同 PawnKind，id 在该队内唯一，队伍总人数最多 200。

群战用 `<PawnGroup id="mixedTeam" squad="mixed" count="20" team="A" .../>` 设置整队站位；三种擂台用 `<Entrant id="mixedTeam" squad="mixed" mode="count"/>`，人数从 15+5 推导，若另填 count 必须等于 20。CP 模式仅检查预算能否容纳完整名单，不按预算增减成员。每个 squad 仍只有一个生成 factionDef；模板可引用不同模组的已加载 PawnKind，尚不支持同队不同生成派系。

新来源先生成配置后的成员，再冻结公开属性/配装和成员种子；巡礼、预览、比赛及重赛按该快照重建。ConfigurationId/ConfigurationXml 保留 group 身份，同兵种的枪手与剑士在混编展示和统计中分别分组。显式配置的 Hediffs/Abilities/Resources 使用现有捕获与恢复能力；不覆盖未注册模组组件、能力冷却/充能等私有状态，不代表完整存档复制。结果按 XML 显式要求和冻结成员分别核对实际种族、性别、年龄、特性、技能、基因、外观、配装、健康、能力和公开资源；浮点值使用绝对 0.0001 容差，显式年龄使用一 tick 对应年数的容差。这些是初始属性核对，不代替战斗机制断言。

faction/custom/generator 的原生编队选择和极限增长算法保留；直接 pawnKind、普通 Pawn/PawnGroup 现在也建立逐成员冻结名单，使用 GenerationPolicy=frozen 记录实际政策。完整示例：[三种擂台](../assets/arena-template-groups.autotest.xml)、[独立群战](../assets/groupcombat-template-groups.autotest.xml)。

## 冻结、复现与扩展生成器

群战在执行前解析预算与混编名单，同 Case 同种子的换边轮复用同一名单和冻结配装；新种子可产生新名单。擂台在每届规则页首次绘制后准备本届参赛队，混编成员及其原生配装在后续比赛/重赛中复用，战斗状态每场新建。直接 pawnKind、普通 Pawn/PawnGroup 与混编均保留逐成员名单。显式 Xenotype 在原生生成请求中生效并检查战斗合法性；显式生理年龄也在生成前传入。

角色出生完成后先核对 GeneratedSnapshot；同步准备动作逐条记录 Preparation，合法换装或健康修改按动作核对，最后核对 InitialSnapshot。缺失任一阶段或必需记录均拒绝，不用准备后的实际值覆盖原冻结预期。绑定动作若改变额外公开属性，须用 DebugAction/Expect 明确后置条件；未声明字段仍须保持。

核对 ResolvedRosters 的 SquadId、FactionDef、CostMode、GroupMakerIndex/Fingerprint、Members（PawnKind/Xenotype/BaseCombatPower/XenotypeFactor/Cost/Seed/PawnTemplateXml）及真实初始单位快照；仅有代表 PawnKind 不证明整队组成。换边结果还应核对实际生成坐标和装备。配置静态编译成功不证明该兵种池在游戏中可生成。

极限挑战的人数增长循环重复冻结基准成员，保留兵种、异种和原生配装，旧档快照不变；CP 预算增长每档重新配兵，魔王保持基准阵容，换魔王时挑战者恢复初值。固定 Composition 不能用于 CP 增长，改用人数增长或加权混编。加法/乘法、上限与证据核对见 [arena.md](arena.md)。

已有扩展注册时才能用：

```xml
<SquadTemplate id="extensionRaid" source="generator" factionDef="Pirate">
  <Generator id="my.registered.generator"><Param name="profile" value="heavy"/></Generator>
</SquadTemplate>
```

扩展由模组调用 `TestSquadGenerators.Register(id, ITestSquadGenerator)`，Generate 接收上下文和参数字典，返回 PawnKind/可选 Xenotype 的成员列表；仍走统一成本、人数、原生生成及冻结名单校验。XML 不编译 C#，未知 ID 会失败。仅在用户确需新增生成能力时改代码，不为普通混编测试引入扩展。
