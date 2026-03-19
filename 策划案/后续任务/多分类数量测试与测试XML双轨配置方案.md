# 多分类数量测试与测试XML双轨配置方案

## 目标

当前自动测试链路在快速测试场景下，主要依赖以下几种控制方式：

- `scope` 控制大类范围，例如 `all`、`threeviews`、`combatpawns`
- `autoTestBatchSize` 控制总数量上限
- XML 驱动模式下通过 `LogSyncAutoTestSpecDef` 按 `category/defName` 定向筛选

这套能力已经能完成基础自动化，但不适合现在的真实测试需求。当前缺口主要有：

- 快速测试时，无法一次性精确指定多个测试分类及各自数量
- 快速测试时，无法对“某个分类是否截图”做单独控制
- 现有批量限制是“全局总量限制”，不是“分类内数量限制”
- 现有 XML 更偏向单次定向筛选，不适合长期承载“数量测试策略”
- 现有 XML 对黑名单/白名单支持不足，无法稳定表达“按分类跑 + 局部强制加入/排除”的组合逻辑

本方案目标是建立一套双轨配置体系：

- 轨道 A：快速测试/脚本输入侧，允许用户通过类似 JSON 的结构输入多个分类策略
- 轨道 B：原模组内自带测试 XML，允许长期沉淀可复用的测试策略，兼容数量测试和 defName 白黑名单


## 用户需求拆解

本次要满足的能力分成两组。

### 1. 快速测试参数组

希望 `规范化测试/run_test.py` 在快速测试时支持：

- 多次输入参数，然后组合成一个 JSON 风格的配置
- 指定哪些分类要执行
- 指定每个分类的测试数量
- 指定每个分类是否截图

这组需求的重点是“临时测试编排”，强调输入灵活、便于命令行快速调整。

### 2. 测试 XML 配置组

希望原模组下自带的测试 XML 能从两种角度配置：

- 数量测试角度：
  - 指定测试分类
  - 指定每个分类数量
  - 指定每个分类是否截图
- defName 策略角度：
  - 黑名单：即使命中选中分类，也必须排除
  - 白名单：即使未选中分类，或即使被黑名单命中，也要默认加入
  - 白名单项允许单独控制是否截图

这组需求的重点是“长期可维护配置”，强调可复用、可沉淀、适合固定测试模板。


## 设计原则

### 1. 先保留现有兼容性

以下旧能力必须继续可用：

- 旧的 `scope` 行为
- 旧的 `autoTestBatchSize` 行为
- 旧的 `LogSyncAutoTestSpecDef` 的 `scope/categories/defNames/captureScreenshots`
- 旧的全量测试、批量测试、点名测试命令

也就是说，新能力是“增量扩展”，不是重做现有自动测试系统。

### 2. 快速测试与 XML 配置职责分离

建议明确两类入口的责任：

- `run_test.py`：负责把用户输入翻译成模组可读配置
- 模组代码：负责统一解析配置、筛选目标、应用数量限制、应用截图开关
- XML：负责长期保存、复用、复合策略表达

换句话说，`run_test.py` 不应该直接实现筛选逻辑，而应该只是“配置生成与下发器”。

### 3. 数量限制必须按分类生效，而不是全局混剪

例如：

```json
{
  "categories": {
    "ApparelThreeView": { "limit": 10, "capture": true },
    "WeaponThreeView": { "limit": 5, "capture": false },
    "AlienCombat": { "limit": 3, "capture": true }
  }
}
```

预期语义应为：

- `ApparelThreeView` 最多取 10 个
- `WeaponThreeView` 最多取 5 个
- `AlienCombat` 最多取 3 个

而不是把这三类混在一起再做一次总量裁剪。

### 4. 白名单优先级最高

对于白名单/黑名单，建议统一采用以下优先级：

- 白名单强制加入优先级最高
- 黑名单次之
- 分类筛选再次之
- 全局 scope 最后作为粗粒度入口

换成可执行规则就是：

1. 如果命中白名单，默认加入
2. 如果未命中白名单但命中黑名单，则排除
3. 否则按分类策略是否启用决定是否参与

其中“白名单即使被黑名单命中也要加入”，按你的要求，白名单优先于黑名单。


## 建议的数据模型

### 1. 脚本输入侧的数据模型

建议给 `run_test.py` 新增一个“JSON 风格测试计划”参数，例如：

