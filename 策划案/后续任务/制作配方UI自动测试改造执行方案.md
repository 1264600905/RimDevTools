# 制作配方 UI 自动测试改造执行方案

## 背景

当前自动测试里的 `Recipe` 测试，并没有真正走到你要求的“工作台制作 UI”语义。

你这次明确要求的目标流程是：

1. 选中目标工作台。
2. 打开工作台的 bills / 制作列表。
3. 添加目标配方。
4. 点击该配方的“详情”按钮。
5. 进入配方配置窗口。
6. 让该工作台真正生产这个物品。
7. 自动补齐原料。
8. 自动立刻完成生产。

用于确认 UI 语义的参考截图：

- `D:\RimworldProject\环世界日志同步mod\参考文档\Screenshot_54.png`

该截图对应的不是产物 `InfoCard`，而是工作台账单（Bill）详情配置窗口，也就是 RimWorld 原生的 `Dialog_BillConfig` 语义。


## 当前源码结论

## 一、当前 Recipe 测试入口

当前配方测试入口在：

- `source/AutoTestProject/AutoTestRunner.ContentModule.cs`

关键方法：

- `EnqueueRecipeDefs()`
- `StartRecipeSession(AutoTestWorkItem item)`
- `TryProduceRecipe(ActiveSession session, out string errorMessage)`

### 1. 配方收集逻辑

`EnqueueRecipeDefs()` 会：

- 扫描 `RecipeDef`
- 解析主产物 `ResolvePrimaryRecipeProduct(recipe)`
- 解析工作台 `ResolveRecipeWorktable(recipe)`
- 提前放置工作台 `PlaceRecipeWorktables(requiredWorktables)`
- 把测试项加入 `pendingItems`

这说明当前框架已经具备：

- 找到可测配方
- 找到对应工作台
- 提前把工作台摆到测试地图上

这部分是后续新流程可以直接复用的基础设施。

### 2. 当前会话启动逻辑

`StartRecipeSession()` 的关键行为是：

- 找到已放置工作台
- 把 `InfoCardDef` 设为 `item.ThingDef ?? (Def)item.RecipeDef`
- 把阶段设为 `WaitingForPreOpenSettle`
- 把结果阶段记为 `info_open`

也就是说，当前 Recipe 测试会话启动后，进入的是 **InfoCard 打开链路**，不是工作台 Bills UI 链路。


## 二、当前 UI 打开的是 InfoCard，而不是工作台制作 UI

当前 UI 逻辑在：

- `source/AutoTestProject/AutoTestRunner.InfoCardModule.cs`

关键方法：

- `ProcessWaitingForInfoCardOpen()`
- `IsInfoCardPresent()`
- `OpenInfoCardOnUiThread()`
- `CompleteActiveSession()`

### 证据 1：当前 UI 打开函数就是 InfoCard

`OpenInfoCardOnUiThread()` 内部走的是：

- `AutoTestInfoCardUiAdapter.Open(activeSession.AttachedHediff)`
- `AutoTestInfoCardUiAdapter.Open(activeSession.SpawnedThing)`
- `AutoTestInfoCardUiAdapter.Open(activeSession.InfoCardDef)`

并且 `IsInfoCardPresent()` 明确检查的是：

- `Find.WindowStack?.WindowOfType<Dialog_InfoCard>()`

这说明当前 Recipe 测试 UI 验证的核心窗口对象是：

- `Dialog_InfoCard`

而不是：

- 工作台的 `ITab_Bills`
- 账单详情窗口 `Dialog_BillConfig`

### 证据 2：Recipe 测试完成前仍然依赖 InfoCard 生命周期

当前 Recipe 测试流程是：

1. 打开 InfoCard
2. 等待 InfoCard 稳定
3. 截图
4. 自动关闭 InfoCard
5. `CompleteActiveSession()` 里如果 `Kind == Recipe`，再调用 `TryProduceRecipe(...)`

这意味着当前流程的语义实际上是：

- **先验证 InfoCard 能打开**
- **再在代码层面直接模拟生产**

而不是：

- **在工作台 UI 里添加账单并查看账单详情后，再完成生产**


## 三、当前“生产”已经存在，但不是通过真实 UI 驱动

