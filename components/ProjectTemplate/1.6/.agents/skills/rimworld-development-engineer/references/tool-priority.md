# Tool Priority

## Evidence Order

1. Read current project source, Defs, patches, configuration, and tests.
2. Decide whether the task requires new official RimWorld/DLC facts.
3. For official Defs, XML, types, members, implementations, or Harmony targets, invoke `rimsage` and follow its availability and research workflow.
4. For third-party mods, inspect `参考文档`; use `ref search` and `ref pull` when the needed mod is absent.
5. Implement only after the evidence needed for the chosen API, field, Def, or target is recorded.
6. Verify the result through this project's static checks, build, deployment plan, and appropriate game tests.

RimSage is a conditional research prerequisite, not a prerequisite for local edits, builds, deployments, tests, or log diagnosis that introduce no new official assumptions.

## Official Content

Use RimSage to discover exact Defs, inspect raw or merged XML, follow class bridge fields, search official source, and read focused C# symbols. If the service is missing or fails, follow the `rimsage` skill's configuration repair and single retry. Stop dependent research when access still cannot be restored.

Do not add version or freshness checks. Accept a working local service named `rimsage` like the default remote service.

## Current Project

Read editable project files directly. Existing source and tests are authoritative for this project's implemented behavior. Do not use remote evidence to claim a local change exists.

## Third-Party Content

Use the existing local reference first. When pulling is necessary:

```powershell
.\.tools\RimDevTools.exe ref search "<query>"
.\.tools\RimDevTools.exe ref pull "<exact-query>"
```

Choose `--deps`, `--all`, or `--force` only when the task explicitly requires that behavior. Inspect About metadata, LoadFolders, XML, patches, assemblies, and generated ILSpy source after pulling.

## Project Operations

- Health: `RimDevTools.exe doctor`
- Deployment plan: `RimDevTools.exe deploy --dry-run`
- Deployment: `RimDevTools.exe deploy`
- Tests: `RimDevTools.exe test run`
- Reference research: `RimDevTools.exe ref search|pull|deps|related`

The packaged ILSpy executable remains required by `ref pull`.
