# RimDevTools CLI V2 实现验证报告

验证日期：2026-08-07

## 实现结论

`RimDevTools_CLI命令设计_V2.md` 的阶段一至阶段五已实现。正式命令由 C# 实现；发布包不依赖旧 Python/PowerShell 业务脚本，包含自包含单文件 CLI、版本匹配的 RimSearcher DataMod、ILSpy 和许可证。

父仓库主要提交：

- `ded9de3`：命令参数、安全路径、锁、测试 runId 和部署输出修复。
- `993f649`：`data refresh/status`、SQLite 验证与事务流程。
- `d508e80`：自包含发布、组件版本和许可证验证。
- `1cd6652`：LogSync 设置文件不存在时的事务创建与恢复。
- `0eca93d`：Shader 失败保护和 stale data 环境测试。

RimSearcher 独立仓库主要提交：

- `676c13b`：`--db`、项目发现、自动导出、数据库元数据和协议测试。
- `864f1fc`：项目数据库与自动导出文档。
- `fb0fa33`：导出前创建数据库输出目录。

## 自动验证

| 验证项 | 结果 |
| --- | --- |
| RimDevTools Release 构建 | 0 警告，0 错误 |
| RimDevTools 回归套件 | 81/81 通过 |
| RimSearcher CLI/协议测试 | 14/14 通过 |
| RimSearcher CLI 构建 | 0 警告，0 错误 |
| RimSearcher DataMod 构建 | 0 警告，0 错误 |
| 发布布局测试 | 16/16 通过 |
| 发布组件版本 | RimSearcher/DataMod 均为 3.1.3.0 |

回归范围包括项目发现、正式 JSONC、参数白名单、Reparse Point、全局 Mutex、事务冲突/恢复、部署回滚、参考模组歧义/循环/批量回滚、四种测试模式、runId 隔离、真实 SQLite 完整性/版本/元数据、导出失败/超时/信号冲突和发布布局。

## 真实 RimWorld 1.6 验证

真实游戏版本：`1.6.4871 rev591`。真实语言：`ChineseSimplified (简体中文)`。

| 流程 | 结果 | 证据摘要 |
| --- | --- | --- |
| `test run --mode quick` | 通过 | 收到真实 `RUNTIME_READY_TO_CLOSE` |
| `test run --mode manual` | 通过 | 主窗口正常关闭，CLI 识别退出 |
| 外部 ModList quick | 通过 | 使用真实 `ModsConfig.xml`，自动补充项目模组 |
| `test run --mode auto` | 通过 | runId `6d1f6bd79b954583acec32376348f672`，6/6 用例通过 |
| `test run --mode xml` | 通过 | runId `afa33674e92246e88b06496a7a517ee2`；当前项目 XML 清单为 0/0 |
| `test run --deploy --mode quick` | 通过 | 同一环境锁内构建、原子部署和真实启动 |
| 默认 `data refresh` | 通过 | runId `6a7b9933d2324ea491395d73d536a1a6`，26 个实际模组 |
| 外部 ModList `data refresh` | 通过 | runId `6bdeb13f518749ed9cb4bad910b652a9`，36 个实际模组 |
| 项目数据库自动发现 | 通过 | 无 `--db` 执行 `rimsearcher types` 成功 |
| 显式数据库查询 | 通过 | `--db .rimdev\defs-external.db types` 返回 245 种 Def 类型 |

默认数据库：

- 大小：343,719,936 字节。
- `PRAGMA integrity_check = ok`。
- `user_version = 30103`，与 RimSearcher 3.1.3 匹配。
- 元数据记录 26 个有序实际模组，`refreshRecommended=false`。

外部环境数据库：

- 大小：504,791,040 字节。
- 记录 36 个实际模组。
- 可查询 245 种 Def 类型和 5,744 个 ThingDef。

首次真实数据导出发现 DataMod 未创建 `.rimdev` 父目录，返回 SQLite Error 14；修复提交 `fb0fa33` 后重新构建、安装并通过上述两次真实导出。

## 恢复与发布证据

- 所有真实测试结束后，RimDevTools 待恢复事务数为 0。
- 自动导出 request/processing 文件数为 0。
- RimWorld 残留进程数为 0。
- Shader 关闭的真实组合部署未访问 Unity。
- Shader 启用且 Unity 缺失的回归测试确认部署被阻止且旧目标保持不变。
- 发布包通过 16 项布局/启动/版本/许可证检查。
- 测试前安装备份保存在 `artifacts/installed-backups/20260807-before-v2-real-tests`。

## 第一阶段验收标准

1. 正式业务命令不依赖旧 Python/PowerShell 脚本：通过。
2. 正式命令输出稳定 JSON：通过。
3. RimWorld 环境级写命令全局互斥：通过。
4. 正常、失败、超时和取消路径恢复配置：通过。
5. Shader 关闭不要求 Unity：通过。
6. `ref pull` 歧义和冲突不会静默覆盖：通过。
7. DataMod 无人值守导出并退出游戏：通过。
8. `data status` 验证版本、游戏、语言和实际模组环境：通过。
9. RimSearcher 支持 `--db` 与有限项目发现：通过。
10. 发布包包含版本匹配组件和许可证：通过。

XML 模式当前项目未定义 XML 用例，因此真实运行证明了模式、runId、结果和恢复链路，但没有 XML 业务用例覆盖；自动模式的 6 个标准用例均实际执行并通过。
