# 擂台测试与 combatPower 配兵实施计划

制定日期：2026-11-08。

状态：已实施并完成 RimWorld 1.7 本地部署与实机验收（2026-11-08）。独立 arena 入口、两种配兵、赛事调度、对阵图与结果校验均已支持；使用方式见 [RimDevTools 擂台测试使用说明](../RimDevTools擂台测试使用说明.md)。

实现采用 `CombatFightService` 承担共享单场执行与活动上下文，`GroupCombatService` 保留群战适配职责；赛事纯逻辑为 `ArenaScheduler`、`ArenaScore`、`ArenaEndRule`，布局为 `ArenaBracketLayout`，GUI 由 `ArenaService` 驱动。本文中的职责名称以这些实际类型为准。

核心测试 539 项、擂台 CLI 校验 25 项、群战 CLI 校验 19 项通过。实机覆盖五队资格赛、六队低分加赛、数量/预算配兵、原生分裂及复活、取消/异常/超时/未决平局、三种截图尺寸和十队天梯，以及旧群战、普通 auto 回归。64 队只验证纯逻辑与布局，未运行完整实机赛事。工具全套测试仍受既有 ToolUpdateTests 更新检查失败影响；一次超时边界启动日志出现原生 TicksAbs 初始化错误，其余正向实机未复现，未据此修改无关游戏启动流程。原始测试产物由工具管理，在聊天报告，不另建验收报告。

## 1. 目标与已确认边界

增加独立的“擂台测试”，承载 BvB 单败淘汰赛。参赛者为用户指定的 PawnKind 队伍；相同预算自动换算人数，逐场对决、胜者晋级，最终产生冠军。

| 能力 | 现有群战测试 | 新增擂台测试 |
| --- | --- | --- |
| 固定单位数量 | 保留 | 支持 |
| 按 combatPower 预算配兵 | 新增、显式启用 | 支持、默认推荐 |
| 单场战斗与伤害统计 | 保留 | 复用共同底层 |
| 参赛名单、配对、晋级、冠军 | 不引入 | 独立实现 |
| 赛前对阵树、奇数加赛、胜利评分 | 不引入 | 独立实现 |
| 入口与结果身份 | 保持 groupcombat | 使用 arena |

本计划中的“袭击点”“战力”均仅指加载后 `PawnKindDef.combatPower`。不使用殖民地财富、难度、故事叙述者、袭击生成器倍率、装备市值或实际伤害修正它。

首版面向 RimWorld 1.7。已有 1.6 命令继续工作，新增能力在未支持的游戏版本明确拒绝，不扩展为全面回迁。现有群战 XML、时间上限结算、换边实验和结果格式保持兼容。普通 auto 扫描不运行擂台或群战。

执行本计划不包含自动发布、上传创意工坊或修改被测单位的 Def。后续实施按阶段交付，真实结果在聊天中报告；不要求额外完成清单或测试报告。

## 2. 当前代码基线与改动位置

基线包含提交 `13d6eac`：倒地单位不计入存活，同名单位增加空格及递增编号；这些行为两种测试均保留。

| 当前文件或模块 | 已有能力、实施时的用途 |
| --- | --- |
| `source/AutoTestRunner/Scenarios/ScenarioLoader.cs` | 现有 XML 严格校验、固定数量和阵型边界；增加群战预算字段，保留旧重载 |
| `Scenarios/GroupCombatLoader.cs` | 双方群战、实验展开；继续只管理群战 |
| `Scenarios/GroupCombatLedger.cs`、`GroupCombatContracts.cs` | 伤害、状态、存活统计、同名编号；共享统计基础，隔离赛事评分和终止规则 |
| `Game/Scenarios/ScenarioGameBackend.cs`、`.GroupCombat.cs` | 原生 Pawn 生成、模板、环境、准备动作；在生成前应用已解析人数 |
| `Game/GroupCombatService.cs` | 当前依赖静态会话、队列和群战结算；提取单场战斗执行能力 |
| `Game/GroupCombatDamageCapture.cs`、`GroupCombatCamera.cs`、`GroupCombatOverlay.cs`、`GroupCombatScreenshot.cs` | 直接依赖 GroupCombatService；接入共享的活动战斗上下文 |
| `Game/GroupCombatLifecycleAdapters.cs`、`Integration/GroupCombatDataProbe.cs` | 召唤、复活、伤害探针；继续兼容并验证上下文互斥 |
| `AutoTestBootstrap.cs`、`Integration/ManualTestNativeApi.cs` | Tick、Frame、GUI、取消和状态查询；增加擂台调度 |
| `source/LogSyncMod/LogSyncSettings.cs`、`LogSyncMod.cs` | 会话设置、加载启动和请求保护；增加 arena 会话 |
| `source/LogSyncMod/TestSessionExitPolicy.cs`、`TestSessionCompletion.cs` | 自动退出及完成身份；支持整场赛事结束后退出 |
| `RimDevTools/Commands/TestCommand.cs`、`Infrastructure/TestHostResolver.cs`、`CliHelp.cs` | 参数、宿主、设置事务、心跳、超时、结果校验；新增 arena 分支 |
| `source/AutoTestRunner/Core/TestArtifactDirectory.cs` | 工具正式目录和独立会话分类；登记 arena，使用同一分配机制 |
| `RimDevTools/Infrastructure/GroupCombatResultValidator.cs` | 群战结果独立核对；预算信息采用可选扩展，不强制旧结果具备新字段 |