当前真正产出配方的逻辑在：

- `source/AutoTestProject/AutoTestRunner.ContentModule.cs`
- `TryProduceRecipe(ActiveSession session, out string errorMessage)`

这个方法当前已经做了很多关键事情：

- 获取工作台 `GetPlacedRecipeWorktable(session.Item.WorktableDef)`
- 校验 `IBillGiver`
- 找一个殖民者作为 worker
- 创建 `Bill_Production`
- 把 Bill 加进 `BillStack`
- 自动生成材料 `TryCreateRecipeIngredients(...)`
- 调用 `GenRecipe.MakeRecipeProducts(...)`
- 把产物生成到地图
- 调用 `productionBill.Notify_IterationCompleted(worker, ingredients)`
- 清理原料
- 删除测试用 Bill

所以当前系统**并不缺“实际生产能力”**。

当前缺的是：

- 把“直接逻辑生产”改造成“先走真实工作台 UI 语义，再触发生产”的完整链路。


## 四、参考 RimWorld 原生 UI 结构后的结论

结合 RimWorld 源码，可确认你要的 UI 路径对应以下原生对象：

### 1. 工作台账单页签

文件：

- `Sources/RimWorld/ITab_Bills.cs`

关键语义：

- 选中 `Building_WorkTable`
- 账单页签里显示当前 `BillStack`
- 通过 `OptionsMaker()` 构造“添加清单”的配方菜单
- 选择配方后执行：
  - `Bill bill2 = recipe.MakeNewBill(precept);`
  - `SelTable.billStack.AddBill(bill2);`

这正是“选中工作台 -> 添加配方”的原生语义。

### 2. 账单详情窗口

文件：

- `Sources/RimWorld/Dialog_BillConfig.cs`

关键语义：

- 构造函数：`Dialog_BillConfig(Bill_Production bill, IntVec3 billGiverPos)`
- 这是截图 `Screenshot_54.png` 对应的窗口类型
- 窗口里包含：
  - 重复模式
  - 次数调节
  - 原料搜索半径
  - 存储设置
  - 工作者限制

这说明你要的“详情”不是 InfoCard，而是：

- `Dialog_BillConfig`

### 3. 工作台本体

文件：

- `Sources/RimWorld/Building_WorkTable.cs`

关键语义：

- `Building_WorkTable : Building, IBillGiver`
- 内含 `BillStack`
- 账单、交互格、原料堆叠格等都在这里

### 4. 生产账单对象

文件：

- `Sources/RimWorld/Bill_Production.cs`

关键语义：

- 账单次数
- 暂停状态
- 产物统计
- 目标数量
- 账单配置状态

这说明后续如果要测试“真正的账单详情 UI”，应该围绕：

- `Building_WorkTable`
- `ITab_Bills`
- `Bill_Production`
- `Dialog_BillConfig`

来设计执行链路。


## 当前实现与目标需求的差异

## 你要的

你要的是：

- 测的是“工作台制作流程”
- 看到的是“Bill 列表与 Bill 详情窗口”
- 验的是“建筑真的把这个配方做出来了”

## 当前实现

当前实现是：

- 先打开配方产物或配方本身的 `InfoCard`
- 确认 InfoCard 能开、能截图、能关闭
- 然后在逻辑层直接创建 Bill 和材料并执行 `GenRecipe.MakeRecipeProducts`

## 直接结论

当前 Recipe 测试并不是“制作 UI 测试”，而是：

- **InfoCard 测试 + 逻辑层模拟生产测试**

这和你的目标语义不一致，所以必须改造。


## 改造目标

本次后续执行的目标，不是把 Recipe 测试改成“另一个 InfoCard 变体”，而是建立一条新的 **Recipe Bill UI 测试链路**。

目标链路定义为：

1. 找到并放置正确工作台。
2. 选中该工作台。
3. 打开 `ITab_Bills` 对应的账单界面。
4. 把目标 `RecipeDef` 添加进该工作台 `BillStack`。
5. 点击该 Bill 的“详情”，打开 `Dialog_BillConfig`。
6. 确认详情窗口稳定打开，并截图。
7. 关闭详情窗口。
8. 自动补齐该 Bill 所需材料。
9. 触发该工作台真实完成一次生产。
10. 验证产物已生成。
11. 记录工作台、Bill、材料、产物、worker 等结果信息。


