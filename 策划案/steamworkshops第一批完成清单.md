# steamworkshops 第一批完成清单

本清单按用户本次实施要求编写并随第一批代码提交。验收日期：2026-11-04；后续第二、三批仍须分别真实验收、提交，本文不代表全部计划完成。

## 实现及验收

| 计划项 | 实现 | 验证与结论 |
|---|---|---|
| B1-01 正式入口、参数、帮助 | 完成 | 五个子命令已注册；status/search/上传预检可用；尚未交付的写操作明确拒绝。旧上传入口及其 helper 停用，不能绕过收据。 |
| B1-02 共享原生内核 | 完成 | 使用真实 steam_api64.dll，隔离 helper 初始化 App ID 294100、账号、库状态和回调；查询禁用缓存响应、释放句柄，公开 ID 为字符串。 |
| B1-03 doctor | 完成 | 奶牛枪真实输出 Steam 已启动、RimWorld 库中可用、Workshop 在线；摘要在 stderr，stdout 为单个 JSON。项目发现失败仍有三项摘要，异常状态单元覆盖。 |
| B1-04 status/search/cache | 完成 | 主条目 3833398574 为 private，当前账号与远端 owner 均为 76561198825362752；公开 Harmony 2009463077 为 not_owner，仅查询未写入。真实查询到订阅数 0 和 Steam 更新时间 1793767916（2026-11-04T04:51:56Z / 12:51:56+08:00）。 |
| B1-04 分页 | 完成 | 实际简略页 1/2/3 各 25 条，详情页 5 为 10 条；核对官方页内位置 26–50、官方第二页 1–25、官方第一页 41–50。跨 CLI 的 next 正确保留模式、命中缓存；超过 3/5 页拒绝。 |
| B1-05 成功部署收据 | 完成 | 完整目标文件集合、大小、SHA-256、配置指纹、构建/同步输入、构建输出及保留文件来源均记录；通过 MSBuild 评估实际 Compile/资源/引用和展开的导入图，跟踪新增源码及 props 变化。失败和热部署不签发完整基线。 |
| B1-05 两层校验 | 真实通过 | 真实目标新增、缺失、同长度修改；真实源项目新增同步资源、未部署源码修改，五种情况均列出具体差异并拒绝。每例在 finally 恢复原字节，再正式部署。 |
| B1-06 奶牛枪资源 | 真实通过 | 同步项加入 1.7/Textures，180 张 PNG 全部部署并逐个 SHA-256 匹配；最终目标 188 个文件，无 PDB/deps.json。原 packageId 和发布 ID 保留。 |
| B1-07 测试驱动 | 完成 | 固定项目、游戏、部署路径和身份白名单；拒绝 fake mode/注入探针。单元与原有回归共 395 项通过。真实驱动 --live-steamworkshops --batch 1 通过。 |
| 官方库第一批技术检查 | 真实通过（仅加载） | 从游戏安装隔离加载 UnityStubs、Waylith.AssetBake、Waylith，返回实际版本和 SHA-256；确认 BakeForUpload 与 ContentPackBaker.Bake 签名。未执行烘焙，不宣称资源包通过。 |

## 实际命令与最终状态

- 验收 CLI：`D:\RimworldProject\环世界日志同步mod\RimDevTools\bin\Release\net10.0\RimDevTools.exe`，版本 1.1.0.0；Release 构建 0 警告、0 错误。
- 全量检查：`dotnet run --project RimDevTools.Tests/RimDevTools.Tests.csproj -c Release`。
- 真实驱动：`dotnet run --project RimDevTools.Tests/RimDevTools.Tests.csproj -c Release -- --live-steamworkshops --batch 1 --tool <上述CLI>`。
- 真实项目：`D:\RimworldProject3\奶牛枪`；部署：`H:\SteamLibrary\steamapps\common\RimWorld\Mods\CowGun`。
- 真实驱动最后部署 ID：`31f641cd30664a33872eb894e4a78181`。随后对补齐的导入图跟踪再正式部署并复核，最终 deployRunId：`78aaac5606b34a008ec1c406d78b1cca`。
- 工具返回的收据原始路径：`D:\RimworldProject3\奶牛枪\.tools\ProjectData\deployment-receipt.json`。
- 搜索状态原始路径：`D:\RimworldProject3\奶牛枪\.tools\ProjectData\steamworkshops-search-cache.json`。缓存清理不处理收据或绑定。
- 原始文件基线有效，`publishReady=false`、`assetPackState=required_unverified`；只读上传预检返回 `ASSET_PACK_UNVERIFIED`，差异为空。
- 本批未创建或提交任何远端内容，工坊主条目仍为 private/0.1.1；尚无临时首次发布条目。
- 没有创建额外日志、截图、存档或 stdout 归档。只有用户明确要求的本完成清单随代码保存。

## 变更范围与交接

主仓库包含命令/帮助/原生会话、分页缓存、ProjectData 路径、部署收据与校验器、doctor、单元/真实测试驱动及 Git 忽略规则。原有未提交的 Steamworks 原型经检查纳入衔接，旧写入口停用。

外部奶牛枪项目的 `mod_config.jsonc`、`README.md` 和 `.gitignore` 已修改；该目录当前不是 Git 仓库，其文件不混入开发仓库提交。实际部署收据、缓存及绑定保存在被忽略的 ProjectData，未提交运行数据。

第二批必须完成：官方库真实烘焙全部贴图及合成 WAV、官方参考对照与游戏包内读取、安全内容上传与组合命令、描述/预览编辑、临时身份首次上传和四种可见性回读。第三批必须完成：真实订阅下载、下载副本逐文件核验、发行包及最终回归。贡献者检测和历史更新次数按计划延期/删除。
