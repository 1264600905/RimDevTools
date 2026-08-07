# C# And Harmony

## Project Contract

The template targets .NET Framework 4.7.2 and references RimWorld/Unity/Harmony assemblies through project reference paths. Keep game assemblies non-private so they are not copied into the mod output. Build through the configured `.csproj` and do not package game DLLs.

## Preferred Extension Points

Use the least invasive mechanism:

1. `Mod` plus `ModSettings` for configuration UI and persistence.
2. `DefModExtension` for custom fields attached to Defs.
3. `ThingComp`/`CompProperties` for reusable Thing behavior.
4. `GameComponent`, `MapComponent`, or `WorldComponent` for lifecycle-scoped state.
5. Harmony only when an existing behavior must be intercepted.

Call base lifecycle methods when required. Persist mutable state with the correct `Scribe_*` API and stable keys. Distinguish new spawn from load using lifecycle parameters such as `respawningAfterLoad`.

Avoid expensive per-tick scans. Prefer rare/long ticks, maintained indexes, event-driven updates, or bounded work.

## Harmony Workflow

1. Load the real target assembly with DecompilerServer.
2. Resolve the exact declaring type/member and overload.
3. Inspect callers/callees to prove the method is on the desired runtime path.
4. Choose Prefix, Postfix, finalizer, or transpiler based on the smallest behavioral change.
5. Verify instance/static shape, arguments, return/ref values, generics, and compiler-generated members.
6. Use a stable unique Harmony ID and patch only the current assembly.

Prefer `nameof(Type.Member)` when accessible. For overloads, specify argument types or use an explicit target resolver. Treat transpilers as high risk: match robust instruction patterns, fail visibly when the pattern changes, and test game-version updates.

## Compatibility

Do not patch by guessed method name. Do not use a pulled decompiled tree as stronger evidence than DecompilerServer against the current DLL. Check other Harmony owners/patches when ordering or conflicts matter. Use priorities/before/after only with a documented compatibility reason.

## Build And Runtime Verification

- Build the configured project with warnings visible.
- Confirm the intended DLL lands under `1.6/Assemblies`.
- Run a quick test for static initialization and XML type resolution.
- Add focused auto/manual coverage for the behavior.
- Test save/load when mutable state or Def identity is involved.
