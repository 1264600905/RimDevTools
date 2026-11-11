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
        internal TemporaryPawnSnapshot CaptureCombatPawn(Pawn pawn)
        {
            if (!continueArena) return TemporaryPawnCapture.Capture(pawn);
            TemporaryPawnSnapshot snapshot = null;
            ArenaContinuity.TryOptional("pawn=" + pawn.GetUniqueLoadID() + "; snapshot",
                () => snapshot = TemporaryPawnCapture.Capture(pawn), message => trace("arena.warning.snapshot", message));
            return snapshot;
        }
        internal static Pawn GenerateSquadPawn(ResolvedSquadMember member, Faction faction, RimWorld.Planet.PlanetTile tile,
            bool continueArena = false, Action<string, string> diagnostic = null)
        {
            Rand.PushState(member.Seed);
            try { return GenerateTemporaryPawn(TemporaryPawnData.Parse(member.PawnTemplateXml), faction, diagnostic, out _, restoreColorState: true, forceConfiguredXenotype: true, restoreMember: member, continueArena: continueArena); }
            finally { Rand.PopState(); }
        }

        internal void PrepareGroupCombat(GroupCombatRoundInput definition, Map map, IReadOnlyDictionary<string, Faction> factions, bool continueArena = false)
        {
            this.continueArena = continueArena;
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
                var battlefield = CombatMapBuilder.Generate(Map, runtimeRoot.Element("Scene"), definition.Seed);
                PrepareEnvironment(runtimeRoot.Element("Scene"));
                battlefield?.PrepareSpawns(runtimeRoot.Element("Actors"), trace);
                foreach (var actor in runtimeRoot.Element("Actors").Elements())
                    using (ArenaPreparationContext.Enter(actor.Element("basePawnKind")?.Value.Trim(), runtimeRoot.Element("Steps")))
                        CreateActor(actor);
                if (continueArena) ArenaContinuity.TryOptional("environment verification", () => ValidateEnvironment(runtimeRoot.Element("Scene")), message => trace("arena.warning.environment", message));
                else ValidateEnvironment(runtimeRoot.Element("Scene"));
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
                    if (continueArena)
                    {
                        ArenaContinuity.TryOptional("step=" + A(step, "id", step.Name.LocalName), () =>
                        {
                            if (step.Name == "Require") { if (!Condition(step)) throw new InvalidOperationException("Preparation predicate not met: " + step); }
                            else if (!Execute(step, true)) throw new InvalidOperationException("Preparation action unfinished: " + step);
                            if (step.Element("Expect") != null && !Condition(step.Element("Expect"))) throw new InvalidOperationException("Preparation postcondition not met: " + step);
                            PreparationRecords.Add(new CombatPreparationRecord { StepId = A(step, "id"), Completed = true });
                        }, message => trace("arena.warning.preparation", message));
                        continue;
                    }
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
                for (var i = 0; i < members.Count; i++)
                {
                    var snapshot = CaptureCombatPawn(members[i]);
                    GeneratedSnapshots.Add(A(actor, "id") + "[" + i + "]", snapshot == null ? null : TemporaryPawnData.Serialize(snapshot));
                }
                if (squadRosters.TryGetValue(A(actor, "id"), out var roster))
                {
                    var units = members.Select((p, i) => new GroupCombatUnit { Id = A(actor, "id") + "[" + i + "]", Kind = p.kindDef.defName, ConfigurationId = roster.Members[i].ConfigurationId, ConfigurationKey = SquadTemplates.ConfigurationKey(roster.Members[i]),
                        GeneratedSnapshot = GeneratedSnapshots[A(actor, "id") + "[" + i + "]"], InitialSnapshot = GeneratedSnapshots[A(actor, "id") + "[" + i + "]"] }).ToList();
                    string error = null;
                    if (continueArena) ArenaContinuity.TryOptional("roster verification", () => error = SquadTemplates.ValidateUnits(roster, units, A(actor, "id"), message => trace("arena.warning.roster", message)), message => trace("arena.warning.roster", message));
                    else error = SquadTemplates.ValidateUnits(roster, units, A(actor, "id"));
                    if (error != null)
                    {
                        if (!continueArena) throw new ScenarioConfigurationException("Generated combat roster " + A(actor, "id") + ": " + error);
                        trace("arena.warning.roster", "Generated combat roster " + A(actor, "id") + ": " + error);
                    }
                }
            }
        }
    }
}
