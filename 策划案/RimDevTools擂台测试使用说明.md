# RimDevTools 擂台测试与 combatPower 配兵

首版支持 RimWorld 1.7。擂台使用独立 `arena` 入口，普通 `auto` 不执行擂台；`groupcombat` 继续运行原有群战。袭击点、战力均指游戏加载继承及模组补丁后的 `PawnKindDef.combatPower`。

擂台与普通群战默认使用原版“立刻进攻”AI（`assaultColony`），直接复用 `LordJob_AssaultColony` 的原生进攻流程与 `AssaultColony` 职责，选敌、技能、射击选位、攻击与任务调度均交给游戏原有行为树。工具仅禁止自治疗、群体溃逃、超时撤退、绑架、偷窃及其他群体离场转换，不再定时给角色下发战斗任务。旧配置的 `battle`、`seekAndAttack`、`native` 都兼容为同一原生进攻策略。生理恢复、再生及模组治疗能力不作屏蔽。

Tournament.seed 为 -1 时，每届开赛随机抽取一次实际种子，保存到 ArenaPlan.ResolvedSeeds；原 XML 与输入哈希不变，重赛及后续对局沿用冻结值。工作台默认 -1，也可点击随机按钮生成具体种子，或手动输入复现赛事。

## 运行

在项目根目录执行。本地工具已生成在 `.tools/RimDevTools.exe`，可直接执行 `./.tools/RimDevTools.exe test run ...`。源码仓库也可使用下列命令；发布工具时将 `dotnet RimDevTools/bin/Release/net10.0/RimDevTools.dll` 替换为 `RimDevTools.exe`。

```powershell
dotnet build source/AutoTestRunner/AutoTestRunner.1.7.csproj -c Release
dotnet build RimDevTools/RimDevTools.csproj -c Release
dotnet RimDevTools/bin/Release/net10.0/RimDevTools.dll test run --deploy --mode arena --scenario-path AutoTest.Core.Tests/Fixtures/ArenaAcceptance.autotest.xml --test-name Arena_BudgetFive --test-name Arena_BudgetSix
```

可以重复指定 `--test-name`，依次执行多个独立赛事。必须同时传场景路径和名称；目录只读取本层 `*.autotest.xml`，跳过其他根类型文件，跳过数保存在冻结计划的 `SkippedFiles`。显式指定其他类型文件会报错。擂台按最大对局数、重赛次数和单场实际时限估算命令总超时；`--timeout-seconds` 可显式覆盖。测试用工具事务部署并恢复游戏设置与启用模组，不上传创意工坊。

## XML

```xml
<ArenaSuite schemaVersion="1" id="my.arena" gameVersion="1.7">
  <Tournament id="main" name="3000战力擂台" seed="12345">
    <testDefName>Arena_3000CP</testDefName>
    <Scene type="mainMap" sizeX="100" sizeZ="100">
      <Environment weather="Clear" temperature="21" roof="none" naturalSun="true"/>
    </Scene>
    <Roster mode="combatPower" budget="3000"/>
    <Entrants>
      <Entrant id="wolves" pawnKind="Wolf_Timber" name="森林狼"/>
      <Entrant id="mercenaries" pawnKind="Mercenary_Gunner" name="雇佣兵"/>
      <Entrant id="scythers" pawnKind="Mech_Scyther"/>
    </Entrants>
    <Battle durationSeconds="180" timeoutRealSeconds="300"
            timeSpeed="fast" confirmationTicks="300"/>
    <Scoring survivalWeight="45" suppressionWeight="25" exchangeWeight="20" speedWeight="10" timeReferenceSeconds="180"/>
    <TieBreak onDraw="replay" maxReplays="2"/>
    <Presentation bracketSeconds="3" settlementSeconds="3" advanceStartSeconds="3"/>
    <Output width="1920" height="1080"/>
  </Tournament>
</ArenaSuite>
```

固定数量用 `<Roster mode="count" count="20"/>`。数量模式可以在单个 `Entrant` 上写 `count="10"` 覆盖人数；预算模式可以写 `budget="3200"` 覆盖该参赛者的预算，未指定时沿用 `Roster.budget`。数量模式禁止预算覆盖，预算模式禁止人数覆盖。参赛者为 2～64 种互不重复的 PawnKind，ID 也须唯一。两种擂台模式都只允许具有有限正战力的单位。首版继承原生 PawnKind 配装。

人类参赛者可填写 `xenotype="Yttakin"` 等 XenotypeDef，明确指定基因类型；需要启用 Biotech 且该 Def 已加载。赛前展示与实际对局都应用该配置，换边后仍跟随原参赛者。省略时保留原生生成结果。例：`<Entrant id="pirates" pawnKind="Pirate_Yttakin" xenotype="Yttakin" count="80"/>`。

### 混编队伍与两种预算

