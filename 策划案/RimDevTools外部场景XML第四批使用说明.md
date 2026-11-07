# 外部场景 XML 第四批使用说明：独立群战

适用后端：RimWorld 1.7。群战是数值评测；输赢、执行是否完成、伤害测量质量分别记录，不进入普通回归通过率。

## 运行

```powershell
RimDevTools.exe test run --mode groupcombat --scenario-path AutoTest.Core.Tests/Fixtures/GroupCombatHumans.example.xml --test-name Batch4_HumanFirefight
RimDevTools.exe test run --mode groupcombat --scenario-path AutoTest.Core.Tests/Fixtures/GroupCombatHumans.example.xml --test-name Batch4_HumanRepeat
RimDevTools.exe test run --mode groupcombat --scenario-path AutoTest.Core.Tests/Fixtures/GroupCombatSpecies.example.xml --test-name Batch4_AnimalGroups --test-name Batch4_MechanoidGroups
```

开发源码时可用 `dotnet RimDevTools/bin/Release/net10.0/RimDevTools.dll` 替代 EXE。游戏验证前先构建 `source/AutoTestRunner/AutoTestRunner.1.7.csproj`，再运行 `test run --deploy ...`；项目通用部署不会自行构建 AutoTestRunner。

`--scenario-path` 接受文件或目录，目录只枚举当前层 `*.autotest.xml`。`--test-name` 必填，可重复指定，支持稳定 `testDefName`、完整 caseId 或唯一显示名；重名和无匹配会报错。完整 caseId 可只运行矩阵展开后的一项。群战文件不能进入普通 `auto` 入口，也不接受普通回归的 `--scenario-only`、Coverage、Success 或 Targets。

## 最小结构

```xml
<TestSuite schemaVersion="1" id="my.benchmark" gameVersion="1.7">
  <Case id="wolves-bears" kind="groupCombat" name="森林狼对北极熊" seed="12345" timeoutRealSeconds="300">
    <testDefName>MyMod_WolvesVsBears</testDefName>
    <Scene type="mainMap" sizeX="100" sizeZ="100">
      <Environment weather="Clear" temperature="21" roof="none" naturalSun="true"/>
    </Scene>
    <Teams><Team id="A" name="森林狼"/><Team id="B" name="北极熊"/></Teams>
    <Actors>
      <PawnGroup id="wolves" team="A" count="20" originX="22" originZ="42" formation="grid" columns="4" spacing="2"><basePawnKind>Wolf_Timber</basePawnKind></PawnGroup>
      <PawnGroup id="bears" team="B" count="20" originX="62" originZ="42" formation="grid" columns="4" spacing="2"><basePawnKind>Bear_Polar</basePawnKind></PawnGroup>
    </Actors>
    <GroupCombat durationSeconds="180" timeSpeed="fast"/>
  </Case>
</TestSuite>
```

可复用前三批的 Templates、ScenePresets、Requirements、Matrix、角色覆盖及固定布局。角色总数最多 300，两队均须有成员；初始角色必须生成在本轮地图，阵型必须完全位于边界内。新建普通主地图而非口袋地图，默认使用 `LogSync_AutoTestStudioMap`，也可显式引用适用的已加载 MapGeneratorDef。

双方使用新建临时阵营，彼此双向敌对；XML 顺序中的第一队固定与玩家双向敌对。不会借用玩家或其他既有阵营作为参战方。地图、世界对象、参战 Pawn 和临时阵营由每轮结束路径清理，之后恢复原地图、选择、镜头、速度和分辨率。

## AI、时间与准备

每个 Team 可独立填写 `ai`，省略时继承 GroupCombat 的 `aiPolicy`，缺省为 `battle`：

| 值 | 行为 |
| --- | --- |
| battle | 默认群战任务，沿用原生 AssaultColony 进攻职责，关闭群体逃跑转换与原版 NPC 自疗入口；保留种族/模组原有行为树 |
| assaultThings | 原生 LordJob_AssaultThings，指定对方初始成员为攻击目标；原生索敌半径较小，范围外可能走破坏目标分支并下达近战任务，不适合远程交火对比 |
| assaultColony / native | 原生 LordJob_AssaultColony，关闭劫掠、超时逃跑和偷窃选项；可显式使用，按原生战斗逻辑选择武器、射击位置及贴身近战 |
| defendPoint | 原生据点防御，可设置 defendX、defendZ、defendRadius；省略坐标时取本队出生位置均值 |
| seekAndAttack | 原生战斗 Lord 加工具调度原生攻击/技能 Job，处理距离外接敌；保持已有攻击预热和连射 |