## 建议的实现方向

## 第一部分：不要删除现有生产逻辑，而是调整其位置

当前 `TryProduceRecipe()` 已经具备：

- 自动加 Bill
- 自动补材料
- 调用 RimWorld 产物生成逻辑
- 记录结果

因此建议：

- **不要推翻这部分**
- 而是把它从“InfoCard 完成后直接调用”改成“Bill UI 验证完成后再调用”

也就是说，当前 `TryProduceRecipe()` 更适合作为：

- **生产执行器**

而不是：

- 当前 Recipe 测试的完整主流程。


## 第二部分：Recipe 测试要从 InfoCard 会话分叉成 Bill UI 会话

当前会话模型只有一条 InfoCard 打开链路。

建议后续新增一条专门的 Recipe UI 测试阶段序列，而不是继续把 Recipe 塞进 InfoCard 生命周期里。

建议新增的阶段语义大致如下：

1. `recipe_select_worktable`
2. `recipe_open_bills_tab`
3. `recipe_add_bill`
4. `recipe_open_bill_config`
5. `recipe_capture_bill_config`
6. `recipe_close_bill_config`
7. `recipe_produce`
8. `recipe_verify_product`

这样能避免继续出现这种结构问题：

- Recipe 明明要测工作台 UI
- 结果却挂在 `InfoCardModule` 里跑完


## 第三部分：优先复用现有基础设施

后续实际实现时，建议复用以下现有能力。

### 1. 继续复用的部分

- `EnqueueRecipeDefs()`
- `ResolveRecipeWorktable()`
- `PlaceRecipeWorktables()`
- `GetPlacedRecipeWorktable()`
- `TryCreateRecipeIngredients()`
- `TryProduceRecipe()` 的底层产出逻辑
- 截图服务 `AutoTestScreenshotService`

### 2. 不应继续复用为主流程入口的部分

- `StartRecipeSession()` 当前的 `InfoCardDef` 逻辑
- `OpenInfoCardOnUiThread()`
- `IsInfoCardPresent()`
- `Dialog_InfoCard` 检测链路

这些可以保留给：

- Building / Item / Hediff / Terrain 原有 InfoCard 测试

但 Recipe 应该脱离它。


## 第四部分：Bill UI 的推荐验证语义

要与参考截图对齐，建议至少验证以下 UI 语义：

### A. Bills 页签已经打开

验证点：

- 当前选中对象是目标工作台
- 当前工作台实现 `IBillGiver`
- 账单列表中存在刚加入的目标配方

### B. 详情窗口已经打开

验证点：

- 当前窗口是 `Dialog_BillConfig`
- `Dialog_BillConfig.bill.recipe == 目标 RecipeDef`
- 窗口已稳定若干帧后再截图

### C. 详情窗口对应的是正确 Bill

验证点：

- 不是别的窗口
- 不是产物 `InfoCard`
- 不是 RecipeDef 的信息介绍窗口

### D. 真正产出发生在该 Bill 上

验证点：

- 该 Bill 已加入对应工作台 `BillStack`
- 材料按配方需求被准备
- 调用生产后成功出现产物
- 结果中能记录：
  - `Worktable`
  - `Recipe`
  - `Ingredients`
  - `Products`
  - `Worker`


## 推荐改造步骤

## 第 1 步：收缩当前 Recipe 流程职责

目标：

- 把当前 Recipe 测试从“InfoCard 验证 + 生产”改成“Bill UI 验证 + 生产”

具体方向：

- 保留 Recipe 的收集与工作台放置逻辑
- 重写 Recipe 会话启动后的 UI 路径
- 不再默认打开 `Dialog_InfoCard`

## 第 2 步：建立工作台选择与 Bills UI 驱动

目标：

- 让测试真正从工作台本身开始

具体方向：

- 选中 `GetPlacedRecipeWorktable()` 返回的工作台
- 驱动 Bills tab 打开
- 在账单列表加入目标 Recipe 的 `Bill_Production`

