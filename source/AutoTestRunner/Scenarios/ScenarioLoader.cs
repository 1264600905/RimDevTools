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
    public static class ScenarioLoader
    {
        public static ScenarioPlan Load(string path)
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
                catch (XmlException ex) { throw new ScenarioConfigurationException(file + ":" + ex.LineNumber + ": " + ex.Message); }
                return new ScenarioInput { Path = file, Sha256 = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(), Xml = doc.ToString(SaveOptions.DisableFormatting) };
            }).ToList();
            return Compile(inputs);
        }

        public static ScenarioPlan Compile(IEnumerable<ScenarioInput> inputs)
        {
            var plan = new ScenarioPlan { Inputs = inputs.ToList() };
            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var input in plan.Inputs)
            {
                using var reader = XmlReader.Create(new StringReader(input.Xml), ReaderSettings());
                XElement root;
                try { root = XDocument.Load(reader, LoadOptions.SetLineInfo).Root; }
                catch (XmlException ex) { throw new ScenarioConfigurationException(input.Path + ":" + ex.LineNumber + ": " + ex.Message); }
                if (root == null || root.Name != "TestSuite") Fail(root, input.Path, "expected TestSuite");
                foreach (var e in root.DescendantsAndSelf()) e.AddAnnotation(new SourceLine { Line = ((IXmlLineInfo)e).LineNumber });
                ValidateTree(root, input.Path);
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
                    copy.SetAttributeValue("packageId", A(root, "packageId", "ludeon.rimworld"));
                    if (root.Element("Requirements") != null)
                    {
                        var requirements = copy.Element("Requirements");
                        if (requirements == null) copy.AddFirst(Copy(root.Element("Requirements")));
                        else requirements.Add(root.Element("Requirements").Elements().Select(Copy));
                    }
                    if (copy.Element("Scene") == null || copy.Element("Actors") == null || copy.Element("Steps") == null || copy.Element("Success") == null) Fail(node, input.Path, "Case requires Scene, Actors, Steps and Success");
                foreach (var actor in copy.Element("Actors").Elements().ToList())
                    {
                        if (actor.Attribute("template") == null) continue;
                        var combined = Merge(Expand(A(actor, "template"), new HashSet<string>()), actor);
                        combined.Name = actor.Name;
                        actor.ReplaceWith(combined);
                    }
                    ValidateCase(copy, input.Path);
                    plan.Cases.Add(new ScenarioCase { Name = A(node, "name", name), TestDefName = name, CaseId = name + "/default/1", Source = location, InputHash = input.Sha256, PackageId = A(root, "packageId", "ludeon.rimworld"), Xml = copy.ToString(SaveOptions.DisableFormatting), SourceLocations = copy.DescendantsAndSelf().ToDictionary(ElementKey, e => Location(e, input.Path)) });
                }
            }
            if (plan.Cases.Count == 0) throw new ScenarioConfigurationException("No runnable Case definitions");
            return plan;
        }

        public static XmlReaderSettings ReaderSettings() => new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 4 * 1024 * 1024 };
        public static string A(XElement node, string name, string fallback = null) => (string)node?.Attribute(name) ?? fallback;
        public static int I(XElement node, string name, int fallback = 0) => int.Parse(A(node, name, fallback.ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture);
        public static double D(XElement node, string name, double fallback = 0) => double.Parse(A(node, name, fallback.ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture);
        public static string Location(XElement node, string path) => path + ":" + (node?.Annotation<SourceLine>()?.Line ?? (node as IXmlLineInfo)?.LineNumber ?? 0) + ":" + (node == null ? "" : string.Join("/", node.AncestorsAndSelf().Reverse().Select(e => e.Name.LocalName)));
        public static void Fail(XElement node, string path, string message) => throw new ScenarioConfigurationException(Location(node, path) + ": " + message);
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

        private sealed class SourceLine { public int Line; }
        private static XElement Copy(XElement source)
        {
            var result = new XElement(source);
            foreach (var pair in source.DescendantsAndSelf().Zip(result.DescendantsAndSelf(), (a, b) => new { a, b }))
                pair.b.AddAnnotation(new SourceLine { Line = pair.a.Annotation<SourceLine>()?.Line ?? ((IXmlLineInfo)pair.a).LineNumber });
            return result;
        }

        private static void ValidateCase(XElement c, string path)
        {
            var scene = c.Element("Scene");
            if (A(scene, "type", "pocketMap") != "pocketMap") Fail(scene, path, "only pocketMap is available in batch 1");
            if (I(scene, "sizeX", 50) < 30 || I(scene, "sizeX", 50) > 250 || I(scene, "sizeZ", 50) < 30 || I(scene, "sizeZ", 50) > 250) Fail(scene, path, "map dimensions must be 30..250");
            if (A(scene, "revealFog", "all") != "all") Fail(scene, path, "batch 1 requires revealFog=all");
            if (D(c, "timeoutRealSeconds", 60) <= 0) Fail(c, path, "timeoutRealSeconds must be positive");
            var actors = new HashSet<string>();
            var teams = new HashSet<string>();
            foreach (var team in c.Element("Teams")?.Elements("Team") ?? Enumerable.Empty<XElement>())
                if (!teams.Add(Required(team, "id", path))) Fail(team, path, "duplicate team id");
            foreach (var actor in c.Element("Actors").Elements())
            {
                var id = Required(actor, "id", path);
                if (!actors.Add(id)) Fail(actor, path, "duplicate actor id " + id);
                if (A(actor, "team") != null && !teams.Contains(A(actor, "team"))) Fail(actor, path, "unknown team");
                var count = I(actor, "count", 1);
                if (count < 1 || count > 100) Fail(actor, path, "count must be 1..100");
                for (var n = 0; n < count && actor.Name == "PawnGroup"; n++) if (!actors.Add(id + "[" + n + "]")) Fail(actor, path, "duplicate group member");
                if (actor.Name == "Corpse" && !actors.Add(id + ".innerPawn")) Fail(actor, path, "duplicate corpse innerPawn");
                if (actor.Name == "Pawn" || actor.Name == "PawnGroup" || actor.Name == "Corpse")
                    if (string.IsNullOrWhiteSpace(actor.Element("basePawnKind")?.Value)) Fail(actor, path, "basePawnKind is required");
            }
            var steps = new HashSet<string>();
            foreach (var step in c.Element("Steps").Elements()) if (!steps.Add(Required(step, "id", path))) Fail(step, path, "duplicate step id");
            foreach (var element in c.Descendants())
            {
                foreach (var attribute in new[] { "actor", "target", "group", "focus" })
                    if (A(element, attribute) != null && !actors.Contains(A(element, attribute))) Fail(element, path, "unknown actor reference " + A(element, attribute));
                if (A(element, "ref") != null && !steps.Contains(A(element, "ref"))) Fail(element, path, "unknown step reference " + A(element, "ref"));
                if (A(element, "since") != null && A(element, "since") != "prepared" && !steps.Contains(A(element, "since"))) Fail(element, path, "unknown baseline reference");
                if (A(element, "mode") != null && new[] { "Apparel", "Equipment", "Traits", "Skills", "Hediffs", "Abilities" }.Contains(element.Name.LocalName) && !new[] { "inherit", "patch", "replace" }.Contains(A(element, "mode"))) Fail(element, path, "invalid merge mode");
                if (A(element, "quantifier") != null && !new[] { "any", "all", "count" }.Contains(A(element, "quantifier"))) Fail(element, path, "invalid quantifier");
                if (A(element, "transition") != null && !new[] { "added", "removed" }.Contains(A(element, "transition"))) Fail(element, path, "invalid transition");
                if (A(element, "observation") != null && !new[] { "current", "eventually", "always" }.Contains(A(element, "observation"))) Fail(element, path, "invalid observation");
                if (element.Name == "Not" && element.Elements().Count() != 1) Fail(element, path, "Not requires one condition");
                if (new[] { "All", "Any", "Success", "Wait", "Require" }.Contains(element.Name.LocalName) && !element.Elements().Any()) Fail(element, path, "condition cannot be empty");
                if (A(element, "force", "false") != "false") Fail(element, path, "external scenes cannot force abilities");
                if (A(element, "spawn") != null && !new[] { "true", "false" }.Contains(A(element, "spawn"))) Fail(element, path, "spawn must be true/false");
                if (A(element, "melee") != null && !new[] { "true", "false" }.Contains(A(element, "melee"))) Fail(element, path, "melee must be true/false");
                if (A(element, "formation", "line") != "line") Fail(element, path, "batch 1 supports line formation");
                if (A(element, "onMissing", "fail") != "fail" && A(element, "onMissing") != "add") Fail(element, path, "onMissing must be fail/add");
                if (A(element, "durationTicks") != null && I(element, "durationTicks") < 1 || A(element, "stableTicks") != null && I(element, "stableTicks") < 1 || A(element, "count") != null && I(element, "count") < 1) Fail(element, path, "tick durations, stability and counts must be positive");
                if (A(element, "timeoutRealSeconds") != null && D(element, "timeoutRealSeconds") <= 0) Fail(element, path, "timeoutRealSeconds must be positive");
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
        }

        // Exact parent/child and attribute whitelist. No Class, private paths, arbitrary calls or ignored fields.
        private static readonly Dictionary<string, string> Children = new Dictionary<string, string> {
            {"TestSuite", "Requirements Templates Case"}, {"Requirements", "Mod DLC"}, {"Templates", "PawnTemplate"},
            {"PawnTemplate", "basePawnKind race gender minGenerationAge maxGenerationAge apparelTags apparelMoney weaponTags weaponMoney forcedTraits skills abilities Apparel Equipment Traits Skills Hediffs Abilities Appearance"},
            {"Case", "testDefName Requirements Scene Teams Actors Steps Success Targets"}, {"Targets", "Def"},
            {"Teams", "Team Relation"}, {"Actors", "Pawn PawnGroup Building Thing Corpse DefRef TerrainArea"},
            {"Steps", "Require Checkpoint Wait Screenshot CastAbility Attack Spawn CreateThing DropEquipment Equip AddHediff RemoveHediff Destroy Kill Pause"},
            {"Apparel", "Add Replace Remove"}, {"Equipment", "SetPrimary"}, {"Traits", "Set Remove"}, {"Skills", "Set"}, {"Hediffs", "Ensure Remove"}, {"Abilities", "Ensure Remove"},
            {"Appearance", "hair bodyType headType hairColor skinColor"}, {"apparelTags", "li"}, {"weaponTags", "li"},
            {"forcedTraits", "li"}, {"skills", "li"}, {"abilities", "li"}, {"li", "def degree skill range passion"}
        };
        private const string Conditions = "All Any Not HasHediff ThingExists ActorState CorpseState ActorCount Equipped Wearing HasTrait SkillLevel HasAbility StepSucceeded";
        private static readonly Dictionary<string, string> Attributes = new Dictionary<string, string> {
            {"TestSuite", "schemaVersion id packageId gameVersion"}, {"Case", "id name seed timeoutRealSeconds"}, {"PawnTemplate", "id extends"},
            {"Scene", "type sizeX sizeZ revealFog"}, {"Team", "id factionDef"}, {"Relation", "a b value"},
            {"Def", "defType defName"}, {"Mod", "packageId"}, {"DLC", "packageId"},
            {"Require", "id"}, {"Checkpoint", "id"}, {"Wait", "id timeoutRealSeconds stableTicks durationTicks observation"},
            {"Screenshot", "id focus"}, {"CastAbility", "id actor def target force timeoutRealSeconds"}, {"Attack", "id actor target melee timeoutRealSeconds"},
            {"Spawn", "id actor"}, {"Equip", "id actor def stuff"}, {"AddHediff", "id actor def severity part durationTicks"}, {"RemoveHediff", "id actor def part"},
            {"CreateThing", "id def stuff x z count"}, {"DropEquipment", "id actor"},
            {"Destroy", "id actor"}, {"Kill", "id actor"}, {"Pause", "id"},
            {"Apparel", "mode"}, {"Equipment", "mode"}, {"Traits", "mode"}, {"Skills", "mode"}, {"Hediffs", "mode"}, {"Abilities", "mode"},
            {"Add", "def stuff quality"}, {"Replace", "matchDef matchLayer withDef stuff quality onMissing"}, {"Remove", "def part"}, {"SetPrimary", "def stuff quality"},
            {"Set", "def degree level passion"}, {"Ensure", "def severity part durationTicks"},
            {"StepSucceeded", "ref"}, {"ActorCount", "group equals"},
            {"HasHediff", "actor def severityMin severityMax quantifier equals transition since observation"},
            {"ThingExists", "actor def quantifier equals transition since observation"}, {"ActorState", "actor state quantifier equals observation"}, {"CorpseState", "actor state"},
            {"Equipped", "actor def"}, {"Wearing", "actor def"}, {"HasTrait", "actor def degree"}, {"SkillLevel", "actor def min max"}, {"HasAbility", "actor def"}
        };

        private static void ValidateTree(XElement node, string path)
        {
            var name = node.Name.LocalName;
            var required = name switch {
                "Thing" or "Building" or "CreateThing" or "Add" or "SetPrimary" or "Ensure" or "Set" or "Remove" => "def",
                "Team" => "id factionDef", "Relation" => "a b value", "Def" or "DefRef" => "defType defName",
                "Mod" or "DLC" => "packageId", "CastAbility" => "actor def target", "Attack" => "actor target",
                "Equip" or "AddHediff" or "RemoveHediff" => "actor def", "Spawn" or "DropEquipment" or "Destroy" or "Kill" => "actor",
                "HasHediff" or "Equipped" or "Wearing" or "HasTrait" or "SkillLevel" or "HasAbility" => "actor def",
                "ActorState" or "CorpseState" => "actor state", "ActorCount" => "group equals", "StepSucceeded" => "ref", "Replace" => "withDef",
                "TerrainArea" => "def x z width height", _ => ""
            };
            foreach (var field in required.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)) Required(node, field, path);
            var actor = new[] { "Pawn", "PawnGroup", "Corpse" }.Contains(name);
            var thing = new[] { "Thing", "Building" }.Contains(name);
            var attrs = name == "PawnGroup" ? "id template team count originX originZ spacing formation spawn" : actor ? "id template team x z spawn" : thing ? "id def stuff team x z count quality spawn" : name == "DefRef" ? "id defType defName" : name == "TerrainArea" ? "id def x z width height" : Attributes.TryGetValue(name, out var text) ? text : "";
            if (name == "Set") attrs = node.Parent?.Name == "Skills" ? "def level passion" : "def degree";
            if (name == "Ensure") attrs = node.Parent?.Name == "Hediffs" ? "def severity part durationTicks" : "def";
            if (name == "Remove") attrs = node.Parent?.Name == "Hediffs" ? "def part" : "def";
            foreach (var a in node.Attributes()) if (!attrs.Split(' ').Contains(a.Name.ToString())) Fail(node, path, "unknown attribute " + a.Name);
            var children = actor ? Children["PawnTemplate"] : name == "li" ? node.Parent?.Name.LocalName switch { "forcedTraits" => "def degree", "skills" => "skill range passion", _ => "" } : Children.TryGetValue(name, out var allowed) ? allowed : new[] { "Require", "Wait", "Success", "All", "Any", "Not" }.Contains(name) ? Conditions : "";
            foreach (var e in node.Elements())
            {
                if (!children.Split(' ').Contains(e.Name.ToString())) Fail(e, path, "unknown/unsupported element (batch 1): " + e.Name);
                var multiple = name == "TestSuite" ? e.Name == "Case" : new[] { "Templates", "Actors", "Steps", "Teams", "Requirements", "Targets", "Apparel", "Equipment", "Traits", "Skills", "Hediffs", "Abilities", "All", "Any", "Require", "Wait", "Success", "forcedTraits", "skills", "abilities", "apparelTags", "weaponTags" }.Contains(name);
                if (node.Elements(e.Name).Count() > 1 && !multiple) Fail(e, path, "duplicate field " + e.Name);
                ValidateTree(e, path);
            }
            if (node.HasElements && node.Nodes().OfType<XText>().Any(t => !string.IsNullOrWhiteSpace(t.Value))) Fail(node, path, "mixed text is not supported");
            foreach (var a in node.Attributes().Where(a => new[] { "x", "z", "count", "sizeX", "sizeZ", "seed", "originX", "originZ", "spacing", "stableTicks", "durationTicks", "degree", "level", "equals", "width", "height" }.Contains(a.Name.LocalName)))
                if (!int.TryParse(a.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)) Fail(node, path, "invalid integer " + a.Name);
            foreach (var a in node.Attributes().Where(a => new[] { "timeoutRealSeconds", "severity", "severityMin", "severityMax", "min", "max" }.Contains(a.Name.LocalName)))
                if (!double.TryParse(a.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) || double.IsNaN(d) || double.IsInfinity(d)) Fail(node, path, "invalid finite number " + a.Name);
        }
    }
}
