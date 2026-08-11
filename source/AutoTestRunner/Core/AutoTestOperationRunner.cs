using System;

namespace LogSyncMod.AutoTest
{
    public sealed class AutoTestOperationRunner
    {
        private readonly string runId;
        private readonly IAutoTestClock clock;
        private readonly AutoTestOperationJournal journal;

        public AutoTestOperationRunner(string runId, IAutoTestClock clock, AutoTestOperationJournal journal)
        {
            this.runId = runId;
            this.clock = clock ?? throw new ArgumentNullException("clock");
            this.journal = journal ?? throw new ArgumentNullException("journal");
        }

        public AutoTestOperationEnvelope Run(string capabilityId, string caseId, string stepId, Action action)
        {
            return Run<object>(capabilityId, caseId, stepId, delegate { action(); return null; }, delegate { return "completed"; });
        }

        public AutoTestOperationEnvelope Run<T>(string capabilityId, string caseId, string stepId, Func<T> action, Func<T, string> summarize)
        {
            AutoTestOperationEnvelope envelope = new AutoTestOperationEnvelope
            {
                OperationId = "op_" + Guid.NewGuid().ToString("N"), RunId = runId, CaseId = caseId,
                StepId = stepId, CapabilityId = capabilityId, Status = AutoTestOperationStatus.Running,
                Success = false, StartedAtUtc = clock.UtcNow
            };
            long started = clock.RealtimeMilliseconds;
            journal.RecordStarted(envelope);
            using (AutoTestOperationContext.PushCase(caseId))
            using (AutoTestOperationContext.PushStep(stepId, capabilityId))
            using (AutoTestOperationContext.PushOperation(envelope.OperationId))
            {
                try
                {
                    T result = action();
                    envelope.ResultSummary = summarize == null ? null : summarize(result);
                    envelope.Status = AutoTestOperationStatus.Completed;
                    envelope.Success = true;
                }
                catch (TimeoutException ex) { Fail(envelope, AutoTestOperationStatus.TimedOut, "operation.timed_out", ex); }
                catch (OperationCanceledException ex) { Fail(envelope, AutoTestOperationStatus.Cancelled, "operation.cancelled", ex); }
                catch (Exception ex) { Fail(envelope, AutoTestOperationStatus.Failed, "operation.failed", ex); }
            }
            envelope.CompletedAtUtc = clock.UtcNow;
            envelope.DurationMs = Math.Max(0, clock.RealtimeMilliseconds - started);
            journal.RecordTerminal(envelope);
            return envelope;
        }

        private static void Fail(AutoTestOperationEnvelope envelope, AutoTestOperationStatus status, string code, Exception ex)
        {
            envelope.Status = status;
            envelope.Success = false;
            envelope.Error = new AutoTestOperationError { Code = code, Message = ex.Message, ExceptionType = ex.GetType().FullName, StackTrace = ex.ToString() };
        }
    }
}