表中省略前缀的 `Scenarios/`、`Game/`、`Integration/` 文件均位于 `source/AutoTestRunner/`。实施前以 `rg` 检索所有 `GroupCombatService`、`groupcombat`、`groupCombatPlan`、`testSessionKind` 引用，不能只在 TestCommand 增加一个字符串分支。

## 3. 共用配兵契约

### 3.1 数量与预算

群战 `PawnGroup` 增加 `combatPowerBudget`：

- 显式 `count` 与 `combatPowerBudget` 互斥，同时填写即配置错误。
- 两者都未填写时保持旧规则：`count=1`。
- `count` 路径不新增 combatPower 有效性要求，避免破坏既有群战。
- 预算路径要求预算和单体 combatPower 都为有限正数。
- 数量为满足 `N × CP <= B` 的最大整数，基本公式为 `floor(B / CP)`。使用确定的数值转换与乘积复核处理浮点边界，不能用宽泛容差多生成一个超预算单位。
- 结果记录 `requestedBudget`、`unitCombatPower`、`resolvedCount`、`actualCombatPower`、`unusedBudget`。例如 B=3000、CP=85 时，N=35、实际 2975、剩余 25。
- CP>B 得到零单位，明确报错；不强行生成一个，不提高预算。
- 使用当前加载后的基础 PawnKind 战力；模板修改装备、技能、基因或 race 不重新估值。结果记录模板身份，展示战力口径，避免把它描述成装备后的精确战斗力。

预算配兵为群战可选能力，不要求群战双方使用相同预算。擂台的战力模式则整场赛事共用同一预算，各方实际投入可能因整除余数不同而略有差异。

### 3.2 解析时机和可复核性

1. CLI 固定 XML、模板依赖、选择名称、种子及内容哈希，完成结构校验。
2. 游戏加载所有模组、继承与补丁后，在主线程读取 `DefDatabase<PawnKindDef>`。
3. 开战前一次性解析该赛事全部参赛者；群战一次性解析本次计划所有预算编组。禁止在生成了一部分 Pawn 后才发现后续预算无效。
4. 形成只读 `ResolvedCombatRoster`，保存 PawnKind、战力、数量、来源和已加载模组身份；此后配对和各场生成均使用该快照。
5. 用解析后的数量重新验证每个阵型坐标、地图范围和人口限制。原有按缺省 count=1 的检查不能替代这一步。
6. CLI 校验报告中的预算算术、名册与实际生成数量；声明其战力来源为游戏解析快照，不能声称 CLI 从磁盘原始 XML 独立还原了所有补丁。

首版保留当前人口保护：单编组最多 100，单场初始总数最多 300；擂台每方一个同类编组，因此每方最多 100。超限报告所需人数及限制，不截断人数、不默默降低预算。未来提高限制应独立做性能验证。

### 3.3 群战配置示例

以下为新增语法目标，可保存为正式场景输入；本文不生成这些 XML 文件：

```xml
<TestSuite schemaVersion="1" id="budget.groupcombat" gameVersion="1.7">
  <Case id="wolves-mercenaries" kind="groupCombat" name="双方3000战力" seed="12345">
    <testDefName>Budget_WolvesVsMercenaries</testDefName>
    <Scene type="mainMap" sizeX="100" sizeZ="100"/>
    <Teams><Team id="A"/><Team id="B"/></Teams>
    <Actors>
      <PawnGroup id="wolves" team="A" combatPowerBudget="3000"
                 originX="20" originZ="30" formation="grid" columns="10" spacing="2">
        <basePawnKind>Wolf_Timber</basePawnKind>
      </PawnGroup>
      <PawnGroup id="mercenaries" team="B" combatPowerBudget="3000"
                 originX="60" originZ="30" formation="grid" columns="10" spacing="2">
        <basePawnKind>Mercenary_Gunner</basePawnKind>
      </PawnGroup>
    </Actors>
    <GroupCombat timeSpeed="fast"/>
  </Case>
</TestSuite>
```

## 4. 擂台独立入口和配置

### 4.1 CLI

```powershell
RimDevTools.exe test run --deploy --mode arena --scenario-path .tools/ProjectData/arena.autotest.xml --test-name Arena_3000CP
```

