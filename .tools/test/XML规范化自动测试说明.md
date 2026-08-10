# XML 规范化自动测试说明

## 目标

这套机制允许被测模组在自己的 XML 中声明 `LogSyncAutoTestSpecDef`，由 LogSyncMod 在运行时按规则筛选自动测试目标，而不是完全依赖自动枚举。

它适合以下场景：

- 只测某几个明确的 `defName`
- 只测某一类内容，例如配方、炮塔、三视图
- 让部分目标开启截图，其他目标只做通过性验证
- 为炮塔或远程武器补抓投射物表现帧

## 启用方式

### 游戏内

1. 在被测模组自己的 XML 中新增 `LogSyncAutoTestSpecDef`
2. 在 LogSyncMod 设置中把自动测试模式切换为“XML 规范驱动”
3. 正常点击自动测试入口运行

### 脚本方式

```powershell
python .\规范化测试\run_test.py `
  --auto-test-package-id your.mod.packageid `
  --auto-test-mode xml-driven `
  --auto-test-scope all `
  --timeout-seconds 600
```

补充说明：

- 如果没有传 `--auto-test-batch-size`，`run_test.py` 会主动清除旧的 `autoTestBatchSize`，避免沿用上一次残留配置
- 只有显式传入 `--auto-test-batch-size N` 时，才会写入新的批量上限

## Def 类型与字段

Def 根类型必须写全限定名：`LogSyncMod.LogSyncAutoTestSpecDef`

当前支持字段如下：

- `defName`
  - 规则自身名称
- `enabled`
  - 是否启用，默认 `true`
- `scope`
  - 规则作用范围
- `categories`
  - 分类过滤列表
- `defNames`
  - 指定要命中的 DefName 列表
- `captureScreenshots`
  - 是否允许常规截图
- `captureTurretProjectileScreenshots`
  - 是否允许炮塔投射物截图
- `captureProjectileScreenshots`
  - 是否允许通用投射物截图
- `turretProjectileScreenshotTicks`
  - 炮塔投射物截图时间点
- `projectileScreenshotTicks`
  - 通用投射物截图时间点

内部规则是：

- `testTypes` 精确限制 CaseType；写入后必须命中相应 CaseType
- `categories` 和 `defNames` 之间是“任一命中即可”
- 如果两者都不写，则表示命中该 `scope` 下全部目标
- 同一个目标如果命中多条规则，会按更精确的规则优先，例如精确 `defName` 优先于纯分类

## Scope 可用值

当前代码中的 `AutoTestScope` 可用值为：

自动模式支持全局可重复参数 `--scope` 和 `--defname`；旧的 `--auto-test-scope`、`--auto-test-defname` 与 `scope=all` 不再接受。正式 Scope 为 `UI`、`Combat`、`View`、`Misc`。`Standard` 是横跨四类的兼容预设，但不隐式启用 CombatPawns、SkillPocketMaps。旧细分 Scope 仍可在旧配置、XML 和 CLI 中作为窄范围兼容值使用。`--defname` 与范围取交集，未命中目标会让结果 JSON/Markdown 标记为 Incomplete。XML 模式可用多个全局范围扩大可发现类别，但具体用例优先由单条 `LogSyncAutoTestSpecDef` 的 `testTypes`、`categories`、`defNames` 决定。

- `Standard`
- `UI`
- `Combat`
- `View`
- `Misc`

兼容细分值：

- `Content`
- `Recipes`
- `Hediffs`
- `Turrets`
- `RangedWeapons`
- `MeleeWeapons`
- `ThreeViews`
- `CombatPawns`
- `SkillPocketMaps`

说明：

- `Standard` 表示标准自动枚举预设，横跨四类但排除 CombatPawns 和 SkillPocketMaps
- `XmlDriven` 模式下，是否真的会被选中，仍然取决于 XML 规则是否命中
- 新结果统一报告四个正式 Scope；旧细分值只用于兼容筛选

## Categories 可用值

当前代码中的分类键如下：

- `Building`
- `Item`
- `Plant`
- `Terrain`
- `Recipe`
- `Hediff`
- `AnimalInfoCard`
- `MechanoidInfoCard`
- `PawnInfoCard`
- `GeneInfoCard`
- `XenotypeInfoCard`
- `Turret`
- `RangedWeapon`
- `MeleeWeapon`
- `SkillPocketMap`
- `HumanoidThreeView`
- `AnimalThreeView`
- `MechanoidThreeView`
- `AlienThreeView`
- `ApparelThreeView`
- `WeaponThreeView`
- `ApparelCombat`
- `AnimalCombat`
- `MechanoidCombat`
- `AlienCombat`

常见搭配：

- `scope=UI` 通常配 `Building`、`Item`、`Terrain`、`Recipe`、`Hediff`
- `scope=View` 可配 `HumanoidThreeView`、`AlienThreeView`、`ApparelThreeView`、`WeaponThreeView` 等
- `scope=Misc` 可用 `testTypes=SkillPocketMap` 精确选择技能口袋地图
- `scope=Combat` 可用 `testTypes=TurretPocketMap` 精确选择炮塔测试

## 三视图规则说明

三视图现在分为几类不同语义：

- `HumanoidThreeView`
  - 展示目标模组的人类 PawnKind 三视图
- `AlienThreeView`
  - 展示目标模组的外星人类 PawnKind 三视图
- `AnimalThreeView`
  - 展示动物三视图
- `MechanoidThreeView`
  - 展示机械体三视图
