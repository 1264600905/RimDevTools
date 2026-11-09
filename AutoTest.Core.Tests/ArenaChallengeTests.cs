using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using LogSyncMod.AutoTest.Scenarios;

internal static class ArenaChallengeTests
{
    internal static ArenaPlan Parse(string format = "limitChallenge", string growth = "<Growth multiplier='1.5' maxValue='5'/>", string roster = "<Roster mode='count' count='1'/>", string presentation = "")
    {
        var xml = "<ArenaSuite schemaVersion='1' id='challenges'><Tournament id='main' format='" + format + "'><testDefName>ChallengeFixture</testDefName>"
            + "<Scene type='mainMap' sizeX='100' sizeZ='100'/>" + roster
            + "<Entrants><Entrant id='b1' pawnKind='Wolf' role='boss'/><Entrant id='b2' pawnKind='Wolf' role='boss'/>"
            + "<Entrant id='c1' pawnKind='Wolf' role='challenger'/><Entrant id='c2' pawnKind='Wolf' role='challenger'/></Entrants>"
            + growth + presentation + "<Battle confirmationTicks='0'/><TieBreak maxReplays='0'/></Tournament></ArenaSuite>";
        return ArenaLoader.Compile(new[] { new ScenarioInput { Path = "challenge.xml", Sha256 = "frozen", Xml = xml } }, new[] { "ChallengeFixture" });
    }
    internal static void Run(Action<bool, string> check)
    {
        void Reject(Func<ArenaPlan> action, string label)
        {
            try { action(); check(false, label); }
            catch (ScenarioConfigurationException) { check(true, label); }
        }
        ArenaTournamentResult Result(ArenaPlan plan) => new() { Input = plan.Tournaments[0], Rosters = plan.Tournaments[0].Entrants
            .Select(e => CombatRoster.Resolve(e.Id, e.PawnKind, 10, e.Budget ?? plan.Tournaments[0].Budget, e.Count ?? plan.Tournaments[0].Count)).ToList() };
        var roundRobin = Result(Parse("bossRoundRobin", ""));
        check(roundRobin.Input.ShowcasePages.Count == 2 && roundRobin.Input.ShowcasePages.All(p => p.EntrantIds.Select(id => roundRobin.Input.Entrants.Single(e => e.Id == id).Role).Distinct().Count() == 1),
            "bosses and challengers are introduced on separate showcase pages");
        var customPages = Parse("bossRoundRobin", "", presentation: "<Presentation><Showcase><Page seconds='7' caption='介绍'><Entrant ref='b1'/><Entrant ref='c1'/></Page></Showcase></Presentation>").Tournaments[0];
        check(customPages.ShowcasePages.Count == 4 && customPages.ShowcasePages.Take(2).All(p => p.Seconds == 7 && p.Caption == "介绍" && p.EntrantIds.Count == 1)
            && customPages.ShowcasePages.SelectMany(p => p.EntrantIds).Distinct().Count() == 4,
            "explicit mixed showcase pages are split by role while preserving their captions, durations and remaining entrants");
        var rr = new ArenaChallengeScheduler(roundRobin);
        var order = new List<string>();
        ArenaMatch match;
        while ((match = rr.Next()) != null)
        {
            order.Add(match.Right + "/" + match.Left);
            rr.Resolve(match.Right == "c1" ? match.Left : match.Right, new ArenaScore { IsRankable = true, Value = match.Left == "b1" ? 80 : 90 });
        }
        var standings = ArenaChallengeRules.Standings(roundRobin);
        check(order.SequenceEqual(new[] { "c1/b1", "c1/b2", "c2/b1", "c2/b2" }) && roundRobin.Challenges.All(p => p.Matches.Count == 1), "boss round robin schedules every ordered cross-role pair once after either side loses");
        check(standings[0].Wins == 1 && standings[0].Losses == 1 && standings[0].AverageVictoryScore == 80 && standings[1].AverageVictoryScore == 90, "boss average uses only unrounded victory scores");
        var limits = Result(Parse());
        var additive = Result(Parse(growth: "<Growth mode='add' increment='2' maxValue='5'/>"));
        var additiveSchedule = new ArenaChallengeScheduler(additive); var additiveValues = new List<decimal>();
        while ((match = additiveSchedule.Next()) != null) { additiveValues.Add(match.ChallengeValue.Value); additiveSchedule.Resolve(match.Left, new ArenaScore { IsRankable = true, Value = 50 }); }
        check(additiveValues.SequenceEqual(Enumerable.Range(0, 4).SelectMany(_ => new decimal[] { 1, 3, 5 })), "additive count growth adds fixed members and resets each pair at the exact cap");
        var additiveCP = Result(Parse(growth: "<Growth target='budget' mode='add' increment='2.5' maxValue='15'/>", roster: "<Roster mode='combatPower' budget='10'/>"));
        var additiveCPSchedule = new ArenaChallengeScheduler(additiveCP); var additiveCPValues = new List<decimal>();
        while ((match = additiveCPSchedule.Next()) != null) { additiveCPValues.Add(match.ChallengeValue.Value); additiveCPSchedule.Resolve(match.Left, new ArenaScore { IsRankable = true, Value = 50 }); }
        check(additiveCPValues.SequenceEqual(Enumerable.Range(0, 4).SelectMany(_ => new decimal[] { 10, 12.5m, 15 })), "additive CP growth preserves fractional increments");
        check(!ArenaPresentation.RulesReady(4.99, true) && ArenaPresentation.RulesReady(5, true), "ready entrants wait only until the five second real time minimum");
        check(!ArenaPresentation.RulesReady(9, false) && ArenaPresentation.RulesReady(9, true), "slow preparation advances as soon as ready without an additional five second wait");
        var schedule = new ArenaChallengeScheduler(limits);
        var values = new List<decimal>();
        while ((match = schedule.Next()) != null)
        {
            values.Add(match.ChallengeValue.Value);
            schedule.Resolve(match.Left, new ArenaScore { IsRankable = true, Value = 80 });
        }
        check(values.SequenceEqual(Enumerable.Range(0, 4).SelectMany(_ => new decimal[] { 1, 2, 3, 5 }))
            && limits.Challenges.All(p => p.Status == "limitReached" && p.LimitReason == "maxValue"), "count growth rounds each loss upward, allows exactly the cap, and resets every boss pair");
        var wins = Result(Parse()); var winSchedule = new ArenaChallengeScheduler(wins);
        var first = winSchedule.Next(); winSchedule.Resolve(first.Right, new ArenaScore { IsRankable = true, Value = 100 });
        var next = winSchedule.Next();
        check(wins.Challenges[0].Status == "won" && wins.Challenges[0].WinningValue == 1 && next.Left == "b2" && next.Level == 0 && next.ChallengeValue == 1, "initial victory terminates only its own pair and resets the next boss");
        next.Attempts.Add(new GroupCombatRoundResult()); winSchedule.ResolveDraw();
        var afterDraw = winSchedule.Next();
        check(wins.Challenges[1].Status == "unresolvedDraw" && afterDraw.Right == "c2" && afterDraw.Level == 0, "draw exhaustion ends a pair without growth and continues the remaining schedule");
        check(ArenaChallengeRules.Standings(wins)[0].AverageVictoryScore == null, "zero boss victories keep an absent average rather than a fake score");
        var budget = Result(Parse(growth: "<Growth target='budget' multiplier='1.5' maxValue='22.5'/>", roster: "<Roster mode='combatPower' budget='10'/>") );
        var bs = new ArenaChallengeScheduler(budget); var budgetValues = new List<decimal>();
        while ((match = bs.Next()) != null) { budgetValues.Add(match.ChallengeValue.Value); bs.Resolve(match.Left, new ArenaScore { IsRankable = true, Value = 50 }); }
        check(budgetValues.SequenceEqual(Enumerable.Range(0, 4).SelectMany(_ => new decimal[] { 10, 15, 22.5m })), "CP growth preserves fractional budgets and the configured upper boundary");
        var baseRoster = new ResolvedCombatRoster { Id = "fixed", Mode = "count", PawnKind = "A", ResolvedCount = 2, UnitCombatPower = 15,
            Members = new() { new() { PawnKind = "A", Cost = 10, Seed = 7, PawnTemplateXml = "<PawnTemplate/>" }, new() { PawnKind = "B", Cost = 20, Seed = 8, PawnTemplateXml = "<PawnTemplate/>" } } };
        var before = JsonSerializer.Serialize(baseRoster);
        var scaled = ArenaChallengeRules.CountRoster(baseRoster, 5, 123);
        check(scaled.Members.Select(m => m.PawnKind).SequenceEqual(new[] { "A", "B", "A", "B", "A" }) && scaled.ActualCombatPower == 70
            && scaled.Members[2].Seed != scaled.Members[0].Seed && JsonSerializer.Serialize(baseRoster) == before, "fixed squad growth repeats baseline composition and loadouts without mutating prior snapshots");
        check(ArenaChallengeRules.DeadlineSeconds(limits.Input) > ArenaChallengeRules.DeadlineSeconds(roundRobin.Input), "deadline accounts for all possible growth levels and draw retries");
        check(ArenaScheduler.AttemptSeed(limits.Input, first, 0) != ArenaScheduler.AttemptSeed(limits.Input, new ArenaMatch { Left = first.Left, Right = first.Right, Level = 1 }, 0), "different growth levels have distinct reproducible battle seeds");
        Reject(() => Parse(growth: "<Growth multiplier='1' maxValue='5'/>") , "non-growing multipliers are rejected before launch");
        Reject(() => Parse(growth: "<Growth mode='add' increment='0' maxValue='5'/>"), "zero additive increments are rejected");
        Reject(() => Parse(growth: "<Growth mode='add' increment='1.5' maxValue='5'/>"), "additive member increments must be integers");
        Reject(() => Parse(growth: "<Growth mode='add' increment='1' multiplier='2' maxValue='5'/>"), "additive and multiplicative values cannot be mixed");
        Reject(() => Parse(growth: "<Growth mode='multiply' increment='1' maxValue='5'/>"), "multiplicative mode cannot silently accept an increment");
        Reject(() => Parse(growth: "<Growth multiplier='1.5' maxValue='201'/>") , "count growth cannot bypass the current team member limit");
        Reject(() => Parse(growth: "<Growth multiplier='1.5' maxValue='2.5'/>") , "count growth caps must be integers");
        Reject(() => Parse(growth: "<Growth target='budget' multiplier='2' maxValue='100'/>") , "growth unit and roster allocation unit must agree");
        Reject(() => Parse("bossRoundRobin"), "round robin cannot silently accept growth configuration");
        Reject(() => Parse("unknown"), "unknown challenge format is rejected");
        Reject(() => Parse(growth: "<Growth target='budget' multiplier='1.0000000000000000000000000001' maxValue='100'/>", roster: "<Roster mode='combatPower' budget='0.1'/>") , "decimal growth stagnation is rejected without entering an infinite loop");
    }
}
