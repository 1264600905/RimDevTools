# 第四批完成清单

日期：2026-11-07。实现基线：第三批提交 `6a4d016`；本清单随第四批代码单独提交，不预填自身提交哈希。实测游戏为 RimWorld `1.7.5000 rev1275`，LogSync 宿主 `1.2.7.0`、CLI `1.3.0.0`，加载 25 个模组，包括原版/DLC、HAR、`hentailoliteam.axolotl`。第三批普通弹窗交互仍按既有约定移交用户，本批不替它宣称验收完成。

## 交付功能

- C29 独立 `groupcombat` 入口，必须指名稳定规则、完整实例 ID 或唯一显示名；普通回归拒绝群战，双方之外的配置执行前报错。
- 复用外部模板、环境、固定布局、生成及准备服务；支持暂存角色文件导入与内容/依赖哈希冻结。按明确人数和网格/直线阵型生成，最多 300 人。
- 每轮新建普通主地图和两个临时阵营，左侧与玩家双向敌对；双方独立选择原生群体 AI，默认 assaultThings，可用 seekAndAttack 调度原生攻击/技能及距离外接敌。
- 模拟 tick 共同计时，默认 180 秒、一倍速，XML 可配置速度；支持原生暂停并冻结战斗与存活计时；全倒、同时全倒、超时存活数比较及独立实际时间上限。取消、准备错误和运行异常保留状态并清理，失败后继续后续轮次，清理失败则停止复用残留。
- 原生 TakeDamage 结算事件账本，逐单位、双方和矩阵核对；独立记录友伤、自伤、环境、未知来源、原生护盾、护甲/物品及建筑通道。嵌套转发与反伤分开处理，不用日志去重合并伤害。
- 个人 DPS 按自身在场存活时间，面板人均 DPS/承伤仅对当前场上存活者（含倒地）求平均；完整分布保留所有初始单位，含阵亡与零值；原始精度计算极值、单位 ID、平均及中位数。事件明细容量不影响累计值。
- 半透明左右 Top10、存活编组前五种、真实人类角色图及实时汇总；共同刻度、250 毫秒刷新、无滚动条的紧凑布局与点击穿透。独立缓存初始人类头像，阵亡或对象销毁后仍保留排行图像，清理时释放缓存；动物/机械族对象销毁后回退到物种 Def 图标。
- 显式种子、镜像换边、每轮独立身份/地图和跨场分布；全量数值 JSON、Markdown 与原始截图使用本次正式产物，不进入回归通过率。自动保留清理跳过群战和手动会话，群战也不清理已有回归产物。
- 命令启动的 quick/auto/groupcombat/manual 共用原生退出门控，核对活动会话身份、后续主线程心跳和写入宽限；普通游戏内手动测试不会触发退出。唯一地图释放后也通过该门控退出。

最新追加规则：专用相机按存活单位边界进行广角跟随，中心和缩放均平滑过渡，保留缩放余量并隔离战斗震屏；按真实投影适配 CameraPlus，不直接把 RootSize 当作可见世界范围。群战默认无边框全屏，XML Output 尺寸只控制截图生成，避免游戏窗口缩小。单位面板状态为“存活 / 倒地 / 死亡 / 离场 / 移除”。

字段、命令和边界见[第四批使用说明](RimDevTools外部场景XML第四批使用说明.md)。

## 构建与纯逻辑验证

| 检查 | 实际结果 |
| --- | --- |
| Core | 340 项通过，包含独立入口、唯一规则身份、重名、网格边界、AI 参数、换边、已知伤害序列、奇偶中位数、零值、提前死亡、暂停、同 tick 结算及 20000 次增量事件 |
| CLI | 617 项通过，含结果身份/时钟/人数/矩阵/DPS/分布校验，及自动保留策略保护独立群战会话 |
| 1.7 宿主构建 | 0 警告、0 错误；实测前明确构建 AutoTestRunner，部署后核对游戏 DLL 与源码构建 SHA-256 一致 |
| Tools 发行载荷 | 22 项通过；1.6/1.7 宿主与源码构建一致，不夹带 ProjectData、日志或测试历史 |

