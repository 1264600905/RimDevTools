# 战力预算与混编队伍

## 选择单位配置方式

- 精确技能、装备、Hediff 等机制对比：群战用 PawnTemplate + Pawn/PawnGroup，在真实 basePawnKind 上覆盖。
- 同一 PawnKind 按战力凑数：群战 PawnGroup 的 combatPowerBudget，或擂台 Roster mode=combatPower。
- 复现派系袭击兵种池、混合兵种/异种：使用 Suite 级 SquadTemplates；群战 PawnGroup 引用 squad，擂台 Entrant 引用 squad。普通 auto 回归不接受 SquadTemplates。

普通单兵群战示例：

```xml
<PawnGroup id="wolves" team="A" combatPowerBudget="1200"
           originX="18" originZ="30" formation="grid" columns="10" spacing="2">
  <basePawnKind>Wolf_Timber</basePawnKind>
</PawnGroup>
```

count 与 combatPowerBudget 互斥。加载游戏后用真实 `PawnKindDef.combatPower` 做十进制预算解析，人数向下取整，报告 UnitCombatPower、ResolvedCount、ActualCombatPower、UnusedBudget；不足一人或超过 200 人直接失败，不补到一人或截成 200。普通群战 PawnGroup 不支持 costMode（只有 squad 支持），所以即使覆写 Xenotype，它的预算仍按基础 PawnKind CP；需要计入异种倍率时选择混编或擂台相应入口。

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

squad PawnGroup 必须且只能给 count/combatPowerBudget 之一，禁止 template、basePawnKind、Equipment、Xenotype 等单兵覆盖子节点；配置放到模板，精确配装则用普通 PawnTemplate。SquadTemplate 内联节点由工具冻结，不是额外单兵覆盖接口。混编预算上限 10000000，解析后的名单须 1—200 人，不超预算；原生兵种池、袭击规则和出生占地仍可能拒绝配置。

擂台用 `<Entrant id="raiders" squad="mixedRaid"/>`，继承 Roster 的 count/budget/costMode，可按对应模式单独覆盖 count 或 budget。模板仅限当前 Suite，squad 项不再另设 xenotype。

## 冻结、复现与扩展生成器

群战在执行前解析预算与混编名单，同 Case 同种子的换边轮复用同一名单和冻结配装；新种子可产生新名单。擂台在每届规则页首次绘制后准备本届参赛队，混编成员及其原生配装在后续比赛/重赛中复用，战斗状态每场新建。普通 pawnKind 队没有完整混编成员快照，不应把种子变化后的原生随机配装解释为固定名单复用。

核对 ResolvedRosters 的 SquadId、FactionDef、CostMode、GroupMakerIndex/Fingerprint、Members（PawnKind/Xenotype/BaseCombatPower/XenotypeFactor/Cost/Seed/PawnTemplateXml）及真实初始单位快照；仅有代表 PawnKind 不证明整队组成。换边结果还应核对实际生成坐标和装备。配置静态编译成功不证明该兵种池在游戏中可生成。

极限挑战的人数增长循环重复冻结基准成员，保留兵种、异种和原生配装，旧档快照不变；CP 预算增长每档重新配兵，魔王保持基准阵容，换魔王时挑战者恢复初值。固定 Composition 不能用于 CP 增长，改用人数增长或加权混编。加法/乘法、上限与证据核对见 [arena.md](arena.md)。

已有扩展注册时才能用：

```xml
<SquadTemplate id="extensionRaid" source="generator" factionDef="Pirate">
  <Generator id="my.registered.generator"><Param name="profile" value="heavy"/></Generator>
</SquadTemplate>
```

扩展由模组调用 `TestSquadGenerators.Register(id, ITestSquadGenerator)`，Generate 接收上下文和参数字典，返回 PawnKind/可选 Xenotype 的成员列表；仍走统一成本、人数、原生生成及冻结名单校验。XML 不编译 C#，未知 ID 会失败。仅在用户确需新增生成能力时改代码，不为普通混编测试引入扩展。
