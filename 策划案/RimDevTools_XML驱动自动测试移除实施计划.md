# RimDevTools XML 驱动自动测试移除实施计划

## 1. 目标

彻底移除由 `LogSyncAutoTestSpecDef` 驱动的 XML 自动测试模式，统一保留代码生成的 `auto` 自动测试。

本次不做旧版兼容：

- `test run --mode xml` 返回 `MODE_INVALID`，不映射到 `auto`。
- 不再读取或写入 `autoTestMode`，不保留旧 XML Spec 的读取逻辑。
- AutoTest 运行请求不再接受 `mode: "xml"`。

## 2. 最终行为

| 项目 | 最终行为 |
|---|---|
| 测试模式 | `quick`、`manual`、`auto`；默认仍为 `quick` |
| 自动测试计划 | 由 AutoTestRunner 代码枚举并生成 |
| 范围选择 | `--scope` 只用于 `auto`，允许重复传入 |
| 目标选择 | `--defname` 只用于 `auto`，允许重复传入 |
| 批次上限 | `--batch-size` 只用于 `auto`，默认值保持 40 |
| 截图 | 保留 `autoTestCaptureScreenshots` 和现有 `auto` 默认行为 |
| 逐用例 XML 覆盖 | 删除；不再配置截图、pawn、建筑、recipe 或视图覆盖 |
| 日志豁免 | 删除 Spec 提供的 `allowedErrorPatterns` / `allowedWarningPatterns` |

`autoTestScopes`、`autoTestTargetDefNames`、`autoTestBatchSize` 和 `autoTestCaptureScreenshots` 属于普通自动测试设置，应保留。

## 3. 移除边界

本次移除的是 AutoTest Spec 配置功能，不是 RimWorld 对 XML 的使用。保留以下内容：

- RimWorld 的普通 Def、语言和模组元数据 XML。
- `1.6/Defs/Internal/TestDefs.xml` 与 `1.7/Defs/Internal/TestDefs.xml` 中自动测试需要的 MapGenerator、GenStep、ThingDef 和 HediffDef 夹具。
- RimDevTools 的 `deploy --xml-hot`、XML 文件部署和热重载功能。
- AutoTestRunner 的 scope、DefName、batch、结果验证、截图和测试运行能力。

## 4. 当前实现位置

### RimDevTools 命令与运行时接口

- `RimDevTools/Commands/TestCommand.cs` 接受 `xml`，并在宿主检测、`quicktest` 启动、结果保留、截图和配置写入等处与 `auto` 共用分支。
- `RimDevTools/Infrastructure/TestHostResolver.cs` 将 `xml` 作为需要 AutoTestRunner 的模式。
- `RimDevTools/Infrastructure/CliHelp.cs`、`RimDevTools/README.md` 和 `RimDevTools/Commands/DoctorCommand.cs` 描述 `auto/XML`。
- `source/AutoTestRunner/Contracts/AutoTestRuntimeContracts.cs` 的 `AutoTestRunRequestDto` 暴露 `Mode`；`AutoTestBootstrap.cs` 将 `xml` 转为 `XmlDriven`。

### LogSync 设置和自动测试运行器

- `source/LogSyncMod/LogSyncSettings.cs` 定义并序列化 `AutoTestMode` / `autoTestMode`，并提供 `UseXmlDrivenAutoTest()`。
- `source/LogSyncMod/LogSyncMod.cs` 显示模式菜单，并在 XML 模式下禁用 DefName 白名单。
- `source/LogSyncMod/LogSyncAutoTestSpecDef.cs` 定义 XML Spec 字段和归一化逻辑。
- `source/AutoTestRunner/AutoTestRunner.SpecModule.cs` 载入目标模组 Spec、按 scope/type/category/DefName 匹配，并决定截图规则。
- `source/AutoTestRunner/Integration/AutoTestRunner.SemanticModule.cs` 从 Spec 取得允许的 error/warning 模式。
- Spec 类型还传入内容、信息卡、战斗、口袋地图、三视图、Hediff pawn 生成、建筑阵营和 recipe 执行分类等路径。

仓库当前 1.6 与 1.7 Def 目录没有 `LogSyncAutoTestSpecDef` 实例；已有 XML 模式运行记录也显示计划用例数为 0。删除仍需清理运行时和文档中的完整依赖链。

## 5. 实施步骤