`ArenaSuite` 或群战的 `TestSuite` 可以添加 `SquadTemplates`。参赛者用 `squad` 引用模板，与 `pawnKind` 二选一。原有 XML 默认保留基础 PawnKind CP 口径；显式 `costMode="native"` 时混编成员按 `PawnKind.combatPower × Xenotype.combatPowerFactor` 计费。原版袭击事件对点数的额外调整不在这里执行。

```xml
<SquadTemplates>
  <SquadTemplate id="yttakinRaid" source="faction" factionDef="PirateYttakin">
    <GroupMaker mode="random" kindDef="Combat" referencePoints="5000"/>
    <RaidRules strategyDef="ImmediateAttack"/>
    <Xenotypes mode="inherit"/>
  </SquadTemplate>
</SquadTemplates>
```

CP 预算：`<Roster mode="combatPower" budget="5000" costMode="native"/>`；人数预算：`<Roster mode="count" count="20" costMode="native"/>`。人数是整支混编队伍的总人数，不是每个兵种的人数。参赛者可用 `budget` 或 `count` 覆盖本方额度，两种模式不得混用。混编和原有单兵种可同场参赛：

```xml
<Entrants>
  <Entrant id="iguanas" pawnKind="DMS_Mech_Iguana" name="鬣蜥"/>
  <Entrant id="pirates" squad="yttakinRaid" name="毛绒种混编队"/>
</Entrants>
```

原版只有按点数配兵的接口。人数模式复用原版编队适用性、候选项、策略限制、兵种/异种权重，再抽满指定人数。`referencePoints`（默认 5000）仅为人数模式选择模板及筛选兵种提供原版所需的点数环境，不是另一条 CP 上限；CP 模式直接使用本方实际预算。无法凑齐人数时显式报错，不重复首领或偷偷减少人数。每队限 1～200 人；CP 预算上限 10000000，超出可支持规模时拒绝启动。

指定原版编队：把 `GroupMaker` 改为 `<GroupMaker mode="specific" kindDef="Combat" index="0"/>`。编号为加载完成后该派系**全部** `pawnGroupMakers` 的从零开始下标。结果 JSON 的 `Rosters[].GroupMakers` 列出编号、类型、权重、可用性、候选兵种、禁用策略及指纹；`GroupMakerIndex` 记录实际选择。可把返回的 SHA256 写入可选属性 `fingerprint`，更新模组后若内容变化则拒绝生成。`kindDef` 当前仅接受 `Combat`。

自定义权重编队仍复用原版配兵：

```xml
<SquadTemplate id="customRaid" source="custom" factionDef="Pirate">
  <Composition mode="weighted" allocator="native">
    <Option pawnKind="Mercenary_Gunner" weight="3"/>
    <Option pawnKind="Mercenary_Heavy" weight="1"/>
  </Composition>
  <Xenotypes mode="weighted">
    <Option def="Baseliner" weight="1"/>
    <Option def="Yttakin" weight="2"/>
  </Xenotypes>
</SquadTemplate>
```

权重不是最终人数比例。固定名单用 `Composition mode="fixed"`，子项改成 `<Member pawnKind="Mercenary_Gunner" count="10"/>`。人数模式要求名单人数恰好等于人数额度；CP 模式要求名单总花费不超过预算，不截断也不补人。固定名单也可以使用异种随机池。

异种策略可以是 `<Xenotypes mode="inherit"/>`、`<Xenotypes mode="fixed" def="Yttakin"/>` 或上例的权重池。异种在配兵阶段参与成本和适用性判断，再传给原生 Pawn 生成器，不在生成结束后简单替换。异种覆盖仅应用到人类成员，原版或模组编队中的动物、机械等非人类成员保留其种族，不强加异种；萌螈等种族通过相应派系和 PawnKind 选择。

群战复用完全相同的模板，在 `Actors` 中引用：

```xml
<!-- 人数预算 -->
<PawnGroup id="pirates" team="B" squad="yttakinRaid" count="20"
           costMode="native" originX="70" originZ="30"
           formation="grid" columns="10" spacing="2"/>
<!-- CP预算：用 combatPowerBudget="5000" 替换 count="20" -->
```

混编 PawnGroup 不接受单角色模板或其他 Pawn 覆盖字段；需要修改组成或异种时修改队伍模板。同一队伍阵营的混编组须使用相同生成派系，以保留派系相关能力。每个参赛者的兵种、异种、个人种子、技能、特征和配装在开赛前冻结；后续每轮生成新的角色并恢复配装，原生及模组的生成/出生效果继续运行。普通群战同种子换边也沿用相同名单。装备与异种会由 CLI 对照每个实际初始角色复核。快照不承诺复制模组私有组件状态或所有世界关系。

