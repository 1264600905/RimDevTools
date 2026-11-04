# steamworkshops 三批次实施计划与奶牛枪真实验收

状态：实施中。第一批基础内核、诊断查询、搜索分页与部署收据已完成实现及奶牛枪真实验收，详见 [第一批完成清单](steamworkshops第一批完成清单.md)。第二、三批仍待完成；下文保留实施前需求和冻结的验收条件。

## 1. 原始需求记录

> 接下来咱们就只做分析讨论和编写文档了，我的想法是做一个新的rimdevtools命令入口， 叫做 steamworkshops 功能包括 上传模组， 修改模组描述， 搜索模组，订阅模组订阅模组需要支持查询某个模组是否正在下载或者下载完成。   同时支持类似1.7的资源文件自动打包zip，保证和官方的一致性。   但是这些功能还需要进一步确认细节，你帮我分析分析怎么做好，命令怎么写，具体需要输入哪些内容。等的详细计划

> 官方的烘培干啥了，能不进入游戏复现嘛？

> 从项目构建并生成独立发布目录（推荐）

> 使用相同官方库、参数和目录规则，验证资源内容及游戏读取一致（推荐）

> “使用相同官方库”这个好做嘛？不会要带上很多官方dll吧？

> 独立资源路径我感觉不好吧，因为和现有部署连不上，要不这样直接从部署的文件上传，但是新增一个哈希值和存在性校验，看看是不是之前部署的文件，有没有新增内容。 你命令写的太多了，我觉得好多没必要可以简化简化吧。，然后写一下命令。

> RimDevTools.exe steamworkshops upload --create   这个不需要吧？

> 加点小细节，doctor命令检查后必定输出一行关于steam是否启动，steam是否购买环世界，steamworkshop是否在线可用的输出，默认常驻显示。  新增一个 RimDevTools.exe steamworkshops status命令，自动检查workshop是否在线可用等，同时打印当前模组的id，创意工坊状态公开还是私有，订阅人数，历史更新次数。   搜索也支持显示搜索的目标模组的这些信息，注意steam有一个 PublishedFileId.txt里面会记包名的，你查当前模组优先使用这个。

> 还有RimDevTools.exe steamworkshops status命令需要检查用户是否有更新模组的权限，只有第一次上传的人才有权限更新模组，贡献者只能更新描述和图片。

> 游戏授权不需要过分确认，只要库里面有环世界就能上传。还有就是部署支持一个部署并上传的能力。

> 把计划写成可以直接落地实施的计划，指定真实测试环境 H:\SteamLibrary\steamapps\common\RimWorld\Mods\CowGun  和     D:\RimworldProject3\奶牛枪    分三批次任务实现全部功能并使用奶牛枪这个项目进行真实的测试部署等。

> 你把这个搬进去，写明这个是测试的贴图就行 H:\SteamLibrary\steamapps\common\RimWorld\Mods\社长OC计划\Textures  都是我写的mod。

> 历史更新次数找不到可以看到最近一次更新的时间嘛？   首次上传和可见性切换都需要进行真实测试，你可以稍微改改奶牛枪的包名或者删掉发布文件就能测试首次上传了，已经上传的内容不需要删除，我会在测试完毕后自行删除的，贡献者账号我还真没有，这检测先不做吧。

> 那历史更新次数 就改成 最近一次更新的时间. 好都写入文档

> 优先遵循官方的分页方式和说明，官方接口支持30条分页嘛？详情显示也使用官方支持的模式。 发布后超时不留下阻塞逻辑，而是返回值告知上传超时，以及当前时间和目标模组的最新提交时间，让AI自己判断提交成功没有。

> 这样我们自己做个自定义分页吧，就是简略搜索以25个为一页，最多支持3页，详细搜索以10个为一页，最多支持5页。更多的页数就不支持了，说明搜索不够准确。 还有我们之前不是说了部署后上传创意工坊需要做哈希值校验嘛？那个哈希值校验的文本你会放到哪？

> 改成到.tools/ProjectData 下面 分页的数据暂存也可以放到这个路径，同时这个路径还要加入gitignore  以后如果还有新文件暂存数据都放进这个里面。分页支持next 命令快速查看下一个分页。

**最新范围更新：历史更新次数已取消，统一显示最近一次更新时间；首次发布与四种可见性切换必须真实验收；用户允许临时修改奶牛枪身份并保留新建远端条目，由用户测试后自行删除。贡献者专项检测和真实测试暂缓，不作为本轮三批完成条件。**

**需求更新：最新指令取代此前“从项目构建独立发布目录”的选择。现在以普通 deploy 的成功结果为上传来源，不再提供 prepare 或独立发布目录入口。**此前有关官方库、参数和目录规则的一致性要求继续有效。

**实施前最新澄清：底层仍使用原生 Steamworks 官方每页最多 50 条的查询，上层改为自定义分页：简易搜索每页 25 条、最多 3 页（前 75 条），详情搜索每页 10 条、最多 5 页（前 50 条），支持 search next 沿用最近搜索查看下一页。超过上限直接提示搜索不够准确、请缩小关键词，不继续查询。详情搜索使用官方默认的 255 字节描述摘要，status 与精确 ID/链接查询使用官方完整描述模式。发布超时仅返回超时和时间证据，不保存跨调用的未确认阻塞，不增加 --recover；是否成功及是否再次提交由调用方 AI 判断。哈希收据、分页缓存及后续新增工具运行/暂存数据统一放在项目根目录 .tools/ProjectData，并加入 .gitignore；校验差异由 CLI JSON 返回。**

### 已确认的决定

| 项目 | 决定 |
|---|---|
| 当前工作范围 | 编写三批实施计划；按最新单独指令已复制测试贴图并更新奶牛枪 README。CLI 逻辑、配置、真实部署与发布均留待实施阶段 |
| 新入口 | `RimDevTools.exe steamworkshops ...`，准确使用这个复数拼写 |
| 默认发布来源 | 游戏 Mods 中由现有普通 deploy 部署的目标目录；用持久部署收据验证来源、存在性、哈希及新增文件 |
| 命令数量 | upload、edit、search、subscribe、status 五个子命令；最新要求明确新增 status，替代此前 subscribe --status 的拟议写法 |
| 首次发布与更新 | 统一使用 upload，根据项目、部署收据及绑定记录自动判断，不再要求 --create |
| 环境摘要 | doctor 默认始终显示 Steam 运行、RimWorld 授权、Workshop 在线状态，成功检查也不隐藏 |
| 游戏库检查 | 仅检查当前 Steam 账号的 RimWorld 库可用状态，不再细分购买来源、家庭共享或试用等许可类型 |
| 部署并上传 | 用户已要求支持；建议通过现有 deploy 的 --upload 参数完成，不增加新子命令 |
| 当前模组定位 | 优先读取项目 About/PublishedFileId.txt 的数字工坊 ID，packageId 另从 About.xml 读取 |
| 权限目标 | 本轮核验原上传账号/owner 的内容及元数据权限；贡献者身份识别与专项编辑检测暂缓，不要求第二个账号 |
| 搜索分页与描述 | 基于原生查询自定义分页：简易 25 条/页、最多 3 页，详情 10 条/页、最多 5 页；search next 延续最近搜索；简易列表只显示名称、工坊 ID、订阅人数，--details 额外显示官方摘要；status 与精确 ID/链接查询显示完整信息和完整描述 |
| 工具数据目录 | 项目根目录 .tools/ProjectData，统一保存本次和以后新增的工具运行元数据、缓存与暂存文件，并加入 .gitignore |
| 哈希收据位置 | .tools/ProjectData/deployment-receipt.json，部署成功后由工具自动维护；不进入游戏 Mods 或上传内容，不另写校验报告 |
| 更新时间 | status 与精确 ID/链接查询显示 Steam 条目最近更新时间；关键词列表按简易/详情字段输出；取消历史更新次数字段与网页计数流程 |
| 发布超时 | 返回超时、当前时间及目标条目的 Steam 最近更新时间，供 AI 判断；不留下跨调用阻塞，不自动重试 |
| 真实发布测试 | 同一奶牛枪项目临时新身份做首次端到端发布；在新条目真实切换所有可见性，结束恢复原身份；远端测试条目由用户自行清理 |
| 一致性标准 | 相同官方库、参数和目录规则，验证资源内容及游戏读取一致；不要求整个 ZIP 逐字节相同 |
| 既有证据 | 奶牛枪首次私有上传、同一 ID 的内容更新与描述更新已经完成，详见原验证文档 |

### 本实施版采用的默认值

- 优先保持上传和烘焙均不启动游戏可执行文件；官方库调用不成立时停止并明确报错。不把用户询问专用模式视为已经允许启动游戏程序。
- 首次上传默认 private，更新保留远端描述和可见性，除非参数明确要求修改。
- 订阅默认只处理指定条目，下载由 Steam 管理，不自动启用、排序或递归订阅依赖。
- 首版正式支持本机 Windows x64 与已验证的目标游戏版本；其他环境逐项验证。
- 官方烘焙在第二批接到普通 deploy 的既有 staging，提交完成后再保存最终部署收据；烘焙失败不能绕过检查继续发布。
- 已有条目 upload 默认只改内容，保留远端标题、简介、主预览图、可见性和人工标签；元数据修改通过 edit 明确执行。
- 常规更新仍使用原 ID=3833398574；首次上传和可见性切换用同一项目中受控创建的临时测试身份，严格复位本地原绑定，不自动删除远端条目。
- 本轮仅检测 owner/非 owner 权限，贡献者专项明确延期；其他必测项不能仅凭字段存在就宣布真实验收完成。
- search 的 --page 是从 1 开始的用户分页：简易每页 25 条、最多 3 页，--details 每页 10 条、最多 5 页；内部映射到官方 50 条页，超过上限先报错并建议缩小关键词。
- steamworkshops search next 使用本项目最近一次关键词搜索的模式和下一页游标；必要数据保存在 .tools/ProjectData/steamworkshops-search-cache.json，缓存有效期默认 10 分钟，不突破原页数上限。
- 本次及以后新增的工具数据文件、运行元数据和临时请求文件统一由工具在 .tools/ProjectData 管理；已有部署事务暂存及日志/测试原始产物继续遵循既有路径约定。
- upload、edit 及 deploy --upload 的发布超时均返回时间证据，后续显式调用仍执行正常前置校验，不因上次超时被额外拦截。

## 2. 可行性与证据

### 2.1 当前已验证的基础

**已验证，来自当前源码及既有实测：**

- 现有 `workshop probe` 和 `workshop upload` 原型使用独立 helper 加载 `steam_api64.dll`，通过当前 Steam 桌面会话查询、创建和更新工坊条目。
- 奶牛枪 ID `3833398574` 已完成首次私有发布及原条目更新；更新为 `0.1.1` 后远端大小为 59,100 字节，描述与请求一致。未启动 RimWorld。
- 原型已经具有作者与应用校验、发布文件哈希复核、预期 ID 校验、明确元数据修改参数，以及结果不确定时不自动重传的保护。这是既有行为证据；新入口按最新要求保留本次调用不自动重试，移除超时后的跨调用阻塞。
- 原型依赖“先部署到游戏 Mods 再上传”，与最新确认的来源一致；需要补齐部署成功基线与上传前复核，而非另建发布流程。
- 原型仍在同一上传器内混合查询、创建、内容提交和元数据设置；尚无独立描述修改、搜索、订阅和下载状态命令。
- 当前 `DeploymentFileTransaction.FilesEqual` 会比较文件长度及 SHA-256；但 `.rimdev-transaction.json` 是回滚事务清单，主要保存原有/新增路径，成功提交后清理，不是持久的成功部署哈希收据，不能直接当作上传基线。
- 当前 doctor 的 AlwaysVisibleChecks 没有 Steam 会话/授权/在线探测项；项目发现失败时也会提前返回。当前 SteamSessionGuard 只按进程存在构造状态，不能用其 IsLoggedIn 代替真实在线登录或购买判断。
- RimSage 的 ModMetaData.Init 明确按 ulong 解析 PublishedFileId.txt；奶牛枪该文件实际内容为 3833398574，包名 trigger.cowgun 则在 About.xml 的 packageId 中。ID 文件不是名称/包名索引。

这些证据支持复用 Steamworks 通信内核，不代表新的全部能力和异常路径已经验证。

既有记录：[Steamworks 独立上传器方案、能力边界与验证结果](Steamworks独立上传器方案与最小验证.md)。

### 2.2 官方的“烘焙”具体做什么

以下事实本轮通过 RimSage 打开决定性源码确认：