沿用现有参数的读取和项目发现机制，必须显式传入入口、场景路径及测试定义名。arena 文件夹输入使用非递归 `*.autotest.xml`，按根节点识别 ArenaSuite，跳过其他入口的文件并报告跳过数量；显式指向错误类型的文件、同一文件混合多种入口、没有匹配名称时均报明确错误。已有 auto/groupcombat 文件夹加载的接受和拒绝规则保持不变；发现 ArenaSuite 时给出使用独立 arena 入口的说明，普通回归扫描不得执行擂台。

擂台使用独立 `ArenaSuite`、`ArenaLoader`、`ArenaPlan`，不把 Tournament 塞进群战的双队 Case 队列。不改掉 `ScenarioLoader.Load(path, bool groupCombat)` 的旧调用语义；公共 XML 安全读取、模板解析等能力可提取复用。

一次命令可显式选择多个赛事，依次独立运行、分别出冠军；某赛事失败则终止当前命令并保留已完成结果，不跳过失败对局继续生成其晋级分支。

### 4.2 目标 XML

```xml
<ArenaSuite schemaVersion="1" id="arena.example" gameVersion="1.7">
  <Tournament id="main" name="3000战力擂台赛" seed="12345">
    <testDefName>Arena_3000CP</testDefName>
    <Scene type="mainMap" generatorDef="LogSync_AutoTestStudioMap"
           sizeX="100" sizeZ="100" revealFog="all">
      <Environment weather="Clear" temperature="21" roof="none" naturalSun="true"/>
    </Scene>
    <Roster mode="combatPower" budget="3000"/>
    <Entrants>
      <Entrant id="wolves" pawnKind="Wolf_Timber"/>
      <Entrant id="mercenaries" pawnKind="Mercenary_Gunner"/>
      <Entrant id="scythers" pawnKind="Mech_Scyther"/>
      <Entrant id="bears" pawnKind="Bear_Polar"/>
    </Entrants>
    <Battle durationSeconds="180" timeoutRealSeconds="300" timeSpeed="fast"
            confirmationTicks="300"/>
    <Scoring survivalWeight="45" suppressionWeight="25" exchangeWeight="20" speedWeight="10" timeReferenceSeconds="180"/>
    <TieBreak onDraw="replay" maxReplays="2"/>
    <Presentation bracketSeconds="5" settlementSeconds="3"/>
    <Output width="1920" height="1080"/>
  </Tournament>
</ArenaSuite>
```

固定数量模式替换 Roster 为 `<Roster mode="count" count="20"/>`；此时 Entrant 可以用 `count` 覆盖本方数量。战力模式禁止 Entrant 覆盖数量或预算，确保预算统一；count 模式禁止 budget。

首版参赛者 2..64 个，使用显式 PawnKind 白名单，不默认枚举全 DefDatabase。Entrant ID、赛事 testDefName 必须唯一，同一赛事重复 PawnKind 报错。两种配兵模式下的擂台都要求 combatPower 有限且大于零；因此固定数量模式也不会引入缺少有效战力的参赛者。支持可选显示名；单体细节首版继承原生 PawnKind，不额外引入擂台专属配装编辑系统。

所有参数严格检查类型、有限性和范围；沿用禁用 DTD、外部实体、任意代码执行的 XML 读取方式。上述默认值是首版实施默认，不需要在每份配置中重复填写。数值边界明确如下：

| 参数 | 首版范围 |
| --- | --- |
| seed | Int32；缺省 12345 |
| durationSeconds、timeReferenceSeconds | 有限数，(0,86400] |
| timeoutRealSeconds | 正整数，1..86400，仍受命令总 Deadline 约束 |
| confirmationTicks | 整数，0..36000 |
| survivalWeight、suppressionWeight、exchangeWeight、speedWeight | 有限数，0..100，四项合计 100，默认 45/25/20/10 |
| maxReplays | 整数，0..10，默认 2；0 表示平局直接未决停止 |
| bracketSeconds、settlementSeconds | 有限数，1..60；默认分别为 5 和 3 |
| Output 分辨率 | 成对提供；width=800..3840，height=600..2160，沿用现有范围 |

每个赛事固定一套评分权重和时间基准，不允许各对局单独修改，否则低分加赛的比较失去统一口径。

## 5. 赛事算法

### 5.1 参赛身份与去重

- 参赛者身份为冻结输入中的 Entrant ID，不以翻译名或左右队号作为身份。
- `pairKey` 为两个 Entrant ID 按 Ordinal 排序后的结构化二元组；不能简单用无转义字符串拼接，以免 ID 中分隔符产生碰撞。
- A 对 B 与 B 对 A 只产生一个逻辑 Match。左右位置依据赛事种子稳定决定，不自动换边另打一局。
- 平局重赛是同一 Match 的多个 Attempt；结果不得把重赛算成额外的淘汰场次。
- 种子、同值排序键及对局 ID 使用固定哈希算法，不使用进程随机化的 `string.GetHashCode()`。

