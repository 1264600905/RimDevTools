using System;

namespace LogSyncMod.AutoTest
{
    public interface IAutoTestClock
    {
        DateTime UtcNow { get; }
        long RealtimeMilliseconds { get; }
    }

    public sealed class AutoTestSystemClock : IAutoTestClock
    {
        private readonly DateTime startedAtUtc = DateTime.UtcNow;
        public DateTime UtcNow { get { return DateTime.UtcNow; } }
        public long RealtimeMilliseconds { get { return (long)(DateTime.UtcNow - startedAtUtc).TotalMilliseconds; } }
    }
}
