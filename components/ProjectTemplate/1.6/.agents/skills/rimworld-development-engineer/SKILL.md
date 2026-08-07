---
name: rimworld-development-engineer
description: Implement, diagnose, and verify RimWorld 1.6 mod changes end to end in this template. Use when a request requires actual C#/XML/assets work, evidence-backed API or Def research, build/deploy decisions, Harmony targeting, game tests, log diagnosis, or repeated repair-and-retest cycles across the project-local RimDevTools, RimSearcher, and DecompilerServer tools.
---

# RimWorld Development Engineer

Own the implementation through verification. Do not stop after editing when a safe, relevant build or test remains available.

Read [references/tool-priority.md](references/tool-priority.md) before researching game/mod behavior. Read [references/verification-loop.md](references/verification-loop.md) before deploying or launching RimWorld.

## Use The Evidence Tools In The Right Order

For C# and binary behavior, **use DecompilerServer first**:

1. Locate the actual game or mod assembly from `mod_config.jsonc`, the project, the deployed mod, or a known local mod path.
2. Load it directly with DecompilerServer and verify its context identity.
3. Search canonical types/members, inspect signatures, decompile focused source, then expand to IL/callers/callees/usages as needed.
4. Use `RimDevTools ref pull` only as a fallback when the assembly cannot be located or loaded directly, when dependencies must be collected, or when the task explicitly requires a durable reference copy under `参考文档`.

Never run `ref pull` merely to inspect code that DecompilerServer can load directly. A pulled ILSpy tree is secondary evidence; DecompilerServer against the real assembly is authoritative.

For Def data, use RimSearcher against the runtime-merged database. Use raw installed Core XML only for source constructs absent from the runtime database, especially abstract `ParentName` definitions, and label such evidence as source XML rather than effective runtime data.

## Implement

1. Read the nearest project files and existing conventions.
2. Run `RimDevTools.exe doctor` when project/tool state is uncertain.
3. Establish the relevant Def and/or code evidence before choosing fields, APIs, overloads, or Harmony targets.
4. Make the smallest coherent change in `1.6`, `Languages`, `About`, `source`, or approved asset directories.
5. Keep package IDs, namespaces, assembly names, translation keys, Def names, and config paths consistent.
6. Preserve user changes and avoid unrelated refactors.

For domain patterns and reusable templates, invoke the `rimworld-modding` skill. For tool details, invoke `rimdevtools`, `rimsearcher`, or `decompiler-mcp` rather than duplicating their full manuals here.

## Verify

Match verification to the change:

- Parse changed XML and validate references/keys.
- Build every changed C# project.
- Run `RimDevTools.exe deploy --dry-run` before an actual deployment.
- Use `RimDevTools.exe test run --deploy --mode quick` for startup/runtime smoke tests.
- Use auto/XML modes for feature cases supported by LogSync AutoTest.
- Use manual mode only for genuinely interactive behavior.

Read the final structured JSON result and relevant `.tools/log/精选日志.log` content. Do not infer success solely from process exit or a lack of visible UI errors.

## Repair Loop

When a test fails:

1. Attribute the error to the current mod, another mod, the game, or infrastructure using stack/context evidence.
2. Fix the root cause without removing required behavior merely to silence the test.
3. Repeat the narrowest verification that can disprove the failure, then rerun the end-to-end check.
4. After the same blocking condition survives three evidence-based approaches, report the exact error, evidence, and attempted fixes before expanding scope.

Do not use global warning whitelists. Messages such as numeric parse warnings or translation errors may be harmless in one environment but actionable when emitted by the current mod. Attribute before ignoring.

## Safety And Delivery

- Do not run deployment, tests, or data refresh concurrently; they share the RimWorld environment lock.
- Do not manually edit live global RimWorld configuration during a RimDevTools transaction.
- Do not delete `.rimdev` recovery state, databases, logs, or user files to force a pass.
- Do not use `git clean`.
- Do not commit unless the user requests it.
- Match code comments and user-facing language to the surrounding project and the user's language.
- Summarize implemented behavior, evidence, and verification results at delivery.
