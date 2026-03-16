# InfoCard 关闭问题排查报告

## 0. 最新补充结论（2026-03-16）

本报告前半部分记录的是排查历史，因此其中有一些“当时尚未解决”的表述。经过后续继续修改和真实测试，当前结论已经更新如下：

- `InfoCard` 打开后，确实会让游戏 `tick` 暂停。
- 之前自动测试卡住，不是因为“没打开到 UI”，而是因为状态机大量依赖 `Tick()` 推进。
- 当 `Dialog_InfoCard` 打开后，`Tick()` 不再继续推进，导致：
  - 打开确认无法稳定完成
  - 关闭请求逻辑无法进入
  - 从外部观察就会表现成“窗口已经开了，但自动测试一直不往下走”

这次最终生效的修复思路是：

- 保留 `Tick()` 只负责启动会话、挑选下一个测试对象、整体收尾。
- 把 `InfoCard` 相关阶段全部迁移到 `WindowStackOnGUI -> Repaint` 阶段推进。
- 也就是说：
  - `等待打开前稳定`
  - `确认窗口已打开`
  - `等待关闭前稳定`
  - `发起关闭动作`
  - `确认窗口已关闭`
  这些都改成由 UI 帧驱动，而不是游戏逻辑 tick 驱动。

当前真实测试结果：

- 已对 `sbz.neatstorage` 做过 `batch size=1` 的真实游戏启动测试，结果通过。
- 已对 `sbz.neatstorage` 做过 `batch size=10` 的真实游戏启动测试，结果通过。
- 日志中已经明确出现完整链路：
  - `OpenRequested`
  - `OpenDetected`
  - `OpenConfirmed.RepaintFrames=3`
  - `CloseActionIssued.CloseXClick`
  - `InfoCard close confirmed`

这说明当前对 `Dialog_InfoCard` 的自动打开、检测、关闭链路已经打通。

另外，当前关闭策略的适用范围也需要明确：

- 现在的关闭方案本质上是“拿到 `Window.windowRect` 后，模拟点击右上角关闭 `X`”。
- 这对标准 `Verse.Window` 弹窗通常有效，前提是该窗口：
  - 真实挂在 `Find.WindowStack`
  - `doCloseX = true`
  - 使用 RimWorld 常规窗口布局
- 它不是“所有弹出式 UI 的通用关闭接口”。
- 对没有 `X` 的窗口、依赖点窗口外关闭的浮层、`ITab`、检查面板、主标签页等 UI，仍然需要单独适配。

因此，本报告目前的最终结论应该理解为：

- `InfoCard` 这条链路已经可用。
- 真正关键的解决点不是“怎么关 X 按钮”，而是“打开后不能再依赖暂停的 tick 推状态机”。
- 如果后续扩展到别的 UI，应该复用“UI 帧驱动状态机 + 按 UI 类型选择关闭策略”的思路，而不是直接假设当前的 `CloseXClick` 可以覆盖所有界面。

## 1. 报告目的

本文档只记录 `InfoCard` 自动关闭问题的排查过程、每次修改的意图与结果、当前可确认的事实，以及后续建议。

约束说明：

- 本轮按要求不再修改任何代码逻辑。
- 本文档只做事实归档与技术分析。
- 这里的“历史十几次修改”以本次连续排查链路中的实际尝试为准，按时间顺序整理。

---

## 2. 问题定义

目标是让自动测试流程在 RimWorld 游戏内：

1. 真实进入测试地图。
2. 真实放置目标模组的建筑、物品、地形。
3. 真实打开对应对象的 `InfoCard`。
4. 自动关闭该 `InfoCard`。

当前稳定能做到的部分：

- 能进入快速测试地图。
- 能筛选指定 `packageId` 的建筑、物品、地形。
- 能放下建筑和物品，也能设置地形。
- 能产出自动测试报告。

当前始终未解决的关键问题：

- `Dialog_InfoCard` 会在请求打开后非常快地从 `WindowStack` 中消失。
- 因此自动测试始终无法稳定确认“打开成功”。
- 自然也无法进一步验证“自动关闭成功”。

---

## 3. 当前代码相关文件

问题相关的主要文件如下：

- [AutoTestRunner.cs](/workspace/D:/RimworldProject/环世界日志同步mod/source/AutoTestProject/AutoTestRunner.cs)
- [AutoTestBootstrap.cs](/workspace/D:/RimworldProject/环世界日志同步mod/source/AutoTestProject/AutoTestBootstrap.cs)
- [AutoTestUiAdapters.cs](/workspace/D:/RimworldProject/环世界日志同步mod/source/AutoTestProject/AutoTestUiAdapters.cs)
- [run_test.py](/workspace/D:/RimworldProject/环世界日志同步mod/run_test.py)
- [LogSyncSettings.cs](/workspace/D:/RimworldProject/环世界日志同步mod/source/TempProject/LogSyncSettings.cs)

