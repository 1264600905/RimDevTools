# RimDevTools 第三批使用说明

适用后端：RimWorld 1.7。手动选择弹窗的交互验收按用户 2026-11-07 的最新要求交由用户后续进行；本文说明已实现的入口和配置，不代表所有交互状态已完成自动验收。

## 角色暂存

游戏内先选中一个活着的 Pawn，再打开开发者动作中的 `RimDevTools → 暂存测试角色`。

角色数据写到最近自动测试项目的 `.tools/ProjectData/temporary-test-pawn.xml`。最近项目由 CLI 在正式启动自动测试时记录；如果没有有效记录，入口要求明确填写项目根目录。普通测试弹窗中也可切换输入项目，不从部署目录或日志位置猜测项目。

角色文件是版本 1 的 `TemporaryTestPawn` 包装与共享 `PawnTemplate`，包含模板 SHA-256、录入时间、源对象身份、已录入字段和未录入范围。支持部分定义的独立 `PawnTemplate`；未指定字段继续使用原生 PawnKind 生成规则。包装文件的模板被修改而哈希没有更新时会拒绝载入，不继续使用损坏数据。

录入范围：

- PawnKind、race、性别、生理和实际年龄。
- 发型、头型、体型、发色和肤色；Gene/Xenotype。
- 服装及主武器，材料、品质和启用的颜色覆盖。
- 特性、未禁用的工作技能及热情、原生 Ability。
- Hediff 的 Def、严重度、可明确定位的身体部位和消失倒计时。
- `Gene_Resource` 的当前值、原生灵能专注度及已注册的 Axolotl 螈气资源。

不会导出完整游戏对象图。名字、背景、关系、工作、背包物品、技能冷却/次数、胡须和纹身、未注册的模组私有组件状态等会列入未录入范围。相同 Def 与部位上多份不能精确表达的 Hediff 也会注明，不能把这个文件当作完整存档克隆。

捕获只读源角色。执行测试时创建新 Pawn；一次提交会固定角色数据和哈希，后续更新文件不会改动已经提交的批次。替换采用同目录临时文件和原子替换，写入失败保留旧文件。

## 普通测试弹窗

开发者动作 `RimDevTools → 自动测试` 打开专用窗口。顶层新增入口只有这两个。

左侧选择物品、建筑、PawnKind、技能、配方、健康、地形、基因、异种、发型、头型和体型等目标，也列出当前地图可对应到这些定义的实例。可搜索名称、DefName 和模组，按模组、类型、物品分类、第三方或已选状态筛选；全选只影响当前筛选。选择不会因滚动或换筛选丢失。

右侧只有“战斗测试、UI 测试、三视图测试”三个大类；技能在战斗下，植物/建筑/地形等视图也在三视图下。首次无记录时类别全选，以后读取上次实际提交的目标和类别。父类选择影响其全部子类，部分选择有标记；类别 ID 的提示用于对应程序类型。

底部预览适用组合和不适用原因。开始后冻结目标、统一类别和角色数据，保存选择并关窗；任务串行执行，普通用例失败不会变成通过，也不要求确认后才能继续。可重新打开查看上次选择、提交后续批次或取消。未开始就关闭的草稿不覆盖上次选择，失效目标不会被静默替换。

入口只使用内置普通测试能力，不读取作者编写的测试 XML、上次 CLI 场景缓存、Coverage 补充/替代规则，也没有群战选择。自动暂存的角色 XML 是角色数据，不是测试脚本。

需要展示者、射手、操作员、工作者或施法角色时优先使用暂存数据；直接测试某个动物/机械族/种族或生命阶段时仍保留被测目标。具体被测装备、外观和变体按该内置测试准备规则处理。整套配装展示保留完整配装；直接选择 Pawn 实例时使用该实例冻结的角色数据。Thing 实例冻结 Def、材料、品质、颜色、耐久和堆叠量，不克隆任意私有组件。

不可销毁而无法保证清理的装备不运行内置服装展示/战斗探针，明确记录不适用。

## 手动结果

首次正式提交通过 RimDevTools 共用分配器创建 `.tools/log/[年月, 时分]/...` 原始产物路径。一次游戏进程对应一个 `GameLaunchId` 和 `ManualSessionId`，每次提交另有 `InvocationId`。同次启动后续批次追加到同一份手动汇总；切项目只改变输入项目及角色文件，不改变已经建立的输出会话。

退出并重启游戏后新建会话，旧结果保留；回主菜单或切存档不重置进程会话。选择历史与结果会话分别保存。自动回归的保留清理不会删除手动会话。

机器 JSON、Markdown 和图像保留全部手动结果，包括成功、失败和不适用；图像名包含执行身份，重复测试同一物品不覆盖之前的图。角色数据、实际输入项目和完整执行证据分别记录。手动执行不会开启 `autoTestEnabled`、复用自动 RunId 或发出自动完成信号。

