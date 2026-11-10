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

模板为 Claude Code、opencode 和 pi 配置同一个只读服务：

- Claude Code：`.mcp.json`
- opencode：`opencode.jsonc`
- pi：`.pi/mcp.json`

默认端点是 `https://mcp.rimsage.com/mcp`。

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
- `rimworld-xml-test`：高度自定义 XML 场景、角色配装、效果断言、参数矩阵、地图、模板组合群战与淘汰/魔王轮战/极限挑战；含完整字段索引、动作绑定说明及可复用 XML 模板，当前场景后端仅支持 1.7，随 1.6 模板附带不代表已回迁。
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

quick 和 auto 使用 `mod_config.jsonc.isolated_desktop`；模板默认值为 `false`。manual 默认使用当前桌面。运行中的临时 ModList 和 LogSync 设置由事务恢复，不要手工并发修改。


日志会自动同步到.tools下面的log/精选日志.txt  如无必要禁止直接读取原log文件.

## Steam 创意工坊

```powershell
.\.tools\RimDevTools.exe steamworkshops status
.\.tools\RimDevTools.exe steamworkshops search "Harmony"
.\.tools\RimDevTools.exe steamworkshops search next
.\.tools\RimDevTools.exe steamworkshops search "Harmony" --details --page 5
.\.tools\RimDevTools.exe deploy
.\.tools\RimDevTools.exe steamworkshops upload --dry-run
.\.tools\RimDevTools.exe steamworkshops upload
.\.tools\RimDevTools.exe deploy --upload
.\.tools\RimDevTools.exe steamworkshops edit
.\.tools\RimDevTools.exe steamworkshops edit --visibility private
.\.tools\RimDevTools.exe steamworkshops subscribe <精确ID或工坊链接> --wait
.\.tools\RimDevTools.exe steamworkshops status <精确ID或工坊链接> --wait
```

- 五个子命令为 upload、edit、search、subscribe、status。当前身份优先使用项目 About/PublishedFileId.txt；它保存数字工坊 ID，packageId 另读 About.xml。所有写入只对已获用户授权的具体条目执行；已有会话授权可以沿用。
- upload 只使用成功普通 deploy 的目录，逐项复核新增/缺失/修改文件及未部署项目输入；不自动构建，不提供 --create 或哈希绕过。首次自动新建 private，更新保留远端人工元数据。deploy --upload 在同一锁下串联两阶段；--dry-run 只显示计划，与 --xml-hot 互斥。
- 1.7 普通部署调用本机官方库生成加载目录内的 Assets.zip，默认保留源资源；1.6 跳过烘焙。正常部署、烘焙和发布不启动游戏。官方库不随工具分发。
- edit 不上传内容：无字段参数时同步项目 About 简介；--description-file、--preview-file、--visibility 只改指定字段。可见性为 private/public/friends-only/unlisted；本版仅放行已核验 owner，贡献者检测延期。
- 简易搜索只显示名称、ID、订阅人数，每页 25 条，最多 3 页；--details 额外返回官方 255 字节摘要，每页 10 条，最多 5 页。--page 从 1 开始；基于官方 50 条页依序切片，超限要求更准确的关键词。精确 ID/链接和 status 返回完整描述。
- search next 沿用本项目最近成功关键词、模式和页码，不与 --page/--details 混用。缓存从首次搜索起有效 10 分钟；缓存命中标记 source=cache 和原查询时间，不能作为当前在线、权限或发布成功证据。无项目时单次搜索可用，next 需进入项目或 --project。
- subscribe 只订阅指定 ID，并按需请求下载；--wait 核对匹配 App ID/条目的完成回调和最新安装状态。status --wait 只观察已有下载，不订阅、不请求下载。不启用/排序模组、不递归依赖、不自动退订。Installed 与 NeedsUpdate 并存时不能判定完成；进度不可得为 null。
- 收据 deployment-receipt.json、分页 steamworkshops-search-cache.json、绑定及新增运行暂存文件统一在项目 .tools/ProjectData，必须忽略于 Git，并排除部署和发行。不能把清理缓存变成删除收据或绑定。部署/上传的 artifacts 返回实际收据路径；测试日志仍按 test run 的 artifacts 对象定位，不另存 stdout 或复制证据。
- 发布超时返回非零、timedOut=true、remoteOutcome=unknown、当前时间、目标 Steam 最近更新时间（或 null 与原因）、operationId 和提交阶段。超时不等于远端取消；不自动重试，不保存跨调用超时阻塞，不使用 --recover。由 AI 根据证据决定是否再次显式提交；已知 ID 保存失败和真实绑定冲突仍须修复。
- doctor 每次在 stderr 显示 Steam 运行、RimWorld 库可用性、Workshop 在线摘要，stdout 仍只有最终 JSON。Steam 时间来自条目 m_rtimeUpdated，可能不随元数据编辑改变；不提供历史更新次数。
