# RimDevTools

RimDevTools 是一套面向 RimWorld 1.6/1.7+ 模组开发的 Windows 工具链。LogSyncMod 随包提供 RimWorld 1.6 与 1.7 两套兼容文件；项目发布模板提供可分别选择的 RimWorld 1.6 与 1.7 版本。

它的目标是把模组开发中反复出现的环境检查、部署、依赖定位、日志采集、自动测试和发布打包流程统一起来，让开发者能够用可重复、可诊断的命令完成日常工作。

## 仓库包含什么

| 组件 | 作用 |
| --- | --- |
| RimDevTools CLI | 检查 1.6/1.7+ 开发环境、部署模组、复制参考模组、管理测试配置并启动测试 |
| LogSyncMod | 兼容 RimWorld 1.6/1.7；按 PackageId、关键词和日志级别筛选日志并同步到独立文件 |
| AutoTestRunner | 在游戏内执行 UI、战斗、视图、配方、技能等自动测试并生成结构化结果与证据 |
| Project Template | 提供 RimWorld 1.6 与 1.7 项目结构、三语翻译目录和构建配置 |

## 主要能力

- 检查 RimWorld 1.6/1.7+、Steam、对应的 .NET 构建环境与 Harmony 程序集。
- 将项目程序集、Defs、About、语言文件等内容以事务化逐文件方式部署到 RimWorld Mods 目录，并检查目标文件占用。
- 搜索本地及 Workshop 模组，并安全复制 XML、程序集和 ILSpy 反编译源码作为开发参考。
- 事务化修改并恢复 `ModsConfig.xml` 和测试配置，避免测试中断后污染用户环境。
- 运行 quick、manual、auto 自动测试，支持分类、精确 DefName、批量上限和截图。
- 输出单一 JSON 结果、独立 Evidence 文件、关联日志和主线程心跳诊断。
- 生成包含 CLI、工具 Mod、ILSpy、项目模板和技能配置的完整发布包。

## 下载与快速开始