## 第 3 步：建立 Bill 详情窗口驱动与截图

目标：

- 实现和 `Screenshot_54.png` 一致的窗口级测试

具体方向：

- 为目标 Bill 打开 `Dialog_BillConfig`
- 等待窗口稳定
- 调用统一截图服务截图
- 验证窗口类型与 Bill 绑定正确

## 第 4 步：把生产执行放到 Bill UI 验证之后

目标：

- 保持“先验证 UI，再做产出”的业务语义

具体方向：

- 在 Bill 详情窗口完成验证后
- 关闭窗口
- 再调用生产执行器
- 继续沿用自动补材料与立刻完成生产的机制

## 第 5 步：补结果记录与失败分类

建议新增或明确以下失败语义：

- `failed_recipe_worktable_select`
- `failed_recipe_bills_tab_open`
- `failed_recipe_bill_add`
- `failed_recipe_bill_config_open`
- `failed_recipe_bill_config_interrupted`
- `failed_recipe_bill_config_close`
- `failed_recipe_produce`
- `failed_recipe_product_missing`

这样以后看结果时，能明确知道失败发生在：

- UI 打不开
- UI 打开错了
- Bill 没加进去
- 详情窗口没开出来
- 产物没做出来


## 验证方案

## 验证 1：工作台 UI 路径正确

目标：

- 不再打开产物 InfoCard
- 而是走工作台 Bills UI

验收标准：

- 测试日志中明确记录当前工作台 defName
- 当前窗口对象不是 `Dialog_InfoCard`
- 当前测试过程出现 `Dialog_BillConfig`

## 验证 2：截图语义正确

目标：

- 截图内容与 `Screenshot_54.png` 同类

验收标准：

- 截图画面是 Bill 详情配置窗口
- 不是产物或配方 InfoCard
- 截图标题与 notes 中写明：
  - Recipe
  - Worktable
  - BillConfigOpened=True

## 验证 3：真实生产完成

目标：

- 不是只打开 UI，而是真正完成一次制作

验收标准：

- 原料成功生成并被用于该配方
- 地图中出现至少一个产物
- Notes 记录 `Ingredients=` 与 `Products=`
- `Status=passed`

## 验证 4：Recipe 与其他 InfoCard 测试解耦

目标：

- Recipe 测试不再依赖 InfoCard 生命周期

验收标准：

- Item / Building / Hediff / Terrain 仍走原有 InfoCard 路线
- Recipe 走独立 Bill UI 路线


## 风险与注意事项

### 1. 最大风险不是生产，而是 UI 驱动链路

当前代码已经有生产逻辑，真正难点在于：

- 如何稳定打开并识别 Bills tab
- 如何稳定打开并识别 `Dialog_BillConfig`
- 如何让 UI 自动化和现有 InfoCard 自动化共存，而不互相污染

### 2. 不能把“打开账单详情”偷换成“打开 InfoCard”

这是这次改造里最重要的约束。

只要最终仍然是：

- 打开 RecipeDef 或产物的 `InfoCard`

那就不符合你的要求。

### 3. 不能只验证 UI，不验证产出

你要求的是：

- 这个建筑真正生产该物品

所以不能只停留在：

- Bill 能加进去
- 详情窗口能打开

必须把真实产出也纳入验收。


## 本文结论

当前 Recipe 自动测试的根本问题不是“生产逻辑缺失”，而是：

- **UI 主语错了**

现在测的是：

- `InfoCard`

但你要测的是：

- `工作台 Bills UI + Bill 详情窗口 + 真正生产`

因此后续正确的执行方向应是：

1. 保留当前配方发现、工作台放置、自动补材料、即时生产这些基础设施。
2. 把 Recipe 测试从 InfoCard 生命周期里拆出来。
3. 新建一条围绕 `Building_WorkTable -> ITab_Bills -> Bill_Production -> Dialog_BillConfig` 的 UI 测试链路。
4. 在该 UI 链路验证通过后，再执行真实生产并记录产物结果。

如果下一步开始实际改代码，我建议严格按本文执行，不顺手扩到别的测试类型，先只把 `Recipe` 的 UI 语义纠正到你要求的这条链路上。
