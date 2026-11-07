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
        internal IReadOnlyDictionary<string, List<Thing>> GroupActors => actors;
        internal void PrepareGroupCombat(GroupCombatRoundInput definition, Map map, IReadOnlyDictionary<string, Faction> factions)
        {
            currentDefinition = new ScenarioCase { Xml = definition.Xml, TestDefName = definition.TestDefName, CaseId = definition.CaseId, CaseType = "GroupCombat", Source = definition.Source };
            runtimeRoot = XElement.Parse(definition.Xml);
            ValidateDefs(runtimeRoot, currentDefinition);
            foreach (var requirement in runtimeRoot.Element("Requirements")?.Elements() ?? Enumerable.Empty<XElement>())
                if (!LoadedModManager.RunningMods.Any(m => string.Equals(m.PackageIdPlayerFacing, A(requirement, "packageId"), StringComparison.OrdinalIgnoreCase))) throw new ScenarioConfigurationException("Required mod/DLC is not loaded: " + requirement);
            PrepareBoundActions();
            groupMap = map;
            foreach (var faction in factions) teams.Add(faction.Key, faction.Value);
            Rand.PushState(definition.Seed);
            try
            {
                PrepareEnvironment(runtimeRoot.Element("Scene"));
                foreach (var actor in runtimeRoot.Element("Actors").Elements()) CreateActor(actor);
                ValidateEnvironment(runtimeRoot.Element("Scene"));
                bool Condition(XElement e)
                {
                    if (e.Name == "Any") return e.Elements().Any(Condition);
                    if (e.Name == "Not") return !Condition(e.Elements().Single());
                    if (e.HasElements) return e.Elements().All(Condition);
                    var sample = Sample(e); return ScenarioExecution.Match(e, sample, sample);
                }
                foreach (var step in runtimeRoot.Element("Steps").Elements())
                {
                    if (step.Name == "Require") { if (!Condition(step)) throw new InvalidOperationException("Group preparation predicate failed: " + step); }
                    else if (!Execute(step, true)) throw new ScenarioConfigurationException("Group preparation action did not finish synchronously: " + step);
                    trace("group.preparation", step.ToString(SaveOptions.DisableFormatting));
                }
            }
            finally { Rand.PopState(); }
        }
    }
}
