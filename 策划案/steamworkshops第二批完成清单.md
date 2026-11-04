# steamworkshops 第二批完成清单

验收日期：2026-11-04。第二批实现、单元检查及奶牛枪真实验收完成；本清单随第二批提交。第三批订阅/下载、发行及最终回归仍待完成。

## 逐项验收

| 计划项 | 实现与真实结果 |
|---|---|
| B2-01 官方库独立烘焙 | 从本机游戏加载官方程序集；使用官方 AllLoadFolders、Request 和 BakeForUpload。先通过四张真实样本和合成 WAV，再处理全部 180 张 PNG 与 WAV。未用普通 ZIP 压缩替代烘焙。 |
| 1.6 跳过 | 按用户实施中补充，仅 1.7 调用烘焙；1.6 在目录/库探测前直接跳过，不依赖现代资源库。该分支有单元覆盖。未宣称本批对真实 1.6 游戏做过完整实测。 |
| B2-02 部署与完整基线 | 烘焙在原 deploy staging、正式事务提交前执行。目标生成 1.7/Assets.zip，源 PNG/WAV 保留；官方 IsPackSidecar 过滤辅助文件。收据包含资源包、官方库指纹、设置、CPU 后端及解码后条目哈希。 |
| 官方一致性 | 明确的测试路径启动真实 RimWorld.exe -Headless -BakeModAssetPacks，使用独立 staging 生成官方参考。180 张 PNG、1 个 WAV、manifest 和纹理属性共 183 个解码后条目逐一 SHA-256 相同，设置和 CPU 后端相同；比较不依赖整个 ZIP 容器哈希。 |
| 当前安装版本适配 | 本机官方 Request 支持新增选项，纹理采用 .dds.split。使用实际 Request 默认参数和官方纹理查找接口，不用旧源码的 BakeSettings 签名或只识别 .dds 的覆盖判断。 |
| B2-03 安全内容上传 | 完整目录及项目输入校验、owner/应用核验、提交期间文件保护、提交前后再核对。更新 3833398574 成功，CreateItem 未调用，SubmitItemUpdate 回调为 1；远端大小为 60,591,424 字节，与本次目录一致。 |
| B2-04 deploy --upload | 同一个实际环境锁贯穿部署、回读收据和上传；传递刚完成的 deployRunId。保持正常环境等待/恢复，输出单个 JSON。组合真实提交通过。dry-run 只显示两个阶段，--xml-hot --upload 在变更前拒绝。 |
| B2-05 元数据编辑 | 真实同步 About 描述、换为 President.png、修改独立 BBCode 描述；SetItemContentCalled=false，远端内容大小保持不变。之后组合内容上传保留手工描述、预览、private 和标签；最后描述恢复为项目 0.1.2，预览恢复项目 Preview.png。 |
| B2-06 游戏读取 | 只在显式 --verify-workshop-assets 时运行测试 Mod 入口。先移出目标四张 PNG 和 WAV，再启动全新游戏进程。主线程实际读到四个纹理及音频，根目录严格为 CowDeploy，信号含本轮 runId。最终 0 错误、0 警告，源文件和配置恢复。 |
| B2-07 故障/超时 | 真实资产包损坏、目标新增/缺失/同长度修改、未部署源码均阻止提交，返回 submitCallCount=0。受控测试覆盖部署失败和收据保存失败不上传、超时非零/unknown、时间来源、最多一次详情查询、未知 ID/离线原因、锁释放及下一次显式上传不受历史超时阻塞。 |

## 真实操作标识

