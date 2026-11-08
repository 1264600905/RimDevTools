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
