# Goal 2 完成报告：语义探针与现有测试深度断言

状态：已完成（按真实结果收口，不要求第三方内容全部通过）

实施日期：2026-08-10

对应策划案：[Goal2-语义探针与现有测试深度断言.md](./Goal2-语义探针与现有测试深度断言.md)

## 1. 完成结论

Goal 2 的代码、定向游戏回归、代表性 Standard 全量回归、发布包、部署和报告均已完成。

验收口径保留真实游戏结果：第三方 Mod 产生的 Error/Fatal 会使对应 Case 失败；目标类型不适用的武器会明确标记为 `unsupported`，不会伪装成 passed。最终 Axolotl Standard 运行因此不是“全绿”，但证明了 Manifest 完整、语义断言工作、错误按 Case 窗口归因、unsupported 分类和 Evidence 受控。

## 2. 主要实现范围

### 2.1 稳定引用与核心合同

新增 `Contracts`、`Core` 纯逻辑层：

- Map、Thing、Pawn、Window、Selection、Gizmo、Case、Operation、Capture 的运行期稳定 ID。
- 顺序敏感的 Selection Fingerprint；Gizmo ID 嵌入 Selection Fingerprint，选择变化后拒绝旧 Gizmo。
- 分层 AutomationReadiness（game/map/playable/visual/automation）和连续稳定轮询。
- 受限 SemanticValueGraph：最大深度、集合/子节点/字符串上限、循环引用、私有字段、getter 异常和危险类型保护。
- 统一 Assertion Library，始终保留 expected/actual/details/lastSnapshot。
- 按游标窗口的 Error/Fatal/Warn Policy 与显式 allowlist。
- 公共、无 RimWorld 类型的 `AutoTestRuntimeApi` DTO 门面：状态、套件、入队运行、取消和 Evidence 查询。

### 2.2 游戏 Probe 与 Adapter

新增 Readiness、Map、Thing、Pawn、Combat、Ability、Window、Selection、Notification Probe，以及目标解析、Gizmo 校验、进程内 UI Adapter。

默认 UI 关闭路径不再调用 Win32 鼠标 API，也不使用 `Thread.Sleep`；窗口通过进程内 `Window.Close`/`WindowStack.TryRemove` 操作。

### 2.3 既有测试族迁移

已接入语义断言的测试族：

- InfoCard：稳定目标、窗口绑定、InspectString Probe 结果、关闭清理。
- Recipe Bill UI：正确 BillStack、BillConfig 的 RecipeDef 绑定、生产结果、关闭后 Bill 移除。
- Turret：地图/炮塔/供电/敌人、锁敌、投射物或适用性、伤害、清理。
- Ranged：TargetingMode 分类；Pawn、Location、Building、Projectile、Beam/Instant、Unsupported 不再混用。
- Melee：装备步骤即时确认、可用 Verb、攻击距离/Job、伤害、实体存活和清理。
- Skill/Ability：Ability 所有权、Gizmo、forceBypass、目标类型、排队/施放、cooldown/charges/Hediff/目标状态变化、清理。
- CombatPawn：初始 loadout/body/race/health、受击变化、图形刷新和目标有效性。
- ThreeView：生成、race/body/apparel/weapon、三方向/位置、Capture、速度恢复和 cleanup。

Skill 在没有 AutoAbility 程序集时增加了安全的 RimWorld 原生 `Ability.QueueCastingJob` 后备路径；Evidence 明确记录 `forceBypass=true`，真实效果仍必须由状态变化证明。

## 3. 日志窗口修正

异步 Tick 中产生的游戏日志可能没有 `caseId`。错误策略现在按 `runId + case start cursor` 读取完整 Case 时间窗，不会因缺少上下文标签而漏掉 Error/Fatal。

InfoCard 会话在实际 close/failed 终点立即关闭 semantic Case，而不是等整个 Run 的 `Finish()`；这样异步错误只归属于发生期间的 Case，不会污染后续 Case。

真实验证：

- `CookCharredTailx4` Run `c66aabadcda24702bf663b9677b8cb53`：Manifest 1/1，因异步 stack-limit Error 在 `logs.no_unexpected_severe` 失败，`unexpectedSevere=1`。
- 正常 InfoCard Run `859eef0ca590453cb704ec8060ccfe92`：1/1 通过，未被该错误污染。

