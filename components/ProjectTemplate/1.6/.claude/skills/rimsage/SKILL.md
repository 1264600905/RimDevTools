---
name: rimsage
description: 通过 RimSage MCP 研究官方 RimWorld 和 DLC 的 Def、XML 继承、C# 类型、成员、签名、实现和 Harmony 目标。当任务需要官方游戏 API 或行为证据、需要将 Def 关联到实现类，或需决定第三方模组研究是否应转入本地参考文件和 RimDevTools mod copy 时使用。
---

# RimSage

将 RimSage 用作官方 RimWorld 与官方 DLC 内容的证据来源。每项任务首次调用工具前，阅读 [references/tool-reference.md](references/tool-reference.md)。

## 判断是否需要 RimSage

确认官方 Def、XML 继承、游戏类、字段、方法、签名、实现或 Harmony 目标时，必须使用 RimSage。对依赖源代码或 Def 证据的官方行为作出声明前，也必须使用它。

仅涉及既有项目文件、翻译、格式、资产、构建、部署、测试或日志诊断，且未引入新的官方 API 或 Def 假设时，不需要 RimSage。

直接读取当前项目文件。RimSage 无法证实当前项目或第三方模组的事实。

## 确认可用性

需要 RimSage 时：

1. 检查当前客户端是否公开了名为 `rimsage` 的 MCP 服务及其工具。
2. 若不存在，检查当前项目客户端配置，并验证语法和服务名。
3. 安全修复明显缺失的条目、错误服务名或损坏的模板默认配置。
4. 仅重试一次发现或一个代表性调用。
5. 远程端点不可达、已配置的本地进程无法启动、客户端必须重启，或需要凭据/环境处理时，停止依赖 RimSage 的研究并请用户恢复访问。
6. 绝不将记忆、文件名、无关本地文档或猜测作为 RimSage 证据。

正常工作的本地 MCP 服务 `rimsage` 应与模板远程服务同等接受，不要因传输方式或端点不同而拒绝。不要检查索引版本、游戏补丁版本或新鲜度元数据。
如果rimsage服务不可用或没有本地替代，应该向用户说明此情况。

## 研究官方内容

1. 用 `search_defs` 找到精确的 Def 和 Def 类型。
2. 按问题需要，用 `get_def_details` 检查原始或合并后的 XML。
3. 通过 `thingClass`、`compClass`、`workerClass` 和 `jobClass` 等桥接字段定位实现类型。
4. 用 `search_source` 定位类型、成员、字段访问和相关 XML。
5. 对聚焦的类型或方法使用 `read_csharp_symbol`。
6. 长类型、缺失索引条目或需上下文时，用 `read_file` 按行范围读取。用 `list_directory` 发现服务器路径，不要猜测。
7. 记录准确的 Def 名称、类型、成员、路径和决定性片段。以本项目的构建及 quick/auto/XML 或真实游戏测试验证最终实现。

## 研究第三方模组

1. 确认对象是第三方模组而非官方内容。
2. 在 `参考文档` 中查找现有本地副本。
3. 若不存在，用 `.\.tools\RimDevTools.exe mod search <query>` 消除歧义。
4. 先运行 `mod copy <query> --dry-run` 检查计划，再仅带任务所需选项运行 `mod copy`。不要隐式添加 `--deps`、`--include-ignored` 或 `--overwrite-existing`。
5. 若目标与现有参考目录冲突，报告冲突并询问用户是否意图替换。
6. 在本地检查拉取的 About 元数据、LoadFolders、XML、补丁、程序集和 ILSpy 源码。
7. RimSage 仅用于该模组调用的官方 API；绝不声称 RimSage 检查过第三方模组本身。
