using LogSyncMod.AutoTest.Scenarios;

internal static class GroupCombatLifecycleTests
{
    internal static void Run(Action<bool, string> check)
    {
        var teams = new[] { KeyValuePair.Create("A", "A"), KeyValuePair.Create("B", "B") };
        var ledger = new GroupCombatLedger(new[] { new GroupCombatUnit { Id = "a", Team = "A" }, new GroupCombatUnit { Id = "b", Team = "B" } }, teams);
        ledger.SetState("a", "dead", 60); var before = ledger.Snapshot(60);
        check(ledger.AddUnit(new GroupCombatUnit { Id = "summon", Team = "A" }, 60) && !ledger.AddUnit(new GroupCombatUnit { Id = "summon", Team = "A" }, 60), "summoned units register once across repeated lifecycle hooks");
        ledger.Record(new GroupCombatDamageEvent { SourceId = "summon", VictimId = "b", Amount = 20, RawAmount = 20 });
        var after = ledger.Snapshot(120); var team = after.Teams[0];
        check(team.Initial == 1 && team.Joined == 1 && team.Total == 2 && team.Alive == 1 && team.Dead == 1 && team.MeanOutput == 10, "dynamic counts preserve initial population and include reinforcements in mean output");
        check(after.Units.Single(u => u.Id == "summon").AliveTicks == 60 && team.UnrosteredEnemyDamage == 0 && team.EnemyDamage == 20 && before.Units.Count == 2, "summon lifetime starts at arrival, damage is attributed and old snapshots stay detached");
        var rule = new GroupCombatEndRule(300);
        check(!rule.Evaluate(after, 10800), "dead initial roster cannot lose while summoned participants fight");
        ledger.SetState("summon", "dead", 120);
        check(!rule.Evaluate(ledger.Snapshot(120), 10800), "victory confirmation continues simulation for deferred effects");
        ledger.SetPendingRevival("summon", true); var pending = ledger.Snapshot(480);
        check(!rule.Evaluate(pending, 10800) && pending.Teams[0].PendingRevival == 1, "pending resurrection prevents premature loss");
        ledger.SetPendingRevival("summon", false); ledger.SetState("summon", "standing", 480); var revived = ledger.Snapshot(480);
        check(!rule.Evaluate(revived, 10800) && revived.Teams[0].Total == 2 && revived.Units.Single(u => u.Id == "summon").WasDead && !ledger.IsCameraTarget("summon"), "revival reuses identity and resumes combat participation while preserving camera exclusion");
        ledger.SetState("summon", "dead", 600);
        check(!rule.Evaluate(ledger.Snapshot(600), 10800) && !rule.Evaluate(ledger.Snapshot(899), 10800) && rule.Evaluate(ledger.Snapshot(900), 10800), "victory requires an uninterrupted confirmation interval after revival");
        var escape = new GroupCombatRoundResult { Teams = new() { new() { Id = "A", Initial = 1, Escaped = 1 }, new() { Id = "B", Initial = 1, Standing = 1, Alive = 1 } } };
        check(!new GroupCombatEndRule(0).Evaluate(escape, 10800), "escape cannot be silently treated as death or downing");
        var mutual = new GroupCombatRoundResult { Teams = new() { new() { Id = "A", Initial = 1, Dead = 1 }, new() { Id = "B", Initial = 1, Standing = 1, Alive = 1 } } };
        var mutualRule = new GroupCombatEndRule(300); mutualRule.Evaluate(mutual, 10800);
        mutual.ElapsedTicks = 100; mutual.Teams[1].Standing = mutual.Teams[1].Alive = 0; mutual.Teams[1].Dead = 1; mutualRule.Evaluate(mutual, 10800);
        mutual.ElapsedTicks = 300;
        check(!mutualRule.Evaluate(mutual, 10800), "mutual destruction waits for both confirmation windows");
        mutual.ElapsedTicks = 400;
        check(mutualRule.Evaluate(mutual, 10800) && mutual.Teams.All(t => t.Outcome == "draw"), "mutual destruction during confirmation yields a draw");
    }
}
