using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using LogSyncMod.AutoTest.Scenarios;
using RimWorld;
using UnityEngine;
using Verse;

namespace LogSyncMod.AutoTest
{
    internal sealed partial class ScenarioGameBackend
    {
        private static bool InRange(float value, XElement c) => value >= ScenarioLoader.D(c, "min", 0) && value <= ScenarioLoader.D(c, "max", 1);

        private List<Thing> DebugTargets(XElement select)
        {
            if (A(select, "mode") == "single")
            {
                var t = One(A(select, "actor"));
                if (!owned.Contains(t) || !t.Spawned || t.Map != Map || t.Destroyed || t is Pawn p && p.Dead)
                    throw new ScenarioConfigurationException("DebugAction target must be a live, spawned object owned by this scene");
                return new List<Thing> { t };
            }
            var rect = CellRect.FromLimits(I(select, "minX"), I(select, "minZ"), I(select, "maxX"), I(select, "maxZ"));
            if (rect.Cells.Any(c => !c.InBounds(Map))) throw new ScenarioConfigurationException("DebugAction rectangle outside scene");
            var relative = A(select, "relativeTo") == null ? null : One(A(select, "relativeTo"));
            var targets = owned.OfType<Pawn>().Where(p => p.Spawned && p.Map == Map && !p.Dead && !p.Destroyed && rect.Contains(p.Position));
            if (A(select, "filter") == "enemies") targets = targets.Where(p => p.HostileTo(relative));
            if (A(select, "filter") == "allies") targets = targets.Where(p => p.Faction == relative.Faction || p.Faction != null && relative.Faction != null && p.Faction.RelationKindWith(relative.Faction) == FactionRelationKind.Ally);
            var result = targets.Cast<Thing>().OrderBy(t => t.thingIDNumber).ToList();
            if (result.Count == 0) throw new ScenarioConfigurationException("DebugAction selection contains no eligible scene pawns");
            return result;
        }

