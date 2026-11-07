# RimDevTools 擂台测试与 combatPower 配兵

首版支持 RimWorld 1.7。擂台使用独立 `arena` 入口，普通 `auto` 不执行擂台；`groupcombat` 继续运行原有群战。袭击点、战力均指游戏加载继承及模组补丁后的 `PawnKindDef.combatPower`。

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
    <Scoring survivalWeight="100" speedWeight="20" timeReferenceSeconds="180"/>
    <TieBreak onDraw="replay" maxReplays="2"/>
    <Presentation bracketSeconds="5" settlementSeconds="3"/>
    <Output width="1920" height="1080"/>
  </Tournament>
</ArenaSuite>
```

固定数量用 `<Roster mode="count" count="20"/>`。数量模式可以在单个 `Entrant` 上写 `count="10"` 覆盖人数；预算模式禁止参赛者单独覆盖数量或预算。参赛者为 2～64 种互不重复的 PawnKind，ID 也须唯一。两种擂台模式都只允许具有有限正战力的单位。首版继承原生 PawnKind 配装。

配兵人数取预算能容纳的最大整数：`floor(budget/combatPower)`。每方最多 100 人，单场最多 300 人；预算不足一人、非法战力、溢出或超限会停止，不会调整预算或截断人数。3000 战力可以配 40 个 75CP 森林狼，或 35 个 85CP 雇佣枪手；后者实际投入 2975，剩余 25。

Scene 沿用群战的严格场景配置。其他字段范围和完整默认值见[实施计划](全新xml测试实现文档/擂台测试与combatPower配兵实施计划.md)。`Output` 指输出截图分辨率，宽高须同时提供，范围 800～3840 × 600～2160；不改变实时游戏窗口。XML 拒绝未知字段、DTD 与外部实体。

## 赛制与胜负

每场开战前暂停并显示原有阵容与倒计时，再显示初始战场；结算卡片以大号金色数字显示胜利分数，并显示胜者的大图标或人类头像。结算后新增 5.5 秒的赛后天梯：依次突出 A 队、B 队，再播放胜者晋级动画；平局显示重赛状态。参赛对象按 PawnKind 固定配色，晋级节点继承胜者颜色。每次对决重建地图、临时敌对阵营和满编队伍。赛后天梯支持自动定位本场、全局总览和滚动，包含全部参赛者、已定对决和后续待定节点。群战与擂台期间仅隐藏参战单位模型下方的地图名称标签，排行榜、结算页与其他界面仍保留名称。

群战和擂台的初始阵型均优先沿地图 Z 轴纵向排列，再沿 X 轴增加下一列。`line` 为单条纵列；`grid` 沿用 `columns` 配置值作为每条纵列的容量。擂台每列最多 10 个单位，并按真实占地居中。边界检查、群战换边和实际生成共用同一套坐标计算。普通外部场景仍采用原有横向排布。

- 常规轮次依次选择初始人数差最小的两队，同差值用固定种子排序。A 对 B 与 B 对 A 只是一场逻辑对决。
- 初始参赛人数为奇数，最近人数的一对先打资格赛，再由偶数队伍进入普通轮次。
- 一轮完成后，胜者为大于一的奇数时，最近一次胜利分数最低的两队加赛。胜者更新最近分数并晋级。只剩一队即为冠军。
- 每个有胜负的对决淘汰一队；完整赛事有 N−1 场逻辑对决。平局最多重赛两次，仍平局则以 `ARENA_UNRESOLVED_DRAW` 停止，不产生冠军。

倒地、死亡、离场、移除不计战斗能力；已知待复活单位延迟全灭确认。召唤物可以继续战斗，但不增加初始名册的存活评分。达到模拟时间上限时比较初始成员站立比例，比例相同则平局。原群战仍按其原有绝对存活人数规则结算。

默认胜利分数为 `100 × 初始成员站立比例 + 20 × max(0, 1 − 模拟秒数/180)`。时间包含全灭确认，不包含展示。20 人剩 15 人、45 模拟秒获胜得 90 分。分数按未舍入值排序，报告保存组成供 CLI 复算。

运行中的擂台禁止 XML 热重载，重新启动赛事才能重新解析 Def。原生暂停和调速可用，长暂停仍受命令总超时约束。界面支持取消，也可通过现有认证本机测试 API 使用 `arena-status` 和 `arena-cancel`。

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

工具最终 JSON 的 `result.artifacts` 或 `error.details.artifacts` 指向原始产物。擂台在该位置生成 `arena_<runId>_Result.json`、`_Report.md` 和 `_Screenshots`；每次尝试保留 `bracket`（沿用历史文件名，内容为赛前阵容预览）、`initial`、`final` 三张战斗截图，并在赛后动画定格后生成独立的 `postbattle` 天梯截图，路径记录于对局的 `PostBattleScreenshots`。每个完成赛事另有冠军截图。失败保留已经完成的对局，执行或清理错误停止晋级。结果记录加载模组、输入哈希、战力快照、尝试种子、伤害账本、胜负与评分。

CLI 独立重建配对、资格赛、低分加赛、淘汰关系和唯一冠军，并核对人数、预算、分数、账本及截图。伤害测量的归属限制单独记录，不私自改变胜负。不要另存或复制测试证据，直接读取工具原始路径。

源码检查入口：

```powershell
dotnet run --project AutoTest.Core.Tests -c Release
dotnet run --project RimDevTools.Tests -c Release -- --arena-result
dotnet run --project RimDevTools.Tests -c Release -- --groupcombat-result
```

`ArenaAcceptance.autotest.xml` 包含数量双队、固定人数五队，以及五队与六队统一预算赛事；`ArenaLifecycle.autotest.xml` 包含原生肉兽分裂增援和拒死者复活场景，需要启用 Anomaly。`ArenaBoundary.autotest.xml` 包含预期失败的平局重赛、预算不足、单场现实超时、十队天梯展示场景，以及正向的 1280×720 输出场景。十队展示场景有意设置极短时限，以未决平局结束，仅用于检查大名单天梯。不要把边界文件整体当成必须全通过的正向验收。
