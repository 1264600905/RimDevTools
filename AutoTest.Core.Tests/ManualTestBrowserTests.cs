using LogSyncMod.AutoTest;
using LogSyncMod.AutoTest.Scenarios;
using System.Xml.Linq;

internal static class ManualTestBrowserTests
{
    internal static void Run(Action<bool, string> check)
    {
        var rifle = new ManualTestTarget { Label = "突击步枪", DefName = "Gun_AssaultRifle", ModName = "Core", PackageId = "ludeon.rimworld", DefType = "ThingDef" };
        var mod = new ManualTestTarget { Label = "测试步枪", DefName = "TestRifle", ModName = "Example Mod", PackageId = "example.mod", DefType = "ThingDef" };
        var instance = new ManualTestTarget { Label = "测试步枪 [当前实例]", DefName = "TestRifle", ModName = "Example Mod", PackageId = "example.mod", DefType = "ThingDef", InstanceId = "Thing_1" };
        var unsupported = new ManualTestTarget { Label = "未知", DefName = "Unknown", PackageId = "example.mod", DefType = "HediffDef" };
        var targets = new[] { rifle, mod, instance, unsupported };
        var testable = new HashSet<string> { rifle.Key, mod.Key, instance.Key };
        var selected = new HashSet<string> { rifle.Key };
        var filter = new ManualTestBrowserFilter();
        List<ManualTestTarget> Visible() => ManualTestBrowser.Filter(targets, filter, selected, testable);
        check(Visible().Count == 2 && Visible().All(t => t.InstanceId == null), "browser defaults to supported definitions, without duplicate map instances");
        filter.Source = "instances";
        check(Visible().Single() == instance, "map instance tab only lists real instances");
        filter.Source = "all"; filter.Search = "EXAMPLE  Rifle";
        check(Visible().Count == 2, "search matches multiple words across readable mod names and DefNames without case sensitivity");
        filter.Search = ""; filter.Source = "definitions"; filter.ThirdPartyOnly = true;
        check(Visible().Single() == mod, "third-party filter excludes Core and DLC definitions");
        filter.ThirdPartyOnly = false; filter.SelectedOnly = true;
        check(Visible().Single() == rifle, "selected-only filter uses stable target identities");
        filter.SelectedOnly = false; filter.TestableOnly = false; filter.Type = "HediffDef";
        check(Visible().Single() == unsupported, "unsupported definitions remain accessible when the capability filter is disabled");
        filter.Type = null; filter.Package = "EXAMPLE.MOD";
        check(Visible().Count == 2, "package filter compares package IDs case-insensitively");
        ManualTestBrowser.Select(selected, new[] { mod.Key, instance.Key }, true);
        ManualTestBrowser.Select(selected, new[] { mod.Key }, false);
        check(selected.SetEquals(new[] { rifle.Key, instance.Key }), "deselecting current results preserves hidden and instance selections");
        ManualTestBrowser.Select(selected, selected, false);
        check(selected.Count == 0, "clear selection snapshots a live set before removing entries");
        check(ManualTestBrowser.Range(targets, rifle.Key, instance.Key).SequenceEqual(new[] { rifle.Key, mod.Key, instance.Key }), "Shift selection includes both ends of the visible range");
        check(ManualTestBrowser.Range(targets, "stale", instance.Key).SequenceEqual(new[] { instance.Key }), "a stale Shift anchor selects only the clicked target");
        check(ManualTestBrowser.Range(targets, instance.Key, rifle.Key).Count() == 3, "Shift selection works in either direction");
        check(ManualTestBrowser.TypeLabel("PawnKindDef") == "角色 / 生物", "type filters show readable labels");

        var snapshot = TemporaryPawnData.Create(XElement.Parse("<PawnTemplate id='p'><basePawnKind>Colonist</basePawnKind></PawnTemplate>"), "Thing_Human1", null, null);
        var project = Directory.GetCurrentDirectory();
        var exported = TemporaryPawnData.ExportPathFor(project, snapshot, "../角色:测试");
        check(Path.GetDirectoryName(exported) == Path.Combine(project, ".tools", "ProjectData", "PawnTemplates") && !Path.GetFileName(exported).Contains(".."), "export names cannot escape the bound project's template directory");
        var other = TemporaryPawnData.Create(XElement.Parse(snapshot.TemplateXml), "Thing_Human2", null, null);
        check(TemporaryPawnData.ExportPathFor(project, other, "../角色:测试") != exported, "different selected pawns cannot overwrite each other even with identical names and loadouts");
        check(exported != TemporaryPawnData.PathFor(project), "export does not silently replace the active manual-test pawn");
    }
}
