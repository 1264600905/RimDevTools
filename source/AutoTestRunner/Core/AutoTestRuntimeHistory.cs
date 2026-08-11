using System;

namespace LogSyncMod.AutoTest
{
    public sealed class AutoTestRuntimeHistory
    {
        private readonly object gate = new object();
        private AutoTestEvidenceSnapshotDto lastSnapshot;
        private string lastRunId;

        public string LastRunId { get { lock (gate) return lastRunId; } }
        public AutoTestEvidenceSnapshotDto LastSnapshot { get { lock (gate) return Clone(lastSnapshot); } }

        public void Retain(string runId, AutoTestEvidenceSnapshotDto snapshot)
        {
            if (string.IsNullOrWhiteSpace(runId) || snapshot == null) return;
            lock (gate)
            {
                lastRunId = runId;
                lastSnapshot = Clone(snapshot);
                lastSnapshot.RunId = runId;
            }
        }

        public AutoTestEvidenceSnapshotDto GetEvidence(string runId)
        {
            lock (gate)
                return lastSnapshot != null && string.Equals(lastRunId, runId, StringComparison.OrdinalIgnoreCase) ? Clone(lastSnapshot) : null;
        }

        private static AutoTestEvidenceSnapshotDto Clone(AutoTestEvidenceSnapshotDto value)
        {
            if (value == null) return null;
            return new AutoTestEvidenceSnapshotDto
            {
                Found = value.Found,
                RunId = value.RunId,
                Success = value.Success,
                OperationCount = value.OperationCount,
                AssertionCount = value.AssertionCount,
                FailedAssertionCount = value.FailedAssertionCount,
                ErrorCount = value.ErrorCount,
                CaseCount = value.CaseCount,
                ErrorCode = value.ErrorCode
            };
        }
    }
}
