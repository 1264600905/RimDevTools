---
name: rimworld-xml-test
description: 编写、调整和运行 RimDevTools 外部 XML 自动测试，包括效果回归、角色配装、地图生成、三视图、群战、擂台淘汰赛、魔王轮战赛与极限挑战赛、混编队伍和战力预算。需要设计自定义测试场景或战斗评测时使用；普通 Def/Patch 编辑、XML 热重载和仅运行内置自动测试不属于本 skill。
---

# 自定义 XML 场景测试

把用户要验证的机制写成可复用的外部 XML：普通回归和群战使用 `TestSuite`，擂台使用独立的 `ArenaSuite`。明确准备条件、实际动作、观察窗口和结果判据。测试输入不注册到 DefDatabase，不向被测模组添加 `LogSyncAutoTestSpecDef`，不使用已移除的 `--mode xml`。

## 版本与工程位置

- 当前提取的已交付场景后端仅支持 **RimWorld 1.7**。这个 skill 也随 1.6 模板分发，但这不代表后端已回迁。先读项目 `mod_config.jsonc` 的 `game_version` 并核对本地工具能力；若仍是仅支持 1.7 的宿主，说明限制，保留可复用测试输入，不把 XML 改写为 1.6 冒充兼容，也不擅自升级项目。
- 在项目根目录使用本地 CLI：`.\.tools\RimDevTools.exe`。环境、部署和依赖诊断按同模板的 `rimdevtools` skill；本 skill 负责场景设计及结果语义。
- 新输入可放在项目 `Tests/Scenarios/`，也可沿用已有测试源码目录；它是测试输入，不能进入游戏 `Defs`、`Patches`、`sync_items` 或模组发布载荷。不要在日志或运行暂存目录编写测试源码。
- 单文件输入可用任意 XML 后缀；传目录时仅非递归读取当前层 `*.autotest.xml`。普通回归、群战和擂台宜分目录：前两者不可混用，擂台目录入口会跳过根节点不是 `ArenaSuite` 的文件，须核对 `SkippedFiles` 和实际选中项。

## 队伍配置与准备（1.5.7.0 起，限 1.7 后端）

精确混编优先复用 `PawnTemplate → PawnGroup(template/count) → SquadTemplate source="groups"`，由群战 PawnGroup 或三种擂台 Entrant 引用整队。不要为不同赛制复制单兵字段，或新增专用 PawnKind/配装回调。固定名单不会为了另一队的 CP 预算扩缩；Entrant 可独立覆盖 mode/count/budget/costMode。

`costMode=native` 按实际成员基础 CP × 异种倍率核算，不是按装备、品质或技能估价。显式异种和生理年龄在原生生成时生效；同 PawnKind 的不同配置用稳定 id 区分，显示名和随机颜色不承担身份。

擂台 `Preparation` 用 entrant/role/group 过滤范围，步骤内用 entrant 占位符，先执行同步动作，再用 Require 或 DebugAction/Expect 核对。准备前 GeneratedSnapshot、逐步 Preparation、开战 InitialSnapshot 分开保留；合法换装不会重写原冻结预期，也不能通过删除证据让校验通过。核对公开 XML 字段，能力冷却、充能及未注册组件私有状态仍按原生初始化。

## 按目标读取