- `--auto-test-plan-json <json-string>`
- 或 `--auto-test-plan-file <path>`

推荐两者都支持，但优先实现 `--auto-test-plan-json`，后续再补 `--auto-test-plan-file`。

建议结构如下：

```json
{
  "categories": {
    "Building": { "enabled": true, "limit": 20, "capture": false },
    "Item": { "enabled": true, "limit": 30, "capture": false },
    "Recipe": { "enabled": true, "limit": 10, "capture": false },
    "Hediff": { "enabled": false, "limit": 0, "capture": false },
    "Turret": { "enabled": true, "limit": 5, "capture": true },
    "RangedWeapon": { "enabled": true, "limit": 5, "capture": true },
    "MeleeWeapon": { "enabled": false, "limit": 0, "capture": false },
    "AlienThreeView": { "enabled": true, "limit": 20, "capture": true },
    "ApparelThreeView": { "enabled": true, "limit": 40, "capture": true },
    "WeaponThreeView": { "enabled": true, "limit": 10, "capture": true },
    "AlienCombat": { "enabled": true, "limit": 10, "capture": true },
    "ApparelCombat": { "enabled": false, "limit": 0, "capture": false }
  },
  "whitelist": [
    { "defName": "Axolotl_AfterglowMarriageRobe", "capture": true },
    { "defName": "Axolotl_ElectromagneticLauncher", "capture": false }
  ],
  "blacklist": [
    "Axolotl_DebugOnlyItem",
    "Axolotl_ProblematicDummy"
  ]
}
```

### 2. 模组设置侧的数据模型

建议在 `LogSyncSettings` 中新增一个原始 JSON 字符串字段，例如：

- `autoTestCategoryPlanJson`

它用于承接脚本传入的计划文本，并在模组内部解析为内存对象。

建议新增的内存模型：

- `AutoTestCategoryPlan`
- `AutoTestCategoryRule`
- `AutoTestWhitelistRule`

示意结构：

```csharp
AutoTestCategoryPlan
- Dictionary<string, AutoTestCategoryRule> Categories
- List<AutoTestWhitelistRule> Whitelist
- HashSet<string> Blacklist

AutoTestCategoryRule
- bool Enabled
- int? Limit
- bool? Capture

AutoTestWhitelistRule
- string DefName
- bool? Capture
```

### 3. XML 侧的数据模型

不建议继续把所有新能力硬塞进现有 `LogSyncAutoTestSpecDef`，因为它目前是“单条规则筛选器”，不适合承载复杂的整体测试计划。

建议新增一个新的 Def 类型，专门表示“测试计划”，例如：

- `LogSyncAutoTestPlanDef`

推荐结构：

```xml
<Defs>
  <LogSyncMod.LogSyncAutoTestPlanDef>
    <defName>Axolotl_DefaultPlan</defName>
    <enabled>true</enabled>
    <categories>
      <li>
        <category>ApparelThreeView</category>
        <enabled>true</enabled>
        <limit>40</limit>
        <captureScreenshots>true</captureScreenshots>
      </li>
      <li>
        <category>AlienCombat</category>
        <enabled>true</enabled>
        <limit>10</limit>
        <captureScreenshots>true</captureScreenshots>
      </li>
    </categories>
    <blacklist>
      <li>Axolotl_DebugOnlyItem</li>
    </blacklist>
    <whitelist>
      <li>
        <defName>Axolotl_AfterglowMarriageRobe</defName>
        <captureScreenshots>true</captureScreenshots>
      </li>
      <li>
        <defName>Axolotl_ElectromagneticLauncher</defName>
        <captureScreenshots>false</captureScreenshots>
      </li>
    </whitelist>
  </LogSyncMod.LogSyncAutoTestPlanDef>
</Defs>
```

这样职责更清晰：

- `LogSyncAutoTestSpecDef`：保留为旧式筛选规则、兼容 XML-driven 测试
- `LogSyncAutoTestPlanDef`：新增为复杂测试编排计划


## 建议的行为规则

### 1. 快速测试计划的生效范围

当存在 `autoTestCategoryPlanJson` 时：

- 优先使用其中的分类规则进行筛选与限量
- `scope` 仍可作为总入口，但不再是唯一筛选方式
- `autoTestBatchSize` 退化为兜底总量限制，或者在启用计划时直接忽略

建议优先级如下：

