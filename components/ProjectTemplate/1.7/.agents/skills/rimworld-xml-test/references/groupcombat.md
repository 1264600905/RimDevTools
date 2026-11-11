# 独立群战实验

## 入口与结构

```powershell
.\.tools\RimDevTools.exe test run --mode groupcombat --scenario-path "Tests/Benchmarks/groupcombat.autotest.xml" --test-name Example_WolvesVsBears --timeout-seconds 900
```

`--test-name` 必填、可重复，接受稳定 testDefName、完整 caseId 或唯一显示名；无匹配/重名报错。完整 caseId 可点名单个矩阵展开项。禁止普通 auto 入口及 `--scenario-only`，Case 不接受 Coverage/Success/Targets。群战与回归分目录，防止目录输入混用。

```xml
<Case id="wolves-bears" kind="groupCombat" seed="12345" timeoutRealSeconds="300">
  <testDefName>MyMod_WolvesVsBears</testDefName>
  <Scene type="mainMap" sizeX="100" sizeZ="100" battlefield="cover" featureDensity="1" spawnClearance="6"/>
  <Teams><Team id="A" name="狼" ai="defendPoint"/><Team id="B" name="熊" ai="lure"/></Teams>
  <Actors>
    <PawnGroup id="wolves" team="A" count="20" originX="22" originZ="42" formation="grid" columns="4" spacing="2"><basePawnKind>Wolf_Timber</basePawnKind></PawnGroup>
    <PawnGroup id="bears" team="B" count="20" originX="62" originZ="42" formation="grid" columns="4" spacing="2"><basePawnKind>Bear_Polar</basePawnKind></PawnGroup>
  </Actors>
  <GroupCombat durationSeconds="180" timeSpeed="fast"/>
</Case>
```