混编介绍卡存在多种兵种时，每隔 1 个现实秒循环切换代表成员，头像、该类人数和属性同步更新；左右箭头仍可手动切换，操作后重新等待 1 秒再轮播。单一构成固定展示。同显示名称的 PawnKind 合并为同一兵种，包括不同 DefName、异种及配装。战斗侧栏、开战阵容和赛后兵种数使用相同口径；实际生成与配装仍按完整冻结名单执行。底部列全队人数和总 CP，悬停查看各类人数和 CP。属性条是该类真实代表角色的属性，护甲包含出生后动态效果，不对不同配置取平均。JSON 同时记录详细成员配置、介绍采样数据和轮播记录。页面时长不足以展示全部构成时，可增加 `Showcase.pageSeconds` 或 `Page.seconds`。

扩展算法使用显式注册的生成器：

```xml
<SquadTemplate id="extensionTeam" source="generator" factionDef="Pirate">
  <Generator id="myMod.leaderAndGuards">
    <Param name="minimumGuards" value="4"/>
  </Generator>
  <Xenotypes mode="inherit"/>
</SquadTemplate>
```

扩展代码实现 `LogSyncMod.AutoTest.ITestSquadGenerator`，启动时通过 `TestSquadGenerators.Register(id, generator)` 注册。上下文含模板、派系、CP预算或人数、成本口径和种子；返回 `TestSquadMemberSpec` 列表。参数名称、类型及业务规则由生成器校验，工具统一复核人数、成本、兵种与异种。未知 ID 报错，XML 不解析任意 CLR 类型或执行脚本。示例 ID 需要扩展自行注册，并非工具内置算法。

可运行示例：`AutoTest.Core.Tests/Fixtures/ArenaSquads.autotest.xml` 和 `AutoTest.Core.Tests/Fixtures/GroupCombatSquads.autotest.xml`，需要启用 DMS Core 及其依赖。

单兵种 CP 配兵人数取预算能容纳的最大整数：`floor(budget/combatPower)`；混编按上面的编队规则分配。每队最多 200 人（同队多组人数合计），两队单场最多 400 人，三队群战最多 600 人；预算不足一人、非法战力、溢出或超限会停止，不会调整预算或截断人数。3000 战力可以配 40 个 75CP 森林狼，或 35 个 85CP 雇佣枪手；后者实际投入 2975，剩余 25。

Scene 沿用群战的严格场景配置。其他字段范围和完整默认值见[实施计划](全新xml测试实现文档/擂台测试与combatPower配兵实施计划.md)。`Output` 指输出截图分辨率，宽高须同时提供，范围 800～3840 × 600～2160；不改变实时游戏窗口。XML 拒绝未知字段、DTD 与外部实体。

## 赛制与胜负

每场开战前暂停并显示原有阵容与倒计时，再显示初始战场；结算卡片以大号金色数字显示胜利分数，并显示胜者的大图标或人类头像。结算后新增 5.5 秒的赛后天梯：依次突出 A 队、B 队，再播放胜者晋级动画；平局显示重赛状态。参赛对象按 PawnKind 固定配色，晋级节点继承胜者颜色。每次对决重建地图、临时敌对阵营和满编队伍。赛后天梯支持自动定位本场、全局总览和滚动，包含全部参赛者、已定对决和后续待定节点。群战与擂台期间仅隐藏参战单位模型下方的地图名称标签，排行榜、结算页与其他界面仍保留名称。

`Presentation.advanceStartSeconds` 设置在倒计时剩余多少秒时提前准备开战截图，默认 3 秒，范围 0～60；0 表示倒计时结束后才准备，超过倒计时长度时从倒计时开始准备。`bracketSeconds` 为完整倒计时长度，默认 3 秒，显示 3、2、1，再显示“开始”。准备期间 UI 继续倒数，模拟保持暂停；只有倒计时归零且初始截图完整落盘后才按配置档位推进模拟、计时和计分。准备较慢或提前量不足时会继续等待截图，不会提前消耗战斗时间。阵容展示在开战时收起，“开始”在开战后短暂保留。

赛前全员展示可在 `Presentation` 中配置：

```xml
<Presentation bracketSeconds="3" advanceStartSeconds="3">
  <Showcase pageSeconds="5">
    <Page seconds="7" caption="先看看谁能成为本届黑马。">
      <Entrant ref="scythers"/>
      <Entrant ref="wolves"/>
    </Page>
    <Page caption="最后登场的是装备齐全的雇佣兵。">
      <Entrant ref="mercenaries"/>
    </Page>
  </Showcase>
</Presentation>
```

`Page` 的排列顺序及页内 `Entrant` 的排列顺序就是实际展示顺序，`ref` 引用本届 `Entrants` 中的 **id**，不会改变比赛配对。每页允许 1～5 种，每个 id 最多出现一次；未知或重复引用、空页和超过 5 种会在启动前报错。未引用的参赛者自动按原名单顺序补到最后，每页最多 5 种，不会漏掉展示。未写 `Showcase` 时按原名单分页，每页 5 秒。

