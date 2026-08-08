# RimDevTools 发布结构与工具 Mod 部署执行计划

## 文档目标

本文档用于实施 `策划案/修改方案.txt` 中已经确认的三项改造：

1. 在发布包 `.tools/Data` 下同时携带 `LogSyncMod` 和 `RimSearcher_DataMod` 两个独立工具 Mod。
2. 为 RimDevTools 新增工具 Mod 安装命令，将两个随包 Mod 安全安装到 RimWorld 的 `Mods` 目录。
3. 删除原有 `init` 命令和发布包内的嵌套项目模板，让 `artifacts/RimDevTools-Package` 本身直接成为可使用的项目模板。

本文档只规划实施步骤，不在此阶段修改代码。

## 已确认决策

### 1. 两个工具 Mod 保持独立

- `LogSyncMod` 继续使用 packageId `trigger.logsyncmod`。
- `RimSearcher_DataMod` 继续使用 packageId `kearril.RimSearcherDataMod`。
- 不合并 DLL、不合并设置页、不修改现有数据库导出协议。
- 两个 Mod 只在发布、安装和版本检查层面统一管理。

### 2. 发布包根目录就是项目模板

最终发布包必须严格由以下三部分合成：

```text
artifacts/RimDevTools-Package
= components/ProjectTemplate/1.6 下的模板内容
+ 根目录唯一一份 mod_config.jsonc
+ 发布流程生成的 .tools
```

不再在 `.tools/components/ProjectTemplate` 中嵌套携带第二份项目模板，也不再通过 `init` 命令复制项目。

### 3. `mod_config.jsonc` 只保留一个模板源

当前 `components/ProjectTemplate/1.6/mod_config.jsonc` 与发布包根目录的 `mod_config.jsonc` 内容和 SHA-256 一致，属于重复文件。实施时采用以下唯一来源：

```text
components/ProjectTemplate/mod_config.jsonc
```

发布时将它复制到：

```text
artifacts/RimDevTools-Package/mod_config.jsonc
```

同时删除：

```text
components/ProjectTemplate/1.6/mod_config.jsonc
```

不得使用仓库根目录的 `mod_config.jsonc` 作为发布模板配置。仓库根配置属于 LogSyncMod 开发工程，当前内容与通用项目模板配置不同。

## 最终发布目录

发布完成后，`artifacts/RimDevTools-Package` 顶层必须等于 `components/ProjectTemplate/1.6` 顶层内容，加上 `.tools` 和根级 `mod_config.jsonc`。

```text
RimDevTools-Package/
├── .agents/
├── .claude/
├── .codex/
├── .decompilerserver/
├── .tools/
│   ├── RimDevTools.exe
│   ├── rimsearcher.exe
│   ├── Data/
│   │   ├── LogSyncMod/
│   │   │   ├── About/
│   │   │   ├── 1.6/
│   │   │   │   ├── Assemblies/
│   │   │   │   └── Defs/
│   │   │   ├── Languages/
│   │   │   └── LoadFolders.xml
│   │   ├── RimSearcher_DataMod/
│   │   │   ├── About/
│   │   │   ├── Assemblies/
│   │   │   ├── Languages/
│   │   │   └── Native/
│   │   ├── tool-mods.manifest.json
│   │   └── licenses/
│   ├── Ilspy/
│   ├── db/
│   ├── log/
│   │   ├── 精选日志.log
│   │   └── AutoTestOutput/
│   └── modlist/
├── 1.6/
├── About/
├── AssetBundles/
├── Languages/
├── source/
├── UnityProjects/
├── 参考文档/
├── 策划案/
├── .gitignore
├── .mcp.json
├── LoadFolders.xml
├── mod_config.jsonc
├── opencode.jsonc
└── README.md
```

以下路径不得再出现在发布包中：

```text
.tools/components/ProjectTemplate
.tools/components/ProjectTemplate/manifest.json
.tools/components/ProjectTemplate/1.6
```

## 工具 Mod 发布设计

### 1. LogSyncMod 组件内容

发布脚本必须先构建以下工程：

```text
source/TempProject/TempProject.csproj
source/AutoTestProject/AutoTestProject.csproj
```

