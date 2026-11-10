using System.Xml.Linq;
using LogSyncMod.AutoTest.Scenarios;

internal static class CombatResultFixture
{
    internal static void Freeze(ResolvedCombatRoster roster, int seed, string? xenotype = null)
    {
        var config = new XElement("PawnTemplate", new XElement("basePawnKind", roster.PawnKind));
        if (xenotype != null) config.Add(new XElement("Xenotype", new XAttribute("def", xenotype)));
        roster.TemplateXml = config.ToString(SaveOptions.DisableFormatting); roster.GenerationPolicy = "frozen";
        roster.Members = Enumerable.Range(0, roster.ResolvedCount).Select(i => new ResolvedSquadMember {
            PawnKind = roster.PawnKind, Xenotype = xenotype, BaseCombatPower = roster.UnitCombatPower, Cost = roster.UnitCombatPower,
            Seed = SquadTemplates.MemberSeed(seed, roster.Id, i), ConfigurationId = roster.Id,
            ConfigurationXml = roster.TemplateXml, PawnTemplateXml = roster.TemplateXml
        }).ToList();
        roster.NextMemberCost = roster.RequestedBudget.HasValue ? roster.UnitCombatPower : null;
    }
    internal static GroupCombatUnit Unit(ResolvedCombatRoster roster, string team, int i) => new() {
        Id = (team == "A" ? "left" : "right") + "[" + i + "]", Team = team, ConfigurationId = roster.Members[i].ConfigurationId, ConfigurationKey = SquadTemplates.ConfigurationKey(roster.Members[i]), Kind = roster.PawnKind, Name = team + i,
        GeneratedSnapshot = roster.Members[i].PawnTemplateXml, InitialSnapshot = roster.Members[i].PawnTemplateXml
    };
    internal static void Environment(GroupCombatRoundResult result)
    {
        result.StudioIsolation = new ArenaStudioIsolationResult { WorldTileUnchanged = true };
        result.ArenaMaintenance = new ArenaMaintenanceResult { CalendarRestored = true,
            NoonResetTicks = Enumerable.Range(0, result.ElapsedTicks / 5000 + 1).Select(i => i * 5000).ToList(),
            NoonResetHours = Enumerable.Repeat(12f, result.ElapsedTicks / 5000 + 1).ToList() };
    }
}