- CLI：`D:\RimworldProject\环世界日志同步mod\RimDevTools\bin\Release\net10.0\RimDevTools.exe`，1.1.0.0；Release 构建通过。
- 全量单元及原有回归：419 项通过。真实驱动：`dotnet run --project RimDevTools.Tests/RimDevTools.Tests.csproj -c Release -- --live-steamworkshops --batch 2 --tool <上述CLI>`，返回 ok=true、partialBatch=false。
- 独立内容上传：operationId `f7e5e56cb8a840a6998261940d793242`，deployRunId `aa32f449dc254af6abfdc8b8622b859c`。
- 组合上传：operationId `e153d13fa2a64d4ab848fd94799053fd`，deployRunId `6a27a2299998413087215e2d9f99a3d3`。
- 元数据保留测试编辑：operationId `e6b1b994a44340779884466a2964af09`。
- 前置独立上传调试也真实成功：operationId `2127ea096b834255ba08f7f292ac175f`。此次首次将远端大小从 59,100 更新为 60,591,424 字节，Steam 更新时间从 1793767916 变为 1793789708。
- 本机 Steam 的本轮元数据编辑和相同内容再次提交未推进 m_rtimeUpdated；工具如实返回，不据时间相等推断失败或制造新的时间。

## 首次发布与可见性

临时精确身份：`trigger.cowgun.firstuploadtest.056960ba484f`。

新条目：[3833513939](https://steamcommunity.com/sharedfiles/filedetails/?id=3833513939)。第一次正式 upload 自动 CreateItem 并提交，第二次 upload 自动更新同 ID，没有创建第二条；条目由用户后续清理，工具未调用删除接口。

| 步骤 | operationId | 真实回读 |
|---|---|---|
| 首次创建并上传 | 7c79bbbd284b4d849b1c8113f66dbdf1 | 新 ID 保存成功 |
| 再次上传 | 62604b11cf074693b5c9a3b56c8784bd | created=false，ID 不变 |
| private | 3e3dd2f82ef84556a27d4fdd8afe2fe7 | private |
| public | bedeaae659e84231beda5a08094bfdc3 | public |
| friends-only | 3740e20c31cf44428bca6379674625f8 | friends-only |
| unlisted | 06d9879214e5456ca240a423ff6b5345 | unlisted |
| private | a1eec358eb7a4b3999cf659eb8eb9731 | private |

原身份已恢复为 trigger.cowgun / 3833398574，主条目和新条目最终均为 private。已知绑定记录同时保留原身份与测试身份；临时身份事务已清理。

## 游戏证据与最终基线

- 真实游戏读取 runId：`dda7f33e41a74756a4312e9a9dd8b41d`。
- 工具返回的原始日志：`D:\RimworldProject3\奶牛枪\.tools\log\[2026-11, 19-13]\精选日志.log`，包含本轮 WORKSHOP_ASSETS_VERIFIED。
- 实测纹理逻辑尺寸：1024×1024、262×262、512×512、384×384；音频单声道、22050 Hz、4410 samples、0.2 秒。测试时五个原始源文件均不存在于部署目录。
- 最终 deployRunId：`89f6efb7f8eb4116bcb988273976639d`。目标 190 个文件、项目/构建输入 311 个，publishReady=true、assetPackState=verified。
- 收据原始路径：`D:\RimworldProject3\奶牛枪\.tools\ProjectData\deployment-receipt.json`。当前五个源文件的 SHA-256 均已再次对应收据；上传 dry-run 差异为空。
- 正常内容提交不启动游戏；上传返回游戏进程前后列表为空。官方参考和实际读取只发生在明确的测试路径。
- 曾遇到一次 Windows 文件替换 1175，真实事务回滚且收据保持失效；正式重新部署成功。补充仅针对 1175 的同卷原子替换回退并做单元复现，真实共享冲突仍拒绝并保留回滚保护。

## 变更与后续

本批增加官方烘焙适配器、共享发布/元数据服务、超时时间证据、组合锁上下文、五文件可恢复测试事务、实际游戏探针夹具及第一身份事务驱动；移除旧上传内核，旧上传入口继续不可绕过新服务。

奶牛枪外部项目变更：0.1.2 的 About 元数据、程序集版本 1.0.2.0、合成 WAV、Sounds 同步项及仅显式测试参数启用的 Mod 入口。该项目不是 Git 仓库；游戏探针源代码作为开发仓库中的可审查测试夹具一起提交。

第三批仍须交付真实订阅/下载、最新下载副本逐文件核对、0.1.3 内容更新、helper/绑定异常边界补齐、正式发行包和发行 CLI 回归。未把第三批计作完成；贡献者检测和历史次数继续按计划不做。