构建成功后，从当前项目组装 `.tools/Data/LogSyncMod`。只包含 RimWorld 运行所需文件：

- `About/`
- `1.6/Assemblies/LogSyncMod.dll`
- `1.6/Assemblies/LogSyncMod.AutoTest.dll`
- `1.6/Defs/`
- `Languages/`
- `LoadFolders.xml`

以下内容不得进入该组件：

- `source/`
- `.tools/`
- `.agents/`、`.codex/`、`.claude/`
- `参考文档/`、`策划案/`
- 构建缓存、PDB、日志、数据库和测试历史输出

发布组装必须使用本轮构建结果，不能依赖仓库中可能残留的旧 DLL。

### 2. RimSearcher DataMod 组件内容

继续构建：

```text
RimSearcher/Sources/RimSearcher.DataMod/RimSearcher.DataMod.csproj
```

并组装：

- `About/`
- `Assemblies/`
- `Languages/`
- `Native/`

必须保留 Windows、Linux 和 macOS 原生 SQLite 库，发布测试至少强制校验 Windows x64 的 `Native/e_sqlite3.dll`。

### 3. 组件清单

发布脚本生成 `.tools/Data/tool-mods.manifest.json`，建议结构如下：

```json
{
  "schemaVersion": 1,
  "components": [
    {
      "folder": "LogSyncMod",
      "packageId": "trigger.logsyncmod",
      "version": "1.1.0",
      "primaryAssembly": "1.6/Assemblies/LogSyncMod.dll",
      "contentHash": "sha256:..."
    },
    {
      "folder": "RimSearcher_DataMod",
      "packageId": "kearril.rimsearcherdatamod",
      "version": "3.1.3",
      "primaryAssembly": "Assemblies/RimSearcher.DataMod.dll",
      "contentHash": "sha256:..."
    }
  ]
}
```

清单由发布脚本根据实际文件生成，不能手工维护哈希。RimDevTools 安装命令开始执行前必须重新计算源目录哈希并验证清单，防止发布包残缺或被局部替换。

## 版本契约

### 1. LogSyncMod 版本

当前 LogSyncMod 的 `About.xml` 没有 `modVersion`，而 `LogSyncMod.AutoTest.dll` 也没有明确程序集版本。这不足以判断目标安装是较旧还是较新。

实施时必须：

1. 在 LogSyncMod `About/About.xml` 增加 `<modVersion>`。
2. 统一 `LogSyncMod.dll` 与 `LogSyncMod.AutoTest.dll` 的产品版本。
3. 发布测试确认 About 版本、两个程序集版本和组件清单版本一致。

### 2. RimSearcher DataMod 版本

继续使用现有 `About.xml` 的 `modVersion` 和 `RimSearcher.DataMod.dll` 程序集版本，并维持与 `rimsearcher.exe` 的主版本、次版本、构建版本一致。

### 3. 目标版本读取规则

安装命令按以下顺序读取目标 Mod 版本：

1. `About/About.xml` 的 `modVersion`。
2. `modVersion` 缺失时读取清单指定的主程序集版本。
3. 两者都不可用时标记为 `unknown`，默认拒绝覆盖。

不得使用文件最后修改时间判断版本新旧。复制、解压、Git checkout 和重新构建都会改变时间戳，不能代表产品版本。

## 新增 CLI 命令

建议新增命令组：

```powershell
RimDevTools.exe mods install-tools
RimDevTools.exe mods install-tools --mods-path D:\RimWorld\Mods
RimDevTools.exe mods install-tools --dry-run
```

### 参数

| 参数 | 含义 |
| --- | --- |
| `--mods-path <path>` | 显式指定 RimWorld `Mods` 目录。优先级最高。 |
| `--dry-run` | 只输出两个组件的版本、哈希、目标和计划动作，不写入文件。 |

第一阶段不提供隐式强制覆盖。将来如确有需要，可另行设计 `--force`，但必须显式展示会覆盖的目标和冲突原因。

### Mods 路径解析

