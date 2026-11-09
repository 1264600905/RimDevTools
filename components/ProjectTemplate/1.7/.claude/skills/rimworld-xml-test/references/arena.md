# 独立擂台淘汰赛

## 入口与配置边界

```powershell
.\.tools\RimDevTools.exe test run --mode arena --scenario-path "Tests/Arena/arena.autotest.xml" --test-name Example_AnimalArena
```

仅支持 1.7；必须同时给出 `--scenario-path` 和显式 `--test-name`，可重复后者选择多场 Tournament。擂台只接受稳定 `testDefName`（大小写不敏感），不接受群战的 caseId 或显示名选择方式。目录非递归读取 `*.autotest.xml`，跳过非 ArenaSuite 文件并记录 SkippedFiles；未知选择和重复 testDefName 定义仍报错；重复传入同一选择名会去重。

根为 `<ArenaSuite schemaVersion="1" id="my.arena" gameVersion="1.7">`，其下是可选 `SquadTemplates` 与一个或多个 `Tournament`。不是 TestSuite/Case；不支持 `--scenario-only`、Coverage、Success、Steps、PawnTemplate、Matrix、Case 继承、ScenePresets 或任意 AI 配置。不要将普通场景节点移植进来。Tournament 必填 `id`、`testDefName`、`Scene`、`Roster`、`Entrants`；`name` 和 `seed`（默认 12345）可选。

```xml
<Roster mode="combatPower" budget="1200" costMode="pawnKind"/>
<Entrants>
  <Entrant id="wolves" pawnKind="Wolf_Timber" name="森林狼"/>
  <Entrant id="bears" pawnKind="Bear_Polar" name="北极熊" budget="1500"/>
  <Entrant id="raiders" squad="myRaidSquad" name="混编突袭队"/>
</Entrants>
```

上述 squad 须在 Suite 的 SquadTemplates 中定义，见 [squads-and-rosters.md](squads-and-rosters.md)。完整可用起点见 [arena.autotest.xml](../assets/arena.autotest.xml)。

- `Roster mode="count" count="20"`：默认人数 20，Entrant 可单独覆盖 count；禁止任何 budget。
- `Roster mode="combatPower" budget="3000"`：总预算必填，Entrant 可单独覆盖 budget；禁止任何 count。不足一个单位或超过人数上限时失败，不夹紧预算。
- costMode 默认 pawnKind；native 计入指定异种/混编成员的战力系数。按实际加载 Def 解析，不用攻略表固定人数。
- 参赛队 2—64，id 区分大小写唯一；直接使用 pawnKind 的参赛项还要求 PawnKind 不重复，不能靠换 id 报名同一兵种。`pawnKind` 与 `squad` 必须二选一。
- 直接 pawnKind 可用 `xenotype="Yttakin"` 等，但须 Biotech、人形种族及已加载 XenotypeDef；squad 项不允许另加 xenotype，改其模板 Xenotypes。每队 1—200 人，每场只打两队。

