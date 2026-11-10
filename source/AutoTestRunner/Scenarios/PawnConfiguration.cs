#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace LogSyncMod.AutoTest.Scenarios
{
    // Shared by the game and the independent CLI validator. Only public, captured fields
    // are compared; ability cooldowns and private mod component state are not cloned.
    public static class PawnConfiguration
    {
        public static readonly string[] FrozenFields = { "basePawnKind", "race", "gender", "Age", "Appearance", "Traits", "Skills", "Apparel", "Equipment", "Xenotype", "Genes", "Hediffs", "Abilities", "Resources" };
        public static XElement Read(string xml) => XElement.Parse(TemporaryPawnData.Parse(xml).TemplateXml);
        public static bool PartMatches(string actual, string requested)
            => actual == requested || requested != null && !requested.Contains('/') && actual != null && actual.Split('/').Last().Split('[')[0] == requested;
        private static string A(XElement e, string n, string fallback = null) => ScenarioLoader.A(e, n, fallback);
        private static double N(XElement e, string n, double fallback = 0) => ScenarioLoader.D(e, n, fallback);
        private static bool SameValue(string left, string right)
        {
            string Color(string value) => value?.StartsWith("#", StringComparison.Ordinal) == true && value.Length == 7 ? value + "FF" : value;
            return left == right || left?.StartsWith("#", StringComparison.Ordinal) == true && string.Equals(Color(left), Color(right), StringComparison.OrdinalIgnoreCase) || Near(left, right);
        }
        public static bool Near(string left, string right, double tolerance = .0001)
            => double.TryParse(left, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) && double.IsFinite(x)
                && double.TryParse(right, NumberStyles.Float, CultureInfo.InvariantCulture, out var y) && double.IsFinite(y)
                && Math.Abs(x - y) <= tolerance;
        public static bool Equal(XElement left, XElement right)
        {
            if (left == null || right == null) return left == right;
            if (left.Name != right.Name) return false;
            var la = left.Attributes().Where(a => a.Name != "id").OrderBy(a => a.Name.ToString()).ToList();
            var ra = right.Attributes().Where(a => a.Name != "id").OrderBy(a => a.Name.ToString()).ToList();
            if (la.Count != ra.Count || la.Where((a, i) => a.Name != ra[i].Name || !SameValue(a.Value, ra[i].Value)).Any()) return false;
            if (!left.HasElements && !right.HasElements) return SameValue(left.Value, right.Value);
            var remaining = right.Elements().ToList();
            foreach (var item in left.Elements())
            {
                var match = remaining.FindIndex(other => Equal(item, other));
                if (match < 0) return false;
                remaining.RemoveAt(match);
            }
            return remaining.Count == 0;
        }
        public static string ValidateFrozen(XElement expected, XElement actual)
        {
            foreach (var field in FrozenFields)
                if (expected.Element(field) != null && !Equal(expected.Element(field), actual.Element(field))) return "冻结角色属性不符：" + field;
            return null;
        }
        public static string ValidateExplicit(XElement config, XElement actual)
        {
            foreach (var field in new[] { "basePawnKind", "race", "gender" })
                if (config.Element(field) != null && config.Element(field).Value != actual.Element(field)?.Value) return "XML角色字段不符：" + field;
            if (config.Element("Xenotype") != null && A(config.Element("Xenotype"), "def") != A(actual.Element("Xenotype"), "def")) return "XML异种不符";
            foreach (var attr in config.Element("Age")?.Attributes() ?? Enumerable.Empty<XAttribute>())
                if (!Near(attr.Value, A(actual.Element("Age"), attr.Name.LocalName), 1d / 3600000)) return "XML年龄不符：" + attr.Name;
            var age = N(actual.Element("Age"), "biologicalYears");
            if (config.Element("Age") == null && (config.Element("minGenerationAge") != null && age < double.Parse(config.Element("minGenerationAge").Value, CultureInfo.InvariantCulture)
                || config.Element("maxGenerationAge") != null && age > double.Parse(config.Element("maxGenerationAge").Value, CultureInfo.InvariantCulture))) return "生成年龄超出XML范围";
            foreach (var value in config.Element("Appearance")?.Elements() ?? Enumerable.Empty<XElement>())
                if (!Equal(value, actual.Element("Appearance")?.Element(value.Name))) return "XML外观不符：" + value.Name;
            foreach (var field in new[] { "Apparel", "Equipment", "Traits", "Skills", "Genes", "Hediffs", "Abilities" })
            {
                var patch = config.Element(field); if (patch == null || A(patch, "mode", "patch") == "inherit") continue;
                if (actual.Element(field) == null) return "XML角色集合缺失：" + field;
                var items = actual.Element(field)?.Elements().ToList() ?? new List<XElement>();
                var required = patch.Elements().Where(e => e.Name != "Remove").ToList();
                if (A(patch, "mode") == "replace" && field != "Skills" && items.Count != required.Count) return "XML替换集合人数不符：" + field;
                if (A(patch, "mode") == "replace" && field == "Skills" && items.Any(s => !required.Any(r => A(r, "def") == A(s, "def")) && (N(s, "level") != 0 || A(s, "passion", "None") != "None"))) return "XML技能替换保留了未指定技能";
                foreach (var item in patch.Elements())
                {
                    var def = A(item, item.Name == "Replace" ? "withDef" : "def");
                    var matches = items.Where(e => A(e, "def") == def && (field != "Hediffs" || item.Name == "Remove" && A(item, "part") == null || PartMatches(A(e, "part"), A(item, "part")))).ToList();
                    if (item.Name == "Remove") { if (matches.Count != 0) return "XML移除未生效：" + field + "/" + def; continue; }
                    if (matches.Count == 0) return "XML成员缺失：" + field + "/" + def;
                    if (!matches.Any(value => item.Attributes().Where(a => !new[] { "def", "part", "matchDef", "matchLayer", "withDef", "onMissing" }.Contains(a.Name.LocalName))
                        .All(a => SameValue(a.Value, A(value, a.Name.LocalName, a.Name == "degree" ? "0" : null))))) return "XML成员属性不符：" + field + "/" + def;
                }
            }
            foreach (var item in config.Element("forcedTraits")?.Elements("li") ?? Enumerable.Empty<XElement>())
                if (actual.Element("Traits")?.Elements().Any(t => A(t, "def") == item.Element("def")?.Value && A(t, "degree", "0") == (item.Element("degree")?.Value ?? "0")) != true) return "原生特性要求未生效";
            foreach (var item in config.Element("skills")?.Elements("li") ?? Enumerable.Empty<XElement>())
            {
                var skill = actual.Element("Skills")?.Elements().SingleOrDefault(s => A(s, "def") == item.Element("skill")?.Value);
                var range = (item.Element("range")?.Value ?? "0").Split('~');
                if (skill == null || N(skill, "level") < double.Parse(range[0], CultureInfo.InvariantCulture) || N(skill, "level") > double.Parse(range.Last(), CultureInfo.InvariantCulture)
                    || item.Element("passion") != null && A(skill, "passion") != item.Element("passion").Value) return "原生技能要求未生效";
            }
            foreach (var item in config.Element("abilities")?.Elements("li") ?? Enumerable.Empty<XElement>())
                if (actual.Element("Abilities")?.Elements().Any(a => A(a, "def") == item.Value.Trim()) != true) return "原生能力要求未生效";
            foreach (var item in config.Element("Resources")?.Elements() ?? Enumerable.Empty<XElement>())
            {
                var value = actual.Element("Resources")?.Elements(item.Name).SingleOrDefault(v => A(v, "def") == A(item, "def"));
                if (value == null || !Near(A(item, "value"), A(value, "value"))) return "XML资源不符：" + item.Name;
            }
            return null;
        }

        public static bool? Condition(XElement condition, IReadOnlyDictionary<string, XElement> pawns, ISet<string> completed = null)
        {
            var name = condition.Name.LocalName;
            if (name == "StepSucceeded") return completed?.Contains(A(condition, "ref")) == true;
            if (name == "HasGene" && condition.Attribute("active") != null || name == "ResourceLevel") return null; // Active state and resource capacity require native tracker observations.
            if (new[] { "Require", "Expect", "All", "Any", "Not" }.Contains(name))
            {
                var values = condition.Elements().Select(c => Condition(c, pawns, completed)).ToList();
                if (name == "Any") return values.Any(v => v == true) ? true : values.Any(v => v == null) ? null : false;
                if (name == "Not") return values.Single().HasValue ? !values.Single().Value : null;
                return values.Any(v => v == false) ? false : values.Any(v => v == null) ? null : true;
            }
            if (!new[] { "ActorCount", "Equipped", "Wearing", "HasTrait", "SkillLevel", "HasAbility", "HasGene", "HasXenotype", "HasHediff", "PawnAppearance" }.Contains(name)) return null;
            var actor = A(condition, "actor", A(condition, "group"));
            var targets = pawns.Where(p => p.Key == actor || p.Key.StartsWith(actor + "[", StringComparison.Ordinal)).Select(p => p.Value).ToList();
            if (targets.Count == 0) return null; // Extra scene actors are evaluated by native preparation, not a participant snapshot.
            if (name == "ActorCount") return targets.Count == ScenarioLoader.I(condition, "equals");
            bool Test(XElement pawn)
            {
                var def = A(condition, "def");
                IEnumerable<XElement> Items(string field) => pawn.Element(field)?.Elements().Where(e => def == null || A(e, "def") == def) ?? Enumerable.Empty<XElement>();
                return name switch {
                    "Equipped" => Items("Equipment").Any(), "Wearing" => Items("Apparel").Any(),
                    "HasTrait" => Items("Traits").Any(e => A(e, "degree", "0") == A(condition, "degree", "0")),
                    "SkillLevel" => Items("Skills").Any(e => N(e, "level") >= N(condition, "min") && N(e, "level") <= N(condition, "max", 20)),
                    "HasAbility" => Items("Abilities").Any(),
                    "HasGene" => Items("Genes").Any(e => A(condition, "xenogene") == null || A(e, "xenogene", "false") == A(condition, "xenogene")),
                    "HasXenotype" => A(pawn.Element("Xenotype"), "def") == def,
                    "HasHediff" => Items("Hediffs").Any(e => N(e, "severity") >= N(condition, "severityMin") && N(e, "severity") <= N(condition, "severityMax", double.MaxValue)),
                    "ResourceLevel" => Items("Resources").Any(e => N(e, "value") >= N(condition, "min") && N(e, "value") <= N(condition, "max", 1)),
                    "PawnAppearance" => pawn.Element("Appearance")?.Element(A(condition, "property"))?.Value == A(condition, "value"),
                    _ => false
                };
            }
            var count = targets.Count(Test);
            return A(condition, "quantifier", "any") switch { "any" => count > 0, "count" => count == ScenarioLoader.I(condition, "equals"), _ => count == targets.Count };
        }
    }
}
