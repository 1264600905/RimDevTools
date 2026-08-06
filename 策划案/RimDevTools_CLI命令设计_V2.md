# RimDevTools CLI 命令设计 V2

## 文档状态

本文档是 `RimDevTools_CLI命令设计.md` 的第二版实施规格。原始文档保持不变，本版沿用其中未被修改的目标、命令范围和行为，并落实以下已确认决策：

1. 数据库自动导出直接在 RimSearcher DataMod 中实现，不再要求修改 LogSyncMod 主模组。
2. RimDevTools CLI 必须主动阻止会争用 RimWorld 全局配置和游戏进程的命令并发执行。
3. 现有 PowerShell 与 Python 脚本的相关逻辑迁移到 C#，发布版本不以调用旧脚本作为主要实现方式。
4. 工具和命令统一命名为 `RimDevTools`，发布文件名为 `RimDevTools.exe`。
5. 项目发现采用有限的就近原则：先检查当前工作目录，再检查它的直接父目录；两处都没有 `mod_config.jsonc` 时失败，不继续向上递归。
6. Shader 能力未启用时，不检查、不启动 Unity；启用时才将 Unity 视为部署所需依赖。
7. 参考模组拉取采用精确解析、冲突检测、完整预检、暂存后替换等增强规则。
8. CLI 默认面向 AI 和自动化使用，正常结果直接输出结构化 JSON，不额外设计人类输出与 `--json` 双模式。
9. 当前仍处于设计文档阶段，暂不约束未来源码在现有仓库中的最终归属方式。

## 目标

`RimDevTools.exe` 是面向 Windows RimWorld 模组开发环境的自动化命令行入口。第一期将以下既有能力迁移为 C# 实现：

- `.tools/deploy.ps1` 的构建、Shader Bundle 构建和部署流程。
- `.tools/pull/pull_ref.py` 的参考模组检索、拉取、依赖分析和反编译流程。
- `.tools/test/run_test.py` 及 `runner_core` 的日志同步与自动化测试流程。
- 自动启动 RimWorld，通过 RimSearcher DataMod 导出与当前模组环境匹配的 `defs.db`，供 `rimsearcher.exe` 查询。

第一期不包含：

- `init_mod.py` 的项目复制功能迁移。
- 预览图渲染。
- Shader Bundle 构建逻辑本身的重写；CLI 只在功能启用时调用既有 Unity 构建入口。
- Decompiler MCP 管理。
- RimSearcher 查询命令的重复实现。
- LogSyncMod 主模组功能的重写或数据库导出能力合并。

## 技术与组件边界

### RimDevTools CLI

- 使用 C# 实现。
- 建议与 RimSearcher CLI 统一使用 .NET 10。
- 第一阶段目标平台为 Windows x64。
- 推荐发布为 self-contained 单文件，使 `RimDevTools.exe` 本身不依赖目标机器预装 .NET Runtime。
- 使用结构化 JSONC、XML 和 SQLite API，不使用正则表达式模拟结构化文件解析。
- 旧 PowerShell/Python 脚本仅作为行为参照和迁移期间的回归基线，不作为正式发布版本的运行时依赖。

### RimSearcher CLI

- 保持独立的 `rimsearcher.exe`。
- 继续负责 `search`、`list`、`get`、`find`、`fields`、`values`、`types`、`mods` 和更新检查。
- 增加 `--db <path>` 以及项目配置中的数据库路径发现能力。
- 继续使用 SQLite `user_version` 校验 CLI 与 DataMod 的版本一致性。

### RimSearcher DataMod

- 继续负责把游戏实际加载的全部 Def 导出到 SQLite。
- 保留现有手工设置页和手工导出能力。
- 新增由 RimDevTools 驱动的无人值守自动导出模式。
- 自动模式不依赖 LogSyncMod，也不运行 AutoTestProject 中的地图、战斗或截图测试。

### LogSyncMod 与 AutoTestProject

- LogSyncMod 继续服务于 `RimDevTools test` 的日志同步和完成信号。
- AutoTestProject 继续提供 `auto` 和 `xml` 测试模式。
- 数据库导出不引用 AutoTestProject，也不需要取得 LogSyncMod 主模组源码。

## 推荐发布布局

不追求把所有组件压缩为一个文件。推荐发布目录如下：

