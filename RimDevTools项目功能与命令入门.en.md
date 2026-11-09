# From Building a Mod to Publishing on Workshop: A Guide to RimDevTools Features and Commands

[简体中文](RimDevTools项目功能与命令入门.md) | **English**

This guide covers **RimDevTools 1.5.3.0**. The current release packages support RimWorld **1.6 and 1.7**. External XML regression scenarios, group combat and arena tournaments currently require **1.7**. Download the packages from the [1.5.3.0 release page](https://github.com/1264600905/RimDevTools/releases/tag/v1.5.3.0).

The chapters follow a typical development workflow: prepare a project, research references, deploy changes, collect logs and test. The later chapters explain AI skills, RimSage MCP and Workshop publishing. Commands and limitations reflect the current implementation; an older executable may expose fewer features. Run `--version` before following examples from a newer release.

**Contents**

- [1. What RimDevTools does and what is included](#1-what-rimdevtools-does-and-what-is-included)
- [2. First use: project setup and environment checks](#2-first-use-project-setup-and-environment-checks)
- [3. Finding reference mods and using ILSpy](#3-finding-reference-mods-and-using-ilspy)
- [4. Building, deployment and XML hot reload](#4-building-deployment-and-xml-hot-reload)
- [5. LogSyncMod: filtered logs and runtime feedback](#5-logsyncmod-filtered-logs-and-runtime-feedback)
- [6. Test preparation: modes, mod lists and configuration recovery](#6-test-preparation-modes-mod-lists-and-configuration-recovery)
- [7. Automated tests: coverage and execution](#7-automated-tests-coverage-and-execution)
- [8. Debug actions: preparing and checking content in game](#8-debug-actions-preparing-and-checking-content-in-game)
- [9. Seven skills for AI-assisted development](#9-seven-skills-for-ai-assisted-development)
- [10. RimSage MCP: researching official definitions and source](#10-rimsage-mcp-researching-official-definitions-and-source)
- [11. Steam Workshop: search, subscriptions and publishing](#11-steam-workshop-search-subscriptions-and-publishing)
- [12. Command reference and common workflows](#12-command-reference-and-common-workflows)

## 1. What RimDevTools does and what is included

### 1.1 The problems it addresses

Writing XML or C# is only part of making a RimWorld mod. You also need to prepare the build environment, copy files into the correct game directory, enable dependencies, launch the game, inspect logs and repeat those steps after each change. Publishing adds another question: does the uploaded content actually match the version you just checked?

RimDevTools is a Windows toolchain that connects these tasks through CLI commands. You can use individual features, such as filtered logging or deployment, or combine them into a repeatable edit, verify and publish workflow. XML-only authors, C# authors and developers working with AI can each use the parts they need.

The project supplies templates for RimWorld 1.6 and 1.7. The CLI also recognizes the modern 1.7+ game layout, but that does not establish support for every later version. Game assemblies, test hosts and release manifests must support the actual configured version.

### 1.2 How the components work together

| Component | Role | Typical tasks |
| --- | --- | --- |
| RimDevTools CLI | Runs outside the game | Environment checks, builds, deployment, reference discovery, test orchestration and Workshop operations |
| LogSyncMod | Runs inside the game | Filtered logs, runtime signals, XML reload requests and manual debug helpers |
| AutoTestRunner | Executes tests inside the game | Discover supported content, prepare scenes, check assertions, record results and capture screenshots |
| ILSpy | Reads compiled assemblies | Decompile reference DLLs into readable C# |
| Project templates | Provide a development workspace | Project files, version folders, metadata, translation folders and tool configuration |
| Skills | Supply task instructions for AI clients | Research, planning, implementation, verification and player-facing descriptions |
| RimSage MCP | Connects AI clients to official reference material | Search official Defs, inspect XML inheritance and read C# implementations |

For example, when adding an equipment effect, you can first research the official mechanism through RimSage. If a third-party implementation is useful, copy a local reference and inspect its decompiled code. Then change your project, deploy it, collect logs and run the relevant tests. Review the results and actual gameplay before publishing.

## 2. First use: project setup and environment checks

The CLI is a program operated through text commands. The complete examples below assume PowerShell is open in the project root and the executable is installed at `.tools\RimDevTools.exe`.

### 2.1 Choose a package and configure the project

The 1.5.3.0 release provides three ZIP packages:

| Package | Intended use |
| --- | --- |
| `RimDevTools-1.5.3.0-Template-RimWorld-1.6.zip` | Start a RimWorld 1.6 mod project |
| `RimDevTools-1.5.3.0-Template-RimWorld-1.7.zip` | Start a RimWorld 1.7 mod project |
| `RimDevTools-1.5.3.0-Tools.zip` | Add the tools to an existing project without the example mod |

All three include the tools and complete sets of seven skills for both Codex and Claude. The Tools Package also includes both versions of the tool Mod, ILSpy, licenses and a configuration reference. It omits the example project, project README, `.gitignore` and MCP client configuration files. GitHub's separate source downloads are for source-code use. The release no longer supplies a `Tools-Full` package, compatibility packages or separate `.sha256` attachments.

Extract the project into a separate development directory, outside the game's `Mods` directory and Steam Workshop content directories. Maintain source files here; deployment creates the copy the game loads.

Edit `mod_config.jsonc`, which is the project's working configuration. JSONC allows comments. Check at least:

| Field | What to configure |
| --- | --- |
| `mod_name` | The deployment directory name for your mod |
| `game_version` | The exact target game version, such as `1.6` or `1.7` |
| `rimworld_dir` | The matching RimWorld installation root |
| `mods_config_path` | The existing `ModsConfig.xml` that this game actually reads |
| `workshop_paths` | Local Steam Workshop content locations |
| `csproj_paths` | C# projects to build; use `[]` for an XML-only mod |
| `sync_items` | Files and directories to deploy |
| `log_keywords` | Keywords for this project's filtered logs |

Update the name, author and `packageId` in `About/About.xml`. The PackageId identifies a mod in the game; it is separate from the numeric Workshop item ID. For a C# template, also replace example project and assembly names. Template checks help identify placeholders that remain unchanged.

### 2.2 Install prerequisites and run doctor

The CLI requires Windows x64 and the **.NET 10 Runtime**. Building C# also requires the appropriate SDK and references. RimWorld 1.6 projects normally target .NET Framework 4.8, requiring its Developer/Targeting Pack; 4.7.2 is also supported. RimWorld 1.7 projects use .NET 10 and the matching game assemblies. The bundled ILSpy separately requires the **.NET 8 Runtime**.

```powershell
.\.tools\RimDevTools.exe doctor
```

`doctor` checks configuration, game version and assemblies, Harmony, build prerequisites, reference tools, locks and unfinished transactions. It also provides Steam and Workshop diagnostics. Read which path, dependency, version or recovery condition needs attention before changing the environment.

Optional test capabilities are distinguished from ordinary development requirements. A missing test host does not necessarily prevent a normal build or deployment. Conversely, a working CLI does not establish that all optional features are ready. `doctor` does not check RimSage connectivity or the runtime already needed to start the CLI itself.

### 2.3 Read output and get help

Normal commands return **one final JSON object on stdout**. Progress and diagnostics go to stderr; help is plain text. Default JSON contains a summary and key fields. Add `--verbose` for expanded checks, plans and supported subprocess diagnostics.

```powershell
.\.tools\RimDevTools.exe --version
.\.tools\RimDevTools.exe --help
.\.tools\RimDevTools.exe test run --help
```

`--version` must be the only argument. It reports the CLI version and packaged template information; an unpackaged source checkout may report no template identity. Project commands look for `mod_config.jsonc` in the current directory and then its direct parent.

When reading a result, establish whether the operation succeeded, which project and target it used, what failed and which artifacts are available. A successful deployment, test and upload each establish different things, as explained in Chapter 12.

### 2.4 Integrate an existing project

You can retain an existing mod's layout. Install the Tools Package and set `"existing_project_mode": true` in your current `mod_config.jsonc`. Keep your actual `mod_name`, `game_version`, `csproj_paths`, `sync_items` and log settings. Treat the packaged configuration as a manual reference rather than replacing your configured file.

Existing-project mode relaxes template-specific naming, assembly and log-keyword checks. Required metadata, game versions, build dependencies and path safety remain checked. Missing or `false` retains template mode. XML-only projects can use `csproj_paths: []`; multi-project mods should list the required projects explicitly.

Integration does not automatically discover your build layout, generate configuration or edit `.gitignore`; there is no `init` command. Check local modifications before replacing matching skills, and ignore runtime data such as `.tools/ProjectData/` in your own repository.

### 2.5 Update installed tools

For a supported release installation, run these commands in sequence:

```powershell
.\.tools\RimDevTools.exe update --check
.\.tools\RimDevTools.exe update --dry-run
.\.tools\RimDevTools.exe update
.\.tools\RimDevTools.exe update status
```

`--check` queries the latest official stable release. `--dry-run` downloads and verifies the Tools ZIP and previews changes and conflicts without committing replacements. A real update hands work to a separate helper. **`scheduled` means accepted, not installed.** Continue checking `update status` until it reaches a terminal state; after `succeeded`, check `--version`.

**Updaters from 1.5.2 and earlier cannot directly update to 1.5.3: install manually once.** Source checkouts and installations without a trusted release manifest also require manual installation. The 1.5.3 updater understands verification of the complete Tools Package.

The updater verifies the GitHub asset SHA-256 digest, size, platform, runtime and game compatibility. It manages the executable, private tool payload and the core `rimdevtools` skill for both clients. Project source, resources, configuration values, Workshop identity, original template identity and other skills are preserved. Local changes or deletions of managed files stop the entire update with `UPDATE_CONFLICT`. Supported configuration migration preserves comments and unknown fields while adding missing supported entries.

Ordinary commands use a 24-hour update-check cache and at most two seconds of network time; they do not install updates automatically. Help, version and status queries skip these network checks. If recovery is required, let existing updater processes exit and follow the returned `recoveryCommand`. After updating, have the AI reread the full `rimdevtools` skill. Updating the private tool payload does not update LogSyncMod in the game's Mods directory; use `mods install-tools` for that.

## 3. Finding reference mods and using ILSpy

### 3.1 Search, copy and inspect dependencies

`mod search` searches mods already installed in the game Mods directory and configured Workshop directories. `steamworkshops search` searches online Workshop items. Use local search to resolve an exact reference before copying it.

```powershell
.\.tools\RimDevTools.exe mod search "Harmony"
.\.tools\RimDevTools.exe mod dependencies "brrainz.harmony"
.\.tools\RimDevTools.exe mod dependents "brrainz.harmony"
```

`mod dependencies` lists the selected mod's hard dependencies. `mod dependents` finds local mods that depend on it. These answer different questions when investigating prerequisites or the impact of changing a shared component.

`mod copy <query>` copies a local mod into `project_paths.reference_docs`, whose default directory name is `参考文档`, and invokes ILSpy. Preview with `--dry-run`. Add `--deps` to include the hard-dependency closure, `--include-ignored` to include normally ignored resources, or `--overwrite-existing` to replace existing reference copies. These options must be explicit. Official game content, DLC and Harmony are treated as shared dependencies rather than copied through the closure.

### 3.2 What ILSpy provides

XML is readable directly; a DLL contains compiled code. ILSpy reconstructs readable C# so you can follow classes, methods and conditions. For a turret, XML might identify the implementation class while the DLL contains target selection and special firing behavior. The decompiled view helps connect them and investigate compatibility or stack traces.

### 3.3 Where decompiled files go

The bundled command-line tool is `.tools/Ilspy/ilspycmd.exe`, configured through `ilspycmd_path`. `mod copy` normally invokes it for you. It prefers the reference mod's target-version `Assemblies` directory and falls back to a root `Assemblies` directory when that version directory is absent.

Decompiled files go beneath the copied assembly directory in `Decompiled/<assembly name>`. Read the actual destination from the command result. Reference copies remain separate from your implementation, making it easier to distinguish research material from files you intend to deploy.

Default ignore settings omit some large texture, audio and build directories. Use `--include-ignored` when those assets matter to the investigation.

### 3.4 Runtime requirements and interpretation limits

Decompiled code is an implementation reference. Original comments, project organization and some variable names may be absent, and compiler-generated code may appear. Recompiling it into a usable project requires separate work on dependencies and build configuration.

If the CLI runs but decompilation fails, check ILSpy's configured path and its separate .NET 8 Runtime requirement. The CLI itself requires .NET 10. The release package retains ILSpy's third-party license information.

## 4. Building, deployment and XML hot reload

### 4.1 Deploy current changes

Deployment sends the development copy to the directory loaded by the game. `deploy` builds configured C# projects and synchronizes assemblies, Defs, Patches, About metadata, translations and other configured resources. For XML-only mods, `csproj_paths: []` skips C# compilation while retaining synchronization.

```powershell
.\.tools\RimDevTools.exe deploy --dry-run
.\.tools\RimDevTools.exe deploy
```

The preview shows the target and planned synchronization. Add `--verbose` for the full plan. Real deployment stages files, checks files that will change for active users, and commits replacements while keeping the target directory in place. Transaction backups support rollback if a commit fails. Recovery errors identify unfinished work that needs attention.

Use `--sync-source` for a single deployment that includes `source/`, or configure `sync_source: true` for the project default. Protected top-level paths matching `protected_files_regex` are retained.

### 4.2 Synchronize XML while the game is running

`deploy --xml-hot` checks whether RimWorld is running. If it is, the command transactionally synchronizes configured non-DLL files. When Def or Patch XML changes, it waits at least three seconds and requests a reload through LogSyncMod's local HTTP endpoint. If no game is running, it falls back to a normal build and deployment.

The request is handled on the game main thread. It requires an active play session and developer mode. A main-menu session, disabled developer mode or unavailable endpoint prevents a successful reload; copied files alone do not establish that reload completed.

Inspect actual effects in game. Changes to initialization or object creation may not immediately update existing objects. C# changes require a normal build and a new game process to load the changed assemblies. `--xml-hot` belongs to standalone `deploy`; `test run --deploy` always uses normal deployment.

### 4.3 RimWorld 1.7 asset packing

Normal deployment for 1.7 invokes libraries from the installed game to bake `Assets.zip` files in the applicable load folders. Ordinary deployment, asset packing and Workshop uploads do not require launching the game. The older Unity shader-bundle workflow applies to 1.6; 1.7+ uses the native GLSL/ShaderDef workflow.

## 5. LogSyncMod: filtered logs and runtime feedback

LogSyncMod filters records by target PackageId, keywords and severity and writes them to a separate log. When possible, it adds source-mod, XML-file or assembly information to make diagnosis easier in a session with many loaded mods.

### 5.1 Install the tool Mod and configure logging

The packaged component is stored under `.tools/Data/LogSyncMod`, including metadata, translations and version-specific files for 1.6 and 1.7. The game must load its installed copy for the helpers to work. Its PackageId is `trigger.logsyncmod`, separate from your own mod. AutoTestRunner DLLs are included in the same component's version folders.

```powershell
.\.tools\RimDevTools.exe mods install-tools --dry-run
.\.tools\RimDevTools.exe mods install-tools
```

Installation validates the component manifest, metadata, assembly versions and content hashes. Identical content is skipped, older versions can be upgraded, newer versions are retained, and conflicting content at the same version is rejected. Only `PublishedFileId.txt` is preserved during an upgrade.

For manual debugging, use the in-game LogSyncMod settings to choose an output path and target PackageId, or select a currently loaded mod. Set keywords and Message, Warning and Error options, then apply the settings. The page also offers path checking and access to the log folder.

### 5.2 Filtering by source, keyword and level

The target PackageId focuses on a mod's source. Keywords retain records containing a recognizable marker. Level options control Message, Warning and Error output. The CLI passes the project's `log_keywords` when preparing a test.

A keyword-matching Message can enter the synchronization path even when general Message synchronization is disabled. Warning and Error records remain subject to their respective level switches. Leaving the target PackageId unset broadens the source range. Interpret the output using the settings active for that session.

### 5.3 Source hints and duplicate records

Records include severity, time and any identifiable source. Some XML, patch and assembly-loading errors can be associated with a file or DLL. The logger also attempts to connect later source lines with an earlier error when the information arrives separately.

Source detection has limits, especially for shared code, complex call chains and duplicate DefNames. Use these hints with the actual stack trace and local implementation. Repeated records are deduplicated, so log-entry counts describe retained distinct records rather than the number of times a gameplay event occurred.

### 5.4 Counts and truncation

Quick and auto test results include `errorCount` and `warningCount` in their log summary. Also inspect `available`, `countComplete` and `truncated` to determine whether the log was readable, whether counting is complete and whether the line limit affected output.

At the configured line limit, ordinary messages and warnings are suppressed while errors and test-control signals continue. A zero filtered error count only describes the recorded scope; it does not prove the entire game log was error-free. Top-level CLI warnings can also describe dependencies rather than logged game warnings.

### 5.5 Save settings for manually launched sessions

`test settings` waits for the game to exit, obtains the shared environment lock and persists this project's LogSync settings for later manual launches, including launches from Steam. These settings remain in place. `test run` instead applies temporary settings and restores the previous configuration afterward.

### 5.6 Readiness, heartbeat and audio

LogSyncMod emits readiness signals and main-thread heartbeats tagged with the current run ID. The CLI uses these to distinguish current progress from stale records or a process that is merely still alive.

Heartbeats are emitted approximately every five seconds. If the process remains alive but no heartbeat for the current run arrives for 60 seconds, the CLI reports `GAME_HUNG` and terminates the game. Automated sessions also manage muting and restoration as appropriate to the selected mode.

## 6. Test preparation: modes, mod lists and configuration recovery

### 6.1 Choose a mode

| Mode | Purpose |
| --- | --- |
| `quick` | Startup smoke test: detect loading and initialization problems |
| `manual` | Prepare the environment and launch the game for your own interaction |
| `auto` | Run supported in-game checks and validate structured results |
| `groupcombat` | Compare two teams using explicit external XML scenarios; 1.7 only |
| `arena` | Run a multi-team tournament from an external ArenaSuite; 1.7 only |

Ordinary auto tests discover supported content without requiring a project-specific Spec XML. You can also write external regression scenarios, described in Sections 7.15–7.17. Group combat and arena require explicit scenario paths and test names and are never started by ordinary auto discovery.

```powershell
.\.tools\RimDevTools.exe test run --deploy --mode quick
.\.tools\RimDevTools.exe test run --deploy --mode auto --scope combat --scope view
```

**Use `--deploy` to test current source changes.** Without it, the game loads the already-deployed copy. `test run` closes an existing RimWorld session before starting the new run, so save ongoing gameplay first.

### 6.2 Test with several mod lists

A mod list specifies enabled PackageIds and load order. The game's active list lives in `ModsConfig.xml`; you can keep additional XML lists for baseline and compatibility testing. Without `--active-mods-file`, the CLI uses `min_active_mods` from the project configuration.

Select one prepared list per run. The following filenames are examples: replace them with files you have actually prepared. Wait for the first run and configuration restoration to finish before starting the next.

```powershell
.\.tools\RimDevTools.exe test run --mode quick --active-mods-file ".tools\modlist\baseline.xml"
.\.tools\RimDevTools.exe test run --mode quick --active-mods-file ".tools\modlist\compatibility.xml"
```

The same selection works with manual and auto modes. Keep the test targets and scopes consistent when comparing mod combinations. The packaged `.tools/modlist` directory is a convenient location for input lists; another existing path is also accepted.

The file must contain an `<activeMods>` element with `<li>` PackageId entries. Workshop URLs or a text file of display names do not substitute for this XML. The CLI validates the structure and removes duplicate entries.

Every list must include all entries in the project's `min_active_mods` and the current startup requirements:

| Required mod | PackageId |
| --- | --- |
| Harmony | `brrainz.harmony` |
| SmartSpeed | `sarg.smartspeed` |
| Camera+ | `brrainz.cameraplus` |
| Humanoid Alien Races | `erdelf.humanoidalienraces` |
| LogSyncMod | `trigger.logsyncmod` |

The CLI normalizes configured baseline order, adds Core or the current target where applicable, and resolves installations from Mods and Workshop directories. Additional mods still need suitable prerequisites and load order. A required mod missing from the enabled list and a mod whose files cannot be found are separate errors; use the returned details to distinguish them. Resolvable hard dependencies that are missing or disabled can also produce dependency warnings without blocking execution.

### 6.3 Back up before testing and restore afterward

The tool backs up the current enabled-mod list and settings that it temporarily changes. It writes the selected test list and LogSync settings, including output paths, run ID, mode and selections, under a configuration transaction. At the end it restores the previous enabled content, order and tool settings.

This applies to normal completion, failure and cancellations that the CLI can handle. Configuration restoration does not undo spawned items, healed pawns or gameplay you actively save. Those are changes to the running game rather than temporary configuration.

When changing the target game version, the CLI first updates the `<version>` field in `ModsConfig.xml` to the validated `game_version`, before taking the transaction snapshot. Restoration therefore keeps that target version while restoring the earlier enabled-mod list.

Projects share an environment lock to avoid concurrent edits to the same game configuration. Let the CLI manage the active configuration until the run ends. If termination or restoration fails, or the CLI is forcibly interrupted, inspect its recovery state rather than assuming the original list is already restored.

Transaction backups live in the tool-managed `RimDevToolsTransactions` directory beside the game configuration and are associated with a run ID. Use returned diagnostics and cleanup commands to inspect them. The tool handles their retention and cleanup.

### 6.4 Desktop isolation and recovery

Quick and auto default to the visible desktop unless configuration says otherwise. Use `--isolated-desktop` to request a private Windows desktop, or `--no-isolated-desktop` to force the visible one. An explicit flag overrides configuration. Manual mode defaults to the visible desktop for interaction, even when project isolation is enabled, unless explicitly overridden.

A separate desktop still shares the underlying game and configuration. Run game tests sequentially. After an interruption, inspect the state, preview recovery and perform cleanup once RimWorld is fully closed:

```powershell
.\.tools\RimDevTools.exe test cleanup --status
.\.tools\RimDevTools.exe test cleanup --dry-run
.\.tools\RimDevTools.exe test cleanup
```

## 7. Automated tests: coverage and execution

### 7.1 Select scopes, targets and batch limits

| Scope | Main coverage |
| --- | --- |
| `ui` | Supported information cards, bill interfaces and related content checks |
| `combat` | Weapons, turrets and supported apparel or pawn combat scenes |
| `view` | Three-direction and other appearance screenshots |
| `misc` | Currently focuses on ability pocket-map tests |
| `standard` | Default regular combination; does not implicitly include the specialized combat-pawn and skill-pocket-map tests |

Repeat `--scope` to select several scopes. Repeat `--defname` to request exact definitions. A DefName is the internal `<defName>` identifier, which can differ from a displayed label or product name. Recipe tests use RecipeDef names, ability tests use AbilityDef names, and PawnKind tests use the relevant PawnKindDef names.

```powershell
.\.tools\RimDevTools.exe test run --deploy --mode auto --scope combat --scope view --defname MyMod_ArmorCoat
```

Replace `MyMod_ArmorCoat` with an actual supported definition. Use `--target-package-id` to select another locally installed mod as the auto target.

**The default plan limit is 40 cases.** When discovery yields more candidates, enumeration runs sample uniformly at random without replacement across the candidates. Set a positive `--batch-size` to change the limit. Explicit `--defname` runs ignore that limit; requested DefNames that match no target make the result incomplete and fail validation. Older detailed scope values remain accepted as narrower compatibility entries.

A default pass therefore describes the selected cases. Plan targeted coverage for your changes, and review appearance screenshots yourself.

### 7.2 What happens during a run

Auto mode uses the game's quicktest entry point to prepare a disposable test map. The in-game runner waits for suitable map, window and runtime state, discovers targets, builds a plan, executes cases, records evidence and cleans up temporary scenes. Small dedicated test scenes are often called pocket maps.

Discovery reads loaded definitions belonging to the target PackageId. Recipe discovery can also include recipes that produce supported items from the target mod. Selection and batch rules determine what will actually execute; completion validation checks for missing planned results.

One definition may produce several cases: apparel might have an information card, a three-direction view and a damage scene; animals can have several life-stage views. Case counts and Def counts are therefore different measures.

### 7.3 Supported content at a glance

| Content | Preparation and execution | Main observations |
| --- | --- | --- |
| Building, item and plant information cards | Spawn a real instance, open its card, wait for drawing, capture and close | Instance validity, correct window target and window lifecycle |
| Terrain information cards | Apply terrain, inspect its information and restore the scene | Terrain assignment and window access |
| Pawn information cards | Generate a supported PawnKind and inspect applicable pages | Generation, stats, character or health page rendering |
| Gene and xenotype information | Inspect definitions when the relevant DLC capability is available | Correct definition binding and window lifecycle |
| Recipes and bills | Place a worktable, add a bill, prepare ingredients and execute an adapted output path | Bill binding, generated products, quantities, placement and cleanup |
| Hediffs and implants | Select a legal host and body part, prepare prerequisites and apply the condition | Construction, attachment and information access |
| Ranged weapons | Equip a shooter and prepare a pawn, cell or building target | Attack availability, projectile or direct effect, target changes |
| Melee weapons | Equip an attacker and maintain close-range attacks | Initial equipment, attack execution and damage |
| Turrets | Supply power, fuel or an operator as applicable, then add enemies | Operation, target acquisition, firing and damage |
| Apparel and combat pawns | Prepare a wearer or pawn and an attacker | First damage, health changes and graphics refresh |
| Abilities | Prepare an owner and suitable targets, then attempt casting | Ownership, command availability, cast progression and observed effects |
| Three-direction views | Prepare consistent targets facing west, south and north | Appearance, orientation, position, capture and cleanup |
| Static plant, building and terrain views | Arrange a display area and restore affected state afterward | Spawn and rendering paths, scene restoration and visual evidence |

### 7.4 Information cards for objects, terrain and pawns

Object-card tests create actual buildings, items or plants. Preparation handles applicable stuff materials, factions and planting terrain. The test checks that the instance remains valid and spawned, opens the corresponding card, waits for stable drawing, checks the target binding, captures a screenshot and closes the window. Blueprints, frames and corpses do not all use the ordinary-item path.

Window tracing distinguishes a normal close after capture from a card disappearing prematurely. These checks can find generation failures and window lifecycle errors; they describe this particular spawn-and-display path.

Terrain tests apply the target terrain and preserve affected state for restoration. Accessing terrain information does not establish that every pathfinding, construction or event interaction works.

Pawn-card tests use generated animals, mechanoids and other supported PawnKinds. Generation considers context such as factions and can use recorded retries or fallbacks. The test visits applicable stats, character and health pages and checks that they draw and close. Read generation notes to see which conditions were actually used. Gene and xenotype cards depend on the relevant Biotech capabilities and may be skipped when those are unavailable.

### 7.5 Recipes, bills, mech gestation and resurrection

Recipe tests place a compatible worktable, add the requested recipe as a real bill, open its configuration window and verify the binding. They also check window closure and temporary-bill cleanup.

For ordinary crafting recipes, the runner creates a worker, prepares ingredients using recipe and bill filters, invokes the core product-generation path, places products, checks kinds, quantities and stacks, and notifies the bill of completion. This directly exercises output generation. The full sequence of finding ingredients, hauling, reaching the work position and working over time needs separate gameplay checks; the debug helpers in Chapter 8 can help prepare those conditions.

Mech gestation and resurrection have dedicated adapters. They prepare the appropriate facility, bill and ingredients; resurrection also creates the required corpse through the death path. The runner advances the relevant processing cycle and checks that the result is a living, placeable mech pawn with readable health state.

Special pawn-producing or highly customized recipes may have no supported safe execution path. Inspect unsupported or skipped results and their reasons instead of assuming ordinary crafting completed.

### 7.6 Hediffs and implants

Supported Hediff tests cover health conditions, diseases, injuries and implants. The runner selects a legal host and body part using body structure, installation recipes and related hints, prepares prerequisites, adds the condition and checks information access.

Results record the host, body part and prerequisite source, or explain why no legal combination was found. These checks cover construction, attachment and display. Disease progression over several days, treatment balance and complex triggers require scenarios designed for those goals.

### 7.7 Ranged weapons

Ranged tests equip a shooter, verify equipment and attack availability, and prepare the target type required by the attack: pawn, cell or building. Positioning considers the actual range. The runner observes jobs or direct attack calls, projectiles, damage, downing, death and building hit points, with screenshots at relevant stages. Trackable projectiles can receive a centered presentation.

Interpret success according to the weapon path. Some legal firing, ground-targeting or special-projectile paths can accept projectile creation as evidence. A pass does not always establish the intended damage amount. For damage, explosion radius, hit effects or a particular Hediff, inspect the recorded state changes and add checks for that mechanism.

### 7.8 Melee weapons

Melee tests equip a pawn, spawn an enemy, arrange close contact and maintain attacks. They record initial equipment and later damage, downing or death. This distinguishes merely holding the weapon from actually attacking.

Initial equipment and final equipment are tracked separately. A pawn dropping a weapon after being downed is different from an initial equip failure. Read the stage records to distinguish equipment, availability, approach, damage and later-state failures.

### 7.9 Turrets

Turret tests prepare a pocket map with the turret, power and enemies. They supply fuel and an operator where applicable and maintain the operator's task. The runner checks validity, power, targeting or firing evidence and the enemy's health changes, with projectile or combat screenshots where supported.

This helps distinguish a turret defect from missing operating conditions. Custom ammunition, energy systems and independent weapon frameworks still depend on the available adapters.

### 7.10 Apparel, animals, mechanoids and alien races under attack

Combat-pawn tests prepare a suitable wearer or PawnKind, record body, equipment and health state, and expose it to attacks. They observe first damage, later state and graphics refresh.

These scenes help find errors caused by damage, body combinations or worn equipment. Their pass criteria do not establish that armor is strong enough or a unit is balanced. Armor comparisons, DPS and balance require suitable controls and repeated measurements.

### 7.11 Ability casting

Ability tests follow supported AbilityDefs. The runner first looks for a native owning PawnKind in the target mod. For mech-related abilities, it attempts the mechanitor and controlled-mech route. If no suitable native owner is available, it can use a recorded fallback test pawn. It then checks ownership and prepares enemy, friendly, self or cell targets as appropriate.

Execution may configure autocasting and issue direct cast requests, observing jobs, warmup and execution. To reach the ability logic, the isolated test can reset cooldowns and bypass some disabled-command or cast-availability checks; those interventions are recorded. Observations can include commands, target mode, cooldown, charges, health and Hediff changes.

A pass describes casting under those recorded conditions. Normal ability acquisition, insufficient-resource restrictions, progression and all targeting rules need their own checks. Features implemented solely through an independent third-party ability system are not automatically covered by AbilityDef tests.

### 7.12 Three-direction appearance views

The runner prepares three consistent targets, usually facing west, south and north for side, front and rear views. It controls appearance, position and orientation, waits for graphics to stabilize, pauses for capture and removes temporary rendering controls afterward.

Supported targets include humanoids, animals, mechanoids, alien races, apparel, weapons, hair, head types, body types, genes and xenotypes. Some expand by life stage. Apparel is worn and weapons are equipped, so captures show in-game presentation.

Gene views account for prerequisites, activation and consistent gene sets across the three pawns. Missing DLC, unsuitable hosts or no observable appearance effect should be interpreted using the diagnostic status. Automatic assertions check setup, orientation, capture and cleanup. You still need to inspect alignment, clipping, occlusion, colors and art quality yourself.

### 7.13 Static plant, building and terrain views

Plant views directly set seedling, intermediate and mature growth stages; the intermediate value considers harvestable growth where applicable. Building views spawn real objects, and terrain views lay out a display area.

The runner preserves affected terrain, roof, fog, pollution and temporarily moved objects, restores them afterward and checks restoration. Images show rendering at configured stages. Natural growth time, actual harvest yield and seasonal behavior require other checks.

### 7.14 Assertions, evidence and coverage

An assertion is a specific check, such as “the window is bound to this bill,” “the target lost health” or “all three pawns face the planned directions.” Results record failed assertions, operation IDs, stages and observation snapshots to locate the failing step.

Auto tests produce result JSON, separate Evidence files and applicable screenshots. Read the summary first, then the evidence and logs for relevant cases. Built-in result validation checks evidence schema and run identity as well as target and success fields. Group-combat and arena outputs use their own result models.

Review discovered, planned, executed, passed, failed, skipped, unsupported and missing counts. Discovery does not guarantee selection, selection does not guarantee execution, and execution can produce a diagnostic status. `passed_after_retry` means a retry occurred and deserves inspection. Some skipped, unsupported and compatibility statuses do not directly fail the entire run. **Top-level success does not establish complete functional coverage.**

Successful commands expose run paths under `result.artifacts`; failures expose them under `error.details.artifacts` when allocation has occurred. Common fields are `directoryPath`, `syncLogPath` and `autoTestOutputPath`. A reserved path does not guarantee a result file exists: quick/manual runs may have no AutoTest output, and early failures may have no allocated artifacts.

Use the actual returned paths. Read logs, results, screenshots and test saves only in their original tool-generated locations. Unless explicitly asked to export, do not create evidence, task or date-archive directories, redirect or save stdout/stderr, copy test artifacts, or write separate test reports. Do not modify original evidence. Retention and cleanup belong to the tool.

### 7.15 External XML: write your own regression scenarios

External XML is useful for specific expectations such as “casting this ability applies the requested condition” or “this loadout can complete the action.” The current backend requires 1.7. These files are test inputs, not game Defs: keep them outside `Defs`, `Patches` and published mod content.

An ordinary `<TestSuite>` contains Cases describing scenes, actors, preparation, waits and assertions. It supports pawn templates, exact loadouts, map conditions, explicit parameter matrices and screenshots. `<testDefName>` provides a stable test identifier. Start from the examples under `.agents/skills/rimworld-xml-test/assets` or `.claude/skills/rimworld-xml-test/assets`; modified test sources can live under a project directory such as `Tests/Scenarios`.

These commands use an existing packaged example. The first only validates it; the second launches the game:

```powershell
.\.tools\RimDevTools.exe test run --mode auto --scenario-path ".agents/skills/rimworld-xml-test/assets/basic.autotest.xml" --test-name Example_PawnAlive --validate-only
.\.tools\RimDevTools.exe test run --mode auto --scenario-only --scenario-path ".agents/skills/rimworld-xml-test/assets/basic.autotest.xml" --test-name Example_PawnAlive --batch-size 1 --timeout-seconds 360
```

`--validate-only` checks structure, fields, references, matrices and supported constraints without deployment, a game launch or run-artifact allocation. It requires a version-2 configuration targeting 1.7 but does not need a configured game or installed test host. Do not combine it with execution flags such as `--deploy`, batch size or timeout.

Read `result.validation`, or `error.details.validation` on validation failure. Diagnostics include source locations and correction suggestions where available. The compiler stops at the first error; correct it and validate again. Static success does not prove that Defs are loaded, reflected APIs exist or gameplay behaves correctly. Real runs perform the same preflight automatically.

`--scenario-only` selects only external regression cases; omit it to mix with ordinary scope/DefName tests. `--test-name` selects external rules, whereas `--defname` selects ordinary definitions. A scenario path can be a file or a directory; directories load only top-level `*.autotest.xml` files without recursion. Matrices can expand one rule into several cases, so check actual selection and execution counts.

Use the packaged `rimworld-xml-test` references for supported actions, assertions and coverage replacement rules. There is no `--mode xml`. Use your real PackageId and definitions for your own scenarios, and add `--deploy` when verifying source changes. A bundled example establishes only its own example conditions.

### 7.16 Group combat: compare two teams

Group combat uses `kind="groupCombat"` Cases in a `<TestSuite>` to configure teams A and B, maps, formations and combat rules. Teams can use fixed counts or `combatPowerBudget`; `SquadTemplates` support mixed rosters. Each team initially supports at most 200 pawns. This mode is for two-sided comparisons.

```powershell
.\.tools\RimDevTools.exe test run --mode groupcombat --scenario-path ".agents/skills/rimworld-xml-test/assets/groupcombat.autotest.xml" --test-name Example_WolvesVsBears --validate-only
.\.tools\RimDevTools.exe test run --mode groupcombat --scenario-path ".agents/skills/rimworld-xml-test/assets/groupcombat.autotest.xml" --test-name Example_WolvesVsBears --timeout-seconds 900
```

An explicit `--test-name` is required and can select a stable testDefName, full caseId or unique display name; repeat it for multiple selections. Do not use `--scenario-only`. Ordinary auto discovery never executes group-combat rules. Keep regression, group-combat and arena inputs separate to avoid mixing incompatible directory inputs.

The default native `assaultColony` controller retains normal target selection, attacks and mod behavior. Each round creates and cleans up its map, pawns and temporary factions. Multiple seeds and side swaps support comparisons.

Run sessions sequentially and allow configuration restoration between them. `--timeout-seconds` covers the whole run, including all rounds, generation and cleanup; adjust the example budget to your actual scenario. Read victory, execution, cleanup, damage attribution and measurement-quality results separately. A winner does not establish complete damage attribution or complete ability coverage.

### 7.17 Arena tournaments and presentation

Arena uses a separate `<ArenaSuite>`. Each Tournament defines entrants, a map, uniform count or combat-power budget, and combat and presentation rules. It supports 2–64 teams, using single PawnKinds or mixed-roster templates. Selection accepts stable `testDefName` values; group-combat case IDs, ordinary Cases and `--scenario-only` do not apply.

```powershell
.\.tools\RimDevTools.exe test run --mode arena --scenario-path ".agents/skills/rimworld-xml-test/assets/arena.autotest.xml" --test-name Example_AnimalArena --validate-only
.\.tools\RimDevTools.exe test run --mode arena --scenario-path ".agents/skills/rimworld-xml-test/assets/arena.autotest.xml" --test-name Example_AnimalArena
```

A tournament can include many fights, retries and presentation stages. Without an explicit timeout, the CLI estimates a bounded total budget. A manually specified budget must cover the full tournament. Victory quality scores preservation, suppression, exchange and speed for presentation and low-score extra matches in later odd-numbered rounds. Draws follow the configured retry policy; exhausted retries leave an unresolved outcome rather than randomly assigning a champion.

Common XML settings under `Tournament` are shown below. This is a fragment, not a complete tournament file:

```xml
<!-- Default: pair teams with similar counts; use random for seeded random pairings. -->
<Matchmaking mode="closestCount"/>
<!-- Independent switches; both default to false when omitted. -->
<Cleanup corpses="true" droppedItems="true"/>
```

Random matchmaking uses the tournament seed for reproducibility. Initial qualifying matches for an odd entrant count use the chosen matching mode; later odd advancement rounds retain the low-score extra-match rule.

When enabled, cleanup checks the battle map every 300 ticks. Corpses must be at least 300 ticks past death; dropped items must have been observed for at least 300 ticks. In-use targets and recognized corpses awaiting resurrection are deferred. Worn equipment, inventories and buildings are excluded, but pre-placed ground supplies are eligible. Disable the relevant switch when testing corpse or ground-supply mechanics.

The following behaviors apply automatically:

- **Reduced background simulation:** the session removes pawns from the disposable quicktest main map, including colonists, animals and contained pawns, and repeats maintenance every 250 ticks. This is separate from battle-map cleanup switches.
- **Noon resets:** each fight starts at local noon; after every 5,000 combat ticks, or two in-game hours, local time is reset to noon again. Combat elapsed time continues, and the original calendar offset is restored afterward.
- **Health bars:** individual damage and damage-taken rankings show overall health beside names; single-pawn type summaries also display it. Downed pawns use orange and dead pawns have empty bars. Shields are excluded.
- **Rotating mixed-roster previews:** opening cards cycle through multiple available compositions every real-time second. Manual arrows restart the timer; a single composition stays fixed.

Main-map cleanup affects the current disposable test environment. Calendar adjustments can affect mods that depend on absolute time. Opening cards and the final top-five showcase help inspect appearance and composition; use the original `arena_<runId>_Result.json`, generated report and screenshots to assess attempts, actual rosters, measurement quality, champion and completed cleanup. Group-combat and arena results are separate from ordinary regression pass rates.

## 8. Debug actions: preparing and checking content in game

### 8.1 Access and feedback

With developer mode enabled and a map open, LogSyncMod adds preparation helpers to the developer action menu. Window actions open configuration dialogs; rectangle actions let you select an area and process eligible pawns. These helpers work inside the game, while the CLI organizes the session from outside it.

The English names below describe their purpose; the exact label follows the game's active language.

| Action | How to use it | Eligible targets and persistence |
| --- | --- | --- |
| Test supplies | Filter items, select quantities and choose a map location | Spawns real supported items; named presets can be saved separately |
| Toggle full needs | Select pawns in a rectangle | Living, supported player-faction humanlike pawns with needs; select again to remove the state |
| Toggle skill and work boosts | Select pawns in a rectangle | Supported living player-faction humanlikes; select again to remove temporary overrides |
| Toggle psychic and mechanitor testing | Select pawns in a rectangle | Supported player humanlikes, subject to enabled DLC; select again to restore recorded state |
| Quick heal | Select pawns in a rectangle | Eligible living pawns across factions; healing is applied directly |
| Worktable developer ingredient button | Select a compatible worktable that already has a bill | Spawns one craft's ingredients for the top bill; each click adds another set |

Toggle actions report added, removed, skipped and failed counts. A mixed selection is processed per pawn: existing test states are removed while eligible unmarked pawns receive them. Check the feedback and resulting state when you want an entire group configured consistently.

Some actions skip enemies, animals, dead pawns or targets without required components. Quick heal has its own eligibility rules. A completed selection gesture alone does not establish that every selected target was changed.

### 8.2 Supplies and named presets

The supplies window searches labels, DefNames, mod names and PackageIds. It can filter by source mod, item category or third-party content. Select the desired items, adjust quantities and choose a spawn location.

Named presets can be created, copied, switched and deleted. The window summarizes item kinds, total quantities and source-mod distribution. Unspecified quantities generally default to three times the stack limit, while some common items in the default preset have explicit amounts.

Items absent from the current loaded mod list remain in the preset with a missing-item indication. Save preset edits before closing the window. Placement feedback reports actual spawned quantities; terrain or placement restrictions may prevent the full requested amount from appearing.

### 8.3 Toggle full needs

The needs toggle continuously fills existing supported needs for eligible player humanlikes, including applicable mood, food, rest, recreation, outdoors and mod-added needs. It is useful for observing work or interactions without interruptions.

Select again to release the override. Needs resume changing from their current full values; the action does not restore earlier hunger or tiredness levels.

### 8.4 Skills, work, movement and carrying boosts

The work-boost action provides effective skill level 20, a tenfold global work-speed multiplier, +300 movement speed and +1,000 carrying capacity. It also handles supported work and violence restrictions, enables work types and supplies an all-day work schedule. Mod-added skills and work types are considered where supported.

These are primarily temporary overrides that preserve underlying skills, restrictions, priorities and schedules. Existing enabled-work priorities are retained; temporarily enabled jobs use the relevant default priority. Backgrounds, traits, genes and actual body health still exist, so an arbitrary pawn is not guaranteed to perform every special mod job.

Use this to check whether a workflow can execute. Remove the boosts when assessing ordinary skill requirements, production time, efficiency or combat balance.

### 8.5 Psychic and mechanitor preparation

When Royalty is available, the toggle can prepare conditions for psylink level 6. When Biotech is available, it can supply a mechlink and +100 bandwidth. Either DLC can provide its own part independently.

The tool records what it adds or raises. Removing the test state restores the recorded psylink level and removes the relevant implants and powers that the tool added. Removing a tool-added mechlink can affect connected mechs according to normal game rules; include that transition in your observations when relevant.

### 8.6 Quick heal

Quick heal processes eligible living pawns across factions. It treats recognized wounds, old injuries, genuinely missing body parts, diseases and some drug-related conditions, then reports counts. Actual handling depends on the pawn's health and the recognition rules.

It distinguishes missing parts from implants rather than deleting every Hediff. Healing is immediate: using the action again continues treatment and does not restore old injuries.

### 8.7 Give a worktable its ingredients

On a supported worktable with a bill already added, the developer ingredient button prepares one craft's ingredients for the top bill and places them nearby. Selection considers recipe and bill filters, quantity conversions and fixed-ingredient rules. Repeated clicks add more material.

If no spawnable item satisfies an ingredient requirement, the tool reports the missing requirement. This can help diagnose a recipe whose material filters are impossible to satisfy.

### 8.8 Combine helpers into manual checks

Use `test run --mode manual` when you want the CLI to apply a selected mod list, prepare logs and launch a session for you. Add `--quick-start` for a disposable native quicktest game. Once inside, perform the debug actions and normal gameplay yourself.

For a **crafting workflow**, place the worktable, put the target bill first and spawn its ingredients. Then watch a pawn find and haul materials, reach the work position, perform the job and produce the expected output. This supplements the automated recipe check's direct product-generation path. Needs and work boosts can shorten a first feasibility check; remove them for normal requirements and timing. Suspended bills and unsuitable work assignments still need normal correction.

For **psychic or mechanitor interactions**, enable the appropriate DLC and preparation state, then obtain and use your mod's ability through its own workflow. The helper supplies baseline conditions; mod-specific research and acquisition prerequisites still need verification.

For **damage and healing**, inspect the health and combat observations you need before healing, using existing logs and screenshots where available. Healing changes the subject immediately; review that evidence before preparing another round, leaving the original files unchanged.

Debug actions are development conveniences. Their enhanced conditions should be reflected in any conclusion about normal gameplay.

## 9. Seven skills for AI-assisted development

### 9.1 What a skill is and where it lives

A skill is a task-specific handbook for an AI client, usually a `SKILL.md` file plus references. It explains when to use the workflow, what to read, which tools to call, how to interpret results and which project conventions apply.

For example, “add an ability” raises questions about acquisition, ownership, official mechanisms, required XML and C# changes, and verification. The development skills organize that research and implementation, while result validation examines whether failures come from the mod, test construction or environment.

Both game-version templates and the Tools Package include seven complete skills per client: `.agents/skills` for Codex and `.claude/skills` for Claude. Discovery depends on the client; confirm that the relevant skill is actually found and loaded. `references` directories hold detailed instructions, while files such as `agents/openai.yaml` provide display metadata and default prompts.

### 9.2 Responsibilities at a glance

| Skill | Main responsibility | Example request |
| --- | --- | --- |
| `rimdevtools` | Use the local CLI and project configuration correctly | Inspect, deploy, copy references, test, install the tool Mod or manage Workshop items |
| `rimsage` | Research official Defs, XML and C# | Confirm inheritance, API behavior and Harmony targets |
| `rimworld-plan-engineer` | Produce an implementation plan from evidence | Identify feasibility, edit locations, steps, risks and verification |
| `rimworld-development-engineer` | Implement changes and diagnose defects | Update C#, XML, assets and supporting metadata coherently |
| `rimworld-xml-test` | Author and run external XML tests | Effect regressions, exact loadouts, matrices, group combat and tournaments |
| `test-result-validation` | Interpret test results | Distinguish gameplay defects, test false positives and environment problems |
| `about-write-description` | Write a player-facing mod description | Update bilingual About text using implemented features |

### 9.3 Planning: rimworld-plan-engineer

This skill turns a broad idea into an actionable plan. It fits new ability systems, production chains, cross-version support and third-party integration assessments.

The plan retains applicable requirements, separates confirmed facts from assumptions and open questions, and identifies actual files, symbols or XML locations. Steps, verification, risks, alternatives and rollback considerations should match the project. It also gives a one-to-five-star difficulty estimate.

Its deliverable is a plan. The planning directory is configured by `project_paths.planning_docs`, defaulting to `策划案`. A suitable request is: “Write an implementation plan with edit locations, evidence, risks and verification; leave implementation for later.”

### 9.4 Official research: rimsage

This skill requires readable official references when new facts about Defs, APIs, source behavior or Harmony targets are needed. It moves from an exact Def to XML, follows implementation-class fields into C#, then inspects decisive members and calling context.

It also separates reference sources: read the current project locally, third-party mods from copied references and ILSpy output, and official content from the RimSage service. When required official material is unavailable, leave dependent conclusions unresolved. Local edits, translations, deployment and log diagnosis can continue when they introduce no new unverified official assumptions.

Example: “Research the official definitions and implementation first, then explain where this feature should connect.”

### 9.5 Implementation and diagnosis: rimworld-development-engineer

This skill reads the project, adds official or third-party research as needed and implements the complete requested behavior across C#, XML, translations, assets and metadata. A new item may require its Def, texture path, recipe and translations to change together.

For multiple game versions, the source branch and built project must match the current target. Deployment can include existing files for other declared versions without implying those versions were rebuilt or verified in this task. A new 1.7 DLL must not replace a 1.6 DLL.

Project references, output locations and target frameworks should remain consistent. Unwanted `.pdb` and `.deps.json` files should stay out of mod deployment. Preserve user changes and keep implementation within the requested scope.

The workflow chooses a small appropriate check. When runtime verification is needed, it normally starts with quick; explicit automated testing selects the relevant auto targets and scopes. Manual sessions are for user interaction. Example: “Implement this feature, run a startup check, then test these three pieces of equipment in combat and appearance scopes.”

### 9.6 Local operations: rimdevtools

This skill treats `mod_config.jsonc` as the project contract and uses the project's own CLI and command references. It diagnoses environment issues, previews deployment, resolves reference ambiguity, and reads final JSON and original artifacts.

It covers XML-only builds, deploying before source verification, default versus verbose output, shared test configuration and explicit reference-copy options. Workshop work follows the same discipline: check deployed content before upload, edit requested fields and interpret subscription or download state from actual results.

Example: “Check whether this project can build and deploy, using its configuration and environment diagnostics.”

### 9.7 Result interpretation: test-result-validation

Automated tests generate pawns, construct maps, control rendering and sometimes call internal behavior directly. A failed assertion can reveal a defect or an artificial state normal gameplay would not reach.

This skill reads original results, Evidence, logs and screenshots, then reconstructs the test using `references/test-flows.md`. It distinguishes **real gameplay defects**, **test false positives** and **environment problems**, preserving uncertainty when evidence is insufficient.

For a confirmed defect, the explanation should identify the object and DefName, cause, relevant errors, normal-game reproduction, consistency and priority. Conclusions stay in chat unless a saved validation document is explicitly requested, and they cite original artifact locations.

Example: “Review the failed cases and explain which affect normal play, with evidence and repair priorities.”

### 9.8 Player descriptions: about-write-description

This skill writes player-facing BBCode based on implemented functionality. Crafting, unlock conditions, compatibility and DLC prerequisites require support in metadata or implementation. Internal class and field names should become understandable usage information where possible.

The packaged convention places English first and Chinese second, separated by `------`, covering an overview, features, compatibility, prerequisites and credits. Its main edit target is `<description>` in `About/About.xml`.

Updating local About text and updating a Workshop language field are distinct operations. Use the Workshop edit workflow when remote publication is requested. Example: “Update the bilingual description from the features currently implemented, including usage conditions and limitations.”

### 9.9 Custom scenarios: rimworld-xml-test

This skill translates a verification goal into repeatable external XML. It selects real definitions, actors, loadouts, maps, actions and expected outcomes. Its references and examples cover regressions, matrices, three-direction views, mixed rosters, combat-power budgets and tournaments.

It ships in both game-version templates and the Tools Package, but scenario execution currently requires 1.7. The workflow distinguishes object preparation, action initiation and the expected effect actually occurring. Validate statically first, then run within the requested scope and review actual cases, diagnostics and limitations.

Ordinary Def/Patch editing, hot reload and built-in-only auto tests do not require custom scenarios. Example: “Write XML that checks whether this ability applies the expected Hediff after a hit.” Specify whether you want analysis, scenario authoring or execution.

### 9.10 Use the skills together

Planning establishes the route; research confirms official behavior; development implements the change; the CLI skill handles operations; XML testing supplies focused checks; result validation explains failures; and description writing updates player-facing text. Load the skills relevant to the task. Their files also let project authors review and maintain local working conventions.

## 10. RimSage MCP: researching official definitions and source

### 10.1 What MCP provides

MCP connects an AI client to external tools. Once connected, the client can discover tools, submit queries and receive results. In this workflow, a skill supplies the research method and MCP supplies the query channel.

RimSage is used for read-only research into official RimWorld and DLC Defs, XML and C# implementations. It can help explain inheritance, field usage and class members. Facts about your project or a third-party mod still come from local files.

### 10.2 Packaged connection files

Full templates include the following client configurations. The Tools Package includes skills but omits these connection files.

| File | Packaged settings |
| --- | --- |
| `.mcp.json` | Declares a `rimsage` HTTP service and default address for Claude Code |
| `opencode.jsonc` | Declares and enables the remote service and default address for opencode |
| `.pi/mcp.json` | Declares `directTools: true` and `lifecycle: eager` for the service in pi |

The first two use [the RimSage MCP endpoint](https://mcp.rimsage.com/mcp). The packaged pi file does not contain a service URL; its connection details must come from the actual client environment. Confirm that the client discovers and can call the tools. A configuration file alone does not prove connectivity.

These files configure access to a service; they do not install a complete official-source research server locally. A working local RimSage installation can also be used through suitable client configuration.

### 10.3 Six read-only research interfaces

| Interface in the packaged reference | Purpose |
| --- | --- |
| `search_defs` | Find official Defs by name or label, optionally narrowing by Def type |
| `get_def_details` | Read exact Def XML, including raw declarations or merged inheritance where supported |
| `search_source` | Search official XML and C# with applicable file filters |
| `read_file` | Read a returned source path and line range |
| `list_directory` | Inspect available directories and files on the service |
| `read_csharp_symbol` | Read a specific C# type or member with focused context |

Service versions can expose different tool names or parameters. Use the definitions actually discovered by the client. These are MCP interfaces; local project operations remain CLI commands.

### 10.4 Follow a Def into its implementation

A **Def** describes content such as an item, building, recipe, ability or condition. Its `<defName>` is an exact internal identifier, while its label is player-facing. Start with search to find candidates, then inspect the exact Def.

Read both the original declaration and applicable merged inheritance when you need to know where a value comes from. A short child definition may inherit most behavior from a parent. Follow XML fields that name implementation classes into the relevant C# types and methods.

Search snippets locate code; decisive methods and their surrounding context establish behavior. Check when a method runs, what it requires and how its result is used before choosing an implementation or patch target.

For an equipment effect, a typical sequence is: find the official Def, inspect XML and inheritance, follow implementation-class fields, read relevant members, then apply the findings locally and verify against your actual references and game version.

For a third-party mod, use `mod search`, `mod copy` and ILSpy to prepare local references. Return to RimSage when that implementation calls an unfamiliar official API.

### 10.5 Diagnose connection failures separately

Check whether the client loaded the service, whether configuration parses and whether the endpoint is reachable. The packaged skill describes fixing clear configuration problems and retrying once. If credentials, networking, a client restart or service-side work are needed, identify the missing reference material explicitly.

`doctor` checks local development and tool state; it does not probe RimSage network access or service processes. Local environment success and working MCP queries need separate confirmation. Research results still require validation against the current project build and runtime.

## 11. Steam Workshop: search, subscriptions and publishing

The `steamworkshops` family contains `search`, `subscribe`, `status`, `upload` and `edit`. It uses local Steam and the installed game's libraries. These operations require the appropriate Steam environment, but do not require starting RimWorld or supplying an API key.

### 11.1 Search, subscribe and inspect status

`search` accepts keywords, exact Workshop IDs or links. Keyword searches support pagination; `search next` continues the project's recent keyword search. `subscribe` requests a subscription and download; add `--wait` to wait for completion. `status` reports item information, permissions, subscription and download state, and Workshop-registered direct mod and DLC dependencies.

```powershell
.\.tools\RimDevTools.exe steamworkshops search "Harmony"
.\.tools\RimDevTools.exe steamworkshops search next
```

Keyword `--details` results contain Steam's official 255-byte description summary, cleaned for display. Exact-ID/link searches and status use the full returned description. Display cleanup removes actual BBCode, image blocks and bare URLs while retaining useful text; upload text is preserved.

Subscriptions do not enable mods or arrange load order. Workshop dependency queries do not recursively subscribe, download or resolve every About.xml version constraint. Compare local dependency metadata when investigating compatibility. `status --verbose` also exposes additional-preview indexes needed for image edits.

### 11.2 Upload the deployed content

**`upload` publishes the last successful full deployment.** It validates the deployment receipt, target files and project inputs to detect stale deployment. An XML hot synchronization does not substitute for a full deployment receipt.

```powershell
.\.tools\RimDevTools.exe steamworkshops upload --dry-run
```

After reviewing the plan, omit `--dry-run` to submit. Alternatively, `deploy --upload` deploys and uploads under the same lock. It cannot be combined with `--xml-hot`.

A first publication creates a private item for review. Uploading content to an existing item preserves its remote page metadata. For a change note, use `--change-note <text>` or `--change-note-file <UTF-8 file>`; the options are mutually exclusive. Missing or empty notes do not send a fixed default note.

### 11.3 Edit page metadata

`steamworkshops edit` changes titles, descriptions, visibility, the primary image and additional previews. Without field options, it synchronizes the local About description. With explicit field options, it edits the requested fields. It does not build, deploy or upload mod content.

For translated page text, use Steamworks language codes through `--text-language`, such as `english`, `schinese`, `tchinese`, `japanese` or `koreana`. `zh-CN` is not a valid substitute. One submission writes one language and retains others. A title-only or description-only edit reads the other field in that language and supplies it too, avoiding accidental clearing.

The following previews an English page-text update. Replace the title and file path with your actual publication inputs:

```powershell
.\.tools\RimDevTools.exe steamworkshops edit --title "My Mod" --description-file "About\WorkshopDescription.en.txt" --text-language english --dry-run
```

Use `--preview-file` for the main image. Additional images use repeatable `--add-preview <path>`, `--update-preview <index=path>` and `--remove-preview <index>`. Indexes are zero-based and come from `status --verbose`; all operations refer to the pre-edit indexes, and duplicate or conflicting indexes are rejected. Images must be PNG, JPG or GIF under 1 MiB. Additional-preview operations do not replace the main image.

### 11.4 Identity, ownership, timeouts and diagnostics

The numeric item ID is normally stored in `About/PublishedFileId.txt`. The tool uses it to identify an existing item and verifies ownership before writes. After a submission timeout, query remote state before retrying: the wait may have expired even though Steam completed the submission.

Use the read-only `workshop probe` command to diagnose standalone Steamworks initialization and optional item queries. Normal publishing uses `steamworkshops upload`; the old `workshop upload` prototype is disabled.

## 12. Command reference and common workflows

### 12.1 What each success establishes

| Successful operation | What it establishes |
| --- | --- |
| Deployment | This build and file synchronization completed |
| Quick test | This startup run reached its required signals; inspect the filtered-log summary too |
| Auto test | This run met result-validation and completeness rules; review passed, skipped and unsupported cases for actual coverage |
| Upload | The corresponding Workshop content submission completed |

Use feature goals, screenshots and normal gameplay observations to decide whether the release is ready for players.

### 12.2 Command map

Prefix the entries below with `.\.tools\RimDevTools.exe`. Angle-bracket values are placeholders to replace, not literal arguments.

| Goal | Command |
| --- | --- |
| Read tool and template versions | `--version` |
| Inspect the project and local environment | `doctor` |
| Expand all diagnostic checks | `doctor --verbose` |
| Check and preview a tool update | `update --check`, then `update --dry-run` |
| Install an update and read its final state | `update`, then `update status` |
| Build and synchronize to the game | `deploy` |
| Synchronize non-DLL files and request XML reload in a running session | `deploy --xml-hot` |
| Find an installed reference mod | `mod search <keyword>` |
| Copy a reference and decompile its assemblies | `mod copy <query>` |
| Inspect a local mod's prerequisites | `mod dependencies <query>` |
| Find local mods depending on a target | `mod dependents <query>` |
| Run a startup check | `test run --mode quick` |
| Launch an interactive test session | `test run --mode manual` |
| Run built-in automatic checks | `test run --mode auto` |
| Validate external XML without launching the game | `test run --mode auto --scenario-path <path> --test-name <name> --validate-only`; use the matching mode for group combat or arena |
| Run only external regression scenarios, on 1.7 | `test run --mode auto --scenario-only --scenario-path <path>` |
| Run a two-team comparison, on 1.7 | `test run --mode groupcombat --scenario-path <path> --test-name <name>` |
| Run an arena tournament, on 1.7 | `test run --mode arena --scenario-path <path> --test-name <name>` |
| Persist settings for manually launched sessions | `test settings` |
| Inspect interrupted-test state | `test cleanup --status` |
| Preview or perform configuration recovery | `test cleanup --dry-run`, then `test cleanup` |
| Install or update the bundled LogSyncMod | `mods install-tools` |
| Search Workshop online | `steamworkshops search <keyword-or-id-or-link>` |
| Subscribe and wait for download | `steamworkshops subscribe <id-or-link> --wait` |
| Read item, permission and download state | `steamworkshops status` |
| Upload the last full deployment | `steamworkshops upload` |
| Edit Workshop page metadata | `steamworkshops edit` |
| Deploy and upload in sequence | `deploy --upload` |
| Diagnose Steamworks without publishing | `workshop probe` |

### 12.3 Choose a workflow for the task

**First verification of an XML-only mod:** replace template identity and local paths, set `csproj_paths` to `[]`, run doctor and preview deployment. Edit Defs, recipes and translations, deploy, then run a quick startup check. Enter the game to confirm appearance and normal use. Query RimSage when official definitions need research.

**Researching a complex third-party mod:** resolve the exact local mod, copy it and let ILSpy prepare readable code. Read its XML and implementation from the reference directory; use MCP for unfamiliar official calls. Plan or implement your own changes once the mechanism is understood.

**Investigating an auto-test failure:** begin with the run's returned original artifact paths. Reconstruct the case using results, Evidence, logs and screenshots. Repair a gameplay defect after confirming normal-game reachability; address missing dependencies or unsuitable test construction when those are the cause.

**Preparing a release:** identify the required coverage, complete the relevant checks, review a full deployment and preview upload. Publish content and edit page metadata as requested. Keep local bilingual About text and Workshop language fields aligned with the content each is intended to maintain.

For everyday development, start with configuration, doctor, deployment and quick checks. Add manual or focused automated verification based on the feature. As the project grows, use references, ILSpy, exact DefName selection, XML hot reload, custom scenarios, combat comparisons, tournaments and the AI research workflows where they help answer a concrete development question.
