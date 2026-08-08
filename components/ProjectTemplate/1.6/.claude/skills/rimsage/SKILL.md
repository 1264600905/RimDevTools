---
name: rimsage
description: Research official RimWorld and DLC Defs, XML inheritance, C# types, members, signatures, implementations, and Harmony targets through the RimSage MCP. Use when a task needs evidence about official game APIs or behavior, when bridging Defs to implementation classes, or when deciding whether third-party mod research must move to local reference files and RimDevTools ref pull.
---

# RimSage

Use RimSage as the evidence source for official RimWorld and official DLC content. Read [references/tool-reference.md](references/tool-reference.md) before the first tool call in a task.

## Decide Whether RimSage Is Required

Require RimSage when confirming official Defs, XML inheritance, game classes, fields, methods, signatures, implementations, or Harmony targets. Also require it before making a claim about official behavior that depends on source or Def evidence.

Do not require RimSage for changes confined to existing project files, translations, formatting, assets, builds, deployments, tests, or log diagnosis that introduce no new official API or Def assumptions.

Read current project files directly. RimSage cannot establish facts about the current project or third-party mods.

## Establish Availability

When RimSage is required:

1. Check whether the current client exposes an MCP service named `rimsage` and its tools.
2. If absent, inspect the current project's client configuration and validate its syntax and service name.
3. Safely repair an obvious missing entry, wrong service name, or damaged template-default configuration.
4. Retry discovery or one representative call exactly once.
5. If the remote endpoint is unreachable, a configured local process cannot start, the client must restart, or credentials/environment work is needed, stop the RimSage-dependent research and ask the user to restore access.
6. Never present memory, filenames, unrelated local documents, or guesses as RimSage evidence.

Accept a working local MCP service named `rimsage` exactly like the template's remote service. Do not reject it because its transport or endpoint differs. Do not inspect index versions, game patch versions, or freshness metadata.

## Research Official Content

1. Use `search_defs` to find the exact Def and Def type.
2. Use `get_def_details` to inspect raw or merged XML as the question requires.
3. Follow bridge fields such as `thingClass`, `compClass`, `workerClass`, and `jobClass` to implementation types.
4. Use `search_source` to locate types, members, field access, and related XML.
5. Use `read_csharp_symbol` for the focused type or method.
6. Use `read_file` in line ranges when a long type, missing index entry, or surrounding context requires it. Use `list_directory` to discover server paths rather than guessing them.
7. Record exact Def names, types, members, paths, and decisive snippets. Validate the eventual implementation with this project's build and quick/auto/XML or real-game tests.

## Research Third-Party Mods

1. Identify that the subject is a third-party mod rather than official content.
2. Check `参考文档` for an existing local copy.
3. If absent, use `.\.tools\RimDevTools.exe ref search <query>` to disambiguate it.
4. Run `ref pull` only with the options the task needs. Do not add `--deps`, `--all`, or `--force` implicitly.
5. If the target conflicts with an existing reference directory, report it and ask the user whether replacement is intended.
6. Inspect the pulled About metadata, LoadFolders, XML, patches, assemblies, and ILSpy source locally.
7. Use RimSage only for official APIs called by that mod; never claim RimSage inspected the third-party mod itself.