```text
RimDevTools/
├── RimDevTools.exe
├── rimsearcher.exe
├── components/
│   └── RimSearcher_DataMod/
│       ├── About/
│       ├── Assemblies/
│       ├── Languages/
│       └── Native/
├── tools/
│   └── ilspycmd/
└── licenses/
    ├── RimSearcher-LICENSE.txt
    ├── ILSpy-LICENSE.txt
    └── third-party-notices.txt
```

说明：

- `components/RimSearcher_DataMod` 是与当前 `rimsearcher.exe` 版本锁定的自动导出模组。
- CLI 可以检查该组件是否已安装到 RimWorld `Mods` 目录，但第一期不强制实现自动安装命令。
- `ilspycmd` 可以作为随包组件，也可以通过配置指定外部路径；两种模式必须在发布前选定并记录许可证。
- 不随发布包附带固定 `defs.db`。数据库取决于当前启用的 DLC、模组、游戏版本和语言。

## 命令名称与调用方式

发布文件和命令名称统一为：

```powershell
RimDevTools.exe <command> [options]
```

当安装目录已经加入 `PATH` 时，也使用：

```powershell
RimDevTools <command> [options]
```

不再使用 `rimdev` 作为正式命令前缀。

## 项目发现规则

除 `--help` 和 `--version` 外，需要项目上下文的命令按以下固定顺序查找 `mod_config.jsonc`：

1. 检查当前工作目录中的 `mod_config.jsonc`。
2. 若未找到，检查当前工作目录直接父目录中的 `mod_config.jsonc`。
3. 若仍未找到，立即失败。

不会继续检查祖父目录或更高层目录，也不会以 `RimDevTools.exe` 所在目录代替当前工作目录。

示例：

```text
D:\Mods\MyMod\                 # 项目根目录，包含 mod_config.jsonc
D:\Mods\MyMod\source\         # 在此运行时可检查到直接父目录
D:\Mods\MyMod\source\Core\    # 在此运行时不会跨两级找到项目，命令失败
```

找到配置文件后，其所在目录即为项目根目录。以下路径均以项目根目录为基准解析：

- `csproj_paths`
- `sync_items`
- 相对形式的 `rimsearcher_db_path`
- CLI 参数中的项目内相对输出路径

`rimworld_dir`、`workshop_paths` 等机器路径仍按配置中的绝对路径解释。

## 标准输出、错误输出与退出码

RimDevTools 默认面向 AI 和自动化调用，不提供默认文本模式与额外的 `--json` 开关。

### 输出规则

- 命令的最终结果写入标准输出，内容必须是一个完整 JSON 对象。
- 进度、等待状态、构建输出摘要和诊断信息写入标准错误。
- 外部工具的原始输出不得直接混入标准输出。
- 即使失败，只要进程能够正常生成错误结果，标准输出仍应输出包含错误信息的 JSON 对象。
- `--help` 可以输出稳定的纯文本帮助；`--version` 输出 JSON。

成功示例：

```json
{
  "ok": true,
  "command": "data refresh",
  "projectRoot": "D:\\Mods\\MyMod",
  "result": {
    "databasePath": "D:\\Mods\\MyMod\\.rimdev\\defs.db",
    "runId": "39f3c97b0e9e4fa09855eec0288ce9fb"
  },
  "warnings": []
}
```

失败示例：

```json
{
  "ok": false,
  "command": "deploy",
  "error": {
    "code": "PROJECT_NOT_FOUND",
    "message": "当前目录及其直接父目录中均未找到 mod_config.jsonc"
  }
}
```

建议使用稳定的进程退出码：

| 退出码 | 含义 |
| --- | --- |
| `0` | 成功。 |
| `2` | 命令参数或配置格式错误。 |
| `3` | 项目、路径或外部依赖缺失。 |
| `4` | 资源正被另一 RimDevTools 进程占用。 |
| `5` | 构建、复制、反编译等子任务失败。 |
| `6` | 等待 RimWorld 或自动化信号超时。 |
| `7` | 游戏启动、测试或数据库导出失败。 |
| `130` | 用户取消。 |

JSON 中的稳定错误代码比自然语言错误文本更适合作为 AI 判断依据。

## 项目配置契约

项目根目录继续使用 `mod_config.jsonc`，它是 RimDevTools 的唯一项目级配置来源。命令行选项只覆盖单次运行，不回写项目配置。

建议新增配置版本和数据库路径：