全体参战 Pawn 生成后统一调用原生 ClearAllRelations 清理社交关系，包括反向关系；这不保证陌生人永不争吵。默认 battle 通过 LordToil.AllowSelfTend=false 禁止原版 NPC 自疗，不依赖殖民者医疗方案；自带治疗逻辑的模组可能忽略此入口。角色的 ThinkTree 不作替换。个体原生行为和模组行为仍可能逃离，结果单列 escaped。新增、召唤或分裂出的 Pawn 按临时阵营、Lord 阵营或明确注册的归属适配器加入所属队伍；不会根据站位或攻击对象猜测归属。

GroupCombat 的 `timeSpeed` 默认 `normal`（一倍速），接受 normal、fast、superfast，例如 `<GroupCombat timeSpeed="fast"/>`。durationSeconds 默认 180（10800 ticks），支持 `(0,86400]`。群战期间允许使用原生暂停和速度控制；暂停时战斗计时、存活计时、DPS 分母和 Case 的 timeoutRealSeconds 均暂停。CLI 的 timeout-seconds 仍是整个进程的独立实际时间上限。所有成员准备完毕并拍完初始图后才开始共同计时和战斗。

一方全部已登记单位倒地或死亡，且没有待复活单位时，进入胜负确认；默认持续模拟 300 ticks（5 秒），期间出现新单位、起身或复活会重新判定。双方都失去战斗能力时等待双方确认完成，判为平局。`<End confirmationTicks="600"/>` 可将确认时间改为 10 秒，范围 0—36000 ticks；暂停时确认时间也暂停。原版拒绝死亡的自动复活等待期会阻止提前结算。达到模拟时限时比较仍在场的活人数（含倒地）；少者落败，相等平局。死亡、逃离、未知移除分开记录。没有无伤害提前结束策略。

Steps 只作为同步准备阶段，接受 DebugAction、AddHediff、RemoveHediff、Equip、DropEquipment、ApplyDamage、Kill、Destroy、SetRot、BindMech、Require；战斗统计不包含准备伤害。步骤错误和条件不满足保留失败并清理，然后继续后续轮次；清理失败会停止后续轮次以免复用残留。

## 整场继承与局部 Patch

同一个 TestSuite 内，Case 可通过 `extends="父Case的id"` 多层继承整场配置。`abstract="true"` 的 Case 只作模板，不参与执行；每个可执行派生 Case 必须填写自己的 testDefName。属性和单例字段递归覆盖，Actors、Teams、Steps 中相同 id 的节点合并，未提到的成员保留。数组删除或整体替换使用显式 Patch，避免把列表意外拼接。

```xml
<Case id="common" abstract="true" kind="groupCombat">
  <!-- Scene、Teams、Actors、Steps、GroupCombat、Output 等公共配置 -->
</Case>
<Case id="a-vs-b" extends="common" name="a 对 b">
  <testDefName>Battle_A_vs_B</testDefName>
</Case>
<Case id="c-vs-b" extends="a-vs-b" name="c 对 b">
  <testDefName>Battle_C_vs_B</testDefName>
  <Actors><PawnGroup id="left" template="c"/></Actors>
  <Patches>
    <SetAttribute xpath="Teams/Team[@id='A']" name="name" value="C队阵容"/>
    <SetAttribute xpath="GroupCombat" name="durationSeconds" value="180"/>
  </Patches>
</Case>
```

模板 c 仍在 Templates 中定义。局部 Patch 按顺序执行，支持 SetAttribute（xpath/name/value）、SetText（xpath/value）、AddNode、ReplaceNode、RemoveNode；前两种修改属性或叶节点文本，AddNode 和 ReplaceNode 的内容放在 `<Value>...</Value>` 中。XPath 相对于本 Case，必须命中本 Case 内的子元素；不允许跨 Case，匹配为空、继承环、未知父 Case 或修改后的无效配置都会在启动游戏前报错。继承和 Patch 展开后仍经过原有完整校验，再冻结到游戏进程；父 Case 和兄弟 Case 不会被修改。