        private void ExecuteDebugAction(XElement step)
        {
            var binding = runtimeRoot?.Element("ActionBindings")?.Elements("ActionBinding").FirstOrDefault(b => A(b, "id") == A(step, "actionId"));
            ScenarioDebugActions.Validate(step, "runtime", binding);
            var action = A(step, "actionId");
            var targets = DebugTargets(step.Element("Select"));
            string SourceState() => string.Join("|", source.mapPawns.AllPawnsSpawned.Where(p => !owned.Contains(p)).OrderBy(p => p.thingIDNumber).Select(p => Describe(p) + "; needs=" + string.Join(",", p.needs?.AllNeeds.Select(n => n.def.defName + "=" + n.CurLevel) ?? Enumerable.Empty<string>())));
            var sourceBefore = SourceState();
            foreach (var t in targets)
            {
                string State() => t is Pawn p ? Describe(p) + "; needs=" + string.Join(",", p.needs?.AllNeeds.Select(n => n.def.defName + "=" + n.CurLevelPercentage) ?? Enumerable.Empty<string>()) + "; hemogen=" + p.genes?.GetFirstGeneOfType<Gene_Hemogen>()?.ValuePercent + "; psyfocus=" + p.psychicEntropy?.CurrentPsyfocus : t.GetUniqueLoadID() + "; fuel=" + t.TryGetComp<CompRefuelable>()?.Fuel + "; battery=" + t.TryGetComp<CompPowerBattery>()?.StoredEnergyPct;
                trace("debug.before", action + "; target=" + t.GetUniqueLoadID() + "; " + State());
                try
                {
                    if (boundActions.TryGetValue(action, out var boundAction)) boundAction.Execute(step, t, trace);
                    else if (action == "native.refuel")
                    { var comp = t.TryGetComp<CompRefuelable>() ?? throw new ScenarioConfigurationException("Target has no refuelable component"); comp.Refuel(comp.Props.fuelCapacity); if (comp.FuelPercentOfMax < .999f) throw new InvalidOperationException("Refuel postcondition failed"); }
                    else if (action == "native.chargeBattery")
                    { var comp = t.TryGetComp<CompPowerBattery>() ?? throw new ScenarioConfigurationException("Target has no battery component"); comp.SetStoredEnergyPct(1); if (comp.StoredEnergyPct < .999f) throw new InvalidOperationException("Battery postcondition failed"); }
                    else
                    {
                        var p = t as Pawn ?? throw new ScenarioConfigurationException(action + " requires a Pawn");
                        if (action == "axolotl.fillQi")
                        {
                            var before = ReadAxolotlQi(p, out var max);
                            if (max <= 0) throw new ScenarioConfigurationException("Axolotl Qi has no capacity; configure the pawn's actual meridian prerequisites first");
                            var comp = AxolotlEnergy(p);
                            var desired = max * (float)ScenarioLoader.D(step, "value", 1);
                            comp.GetType().GetProperty("Energy", BindingFlags.Instance | BindingFlags.Public).SetValue(comp, desired);
                            var after = ReadAxolotlQi(p, out max);
                            trace("debug.resource", "axolotl.qi; target=" + p.GetUniqueLoadID() + "; before=" + before + "; after=" + after + "; max=" + max);
                            if (!Mathf.Approximately(desired, after)) throw new InvalidOperationException("Axolotl Qi postcondition failed");
                        }
                        else if (action == "native.fillNeeds")
                        { Need(p.needs, "needs"); if (p.needs.AllNeeds.Count == 0) throw new ScenarioConfigurationException("Target has no needs to fill"); foreach (var n in p.needs.AllNeeds) n.CurLevel = n.MaxLevel; if (p.needs.AllNeeds.Any(n => n.CurLevelPercentage < .999f)) throw new InvalidOperationException("Fill needs postcondition failed"); }
                        else if (action == "native.fillHemogen")
                        { var gene = p.genes?.GetFirstGeneOfType<Gene_Hemogen>(); if (gene == null || !gene.Active) throw new ScenarioConfigurationException("An active Hemogenic gene is required"); var value = (float)ScenarioLoader.D(step, "value", 1); gene.ValuePercent = value; if (!Mathf.Approximately(gene.ValuePercent, value)) throw new InvalidOperationException("Hemogen postcondition failed"); }
                        else if (action == "native.setPsyfocus")
                        { Need(p.psychicEntropy, "psychic entropy"); if (p.GetPsylinkLevel() < 1) throw new ScenarioConfigurationException("Psyfocus preparation requires a native psylink"); var value = (float)ScenarioLoader.D(step, "value", 1); p.psychicEntropy.OffsetPsyfocusDirectly(value - p.psychicEntropy.CurrentPsyfocus); if (!Mathf.Approximately(value, p.psychicEntropy.CurrentPsyfocus)) throw new InvalidOperationException("Psyfocus postcondition failed"); }
                        else if (action == "native.addHediff") AddHediff(p, step);
                        else if (action == "native.removeHediff") RemoveHediff(p, step);
                        else if (action == "native.healInjuries") foreach (var injury in p.health.hediffSet.hediffs.OfType<Hediff_Injury>().ToList()) injury.Heal(injury.Severity);
                        else throw new ScenarioConfigurationException("No native adapter for " + action);
                    }
                }
                catch (Exception ex) { trace("debug.error", action + "; target=" + t.GetUniqueLoadID() + "; " + ex); throw; }
                finally { trace("debug.after", action + "; target=" + t.GetUniqueLoadID() + "; " + State()); }
            }
            var sourceUnchanged = sourceBefore == SourceState();
            trace("debug.scope", "sourceMap=" + source.uniqueID + "; sourcePawns=" + string.Join(",", source.mapPawns.AllPawnsSpawned.Select(p => p.GetUniqueLoadID())) + "; selected=" + string.Join(",", targets.Select(t => t.GetUniqueLoadID())) + "; sourceUnchanged=" + sourceUnchanged);
            if (!sourceUnchanged) throw new InvalidOperationException("DebugAction or a mod callback changed source-map pawns outside this scene");
        }

        private readonly Dictionary<string, ScenarioBoundAction> boundActions = new Dictionary<string, ScenarioBoundAction>();

