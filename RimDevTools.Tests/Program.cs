using RimDevTools;

var suite = new TestSuite();
await suite.RunAsync();

internal sealed class TestSuite
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "RimDevToolsTests", Guid.NewGuid().ToString("N"));
    private int _passed;

    public async Task RunAsync()
    {
        Directory.CreateDirectory(_temp);
        try
        {
            TestJsoncAndDiscovery();
            TestModListParser();
            await TestSettingsAndTransactionAsync();
            await TestDeployAndRefAsync();
            Console.WriteLine($"PASS {_passed} tests");
        }
        finally { if (Directory.Exists(_temp)) Directory.Delete(_temp, true); }
    }

    private void TestJsoncAndDiscovery()
    {
        var root = Path.Combine(_temp, "project"); Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "mod_config.jsonc"), "{\n // comments stay comments\n \"mod_name\": \"Demo//Name\",\n \"rimworld_dir\": \"C:\\\\Game\",\n \"min_active_mods\": [],\n}");
        var context = ProjectDiscovery.Find(root, out var error);
        Assert(context is not null && error is null && context.Config.ModName == "Demo//Name", "JSONC parses comments without corrupting // in strings");
        var child = Path.Combine(root, "source"); Directory.CreateDirectory(child);
        Assert(ProjectDiscovery.Find(child, out _)?.Root == root, "direct parent project discovery works");
        var grandchild = Path.Combine(child, "Core"); Directory.CreateDirectory(grandchild);
        Assert(ProjectDiscovery.Find(grandchild, out var missing) is null && missing?.Code == "PROJECT_NOT_FOUND", "project discovery does not climb past direct parent");
    }

    private void TestModListParser()
    {
        var root = Path.Combine(_temp, "mods-project"); var game = Path.Combine(_temp, "game"); Directory.CreateDirectory(Path.Combine(root, "About")); Directory.CreateDirectory(Path.Combine(game, "Mods", "Helper", "About")); Directory.CreateDirectory(Path.Combine(game, "Mods", "LogSync", "About"));
        File.WriteAllText(Path.Combine(root, "About", "About.xml"), "<ModMetaData><packageId>demo.current</packageId></ModMetaData>");
        File.WriteAllText(Path.Combine(game, "Mods", "Helper", "About", "About.xml"), "<ModMetaData><packageId>demo.helper</packageId></ModMetaData>");
        File.WriteAllText(Path.Combine(game, "Mods", "LogSync", "About", "About.xml"), "<ModMetaData><packageId>trigger.logsyncmod</packageId></ModMetaData>");
        File.WriteAllText(Path.Combine(root, "mod_config.jsonc"), $"{{ \"mod_name\": \"Demo\", \"rimworld_dir\": \"{Escape(game)}\", \"min_active_mods\": [\"demo.helper\"] }}");
        File.WriteAllText(Path.Combine(root, "mods.xml"), "<ModsConfigData><ignored><li>not.allowed</li></ignored><activeMods><li>demo.helper</li><li>DEMO.HELPER</li></activeMods></ModsConfigData>");
        var context = ProjectDiscovery.Find(root, out _)!;
        var mods = ModListParser.Load(context, Path.Combine(root, "mods.xml"), out var error);
        Assert(error is null && mods!.SequenceEqual(["demo.helper", "demo.current", "trigger.logsyncmod"], StringComparer.OrdinalIgnoreCase), "ModList reads only activeMods direct li, deduplicates, and inserts required mods");
    }

    private async Task TestSettingsAndTransactionAsync()
    {
        var root = Path.Combine(_temp, "settings-project"); var configDir = Path.Combine(_temp, "rim-config"); Directory.CreateDirectory(Path.Combine(root, "About")); Directory.CreateDirectory(configDir);
        File.WriteAllText(Path.Combine(root, "About", "About.xml"), "<ModMetaData><packageId>demo.current</packageId></ModMetaData>");
        File.WriteAllText(Path.Combine(root, "mod_config.jsonc"), "{ \"mod_name\": \"Demo\", \"log_keywords\": [\"Ready\"] }");
        var settings = Path.Combine(configDir, "Mod_LogSyncMod_LogSyncMod.xml"); var mods = Path.Combine(configDir, "ModsConfig.xml");
        File.WriteAllText(settings, "<Root><ModSettings><logPath>old</logPath></ModSettings></Root>"); File.WriteAllText(mods, "<ModsConfigData><activeMods /></ModsConfigData>");
        var previous = Environment.GetEnvironmentVariable("RIMDEVTOOLS_CONFIG_DIR"); Environment.SetEnvironmentVariable("RIMDEVTOOLS_CONFIG_DIR", configDir);
        try
        {
            var context = ProjectDiscovery.Find(root, out _)!;
            await SettingsEditor.UpdateAsync(context, "auto", true);
            var changed = File.ReadAllText(settings);
            Assert(changed.Contains("Ready") && changed.Contains("True"), "settings writer updates keywords and auto-test state");
            var transaction = await ConfigTransaction.CreateAsync(configDir, "run", [settings, mods]);
            File.WriteAllText(settings, "changed"); File.Delete(mods);
            await transaction.DisposeAsync();
            Assert(File.ReadAllText(settings) == changed && File.Exists(mods), "transaction restores exact original files");
            var recovery = await ConfigTransaction.CreateAsync(configDir, "recovery", [settings]);
            File.WriteAllText(settings, "tool-change"); await recovery.MarkWrittenAsync(settings);
            await TransactionRecovery.RecoverAsync(configDir);
            Assert(File.ReadAllText(settings) == changed, "interrupted transaction restores known tool-written state");
        }
        finally { Environment.SetEnvironmentVariable("RIMDEVTOOLS_CONFIG_DIR", previous); }
    }

    private async Task TestDeployAndRefAsync()
    {
        var root = Path.Combine(_temp, "deploy-project"); var game = Path.Combine(_temp, "deploy-game"); var cfg = Path.Combine(_temp, "deploy-config");
        Directory.CreateDirectory(Path.Combine(root, "About")); Directory.CreateDirectory(Path.Combine(root, "source")); Directory.CreateDirectory(Path.Combine(game, "Mods", "Demo"));
        File.WriteAllText(Path.Combine(root, "About", "About.xml"), "<ModMetaData><packageId>demo.deploy</packageId></ModMetaData>");
        File.WriteAllText(Path.Combine(root, "About", "About.html"), "filtered"); File.WriteAllText(Path.Combine(root, "About", "About.txt"), "kept");
        File.WriteAllText(Path.Combine(root, "source", "Demo.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        File.WriteAllText(Path.Combine(game, "Mods", "Demo", "PublishedFileId.txt"), "123"); File.WriteAllText(Path.Combine(game, "Mods", "Demo", "stale.txt"), "stale");
        File.WriteAllText(Path.Combine(root, "mod_config.jsonc"), $"{{ \"mod_name\": \"Demo\", \"rimworld_dir\": \"{Escape(game)}\", \"csproj_paths\": [\"source/Demo.csproj\"], \"sync_items\": [\"About\"], \"protected_files_regex\": \"^(PublishedFileId\\\\.txt)$\", \"timeouts\": {{ \"existing_game_exit\": 0, \"post_exit_delay\": 0 }} }}");
        var previous = Environment.GetEnvironmentVariable("RIMDEVTOOLS_CONFIG_DIR"); var previousCurrent = Environment.CurrentDirectory; Environment.SetEnvironmentVariable("RIMDEVTOOLS_CONFIG_DIR", cfg);
        try
        {
            var old = Environment.CurrentDirectory; Environment.CurrentDirectory = root;
            try
            {
                var exit = await DeployCommand.RunAsync(["deploy"]);
                var target = Path.Combine(game, "Mods", "Demo");
                Assert(exit == 0 && File.Exists(Path.Combine(target, "PublishedFileId.txt")) && !File.Exists(Path.Combine(target, "stale.txt")), "deploy preserves protected direct child and replaces stale target");
                Assert(File.Exists(Path.Combine(target, "About", "About.txt")) && !File.Exists(Path.Combine(target, "About", "About.html")), "deploy filters About html/svg files");
            }
            finally { Environment.CurrentDirectory = old; }

            var source = Path.Combine(game, "Mods", "RefMod", "About"); Directory.CreateDirectory(source); File.WriteAllText(Path.Combine(source, "About.xml"), "<ModMetaData><name>Reference Mod</name><packageId>demo.reference</packageId></ModMetaData>"); File.WriteAllText(Path.Combine(Path.GetDirectoryName(source)!, "readme.txt"), "reference");
            var refRoot = Path.Combine(root, "参考文档"); var refConfig = Path.Combine(root, "mod_config.jsonc");
            var text = File.ReadAllText(refConfig).Replace("\"Demo\"", "\"Demo\", \"workshop_paths\": [\"" + Escape(Path.Combine(game, "Mods")) + "\"]", StringComparison.Ordinal);
            File.WriteAllText(refConfig, text);
            Environment.CurrentDirectory = root;
            var pullExit = await RefCommand.PullAsync(["ref", "pull", "demo.reference"]);
            Assert(pullExit == 0 && File.Exists(Path.Combine(refRoot, "demo.reference", "readme.txt")), "ref pull stages and atomically installs a reference mod");
        }
        finally { Environment.CurrentDirectory = previousCurrent; Environment.SetEnvironmentVariable("RIMDEVTOOLS_CONFIG_DIR", previous); }
    }

    private static string Escape(string path) => path.Replace("\\", "\\\\");
    private void Assert(bool condition, string name) { if (!condition) throw new InvalidOperationException($"FAIL: {name}"); _passed++; Console.WriteLine($"PASS: {name}"); }
}
