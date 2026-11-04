# 验证循环

## 变更前

1. 确认 `mod_config.jsonc` 路径、目标身份，以及源码分支、`game_version`、构建引用和输出目录指向同一目标版本。
2. 前提条件或恢复状态不确定时运行 `doctor`。
3. 保持 RimWorld 关闭，除非所选 RimDevTools 命令负责启动它；禁止通过电脑操作或 GUI 自动化控制游戏。
4. 避免并发进行部署和测试操作。

## 静态检查

- 每次编辑后解析 XML。
- 检查重复的 Def 名称和未解析的翻译键。
- 使用配置的项目路径仅构建当前目标版本下变更的 C# 项目；其他版本保留已有 DLL。
- 构建前确认模组工程在所有配置中设置 `DebugSymbols=false`、`DebugType=none`、`GenerateDependencyFile=false`；构建后检查目标 `Assemblies/`，禁止生成、部署或打包 `.pdb`、`.deps.json`。
- 用聚焦的 RimSage C# 符号和源码证据验证官方的 Harmony 目标。
- 检查 `About.xml` 的 `supportedVersions`、`LoadFolders.xml` 和 `sync_items` 覆盖所有受支持版本及共享内容；确认各版本内容与所需 DLL 齐备，部署计划仅编译当前目标版本且不会清理其他版本。

## 运行时检查

需要运行时验证时，默认只进行一次 quick 冒烟测试：

```powershell
.\.tools\RimDevTools.exe deploy --dry-run
.\.tools\RimDevTools.exe test run --deploy --mode quick
```

不要使用 CUA、computer-use、鼠标键盘注入、截图识别或其他桌面自动化操作 RimWorld。不要自行编写测试用例、测试项目或测试 DLL。`auto` 仅在玩家明确要求自动测试时运行，并使用 AutoTestRunner 生成的用例；`manual` 仅在玩家明确要求交互验证时使用，由玩家操作游戏。仅在打算部署源码时使用 `--sync-source`。

当用户明确要求游戏运行时同步 Def/patch XML 时，先运行 `deploy --dry-run --xml-hot` 检查计划，再运行独立的 `deploy --xml-hot`。游戏运行时它就地同步文件，复制完成后至少等待 3 秒，再通过本机 HTTP 请求 LogSyncMod 重载 Def；游戏需处于 Playing 且启用开发者模式。游戏未运行时它回退到普通构建和逐文件事务部署。`test run` 不接受 `--xml-hot`；其 `--deploy` 始终是启动前的普通部署。

## 结果审查

要求满足以下全部：

- quick 模式最终 JSON 具有 `ok: true`，并收到当前运行的正常完成信号。
- 精选日志没有当前模组导致的红色 Error/Exception；warning 按来源和影响判断，不要求清空无关 warning。
- 只有玩家明确要求并实际运行 Auto/XML 时，才检查其结果清单完整且无缺失/失败用例。
- 事务恢复成功。

当已知的环境性警告出现时，使用模组身份、堆栈、路径或未变更的基线记录它为何不相关。绝不要仅按文本模式忽略。

## 失败报告

报告：

1. 稳定的错误代码和命令/模式。
2. 少量决定性的日志摘录。
3. 标识归属的 Def/成员/文件证据。
4. 已尝试的最小修复和重跑检查，以及剩余阻塞项。
