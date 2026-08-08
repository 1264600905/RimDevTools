# RimSage Tool Reference

RimSage exposes six read-only research tools. Treat service paths returned by search and listing calls as authoritative.

## `search_defs`

Search official RimWorld and DLC Defs by `defName` or label. Supply the search text and use `defType` when the result set must be narrowed. Use this for discovery; confirm the selected result with `get_def_details`.

## `get_def_details`

Read a Def by exact `defName`. Request raw XML when source structure, inheritance, or declared values matter. Request merged XML when resolved inherited values matter. Do not substitute a label or partial name for the exact identifier.

## `search_source`

Run a regular-expression search over indexed official Def/XML and C# source. Use a glob when the file family is known. Search canonical type/member names and XML bridge fields, then open decisive results rather than treating snippets alone as full context.

## `read_file`

Read a server-side source file by the path returned from RimSage. Use start/end line ranges for focused context and split long files into bounded reads.

## `list_directory`

List a server-side Def or source directory. Use it to discover paths when search results are incomplete or the directory layout is unknown.

## `read_csharp_symbol`

Read an official C# type by its exact type name. Supply an exact member name when only a method or member is needed. Prefer this focused view before reading an entire source file.

## Evidence Rules

- Cross-check a discovered Def with details before relying on it.
- Follow XML class fields into C# symbols explicitly.
- Distinguish raw declarations from merged values.
- Cite the exact tool, query/identifier, returned path, and symbol in working notes.
- Do not add index-version or freshness gates.
- Treat remote and user-configured local services as capability-equivalent.
