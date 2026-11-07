# 外部 XML 场景测试：第二批完成清单

完成日期：2026-11-07。源码基线：第一批提交 `5c21e86` 加本批工作树；本清单随第二批实现独立提交，最终提交哈希以 Git 和聊天交付为准。

## 范围与版本

- [x] C01—C08、C24—C27 自定义战斗、主动/被动技能及功能执行。
- [x] 固定 DebugAction、场景局部单体/矩形选择、资源准备及后置验证。
- [x] 地图预设、局部环境覆盖、已加载生成器、地形/掩体/屋顶布局、通路和真实日照验证。
- [x] 参数矩阵、稳定实例身份、supplement/replace、精确键和物理额度、联合覆盖、CLI 完整性校验。
- [x] 配方功能与账单 UI、技能效果与 Gizmo UI 分开调度和计数；替代功能不承接 UI。
- [x] 日志去重、失败续跑、原始产物路径、对象清理及第一批契约回归。

实际游戏为 **RimWorld 1.7.5000**；CLI **1.3.0.0**，测试宿主程序集 **1.2.7.0**，.NET 10。加载 25 个模组，包含 Core、五个 DLC、Harmony、HAR、Smart Speed、RimHUD、Axolotl 等，完整列表和哈希在原始 Evidence 的 environment 中。

Axolotl 实际加载 Workshop `3292351432/1.6/Assemblies/Axolotl.dll`，程序集版本 `1.0.0.0`，About 标注 1.5/1.6；本次是该已加载内容在 1.7 上的机制验证，不宣称模组全面兼容 1.7。未改动被测模组。

## 构建和自动检查

| 实际执行 | 结果 |
| --- | --- |
| `dotnet build source/AutoTestRunner/AutoTestRunner.1.7.csproj -c Release --no-restore` | 0 警告、0 错误 |
| `dotnet build RimDevTools/RimDevTools.csproj -c Release --no-restore` | 0 警告、0 错误 |
| `dotnet run --project AutoTest.Core.Tests/AutoTest.Core.Tests.csproj -c Release --no-restore` | 240 项通过 |
| `dotnet run --project RimDevTools.Tests/RimDevTools.Tests.csproj -c Release --no-restore` | 600 项通过 |
| `.tools/publish-rimdevtools.ps1 -TemplateGameVersion Tools` | 本地工具载荷构建完成 |
| `dotnet run --project RimDevTools.ReleaseTests/RimDevTools.ReleaseTests.csproj -c Release -- artifacts/RimDevTools-1.3.0.0-Tools` | 22 项通过；两版宿主匹配、ProjectData/日志排除正确 |
| `git diff --check` | 通过 |

打包期间一次检查正确发现包内 DLL 落后于后续更新的源码；重建载荷后重新执行，22 项全部通过。构建、部署及包内 1.7 AutoTestRunner 的实际哈希已只读核对，不复制证据。没有推送或公开发布。

## 真实运行与正式产物

以下根目录均取自 CLI 返回 JSON 的 artifacts；各根目录中 `精选日志.log` 是原始日志，`AutoTestOutput` 保存原始 Result、manifest、Evidence、截图和 Markdown 报告。

| 标识 | runId | 工具原始目录 | 已核对结果 |
| --- | --- | --- | --- |
| P：完整正例 | `1cb18f0207004ac295030f1178d0af73` | `.tools/log/[2026-11, 11-40]` | **23/23 通过** |
| N：受控失败与恢复 | `1b5b657f3f564993bfc4ba16cd697d16` | `.tools/log/[2026-11, 11-35]` | **18/18 执行，17 项预期失败，最后恢复场景通过**；verifiedCoverageKeys 为空 |
| J：联合替代、UI 和范围外补充 | `e810c0c4ef24407685cb7542fc9bb58c` | `.tools/log/[2026-11, 06-06]` | **4/4 通过**；1 个联合 Case 承接 2 项远程测试，2 项 UI 独立保留，最后执行范围外 XML |
| S：技能替代与独立 Gizmo UI | `ae68cf4322dd41929027d30a034ec878` | `.tools/log/[2026-11, 11-38]` | **2/2 通过**；1 项 XML 技能效果与 1 项 AbilityGizmoUi，覆盖仅含技能键 |

