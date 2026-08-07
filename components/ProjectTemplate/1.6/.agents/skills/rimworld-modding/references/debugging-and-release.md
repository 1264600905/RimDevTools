# Debugging And Release

## Diagnose

Start with `RimDevTools.exe doctor`, then select the smallest reproducing test. Use structured command error codes and the selected log. Attribute messages by package/assembly, Def name, stack, path, and timing.

Common classes:

- XML parse/type/cross-reference failure: parse file, verify class/Def identity and load ordering.
- Duplicate Def/key: locate every definition and resolve the ownership contract.
- C# load/type failure: inspect build output, framework/dependencies, and XML `Class` values.
- Harmony no-op/conflict: verify canonical method, patch owners/order, and actual call path with DecompilerServer.
- Null/state failure: use the full stack, lifecycle/load state, and minimal reproduction; do not apply indiscriminate null guards.
- Asset failure: verify loaded path, case, bundle, shader/material, and log ownership.

Do not use global text whitelists. Establish whether a warning predates the change or belongs to another component before classifying it as ambient.

## Compatibility Testing

Test the minimal required mod set first, then each declared optional integration, then representative real load orders. Query effective Defs after the tested active-mod set is exported. Inspect both assemblies directly when a binary compatibility boundary matters.

## Release Checklist

- About identity, versions, dependencies, URL, description, and Preview.png are accurate.
- `LoadFolders.xml` selects only intended content.
- Release build succeeds and deployed DLLs exclude game/framework assemblies.
- No development caches, databases, logs, reference copies, editor state, or source are deployed unintentionally.
- Quick and feature tests pass; save/load is covered when relevant.
- Optional integrations are conditional and tested.
- Localization keys are complete for claimed languages.
- Reused code/assets preserve licenses and attribution.
- Workshop claims match verified behavior.

Use the game/Steam publishing UI or the user's established release mechanism. Do not invent a SteamCMD workflow or mutate Workshop state without explicit authorization.
