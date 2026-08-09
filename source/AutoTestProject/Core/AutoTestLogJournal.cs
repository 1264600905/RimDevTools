using System;
using System.Collections.Generic;
using System.Linq;

namespace LogSyncMod.AutoTest
{
    public sealed class AutoTestLogJournal
    {
        private readonly int capacity;
        private readonly int maxMessageCharacters;
        private readonly List<AutoTestLogEvidence> entries = new List<AutoTestLogEvidence>();
        private readonly object gate = new object();
        private long nextSequence;

        public AutoTestLogJournal(int capacity = 2000, int maxMessageCharacters = 16384)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException("capacity");
            this.capacity = capacity;
            this.maxMessageCharacters = Math.Max(256, maxMessageCharacters);
        }

        public AutoTestLogEvidence Record(string level, string message, string sourcePackageId, string runId, string caseId, string stepId, string operationId, string capabilityId, DateTime? timestampUtc = null)
        {
            string normalizedLevel = NormalizeLevel(level);
            string normalizedMessage = Truncate(message ?? string.Empty, maxMessageCharacters);
            DateTime timestamp = timestampUtc ?? DateTime.UtcNow;
            lock (gate)
            {
                AutoTestLogEvidence last = entries.Count == 0 ? null : entries[entries.Count - 1];
                if (last != null && last.Level == normalizedLevel && last.Message == normalizedMessage &&
                    last.RunId == runId && last.CaseId == caseId && last.OperationId == operationId &&
                    (timestamp - last.TimestampUtc).TotalSeconds <= 5)
                {
                    last.RepeatCount++;
                    last.TimestampUtc = timestamp;
                    return Clone(last);
                }

                AutoTestLogEvidence item = new AutoTestLogEvidence
                {
                    Sequence = ++nextSequence, TimestampUtc = timestamp, Level = normalizedLevel, Message = normalizedMessage,
                    SourcePackageId = sourcePackageId, RepeatCount = 1, RunId = runId, CaseId = caseId,
                    StepId = stepId, OperationId = operationId, CapabilityId = capabilityId
                };
                entries.Add(item);
                if (entries.Count > capacity) entries.RemoveAt(0);
                return Clone(item);
            }
        }

        public List<AutoTestLogEvidence> GetEntries(long afterSequence, string minimumLevel = null, string runId = null, string caseId = null, string operationId = null, int limit = 2000)
        {
            int minimum = Rank(NormalizeLevel(minimumLevel ?? "info"));
            lock (gate)
            {
                return entries.Where(item => item.Sequence > afterSequence && Rank(item.Level) >= minimum &&
                    (runId == null || item.RunId == runId) && (caseId == null || item.CaseId == caseId) &&
                    (operationId == null || item.OperationId == operationId))
                    .OrderBy(item => item.Sequence).Take(Math.Max(0, limit)).Select(Clone).ToList();
            }
        }

        public long LastSequence { get { lock (gate) return nextSequence; } }

        public static string NormalizeLevel(string level)
        {
            string value = (level ?? string.Empty).Trim().ToLowerInvariant();
            if (value == "fatal") return "fatal";
            if (value == "error" || value == "exception") return "error";
            if (value == "warning" || value == "warn") return "warning";
            return "info";
        }

        private static int Rank(string level) { return level == "fatal" ? 4 : level == "error" ? 3 : level == "warning" ? 2 : 1; }
        private static string Truncate(string value, int length) { return value.Length <= length ? value : value.Substring(0, length); }
        private static AutoTestLogEvidence Clone(AutoTestLogEvidence item)
        {
            return new AutoTestLogEvidence { Sequence = item.Sequence, TimestampUtc = item.TimestampUtc, Level = item.Level,
                Message = item.Message, SourcePackageId = item.SourcePackageId, RepeatCount = item.RepeatCount,
                RunId = item.RunId, CaseId = item.CaseId, StepId = item.StepId, OperationId = item.OperationId, CapabilityId = item.CapabilityId };
        }
    }
}
