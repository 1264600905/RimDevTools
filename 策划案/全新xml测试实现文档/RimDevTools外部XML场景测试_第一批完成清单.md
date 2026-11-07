# 外部 XML 场景测试：第一批完成清单

完成日期：2026-11-07。源码基线：`c60a1a6`，本清单与本批实现一起提交。

范围依据：[四批实施计划](RimDevTools外部XML场景测试四批实施计划.md)第 4 节。正式语法、字段表及全部非 UI 类型的共享契约清单见[第一批使用说明](RimDevTools外部场景XML第一批使用说明.md)。第二至四批尚未实施。

## 已交付

- [x] 1.7 外部文件/非递归目录入口、严格 XML 校验、模板继承与循环检查、输入清单/原文件哈希/内容冻结。
- [x] 必填稳定 testDefName、跨文件/Suite 大小写不敏感去重、独立 caseId、多目标关联、源文件/行/字段定位。
- [x] 原生 PawnKind 生成及部分模板、指定阵营；精确武器、帽子/服装、特性、工作技能、Hediff、Ability 与外观覆盖；共享 Def 保持只读。
- [x] Pawn、稳定组成员、建筑、Thing、真实死亡生成的尸体与 innerPawn；相关 Def 和地形区域引用。
- [x] 原生攻击与施放、准备/装备/健康动作、基线、组合条件、量词、变化身份、窗口观察、截图。
- [x] tick 和原生事件观察；旧 Hediff 严重度变化及旧装备掉落不作为新增实例；施放接受/开始/完成与效果分别记录。
- [x] 实际时间超时、错误分类、取消、对象归属、清理、失败后续跑；复用十秒镜头恢复与 CLI 心跳/总超时。
- [x] 复用现有正式结果、日志去重和只列失败的 Results/Screenshot Gallery；机器结果保留完整场景阶段。
- [x] 最近实际启动的自动测试项目上下文独立持久化，事务恢复后仍可读取。
- [x] 按用户追加要求，全部 CLI 测试会话和游戏内自动测试期间关闭已打开的调试日志窗口，阻止重新添加；日志继续保留。

## 构建与自动检查

实际游戏为 **RimWorld 1.7.5000 rev1275**，工具 CLI 1.3.0，运行器/LogSync 程序集版本 1.2.7.0；使用 .NET 10。实测加载 25 个模组，包含原版、五个 DLC、Harmony、SmartSpeed、Axolotl 及原有配置中的辅助模组。

```powershell
dotnet build source/AutoTestRunner/AutoTestRunner.1.7.csproj -c Release --no-restore
dotnet build RimDevTools/RimDevTools.csproj -c Release --no-restore
dotnet run --project AutoTest.Core.Tests/AutoTest.Core.Tests.csproj -c Release
dotnet run --project RimDevTools.Tests/RimDevTools.Tests.csproj -c Release
```

两项构建均为 0 警告、0 错误。核心检查 **192 项通过**，CLI 检查 **587 项通过**，`git diff --check` 通过。CLI 全套中包含预期编译失败的负例；一次并行实测时已有 XML 热重载时序检查失败，停止并行后原样重跑通过，未放宽断言。

## 第一批验收

“核心检查”指纯逻辑/受控后端检查；“实测”指真正启动 1.7 游戏，二者分别记录。

