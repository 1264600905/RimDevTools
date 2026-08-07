---
name: decompiler-mcp
description: Use the project-configured DecompilerServer MCP to inspect real RimWorld, Unity, framework, or mod .NET assemblies. Use for C# API discovery, type/member signatures, decompiled source, IL, constants, callers/callees/usages, string literals, Harmony patch targets, debugging binary behavior, and comparing assembly versions or mod implementations. Prefer this over guessing APIs or relying on stale source mirrors.
---

# DecompilerServer MCP

Use the `decompiler` MCP server configured by the project `.codex/config.toml`. The template launches `.decompilerserver/DecompilerServer.exe` relative to the project root. It requires the .NET 10 runtime.

Use MCP tools, not shell decompilers, for assembly evidence. Read [references/tool-reference.md](references/tool-reference.md) when selecting tool parameters, loading multiple contexts, investigating call relationships, or comparing versions.

## Establish A Trusted Context

1. Call `list_contexts` or `status` before searching.
2. Reuse a loaded/registered alias only after confirming its assembly path and identity.
3. Otherwise call `load_assembly` with either an exact `assemblyPath`, or `gameDir` plus `assemblyFile` for Unity/RimWorld dependency resolution.
4. Assign a short stable `contextAlias`, such as `rw16`, `mod-current`, or `mod-reference`.
5. Verify the loaded context before treating results as evidence.

For RimWorld, obtain `rimworld_dir` from `mod_config.jsonc`; do not guess a Steam path. The primary game assembly is normally under `RimWorldWin64_Data/Managed/Assembly-CSharp.dll`. For a deployed or reference mod, use its actual assembly under the versioned `Assemblies` folder.

## Inspect A Type Or Member

Follow this sequence:

1. Use `search_symbols` for a fragment, or `search_types` for a type-focused query.
2. Use `resolve_member_id` when you have a qualified symbol guess and need a canonical ID.
3. Use `get_members_of_type` or `list_members` in signatures mode before selecting overloads.
4. Copy the exact returned `typeId`, `methodId`, or `memberId`; never construct IDs manually.
5. Use `get_decompiled_source` for a focused type/member. For large types, use `plan_chunking`, `get_source_slice`, or line slices rather than requesting everything repeatedly.
6. Expand only as needed with `find_callers`, `find_callees`, `find_usages`, `search_string_literals`, or `get_il`.
7. Record the context alias, canonical ID, signature, and decisive source/IL behavior in the conclusion.

Once a canonical member ID is returned, follow-up tools can normally route by its embedded assembly identity without repeating the alias.

## Choose Source, IL, Or Relationships

- Use decompiled source for control flow, formulas, field access, and readable intent.
- Use IL for static constructor initialization, compiler-generated behavior, ambiguous decompilation, opcode-level patching, and methods reported with missing/empty source.
- Use callers to identify entry paths into a method.
- Use callees to see what a method invokes and whether targets resolve locally or externally.
- Use usages for broad references to fields, properties, methods, and types, but triangulate empty results.
- Use string-literal search to locate logs, errors, labels, command strings, and indirect registration paths.

## Compare Implementations

1. Load both assemblies under distinct aliases.
2. Call `compare_contexts` for namespace/type/member inventory changes.
3. Call `compare_symbols` for one type or member.
4. Use body comparison for method-level behavioral changes.
5. Decompile both sides when a structural diff does not explain the behavior.
6. Report additions, removals, signature changes, and body/IL changes separately.

## RimWorld Evidence Pipeline

When starting from a Def:

1. Use RimSearcher `get --brief` to obtain `*Class` and `$type` implementation names.
2. Search those exact classes in the trusted `rw16` context.
3. Inspect signatures first, then the methods that read the relevant Def fields.
4. Follow callers/callees until the gameplay entry point and calculation path are established.
5. Verify the code's field reads against RimSearcher values.

When designing a Harmony patch, identify the exact declaring type, member signature, overload, static/instance shape, and return/ref parameters from DecompilerServer. Inspect call sites to confirm the target is on the intended runtime path.

## Handle Failures

- Follow structured `candidates` and `hints`; change the next call instead of repeating a failed guess.
- If an alias is registered but unloaded, address it explicitly to trigger lazy loading.
- If source is unexpectedly empty, verify the assembly has IL and use `get_il`.
- If `find_usages` is empty, search string literals, related accessors, callers/callees, or DefDatabase lookups before concluding there are no references.
- If an assembly fails to load, verify it is managed .NET and load through `gameDir` when dependencies are missing.

## Guardrails

- Do not guess RimWorld APIs, overloads, namespaces, visibility, or field names.
- Do not use IDs from a different context without verifying assembly identity.
- Do not treat decompiler output as the project's editable source.
- Do not claim a call relationship from name similarity alone.
- Do not silently ignore unresolved external calls or missing IL.
- Mark conclusions `[UNVERIFIED]` when the required assembly/context cannot be loaded.
