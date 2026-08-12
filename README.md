# RimDevTools

RimDevTools 是一套面向 RimWorld 1.6 模组开发的 Windows 工具链。本仓库同时维护开发 CLI、日志同步工具 Mod、游戏内自动测试运行器，以及可直接用于创建新项目的发布模板。

它的目标是把模组开发中反复出现的环境检查、部署、依赖定位、日志采集、自动测试和发布打包流程统一起来，让开发者能够用可重复、可诊断的命令完成日常工作。

## 仓库包含什么

| 组件 | 作用 |
| --- | --- |
| RimDevTools CLI | 检查开发环境、部署模组、复制参考模组、管理测试配置并启动测试 |
| LogSyncMod | 按 PackageId、关键词和日志级别筛选 RimWorld 日志，并同步到独立文件 |
| AutoTestRunner | 在游戏内执行 UI、战斗、视图、配方、技能等自动测试并生成结构化结果与证据 |
| Project Template | 提供 RimWorld 1.6 项目结构、三语翻译目录和构建配置 |

## 主要能力

- 检查 RimWorld、Steam、.NET SDK、.NET Framework Targeting Pack 和 Harmony 环境。
- 将项目程序集、Defs、About、语言文件等内容原子部署到 RimWorld Mods 目录。
- 搜索本地及 Workshop 模组，并安全复制 XML、程序集和 ILSpy 反编译源码作为开发参考。
- 事务化修改并恢复 `ModsConfig.xml` 和测试配置，避免测试中断后污染用户环境。
- 运行 quick、manual、auto 和 XML 测试，支持分类、精确 DefName、批量上限和截图。
- 输出单一 JSON 结果、独立 Evidence 文件、关联日志和主线程心跳诊断。
- 生成包含 CLI、工具 Mod、ILSpy、项目模板和技能配置的完整发布包。

## 下载与快速开始

从 [GitHub Releases](https://github.com/1264600905/RimDevTools/releases) 下载最新的 `RimDevTools-Package-*.zip`，解压到独立的开发目录。不要把开发项目放在 RimWorld 的 `Mods` 或 Steam Workshop 目录内。

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
- 编译 RimWorld 模组：dotnet SDK，以及 .NET Framework 4.8 Developer/Targeting Pack；4.7.2 可作为兼容替代。
- 运行游戏测试：RimWorld 1.6、Steam 客户端及有效登录会话。

RimWorld、Harmony 和其他第三方模组程序集不随本仓库分发。

## 常用命令

```powershell
# 查看版本与环境诊断
.\.tools\RimDevTools.exe --version
.\.tools\RimDevTools.exe doctor

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

`auto` 和 `xml` 模式支持重复传入 `--scope`。正式分类为 `ui`、`combat`、`view` 和 `misc`；未指定时使用 Standard 测试集。

`auto` 还支持：

- `--defname <name>`：精确选择测试目标，可重复传入。
- `--batch-size <count>`：限制枚举型测试数量，默认 40。
- `--capture-screenshots`：启用测试截图。
- `--active-mods-file <path>`：使用外部 ModsConfig XML。
- `--isolated-desktop`：在隔离桌面运行游戏。

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

生成带版本后缀的目录和 ZIP：

```powershell
powershell -ExecutionPolicy Bypass -File .tools\publish-rimdevtools.ps1 `
  -VersionSuffix v1.0.3 `
  -CreateZip
```

输出为：

```text
artifacts/RimDevTools-Package-v1.0.3/
artifacts/RimDevTools-Package-v1.0.3.zip
```

验证发布布局：

```powershell
dotnet build RimDevTools.ReleaseTests\RimDevTools.ReleaseTests.csproj
dotnet RimDevTools.ReleaseTests\bin\Debug\net10.0\RimDevTools.ReleaseTests.dll `
  artifacts\RimDevTools-Package-v1.0.3
```

## 目录结构

```text
1.6/                         LogSyncMod 与 AutoTestRunner 构建输出、Defs
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