1. 指定 `--mods-path` 时，使用显式路径，不要求通过 `rimworld_dir` 推导。
2. 未指定时，从当前目录或直接父目录发现 `mod_config.jsonc`。
3. 优先使用现有 RimWorld/Steam 环境解析结果，最终得到 `<rimworld_dir>/Mods`。
4. 目标必须是名为 `Mods` 的目录，或经过结构校验确认是 RimWorld 本地 Mod 根目录。
5. 所有目标必须通过规范化绝对路径、目录包含关系和 Reparse Point 检查。

该命令不执行 `ProjectTemplatePreflight`。安装随包工具 Mod 不要求用户先把 `MyCustomMod` 模板重命名，只需要能够定位工具组件和目标 Mods 目录。

## 安装判断规则

### 1. 目标定位

不能只按文件夹名判断是否已安装。安装命令必须扫描目标 Mods 目录下各 Mod 的 `About/About.xml`，按 packageId 大小写不敏感匹配。

- 未找到 packageId：使用清单中的标准文件夹名安装。
- 找到唯一匹配：在该实际目录上比较和升级。
- 找到多个相同 packageId：拒绝操作并返回 `DUPLICATE_PACKAGE_ID`。
- Workshop 中存在相同 packageId：输出警告，提醒游戏可能出现重复 Mod；第一阶段不修改 Workshop 内容。

### 2. 目录哈希

目录哈希使用规范化 SHA-256：

1. 递归枚举文件。
2. 相对路径统一分隔符并按序排序。
3. 每个文件参与哈希的数据包含相对路径、文件长度和文件字节。
4. 不包含创建时间和最后修改时间。
5. 拒绝目录链接、符号链接和 Windows Reparse Point。

比较已安装目录时排除以下用户或运行时文件：

- `PublishedFileId.txt`
- `defs.db`
- 临时文件、日志和明确登记的运行时输出

排除规则必须集中定义并有单元测试，不能在不同命令中各写一套。

### 3. 版本和哈希决策表

| 状态 | 动作 | 结果代码 |
| --- | --- | --- |
| 目标不存在 | 安装 | `installed` |
| 规范化哈希完全相同 | 跳过 | `unchanged` |
| 源版本高于目标版本 | 更新 | `updated` |
| 源版本低于目标版本 | 跳过 | `target_newer` |
| 版本相同但哈希不同 | 默认拒绝覆盖 | `same_version_conflict` |
| 目标版本不可识别 | 默认拒绝覆盖 | `unknown_target_version` |
| 找到重复 packageId | 拒绝整个命令 | `duplicate_package_id` |

“同版本但哈希不同”默认拒绝覆盖，用于保护用户本地修改、识别未提升版本的重新构建，并避免静默清除未知文件。

## 原子安装流程

正式安装按以下顺序执行：

1. 定位 RimDevTools 可执行文件旁的 `.tools/Data`。
2. 读取并验证 `tool-mods.manifest.json`。
3. 校验两个源组件的 About、packageId、版本、主程序集和目录哈希。
4. 解析目标 Mods 目录。
5. 扫描本地 Mods 和 Workshop packageId 冲突。
6. 生成两个组件的不可变安装计划。
7. 如果是 `--dry-run`，输出计划并结束。
8. 取得 RimWorld 环境级全局锁。
9. 等待正在运行的 RimWorld 退出。
10. 在目标 Mods 目录同卷创建暂存目录。
11. 将所有需要安装或更新的组件复制到暂存目录并重新校验哈希。
12. 备份现有目标目录。
13. 批量替换两个组件。
14. 任一替换失败时，回滚本轮已经替换的全部组件。
15. 全部成功后删除备份和暂存目录。
16. 输出每个组件的源版本、目标旧版本、最终路径、哈希和动作。

更新时必须保留：

- `PublishedFileId.txt`
- RimSearcher DataMod 根目录下已有的 `defs.db`

除明确保护项外，旧目录内容应由新发布组件替换，避免旧 DLL 和废弃依赖残留。

## 取消 init 的修改范围

### CLI

- 删除 `RimDevTools/Commands/InitCommand.cs`。
- 删除 `CliApplication` 中的 `init` 分发。
- 删除 `CliArgs` 中的 `init` 参数契约。
- 删除 `CliHelp` 中的 `init` 入口、帮助文本和示例。
- 更新顶层帮助，说明发布包根目录本身就是项目模板。

