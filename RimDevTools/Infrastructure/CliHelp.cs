namespace RimDevTools;

public static class CliHelp
{
    public static bool TryWrite(string command)
    {
        var text = command.ToLowerInvariant() switch
        {
            "help" => TopLevel,
            "init" => Init,
            "doctor" => Doctor,
            "deploy" => Deploy,
            "ref" => Ref,
            "ref search" => RefSearch,
            "ref pull" => RefPull,
            "ref deps" => RefDeps,
            "ref related" => RefRelated,
            "test" => Test,
            "test run" => TestRun,
            "test settings" => TestSettings,
            "data" => Data,
            "data refresh" => DataRefresh,
            "data status" => DataStatus,
            _ => null
        };
        if (text is null) return false;
        Console.WriteLine(text.Trim());
        return true;
    }

    public static string Path(string[] args)
    {
        if (args.Length == 0 || args[0] is "--help" or "-h") return "help";
        if (args[0] is "ref" or "test" or "data" && args.Length > 1 && !args[1].StartsWith('-'))
            return $"{args[0]} {args[1]}";
        return args[0];
    }

    private const string TopLevel = """
        RimDevTools - Windows RimWorld mod development automation CLI

        Usage:
          RimDevTools.exe <command> [options]
          RimDevTools.exe <command> --help

        Commands:
          init <version>       Create a project from the packaged components template.
          doctor              Inspect project, game, tools, transactions, and database status.
          deploy              Build and atomically deploy the current mod.
          ref                 Search, pull, and inspect local reference mods.
          test                Configure or run quick, manual, automatic, and XML tests.
          data                Refresh or inspect the project RimSearcher database.

        Global options:
          -h, --help          Show top-level or command-specific help.
          --version           Print component versions as JSON.

        Project discovery:
          Project commands look for mod_config.jsonc in the current directory, then its
          direct parent. init, --help, and --version do not require an existing project.

        Output contract:
          Final command results are one JSON object on stdout. Progress and diagnostics
          are written to stderr. Help is stable plain text on stdout.

        Examples:
          RimDevTools.exe doctor
          RimDevTools.exe test run --mode quick
          RimDevTools.exe data refresh --timeout-seconds 180
          RimDevTools.exe ref pull "Harmony" --deps

        Exit codes:
          0 success; 2 invalid arguments/config; 3 missing dependency; 4 busy;
          5 task failure; 6 timeout; 7 game/test/export failure; 130 cancelled.
        """;

    private const string Init = """
        Create a RimWorld mod project from a packaged components template.

        Usage:
          RimDevTools.exe init <version> --output <project-root>

        Arguments:
          version             Currently supported: 1.6.

        Options:
          --output <path>     Required project root. Target must not exist or must be empty.
          -h, --help          Show this help.

        Behavior:
          Reads components/ProjectTemplate/manifest.json beside RimDevTools.exe and copies
          components/ProjectTemplate/<version> verbatim. Shared tooling is installed once
          from the executable directory as .tools, excluding components. Only missing empty
          directories declared by the manifest are created. The output root directly contains
          mod_config.jsonc, 1.6, and .tools as siblings. Do not pass a config-file, version,
          .tools, or source path. No existing project is required.

        Example:
          RimDevTools.exe init 1.6 --output D:\RimWorldMods\MyMod

        Common errors:
          VERSION_UNSUPPORTED, INIT_TARGET_NOT_EMPTY, INIT_TEMPLATE_MISSING,
          INIT_TEMPLATE_INVALID, INIT_FAILED.
        """;

    private const string Doctor = """
        Inspect the current project and all RimDevTools capabilities without mutation.

        Usage:
          RimDevTools.exe doctor

        Options:
          -h, --help          Show this help.

        Checks:
          Project JSONC, RimWorld paths, dotnet/csproj, deployment, ILSpy, LogSync,
          AutoTest, RimSearcher CLI/DataMod, database integrity/version, environment lock,
          and recoverable transactions.

        Output:
          JSON capability checks with available/missing/invalid/disabled/busy status.

        Common errors:
          PROJECT_NOT_FOUND, CONFIG_INVALID, CONFIG_VERSION_UNSUPPORTED.
        """;

