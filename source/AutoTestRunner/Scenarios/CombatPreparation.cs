#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace LogSyncMod.AutoTest.Scenarios
{
    public sealed class CombatPreparationRecord
    {
        public string StepId { get; set; }
        public bool Completed { get; set; }
        public Dictionary<string, string> Snapshots { get; set; } = new(StringComparer.Ordinal);
    }
    public static class CombatPreparation
    {
        public static bool Targets(XElement step, string id, int x = 0, int z = 0, XElement battle = null)
        {
            if (step.Name == "DebugAction" && ScenarioLoader.A(step.Element("Select"), "mode") == "rectangle")
            {
                var s = step.Element("Select");
                if (battle != null && ScenarioLoader.A(s, "filter") != "allScenePawns")
                {
                    string Team(string actor) => ScenarioLoader.A(battle.Element("Actors")?.Elements().SingleOrDefault(a => ScenarioLoader.A(a, "id") == actor?.Split('[')[0]), "team");
                    var team = Team(id); var relative = Team(ScenarioLoader.A(s, "relativeTo"));
                    if (team == null || relative == null || (ScenarioLoader.A(s, "filter") == "allies" ? team != relative : team == relative)) return false;
                }
                return x >= ScenarioLoader.I(s, "minX") && x <= ScenarioLoader.I(s, "maxX") && z >= ScenarioLoader.I(s, "minZ") && z <= ScenarioLoader.I(s, "maxZ");
            }
            return step.DescendantsAndSelf().Attributes().Where(a => a.Name == "actor" || a.Name == "target" || a.Name == "group")
                .Any(a => id == a.Value || id.StartsWith(a.Value + "[", StringComparison.Ordinal));
        }
        private static string Field(string condition) => condition switch {
            "Equipped" => "Equipment", "Wearing" => "Apparel", "HasTrait" => "Traits", "SkillLevel" => "Skills", "HasHediff" => "Hediffs",
            "HasAbility" => "Abilities", "HasGene" => "Genes", "HasXenotype" => "Xenotype", "ResourceLevel" or "ModResourceLevel" => "Resources", "PawnAppearance" => "Appearance", _ => null };
        public static string Validate(GroupCombatRoundResult round, XElement battle)
        {
            var initial = round.Units.Where(u => !u.JoinedDuringBattle).ToList();
            if (initial.Any(u => string.IsNullOrEmpty(u.GeneratedSnapshot) || string.IsNullOrEmpty(u.InitialSnapshot))) return "缺少生成或开战角色快照";
            var current = initial.ToDictionary(u => u.Id, u => PawnConfiguration.Read(u.GeneratedSnapshot), StringComparer.Ordinal);
            var steps = battle.Element("Steps")?.Elements().ToList() ?? new List<XElement>();
            if (round.Preparation == null || round.Preparation.Count != steps.Count) return "准备步骤证据缺失或多余";
            var completed = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < steps.Count; i++)
            {
                var step = steps[i]; var evidence = round.Preparation[i];
                if (evidence == null || !evidence.Completed || evidence.StepId != ScenarioLoader.A(step, "id") || evidence.Snapshots == null) return "准备步骤身份或完成状态不符";
                var targets = initial.Where(u => Targets(step, u.Id, u.X, u.Z, battle)).Select(u => u.Id).OrderBy(id => id, StringComparer.Ordinal).ToList();
                if (!targets.SequenceEqual(evidence.Snapshots.Keys.OrderBy(id => id, StringComparer.Ordinal))) return "准备步骤目标范围不符";
                foreach (var id in targets)
                {
                    var after = PawnConfiguration.Read(evidence.Snapshots[id]);
                    var error = ValidateChange(step, current[id], after, battle.Element("ActionBindings"));
                    if (error != null) return evidence.StepId + ": " + error;
                    current[id] = after;
                }
                if (step.Name == "Require" && PawnConfiguration.Condition(step, current, completed) == false) return "准备断言与角色快照不符：" + evidence.StepId;
                if (step.Element("Expect") != null && PawnConfiguration.Condition(step.Element("Expect"), current, completed) == false) return "准备动作后置断言不符：" + evidence.StepId;
                completed.Add(evidence.StepId);
            }
            foreach (var unit in initial)
                if (!PawnConfiguration.Equal(current[unit.Id], PawnConfiguration.Read(unit.InitialSnapshot))) return "准备完成后的快照与实际初始角色不符：" + unit.Id;
            return null;
        }
        public static string ValidateChange(XElement step, XElement before, XElement after, XElement bindings = null)
        {
            var allowed = new HashSet<string>(StringComparer.Ordinal);
            var name = step.Name.LocalName;
            if (name == "Equip" || name == "DropEquipment") allowed.Add("Equipment");
            if (name == "AddHediff" || name == "RemoveHediff" || name == "ApplyDamage" || name == "Kill") allowed.Add("Hediffs");
            if (name == "DebugAction")
            {
                var action = ScenarioLoader.A(step, "actionId");
                if (action == "native.addHediff" || action == "native.removeHediff" || action == "native.healInjuries") allowed.Add("Hediffs");
                if (action == "native.fillHemogen" || action == "native.setPsyfocus" || action == "axolotl.fillQi") allowed.Add("Resources");
                var binding = bindings?.Elements().SingleOrDefault(b => ScenarioLoader.A(b, "id") == action);
                if (binding?.Elements("ExpectHediff").Any() == true) allowed.Add("Hediffs");
                foreach (var condition in step.Element("Expect")?.Descendants() ?? Enumerable.Empty<XElement>())
                    if (Field(condition.Name.LocalName) is string field) allowed.Add(field);
            }
            foreach (var field in PawnConfiguration.FrozenFields.Where(f => !allowed.Contains(f)))
                if (!PawnConfiguration.Equal(before.Element(field), after.Element(field))) return "准备动作改变未声明属性：" + field;
            var def = ScenarioLoader.A(step, "def");
            var healthAction = name == "AddHediff" || name == "RemoveHediff" || name == "DebugAction" && new[] { "native.addHediff", "native.removeHediff" }.Contains(ScenarioLoader.A(step, "actionId"));
            if (healthAction)
            {
                bool Selected(XElement item) => ScenarioLoader.A(item, "def") == def && (ScenarioLoader.A(step, "part") == null || PawnConfiguration.PartMatches(ScenarioLoader.A(item, "part"), ScenarioLoader.A(step, "part")));
                var original = new XElement("Hediffs", before.Element("Hediffs")?.Elements().Where(h => !Selected(h)) ?? Enumerable.Empty<XElement>());
                var resulting = new XElement("Hediffs", after.Element("Hediffs")?.Elements().Where(h => !Selected(h)) ?? Enumerable.Empty<XElement>());
                if (!PawnConfiguration.Equal(original, resulting)) return "健康准备动作改变了未指定的健康状态";
            }
            if (name == "Equip" && after.Element("Equipment")?.Elements().Any(e => ScenarioLoader.A(e, "def") == def && (step.Attribute("stuff") == null || ScenarioLoader.A(e, "stuff") == ScenarioLoader.A(step, "stuff"))) != true) return "准备装备未生效";
            if (name == "DropEquipment" && after.Element("Equipment")?.HasElements == true) return "准备卸下装备未生效";
            if (name == "DebugAction" && ScenarioLoader.A(step, "actionId") == "native.fillHemogen")
            {
                var resource = after.Element("Resources")?.Elements("GeneResource").SingleOrDefault(e => ScenarioLoader.A(e, "def") == "Hemogenic");
                if (resource == null || !PawnConfiguration.Near(ScenarioLoader.A(resource, "value"), ScenarioLoader.A(step, "value", "1"))) return "准备血原质数值不符";
            }
            if (name == "DebugAction" && ScenarioLoader.A(step, "actionId") == "native.setPsyfocus"
                && !PawnConfiguration.Near(ScenarioLoader.A(after.Element("Resources")?.Element("Psyfocus"), "value"), ScenarioLoader.A(step, "value", "1"))) return "准备灵能专注值不符";
            if (name == "AddHediff" || name == "DebugAction" && ScenarioLoader.A(step, "actionId") == "native.addHediff")
                if (after.Element("Hediffs")?.Elements().Any(e => ScenarioLoader.A(e, "def") == def && (step.Attribute("severity") == null || PawnConfiguration.Near(ScenarioLoader.A(step, "severity"), ScenarioLoader.A(e, "severity")))) != true) return "准备健康状态未生效";
            if (name == "RemoveHediff" || name == "DebugAction" && ScenarioLoader.A(step, "actionId") == "native.removeHediff")
                if (after.Element("Hediffs")?.Elements().Any(e => ScenarioLoader.A(e, "def") == def && (step.Attribute("part") == null || ScenarioLoader.A(e, "part") == ScenarioLoader.A(step, "part"))) == true) return "准备移除健康状态未生效";
            return null;
        }
    }
}
