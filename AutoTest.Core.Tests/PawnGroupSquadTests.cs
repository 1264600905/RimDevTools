using System;
using System.Linq;
using System.Xml.Linq;
using LogSyncMod.AutoTest.Scenarios;

internal static class PawnGroupSquadTests
{
    internal static void Run(Action<bool, string> check)
    {
        const string templates = """
            <Templates>
              <PawnTemplate id="base"><basePawnKind>Colonist</basePawnKind><Traits mode="replace"/><Skills><Set def="Shooting" level="10"/></Skills><Xenotype def="Baseliner"/><Apparel mode="replace"><Add def="Apparel_ArmorCataphract" quality="Normal"/></Apparel></PawnTemplate>
              <PawnTemplate id="gunner" extends="base"><Equipment mode="replace"><SetPrimary def="Gun_Minigun" quality="Normal"/></Equipment></PawnTemplate>
              <PawnTemplate id="swordsman" extends="base"><Skills><Set def="Melee" level="10"/></Skills><Equipment mode="replace"><SetPrimary def="MeleeWeapon_MonoSword" quality="Normal"/></Equipment></PawnTemplate>
            </Templates>
            """;
        const string squad = """
            <SquadTemplates><SquadTemplate id="bossTeam" source="groups" factionDef="Pirate">
              <PawnGroup id="gunners" template="gunner" count="15"/>
              <PawnGroup id="swordsmen" template="swordsman" count="5"/>
            </SquadTemplate></SquadTemplates>
            """;
        const string shared = """
            <Requirements><DLC packageId="ludeon.rimworld.biotech"/></Requirements>
            <ScenePresets><Scene id="studio" sizeX="120" sizeZ="130"><Environment weather="Clear" temperature="20"/></Scene></ScenePresets>
            """;
        string Arena(string format) => "<ArenaSuite schemaVersion='1' id='groups'>" + templates + squad + shared
            + "<Tournament id='main' format='" + format + "'><testDefName>GroupTemplates</testDefName><Scene preset='studio'><Environment temperature='25'/></Scene>"
            + "<Roster mode='combatPower' budget='5000' costMode='native'/><Entrants><Entrant id='boss' squad='bossTeam' mode='count'"
            + (format == "knockout" ? "" : " role='boss'") + "/><Entrant id='challenger' pawnKind='Wolf_Timber'"
            + (format == "knockout" ? "" : " role='challenger'") + "/></Entrants><Battle aiPolicy='defendPoint' timeSpeed='normal'/>"
            + "<Metrics detailLimit='123' requireCompleteAttribution='true'/><Overlay enabled='false'/>"
            + (format == "limitChallenge" ? "<Growth target='budget' mode='add' increment='500' maxValue='6000'/>" : "")
            + "</Tournament></ArenaSuite>";
        ArenaPlan Parse(string xml) => ArenaLoader.Compile(new[] { new ScenarioInput { Path = "groups.xml", Sha256 = "input", Xml = xml } }, new[] { "GroupTemplates" });
        void Reject(string xml, string message)
        {
            try { Parse(xml); throw new InvalidOperationException("Accepted invalid groups: " + message); }
            catch (ScenarioConfigurationException) { check(true, message); }
        }
        foreach (var format in new[] { "knockout", "bossRoundRobin", "limitChallenge" })
        {
            var tournament = Parse(Arena(format)).Tournaments.Single();
            var groupTemplate = tournament.SquadTemplates["bossTeam"];
            var groups = groupTemplate.PawnGroups.Select(XElement.Parse).ToList();
            check(groups.Select(g => ScenarioLoader.I(g, "count")).SequenceEqual(new[] { 15, 5 })
                && groups.All(g => g.Element("basePawnKind").Value == "Colonist" && g.Attribute("template") == null)
                && (string)groups[0].Element("Equipment").Element("SetPrimary").Attribute("def") == "Gun_Minigun"
                && (string)groups[1].Element("Equipment").Element("SetPrimary").Attribute("def") == "MeleeWeapon_MonoSword",
                format + " composes inherited templates of the same PawnKind as distinct 15+5 groups");
            check(tournament.Entrants[0].Count == 20 && ArenaChallengeRules.Budget(tournament, tournament.Entrants[0]) == null
                && ArenaChallengeRules.Budget(tournament, tournament.Entrants[1]) == 5000,
                format + " keeps a fixed twenty-member boss independent of the challenger CP budget");
            var recompiled = Parse(Arena(format)).Tournaments.Single();
            check(recompiled.SquadTemplates["bossTeam"].Xml == groupTemplate.Xml
                && SquadTemplates.Parse(XElement.Parse(groupTemplate.Xml), "frozen.xml").PawnGroups.SequenceEqual(groupTemplate.PawnGroups),
                format + " freezes resolved template configuration without a second template lookup");
            var battle = XElement.Parse(ArenaLoader.CompileBattle(tournament,
                new() { CombatRoster.Resolve("boss", "Colonist", 100, null, 20), CombatRoster.Resolve("challenger", "Wolf_Timber", 75, 5000) }, null, 0).Xml);
            check((string)battle.Element("Scene").Attribute("sizeX") == "120" && (string)battle.Element("Scene").Attribute("sizeZ") == "130"
                && (string)battle.Element("Scene").Element("Environment").Attribute("weather") == "Clear"
                && (string)battle.Element("Scene").Element("Environment").Attribute("temperature") == "25"
                && battle.Element("Requirements").Elements().Any(e => (string)e.Attribute("packageId") == "ludeon.rimworld.biotech")
                && (string)battle.Element("GroupCombat").Attribute("aiPolicy") == "defendPoint"
                && (string)battle.Element("GroupCombat").Element("Metrics").Attribute("detailLimit") == "123"
                && (string)battle.Element("GroupCombat").Element("Overlay").Attribute("enabled") == "false",
                format + " carries shared scene presets, dependencies, native AI, metrics and overlay through the existing battle compiler");
        }
        var groupSuite = XElement.Parse("<TestSuite schemaVersion='1' id='group'>" + templates + squad + """
            <Case id="mixed" kind="groupCombat"><testDefName>Mixed</testDefName><Scene sizeX="100" sizeZ="100"/>
              <Teams><Team id="A"/><Team id="B"/></Teams><Actors>
                <PawnGroup id="mixed" squad="bossTeam" count="20" team="A" originX="10" originZ="10" formation="grid" columns="5"/>
                <PawnGroup id="other" template="gunner" count="2" team="B" originX="70" originZ="10"/>
              </Actors><GroupCombat/><Steps/>
            </Case></TestSuite>
            """);
        var groupPlan = ScenarioLoader.Compile(new[] { new ScenarioInput { Path = "group.xml", Xml = groupSuite.ToString() } }, true);
        var frozenGroup = XElement.Parse(groupPlan.Cases.Single().Xml).Element("Actors").Elements().First();
        check(frozenGroup.Element("SquadTemplate").Elements("PawnGroup").Count() == 2,
            "standalone group combat shares the new composite groups while ordinary PawnGroup template generation remains available");
        Reject(Arena("bossRoundRobin").Replace("template=\"gunner\"", "template=\"missing\""), "unknown member templates fail statically");
        Reject(Arena("bossRoundRobin").Replace("extends=\"base\"", "extends=\"gunner\""), "member template inheritance cycles fail statically");
        Reject(Arena("bossRoundRobin").Replace("id=\"swordsmen\"", "id=\"gunners\""), "duplicate composite group identities fail statically");
        Reject(Arena("bossRoundRobin").Replace("count=\"15\"", "count=\"201\""), "oversized composite groups fail statically");
        Reject(Arena("bossRoundRobin").Replace("mode='count'", "mode='count' count='19'"), "explicit entrant totals must equal the constituent groups");
        Reject(Arena("bossRoundRobin").Replace("source=\"groups\"", "source=\"custom\""), "existing native squad sources do not silently accept group-template configuration");
        Reject(Arena("limitChallenge").Replace("id='challenger' pawnKind", "id='challenger' mode='count' pawnKind"), "growth must match the challenger's own roster mode");
        Reject(Arena("bossRoundRobin").Replace("aiPolicy='defendPoint'", "aiPolicy='unknown'"), "new arena AI entry uses the shared native AI validation");
        Reject(Arena("bossRoundRobin").Replace("detailLimit='123'", "detailLimit='-1'"), "new arena metrics entry uses the shared bounds validation");
        check(Parse(Arena("bossRoundRobin").Replace("id=\"gunners\"", "id=\"__templateOpponent\"")).Tournaments.Single().SquadTemplates["bossTeam"].PawnGroups.Count == 2,
            "user group identities cannot collide with internal template compilation actors");
        var sharedActions = XElement.Parse(Arena("bossRoundRobin"));
        sharedActions.Add(new XElement("ActionBindings", new XElement("ActionBinding", new XAttribute("id", "mod.prepare"), new XAttribute("packageId", "mod.test"),
            new XAttribute("type", "Test.Api"), new XAttribute("method", "Apply"), new XElement("Arguments", new XElement("Argument", new XAttribute("type", "Verse.Pawn"), new XAttribute("source", "selected"))), new XElement("Result", new XAttribute("equals", "true")))));
        sharedActions.Element("Tournament").Add(XElement.Parse("<Preparation><DebugAction id='prepare' actionId='mod.prepare'><Select mode='single' actor='entrant'/></DebugAction></Preparation>"));
        var sharedInput = Parse(sharedActions.ToString()).Tournaments.Single();
        var previewPreparation = ArenaLoader.PreparationCase(sharedInput);
        var preparedBattle = XElement.Parse(ArenaLoader.CompileBattle(sharedInput, new() { CombatRoster.Resolve("boss", "Colonist", 100, null, 20), CombatRoster.Resolve("challenger", "Wolf_Timber", 75, 5000) }, null, 0).Xml);
        string BindingSignature(XElement e) => string.Join("|", e.DescendantsAndSelf().Select(n => n.Name + ":" + string.Join(",", n.Attributes().OrderBy(a => a.Name.LocalName).Select(a => a.Name + "=" + a.Value))));
        check(BindingSignature(previewPreparation.Element("ActionBindings")) == BindingSignature(preparedBattle.Element("ActionBindings"))
            && previewPreparation.Element("Requirements").Elements().Any(e => (string)e.Attribute("packageId") == "mod.test"),
            "suite-level action bindings reach preview preparation and battles with the same method and dependency");

        var source = SquadTemplates.ParseAll(XElement.Parse("<ArenaSuite>" + templates + squad + "</ArenaSuite>"), "source.xml")["bossTeam"];
        var members = source.PawnGroups.SelectMany(xml => {
            var group = XElement.Parse(xml);
            return Enumerable.Range(0, ScenarioLoader.I(group, "count")).Select(_ => new ResolvedSquadMember {
                PawnKind = "Colonist", Xenotype = "Baseliner", BaseCombatPower = 100, Cost = 100,
                ConfigurationId = ScenarioLoader.A(group, "id"), ConfigurationXml = SquadTemplates.ConfigurationXml(group),
                PawnTemplateXml = SquadTemplates.ConfigurationXml(group)
            });
        }).ToList();
        var roster = new ResolvedCombatRoster { Id = "boss", SquadId = source.Id, FactionDef = source.FactionDef, Mode = "count", ResolvedCount = 20, PawnKind = "Colonist", UnitCombatPower = 100, Members = members };
        var units = members.Select((m, i) => new GroupCombatUnit { Id = "left[" + i + "]", Kind = m.PawnKind, ConfigurationId = m.ConfigurationId, ConfigurationKey = SquadTemplates.ConfigurationKey(m), GeneratedSnapshot = m.PawnTemplateXml, InitialSnapshot = m.PawnTemplateXml }).ToList();
        check(SquadTemplates.ValidateRoster(roster, source, null, 20, "pawnKind") == null
            && SquadTemplates.ValidateUnits(roster, units, "left") == null, "composite initial units validate against their distinct frozen configurations");
        units[0].GeneratedSnapshot = units[0].GeneratedSnapshot.Replace("level=\"10\"", "level=\"20\"");
        check(SquadTemplates.ValidateUnits(roster, units, "left") != null, "composite member skill drift fails initial-state validation");
        members[0].ConfigurationId = "swordsmen";
        check(SquadTemplates.ValidateRoster(roster, source, null, 20, "pawnKind") != null, "changing a composite member's configuration identity fails roster validation");
    }
}
