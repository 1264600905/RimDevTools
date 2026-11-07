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
        GroupCombatInheritanceTests.Run(assert);
        GroupCombatLifecycleTests.Run(assert);
        var legacyPopulation = ScenarioLoader.Compile(new[] { new ScenarioInput { Path = "legacy.xml", Sha256 = "h", Xml = Xml.Replace("<GroupCombat>", "<GroupCombat><Metrics population='initialRoster'/>") } }, true);
        assert(XElement.Parse(legacyPopulation.Cases[0].Xml).Element("GroupCombat")!.Element("Metrics")!.Attribute("population")!.Value == "registeredRoster", "legacy roster metadata normalizes to the complete dynamic participant population");
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
        Reject(Xml.Replace("<GroupCombat>", "<GroupCombat timeSpeed='paused'>"), "XML speed rejects pause as an initial battle speed while allowing runtime pause");
        Reject(Xml.Replace("<GroupCombat>", "<GroupCombat><Metrics dpsBasis='battleWindow'/>"), "stale metadata cannot silently describe lifetime DPS as a shared window");
        Reject(Xml.Replace("<GroupCombat>", "<GroupCombat><Overlay><Summary dps='battleWindowMeanPerInitialUnit'/></Overlay>"), "stale initial-roster mean metadata is rejected");
        assert(Compile(Xml.Replace("<GroupCombat>", "<GroupCombat timeSpeed='fast'><Metrics dpsBasis='unitLifetime' teamDpsBasis='battleWindow' meanPopulation='aliveOnMap'/>" )).Cases.Count == 1, "XML accepts explicit battle speed and unambiguous live lifetime mean metadata");
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
        ledger.SetState("a0", "dead", 60); Hit("b0", "a1", 6); Hit("a1", "a2", 2); Hit("a1", "a1", 3); Hit(null, "b1", 5); Hit("unidentified", "b1", 7); Hit("a1", "b1", 99, "shield"); Hit("a1", "b1", 4, "armor");
        var snapshot = ledger.Snapshot(120); var a = snapshot.Teams.Single(t => t.Id == "A"); var b = snapshot.Teams.Single(t => t.Id == "B");
        var survivors = GroupCombatPresentation.SurvivorTotals(snapshot.Units.Where(u => u.Team == "A"));
        assert(survivors.output == 10 && survivors.taken == 11 && a.EnemyDamage == 30, "settlement retains deceased output in whole-team totals but excludes it from survivor totals");
        assert(GroupCombatPresentation.SurvivorTotals(new[] {
            new GroupCombatUnit { State = "downed", EnemyDamage = 7, DamageTaken = 9 },
            new GroupCombatUnit { State = "escaped", EnemyDamage = 100, DamageTaken = 100 }
        }) == (0d, 0d), "settlement excludes downed members and units that left the map from survivor totals");
        assert(Compile(Xml.Replace("<GroupCombat>", "<GroupCombat aiPolicy='battle'>")).Cases.Count == 1, "benchmark assault policy is accepted without replacing pawn think trees");
        assert(a.EnemyDamage == 30 && a.Damage.Mean == 10 && a.Damage.Median == 10 && a.Dps == 15 && a.MeanDps == 2.5 && a.MeanDamageTaken == 5.5, "known sequence computes team window DPS, living lifetime means and all-roster distribution");
        assert(a.MeanOutput == 10 && new GroupCombatTeamResult().MeanOutput == 0, "average output divides total damage including deceased contributions by the initial roster and handles empty teams");
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
        assert(new GroupCombatLedger(roster.Select(u => new GroupCombatUnit { Id = u.Id, Team = u.Team }), names).Snapshot(0).Units.All(u => u.Dps == 0), "not-started window never divides by zero");

        assert(snapshot.Units.Single(u => u.Id == "a0").AliveTicks == 60 && snapshot.Units.Single(u => u.Id == "a0").Dps == 20, "dead units retain their own lifetime DPS rather than the continuing battle window");
        var lifetime = new GroupCombatLedger(new[] { new GroupCombatUnit { Id = "a", Team = "A" }, new GroupCombatUnit { Id = "b", Team = "B" } }, names);
        lifetime.SetState("a", "downed", 30);
        lifetime.SetState("a", "removed", 60);
        var absent = lifetime.Snapshot(120);
        assert(absent.Units[0].AliveTicks == 60 && absent.Teams[0].MeanDps == 0 && absent.Teams[0].MeanDamageTaken == 0, "downed time counts but removed units leave live means with a finite zero for an empty team");
        lifetime.SetState("a", "standing", 120);
        assert(lifetime.Snapshot(180).Units[0].AliveTicks == 120, "re-entered units resume alive time without counting time off the battlefield");
        assert(!lifetime.IsCameraTarget("a") && lifetime.Snapshot(180).Units[0].WasDowned && lifetime.IsCameraTarget("b"), "recovered pawns stay excluded from the camera while undowned participants remain eligible");
        lifetime.SetState("b", "dead", 180);
        lifetime.SetState("b", "standing", 180);
        assert(!lifetime.IsCameraTarget("b") && lifetime.Snapshot(180).Units[1].WasDead, "resurrected pawns stay excluded from camera tracking");
        var quickRecovery = new GroupCombatLedger(new[] { new GroupCombatUnit { Id = "a", Team = "A" }, new GroupCombatUnit { Id = "b", Team = "B" } }, names);
        quickRecovery.MarkDowned("a"); quickRecovery.MarkDead("b");
        assert(!quickRecovery.IsCameraTarget("a") && !quickRecovery.IsCameraTarget("b") && quickRecovery.Snapshot(0).Units.All(u => u.State == "standing"), "native downing and death notifications retain exclusions even before a state sample observes the event");
        assert(new GroupCombatLedger(new[] { new GroupCombatUnit { Id = "a", Team = "A" }, new GroupCombatUnit { Id = "b", Team = "B" } }, names).IsCameraTarget("a"), "camera exclusion history resets for a new battle");
        foreach (var resolution in new[] { (800f, 600f), (1280f, 720f), (1920f, 1080f) })
        {
            var positions = new[] { (3f, 4f), (97f, 90f), (50f, 50f) };
            var frame = GroupCombatPresentation.Frame(positions, resolution.Item1, resolution.Item2, true);
            var halfWidth = frame.size * resolution.Item1 / resolution.Item2;
            var inset = GroupCombatPresentation.PanelWidth(resolution.Item1) + 22;
            assert(positions.All(p => (p.Item1 - frame.x) / (2 * halfWidth) + .5f >= inset / resolution.Item1 && (p.Item1 - frame.x) / (2 * halfWidth) + .5f <= 1 - inset / resolution.Item1 && (p.Item2 - frame.z) / (2 * frame.size) + .5f >= 90 / resolution.Item2 && (p.Item2 - frame.z) / (2 * frame.size) + .5f <= 1 - (GroupCombatPresentation.PanelTop + 6) / resolution.Item2), "dedicated wide camera contains the crowd between panels at " + resolution);
            assert(GroupCombatPresentation.PanelWidth(resolution.Item1) * 2 < resolution.Item1 && GroupCombatPresentation.PanelTop + GroupCombatPresentation.PanelHeight(resolution.Item2) <= resolution.Item2 - 90, "enlarged panels leave a central battlefield and native time controls at " + resolution);
        }
        assert(GroupCombatPresentation.PanelWidth(1920) == 495 && GroupCombatPresentation.ContentScale(495, GroupCombatPresentation.PanelHeight(1080)) == 1.5f, "full HD panels and contents expand to one and a half times the original width and scale");
        assert(new[] { 0f, .99f, 1f, 2f, 3f, GroupCombatPresentation.IntroDuration }.Select(GroupCombatPresentation.CountdownLabel).SequenceEqual(new[] { "3", "3", "2", "1", "开始", "" }), "real-time intro shows each countdown stage before releasing combat");
        var wideFrame = GroupCombatPresentation.Frame(new[] { (22f, 40f), (71f, 52f) }, 1920, 1080, true);
        var closeFrame = GroupCombatPresentation.Frame(new[] { (40f, 40f), (50f, 48f) }, 1920, 1080, true);
        var wideZoom = GroupCombatPresentation.ZoomTarget(0, wideFrame.size);
        var closeZoom = GroupCombatPresentation.ZoomTarget(wideZoom, closeFrame.size);
        assert(closeZoom < wideZoom && closeZoom >= closeFrame.size, "converging armies shrink the camera target instead of retaining the opening view");
        assert(GroupCombatPresentation.ZoomTarget(closeZoom, closeFrame.size + .2f) == closeZoom && GroupCombatPresentation.ZoomTarget(closeZoom, closeFrame.size - .2f) == closeZoom, "small formation movements keep the zoom target stable in both directions");
        assert(GroupCombatPresentation.ZoomTarget(closeZoom, wideFrame.size) >= wideFrame.size, "spreading armies expand the camera target again");
        var motion = new GroupCombatCameraMotion();
        motion.Step(0, 0, 24, 0);
        motion.Step(100, 50, 40, 1f / 60);
        assert(motion.X > 0 && motion.X < 5 && motion.Size > 24 && motion.Size < 25, "camera position and zoom move toward a new target without teleporting");
        for (var i = 0; i < 600; i++) motion.Step(100, 50, 40, 1f / 60);
        assert(Math.Abs(motion.X - 100) < .001 && Math.Abs(motion.Z - 50) < .001 && Math.Abs(motion.Size - 40) < .001, "smoothed camera converges without overshoot or persistent oscillation");
        var inward = new GroupCombatCameraMotion(); inward.Step(wideFrame.x, wideFrame.z, wideZoom, 0);
        inward.Step(closeFrame.x, closeFrame.z, closeZoom, 1f / 60);
        assert(inward.Size < wideZoom && inward.Size > closeZoom, "converging armies zoom in gradually instead of jumping to a close-up");
        var before = motion.X; motion.Step(-100, -50, 60, 20);
        assert(motion.X < before && motion.X > 50 && motion.Size < 43, "long focus gaps preserve a gradual position and zoom transition");

        GroupCombatRoundResult State(params (string id, int standing, int downed)[] counts) => new GroupCombatRoundResult { ElapsedTicks = 100, Teams = counts.Select(c => new GroupCombatTeamResult { Id = c.id, Initial = c.standing + c.downed, Standing = c.standing, Downed = c.downed, Alive = c.standing }).ToList() };
        var ends = new GroupCombatEndRule(0); var both = State(("A", 2, 0), ("B", 2, 0));
        assert(!ends.Evaluate(both, 10800), "default battle window lasts 10800 ticks without an all-down elimination");
        var down = State(("A", 0, 3), ("B", 1, 0));
        assert(new GroupCombatEndRule(0).Evaluate(down, 10800) && down.Teams[0].Outcome == "loss", "all downed loses immediately with zero combat survivors");
        var draw = State(("A", 0, 1), ("B", 0, 2));
        assert(new GroupCombatEndRule(0).Evaluate(draw, 10800) && draw.Teams.All(t => t.Outcome == "draw"), "same tick mutual all-down yields a draw");
        var timeout = State(("A", 1, 3), ("B", 2, 1)); timeout.ElapsedTicks = 10800;
        assert(new GroupCombatEndRule(0).Evaluate(timeout, 10800) && timeout.Teams[1].Outcome == "win", "time-limit survivor counts exclude downed and favor the side with more standing members");
        Reject(Xml.Replace("<Team id=\"B\" name=\"乙\" />", "<Team id=\"B\"/><Team id=\"C\"/>"), "group compiler rejects a third team under the two-side product scope");

    }
}
