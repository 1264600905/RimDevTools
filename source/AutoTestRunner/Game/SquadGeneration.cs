using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using HarmonyLib;
using LogSyncMod.AutoTest.Scenarios;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace LogSyncMod.AutoTest
{
    public sealed class TestSquadMemberSpec
    {
        public string PawnKind { get; set; }
        public string Xenotype { get; set; }
    }
    public sealed class TestSquadGenerationContext
    {
        public SquadTemplate Template { get; internal set; }
        public Faction Faction { get; internal set; }
        public decimal? Budget { get; internal set; }
        public int? Count { get; internal set; }
        public string CostMode { get; internal set; }
        public int Seed { get; internal set; }
    }
    public interface ITestSquadGenerator
    {
        IReadOnlyList<TestSquadMemberSpec> Generate(TestSquadGenerationContext context, IReadOnlyDictionary<string, string> parameters);
    }
    // Extensions register an explicit ID; XML never resolves arbitrary CLR type names.
    public static class TestSquadGenerators
    {
        private static readonly Dictionary<string, ITestSquadGenerator> Generators = new(StringComparer.Ordinal);
        public static void Register(string id, ITestSquadGenerator generator)
        {
            if (string.IsNullOrWhiteSpace(id) || generator == null || !Generators.TryAdd(id, generator)) throw new ArgumentException("Invalid or duplicate squad generator: " + id);
        }
        internal static ITestSquadGenerator Get(string id) => Generators.TryGetValue(id, out var generator) ? generator : throw new ScenarioConfigurationException("Unknown registered squad generator: " + id);
    }
    internal static class SquadGeneration
    {
        internal static string KindLabel(string defName) => GroupCombatPresentation.UnitTypeName(DefDatabase<PawnKindDef>.GetNamedSilentFail(defName)?.LabelCap.ToString(), defName);
        private static T Clone<T>(T value) => (T)typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(value, null);
        internal static Faction CreateFaction(string defName, string name)
        {
            var faction = new Faction { def = DefDatabase<FactionDef>.GetNamed(defName), loadID = Find.UniqueIDsManager.GetNextFactionID(), Name = name, temporary = true, hidden = true };
            faction.ideos = new FactionIdeosTracker(faction);
            if (Faction.OfPlayer?.ideos?.PrimaryIdeo != null) faction.ideos.SetPrimary(Faction.OfPlayer.ideos.PrimaryIdeo);
            return faction;
        }
        internal sealed class FactionScope : IDisposable
        {
            private static readonly HashSet<FactionScope> Active = new();
            internal Faction Faction { get; }
            internal FactionScope(string defName, string name)
            {
                Faction = CreateFaction(defName, name);
                foreach (var other in Find.FactionManager.AllFactions.Concat(Active.Select(s => s.Faction)).Distinct().ToList())
                    Faction.SetRelation(new FactionRelation { other = other, kind = FactionRelationKind.Neutral, baseGoodwill = 0 });
                Active.Add(this);
            }
            public void Dispose() { Faction.RemoveAllRelations(); Active.Remove(this); }
        }
        internal static ResolvedCombatRoster Resolve(string id, SquadTemplate template, decimal? budget, int count, string costMode, int seed, PlanetTile tile)
        {
            SquadTemplates.ValidateCostMode(costMode);
            if (budget.HasValue && (budget <= 0 || budget > 10000000) || !budget.HasValue && (count < 1 || count > CombatRoster.MaxTeamMembers)) throw new ScenarioConfigurationException("Squad budget must be in (0,10000000], or count in 1..200");
            if (template.Source == "groups") return ResolvePawnGroups(id, template, budget, count, costMode, seed);
            using var factionScope = new FactionScope(template.FactionDef, template.Id);
            var faction = factionScope.Faction;
            var strategy = DefDatabase<RaidStrategyDef>.GetNamed(template.StrategyDef);
            var parms = new PawnGroupMakerParms { faction = faction, groupKind = PawnGroupKindDefOf.Combat, points = budget.HasValue ? (float)budget.Value : template.ReferencePoints, tile = tile, raidStrategy = strategy, generateFightersOnly = true };
            var roster = new ResolvedCombatRoster { Id = id, SquadId = template.Id, FactionDef = template.FactionDef, Mode = budget.HasValue ? "combatPower" : "count", RequestedBudget = budget, CostMode = costMode, GenerationPolicy = "frozen" };
            Rand.PushState(seed);
            try
            {
                List<TestSquadMemberSpec> specs;
                if (template.Source == "generator")
                {
                    specs = TestSquadGenerators.Get(template.GeneratorId).Generate(new TestSquadGenerationContext { Template = template, Faction = faction, Budget = budget, Count = budget.HasValue ? null : count, CostMode = costMode, Seed = seed }, template.Parameters)?.ToList()
                        ?? throw new ScenarioConfigurationException("Squad generator returned null");
                }
                else
                {
                    PawnGroupMaker maker;
                    if (template.Source == "faction")
                    {
                        var makers = faction.def.pawnGroupMakers ?? throw new ScenarioConfigurationException("Faction has no pawnGroupMakers: " + template.FactionDef);
                        roster.GroupMakers = makers.Select((m, i) => new SquadMakerInfo { Index = i, KindDef = m.kindDef.defName, Commonality = m.commonality,
                            MaxTotalPoints = m.maxTotalPoints, Eligible = m.kindDef == parms.groupKind && m.CanGenerateFrom(parms), Fingerprint = Fingerprint(m),
                            Options = m.options.Select(o => new SquadOption { PawnKind = o.kind.defName, Weight = (decimal)o.selectionWeight }).ToList(),
                            DisallowedStrategies = m.disallowedStrategies?.Select(d => d.defName).ToList() ?? new() }).ToList();
                        if (template.MakerMode == "specific")
                        {
                            if (template.MakerIndex >= makers.Count) throw new ScenarioConfigurationException("GroupMaker index out of range: " + template.MakerIndex);
                            maker = makers[template.MakerIndex.Value];
                            if (maker.kindDef != parms.groupKind || !maker.CanGenerateFrom(parms)) throw new ScenarioConfigurationException("Selected GroupMaker cannot generate this squad at the configured points/strategy");
                        }
                        else if (!PawnGroupMakerUtility.TryGetRandomPawnGroupMaker(parms, out maker)) throw new ScenarioConfigurationException("Faction has no eligible Combat GroupMaker: " + template.FactionDef);
                        roster.GroupMakerIndex = makers.IndexOf(maker); roster.GroupMakerFingerprint = Fingerprint(maker);
                        if (template.Fingerprint != null && template.Fingerprint != roster.GroupMakerFingerprint) throw new ScenarioConfigurationException("GroupMaker fingerprint changed: " + template.Id);
                    }
                    else maker = new PawnGroupMaker { kindDef = PawnGroupKindDefOf.Combat, options = template.Options.Select(o => new PawnGenOption { kind = Kind(o.PawnKind), selectionWeight = Weight(o.Weight) }).ToList() };
                    var options = maker.options;
                    // Keep original PawnKind identities for native strategy/limit checks.
                    // Only selection sees overridden xenotype weights and accounting costs.
                    using var selection = new XenotypeSelectionScope(options, template, faction, costMode);
                    if (template.Source == "custom" && template.Composition == "fixed")
                    {
                        specs = new List<TestSquadMemberSpec>();
                        var chosen = new List<PawnGenOptionWithXenotype>();
                        foreach (var o in template.Options)
                            for (var i = 0; i < o.Count; i++)
                            {
                                var option = options.Single(x => x.kind.defName == o.PawnKind);
                                var available = PawnGroupMakerUtility.GetOptions(parms, parms.faction.def, new List<PawnGenOption> { option }, parms.points, float.MaxValue, float.MaxValue, chosen, chosen.Any(o => o.Option.kind.factionLeader));
                                available.RemoveAll(o => !PawnGroupMakerUtility.PawnGenOptionValid(o.Option, parms, chosen));
                                if (!available.TryRandomElementByWeight(x => x.SelectionWeight, out var choice)) throw new ScenarioConfigurationException("Fixed member has no valid xenotype/strategy option: " + o.PawnKind);
                                chosen.Add(choice); specs.Add(Spec(choice));
                            }
                    }
                    else if (budget.HasValue)
                    {
                        var cheapest = options.Min(o => o.Cost);
                        if (!float.IsFinite(cheapest) || cheapest <= 0 || (double)budget.Value / cheapest > 10000) throw new ScenarioConfigurationException("Squad budget exceeds bounded allocation range");
                        specs = PawnGroupMakerUtility.ChoosePawnGenOptionsByPoints(parms.points, options, parms).Select(Spec).ToList();
                    }
                    else
                    {
                        specs = new List<TestSquadMemberSpec>(); var chosen = new List<PawnGenOptionWithXenotype>();
                        while (specs.Count < count)
                        {
                            var candidates = PawnGroupMakerUtility.GetOptions(parms, parms.faction.def, options, parms.points, float.MaxValue, null, chosen, chosen.Any(o => o.Option.kind.factionLeader));
                            candidates.RemoveAll(o => !PawnGroupMakerUtility.PawnGenOptionValid(o.Option, parms, chosen));
                            if (!candidates.TryRandomElementByWeight(o => o.SelectionWeight, out var choice)) throw new ScenarioConfigurationException("Cannot fill exact squad count with the current strategy/options: " + template.Id);
                            chosen.Add(choice); specs.Add(Spec(choice));
                        }
                    }
                }
                if (specs.Count < 1) throw new ScenarioConfigurationException("Squad must contain at least one member");
                foreach (var spec in specs)
                {
                    var kind = Kind(spec.PawnKind);
                    if (kind.RaceProps.Humanlike && ModsConfig.BiotechActive && spec.Xenotype == null)
                    {
                        var distribution = template.XenotypeMode == "inherit" ? PawnGenerator.XenotypesAvailableFor(kind, faction: faction).ToDictionary(p => p.Key, p => p.Value)
                            : template.Xenotypes.ToDictionary(x => Xeno(x.Def), x => Weight(x.Weight));
                        if (!distribution.Where(x => x.Key.canGenerateAsCombatant).TryRandomElementByWeight(x => x.Value, out var selected)) throw new ScenarioConfigurationException("No combat xenotype for squad member");
                        spec.Xenotype = selected.Key.defName;
                    }
                    var xenotype = Xeno(spec.Xenotype);
                    if (xenotype != null && (!kind.RaceProps.Humanlike || !ModsConfig.BiotechActive || !xenotype.canGenerateAsCombatant)) throw new ScenarioConfigurationException("Incompatible squad xenotype: " + spec.PawnKind + "/" + spec.Xenotype);
                    var power = CombatRoster.Power(kind.combatPower); var factor = xenotype == null ? 1 : CombatRoster.Power(xenotype.combatPowerFactor);
                    roster.Members.Add(new ResolvedSquadMember { PawnKind = spec.PawnKind, Xenotype = spec.Xenotype, BaseCombatPower = power, XenotypeFactor = factor, Cost = power * (costMode == "native" ? factor : 1), Seed = SquadTemplates.MemberSeed(seed, id, roster.Members.Count) });
                }
                if (budget.HasValue ? roster.ActualCombatPower > budget : specs.Count != count) throw new ScenarioConfigurationException("Squad does not satisfy its CP/count budget; fixed lists are not truncated or padded");
                roster.PawnKind = roster.Members[0].PawnKind; roster.ResolvedCount = roster.Members.Count; roster.UnitCombatPower = roster.ActualCombatPower / roster.ResolvedCount;
                if (specs.Count > CombatRoster.MaxTeamMembers)
                {
                    var limit = new ScenarioConfigurationException("Squad exceeds 200 members; allocation is never silently clamped", "COMBAT_ROSTER_LIMIT_EXCEEDED");
                    limit.Data["RejectedRoster"] = roster;
                    throw limit;
                }
                // Generate fresh native pawns once to freeze their loadouts. No world pawns are borrowed.
                foreach (var member in roster.Members)
                {
                    Pawn pawn = null;
                    try
                    {
                        pawn = GenerateMember(member, faction, tile, strategy, parms.points);
                        member.Childhood = pawn.story?.Childhood?.defName; member.Adulthood = pawn.story?.Adulthood?.defName;
                        var capture = TemporaryPawnCapture.Capture(pawn);
                        var xml = XElement.Parse(capture.TemplateXml);
                        foreach (var field in xml.Elements().Where(e => !new[] { "basePawnKind", "race", "gender", "Age", "Appearance", "Traits", "Skills", "Apparel", "Equipment", "Xenotype", "Genes" }.Contains(e.Name.LocalName)).ToList()) field.Remove();
                        member.PawnTemplateXml = xml.ToString(SaveOptions.DisableFormatting);
                    }
                    finally { Destroy(pawn); }
                }
                var integrityError = SquadTemplates.ValidateRoster(roster, template, budget, count, costMode);
                if (integrityError != null) throw new ScenarioConfigurationException(integrityError);
                return roster;
            }
            finally { Rand.PopState(); }
        }
        private static ResolvedCombatRoster ResolvePawnGroups(string id, SquadTemplate template, decimal? budget, int count, string costMode, int seed)
        {
            var total = SquadTemplates.GroupCount(template);
            if (!budget.HasValue && count != total) throw new ScenarioConfigurationException("Squad count does not match its PawnGroups");
            using var faction = new FactionScope(template.FactionDef, template.Id);
            var roster = new ResolvedCombatRoster { Id = id, SquadId = template.Id, FactionDef = template.FactionDef, Mode = budget.HasValue ? "combatPower" : "count", RequestedBudget = budget, CostMode = costMode, GenerationPolicy = "frozen" };
            foreach (var groupXml in template.PawnGroups)
            {
                var group = XElement.Parse(groupXml);
                var configuration = SquadTemplates.ConfigurationXml(group);
                for (var i = 0; i < ScenarioLoader.I(group, "count"); i++)
                {
                    Pawn pawn = null;
                    var memberSeed = SquadTemplates.MemberSeed(seed, id, roster.Members.Count);
                    Rand.PushState(memberSeed);
                    try
                    {
                        var generation = CombatConfiguration(group, faction.Faction);
                        pawn = ScenarioGameBackend.GenerateTemporaryPawn(TemporaryPawnData.Parse(generation.ToString(SaveOptions.DisableFormatting)), faction.Faction, null, out _, forceConfiguredXenotype: true);
                        var capture = XElement.Parse(TemporaryPawnCapture.Capture(pawn).TemplateXml);
                        var explicitError = PawnConfiguration.ValidateExplicit(XElement.Parse(configuration), capture);
                        if (explicitError != null) throw new ScenarioConfigurationException(explicitError);
                        var retained = new HashSet<string>(new[] { "basePawnKind", "race", "gender", "Age", "Appearance", "Traits", "Skills", "Apparel", "Equipment", "Xenotype", "Genes" });
                        foreach (var field in new[] { "Hediffs", "Abilities", "Resources" }) if (group.Element(field) != null || field == "Abilities" && group.Element("abilities") != null) retained.Add(field);
                        foreach (var field in capture.Elements().Where(e => !retained.Contains(e.Name.LocalName)).ToList()) field.Remove();
                        var power = CombatRoster.Power(Kind(group.Element("basePawnKind").Value).combatPower);
                        var xenotype = pawn.genes?.Xenotype;
                        var factor = xenotype == null ? 1 : CombatRoster.Power(xenotype.combatPowerFactor);
                        roster.Members.Add(new ResolvedSquadMember { PawnKind = pawn.kindDef.defName, Xenotype = xenotype?.defName, BaseCombatPower = power, XenotypeFactor = factor,
                            Cost = power * (costMode == "native" ? factor : 1), Seed = memberSeed, ConfigurationId = ScenarioLoader.A(group, "id"), ConfigurationXml = configuration,
                            PawnTemplateXml = capture.ToString(SaveOptions.DisableFormatting), Childhood = pawn.story?.Childhood?.defName, Adulthood = pawn.story?.Adulthood?.defName });
                    }
                    finally { Destroy(pawn); Rand.PopState(); }
                }
            }
            roster.PawnKind = roster.Members[0].PawnKind; roster.ResolvedCount = roster.Members.Count; roster.UnitCombatPower = roster.ActualCombatPower / roster.ResolvedCount;
            var error = SquadTemplates.ValidateRoster(roster, template, budget, count, costMode);
            if (error != null) throw new ScenarioConfigurationException(error);
            return roster;
        }
        private static XElement CombatConfiguration(XElement actor, Faction faction)
        {
            var config = new XElement("PawnTemplate", actor.Elements().Select(e => new XElement(e)));
            var kind = Kind(config.Element("basePawnKind").Value);
            if (config.Element("Xenotype") != null)
            {
                var xeno = Xeno((string)config.Element("Xenotype").Attribute("def"));
                if (!ModsConfig.BiotechActive || !kind.RaceProps.Humanlike || !xeno.canGenerateAsCombatant) throw new ScenarioConfigurationException("Incompatible combat xenotype: " + kind.defName + "/" + xeno.defName);
            }
            else if (ModsConfig.BiotechActive && kind.RaceProps.Humanlike)
            {
                var options = PawnGenerator.XenotypesAvailableFor(kind, faction: faction).Where(x => x.Key.canGenerateAsCombatant).ToList();
                if (!options.TryRandomElementByWeight(x => x.Value, out var selected)) throw new ScenarioConfigurationException("No combat xenotype for " + kind.defName);
                config.Add(new XElement("Xenotype", new XAttribute("def", selected.Key.defName)));
            }
            return config;
        }
        internal static ResolvedCombatRoster ResolveActor(string id, XElement actor, string factionDef, decimal? budget, int count, string costMode, int seed)
        {
            SquadTemplates.ValidateCostMode(costMode);
            using var faction = new FactionScope(factionDef ?? "LogSync_GroupCombatFaction", id);
            var configuration = new XElement("PawnTemplate", actor.Elements().Select(e => new XElement(e)));
            var roster = new ResolvedCombatRoster { Id = id, PawnKind = configuration.Element("basePawnKind").Value, FactionDef = factionDef, CostMode = costMode,
                Mode = budget.HasValue ? "combatPower" : "count", RequestedBudget = budget, GenerationPolicy = "frozen", TemplateXml = actor.ToString(SaveOptions.DisableFormatting) };
            var maximum = budget.HasValue ? CombatRoster.MaxTeamMembers + 1 : count;
            for (var i = 0; i < maximum; i++)
            {
                Pawn pawn = null;
                var memberSeed = SquadTemplates.MemberSeed(seed, id, i); Rand.PushState(memberSeed);
                try
                {
                    var config = CombatConfiguration(configuration, faction.Faction);
                    pawn = ScenarioGameBackend.GenerateTemporaryPawn(TemporaryPawnData.Parse(config.ToString(SaveOptions.DisableFormatting)), faction.Faction, null, out _, forceConfiguredXenotype: true);
                    var captured = XElement.Parse(TemporaryPawnCapture.Capture(pawn).TemplateXml);
                    var error = PawnConfiguration.ValidateExplicit(configuration, captured);
                    if (error != null) throw new ScenarioConfigurationException(id + ": " + error);
                    var power = CombatRoster.Power(Kind(roster.PawnKind).combatPower);
                    var xenotype = pawn.genes?.Xenotype;
                    var factor = xenotype == null ? 1 : CombatRoster.Power(xenotype.combatPowerFactor);
                    var cost = power * (costMode == "native" ? factor : 1);
                    if (budget.HasValue && roster.ActualCombatPower + cost > budget.Value) { roster.NextMemberCost = cost; break; }
                    foreach (var field in new[] { "Hediffs", "Abilities", "Resources" })
                        if (configuration.Element(field) == null && (field != "Abilities" || configuration.Element("abilities") == null)) captured.Element(field)?.Remove();
                    roster.Members.Add(new ResolvedSquadMember { PawnKind = pawn.kindDef.defName, Xenotype = xenotype?.defName, BaseCombatPower = power, XenotypeFactor = factor, Cost = cost,
                        Seed = memberSeed, ConfigurationId = id, ConfigurationXml = configuration.ToString(SaveOptions.DisableFormatting), PawnTemplateXml = captured.ToString(SaveOptions.DisableFormatting),
                        Childhood = pawn.story?.Childhood?.defName, Adulthood = pawn.story?.Adulthood?.defName });
                    if (roster.Members.Count > CombatRoster.MaxTeamMembers)
                    {
                        roster.ResolvedCount = roster.Members.Count; roster.UnitCombatPower = roster.ActualCombatPower / roster.ResolvedCount;
                        var limit = new ScenarioConfigurationException("Roster exceeds 200 members", "COMBAT_ROSTER_LIMIT_EXCEEDED");
                        limit.Data["RejectedRoster"] = roster;
                        throw limit;
                    }
                }
                finally { Destroy(pawn); Rand.PopState(); }
            }
            if (roster.Members.Count == 0) throw new ScenarioConfigurationException("Budget cannot fund the first generated member", "COMBAT_BUDGET_TOO_SMALL");
            roster.ResolvedCount = roster.Members.Count; roster.UnitCombatPower = roster.ActualCombatPower / roster.ResolvedCount;
            return roster;
        }
        internal static Pawn GenerateMember(ResolvedSquadMember member, Faction faction, PlanetTile tile, RaidStrategyDef strategy = null, float points = 5000)
        {
            Rand.PushState(member.Seed);
            try
            {
                var pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(Kind(member.PawnKind), faction, PawnGenerationContext.NonPlayer, tile,
                forceGenerateNewPawn: true, canGeneratePawnRelations: false, mustBeCapableOfViolence: true,
                forcedXenotype: Xeno(member.Xenotype), validatorPostGear: strategy == null ? null : p => strategy.Worker.CanUsePawn(points, p, new List<Pawn>())));
                TestPawnWorkRestrictions.Remove(pawn);
                return pawn;
            }
            finally { Rand.PopState(); }
        }
        internal static void Destroy(Pawn pawn)
        {
            if (pawn == null) return;
            if (Find.WorldPawns.Contains(pawn)) Find.WorldPawns.RemovePawn(pawn);
            pawn.relations?.ClearAllRelations();
            if (!pawn.Destroyed) pawn.Destroy(DestroyMode.Vanish);
        }
        private static TestSquadMemberSpec Spec(PawnGenOptionWithXenotype choice) => new() { PawnKind = choice.Option.kind.defName, Xenotype = choice.Option.kind.RaceProps.Humanlike ? choice.Xenotype?.defName : null };
        private static PawnKindDef Kind(string name) => DefDatabase<PawnKindDef>.GetNamedSilentFail(name) ?? throw new ScenarioConfigurationException("Unknown squad PawnKind: " + name);
        private static XenotypeDef Xeno(string name) => name == null ? null : DefDatabase<XenotypeDef>.GetNamedSilentFail(name) ?? throw new ScenarioConfigurationException("Unknown squad Xenotype: " + name);
        private static float Weight(decimal value) { var f = (float)value; if (!float.IsFinite(f) || f <= 0) throw new ScenarioConfigurationException("Invalid squad selection weight"); return f; }
        private sealed class XenotypeSelectionScope : IDisposable
        {
            [ThreadStatic] internal static XenotypeSelectionScope Current;
            private readonly XenotypeSelectionScope previous;
            internal readonly Faction Faction;
            internal readonly Dictionary<PawnKindDef, Dictionary<XenotypeDef, float>> Distributions = new();
            internal XenotypeSelectionScope(List<PawnGenOption> options, SquadTemplate template, Faction faction, string costMode)
            {
                previous = Current; Faction = faction;
                foreach (var x in template.Xenotypes) { var def = Xeno(x.Def); if (!ModsConfig.BiotechActive || !def.canGenerateAsCombatant) throw new ScenarioConfigurationException("Xenotype is unavailable for combat: " + x.Def); }
                if (ModsConfig.BiotechActive && (template.XenotypeMode != "inherit" || costMode != "native"))
                {
                    foreach (var kind in options.Select(o => o.kind).Distinct())
                    {
                        var distribution = !kind.RaceProps.Humanlike
                            ? new Dictionary<XenotypeDef, double> { [XenotypeDefOf.Baseliner] = 1 }
                            : template.XenotypeMode == "inherit"
                                ? PawnGenerator.XenotypesAvailableFor(kind, faction: faction).ToDictionary(p => p.Key, p => (double)p.Value)
                                : template.Xenotypes.ToDictionary(x => Xeno(x.Def), x => (double)x.Weight);
                        var sum = distribution.Values.Sum();
                        if (!double.IsFinite(sum) || sum <= 0) throw new ScenarioConfigurationException("No valid xenotype weights for " + kind.defName);
                        Distributions.Add(kind, distribution.ToDictionary(p => {
                            var xeno = costMode == "native" ? p.Key : Clone(p.Key);
                            if (costMode == "pawnKind") xeno.combatPowerFactor = 1;
                            return xeno;
                        }, p => (float)(p.Value / sum)));
                    }
                }
                Current = this;
            }
            public void Dispose() => Current = previous;
        }
        [HarmonyPatch(typeof(PawnGenerator), nameof(PawnGenerator.XenotypesAvailableFor))]
        private static class SquadSelectionXenotypesPatch
        {
            private static void Postfix(PawnKindDef kind, Faction faction, ref Dictionary<XenotypeDef, float> __result)
            {
                var scope = XenotypeSelectionScope.Current;
                if (scope != null && ReferenceEquals(scope.Faction, faction) && scope.Distributions.TryGetValue(kind, out var weights))
                    __result = new Dictionary<XenotypeDef, float>(weights);
            }
        }
        internal static string Fingerprint(PawnGroupMaker maker)
        {
            var xml = new XElement("GroupMaker", new XAttribute("kindDef", maker.kindDef.defName), new XAttribute("commonality", maker.commonality.ToString("R", CultureInfo.InvariantCulture)), new XAttribute("maxTotalPoints", maker.maxTotalPoints.ToString("R", CultureInfo.InvariantCulture)),
                new XElement("DisallowedStrategies", (maker.disallowedStrategies ?? new List<RaidStrategyDef>()).Select(d => new XElement("Def", d.defName))),
                new XElement("Options", maker.options.Select(o => new XElement("Option", new XAttribute("pawnKind", o.kind.defName), new XAttribute("weight", o.selectionWeight.ToString("R", CultureInfo.InvariantCulture))))));
            using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(xml.ToString(SaveOptions.DisableFormatting)))).Replace("-", "").ToLowerInvariant();
        }
    }
}
