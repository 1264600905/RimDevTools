---
name: rimworld-development-engineer
description: Implement, diagnose, and verify RimWorld 1.6 mod changes end to end in this template. Use when a request requires C#/XML/assets work, evidence-backed official API or Def research through RimSage, third-party reference research, build/deploy decisions, Harmony targeting, game tests, log diagnosis, or repeated repair-and-retest cycles.
---

# RimWorld Development Engineer

Own the implementation through verification. Do not stop after editing when a safe, relevant build or test remains available.

Read [references/tool-priority.md](references/tool-priority.md) before researching game/mod behavior. Read [references/verification-loop.md](references/verification-loop.md) before deploying or launching RimWorld.

## Research Before Implementation

1. Read the nearest current-project source, Defs, patches, configuration, and tests first.
2. Decide whether the task needs new facts about official RimWorld/DLC Defs, APIs, source behavior, or Harmony targets.
3. When it does, invoke `rimsage` and follow its availability, one-retry, official research, and failure rules before choosing fields, types, methods, or targets.
4. When the subject is a third-party mod, inspect an existing copy in `参考文档` or use `RimDevTools.exe ref search/ref pull` as documented by `rimsage` and `rimdevtools`.
5. If required RimSage evidence remains unavailable after the allowed repair and retry, stop only the dependent research and ask the user to restore access. Do not replace evidence with guesses.

Current project files remain authoritative for this project's behavior. RimSage is authoritative only for the official content it exposes; pulled XML and ILSpy source are the evidence for third-party mods.

## Implement

1. Complete the applicable research steps above.
2. Read existing conventions at the edit location.
3. Record the relevant Def/API/member evidence before choosing fields, overloads, or Harmony targets.
4. Make the smallest coherent change in `1.6`, `Languages`, `About`, `source`, or approved asset directories.
5. Keep package IDs, namespaces, assembly names, translation keys, Def names, and config paths consistent.
6. Preserve user changes and avoid unrelated refactors.

## Mod Activation Order

Treat `mod_config.jsonc.min_active_mods` as the authoritative default mod list for development and test workflows. Prefer that list over an ad hoc ModList so the project starts with its known working baseline.

When a requested mod must be enabled but is absent from `min_active_mods`:

1. Add its missing dependencies in dependency order immediately before `trigger.logsyncmod`.
2. Add the requested mod after its dependencies and before `trigger.logsyncmod`.
3. Preserve every unrelated entry's order and keep `trigger.logsyncmod` as the insertion anchor.

Before testing, confirm the effective mod list still follows this order. An explicit user-supplied ModList may add further mods, but must retain the configured default mods and their dependency ordering.

For tool details, invoke `rimdevtools` or `rimsage` rather than duplicating their manuals here.

## Verify

Match verification to the change:

- Parse changed XML and validate references/keys.
- Build every changed C# project.
- Run `RimDevTools.exe deploy --dry-run` before an actual deployment.
- Use `RimDevTools.exe test run --deploy --mode quick` for startup/runtime smoke tests.
- Do not run an auto test first: it is slower than quick mode. Fix and rerun quick tests until they pass without current-mod errors, then run `RimDevTools.exe test run --deploy --mode auto` once for the relevant feature case.
- Treat a feature change as fully verified only after that follow-up auto test passes. Do not claim the verification loop is complete on a quick-test pass alone.
- Use XML mode when the feature case requires a declarative LogSync AutoTest specification.
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

- Do not run deployment and game tests concurrently; they share the RimWorld environment lock.
- Do not manually edit live global RimWorld configuration during a RimDevTools transaction.
- Do not delete `.rimdev` recovery state, logs, or user files to force a pass.
- Do not use `git clean`.
- Do not commit unless the user requests it.
- Match code comments and user-facing language to the surrounding project and the user's language.
- Summarize implemented behavior, evidence, and verification results at delivery.
