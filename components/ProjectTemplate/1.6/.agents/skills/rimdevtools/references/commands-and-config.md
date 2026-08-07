# RimDevTools Command And Configuration Reference

## Contents

- Invocation and output
- Commands
- Configuration fields
- Exit and error handling
- Recommended sequences

## Invocation And Output

Run `./.tools/RimDevTools.exe` from the project root or one direct child. Project commands discover `mod_config.jsonc` only at those two levels. `init`, help, and `--version` do not require a project.

Successful and expected failure results end with one JSON object on stdout. Progress goes to stderr. Exit codes are: `0` success, `2` invalid arguments/config, `3` missing dependency, `4` busy, `5` task failure, `6` timeout, `7` game/test/export failure, and `130` cancellation.

## Commands

### `doctor`

Inspect project JSONC, game paths, .NET and project builds, deployment, ILSpy, LogSync, AutoTest, RimSearcher CLI/DataMod, database integrity/version, environment lock, and recoverable transactions. This command is read-only.

### `deploy [--sync-source] [--dry-run]`

Build every configured C# project and atomically deploy `sync_items` to `rimworld_dir/Mods/mod_name`.

- `--dry-run`: return the plan without build/deploy mutation.
- `--sync-source`: include `source/` in deployment.

Common errors: `ENVIRONMENT_BUSY`, `GAME_EXIT_TIMEOUT`, `BUILD_FAILED`, `SHADER_BUILD_FAILED`, `SYNC_ITEM_NOT_FOUND`, `DEPLOY_FAILED`.

### Reference commands

```text
ref search <keyword> [--fuzzy]
ref pull <query> [--version <value>] [--deps] [--all] [--fuzzy] [--force]
ref deps <query> [--version <value>] [--fuzzy]
ref related <query> [--version <value>] [--fuzzy]
```

Search scans the game Mods directory and `workshop_paths`. Pull accepts an absolute directory, exact package ID/name/folder, or a search match. It names targets from sanitized `About.xml` names, copies into `参考文档`, and decompiles assemblies. `--all` disables `pull_ignore_folders`; `--deps` resolves hard dependencies; `--force` replaces all preflighted conflicts. Ambiguity and target collisions fail before copying.

### Test commands

```text
test settings
test run [--deploy] [--sync-source] [--mode quick|manual|auto|xml]
         [--mods-file <path>] [--timeout-seconds <n>]
         [--target-package-id <id>] [--capture-screenshots]
         [--isolated-desktop|--no-isolated-desktop]
```

`test settings` persistently writes LogSync output and keyword settings. `test run` owns a transaction and restores global configuration even after most failure/cancellation paths.

- `--mods-file`: external XML containing an `activeMods` list.
- `--target-package-id`: auto/XML target; defaults to current `About.xml` package ID.
- `--capture-screenshots`: valid for auto/XML only.
- `--timeout-seconds`: positive run limit; otherwise `timeouts.max_wait`.
- Isolation precedence: explicit flag, project `isolated_desktop`, then mode default.

Common errors: `MODE_INVALID`, `MOD_NOT_RESOLVED`, `ENVIRONMENT_BUSY`, `GAME_EXIT_TIMEOUT`, `TEST_TIMEOUT`, `GAME_EXITED_EARLY`, `AUTO_RESULT_FAILED`, `TRANSACTION_RESTORE_FAILED`.

### Data commands

```text
data status
data refresh [--mods-file <path>] [--output <path>]
             [--timeout-seconds <n>]
             [--isolated-desktop|--no-isolated-desktop]
```

`data status` is read-only and reports path source, file size/time, SQLite integrity, component/user version, export run ID, game/language, mod hashes, loaded mods, and refresh recommendations. `data refresh` transactionally launches RimWorld and validates the DataMod export.

Common errors: `DATABASE_PATH_MISSING`, `EXPORT_REQUEST_CONFLICT`, `DATA_REFRESH_TIMEOUT`, `EXPORT_SIGNAL_MISMATCH`, `DATABASE_INVALID`, `VERSION_MISMATCH`.

### `init 1.6 --output <directory>`

Create a project from the packaged 1.6 template. The target must not exist or be empty. This is mainly a distribution-root command; an initialized project normally does not initialize itself.

## Configuration Fields

- `config_version`: must be `2`.
- `mod_name`: deployed folder name; keep it filesystem-safe and aligned with the real mod.
- `game_version`: currently `1.6`.
- `rimworld_dir`: RimWorld installation root containing the executable and `Mods`.
- `workshop_paths`: zero or more Workshop content roots searched by `ref` commands.
- `csproj_paths`: project-relative C# project paths built during deployment.
- `sync_items`: project-relative files/directories copied to the deployed mod.
- `protected_files_regex`: deployed relative filenames preserved across replacement, such as `PublishedFileId.txt`.
- `pull_ignore_folders`: folders omitted from reference pulls unless `--all` is used.
- `ilspycmd_path`: path relative to `.tools`; normally `Ilspy\ilspycmd.exe`.
- `log_keywords`: strings persisted to LogSync by `test settings` or applied transactionally by tests.
- `min_active_mods`: package IDs always included in temporary test mod lists.
- `rimsearcher_db_path`: project-relative or absolute Def database path.
- `isolated_desktop`: default isolation for quick/auto/XML and data refresh.
- `timeouts.steam_launch_detect`: Steam/game process appearance timeout.
- `timeouts.max_wait`: default test/export completion timeout.
- `timeouts.existing_game_exit`: wait for a pre-existing game process to exit.
- `timeouts.post_exit_delay`: stabilization delay after process exit.
- `retention.max_log_files`: selected-log archive limit.
- `retention.max_auto_test_runs`: automatic test artifact run limit.
- `shader_bundle.enabled`: enable Unity batch shader build before deployment.
- `shader_bundle.unity_project_path`: Unity project directory.
- `shader_bundle.unity_exe_path`: Unity Editor executable; required when enabled.
- `shader_bundle.build_method`: fully qualified static Unity build method.
- `shader_bundle.bundle_name`: expected output bundle name.

## Recommended Sequences

Configuration/setup: `doctor` -> repair reported prerequisites -> `doctor`.

Deploy: `doctor` -> `deploy --dry-run` -> inspect plan -> `deploy`.

Smoke test current code: `test run --deploy --mode quick`.

Automated verification: `test run --deploy --mode auto --target-package-id <id> --capture-screenshots`.

Def research: `data status` -> `data refresh` only if stale/invalid -> invoke the RimSearcher skill.
