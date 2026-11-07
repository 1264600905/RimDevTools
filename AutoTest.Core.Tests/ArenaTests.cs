using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;
using LogSyncMod.AutoTest.Scenarios;

internal static class ArenaTests
{
    internal static void Run(Action<bool, string> check)
    {
        void Reject(Action action, string name)
        {
            try { action(); check(false, name); }
            catch (Exception ex) when (ex is ScenarioConfigurationException or FormatException or OverflowException or System.Xml.XmlException) { check(true, name); }
        }
        var budget = CombatRoster.Resolve("wolf", "Wolf", 85, 3000);
        check(budget.ResolvedCount == 35 && budget.ActualCombatPower == 2975 && budget.UnusedBudget == 25, "budget resolves maximal affordable count and preserves unused combatPower");
        check(CombatRoster.Resolve("r", "K", .1m, .3m).ResolvedCount == 3 && CombatRoster.Power(.1f) == .1m, "budget arithmetic uses loaded float round-trip decimals at exact boundaries");
        check(CombatRoster.Resolve("r", "K", .3m, .899999999999999999m).ResolvedCount == 2, "budget arithmetic never uses tolerance to create an unaffordable pawn");
        Reject(() => CombatRoster.Resolve("r", "K", 100, 99), "budget rejects zero affordable units");
        Reject(() => CombatRoster.Resolve("r", "K", 1, 101), "budget rejects excessive rosters without clamping");
        Reject(() => CombatRoster.Power(float.NaN), "allocation rejects NaN combatPower");
        check(CombatRoster.Resolve("r", "K", 100, 3000).ResolvedCount == 30 && CombatRoster.Resolve("r", "K", 3000, 3000).ResolvedCount == 1, "exact budget division covers CP 100 and CP equal to full budget");
        Reject(() => CombatRoster.Resolve("r", "K", .1m, decimal.MaxValue), "huge budget arithmetic overflow fails before allocating a roster");
        Reject(() => CombatRoster.Resolve("r", "K", decimal.MaxValue, null, 2), "count-mode total combatPower overflow fails explicitly");
        check(CombatRoster.Resolve("r", "K", decimal.MaxValue, decimal.MaxValue).ResolvedCount == 1, "maximality check treats overflowing next-unit cost as unaffordable without rejecting a valid roster");
        Reject(() => CombatRoster.Power(float.PositiveInfinity), "arena rejects infinite loaded combatPower");
        Reject(() => CombatRoster.Power(-10), "arena rejects negative loaded combatPower");
        Reject(() => CombatRoster.Positive("-1"), "budget rejects negative points");
        Reject(() => CombatRoster.Power(0), "arena rejects zero combatPower even in count mode");
        Reject(() => CombatRoster.Positive("Infinity"), "budget rejects non-finite input");
        const string groupXml = "<TestSuite schemaVersion='1' id='budget'><Case id='r' kind='groupCombat'><testDefName>Budget</testDefName><Scene type='mainMap' sizeX='100' sizeZ='100'/><Teams><Team id='A'/><Team id='B'/></Teams><Actors><PawnGroup id='a' team='A' combatPowerBudget='3000' originX='10' originZ='20' formation='grid' columns='10'><basePawnKind>Wolf_Timber</basePawnKind></PawnGroup><PawnGroup id='b' team='B' count='2' originX='60' originZ='20'><basePawnKind>Mercenary_Gunner</basePawnKind></PawnGroup></Actors><GroupCombat/></Case></TestSuite>";
        ScenarioPlan Group(string text) => ScenarioLoader.Compile(new[] { new ScenarioInput { Path = "group.xml", Sha256 = "h", Xml = text } }, true);
        var groupPlan = Group(groupXml);
        var resolvedCase = XElement.Parse(groupPlan.Cases.Single().Xml);
        var loadedReads = new List<string>();
        var resolvedGroups = CombatRoster.ResolveGroups(resolvedCase, kind => { loadedReads.Add(kind); return 75; }, "source");
        check(resolvedGroups.Single().ResolvedCount == 40 && ScenarioLoader.I(resolvedCase.Element("Actors").Element("PawnGroup"), "count") == 40 && loadedReads.SequenceEqual(new[] { "Wolf_Timber" }), "group budget resolves only budget actors from loaded defs, preserving count-only eligibility");
        Reject(() => Group(groupXml.Replace("combatPowerBudget='3000'", "combatPowerBudget='3000' count='2'")), "group strict parser rejects count and combatPowerBudget together");
        Reject(() => Group(groupXml.Replace("combatPowerBudget='3000'", "combatPowerBudget='NaN'")), "group strict parser rejects non-finite combatPower budgets");
        void ResolveAndValidate(string text, float power)
        {
            var original = Group(text); var xmlCase = XElement.Parse(original.Cases.Single().Xml);
            CombatRoster.ResolveGroups(xmlCase, _ => power, "source"); xmlCase.Attribute("packageId")?.Remove();
            var suite = new XElement("TestSuite", new XAttribute("schemaVersion", 1), new XAttribute("id", "resolved"), xmlCase);
            Group(suite.ToString(SaveOptions.DisableFormatting));
        }
        Reject(() => ResolveAndValidate(groupXml.Replace("originX='10'", "originX='95'"), 75), "resolved budget formations are revalidated against map edges before spawning");
        Reject(() => ResolveAndValidate(groupXml, 20), "resolved group budget refuses more than 100 units before spawning");
        check(Group(groupXml.Replace("combatPowerBudget='3000'", "")).Cases.Count == 1, "group count default remains one when count and budget are both absent");
        const string xml = "<ArenaSuite schemaVersion='1' id='suite'><Tournament id='main'><testDefName>ArenaTest</testDefName><Scene type='mainMap' sizeX='100' sizeZ='100'/><Roster mode='combatPower' budget='3000'/><Entrants><Entrant id='a' pawnKind='Wolf_Timber'/><Entrant id='b' pawnKind='Mercenary_Gunner'/></Entrants></Tournament></ArenaSuite>";
        ArenaPlan Parse(string text) => ArenaLoader.Compile(new[] { new ScenarioInput { Path = "arena.xml", Sha256 = "h", Xml = text } }, new[] { "ArenaTest" });
        var plan = Parse(xml);
        check(plan.Purpose == "arena" && plan.Tournaments[0].Budget == 3000 && plan.Tournaments[0].MaxReplays == 2 && plan.DefaultDeadlineSeconds > 600, "arena has independent parser, frozen defaults and bounded tournament deadline");
        check(Parse(xml.Replace("mode='combatPower' budget='3000'", "mode='count' count='20'").Replace("id='a' pawnKind", "id='a' count='3' pawnKind")).Tournaments[0].Entrants[0].Count == 3, "arena count mode supports per-entrant counts");
        Reject(() => Parse(xml.Replace("id='a' pawnKind", "id='a' count='3' pawnKind")), "arena budget mode rejects count overrides");
        Reject(() => Parse(xml.Replace("Mercenary_Gunner", "Wolf_Timber")), "arena rejects duplicate PawnKinds");
        Reject(() => Parse(xml.Replace("id='b'", "id='a'")), "arena rejects duplicate entrant IDs");
        Reject(() => Parse(xml.Replace("<Entrants>", "<Entrants typo='true'>")), "arena strict schema rejects unknown fields");
        Reject(() => Parse(xml.Replace("</Tournament>", "<Battle durationSeconds='NaN'/></Tournament>")), "arena validates finite simulated durations");
        Reject(() => Parse(xml.Replace("</Tournament>", "<Scoring timeReferenceSeconds='0'/></Tournament>")), "arena rejects zero scoring time reference");
        Reject(() => Parse(xml.Replace("</Tournament>", "<Output width='1920'/></Tournament>")), "arena requires paired output dimensions");
        Reject(() => Parse("<!DOCTYPE ArenaSuite [<!ENTITY x SYSTEM 'file:///C:/Windows/win.ini'>]>" + xml), "arena disables DTD and external entity resolution");
        Reject(() => ScenarioLoader.Compile(plan.Inputs), "ordinary regression entry refuses ArenaSuite");
        var input = plan.Tournaments.Single();
        var verticalRosters = new List<ResolvedCombatRoster> { CombatRoster.Resolve("a", "Wolf_Timber", 100, null, 27), CombatRoster.Resolve("b", "Bear_Polar", 200, null, 3) };
        var verticalBattle = XElement.Parse(ArenaLoader.CompileBattle(input, verticalRosters, null, 0).Xml);
        var verticalActors = verticalBattle.Element("Actors").Elements("PawnGroup").ToList();
        check(PawnGroupFormation.Extents(verticalActors[0], true) == (4, 18)
            && PawnGroupFormation.Extents(verticalActors[1], true) == (0, 4)
            && verticalActors.All(a => ScenarioLoader.I(a, "originZ") + PawnGroupFormation.Extents(a, true).z / 2 == 50),
            "arena centers both full and partial vertical formations using the same geometry as group combat");
        var score = ArenaScore.Calculate(input, 20, 15, 2700);
        check(score.Value == 90 && score.SurvivalRatio == .75m && score.SpeedFactor == .75m, "victory score uses initial standing ratio and simulated seconds");
        check(ArenaScore.Calculate(input, 20, 0, 12000).Value == 0, "summon-only victory cannot grant original roster survival points; late time bonus floors at zero");
        for (int n = 2; n <= 64; n++)
        {
            var tournament = Tournament(input, n); var schedule = new ArenaScheduler(tournament);
            var eliminated = new HashSet<string>(); var pairs = new HashSet<string>(); var count = 0; var valid = true;
            ArenaMatch match;
            while ((match = schedule.Next()) != null)
            {
                valid &= !eliminated.Contains(match.Left) && !eliminated.Contains(match.Right) && pairs.Add(JsonSerializer.Serialize(match.PairKey));
                eliminated.Add(match.Right); schedule.Resolve(match.Left, new ArenaScore { Value = 100 - count++ });
                if (count > 64) { valid = false; break; }
            }
            check(valid && count == n - 1 && schedule.Champion != null && !eliminated.Contains(schedule.Champion), "knockout invariants: " + n + " entrants, N-1 unique unordered pairs, no eliminated entrant returns");
            if (n is 5 or 20 or 64)
            {
                tournament.Champion = schedule.Champion;
                var post = ArenaBracketLayout.BuildPostBattle(tournament);
                var byId = post.Nodes.ToDictionary(node => node.Id);
                check(post.Nodes.Count == 2 * n && post.Nodes.Where(node => node.Id.StartsWith("entrant/")).Select(node => node.EntrantId).Distinct().Count() == n,
                    "post-battle tree preserves every entrant and completed match: " + n);
                check(post.Nodes.All(a => post.Nodes.All(b => a == b || a.X != b.X || Math.Abs(a.Y - b.Y) >= 56))
                    && post.Edges.All(e => byId[e.From].X < byId[e.To].X), "post-battle layout avoids overlapping cards and backward edges: " + n);
                check(tournament.Matches.All(m => byId[m.Id].EntrantId == m.Winner) && byId["future"].EntrantId == tournament.Champion,
                    "advancement and champion cards retain winner color identity: " + n);
            }
        }
        var five = Tournament(input, 5); var s5 = new ArenaScheduler(five);
        var qualification = s5.Next(); check(qualification.Kind == "qualification" && qualification.Round == 0, "odd initial entrant count begins with a real qualification match");
        s5.Resolve(qualification.Left, new ArenaScore { Value = 10 });
        check(s5.Next().Kind == "round", "qualification winner returns to full even round");
        var six = Tournament(input, 6); var s6 = new ArenaScheduler(six); var wins = new List<string>();
        for (int i = 0; i < 3; i++) { var m = s6.Next(); wins.Add(m.Left); s6.Resolve(m.Left, new ArenaScore { Value = i == 0 ? 100 : i }); }
        var extra = s6.Next();
        check(extra.Kind == "extra" && extra.PairKey.Order().SequenceEqual(wins.Skip(1).Order()), "odd winners select the two lowest latest victory scores after the full round");
        s6.Resolve(extra.Left, new ArenaScore { Value = .125m });
        check(s6.Next().Kind == "round", "extra-match winner advances to the next ordinary round");
        string Trace(int seed)
        {
            var t = Tournament(input, 8); t.Input.Seed = seed; var scheduler = new ArenaScheduler(t); var keys = new List<string>(); ArenaMatch m;
            while ((m = scheduler.Next()) != null) { keys.Add(m.Id + JsonSerializer.Serialize(m.PairKey)); scheduler.Resolve(m.Left, new ArenaScore { Value = 1 }); }
            return string.Join(";", keys);
        }
        check(Trace(12345) == Trace(12345) && Trace(12345) != Trace(23456), "fixed hashing gives reproducible seed-dependent bracket ordering");
        check(ArenaScheduler.Stable("a|b", "c") != ArenaScheduler.Stable("a", "b|c"), "structured pair identities cannot collide through separators");
        var orderA = Tournament(input, 7); var orderB = Tournament(input, 7); orderB.Rosters.Reverse();
        var scheduleA = new ArenaScheduler(orderA); var scheduleB = new ArenaScheduler(orderB);
        var stableOrder = true;
        while (true)
        {
            var a = scheduleA.Next(); var b = scheduleB.Next();
            if (a == null || b == null) { stableOrder &= a == null && b == null; break; }
            stableOrder &= a.Left == b.Left && a.Right == b.Right && a.Kind == b.Kind;
            scheduleA.Resolve(a.Left, new ArenaScore { Value = 50 }); scheduleB.Resolve(b.Left, new ArenaScore { Value = 50 });
        }
        check(stableOrder && scheduleA.Champion == scheduleB.Champion, "equal counts and scores remain stable when XML entrant order is reversed");
        var counts = Tournament(input, 4); counts.Rosters[0].ResolvedCount = 1; counts.Rosters[1].ResolvedCount = 30; counts.Rosters[2].ResolvedCount = 31; counts.Rosters[3].ResolvedCount = 100;
        check(new ArenaScheduler(counts).Next().PairKey.SequenceEqual(new[] { "e1", "e2" }), "greedy pairing chooses globally closest initial roster counts");
        var left = new GroupCombatTeamResult { Id = "A", Initial = 20, Standing = 10, Alive = 10 }; var right = new GroupCombatTeamResult { Id = "B", Initial = 2, Standing = 2, Alive = 2 };
        var battle = new GroupCombatRoundResult { ElapsedTicks = 600, Teams = new() { left, right }, Units = Enumerable.Range(0, 20).Select(i => new GroupCombatUnit { Id = "a" + i, Team = "A", State = i < 10 ? "standing" : "downed" }).Concat(Enumerable.Range(0, 2).Select(i => new GroupCombatUnit { Id = "b" + i, Team = "B" })).ToList() };
        check(new ArenaEndRule(0).Evaluate(battle, 600) && right.Outcome == "win", "arena time-limit compares standing proportions rather than larger absolute survivor counts");
        left.Standing = left.Alive = 0; left.Downed = 20; left.PendingRevival = 1; battle.ElapsedTicks = 1;
        check(!new ArenaEndRule(0).Evaluate(battle, 600), "known pending revival delays arena defeat");
        left.PendingRevival = 0; left.Joined = 1; left.Standing = left.Alive = 1;
        check(!new ArenaEndRule(0).Evaluate(battle, 600), "standing summon keeps its side in the arena fight");
        left.Joined = 0; left.Standing = left.Alive = 0; left.Downed = 0; left.Escaped = 20;
        check(new ArenaEndRule(0).Evaluate(battle, 600) && right.Outcome == "win", "all escaped original units are unavailable for arena defeat determination");
        right.Standing = right.Alive = 0; right.Downed = 2;
        check(new ArenaEndRule(0).Evaluate(battle, 600) && battle.Teams.All(t => t.Outcome == "draw"), "same-tick mutual arena defeat yields a draw");
        var confirm = new ArenaEndRule(300); battle.ElapsedTicks = 1;
        check(!confirm.Evaluate(battle, 600), "arena defeat starts continuous confirmation window");
        battle.ElapsedTicks = 301;
        check(confirm.Evaluate(battle, 301) && battle.EndReason == "allUnavailable", "confirmed defeat takes priority at the exact simulated time limit");
        var reviveRule = new ArenaEndRule(300);
        left.Standing = left.Alive = 0; left.Escaped = left.Initial; right.Standing = right.Alive = right.Initial; right.Downed = 0;
        battle.ElapsedTicks = 1; check(!reviveRule.Evaluate(battle, 1000), "arena begins defeat confirmation for unavailable side");
        left.Standing = left.Alive = 1; left.Escaped--; battle.ElapsedTicks = 301;
        check(!reviveRule.Evaluate(battle, 1000) && left.EliminatedAtTick == null, "revival cancels the continuous defeat confirmation");
        left.Standing = left.Alive = 0; left.Escaped++; battle.ElapsedTicks = 302;
        check(!reviveRule.Evaluate(battle, 1000) && left.EliminatedAtTick == 302, "a second defeat starts a new confirmation window after revival");
        left.Standing = left.Alive = 1; left.Escaped--; left.Joined++; battle.ElapsedTicks = 602;
        check(!reviveRule.Evaluate(battle, 1000), "newly registered standing participant cancels arena defeat");
        var layout = ArenaBracketLayout.Build(Tournament(input, 64));
        check(layout.Nodes.Count == 65 && layout.Width > 0 && layout.Height >= 64 * 64, "64-entrant bracket retains all entrants and a future placeholder for scrollable overview");
        var kinds = Enumerable.Range(0, 64).Select(i => "Kind" + i).ToArray();
        var hues = ArenaPresentation.IdentityHues(kinds);
        var reversedHues = ArenaPresentation.IdentityHues(kinds.Reverse());
        check(hues.All(pair => pair.Value == reversedHues[pair.Key]) && hues.Values.All(h => h >= 0 && h < 1)
            && hues.Values.All(a => hues.Values.All(b => a == b || Math.Min(Math.Abs(a - b), 1 - Math.Abs(a - b)) >= .65f / 64)),
            "64 identity hues are stable across roster ordering and separated even after collision resolution");
        check(ArenaPresentation.Progress(-1, 0, .5f) == 0 && ArenaPresentation.Progress(10, 0, .5f) == 1
            && ArenaPresentation.RevealBSeconds > .5f && ArenaPresentation.AdvanceSeconds > ArenaPresentation.RevealBSeconds + .5f
            && ArenaPresentation.PostBattleSeconds > ArenaPresentation.AnimationSeconds,
            "post-battle A/B reveals precede advancement and leave a stable capture interval");
    }
    private static ArenaTournamentResult Tournament(ArenaTournamentInput source, int count)
    {
        var input = JsonSerializer.Deserialize<ArenaTournamentInput>(JsonSerializer.Serialize(source));
        return new ArenaTournamentResult { Input = input, Rosters = Enumerable.Range(0, count).Select(i => new ResolvedCombatRoster { Id = "e" + i, DisplayName = "Entrant " + i, PawnKind = "K" + i, ResolvedCount = 10 + i % 3, UnitCombatPower = 100 }).ToList() };
    }
}
