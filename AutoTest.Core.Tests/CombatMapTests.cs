using System.Text.Json;
using System.Xml.Linq;
using LogSyncMod.AutoTest;
using LogSyncMod.AutoTest.Scenarios;

internal static class CombatMapTests
{
    internal static void Run(Action<bool, string> check)
    {
        foreach (var style in CombatMapCatalog.Styles)
        {
            foreach (var size in new[] { 40, 100, 140, 250 })
            {
                var map = CombatMapCatalog.Build(style.Id, size, size, 1729);
                check(map.Cells.All(c => c.X > 0 && c.Z > 0 && c.X < size - 1 && c.Z < size - 1), "battlefield geometry stays inside map: " + style.Id + "/" + size);
                check(map.Cells.Select(c => (c.X, c.Z)).Distinct().Count() == map.Cells.Count, "battlefield cells have one final terrain/object assignment: " + style.Id + "/" + size);
                check(style.Id == "open" || map.RouteCovers > 0 && map.Cells.Any(c => c.Thing == "Sandbags" && c.X > size * .3 && c.X < size * .7 && Math.Abs(c.Z - size / 2) < size * .2), "approach routes contain cover: " + style.Id + "/" + size);
                check(style.Id != "urban" || map.Buildings >= (size >= 80 ? 20 : 6), "urban streets contain several rows of buildings: " + size);
                check(JsonSerializer.Serialize(map, new JsonSerializerOptions { IncludeFields = true }) == JsonSerializer.Serialize(CombatMapCatalog.Build(style.Id, size, size, 1729), new JsonSerializerOptions { IncludeFields = true }), "battlefield seed is reproducible: " + style.Id + "/" + size);
            }
            var doc = ArenaWorkbench.Create(); doc.Schedule.Format = "groupcombat";
            doc.Schedule.Battlefield = style.Id; doc.Schedule.Width = 160; doc.Schedule.Height = 120;
            doc.Schedule.FeatureDensity = 1.2; doc.Schedule.SpawnClearance = 8; doc.Schedule.Temperature = style.Temperature;
            doc.Schedule.Ai = "assaultColony"; doc.Teams[0].Ai = "defendPoint"; doc.Teams[1].Ai = "lure";
            ArenaWorkbench.UpgradeUi(doc);
            var exported = XElement.Parse(ArenaWorkbench.CompileGroup(doc).Cases.Single().Xml);
            var scene = exported.Element("Scene"); var teams = exported.Element("Teams").Elements("Team").ToList();
            check((string)scene.Attribute("battlefield") == style.Id && (int)scene.Attribute("sizeX") == 160 && (int)scene.Attribute("sizeZ") == 120
                && (double)scene.Attribute("featureDensity") == 1.2 && (int)scene.Attribute("spawnClearance") == 8, "group UI exports its selected map parameters: " + style.Id);
            check((string)teams[0].Attribute("ai") == "defendPoint" && (string)teams[1].Attribute("ai") == "lure", "group UI preserves independent fortify/lure choices: " + style.Id);
            doc.Schedule.Format = "knockout";
            var tournament = ArenaWorkbench.Compile(doc).Tournaments.Single();
            check((string)XElement.Parse(tournament.Xml).Element("Scene").Attribute("battlefield") == style.Id, "arena uses the same UI/XML battlefield setting: " + style.Id);
        }
        var ice = new XElement("Scene", new XAttribute("battlefield", "glacier")); CombatMapCatalog.NormalizeScene(ice);
        check((double)ice.Element("Environment").Attribute("temperature") == -12, "XML glacier defaults to a cold environment");
        var explicitIce = XElement.Parse("<Scene battlefield='glacier'><Environment temperature='5'/></Scene>"); CombatMapCatalog.NormalizeScene(explicitIce);
        check((double)explicitIce.Element("Environment").Attribute("temperature") == 5, "explicit XML weather/temperature overrides survive map defaults");
        Reject(() => CombatMapCatalog.Validate("unknown", 1, 6), "unknown maps are rejected");
        Reject(() => CombatMapCatalog.Validate("cover", double.NaN, 6), "non-finite map density is rejected");
        Reject(() => CombatMapCatalog.Validate("cover", 2, 6), "unbounded obstacle density is rejected");
        Reject(() => CombatMapCatalog.Validate("cover", 1, 13), "spawn clearance is bounded");
        Reject(() => CombatMapCatalog.NormalizeScene(XElement.Parse("<Scene featureDensity='1'/>")), "map parameters cannot silently apply without a battlefield");
        var fixtures = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures"));
        var groupPath = Path.Combine(fixtures, "CombatMaps.autotest.xml");
        var groups = new[] { new ScenarioInput { Path = groupPath, Xml = File.ReadAllText(groupPath) } };
        check(ScenarioLoader.Compile(groups, true).Cases.Count == 5, "all new map fixtures compile through the group XML entry");
        var arenaPath = Path.Combine(fixtures, "ArenaMaps.autotest.xml");
        check(ArenaLoader.Compile(new[] { new ScenarioInput { Path = arenaPath, Xml = File.ReadAllText(arenaPath) } }, new[] { "Arena_MapsUrban", "Arena_MapsGlacier" }).Tournaments.Count == 2,
            "new maps compile through the tournament XML entry");
        Reject(() => ScenarioLoader.Compile(new[] { new ScenarioInput { Path = "invalid-map.xml", Xml = groups[0].Xml.Replace("featureDensity=\"1\"", "featureDensity=\"bad\"") } }, true), "invalid XML map density is rejected during preflight");
        Reject(() => ScenarioLoader.Compile(new[] { new ScenarioInput { Path = "invalid-map.xml", Xml = groups[0].Xml.Replace("spawnClearance=\"6\"", "spawnClearance=\"2.5\"") } }, true), "fractional spawn clearance is rejected during preflight");
        void Reject(Action action, string message)
        {
            try { action(); check(false, message); }
            catch (ScenarioConfigurationException) { check(true, message); }
        }
    }
}
