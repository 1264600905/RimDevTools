using System.Runtime.Serialization;

namespace LogSyncMod.AutoTest
{
    [DataContract]
    public sealed class AutoTestProbeResult
    {
        [DataMember(Name = "satisfied", Order = 1)] public bool Satisfied { get; set; }
        [DataMember(Name = "message", Order = 2)] public string Message { get; set; }
        [DataMember(Name = "snapshot", Order = 3)] public string Snapshot { get; set; }
    }

    [DataContract]
    public sealed class AutoTestWaitOptions
    {
        public AutoTestWaitOptions()
        {
            TimeoutMs = 5000;
            MinimumPollIntervalTicks = 1;
            RequiredStablePolls = 1;
        }

        [DataMember(Name = "conditionId", Order = 1)] public string ConditionId { get; set; }
        [DataMember(Name = "timeoutMs", Order = 2)] public long TimeoutMs { get; set; }
        [DataMember(Name = "minimumPollIntervalTicks", Order = 3)] public int MinimumPollIntervalTicks { get; set; }
        [DataMember(Name = "requiredStablePolls", Order = 4)] public int RequiredStablePolls { get; set; }
    }

    [DataContract]
    public sealed class AutoTestWaitOutcome
    {
        [DataMember(Name = "terminal", Order = 1)] public bool Terminal { get; set; }
        [DataMember(Name = "satisfied", Order = 2)] public bool Satisfied { get; set; }
        [DataMember(Name = "timedOut", Order = 3)] public bool TimedOut { get; set; }
        [DataMember(Name = "attempts", Order = 4)] public int Attempts { get; set; }
        [DataMember(Name = "stablePolls", Order = 5)] public int StablePolls { get; set; }
        [DataMember(Name = "probeFailureCount", Order = 6)] public int ProbeFailureCount { get; set; }
        [DataMember(Name = "message", Order = 7)] public string Message { get; set; }
        [DataMember(Name = "lastSnapshot", Order = 8)] public string LastSnapshot { get; set; }
        [DataMember(Name = "lastProbeError", Order = 9)] public string LastProbeError { get; set; }
    }
}
