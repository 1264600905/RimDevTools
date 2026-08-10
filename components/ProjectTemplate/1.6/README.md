# Trigger's Project Template

白金trigger的 RimWorld 1.6 模组开发模板。首次使用时必须修改：

- `About/About.xml` 中的名称、作者和 packageId。
- `mod_config.jsonc` 中的模组名、日志关键词、本机 RimWorld 路径和 Workshop 路径。
- `source/MyCustomMod` 中的程序集名称、命名空间和工程文件名。

纯 XML 模组可以将 `mod_config.jsonc` 的 `csproj_paths` 配置为空数组；deploy 会跳过 C# 构建，只同步配置的 XML 和资源。

`doctor`、`deploy` 和 `test` 会拒绝仍使用模板默认名称、packageId 或 `MyCustomMod.dll` 的项目。

## 环境要求

- 运行 `.tools/RimDevTools.exe`：Windows x64 与 .NET 10 Runtime。
- 使用 `mod copy` 的 ILSpy：.NET 8 Runtime。
- 编译模组：dotnet SDK 与 .NET Framework 4.8 Developer/Targeting Pack；4.7.2 可作为兼容替代。
- 运行游戏测试：RimWorld 1.6 与已登录的 Steam 会话。

## RimSage

模板为三种客户端配置同一个只读服务：

- Codex CLI：`.codex/config.toml`
- Claude Code：`.mcp.json`
- opencode：`opencode.jsonc`

默认端点是 `https://mcp.rimsage.com/mcp`。Codex 默认批准该只读服务的工具调用。

`.agents/skills/rimsage` 与 `.claude/skills/rimsage` 规定：

- 涉及官方 Def、XML、C# API、实现行为或 Harmony 目标时，RimSage 是研究前置。
- 当前项目已有代码、翻译、资源、构建、部署、测试和日志诊断不强制连接。
- 配置缺失或损坏时安全修复并重试一次；仍不可用时停止依赖它的研究并请求用户处理。
- 用户自行配置的同名本地服务与默认远程服务等同使用。

RimSage 不读取当前项目或第三方模组。当前项目直接读取工作树；第三方模组先检查 `mod_config.jsonc` 的 `project_paths.reference_docs`（默认 `参考文档`），缺少时使用 `mod search/mod copy`。计划文档目录由 `project_paths.planning_docs` 指定。

必须使用RimSage不要瞎猜源码！必须使用RimSage不要瞎猜源码！必须使用RimSage不要瞎猜源码！如果rimsage不能用要及时告诉用户。

## Skills

项目级 Codex skill 位于 `.agents/skills`，Claude skill 位于 `.claude/skills`：

- `rimsage`：官方 Def、XML 与 C# 研究。
- `rimdevtools`：项目检查、部署、参考模组、测试和工具 Mod 安装。
- `rimworld-development-engineer`：从证据到实现和验证。
- `rimworld-plan-engineer`：只产出基于证据的实施计划。
- `about-write-description`：基于已实现功能更新双语 Workshop 描述。

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

`mods install-tools` 只安装随包 LogSyncMod。`mod copy` 保持选项显式，不会自动使用 `--deps`、`--include-ignored` 或 `--overwrite-existing`。

模板 C# 工程默认目标为 .NET Framework 4.8，并递归包含 `source/MyCustomMod` 下除 `bin/obj` 外的 C# 文件。游戏和 Harmony 引用由 RimDevTools 从真实安装解析。

quick、auto 和 XML 使用 `mod_config.jsonc.isolated_desktop`；模板默认值为 `false`。manual 默认使用当前桌面。运行中的临时 ModList 和 LogSync 设置由事务恢复，不要手工并发修改。


日志会自动同步到.tools下面的log/精选日志.txt  如无必要禁止直接读取原log文件.
