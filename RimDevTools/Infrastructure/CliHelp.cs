namespace RimDevTools;

public static class CliHelp
{
    public static bool TryWrite(string command)
    {
        var text = command.ToLowerInvariant() switch
        {
            "help" => TopLevel,
            "doctor" => Doctor,
            "deploy" => Deploy,
            "mod" => Mod,
            "mod search" => ModSearch,
            "mod copy" => ModCopy,
            "mod dependencies" => ModDependencies,
            "mod dependents" => ModDependents,
            "test" => Test,
            "test run" => TestRun,
            "test settings" => TestSettings,
            "test cleanup" => TestCleanup,
            "mods" => Mods,
            "mods install-tools" => ModsInstallTools,
            "workshop" => Workshop,
            "workshop probe" => WorkshopProbe,
            "workshop upload" => WorkshopUpload,
            "steamworkshops" => SteamWorkshops,
            "steamworkshops upload" => SteamWorkshopsUpload,
            "steamworkshops edit" => SteamWorkshopsEdit,
            "steamworkshops search" => SteamWorkshopsSearch,
            "steamworkshops subscribe" => SteamWorkshopsSubscribe,
            "steamworkshops status" => SteamWorkshopsStatus,
            _ => null
        };
        if (text is null) return false;
        Console.WriteLine(text.Trim());
        return true;
    }

    public static string Path(string[] args)
    {
        if (args.Length == 0 || args[0] is "--help" or "-h") return "help";
        if (args[0] is "mod" or "test" or "mods" or "workshop" or "steamworkshops" && args.Length > 1 && !args[1].StartsWith('-'))
            return $"{args[0]} {args[1]}";
        return args[0];
    }

    private const string TopLevel = """
        RimDevTools - Windows RimWorld mod development automation CLI

        Usage:
          RimDevTools.exe <command> [options]
          RimDevTools.exe <command> --help

        Commands:
          doctor              Inspect project, game, tools, and transaction status.
          deploy              Build and transactionally update the current mod.
          mod search          Find local or Workshop reference mods.
          mod copy            Copy and decompile a reference mod.
          mod dependencies    List one mod's hard dependencies.
          mod dependents      List local mods depending on a target.
          test run            Run quick, manual, or automatic tests.
          test settings       Persist LogSync settings for manual game launches.
          test cleanup        Inspect or recover interrupted transactions.
          mods install-tools  Install the packaged LogSync tool mod.
          workshop probe      Probe standalone Steamworks without launching RimWorld or uploading.
          steamworkshops      upload, edit, search, subscribe, status through desktop Steam.

        Hot file sync:
          RimDevTools.exe deploy --xml-hot checks whether RimWorld is running. If so,
          it transactionally updates configured non-DLL files in place and requests a Def reload after
          a 3-second settle delay. If not, it performs a normal build and file-by-file deploy.

        Global options:
          -h, --help          Show top-level or command-specific help.
          --version           Print RimDevTools and packaged template versions as JSON; must be the only argument.
          --verbose           After a supported command, include detailed results and subprocess diagnostics.

        Project discovery:
          Project commands look for mod_config.jsonc in the current directory, then its
          direct parent. The release directory itself is the project template.
          --help, --version, and mods install-tools with --mods-path need no project discovery.

        Output contract:
          Final command results are one JSON object on stdout. Default output contains
          a concise summary; --verbose adds details and subprocess output. Warnings and
          errors remain visible. Progress and diagnostics are written to stderr.
          Help is stable plain text on stdout.

        Examples:
          RimDevTools.exe doctor [--verbose]
          RimDevTools.exe test run --mode quick
          RimDevTools.exe mod copy "Harmony" --deps --dry-run

        Exit codes:
          0 success; 2 invalid arguments/config; 3 missing dependency; 4 busy;
          5 task failure; 6 timeout; 7 game/test failure; 130 cancelled.
        """;

    private const string Workshop = """
        Inspect standalone Steamworks access without publishing content.

        Usage:
          RimDevTools.exe workshop probe [options]

        Commands:
          probe               Check Steam login, app id, and optional Workshop item query.
          upload              Disabled prototype; use steamworkshops upload with a deployment receipt.
        """;

    private const string SteamWorkshops = """
        Steam Workshop commands through the installed game's native libraries.
        Usage:
          RimDevTools.exe steamworkshops <upload|edit|search|subscribe|status> [options]
        Use each subcommand's --help. No API key or game launch is required.
        Runtime metadata belongs to the project's ignored .tools/ProjectData directory.
        """;

