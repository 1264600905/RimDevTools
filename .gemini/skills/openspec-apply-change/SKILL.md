---
name: openspec-apply-change
description: 实施 OpenSpec 变更中的任务。当用户想要开始实施、继续实施或处理任务时使用。
license: MIT
compatibility: Requires openspec CLI.
metadata:
  author: openspec
  version: "1.0"
  generatedBy: "1.1.1"
---

实施 OpenSpec 变更中的任务。

**输入**：可选择指定变更名称。如果省略，检查是否可以从对话上下文中推断。如果模糊或不明确，必须提示可用的变更。

**步骤**

1. **选择变更**

   如果提供了名称，使用它。否则：
   - 如果用户提到变更，从对话上下文中推断
   - 如果只有一个活跃变更，自动选择
   - 如果模糊，运行 `openspec list --json` 获取可用变更，并使用 **AskUserQuestion 工具** 让用户选择

   始终宣布："正在使用变更：<name>" 以及如何覆盖（例如，`/opsx:apply <other>`）。

2. **检查状态以了解 Schema**
   ```bash
   openspec status --change "<name>" --json
   ```
   解析 JSON 以了解：
   - `schemaName`：正在使用的工作流（例如，"spec-driven"）
   - 哪个产物包含任务（通常是 "tasks"，对于其他 Schema 检查状态）

3. **获取实施指令**

   ```bash
   openspec instructions apply --change "<name>" --json
   ```

   这返回：
   - 上下文文件路径（因 Schema 而异 - 可能是 proposal/specs/design/tasks 或 spec/tests/implementation/docs）
   - 进度（总数，完成，剩余）
   - 带状态的任务列表
   - 基于当前状态的动态指令

   **处理状态：**
   - 如果 `state: "blocked"`（缺少产物）：显示消息，建议使用 openspec-continue-change
   - 如果 `state: "all_done"`：祝贺，建议归档
   - 否则：继续实施

4. **阅读上下文文件**

   阅读实施指令输出中 `contextFiles` 列出的文件。
   文件取决于正在使用的 Schema：
   - **spec-driven**：proposal, specs, design, tasks
   - 其他 Schema：遵循 CLI 输出中的 contextFiles

5. **显示当前进度**

   显示：
   - 正在使用的 Schema
   - 进度："N/M 任务完成"
   - 剩余任务概览
   - 来自 CLI 的动态指令

6. **实施任务（循环直到完成或阻塞）**

   对于每个待处理任务：
   - 显示正在处理哪个任务
   - 进行所需的代码更改
   - 保持更改最小且专注
   - 在任务文件中标记任务完成：`- [ ]` → `- [x]`
   - 继续下一个任务

   **暂停如果：**
   - 任务不清楚 → 寻求澄清
   - 实施揭示了设计问题 → 建议更新产物
   - 遇到错误或阻碍 → 报告并等待指导
   - 用户打断

7. **完成或暂停时，显示状态**

   显示：
   - 本次会话完成的任务
   - 总体进度："N/M 任务完成"
   - 如果全部完成：建议归档
   - 如果暂停：解释原因并等待指导

**实施期间的输出**

```
## 正在实施：<change-name> (schema: <schema-name>)

正在处理任务 3/7：<task description>
[...实施中...]
✓ 任务完成

正在处理任务 4/7：<task description>
[...实施中...]
✓ 任务完成
```

**完成时的输出**

```
## 实施完成

**变更：** <change-name>
**Schema：** <schema-name>
**进度：** 7/7 任务完成 ✓

### 本次会话完成
- [x] Task 1
- [x] Task 2
...

所有任务完成！准备归档此变更。
```

**暂停时的输出（遇到问题）**

```
## 实施暂停

**变更：** <change-name>
**Schema：** <schema-name>
**进度：** 4/7 任务完成

### 遇到的问题
<description of the issue>

**选项：**
1. <option 1>
2. <option 2>
3. 其他方法

你想怎么做？
```

**护栏**
- 继续处理任务直到完成或阻塞
- 开始前始终阅读上下文文件（来自实施指令输出）
- 如果任务模糊，在实施前暂停并询问
- 如果实施揭示了问题，暂停并建议更新产物
- 保持代码更改最小并限定在每个任务范围内
- 完成每个任务后立即更新任务复选框
- 遇到错误、阻碍或不明确的需求时暂停 - 不要猜测
- 使用 CLI 输出中的 contextFiles，不要假设特定的文件名

**流畅的工作流集成**

此技能支持 "对变更的操作" 模型：

- **可以随时调用**：在所有产物完成之前（如果任务存在），部分实施后，与其他操作交错
- **允许产物更新**：如果实施揭示了设计问题，建议更新产物 - 不是阶段锁定的，流畅工作
