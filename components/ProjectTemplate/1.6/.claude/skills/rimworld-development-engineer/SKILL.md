---
name: rimworld-development-engineer
description: 在此模板中端到端实现、诊断并验证 RimWorld 1.6 模组变更。当请求需要 C#/XML/资产工作、通过 RimSage 进行有证据支撑的官方 API 或 Def 研究、第三方参考研究、构建/部署决策、Harmony 目标、游戏测试、日志诊断或反复的修复与重测循环时使用。
---

# RimWorld 开发工程师

负责从实现到验证的完整过程。当仍有安全、相关的构建或测试可用时，不要在编辑后停下。

研究游戏/模组行为前阅读 [references/tool-priority.md](references/tool-priority.md)。部署或启动 RimWorld 前阅读 [references/verification-loop.md](references/verification-loop.md)。

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
4. 在 `1.6`、`Languages`、`About`、`source` 或已批准的资产目录中做出最小的连贯变更。
5. 保持 package ID、命名空间、程序集名称、翻译键、Def 名称和配置路径一致。
6. 保留用户变更，避免无关重构。

## 模组激活顺序

将 `mod_config.jsonc.min_active_mods` 视为开发和测试工作流的权威默认模组列表。优先使用该列表而非临时 ModList，以便项目以其已知的正常基线启动。

当所需模组必须启用但不在 `min_active_mods` 中时：

1. 按其依赖顺序，立即在 `trigger.logsyncmod` 之前添加缺失的依赖。
2. 在其依赖之后、`trigger.logsyncmod` 之前添加所需模组。
3. 保留每个无关条目的顺序，并将 `trigger.logsyncmod` 作为插入锚点。

测试前，确认生效的模组列表仍遵循此顺序。用户明确提供的 ModList 可添加更多模组，但必须保留已配置的默认模组及其依赖顺序。

工具细节请调用 `rimdevtools` 或 `rimsage`，不要在此重复其手册。

## 验证

让验证与变更相匹配：

- 解析变更的 XML 并校验引用/键。
- 构建每个变更的 C# 项目。
- 实际部署前运行 `RimDevTools.exe deploy --dry-run`。
- 对启动/运行冒烟测试使用 `RimDevTools.exe test run --deploy --mode quick`。
- 不要先运行 auto 测试：它比 quick 模式慢。修复并重跑 quick 测试直至无当前模组错误通过，然后为相关功能用例运行一次 `RimDevTools.exe test run --deploy --mode auto`。
- 仅在该后续 auto 测试通过后，功能变更才算完全验证。不要仅凭 quick 测试通过就宣称验证循环完成。
- 当功能用例需要声明式 LogSync AutoTest 规范时使用 XML 模式。
- 仅对真正交互式的行为使用 manual 模式。

读取最终的结构化 JSON 结果及相关 `.tools/log/精选日志.log` 内容。不要仅从进程退出或缺少可见 UI 错误推断成功。

## 修复循环

测试失败时：

1. 使用堆栈/上下文证据将错误归因于当前模组、其他模组、游戏或基础设施。
2. 修复根本原因，不要仅为让测试静默而移除必需行为。
3. 重复最能证伪失败的最小验证，然后重跑端到端检查。
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
