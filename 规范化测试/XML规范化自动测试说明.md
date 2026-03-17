# XML 规范化自动测试说明

## 目标

这套机制允许被测模组在自己的 XML 里声明 `LogSyncAutoTestSpecDef`，由日志同步模组按声明内容自动筛选测试目标，而不是只靠运行时自动枚举。

适用场景：

- 只测指定 `defName`
- 只测某一类内容
- 为特定目标开启截图
- 为炮塔额外抓取投射物表现帧

## 使用方式

1. 在被测模组自己的 Def XML 中新增 `LogSyncAutoTestSpecDef`
2. 在日志同步模组设置里把“测试模式”切换成“XML 规范驱动”
3. 继续使用原有自动测试入口运行测试

如果通过脚本启动，也可以直接指定：

```powershell
python .\规范化测试\run_test.py `
  --auto-test-package-id your.mod.packageid `
  --auto-test-mode xml-driven `
  --auto-test-scope turrets `
  --timeout-seconds 600
```

## Def 结构

可用字段如下：

- `defName`
  规则自身名字
- `enabled`
  是否启用，默认 `true`
- `scope`
  作用范围，可选：`All`、`Content`、`Recipes`、`Hediffs`、`Turrets`
- `categories`
  分类过滤
- `defNames`
  指定要命中的 DefName 列表
- `captureScreenshots`
  是否允许常规截图
- `captureTurretProjectileScreenshots`
  炮塔是否抓投射物截图
- `turretProjectileScreenshotTicks`
  相对“首次检测到投射物出现”的偏移 tick

## 当前支持的分类值

- `Building`
- `Item`
- `Terrain`
- `Recipe`
- `Hediff`
- `Turret`

说明：

- `scope=Content` 时，通常配合 `Building`、`Item`、`Terrain`
- `scope=Turrets` 时，分类值应使用 `Turret`
- 规则命中逻辑是“同 scope 下，`categories` 或 `defNames` 任一命中即可”
- 如果规则既没有写 `categories` 也没有写 `defNames`，则表示命中该 `scope` 下的全部目标

## 炮塔截图规则

炮塔当前支持两类截图：

- 常规口袋地图截图
  由 `captureScreenshots` 控制
- 投射物表现截图
  由 `captureTurretProjectileScreenshots` 控制

投射物截图逻辑：

- 首次检测到口袋地图内有投射物实体出现时，记为开火起点
- 按 `turretProjectileScreenshotTicks` 里的偏移 tick 依次截图
- 默认值是 `5`、`20`、`30`

## XML 示例

```xml
<Defs>
  <LogSyncAutoTestSpecDef>
    <defName>MyMod_TurretVisualSpec</defName>
    <scope>Turrets</scope>
    <defNames>
      <li>RL_145mmL932M7_Base</li>
    </defNames>
    <captureScreenshots>true</captureScreenshots>
    <captureTurretProjectileScreenshots>true</captureTurretProjectileScreenshots>
    <turretProjectileScreenshotTicks>
      <li>5</li>
      <li>20</li>
      <li>30</li>
    </turretProjectileScreenshotTicks>
  </LogSyncAutoTestSpecDef>

  <LogSyncAutoTestSpecDef>
    <defName>MyMod_ContentSanitySpec</defName>
    <scope>Content</scope>
    <categories>
      <li>Building</li>
      <li>Item</li>
    </categories>
    <captureScreenshots>true</captureScreenshots>
  </LogSyncAutoTestSpecDef>

  <LogSyncAutoTestSpecDef>
    <defName>MyMod_RecipeTargetedSpec</defName>
    <scope>Recipes</scope>
    <defNames>
      <li>Recipe_MyWeapon</li>
    </defNames>
    <captureScreenshots>false</captureScreenshots>
  </LogSyncAutoTestSpecDef>
</Defs>
```

## 结果输出

测试报告现在会记录：

- `ScreenshotPath`
- `ScreenshotPaths`

当同一个目标产生多张截图时，会优先写入 `ScreenshotPaths`，Markdown 报告和 JSON 结果都会保留。

## 注意事项

- `XmlDriven` 模式下，只会测试命中 `LogSyncAutoTestSpecDef` 的内容
- 如果目标模组下没有任何 `LogSyncAutoTestSpecDef`，运行器会记录警告，并且不会选出测试目标
- `scope=All` 现在会按统一调度顺序处理所有范围，不再只跑炮塔