### 5.2 选出数量最接近的一组

对当前可配对集合，计算每一对的 `abs(countA-countB)`，选择最小者，移除双方后继续选择。此处明确采用逐次最接近的贪心规则，不声称获得整轮总差值最小的最优匹配。

差值相同时，用赛事 seed 与规范化 pairKey 计算固定排序键，再用 Entrant ID 排序兜底；相同输入和种子得到相同配对。数量取初始解析数量，不取上一场剩余数量。

### 5.3 初始人数为奇数

初始参赛人数为奇数时，先从全部参赛者中选数量最接近的一对打一场资格赛，败者淘汰、胜者与其余参赛者组成偶数集合，再开始正常轮次。资格赛需要真正开战，胜者下一场重新满编；其余参赛者在图中标为“等待资格赛结果”。

例如 5 人：先 A-B，A 胜后，A/C/D/E 重新按数量配成两组。

### 5.4 普通轮次与奇数胜者加赛

1. 对本轮偶数集合生成完整的两两配对，锁定本轮赛程。
2. 串行执行本轮所有 Match，每个 Match 只产生一个晋级者。
3. 本轮执行完后，胜者只剩一个则立即成为冠军，不能因为“奇数”继续加赛。
4. 胜者为大于 1 的奇数时，选择最近一次胜利分数最低的两者打一场加赛；淘汰一者，形成偶数集合。
5. 胜者为偶数时，直接进入下一轮；重新按初始数量选择最接近的配对。

加赛选择的优先级是“分数最低”，不能再次被“数量最接近”覆盖。分数相同以固定种子排序键、Entrant ID 决定顺序。加赛胜者用本场分数更新自己的最近胜利分数，不累加、不保留历史最高分。

6 人示例：A-B、C-D、E-F 三场产生 A/C/E；若 C/E 分数最低，则 C-E 加赛；E 胜后由 A-E 决赛。不同参赛者可能打不同场数，这是已接受的赛事规则，不增加补偿积分。

### 5.5 算法不变量

- 淘汰不可逆；没有败者复活、循环赛、积分晋级或自动轮空冠军。
- 每个有胜负的逻辑 Match 恰好淘汰一个参赛者。
- N 个参赛者完整结束需要恰好 N-1 个有胜负的 Match，资格赛和加赛均包含在其中。
- 不计平局 Attempt，任何一对参赛者最多对决一次：已有负者无法再次进入集合。
- 动态赛程只能引用仍在场的参赛身份、已完成胜者和合法前驱，不能提前固定未确定的晋级者。
- 若战斗执行、清理或结果验证失败，禁止编造分数与胜者来推动后续赛程。

## 6. 单场战斗、胜负与评分

### 6.1 完整重建和原生能力

每个 Attempt 都生成双方完整初始人数、新普通主地图和临时敌对阵营；沿用原生 PawnKind 的装备、技能及能力生成规则。上一场的存档对象、伤势、装备磨损、死亡和召唤物不带入下一场。种子按赛事、规范化对阵身份及 Attempt 序号稳定派生；资格赛与加赛不影响后续不相关对局的生成种子。

召唤、分裂、复活及新增单位继续参与战斗账本和存亡判定。同名编号重置到本场范围。仅初始名册参与存活评分；新增单位不增加评分分母，也不免费提高评分分子。

### 6.2 擂台专属胜负规则

擂台不能直接复用群战按绝对存活人数比较的超时规则，否则相同预算下数量多的一方会占便宜。增加独立 ArenaEndRule：

1. 原生倒地和死亡视为失去战斗能力；离场、移除亦不再提供当前战斗能力，单独记录原因。
2. 全部已登记单位失去战斗能力，且不存在已知待复活单位，经过连续 confirmationTicks 后判负；期间新增或复活会撤销确认。
3. 对方仍具战斗能力则胜；双方都失去战斗能力则平局。与时间上限同 tick 发生时，先处理已成立的全灭判定。
4. 到模拟时间上限且没有全灭胜负时，比较双方初始名册当前站立人数 / 初始人数，比例高者胜。比例用整数交叉乘积比较；不要先舍入百分比。
5. 两个比例相同则平局，不用绝对人数、累计伤害或胜利评分私自改判。

“全部失去战斗能力”包含已确认归属的召唤物；因此初始成员全部倒下但召唤物仍作战时，战斗可以继续。此时若该方最终获胜，其初始名册存活比例可以是 0，必须如实计分。

普通群战的 GroupCombatEndRule 保持原有含义；擂台的超时比例比较、离场处理不得反向改变群战。

### 6.3 胜利分数

```text
r = (己方初始成员站立数 + 0.25 * 己方初始成员倒地存活数) / 己方初始人数
c = 1 - 敌方初始成员站立数 / 敌方初始人数
e = 己方整队对敌伤害 / (己方整队对敌伤害 + 己方整队承伤)
t = 有效战斗 tick / 60
q = max(0, 1 - t / timeReferenceSeconds)
score = survivalWeight * r + suppressionWeight * c + exchangeWeight * e + speedWeight * q
```