实际执行 `dotnet run --project AutoTest.Core.Tests/AutoTest.Core.Tests.csproj -c Release`、`dotnet run --project RimDevTools.Tests/RimDevTools.Tests.csproj -c Release`、`dotnet build source/AutoTestRunner/AutoTestRunner.1.7.csproj -c Release`。CLI 检查中的故意编译错误属于既有负例，最终 617 项通过。

最新增量验证：Core 340、CLI 617、Tools 22 项通过，1.7 宿主 0 警告/错误。Core 增加存活计时、移除后返回、零存活、当前存活者均值与旧元数据拒绝、相机安全边界、平滑收敛/无越界/长帧间隔不瞬移。原生 20 对 20 人类实战在 2595 ticks 全倒结束，702 次结算，双方对敌量 1028.6361 / 1548.0324，当前存活均值按逐单位重算一致。随后两种输出尺寸的真实进攻分别 233 / 198 次结算，暂停各 3 秒时 ticks 分别保持 12 / 84；当前窗口均为 1920×1080 FullScreenWindow。小尺寸输出为全屏帧缩放，不再声称切换为小窗口布局。

## B4 验收对应

| 编号 | 实际输入、调用及结论 |
| --- | --- |
| B4-T01 | CLI 未指名返回 GROUPCOMBAT_NAME_REQUIRED，错误名称返回 SCENARIO_INVALID，auto 显式载入群战被拒绝；Core 验证重名、三/四队和完整实例选择。均未启动错误评测 |
| B4-T02 | 首先 Batch4_HumanFirefight 的 20 对 20 人类接敌，再验证 Wolf_Timber/Bear_Polar 与 Mech_Scyther/Mech_Lancer；逐单位、状态和分布完整，不要求指定胜方 |
| B4-T03 | 实测 group.player_relation 与 group.ai 记录真实关系/LordJob；清理记录 preexistingUnchanged=True、temporaryRelationsRemoved=True，既有阵营关系未改动 |
| B4-T04 | 20 人队伍输出/承伤 Top10 与状态原图已读；每方六种明确配装、12 人的榜单显示前五种和“其余1种”，同 PawnKind 不因随机外观拆组。人类死亡后头像修复已用最终战斗原图复验 |
| B4-T05 | 最新 Batch4_Follow1280/1920 均改为双方 assaultThings 进攻；全屏生成 1280×720、1920×1080 原图，无滚动条；左右 MouseDown/MouseUp/ScrollWheel 共 12 项原生 GUI 不消耗事件检查通过。XML 截图尺寸不改变 1920×1080 无边框全屏窗口。先前 Overlay 防御僵持图仅为历史布局/计时验证，不能当成进攻证据 |
| B4-T06 | 两个无接敌防御场景准确运行默认 10800 ticks，0 事件、同数平局；多种子场景自定义 720 ticks，人类/动物/机械族真实全倒结算。不同存活数、同 tick 双方全倒、奇偶分布、零值/死亡成员和暂停由 Core 已知序列与原生数据检查分别验证 |
| B4-T07 | 受鉴权 POST /v1/tests/manual 的 verify-groupcombat-data：同 tick 同额、共享结果的嵌套转发、反伤、友伤/自伤/环境/未知来源、原生 CompShield、独立调用复用结果对象全部通过 |
| B4-T08 | 同一原生检查累计 20013 事件，只留 4 条明细；A 方对敌量 20040，B 方 2，矩阵与累计/DPS 一致，清理及原地图恢复通过 |
| B4-T09 | 已实测完成、默认/自定义模拟限时、僵持、主动取消、缺失 Def、准备条件异常、实际时间超时、运行期异常和失败后恢复。受控异常在真正运行的 Tick 中触发，冻结于 5584 ticks，清理完成；下一张独立地图恢复轮正常完成 120 ticks。verify-groupcombat-unique-map 的 noMaps/noCurrentMap/noArenaParent/arenaDestroyed 四项为 true，并取得 CLI_TEST_EXIT，唯一地图会话正常退出 |
| B4-T10 | Batch4_HumanRepeat 的种子 101/202 × 原位/换边共四轮，每轮独立地图、caseId 和统计，均 720 ticks 完成、清理成功；跨场分布按规则/队伍分别计算 |
| B4-T11 | 前三批与普通 UI 混合回归 5/5 通过；三次独立群战前后只读复核 17 份已有 Result.json 的 SHA-256，全部一致、0 缺失，另新增本次三份独立结果；未复制或改写既有结果 |
| B4-T12 | 必填 testDefName、同配置不同规则、显示名歧义、完整矩阵 ID 和重复实验实例由 Core 覆盖；实际输出保留规范规则名及独立 round ID，不计入回归通过率 |

