using LogSyncMod.AutoTest;
using LogSyncMod.AutoTest.Scenarios;

internal static class CombatTacticsTests
{
    internal static void Run(Action<bool, string> check)
    {
        check(CombatTacticsPolicy.BaitDemand(8, 0, 10) == 2, "a small subset draws pursuit while allies retain ranged fire");
        check(CombatTacticsPolicy.BaitDemand(8, 2, 10) == 0, "existing melee fighters satisfy bait demand");
        check(CombatTacticsPolicy.BaitDemand(8, 0, 0) == 0, "ranged opponents do not cause bait advances or melee charges");
        check(CombatTacticsPolicy.BaitDemand(1, 0, 10) == 0, "the final shooter remains able to shoot");
        check(CombatTacticsPolicy.BaitDemand(3, 0, 1) == 1, "one pursuer needs only one replacement bait");
        check(CombatTacticsPolicy.ShouldEvade(12, 5.5f, true, true), "a chased bait evades before fast melee reaches contact");
        check(!CombatTacticsPolicy.ShouldEvade(12, 5.5f, false, true), "unthreatened shooters do not all retreat with the bait");
        check(!CombatTacticsPolicy.ShouldEvade(40, 5.5f, true, true), "a distant opening preserves firing and formation");
        check(!CombatTacticsPolicy.ShouldEvade(10, 5.5f, true, true, 3, 1), "one pursuer under concentrated supporting fire does not trigger early retreat");
        check(!CombatTacticsPolicy.ShouldEvade(4, 5.5f, false, false), "nearby allies keep shooting when another pawn is being chased");
        check(CombatTacticsPolicy.ShouldEvade(2, 5.5f, false, false, 3, 1), "a shooter already at contact may still make a short escape");
        var budget = new CombatTacticsBudget();
        for (var i = 0; i < 100; i++) { budget.TakeReach(); budget.TakeMove(); }
        check(budget.ReachChecks == 8 && budget.Moves == 4 && !budget.TakeReach() && !budget.TakeMove(), "dense fights cannot exceed the shared per-batch path/move budget");
        budget.Reset();
        check(budget.TakeReach() && budget.TakeMove() && budget.ReachChecks == 1 && budget.Moves == 1, "budget resumes in the next simulation batch");
        check(!CombatTacticsPolicy.WorthMoving(10, 16, false) && CombatTacticsPolicy.WorthMoving(10, 16, true), "small cover improvements preserve aim while immediate threats allow evasive moves");
        check(CombatTacticsPolicy.FireDistance(30, 0, true) < CombatTacticsPolicy.FireDistance(30, 0, false), "melee threats leave range margin for moving targets");
        check(CombatTacticsPolicy.ShouldBreakRangedExposure(2, 5, 6, true), "mixed hold formation breaks exposure when melee and enemy ranged pressure dominate");
        check(!CombatTacticsPolicy.ShouldBreakRangedExposure(8, 2, 2, true), "a superior ranged line can keep firing instead of abandoning its arc");
        check(CombatTacticsPolicy.ShouldSpread(8, 4, true, 180), "ranged teams spread after securing a firing opportunity");
        check(!CombatTacticsPolicy.ShouldSpread(1, 4, true, 300), "a lone shooter does not waste time on formation spreading");
        var fixtures = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures");
        foreach (var name in new[] { "GroupCombatTactics.autotest.xml", "ArenaTactics.autotest.xml" })
        {
            var path = Path.GetFullPath(Path.Combine(fixtures, name));
            var inputs = new[] { new ScenarioInput { Path = path, Xml = File.ReadAllText(path) } };
            check(name.StartsWith("Arena") ? ArenaLoader.Compile(inputs, new[] { "Arena_TacticsHold", "Arena_TacticsLure" }).Tournaments.Count == 2
                : ScenarioLoader.Compile(inputs, true).Cases.Count >= 9, "tactical fixtures compile through the shared schema: " + name);
        }
    }
}
