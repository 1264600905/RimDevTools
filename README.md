# RimDevTools

**简体中文** | [English](README.en.md)

RimDevTools 是面向 RimWorld 模组开发的 Windows 工具链，将环境检查、构建部署、依赖定位、日志采集、游戏测试与工坊发布组织为可重复的 CLI 操作。随包模板和工具 Mod 提供 RimWorld **1.6、1.7** 对应内容；CLI 也识别 1.7+ 现代布局，实际工具组件及更新支持范围以发行清单为准。

当前版本为 **1.5.4.0**，提供 [RimWorld 1.6、1.7 模板包及 Tools 简化包](https://github.com/1264600905/RimDevTools/releases/tag/v1.5.4.0)。包含旧项目模式、自动更新、外部 XML 场景测试，以及擂台匹配和清理配置、生命条、正午重设与混编轮播。本版新增每场开战前固定 5 秒的双方属性面板展示。自定义场景后端目前仅支持 RimWorld 1.7。

## 仓库包含什么

| 组件 | 作用 |
| --- | --- |
| RimDevTools CLI | 检查开发环境、构建部署、复制参考模组、管理测试、操作工坊及更新本项目工具 |
| LogSyncMod | 兼容 RimWorld 1.6/1.7；按 PackageId、关键词和日志级别筛选日志并同步到独立文件 |
| AutoTestRunner | 在游戏内执行 UI、战斗、视图、配方、技能等自动测试并生成结构化结果与证据 |
| Project Template | 为新项目提供 1.6 或 1.7 工程结构、三语翻译目录和构建配置 |
| Tools Package | 为已有项目和工具更新提供正式工具载荷、两套完整 skill 与人工配置参考 |

## 主要能力

- 检查 RimWorld 1.6/1.7+、Steam、对应的 .NET 构建环境与 Harmony 程序集。
- 将项目程序集、Defs、About、语言文件等内容以事务化逐文件方式部署到 RimWorld Mods 目录，并检查目标文件占用。
- 搜索本地及 Workshop 模组，并安全复制 XML、程序集和 ILSpy 反编译源码作为开发参考。
- 事务化修改并恢复 `ModsConfig.xml` 和测试配置，避免测试中断后污染用户环境。
- 运行 quick、manual、auto 自动测试，支持分类、精确 DefName、批量上限和截图。
- 输出单一 JSON 结果、独立 Evidence 文件、关联日志和主线程心跳诊断。
- 按配置开关放宽旧项目的模板专属检查，沿用现有工程和同步路径。
- 从官方正式 Release 检查和安装工具更新，保留配置与本地修改，支持事务回滚和中断恢复。
- 生成两个完整模板包和一个共用 Tools 简化包，并提供文件清单及 SHA-256 校验。

## 下载与快速开始

从 [GitHub Releases](https://github.com/1264600905/RimDevTools/releases) 选择已实际提供的附件，或从源码生成下列三种包：

| 包名 | 用途 |
| --- | --- |
| `RimDevTools-<version>-Template-RimWorld-1.6.zip` | 新建 RimWorld 1.6 模组项目，包含完整模板和工具 |
| `RimDevTools-<version>-Template-RimWorld-1.7.zip` | 新建 RimWorld 1.7 模组项目，包含完整模板和工具 |
| `RimDevTools-<version>-Tools.zip` | 已有项目所需工具，以及完整的 Codex、Claude skill |

Tools 包提供 `.tools` 工具目录、`.agents/skills` 与 `.claude/skills` 下的全部模板 skill、配置参考及发行清单，包含必要的两版工具 Mod、ILSpy 和许可证；它精简的是示例模组内容。包内 `mod_config.jsonc` 供人工填写，已有项目应保留自己的配置。Tools 包不按游戏版本再拆分。

发布页只上传这三个 ZIP；GitHub 另外自动提供两个源码下载项。每个客户端包含全部 7 个 skill 及其配套文件。附件完整性使用 GitHub 提供的 SHA-256 摘要校验，不再另发校验文件或兼容包。自动更新只管理工具和核心 `rimdevtools` skill，保留其他 skill；已有项目人工接入 Tools 包时，应保留自己的配置并检查同名 skill 的本地修改。1.5.2 及更早版本的更新器不支持本版完整 Tools 包，需手动安装一次。

新模板解压到独立开发目录。项目应位于 RimWorld `Mods` 和 Steam Workshop 内容目录之外，以满足部署位置保护。

首次使用需要编辑 `mod_config.jsonc`，至少确认：

- `mod_name`：当前模组的英文目录名。
- `game_version`：实际目标游戏版本；当前两种模板分别为 `1.6`、`1.7`。
- `rimworld_dir`：RimWorld 安装根目录。
- `mods_config_path`：游戏实际使用且已存在的 `ModsConfig.xml`。
- `workshop_paths`：本机 Steam Workshop 路径。
- `csproj_paths`、`sync_items` 和 `log_keywords`：当前项目的构建、部署与日志设置。

模板项目还需将 About 中的名称、packageId、默认程序集名等占位信息改成实际模组信息。

然后运行：

```powershell
.\.tools\RimDevTools.exe doctor
.\.tools\RimDevTools.exe deploy --dry-run
```

`doctor` 会给出环境和项目配置诊断；`deploy --dry-run` 会显示部署计划但不修改游戏目录。

## 旧项目模式

已有项目在现有 `mod_config.jsonc` 中补充一个开关即可使用旧项目模式。下面只展示相关字段，应合并到原配置中：

```jsonc
{
    "config_version": 2,
    "existing_project_mode": true
}
```

缺失或 `false` 保持模板模式；`true` 让部署、doctor 和测试预检放宽模板名称、工程路径、程序集名及模板日志关键词等专属限制。实际构建与同步继续使用已有的 `csproj_paths`、`sync_items` 和 `game_version`，读取和部署不会自动重写配置。

必要元数据、游戏版本、构建失败、路径安全及 Mods/Workshop 位置保护仍会检查。纯 XML 模组可设 `csproj_paths: []`。C# 工程需通过当前 `dotnet build` 入口构建，并将 DLL 输出到项目配置中可部署、游戏可加载的位置；当前同步按相同相对路径复制，特殊输出映射、独立内容根和特殊构建链仍可能需要人工适配。

## 工具更新与恢复

以下命令需要正式解压安装的 1.3.0.0 或更新版本；旧 EXE 需要先手动安装一次具备更新能力的版本。

```powershell
.\.tools\RimDevTools.exe update --check
.\.tools\RimDevTools.exe update --dry-run
.\.tools\RimDevTools.exe update
.\.tools\RimDevTools.exe update status
```

普通业务命令启动时检查新版，使用 24 小时缓存，单次联网总预算最多 2 秒；失败后退避 1 小时，并保留原业务结果。帮助、版本、状态查询和内部助手不触发启动联网检查。显式检查区分有新版、已是最新和无法确认。

更新器固定官方正式 Release，下载唯一的 Tools 包，核对大小、SHA-256、平台、Runtime、项目游戏版本及配置兼容性。同版本不重复安装，较低版本不自动降级。`--dry-run` 可下载并验证暂存内容，列出文件变化和冲突，但不提交工具、配置或安装状态变更。

`update` 返回 `scheduled` 表示独立助手已确认交接。助手等原进程退出后才替换文件并验证新 EXE，因此需要继续查询 `update status`：

| 状态 | 含义 |
| --- | --- |
| `none` | 尚无更新事务 |
| `scheduled` / `applying` | 已安排 / 正在提交，尚未完成 |
| `succeeded` | 替换及安装后验证完成 |
| `failed` | 提交前失败，安装未发生部分替换 |
| `rolledBack` | 失败后已恢复完整旧安装 |
| `recoveryRequired` | 需要恢复，普通业务命令会被阻止 |

本地受管理文件被修改、删除、缺少可信基线或新路径被用户文件占用时，整次更新停止并列出冲突。配置迁移保留注释、未知字段、用户值、项目模式和游戏目标；首版支持 `config_version=2`，只为缺失的模式字段补充 `false`。

源码、模组资源、工坊身份、项目 README、其他 skill、MCP/编辑器设置、`.gitignore` 和原模板身份不属于更新替换范围。更新项目私有工具 Mod 后，游戏中的组件仍由 `mods install-tools` 管理。工具磁盘更新成功后，请让 AI 重新读取完整的 `rimdevtools` skill。

`update status` 是本地只读查询。需要恢复时，等待已有更新进程退出，然后使用返回的 `recoveryCommand`；保留的助手即使在主 EXE 不能启动时也可运行，原始入口记录在 `.tools/ProjectData/tool-update-state.json`。遇到未知并发修改时会保留备份并报告冲突。工具管理缓存、暂存和回滚数据，保留最近三份已完成事务及所有未完成恢复所需的数据。

检查和安装更新不要求 Steam 登录或游戏环境健康，也不会自动重跑原业务命令。源码运行和缺少可信发行清单的旧安装需要先手动取得新版。

## 环境要求

- Windows x64。
- 运行发布包中的 RimDevTools：.NET 10 Runtime。
- 使用随包 ILSpy：.NET 8 Runtime。
- 编译 RimDevTools：.NET 10 SDK。
- 编译 RimWorld 1.6 模组：dotnet SDK 与 .NET Framework 4.8 Developer/Targeting Pack；4.7.2 可作为兼容替代。1.7+ 模组使用 .NET 10 SDK 与对应版本游戏程序集。
- 运行游戏测试：目标 RimWorld 1.6 或 1.7+、Steam 客户端及有效登录会话。

当前发行清单和随包工具 Mod 声明支持 1.6、1.7；识别更高版本的现代布局不等于配套组件已经验证兼容。LogSyncMod、AutoTestRunner 和测试模组列表是测试能力的前提，普通部署不要求先安装全部测试工具。

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

| 模式 | 用途 |
| --- | --- |
| `quick` | 默认模式，启动游戏并等待本轮就绪信号与主线程心跳 |
| `manual` | 用户手动游玩与观察，默认使用当前桌面 |
| `auto` | 在游戏内执行生成的自动测试，默认启用截图 |

`auto` 模式支持重复传入 `--scope`。正式分类为 `ui`、`combat`、`view` 和 `misc`；未指定时使用 Standard 测试集。

`auto` 还支持：

- `--defname <name>`：精确选择测试目标，可重复传入。
- `--batch-size <count>`：限制枚举型测试数量，默认 40。
- `--active-mods-file <path>`：使用外部 ModsConfig XML。
- `--isolated-desktop`：在隔离桌面运行游戏。

包括 Standard 在内的枚举运行会从全部候选中均匀、无放回抽样；显式 `--defname` 运行忽略批量上限。

`auto` 模式默认启用截图，不需要额外参数。

quick/auto `test run` 完成后，成功 JSON 的 `result.artifacts` 或失败 JSON 的 `error.details.artifacts` 包含日志目录、精选日志和 AutoTest 输出路径；`result.logSummary` 或 `error.details.logSummary` 始终带有 `errorCount` 和 `warningCount`。计数对应本轮精选日志中的记录，并提供日志可用、计数完整和行数截断状态。

自动测试生成 `*_Result.json`，并按实际运行输出 Evidence、报告、清单和截图。以返回的 `autoTestOutputPath` 及结果引用的原始文件为准，运行目录由工具生成，不假定固定归档路径；quick 等模式可能没有 AutoTest 文件。连续 60 秒没有当前 runId 的主线程心跳时，工具会以 `GAME_HUNG` 结束并尝试恢复环境。

直接读取成功结果的 `result.artifacts` 或失败详情的 `error.details.artifacts`，在聊天中报告结论。除明确要求导出外，不另存 stdout/stderr，不复制日志、截图或测试存档，也不新建任务/日期证据归档；保留与清理由工具负责。

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

一次生成两个模板包和一个 Tools 包，共用载荷来自同一次构建。`--version` 报告工具版本与原模板元数据，Tools 包的模板版本为 `unpackaged`。生成 ZIP 及校验文件：

```powershell
powershell -ExecutionPolicy Bypass -File .tools\publish-rimdevtools.ps1 -CreateZip
```

输出为：

```text
artifacts/RimDevTools-1.5.4.0-Template-RimWorld-1.6/
artifacts/RimDevTools-1.5.4.0-Template-RimWorld-1.7/
artifacts/RimDevTools-1.5.4.0-Tools/
```

每个目录对应一个 `.zip`；发布时只上传这三个 ZIP。验证三种发布布局，再执行隔离的 Windows 更新集成测试：

```powershell
dotnet build RimDevTools.ReleaseTests\RimDevTools.ReleaseTests.csproj
dotnet RimDevTools.ReleaseTests\bin\Debug\net10.0\RimDevTools.ReleaseTests.dll `
  artifacts\RimDevTools-1.5.4.0-Template-RimWorld-1.6
dotnet RimDevTools.ReleaseTests\bin\Debug\net10.0\RimDevTools.ReleaseTests.dll `
  artifacts\RimDevTools-1.5.4.0-Template-RimWorld-1.7
dotnet RimDevTools.ReleaseTests\bin\Debug\net10.0\RimDevTools.ReleaseTests.dll `
  artifacts\RimDevTools-1.5.4.0-Tools
dotnet RimDevTools.Tests\bin\Debug\net10.0\RimDevTools.Tests.dll --tool-update
dotnet RimDevTools.Tests\bin\Debug\net10.0\RimDevTools.Tests.dll --tool-update-live
```

Windows 更新实测在套件自行管理的临时环境中构建目标版本，验证真实 EXE 交接、替换及恢复。更多命令契约见 [CLI 说明](RimDevTools/README.md)。

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
.\.tools\RimDevTools.exe steamworkshops subscribe "<id-or-workshop-url>" --wait
.\.tools\RimDevTools.exe steamworkshops status "<id-or-workshop-url>" --wait
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

标题、说明和改动说明可按 `--text-language` 选择语言；`edit` 还支持 `--add-preview`、`--update-preview`、`--remove-preview` 管理附图，具体参数见对应命令的 `--help`。

## 开源与第三方许可

本仓库以 [MIT License](LICENSE) 开源。随包 ILSpy 等第三方组件保留各自许可证，见 [第三方许可说明](RimDevTools/Packaging/licenses/third-party-notices.txt)、[ILSpy 许可证](RimDevTools/Packaging/licenses/ILSpy-LICENSE.txt)和发布包中的 `.tools/Data/licenses`。

RimWorld 是 Ludeon Studios 的商标和产品；本项目与 Ludeon Studios 无隶属或官方合作关系。RimWorld、Harmony 和其他第三方模组程序集不随本仓库分发。

## 致谢

感谢骸鸾、可爱猫猫喵和 biobattery 协助测试本项目。