    private const string SteamWorkshopsUpload = """
        Validate and upload the last successful full deployment; automatically create or update.
        Usage:
          RimDevTools.exe steamworkshops upload [--project <path>] [--dry-run]
        Options:
          --project <path>             Project root (otherwise current directory/direct parent).
          --dry-run                    Verify receipt, complete target hashes and project inputs.
          --timeout-seconds <seconds>  Upload wait budget; default 1800.
          --verbose                    Include diagnostics.
          -h, --help                   Show help.
        Requires .tools/ProjectData/deployment-receipt.json from a full deploy.
        Assets required but not verified block publication. No hash bypass or --create.
        Timeouts return current/Steam update times and unknown outcome; no persistent retry block.
        """;

    private const string SteamWorkshopsEdit = """
        Edit explicit metadata fields; with no field options, synchronize About description only.
        Usage:
          RimDevTools.exe steamworkshops edit [options]
        Options:
          --project <path>             Project root.
          --id <id-or-link>            Explicit item; otherwise project's PublishedFileId.txt.
          --description-file <path>    UTF-8 description (BBCode preserved).
          --visibility <value>         private, public, friends-only, unlisted.
          --preview-file <path>        Primary preview image.
          --dry-run                    Inspect requested fields and owner permissions.
          --timeout-seconds <seconds>  Submit wait budget; default 1800.
          --verbose                    Include diagnostics.
          -h, --help                   Show help.
        Never builds, deploys or sends content. Only verified owners can write.
        """;

    private const string SteamWorkshopsSearch = """
        Search RimWorld Workshop; only the requested user page is printed.
        Usage:
          RimDevTools.exe steamworkshops search <keyword|id|link|next> [options]
        Options:
          --project <path>             Project root for the most recent search context.
          --page <n>                   Simple: 25 items, pages 1-3; details: 10 items, pages 1-5.
          --details                    Add Steam's official default 255-byte description summary.
          --verbose                    Include diagnostics.
          -h, --help                   Show help.
        search next reuses the latest keyword/mode/page for this project (10-minute cache).
        next cannot combine with --page/--details. Narrow keywords to exceed page limits.
        ID/link requests return full description and status; no page/details flags allowed.
        Native 50-item pages are sliced in official order, without discarding intervening items.
        """;

    private const string SteamWorkshopsSubscribe = """
        Subscribe and request a download; never enables or sorts mods.
        Usage:
          RimDevTools.exe steamworkshops subscribe <id-or-link> [options]
        Options:
          --project <path>             Optional project root for game discovery.
          --wait                       Wait for download completion.
          --timeout-seconds <seconds>  Wait budget.
          --verbose                    Include diagnostics.
          -h, --help                   Show help.
        --wait requires this helper's matching App ID/item download callback before disk access.
        Without --wait, a queued download can continue in Steam after this helper exits.
        No recursive dependencies, mod activation/reordering, or automatic unsubscription.
        """;

    private const string SteamWorkshopsStatus = """
        Read live environment, full item description, permissions, statistics and download state.
        Usage:
          RimDevTools.exe steamworkshops status [id-or-link] [options]
        Options:
          --project <path>             Optional project root.
          --wait                       Observe existing download; never subscribes/requests download.
          --timeout-seconds <seconds>  Wait budget.
          --verbose                    Include diagnostics.
          -h, --help                   Show help.
        Default identity is About/PublishedFileId.txt; packageId is separate.
        Contributor identification is deferred; only verified owners may write.
        --wait observes current flags/installation in this invocation; never requests a download.
        Unknown statistics/time stay null. Installed with NeedsUpdate is not complete.
        """;

    private const string WorkshopUpload = """
        This prototype upload entry is disabled.

        Usage:
          RimDevTools.exe steamworkshops upload [--dry-run]
          RimDevTools.exe steamworkshops upload --help

        The current entry always checks the full deployment receipt and project inputs,
        automatically creates a private item or updates its binding, and preserves metadata.
        """;

    private const string WorkshopProbe = """
        Probe Steamworks in a child process without launching RimWorld or uploading content.

        Usage:
          RimDevTools.exe workshop probe [--published-file-id <id>] [--timeout-seconds <seconds>] [--verbose]

        Options:
          --published-file-id <id>   Query one positive Workshop item id; omitted means session-only.
          --timeout-seconds <seconds>  Native probe timeout, 1 to 120 seconds; default 15.
          --verbose                 Include captured native subprocess diagnostics on stderr.
          -h, --help                Show this help.

        Requirements:
          A valid project, a local Windows x64 Steam API DLL, and a running logged-on Steam client.
          Reads the native DLL from the configured game installation; creates no probe files.

        Output:
          JSON with initialization/login status, actual app id and Steam id, UGC availability,
          optional query result, and RimWorld process ids before/after. This is a read-only probe;
          it does not verify item ownership or actual upload permission and never submits an update.

        Example:
          RimDevTools.exe workshop probe --published-file-id 2009463077
        """;

