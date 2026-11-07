internal static class TestSessionExitTests
{
    internal static void Run(Action<bool, string> check)
    {
        foreach (var kind in new[] { "quick", "auto", "groupcombat", "manual" })
        {
            var gate = new LogSyncMod.TestSessionExitPolicy();
            check(gate.Request(kind, "run", true, kind, "run", 100, 4) && !gate.ShouldExit(true, kind, "run", 1099, 5) && gate.ShouldExit(true, kind, "run", 1100, 5) && !gate.ShouldExit(true, kind, "run", 1200, 6), "CLI " + kind + " completion exits once after a later main-thread heartbeat and artifact flush grace");
        }
        var regular = new LogSyncMod.TestSessionExitPolicy();
        check(!regular.Request("manual", "run", false, "manual", "run", 0, 0) && !regular.Pending, "ordinary in-game manual tests never arm game exit");
        check(!regular.Request("auto", "foreign", true, "auto", "run", 0, 0) && !regular.Request("quick", "run", true, "manual", "run", 0, 0), "stale run and wrong-mode completion cannot close a CLI session");
        var pending = new LogSyncMod.TestSessionExitPolicy(); pending.Request("auto", "run", true, "auto", "run", 0, 8);
        check(!pending.ShouldExit(true, "auto", "run", 5000, 8), "native exit waits for a later main-thread heartbeat rather than background ready alone");
        pending.Request("auto", "run", true, "auto", "run", 900, 8);
        check(pending.ShouldExit(true, "auto", "run", 1000, 9), "repeated completion does not indefinitely reset the exit deadline");
        var changed = new LogSyncMod.TestSessionExitPolicy(); changed.Request("auto", "run", true, "auto", "run", 0, 0);
        check(!changed.ShouldExit(true, "auto", "other", 2000, 1) && !changed.ShouldExit(false, "auto", "run", 2000, 1), "native exit rechecks active session identity immediately before quitting");
    }
}
