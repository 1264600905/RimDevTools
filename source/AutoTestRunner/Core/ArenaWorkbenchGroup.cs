using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using LogSyncMod.AutoTest.Scenarios;

namespace LogSyncMod.AutoTest
{
    public static partial class ArenaWorkbench
    {
        public static void ValidateEvent(ArenaWorkbenchDocument document)
        { if (document.Schedule.Format == "groupcombat") CompileGroup(document); else Compile(document); }
        public static ScenarioPlan CompileGroup(ArenaWorkbenchDocument document, bool freezeSeed = false)
        {
            var root = ExportGroup(document, true);
            if (freezeSeed && document.Schedule.Seed == -1) root.Element("Case").SetAttributeValue("seed", RandomNumberGenerator.GetInt32(int.MaxValue));
            var xml = root.ToString(); using var sha = SHA256.Create();
            var input = new ScenarioInput { Path = "ArenaWorkbench.groupcombat.autotest.xml", Xml = xml, Sha256 = Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(xml))).ToLowerInvariant() };
            var plan = ScenarioLoader.Compile(new[] { input }, true); ScenarioLoader.SelectRules(plan, new[] { TestName }); return plan;
        }
        public static XElement ExportGroup(ArenaWorkbenchDocument document, bool enabledOnly = true)
        {
            var teams = document.Teams.Where(t => t.Enabled && !t.IsEmpty).ToList();
            if (enabledOnly && teams.Count != 2) throw new ScenarioConfigurationException("群战测试需启用恰好两支编队；多队晋级请使用擂台赛。");
            var clone = Copy(document); clone.Schedule.Format = "knockout"; clone.Teams.RemoveAll(t => !t.Enabled || t.IsEmpty);
            var source = Export(clone, enabledOnly); var s = document.Schedule;
            var tournament = source.Element("Tournament"); var actors = new XElement("Actors"); var names = new XElement("Teams");
            var entrants = tournament.Element("Entrants").Elements().ToList();
            for (var i = 0; i < entrants.Count; i++)
            {
                var entrant = entrants[i]; var team = i == 0 ? "A" : "B";
                var selectedAi = teams[i].Ai;
                names.Add(new XElement("Team", new XAttribute("id", team), new XAttribute("name", (string)entrant.Attribute("name") ?? team),
                    selectedAi == "inherit" ? null : new XAttribute("ai", selectedAi)));
                var actor = new XElement("PawnGroup", new XAttribute("id", (string)entrant.Attribute("id")), new XAttribute("team", team),
                    new XAttribute("originX", i == 0 ? 10 : 96), new XAttribute("originZ", 45), new XAttribute("formation", "grid"), new XAttribute("columns", 15), new XAttribute("spacing", 2));
                if (entrant.Attribute("squad") != null) actor.Add(new XAttribute("squad", (string)entrant.Attribute("squad")));
                else { actor.Add(new XElement("basePawnKind", (string)entrant.Attribute("pawnKind"))); if (entrant.Attribute("xenotype") != null) actor.Add(new XElement("Xenotype", new XAttribute("def", (string)entrant.Attribute("xenotype")))); }
                actor.Add(entrant.Attribute("budget") != null ? new XAttribute("combatPowerBudget", (string)entrant.Attribute("budget")) : new XAttribute("count", (string)entrant.Attribute("count")));
                actor.Add(new XAttribute("costMode", (string)entrant.Attribute("costMode") ?? "native")); actors.Add(actor);
                var estimatedCount = teams[i].Mode == "combatPower" ? 200 : teams[i].MemberCount;
                var extent = PawnGroupFormation.Extents(new XElement("PawnGroup", new XAttribute("count", Math.Max(1, estimatedCount)), new XAttribute("formation", "grid"), new XAttribute("columns", 15), new XAttribute("spacing", 2)), true);
                actor.SetAttributeValue("originX", i == 0 ? Math.Max(1, s.Width / 5 - extent.x / 2) : Math.Min(s.Width - extent.x - 2, s.Width * 4 / 5 - extent.x / 2));
                actor.SetAttributeValue("originZ", Math.Max(1, s.Height / 2 - extent.z / 2));
            }
            var c = new XElement("Case", new XAttribute("id", "main"), new XAttribute("kind", "groupCombat"), new XAttribute("name", s.Name), new XAttribute("seed", s.Seed), new XAttribute("timeoutRealSeconds", s.Timeout),
                new XElement("testDefName", TestName), new XElement(tournament.Element("Scene")), names, actors, new XElement("Steps"),
                new XElement("GroupCombat", new XAttribute("durationSeconds", s.Duration), new XAttribute("timeSpeed", s.Speed), new XAttribute("aiPolicy", s.Ai), new XElement("End", new XAttribute("confirmationTicks", s.ConfirmationTicks))),
                new XElement(tournament.Element("Output")));
            return new XElement("TestSuite", new XAttribute("schemaVersion", 1), new XAttribute("id", "manual.groupcombat"), new XAttribute("gameVersion", "1.7"),
                new XElement(source.Element("Templates")), new XElement(source.Element("SquadTemplates")), c);
        }
    }
}
