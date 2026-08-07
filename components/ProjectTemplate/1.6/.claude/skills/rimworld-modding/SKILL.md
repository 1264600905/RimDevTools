---
name: rimworld-modding
description: Author and revise RimWorld 1.6 mod content in this initialized project, including XML Defs, PatchOperations, C# components/settings, Harmony patches, textures, sounds, localization, compatibility, debugging, and Workshop preparation. Use when implementing gameplay content or selecting a verified RimWorld authoring pattern; pair with the engineer and tool-specific skills for execution and validation.
---

# RimWorld Modding

Work inside the initialized project. Do not create a second test mod directly under the live RimWorld `Mods` directory.

Use these references as needed:

- [references/project-and-evidence.md](references/project-and-evidence.md): project layout, naming, and evidence selection.
- [references/xml-defs-and-patching.md](references/xml-defs-and-patching.md): Def authoring, inheritance, conditions, and PatchOperations.
- [references/csharp-and-harmony.md](references/csharp-and-harmony.md): settings, persistence, components, extensions, and patches.
- [references/assets-and-localization.md](references/assets-and-localization.md): textures, sounds, bundles, and translations.
- [references/workflows.md](references/workflows.md): items, buildings, patches, batches, and formalization.
- [references/debugging-and-release.md](references/debugging-and-release.md): diagnosis, compatibility, and Workshop readiness.

## Decide From Evidence

1. Inspect current project files and existing naming/conventions.
2. Use RimSearcher for concrete runtime-merged Defs and field values.
3. Use installed Core XML only for abstract parents or source constructs absent from the runtime database.
4. Use DecompilerServer first for game/mod C# APIs, overloads, method bodies, IL, and Harmony targets.
5. Use `RimDevTools ref pull` only as a fallback when a target assembly cannot be directly located/loaded or a durable reference copy is explicitly needed.

Templates under `assets/templates` are syntactically valid starting points, not proof that every field suits a new feature. Copy the closest template, rename all `Template...` identifiers, then verify the selected parent, fields, values, and behavior against RimWorld 1.6 evidence.

## Choose XML Or C#

- Use XML Defs for data-driven content.
- Use PatchOperations for changing existing Def data without replacing source files.
- Use C# when behavior, state, UI, ticking, or integration cannot be expressed safely in XML.
- Use Harmony only when no supported extension/component/event path fits. Patch the narrowest stable method and verify its exact signature/call path.

Prefer a `DefModExtension`, `ThingComp`, `GameComponent`, `MapComponent`, or normal `Mod` settings surface over broad Harmony interception when the design allows it.

## Implement Coherently

Keep these identities aligned:

- `About/About.xml` package ID and dependencies.
- `mod_config.jsonc` mod name, project paths, sync items, logs, and shader settings.
- C# namespace/assembly and Harmony ID.
- Def name prefix and translation keys.
- `LoadFolders.xml` and the actual `1.6` content paths.

Use a unique project prefix for Defs, keys, Harmony IDs, and custom types. Do not silently change package IDs or public Def names after saves may depend on them.

## Validate The Result

1. Parse all changed XML.
2. Build changed C#.
3. Verify APIs and Harmony targets with DecompilerServer.
4. Inspect effective Defs with RimSearcher when available.
5. Run the engineer verification loop, beginning with `deploy --dry-run` and the narrowest suitable game test.
6. Update the About description with `about-write-description` only after behavior is verified.

Do not advertise or publish unverified behavior. Do not add mandatory AI-generation labels unless the user requests them. Preserve attribution and license obligations for reused material.