`Scene type="mainMap"` 与群战共享严格地图解析；尺寸省略时擂台冻结为 100×100，范围 30—250。支持 generatorDef、Environment、Layout、Validation；地图生成及日照限制见 [schema-and-actors.md](schema-and-actors.md#地图与预设)。出生阵型自动沿 Z 优先填充、spacing=2，双方约在地图宽度 1/5 与 4/5，按实际人数验证边界，不提供 Actors 手工站位入口。

## 战斗、重赛与赛程

```xml
<Battle durationSeconds="180" timeoutRealSeconds="300" timeSpeed="fast" confirmationTicks="300"/>
<Matchmaking mode="closestCount"/>
<TieBreak onDraw="replay" maxReplays="2"/>
```

Battle 可省略，默认值如上；timeSpeed 为 normal/fast/superfast，durationSeconds 在 (0,86400]，timeoutRealSeconds 为 1—86400 整数，confirmationTicks 为 0—36000。固定使用原生 assaultColony 即时进攻，无 Battle.aiPolicy 或 Team.ai；与群战共用的 AI 约束和诊断见 [groupcombat.md](groupcombat.md#ai-与准备)。

`Tournament/Matchmaking.mode` 可选 `closestCount`（默认）或 `random`。人数接近模式依次选择初始总人数差最小的两队；随机模式每轮按 `Tournament.seed`、轮次和参赛 id 排序后两两配对，不依赖人数和 XML 名单排列。相同输入、种子及晋级结果可复现，改变 seed 可重新抽签。

初始奇数队的资格赛也使用所选匹配模式；之后出现奇数晋级队时，仍让最近胜利评分最低的两队加赛，不自动轮空。同分用种子和稳定身份哈希定序。正常决出冠军须有 N−1 场已决对局；重赛是同一对局的额外 attempt，每次有独立种子和地图。

一方所有登记单位无 standing 且无 pendingRevival，经过确认窗口后落败；新增、起身或待复活会重判。到模拟时限时，**比较初始成员中仍 standing 的比例**，不是群战的场上存活绝对人数；倒地和战斗新增单位不增加此比例。平局只允许 replay，默认最多重赛 2 次（总共最多 3 attempts），范围 0—10；耗尽后为 `unresolvedDraw`，不掷骰选冠军。执行、清理或评分数据失败会停止赛程，后续赛事可能 notRun。

CLI `--timeout-seconds` 限制整个进程，不随游戏暂停延长。省略时按所选赛事人数、每战 timeout、重赛上限和展示开销估算默认预算；显式填写时须覆盖完整赛程，不能直接照抄单轮群战的 900 秒。战斗计时、个人存活时钟和单战实时时限对暂停的处理沿用群战；场外展示不算战斗秒数。

## 战场清理与时间

```xml
<!-- 放在 Tournament 下；两个开关独立，省略时均为 false -->
<Cleanup corpses="true" droppedItems="true"/>
```

开启后每 5 个模拟秒（300 ticks）分批检查当前擂台。只清理死亡至少 300 ticks 的尸体；地面物品首次发现后也至少等待 300 ticks，当前任务正在操作的目标暂缓清理。不会清理角色穿戴、背包或建筑，但场景预放的地面物资也会被清理。原生拒死次数尚未耗尽、正在复活或已注册模组待复活状态的尸体保留；依赖其他尸体机制的测试应关闭尸体清理。

擂台会话开始时自动清理原主地图全部 Pawn（含殖民者、动物及容器内 Pawn），比赛期间每 250 ticks 补清，减少后台模拟；飞行落地载具随其中的 Pawn 一起移除。作用范围是本次 quicktest 的原主地图，不是参赛展示或战斗地图，也不受 Cleanup 开关控制。

每场准备时设为当地中午 12 点，开战后每经过 5000 ticks（2 个游戏小时）再次设为中午。只调整日历偏移，`TicksGame` 和战斗计时持续递增；每场结束或异常退出恢复原偏移。使用绝对日历时间的模组效果可能受影响。这些时间行为没有额外 XML 开关或可配间隔，不要添加未支持字段。

## 百分制胜利质量

```xml
<Scoring survivalWeight="45" suppressionWeight="25" exchangeWeight="20" speedWeight="10" timeReferenceSeconds="180"/>
```

省略 Scoring 即用上列默认值。四个权重各在 0—100 且**总和必须为 100**；timeReferenceSeconds 在 (0,86400]，独立于 Battle.durationSeconds。当前算法标识为 `victory-quality-v2`，仅为胜者计分：

| 分项 | 比率，乘对应权重 |
| --- | --- |
| 保全 | (初始成员仍 standing 数 + 0.25 × 初始成员仍 downed 数) / 初始人数 |
| 压制 | 1 − 对手初始成员仍 standing 数 / 对手初始人数 |
| 交换 | 胜者累计对敌伤害 / (累计对敌伤害 + 胜者累计承伤)；双方量均为 0 时取 0.5 |
| 速度 | max(0, 1 − 有效模拟秒数 / timeReferenceSeconds)；全失能结束时扣掉最后确认窗口等待 |

伤害仍受群战采集边界影响。读 Score 的 DamageQuality、NeutralExchange、IsRankable 和分项原始值；partial/complete 且 MeasurementRequirementsMet 才可排序，incomplete 不能晋级。评分用于比较胜利质量和低分加赛，不覆盖实际胜负，也不是各队跨全部对局的累计积分。

## 展示与结果核对

```xml
<Presentation bracketSeconds="3" settlementSeconds="3" advanceStartSeconds="3">
  <Showcase pageSeconds="5">
    <Page seconds="6" caption="首批参赛者">
      <Entrant ref="wolves"/><Entrant ref="bears"/>
    </Page>
  </Showcase>
</Presentation>
<Output width="1920" height="1080"/>
```

Presentation 可省略。bracketSeconds 是开战倒计时长度（1—60），settlementSeconds 是结算展示长度（1—60）；advanceStartSeconds（0—60）只控制提前多少秒准备开战截图，**不提前启动模拟**，还须等截图完成。Showcase 默认每页 5 秒，所有页时长 1—60；每个 Page 引用 1—5 个参赛 id，不得跨页重复，caption 为最多 80 字符的单行。未列参赛队按原输入顺序自动每 5 队补页。

混编卡存在多个可切换兵种时每 1 个现实秒循环切换，箭头可手动切换并重置 1 秒计时；单构成固定展示。页面时长不足时不会自动延长，可按构成数量配置 `pageSeconds`。

左右数据栏的输出、承伤排名在角色姓名后显示原生综合健康度条；兵种汇总行只有一人时也显示。倒地为橙色，死亡为空条；生命条独立于伤害条，不包含护盾。生命条和每秒轮播均为自动展示行为，无需额外 XML 字段。

赛前展示采样实际生成、出生并初始化后的角色，护甲含服装与身体覆盖；属性条按本场参赛者相对归一化并用六档品质色显示，不是绝对平衡评级。混编展示成员组成，不能只用代表 PawnKind 解释整队。最终前五按淘汰阶段、最近胜利评分等稳定排序，冠军优先；沿用参赛者卡片展示 10 秒，少于 5 队时显示实际队数。

读取 artifacts 返回的原始 `arena_<runId>_Result.json`、`_Report.md` 与 `_Screenshots`，核对冻结计划、实际 rosters、每场 attempts 的 ExecutionStatus/CleanupComplete/MeasurementQuality、Score、赛程依赖、冠军、Showcase.Completed/PreviewCleanupComplete、TopFive.Completed 和实际截图。缺失文件如实报告，不创建替代证据。JSON 中胜者/参赛者用 entrant id，各战斗内部队伍是 A/B，须按 Left/Right 映射。结果与回归、独立群战文件分开，不能把冠军展示或截图存在当作所有机制均正确。

按本次验证目标读取新增观察值：

- 冻结计划的 `MatchmakingMode` 及实际对局：按所选模式核对资格赛和常规轮次，后续奇数低分加赛仍遵循评分规则。
- `BackgroundCleanup`：核对 `RemovedPawns`、`RemovedColonists`、`RemainingPawns`，区分原主地图与战斗地图。
- 每场 attempt 的 `ArenaMaintenance`：核对清理开关、`CleanupTicks`、清理数量和剩余数量；`MinimumClearedCorpseAgeTicks`、`MinimumClearedItemGroundTicks` 在确有清理时应至少为 300，无清理时可为 null。关闭开关不等于最终地图销毁失败，`CleanupComplete` 是另一项结果。
- `NoonResetTicks`、`NoonResetHours`、`CalendarRestored` 位于 `ArenaMaintenance`：准备时 tick 0，之后每 5000 ticks 一次，小时为 12，场后恢复为 true。验证周期重设须让单场实际战斗达到 5000 ticks；开场已是中午不能证明周期逻辑执行。
- 单位 `HealthPercent` 与截图中的姓名后生命条，以及 `Showcase.CompositionSwitches` 的 Index/Seconds：混编应约每 1 个现实秒循环，和战斗模拟秒数分开判断。
- 拒死相关的 `InitialRevivalUses`、`RevivalsObserved`、`ProtectedRevivalCorpses` 位于 `ArenaMaintenance`。初始有拒死次数或赛事完成不等于实际复活已发生；未观察到复活时如实说明覆盖限制。