```jsonc
{
    "config_version": 2,

    // RimSearcher 当前项目数据库；相对路径以项目根目录为基准。
    "rimsearcher_db_path": ".rimdev\\defs.db"
}
```

RimDevTools 使用支持 JSON 注释的正式解析能力，并允许现有的 `//` 注释。不得通过删除 `//` 后再解析 JSON 的方式读取配置。

配置字段及其能力归属：

| 配置字段 | 使用命令 | 用途 |
| --- | --- | --- |
| `config_version` | 全部项目命令 | 配置契约版本。 |
| `mod_name`、`game_version` | `deploy`、`ref`、`test`、`data` | 标识当前项目和目标游戏版本。 |
| `rimworld_dir`、`workshop_paths` | `doctor`、`ref`、`test`、`data` | 定位游戏、Mods、Workshop 和相关运行环境。 |
| `csproj_paths`、`sync_items`、`protected_files_regex` | `deploy` | 构建、同步和部署保护规则。 |
| `shader_bundle` | `doctor`、`deploy` | Shader 能力启用状态和 Unity 构建参数。 |
| `pull_ignore_folders`、`ilspycmd_path` | `ref` | 参考模组复制和反编译规则。 |
| `log_keywords`、`min_active_mods`、`timeouts`、`retention` | `test`、`data` | 日志、默认模组环境、超时和保留策略。 |
| `rimsearcher_db_path` | `doctor`、`data`、RimSearcher | 项目数据库位置。 |

配置校验必须按命令能力执行。例如，执行 `ref search` 时不应因为 `csproj_paths` 缺失而失败；执行未启用 Shader 的 `deploy` 时不应因为 Unity 路径缺失而失败。

## 命令总览

| 命令组 | 用途 |
| --- | --- |
| `doctor` | 检查项目配置、本机环境和各项能力状态。 |
| `deploy` | 构建并部署当前模组。 |
| `ref` | 搜索、拉取和分析本地参考模组。 |
| `test` | 配置日志同步并运行 RimWorld 测试。 |
| `data` | 自动导出、校验和查看 RimSearcher Def 数据库。 |

## 通用命令

| 命令 | 功能 |
| --- | --- |
| `RimDevTools --help` | 显示命令帮助。 |
| `RimDevTools --version` | 输出 RimDevTools、RimSearcher CLI 和配套 DataMod 版本信息。 |
| `RimDevTools doctor` | 只读检查当前项目及所有能力状态。 |

### doctor 输出模型

`doctor` 不修改任何文件。每个检查项至少包含：

```json
{
  "id": "deploy.unity",
  "status": "available",
  "required": false,
  "path": "D:\\UnityEditor\\Editor\\Unity.exe",
  "message": null
}
```

状态建议固定为：

- `available`
- `missing`
- `invalid`
- `disabled`
- `not_applicable`
- `stale`
- `busy`

检查范围包括：

- 项目发现结果和 JSONC 格式。
- 当前命令所需配置字段。
- RimWorld 安装目录和可执行文件。
- RimWorld 配置目录、`ModsConfig.xml` 和 `Player.log`。
- Steam 可用性；直接启动游戏可用时，Steam 缺失可以只作为警告。
- `dotnet` 及所有 `csproj_paths`。
- Shader 能力启用状态和 Unity。
- Workshop 路径。
- LogSyncMod 和 AutoTest 扩展。
- RimSearcher DataMod。
- 同目录 `rimsearcher.exe` 及其版本。
- `rimsearcher_db_path` 指向的数据库、完整性和版本状态。
- RimDevTools 全局互斥锁及未完成事务。

### Shader 条件检查

当 `shader_bundle.enabled` 为 `false`、缺失或整个配置块不存在时：

- Unity 检查状态为 `disabled` 或 `not_applicable`。
- Unity 缺失不影响 deploy 能力。
- deploy 不启动 Unity，也不执行 Shader Bundle 构建。

只有 `shader_bundle.enabled=true` 时，Unity 路径、Unity 项目和构建方法才是 deploy 的必需项。

## 全局互斥与配置事务

### 互斥范围

以下命令必须取得同一个 RimWorld 环境级独占锁：

- `RimDevTools deploy`
- `RimDevTools test run`
- `RimDevTools test settings`
- `RimDevTools data refresh`

原因是它们可能等待或启动同一个 RimWorld 进程、修改全局配置，或写入游戏 Mods 目录。

以下命令通常只读或只修改项目目录，不需要取得 RimWorld 全局锁：

