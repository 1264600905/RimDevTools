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
        }
        finally { Environment.SetEnvironmentVariable("RIMDEVTOOLS_CONFIG_DIR", previous); }
    }

    private static string Escape(string path) => path.Replace("\\", "\\\\");
    private void Assert(bool condition, string name) { if (!condition) throw new InvalidOperationException($"FAIL: {name}"); _passed++; Console.WriteLine($"PASS: {name}"); }
}
