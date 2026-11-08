#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Xml;
using System.Xml.Linq;

namespace LogSyncMod.AutoTest.Scenarios
{
    public static class ArenaLoader
    {
        public static ArenaPlan Load(string path, IEnumerable<string> names)
        {
            path = Path.GetFullPath(path);
            var directory = Directory.Exists(path);
            var files = directory ? Directory.GetFiles(path, "*.autotest.xml").OrderBy(p => p, StringComparer.Ordinal).ToArray() : new[] { path };
            var inputs = new List<ScenarioInput>(); var skipped = 0;
            foreach (var file in files)
            {
                var bytes = File.ReadAllBytes(file);
                using var stream = new MemoryStream(bytes);
                using var reader = XmlReader.Create(stream, ScenarioLoader.ReaderSettings());
                var doc = XDocument.Load(reader, LoadOptions.SetLineInfo);
                if (directory && doc.Root?.Name != "ArenaSuite") { skipped++; continue; }
                using var sha = SHA256.Create();
                inputs.Add(new ScenarioInput { Path = file, Sha256 = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(), Xml = doc.ToString(SaveOptions.DisableFormatting) });
            }
            var plan = Compile(inputs, names); plan.SkippedFiles = skipped; return plan;
        }
        public static ArenaPlan Compile(IEnumerable<ScenarioInput> inputs, IEnumerable<string> names)
        {
            var plan = new ArenaPlan { Inputs = inputs.ToList(), SelectedTestDefNames = names.Distinct(StringComparer.OrdinalIgnoreCase).ToList() };
            if (plan.SelectedTestDefNames.Count == 0) throw new ScenarioConfigurationException("arena requires explicit --test-name");
            var all = new List<ArenaTournamentInput>();
            foreach (var input in plan.Inputs)
            {
                using var reader = XmlReader.Create(new StringReader(input.Xml), ScenarioLoader.ReaderSettings());
                var root = XElement.Load(reader, LoadOptions.SetLineInfo);
                Check(root, "schemaVersion id gameVersion", "Tournament SquadTemplates", input.Path);
                if (root.Name != "ArenaSuite" || ScenarioLoader.A(root, "schemaVersion") != "1" || ScenarioLoader.A(root, "gameVersion", "1.7") != "1.7") ScenarioLoader.Fail(root, input.Path, "expected ArenaSuite schemaVersion=1 gameVersion=1.7; use the independent arena entry");
                Required(root, "id", input.Path);
                var squadTemplates = SquadTemplates.ParseAll(root, input.Path);
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (var t in root.Elements("Tournament"))
                {
                    Check(t, "id name seed", "testDefName Scene Roster Entrants Battle Scoring TieBreak Presentation Output", input.Path);
                    var id = Required(t, "id", input.Path);
                    if (!ids.Add(id)) ScenarioLoader.Fail(t, input.Path, "duplicate Tournament id");
                    var testName = t.Element("testDefName")?.Value.Trim();
                    if (string.IsNullOrEmpty(testName) || !System.Text.RegularExpressions.Regex.IsMatch(testName, "^[A-Za-z_][A-Za-z0-9_.-]*$")) ScenarioLoader.Fail(t, input.Path, "testDefName must be a stable identifier");
                    Check(t.Element("testDefName"), "", "", input.Path);
                    var roster = t.Element("Roster"); Check(roster, "mode budget count costMode", "", input.Path, true);
                    var mode = ScenarioLoader.A(roster, "mode");
                    var costMode = ScenarioLoader.A(roster, "costMode", "pawnKind"); SquadTemplates.ValidateCostMode(costMode);
                    if (mode != "count" && mode != "combatPower") ScenarioLoader.Fail(roster, input.Path, "Roster mode must be count/combatPower");
                    if (mode == "count" && roster.Attribute("budget") != null || mode == "combatPower" && roster.Attribute("count") != null) ScenarioLoader.Fail(roster, input.Path, "count and budget modes are mutually exclusive");
                    var entrants = t.Element("Entrants"); Check(entrants, "", "Entrant", input.Path, true);
                    var result = new ArenaTournamentInput { Id = id, Name = ScenarioLoader.A(t, "name", testName), TestDefName = testName, Seed = ScenarioLoader.I(t, "seed", 12345), Xml = t.ToString(SaveOptions.DisableFormatting), Source = input.Path, InputHash = input.Sha256, RosterMode = mode, CostMode = costMode, SquadTemplates = squadTemplates, Budget = mode == "combatPower" ? CombatRoster.Positive(Required(roster, "budget", input.Path)) : null, Count = Integer(roster, "count", 20, 1, CombatRoster.MaxTeamMembers, input.Path) };
                    foreach (var e in entrants.Elements("Entrant"))
                    {
                        Check(e, "id pawnKind squad name count budget xenotype", "", input.Path);
                        if (mode == "combatPower" && e.Attribute("count") != null) ScenarioLoader.Fail(e, input.Path, "budget mode forbids entrant count overrides");
                        if (mode == "count" && e.Attribute("budget") != null) ScenarioLoader.Fail(e, input.Path, "count mode forbids entrant budget overrides");
                        if ((e.Attribute("pawnKind") == null) == (e.Attribute("squad") == null)) ScenarioLoader.Fail(e, input.Path, "Entrant requires exactly one of pawnKind/squad");
                        var squadId = ScenarioLoader.A(e, "squad");
                        if (squadId != null && (!squadTemplates.ContainsKey(squadId) || e.Attribute("xenotype") != null)) ScenarioLoader.Fail(e, input.Path, "unknown squad or conflicting entrant xenotype");
                        result.Entrants.Add(new ArenaEntrant { Id = Required(e, "id", input.Path), PawnKind = e.Attribute("pawnKind") == null ? null : Required(e, "pawnKind", input.Path), Squad = squadId, Xenotype = e.Attribute("xenotype") == null ? null : Required(e, "xenotype", input.Path), Name = ScenarioLoader.A(e, "name"), Count = e.Attribute("count") == null ? null : Integer(e, "count", 20, 1, CombatRoster.MaxTeamMembers, input.Path), Budget = e.Attribute("budget") == null ? null : CombatRoster.Positive(Required(e, "budget", input.Path)) });
                    }
                    if (result.Entrants.Count < 2 || result.Entrants.Count > 64 || result.Entrants.Select(e => e.Id).Distinct(StringComparer.Ordinal).Count() != result.Entrants.Count || result.Entrants.Where(e => e.PawnKind != null).Select(e => e.PawnKind).Distinct(StringComparer.Ordinal).Count() != result.Entrants.Count(e => e.PawnKind != null)) ScenarioLoader.Fail(entrants, input.Path, "requires 2..64 distinct entrant IDs and PawnKinds");
                    var battle = t.Element("Battle"); Check(battle, "durationSeconds timeoutRealSeconds timeSpeed confirmationTicks", "", input.Path);
                    result.DurationSeconds = Number(battle, "durationSeconds", 180, 0, 86400, input.Path, true);
                    result.TimeoutRealSeconds = Integer(battle, "timeoutRealSeconds", 300, 1, 86400, input.Path);
                    result.ConfirmationTicks = Integer(battle, "confirmationTicks", 300, 0, 36000, input.Path);
                    if (!new[] { "normal", "fast", "superfast" }.Contains(ScenarioLoader.A(battle, "timeSpeed", "fast"))) ScenarioLoader.Fail(battle, input.Path, "invalid timeSpeed");
                    var scoring = t.Element("Scoring"); Check(scoring, "survivalWeight suppressionWeight exchangeWeight speedWeight timeReferenceSeconds", "", input.Path);
                    result.SurvivalWeight = (decimal)Number(scoring, "survivalWeight", 45, 0, 100, input.Path);
                    result.SuppressionWeight = (decimal)Number(scoring, "suppressionWeight", 25, 0, 100, input.Path);
                    result.ExchangeWeight = (decimal)Number(scoring, "exchangeWeight", 20, 0, 100, input.Path);
                    result.SpeedWeight = (decimal)Number(scoring, "speedWeight", 10, 0, 100, input.Path);
                    result.TimeReferenceSeconds = (decimal)Number(scoring, "timeReferenceSeconds", 180, 0, 86400, input.Path, true);
                    if (result.TimeReferenceSeconds <= 0 || result.SurvivalWeight + result.SuppressionWeight + result.ExchangeWeight + result.SpeedWeight != 100)
                        ScenarioLoader.Fail(scoring, input.Path, "scoring weights must total 100 (defaults: survivalWeight=45, suppressionWeight=25, exchangeWeight=20, speedWeight=10); time reference must be positive");
                    var tie = t.Element("TieBreak"); Check(tie, "onDraw maxReplays", "", input.Path);
                    if (ScenarioLoader.A(tie, "onDraw", "replay") != "replay") ScenarioLoader.Fail(tie, input.Path, "onDraw must be replay");
                    result.MaxReplays = Integer(tie, "maxReplays", 2, 0, 10, input.Path);
                    var presentation = t.Element("Presentation"); Check(presentation, "bracketSeconds settlementSeconds advanceStartSeconds", "Showcase", input.Path);
                    result.BracketSeconds = Number(presentation, "bracketSeconds", 3, 1, 60, input.Path);
                    result.AdvanceStartSeconds = Number(presentation, "advanceStartSeconds", 3, 0, 60, input.Path);
                    result.SettlementSeconds = Number(presentation, "settlementSeconds", 3, 1, 60, input.Path);
                    LoadShowcase(presentation?.Element("Showcase"), result, input.Path);
                    var output = t.Element("Output"); Check(output, "width height", "", input.Path);
                    if ((output?.Attribute("width") != null) != (output?.Attribute("height") != null)) ScenarioLoader.Fail(output, input.Path, "Output width/height must be supplied together");
                    if (output?.Attribute("width") != null) { Integer(output, "width", 1920, 800, 3840, input.Path); Integer(output, "height", 1080, 600, 2160, input.Path); }
                    // Compile a synthetic count=1 battle through the existing strict scene parser.
                    // This validates Scene/Environment/Layout without maintaining a second whitelist.
                    var scene = t.Element("Scene"); if (scene == null) ScenarioLoader.Fail(t, input.Path, "Scene is required");
                    CompileBattle(result, result.Entrants.Take(2).Select(e => new ResolvedCombatRoster { Id = e.Id, PawnKind = e.PawnKind ?? "Colonist", ResolvedCount = 1 }).ToList(), null, 0);
                    all.Add(result);
                }
            }
            if (all.Select(t => t.TestDefName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != all.Count) throw new ScenarioConfigurationException("duplicate arena testDefName");
            foreach (var name in plan.SelectedTestDefNames) if (!all.Any(t => t.TestDefName.Equals(name, StringComparison.OrdinalIgnoreCase))) throw new ScenarioConfigurationException("Unknown arena test name: " + name);
            plan.Tournaments = all.Where(t => plan.SelectedTestDefNames.Contains(t.TestDefName, StringComparer.OrdinalIgnoreCase)).ToList();
            return plan;
        }
        public static GroupCombatRoundInput CompileBattle(ArenaTournamentInput input, List<ResolvedCombatRoster> rosters, ArenaMatch match, int attempt)
        {
            var t = XElement.Parse(input.Xml); var scene = new XElement(t.Element("Scene"));
            var width = ScenarioLoader.I(scene, "sizeX", 100); var height = ScenarioLoader.I(scene, "sizeZ", 100);
            // Explicit defaults are frozen; the group parser otherwise defaults to 50.
            scene.SetAttributeValue("sizeX", width); scene.SetAttributeValue("sizeZ", height);
            var ids = match == null ? rosters.Take(2).Select(r => r.Id).ToArray() : new[] { match.Left, match.Right };
            var actors = new XElement("Actors");
            for (int i = 0; i < 2; i++)
            {
                var r = rosters.Single(r => r.Id == ids[i]); var columns = PawnGroupFormation.ArenaRows(r.ResolvedCount, width);
                var actor = new XElement("PawnGroup", new XAttribute("id", i == 0 ? "left" : "right"), new XAttribute("team", i == 0 ? "A" : "B"), new XAttribute("count", r.ResolvedCount), new XAttribute("formation", "grid"), new XAttribute("columns", columns), new XAttribute("spacing", 2), new XElement("basePawnKind", r.PawnKind ?? "Colonist"));
                var xenotype = input.Entrants.Single(e => e.Id == r.Id).Xenotype;
                if (xenotype != null) actor.Add(new XElement("Xenotype", new XAttribute("def", xenotype)));
                var extent = PawnGroupFormation.Extents(actor, verticalFirst: true);
                var x = i == 0 ? Math.Max(1, width / 5 - extent.x / 2) : Math.Min(width - extent.x - 2, width * 4 / 5 - extent.x / 2);
                var z = Math.Max(1, height / 2 - extent.z / 2);
                actor.SetAttributeValue("originX", x); actor.SetAttributeValue("originZ", z);
                actors.Add(actor);
            }
            var seed = match == null ? input.Seed : ArenaScheduler.AttemptSeed(input, match, attempt);
            var battle = new XElement("Case", new XAttribute("id", "arena"), new XAttribute("kind", "groupCombat"), new XAttribute("seed", seed), new XAttribute("name", input.Name), new XAttribute("timeoutRealSeconds", input.TimeoutRealSeconds), new XElement("testDefName", input.TestDefName), scene,
                new XElement("Teams", ids.Select((id, i) => new XElement("Team", new XAttribute("id", i == 0 ? "A" : "B"), new XAttribute("name", rosters.Single(r => r.Id == id).DisplayName ?? id)))), actors,
                new XElement("GroupCombat", new XAttribute("aiPolicy", "seekAndAttack"), new XAttribute("durationSeconds", input.DurationSeconds.ToString("R", CultureInfo.InvariantCulture)), new XAttribute("timeSpeed", ScenarioLoader.A(t.Element("Battle"), "timeSpeed", "fast")), new XElement("End", new XAttribute("confirmationTicks", input.ConfirmationTicks))), new XElement("Steps"), t.Element("Output") == null ? null : new XElement(t.Element("Output")));
            var suite = new XElement("TestSuite", new XAttribute("schemaVersion", 1), new XAttribute("id", "arena.battle"), battle);
            var compiled = ScenarioLoader.Compile(new[] { new ScenarioInput { Path = input.Source, Sha256 = input.InputHash, Xml = suite.ToString(SaveOptions.DisableFormatting) } }, true);
            var c = compiled.Cases.Single();
            return new GroupCombatRoundInput { CaseId = (match?.Id ?? input.TestDefName) + "/attempt-" + (attempt + 1), TestDefName = input.TestDefName, Name = input.Name + " · " + (match?.Kind == "qualification" ? "资格赛" : match?.Kind == "extra" ? "低分加赛" : "第 " + (match?.Round ?? 1) + " 轮") + " · " + (attempt == 0 ? "首战" : "重赛 " + attempt), Seed = seed, Source = input.Source, InputHash = input.InputHash, Xml = c.Xml };
        }
        private static void LoadShowcase(XElement showcase, ArenaTournamentInput input, string path)
        {
            Check(showcase, "pageSeconds", "Page", path);
            input.ShowcasePageSeconds = Number(showcase, "pageSeconds", ArenaShowcase.PageSeconds, 1, 60, path);
            var used = new HashSet<string>(StringComparer.Ordinal);
            var ids = new HashSet<string>(input.Entrants.Select(e => e.Id), StringComparer.Ordinal);
            foreach (var page in showcase?.Elements("Page") ?? Enumerable.Empty<XElement>())
            {
                Check(page, "seconds caption", "Entrant", path);
                var caption = ScenarioLoader.A(page, "caption", "").Trim();
                if (caption.Length > 80 || caption.Any(char.IsControl)) ScenarioLoader.Fail(page, path, "showcase caption must be a single line of at most 80 characters");
                var compiled = new ArenaShowcasePageInput { Seconds = Number(page, "seconds", input.ShowcasePageSeconds, 1, 60, path), Caption = caption };
                foreach (var entrant in page.Elements("Entrant"))
                {
                    Check(entrant, "ref", "", path);
                    var id = Required(entrant, "ref", path);
                    if (!ids.Contains(id)) ScenarioLoader.Fail(entrant, path, "unknown showcase entrant ref: " + id);
                    if (!used.Add(id)) ScenarioLoader.Fail(entrant, path, "duplicate showcase entrant ref: " + id);
                    compiled.EntrantIds.Add(id);
                }
                if (compiled.EntrantIds.Count < 1 || compiled.EntrantIds.Count > ArenaShowcase.PageSize) ScenarioLoader.Fail(page, path, "showcase Page requires 1..5 entrant references");
                input.ShowcasePages.Add(compiled);
            }
            var remaining = input.Entrants.Where(e => !used.Contains(e.Id)).Select(e => e.Id).ToList();
            for (var offset = 0; offset < remaining.Count; offset += ArenaShowcase.PageSize)
                input.ShowcasePages.Add(new ArenaShowcasePageInput { Seconds = input.ShowcasePageSeconds, EntrantIds = remaining.Skip(offset).Take(ArenaShowcase.PageSize).ToList() });
        }
        private static string Required(XElement e, string a, string path)
        { var s = ScenarioLoader.A(e, a); if (string.IsNullOrWhiteSpace(s)) ScenarioLoader.Fail(e, path, a + " is required"); return s; }
        private static void Check(XElement e, string attrs, string children, string path, bool required = false)
        {
            if (e == null) { if (required) throw new ScenarioConfigurationException(path + ": missing required arena element"); return; }
            if (e.Name.Namespace != XNamespace.None || e.Attributes().Any(a => !attrs.Split(' ').Contains(a.Name.ToString())) || e.Elements().Any(c => !children.Split(' ').Contains(c.Name.ToString())) || e.Nodes().OfType<XText>().Any(n => !string.IsNullOrWhiteSpace(n.Value) && e.Name != "testDefName")) ScenarioLoader.Fail(e, path, "unknown arena attribute, child or text");
            if (e.Name != "ArenaSuite" && e.Name != "Entrants" && e.Name != "Showcase" && e.Name != "Page" && e.Elements().GroupBy(c => c.Name).Any(g => g.Count() > 1)) ScenarioLoader.Fail(e, path, "duplicate arena element");
        }
        private static int Integer(XElement e, string attr, int fallback, int min, int max, string path)
        { var value = ScenarioLoader.I(e, attr, fallback); if (value < min || value > max) ScenarioLoader.Fail(e, path, attr + " outside " + min + ".." + max); return value; }
        private static double Number(XElement e, string attr, double fallback, double min, double max, string path, bool exclusiveMin = false)
        { var value = ScenarioLoader.D(e, attr, fallback); if (!double.IsFinite(value) || value < min || exclusiveMin && value == min || value > max) ScenarioLoader.Fail(e, path, attr + " outside supported finite range"); return value; }
    }
}
