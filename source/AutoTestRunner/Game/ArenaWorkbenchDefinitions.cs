using System;
using System.Linq;
using System.Xml.Linq;
using LogSyncMod.AutoTest.Scenarios;
using RimWorld;
using Verse;

namespace LogSyncMod.AutoTest
{
    internal static class ArenaWorkbenchDefinitions
    {
        internal static void ValidatePawn(XElement xml)
        {
            TemporaryPawnData.Create(xml.Name == "PawnTemplate" ? xml : new XElement("PawnTemplate", new XAttribute("id", "validate"), xml.Elements().Select(e => new XElement(e))), "validation", Array.Empty<string>(), Array.Empty<string>());
            var capabilities = ArenaPawnCapabilities.For(xml);
            if (capabilities.Race?.race == null || capabilities.Kind == null) throw new ScenarioConfigurationException("角色兵种或种族未加载。");
            if (!ArenaBattleGroupCatalog.LivingKind(capabilities.Kind) || capabilities.Race.IsCorpse) throw new ScenarioConfigurationException("尸体或无效兵种不能作为参赛单位。");
            var unsupported = capabilities.Unsupported(xml);
            if (unsupported.Count > 0) throw new ScenarioConfigurationException(capabilities.Kind.LabelCap + " 不支持这些配置：" + string.Join("、", unsupported));
            foreach (var trait in xml.Element("Traits")?.Elements("Set") ?? Enumerable.Empty<XElement>())
                if (!Need<TraitDef>((string)trait.Attribute("def")).degreeDatas.Any(d => d.degree == ((int?)trait.Attribute("degree") ?? 0)))
                    throw new ScenarioConfigurationException("特性档位无效：" + (string)trait.Attribute("def"));
            foreach (var skill in xml.Element("Skills")?.Elements("Set") ?? Enumerable.Empty<XElement>())
            {
                var level = (int?)skill.Attribute("level") ?? 0;
                if (level < 0 || level > 20 || skill.Attribute("passion") != null && !Enum.TryParse<Passion>((string)skill.Attribute("passion"), out _))
                    throw new ScenarioConfigurationException("技能等级或热情无效：" + (string)skill.Attribute("def"));
            }
            var explicitApparel = new System.Collections.Generic.List<ThingDef>();
            foreach (var item in (xml.Element("Apparel")?.Elements() ?? Enumerable.Empty<XElement>()).Concat(xml.Element("Equipment")?.Elements() ?? Enumerable.Empty<XElement>()))
            {
                var name = (string)item.Attribute("def") ?? (string)item.Attribute("withDef"); if (name == null || item.Name == "Remove") continue;
                var def = Need<ThingDef>(name);
                if (item.Parent.Name == "Equipment" && !def.IsWeapon || item.Parent.Name == "Apparel" && !def.IsApparel) throw new ScenarioConfigurationException("物品类型与装备槽不符：" + name);
                if (item.Attribute("stuff") != null)
                {
                    var stuff = Need<ThingDef>((string)item.Attribute("stuff"));
                    if (!def.MadeFromStuff || stuff.stuffProps == null || !stuff.stuffProps.categories.Any(c => def.stuffCategories.Contains(c)))
                        throw new ScenarioConfigurationException("材质不适用于该物品：" + name);
                }
                if (item.Attribute("quality") != null && (!Enum.TryParse<QualityCategory>((string)item.Attribute("quality"), out var quality) || !Enum.IsDefined(quality)))
                    throw new ScenarioConfigurationException("物品品质无效：" + name);
                if (item.Parent.Name == "Apparel" && item.Name == "Add")
                {
                    if (explicitApparel.Any(old => !ApparelUtility.CanWearTogether(old, def, capabilities.Race.race.body)))
                        throw new ScenarioConfigurationException("指定的服装无法同时穿戴：" + name);
                    explicitApparel.Add(def);
                }
            }
            foreach (var h in xml.Element("Hediffs")?.Elements("Ensure") ?? Enumerable.Empty<XElement>())
                if (h.Attribute("severity") != null && (!double.TryParse((string)h.Attribute("severity"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var severity) || !double.IsFinite(severity) || severity < 0)
                    || h.Attribute("durationTicks") != null && (!int.TryParse((string)h.Attribute("durationTicks"), out var duration) || duration < 1))
                    throw new ScenarioConfigurationException("Hediff 严重度或持续时间无效：" + (string)h.Attribute("def"));
            var pawn = new XElement(xml); pawn.Name = "Pawn"; pawn.SetAttributeValue("id", "preview");
            var root = new XElement("Case", new XElement("Actors", pawn));
            ScenarioGameBackend.ValidateDefs(root, new ScenarioCase { Source = "群战工作台" });
        }
        internal static void Validate(ArenaPlan plan)
        {
            foreach (var input in plan.Tournaments)
            {
                var xml = XElement.Parse(input.Xml);
                var env = xml.Element("Scene")?.Element("Environment");
                if (env?.Attribute("weather") != null) Need<WeatherDef>((string)env.Attribute("weather"));
                if (env?.Attribute("roof") != null && (string)env.Attribute("roof") != "none") Need<RoofDef>((string)env.Attribute("roof"));
                if ((string)env?.Attribute("naturalSun") == "true" && env?.Attribute("weather") != null
                    && Need<WeatherDef>((string)env.Attribute("weather")).maxGlow < .99f)
                    throw new ScenarioConfigurationException("所选天气不满足自然光要求，请更换晴朗天气或取消自然光要求。");
                foreach (var entrant in input.Entrants)
                {
                    if (entrant.Xenotype != null) Need<XenotypeDef>(entrant.Xenotype);
                    if (entrant.PawnKind != null) Need<PawnKindDef>(entrant.PawnKind);
                    if (entrant.Squad == null) continue;
                    var squad = input.SquadTemplates[entrant.Squad];
                    var faction = Need<FactionDef>(squad.FactionDef);
                    if (squad.Source == "faction" && squad.MakerIndex.HasValue
                        && (squad.MakerIndex.Value < 0 || squad.MakerIndex.Value >= (faction.pawnGroupMakers?.Count ?? 0)
                            || faction.pawnGroupMakers[squad.MakerIndex.Value].kindDef?.defName != "Combat"))
                        throw new ScenarioConfigurationException("所选战斗组已不可用，请重新选择派系战斗组。");
                    if (squad.Source == "faction")
                    {
                        using var scope = new SquadGeneration.FactionScope(squad.FactionDef, "战斗组校验");
                        var points = ArenaChallengeRules.Budget(input, entrant) is decimal budget ? (float)budget : squad.ReferencePoints;
                        var parms = new PawnGroupMakerParms { faction = scope.Faction, groupKind = PawnGroupKindDefOf.Combat, points = points,
                            tile = Find.CurrentMap.Tile, raidStrategy = Need<RaidStrategyDef>(squad.StrategyDef), generateFightersOnly = true };
                        var candidates = faction.pawnGroupMakers?.Where((m, i) => m.kindDef == PawnGroupKindDefOf.Combat && (!squad.MakerIndex.HasValue || squad.MakerIndex.Value == i)).ToList();
                        if (candidates == null || !candidates.Any(m => ArenaBattleGroupCatalog.Options(m).Any() && m.CanGenerateFrom(parms)))
                            throw new ScenarioConfigurationException((entrant.Name ?? entrant.Id) + "：战斗组在 " + points + " 参考点数 / CP 与当前策略下不可用，请调整点数或更换战斗组。");
                    }
                    foreach (var group in squad.PawnGroups) ValidatePawn(XElement.Parse(group));
                }
            }
        }
        private static T Need<T>(string name) where T : Def, new() => DefDatabase<T>.GetNamedSilentFail(name)
            ?? throw new ScenarioConfigurationException("当前未加载 " + typeof(T).Name + "：" + name);
    }
}