---

## 4. 核心结论

截至目前，问题并不是“不会关闭 `InfoCard`”这么简单，而是更早一步：

- `InfoCard` 在请求瞬间确实被加入过 `WindowStack`。
- 但在自动测试的下一次确认时，它已经不在 `WindowStack` 里了。
- 所以自动测试大多数时候失败在 `failed_info_card_open`，而不是 `failed_info_card_close_timeout`。

也就是说：

- 自动关闭逻辑不是唯一问题。
- 更关键的是：`Dialog_InfoCard` 为什么会被快速移除。

当前最关键的一条实证日志是：

- 请求打开时窗口摘要里出现了 `Dialog_InfoCard`
- 失败时窗口摘要只剩 `ImmediateWindow, MainTabWindow_Inspect, ImmediateWindow`

这说明：

- `Dialog_InfoCard` 不是根本没创建。
- 它是创建后又被移除了。

---

## 5. 已确认的事实证据

### 5.1 `quicktest` 路径是稳定的

自动进入测试地图的逻辑走的是游戏底层入口，而不是 UI 按钮模拟：

- 通过 `quicktest` 启动参数。
- 以及 `QuickStarter.CheckQuickStart()` / `SceneManager.LoadScene("Play")`。

因此：

- “快速测试一下子就进去了”是因为它本来就不是点 UI。
- 它走的是原生快速启动入口。

### 5.2 `Dialog_InfoCard` 确实进入过窗口栈

自动测试日志中明确出现过类似记录：

`InfoCard requested for sbz_CorpseSpot handle=sbz_CorpseSpot windows=4 summary=ImmediateWindow, MainTabWindow_Inspect, ImmediateWindow, Dialog_InfoCard`

这条证据能确认：

- 请求打开时，`Dialog_InfoCard` 已经真实在 `WindowStack` 里。

### 5.3 随后 `Dialog_InfoCard` 又消失了

失败时记录的窗口摘要变成：

`ImmediateWindow, MainTabWindow_Inspect, ImmediateWindow`

这说明：

- `Dialog_InfoCard` 不是没有打开。
- 而是打开后迅速消失。

### 5.4 脚本层曾有一个独立问题

`run_test.py` 早期有一个明显问题：

- 它只等待 `[LogSyncAutoTest] COMPLETED`
- 不检查 RimWorld 进程是否已经退出

结果是：

- 游戏提前退出时，脚本还会继续等待，造成“看起来像卡住了”的假象。

这个问题已经修正过：

- 自动测试模式下现在会持有游戏进程句柄。
- 一旦游戏提前退出，会直接报：
  `RimWorld 进程已提前退出，退出码: ...`

这个问题和 `InfoCard` 关闭失败不是同一个问题，但会干扰判断。

---

## 6. 历史修改时间线

下面按尝试顺序记录本轮排查中做过的主要修改。

### 尝试 1：用 `new Dialog_InfoCard(...) + WindowStack.Add(...)` 直接开卡

做法：

- 在自动测试里直接 `new Dialog_InfoCard((Def)def, null)`。
- 然后 `Find.WindowStack.Add(infoCard)`。
- 再通过遍历 `Find.WindowStack.Windows` 确认是否存在目标 `Dialog_InfoCard`。

目标：

- 先验证是否能稳定打开信息卡。

结果：

- 全部目标都卡在 `failed_info_card_open`。

问题：

- 当时的确认方式是按 `Def` 去匹配，不够稳。
- 还不清楚是“没打开”还是“开了又没了”。

### 尝试 2：改成按窗口实例判断，不再按 `Def` 猜

做法：

- 把确认逻辑改成使用窗口实例自身的 `IsOpen`。
- 依赖 `Window.IsOpen -> WindowStack.IsOpen(window)`。

目标：

- 排除“只是匹配方式错误”的可能。

结果：

- 依然失败。

得到的结论：

- 问题不只是 `Def` 匹配不准。

### 尝试 3：把 UI 操作从 `Tick` 迁到 `WindowStackOnGUI`

做法：

- 原来在 `TickManager.DoSingleTick` 里直接开窗口。
- 改成在 `WindowStack.WindowStackOnGUI` 的 Harmony 补丁里调用 `ProcessUi()`。

目标：

- 避免在逻辑 Tick 中直接操作 IMGUI 窗口。
- 更接近 UI 实际执行时机。