    private const string Deploy = """
        Build and atomically deploy the current mod into RimWorld/Mods.

        Usage:
          RimDevTools.exe deploy [--sync-source] [--dry-run]

        Options:
          --sync-source       Include source/ in this deployment.
          --dry-run           Print the immutable deployment plan without mutation.
          -h, --help          Show this help.

        Configuration:
          mod_name, rimworld_dir, csproj_paths, sync_items, protected_files_regex,
          timeouts, and shader_bundle. Shader settings are required only when enabled.

        Example:
          RimDevTools.exe deploy --dry-run

        Common errors:
          ENVIRONMENT_BUSY, GAME_EXIT_TIMEOUT, BUILD_FAILED, SHADER_BUILD_FAILED,
          SYNC_ITEM_NOT_FOUND, DEPLOY_FAILED.
        """;

    private const string Ref = """
        Search, pull, and inspect local RimWorld reference mods.

        Usage:
          RimDevTools.exe ref <search|pull|deps|related> ...

        Subcommands:
          search <keyword>    Find local candidates.
          pull <query>        Copy and decompile a reference mod.
          deps <query>        List hard dependencies.
          related <query>     List local mods depending on the target.

        Run 'RimDevTools.exe ref <subcommand> --help' for details.
        """;

    private const string RefSearch = """
        Search RimWorld Mods and configured Workshop directories.

        Usage:
          RimDevTools.exe ref search <keyword> [--fuzzy]

        Arguments:
          keyword             Name, packageId, author, or source folder text.

        Options:
          --fuzzy             Also allow subsequence matching.
          -h, --help          Show this help.

        Example:
          RimDevTools.exe ref search "联合重工" --fuzzy

        Output:
          JSON candidate list containing name, packageId, source directory, and deps.

        Common errors:
          PROJECT_NOT_FOUND, INVALID_ARGUMENT.
        """;

    private const string RefPull = """
        Copy a local mod into 参考文档 and decompile its assemblies.

        Usage:
          RimDevTools.exe ref pull <query> [options]

        Arguments:
          query               Absolute mod directory, exact packageId/name/folder, or match.

        Options:
          --version <value>   Assembly version folder; defaults to game_version.
          --deps              Pull the complete hard-dependency closure.
          --all               Do not apply pull_ignore_folders.
          --fuzzy             Allow fuzzy query resolution.
          --force             Replace all preflighted existing targets.
          -h, --help          Show this help.

        Target naming:
          Final and staging folders use the sanitized About.xml <name>, not packageId.
          Ambiguous queries and sanitized-name collisions fail before any copy.

        Example:
          RimDevTools.exe ref pull KindSeal.LOL --deps --force

        Common errors:
          MOD_NOT_FOUND, MOD_AMBIGUOUS, MOD_NAME_INVALID, DEPENDENCY_CYCLE,
          TARGET_COLLISION, TARGET_CONFLICT, PULL_FAILED.
        """;

    private const string RefDeps = """
        List version-aware hard dependencies for one local mod.

        Usage:
          RimDevTools.exe ref deps <query> [--version <value>] [--fuzzy]

        Options:
          --version <value>   Dependency metadata version; defaults to game_version.
          --fuzzy             Allow fuzzy query resolution.
          -h, --help          Show this help.

        Example:
          RimDevTools.exe ref deps KindSeal.LOL --version 1.6

        Common errors:
          MOD_NOT_FOUND, MOD_AMBIGUOUS, INVALID_ARGUMENT.
        """;

    private const string RefRelated = """
        List local mods whose hard dependencies include the target.

        Usage:
          RimDevTools.exe ref related <query> [--version <value>] [--fuzzy]

        Options:
          --version <value>   Dependency metadata version; defaults to game_version.
          --fuzzy             Allow fuzzy query resolution.
          -h, --help          Show this help.

        Example:
          RimDevTools.exe ref related aoba.deadmanswitch.core

        Common errors:
          MOD_NOT_FOUND, MOD_AMBIGUOUS, INVALID_ARGUMENT.
        """;