从 [GitHub Releases](https://github.com/1264600905/RimDevTools/releases) 下载目标游戏版本对应的 `RimDevTools-<工具版本>-Template-RimWorld-<游戏版本>.zip`，解压到独立的开发目录。不要把开发项目放在 RimWorld 的 `Mods` 或 Steam Workshop 目录内。

首次使用需要编辑 `mod_config.jsonc`，至少确认：

- `mod_name`：当前模组的英文目录名。
- `rimworld_dir`：RimWorld 安装根目录。
- `mods_config_path`：游戏实际使用的 `ModsConfig.xml`。
- `workshop_paths`：本机 Steam Workshop 路径。
- `csproj_paths`、`sync_items` 和 `log_keywords`：当前项目的构建、部署与日志设置。

然后运行：

```powershell
.\.tools\RimDevTools.exe doctor
.\.tools\RimDevTools.exe deploy --dry-run
```

`doctor` 会给出环境和项目配置诊断；`deploy --dry-run` 会显示部署计划但不修改游戏目录。

## 环境要求

- Windows x64。
- 运行发布包中的 RimDevTools：.NET 10 Runtime。
- 使用随包 ILSpy：.NET 8 Runtime。
- 编译 RimDevTools：.NET 10 SDK。
- 编译 RimWorld 1.6 模组：dotnet SDK 与 .NET Framework 4.8 Developer/Targeting Pack；4.7.2 可作为兼容替代。1.7+ 模组使用 .NET 10 SDK 与对应版本游戏程序集。
- 运行游戏测试：目标 RimWorld 1.6 或 1.7+、Steam 客户端及有效登录会话。

RimWorld、Harmony 和其他第三方模组程序集不随本仓库分发。

## 常用命令

RimDevTools 默认返回关键摘要，并隐藏大段构建、Unity 和 ILSpy 输出。大多数子命令支持 `--verbose` 查看完整计划、列表和诊断；AI 与日常工作流只在排查问题或需要核对完整清单时添加它。警告和错误始终会显示。

```powershell
# 查看版本与环境诊断
.\.tools\RimDevTools.exe --version
.\.tools\RimDevTools.exe doctor

# 需要查看完整检查项时
.\.tools\RimDevTools.exe doctor --verbose

# 部署当前项目
.\.tools\RimDevTools.exe deploy --dry-run
.\.tools\RimDevTools.exe deploy

# 查找和复制参考模组
.\.tools\RimDevTools.exe mod search "<keyword>"
.\.tools\RimDevTools.exe mod copy "<exact-query>" --dry-run

# 测试
.\.tools\RimDevTools.exe test run --mode quick
.\.tools\RimDevTools.exe test run --mode auto --scope view --scope combat
.\.tools\RimDevTools.exe test cleanup --dry-run

# 安装或更新随包的工具 Mod
.\.tools\RimDevTools.exe mods install-tools --dry-run
```

纯 XML 模组可以将 `mod_config.jsonc` 的 `csproj_paths` 设为空数组，部署时会跳过 C# 构建。

## 自动测试模型

`auto` 模式支持重复传入 `--scope`。正式分类为 `ui`、`combat`、`view` 和 `misc`；未指定时使用 Standard 测试集。

`auto` 还支持：

- `--defname <name>`：精确选择测试目标，可重复传入。
- `--batch-size <count>`：限制枚举型测试数量，默认 40。
- `--active-mods-file <path>`：使用外部 ModsConfig XML。
- `--isolated-desktop`：在隔离桌面运行游戏。

`auto` 模式默认启用截图，不需要额外参数。

quick/auto `test run` 完成后，成功 JSON 的 `result.artifacts` 或失败 JSON 的 `error.details.artifacts` 包含日志目录、精选日志和 AutoTest 输出路径；`result.logSummary` 或 `error.details.logSummary` 始终带有 `errorCount` 和 `warningCount`。计数对应本轮精选日志中的记录，并提供日志可用、计数完整和行数截断状态。

测试结果写入 `.tools/log/AutoTestOutput`。自动测试会生成 `*_Result.json` 和独立的 `*_Evidence.json`；连续 60 秒没有当前 runId 的主线程心跳时，工具会以 `GAME_HUNG` 结束并尝试恢复环境。

## 从源码构建和测试

```powershell
dotnet build AutoTest.Core.Tests\AutoTest.Core.Tests.csproj
dotnet AutoTest.Core.Tests\bin\Debug\net10.0\AutoTest.Core.Tests.dll

dotnet build RimDevTools.Tests\RimDevTools.Tests.csproj
dotnet RimDevTools.Tests\bin\Debug\net10.0\RimDevTools.Tests.dll
```

生成普通发布目录：

```powershell
powershell -ExecutionPolicy Bypass -File .tools\publish-rimdevtools.ps1
```

一次生成 RimWorld 1.6 与 1.7 两个模板包；程序版本和模板游戏版本都会出现在包名与 `--version` 输出中：

```powershell
powershell -ExecutionPolicy Bypass -File .tools\publish-rimdevtools.ps1 -CreateZip
```

输出为：

```text
artifacts/RimDevTools-1.2.2.0-Template-RimWorld-1.6/
artifacts/RimDevTools-1.2.2.0-Template-RimWorld-1.6.zip
artifacts/RimDevTools-1.2.2.0-Template-RimWorld-1.7/
artifacts/RimDevTools-1.2.2.0-Template-RimWorld-1.7.zip
```

验证发布布局：

```powershell
dotnet build RimDevTools.ReleaseTests\RimDevTools.ReleaseTests.csproj
dotnet RimDevTools.ReleaseTests\bin\Debug\net10.0\RimDevTools.ReleaseTests.dll `
  artifacts\RimDevTools-1.2.2.0-Template-RimWorld-1.7
```

## 目录结构

```text
1.6/、1.7/                  LogSyncMod 与 AutoTestRunner 的分版本构建输出和 Defs
About/                       LogSyncMod 元数据和预览图
Languages/                   LogSyncMod 简中、繁中和英文翻译
source/                      LogSyncMod 与 AutoTestRunner 源码
RimDevTools/                 CLI 源码
AutoTest.Core.Tests/         与游戏无关的自动测试核心回归测试
RimDevTools.Tests/           CLI 和环境事务回归测试
RimDevTools.ReleaseTests/    发布包布局与内容验证
components/ProjectTemplate/  发布模板源文件
.tools/                      开发、测试和发布脚本
策划案/                      设计方案与实施记录
参考文档/                    本地第三方参考资料，默认由 Git 忽略
```

## 开源与第三方许可

本仓库以 [MIT License](LICENSE) 开源。

发布包中包含的 ILSpy 等第三方组件保留各自许可证，具体见 `RimDevTools/Packaging/licenses` 和发布包中的 `.tools/Data/licenses`。RimWorld 是 Ludeon Studios 的商标和产品；本项目与 Ludeon Studios 无隶属或官方合作关系。

## 致谢

感谢骸鸾、可爱猫猫喵和 biobattery 协助测试本项目。

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