### 发布脚本

重构 `.tools/publish-rimdevtools.ps1`：

1. 清空并创建发布暂存目录。
2. 复制 `components/ProjectTemplate/1.6` 的全部模板内容到发布根目录。
3. 继续过滤 `.claude/settings.local.json` 等本机私有文件。
4. 将 `components/ProjectTemplate/mod_config.jsonc` 复制到发布根目录。
5. 在发布根目录创建并组装 `.tools`。
6. 不再复制 `.tools/components/ProjectTemplate`。
7. 构建并组装两个工具 Mod。
8. 生成工具 Mod 组件清单。
9. 从空输出目录完成全量发布验证。

### 模板文件

- 将通用配置从 `components/ProjectTemplate/1.6/mod_config.jsonc` 移到 `components/ProjectTemplate/mod_config.jsonc`。
- 更新模板 README，删除“运行 init”相关说明。
- 更新配置注释，删除“init 运行后自动更新”表述。
- 保留 `ProjectTemplatePreflight`，继续阻止尚未替换 `MyCustomMod` 占位符的项目执行普通 `deploy` 和 `test`。

### Manifest

现有 `components/ProjectTemplate/manifest.json` 不再服务于运行时 init。可以保留为发布构建期布局清单，但必须改成验证最终合并输出，而不是描述一个将被 init 复制的嵌套模板。该文件不进入最终发布包。

## 代码修改范围

预计涉及以下区域：

### RimDevTools CLI

- `RimDevTools/CliApplication.cs`
- `RimDevTools/Infrastructure/CliHelp.cs`
- `RimDevTools/Commands/InitCommand.cs`
- 新增 `RimDevTools/Commands/ModsCommand.cs`
- 新增工具组件清单、目录哈希、版本比较和安装事务相关基础设施
- `RimDevTools/Commands/DoctorCommand.cs`

### 发布与配置

- `.tools/publish-rimdevtools.ps1`
- `components/ProjectTemplate/manifest.json`
- `components/ProjectTemplate/mod_config.jsonc`
- `components/ProjectTemplate/1.6/mod_config.jsonc`
- `components/ProjectTemplate/1.6/README.md`
- 根 `README.md`
- `RimDevTools/README.md`

### LogSyncMod 版本

- `About/About.xml`
- `source/TempProject/Properties/AssemblyInfo.cs`
- AutoTest 程序集版本配置

### 测试

- `RimDevTools.Tests/Program.cs`
- `RimDevTools.ReleaseTests/Program.cs`

RimSearcher DataMod 的自动导出逻辑、请求协议、结果协议和数据库 Schema 不需要因本方案修改。

## 测试计划

### 1. 单元测试

必须覆盖：

- 目录哈希不受文件时间、枚举顺序和路径分隔符影响。
- 目录内容、相对路径或文件长度变化会改变哈希。
- Reparse Point 被拒绝。
- About `modVersion` 和程序集版本解析。
- 目标不存在、完全相同、目标较旧、目标较新、同版本冲突、未知版本。
- packageId 大小写不敏感匹配。
- 同一 Mods 根下重复 packageId 拒绝安装。
- `--mods-path` 优先于项目配置。
- 未指定路径时从项目配置和 Steam 环境解析 Mods。
- `--dry-run` 不创建、删除或替换任何文件。
- 失败回滚两个组件，不留下半安装状态。
- `PublishedFileId.txt` 和 `defs.db` 得到保留。
- 未知参数在执行前失败。
- 删除 `init` 后调用 `init` 返回 `UNKNOWN_COMMAND`。

### 2. 发布布局测试

必须从不存在的输出目录重新生成发布包，并验证：

- 发布根目录包含模板内容、唯一 `mod_config.jsonc` 和 `.tools`。
- 发布根目录内容与 `components/ProjectTemplate/1.6` 合并规则一致。
- `components/ProjectTemplate/1.6` 内不存在 `mod_config.jsonc`。
- 发布包内只有一份项目 `mod_config.jsonc`。
- 发布包不存在 `.tools/components/ProjectTemplate`。
- 两个工具 Mod 的 About、程序集和运行依赖完整。
- LogSyncMod 组件不包含源码和开发目录。
- RimSearcher DataMod 包含 `Native/e_sqlite3.dll`。
- `tool-mods.manifest.json` 与实际目录哈希一致。
- RimSearcher CLI 与 DataMod 版本兼容。
- LogSyncMod About 和两个程序集版本一致。
- 日志和数据库目录为空，不携带历史产物。
- 从发布根目录和 `.tools` 目录执行 `doctor` 都能发现同一项目根。

