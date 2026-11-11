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
        public static ScenarioPlan Load(string path, bool groupCombat = false)
        {
            path = Path.GetFullPath(path);
            var files = Directory.Exists(path) ? Directory.GetFiles(path, "*.autotest.xml", SearchOption.TopDirectoryOnly).OrderBy(p => p, StringComparer.Ordinal).ToArray() : new[] { path };
            if (files.Length == 0) throw new ScenarioConfigurationException(path + ": no *.autotest.xml files");
            var inputs = files.Select(file => {
                var bytes = File.ReadAllBytes(file);
                using var sha = SHA256.Create();
                // Preserve the byte hash, while letting XmlReader honor the declared encoding.
                using var stream = new MemoryStream(bytes);
                using var reader = XmlReader.Create(stream, ReaderSettings());
                XDocument doc;
                try { doc = XDocument.Load(reader, LoadOptions.SetLineInfo | LoadOptions.PreserveWhitespace); }
                catch (XmlException ex) { throw XmlFailure(file, ex); }
                return new ScenarioInput { Path = file, Sha256 = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(), Xml = doc.ToString(SaveOptions.DisableFormatting) };
            }).ToList();
            if (groupCombat) ResolveGroupPawnFiles(inputs);
            return Compile(inputs, groupCombat);
        }

        public static ScenarioPlan Compile(IEnumerable<ScenarioInput> inputs, bool groupCombat = false)
        {
            var plan = new ScenarioPlan { Inputs = inputs.ToList(), Purpose = groupCombat ? "groupcombat" : "regression" };
            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var input in plan.Inputs)
            {
                using var reader = XmlReader.Create(new StringReader(input.Xml), ReaderSettings());
                XElement root;
                try { root = XDocument.Load(reader, LoadOptions.SetLineInfo).Root; }
                catch (XmlException ex) { throw XmlFailure(input.Path, ex); }
                if (root == null || root.Name != "TestSuite") Fail(root, input.Path, "expected TestSuite; ArenaSuite requires --mode arena");
                foreach (var e in root.DescendantsAndSelf()) e.AddAnnotation(new SourceLine { Line = ((IXmlLineInfo)e).LineNumber });
                ExpandBattleCases(root, input.Path, groupCombat);
                SquadTemplates.FreezeGroupReferences(root, input.Path, groupCombat);
                ValidateTree(root, input.Path, allowParameters: true);
                if (A(root, "schemaVersion") != "1") Fail(root, input.Path, "schemaVersion must be 1");
                if (A(root, "gameVersion", "1.7") != "1.7") Fail(root, input.Path, "only the 1.7 scenario backend is supported");
                Required(root, "id", input.Path);
                var templates = new Dictionary<string, XElement>(StringComparer.Ordinal);
                foreach (var t in root.Element("Templates")?.Elements() ?? Enumerable.Empty<XElement>())
                { var id = Required(t, "id", input.Path); if (templates.ContainsKey(id)) Fail(t, input.Path, "duplicate template id " + id); templates.Add(id, t); }
                var expanded = new Dictionary<string, XElement>(StringComparer.Ordinal);
                XElement Expand(string id, HashSet<string> stack)
                {
                    if (expanded.TryGetValue(id, out var result)) return Copy(result);
                    if (!templates.TryGetValue(id, out var node)) { Fail(root, input.Path, "unknown template " + id); return null; }
                    if (!stack.Add(id)) Fail(node, input.Path, "template cycle: " + string.Join(" -> ", stack) + " -> " + id);
                    result = node.Attribute("extends") == null ? Copy(node) : Merge(Expand(A(node, "extends"), stack), node);
                    stack.Remove(id);
                    expanded[id] = result;
                    return Copy(result);
                }
                foreach (var id in templates.Keys) Expand(id, new HashSet<string>());
                var scenePresets = new Dictionary<string, XElement>(StringComparer.Ordinal);
                foreach (var preset in root.Element("ScenePresets")?.Elements("Scene") ?? Enumerable.Empty<XElement>())
                {
                    var presetId = Required(preset, "id", input.Path);
                    if (scenePresets.ContainsKey(presetId)) Fail(preset, input.Path, "duplicate scene preset " + presetId);
                    if (preset.Attribute("preset") != null) Fail(preset, input.Path, "nested scene presets are unsupported");
                    scenePresets.Add(presetId, preset);
                }
                var localIds = new HashSet<string>();
                foreach (var node in root.Elements("Case"))
                {
                    var id = Required(node, "id", input.Path);
                    if (!localIds.Add(id)) Fail(node, input.Path, "duplicate Case id " + id);
                    var name = node.Element("testDefName")?.Value.Trim();
                    if (string.IsNullOrWhiteSpace(name) || !System.Text.RegularExpressions.Regex.IsMatch(name, @"^[A-Za-z_][A-Za-z0-9_.-]*$")) Fail(node, input.Path, "testDefName is required and must be a stable identifier");
                    var location = Location(node, input.Path);
                    if (names.TryGetValue(name, out var previous)) Fail(node, input.Path, "duplicate testDefName " + name + "; first definition: " + previous);
                    names.Add(name, location);
                    var copy = Copy(node);
                    ScenarioActionBindings.Freeze(copy, root.Element("ActionBindings"), input.Path);
                    if (root.Element("Requirements") != null)
                    {
                        var requirements = copy.Element("Requirements");
                        if (requirements == null) copy.AddFirst(Copy(root.Element("Requirements")));
                        else requirements.Add(root.Element("Requirements").Elements().Select(Copy));
                    }
                    var isGroup = A(copy, "caseType") == "GroupCombat" || A(copy, "kind") == "groupCombat";
                    if (isGroup != groupCombat) Fail(copy, input.Path, isGroup ? "GroupCombat requires its independent named benchmark entry" : "groupcombat accepts only GroupCombat rules");
                    if (isGroup)
                    {
                        copy.SetAttributeValue("caseType", "GroupCombat");
                        if (copy.Element("Steps") == null) copy.Add(new XElement("Steps"));
                    }
                    if (copy.Element("Scene") == null || copy.Element("Actors") == null || copy.Element("Steps") == null || !isGroup && copy.Element("Success") == null) Fail(node, input.Path, "Case requires Scene, Actors, Steps and Success (GroupCombat uses its own end rules)");
                    var scene = copy.Element("Scene");
                    if (scene.Attribute("preset") != null)
                    {
                        if (!scenePresets.TryGetValue(A(scene, "preset"), out var preset)) Fail(scene, input.Path, "unknown scene preset " + A(scene, "preset"));
                        var resolved = ResolveScenePreset(scene, preset);
                        scene.ReplaceWith(resolved);
                    }
                foreach (var actor in copy.Element("Actors").Elements().ToList())
                    {
                        if (actor.Attribute("template") == null) continue;
                        var combined = Merge(Expand(A(actor, "template"), new HashSet<string>()), actor);
                        combined.Name = actor.Name;
                        combined.Attribute("extends")?.Remove();
                        combined.Attribute("template")?.Remove();
                        actor.ReplaceWith(combined);
                    }
                    foreach (var instance in ExpandMatrix(copy, input.Path))
                    {
                        var xml = instance.Value;
                        ValidateTree(xml, input.Path);
                        xml.SetAttributeValue("packageId", A(root, "packageId", "ludeon.rimworld"));
                        ValidateCase(xml, input.Path, groupCombat);
                        plan.Cases.Add(new ScenarioCase { Name = A(node, "name", name), TestDefName = name, CaseId = name + "/" + instance.Key + "/1", Source = location, InputHash = input.Sha256, PackageId = A(root, "packageId", "ludeon.rimworld"), Xml = xml.ToString(SaveOptions.DisableFormatting), SourceLocations = xml.DescendantsAndSelf().ToDictionary(ElementKey, e => Location(e, input.Path)), CaseType = A(xml, "caseType", "ExternalScenario"), CoverageMode = A(xml.Element("Coverage"), "mode", "supplement"), Coverage = ParseCoverage(xml, input.Path), SelectionTargets = (xml.Element("Targets")?.Elements("Def") ?? Enumerable.Empty<XElement>()).Select(t => new ScenarioCoverageTarget { PackageId = A(root, "packageId", "ludeon.rimworld"), DefType = A(t, "defType"), DefName = A(t, "defName"), CaseType = A(xml, "caseType", "ExternalScenario") }).ToList() });
                    }
                }
            }
            if (plan.Cases.Count == 0) throw new ScenarioConfigurationException("No runnable Case definitions");
            // Detect conflicts before launching the game, even when both rules are outside the chosen range.
            if (!groupCombat) ScenarioPlanner.Select(plan.Cases, Array.Empty<ScenarioOrdinaryCandidate>(), int.MaxValue, scenarioOnly: true);
            return plan;
        }

        public static XmlReaderSettings ReaderSettings() => new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 4 * 1024 * 1024 };
        public static string A(XElement node, string name, string fallback = null) => (string)node?.Attribute(name) ?? fallback;
        public static int I(XElement node, string name, int fallback = 0) => int.Parse(A(node, name, fallback.ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture);
        public static double D(XElement node, string name, double fallback = 0) => double.Parse(A(node, name, fallback.ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture);
        public static string Location(XElement node, string path) => path + ":" + (node?.Annotation<SourceLine>()?.Line ?? (node as IXmlLineInfo)?.LineNumber ?? 0) + ":" + (node == null ? "" : string.Join("/", node.AncestorsAndSelf().Reverse().Select(e => e.Name.LocalName)));
        public static void Fail(XElement node, string path, string message, string suggestion = null) => throw new ScenarioConfigurationException(Location(node, path) + ": " + message) {
            FilePath = path,
            LineNumber = node?.Annotation<SourceLine>()?.Line ?? (node as IXmlLineInfo)?.LineNumber ?? 0,
            LinePosition = (node as IXmlLineInfo)?.LinePosition ?? 0,
            ElementPath = node == null ? null : string.Join("/", node.AncestorsAndSelf().Reverse().Select(e => e.Name.LocalName)),
            Suggestion = suggestion
        };
        public static ScenarioConfigurationException XmlFailure(string path, XmlException ex) => new ScenarioConfigurationException(path + ":" + ex.LineNumber + ": " + ex.Message) {
            FilePath = path, LineNumber = ex.LineNumber, LinePosition = ex.LinePosition,
            Suggestion = "检查此位置附近的标签闭合、属性引号、XML 转义和编码声明；不允许 DTD 或外部实体。"
        };
        public static void UnknownField(XElement node, string path, string kind, string field, string allowed) =>
            Fail(node, path, "unknown " + kind + " " + field, "此位置允许的" + (kind == "attribute" ? "属性" : "子节点") + "：" + (string.IsNullOrWhiteSpace(allowed) ? "无" : allowed) + "。名称区分大小写；请检查拼写与所属节点。");
        public static string ElementKey(XElement node) => string.Join("/", node.AncestorsAndSelf().Reverse().Select(e => e.Name.LocalName + "[" + e.ElementsBeforeSelf(e.Name).Count() + "]"));
        private static string Required(XElement node, string attr, string path) { var v = A(node, attr); if (string.IsNullOrWhiteSpace(v)) Fail(node, path, attr + " is required"); return v; }

        private static XElement Merge(XElement basis, XElement patch)
        {
            var result = Copy(basis);
            foreach (var a in patch.Attributes()) result.SetAttributeValue(a.Name, a.Value);
            foreach (var child in patch.Elements())
            {
                result.Elements(child.Name).Remove();
                result.Add(Copy(child));
            }
            return result;
        }
        public static XElement ResolveScenePreset(XElement scene, XElement preset)
        {
            var resolved = Merge(preset, scene);
            if (preset.Element("Environment") != null && scene.Element("Environment") != null)
                resolved.Element("Environment").ReplaceWith(Merge(preset.Element("Environment"), scene.Element("Environment")));
            resolved.Attribute("id")?.Remove(); resolved.Attribute("preset")?.Remove();
            return resolved;
        }

        private sealed class SourceLine { public int Line; }
        private static XElement Copy(XElement source)
        {
            var result = new XElement(source);
            foreach (var pair in source.DescendantsAndSelf().Zip(result.DescendantsAndSelf(), (a, b) => new { a, b }))
                pair.b.AddAnnotation(new SourceLine { Line = pair.a.Annotation<SourceLine>()?.Line ?? ((IXmlLineInfo)pair.a).LineNumber });
            return result;
        }

        private static void ValidateCase(XElement c, string path, bool groupCombat = false)
        {
            var type = A(c, "caseType", "ExternalScenario");
            if (type != "ExternalScenario" && !ScenarioTypes.NonUi.Contains(type)) Fail(c, path, "unknown/non-UI caseType " + type);
            if (type == "GroupCombat" && !groupCombat) Fail(c, path, "GroupCombat requires its independent named benchmark entry");
            if (!groupCombat && (c.Attribute("kind") != null || c.Element("GroupCombat") != null || c.Element("Output") != null)) Fail(c, path, "benchmark configuration cannot enter a regression rule");
            var scene = c.Element("Scene");
            try { CombatMapCatalog.NormalizeScene(scene); }
            catch (ScenarioConfigurationException ex) { Fail(scene, path, ex.Message); }
            if (!groupCombat && scene.Attribute("battlefield") != null) Fail(scene, path, "battlefield presets require groupCombat or arena");
            var view = ScenarioViews.Types.Contains(type);
            if (A(scene, "type", groupCombat ? "mainMap" : view ? "studio" : "pocketMap") != (groupCombat ? "mainMap" : view ? "studio" : "pocketMap")) Fail(scene, path, groupCombat ? "GroupCombat requires a new ordinary mainMap" : view ? "View uses the existing main-map studio (type=studio), not a pocket map" : "this behavior backend requires pocketMap");
            if (I(scene, "sizeX", 50) < 30 || I(scene, "sizeX", 50) > 250 || I(scene, "sizeZ", 50) < 30 || I(scene, "sizeZ", 50) > 250) Fail(scene, path, "map dimensions must be 30..250");
            if (A(scene, "revealFog", "all") != "all") Fail(scene, path, "batch 1 requires revealFog=all");
            if (scene.Attribute("id") != null) Fail(scene, path, "Scene id belongs only to ScenePresets");
            foreach (var rect in scene.Element("Layout")?.Elements() ?? Enumerable.Empty<XElement>())
                if (I(rect, "width") < 1 || I(rect, "height") < 1 || I(rect, "x") < 0 || I(rect, "z") < 0 || I(rect, "x") + I(rect, "width") > I(scene, "sizeX", 50) || I(rect, "z") + I(rect, "height") > I(scene, "sizeZ", 50)) Fail(rect, path, "layout rectangle outside map");
            var environment = scene.Element("Environment");
            if (environment != null && !new[] { "true", "false" }.Contains(A(environment, "naturalSun", "false"))) Fail(environment, path, "naturalSun must be true/false");
            if (D(c, "timeoutRealSeconds", 60) <= 0) Fail(c, path, "timeoutRealSeconds must be positive");
            var actors = new HashSet<string>();
            var teams = new HashSet<string>();
            foreach (var team in c.Element("Teams")?.Elements("Team") ?? Enumerable.Empty<XElement>())
            {
                if (!groupCombat && (team.Attribute("factionSource") != null || string.IsNullOrWhiteSpace(A(team, "factionDef")))) Fail(team, path, "regression team requires an existing factionDef");
                if (!teams.Add(Required(team, "id", path))) Fail(team, path, "duplicate team id");
            }
            foreach (var actor in c.Element("Actors").Elements())
            {
                var id = Required(actor, "id", path);
                if (!actors.Add(id)) Fail(actor, path, "duplicate actor id " + id);
                if (A(actor, "team") != null && !teams.Contains(A(actor, "team"))) Fail(actor, path, "unknown team");
                if (actor.Attribute("combatPowerBudget") != null)
                {
                    if (!groupCombat || actor.Name != "PawnGroup") Fail(actor, path, "combatPowerBudget requires a groupcombat PawnGroup");
                    if (actor.Attribute("count") != null) Fail(actor, path, "count and combatPowerBudget are mutually exclusive");
                    CombatRoster.Positive(A(actor, "combatPowerBudget"));
                }
                if (actor.Attribute("costMode") != null)
                {
                    if (!groupCombat || actor.Name != "PawnGroup") Fail(actor, path, "costMode requires a groupcombat PawnGroup");
                    SquadTemplates.ValidateCostMode(A(actor, "costMode"));
                }
                var explicitAge = actor.Element("Age")?.Attribute("biologicalYears");
                if (explicitAge != null && (D(actor.Element("Age"), "biologicalYears") < 0
                    || actor.Element("minGenerationAge") != null && D(actor.Element("Age"), "biologicalYears") < double.Parse(actor.Element("minGenerationAge").Value, CultureInfo.InvariantCulture)
                    || actor.Element("maxGenerationAge") != null && D(actor.Element("Age"), "biologicalYears") > double.Parse(actor.Element("maxGenerationAge").Value, CultureInfo.InvariantCulture))) Fail(actor, path, "Age conflicts with generation age range");
                var count = I(actor, "count", 1);
                var maxCount = groupCombat ? CombatRoster.MaxTeamMembers : 100;
                if (count < 1 || count > maxCount) Fail(actor, path, "count must be 1.." + maxCount);
                if (actor.Name != "DefRef")
                {
                    var x = I(actor, "x", I(actor, "originX", 20)); var z = I(actor, "z", I(actor, "originZ", 20));
                    var grid = actor.Name == "PawnGroup" && A(actor, "formation", "line") == "grid";
                    var columns = grid ? I(actor, "columns", 5) : count;
                    if (columns < 1 || columns > maxCount || I(actor, "spacing", 2) < 1) Fail(actor, path, "columns and spacing must be positive (columns <= " + maxCount + ")");
                    var footprint = actor.Name == "PawnGroup" ? PawnGroupFormation.Extents(actor, groupCombat) : actor.Name == "TerrainArea" ? (x: I(actor, "width") - 1, z: I(actor, "height") - 1) : (x: 0, z: 0);
                    if (x < 0 || z < 0 || x + footprint.x >= I(scene, "sizeX", 50) || z + footprint.z >= I(scene, "sizeZ", 50)) Fail(actor, path, "actor lies outside the configured scene");
                }
                for (var n = 0; n < count && actor.Name == "PawnGroup"; n++) if (!actors.Add(id + "[" + n + "]")) Fail(actor, path, "duplicate group member");
                if (actor.Name == "Corpse" && !actors.Add(id + ".innerPawn")) Fail(actor, path, "duplicate corpse innerPawn");
                if (actor.Name == "Pawn" || actor.Name == "PawnGroup" || actor.Name == "Corpse")
                    if (actor.Attribute("squad") == null && string.IsNullOrWhiteSpace(actor.Element("basePawnKind")?.Value)) Fail(actor, path, "basePawnKind is required");
            }
            var steps = new HashSet<string>();
            if (new[] { "PlantGrowthView", "BuildingView", "TerrainPatchView" }.Contains(type))
                foreach (var viewStep in c.Element("Steps").Elements("View").Where(v => v.Attribute("directions") == null)) viewStep.SetAttributeValue("directions", "North");
            foreach (var step in c.Element("Steps").Elements())
            {
                var id = Required(step, "id", path);
                if (id == "prepared") Fail(step, path, "step id 'prepared' is reserved for the scene preparation baseline");
                if (!steps.Add(id)) Fail(step, path, "duplicate step id");
            }
            foreach (var step in c.Element("Steps").Elements().Where(s => s.Attribute("output") != null))
                if (!actors.Add(Required(step, "output", path))) Fail(step, path, "duplicate generated actor id");
            foreach (var element in c.Descendants())
            {
                ScenarioViews.Validate(element, path);
                if (element.Name == "ApplyDamage" && (D(element, "amount") <= 0 || D(element, "armorPenetration", 0) < 0)) Fail(element, path, "damage and armor penetration must be nonnegative; damage must be positive");
                if (element.Attribute("xenogene") != null && !new[] { "true", "false" }.Contains(A(element, "xenogene"))) Fail(element, path, "xenogene must be true/false");
                foreach (var attribute in new[] { "actor", "target", "group", "focus", "relativeTo", "worktable", "instigator" })
                    if (A(element, attribute) != null && !actors.Contains(A(element, attribute))) Fail(element, path, "unknown actor reference " + A(element, attribute));
                if (A(element, "ref") != null && !steps.Contains(A(element, "ref"))) Fail(element, path, "unknown step reference " + A(element, "ref"));
                if (A(element, "since") != null && A(element, "since") != "prepared" && !steps.Contains(A(element, "since"))) Fail(element, path, "unknown baseline reference");
                if (A(element, "mode") != null && new[] { "Apparel", "Equipment", "Traits", "Skills", "Hediffs", "Abilities", "Genes" }.Contains(element.Name.LocalName) && !new[] { "inherit", "patch", "replace" }.Contains(A(element, "mode"))) Fail(element, path, "invalid merge mode");
                if (A(element, "quantifier") != null && !new[] { "any", "all", "count" }.Contains(A(element, "quantifier"))) Fail(element, path, "invalid quantifier");
                if (A(element, "transition") != null && !new[] { "added", "removed" }.Contains(A(element, "transition"))) Fail(element, path, "invalid transition");
                if (A(element, "observation") != null && !new[] { "current", "eventually", "always" }.Contains(A(element, "observation"))) Fail(element, path, "invalid observation");
                if (element.Name == "Not" && element.Elements().Count() != 1) Fail(element, path, "Not requires one condition");
                if (new[] { "All", "Any", "Success", "Wait", "Require", "Expect" }.Contains(element.Name.LocalName) && !element.Elements().Any()) Fail(element, path, "condition cannot be empty");
                if (A(element, "force", "false") != "false") Fail(element, path, "external scenes cannot force abilities");
                if (A(element, "spawn") != null && !new[] { "true", "false" }.Contains(A(element, "spawn"))) Fail(element, path, "spawn must be true/false");
                if (A(element, "melee") != null && !new[] { "true", "false" }.Contains(A(element, "melee"))) Fail(element, path, "melee must be true/false");
                if (!new[] { "line", "grid" }.Contains(A(element, "formation", "line"))) Fail(element, path, "formation must be line/grid");
                if (A(element, "onMissing", "fail") != "fail" && A(element, "onMissing") != "add") Fail(element, path, "onMissing must be fail/add");
                if (A(element, "durationTicks") != null && I(element, "durationTicks") < 1 || A(element, "stableTicks") != null && I(element, "stableTicks") < 1 || A(element, "count") != null && I(element, "count") < 1) Fail(element, path, "tick durations, stability and counts must be positive");
                if (A(element, "timeoutRealSeconds") != null && D(element, "timeoutRealSeconds") <= 0) Fail(element, path, "timeoutRealSeconds must be positive");
                if (element.Name == "DebugAction") ScenarioDebugActions.Validate(element, path);
                if (element.Name == "ExecuteRecipe" && A(element, "accelerate") != "true") Fail(element, path, "native functional recipe execution requires explicit accelerate=true");
                if (element.Name == "SetRot" && !new[] { "Fresh", "Rotting", "Dessicated" }.Contains(A(element, "state"))) Fail(element, path, "invalid rot stage");
                if (element.Name == "CastAbility" && (A(element, "target") != null ? element.Attribute("targetX") != null || element.Attribute("targetZ") != null : element.Attribute("targetX") == null || element.Attribute("targetZ") == null)) Fail(element, path, "CastAbility requires either one target actor or targetX/targetZ");
                if (element.Name == "Wait" && A(element, "observation") == "always" && element.Attribute("durationTicks") == null) Fail(element, path, "always observation requires durationTicks");
                if (element.Name == "HasHediff" && A(element, "actor") == null) Fail(element, path, "HasHediff requires actor");
                if (element.Name == "Corpse" && A(element, "spawn", "true") == "false") Fail(element, path, "Corpse creation requires a spawned native pawn");
                if (element.Name == "Replace" && (A(element, "matchDef") == null) == (A(element, "matchLayer") == null)) Fail(element, path, "Replace requires exactly one of matchDef/matchLayer");
                if (A(element, "mode") == "inherit" && element.HasElements) Fail(element, path, "inherit cannot contain modifications");
                if (element.Name == "StepSucceeded")
                {
                    var ownerStep = element.Ancestors().FirstOrDefault(e => e.Parent?.Name == "Steps");
                    if (ownerStep != null && !ownerStep.ElementsBeforeSelf().Any(e => A(e, "id") == A(element, "ref"))) Fail(element, path, "StepSucceeded must reference an earlier completed step");
                }
                if (A(element, "since") != null && A(element, "since") != "prepared")
                {
                    var ownerStep = element.AncestorsAndSelf().FirstOrDefault(e => e.Parent?.Name == "Steps");
                    if (ownerStep != null && !ownerStep.ElementsBeforeSelf().Concat(new[] { ownerStep }).Any(e => A(e, "id") == A(element, "since"))) Fail(element, path, "baseline must precede observation");
                }
            }
            if (groupCombat) ValidateGroupCombat(c, path);
        }

        // Exact parent/child and attribute whitelist. No Class, private paths, arbitrary calls or ignored fields.
        private static readonly Dictionary<string, string> Children = new Dictionary<string, string> {
            {"TestSuite", "Requirements ActionBindings Templates ScenePresets Case"}, {"Requirements", "Mod DLC"}, {"Templates", "PawnTemplate"}, {"ScenePresets", "Scene"},
            {"Scene", "Environment Layout Validation"}, {"Layout", "TerrainRect RoofRect ThingRect"}, {"Validation", "Reachable"},
            {"PawnTemplate", "basePawnKind race gender minGenerationAge maxGenerationAge apparelTags apparelMoney weaponTags weaponMoney forcedTraits skills abilities Apparel Equipment Traits Skills Hediffs Abilities Appearance Genes Xenotype Age Resources"},
            {"Resources", "GeneResource Psyfocus AxolotlQi"},
            {"Case", "testDefName Requirements ActionBindings Scene Teams Actors Steps Success Targets Coverage Matrix GroupCombat Output"}, {"GroupCombat", "End Metrics Overlay Experiments"}, {"Overlay", "Rankings Summary"}, {"Experiments", "Seed"}, {"Targets", "Def"},
            {"Coverage", "Target"}, {"Target", "Evidence"}, {"Matrix", "Row"}, {"Row", "Param"},
            {"Teams", "Team Relation"}, {"Actors", "Pawn PawnGroup Building Thing Corpse DefRef TerrainArea"},
            {"Steps", "Require Checkpoint Wait Screenshot View SetPlantGrowth CastAbility Attack Spawn CreateThing DropEquipment Equip AddHediff RemoveHediff Destroy Kill Pause DebugAction ExecuteRecipe TransferThing DropInventory SetRot Resurrect ApplyDamage BindMech ManTurret"},
            {"DebugAction", "Select Arg Expect"},
            {"ActionBindings", "ActionBinding"}, {"ActionBinding", "Parameters Arguments Result ExpectHediff"},
            {"Parameters", "Parameter"}, {"Arguments", "Argument"},
            {"ExecuteRecipe", "Ingredients"}, {"Ingredients", "Ingredient"},
            {"Genes", "Add Remove"},
            {"Apparel", "Add Replace Remove"}, {"Equipment", "SetPrimary"}, {"Traits", "Set Remove"}, {"Skills", "Set"}, {"Hediffs", "Ensure Remove"}, {"Abilities", "Ensure Remove"},
            {"Appearance", "hair bodyType headType hairColor skinColor"}, {"apparelTags", "li"}, {"weaponTags", "li"},
            {"forcedTraits", "li"}, {"skills", "li"}, {"abilities", "li"}, {"li", "def degree skill range passion"}
        };
        private const string Conditions = "All Any Not HasHediff ThingExists ActorState CorpseState ActorCount Equipped Wearing HasTrait SkillLevel HasAbility StepSucceeded HealthPercent NeedLevel ResourceLevel ComponentState EnvironmentState ActorPosition ModResourceLevel ThingHeld ThingQuantity HasController PawnAppearance HasGene HasXenotype PlantGrowth TerrainState ThingStyle";
        private static readonly Dictionary<string, string> Attributes = new Dictionary<string, string> {
            {"GroupCombat", "start aiPolicy durationSeconds timeSpeed"}, {"End", "condition onTimeLimit onTie confirmationTicks"},
            {"Metrics", "damageBasis dpsBasis teamDpsBasis population meanPopulation includeZeroValues distributions detailLimit requireCompleteAttribution"},
            {"Overlay", "enabled layout topUnitsPerMetric topTypesPerTeam humanPortrait refreshRealMilliseconds"}, {"Rankings", "damageDealt damageTaken"}, {"Summary", "dps damageTaken"},
            {"Experiments", "swapSides"}, {"Seed", "value"}, {"Output", "purpose showAllParticipants width height"},
            {"TestSuite", "schemaVersion id packageId gameVersion"}, {"Case", "id name seed timeoutRealSeconds caseType kind"}, {"PawnTemplate", "id extends"},
            {"Coverage", "mode"}, {"Target", "packageId defType defName caseType variant"}, {"Evidence", "step"}, {"Row", "id"}, {"Param", "name value"},
            {"Scene", "id preset type sizeX sizeZ revealFog generatorDef battlefield featureDensity spawnClearance"}, {"Team", "id name factionDef factionSource ai defendX defendZ defendRadius"}, {"Relation", "a b value"},
            {"Environment", "weather temperature roof naturalSun"}, {"TerrainRect", "def x z width height"}, {"RoofRect", "def x z width height"}, {"ThingRect", "def stuff x z width height rotation"}, {"Reachable", "fromX fromZ toX toZ"},
            {"EnvironmentState", "property def min max"},
            {"Def", "defType defName"}, {"Mod", "packageId"}, {"DLC", "packageId"},
            {"Require", "id"}, {"Checkpoint", "id"}, {"Wait", "id timeoutRealSeconds stableTicks durationTicks observation"},
            {"Screenshot", "id focus cameraSize stableFrames"}, {"View", "id actor directions pose loadout targetDef cameraSize stableFrames composite timeoutRealSeconds"},
            {"SetPlantGrowth", "id actor value"}, {"PlantGrowth", "actor min max"}, {"TerrainState", "actor def"},
            {"PawnAppearance", "actor property value"}, {"HasGene", "actor def active xenogene"}, {"HasXenotype", "actor def"},
            {"ThingStyle", "actor stuff quality color rotation"}, {"Age", "biologicalYears chronologicalYears"},
            {"GeneResource", "def value"}, {"Psyfocus", "value"}, {"AxolotlQi", "value"},
            {"CastAbility", "id actor def target targetX targetZ force timeoutRealSeconds"}, {"Attack", "id actor target melee timeoutRealSeconds"},
            {"ActorPosition", "actor x z radius"}, {"ModResourceLevel", "actor resource min max"},
            {"ExecuteRecipe", "id actor recipe worktable accelerate output"}, {"Ingredient", "actor count"}, {"TransferThing", "id actor target"}, {"DropInventory", "id actor target"}, {"SetRot", "id actor state"}, {"Resurrect", "id actor"},
            {"ThingHeld", "actor target"}, {"ThingQuantity", "actor def min max"},
            {"Genes", "mode"}, {"Xenotype", "def"}, {"ApplyDamage", "id actor def amount armorPenetration part instigator"},
            {"BindMech", "id actor target"}, {"ManTurret", "id actor target timeoutRealSeconds"}, {"HasController", "actor target"},
            {"Spawn", "id actor"}, {"Equip", "id actor def stuff"}, {"AddHediff", "id actor def severity part durationTicks"}, {"RemoveHediff", "id actor def part"},
            {"CreateThing", "id def stuff x z count"}, {"DropEquipment", "id actor"},
            {"Destroy", "id actor"}, {"Kill", "id actor"}, {"Pause", "id"},
            {"DebugAction", "id actionId value def severity part durationTicks"}, {"Select", "mode actor minX minZ maxX maxZ filter relativeTo"},
            {"ActionBinding", "id packageId type method"}, {"Parameter", "name type min max"},
            {"Argument", "type source parameter value def"}, {"Arg", "name value"}, {"Result", "equals"},
            {"ExpectHediff", "type def count property equals equalsParameter"},
            {"HealthPercent", "actor min max quantifier equals observation"}, {"NeedLevel", "actor def min max quantifier equals"}, {"ResourceLevel", "actor def min max quantifier equals"}, {"ComponentState", "actor component state min max"},
            {"Apparel", "mode"}, {"Equipment", "mode"}, {"Traits", "mode"}, {"Skills", "mode"}, {"Hediffs", "mode"}, {"Abilities", "mode"},
            {"Add", "def stuff quality color"}, {"Replace", "matchDef matchLayer withDef stuff quality color onMissing"}, {"Remove", "def part"}, {"SetPrimary", "def stuff quality color"},
            {"Set", "def degree level passion"}, {"Ensure", "def severity part durationTicks"},
            {"StepSucceeded", "ref"}, {"ActorCount", "group equals"},
            {"HasHediff", "actor def severityMin severityMax quantifier equals transition since observation"},
            {"ThingExists", "actor def quantifier equals transition since observation"}, {"ActorState", "actor state quantifier equals observation"}, {"CorpseState", "actor state"},
            {"Equipped", "actor def"}, {"Wearing", "actor def"}, {"HasTrait", "actor def degree"}, {"SkillLevel", "actor def min max"}, {"HasAbility", "actor def"}
        };

        private static void ValidateTree(XElement node, string path, bool allowParameters = false)
        {
            var name = node.Name.LocalName;
            if (name == "SquadTemplate") { SquadTemplates.Parse(node, path); return; }
            var required = name switch {
                "Thing" or "Building" or "CreateThing" or "Add" or "SetPrimary" or "Ensure" or "Set" or "Remove" => "def",
                "Team" => "id", "Relation" => "a b value", "Def" or "DefRef" => "defType defName",
                "Mod" or "DLC" => "packageId", "Target" => "defType defName caseType", "Evidence" => "step", "Row" => "id", "Param" => "name value", "CastAbility" => "actor def", "Attack" => "actor target",
                "Equip" or "AddHediff" or "RemoveHediff" => "actor def", "Spawn" or "DropEquipment" or "Destroy" or "Kill" => "actor",
                "HasHediff" or "Equipped" or "Wearing" or "HasTrait" or "SkillLevel" or "HasAbility" or "HasGene" or "HasXenotype" or "TerrainState" => "actor def",
                "View" or "PlantGrowth" or "ThingStyle" => "actor", "SetPlantGrowth" => "actor value", "PawnAppearance" => "actor property value",
                "GeneResource" => "def value", "Psyfocus" or "AxolotlQi" => "value",
                "ActorState" or "CorpseState" => "actor state", "HealthPercent" => "actor", "ActorPosition" => "actor x z", "ModResourceLevel" => "actor resource", "NeedLevel" or "ResourceLevel" => "actor def", "ComponentState" => "actor component", "DebugAction" => "actionId", "ActorCount" => "group equals", "StepSucceeded" => "ref", "Replace" => "withDef",
                "TerrainArea" or "TerrainRect" or "ThingRect" => "def x z width height", "RoofRect" => "x z width height", "Reachable" => "fromX fromZ toX toZ", "ExecuteRecipe" => "actor recipe worktable accelerate output", "Ingredient" => "actor count", "SetRot" => "actor state", "Resurrect" => "actor", "TransferThing" or "DropInventory" or "ThingHeld" or "BindMech" or "ManTurret" or "HasController" => "actor target", "Xenotype" => "def", "ApplyDamage" => "actor def amount", _ => ""
            };
            foreach (var field in required.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)) Required(node, field, path);
            var actor = new[] { "Pawn", "PawnGroup", "Corpse" }.Contains(name);
            var thing = new[] { "Thing", "Building" }.Contains(name);
            var attrs = name == "PawnGroup" ? "id template squad costMode team count combatPowerBudget originX originZ spacing formation columns spawn" : actor ? "id template team x z spawn" : thing ? "id def stuff team x z count quality color rotation growth spawn" : name == "DefRef" ? "id defType defName" : name == "TerrainArea" ? "id def x z width height" : Attributes.TryGetValue(name, out var text) ? text : "";
            if (name == "Set") attrs = node.Parent?.Name == "Skills" ? "def level passion" : "def degree";
            if (name == "Ensure") attrs = node.Parent?.Name == "Hediffs" ? "def severity part durationTicks" : "def";
            if (name == "Remove") attrs = node.Parent?.Name == "Hediffs" ? "def part" : "def";
            if (name == "Add" && node.Parent?.Name == "Genes") attrs = "def xenogene";
            foreach (var a in node.Attributes()) if (!attrs.Split(' ').Contains(a.Name.ToString())) UnknownField(node, path, "attribute", a.Name.ToString(), attrs);
            var children = actor ? Children["PawnTemplate"] + (name == "PawnGroup" ? " SquadTemplate" : "") : name == "li" ? node.Parent?.Name.LocalName switch { "forcedTraits" => "def degree", "skills" => "skill range passion", _ => "" } : Children.TryGetValue(name, out var allowed) ? allowed : new[] { "Require", "Expect", "Wait", "Success", "All", "Any", "Not" }.Contains(name) ? Conditions : "";
            foreach (var e in node.Elements())
            {
                if (!children.Split(' ').Contains(e.Name.ToString())) UnknownField(e, path, "element", e.Name.ToString(), children);
                var multiple = name == "TestSuite" ? e.Name == "Case" : name == "DebugAction" ? e.Name == "Arg" : name == "ActionBinding" ? e.Name == "ExpectHediff" : name == "Resources" ? e.Name == "GeneResource" : new[] { "ActionBindings", "Parameters", "Arguments", "Templates", "ScenePresets", "Experiments", "Layout", "Validation", "Actors", "Steps", "Teams", "Requirements", "Targets", "Coverage", "Target", "Matrix", "Row", "Ingredients", "Apparel", "Equipment", "Traits", "Skills", "Hediffs", "Abilities", "Genes", "All", "Any", "Require", "Expect", "Wait", "Success", "forcedTraits", "skills", "abilities", "apparelTags", "weaponTags" }.Contains(name);
                if (node.Elements(e.Name).Count() > 1 && !multiple) Fail(e, path, "duplicate field " + e.Name);
                ValidateTree(e, path, allowParameters);
            }
            if (node.HasElements && node.Nodes().OfType<XText>().Any(t => !string.IsNullOrWhiteSpace(t.Value))) Fail(node, path, "mixed text is not supported");
            foreach (var a in node.Attributes().Where(a => new[] { "x", "z", "count", "sizeX", "sizeZ", "seed", "originX", "originZ", "spacing", "columns", "stableTicks", "durationTicks", "stableFrames", "confirmationTicks", "degree", "level", "equals", "width", "height", "minX", "minZ", "maxX", "maxZ", "fromX", "fromZ", "toX", "toZ", "targetX", "targetZ", "spawnClearance" }.Contains(a.Name.LocalName) && !(a.Name == "equals" && (name == "Result" || name == "ExpectHediff"))))
                if (!(allowParameters && a.Value.Contains("${")) && !int.TryParse(a.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)) Fail(node, path, "invalid integer " + a.Name);
            foreach (var a in node.Attributes().Where(a => new[] { "timeoutRealSeconds", "durationSeconds", "defendRadius", "severity", "severityMin", "severityMax", "min", "max", "value", "temperature", "radius", "amount", "armorPenetration", "growth", "cameraSize", "biologicalYears", "chronologicalYears", "featureDensity" }.Contains(a.Name.LocalName) && !(name == "Param" || name == "Relation" || name == "PawnAppearance" || name == "Argument" || name == "Arg")))
                if (!(allowParameters && a.Value.Contains("${")) && (!double.TryParse(a.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) || double.IsNaN(d) || double.IsInfinity(d))) Fail(node, path, "invalid finite number " + a.Name);
        }
    }
}
