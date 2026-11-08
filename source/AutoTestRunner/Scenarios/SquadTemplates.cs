#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace LogSyncMod.AutoTest.Scenarios
{
    public sealed class SquadOption
    {
        public string PawnKind { get; set; }
        public decimal Weight { get; set; }
        public int Count { get; set; }
    }
    public sealed class SquadXenotype
    {
        public string Def { get; set; }
        public decimal Weight { get; set; }
    }
    public sealed class SquadTemplate
    {
        public string Id { get; set; }
        public string Source { get; set; }
        public string FactionDef { get; set; }
        public string MakerMode { get; set; } = "random";
        public string GroupKind { get; set; } = "Combat";
        public int? MakerIndex { get; set; }
        public string Fingerprint { get; set; }
        public float ReferencePoints { get; set; } = 5000;
        public string StrategyDef { get; set; } = "ImmediateAttack";
        public string Composition { get; set; } = "weighted";
        public List<SquadOption> Options { get; set; } = new();
        public string XenotypeMode { get; set; } = "inherit";
        public List<SquadXenotype> Xenotypes { get; set; } = new();
        public string GeneratorId { get; set; }
        public Dictionary<string, string> Parameters { get; set; } = new(StringComparer.Ordinal);
        public string Xml { get; set; }
    }
    public sealed class ResolvedSquadMember
    {
        public string PawnKind { get; set; }
        public string Xenotype { get; set; }
        public decimal BaseCombatPower { get; set; }
        public decimal XenotypeFactor { get; set; } = 1;
        public decimal Cost { get; set; }
        public int Seed { get; set; }
        // Captured native outfit/traits/skills; generated runtime effects remain native.
        public string PawnTemplateXml { get; set; }
        public string Childhood { get; set; }
        public string Adulthood { get; set; }
    }
    public sealed class SquadMakerInfo
    {
        public int Index { get; set; }
        public string KindDef { get; set; }
        public float Commonality { get; set; }
        public float MaxTotalPoints { get; set; }
        public bool Eligible { get; set; }
        public string Fingerprint { get; set; }
        public List<SquadOption> Options { get; set; } = new();
        public List<string> DisallowedStrategies { get; set; } = new();
    }
    public static class SquadTemplates
    {
        public static Dictionary<string, SquadTemplate> ParseAll(XElement root, string path)
        {
            var result = new Dictionary<string, SquadTemplate>(StringComparer.Ordinal);
            if (root.Elements("SquadTemplates").Count() > 1) ScenarioLoader.Fail(root, path, "duplicate SquadTemplates");
            var section = root.Element("SquadTemplates");
            Check(section, "", "SquadTemplate", path, repeat: true);
            foreach (var node in section?.Elements() ?? Enumerable.Empty<XElement>())
            {
                var value = Parse(node, path);
                if (!result.TryAdd(value.Id, value)) ScenarioLoader.Fail(node, path, "duplicate squad template: " + value.Id);
            }
            return result;
        }
        public static SquadTemplate Parse(XElement node, string path)
        {
            Check(node, "id source factionDef", "GroupMaker RaidRules Composition Xenotypes Generator", path);
            var result = new SquadTemplate { Id = Need(node, "id", path), Source = Need(node, "source", path), FactionDef = Need(node, "factionDef", path), Xml = node.ToString(SaveOptions.DisableFormatting) };
            if (!new[] { "faction", "custom", "generator" }.Contains(result.Source)) ScenarioLoader.Fail(node, path, "squad source must be faction/custom/generator");
            var maker = node.Element("GroupMaker");
            Check(maker, "mode kindDef index fingerprint referencePoints", "", path);
            if (result.Source == "faction" ? maker == null || node.Element("Composition") != null || node.Element("Generator") != null : maker != null)
                ScenarioLoader.Fail(node, path, "faction squads require GroupMaker; other sources cannot use GroupMaker");
            if (maker != null)
            {
                result.MakerMode = Need(maker, "mode", path); result.GroupKind = ScenarioLoader.A(maker, "kindDef", "Combat");
                if (result.GroupKind != "Combat") ScenarioLoader.Fail(maker, path, "combat squads currently require kindDef=Combat");
                if (!new[] { "random", "specific" }.Contains(result.MakerMode)) ScenarioLoader.Fail(maker, path, "GroupMaker mode must be random/specific");
                if (result.MakerMode == "specific") { result.MakerIndex = ScenarioLoader.I(maker, "index", -1); if (result.MakerIndex < 0) ScenarioLoader.Fail(maker, path, "specific GroupMaker requires a nonnegative index"); }
                else if (maker.Attribute("index") != null || maker.Attribute("fingerprint") != null) ScenarioLoader.Fail(maker, path, "random GroupMaker cannot select an index/fingerprint");
                result.Fingerprint = ScenarioLoader.A(maker, "fingerprint");
                if (result.Fingerprint != null && !System.Text.RegularExpressions.Regex.IsMatch(result.Fingerprint, "^[a-f0-9]{64}$")) ScenarioLoader.Fail(maker, path, "fingerprint must be a lowercase SHA256");
                var points = ScenarioLoader.D(maker, "referencePoints", 5000);
                if (!double.IsFinite(points) || points <= 0 || points > 10000000) ScenarioLoader.Fail(maker, path, "referencePoints must be in (0,10000000]");
                result.ReferencePoints = (float)points;
            }
            var rules = node.Element("RaidRules"); Check(rules, "strategyDef", "", path);
            if (rules != null) result.StrategyDef = Need(rules, "strategyDef", path);
            var composition = node.Element("Composition"); Check(composition, "mode allocator", "Option Member", path, repeat: true);
            if ((result.Source == "custom") != (composition != null)) ScenarioLoader.Fail(node, path, "only custom squads require Composition");
            if (composition != null)
            {
                result.Composition = Need(composition, "mode", path);
                if (!new[] { "weighted", "fixed" }.Contains(result.Composition) || ScenarioLoader.A(composition, "allocator", "native") != "native") ScenarioLoader.Fail(composition, path, "Composition supports weighted/fixed and allocator=native");
                foreach (var option in composition.Elements())
                {
                    var fixedMode = result.Composition == "fixed";
                    Check(option, fixedMode ? "pawnKind count" : "pawnKind weight", "", path);
                    if (option.Name != (fixedMode ? "Member" : "Option")) ScenarioLoader.Fail(option, path, "Composition child does not match mode");
                    var count = fixedMode ? ScenarioLoader.I(option, "count", 0) : 0;
                    if (fixedMode && (count < 1 || count > CombatRoster.MaxTeamMembers)) ScenarioLoader.Fail(option, path, "Member count must be 1..200");
                    result.Options.Add(new SquadOption { PawnKind = Need(option, "pawnKind", path), Weight = fixedMode ? 1 : CombatRoster.Positive(Need(option, "weight", path)), Count = count });
                }
                if (result.Options.Count == 0 || result.Options.Count > CombatRoster.MaxTeamMembers || result.Options.Select(o => o.PawnKind).Distinct().Count() != result.Options.Count || result.Options.Sum(o => o.Count) > CombatRoster.MaxTeamMembers) ScenarioLoader.Fail(composition, path, "Composition requires 1..200 unique PawnKinds and at most 200 fixed members");
            }
            var xenotypes = node.Element("Xenotypes"); Check(xenotypes, "mode def", "Option", path, repeat: true);
            if (xenotypes != null)
            {
                result.XenotypeMode = Need(xenotypes, "mode", path);
                if (!new[] { "inherit", "fixed", "weighted" }.Contains(result.XenotypeMode)) ScenarioLoader.Fail(xenotypes, path, "Xenotypes mode must be inherit/fixed/weighted");
                if (result.XenotypeMode == "fixed") result.Xenotypes.Add(new SquadXenotype { Def = Need(xenotypes, "def", path), Weight = 1 });
                else if (xenotypes.Attribute("def") != null) ScenarioLoader.Fail(xenotypes, path, "def requires fixed Xenotypes");
                foreach (var option in xenotypes.Elements())
                {
                    Check(option, "def weight", "", path);
                    if (result.XenotypeMode != "weighted") ScenarioLoader.Fail(option, path, "only weighted Xenotypes accepts options");
                    result.Xenotypes.Add(new SquadXenotype { Def = Need(option, "def", path), Weight = CombatRoster.Positive(Need(option, "weight", path)) });
                }
                if (result.XenotypeMode == "weighted" && result.Xenotypes.Count == 0 || result.Xenotypes.Select(x => x.Def).Distinct().Count() != result.Xenotypes.Count) ScenarioLoader.Fail(xenotypes, path, "weighted Xenotypes requires unique options");
            }
            var generator = node.Element("Generator"); Check(generator, "id", "Param", path, repeat: true);
            if ((result.Source == "generator") != (generator != null)) ScenarioLoader.Fail(node, path, "only generator squads require Generator");
            if (generator != null)
            {
                result.GeneratorId = Need(generator, "id", path);
                foreach (var p in generator.Elements()) { Check(p, "name value", "", path); if (!result.Parameters.TryAdd(Need(p, "name", path), Need(p, "value", path))) ScenarioLoader.Fail(p, path, "duplicate generator parameter"); }
            }
            return result;
        }
        public static void FreezeGroupReferences(XElement root, string path, bool groupCombat)
        {
            var templates = ParseAll(root, path);
            if (!groupCombat && (root.Element("SquadTemplates") != null || root.Descendants("PawnGroup").Any(g => g.Attribute("squad") != null))) ScenarioLoader.Fail(root, path, "SquadTemplates require groupcombat");
            foreach (var group in root.Descendants("PawnGroup").Where(g => g.Attribute("squad") == null && g.Element("SquadTemplate") != null))
                ScenarioLoader.Fail(group, path, "inline frozen SquadTemplate requires a squad reference");
            foreach (var group in root.Descendants("PawnGroup").Where(g => g.Attribute("squad") != null))
            {
                var id = Need(group, "squad", path);
                if (group.Element("SquadTemplate") == null)
                {
                    if (!templates.TryGetValue(id, out var template)) ScenarioLoader.Fail(group, path, "unknown squad: " + id);
                    group.Add(XElement.Parse(template.Xml));
                }
                if (Parse(group.Element("SquadTemplate"), path).Id != id) ScenarioLoader.Fail(group, path, "squad reference does not match frozen template");
                if (group.Elements().Any(e => e.Name != "SquadTemplate") || group.Attribute("template") != null) ScenarioLoader.Fail(group, path, "squad PawnGroup cannot contain single-Pawn overrides");
                if ((group.Attribute("count") != null) == (group.Attribute("combatPowerBudget") != null)) ScenarioLoader.Fail(group, path, "squad PawnGroup requires exactly one of count/combatPowerBudget");
                ValidateCostMode(ScenarioLoader.A(group, "costMode", "pawnKind"));
            }
            root.Element("SquadTemplates")?.Remove();
        }
        public static void ValidateCostMode(string value)
        { if (value != "native" && value != "pawnKind") throw new ScenarioConfigurationException("costMode must be native/pawnKind"); }
        public static string ValidateRoster(ResolvedCombatRoster roster, SquadTemplate template, decimal? budget, int count, string costMode)
        {
            if (roster == null || roster.SquadId != template.Id || roster.FactionDef != template.FactionDef || roster.CostMode != costMode
                || roster.Mode != (budget.HasValue ? "combatPower" : "count") || roster.RequestedBudget != budget
                || roster.Members.Count < 1 || roster.Members.Count > CombatRoster.MaxTeamMembers || roster.ResolvedCount != roster.Members.Count
                || roster.PawnKind != roster.Members[0].PawnKind || roster.UnitCombatPower != roster.ActualCombatPower / roster.ResolvedCount)
                return "混编队伍身份、人数或预算快照不符";
            if (budget.HasValue ? roster.ActualCombatPower > budget : roster.ResolvedCount != count) return "混编队伍超出CP预算或未满足指定人数";
            foreach (var member in roster.Members)
            {
                if (string.IsNullOrWhiteSpace(member.PawnKind) || member.BaseCombatPower <= 0 || member.XenotypeFactor <= 0
                    || member.Cost != member.BaseCombatPower * (costMode == "native" ? member.XenotypeFactor : 1)
                    || string.IsNullOrEmpty(member.PawnTemplateXml)) return "混编成员成本或初始配置无效";
                var xml = XElement.Parse(member.PawnTemplateXml);
                if (xml.Name != "PawnTemplate" || xml.Element("basePawnKind")?.Value != member.PawnKind || (string)xml.Element("Xenotype")?.Attribute("def") != member.Xenotype) return "冻结成员兵种/异种与生成配置不符";
                if (template.Source == "custom" && !template.Options.Any(o => o.PawnKind == member.PawnKind)) return "混编成员超出自定义兵种池";
                if (template.XenotypeMode != "inherit" && member.Xenotype != null && !template.Xenotypes.Any(x => x.Def == member.Xenotype)) return "混编成员超出指定异种池";
            }
            if (template.Source == "custom" && template.Composition == "fixed" && template.Options.Any(o => roster.Members.Count(m => m.PawnKind == o.PawnKind) != o.Count)) return "固定编队人数与XML不符";
            if (template.Source == "faction")
            {
                var maker = roster.GroupMakers.SingleOrDefault(m => m.Index == roster.GroupMakerIndex);
                if (maker == null || !maker.Eligible || maker.KindDef != template.GroupKind || maker.Fingerprint != roster.GroupMakerFingerprint
                    || template.MakerMode == "specific" && maker.Index != template.MakerIndex
                    || template.Fingerprint != null && maker.Fingerprint != template.Fingerprint
                    || roster.Members.Any(m => !maker.Options.Any(o => o.PawnKind == m.PawnKind))) return "混编成员或模板选择与派系编队快照不符";
            }
            return null;
        }
        public static string ValidateUnits(ResolvedCombatRoster roster, IEnumerable<GroupCombatUnit> units, string prefix)
        {
            var initial = units.Where(u => !u.JoinedDuringBattle && u.Id.StartsWith(prefix + "[", StringComparison.Ordinal)).ToList();
            if (initial.Count != roster.Members.Count) return "实际参战人数与混编名单不符";
            for (var i = 0; i < roster.Members.Count; i++)
            {
                var member = roster.Members[i]; var unit = initial.SingleOrDefault(u => u.Id == prefix + "[" + i + "]");
                if (unit == null || unit.Kind != member.PawnKind || unit.InitialSnapshot == null) return "实际参战兵种与混编名单不符";
                var actual = XElement.Parse(TemporaryPawnData.Parse(unit.InitialSnapshot).TemplateXml);
                var expected = XElement.Parse(member.PawnTemplateXml);
                if ((string)actual.Element("Xenotype")?.Attribute("def") != member.Xenotype) return "实际异种与冻结名单不符";
                foreach (var field in new[] { "Apparel", "Equipment" })
                    if (!XNode.DeepEquals(actual.Element(field), expected.Element(field))) return "实际配装与冻结名单不符";
            }
            return null;
        }
        public static int MemberSeed(int seed, string id, int index)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(seed.ToString(CultureInfo.InvariantCulture) + ":" + id + ":" + index));
            return System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(bytes);
        }
        private static string Need(XElement e, string a, string path)
        { var value = ScenarioLoader.A(e, a); if (string.IsNullOrWhiteSpace(value)) ScenarioLoader.Fail(e, path, a + " is required"); return value; }
        private static void Check(XElement e, string attrs, string children, string path, bool repeat = false)
        {
            if (e == null) return;
            if (e.Name.Namespace != XNamespace.None || e.Attributes().Any(a => !attrs.Split(' ').Contains(a.Name.ToString())) || e.Elements().Any(c => !children.Split(' ').Contains(c.Name.ToString())) || e.Nodes().OfType<XText>().Any(t => !string.IsNullOrWhiteSpace(t.Value)) || !repeat && e.Elements().GroupBy(c => c.Name).Any(g => g.Count() > 1)) ScenarioLoader.Fail(e, path, "unknown or duplicate squad field");
        }
    }
}
