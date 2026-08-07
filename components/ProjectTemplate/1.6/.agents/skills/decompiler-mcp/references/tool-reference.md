# DecompilerServer Tool Reference

## Contents

- Context management
- Discovery and stable IDs
- Source and IL
- Relationships and literals
- Comparison
- Parameter rules and pitfalls

## Context Management

### `load_assembly`

Load one assembly by exact `assemblyPath`, or use Unity-friendly `gameDir` plus `assemblyFile`. Always provide a descriptive `contextAlias` when more than one assembly may be used.

```text
load_assembly({
  "assemblyPath": "D:/Mods/Example/1.6/Assemblies/Example.dll",
  "contextAlias": "example"
})

load_assembly({
  "gameDir": "D:/Steam/steamapps/common/RimWorld",
  "assemblyFile": "Assembly-CSharp.dll",
  "contextAlias": "rw16"
})
```

### `list_contexts`, `status`, `get_server_stats`

- `list_contexts`: enumerate loaded items and registered aliases.
- `status`: cheap overview of the current/default alias.
- `get_server_stats({ "contextAlias": "rw16" })`: cache, index, and performance diagnostics for one context.

Registered aliases may load lazily when addressed. Verify paths/MVIDs when two builds share assembly names.

## Discovery And Stable IDs

### `search_symbols` and `search_types`

Pass the search fragment as `query`, optional `limit`, and `contextAlias` when routing is ambiguous.

```text
search_symbols({ "query": "Pawn.Kill", "limit": 20, "contextAlias": "rw16" })
search_types({ "query": "ThingComp", "limit": 20, "contextAlias": "rw16" })
```

Search results return canonical IDs. A type ID conventionally carries a `:T` suffix, but always use the returned value rather than synthesizing it.

### `resolve_member_id`

Use this when starting from a fully qualified or XML-doc-like guess such as `Namespace.Type.Member` or `M:Namespace.Type.Member`. Resolve before decompiling or relationship queries.

### `get_members_of_type` and `list_members`

Pass the returned type ID as `typeId`. Request signatures first to disambiguate constructors, overloads, accessors, nested types, and generic members.

## Source And IL

### `get_decompiled_source`

Pass `memberId`, including for a type target. Non-type members return focused snippets. Verify the displayed declaring type/signature against the selected result.

### `plan_chunking`, `get_source_slice`

Use for large types or generated code. Plan chunks first, then fetch only the relevant source region. Avoid repeated whole-type retrieval.

### `get_il`

Use the canonical `memberId`. IL is authoritative when:

- a static constructor (`.cctor`) decompiles empty;
- compiler-generated state machines obscure behavior;
- exact opcodes/offsets matter for transpilers;
- source reports no body or suspicious simplification;
- constants or initialization order must be proven.

## Relationships And Literals

### `find_callers`

Pass a canonical method ID as `methodId`. Use to establish who invokes a target and whether it is on the gameplay path.

### `find_callees`

Pass `methodId`. Results expose callee-focused fields such as `targetMemberId`, symbol, opcode, IL offset, and resolution. Treat explicit external/unresolved calls honestly.

### `find_usages`

Pass a canonical `memberId`. An empty result may reflect indexing limitations, accessor indirection, reflection, generic dispatch, or missing IL; it is not conclusive by itself.

### `search_string_literals`

Pass text/regex-like input using `pattern` as defined by the visible tool schema. Use it for log prefixes, exception messages, UI labels, package IDs, Def names, and reflection strings.

## Comparison

### `compare_contexts`

Compare inventories between aliases:

```text
compare_contexts({
  "leftContextAlias": "rw15",
  "rightContextAlias": "rw16",
  "namespaceFilter": "Verse",
  "deep": true
})
```

Use shallow comparison for a quick inventory and deep comparison when member-level changes matter.

### `compare_symbols`

Compare one type or member:

```text
compare_symbols({
  "leftContextAlias": "rw15",
  "rightContextAlias": "rw16",
  "symbol": "Verse.Pawn:Kill",
  "symbolKind": "method",
  "compareMode": "body"
})
```

Separate structural signature changes from body-only changes. Confirm renamed/moved members by searching both contexts rather than interpreting removal as deletion automatically.

## Parameter Rules And Pitfalls

- Decompilation/member queries generally take `memberId`; type-member enumeration takes `typeId`.
- `find_callers` and `find_callees` take `methodId`.
- Search tools take `query`; string-literal search takes `pattern`.
- Tool schemas are authoritative. Unknown guessed parameters may be ignored, creating a false impression that a filter applied.
- A guessed member ID can resolve unexpectedly; only use IDs returned by tools and verify signature/output.
- Keep aliases distinct for vanilla, DLC/mod, current/reference, and version comparisons.
- Dependency failures often improve when loading through `gameDir` rather than a bare assembly path.
- Decompiled code can differ cosmetically from original C# while preserving IL behavior.
- Reflection, serialization, XML-driven type names, Harmony patch discovery, and virtual dispatch may not appear as direct usages.

## Evidence Checklist

Before reporting a binary behavior conclusion, capture:

1. Context alias and assembly path/identity.
2. Canonical type/member ID and signature.
3. Relevant source or IL excerpt/behavior.
4. Caller/callee path when reachability matters.
5. Runtime Def values when behavior is data-driven.
6. Comparison aliases and mode when discussing version differences.
