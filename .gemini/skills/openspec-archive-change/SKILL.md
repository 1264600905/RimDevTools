---
name: openspec-archive-change
description: 在实验性工作流中归档已完成的变更。当用户想要在实施完成后最终确定并归档变更时使用。
license: MIT
compatibility: Requires openspec CLI.
metadata:
  author: openspec
  version: "1.0"
  generatedBy: "1.1.1"
---

在实验性工作流中归档已完成的变更。

**输入**：可选择指定变更名称。如果省略，检查是否可以从对话上下文中推断。如果模糊或不明确，必须提示可用的变更。

**步骤**

1. **如果没有提供变更名称，提示选择**

   运行 `openspec list --json` 获取可用变更。使用 **AskUserQuestion 工具** 让用户选择。

   仅显示活跃变更（未归档）。
   如果可用，包括每个变更使用的 Schema。

   **重要**：不要猜测或自动选择变更。始终让用户选择。

2. **检查产物完成状态**

   运行 `openspec status --change "<name>" --json` 检查产物完成情况。

   解析 JSON 以了解：
   - `schemaName`：正在使用的工作流
   - `artifacts`：产物列表及其状态（`done` 或其他）

   **如果有产物未 `done`：**
   - 显示列出未完成产物的警告
   - 使用 **AskUserQuestion 工具** 确认用户是否要继续
   - 如果用户确认则继续

3. **检查任务完成状态**

   阅读任务文件（通常是 `tasks.md`）以检查未完成的任务。

   统计标记为 `- [ ]`（未完成）与 `- [x]`（完成）的任务。

   **如果发现未完成的任务：**
   - 显示列出未完成任务计数的警告
   - 使用 **AskUserQuestion 工具** 确认用户是否要继续
   - 如果用户确认则继续

   **如果不存在任务文件：** 继续而不显示任务相关警告。

4. **评估增量规范同步状态**

   检查 `openspec/changes/<name>/specs/` 中的增量规范。如果不存在，继续而不显示同步提示。

   **如果存在增量规范：**
   - 将每个增量规范与 `openspec/specs/<capability>/spec.md` 中的对应主规范进行比较
   - 确定将应用哪些更改（添加、修改、删除、重命名）
   - 在提示之前显示合并总结

   **提示选项：**
   - 如果需要更改："立即同步（推荐）"，"归档而不同步"
   - 如果已同步："立即归档"，"仍然同步"，"取消"

   如果用户选择同步，使用 Task 工具（subagent_type: "general-purpose", prompt: "Use Skill tool to invoke openspec-sync-specs for change '<name>'. Delta spec analysis: <include the analyzed delta spec summary>"）。无论选择如何，都继续归档。

5. **执行归档**

   如果归档目录不存在，创建它：
   ```bash
   mkdir -p openspec/changes/archive
   ```

   使用当前日期生成目标名称：`YYYY-MM-DD-<change-name>`

   **检查目标是否已存在：**
   - 如果是：报错失败，建议重命名现有归档或使用不同日期
   - 如果否：将变更目录移动到归档

   ```bash
   mv openspec/changes/<name> openspec/changes/archive/YYYY-MM-DD-<name>
   ```

6. **显示总结**

   显示归档完成总结，包括：
   - 变更名称
   - 使用的 Schema
   - 归档位置
   - 规范同步状态（已同步 / 跳过同步 / 无增量规范）
   - 关于任何警告的说明（未完成的产物/任务）

**成功时的输出**

```
## 归档完成

**变更：** <change-name>
**Schema：** <schema-name>
**归档至：** openspec/changes/archive/YYYY-MM-DD-<name>/
**规范：** ✓ 已同步到主规范

所有产物已完成。所有任务已完成。
```

**成功时的输出（无增量规范）**

```
## 归档完成

**变更：** <change-name>
**Schema：** <schema-name>
**归档至：** openspec/changes/archive/YYYY-MM-DD-<name>/
**规范：** 无增量规范

所有产物已完成。所有任务已完成。
```

**带警告的成功输出**

```
## 归档完成（带警告）

**变更：** <change-name>
**Schema：** <schema-name>
**归档至：** openspec/changes/archive/YYYY-MM-DD-<name>/
**规范：** 同步已跳过（用户选择跳过）

**警告：**
- 归档时有 2 个未完成的产物
- 归档时有 3 个未完成的任务
- 增量规范同步已跳过（用户选择跳过）

如果这不是故意的，请检查归档。
```

**错误时的输出（归档已存在）**

```
## 归档失败

**变更：** <change-name>
**目标：** openspec/changes/archive/YYYY-MM-DD-<name>/

目标归档目录已存在。

**选项：**
1. 重命名现有的归档
2. 如果是重复的，删除现有的归档
3. 等到不同的日期再归档
```

**护栏**
- 如果未提供，始终提示选择变更
- 使用产物图 (openspec status --json) 进行完成检查
- 不要因警告阻止归档 - 只需通知并确认
- 移动到归档时保留 .openspec.yaml（它随目录移动）
- 显示发生的清晰总结
- 如果请求同步，使用 Skill 工具调用 `openspec-sync-specs`（Agent 驱动）
- 如果存在增量规范，始终运行同步评估并在提示前显示合并总结
