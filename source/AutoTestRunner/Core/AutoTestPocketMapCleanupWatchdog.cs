using System;
using System.Collections.Generic;
using System.Linq;

namespace LogSyncMod.AutoTest
{
    public sealed class AutoTestPocketMapCleanupAttempt
    {
        public string MapId { get; set; }
        public bool ForcedViewRecovery { get; set; }
        public bool Removed { get; set; }
        public Exception Error { get; set; }
    }

    public sealed class AutoTestPocketMapCleanupWatchdog
    {
        private sealed class PendingCleanup
        {
            public string MapId;
            public long NextAttemptAt;
            public Func<bool> IsRemoved;
            public Action<bool> RestoreSourceView;
            public Action Remove;
        }

        private readonly IAutoTestClock clock;
        private readonly long timeoutMs;
        private readonly Dictionary<string, PendingCleanup> pending = new Dictionary<string, PendingCleanup>(StringComparer.Ordinal);

        public AutoTestPocketMapCleanupWatchdog(IAutoTestClock clock, long timeoutMs = 10000)
        {
            this.clock = clock ?? throw new ArgumentNullException("clock");
            if (timeoutMs <= 0) throw new ArgumentOutOfRangeException("timeoutMs");
            this.timeoutMs = timeoutMs;
        }

        public int PendingCount { get { return pending.Count; } }

        public AutoTestPocketMapCleanupAttempt Request(string mapId, Func<bool> isRemoved, Action<bool> restoreSourceView, Action remove)
        {
            if (pending.ContainsKey(mapId)) return null; // Repeated cleanup must not reset the deadline.
            PendingCleanup item = new PendingCleanup { MapId = mapId, IsRemoved = isRemoved, RestoreSourceView = restoreSourceView,
                Remove = remove, NextAttemptAt = clock.RealtimeMilliseconds + timeoutMs };
            pending.Add(mapId, item);
            return Attempt(item, false);
        }

        public List<AutoTestPocketMapCleanupAttempt> Poll()
        {
            List<AutoTestPocketMapCleanupAttempt> attempts = new List<AutoTestPocketMapCleanupAttempt>();
            foreach (PendingCleanup item in pending.Values.ToList())
            {
                try
                {
                    if (item.IsRemoved()) { pending.Remove(item.MapId); continue; }
                }
                catch { /* An unavailable map check must not disable the timed recovery. */ }
                if (clock.RealtimeMilliseconds < item.NextAttemptAt) continue;
                item.NextAttemptAt = clock.RealtimeMilliseconds + timeoutMs;
                attempts.Add(Attempt(item, true));
            }
            return attempts;
        }

        private AutoTestPocketMapCleanupAttempt Attempt(PendingCleanup item, bool force)
        {
            AutoTestPocketMapCleanupAttempt result = new AutoTestPocketMapCleanupAttempt { MapId = item.MapId, ForcedViewRecovery = force };
            try { item.RestoreSourceView(force); item.Remove(); }
            catch (Exception ex) { result.Error = ex; }
            try { result.Removed = item.IsRemoved(); }
            catch (Exception ex) { if (result.Error == null) result.Error = ex; }
            if (result.Removed) pending.Remove(item.MapId);
            return result;
        }
    }
}
