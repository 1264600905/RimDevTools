# 验证循环

## 变更前

1. 确认 `mod_config.jsonc` 路径、目标身份，以及源码分支、`game_version`、构建引用和输出目录指向同一目标版本。
2. 前提条件或恢复状态不确定时运行 `doctor`。
3. 保持 RimWorld 关闭，除非所选命令负责启动它。
4. 避免并发进行部署和测试操作。

## 静态检查

- 每次编辑后解析 XML。
- 检查重复的 Def 名称和未解析的翻译键。
- 使用配置的项目路径仅构建当前目标版本下变更的 C# 项目；其他版本保留已有 DLL。
- 用聚焦的 RimSage C# 符号和源码证据验证官方的 Harmony 目标。
- 检查 `About.xml` 的 `supportedVersions`、`LoadFolders.xml` 和 `sync_items` 覆盖所有受支持版本及共享内容；确认各版本内容与所需 DLL 齐备，部署计划仅编译当前目标版本且不会清理其他版本。

## 运行时检查

从以下开始：

```powershell
.\.tools\RimDevTools.exe deploy --dry-run
.\.tools\RimDevTools.exe test run --deploy --mode quick
```

仅在打算部署源码时使用 `--sync-source`。存在用例时使用 auto 或 XML 模式；auto 模式默认生成截图，不需要额外截图参数。XML 模式遵循每条测试定义的 `captureScreenshots` 设置。manual 模式用于自动化无法表示的交互。

## 结果审查

要求满足以下全部：

- 最终 JSON 具有 `ok: true`。
- 预期的就绪/完成信号属于当前运行。
- Auto/XML 结果清单完整，无缺失/失败用例。
- 精选日志中没有可归因的新错误/警告。
- 事务恢复成功。

当已知的环境性警告出现时，使用模组身份、堆栈、路径或未变更的基线记录它为何不相关。绝不要仅按文本模式忽略。

## 失败报告

报告：

1. 稳定的错误代码和命令/模式。
2. 少量决定性的日志摘录。
3. 标识归属的 Def/成员/文件证据。
4. 尝试过的变更以及每次重测证明了什么。
5. 剩余的阻塞项和下一步所需证据。
