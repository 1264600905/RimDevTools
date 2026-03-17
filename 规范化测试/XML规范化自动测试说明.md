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
  --auto-test-scope all `
  --timeout-seconds 600
```

说明：

- 不传 `--auto-test-batch-size` 时，`run_test.py` 现在会主动清除旧的 `autoTestBatchSize` 配置，避免残留上一次的批量限制
- 只有在你显式传入 `--auto-test-batch-size N` 时，才会写入新的批量上限

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
- 默认值是 `20`、`30`、`40`
- 内部会额外保留一个“虚拟 `t5` 预热帧”，只用于让第一张真正截图更稳定，不会生成图片文件
- 截图仍然要求“射弹经过当前截图中心附近”才会真正落盘
- 如果炮塔已经通过测试，但仍有剩余投射物截图时间点未完成，口袋地图会额外保留最多 `20` 秒截图宽限期
- 宽限期内如果剩余时间点提前拍满，会提前关闭口袋地图，不会强制等满 `20` 秒

建议：

- 对高射速、短程或投射物较慢的炮塔，优先使用 `20`、`30`、`40`
- 如果某类炮塔仍然难以抓到完整弹道，可以继续把时间点往后调，例如 `30`、`40`、`60`

## XML 编写注意点

- Def 根类型必须写全限定名：`LogSyncMod.LogSyncAutoTestSpecDef`
- 不建议写成 `<LogSyncAutoTestSpecDef>`，否则 RimWorld 可能无法识别为有效 Def 类型
- 建议把测试 XML 放在被测模组自己的 `Defs` 目录下，例如：
  `1.6/Defs/LogSync_AutoTests/YourMod_LogSyncAutoTests.xml`

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
    <defName>MyMod_ContentSanitySpec</defName>
    <scope>Content</scope>
    <categories>
      <li>Building</li>
      <li>Item</li>
    </categories>
    <captureScreenshots>true</captureScreenshots>
  </LogSyncMod.LogSyncAutoTestSpecDef>

  <LogSyncMod.LogSyncAutoTestSpecDef>
    <defName>MyMod_RecipeTargetedSpec</defName>
    <scope>Recipes</scope>
    <defNames>
      <li>Recipe_MyWeapon</li>
    </defNames>
    <captureScreenshots>false</captureScreenshots>
  </LogSyncMod.LogSyncAutoTestSpecDef>
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
- `turretProjectileScreenshotTicks` 只是“允许尝试截图的时间点”，不是强制每个时间点都一定会落盘
- 如果某个时间点没有命中“射弹经过截图中心”的条件，该时间点可能不会生成图片
- 通过测试后的 `20` 秒截图宽限期，只用于补拍剩余炮塔截图，不会改变测试是否通过的判定