| 官方符号与服务源码路径 | 已验证行为 | 对设计的要求 |
|---|---|---|
| `Verse.Steam.Workshop.BakeAndUpload`；`Source/Verse/Steam/Workshop.cs` | 找到官方选定的加载目录，筛选存在 Textures 或 Sounds 的目录，再烘焙与上传 | 调用官方目录和烘焙规则，不能仅按名称扫描 `1.7/Textures` |
| `UnityStubs.ModAssetPacks.Request/BakeSettings/BakeForUpload`；`Source/Engine/UnityStubs/UnityStubs/ModAssetPacks.cs` | 输出加载目录内的 `Assets.zip`；默认 BC7、Quality=1、RawMaxSize=16、AllowBc1=false、Kaiser mip filter；GPU 开关取决于编码器是否已配置 | 不是普通目录压缩；要记录实际设置与编码后端 |
| 同上 `Request`、`SoundFiles` | 将声音按官方 RawSource 规则收集；上传烘焙使用 `shipped=false`，并启用 stamps/roles | 音频处理不能套用贴图规则，也不能改成自认为更适合发布的 shipped 参数 |
| `Waylith.Assets.AssetPackManifest`；`Source/Engine/Waylith/Waylith/Assets/AssetPackManifest.cs` | 资产包中存在 `bake.manifest` 内容哈希清单；旁侧文件有 `.stamps`、`.roles` | 只检查 ZIP 可解压不足以证明资源正确 |
| `Workshop.BuildStagingDirectory` 与 `UnityStubs.ContentPacks` | 发布阶段过滤 `Assets.zip.*` 辅助文件；可选按 `IsPackedSource` 排除已入包资源源文件 | 复用官方谓词，不用“删除全部 PNG”之类替代办法 |
| `Verse.ModContentPack.AllLoadFolders/VersionsInRange`；`Source/Verse/ModContentPack.cs` | 处理 LoadFolders、默认/Common/根目录及版本范围；资产包版本下限为 1.7，特定情况下采用最高的较低版本目录 | 不能用简单的“目录版本 >= 1.7”替代整个选择过程 |
| `Verse.ModMetaData` | 默认 `uploadBuildAssetPack=true`，`uploadSkipSourceTextures=false` | 建议自动烘焙，默认保留源资源 |

可将流程理解为：先按游戏规则选择输入，再处理资源、生成带清单的资产包，最后按官方发布规则组装目录。整个模组仍是一个目录；不会把 DLL、Defs、Languages 等所有文件都打进一个总 ZIP。

官方 UI 的“跳过源贴图”开关实际使用 `IsPackedSource` 判断，不能仅按贴图扩展名理解它。混合支持 1.6/1.7 的模组还需要保留旧版所需源资源，首版默认不剔除源资源。

### 2.3 能否不进入游戏复现，依赖会不会很多

**已验证：**游戏有 `BakeModAssetPacks` 专用模式，`RimWorldWaylith.Bootstrap.Initialize` 在启动 Entry 场景前分支到烘焙流程，完成后退出。它不进入主菜单/存档，但仍运行游戏可执行文件并初始化部分运行环境，不能描述为“完全不运行游戏程序”。

**已验证的静态依赖元数据，仅反射读取，没有执行烘焙：**

| 程序集 | 非框架直接引用情况 |
|---|---|
| `Waylith.AssetBake.dll` | 直接引用 Waylith；公开 `ContentPackBaker.Bake` |
| `UnityStubs.dll` | 11 个非框架直接引用，包括 Waylith、Waylith.AssetBake、兼容层及资源/图形相关库 |
| `Waylith.dll` | 17 个非框架直接引用，包括纹理、Vulkan/GLFW、Zstd/LZ4 等相关库 |

引用数量有重叠，不等于烘焙时必定加载的程序集数量，也不等于完整传递依赖数量。不能承诺“只需三个 DLL”。

**建议：RimDevTools 只分发自己的 helper，不复制整套官方 DLL。**helper 从已安装游戏的 `rimworld_dir` 及其实际程序集/原生库目录按需解析依赖；一个 helper 只绑定一套游戏版本，记录实际加载的版本与哈希。不要修改系统全局 PATH，不依赖当前工作目录随意搜索 DLL。

**推断：**公共烘焙入口与独立 .NET 10 helper 的方向可行，运行游戏 Main 并非调用库方法的必需步骤。

**未解决：**直接调用 `ModAssetPacks.BakeForUpload` 所需的最小初始化、原生纹理/音频依赖、挂载状态及 GPU 环境尚未实测。RimSage 中未取得 `ContentPackBaker` 内核的完整实现，不能据此保证所有资源类型均可脱离游戏引导独立烘焙。

建议首先完成“只准备目录和烘焙、不连接 Steam、不上传”的技术验证。它是正式实施的前置阶段，不在本轮执行。官方专用模式只可在明确的 RimDevTools 测试路径中生成官方参考，不作为正常 deploy/upload 的烘焙回退，不能悄悄启动来掩盖库调用失败。

### 2.4 “与官方一致”的验收口径

已按用户选择确定：

1. 输入加载目录集合、选项、资源枚举和发布过滤与同一套官方实现一致。
2. 使用同一版本官方烘焙库，记录官方 wrapper、核心库及实际编码后端。
3. 对照产物的资源清单、条目内容、资源属性及游戏加载结果，不能只比 ZIP 文件名或总大小。
4. 整个 ZIP 的时间戳、条目顺序等容器差异，不作为单独的失败理由。
5. 同输入、同配置、同后端下，若资源条目内容不同，必须解释原因并完成可读取性、纹理/音频正确性核验，不能笼统声称“都是官方库所以一致”。

官方纹理设置依赖 `TextureBlockEncoder.IsConfigured`，相关编码实现具有 GPU 尝试与 CPU 回退分支。因此不能把独立 CPU 烘焙未经验证地称为与 GUI 的 GPU 烘焙逐字节相同。

### 2.5 外部 API 证据

Steamworks 官方文档支持条目查询、关键词搜索、订阅、下载状态、描述和元数据更新。下载状态是位标志组合，Installed 可以与 NeedsUpdate 并存；上传完成应以提交回调为准，提交后没有取消接口。这些是后文接口设计的依据。

