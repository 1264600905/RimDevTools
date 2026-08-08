---
name: rimdevtools
description: 操作项目本地的 RimDevTools CLI，处理 RimWorld 1.6 模组配置、健康检查、构建、原子部署、参考模组发现与 ILSpy 反编译、由 LogSync 支持的 quick/manual/auto/XML 测试，以及打包的 LogSyncMod 安装。处理 mod_config.jsonc、.tools/RimDevTools.exe、部署、测试、参考模组或工具模组安装时使用。
---

# RimDevTools 工具

在模组根目录使用项目本地 Windows CLI：

```powershell
.\.tools\RimDevTools.exe <command> [options]
```

将 `mod_config.jsonc` 视为项目契约。应在其所在目录或直属子目录运行命令。修改配置，或使用参数、变更行为或错误契约重要的命令前，阅读 [references/commands-and-config.md](references/commands-and-config.md)。

## 选择工作流

1. 遇到环境失败、状态不清或能力疑问，先用只读的 `doctor`。
2. 用 `deploy --dry-run` 检查构建和同步计划。仅当用户要求部署或工作流明确需要时，才运行会修改内容的 `deploy`。
3. 用 `ref search`、`ref deps` 或 `ref related` 只读发现参考模组。仅在任务范围包含复制/反编译参考模组时使用 `ref pull`。
4. 用 `test run --mode quick` 验证启动和运行就绪；用 `manual` 进行用户观察的游玩；对 LogSync AutoTest 用 `auto` 或 `xml`。
5. 用 `mods install-tools --dry-run` 检查打包的 LogSyncMod 安装计划；仅在任务范围包含安装时才去掉 `--dry-run`。
6. 仅在需要持久化 LogSync 设置时使用 `test settings`。正常测试会以事务方式应用并还原设置。

## 诊断

运行：

```powershell
.\.tools\RimDevTools.exe doctor          （一般使用这个，已经满足的验证项目不会显示）
.\.tools\RimDevTools.exe doctor --verbose   （只有当doctor命令解决仍存在问题时使用这个，会显示大量的验证项目状态）
.\.tools\RimDevTools.exe --version
```

读取 stdout 的最后一个 JSON 对象。默认的 `checks` 数组包含可操作问题及已解析的 `Assembly-CSharp.dll` 和 `0Harmony.dll` 绝对路径；用 `ready`、`issueCount` 和 `warningCount` 总结，其他成功/已禁用/不适用检查则使用 `--verbose` 查看。修复范围最小的失败前提条件后重新运行 `doctor`。它不会构建，也不会检查启动工具所需的 .NET 10 Runtime。

## 部署

1. 根据doctor的路径探测，尝试修复 `mod_config.jsonc` 中的 游戏路径，工坊路径，完成后重新执行 `doctor` 验证,检查状态是否更新。
2. 填写正确的项目 `mod_name`、`rimworld_dir`、`csproj_paths` 和 `sync_items`。
3. 运行 `deploy --dry-run` 并检查部署项目是否符合预期,确认包含所有预期程序集和内容根目录。
4. 仅在计划正确后运行 `deploy`。
5. 仅当有意分发源代码时添加 `--sync-source`。

部署会在替换目标前构建已配置项目，并使用原子暂存。不要为绕过失败部署而手动将部分输出复制进活动模组目录。若启用着色器构建，着色器失败即为部署阻塞项。

## 测试

选择范围最小的合适模式：

- `quick`：自动启动并等待运行就绪信号；冒烟测试默认模式。此测试速度最快，只会检查游戏启动报错。
- `manual`：提供人工手动测试的游戏窗口；默认使用当前桌面。
- `auto`：执行 自动化测试，会自动包含可能的测试物品，不需要手动设置，耗时最长，建议作为最终收尾测试。
- `xml`：执行 XML 驱动的 AutoTest 用例, 此功能暂时不推荐使用。

测试必须覆盖当前源代码变更时使用 `--deploy`。`--sync-so