# RimDevTools

[简体中文](README.md) | **English**

RimDevTools is a Windows toolchain for RimWorld mod development. It provides repeatable CLI operations for environment checks, builds, deployment, dependency discovery, log collection, game testing and Workshop publishing. The bundled templates and tool Mod contain files for RimWorld **1.6 and 1.7**. The CLI also recognizes the 1.7+ modern layout; the release manifest defines which game versions its components and updates actually support.

The current version is **1.5.3.0**, with [RimWorld 1.6 and 1.7 templates and a Tools Package](https://github.com/1264600905/RimDevTools/releases/tag/v1.5.3.0). It includes existing-project mode, automatic updates, external XML scenario tests, configurable arena matchmaking and cleanup, health bars, noon resets and rotating mixed-squad previews. The Codex and Claude skills in both templates document these features. The custom scenario backend currently supports RimWorld 1.7 only.

## Repository components

| Component | Purpose |
| --- | --- |
| RimDevTools CLI | Inspect the development environment, build and deploy mods, copy references, manage tests, operate Workshop items and update this project's tools |
| LogSyncMod | Supports RimWorld 1.6/1.7; filters logs by PackageId, keywords and severity, then synchronizes them to a separate file |
| AutoTestRunner | Runs in-game UI, combat, view, recipe and ability tests, producing structured results and evidence |
| Project Template | Supplies a 1.6 or 1.7 project layout, translation directories for three languages and build configuration for new projects |
| Tools Package | Supplies the official tool payload, all Codex and Claude skills and a manual configuration reference for existing projects and tool updates |

## Main capabilities

- Inspect RimWorld 1.6/1.7+, Steam, the appropriate .NET build environment and Harmony assemblies.
- Deploy assemblies, Defs, About metadata and translations to RimWorld's Mods directory through file transactions, checking for files in use.
- Find local and Workshop mods, then safely copy XML, assemblies and ILSpy-decompiled source for reference.
- Temporarily change and restore `ModsConfig.xml` and test settings through transactions, including recovery after interruption.
- Run quick, manual and auto tests, with categories, exact DefNames, batch limits and screenshots.
- Return one final JSON result, separate Evidence files, related logs and main-thread heartbeat diagnostics.
- Relax template-specific checks through a configuration switch for existing projects, keeping their configured project and sync paths.
- Check and install tools from official stable Releases, preserve configuration and local edits, and recover or roll back interrupted updates.
- Build two full templates and one shared Tools Package, with file manifests and SHA-256 checksums.

## Download and quick start

Choose an attachment actually available on [GitHub Releases](https://github.com/1264600905/RimDevTools/releases), or build these three packages from source:

| Package | Use |
| --- | --- |
| `RimDevTools-<version>-Template-RimWorld-1.6.zip` | Create a RimWorld 1.6 mod project with the complete template and tools |
| `RimDevTools-<version>-Template-RimWorld-1.7.zip` | Create a RimWorld 1.7 mod project with the complete template and tools |
| `RimDevTools-<version>-Tools.zip` | Tools for an existing project, with all Codex and Claude skills |

The Tools Package contains `.tools`, all template skills under `.agents/skills` and `.claude/skills`, a configuration reference and a release manifest, including the required tool Mod files for both game versions, ILSpy and licenses. It reduces the example mod content while retaining tool capabilities. Its `mod_config.jsonc` is a manual reference: retain your existing project's configuration. The Tools Package is shared across game versions.

The release uploads only these three ZIPs; GitHub adds two source download entries automatically. Each client includes all 7 skills and their supporting files. Updates verify the GitHub asset SHA-256 digest; separate checksum attachments and compatibility packages are no longer published. Automatic updates manage the tools and core `rimdevtools` skill, preserving other skills. When manually integrating Tools into an existing project, retain your configuration and check local edits to matching skills. Updaters from 1.5.2 and earlier cannot use this full Tools package; install manually once.

Extract a new template into a separate development directory. Keep the project outside RimWorld's `Mods` directory and Steam Workshop content directories to satisfy deployment location checks.

Before first use, edit `mod_config.jsonc` and confirm at least:

- `mod_name`: the current mod's deployment directory name, in English.
- `game_version`: the actual target game version; the two current templates target `1.6` and `1.7`.
- `rimworld_dir`: the RimWorld installation root.
- `mods_config_path`: the existing `ModsConfig.xml` actually used by the game.
- `workshop_paths`: your local Steam Workshop paths.
- `csproj_paths`, `sync_items` and `log_keywords`: your project's build, deployment and log settings.

For a template project, replace the About name, packageId, default assembly name and other placeholders with the actual mod's information.

Then run:

```powershell
.\.tools\RimDevTools.exe doctor
.\.tools\RimDevTools.exe deploy --dry-run
```

`doctor` reports environment and project configuration issues. `deploy --dry-run` shows the deployment plan without changing the game directory.

## Existing-project mode

Enable existing-project mode by adding one switch to the project's current `mod_config.jsonc`. This excerpt shows only the relevant fields; merge them into your existing configuration:

```jsonc
{
    "config_version": 2,
    "existing_project_mode": true
}
```

An absent field or `false` retains template mode. With `true`, deployment, doctor and test preflight relax template-specific restrictions on names, project paths, assembly names and template log keywords. Builds and synchronization keep using your existing `csproj_paths`, `sync_items` and `game_version`. Reading configuration or deploying does not automatically rewrite it.

Required metadata, game versions, build failures, path safety and Mods/Workshop location protection are still checked. Pure XML mods may use `csproj_paths: []`. C# projects must work with the current `dotnet build` entry point and place DLLs where the configured deployment can copy them into game-loadable locations. Synchronization currently preserves relative paths; special output mappings, separate content roots and custom build chains may still require manual adaptation.

## Tool updates and recovery

These commands require a formally unpacked installation of 1.3.0.0 or later. Install an update-capable version manually once if your old EXE does not support updates.

```powershell
.\.tools\RimDevTools.exe update --check
.\.tools\RimDevTools.exe update --dry-run
.\.tools\RimDevTools.exe update
.\.tools\RimDevTools.exe update status
```

Ordinary commands check for updates at startup using a 24-hour cache and a total network budget of at most 2 seconds. Failures back off for 1 hour and preserve the original business result. Help, version, status queries and internal helpers do not trigger startup network checks. Explicit checks distinguish an available update, no newer version and an inability to check.

The updater fixes one official stable Release and its unique Tools attachment, verifying size, SHA-256, platform, Runtime, project game version and configuration compatibility. It skips the same version and does not automatically downgrade. `--dry-run` may download and verify staging data and list file changes or conflicts, but it does not commit tool files, configuration or installation state.

When `update` returns `scheduled`, the independent helper has acknowledged the handoff. It waits for the original process to exit before replacing files and checking the new EXE. Continue querying `update status`:

| State | Meaning |
| --- | --- |
| `none` | No update transaction has been recorded |
| `scheduled` / `applying` | Accepted / committing; installation is not yet complete |
| `succeeded` | Replacement and post-install verification completed |
| `failed` | Failure before committing; no partially replaced installation remains |
| `rolledBack` | A failed update restored the complete original installation |
| `recoveryRequired` | Recovery is needed; ordinary commands are blocked |

Local edits or deletions of managed files, a missing trusted baseline, or a user file occupying a new managed path stop the entire update and report conflicts. Configuration migration preserves comments, unknown fields, user values, project mode and the game target. The first implementation supports `config_version=2` and only supplies `false` when the mode field is absent.

Mod source, resources, Workshop identity, the project README, other skills, MCP/editor settings, `.gitignore` and original template identity are outside the replacement scope. Updating private tool Mod files does not update the game's installed components; use `mods install-tools` for those. After a successful disk update, ask the AI to read the complete `rimdevtools` skill again.

`update status` is a local, read-only query. If recovery is needed, wait for existing update processes to exit and use the returned `recoveryCommand`. The retained helper works even if the main EXE cannot start; its original entry point is recorded in `.tools/ProjectData/tool-update-state.json`. Unknown concurrent edits preserve backups and produce a conflict. The tool manages caches, staging and rollback data, retaining the three most recent completed transactions and all data needed for incomplete recovery.

Checking and installing updates do not require Steam login or a healthy game environment, and do not automatically repeat the original command. Source runs and older installations without a trusted release manifest require a manual upgrade first.

## Requirements

- Windows x64.
- Running the packaged RimDevTools: .NET 10 Runtime.
- Using the bundled ILSpy: .NET 8 Runtime.
- Building RimDevTools: .NET 10 SDK.
- Building RimWorld 1.6 mods: a dotnet SDK and .NET Framework 4.8 Developer/Targeting Pack; 4.7.2 is an accepted fallback. Modern 1.7+ mod projects use .NET 10 SDK and matching game assemblies.
- Running game tests: the target RimWorld 1.6 or 1.7+ installation, Steam client and a valid login session.

The current release manifest and bundled tool Mod declare support for 1.6 and 1.7. Recognizing a later modern layout does not establish component compatibility. LogSyncMod, AutoTestRunner and test mod lists are testing prerequisites; ordinary deployment does not require all test tools to be installed first.

RimWorld, Harmony and other third-party mod assemblies are not distributed with this repository.

## Common commands

RimDevTools returns key summaries by default and hides lengthy build, Unity and ILSpy output. Most subcommands support `--verbose` for complete plans, lists and diagnostics. Use it when troubleshooting or inspecting a full list. Warnings and errors remain visible.

```powershell
# Version and environment diagnostics
.\.tools\RimDevTools.exe --version
.\.tools\RimDevTools.exe doctor

# All diagnostic checks
.\.tools\RimDevTools.exe doctor --verbose

# Deploy the current project
.\.tools\RimDevTools.exe deploy --dry-run
.\.tools\RimDevTools.exe deploy

# Find and copy reference mods
.\.tools\RimDevTools.exe mod search "<keyword>"
.\.tools\RimDevTools.exe mod copy "<exact-query>" --dry-run

# Tests
.\.tools\RimDevTools.exe test run --mode quick
.\.tools\RimDevTools.exe test run --mode auto --scope view --scope combat
.\.tools\RimDevTools.exe test cleanup --dry-run

# Install or update the bundled tool Mod
.\.tools\RimDevTools.exe mods install-tools --dry-run
```

Pure XML mods can set `csproj_paths` to an empty array in `mod_config.jsonc`; deployment then skips C# builds.

## Automatic test model

| Mode | Use |
| --- | --- |
| `quick` | Default: start the game and wait for this run's readiness signal and main-thread heartbeat |
| `manual` | Interactive play and observation, on the current desktop by default |
| `auto` | Execute generated tests in the game, with screenshots enabled by default |

`auto` accepts repeated `--scope` options. The regular categories are `ui`, `combat`, `view` and `misc`; an unspecified scope uses the Standard test set.

`auto` also supports:

- `--defname <name>`: select exact test targets; repeatable.
- `--batch-size <count>`: limit enumerated cases; default 40.
- `--active-mods-file <path>`: use an external ModsConfig XML file.
- `--isolated-desktop`: run the game on an isolated desktop.

Enumerated runs, including Standard, sample uniformly without replacement from all candidates. Explicit `--defname` runs ignore the batch limit.

Screenshots are enabled by default in `auto` mode. No extra option is required.

After quick/auto `test run`, successful JSON includes paths under `result.artifacts`; failures use `error.details.artifacts`. `result.logSummary` or `error.details.logSummary` always includes `errorCount` and `warningCount`. These count records from this run's selected log and indicate availability, completeness and truncation.

Automatic tests produce `*_Result.json` and, as applicable to the run, Evidence, reports, manifests and screenshots. Use the returned `autoTestOutputPath` and original files referenced by the result. The tool creates run directories; do not assume a fixed archive location. Modes such as quick may have no AutoTest files. If the current runId receives no main-thread heartbeat for 60 consecutive seconds, the tool ends with `GAME_HUNG` and attempts environment recovery.

Read the original `result.artifacts` or `error.details.artifacts` and report findings in chat. Unless an export is explicitly requested, do not save extra stdout/stderr copies, copy logs/screenshots/test saves, or create task/date evidence archives. The tool owns retention and cleanup.

## Build and test from source

```powershell
dotnet build AutoTest.Core.Tests\AutoTest.Core.Tests.csproj
dotnet AutoTest.Core.Tests\bin\Debug\net10.0\AutoTest.Core.Tests.dll

dotnet build RimDevTools.Tests\RimDevTools.Tests.csproj
dotnet RimDevTools.Tests\bin\Debug\net10.0\RimDevTools.Tests.dll
```

Generate unpacked release directories:

```powershell
powershell -ExecutionPolicy Bypass -File .tools\publish-rimdevtools.ps1
```

The default build creates two templates and one Tools Package using the same common payload build. `--version` reports the tool version and original template metadata; the Tools Package reports `unpackaged` for its template version. To generate ZIPs and checksums:

```powershell
powershell -ExecutionPolicy Bypass -File .tools\publish-rimdevtools.ps1 -CreateZip
```

Output:

```text
artifacts/RimDevTools-1.5.3.0-Template-RimWorld-1.6/
artifacts/RimDevTools-1.5.3.0-Template-RimWorld-1.7/
artifacts/RimDevTools-1.5.3.0-Tools/
```

Each directory has one `.zip`; upload only these three ZIPs. Verify all three release layouts, then run the isolated Windows update integration tests:

```powershell
dotnet build RimDevTools.ReleaseTests\RimDevTools.ReleaseTests.csproj
dotnet RimDevTools.ReleaseTests\bin\Debug\net10.0\RimDevTools.ReleaseTests.dll `
  artifacts\RimDevTools-1.5.3.0-Template-RimWorld-1.6
dotnet RimDevTools.ReleaseTests\bin\Debug\net10.0\RimDevTools.ReleaseTests.dll `
  artifacts\RimDevTools-1.5.3.0-Template-RimWorld-1.7
dotnet RimDevTools.ReleaseTests\bin\Debug\net10.0\RimDevTools.ReleaseTests.dll `
  artifacts\RimDevTools-1.5.3.0-Tools
dotnet RimDevTools.Tests\bin\Debug\net10.0\RimDevTools.Tests.dll --tool-update
dotnet RimDevTools.Tests\bin\Debug\net10.0\RimDevTools.Tests.dll --tool-update-live
```

Windows integration tests build a target version in the suite's managed temporary environment and verify actual EXE handoff, replacement and recovery. See the [CLI notes](RimDevTools/README.md) for more command contracts.

## Directory layout

```text
1.6/, 1.7/                  Versioned LogSyncMod / AutoTestRunner build outputs and Defs
About/                       LogSyncMod metadata and preview image
Languages/                   Simplified Chinese, Traditional Chinese and English translations
source/                      LogSyncMod and AutoTestRunner source
RimDevTools/                 CLI source
AutoTest.Core.Tests/         Game-independent automatic testing core regressions
RimDevTools.Tests/           CLI and environment transaction regressions
RimDevTools.ReleaseTests/    Release layout and content checks
components/ProjectTemplate/  Package template source
.tools/                      Development, test and publishing scripts
策划案/                      Design plans and implementation records
参考文档/                    Local third-party reference material, ignored by Git by default
```

## Steam Workshop

```powershell
.\.tools\RimDevTools.exe steamworkshops status
.\.tools\RimDevTools.exe steamworkshops search "Harmony"
.\.tools\RimDevTools.exe steamworkshops search next
.\.tools\RimDevTools.exe steamworkshops search "Harmony" --details --page 5
.\.tools\RimDevTools.exe deploy
.\.tools\RimDevTools.exe steamworkshops upload --dry-run
.\.tools\RimDevTools.exe steamworkshops upload
.\.tools\RimDevTools.exe deploy --upload
.\.tools\RimDevTools.exe steamworkshops edit
.\.tools\RimDevTools.exe steamworkshops edit --visibility private
.\.tools\RimDevTools.exe steamworkshops subscribe "<id-or-workshop-url>" --wait
.\.tools\RimDevTools.exe steamworkshops status "<id-or-workshop-url>" --wait
```

- The five subcommands are upload, edit, search, subscribe and status. Project identity first uses About/PublishedFileId.txt for the numeric Workshop ID; packageId is read separately from About.xml. Writes apply only to specifically authorized items; authorization already granted in the session can be reused.
- upload uses only a successful full deploy's directory and verifies added, missing or changed files and undeployed project inputs. It does not build automatically or offer a --create/hash bypass. First publication automatically creates a private item; updates preserve manually edited remote metadata. deploy --upload runs both stages under one lock; --dry-run shows only a plan, and --xml-hot is mutually exclusive.
- Normal 1.7 deployment uses locally installed official libraries to generate Assets.zip in load folders, retaining source resources by default; 1.6 skips baking. Normal deployment, baking and publishing do not start the game. Official libraries are not distributed with the tool.
- edit sends metadata rather than mod content. With no field options it synchronizes the project's About description; --description-file, --preview-file and --visibility change only the requested fields. Visibility values are private/public/friends-only/unlisted. Writes currently require a verified owner; contributor detection is deferred.
- Simple search shows name, ID and subscriber count: 25 items per user page, up to 3 pages. --details adds the official 255-byte summary: 10 items per page, up to 5 pages. --page starts at 1, slicing official 50-item pages in sequence; refine the keyword when reaching the limit. Exact IDs/links and status return full descriptions.
- search next reuses this project's last successful keyword, mode and page; it cannot be mixed with --page/--details. The cache lasts 10 minutes from the initial search. A hit includes source=cache and the original query time and does not prove current connectivity, permissions or publication success. One-off searches work without a project; next requires a project directory or --project.
- subscribe subscribes only the requested ID and requests download when needed. --wait verifies matching App ID/item callbacks and current installation state. status --wait only observes existing downloads. These commands do not enable/order mods, recursively fetch dependencies or automatically unsubscribe. Installed combined with NeedsUpdate is not complete; unavailable progress is null.
- deployment-receipt.json, steamworkshops-search-cache.json, bindings and new runtime staging files live in the project's .tools/ProjectData. Keep runtime data ignored by Git and excluded from deployment and releases. Cache cleanup must preserve receipts and bindings. Deploy/upload artifacts report actual receipt paths; test logs use test run's artifacts object. Do not save extra stdout or copy evidence.
- A publishing timeout returns nonzero, timedOut=true, remoteOutcome=unknown, the current time, the target item's latest Steam update time (or null and a reason), operationId and the submission stage. A timeout does not cancel the remote operation. The tool does not automatically retry, retain a cross-call timeout blocker or use --recover. The AI should assess evidence before explicitly resubmitting. Failed known-ID persistence and actual binding conflicts still require repair.
- doctor writes a Steam running/library availability/Workshop connectivity summary to stderr while stdout remains one final JSON object. Steam time comes from m_rtimeUpdated and may not change after metadata edits; no historical update count is provided.

Use `--text-language` to select the language of titles, descriptions and change notes. `edit` also supports `--add-preview`, `--update-preview` and `--remove-preview` for additional images; see its `--help` for parameters.

## License and third-party notices

This repository uses the [MIT License](LICENSE). Bundled third-party components, including ILSpy, retain their own licenses; see [third-party notices](RimDevTools/Packaging/licenses/third-party-notices.txt), the [ILSpy license](RimDevTools/Packaging/licenses/ILSpy-LICENSE.txt) and `.tools/Data/licenses` in the package.

RimWorld is a trademark and product of Ludeon Studios. This project is not affiliated with or officially partnered with Ludeon Studios. RimWorld, Harmony and other third-party mod assemblies are not distributed with this repository.

## Acknowledgments

Thanks to 骸鸾, 可爱猫猫喵 and biobattery for helping test the project.
