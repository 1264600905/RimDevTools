---
name: rimdevtools
description: 操作项目本地的 RimDevTools CLI，处理 RimWorld 1.6 模组配置、健康检查、构建、事务化逐文件部署和非 DLL 文件热同步、参考模组发现与 ILSpy 反编译、由 LogSync 支持的 quick/manual/auto/XML 测试，以及打包的 LogSyncMod 安装。处理 mod_config.jsonc、.tools/RimDevTools.exe、部署、测试、参考模组或工具模组安装时使用。
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
3. 用 `mod search`、`mod dependencies` 或 `mod dependents` 只读发现参考模组。仅在任务范围包含复制/反编译参考模组时使用 `mod copy`。
4. 用 `test run --mode quick` 验证启动和运行就绪；用 `manual` 进行用户观察的游玩；对 LogSync AutoTest 用 `auto` 或 `xml`。
5. 用 `mods install-tools --dry-run` 检查打包的 LogSyncMod 安装计划；仅在任务范围包含安装时才去掉 `--dry-run`。
6. 仅在需要持久化 LogSync 设置时使用 `test settings`。正常测试会以事务方式应用并还原设置。

## 输出与详细模式

RimDevTools 默认只返回摘要和关键字段；所有命令仍将最终结果写为一个 JSON 对象。多数子命令支持 `--verbose`，用于查看完整计划、候选列表、模组清单和子进程诊断。日常调用不要默认附加 `--verbose`；只有摘要不足以判断结果或正在排查问题时再使用。警告和错误始终可见。构建/Unity/ILSpy 成功时的常规输出默认隐藏；失败时会显示有限的末尾诊断。游戏进程自身 stdout/stderr 始终丢弃。

## 诊断

运行：

```powershell
.\.tools\RimDevTools.exe doctor          （一般使用这个，已经满足的验证项目不会显示）
.\.tools\RimDevTools.exe doctor --verbose   （只有当doctor命令解决仍存在问题时使用这个，会显示大量的验证项目状态）
.\.tools\RimDevTools.exe --version
```

读取 stdout 上唯一的 JSON 对象。默认的 `checks` 数组包含可操作问题及已解析的 `Assembly-CSharp.dll` 和 `0Harmony.dll` 绝对路径；用 `ready`、`issueCount` 和 `warningCount` 总结，其他成功/已禁用/不适用检查则使用 `--verbose` 查看。对于 1.7+，.NET Framework targeting pack 检查默认隐藏，verbose 才会显示 `not_applicable`；不要建议为 1.7+ 安装 4.8 或 4.7.2 targeting pack。修复范围最小的失败前提条件后重新运行 `doctor`。它不会构建，也不会检查启动工具所需的 .NET 10 Runtime。

## 部署

1. 根据doctor的路径探测，尝试修复 `mod_config.jsonc` 中的 游戏路径，工坊路径，完成后重新执行 `doctor` 验证,检查状态是否更新。
2. 填写正确的项目 `mod_name`、`rimworld_dir`、`csproj_paths` 和 `sync_items`。
3. 运行 `deploy --dry-run` 检查部署摘要；需要核对所有目标路径和替换动作时添加 `--verbose`。
4. 仅在计划正确后运行 `deploy`。
5. 仅当有意分发源代码时启用 `sync_source: true`，或对单次部署添加 `--sync-source`。

`deploy --xml-hot` 会先检查 RimWorld 是否正在运行：运行中时将配置项中的非 DLL 文件暂存后逐文件事务更新，若更新了 `Defs/` 或 `Patches/` XML，则至少等待 3 秒，再向 LogSyncMod 的本机 HTTP endpoint 请求 Def 热重载；游戏还需处于 Playing 且启用开发者模式。游戏未运行时，`--xml-hot` 自动回退到普通构建和逐文件事务部署。热重载请求失败时文件仍已部署，命令会给出 warning；该功能不热更新 C# DLL。`test run` 不接受 `--xml-hot`；`test run --deploy` 使用普通部署。

