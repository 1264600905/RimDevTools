using System;
using UnityEngine;

namespace LogSyncMod.AutoTest
{
    public sealed class AutoTestGameClock : IAutoTestClock
    {
        public DateTime UtcNow { get { return DateTime.UtcNow; } }
        public long RealtimeMilliseconds { get { return (long)(Time.realtimeSinceStartup * 1000f); } }
    }
}
