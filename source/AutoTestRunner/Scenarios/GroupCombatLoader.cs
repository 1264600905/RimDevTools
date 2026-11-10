#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace LogSyncMod.AutoTest.Scenarios
{
    public static partial class ScenarioLoader
    {
        // Imports are read exactly once, by the CLI. The game receives their frozen XML and
        // provenance, never paths to reread during a battle or a repeated experiment.
        public static void ResolveGroupPawnFiles(List<ScenarioInput> inputs)
        {
            foreach (var input in inputs)
            {
                using var reader = XmlReader.Create(new StringReader(input.Xml), ReaderSettings());
                var root = XElement.Load(reader);
                foreach (var template in root.Element("Templates")?.Elements("PawnTemplate").Where(t => t.Attribute("file") != null).ToList() ?? new List<XElement>())
                {
                    if (template.HasElements || template.Attributes().Any(a => a.Name != "id" && a.Name != "file")) Fail(template, input.Path, "file PawnTemplate accepts only id and file; use extends for overrides");
                    var path = Path.GetFullPath(A(template, "file"), Path.GetDirectoryName(input.Path));
                    var bytes = File.ReadAllBytes(path);
                    if (bytes.Length > 4 * 1024 * 1024) Fail(template, input.Path, "Pawn template input exceeds 4 MiB");
                    using var sourceReader = XmlReader.Create(new MemoryStream(bytes), ReaderSettings());
                    var xml = XDocument.Load(sourceReader).ToString(SaveOptions.DisableFormatting);
                    var captured = TemporaryPawnData.Parse(xml, path);
                    var replacement = XElement.Parse(captured.TemplateXml); replacement.SetAttributeValue("id", Required(template, "id", input.Path));
                    template.ReplaceWith(replacement);
                    using var sha = SHA256.Create();
                    input.Dependencies.Add(new ScenarioInput { Path = path, Sha256 = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(), Xml = xml });
                }
                input.Xml = root.ToString(SaveOptions.DisableFormatting);
                if (input.Dependencies.Count > 0)
                {
                    using var sha = SHA256.Create();
                    input.Sha256 = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(input.Sha256 + "\n" + string.Join("\n", input.Dependencies.Select(d => d.Sha256))))).Replace("-", "").ToLowerInvariant();
                }
            }
        }

        // Compose existing single-Pawn templates through the ordinary group compiler. The
        // resulting groups contain configuration only; the enclosing battle owns deployment.
        public static List<XElement> CompilePawnGroups(XElement templates, IEnumerable<XElement> sourceGroups, string path)
        {
            var groups = sourceGroups.Select(g => new XElement(g)).ToList();
            var usedIds = new HashSet<string>(groups.Select(g => A(g, "id")), StringComparer.Ordinal);
            string InternalId(string candidate)
            {
                while (!usedIds.Add(candidate)) candidate += "_";
                return candidate;
            }
            var actors = new XElement("Actors");
            foreach (var group in groups)
            {
                group.SetAttributeValue("team", "A"); group.SetAttributeValue("originX", 1); group.SetAttributeValue("originZ", 1);
                group.SetAttributeValue("formation", "grid"); group.SetAttributeValue("columns", 1); group.SetAttributeValue("spacing", 1);
                actors.Add(group);
            }
            actors.Add(new XElement("PawnGroup", new XAttribute("id", InternalId("__templateOpponent")), new XAttribute("team", "B"), new XAttribute("count", 1), new XElement("basePawnKind", "Colonist")));
            if (groups.Count == 0) actors.Add(new XElement("PawnGroup", new XAttribute("id", InternalId("__templateSubject")), new XAttribute("team", "A"), new XAttribute("count", 1), new XElement("basePawnKind", "Colonist")));
            var suite = new XElement("TestSuite", new XAttribute("schemaVersion", 1), new XAttribute("id", "pawnGroupTemplates"), templates == null ? null : new XElement(templates),
                new XElement("Case", new XAttribute("id", "groups"), new XAttribute("kind", "groupCombat"), new XElement("testDefName", "PawnGroupTemplates"),
                    new XElement("Scene", new XAttribute("sizeX", 250), new XAttribute("sizeZ", 250)),
                    new XElement("Teams", new XElement("Team", new XAttribute("id", "A")), new XElement("Team", new XAttribute("id", "B"))), actors,
                    new XElement("GroupCombat"), new XElement("Steps")));
            var compiled = Compile(new[] { new ScenarioInput { Path = path, Xml = suite.ToString(SaveOptions.DisableFormatting) } }, true);
            var resolved = XElement.Parse(compiled.Cases.Single().Xml).Element("Actors").Elements("PawnGroup").Take(groups.Count).ToList();
            foreach (var group in resolved)
                foreach (var attr in group.Attributes().Where(a => a.Name != "id" && a.Name != "count").ToList()) attr.Remove();
            return resolved;
        }

        private static void ValidateGroupCombat(XElement c, string path)
        {
            if (c.Element("Coverage") != null || c.Element("Success") != null || c.Element("Targets") != null) Fail(c, path, "GroupCombat has benchmark results, not regression Coverage/Success/Targets");
            var settings = c.Element("GroupCombat");
            if (settings == null) Fail(c, path, "GroupCombat configuration is required");
            if (settings.Attribute("aiPolicy") == null) settings.SetAttributeValue("aiPolicy", "assaultColony");
            void Fixed(XElement e, string field, string value) { if (e?.Attribute(field) != null && A(e, field) != value) Fail(e, path, field + " must be " + value); }
            Fixed(c, "kind", "groupCombat"); Fixed(settings, "start", "simultaneous");
            if (!new[] { "battle", "seekAndAttack", "assaultThings", "assaultColony", "defendPoint", "native" }.Contains(A(settings, "aiPolicy", "assaultColony"))) Fail(settings, path, "aiPolicy must be battle/seekAndAttack/assaultThings/assaultColony/defendPoint/native");
            if (!new[] { "normal", "fast", "superfast" }.Contains(A(settings, "timeSpeed", "normal"))) Fail(settings, path, "timeSpeed must be normal/fast/superfast");
            var duration = D(settings, "durationSeconds", 180);
            if (!double.IsFinite(duration) || duration <= 0 || duration > 86400) Fail(settings, path, "durationSeconds must be finite and in (0,86400]");
            if (D(c, "timeoutRealSeconds", 600) <= 0) Fail(c, path, "timeoutRealSeconds must be positive");
            var end = settings.Element("End"); Fixed(end, "condition", "oneSideAllDowned"); Fixed(end, "onTimeLimit", "fewestAliveLoses"); Fixed(end, "onTie", "draw");
            if (I(end, "confirmationTicks", 300) < 0 || I(end, "confirmationTicks", 300) > 36000) Fail(end, path, "confirmationTicks must be 0..36000");
            var metrics = settings.Element("Metrics");
            // Keep old inputs loadable while freezing an accurate dynamic population tag.
            if (A(metrics, "population") == "initialRoster") metrics.SetAttributeValue("population", "registeredRoster");
            Fixed(metrics, "damageBasis", "engineSettled"); Fixed(metrics, "dpsBasis", "unitLifetime"); Fixed(metrics, "teamDpsBasis", "battleWindow"); Fixed(metrics, "meanPopulation", "aliveOnMap"); Fixed(metrics, "population", "registeredRoster"); Fixed(metrics, "includeZeroValues", "true"); Fixed(metrics, "distributions", "min,max,mean,median");
            if (I(metrics, "detailLimit", 10000) < 0 || I(metrics, "detailLimit", 10000) > 1000000) Fail(metrics, path, "detailLimit must be 0..1000000");
            if (!new[] { "true", "false" }.Contains(A(metrics, "requireCompleteAttribution", "false"))) Fail(metrics, path, "requireCompleteAttribution must be true/false");
            var overlay = settings.Element("Overlay");
            if (!new[] { "true", "false" }.Contains(A(overlay, "enabled", "true"))) Fail(overlay, path, "enabled must be true/false");
            Fixed(overlay, "layout", "teamsLeftRight"); Fixed(overlay, "topUnitsPerMetric", "10"); Fixed(overlay, "topTypesPerTeam", "5"); Fixed(overlay, "humanPortrait", "renderedPawn"); Fixed(overlay, "refreshRealMilliseconds", "250");
            Fixed(overlay?.Element("Rankings"), "damageDealt", "true"); Fixed(overlay?.Element("Rankings"), "damageTaken", "true");
            Fixed(overlay?.Element("Summary"), "dps", "livingUnitLifetimeMean"); Fixed(overlay?.Element("Summary"), "damageTaken", "totalAndMeanPerLivingUnit");
            Fixed(c.Element("Output"), "purpose", "benchmark"); Fixed(c.Element("Output"), "showAllParticipants", "true");
            var output = c.Element("Output");
            if ((output?.Attribute("width") != null) != (output?.Attribute("height") != null)) Fail(output, path, "Output width and height must be specified together");
            if (output?.Attribute("width") != null && (I(output, "width") < 800 || I(output, "width") > 3840 || I(output, "height") < 600 || I(output, "height") > 2160)) Fail(output, path, "Output resolution must be 800..3840 by 600..2160");
            var teams = c.Element("Teams")?.Elements("Team").ToList() ?? new List<XElement>();
            if (teams.Count != 2) Fail(c, path, "GroupCombat requires exactly two teams");
            foreach (var team in teams)
            {
                Fixed(team, "factionSource", "temporary"); if (team.Attribute("factionDef") != null) Fail(team, path, "GroupCombat creates isolated temporary factions, factionDef is not accepted");
                var ai = A(team, "ai", A(settings, "aiPolicy", "assaultColony"));
                if (!new[] { "battle", "seekAndAttack", "assaultThings", "assaultColony", "defendPoint", "native" }.Contains(ai)) Fail(team, path, "team ai must be battle/seekAndAttack/assaultThings/assaultColony/defendPoint/native");
                if (ai != "defendPoint" && team.Attributes().Any(a => a.Name.LocalName.StartsWith("defend", StringComparison.Ordinal))) Fail(team, path, "defend coordinates/radius require ai=defendPoint");
                if (ai == "defendPoint" && (I(team, "defendX", 0) < 0 || I(team, "defendZ", 0) < 0 || I(team, "defendX", 0) >= I(c.Element("Scene"), "sizeX", 50) || I(team, "defendZ", 0) >= I(c.Element("Scene"), "sizeZ", 50) || D(team, "defendRadius", 28) <= 0 || !double.IsFinite(D(team, "defendRadius", 28)))) Fail(team, path, "invalid defend point/radius");
            }
            foreach (var relation in c.Element("Teams").Elements("Relation"))
            {
                if (!teams.Any(t => A(t, "id") == A(relation, "a")) || !teams.Any(t => A(t, "id") == A(relation, "b")) || A(relation, "a") == A(relation, "b")) Fail(relation, path, "relation needs two distinct configured teams");
                Fixed(relation, "value", "Hostile");
            }
            var pawns = c.Element("Actors").Elements().Where(a => a.Name == "Pawn" || a.Name == "PawnGroup").ToList();
            if (pawns.Count == 0 || pawns.Sum(a => a.Name == "PawnGroup" ? I(a, "count", 1) : 1) > CombatRoster.MaxBattleMembers) Fail(c, path, "GroupCombat requires 1.." + CombatRoster.MaxBattleMembers + " initial participants");
            foreach (var team in pawns.GroupBy(a => A(a, "team")))
                if (team.Sum(a => a.Name == "PawnGroup" ? I(a, "count", 1) : 1) > CombatRoster.MaxTeamMembers)
                    Fail(c, path, "team " + team.Key + " exceeds " + CombatRoster.MaxTeamMembers + " initial participants");
            foreach (var pawn in pawns) { if (A(pawn, "team") == null) Fail(pawn, path, "combat Pawn requires a team"); Fixed(pawn, "spawn", "true"); }
            foreach (var team in teams) if (!pawns.Any(p => A(p, "team") == A(team, "id"))) Fail(team, path, "team has no initial participants");
            foreach (var step in c.Element("Steps").Elements())
                if (!new[] { "DebugAction", "AddHediff", "RemoveHediff", "Equip", "DropEquipment", "ApplyDamage", "Kill", "Destroy", "SetRot", "BindMech", "Require" }.Contains(step.Name.LocalName)) Fail(step, path, "GroupCombat preparation does not support this step; battle timing starts after preparation");
            var experiments = settings.Element("Experiments");
            if (!new[] { "true", "false" }.Contains(A(experiments, "swapSides", "false"))) Fail(experiments, path, "swapSides must be true/false");
            var seeds = experiments?.Elements("Seed").ToList() ?? new List<XElement>();
            if (seeds.Count > 128 || seeds.Any(s => !int.TryParse(A(s, "value"), NumberStyles.Integer, CultureInfo.InvariantCulture, out _))) Fail(experiments, path, "Experiments accepts at most 128 explicit integer Seed values");
        }
    }

    public static class GroupCombatPlan
    {
        public static string DisplayGroupKey(XElement actor)
        {
            // Formation IDs, positions and random appearance are not new unit types. Explicit
            // Pawn configuration does distinguish two loadouts of the same PawnKind.
            var template = new XElement("PawnTemplate", actor.Elements().OrderBy(e => e.Name.LocalName, StringComparer.Ordinal).Select(e => new XElement(e)));
            foreach (var whitespace in template.DescendantNodes().OfType<XText>().Where(t => t.Parent.HasElements && string.IsNullOrWhiteSpace(t.Value)).ToList()) whitespace.Remove();
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(template.ToString(SaveOptions.DisableFormatting)))).Replace("-", "").ToLowerInvariant();
        }

        public static List<GroupCombatRoundInput> Expand(ScenarioPlan plan)
        {
            if (plan.Purpose != "groupcombat" || plan.SelectedTestDefNames.Count == 0) throw new ScenarioConfigurationException("GroupCombat requires explicit named selection");
            var result = new List<GroupCombatRoundInput>();
            foreach (var definition in plan.Cases)
            {
                var c = XElement.Parse(definition.Xml); var experiments = c.Element("GroupCombat").Element("Experiments");
                var seeds = experiments?.Elements("Seed").Select(s => ScenarioLoader.I(s, "value")).ToList() ?? new List<int>();
                if (seeds.Count == 0) seeds.Add(ScenarioLoader.I(c, "seed", 12345));
                for (int i = 0; i < seeds.Count; i++)
                    foreach (var swapped in ScenarioLoader.A(experiments, "swapSides", "false") == "true" ? new[] { false, true } : new[] { false })
                    {
                        var copy = new XElement(c); copy.SetAttributeValue("seed", seeds[i]);
                        if (swapped)
                        {
                            // Exchange the spawn blocks across the map's midline. Terrain/layout
                            // stay fixed, and actual unit positions are recorded for each round.
                            var width = ScenarioLoader.I(copy.Element("Scene"), "sizeX", 50);
                            foreach (var actor in copy.Element("Actors").Elements().Where(a => a.Name == "Pawn" || a.Name == "PawnGroup"))
                            {
                                var attr = actor.Name == "PawnGroup" ? "originX" : "x";
                                var extent = actor.Name == "PawnGroup" ? PawnGroupFormation.Extents(actor, verticalFirst: true).x : 0;
                                actor.SetAttributeValue(attr, width - 1 - ScenarioLoader.I(actor, attr, 20) - extent);
                            }
                            foreach (var team in copy.Element("Teams").Elements("Team").Where(t => t.Attribute("defendX") != null)) team.SetAttributeValue("defendX", width - 1 - ScenarioLoader.I(team, "defendX"));
                        }
                        result.Add(new GroupCombatRoundInput { CaseId = definition.CaseId + "/round-" + (i + 1) + (swapped ? "-swapped" : ""), TestDefName = definition.TestDefName, Name = definition.Name, Seed = seeds[i], Swapped = swapped, Xml = copy.ToString(SaveOptions.DisableFormatting), Source = definition.Source, InputHash = definition.InputHash });
                    }
            }
            if (result.Count > 1024) throw new ScenarioConfigurationException("GroupCombat expanded plan exceeds 1024 rounds");
            return result;
        }
    }
}
