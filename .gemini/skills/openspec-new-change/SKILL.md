---
name: openspec-new-change
description: 使用实验性产物工作流开始一个新的 OpenSpec 变更。当用户想要以结构化的分步方法创建新功能、修复或修改时使用。
license: MIT
compatibility: Requires openspec CLI.
metadata:
  author: openspec
  version: "1.0"
  generatedBy: "1.1.1"
---

使用实验性产物驱动方法开始一个新的变更。

**输入**：用户的请求应包含变更名称（kebab-case）或他们想要构建的内容的描述。

**步骤**

1. **如果没有提供明确的输入，询问他们想要构建什么**

   使用 **AskUserQuestion 工具**（开放式，无预设选项）询问：
   > "你想进行什么变更？描述你想构建或修复的内容。"

   从他们的描述中，得出一个 kebab-case 名称（例如，"增加用户认证" → `add-user-auth`）。

   **重要**：在不了解用户想要构建什么之前，不要继续。

2. **确定工作流 Schema**

   使用默认 Schema（省略 `--schema`），除非用户明确请求不同的工作流。

   **仅在用户提及时使用不同的 Schema：**
   - 特定的 Schema 名称 → 使用 `--schema <name>`
   - "显示工作流" 或 "什么工作流" → 运行 `openspec schemas --json` 并让他们选择

   **否则**：省略 `--schema` 以使用默认值。

3. **创建变更目录**
   ```bash
   openspec new change "<name>"
   ```
   仅在用户请求特定工作流时添加 `--schema <name>`。
   这将在 `openspec/changes/<name>/` 下创建一个使用所选 Schema 的脚手架变更。

4. **显示产物状态**
   ```bash
   openspec status --change "<name>"
   ```
   这显示了哪些产物需要创建，哪些已就绪（依赖关系已满足）。

5. **获取第一个产物的指令**
   第一个产物取决于 Schema（例如，spec-driven 的 `proposal`）。
   检查状态输出，找到第一个状态为 "ready" 的产物。
   ```bash
   openspec instructions <first-artifact-id> --change "<name>"
   ```
   这输出创建第一个产物的模板和上下文。

6. **停止并等待用户指示**

**输出**

完成步骤后，总结：
- 变更名称和位置
- 正在使用的 Schema/工作流及其产物序列
- 当前状态（0/N 产物完成）
- 第一个产物的模板
- 提示："准备好创建第一个产物了吗？只需描述这个变更的内容，我会起草它，或者你可以让我继续。"

**护栏**
- 不要创建任何产物 - 仅显示指令
- 不要超越显示第一个产物模板
- 如果名称无效（不是 kebab-case），要求一个有效名称
- 如果具有该名称的变更已存在，建议继续该变更
- 如果使用非默认工作流，传递 --schema
