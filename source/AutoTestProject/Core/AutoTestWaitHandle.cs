using System;

namespace LogSyncMod.AutoTest
{
    public sealed class AutoTestWaitHandle
    {
        private readonly AutoTestWaitOptions options;
        private readonly Func<AutoTestProbeResult> probe;
        private readonly IAutoTestClock clock;
        private readonly AutoTestOperationJournal journal;
        private readonly AutoTestOperationContextSnapshot context;
        private readonly AutoTestOperationEnvelope waitOperation;
        private readonly long startedMs;
        private int lastPollTick = int.MinValue;
        private AutoTestWaitOutcome outcome = new AutoTestWaitOutcome();

        public string OperationId { get { return waitOperation == null ? context.OperationId : waitOperation.OperationId; } }

        public AutoTestWaitHandle(AutoTestWaitOptions options, Func<AutoTestProbeResult> probe, IAutoTestClock clock, AutoTestOperationJournal journal = null)
        {
            this.options = options ?? throw new ArgumentNullException("options");
            this.probe = probe ?? throw new ArgumentNullException("probe");
            this.clock = clock ?? throw new ArgumentNullException("clock");
            this.journal = journal;
            AutoTestOperationContextSnapshot captured = AutoTestOperationContext.Current;
            startedMs = clock.RealtimeMilliseconds;
            if (journal != null && !string.IsNullOrEmpty(captured.StepId))
            {
                waitOperation = new AutoTestOperationEnvelope
                {
                    OperationId = "op_" + Guid.NewGuid().ToString("N"),
                    RunId = captured.RunId,
                    CaseId = captured.CaseId,
                    StepId = captured.StepId,
                    CapabilityId = captured.CapabilityId,
                    Status = AutoTestOperationStatus.Running,
                    Success = false,
                    StartedAtUtc = clock.UtcNow,
                    ResultSummary = options.ConditionId
                };
                journal.RecordStarted(waitOperation);
                context = new AutoTestOperationContextSnapshot(captured.RunId, captured.CaseId, captured.StepId, captured.CapabilityId, waitOperation.OperationId);
            }
            else
            {
                context = captured;
            }
            if (journal != null) journal.RecordEvent("wait.started", context.RunId, context.CaseId, context.StepId, context.OperationId, context.CapabilityId, options.ConditionId, clock.UtcNow);
        }

        public AutoTestWaitOutcome Poll(int currentGameTick)
        {
            if (outcome.Terminal) return Copy(outcome);
            if (TryTimeout()) return Copy(outcome);
            int interval = Math.Max(0, options.MinimumPollIntervalTicks);
            if (lastPollTick != int.MinValue && currentGameTick - lastPollTick < interval) return Copy(outcome);
            lastPollTick = currentGameTick;
            outcome.Attempts++;
            try
            {
                AutoTestProbeResult result;
                using (AutoTestOperationContext.PushRun(context.RunId))
                using (AutoTestOperationContext.PushCase(context.CaseId))
                using (AutoTestOperationContext.PushStep(context.StepId, context.CapabilityId))
                using (AutoTestOperationContext.PushOperation(context.OperationId))
                    result = probe() ?? new AutoTestProbeResult { Satisfied = false, Message = "Probe returned null." };
                outcome.Message = result.Message;
                outcome.LastSnapshot = result.Snapshot;
                if (result.Satisfied) outcome.StablePolls++; else outcome.StablePolls = 0;
                if (TryTimeout()) return Copy(outcome);
                if (outcome.StablePolls >= Math.Max(1, options.RequiredStablePolls))
                {
                    outcome.Terminal = true;
                    outcome.Satisfied = true;
                    if (journal != null) journal.RecordEvent("wait.satisfied", context.RunId, context.CaseId, context.StepId, context.OperationId, context.CapabilityId, Describe(outcome), clock.UtcNow);
                    CompleteWaitOperation(AutoTestOperationStatus.Completed, null);
                    return Copy(outcome);
                }
            }
            catch (Exception ex)
            {
                outcome.ProbeFailureCount++;
                outcome.StablePolls = 0;
                outcome.LastProbeError = ex.ToString();
                outcome.Message = ex.Message;
                if (journal != null) journal.RecordEvent("wait.probe_failed", context.RunId, context.CaseId, context.StepId, context.OperationId, context.CapabilityId, ex.Message, clock.UtcNow);
            }

            TryTimeout();
            return Copy(outcome);
        }

        private bool TryTimeout()
        {
            if (outcome.Terminal || clock.RealtimeMilliseconds - startedMs < Math.Max(0, options.TimeoutMs)) return false;
            outcome.Terminal = true;
            outcome.TimedOut = true;
            outcome.Satisfied = false;
            if (journal != null) journal.RecordEvent("wait.timed_out", context.RunId, context.CaseId, context.StepId, context.OperationId, context.CapabilityId, Describe(outcome), clock.UtcNow);
            CompleteWaitOperation(AutoTestOperationStatus.TimedOut, new AutoTestOperationError
            {
                Code = "wait.timed_out",
                Message = Describe(outcome),
                ExceptionType = string.IsNullOrEmpty(outcome.LastProbeError) ? null : "wait.probe_failed",
                StackTrace = outcome.LastProbeError
            });
            return true;
        }

        private void CompleteWaitOperation(AutoTestOperationStatus status, AutoTestOperationError error)
        {
            if (waitOperation == null || journal == null || waitOperation.CompletedAtUtc.HasValue) return;
            waitOperation.Status = status;
            waitOperation.Success = status == AutoTestOperationStatus.Completed;
            waitOperation.CompletedAtUtc = clock.UtcNow;
            waitOperation.DurationMs = Math.Max(0, clock.RealtimeMilliseconds - startedMs);
            waitOperation.ResultSummary = Describe(outcome);
            waitOperation.Error = error;
            journal.RecordTerminal(waitOperation);
        }

        public static string Describe(AutoTestWaitOutcome value)
        {
            if (value == null) return string.Empty;
            return "Attempts=" + value.Attempts + "; StablePolls=" + value.StablePolls + "; LastSnapshot=" + (value.LastSnapshot ?? "<none>") + "; LastProbeError=" + (value.LastProbeError ?? "<none>") + "; Message=" + (value.Message ?? string.Empty);
        }

        private static AutoTestWaitOutcome Copy(AutoTestWaitOutcome value)
        {
            return new AutoTestWaitOutcome { Terminal = value.Terminal, Satisfied = value.Satisfied, TimedOut = value.TimedOut,
                Attempts = value.Attempts, StablePolls = value.StablePolls, ProbeFailureCount = value.ProbeFailureCount,
                Message = value.Message, LastSnapshot = value.LastSnapshot, LastProbeError = value.LastProbeError };
        }
    }
}