原生数据探针的受控 DamageWorker 只用于验证真实 TakeDamage 调用与结算返回值的传递/归属，不能据此声称已验证真实伤口净损失。真实战斗和真实原生护盾另有独立记录。唯一地图探针仅允许空闲的 CLI manual 测试进程，移除该进程的临时 quicktest 源地图以实际进入无安全源地图的分支。

## 原始产物和实际命令

以下目录均由 CLI 的 result.artifacts 或 error.details.artifacts 返回，未自建证据目录、复制图像/存档或另存 stdout。原生探针直接读取 stdout JSON，artifacts=null，未额外写报告。

| 用途 | runId / 原始 `.tools/log` 目录 |
| --- | --- |
| 最新平滑相机、全屏、暂停、穿透及进攻 | 8cd6c3c6af2f4d3a802d9ae8df14c238；`[2026-11, 20-31]`；两轮 2959 / 2700 ticks，233 / 198 事件，均清理完成；默认 normal / 显式 fast；两个输出尺寸原图已直接读取 |
| 当前口径 20 对 20 双方进攻 | c5786fd5f55840cab11b1b11865c5896；`[2026-11, 20-23]`；2595 ticks、702 事件，清理完成，双方实际输出与存活均值复核 |
| 人类战斗及四轮重复/换边 | eb353ec25e234843a759e317f938093b；`[2026-11, 19-18]`；5 轮完成 |
| 最终动物/机械族复验 | b38aafbd9a74442dafd49d980bddfbd9；`[2026-11, 19-42]`；动物 1590 ticks/122 事件、机械族 2890 ticks/121 事件，普通主地图及关系清理均通过 |
| 非人类阵亡图标最终复验 | f32c723956274ce48bdacf987bb9a6d6；`[2026-11, 19-45]`；机械族 1468 ticks/77 事件完成并清理，直接读取最终原图确认全灭方 Top10 仍有 Def 图标 |
| 两种分辨率、六编组及默认僵持 | baa6943ea8544cafb8cb3eacbac2b445；`[2026-11, 19-20]`；2 轮均 10800 ticks |
| 准备失败/条件错误/超时/恢复 | a41fd96a51674dff9b69d5752c49f00c；`[2026-11, 19-23]`；4 轮全执行，前三轮保留预期失败，恢复轮完成，全部清理成功 |
| 原生伤害探针最终复验 | 105c9c32acf348ec84e70643c668a907；`[2026-11, 19-34]`；9 项通过，数据直接读取 JSON |
| 主动取消及待执行轮次停止 | 75defb82f1454b49819c30e5a5c4d0b7；`[2026-11, 19-26]`；当前轮 cancelled、清理完成，下一轮未执行 |
| 最终人类头像及关系恢复 | b7a67fcfc7eb47f7bb10949161d77d4c；`[2026-11, 19-28]`；2083 ticks、679 事件、清理完成 |
| 运行期受控异常及恢复 | 6413721e34a441af9cd6669823e16825；`[2026-11, 19-38]`；原生 verify-groupcombat-execution-fault 调用，错误轮及恢复轮均清理成功，原有/临时关系核对通过 |
| 暂存角色导入 | b9fe5ff3e6e7454b9184fcd628fdb721；`[2026-11, 19-01]`；双方各 3 人、1200 ticks 完成，冻结角色依赖，清理成功；本轮确认原文件仍存在 |
| 唯一地图退出 | ec28e26a29f14c13a5e98793124ba1f7；`[2026-11, 19-30]`；原生四项通过及退出日志 |
| 第一批单独回归 | bac4a2ee4fc840e8a6b2a0060928ddc8；`[2026-11, 19-32]`；1/1 通过 |
| 前三批及普通 UI 混合回归 | 74c72a723e904576b0b946b6ad985265；`[2026-11, 19-36]`；5/5 通过，0 缺失 |

