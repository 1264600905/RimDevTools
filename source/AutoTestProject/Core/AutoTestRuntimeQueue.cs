using System;

namespace LogSyncMod.AutoTest
{
    public static class AutoTestRuntimeQueue
    {
        public static bool MatchesQueuedRun(string requestedRunId, string queuedRunId, bool enabled)
        {
            return enabled && !string.IsNullOrWhiteSpace(requestedRunId) &&
                string.Equals(requestedRunId, queuedRunId, StringComparison.OrdinalIgnoreCase);
        }
    }
}
