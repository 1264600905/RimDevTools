---
name: openspec-ff-change
description: 快速创建 OpenSpec 产物。当用户想要快速创建实施所需的所有产物而不必单独逐步完成时使用。
license: MIT
compatibility: Requires openspec CLI.
metadata:
  author: openspec
  version: "1.0"
  generatedBy: "1.1.1"
---

快速创建产物 - 一次性生成开始实施所需的一切。

**输入**：用户的请求应包含变更名称（kebab-case）或他们想要构建的内容的描述。

**步骤**

1. **如果没有提供明确的输入，询问他们想要构建什么**

   使用 **AskUserQuestion 工具**（开放式，无预设选项）询问：
   > "你想进行什么变更？描述你想构建或修复的内容。"

   从他们的描述中，得出一个 kebab-case 名称（例如，"增加用户认证" → `add-user-auth`）。

   **重要**：在不了解用户想要构建什么之前，不要继续。

2. **创建变更目录**
   ```bash
   openspec new change "<name>"
   ```
   这将在 `openspec/changes/<name>/` 下创建一个脚手架变更。

3. **获取产物构建顺序**
   ```bash
   openspec status --change "<name>" --json
   ```
   解析 JSON 以获取：
   - `applyRequires`：实施前所需的产物 ID 数组（例如，`["tasks"]`）
   - `artifacts`：所有产物及其状态和依赖关系的列表

4. **按顺序创建产物直到准备好实施**

   使用 **TodoWrite 工具** 跟踪产物的进度。

   按依赖顺序循环产物（没有待处理依赖的产物优先）：

   a. **对于每个 `ready`（依赖已满足）的产物**：
      - 获取指令：
        ```bash
        openspec instructions <artifact-id> --change "<name>" --json
        ```
      - 指令 JSON 包括：
        - `context`：项目背景（给你的约束 - 不要包含在输出中）
        - `rules`：产物特定规则（给你的约束 - 不要包含在输出中）
        - `template`：用于输出文件的结构
        - `instruction`：此产物类型的 Schema 特定指导
        - `outputPath`：写入产物的位置
        - `dependencies`：已完成的产物，用于阅读上下文
      - 阅读任何已完成的依赖文件以获取上下文
      - 使用 `template` 作为结构创建产物文件
      - 应用 `context` 和 `rules` 作为约束 - 但不要将它们复制到文件中
      - 显示简要进度："✓ Created <artifact-id>"

   b. **继续直到所有 `applyRequires` 产物都完成**
      - 创建每个产物后，重新运行 `openspec status --change "<name>" --json`
      - 检查 `applyRequires` 中的每个产物 ID 是否在产物数组中具有 `status: "done"`
      - 当所有 `applyRequires` 产物都完成时停止

   c. **如果产物需要用户输入**（上下文不清楚）：
      - 使用 **AskUserQuestion 工具** 澄清
      - 然后继续创建

5. **显示最终状态**
   ```bash
   openspec status --change "<name>"
   ```

**输出**

完成所有产物后，总结：
- 变更名称和位置
- 已创建产物的列表及简要描述
- 准备就绪："所有产物已创建！准备实施。"
- 提示："运行 `/opsx:apply` 或让我实施以开始处理任务。"

**产物创建指南**

- 遵循每个产物类型的 `openspec instructions` 中的 `instruction` 字段
- Schema 定义了每个产物应包含的内容 - 遵循它
- 在创建新产物之前阅读依赖产物以获取上下文
- 使用 `template` 作为起点，根据上下文填充
- **重要**：`context` 和 `rules` 是给你的约束，不是文件内容
  - 不要将 `<context>`, `<rules>`, `<project_context>` 块复制到产物中
  - 这些指导你写什么，但绝不应出现在输出中

**护栏**
- 创建实施所需的所有产物（由 Schema 的 `apply.requires` 定义）
- 在创建新产物之前始终阅读依赖产物
- 如果上下文严重不清楚，询问用户 - 但倾向于做出合理的决定以保持势头
- 如果具有该名称的变更已存在，询问用户是否要继续该变更或创建一个新的
- 在继续下一个之前，验证每个产物文件在写入后是否存在