群战结果位于各目录 `AutoTestOutput/groupcombat_<runId>_Result.json`，同前缀 `_Report.md` 与 `_Screenshots/0001_initial.png`、`0001_final.png` 为工具原始产物。普通回归结果前缀为 `ludeon.rimworld_<runId>`。manual 诊断进程未分配 AutoTestOutput 文件夹时不创建替代目录。

群战命令使用 `test run --mode groupcombat --scenario-path AutoTest.Core.Tests/Fixtures/<输入文件> --test-name <规则名>`，均加 `--no-isolated-desktop --timeout-seconds 600`，需要更新宿主时加 `--deploy`。输入分别为 GroupCombatHumans、GroupCombatSpecies、GroupCombatAcceptance、GroupCombatFollow 和 GroupCombatStagedPawn 的 `.example.xml`。

混合回归使用 `test run --mode auto --scenario-path AutoTest.Core.Tests/Fixtures/ScenarioBatch4Regression.example.xml --target-package-id ludeon.rimworld --scope ui --defname Gun_Revolver --defname Steel --no-isolated-desktop --timeout-seconds 600`；样例复用前三批规则并给模板/场景局部引用增加前缀，保持原 testDefName，覆盖原生 MetalbloodInjection、Axolotl_SkillMove、完整配装四方向与两个普通信息卡。

## C01—C29 收尾核对

| 矩阵行 | 已交付类型与验收归属 |
| --- | --- |
| C01—C04 | TurretPocketMap、RangedWeaponPocketMap、MeleeWeaponPocketMap、ApparelCombat；第二批完成清单及对应逐类正/负例 |
| C05—C08 | AnimalCombat、MechanoidCombat、AlienCombat、SkillPocketMap；第二批逐类验收，本批另复验 Axolotl_FloatingCloudLanding 的真实技能阶段 |
| C09—C14 | Humanoid/Animal/Mechanoid/Alien/Apparel/WeaponThreeView；第三批逐类正/负例 |
| C15—C18 | PlantGrowthView、BuildingView、TerrainPatchView、PawnLifeStageThreeView；第三批逐类验收，门仅静态展示，不宣称绘制朝向支持 |
| C19—C23 | Hair/HeadType/BodyType/Gene/XenotypeThreeView；第三批逐类正/负例 |
| C24—C27 | 配方实际执行、健康效果功能、尸体生命周期、普通 Thing 行为；第二批逐类正/负例，保留 UI 与功能边界 |
| C28 | LoadoutView；第三批原版/Axolotl 全配装，本批 Batch3_CompleteLoadout 回归通过 |
| C29 | 独立群战；本清单 B4-T01—T12，独立规则与实例身份 |

本批没有以群战成功替代前三批逐类型验收。前三批完整历史证据以各自清单为准，工具保留策略可能已移除其中较旧原始路径；不为此补造文件。普通弹窗 M01—M06 的交互移交继续有效，不将本次 CLI 混合回归记作全类别手选通过。

## 支持边界和交付范围

- 对敌引擎结算量、执行状态和测量质量分开。通用后端仍为 partial，显式采集异常为 incomplete；requireCompleteAttribution=true 未满足时单独失败。
- 原生 CompShield 完全吸收量单列；第三方护盾、直接 Hediff 修改、流血及绕过 TakeDamage 的持续伤害不猜量/归因。共享伤口对象的聚合包装有明确不完整诊断。首版不新增召唤物归属、第三方队伍或阵营转换专项支持。
- 每次加载仍保留 Axolotl 的旧 1.6 LogSyncAutoTestSpecDef 启动错误及翻译警告，未修改第三方，也未把这些启动问题写成群战采集失败。受控负例保持失败原样。
- 本批文件覆盖共享 XML/群战契约、Core/CLI 检查、独立执行/伤害/榜单/报告、普通主地图恢复、宿主完成门控、样例及说明/计划更新。没有推送或公开发布。

最新六配装实战另出现原生 disabled stat ShootingAccuracyPawn 一次一致性错误，未隐去：与既有 Axolotl 旧 Def 启动错误分别保留在正式日志中；两轮数值校验、清理和正常退出均成功，不能将其描述为零游戏日志错误。