    private const string Doctor = """
        Inspect the current project and all RimDevTools capabilities without mutation.

        Usage:
          RimDevTools.exe doctor [--verbose]

        Options:
          --verbose           Include the complete ordered check list.
          -h, --help          Show this help.

        Checks:
          Configured game version and its matching 1.6 or 1.7+ layout/target framework, Steam libraries
          and RimWorld app 294100, About.xml/Preview.png, game/Harmony assemblies, Harmony DLL version,
          Unity shader-bundle compatibility, Git/worktree, ILSpy, optional test-host readiness,
          lock, and recoverable transactions. LogSync and AutoTestRunner are not project prerequisites.

        Output:
          JSON ready/issueCount/warningCount summary. By default checks contains items that
          need action plus resolved Assembly-CSharp.dll and 0Harmony.dll paths for AI context;
          --verbose includes the complete ordered check list.

        Common errors:
          PROJECT_NOT_FOUND, CONFIG_INVALID, CONFIG_VERSION_UNSUPPORTED.
        """;

    private const string Deploy = """
        Build and transactionally update the current mod in RimWorld/Mods.

        Usage:
          RimDevTools.exe deploy [--sync-source] [--xml-hot | --upload] [--dry-run] [--verbose]

        Options:
          --sync-source       Include source/ in this deployment.
          --upload            After a full deploy, verify its receipt and upload under the same lock.
                              Mutually exclusive with --xml-hot; --dry-run only prints both stages.
          --xml-hot           If RimWorld is running, transactionally update configured non-DLL files in place,
                              wait at least 3 seconds, then request a Def reload. If it is not
                              running, perform the normal build and file-by-file deployment.
          --dry-run           Print the immutable deployment plan without mutation.
          --verbose           Include the complete plan and build/Unity output.
          -h, --help          Show this help.

        Output:
          By default, reports the target, build count, sync items, action counts, and XML
          reload status, and file-commit counts. --verbose includes all paths and plan details. Normal deployment
          keeps the target directory in place, checks changed files for active users, and
          records rollback data before committing.

        Configuration:
          mod_name, rimworld_dir, csproj_paths, sync_items, sync_source, protected_files_regex,
          timeouts, and shader_bundle. Shader settings are required only when enabled.
          Pure XML mods may set csproj_paths to an empty array; deploy then skips C# builds.
          RimWorld 1.7 uses installed official libraries to bake load-folder Assets.zip files.
          RimWorld 1.6 skips baking and does not require modern asset libraries.

        Example:
          RimDevTools.exe deploy --dry-run

        Common errors:
          PROJECT_DEPLOYMENT_LOCATION_INVALID, ENVIRONMENT_BUSY, GAME_EXIT_TIMEOUT, BUILD_FAILED, SHADER_BUILD_FAILED,
          SYNC_ITEM_NOT_FOUND, DEPLOY_TARGET_BUSY, DEPLOY_MANIFEST_REPLACE_FAILED, DEPLOY_MANIFEST_WRITE_FAILED,
          DEPLOY_RECOVERY_REQUIRED, DEPLOY_RECOVERY_FAILED, DEPLOY_ROLLBACK_FAILED, DEPLOY_FAILED.
        """;

    private const string Mod = """
        Search, copy, and inspect local RimWorld reference mods.

        Usage:
          RimDevTools.exe mod <search|copy|dependencies|dependents> ...

        Subcommands:
          search <keyword>        Find local candidates by mod metadata.
          copy <query>            Copy and decompile a reference mod.
          dependencies <query>    List hard dependencies.
          dependents <query>      List local mods depending on the target.

        Run 'RimDevTools.exe mod <subcommand> --help' for details.
        """;

    private const string ModSearch = """
        Search RimWorld Mods and configured Workshop directories.

        Usage:
          RimDevTools.exe mod search <keyword> [--fuzzy] [--verbose]

        Arguments:
          keyword             Name, packageId, author, or source folder text.

        Options:
          --fuzzy             Also allow subsequence matching.
          --verbose           Include all candidates with source paths and dependency metadata.
          -h, --help          Show this help.

        Example:
          RimDevTools.exe mod search "联合重工" --fuzzy

        Output:
          Default JSON lists up to 10 candidates with a total count; --verbose includes
          all candidates, source paths, and dependency metadata.

        Common errors:
          PROJECT_NOT_FOUND, INVALID_ARGUMENT.
        """;

