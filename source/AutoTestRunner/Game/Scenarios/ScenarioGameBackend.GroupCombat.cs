using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using LogSyncMod.AutoTest.Scenarios;
using RimWorld;
using Verse;

namespace LogSyncMod.AutoTest
{
    internal sealed partial class ScenarioGameBackend
    {
        private readonly Dictionary<string, ResolvedCombatRoster> squadRosters = new();
        internal IReadOnlyDictionary<string, List<Thing>> GroupActors => actors;
        internal readonly Dictionary<string, string> GeneratedSnapshots = new(StringComparer.Ordinal);
        internal readonly List<CombatPreparationRecord> PreparationRecords = new();
        internal static Pawn GenerateSquadPawn(ResolvedSquadMember member, Faction faction, RimWorld.Planet.PlanetTile tile)
        {
            Rand.PushState(member.Seed);
            try { return GenerateTemporaryPawn(TemporaryPawnData.Parse(member.PawnTemplateXml), faction, null, out _, restoreColorState: true, forceConfiguredXenotype: true, restoreMember: member); }
            finally { Rand.PopState(); }
        }

        internal void PrepareGroupCombat(GroupCombatRoundInput definition, Map map, IReadOnlyDictionary<string, Faction> factions)
        {
            currentDefinition = new ScenarioCase { Xml = definition.Xml, TestDefName = definition.TestDefName, CaseId = definition.CaseId, CaseType = "GroupCombat", Source = definition.Source };
            runtimeRoot = XElement.Parse(definition.Xml);
            ValidateDefs(runtimeRoot, currentDefinition);
            foreach (var requirement in runtimeRoot.Element("Requirements")?.Elements() ?? Enumerable.Empty<XElement>())
                if (!LoadedModManager.RunningMods.Any(m => string.Equals(m.PackageIdPlayerFacing, A(requirement, "packageId"), StringComparison.OrdinalIgnoreCase))) throw new ScenarioConfigurationException("Required mod/DLC is not loaded: " + requirement);
            PrepareBoundActions();
            groupMap = map;
            foreach (var actor in runtimeRoot.Element("Actors").Elements().Where(a => a.Name == "Pawn" || a.Name == "PawnGroup"))
            {
                var id = A(actor, "id");
                var roster = definition.ResolvedRosters.FirstOrDefault(r => r.Id == id);
                if (roster == null && (id == "left" || id == "right") && definition.ResolvedRosters.Count == 2)
                    roster = definition.ResolvedRosters[id == "left" ? 0 : 1];
                if (roster?.Members.Count > 0) squadRosters.Add(id, roster);
            }
            foreach (var faction in factions) teams.Add(faction.Key, faction.Value);
            Rand.PushState(definition.Seed);
            try
            {
                PrepareEnvironment(runtimeRoot.Element("Scene"));
                foreach (var actor in runtimeRoot.Element("Actors").Elements())
                    using (ArenaPreparationContext.Enter(actor.Element("basePawnKind")?.Value.Trim(), runtimeRoot.Element("Steps")))
                        CreateActor(actor);
                ValidateEnvironment(runtimeRoot.Element("Scene"));
                bool Condition(XElement e)
                {
                    if (e.Name == "StepSucceeded") return PreparationRecords.Any(r => r.Completed && r.StepId == A(e, "ref"));
                    if (e.Name == "Any") return e.Elements().Any(Condition);
                    if (e.Name == "Not") return !Condition(e.Elements().Single());
                    if (e.HasElements) return e.Elements().All(Condition);
                    var sample = Sample(e); return ScenarioExecution.Match(e, sample, sample);
                }
                foreach (var step in runtimeRoot.Element("Steps").Elements())
                {
                    if (GeneratedSnapshots.Count == 0) CaptureGeneratedActors();
                    if (step.Name == "Require") { if (!Condition(step)) throw new InvalidOperationException("Group preparation predicate failed: " + step); }
                    else if (!Execute(step, true)) throw new ScenarioConfigurationException("Group preparation action did not finish synchronously: " + step);
                    if (step.Element("Expect") != null && !Condition(step.Element("Expect"))) throw new InvalidOperationException("Preparation postcondition failed: " + step);
                    var record = new CombatPreparationRecord { StepId = A(step, "id"), Completed = true };
                    foreach (var actor in runtimeRoot.Element("Actors").Elements().Where(a => a.Name == "Pawn" || a.Name == "PawnGroup"))
                    {
                        var members = actors[A(actor, "id")].Cast<Pawn>().ToList();
                        for (var index = 0; index < members.Count; index++)
                        {
                            var id = A(actor, "id") + "[" + index + "]"; var pawn = members[index];
                            if (CombatPreparation.Targets(step, id, pawn.Position.x, pawn.Position.z, runtimeRoot)) record.Snapshots[id] = TemporaryPawnData.Serialize(TemporaryPawnCapture.Capture(pawn));
                        }
                    }
                    PreparationRecords.Add(record);
                    trace("group.preparation", step.ToString(SaveOptions.DisableFormatting));
                }
                if (GeneratedSnapshots.Count == 0) CaptureGeneratedActors();
            }
            finally { Rand.PopState(); }
        }
        private void CaptureGeneratedActors()
        {
            foreach (var actor in runtimeRoot.Element("Actors").Elements().Where(a => a.Name == "Pawn" || a.Name == "PawnGroup"))
            {
                var members = actors[A(actor, "id")].Cast<Pawn>().ToList();
                for (var i = 0; i < members.Count; i++) GeneratedSnapshots.Add(A(actor, "id") + "[" + i + "]", TemporaryPawnData.Serialize(TemporaryPawnCapture.Capture(members[i])));
                if (squadRosters.TryGetValue(A(actor, "id"), out var roster))
                {
                    var units = members.Select((p, i) => new GroupCombatUnit { Id = A(actor, "id") + "[" + i + "]", Kind = p.kindDef.defName, ConfigurationId = roster.Members[i].ConfigurationId, ConfigurationKey = SquadTemplates.ConfigurationKey(roster.Members[i]),
                        GeneratedSnapshot = GeneratedSnapshots[A(actor, "id") + "[" + i + "]"], InitialSnapshot = GeneratedSnapshots[A(actor, "id") + "[" + i + "]"] }).ToList();
                    var error = SquadTemplates.ValidateUnits(roster, units, A(actor, "id"));
                    if (error != null) throw new ScenarioConfigurationException("Generated combat roster " + A(actor, "id") + ": " + error);
                }
            }
        }
    }
}
