# XML 热重载本机 HTTP 方案

## 目标

让 `RimDevTools.exe deploy --xml-hot` 在完成 XML 文件热部署后，直接请求正在运行的 RimWorld 调用游戏内置 Def 热重载。v1 只处理这条部署到重载链路。

## 当前实现

- `deploy --xml-hot` 把配置的同步项就地复制到游戏 Mods 目录，跳过 DLL，不等待游戏退出，也不构建程序集。
- RimWorld 1.6 提供 `PlayDataLoader.HotReloadDefs()`；开发者菜单中的 “Hot reload Defs” 调用此方法。
- 该 API 会重新处理所有启用模组的 Def 和补丁，并刷新相关游戏状态，不是只重载刚部署的模组。它不会热更新 C# DLL。
- CLI 在全部文件复制完成后等待至少 3 秒，再请求运行中的 LogSyncMod 重载；本轮没有复制 Def/Patches XML 时不请求。
- 游戏侧本机 HTTP 服务和主线程请求队列已加入 LogSyncMod。HTTP smoke test 和模拟游戏部署测试已通过；真实 RimWorld 内的重载尚未实测。

## 调用模式

- 独立 `deploy --xml-hot` 会先检测 RimWorld 进程。运行中时使用就地同步并请求热重载；未运行时自动执行普通构建与原子部署。
- `test run` 不接受 `--xml-hot`。它会先退出已有游戏，再部署并启动新游戏，因此没有可接收热重载请求的旧游戏进程；`test run --deploy` 始终是普通部署。

## v1 流程

1. LogSyncMod 在 RimWorld 进程启动后绑定 `127.0.0.1` 的临时 TCP 端口，并在游戏配置目录写入包含 PID、端口和随机令牌的 endpoint 文件。
2. `deploy --xml-hot` 完成所有文件复制后，检查本轮同步项中是否包含 `Defs/` 或 `Patches/` 下的 XML；若没有则跳过重载请求。
3. RimDevTools 从 `mods_config_path` 所在目录读取当前进程的 endpoint 文件，向 `http://127.0.0.1:<port>/v1/xml/hotreload` 发送带 bearer token 的 POST 请求。
4. 游戏侧服务端只负责接收和校验请求，再将它放入线程安全队列。
5. 游戏主线程在可处理的帧中取出请求；确认游戏处于 Playing 状态且开发者模式已开启后，调用 `PlayDataLoader.HotReloadDefs()`。
6. 游戏侧以 HTTP 202 和 `queued` 应答。该应答表示请求已接受并排队，不表示游戏已完成重载。

在发送请求前，RimDevTools 必须从全部文件复制完成时起至少等待 3 秒，避免游戏在文件写入收尾期间开始重载。没有运行中的 RimWorld 进程时不等待；有多个进程时不猜测目标。

HTTP 接收线程不直接访问 Verse/Unity 对象，也不直接调用热重载 API。`HotReloadDefs()` 在排入长任务前会读取当前 Def 数据，因此调用动作必须由游戏主线程执行。调度点应在暂停时仍能运行，例如游戏 UI 帧回调。

## HTTP 约定

- 传输：仅绑定 IPv4 loopback `127.0.0.1`，不接受外部网卡连接。由操作系统分配临时端口，避免固定端口冲突。
- 发现：endpoint 文件 `RimDevTools.LogSyncMod.XmlHotReload.v1.<pid>.endpoint` 位于 RimWorld `Config` 目录，字段为 `version`、`pid`、`port` 和随机 `token`。RimDevTools 从配置的 `mods_config_path` 目录查找；进程退出时尽力删除 endpoint 文件。
- 请求：`POST /v1/xml/hotreload`，带 `Authorization: Bearer <token>`，空 body。
- 接受：HTTP `202 Accepted`，body 为 `queued`。
- 拒绝：HTTP `409 Conflict`，body 为 `game_not_playing` 或 `dev_mode_required`；重载 API 失败时返回 HTTP `500` 和 `reload_failed`；等待游戏主线程处理超时返回 HTTP `504` 和 `request_timeout`。
- 其他：无效 token 返回 HTTP `401`；无效路径返回 HTTP `404`。请求连接和响应等待均使用有限超时。
- 多实例：依据存活 PID 对 endpoint 去重；发现多个运行中的 LogSyncMod endpoint 时，拒绝猜测目标并报告歧义。

## 代码边界

- CLI 在 [`DeployCommand.cs`](../RimDevTools/Commands/DeployCommand.cs) 的热同步完成后等待至少 3 秒再发请求，并将 `xmlHotReload` 状态附加到部署结果。只在显式使用 `--xml-hot` 时发送。
- 游戏侧 HTTP 服务与主线程请求队列放在 [`source/LogSyncMod`](../source/LogSyncMod)；服务随 LogSyncMod 加载启动。主线程 pump 由游戏 UI 帧回调驱动，以便游戏暂停时也能处理请求。
- HTTP smoke test 与模拟游戏进程部署测试覆盖部署、3 秒等待及请求/应答。

## 部署结果

文件复制结果与游戏重载结果分别报告：

- XML 文件复制成功且游戏接受请求：部署成功，`xmlHotReload` 为 `queued`。
- 没有正在运行的游戏、LogSyncMod 未加载、HTTP endpoint 不可用或请求超时：文件仍保留在部署目标中，部署结果说明 `xmlHotReload` 未触发，并附带 warning；不把已完成的文件复制伪装成回滚。
- 游戏拒绝请求：保留已部署文件，返回对应重载状态和错误码。只有重载后游戏才会读取新的 Def/patch。
- 文件复制中途失败：按照现有 `deploy --xml-hot` 错误路径返回，不发送重载请求。

v1 只确认“已排队”，不承诺确认重载完成。重载期间的错误仍由 RimWorld 日志报告。

## 不包含

- 自动监视任意 XML 文件并重载；只有 `deploy --xml-hot` 发起请求。
- DLL/C# 热更新。
- 自动回滚已复制的文件。

## 验收标准

1. 游戏正在 Playing、开发者模式开启且 LogSyncMod 已加载时，执行 `deploy --xml-hot` 并同步 Def/patch XML，游戏无需重启或手动点击菜单即可收到一次热重载请求。
2. 未同步 Def/patch XML 时不发起热重载。
3. 游戏未运行、未加载 LogSyncMod、未进入 Playing 或未开启开发者模式时，CLI 清楚报告重载未触发，且不误报重载成功。
4. HTTP 连接断开、协议错误或客户端超时不会让游戏主线程等待网络 I/O。
5. 原有非 DLL 覆盖、DLL 保留和目标目录旧文件保留行为不变。

## 后续验证

已验证独立进程 HTTP smoke test、模拟游戏部署链路和 LogSyncMod 1.6 构建。最终仍需在 RimWorld 1.6 开发者模式中，用一个 Def 或 patch XML 做一次 `deploy --xml-hot` 实测，并从游戏日志确认内置热重载被调用。