## 自定义视图 XML

支持 `HumanoidThreeView`、`AnimalThreeView`、`MechanoidThreeView`、`AlienThreeView`、`ApparelThreeView`、`WeaponThreeView`、`PlantGrowthView`、`BuildingView`、`TerrainPatchView`、`PawnLifeStageThreeView`、`HairThreeView`、`HeadTypeThreeView`、`BodyTypeThreeView`、`GeneThreeView`、`XenotypeThreeView` 和 `LoadoutView`。

视图使用原有三视图摄影区、姿态和相机服务；`Scene type="studio"`，省略 type 时按该类型默认。优先使用主地图可安全恢复的摄影区；没有足够空位时创建临时普通主地图并在结束后丢弃。视图不使用口袋地图，也不接受口袋地图 `generatorDef`。布局、天气、温度、屋顶和角色配置继续使用共享场景字段。

```xml
<Case id="loadout" caseType="LoadoutView" timeoutRealSeconds="90">
  <testDefName>Example_CompleteLoadout</testDefName>
  <Scene type="studio"/>
  <Actors>
    <Pawn id="p" x="25" z="25">
      <basePawnKind>Colonist</basePawnKind>
      <Age biologicalYears="25" chronologicalYears="25"/>
      <Equipment><SetPrimary def="Gun_AssaultRifle" quality="Excellent"/></Equipment>
    </Pawn>
  </Actors>
  <Steps>
    <Require id="ready"><Equipped actor="p" def="Gun_AssaultRifle"/></Require>
    <View id="outfit" actor="p" directions="South,East,North,West"
          pose="weaponReady" loadout="preserve" cameraSize="11" stableFrames="5"/>
  </Steps>
  <Success><StepSucceeded ref="outfit"/></Success>
</Case>
```

`View` 的方向为 1—4 个不重复的原生方向；角色缺省为正、侧、背，静态视图缺省单张。姿态为 `standing`、`weaponReady` 或 `auto`。摄影站姿只影响展示，不清除真实的倒地、年龄和健康状态。持武器姿态要求具备相应实际装备及站立条件。不可旋转静态对象使用单张；单格门保留原生根据墙体连接确定朝向的行为。

`loadout="preserve"` 保留整套配装；`isolate` 必须明确 `targetDef`，用于单件隔离。`cameraSize` 为 11—100；更近的原生相机根尺寸可能落入 1.7 的 Pawn 绘制层下方，配置会拒绝。`stableFrames` 为 3—120，并沿用拍摄前实际时间的稳定窗口。

同一 Pawn 逐向拍摄，核对身份、外观和装备未在转向中改变；默认输出每方向原图及横排合成图，并记录装备清单。`composite="false"` 可只保留原图。PNG 完整写入且编码器释放后才合成。画面之外、迷雾中或地形边界被裁切的取景不会只因生成文件就通过。

新增配置/步骤/判据：

| 内容 | 字段 |
| --- | --- |
| 年龄 | `Age biologicalYears / chronologicalYears` |
| 物品样式 | 生成项的 `quality / color / rotation`；服装/主武器配置的 `color` |
| 植物 | Thing 的 `growth`；步骤 `SetPlantGrowth actor value`；`PlantGrowth actor min max` |
| 外观 | `PawnAppearance actor property value`，property 支持 kind、race、gender、hair、bodyType、headType、hairColor、skinColor、lifeStage、biologicalYears、rotation |
| 基因与异种 | `HasGene actor def active xenogene`；`HasXenotype actor def` |
| 地形与样式 | `TerrainState actor def` 要求对应 TerrainArea 全区域一致；`ThingStyle actor stuff quality color rotation` |
| 资源 | Pawn 的 `Resources/GeneResource def value`、`Psyfocus value`、`AxolotlQi value`，均经过实际原生值核对 |

可运行的逐类例子见 `AutoTest.Core.Tests/Fixtures/ScenarioBatch3Views.example.xml`，受控失败见同目录 `ScenarioBatch3ViewsNegative.example.xml`。文件是源码测试输入，不部署进被测模组。

```text
dotnet .\RimDevTools\bin\Release\net10.0\RimDevTools.dll test run --deploy --mode auto --scenario-only --scenario-path "AutoTest.Core.Tests\Fixtures\ScenarioBatch3Views.example.xml" --target-package-id ludeon.rimworld --no-isolated-desktop --timeout-seconds 600
```

内置手动整套配装也使用同一套场景执行能力，测试步骤由工具固定生成；没有载入用户测试脚本或替代规则的路径。直接选择某个 PawnKind 时保留这个目标，暂存角色基底相同才直接套用整套角色模板；选择当前 Pawn 实例则使用该实例的数据。
