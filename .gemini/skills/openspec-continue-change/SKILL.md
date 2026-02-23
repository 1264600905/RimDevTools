---
name: openspec-continue-change
description: 通过创建下一个产物继续处理 OpenSpec 变更。当用户想要推进他们的变更、创建下一个产物或继续他们的工作流时使用。
license: MIT
compatibility: Requires openspec CLI.
metadata:
  author: openspec
  version: "1.0"
  generatedBy: "1.1.1"
---

通过创建下一个产物继续处理变更。

**输入**：可选择指定变更名称。如果省略，检查是否可以从对话上下文中推断。如果模糊或不明确，必须提示可用的变更。

**步骤**

1. **如果没有提供变更名称，提示选择**

   运行 `openspec list --json` 以获取按最近修改排序的可用变更。然后使用 **AskUserQuestion 工具** 让用户选择要处理哪个变更。

   提供前 3-4 个最近修改的变更作为选项，显示：
   - 变更名称
   - Schema（如果有 `schema` 字段，否则为 "spec-driven"）
   - 状态（例如，"0/5 tasks", "complete", "no tasks"）
   - 最近修改时间（来自 `lastModified` 字段）

   将最近修改的变更标记为 "(Recommended)"，因为这很可能是用户想要继续的。

   **重要**：不要猜测或自动选择变更。始终让用户选择。

2. **检查当前状态**
   ```bash
   openspec status --change "<name>" --json
   ```
   解析 JSON 以了解当前状态。响应包括：
   - `schemaName`：正在使用的工作流 Schema（例如，"spec-driven"）
   - `artifacts`：产物数组及其状态（"done", "ready", "blocked"）
   - `isComplete`：指示是否所有产物都已完成的布尔值

3. **根据状态行动**：

   ---

   **如果所有产物都已完成 (`isComplete: true`)**：
   - 祝贺用户
   - 显示最终状态，包括使用的 Schema
   - 建议："所有产物已创建！你现在可以实施此变更或将其归档。"
   - 停止

   ---

   **如果有产物准备创建**（状态显示有 `status: "ready"` 的产物）：
   - 从状态输出中选择第一个 `status: "ready"` 的产物
   - 获取其指令：
     ```bash
     openspec instructions <artifact-id> --change "<name>" --json
     ```
   - 解析 JSON。关键字段是：
     - `context`：项目背景（给你的约束 - 不要包含在输出中）
     - `rules`：产物特定规则（给你的约束 - 不要包含在输出中）
     - `template`：用于输出文件的结构
     - `instruction`：Schema 特定的指导
     - `outputPath`：写入产物的位置
     - `dependencies`：已完成的产物，用于阅读上下文
   - **创建产物文件**：
     - 阅读任何已完成的依赖文件以获取上下文
     - 使用 `template` 作为结构 - 填充其部分
     - 应用 `context` 和 `rules` 作为约束 - 但不要将它们复制到文件中
     - 写入指令中指定的输出路径
   - 显示已创建的内容以及现在解锁的内容
   - 在创建一个产物后停止

   ---

   **如果没有产物准备好（全部阻塞）**：
   - 这在有效 Schema 中不应发生
   - 显示状态并建议检查问题

4. **创建产物后，显示进度**
   ```bash
   openspec status --change "<name>"
   ```

**输出**

每次调用后，显示：
- 创建了哪个产物
- 正在使用的 Schema 工作流
- 当前进度（N/M 完成）
- 现在解锁了哪些产物
- 提示："想要继续吗？只需让我继续或告诉我下一步做什么。"

**产物创建指南**

产物类型及其用途取决于 Schema。使用指令输出中的 `instruction` 字段来了解要创建什么。

常见产物模式：

**spec-driven schema** (proposal → specs → design → tasks):
- **proposal.md**：如果不清楚，询问用户关于变更的信息。填写 Why, What Changes, Capabilities, Impact。
  - Capabilities 部分至关重要 - 列出的每个 capability 都需要一个 spec 文件。
- **specs/<capability>/spec.md**：为 proposal 的 Capabilities 部分中列出的每个 capability 创建一个 spec（使用 capability 名称，而不是变更名称）。
- **design.md**：记录技术决策、架构和实施方法。
- **tasks.md**：将实施分解为带复选框的任务。

对于其他 Schema，遵循 CLI 输出中的 `instruction` 字段。

**护栏**
- 每次调用创建一个产物
- 在创建新产物之前，始终阅读依赖产物
- 绝不跳过产物或乱序创建
- 如果上下文不清楚，在创建之前询问用户
- 在标记进度之前，验证写入后产物文件是否存在
- 使用 Schema 的产物序列，不要假设特定的产物名称
- **重要**：`context` 和 `rules` 是给你的约束，不是文件内容
  - 不要将 `<context>`, `<rules>`, `<project_context>` 块复制到产物中
  - 这些指导你写什么，但绝不应出现在输出中