- `ApparelThreeView`
  - 强制让展示体穿上指定服装后截图
- `WeaponThreeView`
  - 强制让展示体拿上指定武器后截图

当前实现有一个重要变更：

- `HumanoidThreeView` 和 `AlienThreeView` 不再强制裸体，也不再强制清空武器
- 这两类测试会尽量保留 Pawn 生成时自带的服装和主武器，用来反映模组原生外观
- `ApparelThreeView` 和 `WeaponThreeView` 仍然会清空原有装备，再强制展示目标服装或目标武器

这意味着：

- 如果你想看种族或 PawnKind 的原生展示效果，用 `HumanoidThreeView` / `AlienThreeView`
- 如果你想看某件衣服或某把武器本身的展示效果，用 `ApparelThreeView` / `WeaponThreeView`

## 炮塔与投射物截图规则

炮塔当前支持两层截图控制：

- 常规口袋地图截图，由 `captureScreenshots` 控制
- 投射物截图，由 `captureTurretProjectileScreenshots` 控制

投射物截图逻辑：

- 首次检测到口袋地图内出现投射物实体时，记为开火起点
- 按 `turretProjectileScreenshotTicks` 中配置的偏移 tick 依次尝试截图
- 默认推荐时间点为 `20`、`30`、`40`
- 内部会保留一个虚拟预热帧，优先保证第一张真正截图更稳定
- 只有投射物轨迹经过当前截图中心附近时，才会真正落盘
- 如果炮塔已经通过测试，但还有剩余投射物截图时间点未完成，系统会保留一段额外宽限期继续补拍

对于远程武器或其他通用投射物场景，可使用：

- `captureProjectileScreenshots`
- `projectileScreenshotTicks`

## 技能口袋地图说明

技能口袋地图现在已经有独立范围：`SkillPocketMaps`

在 XML 规则中可用于：

- 精确限制只测某些技能 Def
- 对某些技能开启截图
- 与其他范围拆分执行，避免混入普通战斗口袋地图

分类值使用：`SkillPocketMap`

## XML 编写注意点

- Def 根类型必须写 `LogSyncMod.LogSyncAutoTestSpecDef`
- 不建议简写成 `<LogSyncAutoTestSpecDef>`，避免 RimWorld 无法按正确类型解析
- 测试 XML 建议放在被测模组自己的 `Defs` 目录下，例如：
  - `1.6/Defs/LogSync_AutoTests/YourMod_LogSyncAutoTests.xml`
- `scope` 与 `categories` 最好语义一致，不要混写无关分类
- 如果只想精确控制某几个目标，优先写 `defNames`

## XML 示例

```xml
<Defs>
  <LogSyncMod.LogSyncAutoTestSpecDef>
    <defName>MyMod_TurretVisualSpec</defName>
    <scope>Turrets</scope>
    <defNames>
      <li>RL_145mmL932M7_Base</li>
    </defNames>
    <captureScreenshots>true</captureScreenshots>
    <captureTurretProjectileScreenshots>true</captureTurretProjectileScreenshots>
    <turretProjectileScreenshotTicks>
      <li>20</li>
      <li>30</li>
      <li>40</li>
    </turretProjectileScreenshotTicks>
  </LogSyncMod.LogSyncAutoTestSpecDef>

  <LogSyncMod.LogSyncAutoTestSpecDef>
    <defName>MyMod_HumanAndAlienThreeView</defName>
    <scope>ThreeViews</scope>
    <categories>
      <li>HumanoidThreeView</li>
      <li>AlienThreeView</li>
    </categories>
    <captureScreenshots>true</captureScreenshots>
  </LogSyncMod.LogSyncAutoTestSpecDef>

  <LogSyncMod.LogSyncAutoTestSpecDef>
    <defName>MyMod_ApparelShowcase</defName>
    <scope>ThreeViews</scope>
    <categories>
      <li>ApparelThreeView</li>
    </categories>
    <captureScreenshots>true</captureScreenshots>
  </LogSyncMod.LogSyncAutoTestSpecDef>

  <LogSyncMod.LogSyncAutoTestSpecDef>
    <defName>MyMod_TargetedSkillSpec</defName>
    <scope>SkillPocketMaps</scope>
    <defNames>
      <li>Ability_MySkillA</li>
      <li>Ability_MySkillB</li>
    </defNames>
    <captureScreenshots>false</captureScreenshots>
  </LogSyncMod.LogSyncAutoTestSpecDef>
</Defs>
```

## 结果输出

当前测试结果中常见的截图字段有：

- `ScreenshotPath`
- `ScreenshotPaths`

说明：

- 单张截图时通常写入 `ScreenshotPath`
- 多张截图时会优先写入 `ScreenshotPaths`
- Markdown 报告与 JSON 结果都会尽量保留这些路径信息

## 注意事项

- `XmlDriven` 模式下，只会执行命中 `LogSyncAutoTestSpecDef` 的目标
- 如果目标模组没有任何有效 `LogSyncAutoTestSpecDef`，运行器会输出警告，并且不会选出测试目标
- `scope` 负责限定大类，`categories` / `defNames` 负责进一步筛选
- `HumanoidThreeView` 与 `AlienThreeView` 现在保留原生服装和武器，不再默认裸体展示
- `ApparelThreeView` 与 `WeaponThreeView` 仍然是“清空后强制展示目标物”的语义
- 投射物截图时间点是“允许尝试截图的时间点”，不是保证一定生成图片的时间点
