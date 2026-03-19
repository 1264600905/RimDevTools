# run_test.py 功能说明

## 文件定位

- 脚本路径：`规范化测试/run_test.py`
- 主要职责：在 RimWorld 本地环境中自动切换模组列表、修改 LogSyncMod 配置、启动游戏、等待自动测试完成、回收现场并输出结果摘要

它本质上不是单纯的“启动脚本”，而是一个面向本地回归测试的测试编排器。

## 当前具备的主要功能

### 1. 启动模式管理

脚本支持四种模式：

- `quick`
  - 普通快速启动，用于最基础的游戏拉起与日志观察
- `manual`
  - 更偏人工介入的启动流程
- `auto`
  - 自动测试模式，按 LogSyncMod 当前自动枚举逻辑执行
- `xml`
  - XML 规范驱动模式，只跑命中 `LogSyncAutoTestSpecDef` 的目标

脚本还能根据参数自动推断该走 `auto` 还是 `xml`。

### 2. LogSyncMod 配置写入

脚本可以直接修改 RimWorld 配置目录中的 `Mod_LogSyncMod_LogSyncMod.xml`，包括：

- `keywords`
- `autoTestEnabled`
- `autoTestTargetPackageId`
- `autoTestOutputPath`
- `autoTestMode`
- `autoTestScope`
- `autoTestBatchSize`

这使它可以把“测试准备”自动化，而不是每次手工进游戏点设置。

补充说明：

- `autoTestPreventPause`
- `autoTestUseFastSpeed`
- `autoTestCaptureScreenshots`
- `autoTestPocketMapMaxConcurrent`

这 4 个字段现在改为完全依赖 LogSyncMod 内部默认值，测试脚本不再写入，也不再提供命令行开关。
默认语义为：

- `autoTestPreventPause=true`
- `autoTestUseFastSpeed=true`
- `autoTestCaptureScreenshots=true`
- `autoTestPocketMapMaxConcurrent=5`

### 3. 模组列表切换

脚本会读取指定的 modlist 文件，例如：

- `规范化测试/modlist/RaceTestMod.xml`
- 其他预设 modlist

然后自动改写 `ModsConfig.xml`，只启用本次测试需要的模组，并自动补入：

- 当前项目 `trigger.logsyncmod`
- 被测模组 packageId

测试结束后会恢复原始模组列表，避免污染玩家日常配置。

### 4. 自动寻找被测模组目录

脚本会根据 `packageId` 自动在以下位置查找目标模组：

- RimWorld 本地 `Mods`
- Steam Workshop `294100` 目录
- 官方 DLC 路径

找到后可进一步完成临时 XML 写入、旧 XML 禁用、结果目录定位等动作。

### 5. 临时自动测试 XML 生成

在 `auto` 模式下，如果传了 `--auto-test-defname`，脚本会：

- 自动生成临时 `LogSyncAutoTestSpecDef`
- 临时禁用目标模组原有自动测试 XML
- 只运行本次指定的目标 Def
- 结束后恢复原有 XML，并删除临时文件

临时 XML 中的截图开关会直接采用默认开启策略，不再由命令行参数控制。

这对“精确抽测几个目标”非常实用。

### 6. 测试前清场与异常恢复

脚本在启动前会做很多保护动作：

- 等待已运行的 RimWorld 先退出
- 恢复上次中断遗留的 `ModsConfig` 备份
- 恢复或清理上次残留的临时自动测试 XML
- 归档旧日志
- 清理旧自动测试输出

这部分是它稳定性比较高的关键原因。

### 7. 游戏启动与完成信号监听

脚本会启动游戏，并轮询日志等待关键完成信号：

- 普通运行信号：`[LogSyncMod] RUNTIME_READY_TO_CLOSE`
- 自动测试完成信号：`[LogSyncAutoTest] COMPLETED`

一旦检测到完成信号，脚本会自动收尾、关闭游戏并输出摘要。

### 8. 日志归档与摘要输出

脚本会管理：

- `规范化测试/精选日志.log`
- 历史归档日志

