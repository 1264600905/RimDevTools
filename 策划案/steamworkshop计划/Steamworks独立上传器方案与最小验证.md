# Steamworks 独立上传器方案、能力边界与验证结果

## 1. 原始需求记录

> 我们能不能模拟这个环世界模组上传的流程，做到不启动游戏上传模组？比如说接入steamworkshop的dll之类的？

> 你写入新文档，放到策划案下面，然后先写个简单的验证

> 这个是专门提供 rimdevtools测试的 路径 D:\RimworldProject3\奶牛枪  你部署这个然后模组内容实际上传一次创意工坊看看能不能做到。

> 我确认一下你这个 workshop相关的能力边界，比如说你可以直接帮我订阅steam的模组嘛？ 比如说你可以搜索模组嘛？ 比如说你可以看模组的简介嘛？比如说可以指定上传的代理地址嘛？

> 刚才奶牛枪你改改内容，然后试试能不能更新？ 还有你是否可以通过这个workshopdll去修改模组的可见性和描述文本以及预览图片？

> 好，这个功能感觉很强啊，你写进文档，上面两轮对话验证的能力都写进文档

本方案以独立进程直接使用 Steamworks 上传 RimWorld 模组。此前《创意工坊自动更新接口设计》中的游戏内 HTTP 上传器保留为另一条方案，不作为本方案依赖。

**当前状态：已完成只读会话探测、奶牛枪私有条目的首次完整上传，以及同一条目的内容与描述更新。两次发布全过程均未启动 RimWorld。** 最初“只做只读验证”的范围已由后续明确上传、更新请求扩展。

下文将本机实测、已实现但未完整实测、官方 API 支持但尚未接入三种情况分别记录，不把能力讨论当作已经通过的测试。

## 2. 可行性与证据

### 已验证的接口与项目事实

- RimSage 读取 `Verse.Steam.Workshop.Upload`：现有条目的更新最终使用 `SteamUGC.StartItemUpdate`、`SetItemContent` 等设置接口与 `SubmitItemUpdate`。上传传输本身可以在独立进程中重新实现，无需调用依赖游戏窗口和长任务的 `Workshop` 类。
- Valve 的 Steamworks API 文档允许可执行程序初始化 Steam API；Steamworks.NET 提供 Non-Unity 独立程序集接入方式。
- 当前项目是 .NET 10 CLI，已通过少量 C# 原生绑定直接加载本机 `steam_api64.dll`，没有新增 Steamworks.NET 包依赖，也不加载游戏主程序集执行上传。现有 `SteamSessionGuard` 仍仅检查 Steam 进程存在；helper 另外检查在线认证、实际 App ID 和目标作者。
- 本机当前游戏安装目录包含 `steam_api64.dll` 与 `com.rlabrecque.steamworks.net.dll`。原生库导出 `SteamAPI_SteamUser_v021`、`SteamAPI_SteamUtils_v010`、`SteamAPI_SteamUGC_v016`，可用少量 C# 原生绑定做独立验证。
- 当前 DLL 支持只读的 UGC Details Query；查询通过 Steam API 异步结果返回，不需要网页抓取。
- RimSage 读取 `Workshop.BakeAndUpload`、`UnityStubs.ModAssetPacks.BakeForUpload`：1.7 烘焙与 Steam 上传是两项工作。烘焙会在发布目录生成 `Assets.zip`；Steam API 仅负责上传已经准备好的目录。

### 能力边界