默认权重为 45/25/20/10，合计必须为 100，timeReferenceSeconds=180。各比例均为 0..1；倒地保全按四分之一权重，死亡、离场、移除与新增单位不计保全。双方均无伤害时交换比例取中性值 0.5。伤害按整队原生已记录伤害计算，包含新增成员输出及已记录的友伤、自伤、环境承伤；不代表准确剩余血量或完整 Hediff 归属。

例如己方初始 20 个，结算站立 15 个、倒地 3 个；敌方初始 10 个、站立 2 个；对敌伤害 300、承伤 100；有效用时 45 秒，得分为 35.4375+20+15+7.5=77.9375。不要把绝对人数或绝对伤害当作固定分值。

全灭确认获胜时扣除败方最后一次连续失能后的确认等待；超时判胜使用完整战斗 tick，不含对阵树、倒计时和展示。暂停、窗口失焦及游戏速度不会直接改变公式。分数保存算法版本、四项比例/分数、原始组成、有效时间及未格式化值，界面可显示两位小数；排序前不得按显示值舍入。CLI 独立复算各项。常规 partial 口径可参与评分；明确采集异常或测量要求失败时，分数仅暂定并停止晋级，不参与低分加赛排序。评分不改变本场胜负规则。

仅对有胜者的最后一个 Attempt 计分。平局 Attempt 不得获得晋级分数，不累加耗时，失败重赛不能提高已有参赛者的分数。

### 6.4 平局、错误与终止

默认允许最多 2 次重赛，即一个 Match 最多 3 个 Attempt。重赛重新生成双方满编队伍并使用下一个稳定种子，展示“重赛 1/2”等状态。

3 次仍平局时，当前赛事以 `unresolvedDraw` 停止，保留对阵和全部 Attempt，无冠军、CLI 非成功退出。不得通过随机抽冠军、直接比较胜利评分或无限重赛假装完成淘汰赛。通过 Battle.durationSeconds 可以增加模拟时长。

异常、游戏无响应、实际时间超时和清理失败属于执行失败，不算负者、不自动重赛；完成清理后停止赛事。正常完成必须只有一个冠军；未完成不得展示冠军。

## 7. 展示与交互

### 7.1 对阵树

每次开战及平局重赛前，先显示整个赛事的树状对阵图，默认 5 个现实秒；游戏模拟暂停。展示内容：

- 参赛者名称/头像、单位 combatPower、人数；战力模式额外显示预算与实际总战力。
- 已完成对局、胜者连线、败者状态、胜利分数、轮次与下一场高亮。
- 资格赛、奇数胜者加赛和平局重赛用明确标签区分。
- 已确定的本轮对阵显示实际参赛者，后续动态分支显示“待定”；不得绘制成已固定的标准 2 的幂次签表。
- 当前人数、剩余 Match、已完成 Match/Attempt 分开表示，冠军只在赛事完成后出现。

实现纯数据布局计算模块供测试使用。2..64 人需可读；较多参赛者采用分区/分页并保留完整赛事缩略总览，当前对局页默认打开，不能把全部文字压缩到不可读。适配 800×600、1280×720、1920×1080。

### 7.2 战斗和结算

保留现有双方榜单、存活/倒地/死亡、同名编号、头像、模拟时间和伤害统计；增加赛事名、轮次、当前对局、资格赛/加赛标识。战力与固定数量模式应有准确文字，不把固定数量赛事宣称为等战力赛事。

结算显示胜者、胜负原因、初始及当前人数、存活比例、模拟耗时、分数组成和晋级去向；默认显示 3 个现实秒，再回到更新后的对阵树。取消按钮取消整场赛事，当前战斗正常清理后终止；战斗期间允许原生暂停和调速。

工具截图必须覆盖每场赛前对阵树、初始阵容、最终结算，以及最终冠军图。屏幕尺寸与截图完成确认复用现有工具机制；截图错误必须保留真实错误，不保存空白图作为通过依据。

## 8. 运行架构与结果契约

### 8.1 分层与兼容

新增文件建议：

| 层 | 建议模块 | 职责 |
| --- | --- | --- |
| 共用纯逻辑 | `CombatRosterPlanner`、`CombatRosterContracts` | count/预算校验、人数计算、解析快照 |
| 共用游戏执行 | `CombatBattleSession`、`ActiveCombatContext` | 单场准备、Tick、Frame、结束、清理、活动账本上下文 |
| 群战适配 | 原 GroupCombatService | 保留队列、实验展开、完成信号与旧结果 |
| 擂台纯逻辑 | `ArenaContracts`、`ArenaLoader`、`ArenaPlanner`、`ArenaScoring`、`ArenaEndRule` | 输入、动态图、配对、计分、独立胜负 |
| 擂台游戏执行 | `ArenaService` | 赛事状态机、调用单场会话、失败终止 |
| 擂台展示 | `ArenaBracketLayout`、`ArenaOverlay` | 图布局与 GUI、当前对局和冠军 |
| 擂台结果 | `ArenaReport`、CLI `ArenaResultValidator` | 独立结果、赛事报告、完整性重算 |