    private const string ModCopy = """
        Copy a local mod into project_paths.reference_docs and decompile its assemblies.

        Usage:
          RimDevTools.exe mod copy <query> [options]

        Arguments:
          query               Absolute mod directory, exact packageId/name/folder, or match.

        Options:
          --version <value>   Assembly version folder; defaults to game_version.
          --deps              Pull the complete hard-dependency closure.
          --include-ignored   Do not apply pull_ignore_folders.
          --fuzzy             Allow fuzzy query resolution.
          --overwrite-existing Replace all preflighted existing targets.
          --dry-run           Print the copy/decompile plan without mutation.
          --verbose           Include the full dependency plan and ILSpy output.
          -h, --help          Show this help.

        Target naming:
          Final and staging folders use the sanitized About.xml <name>, not packageId.
          Ambiguous queries and sanitized-name collisions fail before any copy.

        Output:
          Default dry-run lists up to 10 targets and reports the total; --verbose includes
          the full dependency closure, ignored folders, and assembly plan.

        Example:
          RimDevTools.exe mod copy KindSeal.LOL --deps --dry-run

        Common errors:
          MOD_NOT_FOUND, MOD_AMBIGUOUS, MOD_NAME_INVALID, DEPENDENCY_CYCLE,
          TARGET_COLLISION, TARGET_CONFLICT, COPY_FAILED.
        """;

    private const string ModDependencies = """
        List version-aware hard dependencies for one local mod.

        Usage:
          RimDevTools.exe mod dependencies <query> [--version <value>] [--fuzzy] [--verbose]

        Options:
          --version <value>   Dependency metadata version; defaults to game_version.
          --fuzzy             Allow fuzzy query resolution.
          --verbose           Include the resolved mod's full metadata.
          -h, --help          Show this help.

        Example:
          RimDevTools.exe mod dependencies KindSeal.LOL --version 1.6

        Output:
          Default JSON lists up to 20 dependencies with a total count; --verbose includes all.

        Common errors:
          MOD_NOT_FOUND, MOD_AMBIGUOUS, INVALID_ARGUMENT.
        """;

    private const string ModDependents = """
        List local mods whose hard dependencies include the target.

        Usage:
          RimDevTools.exe mod dependents <query> [--version <value>] [--fuzzy] [--verbose]

        Options:
          --version <value>   Dependency metadata version; defaults to game_version.
          --fuzzy             Allow fuzzy query resolution.
          --verbose           Include full metadata for the target and dependents.
          -h, --help          Show this help.

        Example:
          RimDevTools.exe mod dependents aoba.deadmanswitch.core

        Output:
          Default JSON lists up to 20 dependents with a total count; --verbose includes all.

        Common errors:
          MOD_NOT_FOUND, MOD_AMBIGUOUS, INVALID_ARGUMENT.
        """;

    private const string Test = """
        Configure LogSync or run RimWorld tests.

        Usage:
          RimDevTools.exe test <run|settings|cleanup> ...

        Subcommands:
          run                 Run quick, manual, automatic, or XML testing.
          settings            Persist LogSync path and keyword settings without launching.
          cleanup             Safely recover interrupted configuration transactions.

        Run 'RimDevTools.exe test <subcommand> --help' for details.
        """;