现成示例位于 `AutoTest.Core.Tests/Fixtures/GroupCombatBulbfreaksVsHussars.autotest.xml`：公共场地 → 电荷蜈蚣对电荷步枪骠骑 → 只替换 A队的恶臭瘤兽版本。

## 动态单位生命周期

出生与阵营赋值后立即核对成员，每个模拟 tick 在胜负判断前再扫描查漏。同一 Pawn 复活或重新生成只使用一个稳定记录；新召唤单位有独立记录，存活时钟从加入的 tick 开始。原生分裂时装在 PawnFlyer 内短暂腾空的 Pawn 仍属于场上存活成员。初始、战斗新增、存活、倒地、死亡和待复活分别统计，排名、伤害、均值、胜负与清理共享完整成员名单。

非标准模组可调用 `GroupCombatLifecycleAdapters.Register(id, ownerFaction, pendingRevival)` 提供明确所属阵营及待复活状态，返回的 IDisposable 用于注销；无需按怪物种类修改测试器。模组隐藏在私有系统里的未来召唤或延迟复活无法凭空预测，应提供适配器或配置足够的 confirmationTicks。尚未出生的对象不增加人数；已登记单位的复活不增加累计参战人数。原有倒地/死亡历史不会清除，因此它们重新参战后仍不恢复相机追踪资格。

## 多种子、换边和角色文件

```xml
<GroupCombat durationSeconds="12">
  <Metrics detailLimit="10000" requireCompleteAttribution="false"/>
  <Experiments swapSides="true"><Seed value="101"/><Seed value="202"/></Experiments>
</GroupCombat>
```

每个种子各跑原位、换边两轮，每轮独立地图和 caseId。换边沿地图 X 中线镜像出生区域和明确指定的防御点，布局保持原位。最多 128 个显式种子，展开后总共最多 1024 轮。跨轮结果按 testDefName 和队伍汇总，轮次团队分布与合并单位分布分开计算。

Templates 可引用第三批暂存角色：`<PawnTemplate id="staged" file="../../.tools/ProjectData/temporary-test-pawn.xml"/>`，相对路径以当前场景文件所在目录解析。引用文件只在 CLI 开始时读取一次，内容、哈希与依赖一起冻结；后续修改当前角色文件不会改变已开始的实验。覆盖请另建 `extends="staged"` 模板，file 节点只允许 id 和 file。

## 榜单与输出

顶部居中显示放大的场景标题，第二行显示战斗计时与状态。左右面板分别标为 A队、B队，保留 XML 队伍名称作为说明。全高清下面板宽度和内部字体、图标按原先的 1.5 倍显示，高度适配屏幕并留出原生时间控制；小屏幕自动压缩。

开战前在屏幕中央依次显示“3、2、1、开始”，下方以带角色图标的阵容卡片列出双方编组与数量。倒计时按实际时间推进，游戏保持暂停，战斗计时和伤害统计均为零；沿用三视图展示任务与朝向控制方式，让每个角色面向对方阵容中心并展示武器。倒计时结束后解除展示任务并同时启动双方战斗。

每方半透明面板显示：存活编组前五种及数量、并排的累计输出/承伤 Top10、实际角色渲染图、状态、平均输出和总/存活人均承伤。相同指标跨方使用共同刻度；榜单每 250 毫秒刷新，伤害账本按事件累计。两侧面板只绘图，鼠标点击和滚轮穿透到下方游戏，不创建滚动条；小分辨率自动紧凑显示排行名称与数值。角色随机外观不会拆成新编组，明确配装差异会分组。

战斗结束后暂停模拟并显示结算 3 个实际秒，再清理场景。结算以角色图标标识双方，显示胜负、全队累计对敌输出/承伤和场上存活成员（含倒地）的累计对敌输出/承伤。输出榜按累计对敌伤害排序，保留阵亡者贡献；JSON 中的 DPS 指标仍保留。角色名字与原生地图姓名标签按状态着色：存活绿色，倒地/死亡红色，离场/移除灰色。