### 3. 临时 Mods 目录集成测试

使用临时目录模拟 RimWorld Mods：

1. 空目录首次安装两个组件。
2. 第二次执行得到两个 `unchanged`。
3. 安装较旧目标后执行升级。
4. 放置较新目标后确认跳过。
5. 制造同版本不同内容后确认拒绝覆盖。
6. 制造重复 packageId 后确认整个命令不写入。
7. 在第二个组件替换阶段模拟失败，确认第一个组件回滚。

### 4. 真实 RimWorld 验证

- 使用默认 `rimworld_dir` 安装到真实 Mods。
- 使用显式 `--mods-path` 安装到同一 Mods。
- 重复执行确认哈希相同不复制。
- 在游戏 Mod 列表中确认两个 Mod 均可独立识别和启用。
- 执行 `test run --mode quick` 验证 LogSyncMod。
- 执行 `data refresh` 和 `data status` 验证 RimSearcher DataMod。
- 确认真实游戏退出后无暂存目录、备份目录、遗留事务和残留进程。

## 分阶段实施顺序

### 阶段一：固定版本和组件契约

- 补齐 LogSyncMod `modVersion`。
- 统一 LogSync 两个程序集版本。
- 定义工具组件清单和规范化目录哈希。
- 完成版本与哈希单元测试。

### 阶段二：重构发布布局

- 移动通用 `mod_config.jsonc`。
- 直接复制 1.6 模板到发布根。
- 删除随包嵌套 ProjectTemplate。
- 构建并组装两个工具 Mod。
- 生成组件清单。
- 更新发布布局测试。

### 阶段三：删除 init

- 删除命令实现、注册、参数和帮助。
- 删除 init 专用测试。
- 更新 README、模板文档和技能中的旧命令引用。
- 验证发布根目录可直接执行 `doctor`、`data status` 和普通项目流程。

### 阶段四：实现工具 Mod 安装命令

- 实现路径解析、组件预检、packageId 扫描、版本比较和哈希计划。
- 先完成 `--dry-run`。
- 再加入全局锁、等待游戏退出、暂存替换和批量回滚。
- 增加 doctor 中的随包/已安装组件状态。

### 阶段五：完整验证

- RimDevTools 单元回归。
- RimSearcher CLI/DataMod 回归。
- 从空目录发布。
- 发布布局测试。
- 临时 Mods 安装矩阵。
- 真实 RimWorld quick 和 data refresh。

## 验收标准

只有以下条件全部满足才算完成：

1. `RimDevTools-Package` 可从空目录稳定生成。
2. 发布根结构严格等于 1.6 模板内容、唯一配置和 `.tools` 的合并结果。
3. 发布包不再包含嵌套 ProjectTemplate，也不再提供 `init`。
4. `.tools/Data` 同时包含可安装的 LogSyncMod 和 RimSearcher DataMod。
5. 两个组件的版本、主程序集和目录哈希均可验证。
6. 安装命令支持显式 Mods 路径和配置自动解析。
7. 相同内容不重复复制，较旧目标可升级，较新目标不降级。
8. 同版本冲突、未知版本和重复 packageId 不会被静默覆盖。
9. 两个组件作为一个批次安装，失败时不会留下半完成状态。
10. quick、data refresh 和 data status 在真实 RimWorld 环境通过。

## 实施前最后检查项

以下事项在正式编码开始时再次确认，但不阻塞本执行计划：

- LogSyncMod 本轮采用的正式版本号。
- `mjcg.AutoAbility` 是否确实应作为 LogSyncMod 硬依赖；如果只是技能测试的可选集成，应单独调整依赖声明。
- 是否需要在后续版本增加显式 `--force`。第一阶段默认不提供。
