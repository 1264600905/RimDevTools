using System;

namespace LogSyncMod.AutoTest
{
    internal sealed class InfoCardOperationAdapter
    {
        private readonly string runId;
        private readonly string caseId;
        private readonly AutoTestOperationRunner runner;
        private readonly AutoTestOperationJournal journal;
        private readonly AutoTestEvidenceBuilder evidence;
        private readonly IAutoTestClock clock;
        private readonly AutoTestLogJournal logJournal;
        private long eventAfterSequence;
        private long logAfterSequence;

        public InfoCardOperationAdapter(string runId, string caseId, AutoTestOperationRunner runner, AutoTestOperationJournal journal, AutoTestLogJournal logJournal, AutoTestEvidenceBuilder evidence, IAutoTestClock clock)
        {
            this.runId = runId;
            this.caseId = caseId;
            this.runner = runner;
            this.journal = journal;
            this.logJournal = logJournal;
            this.evidence = evidence;
            this.clock = clock;
        }

        public string CaseId { get { return caseId; } }

        public IDisposable BeginCase()
        {
            IDisposable scope = AutoTestOperationContext.PushCase(caseId);
            eventAfterSequence = journal.LastEventSequence;
            logAfterSequence = logJournal == null ? 0 : logJournal.LastSequence;
            journal.RecordEvent("case.started", runId, caseId, null, null, "ui.info_card", "InfoCard case started.", clock.UtcNow);
            return scope;
        }

        public AutoTestOperationEnvelope Run(string stepId, Action action)
        {
            return runner.Run("ui.info_card", caseId, stepId, action);
        }

        public AutoTestOperationEnvelope Run<T>(string stepId, Func<T> action, Func<T, string> summarize)
        {
            return runner.Run("ui.info_card", caseId, stepId, action, summarize);
        }

        public AutoTestWaitHandle CreateWait(string stepId, AutoTestWaitOptions options, Func<AutoTestProbeResult> probe)
        {
            using (AutoTestOperationContext.PushCase(caseId))
            using (AutoTestOperationContext.PushStep(stepId, "ui.info_card"))
                return new AutoTestWaitHandle(options, probe, clock, journal);
        }

        public void AssertOperation(string name, AutoTestOperationEnvelope operation) { evidence.OperationSucceeded(name, operation); }
        public void AssertTrue(string name, bool actual, string details = null, string operationId = null)
        {
            AutoTestAssertionEvidence assertion = evidence.IsTrue(name, actual, details);
            assertion.OperationId = operationId;
            assertion.CaseId = caseId;
        }
        public void AddCapture(string label, string path, string operationId, string details = null) { evidence.AddCapture(label, "screenshot", path, operationId, details); }

        public void CompleteCase(bool success, string message)
        {
            journal.RecordEvent(success ? "case.completed" : "case.failed", runId, caseId, null, null, "ui.info_card", message, clock.UtcNow);
            evidence.AddCaseWindow(caseId, eventAfterSequence, logAfterSequence, journal, logJournal);
        }
    }
}