- `doctor`
- `ref search`
- `ref deps`
- `ref related`
- `data status`

`ref pull` 使用目标目录级锁，防止两个进程同时替换同一个参考模组目录。

### 锁实现

- 使用基于 RimWorld 配置目录规范化路径哈希生成的 Windows Named Mutex。
- 不按项目根目录生成锁，因为不同项目共享同一份 RimWorld 用户配置。
- 同时写入一个只用于诊断的锁信息文件，记录 PID、命令、项目路径、开始时间和 `runId`。
- Named Mutex 是真实互斥依据；诊断文件不能单独作为锁依据。
- 锁已被占用时立即返回退出码 `4`，不得等待到不确定时间后再执行。

### 可恢复事务

`test run` 和 `data refresh` 修改配置前必须创建事务目录，例如：

```text
<RimWorldConfig>/RimDevToolsTransactions/<runId>/
├── transaction.json
├── ModsConfig.xml.original
├── ToolSettings.xml.original
└── export-request.json
```

事务规则：

1. 取得全局锁。
2. 确认没有正在运行的 RimWorld；若有则按配置超时等待其自行关闭。
3. 读取并备份原文件，记录内容哈希和“原文件不存在”状态。
4. 使用同目录临时文件加原子替换写入临时配置。
5. 启动并监控游戏。
6. 在 `finally` 路径中恢复所有原文件。
7. 只有恢复成功后才删除事务目录并释放锁。

若进程崩溃或机器重启，下一次取得锁的写命令必须先检查遗留事务。确认 RimWorld 已退出后，自动恢复原文件；如果原文件已被用户再次修改且哈希不符合预期，则停止并由 `doctor` 报告冲突，不盲目覆盖。

## 部署命令

| 命令 | 功能 |
| --- | --- |
| `RimDevTools deploy` | 等待 RimWorld 退出，按需构建 Shader，构建 C# 项目并部署。 |
| `RimDevTools deploy --sync-source` | 在本次部署计划中追加 `source/`。 |
| `RimDevTools deploy --dry-run` | 输出完整部署计划，不等待游戏、不构建、不删除、不复制。 |

### 部署阶段

正式部署按以下阶段执行：

1. 发现项目并校验 deploy 所需配置。
2. 解析全部路径，确认部署目标严格位于 `<rimworld_dir>/Mods/` 下。
3. 校验 `protected_files_regex` 可以编译，且只作用于部署目录的直接子项名称。
4. 枚举 `sync_items` 和 `--sync-source` 形成不可变部署计划。
5. 校验所有必需源文件、工程文件和构建工具。
6. 取得 RimWorld 全局锁并等待已运行游戏退出。
7. 仅当 Shader 能力启用时调用 Unity 构建。
8. 按 `csproj_paths` 顺序执行 `dotnet build`。
9. 将同步内容复制到部署目录旁的暂存目录。
10. 对 `About` 保持现有过滤行为，不部署 `.svg` 和 `.html`。
11. 校验暂存结果中的必要文件。
12. 保留 `protected_files_regex` 命中的目标文件并完成目录替换。

任何构建或暂存失败都不得先清空当前可用的部署目录。

### dry-run 输出

`--dry-run` 至少列出：

- 解析后的项目根目录和部署目标。
- 将执行的 Shader 构建及其启用状态。
- 将构建的工程和顺序。
- 将复制的源路径与目标路径。
- 将被删除、替换或保护的现有目标项。
- 缺失的同步项及其严重级别。

## 参考模组命令

| 命令 | 功能 |
| --- | --- |
| `RimDevTools ref search <keyword>` | 搜索本地 Mods 和所有有效 Workshop 路径。 |
| `RimDevTools ref search <keyword> --fuzzy` | 模糊匹配名称、packageId、作者和文件夹名。 |
| `RimDevTools ref pull <query>` | 拉取并反编译模组；版本默认读取 `game_version`。 |
| `RimDevTools ref pull <query> --version <version>` | 覆盖本次程序集目录版本。 |
| `RimDevTools ref pull <query> --deps` | 递归处理硬前置依赖。 |
| `RimDevTools ref pull <query> --all` | 不应用 `pull_ignore_folders`。 |
| `RimDevTools ref pull <query> --fuzzy` | 允许模糊解析查询。 |
| `RimDevTools ref pull <query> --force` | 允许替换已存在的目标。 |
| `RimDevTools ref deps <query>` | 输出目标模组的硬前置依赖。 |
| `RimDevTools ref related <query>` | 输出依赖目标模组的本地模组及关系。 |