| 能力 | 当前实现与验证状态 | 使用边界 |
|---|---|---|
| 独立会话探测 | **已实现、已实测**：初始化成功、用户在线、实际 App ID 为 `294100`、UGC 查询完成 | `workshop probe`；只读成功不能代替作者和提交权限校验 |
| 首次上传模组 | **已实现、已实测**：创建奶牛枪私有条目并上传完整目录 | `workshop upload --create-private`；本地已有 ID 时拒绝重复创建 |
| 更新已有模组 | **已实现、已实测**：同一 ID 更新为奶牛枪 `0.1.1`，没有新建条目 | 使用 `--expected-published-file-id`，要求项目与部署目录中的 ID 均匹配，并验证当前账号为作者 |
| 修改工坊描述 | **已实现、已实测**：新版描述已提交，并通过远端长描述逐字比对 | `--update-description` 读取部署目录 `About/About.xml` 的 description；不传参数时更新保留远端描述 |
| 修改可见性 | **已实现；已实测设置并保持 private** | `--visibility public/friends-only/private/unlisted`；尚未实测四种状态之间的切换。省略时保留原可见性，新建条目仅允许 private |
| 修改主预览图 | **已实现、已实测上传现有图片**；尚未实测替换为另一张图片 | 使用 `SetItemPreview`，图片取自部署目录 `About/Preview.png`；没有独立 `--preview-file` 参数 |
| 读取条目简介与详情 | **上传核验内已实现、已实测**：读取标题、作者、长描述、可见性、大小和更新时间 | 尚无独立通用详情命令。公开页面也可通过网页读取；私有条目受当前账号访问权限限制 |
| 搜索本机模组 | **现有功能**：`mod search` 检索本地及本机 Workshop 安装目录 | 不是工坊在线关键词搜索；本轮未专门重测该既有命令 |
| 搜索在线工坊模组 | **官方 API 支持，CLI 尚未接入、未实测** | 可用 `CreateQueryAllUGCRequest`、`SetSearchText`，限定 RimWorld 并检索标题或简介。聊天中可进行网页搜索，不代表 CLI 已具备在线搜索命令 |
| 订阅模组 | **官方 API 支持，CLI 尚未接入、未实测** | 可用 `SubscribeItem` 绑定当前 Steam 账号。订阅成功、下载完成、游戏启用和加载顺序分别处理，不能据此自动声称模组已可用 |
| 按请求指定 HTTP 查询代理 | **可为未来自有 HTTP 客户端实现；当前未实现、未实测** | 适用于自行发起的网页/Web API 请求，不等同于 Steamworks 上传代理 |
| 为单次 Steamworks 上传指定代理 | **当前不支持，未实测网络层替代方案** | UGC 接口没有公开的单次上传代理设置，CLI 也没有 `--proxy`。不能保证设置 `HTTP_PROXY` 就覆盖上传；TUN、VPN 或进程规则需要另外验证实际流量路径 |

可见性数值为：`public=0`、`friends-only=1`、`private=2`、`unlisted=3`。其中 unlisted 是任何取得链接的人均可访问，但不出现在全局查询中的状态，不是私有。

当前描述和可见性参数随内容上传一并提交，尚无“只改元数据、不上传内容目录”的独立命令。主预览图与模组内容中的任意图片也不是同一个概念。

### 已完成的验证

测试项目为 `D:\RimworldProject3\奶牛枪`，package ID 为 `trigger.cowgun`，游戏版本为 1.7。部署目标为 `H:\SteamLibrary\steamapps\common\RimWorld\Mods\CowGun`。

