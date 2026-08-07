---
name: rimdevtools
description: Operate the project-local RimDevTools CLI for RimWorld 1.6 mod configuration, health checks, builds, atomic deployment, reference-mod discovery and decompilation, LogSync-backed quick/manual/auto/XML tests, and RimSearcher database refresh/status. Use when working with mod_config.jsonc, .tools/RimDevTools.exe, deployment, test runs, reference mods, or project database health.
---

# RimDevTools

Use the project-local Windows CLI from the mod root:

```powershell
.\.tools\RimDevTools.exe <command> [options]
```

Treat `mod_config.jsonc` as the project contract. Run commands from the directory containing it, or from its direct child. Read [references/commands-and-config.md](references/commands-and-config.md) before changing configuration or using a command whose flags, mutation behavior, or error contract matter.

## Select The Workflow

1. Use `doctor` first for setup failures, unclear state, or capability questions. It is read-only.
2. Use `deploy --dry-run` to inspect build and synchronization plans. Run mutating `deploy` only when the user asked to deploy or the requested workflow explicitly requires it.
3. Use `ref search`, `ref deps`, or `ref related` for read-only reference-mod discovery. Use `ref pull` only when copying/decompiling reference mods is in scope.
4. Use `test run --mode quick` for startup and runtime-ready validation; use `manual` for user-observed play; use `auto` or `xml` for LogSync AutoTest cases.
5. Use `data status` before every feature-development workflow and for database diagnosis. Use `data refresh` only when status recommends it and a current runtime-merged Def database is required.
6. Use `test settings` only when persistent LogSync settings are desired. Normal test runs apply and restore settings transactionally.

## Diagnose

Run:

```powershell
.\.tools\RimDevTools.exe doctor
.\.tools\RimDevTools.exe --version
```

Read the final JSON object on stdout. Treat stderr as progress or diagnostics. Distinguish unavailable, missing, invalid, disabled, and busy capabilities rather than collapsing them into a generic failure. Fix the narrowest failing prerequisite, then rerun `doctor`.

## Deploy

1. Validate `mod_name`, `rimworld_dir`, `csproj_paths`, and `sync_items`.
2. Run `deploy --dry-run` and inspect the immutable plan.
3. Confirm all expected assemblies and content roots are included and protected files remain protected.
4. Run `deploy` only after the plan is correct.
5. Add `--sync-source` only when source distribution is intentional.

Deployment builds configured projects before replacing the target and uses atomic staging. Do not manually copy partial output into the live mod directory to work around a failed deployment. If shader building is enabled, treat a shader failure as a deployment blocker.

## Test

Choose the smallest suitable mode:

- `quick`: automated launch and runtime-ready signal; default for smoke tests.
- `manual`: visible interactive session; defaults to the current desktop.
- `auto`: executes AutoTest cases and validates structured results.
- `xml`: executes XML-driven AutoTest cases and rejects failed result JSON.

Use `--deploy` when the test must exercise current source changes. `--sync-source` requires `--deploy`. Quick, auto, XML, and database refresh default to an isolated Windows desktop; manual does not. Override only with the explicit isolation flags.

Never edit live RimWorld `ModsConfig.xml`, LogSync settings, transaction manifests, or request/result files during a run. RimDevTools owns their transactional restoration. On failure, report the stable error code and preserve recovery artifacts.

## References And Data

Use `ref pull` to copy a local/Workshop mod into `参考文档` and decompile its assemblies with packaged ILSpy. Resolve ambiguity with an exact package ID, name, folder, or absolute path. Use `--deps` for a complete hard-dependency closure and `--force` only after reviewing replacement targets.

Use `data refresh` to launch RimWorld with the DataMod and produce a validated `defs.db`. Always use `data status` before feature-development RimSearcher work; for other queries, use it whenever freshness is uncertain. A successful refresh requires matching request, result, log signal, database metadata, component version, and mod hashes.

## Guardrails

- Do not infer success from process exit alone; parse the final JSON and required artifacts.
- Do not retry `ENVIRONMENT_BUSY`; identify or wait for the owning operation.
- Do not delete `.rimdev` transactions to silence recovery errors.
- Do not deploy with a generic template assembly/mod name after starting real development.
- Do not mutate game or Workshop installations during diagnosis unless the user authorized the mutating workflow.
- Keep conclusions tied to command output, config values, or generated reports.