每个名称表示职责，可按仓库风格微调，不能为了复用把所有赛事分支塞回 GroupCombatService。提取共享能力后先通过旧群战回归，再接擂台。保留已有外部生命周期适配器契约。

活动上下文同一时刻只能有一个拥有者。伤害 Hook、复活回调、镜头、截图和状态探针读取当前上下文，不得在同一次原生伤害回调中同时向群战和擂台重复记账。现有 ProbeContext 也必须参加互斥检查。

赛事状态机：`resolving → scheduling → bracket → preparing → countdown → fighting → settling → cleaning → advancing`；未结束则回 scheduling/bracket，最终到 `completed`。任意阶段取消或异常都进入清理，再到 `cancelled/error/unresolvedDraw`，清理失败不得继续下一场。

### 8.2 生命周期、超时和隔离

- 新增 `testSessionKind=arena` 与独立 `arenaPlan`，设置通过现有事务写入并在退出后恢复。
- 每个 Match 完成不触发进程退出；仅全部选中赛事终态后发送 `[LogSyncArena] COMPLETED runId=...`，完成信号只说明可读取终态，是否成功由结果校验决定。
- 接入现有心跳和无响应检测，不通过取消心跳限制放宽长赛事。组装、展示和清理阶段也需维持心跳。
- `--timeout-seconds` 显式传入时严格生效。未传时，arena 的默认 CLI 总等待上限取现有 max_wait 与赛事上界估算的较大值；groupcombat 默认不变。
- 上界按选中赛事求和：每赛事最多 `(N-1) × (1+maxReplays)` 个 Attempt，每 Attempt 预算为实际时间上限＋对阵展示＋结算展示＋30 秒准备清理余量，另加每赛事 180 秒加载/解析余量。以实际 Deadline 保护整个命令；长暂停仍可能触发实际超时，CLI 开始时报告预算。
- 每次清理验证 Pawn/尸体/世界名册、地图、Lord、临时阵营及关系、原地图、镜头、速度和屏幕恢复；赛事终态释放所有资源与静态引用。
- 运行中不热重载参赛 Def 或模板，重启赛事才重新解析。

### 8.3 结果与工具产物

擂台报告 schemaVersion=1、purpose=arena，与群战 purpose=groupcombat 分开；原始位置由 RimDevTools 的 `result.artifacts` 或 `error.details.artifacts` 返回。

在该工具目录内由正式 writer 生成 `arena_<runId>_Result.json`、`arena_<runId>_Report.md` 和工具截图。结果至少包含：

- runId、赛事 ID/testDefName、输入哈希、游戏版本、加载模组、种子、状态与冠军身份。
- 全体解析后的参赛名册、战力来源、配兵模式、预算算术和实际人数。
- 轮次、资格赛/加赛类型、图的节点及前驱、每个 Match 的 pairKey、双方、胜者、败者、最近分数变化。
- 每个 Attempt 的种子、胜负原因、初始及最终名册状态、模拟 tick、完整战斗数值、评分组成、执行/测量状态、截图和清理结果。
- 取消、未决平局、超时和异常发生位置，以及所有已经完成的对局；失败不得覆盖已有原始结果。

ArenaResultValidator 不信任单独写出的 Champion 字段，必须从冻结输入和全部对局重新验证：参赛资格、人数、最近配对优先级、初始奇数资格赛、奇数胜者最低分加赛、无重复/非法复活、Attempt 上限、合法晋级、分数、N-1 淘汰关系、最终唯一冠军及清理完成。群战校验继续独立。

不得将擂台产物写成群战结果以借用 CLI 成功分支。JSON 的 executionStatus、outcome、measurementQuality 分开；伤害归属有限不自动取消合法胜负，但必须保留测量限制。

新增 CLI 错误码至少区分：`ARENA_CONFIG_INVALID`、`COMBAT_POWER_INVALID`、`COMBAT_BUDGET_TOO_SMALL`、`COMBAT_ROSTER_LIMIT_EXCEEDED`、`ARENA_UNRESOLVED_DRAW`、`ARENA_EXECUTION_INCOMPLETE`、`ARENA_RESULT_INVALID`。只有结果已分配时才返回正式 artifacts 路径。

严格遵守根 AGENTS.md：不自建证据/任务/日期归档目录，不重定向、Tee-Object、Out-File、Set-Content 或复制保存 stdout/stderr、日志、截图和存档。只读工具原始产物，在聊天报告结论；保留与清理由工具负责。正式功能 writer 的必要结果输出与手工归档副本不同，后者禁止。