### 查询解析优先级

1. 用户明确提供且有效的模组绝对目录。
2. packageId 大小写不敏感的完全匹配。
3. 模组名称完全匹配。
4. 文件夹名完全匹配。
5. 普通包含匹配。
6. 仅在指定 `--fuzzy` 时使用模糊匹配。

若同一优先级存在多个候选，命令必须失败并输出候选列表，不得静默选择第一项。

### 拉取目标与冲突规则

- 目标根目录仍为项目内的 `参考文档/`。
- 目标文件夹使用经过文件名安全处理的 packageId，避免同名模组互相覆盖。
- JSON 结果中同时保留模组名称、packageId、源目录和目标目录。
- 已存在目标但未指定 `--force` 时，命令失败且不修改任何目标。
- `--deps` 必须先解析完整依赖闭包、去除重复和检测循环，再执行写入。
- 依赖闭包中任意目标发生覆盖冲突时，在开始复制前一次性报告全部冲突。

### 暂存与反编译

每个模组先复制到同一文件系统中的暂存目录：

```text
参考文档/.rimdev-staging/<runId>/<packageId>/
```

复制完成后再执行程序集反编译。默认行为规定为：

- 模组文件复制失败：整个该模组拉取失败。
- 找到程序集但反编译工具缺失：失败，并提示运行 `doctor`。
- 单个程序集反编译失败：该模组状态为失败，保留诊断信息，不替换原目标。
- 没有程序集：允许成功，结果中标记 `decompiledAssemblies=0`。
- 所有阶段成功后才原子替换最终目标目录。

## 测试命令

| 命令 | 功能 |
| --- | --- |
| `RimDevTools test run` | 默认执行 quick 模式。 |
| `RimDevTools test run --deploy` | 测试前执行 deploy。 |
| `RimDevTools test run --sync-source` | 配合 `--deploy` 同步源码。 |
| `RimDevTools test run --mode quick` | 启动并等待 LogSync 运行就绪信号。 |
| `RimDevTools test run --mode manual` | 等待用户手动关闭游戏后收集日志。 |
| `RimDevTools test run --mode auto` | 运行 LogSync 自动测试并验证本轮 JSON 结果。 |
| `RimDevTools test run --mode xml` | 使用 XML 驱动的自动测试。 |
| `RimDevTools test run --mods-file <path>` | 使用指定 ModList XML。 |
| `RimDevTools test run --timeout-seconds <n>` | 覆盖本次超时。 |
| `RimDevTools test settings` | 只更新 LogSync 设置，不启动游戏。 |

### ModList XML 规则

- 使用 XML 解析器读取 `<activeMods>` 下的直接 `<li>` 子节点。
- 不从 XML 其他位置提取 `<li>`。
- 保持输入顺序，并按 packageId 大小写不敏感去重。
- 验证每个非官方 packageId 能在 `rimworld_dir/Mods` 或 `workshop_paths` 中解析。
- 自动补充当前项目 packageId 和 `trigger.logsyncmod`。
- 自动补充项及其插入位置必须写入最终 JSON 结果。
- 缺失模组、无效 XML 或不存在的 `<activeMods>` 必须在启动游戏前失败。
- 未指定 `--mods-file` 时才使用 `min_active_mods`。

### 设置恢复

`test run` 必须精确恢复运行前的 `ModsConfig.xml` 和 LogSyncMod 设置文件，包括“原文件不存在”的状态。

`test settings` 是显式的持久修改命令，不在命令结束时恢复设置；它仍需取得全局锁，并使用原子文件写入。

## 数据库自动导出设计

### 总体流程

`RimDevTools data refresh` 不再通过 LogSyncMod 发起导出。流程如下：

1. 取得 RimWorld 全局锁并恢复可能存在的遗留事务。
2. 解析输出路径并生成唯一 `runId`。
3. 解析请求的模组列表。
4. 自动补充当前项目模组和 `kearril.RimSearcherDataMod`。
5. 验证全部模组可解析，并备份 `ModsConfig.xml`。
6. 写入临时模组列表。
7. 在 RimWorld 配置目录写入一次性自动导出请求。
8. 启动 RimWorld。
9. RimSearcher DataMod 在全部 Def 加载完成后执行导出。
10. DataMod 写入完成结果、数据库元数据和专用日志，并请求游戏退出。
11. CLI 验证结果文件、日志、SQLite 完整性、版本和环境元数据。
12. CLI 终止仍未退出的本轮游戏进程，恢复原配置并清理请求文件。

