---
name: rimworld-development-engineer
description: 在此模板中实现并诊断 RimWorld 模组变更，支持 1.6/1.7 分支开发、按目标版本构建和多版本部署。需要 C#/XML/资产实现、官方 API/Def/Harmony 研究、部署决策或游戏日志诊断时使用；验证遵循最小范围，auto/XML 自动测试仅在玩家明确要求时运行。
---

# RimWorld 开发工程师

负责实现和必要的最低限度验证。不要自行扩大测试范围，也不要因为测试能力可用就运行更多测试。

研究游戏/模组行为前阅读 [references/tool-priority.md](references/tool-priority.md)。运行游戏验证前阅读 [references/verification-loop.md](references/verification-loop.md)。

## 测试产物与归档

- 日志、结果、截图和测试存档只使用 RimDevTools 自动生成的原始路径和产物。除用户明确要求导出外，禁止自建 evidence、任务或日期归档目录，包括 `.tools` 和 `.tools/log` 下的额外目录，也不得换到其他目录绕过。
- 禁止通过 `>`、`2>`、`Tee-Object`、`Out-File`、`Set-Content` 或文件复制另存工具 stdout/stderr 和测试产物；禁止为归档编写一次性包装脚本。
- 新版工具以成功 JSON 的 `result.artifacts` 或失败 JSON 的 `error.details.artifacts` 定位本轮产物；字段为 `directoryPath`、`syncLogPath`、`autoTestOutputPath`。旧版没有这些字段时读取已有 LogSync 设置和产物定位，不创建替代路径或补造文件。路径是输出位置，文件是否生成仍需检查。
- 直接在聊天中报告结论并引用原始路径。除用户明确要求外，不另写测试报告或校验文档；保留与清理由工具负责，不修改原始证据。历史测试产物不构成启动新测试的授权。

## 多版本管理与部署

区分当前源码分支、当前编译目标和完整部署内容。一个模组可以同时包含 `1.6/`、`1.7/` 等版本目录，由游戏按版本加载；当前编译目标只决定本次构建哪一版 DLL。

- **源码通过 Git 分支管理。** 遵循项目已有的版本分支约定，通过切换分支在同一份 `source/` 中开发对应版本；不要复制为 `source/1.6/`、`source/1.7/` 等平行源码树。切换前检查工作区，保留用户未提交的改动，不强制切换或自动丢弃改动。
- **DLL 作为构建产物忽略。** 项目自产 DLL 原则上加入 `.gitignore`，各版本产物分别保留在对应的 `<版本>/Assemblies/` 中；Git 忽略只影响源码版本管理，不能据此排除部署所需 DLL。切换分支时保留已有的各版本产物，不清空其他版本的输出目录。
- **只编译当前目标版本。** 核对当前分支、`mod_config.jsonc.game_version`、`csproj_paths`、游戏程序集引用和项目输出路径一致。目标为 `1.6` 时只编译并更新 `1.6/Assemblies/`，目标为 `1.7` 时只编译并更新 `1.7/Assemblies/`；不要在一次部署中遍历其他版本分支或构建其他版本 DLL。
- **部署包含所有已支持版本。** `sync_items` 应覆盖项目支持的全部版本目录及 `About`、`Languages`、`LoadFolders.xml` 等共享内容，并与 `About.xml` 的 `supportedVersions` 和 `LoadFolders.xml` 的加载配置一致。部署 `1.6` 时仍须部署已有的 `1.7` 内容和 DLL，部署 `1.7` 时同样带上已有的 `1.6` 内容和 DLL；不要把同步范围缩减为当前游戏版本。
- **其他版本复用对应版本的既有产物。** 保留其 XML、资源及 DLL，不用本次构建结果覆盖其他版本 DLL，也不因当前分支未跟踪 DLL 就将其从部署包删除。若完整部署所需的其他版本内容或 DLL 缺失，报告缺失版本和路径；不要静默漏发、借用另一版本 DLL，或擅自切分支补编译。
- **部署前检查完整计划。** 用 `deploy --dry-run` 核对仅构建当前目标版本、同步项包含所有受支持版本，以及更新不会清理其他版本内容。需要查看完整路径和动作时使用 `--verbose`；当前配置无法满足这些条件时先修正配置，不直接执行部署。

## 模组构建产物

编译模组 C# 项目时，禁止生成 `.pdb` 和 `.deps.json`；此规则适用于 1.6、1.7+ 以及 Debug、Release 配置。在模组 `.csproj` 的无条件 `PropertyGroup` 中显式设置：

```xml
<DebugSymbols>false</DebugSymbols>
<DebugType>none</DebugType>
<GenerateDependencyFile>false</GenerateDependencyFile>
```

手动调用 `dotnet build` 或通过 RimDevTools 构建前，确认这些属性生效。构建后检查当前目标的 `Assemblies/`，禁止部署或打包这两类文件。发现残留时先修正生成配置，再仅清理已确认属于本项目的同名旧产物，不清空整个 `Assemblies/` 或其他版本目录。

## 实现前研究

1. 首先阅读最近的当前项目源码、Defs、补丁、配置和测试。
2. 判断任务是否需要关于官方 RimWorld/DLC Def、API、源码行为或 Harmony 目标的新事实。
3. 需要时，调用 `rimsage` 并遵循其可用性、单次重试、官方研究和失败规则，然后再选择字段、类型、方法或目标。
4. 当对象是第三方模组时，检查 `project_paths.reference_docs` 中已有的副本，或按 `rimsage` 和 `rimdevtools` 所述使用 `RimDevTools.exe mod search/mod copy`。
5. 如果允许的修复和重试后仍无法获得所需 RimSage 证据，只停止依赖它的研究，并请用户恢复访问。不要用猜测替代证据。

