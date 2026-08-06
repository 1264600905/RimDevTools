# RimDevTools CLI 命令设计

## 目标

`RimDevTools.exe` 是面向 RimWorld 模组开发的自动化命令行入口。第一期只将以下既有脚本 CLI 化：

- `.tools/deploy.ps1` 的构建与部署流程。
- `.tools/pull/pull_ref.py` 的参考模组检索、拉取、依赖分析和反编译流程。
- `.tools/test/run_test.py` 的日志同步与自动化测试流程。
- 自动启动 RimWorld 并导出与当前模组环境匹配的 `defs.db`，供旁边的 `rimsearcher.exe` 查询。

第一期不包含 `init_mod.py` 的项目复制功能、预览图渲染、Shader Bundle 构建、Decompiler MCP 管理，以及 RimSearcher 查询命令的重复实现。

## 发布布局

不追求把所有组件压缩为单个文件。推荐发布目录如下：

```text
RimDevTools/
├── RimDevTools.exe       # 本项目的 deploy/ref/test/data 自动化入口
├── rimsearcher.exe       # 保持独立的官方 RimSearcher 查询工具
├── defs.db               # 兼容旧 RimSearcher 的可选回退数据库
└── licenses/             # RimSearcher 及其依赖的许可证和声明
```

用户用 `rimdev` 生成或更新数据库，再直接用 `rimsearcher.exe` 查询。例如：

```powershell
rimdev data refresh
.\rimsearcher.exe search "shield"
```

## 命令总览

命令前缀暂定为 `rimdev`。发布文件可以命名为 `RimDevTools.exe`，使用时既可直接调用 EXE，也可在安装目录配置 `rimdev` 命令别名。

| 命令组 | 用途 |
| --- | --- |
| `doctor` | 检查本机环境、项目配置和数据状态。 |
| `deploy` | 构建并部署当前模组。 |
| `ref` | 搜索、拉取和分析本地参考模组。 |
| `test` | 配置日志同步并运行 RimWorld 测试。 |
| `data` | 导出、校验和查看 RimSearcher Def 数据库。 |

所有读取类命令将结构化结果写入标准输出；失败原因写入标准错误。执行删除、覆盖、启动游戏或修改 RimWorld 配置的命令必须返回非零退出码表示失败。

## 项目配置契约

每个模组项目继续保留项目根目录的 `mod_config.jsonc`，它是 RimDevTools 的唯一项目配置来源，不迁移为 EXE 私有配置，也不在命令行中重复维护本机路径。

RimDevTools 必须沿用现有 JSONC 注释语法，并读取以下字段：

| 配置字段 | 使用命令 | 用途 |
| --- | --- | --- |
| `mod_name`、`game_version` | `deploy`、`ref`、`test`、`data` | 标识当前项目及目标 RimWorld 版本。 |
| `rimworld_dir`、`workshop_paths` | `ref`、`test`、`data`、`doctor` | 定位游戏、Workshop 模组和配置目录。 |
| `csproj_paths`、`sync_items`、`protected_files_regex` | `deploy` | 保持当前构建、同步与保护规则。 |
| `pull_ignore_folders`、`ilspycmd_path` | `ref` | 保持参考模组拉取和反编译规则。 |
| `log_keywords`、`min_active_mods`、`timeouts`、`retention` | `test`、`data` | 保持日志同步、默认模组集、超时和日志保留规则。 |
| `rimsearcher_db_path` | `data`、RimSearcher | 当前项目 Def 数据库的保存与默认读取路径。 |

命令行选项只用于覆盖单次运行，例如 `--timeout-seconds`、`--mods-file` 和 `--output`；不得回写 `mod_config.jsonc`。`rimdev doctor` 应校验配置字段完整性，并报告缺失或不存在的路径。

## 通用命令

| 命令 | 功能 |
| --- | --- |
| `rimdev --help` | 显示命令总览及各命令帮助。 |
| `rimdev --version` | 显示 RimDevTools、集成的数据库导出模块版本。 |
| `rimdev doctor` | 检查 `mod_config.jsonc`、RimWorld 安装目录、Steam、`dotnet`、RimWorld 配置目录、日志目录、同目录的 `rimsearcher.exe`、数据库和 LogSyncMod。 |
| `rimdev doctor --json` | 以机器可读 JSON 输出诊断结果，适合 AI 或自动化调用。 |

`doctor` 不修改任何文件。它应明确区分“缺少必需依赖”与“可选能力不可用”，例如未安装 Unity 不应阻止部署、参考模组或数据库导出。

## 部署命令

| 命令 | 功能 |
| --- | --- |
| `rimdev deploy` | 等价当前 `.tools/deploy.ps1`：等待 RimWorld 退出，构建 `csproj_paths`，清理部署目录中未受保护的内容，再同步 `sync_items`。 |
| `rimdev deploy --sync-source` | 在部署时追加同步 `source/` 目录。 |
| `rimdev deploy --dry-run` | 列出将构建、删除和复制的项目，不作任何实际改动。 |