## 4. 真实 RimWorld 回归矩阵

以下命令均使用发布包中的 CLI（scope 名称为实际 `AutoTestSelectionParser` 支持值）。历史输出受自动保留策略限制，Run ID 与关键统计保留在此报告中。

| 测试族 | 实际命令要点 | Run ID | 结果 |
| --- | --- | --- | --- |
| InfoCard | `--mode auto --target-package-id trigger.logsyncmod --scope content --defname LogSync_AutoTestPowerGenerator --capture-screenshots` | `44c851636f3b45f29b7cb36ab192d2f4` | 1/1；13 assertions；12 operations；0 severe；Evidence 57,472 bytes |
| Turret | `--target-package-id ludeon.rimworld --scope turrets --defname Turret_MiniTurret --capture-screenshots` | `d060fe145ba4467083adc284df84fa2` | 通过；锁敌、投射物、伤害、清理语义成立 |
| Ranged | `--target-package-id ludeon.rimworld --scope rangedweapons --defname Gun_AssaultRifle --capture-screenshots` | `c8b65e83e32f467993f9eef8a571192a` | 通过；Pawn targeting 分类成立 |
| Melee | `--target-package-id ludeon.rimworld --scope meleeweapons --defname MeleeWeapon_LongSword --capture-screenshots` | `0753c3c196cf4e8fb384394948cd3ab0` | 通过 |
| Skill/Ability | `--target-package-id ludeon.rimworld.royalty --scope skillpocketmaps --defname Stun --capture-screenshots` | `048ec61af7fa4e03861f948a8f90ea6a` | 通过；AutoAbility 缺失时原生后备施放成功；目标状态变化成立 |
| CombatPawn | `--target-package-id ludeon.rimworld --scope combatpawns --defname Muffalo --capture-screenshots` | `29849e6b739443a1ac5dca03a5ec5f58` | 通过 |
| ThreeView | `--target-package-id ludeon.rimworld --scope threeviews --defname Colonist --capture-screenshots` | `4dbba75b07d946c88f57649a648b738e` | 通过；三方向、Capture、速度恢复和 cleanup 成立 |
| Recipe Bill UI | `--target-package-id ludeon.rimworld --scope recipes --defname CookMealSimple --capture-screenshots` | `4a9ec7fa15044352a9db64b2ee254ad5` | 通过；Manifest 1/1；BillStack、Recipe 绑定、Bill 移除成立 |
| Hediff 族修正回归 | `--target-package-id hentailoliteam.axolotl --scope hediffs` | `3a953b2d81054536a05cf1e768c2ac47` | 35/35 执行并通过窗口 Handle 绑定修正 |
| Melee 波动修正回归 | `--target-package-id hentailoliteam.axolotl --scope meleeweapons --defname Axolotl_FeatherFan --defname Axolotl_Mace` | `ac273b13e49d4017a6597ff8a2b8b87e` | 2/2 通过；装备以 setup 时刻断言，战后丢武器仍保留诊断 |

## 5. 代表性 Standard 全量回归

目标 Mod：`hentailoliteam.axolotl`（静态约 502 ThingDef、48 RecipeDef、52 AbilityDef，内容量明显大于本项目自身）。

最终全量 Run：`a640128dc6e1474b9030e90fcced52cd`

- RunMode：`Standard`
- Manifest discovered/planned/executed：`419 / 419 / 419`
- `manifestMissing=0`
- Operations：500
- Assertions：4,367
- Evidence：约 9.27 MB，日志缓冲上限 2,000 条
- Case 状态：414 passed、4 `failed_semantic_assertion`、1 `unsupported`
- 4 个真实失败均为 `logs.no_unexpected_severe`：`CookCharredTailx4`、`CookStargazyPiex4`、`Axolotl_FlowerDebris`、`Axolotl_WaterInvisibility`；对应 Error/Fatal 来自目标 Mod 的实际堆栈/栈数量问题。
- 1 个 unsupported：`Axolotl_VastSeaCelestialWhaleCannon`，分类为 `building_target`，明确不强迫 Pawn harness。

