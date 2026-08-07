# Authoring Workflows

## Add An Item Or Weapon

1. Define player purpose, acquisition, balance, visuals, and whether behavior is data-only.
2. Find a concrete 1.6 peer through RimSearcher.
3. Copy the nearest item/melee/ranged/apparel template and rename all `Template...` identities.
4. Add only verified fields and references.
5. Add recipe/research/trader/category integration only when required and verified.
6. Add localization and assets.
7. Parse, build if needed, deploy/test, spawn, inspect stats, and exercise acquisition/use.

## Add A Building

1. Define footprint, placement, designation, power, interaction, recipes, research, and visuals.
2. Verify a building of the same functional class.
3. Start from `building.xml`; add comps only for actual behavior.
4. Remember `CompProperties_Power.basePowerConsumption` is positive for consumption and negative for generation in the usual power comp calculation; verify the exact peer before setting it.
5. Test placement in every rotation, power state, breakdown/state transitions, save/load, and interaction jobs.

## Modify Existing Content

Use PatchOperations for Def data and Harmony for code behavior. Identify the exact target from effective data or canonical members, implement the narrowest change, and test with/without optional mods. Avoid positional XPath and guessed overloads.

## Batch Requirements

Parse all requirements first and identify shared naming, research, materials, assets, and compatibility constraints. Implement coherent groups rather than pausing after every trivial item unless user decisions are genuinely missing. Validate each generated file, then run integrated tests for collisions and cross-references.

Use `assets/templates/requirements-template.md` as an input format, not as a mandatory user ceremony.

## Formalize The Template Project

Before real development/deployment, replace `MyCustomMod` identities consistently across About metadata, config, project/solution, namespaces, assembly, translation files/keys, shader bundle names, and assets. Preserve public identifiers once saves or consumers may exist.

After features stabilize:

1. Remove placeholder/demo behavior not part of the product.
2. Write the verified About description with the dedicated skill.
3. Replace provisional assets and validate Preview.png.
4. Run doctor, dry-run deployment, build, quick test, feature tests, and save/load coverage.
5. Review the deployed file set for source, caches, reference docs, and development-only files.

Do not maintain a separate `(Test)` package ID unless the user explicitly wants parallel installations; the deployment/test tooling already provides isolation and transactional test configuration.