1. 白名单强制加入
2. 黑名单排除
3. 分类规则判定是否启用
4. 分类规则判定是否达到该分类数量上限
5. 若无计划规则，则退回现有 `scope + batch size`

### 2. 截图开关规则

建议截图开关优先级如下：

1. 白名单项上的 `capture` 显式设置
2. 分类规则上的 `capture`
3. 旧的 XML spec 中的 `captureScreenshots`
4. 全局 `autoTestCaptureScreenshots`

这样可以保证“局部 override”能力足够强。

### 3. 白名单/黑名单的匹配对象

建议统一以最终结果的 `defName` 为准，而不是 manifest key。

即：

- `ApparelThreeView` 匹配服装 `ThingDef.defName`
- `WeaponThreeView` 匹配武器 `ThingDef.defName`
- `AlienThreeView` / `AnimalThreeView` 匹配 `PawnKindDef.defName`
- `Item/Building/Recipe/Hediff` 匹配对应 Def 的 `defName`

理由是：

- 便于用户理解
- 与现有命令行 `defName` 语义一致
- 避免把 `@pawnKind@bodyType` 这种内部组合键暴露给日常测试配置


## 代码改造计划

### 第一阶段：只做计划通道，不破坏旧流程

目标：让脚本和模组都能识别新的 JSON 风格测试计划，但先不删除旧能力。

涉及内容：

- `run_test.py`
  - 新增 `--auto-test-plan-json`
  - 解析 JSON 并写入模组设置文件的 `autoTestCategoryPlanJson`
- `LogSyncSettings`
  - 新增 `autoTestCategoryPlanJson`
  - 保存/读取该字段
- 模组新增计划解析器
  - 从 JSON 解析为内存对象
  - 解析失败时输出明确日志并回退旧逻辑

### 第二阶段：接入分类数量与截图开关

目标：让计划真正影响筛选、数量、截图。

涉及内容：

- `AutoTestRunner`
  - 在 `Initialize()` 后、`BuildPlannedManifest()` 前应用分类计划
  - 替换或补充现有 `ApplyBatchLimit()`
- 筛选逻辑
  - 对 `pendingItems`
  - 对 `pendingTurretDefs`
  - 对 `pendingRangedWeaponDefs`
  - 对 `pendingMeleeWeaponDefs`
  - 对 `pendingThreeViewTargets`
  - 对 `pendingCombatPawnTargets`
  分别进行按分类限量
- 截图逻辑
  - 新增“按 case/category/defName 决定截图”统一入口
  - 替换现有到处直接调用 `ShouldCaptureScreenshots(...)` 的地方

### 第三阶段：新增 XML 计划 Def

目标：让模组原生支持长期可复用的测试计划。

涉及内容：

- 新增 `LogSyncAutoTestPlanDef`
- 支持从目标 mod 中读取启用的 plan def
- 允许与脚本计划合并，或者规定“脚本计划覆盖 XML 计划”

建议优先级：

- 脚本传入计划 > XML 计划 > 旧设置

### 第四阶段：白名单/黑名单与 override

目标：把复杂选择权补齐。

涉及内容：

- defName 黑名单排除
- defName 白名单强制加入
- 白名单局部截图开关 override
- 日志中输出“加入/排除原因”便于调试


## 推荐实现顺序

建议实际开发顺序如下：

1. 先做 `run_test.py` 的 `--auto-test-plan-json`
2. 再做 `LogSyncSettings.autoTestCategoryPlanJson`
3. 再做模组内 JSON 解析与校验
4. 再做按分类数量裁剪
5. 再做按分类截图开关
6. 再做白名单/黑名单
7. 最后再做 XML 计划 Def

这样做的好处是：

- 每一步都能单独验证
- 快速测试需求能最先落地
- XML 长期配置可放在第二轮完成，不阻塞当前测试效率提升


## 日志与可观测性要求

为了避免以后再出现“命令执行了但语义不对”的问题，建议新增以下日志：

- 计划加载成功日志
- 计划解析失败日志
- 每个分类实际保留数量日志
- 白名单强制加入日志
- 黑名单排除日志
- 每个 case 的截图决策日志（来自分类规则/白名单/默认值）

建议日志格式示例：