群战使用专用跟随视角，持续按本轮从未倒地、从未死亡且当前在场存活单位的整体边界居中；本轮只要倒地或死亡过，之后起身或复活也永久退出相机追踪范围，直到下一轮重新生成。没有合格追踪对象时保留最后视角，并预留两侧榜单、顶部标题和底部原生时间控制区域。双方聚拢时平滑拉近，散开时平滑拉远；中心位置和缩放都按实际时间过渡，不瞬间切点。缩放保留余量与双向缓冲区，忽略小幅位移，设置最小视野避免单人特写，并隔离战斗震屏。兼容 CameraPlus 的实际投影缩放；停止群战后恢复原相机配置和视角。榜单状态使用“存活 / 倒地 / 死亡 / 离场 / 移除”。

群战默认无边框全屏，`<Output width="1280" height="720"/>` 仅设置生成截图的尺寸，按已完成的全屏帧缩放写入正式原始产物，不改变游戏窗口或触发窗口缩小。宽高须一起填写，范围分别为 800—3840、600—2160；省略时直接使用当前全屏帧尺寸，结束恢复启动前的显示设置。`<GroupCombat><Overlay enabled="false"/></GroupCombat>` 可关闭榜单。

CLI 成功返回 result.artifacts，失败返回 error.details.artifacts。工具在对应 autoTestOutputPath 原始路径生成 `groupcombat_<runId>_Result.json`、`_Report.md` 和 `_Screenshots`。JSON 包含冻结输入、每轮初始角色快照、初始及新增逐单位/队伍/矩阵、分布极值的单位 ID、原始精度中位数、明细保留数及测量限制。群战保留全部参与者，不套用普通回归只列失败的过滤规则；不追加或重写既有回归结果。

## 伤害口径与限制

主指标使用原生 Thing.TakeDamage 返回的 DamageResult.totalDamageDealt，表示引擎结算量，不等同于武器面板伤害，也不承诺等于截断后的净生命损失。团队 DPS = 团队对敌 Pawn 结算伤害 /（共同战斗 ticks / 60）。个人 DPS = 该单位对敌 Pawn 结算伤害 /（自身在场存活 AliveTicks / 60）；站立和倒地均算存活，死亡、逃离和移除期间停止累计，重新在场存活后恢复累计。

面板平均输出与 JSON 的 MeanOutput 均为全队累计对敌输出 / 累计参战人数（Initial + Joined），包含阵亡和新增成员贡献，不按时间或当前存活人数相除；人数为零时记 0。兼容保留的 MeanDps 为当前场上每个活着单位的个人 DPS 的算术平均；MeanDamageTaken 为同一批单位自开战以来累计承受伤害的算术平均，单位是伤害量，不是每秒承伤。零输出和零承伤的存活单位也参与均值；场上零存活时两个均值均为 0。总量、排行和完整分布保留全部初始及新增成员（含阵亡者），个人分布中的 DPS 使用各自存活时间。JSON 的 AliveTicks 可用于复核分母。

显式填写指标元数据时使用 `<Metrics damageBasis="engineSettled" dpsBasis="unitLifetime" teamDpsBasis="battleWindow" population="registeredRoster" meanPopulation="aliveOnMap"/>`；其中 population 描述初始及新增成员的完整分布，旧的 initialRoster 标签兼容读取后规范为 registeredRoster，meanPopulation 描述存活人均承伤和兼容保留的 MeanDps。兼容旧配置的显式 Summary 使用 `dps="livingUnitLifetimeMean" damageTaken="totalAndMeanPerLivingUnit"`。旧的 battleWindow 个人 DPS 和初始人数均值配置会拒绝加载，避免配置与结果含义不符。

友伤、自伤、环境和未知来源分别记账，护盾、护甲/物品及建筑使用独立通道。原生 CompShield 完全吸收的输入单独记录为 shield，不增加主 DPS；第三方护盾的吸收量、直接 Hediff 修改、流血及绕过 TakeDamage 的持续效果不推断归属，不归给“最近攻击者”。

同 tick 同额伤害是独立调用，不按文本去重；共享同一 DamageResult 的嵌套转发只记一次，反伤保留独立事件。共享伤口对象的聚合包装会标记测量不完整，不能当成普遍兼容。事件明细最多保留 detailLimit 条（0—1000000），不影响在线累计和矩阵精度。

当前通用后端的 MeasurementQuality 为 partial，遇到明确采集异常则为 incomplete；不能宣称所有模组机制归属完整。显式要求 requireCompleteAttribution=true 时，未达到要求会单独返回测量条件未满足；落败、平局或低 DPS 本身不会使 CLI 失败。