根目录绝对前缀为 `D:/RimworldProject/环世界日志同步mod/`。例如 P 的 Result 为：

```text
D:/RimworldProject/环世界日志同步mod/.tools/log/[2026-11, 11-40]/AutoTestOutput/ludeon.rimworld_1cb18f0207004ac295030f1178d0af73_Result.json
```

P/N/J 的文件前缀为 `ludeon.rimworld_<runId>`，S 为 `hentailoliteam.axolotl_<runId>`。Evidence 和报告由工具直接写在对应 `_Screenshots` 目录。交付时核对原始路径；之后保留/清理由工具负责，不建立归档副本。

输入和复现：

```powershell
# P：正式正例含全部本批类型、资源动作、环境
dotnet RimDevTools/bin/Release/net10.0/RimDevTools.dll test run --deploy --mode auto --scenario-only --scenario-path "AutoTest.Core.Tests/Fixtures/ScenarioBatch2AcceptancePositive.autotest.xml" --batch-size 100 --target-package-id ludeon.rimworld --timeout-seconds 600
# N：故意失败，CLI 返回 AUTO_EVIDENCE_FAILED 是预期
dotnet RimDevTools/bin/Release/net10.0/RimDevTools.dll test run --deploy --mode auto --scenario-only --scenario-path "AutoTest.Core.Tests/Fixtures/ScenarioBatch2AcceptanceNegative.autotest.xml" --batch-size 100 --target-package-id ludeon.rimworld --timeout-seconds 600
# J：精确选择两条规则，混合 UI/远程范围
dotnet RimDevTools/bin/Release/net10.0/RimDevTools.dll test run --deploy --mode auto --scenario-path "AutoTest.Core.Tests/Fixtures/ScenarioBatch2Joint.example.xml" --test-name Batch2_JointReplacement --test-name Batch2_OutOfRangeHediff --scope ui --scope rangedweapons --defname Gun_Autopistol --defname Gun_Revolver --batch-size 4 --target-package-id ludeon.rimworld --timeout-seconds 600
# S：替代效果不移除原有 Gizmo 检查
dotnet RimDevTools/bin/Release/net10.0/RimDevTools.dll test run --deploy --mode auto --scenario-path "AutoTest.Core.Tests/Fixtures/ScenarioBatch2SkillReplacement.example.xml" --scope skillpocketmaps --defname Axolotl_Thunder_SuperSkill --target-package-id hentailoliteam.axolotl --timeout-seconds 600
```

均通过工具原生执行和截图验证，未使用电脑控制技能。

## B2 验收编号

