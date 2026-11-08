using LogSyncMod.AutoTest.Scenarios;

internal static class GroupCombatOverlayTests
{
    internal static void Run(Action<bool, string> check)
    {
        GroupCombatRoundResult Round(bool final = false) => new()
        {
            SimulatedSeconds = final ? 5 : 1,
            Teams = new() {
                new() { Id = "A", Name = "Alpha", Initial = 3, Standing = final ? 1 : 2, Dead = final ? 2 : 1, Outcome = final ? "loss" : "undetermined" },
                new() { Id = "B", Name = "Beta", Initial = 1, Standing = 1, Outcome = final ? "win" : "undetermined" }
            },
            Units = new() {
                new() { Id = "a2", Team = "A", Name = "Two", Kind = "Human", KindLabel = "人类", EnemyDamage = 10, DamageTaken = 1 },
                new() { Id = "a1", Team = "A", Name = "One", Kind = "Human", KindLabel = "人类", EnemyDamage = 10, DamageTaken = 100, State = final ? "dead" : "standing" },
                new() { Id = "a0", Team = "A", Name = "Dead", Kind = "Other", KindLabel = "其他", EnemyDamage = 50, DamageTaken = 50, State = "dead" },
                new() { Id = "b1", Team = "B", Name = "Beta", Kind = "Other", EnemyDamage = 90, DamageTaken = 30 }
            }
        };
        var data = new GroupCombatOverlayData();
        var initial = Round();
        check(data.Refresh(initial, "running", 0), "overlay builds its first snapshot immediately");
        var team = data.Teams[0];
        check(team.Output.Select(r => r.Unit.Id).SequenceEqual(new[] { "a0", "a1", "a2" })
            && team.Taken.Select(r => r.Unit.Id).SequenceEqual(new[] { "a1", "a0", "a2" }), "cached rankings retain dead units and deterministic tie ordering");
        check(team.LivingGroups.Length == 1 && team.LivingGroups[0].Label == "人类 ×2" && team.IntroGroups.Length == 2,
            "overlay caches living composition separately from the complete intro roster");
        check(data.MaximumOutput == 90 && data.MaximumTaken == 100, "cached bar maxima compare both teams");
        check(team.Settlement[3].Contains("20") && team.Settlement[4].Contains("101"), "cached survivor totals exclude dead members");
        var updated = Round(true);
        check(!data.Refresh(updated, "running", .24f) && ReferenceEquals(team, data.Teams[0]) && data.RefreshCount == 1,
            "overlay reuses rankings and formatted text within the refresh interval");
        check(data.Refresh(updated, "running", .25f) && data.Teams[0].LivingGroups[0].Label == "人类 ×1"
            && data.Teams[0].Output[1].DetailedValue.EndsWith("死亡"), "overlay refreshes deaths and rank labels at the next interval");
        check(!data.Refresh(updated, "running", 10) && data.RefreshCount == 2, "paused unchanged snapshots do not rebuild panel data");
        check(data.Refresh(updated, "finishing", .26f) && data.Teams[0].Outcome.StartsWith("落败"), "phase changes bypass throttling to show final results immediately");
        var joined = Round(true);
        joined.Units.Add(new() { Id = "joined", Team = "A", Kind = "New", Name = "Join", EnemyDamage = 200, JoinedDuringBattle = true });
        joined.Teams[0].Joined = 1;
        check(data.Refresh(joined, "finishing", 1) && data.Teams[0].Output[0].Unit.Id == "joined"
            && data.Teams[0].Reinforcements != null && data.Teams[0].Hero.Id != "joined", "new participants enter cached rankings while settlement hero retains initial-roster eligibility");
        var many = new GroupCombatRoundResult { Teams = new() { new() { Id = "A" } } };
        for (var i = 0; i < 20; i++) many.Units.Add(new() { Id = i.ToString("D2"), Team = "A", Kind = "K" + i, Name = "N" + i, EnemyDamage = i });
        data.Refresh(many, "running", 2);
        check(data.Teams[0].Output.Length == 10 && data.Teams[0].LivingGroups.Length == 5 && data.Teams[0].RemainingKinds.Contains("15"),
            "cached panels retain Top10 and five-group display limits");
        data.Refresh(new GroupCombatRoundResult { Teams = new() { new() { Id = "A" } } }, "running", 3);
        check(data.MaximumOutput == 0 && data.Teams[0].Output.Length == 0 && data.Teams[0].RemainingKinds == "零存活", "empty teams have a valid cached display without Max exceptions");
    }
}
