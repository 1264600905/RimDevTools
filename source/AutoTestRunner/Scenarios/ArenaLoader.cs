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
    public static class ArenaPreparationContext
    {
        [ThreadStatic] private static string kind;
        [ThreadStatic] private static HashSet<string> actions;
        public static bool Matches(string pawnKind, string actionId) => kind != null && kind == pawnKind && actions?.Contains(actionId) == true;
        public static IDisposable Enter(string pawnKind, XElement steps)
        {
            var scope = new Scope { PreviousKind = kind, PreviousActions = actions };
            kind = pawnKind;
            actions = new HashSet<string>(steps?.Elements("DebugAction").Select(e => (string)e.Attribute("actionId")) ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            return scope;
        }
        private sealed class Scope : IDisposable
        {
            internal string PreviousKind;
            internal HashSet<string> PreviousActions;
            public void Dispose() { kind = PreviousKind; actions = PreviousActions; }
        }
    }

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
                XDocument doc;
                try { doc = XDocument.Load(reader, LoadOptions.SetLineInfo | LoadOptions.PreserveWhitespace); }
                catch (XmlException ex) { throw ScenarioLoader.XmlFailure(file, ex); }
                if (directory && doc.Root?.Name != "ArenaSuite") { skipped++; continue; }
                using var sha = SHA256.Create();
                inputs.Add(new ScenarioInput { Path = file, Sha256 = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(), Xml = doc.ToString(SaveOptions.DisableFormatting) });
            }
            ScenarioLoader.ResolveGroupPawnFiles(inputs);
            var plan = FreezeSeeds(Compile(inputs, names)); plan.SkippedFiles = skipped; return plan;
        }
        public static ArenaPlan Compile(IEnumerable<ScenarioInput> inputs, IEnumerable<string> names, IReadOnlyDictionary<string, int> resolvedSeeds = null)
        {
            var plan = new ArenaPlan { Inputs = inputs.ToList(), SelectedTestDefNames = names.Distinct(StringComparer.OrdinalIgnoreCase).ToList() };
            if (plan.SelectedTestDefNames.Count == 0) throw new ScenarioConfigurationException("arena requires explicit --test-name");
            var all = new List<ArenaTournamentInput>();
            foreach (var input in plan.Inputs)
            {
                using var reader = XmlReader.Create(new StringReader(input.Xml), ScenarioLoader.ReaderSettings());
                var root = XElement.Load(reader, LoadOptions.SetLineInfo);
                Check(root, "schemaVersion id gameVersion", "Tournament SquadTemplates Templates Requirements ScenePresets ActionBindings", input.Path);
                if (root.Name != "ArenaSuite" || ScenarioLoader.A(root, "schemaVersion") != "1" || ScenarioLoader.A(root, "gameVersion", "1.7") != "1.7") ScenarioLoader.Fail(root, input.Path, "expected ArenaSuite schemaVersion=1 gameVersion=1.7; use the independent arena entry");
                Required(root, "id", input.Path);
                foreach (var section in new[] { "Templates", "Requirements", "ScenePresets", "ActionBindings" })
                    if (root.Elements(section).Count() > 1) ScenarioLoader.Fail(root, input.Path, "duplicate " + section);
                if (root.Element("Templates") != null) ScenarioLoader.CompilePawnGroups(root.Element("Templates"), Enumerable.Empty<XElement>(), input.Path);
                var squadTemplates = SquadTemplates.ParseAll(root, input.Path);
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (var t in root.Elements("Tournament"))
                {
                    Check(t, "id name seed format", "testDefName Scene Roster Entrants Battle Scoring TieBreak Presentation Output Cleanup Matchmaking Growth ActionBindings Preparation Requirements Metrics Overlay", input.Path);
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
                    if (resolvedSeeds != null && resolvedSeeds.TryGetValue(testName, out var seed))
                    {
                        if (result.Seed != -1 || seed < 0) throw new ScenarioConfigurationException("Frozen random seed does not match a seed=-1 tournament: " + testName);
                        result.Seed = seed; plan.ResolvedSeeds.Add(testName, seed);
                    }
                    result.Format = ScenarioLoader.A(t, "format", "knockout");
                    result.SharedXml = new XElement("Shared", root.Elements().Where(e => new[] { "Requirements", "ScenePresets", "ActionBindings" }.Contains(e.Name.LocalName)).Select(e => new XElement(e))).ToString(SaveOptions.DisableFormatting);
                    var growth = t.Element("Growth"); Check(growth, "target mode multiplier increment maxValue", "", input.Path);
                    if (growth != null) result.Growth = new ArenaGrowth { Target = ScenarioLoader.A(growth, "target", "count"),
                        Mode = ScenarioLoader.A(growth, "mode", "multiply"),
                        Multiplier = growth.Attribute("multiplier") == null ? 0 : CombatRoster.Positive(Required(growth, "multiplier", input.Path)),
                        Increment = growth.Attribute("increment") == null ? 0 : CombatRoster.Positive(Required(growth, "increment", input.Path)),
                        MaxValue = CombatRoster.Positive(Required(growth, "maxValue", input.Path)) };
                    foreach (var e in entrants.Elements("Entrant"))
                    {
                        Check(e, "id pawnKind squad name count budget xenotype role mode costMode ai", "", input.Path);
                        var entrantMode = ScenarioLoader.A(e, "mode", mode);
                        if (entrantMode != "count" && entrantMode != "combatPower") ScenarioLoader.Fail(e, input.Path, "Entrant mode must be count/combatPower");
                        if (entrantMode == "combatPower" && e.Attribute("count") != null) ScenarioLoader.Fail(e, input.Path, "budget mode forbids entrant count overrides");
                        if (entrantMode == "count" && e.Attribute("budget") != null) ScenarioLoader.Fail(e, input.Path, "count mode forbids entrant budget overrides");
                        if (entrantMode == "combatPower" && e.Attribute("budget") == null && result.Budget == null) ScenarioLoader.Fail(e, input.Path, "combatPower entrant requires a budget");
                        if (e.Attribute("costMode") != null) SquadTemplates.ValidateCostMode(ScenarioLoader.A(e, "costMode"));
                        if ((e.Attribute("pawnKind") == null) == (e.Attribute("squad") == null)) ScenarioLoader.Fail(e, input.Path, "Entrant requires exactly one of pawnKind/squad");
                        var squadId = ScenarioLoader.A(e, "squad");
                        if (squadId != null && (!squadTemplates.ContainsKey(squadId) || e.Attribute("xenotype") != null)) ScenarioLoader.Fail(e, input.Path, "unknown squad or conflicting entrant xenotype");
                        var ai = e.Attribute("ai") == null ? null : Required(e, "ai", input.Path);
                        if (ai != null && !new[] { "battle", "seekAndAttack", "assaultThings", "assaultColony", "defendPoint", "lure", "native" }.Contains(ai)) ScenarioLoader.Fail(e, input.Path, "entrant ai must be battle/seekAndAttack/assaultThings/assaultColony/defendPoint/lure/native");
                        var entrant = new ArenaEntrant { Id = Required(e, "id", input.Path), Role = ScenarioLoader.A(e, "role"), Ai = ai, RosterMode = ScenarioLoader.A(e, "mode"), CostMode = ScenarioLoader.A(e, "costMode"), PawnKind = e.Attribute("pawnKind") == null ? null : Required(e, "pawnKind", input.Path), Squad = squadId, Xenotype = e.Attribute("xenotype") == null ? null : Required(e, "xenotype", input.Path), Name = ScenarioLoader.A(e, "name"), Count = e.Attribute("count") == null ? null : Integer(e, "count", 20, 1, CombatRoster.MaxTeamMembers, input.Path), Budget = e.Attribute("budget") == null ? null : CombatRoster.Positive(Required(e, "budget", input.Path)) };
                        if (squadId != null && squadTemplates[squadId].Source == "groups" && entrantMode == "count")
                        {
                            var total = SquadTemplates.GroupCount(squadTemplates[squadId]);
                            if (entrant.Count.HasValue && entrant.Count != total) ScenarioLoader.Fail(e, input.Path, "entrant count does not match squad PawnGroups");
                            entrant.Count = total;
                        }
                        result.Entrants.Add(entrant);
                        if (squadId != null && squadTemplates[squadId].Source == "custom" && squadTemplates[squadId].Composition == "fixed" && entrantMode == "count"
                            && (entrant.Count ?? result.Count) != squadTemplates[squadId].Options.Sum(o => o.Count)) ScenarioLoader.Fail(e, input.Path, "entrant count does not match fixed Composition");
                    }
                    if (result.Entrants.Count < 2 || result.Entrants.Count > 64 || result.Entrants.Select(e => e.Id).Distinct(StringComparer.Ordinal).Count() != result.Entrants.Count) ScenarioLoader.Fail(entrants, input.Path, "requires 2..64 distinct entrant IDs");
                    ArenaChallengeRules.Validate(result);
                    var battle = t.Element("Battle"); Check(battle, "durationSeconds timeoutRealSeconds timeSpeed confirmationTicks aiPolicy", "", input.Path);
                    var matchmaking = t.Element("Matchmaking"); Check(matchmaking, "mode", "", input.Path);
                    if (result.Format != "knockout" && matchmaking != null) ScenarioLoader.Fail(matchmaking, input.Path, "Matchmaking only applies to knockout tournaments");
                    result.MatchmakingMode = ScenarioLoader.A(matchmaking, "mode", "closestCount");
                    if (!new[] { "closestCount", "random" }.Contains(result.MatchmakingMode)) ScenarioLoader.Fail(matchmaking, input.Path, "Matchmaking mode must be closestCount/random");
                    var cleanup = t.Element("Cleanup"); Check(cleanup, "corpses droppedItems", "", input.Path);
                    foreach (var attr in new[] { "corpses", "droppedItems" })
                        if (!new[] { "true", "false" }.Contains(ScenarioLoader.A(cleanup, attr, "false"))) ScenarioLoader.Fail(cleanup, input.Path, attr + " must be true/false");
                    result.ClearCorpses = ScenarioLoader.A(cleanup, "corpses", "false") == "true";
                    result.ClearDroppedItems = ScenarioLoader.A(cleanup, "droppedItems", "false") == "true";
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
                    _ = ArenaChallengeRules.DeadlineSeconds(result);
                    all.Add(result);
                }
            }
            if (all.Select(t => t.TestDefName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != all.Count) throw new ScenarioConfigurationException("duplicate arena testDefName");
            foreach (var name in plan.SelectedTestDefNames) if (!all.Any(t => t.TestDefName.Equals(name, StringComparison.OrdinalIgnoreCase))) throw new ScenarioConfigurationException("Unknown arena test name: " + name);
            plan.Tournaments = all.Where(t => plan.SelectedTestDefNames.Contains(t.TestDefName, StringComparer.OrdinalIgnoreCase)).ToList();
            if (resolvedSeeds != null && resolvedSeeds.Keys.Any(k => !plan.ResolvedSeeds.ContainsKey(k))) throw new ScenarioConfigurationException("Frozen seed references an unknown tournament");
            return plan;
        }
        public static ArenaPlan FreezeSeeds(ArenaPlan source, Func<int> random = null)
        {
            var seeds = new Dictionary<string, int>(source.ResolvedSeeds ?? new Dictionary<string, int>());
            foreach (var t in source.Tournaments.Where(t => t.Seed == -1))
            {
                var seed = random == null ? RandomNumberGenerator.GetInt32(int.MaxValue) : random();
                if (seed < 0) throw new ScenarioConfigurationException("Resolved random seed must be nonnegative");
                seeds[t.TestDefName] = seed;
            }
            var result = Compile(source.Inputs, source.SelectedTestDefNames, seeds);
            result.SkippedFiles = source.SkippedFiles; return result;
        }
        public static GroupCombatRoundInput CompileBattle(ArenaTournamentInput input, List<ResolvedCombatRoster> rosters, ArenaMatch match, int attempt)
        {
            var t = XElement.Parse(input.Xml); var scene = new XElement(t.Element("Scene"));
            if (scene.Attribute("preset") != null)
            {
                var preset = input.SharedXml == null ? null : XElement.Parse(input.SharedXml).Element("ScenePresets")?.Elements("Scene").SingleOrDefault(p => ScenarioLoader.A(p, "id") == ScenarioLoader.A(scene, "preset"));
                if (preset == null) ScenarioLoader.Fail(scene, input.Source, "unknown scene preset " + ScenarioLoader.A(scene, "preset"));
                scene = ScenarioLoader.ResolveScenePreset(scene, preset);
            }
            var width = ScenarioLoader.I(scene, "sizeX", 100); var height = ScenarioLoader.I(scene, "sizeZ", 100);
            // Explicit defaults are frozen; the group parser otherwise defaults to 50.
            scene.SetAttributeValue("sizeX", width); scene.SetAttributeValue("sizeZ", height);
            var ids = match == null ? rosters.Take(2).Select(r => r.Id).ToArray() : new[] { match.Left, match.Right };
            var actors = new XElement("Actors");
            var steps = new XElement("Steps");
            for (int i = 0; i < 2; i++)
            {
                var r = rosters.Single(r => r.Id == ids[i]); var columns = PawnGroupFormation.ArenaRows(r.ResolvedCount, width);
                var actor = new XElement("PawnGroup", new XAttribute("id", i == 0 ? "left" : "right"), new XAttribute("team", i == 0 ? "A" : "B"), new XAttribute("count", r.ResolvedCount), new XAttribute("formation", "grid"), new XAttribute("columns", columns), new XAttribute("spacing", 2), new XElement("basePawnKind", r.PawnKind ?? "Colonist"));
                var xenotype = input.Entrants.Single(e => e.Id == r.Id).Xenotype;
                if (xenotype != null) actor.Add(new XElement("Xenotype", new XAttribute("def", xenotype)));
                var extent = PawnGroupFormation.Extents(actor, verticalFirst: true);
                if (input.Format != "knockout" && (extent.x > width - 3 || extent.z > height - 3))
                    throw new ScenarioConfigurationException("Challenge roster does not fit the arena map", "COMBAT_FORMATION_LIMIT_EXCEEDED");
                var x = i == 0 ? Math.Max(1, width / 5 - extent.x / 2) : Math.Min(width - extent.x - 2, width * 4 / 5 - extent.x / 2);
                var z = Math.Max(1, height / 2 - extent.z / 2);
                actor.SetAttributeValue("originX", x); actor.SetAttributeValue("originZ", z);
                actors.Add(actor);
                for (var member = 0; member < r.ResolvedCount; member++)
                    steps.Add(PreparationSteps(input, (i == 0 ? "left" : "right") + "[" + member + "]", r.Id, r.Members.Count > member ? r.Members[member].ConfigurationId : null, match == null && rosters.All(x => x.Members.Count == 0)).Elements());
            }
            var seed = match == null ? input.Seed : ArenaScheduler.AttemptSeed(input, match, attempt);
            var battle = new XElement("Case", new XAttribute("id", "arena"), new XAttribute("kind", "groupCombat"), new XAttribute("seed", seed), new XAttribute("name", input.Name), new XAttribute("timeoutRealSeconds", input.TimeoutRealSeconds), new XElement("testDefName", input.TestDefName), scene,
                new XElement("Teams", ids.Select((id, i) => {
                    var entrant = input.Entrants.Single(e => e.Id == id);
                    return new XElement("Team", new XAttribute("id", i == 0 ? "A" : "B"), new XAttribute("name", rosters.Single(r => r.Id == id).DisplayName ?? id),
                        entrant.Ai == null ? null : new XAttribute("ai", entrant.Ai));
                })), actors,
                new XElement("GroupCombat", new XAttribute("aiPolicy", ScenarioLoader.A(t.Element("Battle"), "aiPolicy", "assaultColony")), new XAttribute("durationSeconds", input.DurationSeconds.ToString("R", CultureInfo.InvariantCulture)), new XAttribute("timeSpeed", ScenarioLoader.A(t.Element("Battle"), "timeSpeed", "fast")), new XElement("End", new XAttribute("confirmationTicks", input.ConfirmationTicks)), t.Element("Metrics") == null ? null : new XElement(t.Element("Metrics")), t.Element("Overlay") == null ? null : new XElement(t.Element("Overlay"))), steps, t.Element("ActionBindings") == null ? null : new XElement(t.Element("ActionBindings")), t.Element("Requirements") == null ? null : new XElement(t.Element("Requirements")), t.Element("Output") == null ? null : new XElement(t.Element("Output")));
            var suite = new XElement("TestSuite", new XAttribute("schemaVersion", 1), new XAttribute("id", "arena.battle"), input.SharedXml == null ? null : XElement.Parse(input.SharedXml).Elements().Select(e => new XElement(e)), battle);
            var compiled = ScenarioLoader.Compile(new[] { new ScenarioInput { Path = input.Source, Sha256 = input.InputHash, Xml = suite.ToString(SaveOptions.DisableFormatting) } }, true);
            var c = compiled.Cases.Single();
            return new GroupCombatRoundInput { CaseId = (match?.Id ?? input.TestDefName) + "/attempt-" + (attempt + 1), TestDefName = input.TestDefName, Name = input.Name + " · " + (match?.Kind == "qualification" ? "资格赛" : match?.Kind == "extra" ? "低分加赛" : "第 " + (match?.Round ?? 1) + " 轮") + " · " + (attempt == 0 ? "首战" : "重赛 " + attempt), Seed = seed, Source = input.Source, InputHash = input.InputHash, Xml = c.Xml };
        }
        // "entrant" is a placeholder for each generated roster member, in XML order.
        // The same synchronous actions run before any preview or combat ticks.
        public static XElement PreparationCase(ArenaTournamentInput input, string entrantId = null, string configurationId = null)
        {
            var local = XElement.Parse(input.Xml).Element("ActionBindings");
            var result = new XElement("Case", PreparationSteps(input, "entrant", entrantId, configurationId), local == null ? null : new XElement(local));
            var shared = input.SharedXml == null ? null : XElement.Parse(input.SharedXml).Element("ActionBindings");
            if (shared != null) ScenarioActionBindings.Freeze(result, shared, input.Source);
            return result;
        }
        public static XElement PreparationSteps(ArenaTournamentInput input, string actor, string entrantId = null, string configurationId = null, bool validateAll = false)
        {
            var preparation = XElement.Parse(input.Xml).Element("Preparation");
            Check(preparation, "", "DebugAction Require Equip DropEquipment AddHediff RemoveHediff ApplyDamage", input.Source);
            var steps = new XElement("Steps");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var source in preparation?.Elements() ?? Enumerable.Empty<XElement>())
            {
                var id = Required(source, "id", input.Source);
                if (!ids.Add(id)) ScenarioLoader.Fail(source, input.Source, "duplicate arena preparation id");
                var selectedId = ScenarioLoader.A(source, "entrant"); var role = ScenarioLoader.A(source, "role"); var group = ScenarioLoader.A(source, "group");
                var selected = input.Entrants.Where(e => (selectedId == null || e.Id == selectedId) && (role == null || e.Role == role)).ToList();
                if (selected.Count == 0 || role != null && role != "boss" && role != "challenger") ScenarioLoader.Fail(source, input.Source, "preparation selector matches no entrant/role");
                if (group != null && !selected.Any(e => e.Squad != null && input.SquadTemplates[e.Squad].PawnGroups.Any(g => ScenarioLoader.A(XElement.Parse(g), "id") == group))) ScenarioLoader.Fail(source, input.Source, "preparation group must name a configured PawnGroup of the selected entrants");
                var step = new XElement(source);
                foreach (var scope in new[] { "entrant", "role", "group" }) step.Attribute(scope)?.Remove();
                if (step.Name == "DebugAction")
                {
                    var select = step.Element("Select");
                    if (step.Elements("Select").Count() != 1 || ScenarioLoader.A(select, "mode") != "single" || ScenarioLoader.A(select, "actor") != "entrant")
                        ScenarioLoader.Fail(source, input.Source, "arena preparation requires Select mode=single actor=entrant");
                }
                foreach (var reference in step.DescendantsAndSelf().Attributes().Where(a => a.Name == "actor" || a.Name == "target" || a.Name == "group"))
                {
                    if (reference.Value != "entrant") ScenarioLoader.Fail(source, input.Source, "arena preparation references must use the entrant placeholder");
                    reference.Value = actor;
                }
                if (!validateAll && entrantId != null && (!selected.Any(e => e.Id == entrantId) || group != null && group != configurationId)) continue;
                foreach (var reference in step.DescendantsAndSelf().Attributes("ref")) reference.Value = actor.Replace("[", "_").Replace("]", "") + "_" + reference.Value;
                step.SetAttributeValue("id", actor.Replace("[", "_").Replace("]", "") + "_" + id);
                steps.Add(step);
            }
            return steps;
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
                if (input.Format == "knockout") input.ShowcasePages.Add(compiled);
                else
                {
                    // Preserve configured captions/durations while separating roles.
                    foreach (var role in new[] { "boss", "challenger" })
                    {
                        var roleIds = compiled.EntrantIds.Where(id => input.Entrants.Single(e => e.Id == id).Role == role).ToList();
                        if (roleIds.Count > 0) input.ShowcasePages.Add(new ArenaShowcasePageInput { Seconds = compiled.Seconds,
                            Caption = compiled.Caption, EntrantIds = roleIds });
                    }
                }
            }
            foreach (var role in input.Format == "knockout" ? new string[] { null } : new[] { "boss", "challenger" })
            {
                var remaining = input.Entrants.Where(e => !used.Contains(e.Id) && (role == null || e.Role == role)).Select(e => e.Id).ToList();
                for (var offset = 0; offset < remaining.Count; offset += ArenaShowcase.PageSize)
                    input.ShowcasePages.Add(new ArenaShowcasePageInput { Seconds = input.ShowcasePageSeconds, EntrantIds = remaining.Skip(offset).Take(ArenaShowcase.PageSize).ToList() });
            }
        }
        private static string Required(XElement e, string a, string path)
        { var s = ScenarioLoader.A(e, a); if (string.IsNullOrWhiteSpace(s)) ScenarioLoader.Fail(e, path, a + " is required"); return s; }
        private static void Check(XElement e, string attrs, string children, string path, bool required = false)
        {
            if (e == null) { if (required) throw new ScenarioConfigurationException(path + ": missing required arena element"); return; }
            foreach (var a in e.Attributes()) if (!attrs.Split(' ').Contains(a.Name.ToString())) ScenarioLoader.UnknownField(e, path, "attribute", a.Name.ToString(), attrs);
            foreach (var c in e.Elements()) if (!children.Split(' ').Contains(c.Name.ToString())) ScenarioLoader.UnknownField(c, path, "element", c.Name.ToString(), children);
            if (e.Name.Namespace != XNamespace.None || e.Nodes().OfType<XText>().Any(n => !string.IsNullOrWhiteSpace(n.Value) && e.Name != "testDefName")) ScenarioLoader.Fail(e, path, "unknown arena namespace or text");
            if (e.Name != "ArenaSuite" && e.Name != "Entrants" && e.Name != "Showcase" && e.Name != "Page" && e.Name != "Preparation" && e.Elements().GroupBy(c => c.Name).Any(g => g.Count() > 1)) ScenarioLoader.Fail(e, path, "duplicate arena element");
        }
        private static int Integer(XElement e, string attr, int fallback, int min, int max, string path)
        { var value = ScenarioLoader.I(e, attr, fallback); if (value < min || value > max) ScenarioLoader.Fail(e, path, attr + " outside " + min + ".." + max); return value; }
        private static double Number(XElement e, string attr, double fallback, double min, double max, string path, bool exclusiveMin = false)
        { var value = ScenarioLoader.D(e, attr, fallback); if (!double.IsFinite(value) || value < min || exclusiveMin && value == min || value > max) ScenarioLoader.Fail(e, path, attr + " outside supported finite range"); return value; }
    }
}