工坊条目：[奶牛枪，3833398574](https://steamcommunity.com/sharedfiles/filedetails/?id=3833398574)，当前为私有。

| 检查项 | 首次上传 | 已有条目更新 |
|---|---|---|
| 操作 | 新建私有条目后提交完整内容 | 更新同一条目，无创建操作 |
| `publishedFileId` | `3833398574` | `3833398574` |
| `creationAttempted` / `created` | `true` / `true` | `false` / `false` |
| Steam 创建结果 | `CreateItem: EResult=1 (OK)` | 未调用创建 |
| Steam 提交结果 | `SubmitItemUpdate: EResult=1 (OK)` | `SubmitItemUpdate: EResult=1 (OK)` |
| 内容文件数 | 8，包含生成的 `About/PublishedFileId.txt` | 8 |
| 本地与远端内容字节数 | 均为 `58,941` | 均为 `59,100` |
| 远端标题 | 奶牛枪 | 奶牛枪 |
| 远端作者、应用 | 作者匹配当前 Steam 用户；应用为 `294100` | 同前，提交前与提交后均核验 |
| 远端可见性 | `2`，private | `2`，private |
| 远端更新时间（Unix 秒） | `1793767460` | `1793767916` |
| 工坊协议是否仍需用户接受 | `false` | `false` |
| RimWorld 进程 ID 列表 | 运行前、后均为 `[]` | 运行前、后均为 `[]` |
| 最终命令结果 | `ok=true`、`succeeded=true` | `ok=true`、`succeeded=true` |

第二次发布前修改并部署了以下内容：

- `About/About.xml` 增加 `modVersion=0.1.1`。
- `Gun_CowGun` 的 `warmupTime` 从 `2.5` 改为 `2`，同步 Def 描述及中英文翻译。
- 工坊描述新增 `[b]0.1.1[/b]：射击前预热时间调整为 2 秒。`，更新后读取远端长描述与本地请求一致。
- 主预览图沿用原图，远端预览文件大小为 `50,634` 字节；没有测试换图或切换公开可见性。

PublishedFileId 已写入源项目和部署目录的 `About/PublishedFileId.txt`，二者一致。重复创建拦截、无效 ID、无效可见性，以及私有创建不能搭配公开可见性的参数检查均已执行。

**证据范围**：以上是用户要求写入文档的结论摘要，依据本聊天中 RimDevTools 已返回的原始 JSON；没有另存 stdout、日志、截图或存档，也没有新增证据目录。发布成功由 Steam 提交回调及随后远端详情查询确认；没有重新下载内容逐文件比较，也没有启动游戏验证这次预热时间修改的运行时表现。

### 尚未验证的部分

- 1.7 资产包的完全离线烘焙及其引擎依赖。奶牛枪没有自定义 Textures/Sounds，本次上传不能证明通用烘焙已实现。
- 其他操作系统、其他游戏版本、不同 Steam 原生接口版本，以及其他账号的环境。
- 四种可见性之间的切换、新图片替换、独立详情命令、在线搜索、订阅与下载等待、上传代理。
- 大型模组传输、网络中断、超时后的恢复等异常路径的端到端验证；已有保护代码不等于这些情况已全部实测。

### 来源

- [Steamworks API 初始化、App ID 与回调](https://partner.steamgames.com/doc/sdk/api)
- [Steam Workshop 创建与更新流程](https://partner.steamgames.com/doc/features/workshop/implementation)
- [ISteamUGC](https://partner.steamgames.com/doc/api/ISteamUGC)
- [订阅模组](https://partner.steamgames.com/doc/api/ISteamUGC#SubscribeItem)
- [在线关键词搜索](https://partner.steamgames.com/doc/api/ISteamUGC#SetSearchText)
- [描述与长描述](https://partner.steamgames.com/doc/api/ISteamUGC#SetItemDescription)
- [主预览图](https://partner.steamgames.com/doc/api/ISteamUGC#SetItemPreview)
- [可见性枚举](https://partner.steamgames.com/doc/api/ISteamRemoteStorage#ERemoteStoragePublishedFileVisibility)
- [Steam 网络与代理涉及的连接类型](https://help.steampowered.com/en/faqs/view/2EA8-4D75-DA21-31EB)
- [ISteamUtils 异步结果](https://partner.steamgames.com/doc/api/ISteamUtils)
- [Steamworks.NET 独立程序集接入](https://steamworks.github.io/installation/#non-unity-instructions)
- [Steamworks.NET 20.2.0 回调布局](https://github.com/rlabrecque/Steamworks.NET/blob/20.2.0/com.rlabrecque.steamworks.net/Runtime/autogen/SteamCallbacks.cs)
- [SteamWorkshopAgent 同路线实现](https://github.com/pardeike/SteamWorkshopAgent/blob/main/src/SteamWorkshopAgent/SteamworksPublisher.cs)：作为独立上传器的参考，其文档所述 macOS 验证不等于本机 Windows 验证。

## 3. 已落地实现与命令

### 第一阶段：只读验证，已完成

已实现命令：

```powershell
RimDevTools.exe workshop probe [--published-file-id <id>] [--timeout-seconds <seconds>] [--verbose]
```

具体位置：

1. `RimDevTools/CliApplication.cs`、`RimDevTools/Infrastructure/CliHelp.cs`：增加命令路由、参数约束与帮助。超时限定为 1 至 120 秒，默认 15 秒。
2. `RimDevTools/Commands/WorkshopCommand.cs`：发现游戏原生库，只启动同一 CLI 的内部 helper；在 helper 的环境中设置 App ID，不修改当前进程环境、游戏配置或安装文件。
3. `RimDevTools/Infrastructure/SteamworksProbe.cs`：仅绑定初始化、用户状态、App ID、UGC 查询和异步结果读取函数，不包含任何创建或上传接口。
4. `RimDevTools/Program.cs`：在内部 helper 模式处理探测；原生库在子进程加载，原生崩溃不会直接终止外层 CLI。原生 stdout/stderr 仅在内存读取，不落盘。

验证内容：

- `SteamAPI_Init` 成功。
- 当前 App ID 为 `294100`，Steam 用户 ID 有效且 `BLoggedOn=true`。
- UGC 接口非空。
- 提供 `--published-file-id` 时，发起一次该条目的 Details Query，泵送回调并取得匹配 query handle 的完成结果；要求 `EResult=OK` 且返回一条结果。
- 未提供条目 ID 时只验证会话与接口，明确返回未执行条目查询；不能据此声称 UGC 网络查询已通过。
- 记录运行前后的 RimWorld 进程 ID，整个实现不调用游戏启动器。
- `uploadAttempted=false`；查询成功不代表当前用户是作者，也不代表具有上传权限。

运行方式（在项目根目录）：

```powershell
dotnet build RimDevTools/RimDevTools.csproj
RimDevTools/bin/Debug/net10.0/RimDevTools.exe workshop probe --help
RimDevTools/bin/Debug/net10.0/RimDevTools.exe workshop probe --published-file-id 2009463077
```

示例 ID 为 Harmony 的公开条目，仅用于只读查询，不用于上传。验收以命令当次 stdout JSON 为准。不得创建测试证据目录、重定向日志、保存 stdout 副本或另写验证报告。此命令不需要生成磁盘测试产物，不补造 `artifacts` 路径。

### 第二阶段：首次上传与已有条目更新，已完成

实际实现统一使用 **`workshop upload`**；没有 `workshop update` 命令。

实现位置：

1. `RimDevTools/Commands/WorkshopUploadCommand.cs`：定位当前项目的部署目录，检查 About 一致性、预期 ID、发布文件清单与 SHA-256，管理环境锁及独立 helper。
2. `RimDevTools/Infrastructure/SteamworksUpload.cs`：在线会话、作者与应用校验，私有创建、ID 保存、内容和元数据设置、回调与远端核验。
3. `RimDevTools/CliApplication.cs`、`RimDevTools/Infrastructure/CliHelp.cs`、`RimDevTools/Program.cs`：参数契约、帮助、命令路由和内部 helper 入口。

| 参数或输入 | 行为 |
|---|---|
| `--create-private` | 创建新私有条目；源项目或部署目录已有有效 ID 时拒绝重复创建 |
| `--expected-published-file-id <id>` | 更新已有条目；与创建模式二选一，两个本地 ID 必须都匹配 |
| `--update-description` | 明确要求用部署 About.xml 更新远端描述；新建时会设置描述 |
| `--visibility <value>` | 明确设置可见性；更新时省略则保持原值；私有创建只允许 private |
| 部署目录 `About/Preview.png` | 作为工坊主预览图；当前上传流程会调用 `SetItemPreview` |
| `--dry-run` | 只检查本地内容和发布参数，不联系 Steam，不提交更新 |
| `--timeout-seconds <seconds>` | 默认 180 秒，允许 10 至 1800 秒；结果不明确时不自动重试 |
| `--verbose` | 原生进程诊断写到 stderr，不保存副本 |

源项目和部署目录必须同时保留相同的工坊 ID。创建成功后 helper 立即写入两个 ID 文件，避免后续误建重复条目。

**工具位置**：新命令位于当前 RimDevTools 源码构建产物。本次奶牛枪部署使用其项目自带的 `.tools/RimDevTools.exe`，上传使用下面的源码构建；不能假定奶牛枪原有工具或已发布模板包已同步加入新命令。

下面是已使用的更新流程示例，最后一条会真实提交已有条目：

```powershell
$rimDevTools = 'D:\RimworldProject\环世界日志同步mod\RimDevTools\bin\Debug\net10.0\RimDevTools.exe'
Set-Location -LiteralPath 'D:\RimworldProject3\奶牛枪'

# 先检查完整部署计划，再构建并部署。
& '.\.tools\RimDevTools.exe' deploy --dry-run
& '.\.tools\RimDevTools.exe' deploy

# 更新前检查目标 ID、描述、可见性与文件清单。
& $rimDevTools workshop upload --expected-published-file-id 3833398574 --update-description --visibility private --dry-run

# 提交到同一个条目，不创建新条目。
& $rimDevTools workshop upload --expected-published-file-id 3833398574 --update-description --visibility private --timeout-seconds 180
```

### 第三阶段：通用发布内容准备与离线烘焙，待实现

将构建、发布目录准备、1.7 资产烘焙与上传拆开。初版只接收已经准备好的目录。研究离线调用资产烘焙库的依赖后，再决定是否扩展；不得将“运行游戏可执行程序的专用烘焙模式”描述成完全不启动游戏程序。

## 4. 风险与替代方案

- Steam 客户端必须运行并在线登录；DLL 加载、API 初始化、账号认证和实际提交权限是不同阶段。
- App ID 只是调用上下文，不赋予目标条目的编辑权限。若改用独立工具 App ID，跨应用发布需游戏方配置 App Publish Permissions，不能假定任意 App ID 都能更新 RimWorld 工坊。
- 验证绑定本机已确认的 Windows x64 接口版本；版本或位数不匹配时明确报错。正式发布时应评估匹配的独立版 Steamworks.NET，减少手写 ABI 布局维护。
- 处理回调与释放查询句柄必须完整；正常和失败路径均关闭 Steam API 并释放已加载的原生库。
- helper 超时或取消只终止本次 helper，不关闭 Steam 或任何游戏进程。
- 当前实际上传命令要求 RimWorld 已关闭；没有自动关闭游戏的行为。只读 probe 与上传命令的要求需分别理解。
- 本轮不分发 Steam 原生 DLL，直接使用本机安装文件。打包正式上传器前再确定依赖获取与分发方式。
- Steam 已提交的更新不能由本地事务撤销；需要恢复时重新上传已验证的旧版包。
- 替代方案包括原文档的游戏内上传器和显式 SteamCMD 上传。当前“不启动游戏”验证失败时仅报告失败，不自动转入这些路径。
- 作者和条目权限仍由 Steam 校验；不能修改其他作者的条目，私有条目读取也受账号权限限制。
- 代理方面，只能说当前没有按上传任务配置代理；本次没有核查网络出口，不能据此声称上传一定直连或已经经过代理。网络层方案应覆盖实际参与通信的进程，并单独实测。

## 5. 实施难度

- 最小会话与 UGC 查询验证：★★☆☆☆（2/5）。
- 更新已准备好的模组目录并可靠处理结果：★★★☆☆（3/5）。
- 加入完全离线烘焙与完整发布恢复：★★★★☆（4/5）。

会话、首次上传、已有条目内容更新和描述更新已完成本机实测；“完整发布工具”的成熟度仍取决于异常恢复、通用资产烘焙、打包分发及其余待接入能力。

## 6. 待明确问题

1. 后续订阅功能需要覆盖到订阅回调、下载等待、依赖处理还是游戏启用与排序；这些应分别定义验收条件。
2. 在线搜索与独立详情命令需要哪些筛选、排序、语言和输出字段。
3. 是否继续实测可见性状态切换及更换预览图；当前测试条目保持私有，不能将本次记录理解为允许自动公开。
4. 代理目标是自有 HTTP 请求还是 Steamworks 内容传输，以及网络层方案需要覆盖的进程与生效验证方式。
5. 通用上传功能是否必须包含 1.7 离线烘焙；当前实现上传已经准备好的目录。
6. 正式依赖获取、打包发布和异常恢复策略；当前实现读取本机游戏安装中的原生 DLL。