这轮结果证明完整发现清单没有下降、unsupported 没有被计为 passed、真实严重日志会失败、Evidence 大小受控，且默认路径没有 Win32 鼠标干扰。

## 6. 自动化与静态回归

| 检查 | 结果 |
| --- | --- |
| `dotnet run --project AutoTest.Core.Tests\AutoTest.Core.Tests.csproj -c Debug` | 46 项通过 |
| `dotnet build source\TempProject\TempProject.csproj -c Debug` | 成功 |
| `dotnet build source\AutoTestProject\AutoTestProject.csproj -c Debug` | 成功；仅既有 CS0649 警告 |
| `dotnet build RimDevTools.Tests\RimDevTools.Tests.csproj -c Debug` | 成功 |
| `dotnet RimDevTools.Tests\bin\Debug\net10.0\RimDevTools.Tests.dll` | 274 项通过 |
| `rg user32/mouse_event/SetCursorPos/Thread.Sleep source\AutoTestProject` | 无匹配 |
| Core/Contracts 运行时引用审计 | 无 Verse/RimWorld/UnityEngine/Harmony/AccessTools/BindingFlags 匹配 |
| `git diff --check` | 通过 |

## 7. 发布、部署与最终包验证

已执行：


```powershell
.\.tools\publish-rimdevtools.ps1
.\.tools\deploy.ps1
```

发布目录：`artifacts/RimDevTools-Package`

发布包版本命令返回：`rimDevToolsVersion=1.0.0.0`。

最终发布包真实冒烟 Run：`44c851636f3b45f29b7cb36ab192d2f4`。该 Run 由 `artifacts/RimDevTools-Package/.tools/RimDevTools.exe` 启动，完成 1 个 InfoCard Case，0 failed assertions、0 severe logs，并生成截图和 Evidence。

最终部署后工作区与实际 Mods 目录 DLL SHA256 一致：

- `LogSyncMod.dll`: `AFAE6D7BDFE7D687D6FB7D325544C91D41A424124B4343DB9B4AE255864030DC`
- `LogSyncMod.AutoTest.dll`: `2FB4A1600802512E296AAA0D51F3D35DF4140C9661F557B99BE0975D23319FCA`

发布包使用 Release 构建，和部署用 Debug 构建的二进制哈希不同；最终包 CLI 已实际启动并完成真实测试，包内容和源码版本来自同一发布流程。

## 8. Goal 2 验收矩阵

| 验收项 | 结论 | 证据 |
| --- | --- | --- |
| AutomationReady 结构化 Probe + 稳定轮询 | 完成 | Core readiness tests；InfoCard/Standard Evidence readiness |
| 主要测试族至少三个语义断言 | 完成 | 8 个族的 SemanticModule 与定向 Run |
| 炮塔/武器失败分类 | 完成 | Turret/Ranged/Melee 断言与 Ranged `building_target` unsupported |
| Skill force bypass 与真实效果 | 完成 | Royalty Stun Run `048ec61...`，forceBypass、warmup、目标状态变化 |
| Error/Fatal 默认失败 + allowlist | 完成 | Core cursor test；`CookCharredTailx4` Run `c66a...` |
| 默认 UI 无 Win32 鼠标/Thread.Sleep | 完成 | 静态审计无匹配 |
| 稳定 Target ID + stale Gizmo 拒绝 | 完成 | Core ID/Fingerprint tests；Selection/Gizmo Adapter |
| SemanticValueGraph 有界安全 | 完成 | Core 循环/深度/集合/getter/私有字段测试 |
| 失败报告 expected/actual/snapshot/log/timeline | 完成 | ReportWriter、Evidence contracts、失败 Recipe Evidence |
| Core 与 RimDevTools 自动化测试 | 完成 | 46 + 274 |
| 代表性全量游戏回归 | 完成 | Axolotl Standard Run `a640...`，419/419，真实失败如实保留 |

## 9. 后续边界

本 Goal 不包含 Goal 3 的通用 UI Layout/Widget Hook、RimBridge Companion 或像素相似度评分。第三方 Mod 的 4 个真实 Error/Fatal 和 1 个 unsupported 已在报告中保留，后续应由目标 Mod 或对应 Spec allowlist/适配器决定如何处理，而不是在 Goal 2 中伪造通过。