### 命令

| 命令 | 功能 |
| --- | --- |
| `RimDevTools data refresh` | 按默认模组环境自动导出数据库。 |
| `RimDevTools data refresh --mods-file <path>` | 按指定 ModList XML 请求环境导出。 |
| `RimDevTools data refresh --output <path>` | 覆盖本次输出路径，不回写项目配置。 |
| `RimDevTools data refresh --timeout-seconds <n>` | 覆盖本次导出超时。 |
| `RimDevTools data status` | 只读检查数据库状态和实际导出环境。 |

### 自动导出请求

请求文件建议位于：

```text
<RimWorldConfig>/RimDevTools.ExportRequest.json
```

请求模型至少包含：

```json
{
  "schemaVersion": 1,
  "runId": "39f3c97b0e9e4fa09855eec0288ce9fb",
  "createdUtc": "2026-08-06T09:00:00Z",
  "expiresUtc": "2026-08-06T09:15:00Z",
  "outputPath": "D:\\Mods\\MyMod\\.rimdev\\defs.db",
  "autoExit": true,
  "requestedModsHash": "sha256:..."
}
```

DataMod 只处理同时满足以下条件的请求：

- `schemaVersion` 受支持。
- `runId` 格式有效。
- 请求未过期。
- 输出路径是绝对路径。
- 本次启动尚未消费相同 `runId`。

DataMod 开始处理后将请求原子改名为处理中状态，避免重复触发。过期请求不执行导出，只写明确失败结果。

### DataMod 触发时机与线程规则

- DataMod 在 RimWorld Def 加载和长事件完成后调度自动导出。
- 不在 Mod 构造函数中同步执行大规模导出。
- 现有 `DefExporter.Export` 可以在后台线程运行，但读取游戏对象的线程安全范围必须通过真实游戏测试确认。
- 完成或失败后的结果写入、日志输出和退出游戏请求必须正确切回游戏主线程。
- 手工导出模式不读取或修改 RimDevTools 请求。

### 自动导出结果

结果文件建议位于请求文件同目录，并包含 `runId`：

```text
<RimWorldConfig>/RimDevTools.ExportResult.<runId>.json
```

成功结果至少包含：

```json
{
  "schemaVersion": 1,
  "runId": "39f3c97b0e9e4fa09855eec0288ce9fb",
  "ok": true,
  "databasePath": "D:\\Mods\\MyMod\\.rimdev\\defs.db",
  "databaseBytes": 12345678,
  "exporterVersion": "3.1.3",
  "gameVersion": "1.6.4871",
  "language": "ChineseSimplified",
  "effectiveModsHash": "sha256:...",
  "completedUtc": "2026-08-06T09:03:10Z"
}
```

失败结果必须包含稳定错误代码和异常摘要。DataMod 同时输出以下专用日志之一：

```text
[RimSearcher] EXPORT_COMPLETE runId=<runId>
[RimSearcher] EXPORT_FAILED runId=<runId> code=<code>
```

### 数据库成功条件

`data refresh` 仅在以下条件全部满足时成功：

1. 收到与本次完全一致的 `runId` 结果文件，且其中 `ok=true`。
2. 检测到包含本次 `runId` 的 `EXPORT_COMPLETE` 日志。
3. 结果中的数据库路径与 CLI 请求的规范化路径相同。
4. `defs.db` 存在、可按只读模式打开且文件非空。
5. SQLite `PRAGMA integrity_check` 返回 `ok`。
6. SQLite `user_version` 与配套 `rimsearcher.exe` 的版本编码一致。
7. 数据库元数据中的 `runId`、游戏版本和有效模组哈希与结果文件一致。
8. CLI 已成功恢复原始 RimWorld 配置。

恢复配置失败时，即使数据库已经导出，也应返回失败并保留事务目录供后续恢复。

### 数据库元数据

数据库新增单行 `export_metadata` 表和有序 `loaded_mods` 表。至少记录：