当前项目文件对本项目行为仍具有权威性。RimSage 仅对其暴露的官方内容具有权威性；拉取的 XML 和 ILSpy 源码是第三方模组的证据。

## 实现

1. 完成上述适用的研究步骤。
2. 在编辑位置阅读既有约定。
3. 在选择字段、重载或 Harmony 目标前，记录相关的 Def/API/成员证据。
4. 在当前目标版本目录（如 `1.6` 或 `1.7`）、`Languages`、`About`、`source` 或已批准的资产目录中做出最小的连贯变更。
5. 保持 package ID、命名空间、程序集名称、翻译键、Def 名称和配置路径一致。
6. 保留用户变更，避免无关重构。

## 游戏测试边界

- 需要游戏运行时验证时，只通过 RimDevTools CLI 发起。禁止使用 CUA、computer-use、桌面/浏览器自动化、鼠标键盘注入、截图识别或远程控制能力操作 RimWorld。
- 不要自行编写测试用例、AutoTest XML、测试项目、测试程序集或测试 DLL。只有玩家明确要求新增或扩展测试时，才编写这些内容。
- 默认只做一次 quick 冒烟测试。RimDevTools 返回 `ok: true`、quick 流程正常完成，且精选日志没有当前模组导致的红色 Error/Exception，即可视为通过；warning 按来源和影响判断，不为清空无关 warning 扩测。
- `auto` 和 `xml` 属于自动测试，只有玩家明确要求自动测试时才运行，并复用已有用例。不要自行扩大 scope 或补造用例。
- `manual` 仅用于玩家明确要求的交互验证；由玩家操作游戏，agent 不代为点击或输入。

## 模组激活顺序

将 `mod_config.jsonc.min_active_mods` 视为开发和测试工作流的权威默认模组列表。优先使用该列表而非临时 ModList，以便项目以其已知的正常基线启动。

当所需模组必须启用但不在 `min_active_mods` 中时：

1. 按其依赖顺序，立即在 `trigger.logsyncmod` 之前添加缺失的依赖。
2. 在其依赖之后、`trigger.logsyncmod` 之前添加所需模组。
3. 保留每个无关条目的顺序，并将 `trigger.logsyncmod` 作为插入锚点。

测试前，确认生效的模组列表仍遵循此顺序。用户明确提供的 ModList 可添加更多模组，但必须保留已配置的默认模组及其依赖顺序。

工具细节请调用 `rimdevtools` 或 `rimsage`，不要在此重复其手册。

使用 RimDevTools 时保留默认精简输出；只有需要完整计划、列表或子进程诊断时才添加 `--verbose`。错误和 warning 会在默认模式下保留。

## 验证

让验证与变更相匹配：

- 解析变更的 XML 并校验引用/键。
- 仅构建当前源码分支和目标游戏版本下变更的 C# 项目；其他版本沿用已有 DLL。
- 实际部署前运行 `RimDevTools.exe deploy --dry-run`，按多版本原则检查构建目标和完整同步范围。
- 用户明确要求游戏运行时同步 XML/资源时，可检查 `deploy --dry-run --xml-hot` 后使用独立的 `deploy --xml-hot`。若 RimWorld 正在运行，它会就地同步非 DLL 文件；复制 `Defs/` 或 `Patches/` XML 后至少等待 3 秒，再向 LogSyncMod 本机 HTTP endpoint 请求内置 Def 热重载。游戏需处于 Playing 且启用开发者模式。若游戏未运行，该选项回退到普通构建和逐文件事务部署。`test run` 不接受 `--xml-hot`，其 `--deploy` 始终使用普通部署。
- 对确实需要游戏运行时检查的改动，默认运行一次 `RimDevTools.exe test run --deploy --mode quick`；满足“游戏测试边界”中的通过条件即可结束验证。
- 只有玩家明确要求自动测试时，才运行现有的 `auto` 或 `xml` 用例；不要自行写用例、测试 DLL、扩大 scope 或做全量回归。
- quick 失败并完成修复后，只重跑能验证该修复的最小检查。不要自动追加 auto/XML 测试。
- 游戏测试结论只覆盖实际运行的版本；完整部署包含其他版本的既有产物，不代表本次已重新编译或验证其他版本。

读取最终的结构化 JSON，并按返回的 `artifacts.syncLogPath` 读取本轮精选日志；旧版未返回路径时按已有 LogSync 设置定位原始日志。只有 JSON 成功且日志无当前模组导致的红色 Error/Exception，quick 才算通过。

## 修复循环

测试失败时：

1. 使用堆栈/上下文证据将错误归因于当前模组、其他模组、游戏或基础设施。
2. 修复根本原因，不要仅为让测试静默而移除必需行为。
3. 修复后只重复最能证伪该失败的最小验证；未经玩家要求，不扩展到 auto/XML 或全量测试。
4. 同一阻塞条件在三种基于证据的方法后仍存在时，在扩大范围前报告确切的错误、证据和尝试过的修复。

不要使用全局警告白名单。数值解析警告或翻译错误等消息在一个环境中可能无害，但由当前模组发出时则可能是可操作的。忽略前先归因。

## 安全与交付

- 不要并发运行部署和游戏测试；它们共享 RimWorld 环境锁。
- 不要在 RimDevTools 事务期间手动编辑实时全局 RimWorld 配置。
- 不要删除 `.rimdev` 恢复状态、日志或用户文件来强行通过。
- 不要使用 `git clean`。
- 除非用户要求，否则不要提交。
- 让代码注释和面向用户的语言与周围项目及用户的语言一致。
- 交付时总结已实现的行为、证据和验证结果。
