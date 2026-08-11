using System;
using System.Threading;

namespace LogSyncMod.AutoTest
{
    public sealed class AutoTestOperationContextSnapshot
    {
        public AutoTestOperationContextSnapshot(string runId, string caseId, string stepId, string capabilityId, string operationId)
        {
            RunId = runId;
            CaseId = caseId;
            StepId = stepId;
            CapabilityId = capabilityId;
            OperationId = operationId;
        }

        public string RunId { get; private set; }
        public string CaseId { get; private set; }
        public string StepId { get; private set; }
        public string CapabilityId { get; private set; }
        public string OperationId { get; private set; }
    }

    public static class AutoTestOperationContext
    {
        private static readonly AsyncLocal<AutoTestOperationContextSnapshot> CurrentState = new AsyncLocal<AutoTestOperationContextSnapshot>();
        public static AutoTestOperationContextSnapshot Current { get { return CurrentState.Value ?? Empty; } }
        private static readonly AutoTestOperationContextSnapshot Empty = new AutoTestOperationContextSnapshot(null, null, null, null, null);

        public static IDisposable PushRun(string runId) { return Push(new AutoTestOperationContextSnapshot(runId, null, null, null, null)); }
        public static IDisposable PushCase(string caseId)
        {
            AutoTestOperationContextSnapshot current = Current;
            return Push(new AutoTestOperationContextSnapshot(current.RunId, caseId, null, null, null));
        }
        public static IDisposable PushStep(string stepId, string capabilityId)
        {
            AutoTestOperationContextSnapshot current = Current;
            return Push(new AutoTestOperationContextSnapshot(current.RunId, current.CaseId, stepId, capabilityId, null));
        }
        public static IDisposable PushOperation(string operationId)
        {
            AutoTestOperationContextSnapshot current = Current;
            return Push(new AutoTestOperationContextSnapshot(current.RunId, current.CaseId, current.StepId, current.CapabilityId, operationId));
        }

        private static IDisposable Push(AutoTestOperationContextSnapshot next)
        {
            AutoTestOperationContextSnapshot previous = CurrentState.Value;
            CurrentState.Value = next;
            return new RestoreScope(previous);
        }

        private sealed class RestoreScope : IDisposable
        {
            private AutoTestOperationContextSnapshot previous;
            public RestoreScope(AutoTestOperationContextSnapshot previous) { this.previous = previous; }
            public void Dispose()
            {
                if (previous == null && CurrentState.Value == null) return;
                CurrentState.Value = previous;
                previous = null;
            }
        }
    }
}
