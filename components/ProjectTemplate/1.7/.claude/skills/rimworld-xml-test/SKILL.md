---
name: rimworld-xml-test
description: 编写、调整和运行 RimDevTools 外部场景 XML，定制 RimWorld 测试的角色、配装、地图、动作、效果断言、参数矩阵、普通回归补充或替代、三视图与群战数值实验。需要高度自定义 XML 测试时使用；普通 Def/Patch 编辑、XML 热重载和仅运行内置自动测试不属于本 skill。
---

# 自定义 XML 场景测试

把用户要验证的机制写成可复用的外部 `TestSuite`：明确准备条件、实际动作、观察窗口和结果判据。测试输入不注册到 DefDatabase，不向被测模组添加 `LogSyncAutoTestSpecDef`，不使用已移除的 `--mode xml`。

## 版本与工程位置

- 当前提取的已交付场景后端仅支持 **RimWorld 1.7**。这个 skill 也随 1.6 模板分发，但这不代表后端已回迁。先读项目 `mod_config.jsonc` 的 `game_version` 并核对本地工具能力；若仍是仅支持 1.7 的宿主，说明限制，保留可复用测试输入，不把 XML 改写为 1.6 冒充兼容，也不擅自升级项目。
- 在项目根目录使用本地 CLI：`.\.tools\RimDevTools.exe`。环境、部署和依赖诊断按同模板的 `rimdevtools` skill；本 skill 负责场景设计及结果语义。
- 新输入可放在项目 `Tests/Scenarios/`，也可沿用已有测试源码目录；它是测试输入，不能进入游戏 `Defs`、`Patches`、`sync_items` 或模组发布载荷。不要在日志或运行暂存目录编写测试源码。
- 单文件输入可用任意 XML 后缀；传目录时仅非递归读取当前层 `*.autotest.xml`。将普通回归与群战放在不同输入目录，避免一次枚举到两种模式。

## 按目标读取

| 当前任务 | 必要参考 / 可改用的起点 |
| --- | --- |
| 基础结构、角色继承、精确配装、地图环境 | [schema-and-actors.md](references/schema-and-actors.md)、[基础场景](assets/basic.autotest.xml) |
| 技能效果、战斗、配方、健康或物品生命周期 | [steps-and-observation.md](references/steps-and-observation.md) |
| 矩阵、精确选择、补充/替代普通回归 | [selection-and-coverage.md](references/selection-and-coverage.md)、[远程矩阵](assets/ranged-matrix.autotest.xml) |
| 三视图、整套配装、暂存角色输入 | [views-and-staging.md](references/views-and-staging.md)、[配装视图](assets/loadout-view.autotest.xml) |
| 双方群战、整场继承、换边与伤害统计 | [groupcombat.md](references/groupcombat.md)、[群战实验](assets/groupcombat.autotest.xml) |

这些参考已从原工程的《全新xml测试实现文档》四批使用说明及交付边界整理，无需取得原工程的策划案或测试夹具。较早计划和完成清单中的“后续批次尚未交付”是历史状态；以对应能力最新使用说明为准。模板文件使用原版定义演示结构，尚需在实际宿主核对定义、原生约束与运行结果。

## 执行工作流

