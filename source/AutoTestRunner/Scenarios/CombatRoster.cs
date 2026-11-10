#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace LogSyncMod.AutoTest.Scenarios
{
    public sealed class ResolvedCombatRoster
    {
        public string Id { get; set; }
        public string Team { get; set; }
        public string PawnKind { get; set; }
        public string DisplayName { get; set; }
        public string Mode { get; set; }
        public decimal? RequestedBudget { get; set; }
        public string CombatPowerSource => CostMode == "native" ? "loaded PawnKindDef.combatPower * XenotypeDef.combatPowerFactor" : "loaded PawnKindDef.combatPower";
        public decimal UnitCombatPower { get; set; }
        public int ResolvedCount { get; set; }
        public string SquadId { get; set; }
        public string FactionDef { get; set; }
        public string CostMode { get; set; } = "pawnKind";
        public int? GroupMakerIndex { get; set; }
        public string GroupMakerFingerprint { get; set; }
        public List<SquadMakerInfo> GroupMakers { get; set; } = new();
        public List<ResolvedSquadMember> Members { get; set; } = new();
        public decimal ActualCombatPower => Members.Count > 0 ? Members.Sum(m => m.Cost) : UnitCombatPower * ResolvedCount;
        public decimal? UnusedBudget => RequestedBudget - ActualCombatPower;
        public string Source { get; set; }
        public string TemplateXml { get; set; }
        public string GenerationPolicy { get; set; }
        public decimal? NextMemberCost { get; set; }
    }

    public static class CombatRoster
    {
        public const int MaxTeamMembers = 200;
        public const int MaxBattleMembers = MaxTeamMembers * 3;
        // Round-trip the loaded Single rather than converting its binary approximation.
        public static decimal Power(float value)
        {
            if (!float.IsFinite(value) || value <= 0) throw new ScenarioConfigurationException("PawnKind combatPower must be finite and positive: " + value, "COMBAT_POWER_INVALID");
            if (!decimal.TryParse(value.ToString("R", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out var power) || power <= 0)
                throw new ScenarioConfigurationException("combatPower cannot be represented as a positive decimal", "COMBAT_POWER_INVALID");
            return power;
        }
        public static decimal Positive(string value)
        {
            if (!decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) || result <= 0)
                throw new ScenarioConfigurationException("combatPowerBudget/budget must be a finite positive decimal: " + value);
            return result;
        }
        public static ResolvedCombatRoster Resolve(string id, string kind, decimal power, decimal? budget, int count = 1)
        {
            if (power <= 0) throw new ScenarioConfigurationException(kind + ": combatPower must be positive", "COMBAT_POWER_INVALID");
            if (budget.HasValue)
            {
                if (budget <= 0) throw new ScenarioConfigurationException("Budget must be positive");
                decimal quotient;
                try { quotient = decimal.Floor(budget.Value / power); }
                catch (OverflowException) { throw new ScenarioConfigurationException(kind + ": budget/count arithmetic overflow", "COMBAT_ROSTER_LIMIT_EXCEEDED"); }
                if (quotient < 1 || quotient > MaxTeamMembers) throw new ScenarioConfigurationException(kind + ": budget resolves to " + quotient + " units; allowed 1.." + MaxTeamMembers + " (budget is never clamped)", quotient < 1 ? "COMBAT_BUDGET_TOO_SMALL" : "COMBAT_ROSTER_LIMIT_EXCEEDED");
                count = (int)quotient;
                // Multiplication is the final authority at decimal division boundaries.
                while (count > 0 && !Fits(count, power, budget.Value)) count--;
                while (count < MaxTeamMembers && Fits(count + 1, power, budget.Value)) count++;
            }
            if (count < 1 || count > MaxTeamMembers) throw new ScenarioConfigurationException(kind + ": count must be 1.." + MaxTeamMembers, "COMBAT_ROSTER_LIMIT_EXCEEDED");
            try { _ = checked(power * count); }
            catch (OverflowException) { throw new ScenarioConfigurationException(kind + ": total combatPower overflow", "COMBAT_ROSTER_LIMIT_EXCEEDED"); }
            return new ResolvedCombatRoster { Id = id, PawnKind = kind, UnitCombatPower = power, RequestedBudget = budget, ResolvedCount = count, Mode = budget.HasValue ? "combatPower" : "count" };
        }
        private static bool Fits(int count, decimal power, decimal budget)
        {
            try { return checked(count * power) <= budget; }
            catch (OverflowException) { return false; }
        }
        public static string ValidateGenerated(ResolvedCombatRoster roster, XElement actor, decimal? budget, int count, string costMode, bool rejectedLimit = false)
        {
            if (roster == null || roster.GenerationPolicy != "frozen" || roster.Members == null || roster.Members.Count < 1 || roster.Members.Count > MaxTeamMembers + (rejectedLimit ? 1 : 0) || roster.ResolvedCount != roster.Members.Count
                || roster.CostMode != costMode || roster.Mode != (budget.HasValue ? "combatPower" : "count") || roster.RequestedBudget != budget
                || roster.UnitCombatPower != roster.ActualCombatPower / roster.ResolvedCount) return "生成名册人数、模式或成本口径不符";
            if (rejectedLimit && roster.ResolvedCount != MaxTeamMembers + 1) return "超员拒绝快照未证明第201个成员可负担";
            if (budget.HasValue ? roster.ActualCombatPower > budget || !rejectedLimit && (!roster.NextMemberCost.HasValue || roster.NextMemberCost <= 0 || budget - roster.ActualCombatPower >= roster.NextMemberCost) : roster.ResolvedCount != count)
                return "逐成员预算分配或固定人数不符";
            var configuration = SquadTemplates.ConfigurationXml(actor);
            foreach (var member in roster.Members)
            {
                if (member.PawnKind != actor.Element("basePawnKind")?.Value || member.ConfigurationXml != configuration || member.BaseCombatPower <= 0 || member.XenotypeFactor <= 0
                    || member.Cost != member.BaseCombatPower * (costMode == "native" ? member.XenotypeFactor : 1) || member.PawnTemplateXml == null) return "成员配置或成本算术不符";
                var captured = XElement.Parse(member.PawnTemplateXml);
                if ((string)captured.Element("Xenotype")?.Attribute("def") != member.Xenotype) return "成员异种成本与冻结异种不符";
                var error = PawnConfiguration.ValidateExplicit(XElement.Parse(configuration), captured);
                if (error != null) return error;
            }
            return null;
        }
        public static List<ResolvedCombatRoster> ResolveGroups(XElement c, Func<string, float> loadedPower, string source)
        {
            var result = new List<ResolvedCombatRoster>();
            foreach (var actor in c.Element("Actors").Elements("PawnGroup").Where(a => a.Attribute("combatPowerBudget") != null && a.Attribute("squad") == null))
            {
                if (actor.Attribute("count") != null) throw new ScenarioConfigurationException("count and combatPowerBudget are mutually exclusive");
                var kind = actor.Element("basePawnKind").Value;
                var resolved = Resolve(ScenarioLoader.A(actor, "id"), kind, Power(loadedPower(kind)), Positive(ScenarioLoader.A(actor, "combatPowerBudget")));
                resolved.Team = ScenarioLoader.A(actor, "team"); resolved.Source = source; resolved.TemplateXml = actor.ToString(SaveOptions.DisableFormatting);
                actor.Attribute("combatPowerBudget").Remove(); actor.SetAttributeValue("count", resolved.ResolvedCount);
                result.Add(resolved);
            }
            return result;
        }
    }
}
