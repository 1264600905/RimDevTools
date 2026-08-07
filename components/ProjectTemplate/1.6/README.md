# MyCustomMod

RimWorld 1.6 模组开发模板。

首次使用时请修改：

- `About/About.xml` 中的名称、作者和 packageId。
- `mod_config.jsonc` 中的模组名、RimWorld 路径和 Workshop 路径。
- `source/MyCustomMod` 中的程序集名称和命名空间。

Codex 会从项目内的 `.codex/config.toml` 启动随模板提供的 DecompilerServer MCP；该配置使用相对于项目根目录的路径，并默认批准这个只读服务器的全部工具调用。

项目级 Codex skill 位于 `.agents/skills`：

- `rimdevtools`：项目检查、部署、参考模组、测试和 Def 数据库管理。
- `rimsearcher`：查询运行时合并后的 RimWorld Def 数据并建立数据证据链。
- `decompiler-mcp`：使用 DecompilerServer 检查真实游戏或模组程序集。
- `rimworld-development-engineer`：按证据实施、部署并完成游戏内验证。
- `rimworld-modding`：RimWorld 1.6 XML、C#、Harmony、资源与发布开发规范。
- `about-write-description`：基于已验证功能更新双语 Workshop 描述。

常用命令：

```powershell
.\.tools\RimDevTools.exe doctor
.\.tools\RimDevTools.exe data status
.\.tools\RimDevTools.exe deploy --dry-run
.\.tools\RimDevTools.exe test run --mode quick
```

开始实际开发前必须先运行 `data status`；若结果建议刷新，则运行 `data refresh` 并再次确认状态有效。先用 RimSearcher 获取运行时 Def 证据，只有需要确认 C# 行为时才使用 DecompilerServer。