| 编号 | 验证与结论 |
| --- | --- |
| B1-T01 | 核心检查验证单文件、非递归目录、中文路径、未知字段/属性、重复 ID、循环；实测缺失 Def 在分配场景前报 `scenario.configuration`，包含源文件第 17 行及字段位置。 |
| B1-T02 | 实测 SpaceRefugee 原生配装后仅覆盖主武器和 Overhead 帽子，并设置 Tough、Shooting=14、Metalblood。原生生成记录和覆盖记录可对照，未指定服装和其他状态保留。 |
| B1-T03 | 同场生成玩家 Pawn、两名同组 Pawn、钢墙、钢材及正常死亡的尸体；尸体和 innerPawn 分别检查，结束后 owned live=0、arenaRegistered=False、sourceRestored=True。 |
| B1-T04 | 实测 Anomaly 的 MetalbloodInjection。先移除初始 Metalblood，再施放；分别记录 accepted/started/completed、新 Hediff 实例与原生 15000 tick 冷却，未强制可用、补次数或清冷却。 |
| B1-T05 | 实测已有 Hediff 移除、新增效果、持续 1 tick 的短暂效果；掉落旧手枪不满足新增条件，随后真正创建的新手枪满足。核心检查另验证旧 Hediff 严重度变化不算新实例。 |
| B1-T06 | 实测错误期望、暂停后的实际时间超时、失败清理及后续恢复场景；核心检查覆盖零目标、探针异常、部分准备失败、取消、清理异常和超时后迟到成功。取消检查没有冒充游戏现场取消实测。 |
| B1-T07 | 不传场景参数，原有 `--scope ui --defname Steel` 实测 1/1 通过；核心报告检查验证去重、失败画廊及全通过时无明细。 |
| B1-T08 | 本项目实际自动测试启动后，配置恢复仍能读取 Config 下独立的最近项目 JSON；CLI 检查验证自动项目身份不被手动设置或事务恢复覆盖。 |
| B1-T09 | 部分模板连续生成，记录基底 Def 未改；玩家阵营、无阵营和 AncientsHostile 均实际保留。NPC 场景也显式解雾，不依靠玩家单位提供视野。 |
| B1-T10 | 核心检查拒绝缺失/空白测试名、同文件、跨文件/不同 Suite 及大小写冲突，并指出定义位置。 |
| B1-T11 | 实测两条不同规则均关联 MetalbloodInjection，其中第一条同时关联手枪；产生两条独立结果。核心检查验证移动文件不改变规则/实例身份，哈希与目标分开保存。 |

首版共享目录覆盖 C01—C29 的角色、目标、步骤、观察、截图和判据契约，使用说明逐类列出适用项及交付批次。**各类型的专用缺省适配、替代映射、参数矩阵、全部视图、手动弹窗与群战均未在本批宣称完成。**

## 真实运行与原始产物

正式命令的公共前缀为：

```powershell
dotnet RimDevTools/bin/Release/net10.0/RimDevTools.dll test run --deploy --mode auto --target-package-id ludeon.rimworld --timeout-seconds 360
```

| 运行 | 追加参数及结论 | 工具返回的原始结果 |
| --- | --- | --- |
| 最终正例 | `--scenario-only --scenario-path AutoTest.Core.Tests/Fixtures/Scenarios`；2/2 通过，含 NPC 阵营、基础状态/效果与物品动作验证；RunMode=ExternalScenario | [原始 JSON](<../.tools/log/[2026-11, 01-00]/AutoTestOutput/ludeon.rimworld_ae75c8d2c47c4a899fbce89afadb0e82_Result.json>) |
| 受控失败 | `--scenario-only --scenario-path AutoTest.Core.Tests/Fixtures/ScenarioFailures.autotest.xml`；3 项预期失败后，恢复用例通过，CLI 如实返回 AUTO_EVIDENCE_FAILED | [原始 JSON](<../.tools/log/[2026-11, 00-56]/AutoTestOutput/ludeon.rimworld_2beb6bcdc1154fa08ad78383be5c6021_Result.json>) |
| 既有入口回归 | `--scope ui --defname Steel`；1/1 通过 | [原始 JSON](<../.tools/log/[2026-11, 00-54]/AutoTestOutput/ludeon.rimworld_7cd84c24b9384ab7915b5fc0ccba69d0_Result.json>) |

截图与 Evidence 路径由各原始 JSON 指向同一次正式运行的原目录。已直接读取原图，确认人物、帽子和地形可见，调试日志窗口没有遮挡。未复制证据、另建证据目录或另存工具输出；保留和清理继续由工具负责。

实测启动日志存在已安装 Axolotl 1.6 内容中 `LogSyncAutoTestSpecDef` 无法解析的既有错误，以及翻译/依赖提示；最终一轮游戏初始化还出现过 `TicksAbs` 起始时间尚未设置的诊断，原始日志均保留。正例和兼容性样本的用例期未出现未预期严重日志。首轮发现的 Scribe 转义损坏、原生年龄/禁用技能约束、重复销毁及截图遮挡已修复并重新运行。

## 交接边界

第一批使用独立外部计划，普通测试的 scope/defname/batch-size 不与 XML 混用，CLI 提前说明不支持。补充/替代、固定 DebugAction 和类型专用适配从第二批继续，不能据此声称已经完成全部非 UI 自定义或全模组回归。没有公开推送或发布。