| 当前任务 | 必要参考 / 可改用的起点 |
| --- | --- |
| 查全部节点字段、父子层级、默认值和固定参数 | [field-index.md](references/field-index.md)，按需查询，不必整份加载 |
| 基础结构、角色继承、精确配装、地图环境 | [schema-and-actors.md](references/schema-and-actors.md)、[基础场景](assets/basic.autotest.xml) |
| 技能效果、战斗、配方、健康或物品生命周期 | [steps-and-observation.md](references/steps-and-observation.md) |
| 调用已加载模组公开准备接口、配置类型化参数与后置断言 | [action-bindings.md](references/action-bindings.md) |
| 矩阵、精确选择、补充/替代普通回归 | [selection-and-coverage.md](references/selection-and-coverage.md)、[远程矩阵](assets/ranged-matrix.autotest.xml) |
| 三视图、整套配装、暂存角色输入 | [views-and-staging.md](references/views-and-staging.md)、[配装视图](assets/loadout-view.autotest.xml) |
| 双方群战、整场继承、换边与伤害统计 | [groupcombat.md](references/groupcombat.md)、[群战实验](assets/groupcombat.autotest.xml) |
| 擂台三赛制、角色分组、人数/CP 加法与乘法增长、规则页及成绩核对 | [arena.md](references/arena.md)、[淘汰赛](assets/arena.autotest.xml)、[魔王轮战与极限挑战](assets/arena-challenges.autotest.xml) |
| 固定战场样式、双方队伍 AI 覆盖、群战出生阵型或擂台自动布阵 | [groupcombat.md](references/groupcombat.md#ai-与准备)、[arena.md](references/arena.md#地图ai-与出生阵型)、[地图字段索引](references/field-index.md#默认值枚举和模式限制) |
| 同一角色模板组合多人，群战与三种擂台复用 | [squads-and-rosters.md](references/squads-and-rosters.md)、[模板混编擂台](assets/arena-template-groups.autotest.xml)、[模板混编群战](assets/groupcombat-template-groups.autotest.xml) |
| 战力配兵、派系/自定义混编、异种与扩展生成器 | [squads-and-rosters.md](references/squads-and-rosters.md)、[混编群战](assets/groupcombat-squads.autotest.xml) |

这些参考按当前加载器、运行后端与测试夹具整理；宿主源码可用时，以 `ScenarioLoader`、`GroupCombatLoader`、`ArenaLoader`、`SquadTemplates`、`CombatFightService` 及地图生成实现为准。不要把历史策划案或旧夹具名称当作当前行为：最新实现默认 `assaultColony`，`battle/seekAndAttack/native` 均兼容到同一原生即时进攻控制器，已撤回早先自定义攻击 Job 调度。模板使用原版定义演示结构，Def、DLC、原生约束和实际画面仍需在宿主核对。

## 执行工作流

1. **确定要观察的结果。** 从用户请求和项目现有定义查出真实 packageId、Def 类型、DefName、必要 DLC/模组及原生前提。明确是效果回归、渲染展示、群战实验还是擂台赛事。只在缺失信息会改变测试含义时询问。
2. **最小化准备。** 优先继承真实 `basePawnKind`，只覆盖相关字段；将资源补充和调试准备放在实际动作之前。用 `Require` 核对关键前提，再以新的 `Checkpoint` 或动作自带基线开始效果观察。
3. **编写稳定输入。** 每条规则填写唯一 `testDefName`，局部对象与步骤使用稳定 `id`。重复配装用 `PawnTemplate`，重复环境用 `ScenePresets`；变体使用显式 Matrix。先跑一个实例再按需要扩展；不要为了通过而增加隐式冷却重置、无敌、强制成功或日志豁免。
4. **采用与目标匹配的判据。** 技能“接受/开始/完成”不等于效果发生；截图存在不等于画面正确；仅装备目标或填入目标名不等于执行了目标机制。使用原生状态、对象身份变化和实际观察窗口证明结果。`ApplyDamage` 只能证明受击机制，不能证明另一把武器命中。
5. **按已授权范围验证。** 编写或修改 XML 后先用相同模式、路径和测试名加 `--validate-only` 做静态预校验，读取 `validation.diagnostics` 的定位和建议；校验错误按首条逐次修复，warning 需核对。此入口不启动游戏、不部署、不创建运行产物，也不能证明游戏机制通过。普通运行同样自动执行这一步。只编写测试时交付输入与命令；用户已要求运行或验证时执行相应测试。先核对版本与依赖，使用范围最小的输入。须覆盖当前源码时用 `--deploy`，沿用 `rimdevtools` 的构建部署要求。开发工具宿主时，确认 csproj_paths 包含对应 AutoTestRunner 工程或先构建相应宿主；仅更新 CLI 或技能文件不会更新游戏中已安装的宿主。
6. **核对本轮实际执行。** 解析 stdout 最终 JSON，再读取工具返回的原始结果。核对选中的物理实例、缺失用例、失败断言、`scenario` 阶段、依赖与输入哈希；混合模式还需核对替代关系及 `verifiedCoverageKeys`。失败后根据原生前提、配置、测试方式和正常游戏可复现性区分原因。

## 命令起点

从相应 assets 复制到测试源码目录后修改身份和目标；以下路径与 `Example_*` 名称对应模板内容。`--deploy` 仅在需要包含当前项目变更时加入。

```powershell
# 先做静态预校验；群战/擂台换成相应 mode，仍须显式 --test-name
.\.tools\RimDevTools.exe test run --mode auto --scenario-path "Tests/Scenarios/basic.autotest.xml" --test-name Example_PawnAlive --validate-only

# 基础自定义回归；显式设置批量上限，不假定目录内所有实例都会执行
.\.tools\RimDevTools.exe test run --mode auto --scenario-only --scenario-path "Tests/Scenarios/basic.autotest.xml" --test-name Example_PawnAlive --batch-size 1 --timeout-seconds 360

# 矩阵回归；选择稳定规则名会保留其所有行作为候选，仍须核对实际选择
.\.tools\RimDevTools.exe test run --mode auto --scenario-only --scenario-path "Tests/Scenarios/ranged-matrix.autotest.xml" --test-name Example_RangedDamage --batch-size 2 --timeout-seconds 600

# 独立群战；必须显式指名，不使用 --scenario-only
.\.tools\RimDevTools.exe test run --mode groupcombat --scenario-path "Tests/Benchmarks/groupcombat.autotest.xml" --test-name Example_WolvesVsBears --timeout-seconds 900

# 混编群战；派系编队与自定义兵种池
.\.tools\RimDevTools.exe test run --mode groupcombat --scenario-path "Tests/Benchmarks/groupcombat-squads.autotest.xml" --test-name Example_SquadBattle --timeout-seconds 900

# 独立擂台；按整场赛程估算 CLI 上限，省略时工具计算默认预算
.\.tools\RimDevTools.exe test run --mode arena --scenario-path "Tests/Arena/arena.autotest.xml" --test-name Example_AnimalArena

# 魔王轮战；极限挑战可改选 Example_LimitCountMultiply / Example_LimitCountAdd
# CP 预算增长为 Example_LimitBudgetMultiply / Example_LimitBudgetAdd
.\.tools\RimDevTools.exe test run --mode arena --scenario-path "Tests/Arena/arena-challenges.autotest.xml" --test-name Example_BossRoundRobin
```

需要限制目标包时追加真实 `--target-package-id`；不要把示例的原版 packageId 当作用户模组的身份。普通 `--defname`、外部 `--test-name` 与 Coverage 的差异见选择参考。

## 结果与产物约定

预校验成功读 `result.validation`，XML 校验失败读 `error.details.validation`：`valid` 仅表示静态编译通过，`diagnostics` 给出 severity/code/message/suggestion 及可用的 file/line/column/element，`runtimeChecks` 列出仍需游戏确认的内容。校验复用执行加载器，覆盖 XML 结构、字段、引用、矩阵/继承、群战/擂台及混编配置的已有约束；Def、反射签名、定制 AI、实际生成与寻路等仍在游戏中验证。独立校验需项目 `config_version=2`、`game_version=1.7`，不依赖游戏/Steam/宿主；不要附加 deploy、batch-size、timeout 等执行参数。

- 日志、结果、截图和测试存档只使用 RimDevTools 自动生成的原始路径。成功读 `result.artifacts`，失败读 `error.details.artifacts`；常见字段为 `directoryPath`、`syncLogPath`、`autoTestOutputPath`。路径存在与文件已生成须另行核对，缺失就如实说明。
- 除用户明确要求导出外，禁止自建测试证据、任务或日期归档目录，包括 `.tools`、`.tools/log` 下的额外目录；不得换目录绕过。禁止重定向、`Tee-Object`、`Out-File`、`Set-Content` 或复制另存 stdout/stderr、日志、截图、测试存档，也不编写一次性归档包装脚本。
- 不修改原始证据，不补造缺失产物；保留与清理由工具负责。测试串行执行，运行中不编辑活动游戏配置、事务清单或请求/结果文件。
- 在聊天中报告选定范围、实际执行数、成功/失败/未执行、日志问题、关键原始路径与局限；除用户明确要求外不另写测试报告或校验文档。区分静态校验、纯逻辑验证和真实游戏运行，不能用前两者声称机制已通过。
- 运行时的断言失败、等待超时和探针异常读取原始结果 `results[].scenario.Diagnostics`；CLI 失败 JSON 的 `error.details.details.scenarioFailures[].diagnostics` 也会附带 code/message/file/line/element/stepId/xml/suggestion。优先按具体条件节点定位，不只看整条 Case 的 Source；无精确来源时不能把 Case 行号当作实际断言行号。CLI 默认最多预览 20 个带诊断的用例，`scenarioFailuresTruncated` 为 true 时用 `--verbose` 或读取原始结果。
- 普通机器 JSON 保留全部物理实例，Markdown 可能只列失败；不要因报告没有成功条目就判断它们未运行。群战与擂台保留全部参与者，胜负、执行/清理状态和测量质量分别报告，不并入回归通过率；擂台另核对赛程、重赛、评分分项与 Rules/Showcase；淘汰赛核对 Champion/TopFive，新赛制核对 Challenges/Summary、增长档位、角色分组及成绩分页，不套用淘汰赛冠军条件。
