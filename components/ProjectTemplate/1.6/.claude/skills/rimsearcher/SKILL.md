---
name: rimsearcher
description: Query and analyze the runtime-merged RimWorld 1.6 Def database with the project-local RimSearcher CLI. Use for Def discovery, exact field extraction, field-tree inspection, reverse lookups, value/type/mod statistics, mod-origin questions, XML/Def mechanics, and building an evidence chain from a Def to its implementing C# types. Pair with DecompilerServer when behavior depends on game or mod code.
---

# RimSearcher

Use RimSearcher as the authority for runtime Def data. Raw XML is only source input and may differ after inheritance, patches, active-mod ordering, and runtime export.

Invoke it from the project root:

```powershell
.\.tools\rimsearcher.exe <command> [options]
```

The CLI reads `rimsearcher_db_path` from `mod_config.jsonc`. An explicit global `--db <path>` overrides it. Read [references/query-reference.md](references/query-reference.md) for complete syntax and query semantics.

## Establish Database State

When database freshness or validity matters, run:

```powershell
.\.tools\RimDevTools.exe data status
```

Refresh with `RimDevTools.exe data refresh` only when status reports missing, invalid, version-mismatched, or stale mod hashes and the user authorizes launching RimWorld. Optionally run `rimsearcher check update`; report an available update but do not interrupt research if the check fails.

## Choose The Shortest Query

- Known exact `defName`: use `get`.
- Need one nested value: use `get --field`.
- Need C# bridge types: use `get --brief`.
- Need to discover candidates: use `search`.
- Need to browse by type or mod: use `list`.
- Need a Def's field schema: use `fields`.
- Need exact reverse lookup at a known path: use `find`.
- Need distinct values for a path: use `values`.
- Need available Def types or contributing mods: use `types` or `mods`.

## Full Analysis Pipeline

1. Search for the target using the smallest meaningful keyword and optional `--type`/`--mod` filters.
2. Resolve a unique `defName`; ask the user only when multiple candidates represent materially different targets.
3. Run `get <defName> --type <T> --brief` to extract all `*Class` values and polymorphic `$type` markers.
4. If no class bridge exists, inspect `fields` or full `get` for referenced Defs and worker/extension objects.
5. Invoke the DecompilerServer skill for each relevant implementation type. Search, resolve returned IDs, inspect signatures, then decompile only the needed members.
6. Cross-check the code path against the exact Def fields and values. Trace references to other Defs when the formula reads indirectly.
7. Report the Def name/type/mod, field paths/values, implementation members, and how they combine. Mark any unverified inference explicitly.

## Reverse Lookup Pipeline

1. Confirm the literal field path using `fields` on a representative Def.
2. Include list indexes in nested paths, for example `comps[0].compClass`.
3. Run `find <fieldPath> <value>` for an exact, case-sensitive match.
4. If list positions vary, inspect neighboring indexes or use `search <class-name>` and verify each hit.
5. Use `get --brief` and DecompilerServer only when behavior, rather than data membership, is required.

## Interpret Results

- Exit `0`: successful query. An empty `list` page is valid pagination.
- Exit `2`: no result or an ambiguity; read stderr hints and pivot. This is not a database/runtime crash.
- Exit `1`: real error such as invalid syntax, missing/corrupt database, or incompatible schema.
- FTS rank values are negative; more negative means more relevant.
- A bare word also performs def-name/label substring support, but general FTS terms match tokens rather than arbitrary fragments.
- Abstract Defs are not instantiated and therefore do not appear in the runtime database.

## Guardrails

- Do not answer runtime Def questions from local XML when the database is available.
- Do not guess field paths, list positions, Def types, or class names; discover them.
- Do not treat `find` zero hits at one list index as proof of no reference.
- Do not dump large full Def JSON when `--brief`, `--field`, or `fields --filter` answers the question.
- Do not fabricate values or normalize away precision silently. Explain that float tails such as `0.0500000007` represent the binary value of source literal `0.05f` when relevant.
- Follow CLI `Hint:` output and change the query; never repeat an identical failed call without new evidence.