`Showcase.pageSeconds` 设置统一默认时长；`Page.seconds` 单独覆盖某页，均支持 1～60 秒的有限小数。`Page.caption` 为可选的单行文案，最多 80 个字符，显示在该页标题下方，按纯文本展示；XML 特殊字符用 `&amp;`、`&lt;` 等转义。不写文案时显示默认副标题。命令默认超时会计入所有页面的配置时长，截图未就绪时会继续等待。

完整验收配置为 `AutoTest.Core.Tests/Fixtures/ArenaShowcase.autotest.xml`，入口名 `Arena_Showcase_Custom`、`Arena_Showcase_Default`，覆盖自定义五人页、单人页、自动补页、默认配置及 1280×720 截图输出。

六项属性的进度条和右侧数值按同一套六档颜色显示：低于 20% 为灰色（粗糙），20%～40% 为白色（普通），40%～60% 为绿色（优秀），60%～80% 为蓝色（稀有），80%～95% 为紫色（史诗），95% 及以上为橙色（传说），达到分界值时进入更高一档。百分比取该项实际属性除以本届同项最高值，跨页保持一致；空条为零值。页面底部显示颜色图例。

擂台自动采用横向列多、纵向行少的阵型，目标宽高约 4:1，并按地图宽度限制横向占地、保留中央接近通道。140 格宽地图中，200 人为 25 列 × 8 行，75 人为 15 列 × 5 行。群战显式 XML 阵型仍按配置：`line` 为单条纵列，`grid.columns` 为每条纵列的容量。边界检查、群战换边和实际生成共用同一套坐标计算。普通外部场景仍采用原有横向排布。

群战和擂台的自动相机实际投影半高最多 28 格，不再为容纳边缘成员无限拉远；达到上限后保持战斗中心，边缘成员可能位于屏幕或侧栏之外。相机还读取 Camera+ 的全局及自定义图标替换阈值，必要时进一步收紧缩放，留出 2 个 UI 像素余量。测试战斗地图内另以 Harmony 补丁将 Camera+ 的有效图标触发阈值设为负数，并禁用屏幕边缘指示图标；全局及自定义兵种规则均适用。同时禁用原版缩放高亮的圆点与剪影，避免相机达到自定义硬上限时触发原版的 90% 范围判定。不会修改玩家的原版或 Camera+ 设置，战斗结束恢复原相机配置与图标行为。

擂台观战入口为 `自动 / 全局 / 交战区 / 跟随`，自动模式以稳定主战区为主，不再定时轮换景别；手动交战区保持局部交战取景，跟随模式关注指定人物。开场采用连续运镜：全局建立 0.2 秒，平移并推近左方 0.6 秒，左方停留 1.5 秒，保持相同尺度横移到右方 0.6 秒，右方停留 1.5 秒，平移并拉远回全局 0.9 秒，全局稳定 0.3 秒，随后开始原有倒计时。总运镜 5.6 秒，整个开场暂停模拟，双方特写取阵形中心附近的代表性群体；开场期间隐藏左右数据面板，完成后恢复。全局镜头固定在地图中心，结束特写进入倒计时时同步重置平移目标，避免继承人物位置从角落漂移回中心。

主战区按局部交战人数、交战距离及最近约数秒的输出/承伤选择，不使用整场累计输出排名决定镜头；当前有效战区至少保持约 12 秒，其他战区活跃度超过两倍时才切换。伤害统计缺失时仍按双方位置和原生任务目标选择。取景按当前站立单位数量调整：双方合计不超过 6 人时，交战区基础投影半高为 9 格、跟随为 8 格；7～16 人时分别为 12 格和 11 格；更多单位时沿用交战区 16 格、跟随 14 格。人数减少只降低交战区的最小取景，其扩大上限仍为 20 格，远程交战可按实际范围保留更宽视野。全局为 16～28 格。开场按各队人数取景，1～2 人的基础半高为 6 格，3～8 人为 8 格，更多成员为 10 格，按核心阵形可扩大至 12 格；双方使用所需的较大尺度，保证横移时不改变缩放。最终缩放含少量余量并受 Camera+ 阈值限制。更换关注区域时平移跟踪，不再强制先拉远再推近。战斗中缩放一旦确定便在同一帧直接设置，不播放渐变；缩小取景仍需持续稳定约 5 秒，且每次最多收近 2 格，忽略小幅缩放变化。开场使用独立的连续运镜时间线。原生相机 root 至少 11，单独设置实际投影，避免相机穿过 Pawn 渲染层。