部署配置只读取项目根目录的 `mod_config.jsonc`。正式执行必须沿用 `protected_files_regex`，以保护 Steam 发布使用的 `PublishedFileId.txt` 等文件。

## 参考模组命令

| 命令 | 功能 |
| --- | --- |
| `rimdev ref search <keyword>` | 在 RimWorld Mods 与 Workshop 目录中搜索模组。 |
| `rimdev ref search <keyword> --fuzzy` | 以模糊匹配搜索名称、包名、作者或文件夹名。 |
| `rimdev ref pull <query>` | 拉取模组到 `参考文档/`，跳过配置中声明的大型目录，并反编译程序集。 |
| `rimdev ref pull <query> --version <version>` | 指定程序集目录版本；未指定时使用 `mod_config.jsonc.game_version`。 |
| `rimdev ref pull <query> --deps` | 递归拉取前置模组。 |
| `rimdev ref pull <query> --all` | 不跳过 Textures、Sounds 等大型目录。 |
| `rimdev ref pull <query> --fuzzy` | 将查询按模糊搜索结果解析。 |
| `rimdev ref deps <query>` | 输出目标模组的前置依赖。 |
| `rimdev ref related <query>` | 查找依赖目标模组或与其相关的本地模组。 |

`ref pull` 是写入命令：若目标目录已存在，必须先显示将被替换的路径，并要求 `--force` 才能覆盖。这样避免沿用旧脚本直接删除目录的行为。

## 测试命令

| 命令 | 功能 |
| --- | --- |
| `rimdev test run` | 执行默认 quick 模式：写入日志同步配置，启动 RimWorld，监控完成信号，生成精选日志。 |
| `rimdev test run --deploy` | 测试前先运行 `rimdev deploy`。 |
| `rimdev test run --sync-source` | 配合 `--deploy` 同步源码。 |
| `rimdev test run --mode quick` | 快速启动验证。 |
| `rimdev test run --mode manual` | 等待用户手动关闭游戏后收集日志。 |
| `rimdev test run --mode auto` | 运行 LogSync 自动测试，并验证本轮 JSON 报告。 |
| `rimdev test run --mode xml` | 使用 XML 驱动的自动测试。 |
| `rimdev test run --mods-file <path>` | 使用指定模组预设。 |
| `rimdev test run --timeout-seconds <n>` | 覆盖本次等待超时。 |
| `rimdev test settings` | 只更新 LogSync 配置，不启动 RimWorld。 |

`test run` 需要备份并恢复 `ModsConfig.xml`。同一台机器上不得并发运行测试或数据库导出，因为两者都会修改模组配置并读取同一份 `Player.log`。

`--mods-file` 接受外部 ModList XML 文件，格式与 RimWorld 的 `<activeMods><li>packageId</li></activeMods>` 列表兼容。命令必须保持 XML 中的加载顺序，并在必要时补充当前项目模组和 `trigger.logsyncmod`，再写入临时 `ModsConfig.xml`。未传入该参数时，才回退到 `mod_config.jsonc.min_active_mods`。

## 数据库命令

数据库导出功能直接合并进 LogSyncMod：在 RimWorld 已加载全部 Def 后，由 LogSyncMod 的自动导出模式调用 RimSearcher DataMod 的导出逻辑，并写入专用完成日志；它不运行 AutoTestProject 中庞大的战斗、地图和截图测试逻辑。

当前 `参考文档/AutoTestProject` 仅包含 `LogSyncMod.AutoTest.dll` 的扩展源码，并依赖已编译的 `LogSyncMod.dll`；若要直接修改 LogSyncMod，需要先取得其主模组源码。这是实现前置条件，不影响 CLI 规格。

| 命令 | 功能 |
| --- | --- |
| `rimdev data refresh` | 临时配置模组列表和 LogSyncMod 自动导出设置，启动 RimWorld，自动导出 `defs.db`，校验数据库，恢复原模组配置。 |
| `rimdev data refresh --mods-file <path>` | 按指定的模组预设导出；数据库内容将严格对应该预设。 |
| `rimdev data refresh --output <path>` | 指定 `defs.db` 输出路径。 |
| `rimdev data refresh --timeout-seconds <n>` | 覆盖导出等待超时。 |
| `rimdev data status` | 显示数据库路径、导出时间、文件大小、完整性、版本匹配状态和导出模组摘要。 |

`data refresh` 的成功条件不是普通的日志就绪信号，而是同时满足以下条件：

1. 检测到包含本次唯一 `runId` 的 `EXPORT_COMPLETE` 日志。
2. 输出的 `defs.db` 存在且可打开。
3. SQLite `integrity_check` 通过。
4. SQLite `user_version` 与同目录 RimSearcher CLI 版本一致。

