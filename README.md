# RimWorld LogSync Development Workspace

本仓库包含 LogSyncMod、自动测试扩展、RimDevTools CLI，以及用于创建 RimWorld 1.6 模组项目的发布模板。

## 环境要求

- Windows x64。
- 运行 `RimDevTools.exe`：.NET 10 Runtime。
- 使用随包 ILSpy 执行 `mod copy`：.NET 8 Runtime。
- 编译 RimDevTools：.NET 10 SDK。
- 编译 RimWorld 模组：dotnet SDK，以及 .NET Framework 4.8 Developer/Targeting Pack；4.7.2 可作为兼容替代。
- 运行游戏测试：可用的 RimWorld 1.6 与已登录的 Steam 会话。

RimSage 使用远程 MCP，不要求在本机安装服务端、索引工具或额外运行时。

## 目录

```text
1.6/                         LogSyncMod 发布程序集与 Defs
About/                       LogSyncMod 元数据
Languages/                   LogSyncMod 翻译
source/                      LogSyncMod 与 AutoTest 源码
RimDevTools/                 CLI 源码
RimDevTools.Tests/           CLI 回归测试
RimDevTools.ReleaseTests/    发布布局测试
components/ProjectTemplate/  RimWorld 1.6 项目模板源
.tools/                      开发、发布、测试与 ILSpy 工具
参考文档/                    默认的第三方模组证据目录（由 project_paths.reference_docs 配置）
```

## 构建与测试

```powershell
dotnet build RimDevTools.Tests\RimDevTools.Tests.csproj
dotnet RimDevTools.Tests\bin\Debug\net10.0\RimDevTools.Tests.dll

powershell -ExecutionPolicy Bypass -File .tools\publish-rimdevtools.ps1
dotnet build RimDevTools.ReleaseTests\RimDevTools.ReleaseTests.csproj
dotnet RimDevTools.ReleaseTests\bin\Debug\net10.0\RimDevTools.ReleaseTests.dll artifacts\RimDevTools-Package
```

发布脚本每次从模板源重建 `artifacts/RimDevTools-Package`，不会复用旧发布内容。发布包包含 RimDevTools、LogSyncMod、AutoTest、ILSpy、三种客户端的 RimSage 配置和两套 RimSage skill。

## RimSage 研究链路

发布模板为 Codex CLI、Claude Code 和 opencode 默认配置 `https://mcp.rimsage.com/mcp`。Codex 对该只读服务使用 `default_tools_approval_mode = "approve"`。

RimSage 是半强制研究前置：

- 查询官方 Def、XML 继承、官方 C# API、实现行为或 Harmony 目标时必须使用。
- 只修改当前项目现有文件，或进行构建、部署、测试和日志诊断时不要求连接。
- 配置明显损坏时，skill 会安全修复并重试一次；服务仍不可用时停止依赖它的结论并要求用户恢复访问。
- 已配置的同名本地服务与默认远程服务等同使用。

当前项目始终直接读取工作树。第三方模组不由 RimSage 提供；先检查 `mod_config.jsonc` 的 `project_paths.reference_docs`，缺少时使用 `mod search/mod copy` 获取 XML、程序集和 ILSpy 源码。计划文档目录由 `project_paths.planning_docs` 指定。

## 常用命令

```powershell
.\.tools\RimDevTools.exe --version
.\.tools\RimDevTools.exe doctor
.\.tools\RimDevTools.exe deploy --dry-run
.\.tools\RimDevTools.exe mod search "<keyword>"
.\.tools\RimDevTools.exe mod copy "<exact-query>" --dry-run
.\.tools\RimDevTools.exe test run --mode quick
.\.tools\RimDevTools.exe test cleanup --dry-run
.\.tools\RimDevTools.exe mods install-tools --dry-run
```

`mods install-tools` 只管理随包的 `trigger.logsyncmod`。安装器校验版本、packageId 和目录哈希，并保留 `PublishedFileId.txt`、防止降级、支持暂存/备份/回滚。

纯 XML 模组可以将 `mod_config.jsonc` 的 `csproj_paths` 配置为空数组；部署会跳过 C# 构建，只同步配置的 XML、About、语言和其他 `sync_items`。

quick、auto 和 XML 测试遵循 `mod_config.jsonc.isolated_desktop`；模板默认使用当前桌面。manual 默认使用当前桌面，除非显式传入 `--isolated-desktop`。测试会事务化修改并恢复 RimWorld 配置。

auto/xml 可重复传入 `--scope` 组合多个测试分类；正式值为 `ui`、`combat`、`view`、`misc`，未传时默认为 Standard。Standard 横跨四类常规测试，但保持现有口径，不隐式启用耗时的 CombatPawns 与 SkillPocketMaps；旧细分 scope 仍作为窄范围兼容值接受。auto 还可重复传入 `--defname` 精确筛选目标，任何未命中的名称都会使本轮失败。`--batch-size` 设置 Targeted 测试计划上限，默认 40；Standard 全量和显式 DefName 测试忽略该上限。RimWorld stdout/stderr 会被安静丢弃，终端 stdout 保持为单一最终 JSON；自动测试也不再落盘独立 Evidence JSON。外部 ModsConfig XML 使用 `--active-mods-file` 指定。

`test run` 和 `doctor` 还会检查启用模组的必要上级依赖。缺失依赖只显示 warning，不阻挡测试；`doctor` 的检查项为 `mods.min_active_dependencies`。测试完成信号和每 5 秒一次的主线程心跳通过 `.tools/log/精选日志.log` 传递，不再扫描 Unity `Player.log`；连续 60 秒没有当前 runId 的心跳会返回 `GAME_HUNG` 并终止游戏。

例如：`RimDevTools.exe test run --mode auto --scope view --scope combat --defname MyMod_ArmorCoat`。

