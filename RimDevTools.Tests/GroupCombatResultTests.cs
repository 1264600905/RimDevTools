using System.Text.Json;
using LogSyncMod.AutoTest.Scenarios;
using RimDevTools;

internal static class GroupCombatResultTests
{
    internal static void Run(Action<bool, string> check)
    {
        var round = new GroupCombatRoundResult { CaseId = "Rule/default/1/round-1", TestDefName = "Rule", Seed = 12345, InputHash = "h", ExecutionStatus = "completed", CleanupComplete = true, ElapsedTicks = 300, SimulatedSeconds = 5, TotalEvents = 2 };
        round.Units.AddRange(new[] {
            new GroupCombatUnit { Id = "a0", Team = "A", EnemyDamage = 0, DamageTaken = 4, Dps = 0, AliveTicks = 60, State = "dead" },
            new GroupCombatUnit { Id = "a1", Team = "A", EnemyDamage = 10, DamageTaken = 6, AliveTicks = 300, Dps = 2 },
            new GroupCombatUnit { Id = "b0", Team = "B", EnemyDamage = 3, DamageTaken = 3, AliveTicks = 300, Dps = .6 },
            new GroupCombatUnit { Id = "b1", Team = "B", EnemyDamage = 7, DamageTaken = 7, AliveTicks = 300, Dps = 1.4 }
        });
        foreach (var id in new[] { "A", "B" })
        {
            var units = round.Units.Where(u => u.Team == id).ToList();
            round.Teams.Add(new GroupCombatTeamResult { Id = id, Initial = 2, Standing = id == "A" ? 1 : 2, Dead = id == "A" ? 1 : 0, Alive = id == "A" ? 1 : 2, EnemyDamage = 10, DamageTaken = 10, Dps = 2, MeanDps = id == "A" ? 2 : 1, MeanDamageTaken = id == "A" ? 6 : 5,
                Damage = GroupCombatDistribution.Calculate(units.Select(u => KeyValuePair.Create(u.Id, u.EnemyDamage))), DpsDistribution = GroupCombatDistribution.Calculate(units.Select(u => KeyValuePair.Create(u.Id, u.Dps))), Taken = GroupCombatDistribution.Calculate(units.Select(u => KeyValuePair.Create(u.Id, u.DamageTaken))) });
        }
        round.Matrix.AddRange(new[] { new GroupCombatMatrixCell { Source = "A", Victim = "B", Channel = "pawn", Amount = 10, Events = 1 }, new GroupCombatMatrixCell { Source = "B", Victim = "A", Channel = "pawn", Amount = 10, Events = 1 } });
        round.Events.AddRange(new[] { new GroupCombatDamageEvent { Id = 1, Amount = 10 }, new GroupCombatDamageEvent { Id = 2, Amount = 10 } });
        var report = new GroupCombatRunReport { RunId = "run", Status = "completed", Rounds = new List<GroupCombatRoundResult> { round }, PlannedRounds = new List<GroupCombatRoundInput> { new GroupCombatRoundInput { CaseId = round.CaseId } } };
        check(GroupCombatResultValidator.ValidateIntegrity(report) == null, "group numeric result independently reconciles initial roster, tick clock, matrix, totals, DPS and distributions");
        void Reject(Action<GroupCombatRunReport> mutation, string message)
        {
            var changed = JsonSerializer.Deserialize<GroupCombatRunReport>(JsonSerializer.Serialize(report))!; mutation(changed);
            check(GroupCombatResultValidator.ValidateIntegrity(changed) != null, message);
        }
        Reject(r => r.Rounds[0].Teams[0].EnemyDamage += 1, "group validator rejects team damage inconsistent with initial-unit totals");
        Reject(r => r.Rounds[0].Teams[0].Alive = 2, "group validator rejects survivor counts inconsistent with dead roster members");
        Reject(r => r.Rounds[0].Teams[0].MeanDps = 1, "group validator rejects DPS averaged over initial members instead of currently living units");
        Reject(r => r.Rounds[0].Units[1].AliveTicks = 150, "group validator rejects lifetime DPS inconsistent with the recorded alive ticks");
        Reject(r => r.Rounds[0].Teams[0].MeanDamageTaken = 5, "group validator rejects mean damage taken diluted by dead units");
        Reject(r => r.Rounds[0].Teams[0].Damage.Median = 10, "group validator recomputes even median including zero-output members");
        Reject(r => r.Rounds[0].Teams[0].Damage.MaxIds.Clear(), "group validator requires identities of maximum and minimum units");
        Reject(r => r.Rounds[0].Matrix[0].Amount = 9, "group validator rejects settled damage matrix inconsistent with received totals");
        Reject(r => r.Rounds[0].TotalEvents++, "group validator rejects evidence truncation that silently drops exact event counts");
        Reject(r => r.Rounds[0].Events[1].Id = 1, "group validator rejects duplicated damage invocation identities");
        Reject(r => r.Rounds[0].SimulatedSeconds = 10, "group validator rejects a wall-clock denominator inconsistent with simulated ticks");
        var expected = ScenarioLoader.Compile(new[] { new ScenarioInput { Path = "fixture.xml", Sha256 = "h", Xml = "<TestSuite schemaVersion='1' id='fixture'><Case id='r' kind='groupCombat'><testDefName>Rule</testDefName><Scene type='mainMap'/><Teams><Team id='A'/><Team id='B'/></Teams><Actors><Pawn id='a' team='A' x='20' z='20'><basePawnKind>Colonist</basePawnKind></Pawn><Pawn id='b' team='B' x='25' z='20'><basePawnKind>Colonist</basePawnKind></Pawn></Actors><GroupCombat/></Case></TestSuite>" } }, groupCombat: true);
        ScenarioLoader.SelectRules(expected, new[] { "Rule" });
        check(GroupCombatResultValidator.ValidateIntegrity(report, expected) == null, "group result matches CLI-frozen named definition and seed rather than trusting only its own manifest");
        round.InputHash = "changed";
        check(GroupCombatResultValidator.ValidateIntegrity(report, expected) != null, "group validator rejects output for a different frozen input hash");
    }
}