        private void PrepareBoundActions()
        {
            Type ResolveType(string name)
            {
                var types = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name, false)).Where(t => t != null).Distinct().ToList();
                if (types.Count != 1) throw new ScenarioConfigurationException("Binding type must resolve uniquely: " + name);
                return types[0];
            }
            object ResolveBoundDef(Type type, string name)
            {
                if (!typeof(Def).IsAssignableFrom(type)) throw new ScenarioConfigurationException("Binding Def argument must derive from Verse.Def: " + type.FullName);
                return typeof(DefDatabase<>).MakeGenericType(type).GetMethod("GetNamed", new[] { typeof(string), typeof(bool) }).Invoke(null, new object[] { name, true })
                    ?? throw new ScenarioConfigurationException("Binding Def not found: " + type.FullName + "/" + name);
            }
            foreach (var b in runtimeRoot.Element("ActionBindings")?.Elements("ActionBinding") ?? Enumerable.Empty<XElement>())
            {
                var package = A(b, "packageId");
                var mod = LoadedModManager.RunningMods.FirstOrDefault(m => string.Equals(m.PackageIdPlayerFacing, package, StringComparison.OrdinalIgnoreCase))
                    ?? throw new ScenarioConfigurationException("Action binding requires loaded mod: " + package);
                foreach (var e in b.Elements("ExpectHediff").Where(e => e.Attribute("def") != null)) Def<HediffDef>(A(e, "def"));
                var executor = new ScenarioBoundAction(b, ResolveType,
                    t => mod.assemblies?.loadedAssemblies.Contains(t.Assembly) == true || package.Equals("ludeon.rimworld", StringComparison.OrdinalIgnoreCase) && t.Assembly == typeof(Pawn).Assembly,
                    ResolveBoundDef, typeof(Hediff),
                    target => target is Pawn p ? p.health.hediffSet.hediffs.Cast<object>() : throw new ScenarioConfigurationException("ExpectHediff requires a Pawn target"),
                    h => ((Hediff)h).def.defName);
                boundActions.Add(A(b, "id"), executor);
                trace("debug.binding.prepared", "action=" + A(b, "id") + "; package=" + package + "; method=" + A(b, "type") + "." + A(b, "method"));
            }
        }

        private static ThingComp AxolotlEnergy(Pawn p)
        {
            var comp = p?.AllComps.FirstOrDefault(c => c.GetType().FullName == "Axolotl.CompAxolotlEnergy");
            if (comp == null) throw new ScenarioConfigurationException("Loaded Pawn lacks Axolotl.CompAxolotlEnergy; use an actual compatible race");
            foreach (var name in new[] { "Energy", "EnergyMax" })
            {
                var property = comp.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
                if (property?.PropertyType != typeof(float) || !property.CanRead || name == "Energy" && !property.CanWrite) throw new ScenarioConfigurationException("Unsupported Axolotl API version: " + name);
            }
            return comp;
        }

        internal static float ReadAxolotlQi(Pawn pawn, out float max)
        {
            var comp = AxolotlEnergy(pawn);
            max = (float)comp.GetType().GetProperty("EnergyMax", BindingFlags.Instance | BindingFlags.Public).GetValue(comp);
            return (float)comp.GetType().GetProperty("Energy", BindingFlags.Instance | BindingFlags.Public).GetValue(comp);
        }

        private void ApplyPawnResources(Pawn pawn, XElement resources)
        {
            foreach (var resource in resources?.Elements() ?? Enumerable.Empty<XElement>())
            {
                var value = (float)ScenarioLoader.D(resource, "value");
                float actual;
                if (resource.Name == "GeneResource")
                {
                    var gene = pawn.genes?.GetGene(Def<GeneDef>(A(resource, "def"))) as Gene_Resource;
                    if (gene == null || !gene.Active) throw new ScenarioConfigurationException("Template requires an active Gene_Resource: " + A(resource, "def"));
                    gene.Value = value; actual = gene.Value;
                }
                else if (resource.Name == "Psyfocus")
                {
                    if (pawn.psychicEntropy == null || pawn.GetPsylinkLevel() < 1) throw new ScenarioConfigurationException("Template Psyfocus requires a native psylink");
                    pawn.psychicEntropy.OffsetPsyfocusDirectly(value - pawn.psychicEntropy.CurrentPsyfocus); actual = pawn.psychicEntropy.CurrentPsyfocus;
                }
                else if (resource.Name == "AxolotlQi")
                {
                    var comp = AxolotlEnergy(pawn); comp.GetType().GetProperty("Energy", BindingFlags.Instance | BindingFlags.Public).SetValue(comp, value);
                    actual = ReadAxolotlQi(pawn, out _);
                }
                else throw new ScenarioConfigurationException("Unregistered Pawn template resource");
                if (Math.Abs(value - actual) > .001f) throw new ScenarioConfigurationException("Native resource rejected template value: " + resource.Name + "; requested=" + value + "; actual=" + actual);
                trace("pawn.resource", resource.Name + "; def=" + A(resource, "def") + "; value=" + actual);
            }
        }

        private static bool SampleComponent(Thing thing, XElement c)
        {
            var type = A(c, "component");
            if (type == "fuel") return InRange((thing.TryGetComp<CompRefuelable>() ?? throw new ScenarioConfigurationException("No fuel component")).FuelPercentOfMax, c);
            if (type == "battery") return InRange((thing.TryGetComp<CompPowerBattery>() ?? throw new ScenarioConfigurationException("No battery component")).StoredEnergyPct, c);
            if (type == "power") return (thing.TryGetComp<CompPowerTrader>() ?? throw new ScenarioConfigurationException("No power component")).PowerOn == bool.Parse(A(c, "state"));
            if (type == "powerOutput") { var output = (thing.TryGetComp<CompPowerTrader>() ?? throw new ScenarioConfigurationException("No power component")).PowerOutput; return output >= ScenarioLoader.D(c, "min", 0) && output <= ScenarioLoader.D(c, "max", double.MaxValue); }
            if (type == "rot") return (thing.TryGetComp<CompRottable>() ?? throw new ScenarioConfigurationException("No rot component")).Stage.ToString() == A(c, "state");
            throw new ScenarioConfigurationException("Unknown component probe: " + type);
        }
    }
}
