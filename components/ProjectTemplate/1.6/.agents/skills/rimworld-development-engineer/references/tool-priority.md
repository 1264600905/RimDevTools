# Tool Priority

## C# And Binary Behavior

Preferred sequence:

1. Inspect editable project source when the behavior belongs to this project.
2. Load the real target assembly directly with DecompilerServer.
3. Verify context, canonical member ID, signature, source/IL, and call path.
4. Pull a reference mod only if direct assembly access is unavailable or insufficient.

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

Check `RimDevTools.exe data status` first when freshness matters. Refresh only when missing/stale/invalid and launching the game is authorized.

Runtime exports omit abstract Defs. Inspect actual installed source XML with a structured parser for `Abstract="true"` parents and inheritance definitions, then verify the resulting concrete Def through RimSearcher.

## Project Operations

- Health: `RimDevTools.exe doctor`
- Deployment plan: `RimDevTools.exe deploy --dry-run`
- Deployment: `RimDevTools.exe deploy`
- Tests: `RimDevTools.exe test run`
- Database: `RimDevTools.exe data status|refresh`
- Reference fallback: `RimDevTools.exe ref search|pull|deps|related`

Do not rely on legacy `deploy.ps1`, `run_test.py`, or `pull_ref.py`; initialized release projects are contracted around the CLI executables.
