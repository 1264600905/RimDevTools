using System.Text.Json;
using LogSyncMod.AutoTest.Scenarios;
using RimDevTools;

internal static class GroupCombatResultTests
{
    internal static void Run(Action<bool, string> check)
    {
        var round = new GroupCombatRoundResult { CaseId = "Rule/default/1/round-1", TestDefName = "Rule", Seed = 12345, InputHash = "h", ExecutionStatus = "completed", CleanupComplete = true, ElapsedTicks = 300, SimulatedSeconds = 5, TotalEvents = 2 };
        round.Units.AddRange(new[] {
            new GroupCombatUnit { Id = "a[0]", Team = "A", EnemyDamage = 0, DamageTaken = 4, Dps = 0, AliveTicks = 60, State = "dead" },
            new GroupCombatUnit { Id = "a[1]", Team = "A", EnemyDamage = 10, DamageTaken = 6, AliveTicks = 300, Dps = 2 },
            new GroupCombatUnit { Id = "b[0]", Team = "B", EnemyDamage = 3, DamageTaken = 3, AliveTicks = 300, Dps = .6 },
            new GroupCombatUnit { Id = "b[1]", Team = "B", EnemyDamage = 7, DamageTaken = 7, AliveTicks = 300, Dps = 1.4 }
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
        var downedReport = JsonSerializer.Deserialize<GroupCombatRunReport>(JsonSerializer.Serialize(report))!;
        downedReport.Rounds[0].Units[2].State = "downed";
        var downedTeam = downedReport.Rounds[0].Teams[1];
        downedTeam.Standing = downedTeam.Alive = 1; downedTeam.Downed = 1;
        downedTeam.MeanDps = 1.4; downedTeam.MeanDamageTaken = 7;
        check(GroupCombatResultValidator.ValidateIntegrity(downedReport) == null, "group validator accepts downed members excluded from survivor counts and living means");
        downedTeam.Alive = 2;
        check(GroupCombatResultValidator.ValidateIntegrity(downedReport) != null, "group validator rejects including downed members in survivor counts");
        var dynamicReport = JsonSerializer.Deserialize<GroupCombatRunReport>(JsonSerializer.Serialize(report))!;
        var dynamicRound = dynamicReport.Rounds[0];
        dynamicRound.Units.Add(new GroupCombatUnit { Id = "summon", Team = "A", JoinedDuringBattle = true, JoinedAtTick = 300 });
        var dynamicTeam = dynamicRound.Teams[0];
        dynamicTeam.Joined = 1; dynamicTeam.Standing++; dynamicTeam.Alive++; dynamicTeam.MeanDps = 1; dynamicTeam.MeanDamageTaken = 3;
        var dynamicUnits = dynamicRound.Units.Where(u => u.Team == "A").ToList();
        dynamicTeam.Damage = GroupCombatDistribution.Calculate(dynamicUnits.Select(u => KeyValuePair.Create(u.Id, u.EnemyDamage)));
        dynamicTeam.DpsDistribution = GroupCombatDistribution.Calculate(dynamicUnits.Select(u => KeyValuePair.Create(u.Id, u.Dps)));
        dynamicTeam.Taken = GroupCombatDistribution.Calculate(dynamicUnits.Select(u => KeyValuePair.Create(u.Id, u.DamageTaken)));
        check(GroupCombatResultValidator.ValidateIntegrity(dynamicReport) == null && dynamicTeam.Total == 3 && dynamicTeam.MeanOutput == 10d / 3, "group validator reconciles initial plus spawned participants including a same-tick zero-lifetime summon");
        dynamicRound.Units.Last().JoinedAtTick = 301;
        check(GroupCombatResultValidator.ValidateIntegrity(dynamicReport) != null, "group validator rejects summon arrival after battle completion");
        dynamicRound.Units.Last().JoinedAtTick = 300; dynamicTeam.Joined = 0;
        check(GroupCombatResultValidator.ValidateIntegrity(dynamicReport) != null, "group validator rejects missing reinforcement population");
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
        var expected = ScenarioLoader.Compile(new[] { new ScenarioInput { Path = "fixture.xml", Sha256 = "h", Xml = "<TestSuite schemaVersion='1' id='fixture'><Case id='r' kind='groupCombat'><testDefName>Rule</testDefName><Scene type='mainMap'/><Teams><Team id='A'/><Team id='B'/></Teams><Actors><PawnGroup id='a' count='2' team='A' originX='20' originZ='20'><basePawnKind>Colonist</basePawnKind></PawnGroup><PawnGroup id='b' count='2' team='B' originX='25' originZ='20'><basePawnKind>Colonist</basePawnKind></PawnGroup></Actors><GroupCombat/></Case></TestSuite>" } }, groupCombat: true);
        ScenarioLoader.SelectRules(expected, new[] { "Rule" });
        foreach (var id in new[] { "a", "b" })
        {
            var roster = CombatRoster.Resolve(id, "Colonist", 100, null, 2); roster.Team = id == "a" ? "A" : "B";
            CombatResultFixture.Freeze(roster, 12345); round.ResolvedRosters.Add(roster);
        }
        foreach (var unit in round.Units) { unit.Kind = "Colonist"; unit.GeneratedSnapshot = unit.InitialSnapshot = "<PawnTemplate><basePawnKind>Colonist</basePawnKind></PawnTemplate>"; }
        foreach (var unit in round.Units)
        {
            var roster = round.ResolvedRosters.Single(r => unit.Id.StartsWith(r.Id + "[", StringComparison.Ordinal));
            unit.ConfigurationId = roster.Members[0].ConfigurationId; unit.ConfigurationKey = SquadTemplates.ConfigurationKey(roster.Members[0]);
        }
        round.StudioIsolation = new ArenaStudioIsolationResult { WorldTileUnchanged = true };
        check(GroupCombatResultValidator.ValidateIntegrity(report, expected) == null, "group result matches CLI-frozen named definition and seed rather than trusting only its own manifest");
        round.InputHash = "changed";
        check(GroupCombatResultValidator.ValidateIntegrity(report, expected) != null, "group validator rejects output for a different frozen input hash");
    }
}
