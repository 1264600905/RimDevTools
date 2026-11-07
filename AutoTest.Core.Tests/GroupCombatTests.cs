using System.Xml.Linq;
using LogSyncMod.AutoTest.Scenarios;

internal static class GroupCombatTests
{
    internal static string Xml => """
        <TestSuite schemaVersion="1" id="numeric" gameVersion="1.7">
          <Templates><PawnTemplate id="human"><basePawnKind>Colonist</basePawnKind></PawnTemplate></Templates>
          <ScenePresets><Scene id="arena" type="mainMap" sizeX="100" sizeZ="80"><Environment weather="Clear" naturalSun="true" /></Scene></ScenePresets>
          <Case id="human-groups" kind="groupCombat" name="人类群战">
            <testDefName>Benchmark_HumanGroups</testDefName><Scene preset="arena" />
            <Teams><Team id="A" name="甲" /><Team id="B" name="乙" /></Teams>
            <Actors>
              <PawnGroup id="riflemen" template="human" team="A" count="20" originX="10" originZ="20" formation="grid" columns="4" spacing="3" />
              <PawnGroup id="smgmen" team="B" count="20" originX="75" originZ="20" formation="grid" columns="4" spacing="3"><basePawnKind>Colonist</basePawnKind></PawnGroup>
            </Actors>
            <GroupCombat><Experiments swapSides="true"><Seed value="11" /><Seed value="22" /></Experiments></GroupCombat>
          </Case>
        </TestSuite>
        """;

