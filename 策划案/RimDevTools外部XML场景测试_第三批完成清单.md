# 第三批完成清单

日期：2026-11-07。实现基线：`49fd226`，本清单随第三批代码独立提交。游戏实测为 RimWorld `1.7.5000`，宿主程序集 `1.2.7.0`，CLI `1.3.0.0`；实测加载 25 个模组，包含原版、DLC、HAR 和 `hentailoliteam.axolotl`。

用户最新调整：“这个弹窗的手动选择测试就之后让我自己测试吧……完成功能即可”。因此本批交付弹窗功能，相关手选/筛选/父子联动/重开等交互验收移交用户；下文不会将代码检查或少量后端样本写成全部交互已自动验收。

## 交付功能

- C09—C23、C28 全部视图类型接入外部 XML，支持对应实体、年龄/外观/基因、配装、环境、步骤、实际状态判据和图像身份。
- 复用原三视图摄影区、姿态稳定和相机服务；空位不足时使用临时普通主地图，完成后丢弃。视图不再使用口袋地图。
- 同一 Pawn 逐向拍摄，整套保留与单件隔离分别处理；支持第四方向、原图、横排合成图和配装清单。PNG 写完才合成，取景和方向不满足不会通过。
- 单格门保留原生墙体连接定向，作为静态建筑展示；不强制门的绘制朝向。
- 版本化角色 XML，模板哈希、录入/未录入范围、身体部位、原生能力与已注册资源；部分模板、原子替换和输入冻结。
- 恰好两个新顶层动作：暂存测试角色、自动测试。普通弹窗支持定义/当前实例、稳定多选、搜索与筛选、三大类父子选择、适用性预览、开始关窗、提交选择历史、取消和串行执行。
- 内置类别目录覆盖 38 个 CaseType；配方信息卡与账单/生产分别执行，技能归战斗，其他静态视图归三视图，整套配装有内置默认入口。
- 手动入口不读取手写场景 XML、上次 CLI 场景计划或 Coverage，不参与群战；角色数据按当前输入项目载入。
- 每次游戏进程独立手动会话，同次启动全部批次追加；输入项目切换不换输出会话，重启新建。自动完成信号、计数和产物保持独立；截图含执行身份，自动保留清理避开手动会话。
- 新生成展示者/工作者等优先使用暂存数据，源 Pawn 只读；实例输入在提交时冻结，不在测试中搬动或改装原对象。
- 手动队列按游戏 tick 推进，暂停期间处理界面和恢复，避免提前清理截图；全程关闭调试日志窗口。

使用方式、字段和边界见[第三批使用说明](RimDevTools外部场景XML第三批使用说明.md)。

## 实际验证

| 检查 | 实际结果 |
| --- | --- |
| 1.7 宿主与 CLI 构建 | 通过，0 警告、0 错误 |
| Core 检查 | 280 项通过；含视图参数、模板哈希/损坏、写失败保留、冻结、项目隔离和进程会话策略 |
| CLI 检查 | 602 项通过；含手动会话不被自动保留策略删除 |
| Tools 载荷 | 22 项通过；实际 DLL/清单匹配，不夹带 ProjectData、日志或测试历史 |
| 外部视图正例 | 17/17 通过，0 缺失；原版与 Axolotl 配装、16 类能力均有记录 |
| 受控错误 | 16 项均在明确错误期望处失败，后续恢复场景通过；17 项全部执行，清理 live=0、源地图恢复 |
| 原生角色数据验证 | 12/12 通过：源状态不变、新对象身份、基底/年龄、特性/技能、三件服装、主武器、Torso 上 Scratch、外观、哈希、原生能力/基因资源、重启后暂存数据载入、对象清理 |
| 既有自动入口 | 无场景 XML 的 Gun_Revolver、Steel 信息卡 2/2 通过；第二批浮云落地场景回归通过 |
| 已有自动结果保护 | 原生手动数据检查前后，正例与负例 Result.json 的 SHA-256 均未改变 |

原生角色数据验证创建并读取工具自己的受控角色，含 `Nimble`、Shooting 12、极佳防弹背心/钢头盔、精良突击步枪、Torso 上严重度 3 的 Scratch，以及 Biotech 的 `MetalbloodInjection` / Hemogenic 资源。实际生成和捕获使用正式生成/捕获服务，不通过另写一个返回固定值的测试接口代替。