两侧统计栏在特写结束后恢复固定完整布局。自动或跟随模式下可点击战场中的站立单位指定关注对象，自动模式点选后采用跟随取景；目标死亡、倒地或离场后，自动模式回到主战区，手动跟随则替换人物，倒地恢复或复活的单位可以重新进入镜头。手动模式保持所选取景，点击 `自动` 恢复主战区取景；全局直接定位地图中心，所选模式沿用到同一游戏进程内的后续对局。旧 API 的 `Close` 模式兼容为跟随。镜头决策每约 0.35 秒更新，但取景以交战中心为准，附近成员进出、倒地造成的包围盒变化不再直接推动中心；平移目标有 3 格静止范围，新的位置持续稳定约 0.75 秒后才采纳；连续同向跑动可继续确认，超过 2 格的反向或大幅变向会重新确认，再以不超过每秒 10 格的速度平滑移动。开场运镜使用独立的明确时间线。相机导播异常时退回原全局相机，不中止赛程。

`ArenaTribe200VsEmpire75.autotest.xml` 中的 `Arena_Tribe200VsEmpire100_Camera` 为 200 人部落对 100 人帝国的相机验证场景，使用一倍速；同文件原有 200 对 75 场景仍使用其原配置。

- `Tournament` 可配置 `<Matchmaking mode="closestCount"/>`（默认），常规轮次依次选择初始总人数差最小的两队，同差值用固定种子排序。使用 `<Matchmaking mode="random"/>` 时每轮按种子和轮次随机排序，再两两配对；不依赖人数或 XML 名单排列。相同种子、名单和晋级结果可复现配对，改变 `Tournament.seed` 可重新抽签。A 对 B 与 B 对 A 只是一场逻辑对决。
- 初始参赛队伍数为奇数，按所选匹配模式挑两队先打资格赛，再由偶数队伍进入普通轮次。
- 一轮完成后，胜者为大于一的奇数时，最近一次胜利分数最低的两队加赛。胜者更新最近分数并晋级。只剩一队即为冠军。
- 每个有胜负的对决淘汰一队；完整赛事有 N−1 场逻辑对决。平局最多重赛两次，仍平局则以 `ARENA_UNRESOLVED_DRAW` 停止，不产生冠军。

倒地、死亡、离场、移除不计战斗能力；已知待复活单位延迟全灭确认。召唤物可以继续战斗，但不增加初始名册的存活评分。达到模拟时间上限时比较初始成员站立比例，比例相同则平局。原群战仍按其原有绝对存活人数规则结算。

`Tournament` 可配置 `<Cleanup corpses="true" droppedItems="true"/>`，两个开关独立，省略时均为 `false`。战斗期间每 5 个模拟秒（300 ticks）分批检查当前擂台上的尸体和地面物品；只清理死亡至少 5 个模拟秒的尸体，地面物品首次发现后也至少等待 5 个模拟秒，并暂时跳过角色当前任务正在操作的目标，不清角色穿戴、背包或建筑。尸体有原生拒死次数、正在复活或已注册的模组待复活状态时保留。原始结果的 `CleanupTicks` 记录批次时刻，`MinimumClearedCorpseAgeTicks` 和 `MinimumClearedItemGroundTicks` 记录本场最短清理等待时间。清理地面物品也会移除场景预放物资，开启尸体清理会限制依赖未注册尸体机制的模组玩法。

擂台开始前自动移除原主地图所有 Pawn（包括殖民者、动物和容器中的 Pawn），比赛期间每 250 ticks 检查补清。仅针对本次 CLI quicktest 的原主地图，不会清理展示或战斗地图。每场擂台在准备时校准当地中午 12 点，开战后每 5000 ticks（2 个游戏小时）再次校准；只调整全局日历偏移，战斗计时及技能常用的游戏 tick 持续递增，每场结束或异常退出恢复原偏移。使用绝对日历时间的模组效果可能受影响。

左右数据栏的输出和承伤排名在角色姓名后显示原生综合健康度生命条；倒地为橙色，死亡为空条。生命条独立于伤害条，不代表护盾。原始结果 JSON 记录 `BackgroundCleanup`、每场 `ArenaMaintenance`、单位 `HealthPercent` 和展示 `CompositionSwitches`，可直接检查清理数量、正午重设 tick、日历恢复及轮播记录。

胜利质量评分为百分制，默认四项合计 100 分：

| 分项 | 默认分值 | 计算 |
|---|---:|---|
| 己方保全 | 45 | `(初始成员站立数 + 0.25 × 初始成员倒地存活数) / 初始人数` |
| 敌方压制 | 25 | `1 − 敌方初始成员站立数 / 敌方初始人数` |
| 伤害交换 | 20 | `己方整队对敌伤害 / (己方整队对敌伤害 + 己方整队承伤)` |
| 获胜速度 | 10 | `max(0, 1 − 有效模拟秒数 / timeReferenceSeconds)` |

各分项为上表比例乘以对应分值。`survivalWeight`、`suppressionWeight`、`exchangeWeight`、`speedWeight` 可配置为 0～100，**四项必须合计 100**；`timeReferenceSeconds` 默认 180。省略 `Scoring` 使用全部默认值。旧 XML 若显式写出 100/20 权重，需要改为上面的四项配置，或删除 `Scoring` 使用新默认值；不再接受总分超过 100 的旧权重。

