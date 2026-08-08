# RimDevTools Command And Configuration Reference

## Invocation And Output

Run `.\.tools\RimDevTools.exe` from the project root or one direct child. Project commands discover `mod_config.jsonc` only at those levels. Help, `--version`, and `mods install-tools --mods-path` do not require a project.

Commands end with one JSON object on stdout; progress goes to stderr. Exit codes are `0` success, `2` invalid arguments/config, `3` missing dependency, `4` busy, `5` task failure, `6` timeout, `7` game/test failure, and `130` cancellation. `--version` returns only `ok`, `command`, and `rimDevToolsVersion`.

## Commands

### `doctor [--verbose]`

Inspect build prerequisites, Steam/RimWorld/Workshop discovery, game and Harmony assemblies, Unity, Git/worktree state, required mods, ILSpy, LogSync, AutoTest, packaged tool mods, locks, and transactions. It is read-only and does not probe RimSage. Default output contains actionable checks and critical assembly paths; `--verbose` includes all checks.

### `deploy [--sync-source] [--dry-run]`

Build configured C# projects and atomically deploy `sync_items` to `rimworld_dir/Mods/mod_name`.

- `--dry-run`: return the immutable plan without building or deploying.
- `--sync-source`: include `source/` in deployment.

Common errors: `ENVIRONMENT_BUSY`, `GAME_EXIT_TIMEOUT`, `BUILD_FAILED`, `SHADER_BUILD_FAILED`, `SYNC_ITEM_NOT_FOUND`, `DEPLOY_FAILED`.

### Reference Commands

```text
ref search <keyword> [--fuzzy]
ref pull <query> [--version <value>] [--deps] [--all] [--fuzzy] [--force]
ref deps <query> [--version <value>] [--fuzzy]
ref related <query> [--version <value>] [--fuzzy]
```

Search scans the game Mods directory and `workshop_paths`. Pull accepts an absolute directory, exact package ID/name/folder, or search match. It names targets from sanitized About names, copies into `参考文档`, and decompiles assemblies with packaged ILSpy.

- `--deps`: pull the complete hard-dependency closure.
- `--all`: disable `pull_ignore_folders`.
- `--force`: replace all preflighted conflicts.
- `--fuzzy`: allow fuzzy candidate matching.
- `--version`: select version-specific dependency metadata.

Do not add these options implicitly. Ambiguity, dependency cycles, target conflicts, and sanitized-name collisions fail before copying. Common errors include `MOD_NOT_FOUND`, `MOD_AMBIGUOUS`, `DEPENDENCY_CYCLE`, `TARGET_COLLISION`, `TARGET_CONFLICT`, and `PULL_FAILED`.

### Test Commands

```text
test settings
test run [--deploy] [--sync-source] [--mode quick|manual|auto|xml]
         [--mods-file <path>] [--timeout-seconds <n>]
         [--target-package-id <id>] [--capture-screenshots]
         [--isolated-desktop|--no-isolated-desktop]
```

`test settings` persistently writes LogSync output and keyword settings. `test run` owns a transaction and restores global configuration after success, failure, timeout, or cancellation when restoration remains possible.

Quick is the startup smoke test. Manual is interactive. Auto and XML validate structured AutoTest results. `--capture-screenshots` applies only to auto/XML. Isolation precedence is explicit flag, project setting, then mode default.

### `mods install-tools [--mods-path <path>] [--dry-run]`

Install the packaged LogSyncMod. The manifest must contain exactly the `trigger.logsyncmod` component. The installer verifies package ID, version, directory hash, staging, backup, rollback, and downgrade rules; only `PublishedFileId.txt` is preserved during upgrade.

Without `--mods-path`, resolve the RimWorld Mods directory from project configuration. Use `--dry-run` to inspect the single-component plan without writing.

## Configuration Fields

- `config_version`: must be `2`.
- `mod_name`: deployed folder name.
- `game_version`: target version, currently `1.6`.
- `rimworld_dir`: RimWorld installation root.
- `workshop_paths`: Workshop roots searched by reference commands.
- `csproj_paths`: C# projects built by deployment.
- `sync_items`: project files/directories copied during deployment.
- `protected_files_regex`: deployed relative paths preserved across replacement.
- `pull_ignore_folders`: folders omitted by reference pulls unless `--all` is used.
- `ilspycmd_path`: path relative to `.tools`, normally `Ilspy\ilspycmd.exe`.
- `log_keywords`: strings applied to LogSync settings.
- `min_active_mods`: package IDs always included in temporary test mod lists.
- `isolated_desktop`: default isolation for quick/auto/XML.
- `timeouts.steam_launch_detect`: Steam/game appearance timeout.
- `timeouts.max_wait`: default test completion timeout.
- `timeouts.existing_game_exit`: wait for an existing game process.
- `timeouts.post_exit_delay`: stabilization delay after exit.
- `retention.max_log_files`: selected-log archive limit.
- `retention.max_auto_test_runs`: automatic test run retention.
- `shader_bundle.*`: optional Unity batch shader build settings.

## Recommended Sequences

- Setup: `doctor` -> repair prerequisites -> `doctor`.
- Deploy: `doctor` -> `deploy --dry-run` -> inspect -> `deploy`.
- Smoke: `test run --deploy --mode quick`.
- Automated: `test run --deploy --mode auto --target-package-id <id>`.
- Third-party evidence: `ref search` -> exact `ref pull` -> inspect local XML and ILSpy source.
