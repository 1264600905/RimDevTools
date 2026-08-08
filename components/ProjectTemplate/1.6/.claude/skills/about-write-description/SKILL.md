---
name: about-write-description
description: Write or revise only the About/About.xml description for a RimWorld 1.6 mod using verified project behavior. Use for Steam Workshop BBCode descriptions, bilingual English/Chinese feature summaries, compatibility statements, and player-facing documentation grounded in About metadata, Defs, patches, languages, settings, and C# implementation.
---

# About Description Writer

Update only the `<description>` element in `About/About.xml`. Preserve every other element, attribute, comment, ordering choice, and encoding unless the user explicitly expands the task.

Read [references/evidence-and-format.md](references/evidence-and-format.md) before writing. Treat the project as the source of truth; do not copy a feature list from planning documents without implementation evidence.

## Gather Evidence

1. Parse `About/About.xml` and record `name`, `packageId`, `supportedVersions`, dependencies, load ordering, URL, and the current description.
2. Read `mod_config.jsonc`, `LoadFolders.xml`, `1.6/Defs`, `1.6/Patches`, `Languages`, and `source` as applicable.
3. Identify player-visible settings, commands, gizmos, incidents, items, buildings, mechanics, restrictions, costs, and feedback.
4. Use RimSage when an advertised statement depends on an official Def, term, API, or behavior.
5. For third-party compatibility claims, inspect existing `参考文档` or use `ref search/ref pull` when acquiring evidence is in scope.
6. Treat git history and planning documents only as discovery hints. Confirm every advertised feature in current files or tool evidence.

Ask for a missing Chinese or English product name only when the requested bilingual title cannot be derived safely. Do not invent dependencies, compatibility, recipes, research, controls, settings, or credits.

## Write For Players

For each feature, communicate:

1. The player need or situation.
2. How the player accesses or triggers it.
3. The visible result, cost, restriction, or tradeoff.

Do not expose implementation names such as classes, fields, `defName`, XPath, Harmony methods, or config keys. Avoid marketing claims, unsupported superlatives, emojis, and generic filler.

## Produce Bilingual BBCode

Write the English section first, then a line containing only `------`, then the Chinese section. Keep each section in its own language except established product, DLC, and mod names.

Use this structure in both languages:

```text
[h1][b]Language-specific title[/b][/h1]
[b]Summary[/b]
Short factual summary.

[h2][b]Features[/b][/h2]
[list]
[*]Player-facing feature explanation
[/list]

[b]Compatibility and requirements[/b]
Verified version, DLC, dependency, and compatibility facts only.

[b]Credits[/b]
Verified reused libraries, dependencies, or compatibility targets only.
```

Omit empty sections rather than inventing content. Do not emit an `[img]` block unless the repository contains a verified public image URL intended for Workshop use. A local path or prose placeholder is not a valid Steam image URL.

## Update XML Safely

Use a structured XML API. Replace only the description node value/content and save valid XML. Escape `&`, `<`, and `>` correctly; do not place pseudo-tags such as `<image here>` inside the text. Preserve BBCode brackets as text.

After editing:

1. Parse the saved XML again.
2. Confirm exactly one `<description>` exists.
3. Inspect the diff and verify no other About metadata changed.
4. Confirm each claimed feature has a recorded source.
5. Report the file changed and any intentionally omitted, unverified claims.

Do not dump the entire XML in the final response unless the user asks for it.
