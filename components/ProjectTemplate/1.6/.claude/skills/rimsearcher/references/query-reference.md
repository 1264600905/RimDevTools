# RimSearcher Query Reference

## Contents

- Database resolution
- Search and browse
- Exact Def lookup
- Field and reverse lookup
- Statistics and maintenance
- Query semantics and troubleshooting

## Database Resolution

The global `--db <path>` option may appear anywhere and is removed before command parsing. Resolution order is:

1. Explicit `--db` path, relative to current working directory when not absolute.
2. `rimsearcher_db_path` from `mod_config.jsonc` in current directory or direct parent.
3. Newest `*.db` under the executable's `db` directory.
4. `defs.db` beside the executable.

The template config uses `.tools\db\defs.db`.

## Search And Browse

```text
rimsearcher search <keyword> [--type <T>] [--mod <M>] [--limit <N>]
                   [--count] [--name-only]
rimsearcher list [--type <T>] [--mod <M>] [--limit <N>]
                 [--offset <N>] [--total]
```

`search` uses SQLite FTS5 plus CJK bigram expansion. It supports prefix `*`, `OR`, `NOT`, and quoted phrases. Whole tokens matter: camelCase is not split. A bare word also checks name/label substrings. Use `--name-only` to exclude full serialized data and `--count` when only cardinality is needed.

`list` provides stable pagination by Def type/mod. An empty page exits `0`. Use `--total` to return both count and results.

## Exact Def Lookup

```text
rimsearcher get <defName> [--type <T>] [--brief | --field <path>]
```

`defName` is exact. Supply `--type` when the same name exists under multiple Def types.

- Default: return full runtime JSON.
- `--brief`: return identity/origin plus `classes[]`, collected recursively from string-valued fields ending in `Class` and polymorphic `$type` markers.
- `--field <path>`: return one JSON value. Paths use `a.b[0].c`; quote paths containing `$type` in shells.

`--brief` and `--field` are mutually exclusive. Prefer them over full output to avoid host truncation.

## Field And Reverse Lookup

```text
rimsearcher fields <defName> --type <T> [--limit <N>] [--filter <glob>]
rimsearcher find <fieldPath> <value> [--type <T>] [--mod <M>] [--limit <N>]
rimsearcher values <fieldPath> [--type <T>] [--limit <N>]
```

`fields` returns flattened runtime field paths and values. Both Def name and type are required. Filters are glob-like; list indexes are literal. Examples:

```text
fields Gun_BoltAction --type ThingDef --filter "comps*.*"
fields SomeRaid --type RaidStrategyDef --filter "*workerClass"
```

`find` performs exact, case-sensitive matching against a literal path suffix and includes list indexes. Values must be complete; use lowercase JSON booleans and literal `null` for empty-field searches. Examples:

```text
find compClass CompRottable --type ThingDef
find pawnGroupMakers[0].kindDef OutlanderRough
find workerClass RimWorld.SomeWorker --mod Core
```

If positions differ between Defs, query adjacent indexes or use FTS search on the value and verify results. A path like `comps[0].compClass` does not cover `comps[1].compClass`.

`values` enumerates distinct values and counts. A bare suffix such as `compClass` aggregates across nested list depths and is useful for discovering the valid vocabulary before `find`.

## Statistics And Maintenance

```text
rimsearcher types
rimsearcher mods
rimsearcher check update
```

`types` returns all runtime Def types and counts. `mods` returns all contributing mods and Def counts. `check update` is advisory and may require network access; a failure must not invalidate local query results.

## Query Semantics And Troubleshooting

- stdout contains JSON/query data; stderr contains errors and actionable hints.
- Exit `2` means no result or ambiguity. Search/find still emit `[]` on stdout when empty.
- Exit `1` means command, SQLite, path, or schema failure.
- `no such table` generally means the database was exported by an incompatible DataMod; refresh it.
- SQLite corruption errors require a new export, not repeated queries.
- FTS syntax errors should be simplified or replaced with exact `find`/`values` operations.
- Unknown `--type` values are rejected; use `types` to discover canonical values.
- `--mod` filters by exported mod identity; use `mods` before assuming a display name.
- Runtime output reflects active mod ordering and patches at export time. Check `data status` when the active set changed.

## Evidence Record

For a mechanic analysis, retain enough evidence to reproduce the conclusion:

1. Database status/export metadata.
2. Search query and selected Def identity.
3. Exact field paths and values.
4. `classes[]` bridge or referenced Def chain.
5. Decompiler context alias and member IDs.
6. Formula/branch cross-check between code and data.