### 阶段 A：删除 CLI 和远程请求中的 XML 模式

1. 在 `TestCommand.cs` 将有效模式限定为 `quick`、`manual`、`auto`。
2. 将所有 `mode is "auto" or "xml"` 分支收敛为 `mode == "auto"`，包括：
   - AutoTestRunner 宿主要求；
   - `quicktest` 启动参数；
   - 自动测试结果目录和日志保留策略；
   - 返回 JSON 中的截图和自动测试字段；
   - `SettingsEditor.UpdateAsync` 写入的自动测试设置。
3. `--scope` 和 `--batch-size` 校验改为只接受 `auto`；`--defname` 规则保持只接受 `auto`。
4. 在 `TestHostResolver.cs`、`DoctorCommand.cs` 和 CLI 帮助中删除 XML 模式说明。
5. 从 `AutoTestRunRequestDto` 删除 `Mode`，从 `AutoTestBootstrap.RequestRuntimeRun` 删除 XML 分支。确认 RimSage/运行时调用方无需再发送该字段。
6. 删除按模式选择的隔离桌面默认分支时，保留现行 `auto` 行为；同步更新 `components/ProjectTemplate/mod_config.jsonc` 的 quick/auto/xml 注释。

### 阶段 B：移除游戏设置中的模式开关

1. 从 `LogSyncSettings.cs` 删除 `AutoTestMode`、`autoTestMode`、对应的 Scribe 读写与校验，以及 `UseXmlDrivenAutoTest()`。
2. 从 `LogSyncMod.cs` 删除模式菜单和 `GetAutoTestModeLabel()`；DefName 输入保持可用。
3. 删除 `Languages/ChineseSimplified`、`ChineseTraditional`、`English` 中模式选择器和 XML 提示对应的翻译键，包括 `AutoTestMode*` 与 `AutoTestTargetDefNamesXmlHint`。
4. 删除测试结果 Notes 中 `AutoTestMode=Automatic/XmlDriven` 的模式标记；其它测试结果字段不变。
5. 更新 `source/LogSyncMod/LogSyncMod.cs` 中说明 Auto/XML 设置恢复方式的注释。
6. 不增加旧设置迁移代码。

### 阶段 C：移除 Spec 定义、筛选和逐用例覆盖

1. 删除 `LogSyncAutoTestSpecDef.cs` 和 `AutoTestRunner.SpecModule.cs` 中只服务于 XML Spec 的加载、匹配、评分和 scope/type/category 映射。
2. 将现有 `MatchesSpecFilters` 调用改为只应用 `settings.MatchesAutoTestTargetDefName(def.defName)`；无目标白名单时继续按 scope 枚举候选。
3. 从 `AutoTestModels.cs`、case 构造函数和调用点删除 `SpecDef` 参数/属性及相关传递。
4. 清理所有 Spec 消费点，包括：
   - `AutoTestRunner.ContentModule.cs`、`AutoTestRunner.ExtendedInfoCardModule.cs`、`AutoTestRunner.CombatPawnModule.cs`；
   - `AutoTestRunner.TurretModule.cs`、`AutoTestRunner.RangedWeaponModule.cs`、`AutoTestRunner.MeleeWeaponModule.cs`、`AutoTestRunner.SkillPocketMapModule.cs`；
   - `AutoTestRunner.ThreeViewModule.cs`、`AutoTestRunner.StaticViewModule.cs`；
   - `AutoTestHediffContextResolvers.cs`、`AutoTestGenerationContext.cs`、`AutoTestRecipeExecutionModule.cs`；
   - `AutoTestRunner.SemanticModule.cs` 和 AutoTestRunner 初始化流程中的 `LoadActiveSpecs()`。
5. 截图继续由 `autoTestCaptureScreenshots` 控制。投射物截图使用普通自动模式已有默认值，不读取 XML tick 列表。
6. 删除 Spec 日志豁免。检查 `AutoTestErrorPolicy` 是否仍有非 Spec 调用方；若无则删除无调用路径的参数和逻辑，若有则保留其通用策略接口，不再从 Def XML 加载豁免。
7. 清理因此变为无调用方的 helper、参数、using 和私有类型；不要顺带重构无关 AutoTest 模块。

### 阶段 D：更新 README、技能和模板文档

必须先改模板源文件，再生成发布包副本。模板同时维护 `.agents` 和 `.claude` 两套技能，均需同步更新：

