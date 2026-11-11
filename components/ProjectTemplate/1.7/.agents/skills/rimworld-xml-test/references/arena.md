# 独立擂台赛事：淘汰、魔王轮战与极限挑战

## 入口与配置边界

```powershell
.\.tools\RimDevTools.exe test run --mode arena --scenario-path "Tests/Arena/arena.autotest.xml" --test-name Example_AnimalArena
```

仅支持 1.7；必须同时给出 `--scenario-path` 和显式 `--test-name`，可重复后者选择多场 Tournament。擂台只接受稳定 `testDefName`（大小写不敏感），不接受群战的 caseId 或显示名选择方式。目录非递归读取 `*.autotest.xml`，跳过非 ArenaSuite 文件并记录 SkippedFiles；未知选择和重复 testDefName 定义仍报错；重复传入同一选择名会去重。

根为 `<ArenaSuite schemaVersion="1" id="my.arena" gameVersion="1.7">`，其下是一个或多个 `Tournament`，以及可选 `Templates`、`SquadTemplates`、`Requirements`、`ScenePresets`、`ActionBindings`。Templates 中的 PawnTemplate 支持 extends/file，供 source=groups 的 SquadTemplate 复用，见 [squads-and-rosters.md](squads-and-rosters.md#pawngroup-引用-pawntemplate17-新增)。不是 TestSuite/Case；不支持 `--scenario-only`、Coverage、Success、任意 Steps、Matrix 或 Case 继承/补丁。Tournament 必填 `id`、`testDefName`、`Scene`、`Roster`、`Entrants`；`name`、`seed`（默认 12345）和 `format` 可选。

| `Tournament.format` | 赛程与最终结果 |
| --- | --- |
| `knockout`（默认） | 原单败淘汰赛，决出冠军并展示前五名 |
| `bossRoundRobin` | 每个挑战者依名单顺序挑战每个魔王一次，最终展示各魔王胜场与平均胜利评分 |
| `limitChallenge` | 每个挑战者逐一挑战各魔王，战败后增加人数或 CP 预算，直到获胜或达到限制 |

三种赛制共用地图、原生战斗、评分、清理和展示基础设施；新赛制使用独立的配对与成绩页，不生成淘汰冠军。完整起点：[淘汰赛](../assets/arena.autotest.xml)、[魔王轮战与四种增长方式](../assets/arena-challenges.autotest.xml)。

```xml
<Roster mode="combatPower" budget="1200" costMode="pawnKind"/>
<Entrants>
  <Entrant id="wolves" pawnKind="Wolf_Timber" name="森林狼" ai="lure"/>
  <Entrant id="bears" pawnKind="Bear_Polar" name="北极熊" budget="1500"/>
  <Entrant id="raiders" squad="myRaidSquad" name="混编突袭队" ai="defendPoint"/>
</Entrants>
<Battle aiPolicy="assaultColony"/>
```

上述 squad 须在 Suite 的 SquadTemplates 中定义，见 [squads-and-rosters.md](squads-and-rosters.md)。完整可用起点见 [arena.autotest.xml](../assets/arena.autotest.xml)。

- `Roster mode="count" count="20"`：默认人数 20，继承此模式的 Entrant 可单独覆盖 count，禁止 budget。
- `Roster mode="combatPower" budget="3000"`：默认预算必填，继承此模式的 Entrant 可单独覆盖 budget，禁止 count。不足一个单位或超过人数上限时失败，不夹紧预算。
- 新增 Entrant.mode=count/combatPower 和 costMode 独立覆盖：固定 20 人魔王可用 `<Entrant id="boss" squad="knights" mode="count" role="boss"/>`，挑战者继续继承 5000 CP。从 count 默认切换到 combatPower 时须显式给 budget；source=groups 的 count 自动取组内人数之和。未设置这些新属性时保持原有继承行为。
- costMode 默认 pawnKind；native 计入指定异种/混编成员的战力系数。按实际加载 Def 解析，不用攻略表固定人数。
- 参赛队 2—64，id 区分大小写唯一；`pawnKind` 与 `squad` 必须二选一。三种赛制均允许相同 PawnKind 以不同 id、人数或异种配置参赛；参赛 id、配置来源与实际配装用于身份，显示名不参与去重。
- 直接 pawnKind 可用 `xenotype="Yttakin"` 等，但须 Biotech、人形种族及已加载 XenotypeDef；squad 项不允许另加 xenotype，改其模板 Xenotypes。每队 1—200 人，每场只打两队。

`Scene type="mainMap"` 与群战共享严格地图解析；可通过 preset 引用 Suite 的 ScenePresets，再用本场字段覆盖。尺寸省略且预设未给尺寸时擂台冻结为 100×100，范围 30—250。支持 generatorDef、Environment、Layout、Validation；地图生成及日照限制见 [schema-and-actors.md](schema-and-actors.md#地图与预设)。

### 地图、AI 与出生阵型

群战和擂台可在 `Scene` 上选宿主内置的固定战场样式。样式通过 seed 复现地形和掩体；它不替换 MapGeneratorDef，不接受自定义样式名。

| `battlefield` | 固定场景 |
| --- | --- |
| `open` | 开阔平地，无额外障碍，适合基础战力对比 |
| `cover` | 交错沙袋和射击通道 |
| `mountainRidges` | 花岗岩 / 板岩山脊与山口 |
| `mountainValley` | 砂岩峡谷、支脉隘口和中央宽谷 |
| `glacier` | 冰面、积雪与岩岛 |
| `urban` | 废墟街区、建筑、道路和巷道 |

```xml
<Scene type="mainMap" sizeX="140" sizeZ="140" battlefield="urban"
       featureDensity="1" spawnClearance="6" revealFog="all">
  <Environment weather="Clear" temperature="21" roof="none" naturalSun="true"/>
</Scene>
```

`featureDensity` 默认 1、范围 0.5—1.5；`spawnClearance` 默认 6、范围 2—12。两者只有与 `battlefield` 同时使用才有效；样式会补入默认 Environment，显式天气、温度、屋顶和日照仍可覆盖。省略 `battlefield` 使用所选地图生成器生成的场景；默认测试生成器是隔离用平地，不能因此推断会有自然山脉或废墟。普通回归模式不接受 `battlefield`。

`Battle.aiPolicy` 是赛制默认 AI，默认 `assaultColony`。每个 `Entrant` 可用可选 `ai` 独立覆盖；省略 `ai` 就跟随默认，队伍身份在淘汰赛换边及挑战赛每场重建时保持绑定。示例中森林狼使用 `lure`，北极熊继承 `assaultColony`，混编突袭队使用 `defendPoint`：

```xml
<Battle aiPolicy="assaultColony"/>
<Entrants>
  <Entrant id="wolves" pawnKind="Wolf_Timber" ai="lure"/>
  <Entrant id="bears" pawnKind="Bear_Polar"/>
</Entrants>
```

`ai` 可选 `battle`、`seekAndAttack`、`assaultThings`、`assaultColony`、`defendPoint`、`lure`、`native`。battle/seekAndAttack/native 是 assaultColony 原生即时进攻控制器的兼容别名；assaultThings 使用原生攻击目标行为；defendPoint 和 lure 使用共享群战战术控制器。Arena 当前不提供逐队 defendX/defendZ/defendRadius 属性，defendPoint 以该队本场自动出生位置为锚点。AI 的具体行为、限制及群战 Team 防御坐标见 [groupcombat.md](groupcombat.md#ai-与准备)。

Arena 不接受 Actors 手工站位或 PawnGroup formation。每场按双方实际人数自动布置宽浅阵型：spacing 固定为 2，先沿 Z 填充再沿 X 展开，尽量让两侧宽度接近并留中央接敌通道；默认双方约在地图宽度的 1/5 与 4/5。队伍扩张或预算配兵后按实际占地重新校验边界。群战手写 `line/grid`、`columns`、`spacing` 的规则见 [groupcombat.md](groupcombat.md#入口与结构)。

Suite 与 Tournament 均可声明 Requirements（同群战的 Mod/DLC packageId），每场交由共享加载器核对。Tournament 可配置 Metrics 和 Overlay，写法是群战 GroupCombat 内的同名节点，擂台中直接放在 Tournament 下；例如 `<Metrics detailLimit="20000" requireCompleteAttribution="true"/>`。显式要求完整归属但未达标时最终 CLI 校验失败，仍保留已执行赛事和测量诊断；未配置该要求时保持评分回退行为。当前原生采集后端仍为 partial，开放该字段不表示已提供 complete 测量。完整起点见 [arena-template-groups.autotest.xml](../assets/arena-template-groups.autotest.xml)。

## 魔王轮战与极限挑战配置

两种新赛制的每个 Entrant 都必须填写 `role="boss"` 或 `role="challenger"`，且两种角色各至少一队；淘汰赛不接受 role 或 Growth。新赛制禁止 Matchmaking。配对顺序固定为外层挑战者、内层魔王，均按 XML 名单顺序；同角色之间不交战。每场重建满编阵容，魔王不继承上场伤势。

```xml
<!-- 放在 Tournament format="limitChallenge" 内；仍须 testDefName、Scene 等必填节点 -->
<Roster mode="count" count="2"/>
<Entrants>
  <Entrant id="boss" pawnKind="Wolf_Timber" role="boss" count="4" name="群狼魔王"/>
  <Entrant id="rats" pawnKind="Rat" role="challenger" name="鼠群挑战者"/>
</Entrants>
<Growth target="count" mode="add" increment="2" maxValue="8"/>
```

`bossRoundRobin` 不填写 Growth，不淘汰队伍；每组安排一次对决（平局可重赛）。最终按魔王分别统计 Wins、Losses、Unresolved；平均分只统计魔王胜局的未舍入评分总和 / 胜场，零胜显示“—”，不将败局当作 0 分参与平均。

`limitChallenge` 必须有 Growth，优先使用人数增长；支持以下四种写法，每次只选一种：

| 增长方式 | Growth 示例 | 从初始值开始的档位示例 |
| --- | --- | --- |
| 人数乘法 | `<Growth target="count" mode="multiply" multiplier="1.5" maxValue="100"/>` | 10 → 15 → 23 → 35，逐次向上取整 |
| 人数加法 | `<Growth target="count" mode="add" increment="2" maxValue="8"/>` | 2 → 4 → 6 → 8 |
| CP 预算乘法 | `<Growth target="budget" mode="multiply" multiplier="2" maxValue="1600"/>` | 100 → 200 → 400 → 800 → 1600 |
| CP 预算加法 | `<Growth target="budget" mode="add" increment="150" maxValue="400"/>` | 100 → 250 → 400 |

- target 默认 count，mode 默认 multiply，兼容旧的仅 multiplier 写法。count 必须匹配每个挑战者的实际 mode=count；budget 必须匹配其 combatPower。魔王可独立选模式。
- multiply 只填 multiplier，且大于 1；add 只填正 increment，人数增量必须为整数，CP 允许小数。maxValue 必填且不小于每个挑战者初始值；人数上限为 1—200 整数，CP 上限为 10000000。
- 只有挑战者战败才增长；平局在当前档位重赛，不增长。获胜立即结束该组；换魔王恢复挑战者初始值，没有额外的战败次数上限。
- 等于上限仍可打，下一档超过上限才结束为 `limitReached`，不把超限值夹紧到上限。数值不再增长、溢出或无法形成可执行总时限的配置会被拒绝或按上限结束，须读取诊断。
- 人数增长依次重复冻结基准成员，保留兵种、异种和原生配装，各基准成员副本数最多相差一份，历史快照不变。CP 增长每档重新按预算配兵，可能改变人数或组成；固定成员混编不支持预算增长，改用人数增长或加权混编。预算增长不会修改角色伤害或生命值。
- 合法初始阵容之后，下一档触及人数/阵型技术限制可结束为 limitReached，并记录 LimitReason、被拒绝的 LimitRoster。初始阵容非法、生成错误、执行/清理失败或现实超时仍是错误，不能记作战败或正常触顶。

极限成绩展示每组状态、首次获胜人数/预算、档数、战斗次数、实际人数/总 CP 和限制原因。“首次获胜值”仅是配置档位序列中的首次胜利，不代表理论最小门槛。

## 战斗、重赛与淘汰赛程

```xml
<Battle durationSeconds="180" timeoutRealSeconds="300" timeSpeed="fast" confirmationTicks="300"/>
<Matchmaking mode="closestCount"/>
<TieBreak onDraw="replay" maxReplays="2"/>
```

Battle 可省略，默认值如上；timeSpeed 为 normal/fast/superfast，durationSeconds 在 (0,86400]，timeoutRealSeconds 为 1—86400 整数，confirmationTicks 为 0—36000。Battle.aiPolicy 提供赛制默认 AI，Entrant.ai 可独立覆盖，具体取值、控制器映射和限制见“地图、AI 与出生阵型”及 [groupcombat.md](groupcombat.md#ai-与准备)。

仅淘汰赛的 `Tournament/Matchmaking.mode` 可选 `closestCount`（默认）或 `random`。人数接近模式依次选择初始总人数差最小的两队；随机模式每轮按 `Tournament.seed`、轮次和参赛 id 排序后两两配对，不依赖人数和 XML 名单排列。相同输入、种子及晋级结果可复现，改变 seed 可重新抽签。

初始奇数队的资格赛也使用所选匹配模式；之后出现奇数晋级队时，仍让最近胜利评分最低的两队加赛，不自动轮空。同分用种子和稳定身份哈希定序。正常决出冠军须有 N−1 场已决对局；重赛是同一对局的额外 attempt，每次有独立种子和地图。

一方所有登记单位无 standing 且无 pendingRevival，经过确认窗口后落败；新增、起身或待复活会重判。到模拟时限时，**比较初始成员中仍 standing 的比例**，不是群战的场上存活绝对人数；倒地和战斗新增单位不增加此比例。平局只允许 replay，默认最多重赛 2 次（总共最多 3 attempts），范围 0—10；耗尽后为 `unresolvedDraw`。淘汰赛停止整届，不掷骰选冠军；新赛制只结束当前组，继续其他组。执行或清理失败停止赛程，后续赛事可能 notRun；伤害测量不足的评分回退见下文。

CLI `--timeout-seconds` 限制整个进程，不随游戏暂停延长。省略时按所选赛事的全部配对、潜在增长档位、每战 timeout、重赛上限、规则页和展示分页开销估算默认预算；显式填写时须覆盖完整赛程，不能直接照抄单轮群战的 900 秒。战斗计时、个人存活时钟和单战实时时限对暂停的处理沿用群战；场外展示不算战斗秒数。

## 战场清理与时间

默认空白测试场地（地图父对象与生成器均为 `LogSync_AutoTestStudioMap`）隔离世界地块变体、生态区附加生成步骤与后置初始化，避免铺平后再次生成废墟；不修改原世界地块，也不影响其他或自定义地图。生成角色前要求残留物和非土壤格数均为 0，否则明确报错。不要用隔离行为推断自定义 generatorDef 或 Layout 也会被强制清空。

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

伤害仍受群战采集边界影响。读 Score 的 DamageQuality、NeutralExchange、UsedDamageFallback、IsRankable 和分项原始值；测量不满足要求时交换项回退到中性值 0.5，保留测量诊断，仍按实际胜者晋级或推进挑战。不要将测量不足等同执行失败。评分用于比较胜利质量和淘汰赛低分加赛，不覆盖实际胜负，也不是各队跨全部对局的累计积分。

## 展示与结果核对

三种赛制均为：**规则页 → 参赛者巡礼 → 单场预览 → 战斗 → 赛后页 → 最终成绩**。规则文案面向观众自动生成，不需要在 XML 手写内部操作说明。规则页首个实际绘制帧之后才准备本届名册、装备和预览角色，以单调现实时间计满至少 5 秒；准备时间（含 300 原生初始化 ticks）计入其中。准备不足 5 秒则补足，超过 5 秒则准备完成直接进入巡礼，不额外再等 5 秒。没有规则页时长 XML 字段，不要自行添加。

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

新赛制巡礼的魔王和挑战者分别分页；手动混合 Page 自动按角色拆分并保留 caption、seconds，未列项也按角色补页，每页最多 5 队。单场预览仍同时展示双方，暂停模拟 5 秒，肖像/属性沿用基准快照，人数与 CP 使用本档实际值。

新赛制赛后页固定展示 5.5 秒，包含双方大肖像或 icon、胜者金色高亮、胜利评分、本档/下档安排，以及每页 4 条的图标交锋记录。最终成绩用带肖像的双列卡片，每页最多 8 项，自动逐页显示至少 10 秒，模拟不推进；轮战列全部魔王，极限列全部挑战组。单张尾卡居中、长名适配、混编肖像可轮播，紧凑卡悬停可看实际人数、CP、比例与限制原因。这些固定页面时长与页容量无额外 XML 字段。

混编卡存在多个可切换兵种时每 1 个现实秒循环切换，箭头可手动切换并重置 1 秒计时；单构成固定展示。页面时长不足时不会自动延长，可按构成数量配置 `pageSeconds`。

左右数据栏的输出、承伤排名在角色姓名后显示原生综合健康度条；兵种汇总行只有一人时也显示。倒地为橙色，死亡为空条；生命条独立于伤害条，不包含护盾。生命条和每秒轮播均为自动展示行为，无需额外 XML 字段。

赛前展示采样实际生成、出生并初始化后的角色，护甲含服装与身体覆盖；属性条按本场参赛者相对归一化并用六档品质色显示，不是绝对平衡评级。混编展示成员组成，不能只用代表 PawnKind 解释整队。仅淘汰赛的最终前五按淘汰阶段、最近胜利评分等稳定排序，冠军优先；沿用参赛者卡片展示 10 秒，少于 5 队时显示实际队数。

读取 artifacts 返回的原始 `arena_<runId>_Result.json`、`_Report.md` 与 `_Screenshots`，核对冻结计划、实际 rosters、每场 attempts 的 ExecutionStatus/CleanupComplete/MeasurementQuality、Score、赛程依赖和实际截图。淘汰赛另核对 N−1 已决对局、唯一 Champion、TopFive.Completed；新赛制核对 Challenges、Summary，不套用冠军或 N−1 条件。缺失文件如实报告，不创建替代证据。JSON 中胜者/参赛者用 entrant id，各战斗内部队伍是 A/B，须按 Left/Right 映射。结果与回归、独立群战文件分开，不能把展示完成或截图存在当作所有机制均正确。

完整正常赛程写完结果后正常退出游戏并恢复 CLI 配置；新赛制含未决组但完成其余组也可正常结束。淘汰赛平局耗尽、运行错误、超时或取消不自动关游戏；读取 CLI 的 gameStillRunning、gameProcessId、transactionRetained，游戏关闭后再按需 test cleanup。现实超时不是战败。

按本次验证目标读取新增观察值：

- `Rules`：MinimumSeconds=5、Completed=true、VisibleSeconds ≥ max(5, LoadingSeconds)，截图为工具原始 `_rules.png`。同时核对 Showcase.Completed、PreviewCleanupComplete、各页 EntrantIds/实际时长/截图；新赛制每页不能混合两种角色。
- `Challenges`：配对完整且顺序正确；每个档位对局的 PairId、从 0 起的 Level、ChallengeValue、ResolvedRosters、Attempts，平局不增长、跨魔王恢复初值。组状态为 won、lost（轮战）、limitReached 或 unresolvedDraw；触顶须核对 LimitReason/LimitRoster，而非把执行错误吞作正常成绩。
- `Summary.Bosses`：独立复算轮战胜/败/未决与平均胜利评分；`Summary.Completed` 与 `Summary.Pages` 的 EntryIds、VisibleSeconds ≥ 10、SimulationTicks=0、原始 Screenshot。极限成绩核对首次获胜值和实际名册，不将它解释为最低门槛。
- 单场 `PreBattles` 与 `PostBattleScreenshots`，以及 attempt 的 bracket/initial/final 三张原始战斗截图。`_bracket.png` 是历史文件名，现为赛前阵容预览，不证明新赛制生成淘汰天梯。
- attempt 的 `StudioIsolation`：OriginalTileMutators、SuppressedGenSteps、WorldTileUnchanged=true、RemainingThingsBeforeActors=0、NonSoilCellsBeforeActors=0；隔离证据与原主地图后台清理分别核对。
- 仅淘汰赛核对冻结计划的 `MatchmakingMode` 及实际对局：按所选模式核对资格赛和常规轮次，后续奇数低分加赛仍遵循评分规则。
- `BackgroundCleanup`：核对 `RemovedPawns`、`RemovedColonists`、`RemainingPawns`，区分原主地图与战斗地图。
- 每场 attempt 的 `ArenaMaintenance`：核对清理开关、`CleanupTicks`、清理数量和剩余数量；`MinimumClearedCorpseAgeTicks`、`MinimumClearedItemGroundTicks` 在确有清理时应至少为 300，无清理时可为 null。关闭开关不等于最终地图销毁失败，`CleanupComplete` 是另一项结果。
- `NoonResetTicks`、`NoonResetHours`、`CalendarRestored` 位于 `ArenaMaintenance`：准备时 tick 0，之后每 5000 ticks 一次，小时为 12，场后恢复为 true。验证周期重设须让单场实际战斗达到 5000 ticks；开场已是中午不能证明周期逻辑执行。
- 单位 `HealthPercent` 与截图中的姓名后生命条，以及 `Showcase.CompositionSwitches` 的 Index/Seconds：混编应约每 1 个现实秒循环，和战斗模拟秒数分开判断。
- 拒死相关的 `InitialRevivalUses`、`RevivalsObserved`、`ProtectedRevivalCorpses` 位于 `ArenaMaintenance`。初始有拒死次数或赛事完成不等于实际复活已发生；未观察到复活时如实说明覆盖限制。

## 同步准备与属性验收

Tournament/Preparation 支持 Require、DebugAction、Equip、DropEquipment、AddHediff、RemoveHediff、ApplyDamage。每步保持唯一 id，并可设置 entrant="参赛者ID"、role="boss|challenger"、group="配置组ID" 过滤目标；多个选择条件取交集，未知或无匹配的参赛者/配置组静态拒绝。group 只引用 source=groups 的内部 PawnGroup，不是 PawnKind。淘汰赛使用 entrant/group，role 仅用于有角色的赛制。

步骤内 actor/target/group 引用使用 entrant 占位符；DebugAction 的 Select 仍是 mode=single actor=entrant。编译后按选中成员展开，不作用于对方或未选中的配置组；预览与比赛使用相同选择。

```xml
<Preparation>
  <Equip id="equip" role="boss" group="gunners" actor="entrant" def="Gun_SniperRifle"/>
  <Require id="armed" role="boss" group="gunners">
    <Equipped actor="entrant" def="Gun_SniperRifle"/>
    <SkillLevel actor="entrant" def="Shooting" min="10" max="10"/>
  </Require>
</Preparation>
```

Require 使用现有同步条件求值；DebugAction 可带 Expect 条件作为后置断言。原生成员先通过 XML 与冻结名单核对，再执行准备动作，并保留准备前、逐步、开战三个阶段的快照。结果不能通过删除名册、角色快照、规则页、赛前/赛后展示、场地隔离或环境维护记录来跳过校验。私有组件、冷却与充能仍不属于公开属性复制契约。
