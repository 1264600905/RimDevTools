using System.Text.Json;
using System.Xml.Linq;
using LogSyncMod.AutoTest.Scenarios;
using RimDevTools;

internal static class ArenaResultTests
{
    internal static void Run(Action<bool, string> check)
    {
        ArenaChallengeResultTests.Run(check);
        foreach (int n in new[] { 2, 5, 6, 7, 8, 64 })
        {
            var report = Build(n);
            var error = ArenaResultValidator.ValidateIntegrity(report, report.Plan, verifyArtifactFiles: false);
            check(error == null, "arena independent validator reconstructs " + n + " entrant tournament: " + error);
        }
        var randomSeedReport = Build(5, seed: -1);
        check(randomSeedReport.Plan.Tournaments[0].Seed == 7331 && ArenaResultValidator.ValidateIntegrity(randomSeedReport, randomSeedReport.Plan, false) == null,
            "independent validator accepts a seed=-1 tournament with its once-frozen seed");
        var expectedRandom = JsonSerializer.Deserialize<ArenaPlan>(JsonSerializer.Serialize(randomSeedReport.Plan))!;
        randomSeedReport.Tournaments[0].Input.Seed++;
        check(ArenaResultValidator.ValidateIntegrity(randomSeedReport, expectedRandom, false) != null,
            "independent validator rejects a substituted actual random seed");
        var valid = Build(6);
        foreach (var n in new[] { 5, 6, 8 })
        {
            var random = Build(n, matchmaking: "random");
            check(ArenaResultValidator.ValidateIntegrity(random, random.Plan, false) == null, "independent validator accepts seeded random bracket with " + n + " entrants");
            random.Tournaments[0].Input.MatchmakingMode = "closestCount";
            check(ArenaResultValidator.ValidateIntegrity(random, random.Plan, false) != null, "validator rejects a result-side matchmaking substitution");
        }
        void Reject(Action<ArenaRunReport> mutation, string label)
        {
            var copy = JsonSerializer.Deserialize<ArenaRunReport>(JsonSerializer.Serialize(valid))!; mutation(copy);
            bool rejected;
            try { rejected = ArenaResultValidator.ValidateIntegrity(copy, valid.Plan, false) != null; }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or NullReferenceException) { rejected = true; }
            check(rejected, label);
        }
        Reject(r => r.Tournaments[0].Champion = "not-an-entrant", "arena rejects a forged champion");
        Reject(r => r.Tournaments[0].Matches[0].Score.Value += 1, "arena independently recomputes decimal victory scores");
        Reject(r => r.Tournaments[0].Matches[0].Score.ElapsedTicks += 60, "arena rejects wall-time substitution in score components");
        Reject(r => r.Tournaments[0].Matches[0].Score.PreservationPoints++, "arena independently validates preservation points");
        Reject(r => r.Tournaments[0].Matches[0].Score.SuppressionPoints++, "arena independently validates suppression points");
        Reject(r => r.Tournaments[0].Matches[0].Score.ExchangePoints++, "arena independently validates exchange points");
        Reject(r => r.Tournaments[0].Matches[0].Score.SpeedPoints++, "arena independently validates speed points");
        Reject(r => r.Tournaments[0].Matches[0].Score.EffectiveElapsedTicks++, "arena rejects forged confirmation-free elapsed time");
        Reject(r => r.Tournaments[0].Matches[0].Score.Algorithm = "legacy", "arena rejects obsolete scoring algorithms");
        Reject(r => r.Tournaments[0].Matches[0].Score.EnemyDamage++, "arena validates scoring damage against the entire team ledger");
        Reject(r => r.Tournaments[0].Matches[0].Score.IsRankable = false, "arena rejects advancing with an unusable score");
        Reject(r => r.Tournaments[0].Matches[0].Attempts[0].MeasurementRequirementsMet = false, "arena requires explicit neutral exchange fallback after measurement failure");
        Reject(r => r.Tournaments[0].Matches[0].Attempts[0].MeasurementQuality = "incomplete", "arena rejects unmarked incomplete damage capture");
        var recovered = Build(2);
        var recoveredMatch = recovered.Tournaments[0].Matches[0];
        recoveredMatch.Attempts[0].MeasurementQuality = "incomplete";
        recoveredMatch.Attempts[0].MeasurementRequirementsMet = false;
        recoveredMatch.Score = ArenaScore.Calculate(recovered.Tournaments[0].Input, recoveredMatch.Attempts[0]);
        recovered.Tournaments[0].TopFive.Entries = ArenaStandings.Build(recovered.Tournaments[0]);
        check(ArenaResultValidator.ValidateIntegrity(recovered, recovered.Plan, false) == null, "arena independently validates neutral exchange fallback without discarding measurement failure");
        Reject(r => r.Tournaments[0].Matches[0].Kind = "extra", "arena reconstructs qualification/round/extra kinds");
        Reject(r => r.Tournaments[0].Matches[0].Attempts[0].CleanupComplete = false, "arena never advances a fight with failed cleanup");
        Reject(r => r.Tournaments[0].Matches[0].Attempts[0].Seed++, "arena checks attempt seed against frozen unordered pair identity");
        Reject(r => r.Tournaments[0].Matches[0].Attempts[0].Units[0].State = "downed", "arena rejects survivor counters inconsistent with native unit snapshots");
        Reject(r => r.Tournaments[0].Matches[0].Attempts.Add(r.Tournaments[0].Matches[0].Attempts[0]), "arena rejects replay after an already decisive attempt");
        Reject(r => r.Tournaments[0].Matches.RemoveAt(0), "arena requires exactly N-1 decisive matches");
        Reject(r => r.Tournaments[0].Matches[3].Left = r.Tournaments[0].Matches[0].Right, "arena rejects an eliminated entrant returning in low-score extra match");
        Reject(r => r.Tournaments[0].Matches[0].Attempts[0].Screenshots.RemoveAt(0), "arena requires separate bracket, initial battlefield and final captures");
        Reject(r => r.Tournaments[0].Matches[0].PostBattleScreenshots.Add("unfinished.png"), "arena rejects an invalid post-battle capture name");
        Reject(r => r.Tournaments[0].Matches[0].PostBattleScreenshots.AddRange(new[] { "one_postbattle.png", "two_postbattle.png" }), "arena rejects post-battle captures that do not match attempt count");
        Reject(r => r.Tournaments[0].Matches[0].PreBattles[0].VisibleSeconds = 4.99, "arena requires a full five-second matchup presentation");
        Reject(r => r.Tournaments[0].Matches[0].PreBattles[0].DurationSeconds = 6, "arena matchup duration is fixed at five seconds");
        Reject(r => r.Tournaments[0].Matches[0].PreBattles[0].SimulationTicks = 1, "arena matchup keeps simulation paused");
        Reject(r => r.Tournaments[0].Matches[0].PreBattles[0].Completed = false, "arena rejects an unfinished matchup presentation");
        Reject(r => r.Tournaments[0].Matches[0].PreBattles[0].EntrantIds.Reverse(), "arena matchup cards follow the actual A/B sides");
        Reject(r => r.Tournaments[0].Matches[0].PreBattles[0].Screenshot = "unfinished.png", "arena requires a matchup screenshot");
        Reject(r => r.Tournaments[0].Matches[0].PreBattles.Add(r.Tournaments[0].Matches[0].PreBattles[0]), "arena requires one matchup presentation per attempt");
        var legacy = Build(2);
        foreach (var m in legacy.Tournaments[0].Matches) m.PreBattles.Clear();
        check(ArenaResultValidator.ValidateIntegrity(legacy, legacy.Plan, false) != null, "arena requires matchup presentation evidence for every format");
        var presented = Build(6);
        foreach (var m in presented.Tournaments[0].Matches) m.PostBattleScreenshots = new() { m.Id + "_postbattle.png" };
        check(ArenaResultValidator.ValidateIntegrity(presented, presented.Plan, false) == null, "arena accepts per-attempt post-battle captures while retaining original three battle captures");
        Reject(r => r.Tournaments[0].ChampionScreenshot = null!, "arena requires a final champion capture");
        Reject(r => r.Tournaments[0].TopFive = null!, "arena requires a completed top-five presentation");
        Reject(r => r.Tournaments[0].Rules = null!, "knockout cannot omit mandatory rule evidence");
        Reject(r => r.Tournaments[0].Matches[0].Attempts[0].StudioIsolation = null!, "knockout cannot omit studio isolation evidence");
        Reject(r => r.Tournaments[0].Matches[0].Attempts[0].ArenaMaintenance = null!, "knockout cannot omit maintenance evidence");
        Reject(r => r.Tournaments[0].Matches[0].Attempts[0].ResolvedRosters.Clear(), "knockout cannot omit the actual battle roster");
        Reject(r => r.Tournaments[0].Matches[0].Attempts[0].Units[0].GeneratedSnapshot = null!, "knockout cannot omit generated pawn attributes");
        Reject(r => r.Tournaments[0].Matches[0].Attempts[0].Units[0].InitialSnapshot = null!, "knockout cannot omit initial pawn attributes");
        Reject(r => r.Tournaments[0].Rosters[0].GenerationPolicy = null!, "generation policy cannot disappear to bypass member accounting");
        Reject(r => r.Tournaments[0].TopFive.VisibleSeconds = 9.99, "arena rejects a final page shorter than ten seconds");
        Reject(r => r.Tournaments[0].TopFive.DurationSeconds = 5, "arena requires a fixed ten-second final page");
        Reject(r => r.Tournaments[0].TopFive.SimulationTicks = 1, "arena final page must keep simulation paused");
        Reject(r => r.Tournaments[0].TopFive.Completed = false, "arena rejects an unfinished top-five presentation");
        Reject(r => r.Tournaments[0].TopFive.Entries.Reverse(), "arena independently reconstructs placing order");
        Reject(r => r.Tournaments[0].TopFive.Entries[1].Rank = 1, "arena rejects duplicated placing numbers");
        Reject(r => r.Tournaments[0].TopFive.Entries[1].LastVictoryScore++, "arena checks placing tiebreak scores against the match history");
        Reject(r => r.Tournaments[0].TopFive.Entries.RemoveAt(4), "arena requires five distinct places when enough entrants compete");
        Reject(r => r.Tournaments[0].Rosters[0].UnitCombatPower = 0, "arena count mode still requires positive combatPower");
        Reject(r => r.Tournaments[0].Input.SurvivalWeight++, "arena rejects result-side changes to tournament scoring configuration");
        Reject(r => r.Tournaments[0].Input.ShowcasePages[0].Caption = "篡改文案", "arena rejects result-side changes to XML showcase copy");
        Reject(r => r.Tournaments[0].Input.ShowcasePages[0].Seconds++, "arena rejects result-side changes to XML showcase duration");
        Reject(r => r.Tournaments[0].Input.ShowcasePages[0].EntrantIds.Reverse(), "arena rejects result-side changes to XML showcase order");
        Reject(r => r.Tournaments[0].Rosters[0].ResolvedCount++, "arena rejects actual rosters inconsistent with count mode input");
        Reject(r => r.Tournaments[0].Matches[0].Attempts[0].EndReason = "timeLimitRatio", "arena rejects time-limit decisions before the configured simulated deadline");
        var maintained = Build(2);
        var maintenance = new ArenaMaintenanceResult { CalendarRestored = true, NoonResetTicks = new() { 0 }, NoonResetHours = new() { 12 } };
        maintained.Tournaments[0].Matches[0].Attempts[0].ArenaMaintenance = maintenance;
        check(ArenaResultValidator.ValidateIntegrity(maintained, maintained.Plan, false) == null, "arena accepts original maintenance observations at local noon with restored calendar");
        maintenance.NoonResetHours[0] = 14;
        check(ArenaResultValidator.ValidateIntegrity(maintained, maintained.Plan, false) != null, "arena rejects an incorrectly calibrated local noon");
        maintenance.NoonResetHours[0] = 12; maintenance.CalendarRestored = false;
        check(ArenaResultValidator.ValidateIntegrity(maintained, maintained.Plan, false) != null, "arena rejects an unrestored calendar after cleanup");
        var budget = Build(5, true);
        check(ArenaResultValidator.ValidateIntegrity(budget, budget.Plan, false) == null, "arena budget tournament validates maximal allocation from loaded combatPower snapshots");
        var mixedBudget = Build(25, true, entrantBudget: 3200);
        check(mixedBudget.Tournaments[0].Rosters[0].RequestedBudget == 3200 && mixedBudget.Tournaments[0].Rosters.Skip(1).All(r => r.RequestedBudget == 3000)
            && ArenaResultValidator.ValidateIntegrity(mixedBudget, mixedBudget.Plan, false) == null,
            "arena validates 25 entrants with an individual budget override and inherited budgets");
        mixedBudget.Tournaments[0].Rosters[0].RequestedBudget = 3000;
        check(ArenaResultValidator.ValidateIntegrity(mixedBudget, mixedBudget.Plan, false) != null, "arena rejects a roster that ignores its frozen individual budget override");
        var extreme = Build(2, true, decimal.MaxValue);
        check(ArenaResultValidator.ValidateIntegrity(extreme, extreme.Plan, false) == null, "arena accepts affordable extreme combatPower when the next unit would overflow decimal");
        budget.Tournaments[0].Rosters[0].UnitCombatPower = 1000;
        check(ArenaResultValidator.ValidateIntegrity(budget, budget.Plan, false) != null, "arena rejects claimed combatPower inconsistent with maximal budget count");
    }
    private static ArenaRunReport Build(int count, bool budget = false, decimal? extremePower = null, decimal? entrantBudget = null, string matchmaking = "closestCount", int seed = 12345)
    {
        var xml = new XElement("ArenaSuite", new XAttribute("schemaVersion", 1), new XAttribute("id", "fixture"),
            new XElement("Tournament", new XAttribute("id", "main"), new XAttribute("seed", seed), new XElement("testDefName", "ArenaFixture"), new XElement("Scene", new XAttribute("type", "mainMap"), new XAttribute("sizeX", 100), new XAttribute("sizeZ", 100)),
            budget ? new XElement("Roster", new XAttribute("mode", "combatPower"), new XAttribute("budget", extremePower ?? 3000)) : new XElement("Roster", new XAttribute("mode", "count"), new XAttribute("count", 2)),
            new XElement("Entrants", Enumerable.Range(0, count).Select(i => new XElement("Entrant", new XAttribute("id", "e" + i), new XAttribute("pawnKind", "Kind" + i), i == 0 && entrantBudget.HasValue ? new XAttribute("budget", entrantBudget.Value) : null))),
            new XElement("Battle", new XAttribute("confirmationTicks", 0)), new XElement("Matchmaking", new XAttribute("mode", matchmaking))));
        var plan = ArenaLoader.Compile(new[] { new ScenarioInput { Path = "fixture.xml", Sha256 = "frozen", Xml = xml.ToString(SaveOptions.DisableFormatting) } }, new[] { "ArenaFixture" });
        plan = ArenaLoader.FreezeSeeds(plan, () => 7331);
        var input = plan.Tournaments.Single();
        var t = new ArenaTournamentResult { Input = input };
        foreach (var e in input.Entrants) t.Rosters.Add(CombatRoster.Resolve(e.Id, e.PawnKind, extremePower ?? (budget ? 100 + int.Parse(e.Id[1..]) : 100), e.Budget ?? input.Budget, input.Count));
        foreach (var r in t.Rosters) CombatResultFixture.Freeze(r, input.Seed);
        t.Rules = new ArenaRulesPageResult { Completed = true, VisibleSeconds = 5, Screenshot = "fixture_rules.png" };
        var schedule = new ArenaScheduler(t);
        ArenaMatch m;
        while ((m = schedule.Next()) != null)
        {
            var battle = ArenaLoader.CompileBattle(input, t.Rosters, m, 0);
            var units = new List<GroupCombatUnit>();
            foreach (var (id, team) in new[] { (m.Left, "A"), (m.Right, "B") })
            {
                var r = t.Rosters.Single(r => r.Id == id);
                units.AddRange(Enumerable.Range(0, r.ResolvedCount).Select(i => CombatResultFixture.Unit(r, team, i)));
            }
            var ledger = new GroupCombatLedger(units, new[] { KeyValuePair.Create("A", m.Left), KeyValuePair.Create("B", m.Right) });
            ledger.Record(new GroupCombatDamageEvent { SourceId = "left[0]", VictimId = "right[0]", Amount = 249.8938446044921875, RawAmount = 249.8938446044921875 });
            ledger.Record(new GroupCombatDamageEvent { SourceId = "right[0]", VictimId = "left[0]", Amount = 40.712345678912345, RawAmount = 40.712345678912345 });
            foreach (var u in units.Where(u => u.Team == "B")) ledger.SetState(u.Id, "downed", 600);
            var result = ledger.Snapshot(600, true);
            result.CaseId = battle.CaseId; result.TestDefName = battle.TestDefName; result.Seed = battle.Seed; result.InputHash = battle.InputHash;
            result.CleanupComplete = true; result.ExecutionStatus = "completed";
            result.ResolvedRosters = ArenaChallengeRules.Copy(new List<ResolvedCombatRoster> { t.Rosters.Single(r => r.Id == m.Left), t.Rosters.Single(r => r.Id == m.Right) });
            CombatResultFixture.Environment(result);
            result.Screenshots = new() { m.Id + "_bracket.png", m.Id + "_initial.png", m.Id + "_final.png" };
            new ArenaEndRule(0).Evaluate(result, 10800);
            m.Attempts.Add(result); m.BracketScreenshots.Add(result.Screenshots[0]); m.PostBattleScreenshots.Add(m.Id + "_postbattle.png");
            m.PreBattles.Add(new ArenaPreBattleResult {
                EntrantIds = new() { m.Left, m.Right }, Completed = true, VisibleSeconds = 5,
                Screenshot = m.Id + "_prebattle.png"
            });
            schedule.Resolve(m.Left, ArenaScore.Calculate(input, result));
        }
        t.Champion = schedule.Champion; t.Status = "completed"; t.ChampionScreenshot = "champion.png";
        t.TopFive = new ArenaTopFiveResult { Entries = ArenaStandings.Build(t), Completed = true, VisibleSeconds = 10, Screenshot = t.ChampionScreenshot };
        return new ArenaRunReport { Plan = plan, RunId = "run", Status = "completed", CompletedAtUtc = DateTime.UtcNow.ToString("O"), LoadedMods = new() { "test@1" }, Tournaments = new() { t } };
    }
}
