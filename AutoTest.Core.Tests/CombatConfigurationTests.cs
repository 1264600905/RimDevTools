using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;
using LogSyncMod.AutoTest.Scenarios;

internal static class CombatConfigurationTests
{
    internal static void Run(Action<bool, string> check)
    {
        var config = XElement.Parse("""
            <PawnTemplate><basePawnKind>Colonist</basePawnKind><gender>Male</gender>
            <Age biologicalYears="25" chronologicalYears="30"/><Traits mode="replace"/>
            <Skills mode="patch"><Set def="Shooting" level="10" passion="None"/></Skills>
            <Xenotype def="Baseliner"/><Genes mode="replace"><Add def="Hemogenic" xenogene="false"/></Genes>
            <Apparel mode="replace"><Add def="Apparel_ArmorCataphract" quality="Normal"/></Apparel>
            <Equipment mode="replace"><SetPrimary def="Gun_Autopistol" quality="Normal"/></Equipment>
            <Hediffs mode="replace"><Ensure def="FoodPoisoning" severity="0.3" durationTicks="600"/></Hediffs>
            <Abilities mode="replace"><Ensure def="Stun"/></Abilities><Resources><GeneResource def="Hemogenic" value="0.7"/></Resources>
            </PawnTemplate>
            """);
        check(PawnConfiguration.ValidateExplicit(config, config) == null, "all public explicit pawn fields reconcile with their captured values");
        foreach (var field in new[] { "Age", "Traits", "Skills", "Genes", "Apparel", "Equipment", "Hediffs", "Abilities", "Resources" })
        {
            var changed = new XElement(config); changed.Element(field).Remove();
            check(PawnConfiguration.ValidateExplicit(config, changed) != null, "missing explicit pawn field is rejected: " + field);
            check(PawnConfiguration.ValidateFrozen(config, changed) != null, "frozen pawn field cannot disappear: " + field);
        }
        var reordered = new XElement(config.Name, config.Elements().Reverse());
        check(PawnConfiguration.Equal(config, reordered), "snapshot collection ordering does not change configuration identity");
        check(PawnConfiguration.Condition(XElement.Parse("<CorpseState actor='extraCorpse' state='spawned'/>"), new Dictionary<string, XElement>()) == null,
            "native-only assertions on extra scene actors are not falsely rejected by participant snapshot validation");
        var member1 = new ResolvedSquadMember { PawnKind = "Colonist", ConfigurationId = "gunners", ConfigurationXml = config.ToString(), PawnTemplateXml = config.ToString() };
        var variant = new XElement(config); variant.Element("Skills").Element("Set").SetAttributeValue("level", "20");
        var member2 = new ResolvedSquadMember { PawnKind = "Colonist", ConfigurationId = "gunners", ConfigurationXml = variant.ToString(), PawnTemplateXml = variant.ToString() };
        check(SquadTemplates.ConfigurationKey(member1) != SquadTemplates.ConfigurationKey(member2), "equal local group IDs with distinct skill configurations retain distinct identities");
        var colored = new XElement(config); colored.Element("Equipment").Element("SetPrimary").SetAttributeValue("color", "#FF0000FF");
        check(SquadTemplates.ConfigurationKey(member1) == SquadTemplates.ConfigurationKey(member1, colored), "random generated gear colors do not fragment a configured unit type");
        check(PawnConfiguration.Equal(XElement.Parse("<hairColor>#000000</hairColor>"), XElement.Parse("<hairColor>#000000FF</hairColor>")), "RGB and RGBA spellings compare as the same opaque color");
        var wrong = new XElement(config); wrong.Element("Resources").Element("GeneResource").SetAttributeValue("value", ".4");
        check(PawnConfiguration.ValidateExplicit(config, wrong) != null, "resource drift is independently rejected");
        wrong = new XElement(config); wrong.Element("Age").SetAttributeValue("biologicalYears", "26");
        check(PawnConfiguration.ValidateExplicit(config, wrong) != null, "age drift is independently rejected");
        var equipped = new XElement(config); equipped.Element("Equipment").Element("SetPrimary").SetAttributeValue("def", "MeleeWeapon_MonoSword");
        var equip = XElement.Parse("<Equip id='equip' actor='left[0]' def='MeleeWeapon_MonoSword'/>");
        check(CombatPreparation.ValidateChange(equip, config, equipped) == null, "declared synchronous preparation can change a frozen loadout");
        wrong = new XElement(equipped); wrong.Element("Skills").Element("Set").SetAttributeValue("level", "20");
        check(CombatPreparation.ValidateChange(equip, config, wrong) != null, "equipment preparation cannot silently change skills");
        var round = new GroupCombatRoundResult {
            Units = new() { new GroupCombatUnit { Id = "left[0]", Kind = "Colonist", GeneratedSnapshot = config.ToString(), InitialSnapshot = equipped.ToString() } },
            Preparation = new() { new CombatPreparationRecord { StepId = "equip", Completed = true, Snapshots = new() { ["left[0]"] = equipped.ToString() } } }
        };
        var battle = new XElement("Case", new XElement("Steps", equip));
        check(CombatPreparation.Validate(round, battle) == null, "generation and post-preparation snapshots form a validated lifecycle");
        var copy = JsonSerializer.Deserialize<GroupCombatRoundResult>(JsonSerializer.Serialize(round)); copy.Preparation.Clear();
        check(CombatPreparation.Validate(copy, battle) != null, "omitting preparation evidence cannot bypass validation");
        copy = JsonSerializer.Deserialize<GroupCombatRoundResult>(JsonSerializer.Serialize(round)); copy.Units[0].GeneratedSnapshot = null;
        check(CombatPreparation.Validate(copy, battle) != null, "omitting the generated snapshot cannot bypass validation");
        copy = JsonSerializer.Deserialize<GroupCombatRoundResult>(JsonSerializer.Serialize(round)); copy.Preparation[0].Snapshots["other[0]"] = equipped.ToString();
        check(CombatPreparation.Validate(copy, battle) != null, "preparation evidence cannot substitute a different target");
        var require = XElement.Parse("<Require id='skills'><SkillLevel actor='left[0]' def='Shooting' min='20'/></Require>");
        round.Preparation.Add(new CombatPreparationRecord { StepId = "skills", Completed = true, Snapshots = new() { ["left[0]"] = equipped.ToString() } });
        battle.Element("Steps").Add(require);
        check(CombatPreparation.Validate(round, battle) != null, "a claimed successful preparation assertion is independently checked against its snapshot");
        var fixture = "AutoTest.Core.Tests/Fixtures/ArenaConfigurationAlignment.autotest.xml";
        var input = ArenaLoader.Load(fixture, new[] { "Alignment_GroupsKnockout" }).Tournaments.Single();
        var groupRoster = new ResolvedCombatRoster { Id = "knights", PawnKind = "Colonist", ResolvedCount = 3, Members = new() {
            new() { ConfigurationId = "gunners" }, new() { ConfigurationId = "gunners" }, new() { ConfigurationId = "swordsmen" } } };
        var ratRoster = CombatRoster.Resolve("rat", "Rat", 10, null, 1);
        var scoped = XElement.Parse(ArenaLoader.CompileBattle(input, new() { groupRoster, ratRoster }, null, 0).Xml).Element("Steps").Elements().ToList();
        check(scoped.Count == 7 && scoped.Where(e => e.Name == "Equip").All(e => ((string)e.Attribute("actor")).StartsWith("left[", StringComparison.Ordinal))
            && !scoped.Where(e => e.Name == "Equip").Any(e => (string)e.Attribute("actor") == "left[2]"), "entrant and configuration selectors expand only onto the selected frozen members");
        check(!ArenaLoader.PreparationSteps(input, "entrant", "rat").HasElements, "unselected entrants receive no preparation actions");
        var rectangle = XElement.Parse("<DebugAction><Select mode='rectangle' filter='enemies' relativeTo='left[0]' minX='0' minZ='0' maxX='30' maxZ='30'/></DebugAction>");
        var teams = XElement.Parse("<Case><Actors><PawnGroup id='left' team='A'/><PawnGroup id='right' team='B'/></Actors></Case>");
        check(!CombatPreparation.Targets(rectangle, "left[0]", 10, 10, teams) && CombatPreparation.Targets(rectangle, "right[0]", 10, 10, teams), "preparation evidence applies rectangle enemy filters before selecting actors");
        foreach (var replacement in new[] { ("entrant=\"knights\"", "entrant=\"missing\""), ("group=\"gunners\"", "group=\"missing\"") })
        {
            var xml = System.IO.File.ReadAllText(fixture).Replace(replacement.Item1, replacement.Item2);
            try { ArenaLoader.Compile(new[] { new ScenarioInput { Path = fixture, Xml = xml } }, new[] { "Alignment_GroupsKnockout" }); check(false, "unknown preparation selector was accepted"); }
            catch (ScenarioConfigurationException) { check(true, "unknown preparation entrant/group fails static compilation"); }
        }
    }
}