1. **确定要观察的结果。** 从用户请求和项目现有定义查出真实 packageId、Def 类型、DefName、必要 DLC/模组及原生前提。明确是效果回归、渲染展示还是群战实验。只在缺失信息会改变测试含义时询问。
2. **最小化准备。** 优先继承真实 `basePawnKind`，只覆盖相关字段；将资源补充和调试准备放在实际动作之前。用 `Require` 核对关键前提，再以新的 `Checkpoint` 或动作自带基线开始效果观察。
3. **编写稳定输入。** 每条规则填写唯一 `testDefName`，局部对象与步骤使用稳定 `id`。重复配装用 `PawnTemplate`，重复环境用 `ScenePresets`；变体使用显式 Matrix。先跑一个实例再按需要扩展；不要为了通过而增加隐式冷却重置、无敌、强制成功或日志豁免。
4. **采用与目标匹配的判据。** 技能“接受/开始/完成”不等于效果发生；截图存在不等于画面正确；仅装备目标或填入目标名不等于执行了目标机制。使用原生状态、对象身份变化和实际观察窗口证明结果。`ApplyDamage` 只能证明受击机制，不能证明另一把武器命中。
5. **按已授权范围验证。** 只编写测试时交付输入与命令；用户已要求运行或验证时执行相应测试。先核对版本与依赖，使用范围最小的输入。须覆盖当前源码时用 `--deploy`，沿用 `rimdevtools` 的构建部署要求。源码仓库中的 AutoTestRunner 若已修改，须先构建相应宿主，通用部署不会自行构建它。
6. **核对本轮实际执行。** 解析 stdout 最终 JSON，再读取工具返回的原始结果。核对选中的物理实例、缺失用例、失败断言、`scenario` 阶段、依赖与输入哈希；混合模式还需核对替代关系及 `verifiedCoverageKeys`。失败后根据原生前提、配置、测试方式和正常游戏可复现性区分原因。

## 命令起点

从相应 assets 复制到测试源码目录后修改身份和目标；以下路径与 `Example_*` 名称对应模板内容。`--deploy` 仅在需要包含当前项目变更时加入。

```powershell
# 基础自定义回归；显式设置批量上限，不假定目录内所有实例都会执行
.\.tools\RimDevTools.exe test run --mode auto --scenario-only --scenario-path "Tests/Scenarios/basic.autotest.xml" --test-name Example_PawnAlive --batch-size 1 --timeout-seconds 360

# 矩阵回归；选择稳定规则名会保留其所有行作为候选，仍须核对实际选择
.\.tools\RimDevTools.exe test run --mode auto --scenario-only --scenario-path "Tests/Scenarios/ranged-matrix.autotest.xml" --test-name Example_RangedDamage --batch-size 2 --timeout-seconds 600

# 独立群战；必须显式指名，不使用 --scenario-only
.\.tools\RimDevTools.exe test run --mode groupcombat --scenario-path "Tests/Benchmarks/groupcombat.autotest.xml" --test-name Example_WolvesVsBears --timeout-seconds 900
```

需要限制目标包时追加真实 `--target-package-id`；不要把示例的原版 packageId 当作用户模组的身份。普通 `--defname`、外部 `--test-name` 与 Coverage 的差异见选择参考。

## 结果与产物约定

- 日志、结果、截图和测试存档只使用 RimDevTools 自动生成的原始路径。成功读 `result.artifacts`，失败读 `error.details.artifacts`；常见字段为 `directoryPath`、`syncLogPath`、`autoTestOutputPath`。路径存在与文件已生成须另行核对，缺失就如实说明。
- 除用户明确要求导出外，禁止自建测试证据、任务或日期归档目录，包括 `.tools`、`.tools/log` 下的额外目录；不得换目录绕过。禁止重定向、`Tee-Object`、`Out-File`、`Set-Content` 或复制另存 stdout/stderr、日志、截图、测试存档，也不编写一次性归档包装脚本。
- 不修改原始证据，不补造缺失产物；保留与清理由工具负责。测试串行执行，运行中不编辑活动游戏配置、事务清单或请求/结果文件。
- 在聊天中报告选定范围、实际执行数、成功/失败/未执行、日志问题、关键原始路径与局限；除用户明确要求外不另写测试报告或校验文档。区分静态校验、纯逻辑验证和真实游戏运行，不能用前两者声称机制已通过。
- 普通机器 JSON 保留全部物理实例，Markdown 可能只列失败；不要因报告没有成功条目就判断它们未运行。群战保留全部参与者，胜负、执行状态和测量质量分别报告，不并入回归通过率。
