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
            _ => null
        };
        if (text is null) return false;
        Console.WriteLine(text.Trim());
        return true;
    }

    public static string Path(string[] args)
    {
        if (args.Length == 0 || args[0] is "--help" or "-h") return "help";
        if (args[0] is "mod" or "test" or "mods" && args.Length > 1 && !args[1].StartsWith('-'))
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
          test run            Run quick, manual, automatic, or XML tests.
          test settings       Persist LogSync settings for manual game launches.
          test cleanup        Inspect or recover interrupted transactions.
          mods install-tools  Install the packaged LogSync tool mod.

        Hot file sync:
          RimDevTools.exe deploy --xml-hot checks whether RimWorld is running. If so,
          it transactionally updates configured non-DLL files in place and requests a Def reload after
          a 3-second settle delay. If not, it performs a normal build and file-by-file deploy.

        Global options:
          -h, --help          Show top-level or command-specific help.
          --version           Print the RimDevTools version as JSON; must be the only argument.
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
          lock, and recoverable transactions. Test mods and AutoTest XML are not project prerequisites.

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
          RimDevTools.exe deploy [--sync-source] [--xml-hot] [--dry-run] [--verbose]

        Options:
          --sync-source       Include source/ in this deployment.
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

        Example:
          RimDevTools.exe deploy --dry-run

        Common errors:
          PROJECT_DEPLOYMENT_LOCATION_INVALID, ENVIRONMENT_BUSY, GAME_EXIT_TIMEOUT, BUILD_FAILED, SHADER_BUILD_FAILED,
          SYNC_ITEM_NOT_FOUND, DEPLOY_TARGET_BUSY, DEPLOY_RECOVERY_REQUIRED, DEPLOY_RECOVERY_FAILED, DEPLOY_ROLLBACK_FAILED, DEPLOY_FAILED.
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
          --mode <value>              quick (default), manual, auto, or xml.
          --active-mods-file <path>   External ModsConfig XML activeMods list.
          --timeout-seconds <n>       Positive wait limit; defaults to timeouts.max_wait.
          --target-package-id <id>    Auto/XML target; defaults to current project.
          --scope <value>             Repeatable auto/xml scope; defaults to Standard.
          --defname <value>           Repeatable auto-mode DefName allowlist.
          --batch-size <n>            Positive auto/xml random-case limit; defaults to 40.
          --isolated-desktop          Force a private Windows desktop for this run.
          --no-isolated-desktop       Force the current visible desktop for this run.
          --verbose                  Include the active mod list, additions, and deploy diagnostics.
          -h, --help                  Show this help.

        Deployment behavior:
          test run exits an existing game before deploying and starting tests. --deploy uses
          the normal build and file-by-file deployment; --xml-hot is standalone deploy only.

        Isolation precedence:
          Explicit CLI flag > isolated_desktop project config > built-in false for
          quick/auto/xml. Manual defaults to the visible desktop regardless of config,
          unless --isolated-desktop is explicitly supplied.

        Examples:
          RimDevTools.exe test run --mode quick --timeout-seconds 120
          RimDevTools.exe test run --mode auto --scope combat --scope view --defname MyMod_ArmorCoat

        Scope values:
          standard, ui, combat, view, misc.
          Legacy detailed values remain accepted with their original narrow behavior.

        Batch behavior:
          Auto/XML runs, including Standard, plan at most --batch-size cases.
          If more candidates are available, targets are sampled uniformly at random without replacement.
          Explicit --defname runs ignore the batch limit.

        Screenshot behavior:
          Automatic tests enable screenshots by default. XML-driven tests follow each spec's captureScreenshots setting.

        Output:
          Default JSON includes runId, mode, target, mod/addition counts, isolation source,
          signal, result, and dependency warnings. --verbose includes full mods/additions.

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
