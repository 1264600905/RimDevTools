# Tool Priority

## Mandatory Database Gate

Run these commands before any feature implementation or diagnosis:

```powershell
.\.tools\RimDevTools.exe data status
# Run only when status recommends it:
.\.tools\RimDevTools.exe data refresh
.\.tools\RimDevTools.exe data status
```

The database is ready only when the final status reports `refreshRecommended: false`, no `reasonCodes`, `exists: true`, `canOpen: true`, `integrity: "ok"`, and `versionMatches: true`. Its metadata must also match the configured game version, current project package ID, and requested mod-set hash. Do not start code or XML changes while this gate is failing.

Run `doctor` before the gate when environment or recovery state is uncertain. A refresh launches RimWorld and mutates the project database, so obtain authorization when that operation is not already implied by the requested development workflow.

## Evidence Order

1. Pass the database gate.
2. Query effective runtime Defs with RimSearcher.
3. Inspect editable project source when behavior belongs to this project.
4. Use DecompilerServer when C# or binary behavior still needs proof.
5. Use raw installed XML only for source-only constructs omitted from runtime exports.
6. Use `ref pull` only as the final fallback described below.

## C# And Binary Behavior

Preferred sequence:

1. Use the RimSearcher evidence already established after the database gate to identify relevant Def classes and values.
2. Inspect editable project source when the behavior belongs to this project.
3. Load the real target assembly directly with DecompilerServer.
4. Verify context, canonical member ID, signature, source/IL, and call path.
5. Pull a reference mod only if direct assembly access is unavailable or insufficient.

Common direct assembly locations:

- Game: `<rimworld_dir>/RimWorldWin64_Data/Managed/Assembly-CSharp.dll`
- Current project output: `1.6/Assemblies/*.dll`
- Local mod: `<mod>/1.6/Assemblies/*.dll` or its `LoadFolders.xml`-selected folder
- Workshop/local Mods roots from `mod_config.jsonc`

`ref pull` is not a decompiler prerequisite. It copies a local mod into `参考文档`, optionally resolves hard dependencies, and produces durable ILSpy output. That mutation and duplication are unnecessary for ordinary API inspection.

## Def Data

Use RimSearcher for effective runtime Defs:

- `search` to discover.
- `get --field` for one value.
- `get --brief` for C# bridge types.
- `fields` for structure.
- `find` and `values` for reverse lookup/enumeration.

The database gate is unconditional for development work, not merely a check when freshness seems relevant. Refresh only when status recommends it and launching the game is authorized.

Runtime exports omit abstract Defs. Inspect actual installed source XML with a structured parser for `Abstract="true"` parents and inheritance definitions, then verify the resulting concrete Def through RimSearcher.

## Project Operations

- Health: `RimDevTools.exe doctor`
- Deployment plan: `RimDevTools.exe deploy --dry-run`
- Deployment: `RimDevTools.exe deploy`
- Tests: `RimDevTools.exe test run`
- Database: `RimDevTools.exe data status|refresh`
- Reference fallback: `RimDevTools.exe ref search|pull|deps|related`

Do not rely on legacy `deploy.ps1`, `run_test.py`, or `pull_ref.py`; initialized release projects are contracted around the CLI executables.
