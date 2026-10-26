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
          deploy              Build and atomically deploy the current mod.
          mod search          Find local or Workshop reference mods.
          mod copy            Copy and decompile a reference mod.
          mod dependencies    List one mod's hard dependencies.
          mod dependents      List local mods depending on a target.
          test run            Run quick, manual, automatic, or XML tests.
          test settings       Persist LogSync settings for manual game launches.
          test cleanup        Inspect or recover interrupted transactions.
          mods install-tools  Install the packaged LogSync tool mod.

        Hot file sync:
          RimDevTools.exe deploy --xml-hot copies configured non-DLL files in place
          without stopping the game or building assemblies. It does not reload XML
          already loaded by the running game; see 'RimDevTools.exe deploy --help'.

        Global options:
          -h, --help          Show top-level or command-specific help.
          --version           Print the RimDevTools version as JSON; must be the only argument.

        Project discovery:
          Project commands look for mod_config.jsonc in the current directory, then its
          direct parent. The release directory itself is the project template.
          --help, --version, and mods install-tools with --mods-path need no project discovery.

        Output contract:
          Final command results are one JSON object on stdout. Progress and diagnostics
          are written to stderr. Help is stable plain text on stdout.

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
          .NET Framework 4.8 (preferred) or 4.7.2 build prerequisites, Steam libraries and RimWorld app 294100,
          About.xml/Preview.png, real game/Harmony assemblies, Harmony DLL version, Unity,
          Git/worktree, required mods, ILSpy, LogSync, AutoTest, packaged tool mods, lock,
          and recoverable transactions.

        Output:
          JSON ready/issueCount/warningCount summary. By default checks contains items that
          need action plus resolved Assembly-CSharp.dll and 0Harmony.dll paths for AI context;
          --verbose includes the complete ordered check list.

        Common errors:
          PROJECT_NOT_FOUND, CONFIG_INVALID, CONFIG_VERSION_UNSUPPORTED.
        """;

    private const string Deploy = """
        Build and atomically deploy the current mod into RimWorld/Mods.

        Usage:
          RimDevTools.exe deploy [--sync-source] [--xml-hot] [--dry-run]

        Options:
          --sync-source       Include source/ in this deployment.
          --xml-hot           Copy configured non-DLL files in place without stopping the game or building assemblies.
                              Does not reload XML already loaded by the game.
          --dry-run           Print the immutable deployment plan without mutation.
          -h, --help          Show this help.

        Configuration:
          mod_name, rimworld_dir, csproj_paths, sync_items, sync_source, protected_files_regex,
          timeouts, and shader_bundle. Shader settings are required only when enabled.
          Pure XML mods may set csproj_paths to an empty array; deploy then skips C# builds.

        Example:
          RimDevTools.exe deploy --dry-run

        Common errors:
          PROJECT_DEPLOYMENT_LOCATION_INVALID, ENVIRONMENT_BUSY, GAME_EXIT_TIMEOUT, BUILD_FAILED, SHADER_BUILD_FAILED,
          SYNC_ITEM_NOT_FOUND, DEPLOY_FAILED.
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
          RimDevTools.exe mod search <keyword> [--fuzzy]

        Arguments:
          keyword             Name, packageId, author, or source folder text.

        Options:
          --fuzzy             Also allow subsequence matching.
          -h, --help          Show this help.

        Example:
          RimDevTools.exe mod search "联合重工" --fuzzy

        Output:
          JSON candidate list containing name, packageId, source directory, and deps.

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
          -h, --help          Show this help.

        Target naming:
          Final and staging folders use the sanitized About.xml <name>, not packageId.
          Ambiguous queries and sanitized-name collisions fail before any copy.

        Example:
          RimDevTools.exe mod copy KindSeal.LOL --deps --dry-run

        Common errors:
          MOD_NOT_FOUND, MOD_AMBIGUOUS, MOD_NAME_INVALID, DEPENDENCY_CYCLE,
          TARGET_COLLISION, TARGET_CONFLICT, COPY_FAILED.
        """;

    private const string ModDependencies = """
        List version-aware hard dependencies for one local mod.

        Usage:
          RimDevTools.exe mod dependencies <query> [--version <value>] [--fuzzy]

        Options:
          --version <value>   Dependency metadata version; defaults to game_version.
          --fuzzy             Allow fuzzy query resolution.
          -h, --help          Show this help.

        Example:
          RimDevTools.exe mod dependencies KindSeal.LOL --version 1.6

        Common errors:
          MOD_NOT_FOUND, MOD_AMBIGUOUS, INVALID_ARGUMENT.
        """;

    private const string ModDependents = """
        List local mods whose hard dependencies include the target.

        Usage:
          RimDevTools.exe mod dependents <query> [--version <value>] [--fuzzy]

        Options:
          --version <value>   Dependency metadata version; defaults to game_version.
          --fuzzy             Allow fuzzy query resolution.
          -h, --help          Show this help.

        Example:
          RimDevTools.exe mod dependents aoba.deadmanswitch.core

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
          --deploy                    Deploy before testing.
          --sync-source               Include source/; requires --deploy.
          --xml-hot                   Copy configured non-DLL files in place; requires --deploy.
          --mode <value>              quick (default), manual, auto, or xml.
          --active-mods-file <path>   External ModsConfig XML activeMods list.
          --timeout-seconds <n>       Positive wait limit; defaults to timeouts.max_wait.
          --target-package-id <id>    Auto/XML target; defaults to current project.
          --scope <value>             Repeatable auto/xml scope; defaults to Standard.
          --defname <value>           Repeatable auto-mode DefName allowlist.
          --batch-size <n>            Positive auto/xml random-case limit; defaults to 40.
          --capture-screenshots       Enable AutoTest screenshots for auto/xml only.
          --isolated-desktop          Force a private Windows desktop for this run.
          --no-isolated-desktop       Force the current visible desktop for this run.
          -h, --help                  Show this help.

        Hot file sync:
          --xml-hot copies files only; it does not reload XML already loaded by RimWorld.
          test run still exits an existing game before starting the test process.

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

        Output:
          JSON runId, mode, target, mods/additions, isolation source, signal, result, and
          non-blocking dependency warnings for enabled Mods whose hard parents are disabled.

        Common errors:
          MODE_INVALID, MOD_NOT_RESOLVED, ENVIRONMENT_BUSY, GAME_EXIT_TIMEOUT,
          TEST_TIMEOUT, GAME_EXITED_EARLY, AUTO_RESULT_FAILED, TRANSACTION_RESTORE_FAILED.
        """;

    private const string TestSettings = """
        Persist the LogSync output path and configured keywords without launching RimWorld.

        Usage:
          RimDevTools.exe test settings

        Options:
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
          RimDevTools.exe test cleanup [--dry-run] [--status]

        Options:
          --dry-run           Report recoverable transactions without changing files.
          --status            Report lock, transaction, game, and deployment-artifact status only.
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
          RimDevTools.exe mods install-tools [--mods-path <path>] [--dry-run]

        Options:
          --mods-path <path>  Explicit RimWorld Mods directory. Without it, resolve rimworld_dir.
          --dry-run           Validate components and print the immutable install plan only.
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