结果：

- 仍然失败。

得到的结论：

- 仅仅把操作搬到 GUI 阶段，不足以解决问题。

### 尝试 4：禁用 `closeOnClickedOutside`

做法：

- 打开的 `Dialog_InfoCard` 设置：
  `closeOnClickedOutside = false`

目标：

- 排除“打开后被窗口外点击立刻关掉”的可能。

结果：

- 仍然失败。

得到的结论：

- 问题不只是 `closeOnClickedOutside`。

### 尝试 5：通过 `Widgets.CloseButtonFor` 劫持“关闭按钮被点击”

做法：

- 给 `Widgets.CloseButtonFor(...)` 打 Harmony 补丁。
- 当当前绘制窗口正好是目标 `InfoCard` 时，强制让返回值变成 `true`。

目标：

- 模拟“点击底部关闭按钮”。

结果：

- 没有解决问题。

原因分析：

- 这不是真实鼠标点击。
- 只是篡改按钮返回值。
- 并且失败主要还停留在 `failed_info_card_open` 阶段，说明问题早于关闭阶段。

### 尝试 6：把关闭思路转成“Window 适配层”

做法：

- 新增了 `IAutoTestUiHandle` / `AutoTestWindowHandle<TWindow>` / `AutoTestInfoCardHandle`。
- 不再把 `InfoCard` 作为特殊逻辑散落在 Runner 中。
- 统一通过适配层暴露：
  `IsOpen` / `RequestClose()` / `ForceClose()`

目标：

- 为后续更多 UI 做统一抽象。
- 先把 `InfoCard` 作为 `Window` 系 UI 跑通。

结果：

- 架构更清晰了。
- 但 `InfoCard` 依旧没稳定留住。

得到的结论：

- 抽象层方向是对的。
- 但不能单靠“抽象更好”解决时序问题。

### 尝试 7：删除按钮劫持，改回底层 `Window.Close/TryRemove`

做法：

- 移除 `Widgets.CloseButtonFor` 的 Harmony 补丁。
- 关闭逻辑改成通过 `UiHandle.RequestClose()` 走底层关闭。

目标：

- 不再围绕按钮模拟打转。
- 先验证最稳定的 `Window` 关闭路径。

结果：

- 仍然失败在 `failed_info_card_open`。

得到的结论：

- 当前核心瓶颈仍然在“打开后不稳定存在”。

### 尝试 8：把 `autoTestBatchSize` 真正接入执行器

做法：

- 原来设置里有 `autoTestBatchSize`，但执行器并没有使用。
- 新增 `ApplyBatchLimit()`，只保留前 N 个待测项。

目标：

- 支持快速做单条最小验证，不必每次跑全量。

结果：

- 生效。
- 后续很多诊断都基于 `batchSize=1`。

意义：

- 这是有效修改，但它只提升排查效率，不解决 `InfoCard` 本身问题。

### 尝试 9：打开路径改成更接近官方 `Widgets.InfoCardButton(Thing)` 分支

做法：

- 对建筑/物品，不再只用 `Def` 构造。
- 优先按真实 `Thing` / `IConstructible` 对应的构造器打开。
- 地形仍走 `Def` 路径。

目标：

- 尽量贴近游戏 inspect 面板里点击 info 按钮时的构造分支。

结果：

- 依然失败。

得到的结论：

- 不是构造器选错这么简单。

### 尝试 10：限制 `ProcessUi()` 只在 `Repaint` 事件执行

做法：

- 在 `ProcessUi()` 里新增判断：
  只有 `Event.current.type == EventType.Repaint` 时才做 UI 动作。

目标：

- 避免在不稳定的 GUI 事件阶段操作窗口。

结果：

- 依然失败。

得到的结论：

- 问题不是单纯的 GUI 事件类型不对。

### 尝试 11：增加窗口重绑定逻辑

做法：

- `IsInfoCardPresent()` 不再只盯着原始窗口实例。
- 如果原句柄失效，会回扫 `WindowStack.WindowOfType<Dialog_InfoCard>()`。
- 如果还能找到，就重新绑定到当前实例。

目标：

- 排除“原实例引用失效，但窗口还在”的可能。

结果：

- 仍然失败。

得到的结论：

- 不是单纯的实例句柄漂移问题。

### 尝试 12：增加窗口摘要日志

做法：

- 在请求打开时记录窗口列表摘要。
- 在打开确认超时时记录失败时窗口摘要。

目标：

- 明确知道窗口栈在“请求瞬间”和“失败瞬间”分别有什么。

结果：

