# XML Defs And Patching

## Author Defs

Use XML for content and configuration. Start from a verified concrete Def of the same role, then determine which values are inherited and which must be supplied.

Before authoring:

1. Find concrete peers with RimSearcher `search` or `list --type`.
2. Inspect one with `get`, `get --field`, and `fields`.
3. Use `get --brief` to find implementing C# types when field semantics are behavioral.
4. Parse installed source XML only to inspect abstract parents and inheritance.
5. Confirm enum/type semantics through DecompilerServer when the serialized contract is unclear.

Use valid XML at every intermediate step. Templates use `Template...` text identifiers deliberately; replace text values, never angle-bracket pseudo-placeholders.

## Common Def Concerns

- `defName` is a save/reference contract; keep it stable.
- `label` and `description` are player-facing and should use established project language strategy.
- `ParentName` merges inherited XML. Verify list merge/replace behavior rather than assuming it.
- `Class` selects a polymorphic C# type. Verify the type exists and matches the node.
- `MayRequire`/`MayRequireAnyOf` prevent loading when optional packages are absent.
- `texPath` is relative to `Textures` and normally omits the extension.
- Def references are case-sensitive identifiers, not labels.

## PatchOperations

Use patches under `1.6/Patches` to modify existing Defs. Prefer an exact, stable XPath and make no-match behavior intentional.

Common operations:

- `PatchOperationAdd`: add child nodes to an existing container.
- `PatchOperationRemove`: remove matched nodes.
- `PatchOperationReplace`: replace matched nodes with supplied values.
- `PatchOperationSequence`: run operations in order, optionally stopping on failure.
- `PatchOperationConditional`: branch on whether an XPath matches.
- `PatchOperationFindMod`: gate compatibility behavior by active mod.

Validate XPath against actual source/effective structure. For list entries, match an identity/value rather than a fragile position whenever possible. Keep each compatibility concern in a focused patch file.

## Verification

Parse patch XML, deploy to a test environment, and check patch-operation errors in the selected log. Then query the effective target through RimSearcher after a current data export. A syntactically valid patch can still no-op because its XPath matched nothing.

Do not directly edit vanilla or another mod's source files.