| 编号 | 输入、实际验证与结论 |
| --- | --- |
| B2-T01 | P 的 `Batch2_DebugRectangleAndSourceIsolation`：敌方矩形命中 2 人，全场景框内命中 3 人；框外角色未附加效果。debug.scope 记录源地图原有角色、实际目标及 sourceUnchanged=True。单体由资源准备等用例验证。 |
| B2-T02 | P 实际执行全部登记动作：fillNeeds/fillHemogen/setPsyfocus/fillQi/refuel/chargeBattery/healInjuries/addHediff/removeHediff，并核对后置状态。N 中缺少有效血原质基因明确失败；Anesthetic 请求严重度 1000 被原生截断，精确后置验证拒绝。未知/交互式未登记动作、错误参数和非法选区由核心检查拒绝。 |
| B2-T03 | N 的 `Batch2_ExpectedDebugInterferenceRejection`：DebugAction 添加 AlcoholHigh 后，旧基线的“新增效果”归因被拒绝，保留 before/after 和 debug.interference；无绿色覆盖。 |
| B2-T04 | P 的 Longjump：Sanguophage、血原质填充、原生施放、到达指定坐标及血原质实际消耗；N 中缺少基因时明确拒绝。另验证 Deathless 被致命心脏伤害触发 RegenerationComa。 |
| B2-T05 | P：浮云渡 `Axolotl_SkillMove` 到达指定地面坐标；螈气斩 `Axolotl_SwordAura` 造成伤害；`Axolotl_Thunder_SuperSkill` 新增 `Axolotl_ThunderStrengthen`。准备 5 级经脉及 500 螈力，原生费用、冷却和效果分开记录。S 独立保留 UI。 |
| B2-T06 | P：室外预设、Concrete 区域、Cloth Sandbags、屋顶矩形及可达性；引用已加载的 OutdoorPocketMap；自然 sunGlow/skyGlow 与太阳能实际输出共同验证。室内覆盖为全屋顶和 18°C。截图原图已读取。 |
| B2-T07 | P 的双武器矩阵各自原生攻击、伤害及证据；J 的两名射手分别命中，联合 Case 两个目标都有 coverage.target_used 和独立断言。N 的“只拥有技能未实际使用”被完成阶段拒绝。 |
| B2-T08 | 核心检查覆盖 replace 冲突、UI 误覆盖、未知规则名、同名选择、空/错误证据；CLI 检查拒绝重复结果、缺失执行、未执行/不支持的绿色覆盖和错误变体。真实 `Batch2_ApparelExactVariant` 仅承接 `Apparel_PowerArmor@Colonist@Male`；错误 BodyType 变体在规划阶段明确失败，执行数和绿色覆盖均为 0。 |
| B2-T09 | N 的多个独立失败保留各自结果，之后恢复场景通过。13 次相同前置失败日志在报告中只有一份正文，Occurrences=13，关联 13 个 caseId。Results/画廊仅失败，机器结果保留恢复成功项；矩阵行和标题显示独立 caseId。 |
| B2-T10 | J 的 1 个联合 Case 承接 2 项，只占 1 个额度。双武器矩阵的真实上限 1 运行保留 2 个替代、ReplacementLimitExceeded=True，2/2 通过。核心检查同时核对这些边界。 |
| B2-T11 | J 上限 4 实际顺序为联合替代、两项 UI、范围外 Hediff；相同命令上限 3 时范围外规则以 batch-limit 排除，3/3 通过。拼错变体拒绝而非当作范围外补充。 |
| B2-T12 | 下表逐行正反例全部完成；普通配方曾以无 XML 的 Recipes 范围实测两条独立 UI/功能结果通过。Gizmo/UI 新独立用例亦通过，未将 XML 效果等同为 UI。 |

补充真实历史运行：变体 `e80caa1beb264ee79c1602c02c1929b6`、超限矩阵 `13022c8696a74fcdacc7f9a6fa948de6`、无剩余额度 `3f69f1e69726428d92b41227d7c27d4c`、普通配方 `c22e6e01dafa49cba46d2ea7a2f2cc4d`、错误覆盖 `f593a1639eca4a389f6689190b0c58ac`，当次 stdout JSON 和原始结果已核对；这些旧目录已被工具保留策略清理，不再提供失效文件链接，也未重建替代证据。

## C01—C08、C24—C27 逐类结果

所有正例见 P，同一原始结果中的完整 testDefName/caseId、冻结输入、参数和 trace 可逐项核对；相应受控失败见 N。表中的短名均以 `Batch2_` 开头。

