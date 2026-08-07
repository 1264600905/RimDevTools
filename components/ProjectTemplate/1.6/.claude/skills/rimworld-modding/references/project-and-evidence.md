# Project And Evidence

## Project Layout

```text
About/                         identity, dependencies, Preview.png
1.6/Defs/                     concrete XML definitions
1.6/Patches/                  PatchOperations
1.6/Assemblies/               built mod DLLs
Languages/<Language>/Keyed/   translated keys
source/<Assembly>/            editable C# project
AssetBundles/                 built shader/resource bundles
UnityProjects/                bundle build source project
LoadFolders.xml               RimWorld version/load selection
mod_config.jsonc              RimDevTools project contract
```

Use `LoadFolders.xml` as the authority for which folders RimWorld loads. Use `sync_items` as the authority for what deployment copies. When adding a new root, update both only when required.

## Naming

- Package ID: stable, lowercase publisher/mod identity.
- Def names: stable unique prefix plus descriptive PascalCase segments.
- Translation keys: unique project prefix.
- C# namespace and assembly: match the project identity.
- Harmony ID: package-ID style and globally unique.
- Files: group by content type; do not encode mutable display labels into public identifiers.

## Evidence Matrix

| Question | Primary evidence |
|---|---|
| Effective concrete Def value | RimSearcher |
| Def origin/mod and reverse references | RimSearcher |
| Abstract `ParentName` body | Installed Core/DLC XML parsed structurally |
| Game/mod API signature and behavior | DecompilerServer on real assembly |
| Current project behavior | Editable source plus tests |
| Local reference mod code | DecompilerServer direct load first |
| Durable copied reference/dependencies | RimDevTools `ref pull` fallback |
| Build/deploy/test health | RimDevTools JSON and reports |

Runtime Def exports omit abstract definitions. Do not misinterpret an absent abstract parent as a missing Def. Conversely, source XML can differ from the effective active-mod result; do not answer runtime-value questions from source XML alone.

## Dependencies And Conditions

Declare a required mod in `modDependencies` and use load ordering only when technically necessary. Use `MayRequire`, `MayRequireAnyOf`, versioned dependencies, or conditional folders for optional integrations. Verify package IDs from actual About metadata rather than display names.

Keep compatibility patches isolated and make their ownership obvious. A claimed compatibility relationship requires an explicit conditional data/code path or a recorded test.
