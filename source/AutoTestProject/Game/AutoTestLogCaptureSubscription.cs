using System;

namespace LogSyncMod.AutoTest
{
    public sealed class AutoTestLogCaptureSubscription : IDisposable
    {
        private readonly string runId;
        private readonly AutoTestLogJournal journal;
        private bool disposed;

        public AutoTestLogCaptureSubscription(string runId, AutoTestLogJournal journal)
        {
            this.runId = runId;
            this.journal = journal ?? throw new ArgumentNullException("journal");
            global::LogSyncMod.LogPatches.LogCaptured += OnLogCaptured;
        }

        private void OnLogCaptured(global::LogSyncMod.LogSyncCapturedEntry entry)
        {
            if (disposed || entry == null) return;
            AutoTestOperationContextSnapshot context = AutoTestOperationContext.Current;
            journal.Record(entry.Level, entry.Text, entry.SourcePackageId, context.RunId ?? runId, context.CaseId,
                context.StepId, context.OperationId, context.CapabilityId, entry.TimestampUtc);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            global::LogSyncMod.LogPatches.LogCaptured -= OnLogCaptured;
        }
    }
}