跨进程检查的 GameLaunchId 为 `8b887e1c37e64ee990e6213c7eca57d0`；重新从磁盘载入的暂存模板哈希为 `371c0ece008e1ccba8a8cf0a7914bf10a063dd8b8907d90515f7449eb19c2bd8`，生成基底为 Colonist。原生数据诊断直接读取返回 JSON，`artifacts=null`；没有为它额外建立证据目录或另存 stdout。

## 逐类视图矩阵

以下正例见 `ScenarioBatch3Views.example.xml`。每类另有同名前缀加 `_WrongExpectation` 的错误期望，在 `ScenarioBatch3ViewsNegative.example.xml`；原始失败状态保留。

| 编号 | 类型 | 正例 testDefName 与实际参数 |
| --- | --- | --- |
| C09 | HumanoidThreeView | Batch3_HumanAppearance：Human、Male、完整原版衣物 |
| C10 | AnimalThreeView | Batch3_MuffaloAppearance：Female、5 岁、West/South/North |
| C11 | MechanoidThreeView | Batch3_ScytherAppearance：Mech_Scyther |
| C12 | AlienThreeView | Batch3_AxolotlAppearance：Axolotl_Colonist、Female、25 岁 |
| C13 | ApparelThreeView | Batch3_IsolatedVest：隔离防弹背心，核对其他衣物移除 |
| C14 | WeaponThreeView | Batch3_RifleReadyPose：实际持有突击步枪、持武器姿态 |
| C15 | PlantGrowthView | Batch3_RiceStages：稻米 0.2 → 1，分阶段图像 |
| C16 | BuildingView | Batch3_SteelDoor：钢门的生成与静态图像；按用户要求撤销门旋转试验 |
| C17 | TerrainPatchView | Batch3_ConcretePatch：6×4 混凝土区域，全边界取景 |
| C18 | PawnLifeStageThreeView | Batch3_YoungMuffalo：0.1 岁与实际生命阶段 |
| C19 | HairThreeView | Batch3_ShavedHair：Shaved、明确发色 |
| C20 | HeadTypeThreeView | Batch3_HeadAppearance：Male_AverageNormal |
| C21 | BodyTypeThreeView | Batch3_FatBodyAppearance：Fat 与实际衣物适配 |
| C22 | GeneThreeView | Batch3_BlueSkinGene：Skin_Blue、active、xenogene |
| C23 | XenotypeThreeView | Batch3_HussarXenotype：Hussar 实际基因/外观 |
| C28 | LoadoutView | Batch3_CompleteLoadout 与 Batch3_AxolotlCompleteLoadout：原版/模组配装保留、同对象四方向、合成图和清单 |

已直接打开原始图像检查角色、服装、武器、植物、建筑和地形。复测中特别修复了相机落到 Pawn 绘制层以下、PNG 文件尚被编码器占用、地形边界裁切、摄影区无空位和手动调度提前清理等问题。门强制定向的尝试已按用户指示撤回，不把它作为功能交付。

17 项正例运行中的门多方向图片属于撤回前的记录，不作为最终门定向能力的证据。撤回后对 `Batch3_SteelDoor` 的单张静态展示单独补验，`1475aae5f8914740abf9a542fa6bc767`，1/1 通过；最终代码没有门绘制朝向补丁。

## B3 / M 验收对应与移交

| 编号 | 当前证据与边界 |
| --- | --- |
| B3-T01 | 原生数据检查实际捕获、重建装备/技能/特性/Hediff/能力/资源，并核对源哈希及新身份 |
| B3-T02 | 新启动进程从既有 ProjectData 载入暂存角色并正常生成；不依赖原 Pawn 对象 |
| B3-T03 | 项目 A/B 文件隔离、首次输出路径固定已由实际文件/会话策略检查覆盖；游戏内项目切换交互移交用户 |
| B3-T04 | 部分模板、损坏/错误字段/哈希、写入失败保留旧文件有逻辑检查；无绑定和不可写路径具备明确诊断 |
| B3-T05 | 两个动作及弹窗已实现；早期原生入口曾实际打开，完整交互/布局验收按最新要求移交 |
| B3-T06 | 同一后端业务提交实际多目标、多类别，适用项执行、不适用项单列；手选过程移交 |
| B3-T07 | 实际进程会话 `6867a9d...` 与 `797a5e0...` 使用不同会话/路径；同一进程的多批记录保留，纯策略检查覆盖重启与输入项目切换 |
| B3-T08 | 自动 JSON 结果哈希前后不变；手动任务使用独立 ID，不发自动 COMPLETED 信号 |
| B3-T09 | 进程级静态会话不随存档重建，执行互斥已接入；回菜单/切存档及忙碌弹窗交互移交用户 |
| B3-T10 | 原版配装及 Axolotl 民服/武器正侧背与第四方向原图已检查，配装身份保持一致 |
| B3-T11 | C09—C23、C28 有逐类真实正例与受控失败；两条整套配装规则不覆盖彼此 |
| B3-T12 / M01—M05 | 全部内置类别映射和批量执行功能已交付；真实样本包括 Pawn/Thing 信息卡、武器三视图和整套配装。没有宣称 38 类全部经过逐项手选实测 |
| B3-T13 / M06 | 稳定选择、父子联动、开始关窗与历史恢复已实现；完整交互验收由用户后续进行 |
| B3-T14 | 手动初始化独立创建普通设置，场景计划为空；真实业务调用没有使用手写测试 XML，实际复用暂存数据 |
| B3-T15 | 初期手动汇总保留了失败后继续执行的记录；时钟问题修复后后端视图通过。外部负例 16 失败后恢复场景通过 |

