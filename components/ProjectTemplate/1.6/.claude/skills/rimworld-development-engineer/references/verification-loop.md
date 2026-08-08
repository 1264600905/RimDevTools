# Verification Loop

## Before Mutation

1. Confirm `mod_config.jsonc` paths and target identity.
2. Run `doctor` if prerequisites or recovery state are uncertain.
3. Keep RimWorld closed unless the selected command owns launching it.
4. Avoid simultaneous deploy and test operations.

## Static Checks

- Parse XML after every edit.
- Check duplicate Def names and unresolved translation keys.
- Build changed C# projects with the configured project path.
- Verify official Harmony targets with focused RimSage C# symbol and source evidence.
- Check `LoadFolders.xml` and `sync_items` agree with edited locations.

## Runtime Checks

Start with:

```powershell
.\.tools\RimDevTools.exe deploy --dry-run
.\.tools\RimDevTools.exe test run --deploy --mode quick
```

Use `--sync-source` only when source deployment is intended. Use auto or XML modes when cases exist; request screenshots only for those modes. Manual mode is for interactions automation cannot represent.

## Result Review

Require all of the following:

- Final JSON has `ok: true`.
- Expected readiness/completion signal belongs to the current run.
- Auto/XML result manifest is complete with no missing/failed cases.
- Selected log has no attributable new error/warning.
- Transaction restoration succeeded.

When a known ambient warning appears, record why it is unrelated using mod identity, stack, path, or unchanged baseline. Never ignore by text pattern alone.

## Failure Report

Report:

1. Stable error code and command/mode.
2. Small decisive log excerpt.
3. Def/member/file evidence identifying ownership.
4. Changes attempted and what each retest proved.
5. Remaining blocker and next evidence needed.
