using System.Text.Json;
using System.Xml.Linq;
using LogSyncMod.AutoTest.Scenarios;
using RimDevTools;

internal static class ArenaResultTests
{
    internal static void Run(Action<bool, string> check)
    {
        foreach (int n in new[] { 2, 5, 6, 7, 8, 64 })
        {
            var report = Build(n);
            var error = ArenaResultValidator.ValidateIntegrity(report, report.Plan, verifyArtifactFiles: false);
            check(error == null, "arena independent validator reconstructs " + n + " entrant tournament: " + error);
        }
        var valid = Build(6);
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
        var presented = Build(6);
        foreach (var m in presented.Tournaments[0].Matches) m.PostBattleScreenshots.Add(m.Id + "_postbattle.png");
        check(ArenaResultValidator.ValidateIntegrity(presented, presented.Plan, false) == null, "arena accepts per-attempt post-battle captures while retaining original three battle captures");
        Reject(r => r.Tournaments[0].ChampionScreenshot = null!, "arena requires a final champion capture");
        Reject(r => r.Tournaments[0].Rosters[0].UnitCombatPower = 0, "arena count mode still requires positive combatPower");
        Reject(r => r.Tournaments[0].Input.SurvivalWeight++, "arena rejects result-side changes to tournament scoring configuration");
        Reject(r => r.Tournaments[0].Rosters[0].ResolvedCount++, "arena rejects actual rosters inconsistent with count mode input");
        Reject(r => r.Tournaments[0].Matches[0].Attempts[0].EndReason = "timeLimitRatio", "arena rejects time-limit decisions before the configured simulated deadline");
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
    private static ArenaRunReport Build(int count, bool budget = false, decimal? extremePower = null, decimal? entrantBudget = null)
    {
        var xml = new XElement("ArenaSuite", new XAttribute("schemaVersion", 1), new XAttribute("id", "fixture"),
            new XElement("Tournament", new XAttribute("id", "main"), new XElement("testDefName", "ArenaFixture"), new XElement("Scene", new XAttribute("type", "mainMap"), new XAttribute("sizeX", 100), new XAttribute("sizeZ", 100)),
            budget ? new XElement("Roster", new XAttribute("mode", "combatPower"), new XAttribute("budget", extremePower ?? 3000)) : new XElement("Roster", new XAttribute("mode", "count"), new XAttribute("count", 2)),
            new XElement("Entrants", Enumerable.Range(0, count).Select(i => new XElement("Entrant", new XAttribute("id", "e" + i), new XAttribute("pawnKind", "Kind" + i), i == 0 && entrantBudget.HasValue ? new XAttribute("budget", entrantBudget.Value) : null))),
            new XElement("Battle", new XAttribute("confirmationTicks", 0))));
        var plan = ArenaLoader.Compile(new[] { new ScenarioInput { Path = "fixture.xml", Sha256 = "frozen", Xml = xml.ToString(SaveOptions.DisableFormatting) } }, new[] { "ArenaFixture" });
        var input = plan.Tournaments.Single();
        var t = new ArenaTournamentResult { Input = input };
        foreach (var e in input.Entrants) t.Rosters.Add(CombatRoster.Resolve(e.Id, e.PawnKind, extremePower ?? (budget ? 100 + int.Parse(e.Id[1..]) : 100), e.Budget ?? input.Budget, input.Count));
        var schedule = new ArenaScheduler(t);
        ArenaMatch m;
        while ((m = schedule.Next()) != null)
        {
            var battle = ArenaLoader.CompileBattle(input, t.Rosters, m, 0);
            var units = new List<GroupCombatUnit>();
            foreach (var (id, team) in new[] { (m.Left, "A"), (m.Right, "B") })
            {
                var r = t.Rosters.Single(r => r.Id == id);
                units.AddRange(Enumerable.Range(0, r.ResolvedCount).Select(i => new GroupCombatUnit { Id = team + i, Team = team, Kind = r.PawnKind, Name = team + " " + i }));
            }
            var ledger = new GroupCombatLedger(units, new[] { KeyValuePair.Create("A", m.Left), KeyValuePair.Create("B", m.Right) });
            foreach (var u in units.Where(u => u.Team == "B")) ledger.SetState(u.Id, "downed", 600);
            var result = ledger.Snapshot(600, true);
            result.CaseId = battle.CaseId; result.TestDefName = battle.TestDefName; result.Seed = battle.Seed; result.InputHash = battle.InputHash;
            result.CleanupComplete = true; result.ExecutionStatus = "completed";
            result.Screenshots = new() { m.Id + "_bracket.png", m.Id + "_initial.png", m.Id + "_final.png" };
            new ArenaEndRule(0).Evaluate(result, 10800);
            m.Attempts.Add(result); m.BracketScreenshots.Add(result.Screenshots[0]);
            schedule.Resolve(m.Left, ArenaScore.Calculate(input, result.Teams[0].Initial, result.Teams[0].Standing, result.ElapsedTicks));
        }
        t.Champion = schedule.Champion; t.Status = "completed"; t.ChampionScreenshot = "champion.png";
        return new ArenaRunReport { Plan = plan, RunId = "run", Status = "completed", CompletedAtUtc = DateTime.UtcNow.ToString("O"), LoadedMods = new() { "test@1" }, Tournaments = new() { t } };
    }
}