保全与压制只按初始名册计数，新增召唤物不直接增减这两项；其伤害仍计入所属整队。承伤包含已记录的敌伤、友伤、自伤和环境伤害，输出不包含友伤、自伤。双方伤害均为零时，交换项取中性比例 0.5，并明确标记。该项按原生 `DamageResult.totalDamageDealt` 口径统计，不是准确血量损失；并未覆盖所有流血、直接修改 Hediff 等效果。常规 `partial` 口径可用；采集异常或测量要求不满足时，保留原始异常及伤害记录，将伤害交换项取中性比例 0.5，标记 `UsedDamageFallback`，继续按实际胜负晋级；保全、压制、速度仍按原始状态计算，降级评分也参与低分加赛排序。

全灭确认获胜时，有效时间截至败方本次连续失能的起始 tick，扣除最后的确认等待；超时判胜使用完整模拟战斗时间。倒计时、展示、现实等待与游戏速度不直接计分。没有准确战后剩余血量数据，因此不以累计承伤推算血量。分数评价本场获胜质量，不能直接当作跨对手的物种强度。平局不计晋级分。

结算面板显示总分、四项分数与有效用时；报告保存算法版本、原始组成、统计口径和未舍入值供 CLI 独立复算。低分加赛采用最新一场胜利的未舍入总分，同分时仍沿用原有稳定排序。评分不改变本场胜负判定。

运行中的擂台禁止 XML 热重载，重新启动赛事才能重新解析 Def。原生暂停和调速可用，长暂停仍受命令总超时约束。界面支持取消，也可通过现有认证本机测试 API 使用 `arena-status` 和 `arena-cancel`。

每届擂台决出冠军、播放完最后一场晋级天梯后，复用赛前介绍页展示本届第一至第五名，按名次从左到右排列，整页显示 **10 秒**。不足五种参赛者时只展示实际名次。冠军、亚军分别为决赛胜者和败者；其余按淘汰阶段由晚到早排名，同轮低分加赛的败者排在该轮普通对局败者之前；同阶段按最近一场胜利的未舍入评分降序，未获胜者取 0 分，同分沿用种子与参赛者 ID 的固定排序。该名次属于本届淘汰赛结果，不新增季军争夺战。

荣誉榜沿用赛前真实角色的肖像和属性快照，属性条仍按全体参赛者的同项最大值缩放，前三名用金、银、铜标识。赛前展示后立即清理预览 Pawn 和地图，仅保留肖像纹理至本届结束；荣誉榜不重新生成 Pawn，也不推进模拟。10 秒从首个实际绘制帧开始计时；结束后或取消时释放缓存。结果 `TopFive` 保存名次、胜场、最近胜利评分、截图和实际展示时长，CLI 独立复算名次并检查满 10 秒且模拟 tick 为 0。`arena-status.presentation` 此时为 `topFive`。

## 魔王轮战赛与极限挑战赛

两种赛制与原单败淘汰赛并列，继续使用 `--mode arena`。`Tournament.format` 省略时仍为原淘汰赛；新赛制允许同一 PawnKind 以不同组合 ID、人数或异种配置分别参赛。

| 配置 | 魔王轮战赛 | 极限挑战赛 |
|---|---|---|
| `Tournament.format` | `bossRoundRobin` | `limitChallenge` |
| `Entrant.role` | 至少一个 `boss` 和一个 `challenger` | 同左 |
| 配对顺序 | 每个对手按名单顺序挑战每个魔王 | 同左，每组独立从初始值开始 |
| 战败后 | 继续下一组 | 挑战者增长后再打同一魔王 |
| 最终成绩 | 每个魔王的胜场、败场、未决、平均胜利评分 | 每组攻克状态、首次获胜值、档数、战斗次数与限制原因 |

每场双方重新满编，魔王不会继承上一场伤势。魔王轮战赛不淘汰任何组合，平均胜利评分只统计该魔王的胜局；零胜场显示“—”，败局不记作 0 分掺入平均值。

极限挑战优先使用人数增长：

```xml
<Roster mode="count" count="1"/>
<Entrants>
  <Entrant id="boss" pawnKind="Wolf_Timber" role="boss" count="2" name="双狼魔王"/>
  <Entrant id="rats" pawnKind="Rat" role="challenger" name="鼠群"/>
</Entrants>
<Growth target="count" multiplier="1.5" maxValue="100"/>
```

以上是 `Tournament format="limitChallenge"` 内的片段，完整赛事还需保留 `testDefName`、`Scene` 等原有字段。`target` 默认 `count`；每次战败后 `下一档人数 = ceil(本档人数 × multiplier)`，例如 10 → 15 → 23 → 35。倍率必须大于 1，人数上限须为 1～200 的整数，且不小于挑战者初始人数。固定混编按冻结成员顺序循环扩编，保留基准成员的兵种、异种和配装，各成员副本数量最多相差一份；基准名单和历史对局不变。

