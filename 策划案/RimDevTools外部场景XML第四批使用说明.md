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

可复用前三批的 Templates、ScenePresets、Requirements、Matrix、角色覆盖及固定布局。角色总数最多 300，两队均须有成员；角色必须生成在本轮地图，阵型必须完全位于边界内。新建普通主地图而非口袋地图，默认使用 `LogSync_AutoTestStudioMap`，也可显式引用适用的已加载 MapGeneratorDef。

双方使用新建临时阵营，彼此双向敌对；XML 顺序中的第一队固定与玩家双向敌对。不会借用玩家或其他既有阵营作为参战方。地图、世界对象、参战 Pawn 和临时阵营由每轮结束路径清理，之后恢复原地图、选择、镜头、速度和分辨率。

## AI、时间与准备

每个 Team 可独立填写 `ai`，省略时继承 GroupCombat 的 `aiPolicy`，缺省为 `assaultThings`：

| 值 | 行为 |
| --- | --- |
| assaultThings | 原生 LordJob_AssaultThings，指定对方初始成员为攻击目标 |
| assaultColony / native | 原生 LordJob_AssaultColony，关闭劫掠、超时逃跑和偷窃选项 |
| defendPoint | 原生据点防御，可设置 defendX、defendZ、defendRadius；省略坐标时取本队出生位置均值 |
| seekAndAttack | 原生战斗 Lord 加工具调度原生攻击/技能 Job，处理距离外接敌；保持已有攻击预热和连射 |

普通原生行为和模组行为仍可能逃离，结果单列 escaped。不会新增召唤物归属或阵营转换规则。

GroupCombat 的 `timeSpeed` 默认 `normal`（一倍速），接受 normal、fast、superfast，例如 `<GroupCombat timeSpeed="fast"/>`。durationSeconds 默认 180（10800 ticks），支持 `(0,86400]`。群战期间允许使用原生暂停和速度控制；暂停时战斗计时、存活计时、DPS 分母和 Case 的 timeoutRealSeconds 均暂停。CLI 的 timeout-seconds 仍是整个进程的独立实际时间上限。所有成员准备完毕并拍完初始图后才开始共同计时和战斗。

一方站立数为零即落败，同 tick 双方全倒为平局。达到模拟时限时比较仍在场的活人数（含倒地）；少者落败，相等平局。死亡、逃离、未知移除分开记录。没有无伤害提前结束策略。

Steps 只作为同步准备阶段，接受 DebugAction、AddHediff、RemoveHediff、Equip、DropEquipment、ApplyDamage、Kill、Destroy、SetRot、BindMech、Require；战斗统计不包含准备伤害。步骤错误和条件不满足保留失败并清理，然后继续后续轮次；清理失败会停止后续轮次以免复用残留。

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

每方半透明面板显示：存活编组前五种及数量、并排的输出/承伤 Top10、实际角色渲染图、状态、存活人均 DPS 和总/存活人均承伤。相同指标跨方使用共同刻度；榜单每 250 毫秒刷新，伤害账本按事件累计。两侧面板只绘图，鼠标点击和滚轮穿透到下方游戏，不创建滚动条；小分辨率自动紧凑显示排行名称与数值。角色随机外观不会拆成新编组，明确配装差异会分组。

群战使用专用跟随视角，持续按场上存活单位（含倒地）的整体边界居中，并预留两侧榜单、顶部标题和底部原生时间控制区域。中心位置和缩放都按实际时间平滑过渡，不瞬间切点；留出缩放余量，隔离战斗震屏，战斗中只在容纳更多单位时扩大，不随伤亡缩成单人特写。兼容 CameraPlus 的实际投影缩放；停止群战后恢复原相机配置和视角。榜单状态使用“存活 / 倒地 / 死亡 / 离场 / 移除”。

群战默认无边框全屏，`<Output width="1280" height="720"/>` 仅设置生成截图的尺寸，按已完成的全屏帧缩放写入正式原始产物，不改变游戏窗口或触发窗口缩小。宽高须一起填写，范围分别为 800—3840、600—2160；省略时直接使用当前全屏帧尺寸，结束恢复启动前的显示设置。`<GroupCombat><Overlay enabled="false"/></GroupCombat>` 可关闭榜单。

CLI 成功返回 result.artifacts，失败返回 error.details.artifacts。工具在对应 autoTestOutputPath 原始路径生成 `groupcombat_<runId>_Result.json`、`_Report.md` 和 `_Screenshots`。JSON 包含冻结输入、每轮初始角色快照、逐单位/队伍/矩阵、分布极值的单位 ID、原始精度中位数、明细保留数及测量限制。群战保留全部参与者，不套用普通回归只列失败的过滤规则；不追加或重写既有回归结果。

## 伤害口径与限制

主指标使用原生 Thing.TakeDamage 返回的 DamageResult.totalDamageDealt，表示引擎结算量，不等同于武器面板伤害，也不承诺等于截断后的净生命损失。团队 DPS = 团队对敌 Pawn 结算伤害 /（共同战斗 ticks / 60）。个人 DPS = 该单位对敌 Pawn 结算伤害 /（自身在场存活 AliveTicks / 60）；站立和倒地均算存活，死亡、逃离和移除期间停止累计，重新在场存活后恢复累计。

MeanDps 为当前场上每个活着单位的个人 DPS 的算术平均；MeanDamageTaken 为同一批单位自开战以来累计承受伤害的算术平均，单位是伤害量，不是每秒承伤。零输出和零承伤的存活单位也参与均值；场上零存活时两个均值均为 0。总量、排行和完整分布仍保留全部初始成员（含阵亡者），个人分布中的 DPS 使用各自存活时间。JSON 的 AliveTicks 可用于复核分母。

显式填写指标元数据时使用 `<Metrics damageBasis="engineSettled" dpsBasis="unitLifetime" teamDpsBasis="battleWindow" population="initialRoster" meanPopulation="aliveOnMap"/>`；其中 population 描述完整分布，meanPopulation 描述面板人均值。显式 Summary 使用 `dps="livingUnitLifetimeMean" damageTaken="totalAndMeanPerLivingUnit"`。旧的 battleWindow 个人 DPS 和初始人数均值配置会拒绝加载，避免配置与结果含义不符。

友伤、自伤、环境和未知来源分别记账，护盾、护甲/物品及建筑使用独立通道。原生 CompShield 完全吸收的输入单独记录为 shield，不增加主 DPS；第三方护盾的吸收量、直接 Hediff 修改、流血及绕过 TakeDamage 的持续效果不推断归属，不归给“最近攻击者”。

同 tick 同额伤害是独立调用，不按文本去重；共享同一 DamageResult 的嵌套转发只记一次，反伤保留独立事件。共享伤口对象的聚合包装会标记测量不完整，不能当成普遍兼容。事件明细最多保留 detailLimit 条（0—1000000），不影响在线累计和矩阵精度。

当前通用后端的 MeasurementQuality 为 partial，遇到明确采集异常则为 incomplete；不能宣称所有模组机制归属完整。显式要求 requireCompleteAttribution=true 时，未达到要求会单独返回测量条件未满足；落败、平局或低 DPS 本身不会使 CLI 失败。
