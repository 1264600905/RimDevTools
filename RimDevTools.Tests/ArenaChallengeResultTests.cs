using System.Text.Json;
using System.Xml.Linq;
using LogSyncMod.AutoTest.Scenarios;
using RimDevTools;

internal static class ArenaChallengeResultTests
{
    internal static void Run(Action<bool, string> check)
    {
        foreach (var (format, target, draw, losses, bosses) in new[] {
            ("bossRoundRobin", "count", false, false, 2),
            ("limitChallenge", "count", false, false, 2),
            ("limitChallenge", "budget", false, false, 2),
            ("limitChallenge", "count", true, false, 2),
            ("limitChallenge", "count", false, true, 2),
            ("bossRoundRobin", "count", false, false, 10) })
        {
            var report = Build(format, target, draw, losses, bosses);
            var error = ArenaResultValidator.ValidateIntegrity(report, report.Plan, false);
            check(error == null, $"challenge validator accepts {format}/{target}, draw={draw}, capped={losses}, bosses={bosses}: {error}");
        }
        foreach (var target in new[] { "count", "budget" })
        {
            var additive = Build(target: target, add: true);
            check(ArenaResultValidator.ValidateIntegrity(additive, additive.Plan, false) == null, "validator independently accepts additive " + target + " growth");
        }
        var valid = Build();
        valid.Tournaments[0].Rules = new ArenaRulesPageResult { Completed = true, LoadingSeconds = 8, VisibleSeconds = 8, Screenshot = "fixture_rules.png" };
        check(ArenaResultValidator.ValidateIntegrity(valid, valid.Plan, false) == null, "rule page accepts slow loading without requiring a second five second wait");
        var rosterLimit = Build(target: "budget", losses: true, limit: "members");
        var formationLimit = Build(losses: true, limit: "formation");
        check(ArenaResultValidator.ValidateIntegrity(rosterLimit, rosterLimit.Plan, false) == null, "challenge validator accepts a verified CP-to-population limit without fighting an illegal stage");
        check(ArenaResultValidator.ValidateIntegrity(formationLimit, formationLimit.Plan, false) == null, "challenge validator accepts a verified map formation limit with rejected stage evidence");
        void Reject(Action<ArenaRunReport> change, string label, ArenaRunReport? source = null)
        {
            var baseline = source ?? valid;
            var copy = JsonSerializer.Deserialize<ArenaRunReport>(JsonSerializer.Serialize(baseline))!;
            change(copy);
            var rejected = false;
            try { rejected = ArenaResultValidator.ValidateIntegrity(copy, baseline.Plan, false) != null; }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or NullReferenceException or ScenarioConfigurationException) { rejected = true; }
            check(rejected, label);
        }
        Reject(r => r.Tournaments[0].Rules!.VisibleSeconds = 4, "rule page rejects less than five real seconds");
        Reject(r => r.Tournaments[0].Rules!.VisibleSeconds = 7, "rule page cannot finish before preparation completes");
        Reject(r => r.Tournaments[0].Rules!.Completed = false, "rule page requires completion");
        Reject(r => r.Tournaments[0].Challenges.RemoveAt(0), "challenge validator rejects omitted cross-role pairs");
        Reject(r => r.Tournaments[0].Challenges[1] = r.Tournaments[0].Challenges[0], "challenge validator rejects duplicate pairs");
        Reject(r => r.Tournaments[0].Matches[1].ChallengeValue = 3, "challenge validator independently recomputes growth and rounding");
        Reject(r => r.Tournaments[0].Matches[1].Level = 0, "challenge validator rejects reset within an active pair");
        Reject(r => r.Tournaments[0].Matches[2].ChallengeValue = 2, "challenge validator requires an initial value reset for each boss");
        Reject(r => r.Tournaments[0].Matches[0].Score.Value++, "challenge validator independently reconstructs victory scores from the ledger");
        Reject(r => r.Tournaments[0].Summary!.Bosses[0].AverageVictoryScore++, "challenge validator rejects forged average victory score");
        Reject(r => r.Tournaments[0].Summary!.Bosses[0].Wins++, "challenge validator rejects forged boss win count");
        Reject(r => r.Tournaments[0].Challenges[0].WinningValue = 4, "challenge validator rejects forged first winning value");
        Reject(r => r.Tournaments[0].Matches[1].ResolvedRosters[1].ResolvedCount++, "challenge validator rejects stage roster inconsistent with growth");
        Reject(r => r.Tournaments[0].Matches[0].Attempts[0].ResolvedRosters.Clear(), "challenge validator requires actual battle roster evidence");
        Reject(r => r.Tournaments[0].Summary!.Pages.Clear(), "challenge validator requires all final score pages");
        Reject(r => r.Tournaments[0].Summary!.Pages[0].VisibleSeconds = 9, "challenge validator requires ten visible seconds per final page");
        Reject(r => r.Tournaments[0].Summary!.Pages[0].SimulationTicks = 1, "challenge validator rejects simulation progress during final scores");
        Reject(r => r.Tournaments[0].Matches[0].PostBattleScreenshots.Clear(), "challenge validator requires completed post-battle captures");
        Reject(r => r.Tournaments[0].Matches[0].Attempts[0].StudioIsolation = null, "challenge validator requires blank studio isolation evidence");
        Reject(r => r.Tournaments[0].Matches[0].Attempts[0].StudioIsolation!.RemainingThingsBeforeActors = 1, "challenge validator rejects ruins or other native content left before deployment");
        Reject(r => r.Tournaments[0].Matches[0].Attempts[0].StudioIsolation!.WorldTileUnchanged = false, "challenge validator rejects modifying the original world tile to clear the test map");
        var capped = Build(losses: true);
        Reject(r => r.Tournaments[0].Challenges[0].LimitReason = "unknown", "challenge validator rejects fabricated stop reasons", capped);
        Reject(r => r.Tournaments[0].Challenges[0].LimitReason = "COMBAT_ROSTER_LIMIT_EXCEEDED", "challenge validator cannot replace an available stage with a fabricated population limit", capped);
        Reject(r => r.Tournaments[0].Challenges[0].LimitRoster!.ResolvedCount = 1, "challenge validator rejects a fabricated rejected formation roster", formationLimit);
        Reject(r => r.Tournaments[0].Challenges[0].Matches.RemoveAt(2), "challenge validator rejects stopping before an available capped stage", capped);
        var cp = Build(target: "budget");
        var addCount = Build(add: true);
        Reject(r => r.Tournaments[0].Matches[1].ChallengeValue = 2, "additive growth cannot be forged as multiplication", addCount);
        var addBudget = Build(target: "budget", add: true);
        Reject(r => r.Tournaments[0].Matches[1].ChallengeValue = 200, "additive CP budget is independently recomputed", addBudget);
        Reject(r => r.Tournaments[0].Matches[1].ResolvedRosters[1].UnitCombatPower++, "challenge validator rejects altered unit costs while increasing CP budget", cp);
        var rr = Build("bossRoundRobin");
        Reject(r => r.Tournaments[0].Summary!.Bosses[0].AverageVictoryScore /= 2, "round robin average cannot silently include defeat as a zero score", rr);
    }

    private static ArenaRunReport Build(string format = "limitChallenge", string target = "count", bool draw = false, bool losses = false, int bossCount = 2, string? limit = null, bool add = false)
    {
        var xml = new XElement("ArenaSuite", new XAttribute("schemaVersion", 1), new XAttribute("id", "challenges"),
            new XElement("Tournament", new XAttribute("id", "main"), new XAttribute("format", format),
                new XElement("testDefName", "ChallengeFixture"), new XElement("Scene", new XAttribute("type", "mainMap"), new XAttribute("sizeX", limit == "formation" ? 30 : 100), new XAttribute("sizeZ", limit == "formation" ? 30 : 100)),
                new XElement("Roster", new XAttribute("mode", target == "count" ? "count" : "combatPower"), new XAttribute(target == "count" ? "count" : "budget", target == "count" ? 1 : 100)),
                new XElement("Entrants", Enumerable.Range(1, bossCount).Select(i => new XElement("Entrant", new XAttribute("id", "b" + i), new XAttribute("pawnKind", "SameKind"), new XAttribute("role", "boss")))),
                format == "limitChallenge" ? new XElement("Growth", new XAttribute("target", target), new XAttribute(add ? "increment" : "multiplier", limit == "formation" ? 100 : limit == "members" ? 500 : 2), new XAttribute("maxValue", limit == "formation" ? 100 : limit == "members" ? 50000 : target == "count" ? 4 : 400)) : null,
                new XElement("Battle", new XAttribute("confirmationTicks", 0)), new XElement("TieBreak", new XAttribute("maxReplays", 0))));
        if (add) xml.Element("Tournament")!.Element("Growth")!.SetAttributeValue("mode", "add");
        xml.Element("Tournament")!.Element("Entrants")!.Add(Enumerable.Range(1, 2).Select(i => new XElement("Entrant", new XAttribute("id", "c" + i), new XAttribute("pawnKind", "SameKind"), new XAttribute("role", "challenger"))));
        var plan = ArenaLoader.Compile(new[] { new ScenarioInput { Path = "fixture.xml", Sha256 = "frozen", Xml = xml.ToString(SaveOptions.DisableFormatting) } }, new[] { "ChallengeFixture" });
        var input = plan.Tournaments.Single();
        var t = new ArenaTournamentResult { Input = input };
        foreach (var e in input.Entrants) t.Rosters.Add(CombatRoster.Resolve(e.Id, e.PawnKind, 100, input.Budget, input.Count));
        var schedule = new ArenaChallengeScheduler(t);
        ArenaMatch m;
        while ((m = schedule.Next()) != null)
        {
            var baseline = t.Rosters.Single(r => r.Id == m.Right);
            var roster = ArenaChallengeRules.Copy(baseline);
            try
            {
                if (m.Level > 0)
                {
                    if (target == "count") roster = ArenaChallengeRules.CountRoster(baseline, (int)m.ChallengeValue!.Value, SquadTemplates.MemberSeed(input.Seed, m.PairId, m.Level));
                    else roster = CombatRoster.Resolve(baseline.Id, baseline.PawnKind, 100, m.ChallengeValue);
                }
                m.ResolvedRosters = new() { ArenaChallengeRules.Copy(t.Rosters.Single(r => r.Id == m.Left)), roster };
                ArenaLoader.CompileBattle(input, m.ResolvedRosters, m, 0);
            }
            catch (ScenarioConfigurationException ex) when (limit != null && m.Level > 0)
            {
                schedule.ReachRosterLimit(ex.Code, ex.Code == "COMBAT_FORMATION_LIMIT_EXCEEDED" ? roster : null);
                continue;
            }
            var battle = ArenaLoader.CompileBattle(input, m.ResolvedRosters, m, 0);
            var units = new List<GroupCombatUnit>();
            foreach (var (r, team) in new[] { (m.ResolvedRosters[0], "A"), (m.ResolvedRosters[1], "B") })
                units.AddRange(Enumerable.Range(0, r.ResolvedCount).Select(i => new GroupCombatUnit { Id = team + i, Team = team, Kind = r.PawnKind, Name = team + i }));
            var ledger = new GroupCombatLedger(units, new[] { KeyValuePair.Create("A", m.Left), KeyValuePair.Create("B", m.Right) });
            var isDraw = draw && m.Round == 1;
            var bossWins = losses || (format == "bossRoundRobin" ? m.Right == "c1" : m.Left == "b1" && m.Level == 0);
            if (!isDraw)
                foreach (var unit in units.Where(u => u.Team == (bossWins ? "B" : "A"))) ledger.SetState(unit.Id, "downed", 600);
            var result = ledger.Snapshot(isDraw ? 10800 : 600, true);
            result.CaseId = battle.CaseId; result.TestDefName = battle.TestDefName; result.InputHash = battle.InputHash; result.Seed = battle.Seed;
            result.ExecutionStatus = "completed"; result.CleanupComplete = true; result.ResolvedRosters = ArenaChallengeRules.Copy(m.ResolvedRosters);
            result.StudioIsolation = new ArenaStudioIsolationResult { WorldTileUnchanged = true };
            result.Screenshots = new() { m.Id + "_bracket.png", m.Id + "_initial.png", m.Id + "_final.png" };
            new ArenaEndRule(0).Evaluate(result, 10800);
            m.Attempts.Add(result); m.BracketScreenshots.Add(result.Screenshots[0]); m.PostBattleScreenshots.Add(m.Id + "_postbattle.png");
            m.PreBattles.Add(new ArenaPreBattleResult { EntrantIds = new() { m.Left, m.Right }, Completed = true, VisibleSeconds = 5, Screenshot = m.Id + "_prebattle.png" });
            if (isDraw) schedule.ResolveDraw();
            else schedule.Resolve(bossWins ? m.Left : m.Right, ArenaScore.Calculate(input, result));
        }
        t.Status = "completed"; t.Summary = new ArenaChallengeSummary { Bosses = ArenaChallengeRules.Standings(t), Completed = true };
        var entries = ArenaChallengeRules.SummaryEntries(t);
        for (var i = 0; i < entries.Count; i += ArenaChallengeRules.SummaryPageSize)
            t.Summary.Pages.Add(new ArenaSummaryPage { EntryIds = entries.Skip(i).Take(ArenaChallengeRules.SummaryPageSize).ToList(), VisibleSeconds = 10, Screenshot = "summary_" + i + ".png" });
        return new ArenaRunReport { Plan = plan, RunId = "fixture", Status = "completed", CompletedAtUtc = DateTime.UtcNow.ToString("O"), LoadedMods = new() { "test@1" }, Tournaments = new() { t } };
    }
}