也支持 CP 配兵预算增长：将 Roster 改为 `mode="combatPower" budget="100"`，挑战者可覆盖 `budget`，并使用 `<Growth target="budget" multiplier="2" maxValue="1600"/>`。预算每次直接相乘，允许小数；上限不超过 10000000。预算用于原有配兵，不修改角色伤害或生命，增加预算也不保证每档人数或兵种相同。固定成员数量的混编不能使用预算增长，应使用人数增长或改成加权混编。

加法增长使用 `mode="add"` 和 `increment`，不填写 `multiplier`。人数增量须为正整数，CP 预算增量可为正小数：

```xml
<!-- 每次失败增加 2 人：2 → 4 → 6 → 8 -->
<Growth target="count" mode="add" increment="2" maxValue="8"/>
<!-- 每次失败增加 150 CP 预算：100 → 250 → 400 -->
<Growth target="budget" mode="add" increment="150" maxValue="400"/>
```

省略 `mode` 仍使用乘法增长，兼容已有 XML。两种增长方式采用相同的胜负、平局、逐魔王重置与上限规则。完整加法示例为同一配置文件中的 `Arena_LimitAddCount` 和 `Arena_LimitAddCP`。

等于最大值允许继续挑战，下一档超过最大值时该组结束。达到 200 人或地图无法容纳下一档时，记录限制原因；初始阵容就不合法、生成故障、清理失败或现实超时仍报运行错误。每组获胜立即停止，结果中的“首次获胜人数 / 预算”是按配置增长规则逐档尝试的结果，不表示理论最低门槛。

平局在原档位按 `TieBreak.maxReplays` 重赛，不增长。重赛耗尽后该组记录 `unresolvedDraw`，继续其他组；运行完整赛程后可以正常完成并自动退出，最终成绩会单列未决。原淘汰赛仍沿用平局耗尽后停止整届赛事的行为。

新赛制赛后页使用双方大肖像、胜者高亮与胜利评分，显示本档值、下一步安排和每页 4 条的图标交锋记录；最终页为带肖像的成绩卡，每页最多 8 项，自动逐页展示，每页至少 10 秒。轮战展示所有魔王，极限挑战展示所有挑战组。混编肖像沿用初始巡礼代表成员，人数和实际总 CP 使用本场名册，悬停紧凑成绩卡可查看更多数据。

开赛巡礼按“本届魔王”和“本届挑战者”分别分页，手动配置的混合巡礼页也会自动拆开，保留各页时长和说明。单场对阵预览继续同时展示本场双方。

三种赛制都先显示面向观众的比赛规则页，再显示参赛者巡礼。规则页首次绘制后才开始准备本届名册、装备与预览角色，准备期间保持规则页；从首次绘制起至少展示现实 5 秒。准备不足 5 秒时等满 5 秒，超过 5 秒时准备完成便直接进入巡礼，不额外再等 5 秒。结果 `Rules` 保存最低时长、准备耗时、实际停留时长和原始规则截图；预览角色的原生初始化 tick 包含在准备耗时中。

默认空白测试场地隔离原生世界地块变体及生态区附加生成步骤，避免铺平后又生成废墟；不修改原世界地块。每场结果的 `StudioIsolation` 记录屏蔽步骤、生成后残留物和非土壤格数，发现残留会直接报错。隔离只作用于工具自建的默认空白测试地图。

完整样例为 `AutoTest.Core.Tests/Fixtures/ArenaBossChallenges.autotest.xml`：

```powershell
dotnet run --project RimDevTools -c Release -- test run --deploy --mode arena --scenario-path AutoTest.Core.Tests/Fixtures/ArenaBossChallenges.autotest.xml --test-name Arena_BossRoundRobin
dotnet run --project RimDevTools -c Release -- test run --deploy --mode arena --scenario-path AutoTest.Core.Tests/Fixtures/ArenaBossChallenges.autotest.xml --test-name Arena_LimitCount
dotnet run --project RimDevTools -c Release -- test run --deploy --mode arena --scenario-path AutoTest.Core.Tests/Fixtures/ArenaBossChallenges.autotest.xml --test-name Arena_LimitCP
```

同文件的 `Arena_LimitFixedSquad` 验证固定佣兵混编人数增长。可重复 `--test-name` 连续运行多个赛事。默认总超时按全部组、潜在增长档位、重赛和最终分页估算，也可用现有 `--timeout-seconds` 指定运行截止时间；超时不计为比赛战败。

`Arena_BossGallery` 为 10 位魔王的界面回归样例，覆盖 720p、长组合名、巡礼与最终成绩分页；`Arena_LimitAddCount` 使用固定佣兵混编验证人数加法，`Arena_LimitAddCP` 验证 CP 预算加法。

原始 JSON 的赛事结果增加 `Challenges` 和 `Summary`，每个档位对局记录 `PairId`、`Level`、`ChallengeValue` 和 `ResolvedRosters`。CLI 独立复核配对、逐档值、实际名册、评分、平均值和展示；仍只读取工具原始产物。

