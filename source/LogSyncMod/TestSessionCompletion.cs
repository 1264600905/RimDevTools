using System.Diagnostics;
using UnityEngine;
using Verse;

namespace LogSyncMod
{
    public static class TestSessionCompletion
    {
        private static readonly object Sync = new object();
        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private static readonly TestSessionExitPolicy Policy = new TestSessionExitPolicy();
        private static bool heartbeatSent;
        // May be requested by quick's timer; Unity APIs are only called by Pump on the main thread.
        public static void Complete(string kind, string runId)
        {
            lock (Sync)
            {
                var settings = LogSyncMod.settings;
                Policy.Request(kind, runId, settings?.testSessionActive == true, settings?.testSessionKind, settings?.autoTestRunId, Clock.ElapsedMilliseconds, RuntimeHeartbeat.Sequence);
            }
        }
        internal static void Pump()
        {
            lock (Sync)
            {
                if (!Policy.Pending) return;
                if (!heartbeatSent) { RuntimeHeartbeat.Pulse(force: true); heartbeatSent = true; }
                var settings = LogSyncMod.settings;
                if (!Policy.ShouldExit(settings?.testSessionActive == true, settings?.testSessionKind, settings?.autoTestRunId, Clock.ElapsedMilliseconds, RuntimeHeartbeat.Sequence)) return;
                Log.Message("[LogSyncMod] CLI_TEST_EXIT runId=" + Policy.RunId + " kind=" + Policy.Kind);
                TestAudioController.Restore("cli_test_completed");
                Application.Quit();
            }
        }
    }
}