- 这是本轮最关键的一次有效诊断。

拿到的关键证据：

- 请求瞬间有 `Dialog_InfoCard`
- 失败瞬间没有 `Dialog_InfoCard`

意义：

- 证明窗口是“被移除”，而不是“根本没进栈”。

### 尝试 13：禁用 `closeOnAccept` 和 `closeOnCancel`

做法：

- 打开的 `Dialog_InfoCard` 设置：
  `closeOnAccept = false`
  `closeOnCancel = false`

目标：

- 排除系统级确认/取消键把窗口秒关的可能。

结果：

- 依然失败。

得到的结论：

- 不是默认 Accept/Cancel 快捷键导致的。

### 尝试 14：为 `WindowStack.TryRemove(Window, bool)` 增加 `Dialog_InfoCard` 移除追踪

做法：

- 给 `WindowStack.TryRemove(Window, bool)` 打 Harmony 跟踪补丁。
- 当被移除的是 `Dialog_InfoCard` 时，打印调用栈。

目标：

- 直接抓出是谁关掉了 `InfoCard`。

结果：

- 这次排查没有拿到有效证据。

原因：

- 中间有几轮测试被人工中断。
- 结论被污染，无法据此下最终判断。

### 尝试 15：按用户要求改成“窗口外点击关闭”

做法：

- 允许 `InfoCard` 被窗口外点击关闭。
- 原先先尝试走 `WindowStack` 内部的“clicked outside”逻辑。

目标：

- 按用户要求改成“窗口外点击”的思路。

结果：

- 用户指出这不是真正的鼠标点击。

结论：

- 这只是内部关闭接口，不是输入模拟。

### 尝试 16：改成 Win32 真实鼠标点击输入

做法：

- 在 `AutoTestUiClickSimulator` 里加入 Win32 调用：
  `ClientToScreen`
  `SetCursorPos`
  `mouse_event(left down / left up)`
- 把关闭点换算到实际屏幕坐标。
- 先后尝试过两个位置：
  - 屏幕上方中间，下偏 `10f`
  - 屏幕左侧垂直中心，右偏 `10f`

目标：

- 满足“真正模拟鼠标点击”的要求。

当前状态：

- 代码已写入并部署。
- 但没有拿到一轮足够干净且可证明有效的最终成功结论。

也就是说：

- 这是最新方案。
- 但还没有形成“已证实成功”的测试闭环。

---

## 7. 测试期间出现过的非功能性问题

### 7.1 部署与测试被错误并行过

曾出现过一次错误做法：

- 编译部署和测试脚本被并行执行。
- 结果编译失败时测试脚本仍然继续启动。

这个做法本身是错误的。

后续排查已改成：

- 先编译部署成功
- 再执行测试

### 7.2 游戏目录里的 DLL 曾被旧进程锁住

出现过以下情况：

- 工作区里新 DLL 已生成
- 但游戏目录里的旧 DLL 因被 RimWorld 占用，没有覆盖成功

这会导致：

- 实际运行的还是旧逻辑
- 造成“明明改了，但现象没变”的错觉

后续处理方式：

- 先停掉 `RimWorldWin64.exe`
- 再部署
- 再确认游戏目录 DLL 时间戳

### 7.3 测试结果曾被人工关闭游戏污染

有几轮测试中：

- 游戏被人工提前关闭
- 导致无法判断“是自动关闭成功”还是“只是脚本收尾”

因此后续所有可信判断都只能以：

- 未被人工中断
- 有清晰阶段日志

的测试为准。

---

## 8. 当前代码状态总结

目前 AutoTest 相关代码已经包含以下能力：

- 支持指定 `packageId` 的定向自动测试
- 支持建筑 / 物品 / 地形三类收集
- 支持 `batchSize`
- 支持自动写测试报告
- 支持真实 `quicktest` 启动
- 已新增 `Window` 系 UI 适配层雏形
- 已加入真实鼠标点击输入方案

但在 `InfoCard` 关闭问题上，仍然没有形成稳定成功结论。

最准确的现状描述是：

- 自动测试系统整体已经具备基础框架
- 但 `InfoCard` 是当前唯一没有打通的 UI 关键点
- 并且它失败的主要阶段仍然是“打开确认失败”

---

## 9. 失败原因分析

基于现有证据，最合理的解释有三种：

### 9.1 `Dialog_InfoCard` 被某个游戏内部逻辑主动移除

证据：

- 它确实进入过 `WindowStack`
- 随后又消失了

这说明：

- 不一定是输入问题
- 也可能是游戏内部某个窗口管理逻辑、选择切换逻辑、或别的 UI 刷新逻辑把它关掉了