普通部署会先构建并校验暂存目录，然后让目标目录保持原位，逐文件提交变更。提交前会检查将被替换或删除的文件是否占用；失败时输出被占用路径及可识别的进程。事务记录会在提交失败或意外中断时恢复原文件。不要为绕过失败部署而手动将部分输出复制进活动模组目录；若错误为 `DEPLOY_TARGET_BUSY`，关闭报告的占用程序后重试。若出现 `DEPLOY_RECOVERY_REQUIRED`，先停止 RimWorld，再运行 `test cleanup --dry-run` 检查事务；若出现 `DEPLOY_ROLLBACK_FAILED` 或 `DEPLOY_RECOVERY_FAILED`，保留备份并先运行 `test cleanup --dry-run` 查看恢复状态。`protected_files_regex` 匹配的顶层项目保持不变，更新后可能留下空目录。若启用着色器构建，着色器失败即为部署阻塞项。

## 测试

选择范围最小的合适模式：

- `quick`：自动启动并等待运行就绪信号；冒烟测试默认模式。此测试速度最快，只会检查游戏启动报错。
- `manual`：提供人工手动测试的游戏窗口；默认使用当前桌面。
- `auto`：自动发现测试目标，可使用多个 scope 和精确 DefName 白名单。
- `xml`：由目标模组中的 XML Spec 决定具体测试目标。

auto 测试参数遵循以下规则：

- `--scope <value>` 可重复，多个 scope 取并集。正式值为 `standard/ui/combat/view/misc`；旧细分值仍以原窄范围语义兼容。
- `--defname <value>` 可重复，仅适用于 auto。DefName 是精确白名单，与 scope 取交集；不传 DefName 时枚举所选 scope 下的目标。
- `--batch-size <n>` 适用于 auto/xml，默认 40。包括 Standard 在内的枚举运行都受该计划上限控制；候选超限时从全部候选中全局均匀、无放回随机抽取。
- 未传 scope 时默认为 `Standard`；Standard 横跨 UI、Combat、View、Misc，但不隐式启用 `CombatPawns` 和 `SkillPocketMaps`。
- 显式 DefName 测试继续忽略 `autoTestBatchSize`；其他 auto/XML 运行（包括 Standard）都受该限制。
- 启用模组缺少必要上级依赖时只输出 warning，不阻挡测试；应根据 warning 补齐 `min_active_mods` 或 `--active-mods-file`。

例 1，只测试目标模组的三视图：

```powershell
.\.tools\RimDevTools.exe test run --mode auto --target-package-id aoba.deadmanswitch.core --scope threeviews
```

例 2，同时测试指定的近战和远程武器：

```powershell
.\.tools\RimDevTools.exe test run --mode auto --target-package-id aoba.deadmanswitch.core `
  --scope meleeweapons --scope rangedweapons `
  --defname DMS_StunBaton --defname DMS_TacticalPistol
```

测试必须覆盖当前源代码变更时使用 `--deploy`。`--sync-source` 需要 `--deploy`。quick、auto 和 XML 使用配置的桌面隔离默认值；manual 除非明确覆盖，否则使用当前桌面。

运行期间绝不编辑活动 RimWorld 的 `ModsConfig.xml`、LogSync 设置、事务清单或请求/结果文件。RimDevTools 负责事务性还原。失败时报告稳定错误代码并保留恢复产物。

## 参考资料与工具安装

使用 `mod copy --dry-run` 先检查目标摘要和冲突；默认最多列出 10 个目标。需要查看完整依赖闭包、跳过项、程序集计划时添加 `--verbose`。确认计划后使用 `mod copy` 复制到 `mod_config.jsonc` 的 `project_paths.reference_docs`，并通过打包的 ILSpy 反编译程序集。用精确 package ID、名称、目录或绝对路径消除歧义；以 `--deps` 获取除官方内容与 Harmony 外的硬依赖闭包，仅在审查替换目标后使用 `--overwrite-existing`。

使用 `mods install-tools` 安装单个打包的 LogSyncMod 组件。安装器会验证清单、package ID、版本和内容哈希，保留 `PublishedFileId.txt`，阻止降级，并通过暂存/备份回滚。

## 防护规则

- 不要仅从进程退出推断成功；应解析最后的 JSON 和产物。
- 不要重试 `ENVIRONMENT_BUSY`；识别或等待占用操作。
- 不要删除 `.rimdev` 事务来掩盖恢复错误。
- 开始实际开发后，不要以通用模板程序集/模组名称部署。
- 测试进行期间，不要并发执行测试指令，因为环世界是单线程游戏，日志只有一个文件，并发执行后果不可控。
- 结论应关联命令输出、配置值或生成报告。