- `rimdevtools/SKILL.md` 与 `rimdevtools/references/commands-and-config.md`；
- `test-result-validation/SKILL.md` 与 `test-result-validation/references/test-flows.md`：删除 XML Spec 流程章节；
- `rimworld-development-engineer/SKILL.md` 与 `references/verification-loop.md`；
- `rimworld-plan-engineer/references/planning-contract.md`；
- `rimsage/SKILL.md`，以及仓库级 `.agents/rimsage/SKILL.md`。

将这些文档中的 `auto/XML`、`auto/xml`、XML Spec 命令和流程改成仅适用于 `auto` 的说明。同步检查：

- 根目录 `README.md`、`RimDevTools/README.md`；
- `components/ProjectTemplate/1.6/README.md`、`components/ProjectTemplate/1.6/About/About.xml`、`components/ProjectTemplate/mod_config.jsonc`；
- `RimDevTools/Infrastructure/CliHelp.cs`、`DoctorCommand.cs`；
- `策划案/修改计划.md` 等仍被当作当前操作指导的文档。

策划案中的历史报告可以保留为历史记录；如保留，须避免被当前 skill/README 引用为现行用法。发布包 `artifacts/RimDevTools-Package` 应通过项目打包流程重新生成，不单独手改生成文件。

### 阶段 E：更新自动化测试与构建检查

1. 更新 `RimDevTools.Tests/Program.cs`：
   - `--mode xml` 返回 `MODE_INVALID`；
   - `--scope`、`--defname`、`--batch-size` 在 `auto` 下保留原校验；
   - quick/manual 对这些自动测试参数仍按原规则拒绝；
   - 配置更新不再写入 `autoTestMode`；
   - 原先用 `--mode xml` 覆盖 AutoTest 结果失败、恢复配置等通用行为的测试，改用 `--mode auto`，避免失去这些路径的覆盖。
2. 在发布测试中确认打包 CLI 帮助不再列出 XML 模式，并确认无效模式请求失败。
3. 按当前交付要求只构建并部署 1.7 的 LogSyncMod 和 AutoTestRunner；1.6 程序集不重新构建或部署。
4. 运行 RimDevTools 与 AutoTest 现有测试，验证自动模式结果、scope、DefName、batch、截图和错误判定路径。
5. 如执行 RimWorld 集成测试，日志、结果和截图只使用 RimDevTools 返回的原始产物路径；直接读取 stdout JSON，不另存测试输出或建立证据目录。

## 6. 验收标准

- `test run --mode xml` 明确返回 `MODE_INVALID`，没有 XML 到 auto 的回退。
- CLI、运行时请求 DTO、游戏设置和 AutoTestRunner 中不再存在 XML 模式状态或 `LogSyncAutoTestSpecDef` 运行时依赖。
- 普通 `auto` 仍支持 scope、DefName、batch size、截图、完成/失败结果验证和现有结果输出。
- Spec 的 allowed error/warning 列表不再改变自动测试通过条件。
- 自动测试运行不再要求或查找 XML Spec；`TestDefs.xml` 中仍被 auto 使用的测试夹具完整保留。
- 发布 README、模板 README、技能说明和 CLI help 均不再指导用户运行 XML 自动测试。
- `deploy --xml-hot` 与普通 RimWorld XML Def 支持保持可用。
- 1.7 的 LogSyncMod 和 AutoTestRunner 构建通过；本次不验收 1.6。

## 7. 风险与注意事项

- Spec 的 `allowedErrorPatterns` / `allowedWarningPatterns` 当前会进入普通 `auto` 的语义检查。移除会让原先被 Spec 豁免的日志重新参与失败判定；这属于有意移除的配置行为，应在验收时确认。
- `TestDefs.xml` 虽然是 XML 文件，但提供自动测试运行时夹具，误删会破坏普通 `auto`。
- `AutoTestRunRequestDto.Mode` 属于运行时 API 契约；删除后外部调用方需按单一 auto 流程提交请求。
- `--xml-hot` 与 XML 自动测试模式名称相近，但属于部署功能，必须保留。

## 8. 提交批次

1. RimDevTools CLI 与 AutoTest 运行请求接口。
2. LogSyncMod 设置和 AutoTestRunner Spec 配置链。
3. 技能、README、模板、发布包与实施文档。

每批单独提交；发布包由 `components/ProjectTemplate` 重新生成。
