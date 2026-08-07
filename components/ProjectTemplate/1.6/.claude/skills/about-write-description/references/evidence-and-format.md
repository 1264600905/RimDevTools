# Evidence And Format Reference

## Evidence Priority

Use current implementation evidence in this order:

1. `About/About.xml` for identity, supported versions, dependencies, and declared load relationships.
2. `1.6/Defs` and `1.6/Patches` for shipped content and data changes.
3. `source` for settings and player-visible behavior.
4. `Languages` for established player-facing terminology.
5. RimSearcher for effective runtime Def values after inheritance and patches.
6. DecompilerServer for behavior implemented in compiled game or mod assemblies.
7. Git history and `策划案` only as leads that must be reverified.

Absence matters. Do not say an item is craftable without a recipe or inherited recipe path, research-gated without a verified prerequisite, configurable without a visible setting, or compatible with a mod without a patch/dependency/code path.

## Language Rules

- English section: English prose and English title.
- Separator: one line containing `------`.
- Chinese section: Chinese prose and Chinese title.
- Keep proper names unchanged when translation would make identity ambiguous.
- Keep both sections semantically equivalent; do not advertise extra features in one language.

## BBCode Rules

Use supported, simple Workshop constructs: `[h1]`, `[h2]`, `[b]`, `[i]`, `[list]`, `[*]`, `[url]`, and `[img]` only with a verified URL. Close every paired tag. Avoid nested structures that are hard to maintain.

The content lives inside XML. XML escaping and BBCode are separate layers:

- Write `&amp;` when a literal ampersand is serialized as XML text.
- Never insert raw pseudo-XML placeholders.
- Let a structured XML serializer handle escaping where possible.
- Parse the result after saving.

## Claim Record

For review, retain a compact internal mapping:

| Claim | Evidence |
|---|---|
| Supported RimWorld version | `About/About.xml` |
| Player setting | setting class plus translated label |
| Item/building behavior | Def field plus implementation member if behavioral |
| Dependency | `modDependencies`, `loadAfter`, or guarded patch/code |
| Compatibility | explicit patch, conditional load, or tested integration |

Do not include this technical mapping in the player-facing description.