- 元数据结构版本。
- 导出 `runId`。
- 导出器版本和 SQLite `user_version`。
- RimWorld 完整版本号。
- 当前语言。
- UTC 导出时间。
- 输入 ModList XML 的 SHA-256；未提供文件时记录默认配置来源及其哈希。
- 请求模组顺序哈希。
- 游戏实际有效模组顺序哈希。
- 当前项目 packageId。

`loaded_mods` 每行至少包含：

- `load_order`
- `package_id`
- `name`
- `version`
- `root_path`
- `is_official`
- `is_tooling`

这里的“数据库对应模组环境”以游戏实际加载的 `effectiveMods` 为准。输入 XML、当前项目和 DataMod 自动补充项都分别记录，因此不会把“请求环境”和“实际环境”混为一谈。

### data status

`data status` 不启动游戏、不修改配置，也不取得独占锁。输出至少包括：

- 解析后的数据库路径和路径来源。
- 文件存在性、大小和最后修改时间。
- SQLite 可打开状态。
- 完整性检查结果。
- `user_version`、DataMod 版本和 RimSearcher CLI 版本匹配状态。
- 游戏版本和语言。
- 导出时间和 `runId`。
- 请求模组摘要、工具模组摘要和实际模组顺序。
- 输入哈希与有效环境哈希。
- 是否建议重新执行 `RimDevTools data refresh` 以及稳定原因代码。

## RimSearcher 数据库定位扩展

RimSearcher CLI 不重复到 RimDevTools 中实现，但需要支持项目数据库路径。

| 调用方式 | 数据库解析规则 |
| --- | --- |
| `rimsearcher.exe --db <path> <command>` | 使用显式路径，优先级最高；相对路径按当前工作目录解析。 |
| `rimsearcher.exe <command>` | 先检查当前工作目录，再检查直接父目录的 `mod_config.jsonc`。 |
| 两处均未找到配置 | 回退到 `rimsearcher.exe` 同目录的 `defs.db`。 |

项目配置中的 `rimsearcher_db_path` 相对路径按配置文件所在目录解析。

路径优先级固定为：

```text
--db
> 当前目录 mod_config.jsonc 中的 rimsearcher_db_path
> 直接父目录 mod_config.jsonc 中的 rimsearcher_db_path
> rimsearcher.exe 同目录 defs.db
```

一旦某一级明确解析出了数据库路径，若该路径不存在、损坏或版本不匹配，必须报错并提示运行 `RimDevTools data refresh`，不得静默改用更低优先级数据库。

## 项目配置初始化指引

模板根目录的 `init_mod.py` 仍用于生成独立模组项目：

```powershell
python init_mod.py "模组英文名" "作者.模组包名" "D:\RimworldProject2\新项目目录"
```

第一期不把该功能迁移进 RimDevTools，但模板和 `init_mod.py` 后续需要保证新项目包含：

```jsonc
"config_version": 2,
"rimsearcher_db_path": ".rimdev\\defs.db"
```

生成项目后，开发者仍需检查机器相关字段：

| 字段 | 必填条件 |
| --- | --- |
| `rimworld_dir` | 使用 deploy、test 或 data 时必填。 |
| `workshop_paths` | 搜索、依赖验证或使用 Workshop 模组时填写。 |
| `shader_bundle.unity_exe_path` | 仅 `shader_bundle.enabled=true` 时必填。 |
| `min_active_mods` | 未提供 `--mods-file` 的 test/data 命令需要。 |
| `rimsearcher_db_path` | 使用 data 或项目内 RimSearcher 查询时必填。 |

首次使用建议执行：

```powershell
RimDevTools doctor
RimDevTools data refresh
rimsearcher types
```

## 安全与路径约束

- 所有删除和替换操作必须使用规范化绝对路径。
- 部署目标必须验证位于 RimWorld `Mods` 目录内。
- 参考模组目标必须验证位于项目 `参考文档` 目录内。
- 数据库临时文件必须与最终数据库位于同一目录，以支持同卷原子替换。
- 不跟随会把删除范围导向受控目录之外的目录链接或 Windows Reparse Point。
- 覆盖前必须在 JSON 结果中给出规范化目标路径。
- `--force` 只允许已明确列入执行计划的目标，不扩大删除范围。
- Ctrl+C、超时和异常均进入统一清理与恢复流程。

## 版本与兼容性