    private const string Test = """
        Configure LogSync or run RimWorld tests.

        Usage:
          RimDevTools.exe test <run|settings> ...

        Subcommands:
          run                 Run quick, manual, automatic, or XML testing.
          settings            Persist LogSync path and keyword settings without launching.

        Run 'RimDevTools.exe test <subcommand> --help' for details.
        """;

    private const string TestRun = """
        Run a transactional RimWorld test and restore all global configuration.

        Usage:
          RimDevTools.exe test run [options]

        Options:
          --deploy                    Deploy before testing.
          --sync-source               Include source/; requires --deploy.
          --mode <value>              quick (default), manual, auto, or xml.
          --mods-file <path>          External ModsConfig XML activeMods list.
          --timeout-seconds <n>       Positive wait limit; defaults to timeouts.max_wait.
          --target-package-id <id>    Auto/XML target; defaults to current project.
          --capture-screenshots       Enable AutoTest screenshots for auto/xml only.
          --isolated-desktop          Force a private Windows desktop for this run.
          --no-isolated-desktop       Force the current visible desktop for this run.
          -h, --help                  Show this help.

        Isolation precedence:
          Explicit CLI flag > isolated_desktop project config > built-in true for
          quick/auto/xml. Manual defaults to the visible desktop regardless of config,
          unless --isolated-desktop is explicitly supplied.

        Examples:
          RimDevTools.exe test run --mode quick --timeout-seconds 120
          RimDevTools.exe test run --mode auto --target-package-id aoba.deadmanswitch.core --capture-screenshots

        Output:
          JSON runId, mode, target, mods/additions, isolation source, signal, and result.

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
          Acquires the shared environment lock and performs an atomic settings write.
          Unlike test run, this explicit command does not restore the previous settings.

        Common errors:
          ENVIRONMENT_BUSY, GAME_EXIT_TIMEOUT, SETTINGS_UPDATE_FAILED.
        """;

    private const string Data = """
        Refresh or inspect the project-scoped RimSearcher Def database.

        Usage:
          RimDevTools.exe data <refresh|status> ...

        Subcommands:
          refresh             Launch RimWorld and export a validated SQLite database.
          status              Inspect database integrity, version, and export metadata.

        Run 'RimDevTools.exe data <subcommand> --help' for details.
        """;

    private const string DataRefresh = """
        Launch RimWorld, export defs.db through DataMod, validate it, and restore config.

        Usage:
          RimDevTools.exe data refresh [options]

        Options:
          --mods-file <path>          External ModsConfig XML activeMods list.
          --output <path>             Override rimsearcher_db_path for this run.
          --timeout-seconds <n>       Positive wait limit; defaults to timeouts.max_wait.
          --isolated-desktop          Force a private Windows desktop for this run.
          --no-isolated-desktop       Force the current visible desktop for this run.
          -h, --help                  Show this help.

        Isolation precedence:
          Explicit CLI flag > isolated_desktop project config > built-in true.

        Example:
          RimDevTools.exe data refresh --timeout-seconds 180

        Success requires:
          Matching runId result and log signal, valid path/size, SQLite integrity,
          component version, game/language metadata, mod hashes, and config restoration.

        Common errors:
          ENVIRONMENT_BUSY, EXPORT_REQUEST_CONFLICT, DATA_REFRESH_TIMEOUT,
          EXPORT_SIGNAL_MISMATCH, DATABASE_INVALID, VERSION_MISMATCH.
        """;

    private const string DataStatus = """
        Inspect the configured RimSearcher database without launching or mutation.

        Usage:
          RimDevTools.exe data status

        Options:
          -h, --help          Show this help.

        Output:
          JSON path source, existence/size/time, integrity, user/component versions,
          export runId/game/language, requested/effective mod hashes, loaded mods, and
          refreshRecommended reason codes.

        Common errors:
          DATABASE_PATH_MISSING, PROJECT_NOT_FOUND, CONFIG_INVALID.
        """;
}