- `AutoTest plan loaded: categories=5 whitelist=3 blacklist=2 source=SettingsJson`
- `AutoTest plan category kept: ApparelThreeView kept=40 total=70 capture=True`
- `AutoTest plan whitelist forced include: def=Axolotl_AfterglowMarriageRobe capture=True`
- `AutoTest plan blacklist excluded: def=Axolotl_DebugOnlyItem`
- `AutoTest capture rule resolved: case=ApparelThreeView def=Axolotl_CloudRobe capture=True source=CategoryPlan`


## 验证方案

### 验证 1：多分类数量控制

输入：

```json
{
  "categories": {
    "ApparelThreeView": { "enabled": true, "limit": 10, "capture": true },
    "WeaponThreeView": { "enabled": true, "limit": 5, "capture": false }
  }
}
```

预期：

- 只执行这两个分类
- `ApparelThreeView=10`
- `WeaponThreeView=5`
- apparel 截图生成
- weapon threeview 不生成截图

### 验证 2：白名单强制加入

输入：

```json
{
  "categories": {
    "ApparelThreeView": { "enabled": false, "limit": 0, "capture": false }
  },
  "whitelist": [
    { "defName": "Axolotl_AfterglowMarriageRobe", "capture": true }
  ]
}
```

预期：

- `ApparelThreeView` 总体不跑
- 但 `Axolotl_AfterglowMarriageRobe` 仍会被执行
- 并且生成截图

### 验证 3：黑名单覆盖分类默认选择

输入：

```json
{
  "categories": {
    "ApparelThreeView": { "enabled": true, "limit": 10, "capture": true }
  },
  "blacklist": ["Axolotl_CloudRobe"]
}
```

预期：

- `ApparelThreeView` 仍取 10 个
- 其中不包含 `Axolotl_CloudRobe`

### 验证 4：白名单优先于黑名单

输入：

```json
{
  "categories": {
    "ApparelThreeView": { "enabled": false, "limit": 0, "capture": false }
  },
  "blacklist": ["Axolotl_AfterglowMarriageRobe"],
  "whitelist": [
    { "defName": "Axolotl_AfterglowMarriageRobe", "capture": true }
  ]
}
```

预期：

- 该物品仍被执行
- 并按白名单截图配置执行


## 风险与注意事项

### 1. 现有数据结构不统一

当前不同测试类型分散在多个队列中：

- `pendingItems`
- `pendingTurretDefs`
- `pendingRangedWeaponDefs`
- `pendingMeleeWeaponDefs`
- `pendingThreeViewTargets`
- `pendingCombatPawnTargets`

如果直接硬写分类计划逻辑，容易变成六套重复代码。建议抽一层“队列条目视图”或“计划筛选辅助函数”。

### 2. 三视图/战斗测试存在变体问题

某些三视图和战斗测试内部会根据展示 pawn、bodyType、装备组合生成多个变体。规划时需要明确：

- 分类限量是按“最终 case 数”计数
- 白名单/黑名单匹配用最终结果 `defName`

否则用户会难以预测行为。

### 3. JSON 解析实现方式要谨慎

当前项目里没有现成 JSON 解析库可直接复用到设置解析逻辑。实现时有两种可选策略：

- 引入轻量 JSON 解析工具
- 自己实现一个足够小的受限解析器

更推荐前者，但需要确认 RimWorld 运行环境和项目引用方式是否方便接入。


## 最终建议

建议把这次改造拆成两个可交付版本。

### 版本 A：先解决快速测试痛点

范围：

- `run_test.py` 支持 JSON 风格分类计划输入
- 模组支持按分类启用/数量/截图开关
- 支持 defName 白名单/黑名单

这版完成后，已经能满足绝大多数日常测试诉求。

### 版本 B：再做 XML 双轨沉淀

范围：

- 新增 `LogSyncAutoTestPlanDef`
- 支持长期维护默认测试计划
- 支持脚本计划覆盖 XML 计划

这版完成后，测试系统才算真正具备“临时编排 + 固定模板”双轨能力。


## 本文结论

我建议按以下思路落地：

- 保留现有 `scope + batch size + spec xml` 兼容链路
- 新增 JSON 风格“测试计划”作为快速测试主入口
- 新增 XML 风格“测试计划 Def”作为长期配置主入口
- 统一支持三件事：
  - 分类是否执行
  - 分类测试数量
  - 分类是否截图
- 同时支持 defName 白名单/黑名单
- 白名单允许单独控制截图，并拥有最高优先级

如果你认可这份规划，下一步我建议先只做版本 A，再逐步推进版本 B。