## 原始产物与命令

均位于项目原始 `.tools/log`；未复制日志、图像、存档或结果，没有另建任务/证据目录。

| 用途 | runId / 原始目录 |
| --- | --- |
| 外部正例 17 项 | `2e3b10f176d74a888d0acfb911c379c1`；`.tools/log/[2026-11, 17-17]/AutoTestOutput/ludeon.rimworld_2e3b10f176d74a888d0acfb911c379c1_Result.json` |
| 最终门静态展示 | `1475aae5f8914740abf9a542fa6bc767`；`.tools/log/[2026-11, 17-42]/AutoTestOutput/ludeon.rimworld_1475aae5f8914740abf9a542fa6bc767_Result.json` |
| 受控失败与恢复 | `797ac54b394d423fb19e73546d880848`；`.tools/log/[2026-11, 17-26]/AutoTestOutput/ludeon.rimworld_797ac54b394d423fb19e73546d880848_Result.json` |
| 无 XML 自动回归 2 项 | `43d35c5e6f7045d9bdabcf57f10108d0`；`.tools/log/[2026-11, 17-33]/AutoTestOutput/ludeon.rimworld_43d35c5e6f7045d9bdabcf57f10108d0_Result.json` |
| 第二批场景回归 | `1a8f5befca034fec8f54392bdf6745b3`；`.tools/log/[2026-11, 12-51]`，若后续保留清理移除则以实际文件存在性为准 |
| 早期手动汇总 | ManualSessionId `4ca282de52364b4f8c4096bcb9db12cf`；`.tools/log/[2026-11, 12-36]/AutoTestOutput/manual_4ca282de52364b4f8c4096bcb9db12cf_Result.json` |
| 暂存角色后端样本 | ManualSessionId `b68b94ccbece4972a6ba4961be1c84a3`；`.tools/log/[2026-11, 12-47]/AutoTestOutput/manual_b68b94ccbece4972a6ba4961be1c84a3_Result.json` |
| 原生数据检查宿主 | `8d3f9e60b19e414387d72610cfb535e4`；CLI 返回 `.tools/log/[2026-11, 17-36]`；具体数据检查采用原生接口 stdout JSON |

正/负例命令使用 `test run --deploy --mode auto --scenario-only --scenario-path "AutoTest.Core.Tests\Fixtures\ScenarioBatch3Views.example.xml"`（负例换对应文件），目标为 `ludeon.rimworld`，`--no-isolated-desktop --timeout-seconds 600`。普通回归使用 `--scope ui --defname Gun_Revolver --defname Steel`，不传场景参数。

原生数据检查通过受鉴权本机接口 `POST /v1/tests/manual` 的 `verify-pawn-data` 调用，直接复用捕获与生成服务；没有执行弹窗筛选/选择自动化。源/克隆 ID 为 `Thing_Human52739`、`Thing_Human52748`，受控模板哈希 `9dcbe1d514dd37dfb7b1055268eb5589ae4417f5954d066925fd30c892b54774`，12 项实际检查均为 true。

第三方启动阶段仍有 Axolotl 1.6 测试定义引用缺失类型的既有错误；没有修改第三方文件或将它表述为工具兼容已经全面通过。最终正例用例没有因此被伪造为失败或隐藏自身异常。