    public static void Run(Action<bool, string> assert)
    {
        ScenarioPlan Compile(string xml, bool group = true) => ScenarioLoader.Compile(new[] { new ScenarioInput { Path = "fixture.autotest.xml", Sha256 = "frozen", Xml = xml } }, group);
        void Reject(string xml, string message, bool group = true)
        {
            try { Compile(xml, group); assert(false, message); }
            catch (ScenarioConfigurationException) { assert(true, message); }
        }
        var plan = Compile(Xml);
        assert(plan.Purpose == "groupcombat" && plan.Cases.Single().CaseType == "GroupCombat", "group compiler reuses expanded templates and scene presets with independent benchmark purpose");
        Reject(Xml, "ordinary regression entry refuses group rules even during whole-directory selection", false);
        try { GroupCombatPlan.Expand(plan); assert(false, "group named selection required"); } catch (ScenarioConfigurationException) { assert(true, "group named selection required"); }
        ScenarioLoader.SelectRules(plan, new[] { "Benchmark_HumanGroups" });
        var rounds = GroupCombatPlan.Expand(plan);
        assert(rounds.Count == 4 && rounds.Select(r => r.CaseId).Distinct().Count() == 4 && rounds.Select(r => r.Seed).SequenceEqual(new[] { 11, 11, 22, 22 }), "explicit seeds and side exchange expand unique round identities without renaming the rule");
        assert((int)XElement.Parse(rounds[1].Xml).Element("Actors").Elements().First().Attribute("originX") == 80, "side exchange reflects actual grid extent and preserves valid spawn coordinates");
        var byId = Compile(Xml); ScenarioLoader.SelectRules(byId, new[] { byId.Cases.Single().CaseId });
        assert(byId.Cases.Count == 1, "full stable case identity can explicitly select a benchmark");
        Reject(Xml.Replace("<Team id=\"B\" name=\"乙\" />", "<Team id=\"B\"/><Team id=\"C\"/><Team id=\"D\"/>"), "group compiler rejects a fourth team");
        Reject(Xml.Replace("<GroupCombat>", "<Coverage/><GroupCombat>"), "group compiler rejects regression coverage");
        Reject(Xml.Replace("<GroupCombat>", "<GroupCombat durationSeconds=\"0\">"), "group compiler rejects zero battle duration");
        Reject(Xml.Replace("<GroupCombat>", "<GroupCombat aiPolicy=\"magic\">"), "group compiler rejects unknown AI rather than silently substituting one");
        Reject(Xml.Replace("<GroupCombat>", "<GroupCombat><Metrics includeZeroValues=\"false\"/>"), "group compiler preserves initial roster including zero-output members");
        Reject(Xml.Replace("originZ=\"20\"", "originZ=\"75\""), "group grid footprint validates both map axes");
        Reject(Xml.Replace("<Team id=\"A\" name=\"甲\" />", "<Team id=\"A\" factionDef=\"player\"/>"), "group compiler forbids borrowing player factions");
        var duplicate = XElement.Parse(Xml); var second = new XElement(duplicate.Element("Case")); second.SetAttributeValue("id", "other"); duplicate.Add(second);
        Reject(duplicate.ToString(), "group testDefName remains unique across benchmark definitions");
        second.Element("testDefName").Value = "Benchmark_AnotherRule";
        duplicate.Elements("Case").Last().ReplaceWith(second);
        var ambiguous = Compile(duplicate.ToString());
        try { ScenarioLoader.SelectRules(ambiguous, new[] { "人类群战" }); assert(false, "ambiguous benchmark display names rejected"); }
        catch (ScenarioConfigurationException) { assert(true, "ambiguous benchmark display names rejected"); }
        ScenarioLoader.SelectRules(ambiguous, new[] { "Benchmark_AnotherRule" });
        assert(ambiguous.Cases.Single().TestDefName == "Benchmark_AnotherRule", "same configuration and display name can define separately selected stable benchmark rules");
        var independentAi = Compile(Xml.Replace("<Team id=\"A\" name=\"甲\" />", "<Team id=\"A\" name=\"甲\" ai=\"assaultColony\"/>").Replace("<Team id=\"B\" name=\"乙\" />", "<Team id=\"B\" name=\"乙\" ai=\"defendPoint\" defendX=\"80\" defendZ=\"30\" defendRadius=\"20\"/>"));
        var aiTeams = XElement.Parse(independentAi.Cases[0].Xml).Element("Teams").Elements("Team").ToList();
        assert((string)aiTeams[0].Attribute("ai") == "assaultColony" && (string)aiTeams[1].Attribute("ai") == "defendPoint", "each team independently freezes an explicit supported group AI and its native defense parameters");
        Reject(Xml.Replace("<Team id=\"A\" name=\"甲\" />", "<Team id=\"A\" ai=\"Arbitrary.Class\"/>"), "unknown group AI class cannot become an arbitrary reflection entry");
        Reject(Xml.Replace("<Team id=\"A\" name=\"甲\" />", "<Team id=\"A\" ai=\"assaultThings\" defendRadius=\"20\"/>"), "unused defense settings do not silently pass under an assault AI");
        var matrixRoot = XElement.Parse(Xml); matrixRoot.Element("Case").Add(new XElement("Matrix", new XElement("Row", new XAttribute("id", "first")), new XElement("Row", new XAttribute("id", "second"))));
        var matrixPlan = Compile(matrixRoot.ToString()); var exactId = matrixPlan.Cases.Last().CaseId; ScenarioLoader.SelectRules(matrixPlan, new[] { exactId });
        assert(matrixPlan.Cases.Count == 1 && matrixPlan.SelectedCaseIds.Single() == exactId, "explicit complete stable ID selects one benchmark matrix instance rather than the entire rule");
        var groupingA = XElement.Parse("<PawnGroup id='front' count='5' originX='10'><basePawnKind>Colonist</basePawnKind><Equipment mode='replace'><SetPrimary def='Gun_Revolver'/></Equipment></PawnGroup>");
        var groupingB = new XElement(groupingA); groupingB.SetAttributeValue("id", "rear"); groupingB.SetAttributeValue("count", 10); groupingB.SetAttributeValue("originX", 70);
        assert(GroupCombatPlan.DisplayGroupKey(groupingA) == GroupCombatPlan.DisplayGroupKey(groupingB), "roster display merges the same PawnKind/template across formations without splitting random hair or spawn locations");
        groupingB.Element("Equipment").Element("SetPrimary").SetAttributeValue("def", "Gun_AssaultRifle");
        assert(GroupCombatPlan.DisplayGroupKey(groupingA) != GroupCombatPlan.DisplayGroupKey(groupingB), "explicit loadout differences distinguish display groups of the same PawnKind");
        Reject(Xml.Replace("<GroupCombat>", "<Output width='400' height='300'/><GroupCombat>"), "benchmark output rejects unusable screen resolution rather than clipping hidden statistics");

        var roster = new[] {
            new GroupCombatUnit { Id = "a0", Team = "A", Name = "A0" }, new GroupCombatUnit { Id = "a1", Team = "A", Name = "A1" }, new GroupCombatUnit { Id = "a2", Team = "A", Name = "zero" },
            new GroupCombatUnit { Id = "b0", Team = "B", Name = "B0" }, new GroupCombatUnit { Id = "b1", Team = "B", Name = "B1" }
        };
        var names = new[] { KeyValuePair.Create("A", "riflemen"), KeyValuePair.Create("B", "smgmen") };
        var ledger = new GroupCombatLedger(roster, names, detailLimit: 2);
        void Hit(string source, string victim, double amount, string channel = "pawn") => ledger.Record(new GroupCombatDamageEvent { Tick = 1, SourceId = source, VictimId = victim, Amount = amount, RawAmount = amount, Channel = channel });
        Hit("a0", "b0", 10); Hit("a0", "b0", 10); Hit("a1", "b1", 10);
        ledger.SetState("a0", "dead"); Hit("b0", "a1", 6); Hit("a1", "a2", 2); Hit("a1", "a1", 3); Hit(null, "b1", 5); Hit("unidentified", "b1", 7); Hit("a1", "b1", 99, "shield"); Hit("a1", "b1", 4, "armor");
        var snapshot = ledger.Snapshot(120); var a = snapshot.Teams.Single(t => t.Id == "A"); var b = snapshot.Teams.Single(t => t.Id == "B");
        assert(a.EnemyDamage == 30 && a.Damage.Mean == 10 && a.Damage.Median == 10 && a.Dps == 15 && a.MeanDps == 5, "known sequence computes exact common-window DPS, odd median and zero/dead initial members");
        assert(a.Damage.Min == 0 && a.Damage.MinIds.Single() == "a2" && a.Damage.MaxIds.Single() == "a0" && a.Dead == 1, "distribution extrema identify units including zero values and dead top damage dealer");
        assert(b.Taken.Median == 21 && b.DamageTaken == 42 && a.FriendlyDamage == 2 && a.SelfDamage == 3, "even median and taken totals include explicit environment/unknown/friendly/self categories");
        assert(snapshot.Matrix.Where(c => c.Victim == "B" && c.Channel == "pawn").Sum(c => c.Amount) == b.DamageTaken && snapshot.Matrix.Single(c => c.Channel == "shield").Amount == 99, "shield and armor channels never inflate main damage and matrix reconciles with total taken");
        assert(snapshot.TotalEvents == 10 && snapshot.Events.Count == 2 && snapshot.DroppedDetailEvents == 8 && snapshot.Events[0].Id != snapshot.Events[1].Id, "same-tick same-amount hits retain independent event IDs and bounded details cannot truncate counters");
        assert(!snapshot.MeasurementRequirementsMet, "unknown damage attribution remains visible as unmet measurement quality");
        assert(ledger.Snapshot(120).Teams[0].Dps == a.Dps, "wall-clock pause without advanced ticks cannot change DPS denominator");
        Hit("a1", "b0", 9);
        assert(snapshot.Teams[0].EnemyDamage == 30 && snapshot.Units.Single(u => u.Id == "a1").EnemyDamage == 10, "snapshots are detached from later ledger mutations");
        for (int i = 0; i < 20000; i++) Hit("a1", "b0", 1);
        var overflow = ledger.Snapshot(600);
        assert(overflow.TotalEvents == 20011 && overflow.Teams[0].EnemyDamage == 20039 && overflow.Teams[0].Damage.Mean * 3 == 20039, "twenty thousand additional events preserve exact roster/team totals beyond retained evidence capacity");
        assert(ledger.Snapshot(0).Teams.All(t => t.Dps == 0) && ledger.Snapshot(0).Units.All(u => u.Dps == 0), "not-started window never divides by zero");

        GroupCombatRoundResult State(params (string id, int standing, int downed)[] counts) => new GroupCombatRoundResult { ElapsedTicks = 100, Teams = counts.Select(c => new GroupCombatTeamResult { Id = c.id, Standing = c.standing, Downed = c.downed, Alive = c.standing + c.downed }).ToList() };
        var ends = new GroupCombatEndRule(); var both = State(("A", 2, 0), ("B", 2, 0));
        assert(!ends.Evaluate(both, 10800), "default battle window lasts 10800 ticks without an all-down elimination");
        var down = State(("A", 0, 3), ("B", 1, 0));
        assert(new GroupCombatEndRule().Evaluate(down, 10800) && down.Teams[0].Outcome == "loss", "all downed loses immediately even though downed combatants are alive");
        var draw = State(("A", 0, 1), ("B", 0, 2));
        assert(new GroupCombatEndRule().Evaluate(draw, 10800) && draw.Teams.All(t => t.Outcome == "draw"), "same tick mutual all-down yields a draw");
        var timeout = State(("A", 1, 3), ("B", 2, 1)); timeout.ElapsedTicks = 10800;
        assert(new GroupCombatEndRule().Evaluate(timeout, 10800) && timeout.Teams[0].Outcome == "win", "time-limit alive counts include downed and can favor a side with fewer standing members");
        Reject(Xml.Replace("<Team id=\"B\" name=\"乙\" />", "<Team id=\"B\"/><Team id=\"C\"/>"), "group compiler rejects a third team under the two-side product scope");

    }
}