- [Steamworks 初始化与回调](https://partner.steamgames.com/doc/sdk/api)
- [ISteamUGC 查询、搜索、订阅与状态](https://partner.steamgames.com/doc/api/ISteamUGC)
- [下载状态标志](https://partner.steamgames.com/doc/api/ISteamUGC#EItemState)
- [下载与完成回调](https://partner.steamgames.com/doc/api/ISteamUGC#DownloadItem)
- [内容与元数据提交](https://partner.steamgames.com/doc/features/workshop/implementation#Uploading_a_Workshop_Item)
- [可见性枚举](https://partner.steamgames.com/doc/api/ISteamRemoteStorage#ERemoteStoragePublishedFileVisibility)
- [当前 Steam 应用库可用状态](https://partner.steamgames.com/doc/api/ISteamApps#BIsSubscribed)：本方案仅用简单可用性判断，不开展购买来源核验。
- [订阅计数读取](https://partner.steamgames.com/doc/api/ISteamUGC#GetQueryUGCStatistic)
- [Steam 条目详情与最近更新时间](https://partner.steamgames.com/doc/api/ISteamUGC#SteamUGCDetails_t)：使用 m_rtimeUpdated，不以本地修改时间代替。
- [原生 UGC 官方分页](https://partner.steamgames.com/doc/api/ISteamUGC#CreateQueryAllUGCRequest)：页码从 1 开始，kNumUGCResultsPerPage=50；该原生接口没有每页条数参数。
- [官方描述返回模式](https://partner.steamgames.com/doc/api/ISteamUGC#SetReturnLongDescription)：默认返回 255 字节摘要，设置 true 后返回完整描述。
- [Web API QueryFiles](https://partner.steamgames.com/doc/webapi/IPublishedFileService#QueryFiles)：另有 numperpage 参数可指定每页条数，但需要 Web API key；本版继续使用已有 Steam 桌面会话的原生接口，通过本地切片实现 25/10 条用户分页，不引入另一套凭据和查询链路。

## 3. 可落地修改计划

### 3.0 固定环境、测试资源与执行约定

| 用途 | 固定值 |
|---|---|
| RimDevTools 开发仓库 | `D:\RimworldProject\环世界日志同步mod` |
| 唯一主验收项目 | `D:\RimworldProject3\奶牛枪` |
| 唯一主部署目录 | `H:\SteamLibrary\steamapps\common\RimWorld\Mods\CowGun` |
| 游戏安装目录 | `H:\SteamLibrary\steamapps\common\RimWorld` |
| 游戏版本与目标 | Windows x64、RimWorld 1.7、`trigger.cowgun`、`Gun_CowGun` |
| 原有工坊条目 | `3833398574`；项目与部署目录的 About/PublishedFileId.txt 均已确认一致 |
| 发布策略 | 常规更新原条目并保持私有；首次/可见性真实测试使用临时新身份和新条目，测试后本地恢复原绑定，所有远端条目留给用户清理 |
| 实施前实际部署基线 | 8 个文件、59,100 字节；尚无这次新增的测试贴图与资产包 |
| 游戏配置 | `C:\Users\liu\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Config\ModsConfig.xml` |
| Steam 原生库 | `H:\SteamLibrary\steamapps\common\RimWorld\steam_api64.dll` |

后续三批的真实部署、发布、订阅和游戏检查均在以上环境进行。通用命令实现不能硬编码这些路径；真实集成测试夹具则必须检查规范化路径、packageId 和 ID，匹配后才允许变更测试模组。

**已准备的用户测试贴图：**

- 原始来源：`H:\SteamLibrary\steamapps\common\RimWorld\Mods\社长OC计划\Textures`。
- 已复制至：`D:\RimworldProject3\奶牛枪\1.7\Textures\WorkshopTestAssets\ShezhangOC`。
- 180 张 PNG，35,047,341 字节；逐文件 SHA-256 与来源一致，源目录未移动。
- 来源、作者授权及“RimDevTools 工坊测试贴图”用途已写入奶牛枪 README。
- 当前奶牛枪 sync_items **尚未包含 1.7/Textures**；第一批必须补齐同步项，不能看到源项目有贴图就声称已真实部署。
- 未导入来源模组的 Defs 或代码，不把测试贴图误说成奶牛枪已新增的玩法。

游戏读取验收至少覆盖以下真实样本，路径均相对于测试贴图根目录：

| 样本 | 源 PNG 尺寸 |
|---|---|
| CEO/Weapon/PresidentSpear.png | 1024 × 1024 |
| CEO/Icon/President.png | 262 × 262，非二次幂尺寸 |
| CEO/Effects/ReflectShield/ForceField.png | 512 × 512 |
| Wolfein/Heads/WolfeinHead11_south.png | 384 × 384 |

奶牛枪当前自带 `.tools/RimDevTools.exe` 实测版本为 **1.0.8.0**，不是本次功能实现。每批开发完成后，统一使用源码新构建的 Release CLI：

```powershell
$TaskCodeRoot = 'D:\RimworldProject\环世界日志同步mod'
$CowProject = 'D:\RimworldProject3\奶牛枪'
$CowDeploy = 'H:\SteamLibrary\steamapps\common\RimWorld\Mods\CowGun'
$TaskTool = Join-Path $TaskCodeRoot 'RimDevTools\bin\Release\net10.0\RimDevTools.exe'

# 以下命令在各批实施时执行，本轮不执行。
dotnet build (Join-Path $TaskCodeRoot 'RimDevTools\RimDevTools.csproj') -c Release
dotnet run --project (Join-Path $TaskCodeRoot 'RimDevTools.Tests\RimDevTools.Tests.csproj') -c Release
Set-Location -LiteralPath $CowProject
```

不得为了让测试跑过而退回旧 CLI、复制单个 DLL 到游戏目录或换一个无关项目。原有未提交的原型改动需纳入检查后继续开发，不强制清理工作区。

**真实测试驱动：**在既有 RimDevTools.Tests 内新增 SteamWorkshopsLiveTests 与 `--live-steamworkshops --batch 1|2|3 --tool <新CLI>` 模式。它只调用正式 CLI、核对 JSON 和执行受控故障注入，不是一次性归档脚本。每次变更都限制在指定奶牛枪路径，测试结束通过正式部署/事务恢复复位。普通单元测试仍使用原测试入口。

live 模式必须拒绝 FakeRimWorld、RIMDEVTOOLS_FAKE_MODE 和单元测试注入探针，核对使用的游戏可执行文件、Steam 原生库及部署路径均来自上表真实环境；单元检查成功不计为真实用例通过。

live driver 与各批示例命令描述的是同一组真实用例，优先由 driver 执行并检查；手动复现只选需要的步骤，不因文档同时列出两种方式就重复整套远端创建和修改。

日志、截图、游戏测试产物只用 RimDevTools 自动返回的原始路径，禁止重定向、Tee、复制留档或另建日期/任务证据目录。收据和发布事务是工具运行元数据，不是 stdout 副本。每批验收结论直接在聊天汇报；用户未要求时不另写测试报告。

### 3.1 五个子命令，status 统一查看状态

以下是拟议语法，尚未实现。日常在项目根目录运行，不要求反复输入项目路径、部署路径或已绑定的工坊 ID。

| 命令 | 用途 | 最少输入 |
|---|---|---|
| steamworkshops upload | 校验上次普通部署结果，再自动首次上传或更新内容 | 当前项目 |
| steamworkshops edit | 只改描述、可见性或主预览图 | 当前项目；无字段参数时同步项目 About 简介 |
| steamworkshops search <关键词或ID或next> | 搜索模组、按精确 ID/链接查看完整详情，或延续最近搜索查看下一页 | 关键词、条目 ID、工坊链接，或 next |
| steamworkshops subscribe <ID> | 订阅，并可选等待下载完成 | 条目 ID 或工坊链接 |
| steamworkshops status [ID] | 默认检查当前模组的环境、远端信息、统计、权限及本机下载状态；也可指定其他条目 | 当前项目，或显式 ID/链接 |

合并规则：

- 不再提供 prepare：沿用现有 deploy，上传本身不重新构建、不建立另一套发布来源。
- 不单列 probe：命令内部执行必要检查，环境诊断复用现有 doctor。
- 不单列 info：search 输入精确 ID/链接时返回完整简介和条目详情。
- 按最新要求单列 status，不再设计 subscribe --status 这个重复入口。
- subscribe --wait 订阅后等下载；status ID --wait 只观察已有下载，不订阅、不请求下载。
- upload --dry-run 执行当前上传前检查；超时证据由发生超时的调用返回，不增加 operation 命令、--recover 或跨调用未确认阻塞。
- 现有 deploy 增加 --upload，串联普通部署和同一个上传服务，不增加“部署上传”子命令。

正式入口统一为 steamworkshops。现有 workshop 原型及历史文档不作为新语法已实现的证据；旧原型不保留独立上传路径。实现中如需兼容转发，必须进入相同的新共享服务，执行相同的部署收据、权限与超时规则，不能绕过正式入口的校验。

### 3.2 doctor：默认常驻的 Steam 摘要

每次 doctor 检查结束后，都显示一行 Steam 摘要，包括检查通过的情况，不要求 --verbose。“常驻”指每次输出都保留该行，不是启动后台监控。

示意格式（不是本轮实测）：

```text
Steam：已启动 | RimWorld：库中可用 | Workshop：在线可查询
Steam：未启动 | RimWorld：未确认 | Workshop：不可用（Steam 未运行）
```

输出设计：

- 人类可读摘要固定写到 stderr；stdout 继续只返回一个最终 JSON，不破坏已有机器读取契约。
- JSON 中同样默认保留 steam.runtime、steam.rimworld_library、steam.workshop_online 三项，加入 AlwaysVisibleChecks；成功项不被普通输出过滤。
- 项目不存在、配置损坏、缺少 DLL、接口不支持或超时等受控返回路径，也要显示摘要。不能沿用发现项目失败就跳过全部 Steam 摘要的逻辑。
- 使用有界的共享探测；建议默认总预算 10 秒，超时按项返回未确认及原因，不让 doctor 无限等待。
- 普通开发项目不因 Steam 离线就一律判成无法静态构建；摘要常驻与哪些操作必须在线是两回事，发布/订阅命令再严格校验所需前提。

三项检查要独立：

| 项目 | 判定依据与边界 |
|---|---|
| Steam 是否启动 | 当前 Steam 客户端进程；不把程序已安装等同于正在运行 |
| 当前账号库中是否有 RimWorld | 在实际 App ID=294100 的 Steam 会话中做一次库可用性查询；只返回可用、不可用或未确认，不细分许可类型 |
| Workshop 是否在线可用 | 登录与原生接口就绪后执行一次无写入的 RimWorld UGC 查询；实际成功的在线查询才标为 available，不仅检查 DLL 加载或 BLoggedOn |

按最新要求，只要当前账号能从 Steam 库访问 RimWorld，就满足本工具的游戏库前提，不再查询购买流水、GetAppOwner、家庭共享/免费周末/试用等分类，也不把它们另加成上传门槛。可复用当前 App ID 的 BIsSubscribed 等简单查询；无法查询就显示未确认，不把本机有安装文件或 Steam 进程存在直接当成账号库中可用。不要为了检查而调用会启动游戏的 RestartAppIfNecessary。

库中可用与上传目标权限分别处理：不再过度核验游戏授权，但仍检查 Steam 会话、Workshop 在线、条目所属游戏和当前账号的内容更新权限。游戏库条件通过不等于可以覆盖其他作者的条目，实际提交结果仍由 Steam 返回。

在线探测不依赖当前模组是否存在/私有；可用独立的只读 UGC 查询验证服务。具体条目无权限或已删除，不等于整个工坊离线。只取得缓存时注明缓存来源，不能冒充本次在线可用。此检查不证明上传权限或全部 Steam 服务都正常。

### 3.3 常用命令直接写法

在奶牛枪项目目录中，正常修改后仍用原来的部署命令：

```powershell
# 1. 普通部署：构建、同步，并由工具生成成功部署收据。
RimDevTools.exe deploy

# 2. 可先检查；不会重新部署，也不会上传。
RimDevTools.exe steamworkshops upload --dry-run

# 3. 首次上传或更新统一使用这一条；根据绑定记录自动判断。
RimDevTools.exe steamworkshops upload

# 也可以一步完成普通部署、校验和上传。
RimDevTools.exe deploy --upload

# 只查看组合操作计划，不部署、不打包、不上传。
RimDevTools.exe deploy --upload --dry-run
```

其他功能：

```powershell
# 不上传模组内容，只同步当前项目 About 中的简介。
RimDevTools.exe steamworkshops edit

# 明确修改某一项时，仅修改指定字段。
RimDevTools.exe steamworkshops edit --description-file "D:\workshop-description.txt"
RimDevTools.exe steamworkshops edit --visibility private
RimDevTools.exe steamworkshops edit --preview-file "D:\preview.png"

# 简易搜索每页 25 条，--page 只支持 1 至 3。
RimDevTools.exe steamworkshops search "Harmony"
RimDevTools.exe steamworkshops search "Harmony" --page 2
RimDevTools.exe steamworkshops search "Harmony" --page 3

# 详情搜索每页 10 条，--page 只支持 1 至 5，额外显示官方描述摘要。
RimDevTools.exe steamworkshops search "Harmony" --details
RimDevTools.exe steamworkshops search "Harmony" --details --page 5

# 延续本项目最近一次关键词搜索，自动保留简易/详情模式。
RimDevTools.exe steamworkshops search "Harmony" --details
RimDevTools.exe steamworkshops search next

# 精确 ID 则查看完整简介及本机下载状态。
RimDevTools.exe steamworkshops search 2009463077

# 订阅；可选择等待下载完成。
RimDevTools.exe steamworkshops subscribe 2009463077
RimDevTools.exe steamworkshops subscribe 2009463077 --wait

# 查看当前模组：优先使用项目 About/PublishedFileId.txt。
RimDevTools.exe steamworkshops status

# 查看指定条目的信息、权限与下载状态，不订阅、不请求下载。
RimDevTools.exe steamworkshops status 2009463077
RimDevTools.exe steamworkshops status 2009463077 --wait
```

上面是讨论中的正式语法，不是要求本轮执行的命令。

#### deploy --upload：组合操作约定

沿用现有 deploy，不另建构建或发布路径。其行为等价于“普通 deploy 成功后，立即对这次部署结果执行 steamworkshops upload”，而不是跳过校验直接上传。

1. 执行普通部署、必要资源打包和成功部署收据保存。
2. 把本次 deployRunId、目标路径与收据直接交给共享上传服务，避免选到其他项目或旧部署。
3. 重新核对完整文件集合、SHA-256、项目输入及条目绑定；通过后进入同一个新建/更新流程。
4. 持有同一个环境锁，贯穿部署、收据保存、上传前复核和内容保护，避免阶段之间被其他 deploy 插入。
5. 输出一个最终 JSON，分别记录 deployment 与 upload 两阶段状态；不能通过嵌套调用两个会各自输出最终 JSON 的 CLI 入口来拼接结果。

| 情况 | 结果 |
|---|---|
| 部署失败、回读失败或收据保存失败 | 不启动上传，返回部署阶段错误 |
| 部署成功，上传前校验不通过 | 不创建/提交工坊更新；保留部署结果，明确返回校验差异 |
| 部署成功，Steam 不可用、无条目权限或上传明确失败 | 保留部署结果和有效收据；整体返回非零，明确 deploymentSucceeded=true、uploadSucceeded=false |
| 上传已提交但等待超时 | 保留本地部署与有效收据，返回非零、timedOut=true、remoteOutcome=unknown 及当前时间/远端最近更新时间；不回滚部署、不自动重试，也不留下跨调用阻塞 |
| 两阶段完成并核验成功 | 最终 ok=true，并返回本次部署 ID 与工坊条目 ID |

上传明确失败时，修复问题后可只运行 steamworkshops upload，不必再部署。超时则由调用方 AI 根据返回证据判断是否再次显式调用；新调用执行正常校验，不要求先解除上次超时状态。普通 deploy 没有 --upload 时仍只做部署，不隐式发布。

deploy --upload --dry-run 仅展示两个阶段的计划，不构建、不烘焙、不写收据、不创建或提交条目。未来构建结果尚不存在的检查标记为待部署后验证，不能根据旧目录断言新产物已通过哈希校验。

--upload 与 --xml-hot 互斥：发布链使用完整普通部署，避免旧 DLL 留在热部署目录中。上传阶段继续自动判断首次新建或更新，不要求 --create。

实现时应把组合编排放在共享服务层。现有 DeployCommand.ExecuteAsync 的 environmentPrepared 不仅影响锁，还会跳过环境等待/恢复；不能只为避免重复取锁就盲目设为 true。需要显式传递已持有的锁上下文并真正完成前置处理，避免重复加锁或跳过恢复。

### 3.4 成功部署收据：把上传与现有 deploy 连起来

建议新增由 RimDevTools 自动管理的 DeploymentReceipt，表示“这个项目、这次普通部署、这个目标目录及这些文件”的对应关系。

不能只保存一个总哈希，也不能把回滚清单当作成功部署证明。至少保存：

| 内容 | 作用 |
|---|---|
| schemaVersion、工具版本、deployRunId、部署模式与完成状态 | 判断记录有效，拒绝未完成/失败/不支持版本的记录 |
| 规范化 projectRoot、targetRoot、packageId、gameVersion | 防止读错项目、目录、包名或目标版本 |
| 实际部署配置指纹 | 包含影响构建和同步的配置；路径或同步范围变化应失效 |
| 构建/发布输入文件清单与指纹 | 判断部署后项目源码、XML、资源等是否又有变化 |
| 目标目录完整文件清单 | 每个文件记录相对路径、大小、SHA-256；普通内容与绑定身份分别分组，二者并集覆盖全部文件 |
| 文件来源标记 | 区分本次复制、构建产物、按保护规则保留的既有版本文件 |
| 工坊绑定身份 | 以规范化项目根目录和 packageId 区分身份；记录 PublishedFileId.txt 的存在性、哈希和 ID，独立处理工具合法写入，不能因临时新包名丢失原绑定 |
| 资源包状态 | 官方库/参数指纹、生成的 Assets.zip 路径与哈希、是否完成验证 |

存储固定为项目根目录下由工具自动管理的 .tools/ProjectData/deployment-receipt.json。奶牛枪对应 D:\RimworldProject3\奶牛枪\.tools\ProjectData\deployment-receipt.json，放在模组部署目录外，不能上传。它是功能必需的运行元数据，不是 stdout 副本、测试报告或日期归档；只保留当前有效部署基线，正常部署原子更新，不按日期或任务另存副本。绑定记录仍须按项目与 packageId 管理，不能因收据更新丢失原身份。

该 JSON 文本保存逐文件相对路径、大小、SHA-256、项目输入指纹、deployRunId 等 3.4 所列内容。上传时在内存中重新计算并比较，默认 CLI JSON 只返回收据路径、部署标识、校验结论及实际差异，不重复输出整份逐文件哈希清单，不另写 hash.txt、校验报告或日志副本。成功路径通过 result.artifacts、失败路径通过 error.details.artifacts 返回实际收据位置；文件缺失时如实报错，不自动拿当前目标目录补造基线。

.tools/ProjectData 属于工具运行数据目录，必须从构建/同步输入采集和部署复制范围显式排除，即使同步项覆盖项目根目录也不能复制进游戏 Mods；其内容不随发行模板或 Workshop 上传。本轮只更新计划与 .gitignore，不创建该数据目录、收据或缓存文件。

#### 统一工具数据目录

- 项目根目录下 .tools/ProjectData 是本次及以后新增工具运行元数据、分页缓存、helper 请求数据和其他暂存文件的统一位置；使用同一项目路径解析规则，不再新建分散的 .rimdev 数据目录、任务目录或日期目录。
- 本批收据使用 deployment-receipt.json，分页状态和必要结果缓存使用 steamworkshops-search-cache.json；新增工坊绑定持久数据也在该目录下由工具管理。原子写入的临时文件与其目标文件放在同一数据目录，工具负责更新和清理。
- 该目录在项目 .gitignore 中以 /.tools/ProjectData/ 整目录忽略。开发仓库、1.6/1.7 项目模板及本次奶牛枪项目均须包含此规则；发行模板只带忽略规则，不携带运行数据。
- 分页缓存可过期重建；部署收据和已知工坊绑定是功能所需基线，不能当缓存一起清空。缓存清理只能操作明确归属的缓存文件，不能递归清空 ProjectData。
- 日志、截图、测试结果和测试存档仍使用 RimDevTools 自动生成的既有原始路径，不为本约定复制/迁移测试证据。现有部署 staging 和回滚事务本轮不迁移；新增项目数据遵循本目录规则。

收据产生时机：

1. 普通 deploy 开始时，将本次操作标为进行中，旧收据不能被当作这次部署成功。
2. 采集本次实际构建/同步输入，不把 .git、日志、测试产物算成项目待发布变化。
3. 执行原有构建、暂存和文件事务提交；资源包与收据的关系见 3.10。
4. 对最终目标目录重新枚举并回读 SHA-256；构建前后输入也要复核，期间变化则不签发成功基线。
5. 原子保存新收据，完成全部步骤后才返回可上传的部署成功状态。
6. 提交失败、回读失败或收据保存失败时，保留明确失败/失效状态，不让旧记录给新目录背书。

本轮构建产生的 DLL 等输出与源码输入分开记账：不能把构建前的旧 DLL 当作必须保持不变的输入，否则正常构建就会被误判。记录源码构建前后的一致性，以及构建完成后的输出哈希；其他版本沿用的既有 DLL 则属于本轮发布输入，也要记录。

没有收据的旧部署目录，要求重新执行一次正式普通 deploy。不能直接扫描当前目录并宣布它就是可信的“上次部署”。

### 3.5 上传前的两层校验

**第一层：部署目录有没有偏离部署成功时的内容。**

重新递归枚举整个目标目录，将“当前集合”与“收据集合”双向比较：

| 差异 | 处理 |
|---|---|
| missing | 清单里的文件不存在，阻止上传 |
| modified | 大小或 SHA-256 变化，阻止上传 |
| added | 当前目录多出清单中没有的文件，阻止上传 |
| identityMismatch | packageId、路径、版本或工坊绑定不匹配，阻止上传 |
| receiptInvalid | 收据缺失、未完成、版本不支持或本次部署失败，阻止上传 |

必须重新枚举文件集合。只检查清单里已有的路径，会漏掉后来加进来的文件。SHA-256 按内容计算，不用修改时间代替；上传前还需重新检查路径安全与文件占用。

受 protected_files_regex 保护的文件也要记录真实存在性与哈希，不能整段跳过。保留旧版本目录只说明这次保留了它，不等于本次重新编译或完成了该版本的游戏测试。

**第二层：项目有没有改过却没有重新部署。**

仅比较“部署目录 vs 收据”，无法发现项目中新加了 XML、改了 C#，但游戏目录仍然完全没变的情况。因此还要重新计算项目的构建/发布输入清单，和部署时的输入指纹比较。

- 项目新增、删除或修改参与构建/同步的文件：返回 PROJECT_NOT_DEPLOYED，并列出变化，要求先 deploy。
- 同步范围、当前版本、csproj_paths、构建参数或相关导入文件变化：同样要求重新部署。
- README 或策划文档未被纳入发布/构建输入时，修改它们不应阻止上传；显式纳入同步清单的文档仍要跟踪。
- 不能只对旧输入列表算哈希，必须能发现新加入的相关文件。
- C# 输入不能只看 DLL 或 csproj 本身：应记录实际 Compile/资源项、ProjectReference、导入的 props/targets 及必要构建依赖。首版无法覆盖的外部输入明确标记，不宣称已证明最新源码都已编译。
- deploy --xml-hot 会跳过 DLL，因此不能给混合目录签发“完整构建可发布”的新收据；建议让上传基线失效，发布前执行普通 deploy。

两层全部通过才能说“待上传内容与该次成功部署一致，且已跟踪的项目输入没有产生未部署变化”。这不等于游戏功能已通过运行时测试。

upload --dry-run 返回 deployRunId、target、missing/modified/added、项目输入变化、推断动作 create/update 及是否可上传。正常 upload 内部也执行同样检查，不要求用户先单独检查一次，更不自动把不一致状态接受成新基线。

### 3.6 upload：直接上传校验通过的部署目录

最小流程：

```text
普通 deploy 成功
  → 生成部署收据
  → upload 比较目标文件与项目输入
  → 核对 Steam 账号、目标应用与条目
  → 锁定内容并在提交前复核
  → SetItemContent(部署目录)
  → 提交回调
  → 远端核验
```

输入约定：

| 参数 | 规则 |
|---|---|
| 无参数 | 从配置确定部署目录，从收据确定来源；未绑定时首次创建，已有有效绑定时更新 |
| --dry-run | 只读检查，不构建、不重新部署、不上传 |
| --project path | 只有不在项目目录运行时才需要显式指定 |
| --timeout-seconds n | 高级可选参数；超时不是取消 Steam 提交 |

首版不暴露 prepare、--prepared-dir、独立 --content-dir，也不增加忽略哈希错误的 --force。发现差异就回到 deploy 解决，避免形成多套互不相通的发布来源。

新建/更新的判断放在工具内部，不再提供 --create。建议按以下规则执行：

| 项目、部署目录及收据/绑定状态 | 动作 |
|---|---|
| 项目和部署目录均无 ID，收据身份项也记录未绑定，且没有已知创建 ID | 自动首次创建，默认 private；不因历史超时另设阻塞 |
| 项目、部署目录与收据的有效 ID 一致 | 更新同一个条目 |
| ID 文件为空、格式错误、只有一侧存在或双方不一致 | 报绑定异常，不退回新建 |
| 当前 ID 文件缺失，但已有绑定记录表明以前有 ID | 报绑定丢失，要求恢复；不能当首次创建 |
| 已取得创建 ID，但 ID 文件尚未成功保存 | 返回具体写入错误及已知 ID，保留真实绑定事实；不丢弃已知 ID 改走新建，也不设置超时阻塞 |
| 创建请求超时且未取得 ID | 当次返回超时、publishedFileId=null 与时间字段不可得的原因；不猜测目标、不自动重试、不写入阻塞标记；后续是否重试由调用方 AI 决定 |
| 已有 ID 的远端条目不可访问、不属于当前账号、已删除或查询失败 | 返回具体错误，不改为创建新条目 |

普通 deploy 不能因 ID 文件被删就清除此前已知绑定，必须区分“从未记录绑定”和“已有绑定丢失”。无任何本地绑定/创建记录时按首次处理，不通过模糊名称猜测远端 ID。当前“更新必须显式传预期 ID”的原型参数可在内部用已校验的绑定替代。

已有条目的内容上传默认保留远端标题、描述、主预览图、可见性和人工标签；只改元数据走 edit。首次发布从 About 初始化标题、描述、预览，分类默认 Mod，附加 About 声明的支持版本标签。翻译分类和自动同步已有标签不扩展到首版。

内容更新沿用 status 的账号/远端 owner 证据，但在真正提交前必须重新校验，不能仅信任之前显示的一次状态。本轮暂停贡献者专项检测；不能把非 owner 当成已获贡献者权限，也不能通过隐式新建绕过既有条目权限。

首次创建后，工具会合法新增源项目和部署目录的 About/PublishedFileId.txt。该文件在目标清单和项目输入中均作为单独身份项记录，仍校验存在性、哈希和 ID，但不按普通内容的新增去误报。只允许在 Steam 返回新 ID 后定向更新身份收据；不能借此重算整份清单，掩盖其他文件变化。写入失败返回已创建 ID，禁止自动重复创建。

提交期间持有环境锁并保护文件，防止本工具并发 deploy；提交前复核清单，提交后若发现内容变化不能报告已验证一致。不得只在很早的预检时计算一次哈希就任由目录继续变化。

首版保持同步等待，上传默认等待预算 1800 秒；不增加后台常驻服务。CLI 取消/超时会停止本次 helper，但停止本地进程不代表取消了 Steam 远端动作。本次调用不自动重试，不持久化 unconfirmed/pending 阻塞标记，不因上次超时拒绝后续显式 upload/edit/deploy --upload，也不增加 --recover 或人工解锁步骤。环境锁只覆盖实际执行和退出清理，不把超时变成持续锁定。

#### 发布超时的返回契约

upload、edit 及 deploy --upload 的发布阶段共用此契约。已取得目标 ID 时，超时后进行一次独立、有界的只读详情查询，禁用可接受的缓存响应（SetAllowCachedResponse=0），查询预算最多 10 秒；查询失败不能拖成无限等待，仍返回原超时错误并保留失败原因。当前时间在这次查询结束、生成最终返回值时采集。

| 字段 | 规则 |
|---|---|
| timedOut、remoteOutcome、operationId、stage | timedOut=true，remoteOutcome=unknown，返回本次操作标识与最后已知阶段；退出码为超时，不能伪装成上传确定失败或成功 |
| publishedFileId | 已知目标 ID 使用字符串返回；首次创建尚未得到 ID 时为 null，不按模糊名称猜测 |
| operationStartedAtUnix、operationStartedAt | 本次操作起始时间，Unix 秒及 UTC ISO 8601，便于 AI 对照 |
| submittedAtUnix、submittedAt | 已实际发出 SubmitItemUpdate 时返回本机记录的发出时间；尚未发出或无法确认时为 null，不称为 Steam 接收时间 |
| currentTimeUnix、currentTime | 返回时的本机当前时间，Unix 秒及 UTC ISO 8601；人类摘要另显示 Asia/Shanghai（+08:00） |
| lastUpdatedAtUnix、lastUpdatedAt | 目标条目最新查询的 m_rtimeUpdated，Unix 秒及 UTC ISO 8601；这是 Steam 的条目最近更新时间，不保证仅代表内容提交 |
| remoteQuery | 查询时间、来源、是否命中缓存及失败原因；离线、未知 ID 或查询失败时，最近更新时间为 null 并解释，不用当前时间或旧缓存冒充 |

上述字段放入当次超时的 error.details；组合命令同时保留 deploymentSucceeded=true 以及 upload 阶段 timedOut=true、remoteOutcome=unknown，不能用 uploadSucceeded=false 表示已确认远端提交失败。若提交前已查询到最近更新时间，可附带提交前值作为对照，但不能覆盖超时后查询值。

工具只提供证据，不根据时间接近就自动判定成功或失败；时间不能单独证明本次上传的内容完整一致。调用方 AI 可以再执行 status 复查并自行决定是否显式重试。正常的收据、文件哈希、权限和真实绑定冲突检查继续执行，它们不属于被移除的超时阻塞。已知创建 ID 应尽早定向保存到绑定记录及 ID 文件；ID 保存失败按实际写入错误报告，与超时阻塞区分。

### 3.7 edit：描述、可见性和主预览图

- 在当前项目直接运行 edit：只把源项目 About/About.xml 的 description 同步到工坊，不上传内容文件。
- --description-file：显式指定 UTF-8 描述；保留 BBCode，校验字节数和空文本语义。
- --visibility：private/public/friends-only/unlisted，省略就不动该字段。
- --preview-file：显式指定主预览图片，验证真实格式与接口大小要求。
- 提供任意字段参数时，仅改指定字段，不额外同步默认简介。
- 不在项目中编辑时，可使用 --id 指定条目；没有项目的描述更新需要明确 description-file。
- --dry-run 显示要改的字段、远端目标与 owner 预检权限；实际修改前重新校验。本轮只放行已核验 owner，非 owner 的贡献者权限不作检测、不推断为允许。
- 语言、预览图片格式和权限仍按前文已核实的 Steam 接口边界处理。
- 元数据提交超时也使用 3.6 的时间证据返回契约，不保留跨调用阻塞，不自动重试。

edit 不构建、不部署、不烘焙、不调用 SetItemContent，也不把修改远端元数据当成一次重新部署。若用户修改了本地 About 后只执行 edit，之后内容上传仍应根据项目输入指纹要求重新 deploy。

### 3.8 search 与 subscribe：搜索信息与订阅动作

search 按最新指令在原生 Steamworks 查询上增加有上限的自定义分页，描述仍使用官方模式：

- 输入文字：联网搜索 RimWorld 模组，默认按官方关键词相关度排序；不在本地重新排序。
- 简易搜索 search <关键词>：每个结果只显示名称、工坊 ID、订阅人数；每页最多 25 条，--page 仅支持 1 至 3，最多可查看相关度最高的前 75 条。
- 详情搜索 search <关键词> --details：在简易字段上额外显示 Steam 默认返回的 description 摘要，使用 SetReturnLongDescription=false（或保持官方默认），即官方截到 255 字节的模式；每页最多 10 条，--page 仅支持 1 至 5，最多可查看前 50 条。不自行追加描述截断长度、不抓网页生成摘要。
- --page 默认 1，是用户页码，不直接当作官方页码。底层 CreateQueryAllUGCRequest 仍按 kNumUGCResultsPerPage=50 查询，取到需要的官方页后按下表切片，保持官方相关度顺序；不能简单地每次都取新官方页再截取前 N 条，造成中间结果丢失。
- 简易第 4 页及以上、详情第 6 页及以上在发起联网查询前返回非零参数错误 SEARCH_PAGE_LIMIT_EXCEEDED，并说明“搜索范围过大，请缩小搜索范围或使用更准确的关键词”；不得悄悄查询更多页或自动切换模式。非正整数页码按普通参数错误处理。
- 响应同时给出 page、pageSize、maxPages、returnedCount、totalMatchingResults、hasNextPage、limitReached。totalMatchingResults 保留 Steam 的实际总数，不伪装成只有 75/50 条；hasNextPage 同时受实际结果和用户分页上限约束。到最后一个允许页且仍有更多匹配项时，limitReached=true 并给出缩小关键词的提示；范围内的无结果页正常返回空列表，不冒充页数超限。
- 本地分页只输出本次请求的一页，不将官方整页或全部可访问页一起输出给 AI；需要跨调用使用的搜索状态和已取得的官方页缓存在 .tools/ProjectData/steamworkshops-search-cache.json。缓存命中返回 source=cache、原查询时间和过期时间，不能冒充本次已联网查询；缺少所需官方页时才联网取该页，随后更新缓存。
- 原生接口没有每页条数参数；另一个 Web API QueryFiles 有 numperpage，但需 API key。本版不为自定义页容量切换接口或引入 API key；本次 25 条×3 页、10 条×5 页的决定取代此前“直接输出官方整页”的决定。
- 输入纯数字 ID 或标准工坊条目链接：沿用精确查询，调用 SetReturnLongDescription=true，返回完整描述及与 status 相同的完整状态信息；单项查询不接受分页参数。
- status 同样使用 SetReturnLongDescription=true 取得完整描述，显示完整信息；关键词列表不额外塞入作者、权限、下载状态和更新时间等完整详情字段。
- 排序、标签和语言作为后续按需开放的参数，不堆到日常命令中；不从模糊名称自动选择订阅目标，不因为搜索结果出现模组就自动订阅。

用户分页到官方查询的固定映射（条目位置从 1 开始）：

| 模式 | 用户页 | 官方页 | 取该官方页的条目 |
|---|---|---|---|
| 简易，25 条/页 | 1 | 1 | 第 1 至 25 条 |
| 简易，25 条/页 | 2 | 1 | 第 26 至 50 条 |
| 简易，25 条/页 | 3 | 2 | 第 1 至 25 条，即全局第 51 至 75 条 |
| 详情，10 条/页 | 1 至 5 | 1 | 第 (用户页−1)×10+1 至 用户页×10 条 |

官方实际返回不足所需区间时，只返回真实存在的结果；不能越过本次用户上限再补查后续内容。切片、模式上限和提示规则须有单元覆盖。

#### search next：延续最近搜索

```powershell
RimDevTools.exe steamworkshops search "Harmony"
RimDevTools.exe steamworkshops search next
RimDevTools.exe steamworkshops search next

RimDevTools.exe steamworkshops search "Harmony" --details
RimDevTools.exe steamworkshops search next
```

- next 是 search 下的翻页控制词，完整命令为 steamworkshops search next，不增加第六个工坊子命令。它读取本项目最近一次成功关键词搜索的关键词、模式和已显示页，自动请求下一用户页，不要求重复输入关键词、--details 或 --page；next 不与新的关键词、--details 或 --page 混用。
- 每次显式 search <关键词> [--details] [--page n] 都建立新搜索上下文，将当前页设为实际成功显示的页。精确 ID/链接查询及 status 不覆盖关键词搜索上下文。
- 统一缓存文件保存 schemaVersion、规范化 projectRoot、App ID、关键词、模式、pageSize、maxPages、当前页、官方总结果数、已取得官方页及各页原查询时间、createdAt/expiresAt。只保留本项目最近一个搜索上下文，成功后原子替换，不按任务或日期积累多份文件。
- 缓存默认从建立搜索时起有效 10 分钟；仅从缓存翻页不延长有效期。过期、格式错误、项目/App ID 不匹配或无有效上下文时返回明确原因，要求重新执行带关键词的搜索，不自动套用其他项目的数据。
- 简易第 1 页到第 2 页、详情各页通常可直接复用已缓存的官方第一页；简易第 3 页按需查询官方第二页。只有页面取得且缓存游标原子保存成功后才推进当前页，查询或保存失败不跳过页面。
- 达到简易第 3 页或详情第 5 页且仍有更多匹配项时，next 返回 SEARCH_PAGE_LIMIT_EXCEEDED，提示搜索不够准确、请缩小关键词，不联网查询更多页。若实际结果已耗尽，则返回 endOfResults=true、空结果和 hasNextPage=false，不重复输出上一页，游标不推进。
- 有项目上下文时才在该项目 ProjectData 中缓存。无项目上下文仍可按显式 --page 做单次搜索，但 next 返回 SEARCH_CONTEXT_NOT_FOUND 并提示进入项目或使用 --project；不在任意当前目录、游戏配置目录或全局临时目录新建分页缓存。
- 查询结果会随 Steam 数据变化；同一个缓存官方页的切片保持一致，按需再取官方第二页不承诺与第一页构成服务器静态快照。搜索缓存不能作为 status 的当前在线、权限或提交时间证据。

subscribe 支持以下写法：

| 写法 | 语义 |
|---|---|
| subscribe ID | 订阅并在需要时请求下载，返回订阅结果和当前状态 |
| subscribe ID --wait | 订阅后等待下载完成或超时 |

默认不递归订阅依赖、不修改 ModsConfig.xml、不启用或排序模组。只读查询统一使用 status；status --wait 只等待已有下载，没有可等待任务时返回当前状态及原因，不自动补订阅。

下载完成仍使用分层状态，而非“文件夹存在”或只有 Installed 标志：

```text
downloadComplete = Installed && !NeedsUpdate && !Downloading && !DownloadPending
                   && installInfoValid && installDirectoryExists
ready = Subscribed && downloadComplete
```

返回 subscribed、downloading、downloadPending、needsUpdate、downloadComplete、进度和就绪后的路径。进度不可得返回 null，不编造 0%/100%。Installed 可以与 NeedsUpdate 并存；未知、离线、flags=0 不能直接判作下载失败或条目不存在。

同一 helper 发起 DownloadItem 时要等匹配 App ID/条目 ID 的完成回调；后续另一次 status 按当前状态复核，不等待早已在前一进程发生的回调。GetItemInstallInfo 的 used 标记、LegacyItem 文件路径等边界继续按原官方 API 说明处理。

超时或取消不自动退订，也不把已成功订阅伪装成没有发生。状态查询成功可 ok=true 且 downloadComplete=false；带 --wait 未等到目标条件则返回超时及当前状态。

### 3.9 status：当前模组信息、统计与字段级权限

```powershell
# 默认当前项目；不要求项目已经重新部署，查看状态本身不触发发布。
RimDevTools.exe steamworkshops status

# 显式查看其他条目。
RimDevTools.exe steamworkshops status 2009463077
```

status 首先执行与 doctor 共享的 Steam/游戏库可用性/Workshop 探测，再显示本地定位结果、远端信息和当前用户权限。整个命令只读，不尝试上传来测试权限。

#### 当前模组 ID 的解析顺序

1. 用户显式提供 ID/链接时查询指定目标；不把当前项目 ID 强行覆盖用户指定目标。
2. 默认查询当前模组时，优先读取项目 About/PublishedFileId.txt。该文件只存数字 PublishedFileId；包名从 About/About.xml 的 packageId 读取，名称从 name 读取。
3. 同时核对已有部署收据/绑定记录及部署目录同名文件；发现冲突要输出 bindingConsistent=false 和各来源，不静默猜测正确项。
4. 项目 ID 文件缺失但已知绑定存在时，只读状态可用已核验的记录或匹配包名的部署文件辅助查询，并明确 idSource 与绑定缺失；不自动修复、不当作首次发布。
5. ID 文件格式错误直接报告绑定异常，不能用名称搜索结果替代。所有位置都未绑定时输出“未发布”，远端可见性和统计为 null/不适用，环境摘要仍照常显示。

#### 默认必须显示的字段

| 字段 | 含义与来源 |
|---|---|
| steamRunning、rimWorldInLibrary、workshopAvailability | 与 doctor 同一健康探测；带 checkedAt、来源和失败原因，不增加购买来源或许可类型字段 |
| modName、packageId | 本地 About 元数据；按远端指定目标查询时允许本地字段为空 |
| title、description、descriptionMode | 远端标题与完整描述；SetReturnLongDescription=true，descriptionMode=full，不把列表摘要当作完整描述 |
| publishedFileId、idSource、bindingConsistent | 精确 ID 及其来源，禁止把包名填到 ID 字段 |
| visibility | public/friends-only/private/unlisted，未知/未发布分别表示，不简化成只有公开/私有 |
| subscriptionCount | 当前订阅计数，优先 GetQueryUGCStatistic 的 NumSubscriptions；不要用 NumUniqueSubscriptions 或本机是否订阅代替 |
| lastUpdatedAtUnix、lastUpdatedAt | Steam 最近一次更新时间的 Unix 秒及带时区 ISO 8601 表示，取 m_rtimeUpdated |
| ownerSteamId、currentUserSteamId、userRole | 发布账号与当前用户，以及 owner/not_owner/unknown；本轮不识别 contributor |
| canUpdateContent、canEditDescription、canEditPreview、canChangeVisibility | 分项权限；未查明使用 null/unknown 和原因，不强填 true 或 false |
| permissionEvidence、contributorCheck | 记录 owner 预检来源；contributorCheck 固定为 not_performed/deferred，不冒充贡献者已检测 |
| subscribed、downloading、downloadComplete、progress | 本机下载状态，继续使用第 3.8 节的组合状态判定 |

订阅人数读取失败显示未知，不默认 0。没有绑定、访问受限、离线、部分统计不可得都要保留字段并说明原因；核心查询完成但部分字段缺失时标记 partial，不宣称取得完整状态。

#### 最近一次更新时间

历史更新次数字段及 Change Notes 网页计数流程从本轮范围删除，不再需要额外网页请求、分页计数或计数来源确认。

直接取 SteamUGCDetails_t.m_rtimeUpdated。既有原型已通过真实奶牛枪更新读到该值，Native DTO 中的 UpdatedAt 即对应来源，不需要新找历史统计 API。

- lastUpdatedAtUnix：Steam 原始 Unix 秒。
- lastUpdatedAt：从该值转换的 UTC ISO 8601 时间。
- 人类可读摘要明确显示时区；本次环境按 Asia/Shanghai（+08:00）验收，不能把 UTC 字符串不换算就当本地时间。
- 未发布、查询失败或 Steam 未给有效时间时使用 null/未知并说明原因，不能用当前时间、文件修改时间或本地部署时间补造。
- 这个值表示 Steam 记录的条目最后更新时间，不把它承诺成“仅内容文件更新的时间”；纯元数据编辑是否推进时间以 Steam 实际返回为准。

status、精确 ID/链接的 search 结果及发布超时详情包含该时间；关键词简易/详情列表遵循 3.8 的精简字段，不强制输出更新时间。订阅数与更新时间尽量随原生查询取得，不逐条抓网页。内容更新验收保留修改前后 Steam 时间值，允许 Unix 秒分辨率下同秒提交；若返回值早于已核验旧值则核查来源、缓存与时钟，不用本地时间冒充，也不据此建立跨调用超时阻塞。

#### 本轮权限检测：owner 保留，贡献者专项延期

用户已明确没有贡献者测试账号，贡献者身份识别、贡献者描述/图片专项授权及真实用例本轮均不做，不作为三批完成条件。此前“贡献者可编辑描述和图片”的目标记录保留为后续扩展方向，不在本版声称已经实现。

| 本轮已核验角色 | 内容更新 | 描述/主预览图/可见性编辑 | 状态说明 |
|---|---|---|---|
| owner | 可按本地预检规则放行，仍以 Steam 提交结果为准 | 明确参数指定的字段可放行 | 远端 ownerSteamId 等于当前 SteamID |
| not_owner | 拒绝 | 本版不放行非 owner 写入 | 仅表示不是上传账号；contributorCheck=not_performed，不声称已确认其是否为贡献者 |
| unknown | 不能放行 | 不能放行 | 身份/目标不可核验，明确原因 |

原上传账号以 Steam 远端 ownerSteamId 与当前 SteamID 比较作为可核验依据，不用 About.author 文本、包名或本地 ID 文件证明所有权。owner 的更新能力已有原型真实证据，本轮新入口仍需复测。

status 不为探测权限提交空更新，edit 不夹带 SetItemContent，upload 不因非 owner 就自动创建另一个条目。未来恢复贡献者能力时再独立核验贡献者 SteamID、Steamworks 的描述/主图实际权限及页面附加图片权限；本轮不要求用户准备第二账号，不发送贡献者邀请。

### 3.10 资源打包接入现有部署，不增加用户入口

**建议实现位置：普通 deploy 已有的暂存/提交过程。**

1. 继续按现有规则构建和复制同步项到 deploy 自己已有的内部 staging。
2. 在提交前，对该内容调用官方目录选择和烘焙库。
3. 按官方发布规则处理资产包及 Assets.zip.* 辅助文件，默认保留源资源，避免破坏旧版本兼容。
4. 用现有部署事务提交最终内容到游戏 Mods 目标。
5. 回读最终目录，资源包与其他文件一起进入成功部署收据。
6. upload 只验证和提交该部署目录，不再生成第二套内容。

这里的内部 staging 是现有 deploy 的实现细节，不是新的独立发布来源，也不需要用户执行 prepare。官方库加载、CPU/GPU 后端记录、资源一致性验收保持前文要求。

这样 Assets.zip 本身有部署哈希；后来人工替换 ZIP、添加未登记 ZIP 或改动源资源，都会被检查出来。不能为了允许自动打包而通用忽略所有 ZIP，也不能在上传时悄悄烘焙并接受目标变化。

本实施版冻结为第二批接入普通 deploy 的既有 staging。需要官方资源包却烘焙失败时，整次完整发布部署不得签发可上传收据；不转到上传阶段悄悄转换内容。第一批烘焙尚未交付时的过渡基线只能标为未完成发布验证，不用于上传。

### 3.11 三批任务总览与交接

三批按顺序执行，每批都是“实现 + 必要单元检查 + 奶牛枪真实环境验证”，不能只交代码或把假客户端当作真实验收。

| 批次 | 交付范围 | 奶牛枪真实验收重点 | 退出条件 |
|---|---|---|---|
| 第一批 | 共享 Steamworks 内核、doctor、status、search、部署收据和校验器基础 | 新 CLI 真实查询现有奶牛枪条目；普通部署真实同步 180 张测试贴图并保存基线；真实目录差异拦截 | 查询/身份/基线可靠；尚未实现的烘焙和写操作明确不可用，不能假装已完整发布 |
| 第二批 | 官方烘焙、完整部署基线、upload 自动创建/更新、deploy --upload、edit、游戏资产读取验证 | 实际生成 Assets.zip；真实更新原私有条目；改简介和换预览图再恢复；验证游戏能从包读取资源 | 独立上传与组合上传均成功，资产包与游戏读取通过，缺失/新增/修改均被拦截 |
| 第三批 | subscribe、下载状态/等待、权限与统计补齐、异常恢复、发行包与全流程回归 | 真实订阅/下载奶牛枪工坊版本并逐文件核对；同 ID 再更新；新发行包在同项目工作 | 五个命令及组合流程闭环；外部受阻项单独列明，不把未验证能力记作全部通过 |

每批交接必须在聊天中给出：修改文件、单元检查结果、实际 CLI 路径、deployRunId/operationId、条目 ID、真实步骤结果及尚未通过项。日志和产物只引用工具返回原始路径，不另建“第一批报告”等测试归档文档。

### 3.12 第一批：基础内核、诊断查询与部署收据

#### 实施清单

| 编号 | 文件/符号 | 必须实现的内容 |
|---|---|---|
| B1-01 | RimDevTools/CliApplication.cs、Program.cs、Infrastructure/CliHelp.cs | 注册五个正式子命令和共用 helper；upload --dry-run 本批完成本地基线检查，真正写入尚未实现时明确拒绝；帮助和参数契约先齐全 |
| B1-02 | 新增 Infrastructure/SteamworksClient.cs、SteamworksSessionProbe.cs；整理原 SteamworksProbe.cs | 复用已验证原生 ABI，统一初始化、账号/App ID/库可用性、回调泵、超时、释放；所有公开 ID 使用字符串 |
| B1-03 | Commands/DoctorCommand.cs | 默认常驻三项摘要及 JSON；项目发现失败也有摘要；仅查当前账号库中可用，不细分许可来源 |
| B1-04 | 新增 Commands/SteamWorkshopsCommand.cs、WorkshopQueryService、WorkshopItemInfo、WorkshopPermissionEvaluator、搜索缓存管理 | 实现 status/search；底层官方 50 条分页，上层简易 25 条×最多 3 页、详情 10 条×最多 5 页，支持 search next 和项目 ProjectData 缓存；超限提示缩小关键词；--details 使用官方摘要，status/精确查询使用官方完整描述；优先 PublishedFileId.txt，区分包名、绑定冲突和未发布；订阅计数、Steam 更新时间及 owner 证据清楚 |
| B1-05 | Commands/DeployCommand.cs、Infrastructure/DeploymentFileTransaction.cs；新增 DeploymentReceiptStore、DeploymentVerifier | 普通部署在项目 .tools/ProjectData/deployment-receipt.json 保存完整文件/输入基线，统一工具数据路径并排除该目录；覆盖新增、缺失、同长度修改和保留文件；失败/热部署不能给旧 DLL 背书 |
| B1-06 | 奶牛枪 mod_config.jsonc、README.md | sync_items 加入 1.7/Textures，使已复制测试贴图参与正式部署；保持原有同步项、packageId、工坊 ID 和模组顺序 |
| B1-07 | RimDevTools.Tests/Program.cs；新增 SteamWorkshopsTests.cs、SteamWorkshopsLiveTests.cs | 参数/状态/权限/收据的单元检查与固定路径真实驱动；禁止对其他模组做写入测试 |

B1-05/B1-06 的阶段性收据须记录资源包尚未完成验证，例如 assetPackState=required_unverified、publishReady=false。本批可以完成原始文件部署和基线测试，但不能以此放行已有测试贴图的工坊上传。第二批完成烘焙后才转为可发布状态。

本批同时确定官方库的最低加载依赖与可调用入口；更新时间直接读取已具备基础的 Steam 条目字段。只做静态加载时不把它记成烘焙已通过；不再开展历史网页计数或贡献者检测研究。

#### 精确执行命令

先按 3.0 构建新 CLI 并执行单元检查，再在 CowProject 中运行：

```powershell
& $TaskTool doctor
& $TaskTool deploy --dry-run
& $TaskTool deploy
& $TaskTool steamworkshops status
& $TaskTool steamworkshops search 3833398574
& $TaskTool steamworkshops search 'Harmony'
& $TaskTool steamworkshops search next
& $TaskTool steamworkshops search 'Harmony' --page 2
& $TaskTool steamworkshops search 'Harmony' --page 3
& $TaskTool steamworkshops search 'Harmony' --details
& $TaskTool steamworkshops search next
& $TaskTool steamworkshops search 'Harmony' --details --page 5
& $TaskTool steamworkshops status 2009463077

dotnet run --project (Join-Path $TaskCodeRoot 'RimDevTools.Tests\RimDevTools.Tests.csproj') -c Release -- --live-steamworkshops --batch 1 --tool $TaskTool
```

公开 Harmony ID 只做只读查询与非 owner 对照，绝不对它执行 edit、upload 或故障注入。

#### 真实验收与预期

1. doctor 在当前正常环境输出三项摘要，stdout 可解析为一个 JSON；离线/缺 DLL/配置错误等失败路径由可控测试覆盖，不为测试擅自退出用户 Steam。
2. status 读取奶牛枪 ID=3833398574、packageId=trigger.cowgun、private；owner 与当前 Steam 账号一致时内容更新预检允许。对非本人条目，内容更新预检拒绝或明确未确认，不能猜测为贡献者。
3. search 的简易列表仅包含名称、ID、订阅人数，每页最多 25 条、最多 3 页；--details 额外使用 Steam 的 255 字节摘要，每页最多 10 条、最多 5 页。真实查询核对简易第 2/3 页和详情第 5 页按 3.8 映射切片，仅输出请求页；实际跨 CLI 调用的 search next 保留关键词、模式并推进一页，必要缓存位于 .tools/ProjectData。受控单元检查覆盖切片不遗漏、不重复，简易第 4 页/详情第 6 页联网前拒绝，next 到上限/实际末页、缓存过期/损坏/串项目、失败不推进游标及缓存不作在线证据。精确 ID/链接查询与 status 才提供完整描述、可见性、更新时间及权限等详情。原生订阅数取得 0 时保留 0，读取失败为 null，并带原因；另覆盖实际结果不足一页、空页及最后允许页仍有更多结果的提示。
4. 普通 deploy 真实构建 CowGun.dll，目标仍为 CowDeploy；180 张测试 PNG 的相对路径、大小和 SHA-256 与项目对应，不产生 PDB/deps.json。
5. 新收据确实来自本次成功部署，包含所有目标文件及构建输入；当前需要资源包但尚未验证，所以不能上传。
6. live driver 在真实 CowDeploy 中逐项测试：新增文件、删除已登记文件、修改同长度文件；在 CowProject 中测试新增同步资源、修改构建输入但未部署。每种差异都被只读上传预检列出。
7. 故障注入仅由工具内驱动/事务执行，原值保存在内存或既有工具事务中；不得编写一次性备份/归档脚本。每例复位后重新普通部署并验证收据，不能把异常状态留给第二批。
8. 模拟失败/崩溃的测试不能产生真实 Steam 提交；本批不发布新增贴图。

退出时至少交付一次真实成功部署 ID、一份工具自动生成的有效基线，以及上述正反例结果。仅凭代码构建成功不能完成本批。

### 3.13 第二批：官方打包、安全发布、组合命令与元数据编辑

#### 实施清单

| 编号 | 文件/符号 | 必须实现的内容 |
|---|---|---|
| B2-01 | 新增 Infrastructure/OfficialAssetBakeAdapter.cs 及隔离 helper | 从本机游戏目录按需加载官方库；先用少量真实样本验证入口、依赖和后端，再处理全部资源；不用普通 ZipArchive/Compress-Archive 替代烘焙 |
| B2-02 | DeployCommand、DeploymentReceiptStore、DeploymentVerifier | 在现有 deploy staging 中选择加载目录、烘焙和过滤，再提交；最终 Assets.zip 进入收据；失败不签发 publishReady=true |
| B2-03 | WorkshopUploadCommand/发布服务、SteamworksClient | 新入口自动判定创建/更新、私有默认、owner 校验、完整内容保护、回调与远端核验；正常入口不能绕过收据 |
| B2-04 | DeployCommand.RunAsync/共享编排服务 | deploy --upload 使用同一锁上下文和本次收据；无双重 JSON；部署/上传两阶段成功、失败和未知状态分别返回 |
| B2-05 | 新增 WorkshopMetadataEditor | edit 只设置明确字段，不调用 SetItemContent；已有内容上传保留远端人工元数据；当前 owner 的描述/主图真实验证 |
| B2-06 | CowGun 项目资源、测试 Mod 入口；TestCommand、CliApplication、CliHelp | 增加受测试开关控制的游戏资源读取检查；新增仅测试使用的 test run --mode quick --verify-workshop-assets 参数，不增加工坊子命令 |
| B2-07 | SteamWorkshopsTests、SteamWorkshopsLiveTests | 覆盖正式发布失败边界、首传自动选择、metadata-only 路径、组合锁和两阶段结果，以及实际资产对照；受控测试验证超时时间证据和无跨调用阻塞，不靠制造真实未知提交反复创建条目 |

本批增加一个本地合成的短 WAV 作为真实音频输入，例如 0.2 秒、22050 Hz、单声道 PCM16，保存到：

D:\RimworldProject3\奶牛枪\1.7\Sounds\WorkshopTestAssets\CowGunTone.wav

由工具内 fixture 生成器准备，作为模组测试输入，不是日志/测试结果。同步项加入 1.7/Sounds，并在 README 标明用途。不要复制原版游戏音频或为归档建立临时脚本。

#### 烘焙必须通过的技术闸门

- 按前文官方 AllLoadFolders 和 BakeForUpload 规则处理；记录实际加载的程序集、参数及 CPU/GPU 后端。
- 最终 CowDeploy 中真实存在有效的 1.7/Assets.zip，且包内 manifest、贴图条目和 WAV 条目可验证。
- 处理全部 180 张用户贴图，不能只把四张样本打包就宣布资源测试完成；四张样本用于运行时读取对照。
- .stamps/.roles 等辅助产物按官方发布阶段过滤；源 PNG/WAV 默认保留，来源模组原文件不受影响。
- 直接库调用失败时本批受阻，不自动启动游戏 EXE 作为正常 deploy/upload 的烘焙回退，也不通过关闭烘焙假装完成全部功能。
- 同库、同参数、同输入、同编码后端的官方参考产物与 helper 产物对照资源条目；不要求整个 ZIP 哈希相同。
- 官方专用烘焙模式如用于参考，只能由明确的 RimDevTools 测试路径启动，并记录它属于验收过程；正常上传期间仍要证明没有启动游戏。不能把对照测试与生产回退混在一起。

#### 游戏读取检查的具体要求

奶牛枪测试 Mod 入口仅在 --verify-workshop-assets 对应测试开关存在时运行，在游戏主线程及加载完成后执行。实施前检查现有 Mod 入口，避免新增重复入口。

RimSage 已确认 ContentFinder<T>.Get 要求主线程，官方 AudioGrain_Clip 也用 ContentFinder<AudioClip>.Get。因此运行时检查需实际读取四张样本与短 WAV，不能只靠启动无红字推断资产可读。

检查至少包括：纹理非空、逻辑尺寸与官方参考结果一致、音频非空及采样/时长合理、资产包存在且包含对应资源。源 PNG 尺寸记录见 3.0，不自行猜测压缩后尺寸。

为排除“游戏其实读到了原始 PNG/WAV”的假通过，工具测试模式需在持有环境锁的事务中临时移出 CowDeploy 内这四张样本的源 PNG 和短 WAV，启动全新游戏进程执行检查，再在 finally 中结束游戏、恢复文件及配置。只处理明确的五个文件，不能递归删除资源目录，不能改动 CowProject 原始贴图。测试事务及原始路径由 RimDevTools 管理。

成功信号带本轮 runId，例如 WORKSHOP_ASSETS_VERIFIED，包含读取样本、结果和来自包的验证信息；不得复用上一轮日志。失败写明确 Error 并令验证命令失败。普通 quick 无错误和 zip 能解压均不能单独算通过。

运行时还须输出并核对实际加载的 trigger.cowgun 根目录等于 CowDeploy；不能在本地和 Workshop 同时有副本时，误读另一个目录的资源来通过指定部署环境验收。

该测试开关只扩展既有 quick 验证，不默认追加 auto 或扩大到无关 Def；不使用鼠标键盘、CUA 或截图识别操作游戏。

#### 精确执行命令

本批工具内 live driver 自动完成样本/输入变更及复位。下面是核心真实链路，必须使用新 Release CLI，且每个实际写入前检查返回结果：

```powershell
Set-Location -LiteralPath $CowProject
& $TaskTool doctor
& $TaskTool deploy --dry-run
& $TaskTool deploy
& $TaskTool steamworkshops upload --dry-run
& $TaskTool steamworkshops upload
& $TaskTool steamworkshops status

# 修改真实工坊简介，以及换主预览图并恢复。
& $TaskTool steamworkshops edit
& $TaskTool steamworkshops edit --preview-file (Join-Path $CowProject '1.7\Textures\WorkshopTestAssets\ShezhangOC\CEO\Icon\President.png')
& $TaskTool steamworkshops status
& $TaskTool steamworkshops edit --preview-file (Join-Path $CowProject 'About\Preview.png') --visibility private

# 游戏读取校验是单独验收过程，不属于上传时启动游戏。
& $TaskTool test run --mode quick --verify-workshop-assets

# 再验证部署并上传组合路径。
& $TaskTool deploy --upload --dry-run
& $TaskTool deploy --upload

dotnet run --project (Join-Path $TaskCodeRoot 'RimDevTools.Tests\RimDevTools.Tests.csproj') -c Release -- --live-steamworkshops --batch 2 --tool $TaskTool
```

#### 真实内容变更与验收

1. 将奶牛枪源版本由 0.1.1 更新为 0.1.2，加入清楚的测试资源说明；通过正常代码/数据修改产生真实新内容，不只重传完全相同目录。
2. 至少完成一次独立 upload 和一次 deploy --upload 的真实内容提交；两次均更新 3833398574，created=false，Steam 回调 OK，远端内容大小和本次最终清单对应。
3. 原身份更新时源目录、部署目录、收据和远端条目 ID 一致。首次自动创建必须按下文 TestIdentityScope 在同一奶牛枪路径真实执行，不再仅用单元覆盖或历史证据替代。
4. metadata-only 修改后远端描述/预览确实改变，内容句柄/内容清单保持不变。随后上传已有内容不能悄悄覆盖人工简介、主图或可见性。
5. 可见性须在新建测试条目真实完成 private → public → friends-only → unlisted → private，并逐步回读核验；最终本地主身份恢复，原条目和测试条目均回到 private，预览图恢复到项目 About/Preview.png。远端条目不自动删除，由用户自行清理。
6. 真实路径的新增、缺失、修改、未部署源码和资产包损坏均阻止提交；要证明没有调用 SubmitItemUpdate，而不是只检查一句报错文本。
7. 部署失败不上传，上传失败不回滚部署，收据失败不上传；dry-run 无构建/打包/提交，--xml-hot --upload 被拒绝，输出始终一个 JSON。发布超时的受控测试须核对 timedOut、当前时间、目标 ID、Steam 最近更新时间或明确不可得原因；当前调用不自动重试，结束后释放锁，后续显式调用不因超时历史被拦截。
8. 内容上传的真实记录显示 RimWorld 在提交前后均未运行；游戏读取用例另有工具 runId，不能用它否认上传的无游戏要求。
9. 本批正常结束后，再次普通部署/校验通过，CowDeploy 可用、收据有效，临时移出的五个文件恢复，无遗留故障注入。

本批不能在官方库烘焙失败、包读取失败或只能从原始 PNG 读取时标完成。

#### 同一项目中的首次上传和可见性真实测试

用户已授权临时改包名/发布绑定并保留远端测试内容。本流程由 live driver 的 TestIdentityScope 工具事务执行，不是要求本轮立即修改或发布。

1. 确认当前主身份 packageId=trigger.cowgun、ID=3833398574，目标仍为 CowDeploy；把源 About、源/部署 ID 文件及必要绑定状态纳入可恢复事务。
2. 在原 CowProject 内临时使用有效新 packageId，例如 trigger.cowgun.firstuploadtest.<runSuffix>，显示名明确带“奶牛枪首次上传测试”。保持 mod_name=CowGun，部署路径不变，不新建其他测试项目/任务目录。临时身份及其后返回的新 ID 必须登记到本次 TestIdentityScope 的精确允许列表，不能给任意同前缀身份放开写权限。
3. 仅对这个新身份清除/移出 PublishedFileId.txt，并生成该身份未绑定的正常部署收据。绑定按项目根目录与 packageId 区分，不能抹掉原身份 3833398574 的记录。
4. 执行正常 deploy，再执行不带创建参数的 steamworkshops upload。必须由正式规则识别首次发布，实际调用 CreateItem/SubmitItemUpdate，保存新 ID 并完成远端核验。
5. 对新测试条目再次执行 upload，确认不再创建第二个条目且 ID 不变；这是自动首次/更新切换的真实用例。
6. 保持临时身份上下文，对其逐次执行 edit --visibility public、friends-only、unlisted、private，每次用 status 回读实际值为 0、1、3、2，并确认条目 ID 与内容对应关系没有变化。
7. 如提交被 Steam 拒绝、审核/协议流程导致状态未生效或回读不一致，记录真实失败，不以 setter 返回 true 代替切换成功。
8. finally 恢复源项目原 About 与 ID、部署原 ID 和原身份上下文，再普通 deploy，确认主绑定重新为 3833398574、收据有效、目标仍为 CowDeploy。原项目的 min_active_mods 等非必要字段不要改动；确需改动的字段也必须纳入恢复。
9. 新测试条目最后设为 private，保留其远端内容；新 ID、链接与每次真实状态在 CLI 返回值及聊天交接中列明，由用户测试后自行删除。工具不调用 DeleteItem。

仅删除原身份的 ID 而包名/既有绑定不变，仍应命中“绑定丢失”保护，不能作为成功首次发布用例。TestIdentityScope 崩溃后必须先恢复或继续同一个已记录测试身份，不能每失败一次就换后缀继续创建远端条目；这是恢复本地测试事务，不是发布超时阻塞。若已发出创建但结果未知，driver 停止本轮后续写入并返回 3.6 的超时/时间证据，不自动重试、不保存 unconfirmed 阻塞；之后是否再次显式提交由调用方 AI 判断。已取得的新 ID 必须返回并保留绑定事实，不因恢复测试原身份而丢失。

### 3.14 第三批：订阅下载、权限统计与发行包闭环

#### 实施清单

| 编号 | 文件/符号 | 必须实现的内容 |
|---|---|---|
| B3-01 | 新增 WorkshopSubscriptionService，扩展 SteamworksClient/命令入口 | SubscribeItem 确认、必要 DownloadItem、--wait、跨调用状态复核、无进度/排队/旧版 Installed 的正确处理 |
| B3-02 | WorkshopQueryService、WorkshopPermissionEvaluator | 完成订阅人数、Steam 最近更新时间及 owner/非 owner 检测；不开发历史网页计数或贡献者专项 |
| B3-03 | 发布结果和环境锁服务、DeployCommand | 完成超时、helper 退出与 ID 保存失败的结果契约；超时返回当前时间及目标 Steam 最近更新时间，退出释放锁，不保留跨调用阻塞，不自动重试；已知 ID 写入错误与真实绑定恢复独立处理 |
| B3-04 | SteamWorkshopsLiveTests | 使用奶牛枪同一真实项目、部署目录与条目完成订阅、更新、下载后哈希、状态和最终回归 |
| B3-05 | .tools/publish-rimdevtools.ps1、README、模板 README/技能/命令参考、各项目 .gitignore、ReleaseTests/Program.cs | 发行新入口和 search next 帮助，模板包含 /.tools/ProjectData/ 忽略规则；包内自带 helper，但不带官方库副本、贴图测试项目、ProjectData 运行数据、收据、日志或测试历史 |

#### 真实订阅与下载用例

```powershell
Set-Location -LiteralPath $CowProject
& $TaskTool steamworkshops status
& $TaskTool steamworkshops subscribe 3833398574
& $TaskTool steamworkshops status 3833398574
& $TaskTool steamworkshops subscribe 3833398574 --wait
& $TaskTool steamworkshops status 3833398574 --wait

dotnet run --project (Join-Path $TaskCodeRoot 'RimDevTools.Tests\RimDevTools.Tests.csproj') -c Release -- --live-steamworkshops --batch 3 --tool $TaskTool
```

必须在真实 Steam 客户端中订阅/请求下载，不能用 fake mode 的 ready 当成验收。

1. 记录测试前是否已订阅、是否已安装；已订阅则测幂等，不为了制造首次订阅擅自退订。
2. 状态查询无任何订阅/下载副作用；--wait 要能等到目标 ID 的下载完成并核对安装目录。
3. 安装目录由 GetItemInstallInfo 返回。预期通常在 H:\SteamLibrary\steamapps\workshop\content\294100\3833398574，但代码不能凭这个预期拼出成功。
4. 下载完成后按本次已提交的内容清单逐文件比 SHA-256，验证 DLL、XML、全部测试贴图、WAV 和 Assets.zip，不只比较总大小或目录存在。
5. 再把奶牛枪更新为 0.1.3，或修改短 WAV/另一项明确测试输入，经 deploy --upload 产生新版本，然后验证已订阅副本能更新至新清单。
6. downloading/pending 可能因下载很快而短暂出现；live driver 记录实际观察到的状态，不编造未观察到的中间态。组合 flags 的判断另有单元覆盖。
7. helper 退出后后续 status 仍能复核下载进度/完成状态。超时不退订、不改 ModsConfig、不把旧 Installed 标志当最新版完成。
8. 同时存在本地 CowGun 与 Workshop 副本时，current status 仍优先源项目的 PublishedFileId；不能按名称随机选副本。
9. 测试后新增的订阅状态保留并在交接说明；本功能没有自动退订清理步骤。

#### owner 权限与最近更新时间的真实验收

- 当前 owner：真实核对 ownerSteamId，并通过第二批内容/元数据提交验证。
- 普通非 owner：只读查询公开模组及权限判断；禁止为测拒绝而提交修改其他作者条目。
- contributor：用户已决定本轮不检测、不测试，不需要第二个账号，不请求邀请，不作为未完成阻塞项；输出明确 not_performed，不能冒充已识别。
- status、精确 ID/链接 search 及发布超时返回的最近更新时间必须来自 m_rtimeUpdated，原始 Unix 秒与 UTC/本地带时区显示一致；关键词列表按 3.8 精简输出。当前私有奶牛枪也通过已登录原生查询读取，不抓历史网页。
- 对本批真实更新前后各读一次 Steam 时间并与查询结果对应，验证没有误用创建时间、查询时刻或本地文件修改时间；同秒提交不因时间相等误报。
- 未发布或查询失败的时间为 null/未知并说明原因。历史计数、Change Notes 抓取和贡献者专项均从这三批的验收范围移除。

#### 发行包真实验收

在完成源码检查和上述真实流程后，生成 1.7 模板包，并用发行包里的新 CLI 在同一 CowProject 再做 doctor/status、部署预览和一次组合上传回归：

```powershell
Set-Location -LiteralPath $TaskCodeRoot
& (Join-Path $TaskCodeRoot '.tools\publish-rimdevtools.ps1') -TemplateGameVersion 1.7 -CreateZip

$TaskToolVersion = (& $TaskTool --version | ConvertFrom-Json).rimDevToolsVersion
$TaskRelease = Join-Path $TaskCodeRoot ('artifacts\RimDevTools-{0}-Template-RimWorld-1.7' -f $TaskToolVersion)
dotnet run --project (Join-Path $TaskCodeRoot 'RimDevTools.ReleaseTests\RimDevTools.ReleaseTests.csproj') -c Release -- $TaskRelease

$TaskReleaseTool = Join-Path $TaskRelease '.tools\RimDevTools.exe'
Set-Location -LiteralPath $CowProject
& $TaskReleaseTool doctor
& $TaskReleaseTool steamworkshops status
& $TaskReleaseTool deploy --upload --dry-run
& $TaskReleaseTool deploy --upload
```

正式打包脚本和版本输出决定实际包路径；若版本/命名规则变更，使用工具的真实返回位置，不创建替代目录。无需覆盖奶牛枪旧 .tools/log 或历史产物来完成此验收。

只构建/验证当前 1.7 源码目标，其他版本沿用已有产物；不能因包中包含 1.6 DLL 就宣称本次已真实验证 1.6。

### 3.15 全部完成的判定

以下必须分别记录为“实现通过”“真实通过”“未测/受阻”，不能只用一个总勾选隐藏差异：

| 验收项 | 最低要求 |
|---|---|
| doctor 与五个命令 | 新源码 CLI 和最终发行 CLI 均可用；帮助、参数、输出契约一致 |
| 搜索分页与描述 | 简易 25 条/页、最多 3 页；详情 10 条/页、最多 5 页；search next 跨调用延续本项目最近搜索；映射官方查询后仅输出请求页，超限先拒绝并提示缩小关键词；详情列表为官方摘要，status/精确查询为官方完整描述 |
| 工具数据目录 | 收据、搜索缓存和新增暂存数据统一在 .tools/ProjectData；.gitignore 忽略整个目录，部署/发行排除其内容，缓存清理不删除收据与绑定 |
| 当前模组定位 | 指向 3833398574，包名与 ID 分开，路径冲突/缺失正确处理 |
| 部署与完整性 | 真实 CowProject → CowDeploy，成功收据、原始/新增/缺失/修改检测及失败复位 |
| 官方资源包 | 全部测试贴图及 WAV 经官方库处理；有效 manifest，样本游戏包内读取通过 |
| 内容发布 | 原条目真实更新；独立 upload 与 deploy --upload 均通过，实际提交期间未启动游戏 |
| 首次发布 | 同一奶牛枪路径临时新身份，正式 upload 自动创建并保存新 ID，再次 upload 只更新；结束恢复原包名与 3833398574 |
| 可见性 | 新测试条目 private → public → friends-only → unlisted → private 全部真实提交并回读；远端测试条目留给用户清理 |
| 元数据 | 真实描述更新、不同预览图片替换与恢复；内容没有被 metadata-only 命令重传 |
| 订阅与下载 | 真正使用 Steam 回调与状态，最新工坊下载副本逐文件对应提交清单 |
| 权限与统计 | owner 实测、非 owner 判断、订阅人数和 Steam 最近更新时间正确；贡献者专项延期，不要求第二账号，不提供历史更新计数 |
| 超时、恢复与发行 | 发布超时返回当前时间及目标 Steam 最近更新时间/不可得原因，本次不自动重试，后续显式调用无超时阻塞；测试事务恢复；新包不带测试历史或官方库副本 |

本轮仅落地了用户要求的贴图输入和 README 说明，以及本计划本身。三批实现、真实部署、游戏验证、订阅和后续上传均尚未开始。

## 4. 风险与替代方案

| 风险 | 处理 |
|---|---|
| 把“哈希一致”误当作“已包含最新源码” | 同时核对目标目录基线与项目输入指纹 |
| 只检查已登记路径，漏掉新增文件 | 每次重新枚举双方集合，比较 added/missing/modified |
| 复用临时事务记录，成功后找不到基线 | 单独保存必要的成功部署收据，工具自动管理 |
| 热部署留下旧 DLL | 热部署不能签发完整发布基线，上传前要求普通 deploy |
| 合法生成 ID 或资源包破坏基线 | ID 独立身份记账；资产包纳入正式部署最终产物，不通用忽略 |
| 保护目录含旧文件 | 完整哈希并标记保留来源，不宣称所有版本均重新构建/测试 |
| 校验后内容继续变化 | 环境锁、文件保护、提交前复核；失败不自动放行 |
| 烘焙增加普通部署耗时/依赖 | 第二批固定接入 deploy 既有 staging；失败不能伪装已生成官方资产包或放行发布 |
| 订阅与状态模式混淆 | 独立 status 明确只读，参数组合验证，状态字段分层 |
| 把库中可用误扩展为过度授权核验 | doctor 只查当前账号库中是否可用，不细分购买/共享/试用来源；在线和条目权限仍独立 |
| 部署上传组合掩盖部分失败或重复加锁 | 共享服务与同一锁上下文，两阶段状态独立，一次最终 JSON；只在部署和校验成功后上传 |
| 时间来源或时区错误 | 直接读取 Steam m_rtimeUpdated，保留 Unix 秒，明确 UTC/本地时区，不用本地文件时间代替 |
| 贡献者检测已延期 | 本轮仅核验 owner，不猜测非 owner 的贡献者身份，不要求第二账号，也不按此阻塞发布 |
| 首次发布测试破坏原绑定 | TestIdentityScope 对临时新 packageId 定向处理绑定，事务恢复并重新普通部署；不能抹掉原身份记录 |
| 可见性切换测试留下不明状态 | 在新测试条目逐项核验，最终 private，列出所有新 ID/链接；不调用远端删除 |
| 发布超时 | 当次返回超时、当前时间、目标 Steam 最近更新时间及来源/失败原因，AI 自行判断；不自动重试，不保存跨调用阻塞或要求 --recover |

直接从部署目录上传能更好地连接现有工作流，但前提是部署收据可靠。首次无收据、项目有未部署变化或目录被手工修改，都应回到普通 deploy，不新增“相信当前目录”的快捷入口。

官方库和资源兼容性风险仍按第 2 节处理；未实测的直接库调用不能因为命令变少就被视为已解决。

## 5. 实施难度

整体难度：★★★★☆（4/5）。

- 五个命令、doctor 常驻输出及复用现有上传：★★☆☆☆（2/5）。
- 成功部署收据、双向内容校验及项目输入新鲜度：★★★☆☆（3/5）。
- 搜索、只改元数据、订阅与可靠下载状态：★★★☆☆（3/5）。
- 最近更新时间与 owner 预检：★★☆☆☆（2/5），复用已有原生字段；贡献者检测和历史次数已移出本轮。
- 官方烘焙、完整多版本内容及异常恢复：★★★★☆（4/5）。

命令数量可以减少，内部仍需保留完整校验，不把省略用户操作步骤等同于省略一致性判断。

## 6. 冻结决定、外部条件与阻塞规则

不再把常规实现选择留给执行者重新设计。按以下决定落地，用户后续改变需求时再更新本计划。

| 项目 | 本实施版决定 |
|---|---|
| 环境与测试资产 | 固定使用 3.0 的 CowProject/CowDeploy 和现有 3833398574；180 张用户贴图已准备，第一批补同步项 |
| 命令 | 五个 steamworkshops 子命令和 deploy --upload；不恢复 prepare/probe/info 或 subscribe --status，不要求 --create |
| 搜索分页 | 底层原生 CreateQueryAllUGCRequest 每页最多 50 条；用户 --page 为自定义页，简易 25 条×最多 3 页、详情 10 条×最多 5 页；search next 延续最近搜索，超限提示搜索不够准确，不引入 Web API key |
| 工具数据目录 | .tools/ProjectData 统一存放本次和后续新增运行元数据、缓存与暂存文件；加入 .gitignore，缓存默认 10 分钟，测试原始产物不迁移 |
| 哈希收据 | 固定存放项目根目录 .tools/ProjectData/deployment-receipt.json，工具自动原子维护当前部署基线；不部署、不上传、不另写校验报告，实际路径随 artifacts 返回 |
| 描述模式 | 关键词 --details 使用官方默认 255 字节摘要；status 与精确 ID/链接查询使用 SetReturnLongDescription=true 获取完整描述 |
| 发布来源 | 已部署目录；目录双向哈希与项目输入新鲜度均通过后才提交 |
| 游戏库条件 | 当前账号库中可用即可，不细分购买/共享/试用来源 |
| 首次与更新 | 正常流程自动判断；首次真实测试使用同路径临时新 packageId，定向移出其发布文件，完成后恢复原包名与 ID |
| 已有条目元数据 | 内容上传默认保留远端标题、描述、主图、可见性和人工标签；显式 edit 才改字段 |
| 官方资源包 | 第二批接入 deploy 的既有 staging，官方库按需从游戏安装读取，默认保留源资源 |
| 游戏进程 | 正常 deploy/upload 的烘焙与提交不启动游戏；游戏读取/官方参考只在明确的 RimDevTools 验收流程中启动 |
| 下载 | 第三批真正订阅/下载奶牛枪；status 只读，--wait 等待就绪，不自动退订或启用模组 |
| 更新时间 | 取消历史更新次数；status/精确查询和发布超时返回 Steam 最近一次更新时间，含 Unix 秒及带时区表示；关键词列表按简易/详情字段输出 |
| 权限范围 | owner/非 owner 预检继续做；贡献者专项身份和编辑能力不检测、不测试、不要求第二账号 |
| 可见性真实测试 | 用户已授权所有可见性真实切换；在新测试条目执行，最终 private，不删除远端条目，由用户自行清理 |
| 超时 | 首版同步有界等待，不增加常驻服务；返回超时、当前时间和目标 Steam 最近更新时间供 AI 判断；停止本地 helper 不代表远端取消，不自动重试、不设置跨调用阻塞、不增加 --recover |
| 测试产物 | 仅工具自动原始路径，不复制日志/截图/存档，不建任务/日期归档；每批在聊天交接 |

以下真实验收不能靠填默认值或旧实验记录跳过；首次上传和可见性测试已获用户明确允许，不再等待重复确认：

| 闸门 | 需要什么 | 未满足时 |
|---|---|---|
| G1：直接官方库烘焙 | 第二批在真实贴图/WAV 上加载、生成资产包并通过游戏读取 | 第二批资源功能受阻，不能改用普通 ZIP 或未说明的游戏 EXE 回退后标成功 |
| G2：Steam 更新时间 | status/精确查询读取并正确转换 m_rtimeUpdated；真实更新前后核对返回值；超时按 3.6 返回时间证据 | 不用本地时间填充；正常条目取不到有效值须排查，超时查询失败明确返回 null 与原因，不用历史次数替代或建立超时阻塞 |
| G3：新入口首次发布 | 在同一奶牛枪项目/部署路径内用临时新身份，实际创建、上传、绑定并再次更新 | 必测；只有单元覆盖或旧原型记录不足以完成第二批 |
| G4：可见性真实切换 | 新测试条目 public、friends-only、unlisted、private 全部真实提交及回读，最后 private | 必测；状态未生效则报告失败，不以 setter 成功代替 |
| G5：原身份恢复与远端保留 | 本地恢复 trigger.cowgun/3833398574 并重新部署，列出测试新条目 ID 与链接 | 恢复失败则批次失败；不删除任何已上传远端条目，交用户自行清理 |

三批任务的“全部完成”必须满足 3.15，且每个闸门的已通过/未测/受阻状态明确。若某个 Steam 端能力经核实不提供，需向用户报告并调整承诺，不能把目标策略、字段占位或旧实验记录当作新的完整验收。

本轮明确延期项：贡献者专项检测/编辑测试；明确删除项：历史更新次数及网页历史计数。它们不再要求账号或额外来源，也不作为本轮未完成阻塞项。

本轮已完成：复制用户授权的 180 张测试 PNG 到奶牛枪测试资源路径、SHA-256 逐文件比较、README 来源说明和这份三批实施计划。未修改 CLI 实现、奶牛枪配置或 C#，未把新增资源部署/上传，未启动游戏。


用户此前补充（保留原文；30/10 条页容量已由后续最新澄清取代）：
搜索内容可能太多了，所以支持简易搜索和详情搜索，简易搜索只显示搜到的模组的名称，工坊id，订阅数量，最多一次显示相关性最强的30条，支持分页，可以通过控制分页查询第二页第三页等。（我记得steam本身的搜索分页就是30条，不知道能不能走官方分页查询逻辑能走就最好） 然后是 详情搜索， 额外显示模组描述（这个描述steam也会在分页里面给出，要是能走steam的路子就最好，免得还要自己省略）,但是每页只有10条。

而steamworkshops status 会显示完整的信息。

实施前第一轮澄清（超时约定继续有效；直接输出官方整页的约定已由下方最新决定取代）：

> 优先遵循官方的分页方式和说明，官方接口支持30条分页嘛？详情显示也使用官方支持的模式。 发布后超时不留下阻塞逻辑，而是返回值告知上传超时，以及当前时间和目标模组的最新提交时间，让AI自己判断提交成功没有。

接口事实：原生 ISteamUGC 每页最多 50 条且没有自定义页容量参数；Web API QueryFiles 的 numperpage 属于另一个需要 API key 的接口，本版不切换。关键词详情用官方默认摘要，status/精确查询请求官方完整描述。发布超时的时间字段与非阻塞规则见 3.6，不新增 --recover；Steam 只能提供条目最近更新时间 m_rtimeUpdated，不将其改称为能单独证明本次内容提交成功的时间。

实施前最新决定（自定义分页取代直接输出官方整页）：

> 这样我们自己做个自定义分页吧，就是简略搜索以25个为一页，最多支持3页，详细搜索以10个为一页，最多支持5页。更多的页数就不支持了，说明搜索不够准确。 还有我们之前不是说了部署后上传创意工坊需要做哈希值校验嘛？那个哈希值校验的文本你会放到哪？

落实口径：简易 25 条/页、最多 3 页，详情 10 条/页、最多 5 页；底层官方 50 条页按 3.8 的固定映射切片，只输出请求页，超限提示缩小关键词。哈希基线的最终目录已由下方统一数据目录决定调整；上传时内存复算，校验结论与实际差异通过 CLI JSON 返回，不另写报告，细则见 3.4。

实施前最新目录与翻页决定：

> 改成到.tools/ProjectData 下面 分页的数据暂存也可以放到这个路径，同时这个路径还要加入gitignore  以后如果还有新文件暂存数据都放进这个里面。分页支持next 命令快速查看下一个分页。

落实口径：奶牛枪哈希收据改为 D:\RimworldProject3\奶牛枪\.tools\ProjectData\deployment-receipt.json，分页缓存为同目录 steamworkshops-search-cache.json，本次和以后新增的工具运行/暂存数据统一使用 .tools/ProjectData。当前仓库、1.6/1.7 项目模板和奶牛枪的 .gitignore 已加入 /.tools/ProjectData/。新增 steamworkshops search next 的计划规则见 3.8，仍受简易最多 3 页、详情最多 5 页限制。本轮未创建 ProjectData 或实现 CLI。