    private const string TestRun = """
        Run a transactional RimWorld test and restore all global configuration.

        Usage:
          RimDevTools.exe test run [options]

        Options:
          --deploy                    Build and transactionally update before testing.
          --sync-source               Include source/; requires --deploy.
          --verify-workshop-assets    1.7 quick test: temporarily remove the five known sample sources,
                                      require this run's package-reading signal, and restore sources.
          --mode <value>              quick (default), manual, or auto.
          --active-mods-file <path>   External ModsConfig XML activeMods list.
          --timeout-seconds <n>       Positive wait limit; defaults to timeouts.max_wait.
          --target-package-id <id>    Auto target; defaults to current project.
          --scope <value>             Repeatable auto scope; defaults to Standard.
          --defname <value>           Repeatable auto-mode DefName allowlist.
          --batch-size <n>            Positive auto random-case limit; defaults to 40.
          --isolated-desktop          Force a private Windows desktop for this run.
          --no-isolated-desktop       Force the current visible desktop for this run.
          --verbose                  Include the active mod list, additions, and deploy diagnostics.
          -h, --help                  Show this help.

        Deployment behavior:
          test run exits an existing game before deploying and starting tests. --deploy uses
          the normal build and file-by-file deployment; --xml-hot is standalone deploy only.

        Isolation precedence:
          Explicit CLI flag > isolated_desktop project config > built-in false for
          quick/auto. Manual defaults to the visible desktop regardless of config,
          unless --isolated-desktop is explicitly supplied.

        Examples:
          RimDevTools.exe test run --mode quick --timeout-seconds 120
          RimDevTools.exe test run --mode auto --scope combat --scope view --defname MyMod_ArmorCoat

        Scope values:
          standard, ui, combat, view, misc.
          Legacy detailed values remain accepted with their original narrow behavior.

        Batch behavior:
          Auto runs, including Standard, plan at most --batch-size cases.
          If more candidates are available, targets are sampled uniformly at random without replacement.
          Explicit --defname runs ignore the batch limit.

        Screenshot behavior:
          Automatic tests enable screenshots by default.

        Output:
          Default JSON includes runId, mode, target, mod/addition counts, isolation source,
          signal, result, and dependency warnings. --verbose includes full mods/additions.
          After a run directory is allocated, result.artifacts (success) or
          error.details.artifacts (failure) includes the absolute directoryPath,
          syncLogPath, and autoTestOutputPath. Quick/auto runs also return
          result.logSummary or error.details.logSummary with errorCount and warningCount
          from the selected sync log, plus availability, completeness, and truncation status.
          Some modes do not generate AutoTest files.
          Read the original artifacts; do not create extra archives unless requested.

        Common errors:
          MODE_INVALID, MOD_NOT_RESOLVED, ENVIRONMENT_BUSY, GAME_EXIT_TIMEOUT,
          TEST_TIMEOUT, GAME_EXITED_EARLY, AUTO_RESULT_FAILED, TRANSACTION_RESTORE_FAILED.
        """;

    private const string TestSettings = """
        Persist the LogSync output path and configured keywords without launching RimWorld.

        Usage:
          RimDevTools.exe test settings [--verbose]

        Options:
          --verbose           Include the effective log paths and keyword count.
          -h, --help          Show this help.

        Behavior:
          Waits for RimWorld to exit, acquires the shared environment lock, and atomically
          writes the project log path, log_keywords, and AutoTest paths/options. Use it to
          prepare a manually launched game session. Unlike test run, it leaves the settings
          in place and does not restore the previous values.

        Output:
          After a run directory is allocated, result.artifacts (success) or
          error.details.artifacts (failure) includes the absolute directoryPath,
          syncLogPath, and autoTestOutputPath without requiring --verbose.

        Common errors:
          ENVIRONMENT_BUSY, GAME_EXIT_TIMEOUT, SETTINGS_UPDATE_FAILED.
        """;

    private const string TestCleanup = """
        Inspect or safely recover interrupted RimDevTools configuration transactions.

        Usage:
          RimDevTools.exe test cleanup [--dry-run] [--status] [--verbose]

        Options:
          --dry-run           Report recoverable transactions without changing files.
          --status            Report lock, transaction, game, and deployment-artifact status only.
          --verbose           Include full lock details and transaction/artifact paths; default output still names an active lock owner.
          -h, --help          Show this help.

        Behavior:
          --status never acquires the write lock or changes files. Mutation acquires the
          shared environment lock and refuses to run while RimWorld is active. ModsConfig.xml
          recovery restores only the original activeMods list.

        Common errors:
          ENVIRONMENT_BUSY, GAME_RUNNING_CLEANUP_REFUSED, TRANSACTION_CLEANUP_INCOMPLETE.
        """;

    private const string Mods = """
        Install or inspect the tool mods packaged beside RimDevTools.

        Usage:
          RimDevTools.exe mods install-tools [options]

        The command manages the packaged trigger.logsyncmod component.
        """;

    private const string ModsInstallTools = """
        Install the packaged LogSyncMod into RimWorld/Mods.

        Usage:
          RimDevTools.exe mods install-tools [--mods-path <path>] [--dry-run] [--verbose]

        Options:
          --mods-path <path>  Explicit RimWorld Mods directory. Without it, resolve rimworld_dir.
          --dry-run           Validate components and print the immutable install plan only.
          --verbose           Include source paths, hashes, and complete component records.
          -h, --help          Show this help.

        Behavior:
          Validates the packaged manifest, versions, packageIds, and SHA-256 content hashes.
          Identical targets are skipped, older targets are atomically upgraded, newer targets
          are preserved, and same-version content conflicts are rejected. PublishedFileId.txt
          is preserved during upgrades.

        Common errors:
          MODS_PATH_NOT_FOUND, SOURCE_HASH_MISMATCH, SOURCE_VERSION_MISMATCH,
          DUPLICATE_PACKAGE_ID, INSTALL_PLAN_CONFLICT, TOOL_MOD_INSTALL_FAILED.
        """;
}