## 群战预算

原群战入口和数量写法继续可用，只增加可选 `PawnGroup.combatPowerBudget`，与显式 `count` 互斥；两者都未写仍为一人。数量群战不新增战力要求，可以在不同编组混用数量和预算；预算不要求双方相等。

```xml
<PawnGroup id="wolves" team="A" combatPowerBudget="3000"
           originX="20" originZ="30" formation="grid" columns="10" spacing="2">
  <basePawnKind>Wolf_Timber</basePawnKind>
</PawnGroup>
```

完整样例为 `AutoTest.Core.Tests/Fixtures/GroupCombatBudget.autotest.xml`，入口名 `GroupCombat_Budget3000`。所有预算在生成单位前解析，重新验证阵型边界；结果包含已加载战力、人数、实际投入、剩余预算及模板原文。

## 结果与检查

每届擂台在第一场对决前自动展示全部参赛者，每页最多 5 种，默认停留 5 秒，少于 5 种居中展示。单体 CP、血量、原生近战加权平均伤害、攻击速度（平均冷却的倒数）、利器护甲和钝器护甲同时显示具体数值及进度条；血量是成年展示单位全身各部位最大生命值之和，伤害单位为每击，攻速为次/秒。护甲采用原版装备页的总体护甲口径，从真实 Pawn 的天然护甲、穿戴衣甲的实际属性和部位覆盖率汇总，包含品质、材质、基因和当前 Hediff 对属性的影响，不是伤害减免率，也不包含护盾容量。同项进度条以本届所有参赛者的最高值为满格，跨页保持同一尺度。

展示前在独立临时地图按原生 PawnKind 生成、实际放置成年角色和原生装备，运行 300 个原生 tick 后读取属性；这段初始化会推进游戏时间，不计入页面停留时间和战斗时间。这样允许 SpawnSetup、Comp 和 Hediff 初始化生效，不猜测尚未触发的战斗条件效果。人类图像使用该真实 Pawn 的原生渲染器，包含头发、衣服和头饰。读取完成后暂停游戏进行分页展示，展示结束或取消时销毁预览 Pawn、渲染纹理和临时地图。结果 `Showcase` 记录初始化 tick 数、清理状态、读取时穿戴/武器、生成后与读取时的 Hediff，以及每页顺序、文案、时长和原始截图。结束后进入原有的 3 秒开战倒计时。

工具最终 JSON 的 `result.artifacts` 或 `error.details.artifacts` 指向原始产物。擂台在该位置生成 `arena_<runId>_Result.json`、`_Report.md` 和 `_Screenshots`；每次尝试保留 `bracket`（沿用历史文件名，内容为赛前阵容预览）、`initial`、`final` 三张战斗截图，并在赛后动画定格后生成独立的 `postbattle` 天梯截图，路径记录于对局的 `PostBattleScreenshots`。每个完成赛事另有冠军截图。失败保留已经完成的对局，执行或清理错误停止晋级。结果记录加载模组、输入哈希、战力快照、尝试种子、伤害账本、胜负与评分。

CLI 独立重建配对、资格赛、低分加赛、淘汰关系和唯一冠军，并核对人数、预算、分数、账本及截图。伤害测量的归属限制单独记录，不私自改变胜负。不要另存或复制测试证据，直接读取工具原始路径。

完整擂台赛程正常结束并展示荣誉榜后，先写完产物，再由游戏正常退出，CLI 等待退出并恢复配置事务。报错、未决平局、心跳超时、等待超时或取消命令不自动关闭游戏；不能继续的执行或清理问题仅停止赛程。CLI 返回 `gameStillRunning`、`gameProcessId` 和 `transactionRetained`；游戏仍运行时保留配置事务，待手动关闭游戏后执行 `test cleanup`，或由下一次测试启动时恢复。正常赛程若未在 10 秒内退出，CLI 报 `ARENA_EXIT_TIMEOUT` 并保留游戏，不强杀进程。

源码检查入口：

```powershell
dotnet run --project AutoTest.Core.Tests -c Release
dotnet run --project RimDevTools.Tests -c Release -- --arena-result
dotnet run --project RimDevTools.Tests -c Release -- --groupcombat-result
```

`ArenaAcceptance.autotest.xml` 包含数量双队、固定人数五队，以及五队与六队统一预算赛事；`ArenaLifecycle.autotest.xml` 包含原生肉兽分裂增援和拒死者复活场景，需要启用 Anomaly。`ArenaBoundary.autotest.xml` 包含预期失败的平局重赛、预算不足、单场现实超时、十队天梯展示场景，以及正向的 1280×720 输出场景。十队展示场景有意设置极短时限，以未决平局结束，仅用于检查大名单天梯。不要把边界文件整体当成必须全通过的正向验收。