只支持双方，各队须有成员，每队初始最多 200 人。共享 `MaxBattleMembers` 常量为 600，但双方各 200 的限制意味着一场最多 400，不能把 600 当成可用人数。全部初始角色须生成在本轮普通主地图内，阵型完整位于边界内。默认生成器 `LogSync_AutoTestStudioMap`，地图规则见 [schema-and-actors.md](schema-and-actors.md#地图与预设)。

`PawnGroup` 可用 `count` 或互斥的 `combatPowerBudget`，混编则引用 Suite 的 `SquadTemplates`；成本与冻结名单见 [squads-and-rosters.md](squads-and-rosters.md)。战斗阵型先沿 Z 填充，再沿 X 展开：grid 的 `columns` 实际是每条纵列的人数，`line` 是沿 Z 的单列。`spacing` 默认 2，至少为 1；grid 的 `columns` 默认 5。不要按普通回归先 X 后 Z 的阵型估算边界；预算解析后也会重新校验实际占地。擂台自动安排出生阵型，另见 [arena.md](arena.md#地图ai-与出生阵型)。

### 固定战场地图

群战和擂台共享 `Scene.battlefield` 样式目录：`open`（开阔平地）、`cover`（沙袋掩体）、`mountainRidges`（花岗岩山脊）、`mountainValley`（砂岩峡谷）、`glacier`（冰川雪原）、`urban`（废墟街区）。这些是宿主实现的固定样式，不是任意 biome、TerrainDef 或 MapGeneratorDef 名称。样式在默认主地图生成后放置确定性地形 / 建筑 / 掩体，受地图 seed 控制。

```xml
<Scene type="mainMap" sizeX="120" sizeZ="120" battlefield="mountainRidges"
       featureDensity="1" spawnClearance="6"/>
```

`featureDensity` 默认 1、范围 0.5—1.5；`spawnClearance` 默认 6、范围 2—12。它们必须与 `battlefield` 一起出现。battlefield 会补入默认天气、温度、屋顶、日照；可在 `Environment` 显式覆盖。出生区清理后仍会检查通路和地图边界。省略 battlefield 则保留所选生成器场景；默认 `LogSync_AutoTestStudioMap` 是测试平地，并非自然生成的山地。仅群战和 Arena 接受 battlefield，普通 auto 不接受。

双方使用新建临时阵营，彼此双向敌对；XML 第一队固定与玩家双向敌对，不借用已有阵营。每轮独立创建并清理地图、世界对象、Pawn、临时阵营，恢复原地图、选择、镜头、速度与分辨率。清理失败停止后续轮次，不能继续复用残留。

## AI 与准备

Team 的 `ai` 优先于 `GroupCombat.aiPolicy`，默认 **assaultColony**。擂台使用同一策略表：`Battle.aiPolicy` 是赛制默认值，每个 `<Entrant ai="...">` 可以覆盖；省略时继承默认，生成每场对局的左右 Team 后仍按 entrant 身份应用覆盖。`Entrant.ai` 不接受 `defendX`、`defendZ`、`defendRadius`；Arena 的 defendPoint 锚定每队本场出生点。当前群战和擂台共用 `CombatFightService`；早先自定义 AI 已在最新实现撤回，不能根据旧 `TestAi_*` 夹具名宣称还在强制索敌或调度攻击 Job。

| AI | 用途 / 限制 |
| --- | --- |
| assaultColony | 默认原生即时进攻：继承 LordJob_AssaultColony 与原生 AssaultColony duty，保留原生状态图、索敌、走位、攻击 Job、技能和模组 ThinkTree |
| battle / seekAndAttack / native | 上项的兼容别名；不再启用独立测试 AI 或远程接敌 Job 调度器 |
| assaultThings | 原生 AssaultThings 指向对方初始成员；索敌半径小，距离外可能下达近战破坏任务，不适合远程交火对比 |
| defendPoint | 共享战术控制器，在据点范围内防守；仅群战 Team 可填 defendX/defendZ/defendRadius，省略位置取本队出生均值、radius 默认 28；Arena 以本场出生均值为锚点且不接受这些坐标属性 |
| lure | 共享战术控制器，按队伍武器角色组织诱敌与远程支援；远程成员保留射击职责 |

所有支持值：`battle`、`seekAndAttack`、`assaultThings`、`assaultColony`、`defendPoint`、`lure`、`native`。`battle`、`seekAndAttack`、`native` 是 `assaultColony` 原生即时进攻控制器的兼容别名。可在一场中混用，例如默认 AI 为 assaultColony，Team A 用 defendPoint、Team B 用 lure：

```xml
<Teams>
  <Team id="A" name="固守方" ai="defendPoint" defendX="15" defendZ="45" defendRadius="8"/>
  <Team id="B" name="勾引方" ai="lure"/>
</Teams>
<GroupCombat aiPolicy="assaultColony" durationSeconds="180" timeSpeed="fast"/>
```

只在 `ai="defendPoint"` 时允许群战 `defendX`、`defendZ`、`defendRadius`。省略坐标时锚点为本队成员出生坐标均值，半径默认 28；显式坐标必须在地图内，半径为有限正数。群战 team 未写 ai 时继承 `GroupCombat.aiPolicy`；赛制默认值未写时为 `assaultColony`。

即时进攻控制器关闭绑架、偷窃、超时撤退和 AddFleeToil，并移除原生 ExitMap toil 及指向它的转换；只在当前战斗地图的该控制器中以 `LordToil.AllowSelfTend=false` 禁原版 NPC 自疗。这些约束不应泛化到 assaultThings/defendPoint 或模组私有治疗。生成后用原生 ClearAllRelations 清社交关系，不保证以后永不争吵；个体异常逃离仍单列 escaped。

排查不交战时读取本轮原始日志中的 `group.ai`（`controller=native`、`customJobScheduler=false`、duty 等）、`group.ai_native_job` 和 `group.ai_shot`（首次成功发射的武器、距离、射程）。job 诊断每 120 ticks 采样，发射日志不等于命中；结合实际伤害和原生前提判断。XML 不提供任意 C# AI 类入口，也不要另写强制逐 tick 攻击脚本来掩盖原生行为。

Steps 仅同步准备，接受 DebugAction、AddHediff、RemoveHediff、Equip、DropEquipment、ApplyDamage、Kill、Destroy、SetRot、BindMech、Require。准备伤害不纳入战斗统计。执行错误或清理失败会保留诊断并停止后续轮次。

## 时间、复活与结算

- GroupCombat.timeSpeed 为 normal/fast/superfast，默认 normal。durationSeconds 默认 180（10800 ticks），范围 `(0,86400]`。
- 全部准备与初始图完成后共同开始战斗计时。原生暂停会冻结战斗、单位存活时钟、DPS 分母及 Case.timeoutRealSeconds；CLI.timeout-seconds 仍是整个进程独立实际上限，须留出多轮生成、倒计时和清理开销。
- 一方所有已登记单位倒地/死亡且无待复活者时进入默认 300 ticks 胜负确认。`<End confirmationTicks="600"/>` 放在 GroupCombat 内可调整，范围 0—36000。新单位、起身或复活重新判定；原生拒绝死亡待复活期阻止提前结束。
- 双方均失能时等待双方确认后平局；达到模拟上限比较仍 standing 的绝对人数，少者落败，相等平局；倒地不计入该人数。死亡/逃离/未知移除分列，不使用“无伤害”提前结束。
- 新召唤/分裂成员按临时阵营、Lord 阵营或明确适配器归属，不猜站位或最近攻击者。同 Pawn 复活不增加累计参战人数，新 Pawn 单独记录；待复活与新增生命周期受支持范围影响。
- 私有系统延迟召唤/复活不能凭空预测，可配置更长 confirmationTicks，或由模组提供明确 `GroupCombatLifecycleAdapters.Register(id, ownerFaction, pendingRevival)` 适配；不要为某怪物种类硬编码推断。

## 继承、Patch 与多轮

群战 Case 可 `extends="父CaseID"` 多层继承整场配置。`abstract="true"` 只作模板，不执行；每个可执行派生 Case 必须有自己的 testDefName。属性/单例递归覆盖，Actors/Teams/Steps 同 id 合并，未提及成员保留。与 PawnTemplate 同名字段整体覆盖的语义区分。

```xml
<Case id="common" abstract="true" kind="groupCombat">
  <!-- 填写公共 Scene、Teams、Actors、GroupCombat -->
</Case>
<Case id="variant" extends="common">
  <testDefName>MyMod_VariantBattle</testDefName>
  <Patches>
    <SetAttribute xpath="Teams/Team[@id='A']" name="name" value="新队伍"/>
    <SetText xpath="Actors/PawnGroup[@id='wolves']/basePawnKind" value="Wolf_Timber"/>
  </Patches>
</Case>
```

Patches 顺序执行 SetAttribute(xpath/name/value)、SetText(xpath/value)、AddNode、ReplaceNode、RemoveNode。AddNode/ReplaceNode 的内容置于 Value。XPath 相对本 Case 且必须命中内部子元素，不跨 Case；删除/整表替换须显式 Patch。无匹配、继承环、未知父项和变更后的非法配置在启动前失败，不改变父或兄弟 Case。

```xml
<GroupCombat durationSeconds="180" timeSpeed="fast">
  <End confirmationTicks="300"/>
  <Metrics detailLimit="10000" requireCompleteAttribution="false"/>
  <Experiments swapSides="true"><Seed value="101"/><Seed value="202"/></Experiments>
</GroupCombat>
```

每个种子各跑原位/换边两轮，最多 128 个显式种子、展开后总计最多 1024 轮。换边镜像 X 方向出生和明确防御点，布局不镜像；每轮独立地图与 caseId。按 testDefName/队伍汇总，轮次团队分布和合并单位分布分开。暂存角色 file 导入见 [views-and-staging.md](views-and-staging.md)。

## 指标与测量质量

主伤害是原生 `Thing.TakeDamage` 返回的 `DamageResult.totalDamageDealt`，表示引擎结算量，不等于面板伤害，也不承诺等于截断后的净生命损失。

| 指标 | 分母 / 人群 |
| --- | --- |
| 团队 DPS | 对敌 Pawn 结算伤害 / 共同战斗模拟秒数 |
| 个人 DPS | 自身对敌 Pawn 结算伤害 / 自身 AliveTicks 对应秒数；站立/倒地算存活，死亡/逃离/移除停计，复活恢复 |
| MeanOutput / 面板平均输出 | 全队累计对敌输出 / 累计参战人数（Initial + Joined），含阵亡和新增贡献 |
| MeanDps（兼容字段） | 当前场上活单位个人 DPS 算术平均，零输出也参加 |
| MeanDamageTaken | 同一批当前场上活单位自开战累计承伤的均值，不是每秒承伤 |
| 排名及完整分布 | 全部初始及新增单位，保留阵亡贡献；个人 DPS 各用自己的存活时间 |

显式指标元数据用 `damageBasis="engineSettled" dpsBasis="unitLifetime" teamDpsBasis="battleWindow" population="registeredRoster" meanPopulation="aliveOnMap"`。旧 `population="initialRoster"` 输入会归一化为 registeredRoster，不能据此解释成仅统计初始成员；battleWindow 个人 DPS 不再是合法口径。保留原始精度，不从显示四舍五入后的数字重算排名/中位数。

友伤、自伤、环境、未知来源分账，建筑/护甲/护盾通道独立；原生 CompShield 完全吸收量另记 shield，不进主 DPS。第三方护盾、直接 Hediff 修改、流血及绕过 TakeDamage 的持续伤害不猜测量与归因。同 tick 同额的独立调用不去重，共享 DamageResult 的嵌套转发只计一次。detailLimit 为 0—1000000，只裁事件明细，不裁在线累计。

通用后端 MeasurementQuality 为 partial，明确采集异常为 incomplete；不能宣称所有模组归属完整。`requireCompleteAttribution=true` 未满足时单独失败；落败、平局、低 DPS 本身不是 CLI 失败。

## 输出与画面

工具返回原始 artifacts，生成 `groupcombat_<runId>_Result.json`、`_Report.md` 和 `_Screenshots`；保留完整冻结输入、轮次/角色快照、队伍与逐单位指标、分布及测量限制，不写入既有回归结果。

默认无边框全屏。Case 的 `<Output width="1280" height="720"/>` 仅设原始截图输出尺寸，不缩小游戏窗口；宽高一起填，分别 800—3840 / 600—2160。GroupCombat 内 `<Overlay enabled="false"/>` 可关榜单。相机跟踪只含本轮从未倒地/死亡且当前在场活单位，起身/复活后也不恢复资格，故镜头没跟随不等于角色没参战。

在聊天中分别说明执行完成与清理状态、胜负/轮数/种子/换边、指标口径、测量质量、实际日志及原始路径；不要把一轮胜负当作普适平衡结论或回归通过率。

全部普通 Pawn/PawnGroup 和 squad 现在共用逐成员冻结及准备证据校验。GeneratedSnapshot 核对出生配置，Preparation 逐条保留目标与动作后快照，InitialSnapshot 记录开战状态；Equip/DropEquipment 等合法动作不会与准备前冻结名单冲突。配置身份用于榜单分组，同名兵种不会覆盖不同配置。普通 PawnGroup 可使用 costMode=pawnKind/native，详见混编与预算参考。
