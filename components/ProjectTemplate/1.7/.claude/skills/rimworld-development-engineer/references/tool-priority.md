# 工具优先级

## 证据顺序

1. 读取当前项目源码、Defs、补丁、配置和测试。
2. 判断任务是否需要新的官方 RimWorld/DLC 事实。
3. 对于官方 Def、XML、类型、成员、实现或 Harmony 目标，调用 `rimsage` 并遵循其可用性和研究工作流。
4. 对于第三方模组，检查 `project_paths.reference_docs`；当所需模组缺失时使用 `mod search` 和 `mod copy`。
5. 仅在所选 API、字段、Def 或目标所需证据被记录后才实现。
6. 通过本项目的静态检查、构建、部署计划和适当的游戏测试验证结果。

RimSage 是有条件的研究前提，而不是本地编辑、构建、部署、测试或日志诊断的前提，只要这些操作不引入新的官方假设。

## 官方内容

使用 RimSage 发现精确的 Def、检查原始或合并的 XML、跟随类桥接字段、搜索官方源码并读取聚焦的 C# 符号。如果服务缺失或失败，遵循 `rimsage` 技能的配置修复和单次重试。当访问仍无法恢复时停止依赖它的研究。

不要添加版本或新鲜度检查。接受名为 `rimsage` 的正常工作的本地服务，就像默认远程服务一样。

## 当前项目

直接读取可编辑的项目文件。既有源码和测试对本项目已实现的行为具有权威性。不要用远程证据声称本地变更存在。

## 第三方内容

先使用既有本地参考。需要拉取时：

```powershell
.\.tools\RimDevTools.exe mod search "<query>"
.\.tools\RimDevTools.exe mod copy "<exact-query>" --dry-run
```

仅在任务明确要求时选择 `--deps`、`--include-ignored` 或 `--overwrite-existing`。确认预览计划后执行复制，再检查 About 元数据、LoadFolders、XML、补丁、程序集和生成的 ILSpy 源码。

## 项目操作

RimDevTools 默认输出摘要并隐藏成功的子进程日志。保持默认精简模式；仅在摘要不足以判断或需要诊断细节时传入 `--verbose`。warning 和错误始终保留。

- 健康：`RimDevTools.exe doctor`
- 部署计划：`RimDevTools.exe deploy --dry-run`
- 部署：`RimDevTools.exe deploy`
- XML/Def 热同步：`RimDevTools.exe deploy --xml-hot`（游戏运行时热同步；游戏未运行时回退到普通部署。复制 Def/Patches XML 后等待至少 3 秒再请求重载）
- 测试：`RimDevTools.exe test run`
- 参考研究：`RimDevTools.exe mod search|copy|dependencies|dependents`

打包的 ILSpy 可执行文件仍然是 `mod copy` 所必需的。