## 9. 分阶段实施任务

按依赖顺序推进。每阶段先完成代码和相关验证，再进入下一阶段；阶段提交仅在用户授权提交时执行，不混入工作区原有 README、版本号等无关修改。

### P1：共用配兵与群战预算模式

- [x] P1-01 定义配兵请求/解析快照和纯函数，冻结有效战力、向下取整及超限错误契约。
- [x] P1-02 严格校验 PawnGroup.combatPowerBudget，与显式 count 互斥，旧 XML 缺省不变。
- [x] P1-03 主线程加载后解析全部请求；生成前重查阵型及人数上限。
- [x] P1-04 群战结果及 CLI 增加可选配兵明细和算术核对。
- [x] P1-05 验证旧 20 狼对 20 雇佣枪手仍可运行，新增双方 3000CP 场景可运行。

退出条件：普通群战不要求新增字段；既有数量模式与预算模式均通过，群战终止规则未改变。

### P2：提取共享单场执行器

- [x] P2-01 以最小范围从静态 GroupCombatService 提取 CombatFightService 和活动上下文，群战继续通过适配器调用。
- [x] P2-02 迁移伤害 Hook、生命周期、镜头、HUD、截图和 probe 上下文访问，保证互斥和单次记账。
- [x] P2-03 明确注入胜负策略、展示配置及完成回调，移除共享执行器对群战完成信号的硬编码。
- [x] P2-04 通过既有伤害归属、同名编号、倒地/复活/召唤和取消清理回归。

退出条件：这一阶段尚未启用擂台时，旧群战实测和 CLI 校验全部保持可用。

### P3：赛事解析、配对与评分纯逻辑

- [x] P3-01 实现 ArenaSuite 严格解析、显式名单、两种配兵方式和独立计划身份。
- [x] P3-02 实现稳定 pairKey/seed、最近数量配对、资格赛、普通轮次、奇数胜者加赛与 N-1 不变量。
- [x] P3-03 实现独立胜负策略、评分、最近胜利分数与最多 3 次 Attempt。
- [x] P3-04 通过第 10 节的纯逻辑边界，赛事图不依赖 Unity 或游戏对象。

退出条件：给定人工对局结果可以确定推进 2、3、5、6、7、8、64 人赛事，并正确拒绝非法晋级。

### P4：擂台入口和完整运行链路

- [x] P4-01 接入 mode=arena、宿主要求、独立设置、Bootstrap、忙碌互斥、取消及状态查询。
- [x] P4-02 ArenaService 串行驱动共享单场执行器，每次重建满编队伍和干净战场。
- [x] P4-03 实现比赛/整赛事超时、心跳、完成信号和仅赛事终态退出；错误停止推进。
- [x] P4-04 实现正式 Arena writer 与 CLI validator，含失败产物和独立保留分类。
- [x] P4-05 先实测 2 人擂台，验证预算、胜负、评分、清理、唯一冠军和自动退出闭环。

退出条件：从 CLI 发起的最小赛事能完整通过，模式串用和伪造冠军均被拒绝。

### P5：树状对决与赛事呈现

- [x] P5-01 实现树布局、动态待定节点、资格赛/加赛分支和当前对局高亮。
- [x] P5-02 实现赛前展示、战斗赛事信息、结算分数、重赛标识和冠军界面。
- [x] P5-03 大名单分区和总览，验证三种分辨率；GUI 不妨碍战斗原生暂停/调速。
- [x] P5-04 通过工具原生截图确认每场赛前图、初始图、结算图和最终冠军图真实存在且内容正确。

退出条件：可从赛前图、单场结算和冠军图完整理解赛事推进，待定分支未伪装为固定对阵。

### P6：完整验收与使用说明

- [x] P6-01 更新 CLI 帮助、正式 XML 用法和源码 Fixtures；不同入口示例分开。
- [x] P6-02 执行核心测试与 CLI 专项测试；失败须判断是否与改动相关，既有无关失败如实报告。
- [x] P6-03 实测 5 人资格赛和 6 人低分加赛赛事，至少一场用固定数量、一场用统一预算。
- [x] P6-04 擂台实测后再运行旧群战，确认设置恢复、统计口径、结果和自动退出未受影响。
- [x] P6-05 直接读取正式结果与截图，在聊天报告已通过项目和剩余限制；不另建测试报告。

退出条件：第 10 节必验项目完成；正常赛事产生唯一冠军，异常赛事没有伪冠军，旧群战可继续使用。

## 10. 验收矩阵