并在测试结束后输出：

- 日志大小
- 行数
- 末尾预览

这样不必每次手动翻整份日志才能确认结果。

### 9. 自动测试产物管理

脚本会自动管理 `AutoTestOutput` 下的运行结果：

- `*_Manifest.json`
- `*_Result.json`
- `*_Report.md`
- `*_Screenshots/`

同时按保留数量自动清理旧产物，避免目录长期膨胀。

### 10. settings-only 配置模式

脚本支持只写设置、不启动游戏：

- 可用于先准备测试环境
- 可用于调试 LogSyncMod 配置是否被正确写入
- 对排查“到底是脚本没写进去，还是游戏逻辑没生效”很有帮助

## 它现在其实承担了三类职责

从结构上看，`run_test.py` 目前混合了三类工作：

### A. 环境管理

- ModsConfig 备份与恢复
- 模组列表切换
- 外部目录查找
- 残留文件清理

### B. 测试配置生成

- 写 LogSyncMod 设置
- scope/mode 参数归一化
- 临时 XML 生成与恢复

### C. 测试运行编排

- 启动 RimWorld
- 轮询日志
- 等待完成信号
- 输出摘要与收尾

这也是为什么它虽然好用，但代码量已经明显变大。

## 是否可以精简

可以精简，但我不建议做“功能删除式精简”，更建议做“结构拆分式精简”。

原因是：

- 现在很多功能虽然看着多，但都是真实解决过痛点的
- 尤其是“恢复现场”“临时 XML”“日志归档”“自动补 packageId”这些，删掉后回归体验会明显变差
- 真正的问题不是功能太多，而是单文件承担职责过多

## 建议保留的功能

这些功能建议保留，不值得删：

- 模组列表切换与恢复
- LogSyncMod 核心设置自动写入
- 自动等待游戏退出/启动
- 完成信号监听
- 临时 XML 生成与恢复
- 旧日志与旧测试结果清理
- settings-only 模式

这些都是高频开发功能，直接关系到回归效率。

## 可以考虑精简或下沉的部分

### 1. 启动模式可以收敛

当前 `quick` / `manual` / `auto` / `xml` 四种模式里，真正高频的是：

- `auto`
- `xml`

可以评估是否把：

- `quick`
- `manual`

弱化为内部兼容分支，减少对外说明成本。

### 2. 参数归一化逻辑有重复

`scope`、`mode` 的字符串归一化在脚本内出现了不止一处。

这部分可以抽成统一函数或统一常量映射，避免后面新增 scope 时漏改。

### 3. 文件恢复逻辑可以模块化

下面这些逻辑适合拆成独立辅助模块：

- `ModsConfig` 备份/恢复
- 临时 XML 写入/恢复
- 输出目录清理
- 日志归档

这样主流程会更短，更容易看清“测试到底怎么跑”。

### 4. 输出摘要可以分层

现在脚本既负责跑测试，又负责打印较多运行信息。

可以把输出拆成：

- 简洁模式：只看成败和结果路径
- 详细模式：输出完整恢复/清理细节

这样日常使用会更清爽。

## 推荐的精简方向

如果后续要整理，我建议按这个方向做，而不是直接删功能：

1. 把 `run_test.py` 保留为主入口
2. 把“模组列表管理”“自动测试 XML 管理”“结果归档管理”拆成 3 个独立模块
3. 把参数归一化表集中到一处
4. 对外主打两种模式：`auto` 和 `xml`
5. 保留 `settings-only` 作为调试辅助模式

这样能做到：

- 用户使用层面更简单
- 开发维护层面更清晰
- 现有能力几乎不丢

## 当前结论

`run_test.py` 不是“功能过多到应该砍掉一半”，而是“功能已经形成完整测试闭环，但应该拆分结构”。

现在这轮精简之后，脚本对外已经少了 4 个容易让人迷惑的自动测试参数，入口更接近日常使用习惯。

一句话总结：

- 功能上值得保留
- 结构上值得重构
- 不建议为了精简而删除现场恢复、临时 XML、结果归档这些核心能力