| 编号 | 类型 | 正例定义与可观察结果 | 受控失败 |
| --- | --- | --- | --- |
| C01 | TurretPocketMap | TurretPocketMap_CustomDamage：钢制迷你炮塔、供电/燃料、敌方 Pawn、实际伤害 | 同类型 ExpectedAssertionFailure |
| C02 | RangedWeaponPocketMap | RangedDamage/autopistol/1、revolver/1：实际换枪、射击、墙体损伤 | 同规则 ExpectedAssertionFailure，两行分别失败 |
| C03 | MeleeWeaponPocketMap | MeleeWeaponPocketMap_CustomDamage：钢剑、相邻接敌及损伤 | 同类型 ExpectedAssertionFailure |
| C04 | ApparelCombat | ApparelCombat_CustomDamage：PowerArmor 穿戴者受到原生近战攻击 | 同类型 ExpectedAssertionFailure |
| C05 | AnimalCombat | AnimalCombat_CustomDamage：Wolf_Timber 原生接敌及损伤 | 同类型 ExpectedAssertionFailure |
| C06 | MechanoidCombat | MechanoidCombat_CustomDamage：Mech_Scyther 原生攻击；培育附例验证控制关系 | 同类型 ExpectedAssertionFailure |
| C07 | AlienCombat | AlienCombat_CustomDamage：Axolotl_Colonist 实际种族及钢剑攻击 | 同类型 ExpectedAssertionFailure |
| C08 | SkillPocketMap | Axolotl 三项、HemogenCostAndNativeLongjump、PassiveDeathlessReactionToDamage | ThunderStrengthen_ExpectedAssertionFailure、缺资源及无实际目标使用 |
| C24 | RecipeExecution | StonecuttingProductsAndConsumption：1 花岗岩块→20 石砖并消耗材料；NativeMechGestation、NativeMechResurrectionIdentity | 石块配方 ExpectedAssertionFailure |
| C25 | HediffEffect | HediffSeverityAndRemoval、DebugRectangleAndSourceIsolation、RegisteredResourceAndHealingActions | 同类型 ExpectedAssertionFailure；调试干预及后置状态拒绝 |
| C26 | CorpseLifecycle | CorpseRotAndSamePawnResurrection：腐烂后同 innerPawn 原生复活；机械复活亦保留身份 | 同类型 ExpectedAssertionFailure |
| C27 | ThingBehavior | InventoryPreservesThingIdentity：Steel×25 装入、原生掉落不算新建、销毁 | 同类型 ExpectedAssertionFailure |

P/N 所有场景均核对 owned live=0、arenaRegistered=False、sourceRestored=True。独立 UI 用例核对生成角色从 WorldPawns 移除并销毁。截图读取确认人物/地形可见、技能落点、炮塔受击目标及 UI 按钮；未出现调试日志窗口覆盖。

## 限制、第三方情况与下一批

- 只交付 1.7 场景后端；1.6 的 CLI 和已有宿主解析检查通过，不宣称全面回迁。
- ExecuteRecipe 为明确 accelerate=true 的原生功能执行，验证真实产物/材料/机械生命周期；不声称验证长时间工人工作 AI。UI 独立。
- 自动炮塔遵守原生索敌规则；无法维持的强制墙体目标明确拒绝，不强行改变原生限制。品质只用于真实拥有品质组件的物品；木棒等不支持时明确诊断。
- 固定资源适配只覆盖文档登记接口。不能将任意原生 DebugAction 菜单当作无交互动作，不能执行用户提供的代码/类名/字段路径。
- 本机 Axolotl 1.6 文件仍有启动阶段 `LogSyncAutoTestSpecDef` 缺失错误及数值解析警告；原始日志保留，未通过改动模组消除。P 执行期间 UnexpectedSevereLogs=0。
- Gizmo UI 的通过仅代表实际选中角色的按钮绑定和渲染检查；例如螈气不足的原生禁用状态仍如实记录，不宣称技能可用或已产生效果。
- 第三批专用视图、角色 XML 暂存、普通测试弹窗与手动会话尚未交付；第四批独立群战尚未交付。

本批主要文件：Scenarios 的解析/展开/计划/执行契约，Game/Scenarios 的固定动作、环境、功能、覆盖和种子适配，普通配方/技能 UI 拆分，CLI 选择及结果校验，核心/CLI 测试与外部 fixtures，第二批使用说明和本清单。详细字段与运行方式以[第二批使用说明](RimDevTools外部场景XML第二批使用说明.md)为准。