### 9.2 自动测试触发时机仍然和游戏 UI 生命周期不完全对齐

虽然已经做过：

- GUI 阶段执行
- `Repaint` 限制

但仍有可能：

- 选中对象、检查面板刷新、信息卡生成、窗口栈更新这些步骤之间的时序还不对

### 9.3 鼠标点击虽然变成了真实输入，但没有拿到干净结论

最新方案已经改成 Win32 真实点击。

但目前缺少一轮同时满足下面条件的测试：

- 不被人工中断
- 有阶段级日志
- 能证明点击前后窗口状态变化

所以不能断言：

- 最新鼠标模拟一定有效
- 也不能断言一定无效

---

## 10. 为什么“快速测试”能成功，而 `InfoCard` 一直卡

这是本轮排查中最容易混淆的点。

两者根本不是同一种动作：

### 快速测试

当前实现直接走：

- 启动参数 `quicktest`
- `QuickStarter.CheckQuickStart()`
- 或 `SceneManager.LoadScene("Play")`

这相当于：

- 调用游戏底层入口
- 根本不是模拟点 UI 按钮

所以它天然稳定。

### `InfoCard` 自动关闭

目标是：

- 先打开某个具体对象的信息卡
- 再让它自己消失

这个过程依赖：

- 选择状态
- 检查面板状态
- `Dialog_InfoCard` 自身生命周期
- 窗口栈更新
- 输入时序

所以它比 `quicktest` 复杂得多。

结论：

- “快速测试能秒进”并不能推导出“InfoCard 也能用类似难度秒关”

---

## 11. 建议的后续方向

如果后续继续排查，建议按下面顺序做。

### 方向 1：把 `InfoCard` 的打开和关闭都拆成阶段信号

需要明确打出类似日志：

- `INFOCARD_OPEN_REQUESTED`
- `INFOCARD_OPEN_CONFIRMED`
- `INFOCARD_CLOSE_REQUESTED`
- `INFOCARD_CLOSE_CONFIRMED`

这样脚本能在失败时立刻停住，不必等整轮测试完成。

### 方向 2：为鼠标点击方案增加点击前后快照

至少记录：

- 点击位置
- 点击前窗口列表
- 点击后窗口列表
- 当前鼠标屏幕坐标

这样才能证明：

- 真实鼠标点击有没有发出去
- 发出去后打到了什么位置

### 方向 3：继续抓 `Dialog_InfoCard` 被移除的真实调用栈

这是最关键的证据。

一旦能稳定拿到“谁移除了它”的调用栈，问题会明显收敛。

### 方向 4：把 `InfoCard` 从“大批量流程”里彻底独立出来

建议做一个单独的短测试命令，只测：

1. 放下一个对象
2. 打开 `InfoCard`
3. 关闭 `InfoCard`
4. 立即退出

这样比在全量测试里排查高效得多。

---

## 12. 最终结论

本轮排查已经明确了很多事情，但还没有把问题彻底解决。

已经明确的部分：

- 自动测试框架基本成型。
- `quicktest` 启动链路稳定。
- `Dialog_InfoCard` 确实会进入窗口栈。
- 它之后又被移除了。
- 失败主阶段仍然是 `failed_info_card_open`。
- 早期有过脚本等待逻辑、部署锁文件、人工中断污染结论等问题。
- 最新方案已经改成真实鼠标点击，而不是内部窗口关闭捷径。

尚未明确的部分：

- `Dialog_InfoCard` 是被谁、在什么准确时机移除的。
- 最新真实鼠标点击方案是否已经有效。

所以当前最准确的总体评价是：

- 这不是“完全没头绪”的问题。
- 但也不是“只差一个关闭按钮”的问题。
- 目前已经拿到足够多的中间证据，说明问题集中在 `Dialog_InfoCard` 生命周期和 UI 时序上。

---

## 13. 附：本轮排查中的关键误区

最后单独列出几条本轮已经证伪或应避免继续投入的方向：

- 不要再把“收到 `COMPLETED`”误认为“功能测试通过”
- 不要再把“游戏退出了”误认为“InfoCard 成功被自动关闭”
- 不要再并行执行“编译部署”和“测试脚本”
- 不要再把“内部关闭接口”当成“真实鼠标点击”
- 不要再忽略“人工手动关闭游戏/窗口”对结论的污染

---

## 14. 文档结尾说明

本报告写入时间对应当前工作区状态。

它的作用是：

- 为后续继续排查提供事实基础
- 避免重复走已被证伪的路线
- 区分哪些修改只是“改善排查效率”，哪些修改真正改变了逻辑