- RimSearcher CLI 和 DataMod 使用同一产品版本，并继续通过 SQLite `user_version` 严格锁定。
- 元数据表结构发生不兼容变化时必须提升产品版本。
- RimDevTools 记录其兼容的 DataMod 版本范围；第一期建议只允许完全相等版本。
- `RimDevTools --version` 应读取实际配套组件，而不是只输出编译时常量。
- `doctor` 对版本不匹配输出明确的 `VERSION_MISMATCH`，不得自动覆盖用户组件。

## 验证与测试要求

### 单元测试

- 当前目录和直接父目录的项目发现；验证不会继续向上查找。
- JSONC 注释、字符串中的 `//`、路径转义和错误定位。
- 路径包含关系、防目录穿越和 Reparse Point 检查。
- `protected_files_regex` 和部署计划生成。
- ModList XML 仅解析 `<activeMods>`，保持顺序并去重。
- 参考模组候选排序、歧义失败、依赖循环和目录冲突。
- SQLite 完整性、版本和元数据校验。
- JSON 输出模型和稳定退出码。

### 进程与事务测试

- 第二个写命令无法绕过全局锁。
- 构建失败不会清空现有部署目录。
- CLI 在写入临时 ModsConfig 后异常退出，下次运行可以恢复。
- 用户在中断后修改配置时不会被恢复逻辑静默覆盖。
- 超时、Ctrl+C、RimWorld 提前退出和 DataMod 失败均恢复原文件。
- 旧 `runId` 的日志和结果文件不能满足新运行。

### 真实游戏集成测试

- quick、manual、auto、xml 四种测试模式各至少一次。
- 默认模组集和外部 ModList XML 各至少一次。
- DataMod 自动导出后游戏自动退出。
- 导出数据库可被同版本 RimSearcher 查询。
- 数据库记录的有效模组顺序与游戏实际加载顺序一致。
- Shader 关闭时完全不访问 Unity；开启时构建失败能阻止部署。

## 分阶段实施顺序

### 阶段一：CLI 基础设施

- C# 命令框架。
- 项目发现与 JSONC 配置。
- JSON 输出和退出码。
- doctor 能力模型。
- 全局锁和事务恢复框架。

### 阶段二：部署与参考模组

- deploy 计划、dry-run、构建、暂存和替换。
- Shader 条件能力。
- ref 搜索、精确解析、依赖闭包、拉取和反编译。

### 阶段三：测试迁移

- ModsConfig XML 管理。
- LogSync 设置管理。
- 游戏进程启动和监控。
- quick、manual、auto、xml 模式。
- 日志归档和自动测试结果校验。

### 阶段四：数据库自动导出

- DataMod 自动请求、触发、结果和退出流程。
- 数据库元数据表。
- `data refresh` 和 `data status`。
- RimSearcher `--db` 与有限项目发现。

### 阶段五：发布验证

- self-contained Windows x64 发布。
- 组件版本锁定。
- 第三方许可证。
- 单元、事务和真实 RimWorld 集成测试。

## 第一阶段验收标准

满足以下条件才视为第一期完成：

1. 正式发布流程不依赖 Python 或 `.tools/deploy.ps1`、`.tools/test/run_test.py`、`.tools/pull/pull_ref.py`。
2. 所有正式命令默认产生稳定、可解析的 JSON 结果。
3. 两个项目无法并发修改同一 RimWorld 环境。
4. 任意失败、超时或正常结束后，RimWorld 配置能够恢复到运行前内容。
5. Shader 关闭时部署不要求 Unity。
6. `ref pull` 不会因模糊首选或显示名称冲突静默覆盖错误模组。
7. DataMod 可以在无人值守模式下导出数据库并退出游戏。
8. `data status` 能证明数据库的版本、游戏版本、语言和实际模组环境。
9. RimSearcher 能使用 `--db` 或就近项目配置查询数据库。
10. 发布包包含所需许可证和与 CLI 版本匹配的 DataMod。

## 非目标

- 不打包 RimWorld、Steam、Unity 或浏览器。
- 不将固定 `defs.db` 嵌入任何 EXE。
- 不把 RimSearcher 查询命令复制进 RimDevTools。
- 不管理 DecompilerServer MCP。
- 不把 AutoTestProject 的完整测试逻辑放入 DataMod。
- 不要求 LogSyncMod 主模组源码来实现数据库导出。
- 不在第一期迁移 `init_mod.py`、预览图渲染或 Shader Bundle 构建实现。
- 不在本设计阶段决定 RimDevTools 和 RimSearcher 修改源码最终采用独立仓库、子模块还是其他目录组织方式。