默认输出位置读取 `mod_config.jsonc.rimsearcher_db_path`，建议使用项目相对路径 `.rimdev/defs.db`。`rimdev data refresh --output <path>` 仅覆盖本次输出，不回写配置。数据库不能永久嵌入 EXE：它取决于当前启用的 DLC 和模组，且 RimSearcher CLI 与 LogSyncMod 中的导出模块版本必须匹配。

`data refresh --mods-file <path>` 使用与 `test run --mods-file` 完全相同的外部 ModList XML 规则。命令在数据库元数据中写入该 XML 的内容哈希和最终生效的 packageId 顺序，使 `data status` 能证明 `defs.db` 对应哪个模组环境。

## RimSearcher 数据库路径扩展

RimDevTools 不重复实现 RimSearcher 的查询命令，但后续维护的 `rimsearcher.exe` 需要增加数据库定位能力，使其能直接查询当前项目的数据库，而不要求把数据库复制到 EXE 旁边。

| 调用方式 | 数据库解析规则 |
| --- | --- |
| `rimsearcher.exe --db <path> <command>` | 使用显式指定的数据库路径，优先级最高。相对路径按当前工作目录解析。 |
| `rimsearcher.exe <command>` | 从当前工作目录向上查找 `mod_config.jsonc`，读取 `rimsearcher_db_path`。相对路径按该配置文件所在项目根目录解析。 |
| 未找到配置或配置项 | 为兼容官方 RimSearcher 行为，回退读取 `rimsearcher.exe` 同目录的 `defs.db`。 |

建议新增的项目配置如下：

```jsonc
// RimSearcher 当前项目数据库。相对路径以 mod_config.jsonc 所在目录为基准。
"rimsearcher_db_path": ".rimdev\\defs.db",
```

路径解析优先级固定为 `--db` > `mod_config.jsonc.rimsearcher_db_path` > EXE 同目录 `defs.db`。若最终路径不存在、SQLite 完整性校验失败或版本不匹配，RimSearcher 应提示执行 `rimdev data refresh`，而不是静默回退到另一份数据库。

此项属于对 RimSearcher 的本地兼容扩展，需要在其 C# CLI 源码中实现并重新发布 `rimsearcher.exe`；仍需保留其 MIT 许可证与版本匹配机制。

## 项目配置初始化指引

### 1. 生成独立模组项目

模板根目录的 `init_mod.py` 用于生成新项目，不能在模板母本中直接开发。执行：

```powershell
python init_mod.py "模组英文名" "作者.模组包名" "D:\RimworldProject2\新项目目录"
```

脚本会复制模板，并自动完成以下项目级更新：

- `mod_config.jsonc` 中的 `mod_name`、`log_keywords`、`csproj_paths` 和 Shader Bundle 名称。
- `About/About.xml` 中的模组名和 `packageId`。
- `source/MyCustomMod/` 目录、解决方案、工程文件及核心类型名称。
- 新项目的 Git 初始化。

### 2. 完成机器相关配置

生成项目后，必须检查新项目根目录的 `mod_config.jsonc`。以下字段与开发者机器相关，`init_mod.py` 不应猜测或修改：

| 字段 | 必填条件 | 说明 |
| --- | --- | --- |
| `rimworld_dir` | 必填 | RimWorld 安装根目录。 |
| `workshop_paths` | 建议填写 | Steam Workshop 的 RimWorld 内容目录。 |
| `shader_bundle.unity_exe_path` | 仅启用 Shader 时必填 | Unity Editor 可执行文件路径。 |
| `min_active_mods` | 使用测试/导出时必填 | 默认测试和数据库导出的基础模组顺序。 |
| `rimsearcher_db_path` | 使用数据库导出/查询时必填 | 当前项目数据库位置，建议设为 `.rimdev\\defs.db`。 |

建议首次配置完成后执行：

```powershell
rimdev doctor
```

只有 `doctor` 确认 RimWorld 路径、配置目录和日志目录可访问后，才执行部署、测试或数据库导出。之后可先执行：

```powershell
rimdev data refresh
.\rimsearcher.exe types
```

以验证“启动游戏 -> 自动导出 Def 数据库 -> 本地查询”的完整链路。

## 非目标

- 不将 RimWorld、Steam、Unity 或浏览器打包进 EXE；CLI 只检查并调用本机已安装的程序。
- 不将固定 `defs.db` 内嵌到 EXE；每次导出必须绑定具体模组环境。
- 不把 RimSearcher 的 `search`、`list`、`get`、`find`、`fields`、`values`、`types`、`mods` 和 `check update` 命令复制进 RimDevTools；它们由同目录的原始 `rimsearcher.exe` 继续提供。
- 不管理 DecompilerServer MCP；代码反编译仍由独立 MCP 服务提供。
- 不把 `AutoTestProject` 的完整测试逻辑塞进 LogSyncMod 的数据库导出模块；数据库导出应保持轻量、可重复且不依赖地图。
