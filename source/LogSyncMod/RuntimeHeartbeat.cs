using System;
using System.Diagnostics;
using System.Threading;
using HarmonyLib;
using Verse;

namespace LogSyncMod
{
    internal static class RuntimeHeartbeat
    {
        private const long IntervalMilliseconds = 5000;
        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private static long lastHeartbeatAt = -IntervalMilliseconds;
        private static long sequence;

        public static void Pulse()
        {
            LogSyncSettings settings = LogSyncMod.settings;
            string runId = settings?.autoTestRunId;
            if (string.IsNullOrWhiteSpace(runId)) return;

            long now = Clock.ElapsedMilliseconds;
            if (now - Volatile.Read(ref lastHeartbeatAt) < IntervalMilliseconds) return;

            Volatile.Write(ref lastHeartbeatAt, now);
            long nextSequence = Interlocked.Increment(ref sequence);
            Log.Message("[LogSyncAutoTest] HEARTBEAT runId=" + runId + " seq=" + nextSequence);
        }
    }

    [HarmonyPatch(typeof(Root_Play), nameof(Root_Play.Update))]
    internal static class Root_Play_Update_RuntimeHeartbeatPatch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            RuntimeHeartbeat.Pulse();
        }
    }

    [HarmonyPatch(typeof(Root_Entry), nameof(Root_Entry.Update))]
    internal static class Root_Entry_Update_RuntimeHeartbeatPatch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            RuntimeHeartbeat.Pulse();
        }
    }
}