| 编号 | 场景 | 必须满足的结果 |
| --- | --- | --- |
| R01 | B=3000，CP=85；CP=100；CP=3000 | 分别为 35/30/1，实际投入不超预算，余数正确 |
| R02 | CP=0、负数、NaN、Infinity、未解析 Def；B<CP | 开战前明确拒绝，未生成部分场景 |
| R03 | 小数战力整除边界、商略小/略大于整数、巨大预算 | 不超支、不溢出；触发限制时不截断 |
| R04 | count 与预算同时提供；旧 XML 两者未提供 | 前者失败；群战后者仍为原缺省人数 |
| R05 | 按预算解析后阵型越界/超 100 单编组 | 生成前失败，解释实际人数和限制 |
| R06 | combatPower 来自继承和模组补丁 | 采用已加载最终值，结果快照能追踪来源 |
| A01 | 2 人赛事 | 恰好 1 个有胜负 Match，唯一冠军 |
| A02 | 3、5、7 人 | 先且仅先打一场资格赛；其余按规则推进，合计 N-1 Match |
| A03 | 6 人，三位胜者分数可控 | 低分两者加赛，第三人等待；加赛胜者进决赛 |
| A04 | 8 人推进后出现普通轮次 | 每轮先锁定数量配对，整轮结束再判奇偶，无提前插赛 |
| A05 | 同数量、同分、不同 XML 排列与左右位置 | 同一规范身份和种子保持稳定；无 A-B/B-A 重复赛 |
| A06 | 加赛胜利后再参与后续比较 | 使用最近一次胜利分数，非累计分/最高分 |
| A07 | 20 人剩 15，45 秒，默认权重 | 分数 90；暂停/调速不直接改变公式 |
| A08 | 倒地、复活、召唤、离场 | 倒地不计存活；复活可撤销败北确认；新增参与战斗而不增加评分存活比例 |
| A09 | 时间上限时一方人数多但存活比例低 | 擂台按比例判胜，旧群战仍按原绝对人数规则 |
| A10 | 同归于尽、比例平局、持续不接敌 | 重赛最多两次；仍平局为 unresolvedDraw，非成功、无冠军 |
| A11 | 同一 Match 三个 Attempt | 使用稳定不同种子，满编重建；只淘汰一次，不累计评分 |
| A12 | 非法参赛者、重复 ID/PawnKind、仅一人 | 明确拒绝，不自动删除后继续赛事 |
| A13 | 伪造冠军、修改分数、跳过低分加赛、败者晋级 | ArenaResultValidator 拒绝结果 |
| A14 | 取消、异常、现实超时、清理失败 | 停止晋级，保留真实终态，恢复设置；清理失败不开始下一场 |
| A15 | 64 人纯逻辑和三种分辨率展示 | 赛程有界、图可读、当前比赛可定位；不要求首版实机跑完 64 人赛事 |
| C01 | 擂台结束后旧群战与普通 auto | 无赛事残留、错误模式启动、重复伤害记账或过早退出 |
| C02 | 比赛数较多、默认总超时估算与显式 timeout | 不受旧 600 秒默认意外截断；显式超时仍被遵守 |
| C03 | 正式 artifacts 路径与所有截图 | 仅使用工具原始位置，文件真实存在，不创建归档副本 |

纯逻辑检查放入 `AutoTest.Core.Tests`，CLI 解析与完整性校验放入 `RimDevTools.Tests`；必要时增加专项运行参数，避免无关完整工具测试阻塞本功能验证。涉及原生 Def、生命周期、截图、设置事务和自动退出的结论必须有真实游戏验证，不能仅以伪造 battle result 代替。

推荐验证命令（新入口和新专项参数需在对应阶段实现后执行）：

```powershell
dotnet build source/AutoTestRunner/AutoTestRunner.1.7.csproj -c Release
dotnet run --project AutoTest.Core.Tests/AutoTest.Core.Tests.csproj -c Release
dotnet run --project RimDevTools.Tests/RimDevTools.Tests.csproj -c Release -- --groupcombat-result
dotnet run --project RimDevTools.Tests/RimDevTools.Tests.csproj -c Release -- --arena-result
RimDevTools/bin/Release/net10.0/RimDevTools.exe test run --deploy --mode groupcombat --scenario-path AutoTest.Core.Tests/Fixtures/GroupCombatWolvesVsMercenaries.autotest.xml --test-name GroupCombat_WolvesVsMercenaries
RimDevTools/bin/Release/net10.0/RimDevTools.exe test run --deploy --mode arena --scenario-path .tools/ProjectData/arena.autotest.xml --test-name Arena_3000CP
```

## 11. 首版完成标准与后续边界

完成标准：群战和擂台均支持数量/预算配兵；独立 arena 入口可从合法名单完成资格赛、普通轮次、低分加赛和决赛；每场前展示对阵树，胜利分数可复算；正常运行有唯一冠军与完整清理，未决或执行失败诚实停止；旧群战通过兼容验收。

后续能力不纳入本轮：全面 1.6 回迁、断点续跑、双败/循环赛、混编队伍预算优化、自动扫描全模组单位、超过当前人数限制的大规模性能扩容、装备后动态战力估值、多局胜率统计赛制。新增这些能力不得悄悄改变本计划确定的首版规则。
