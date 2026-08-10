using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;

namespace LogSyncMod.AutoTest
{
    public sealed class AutoTestEvidenceBuilder
    {
        private readonly AutoTestEvidenceManifest manifest;
        private readonly IAutoTestClock clock;

        public AutoTestEvidenceBuilder(AutoTestEvidenceManifest manifest, IAutoTestClock clock)
        {
            this.manifest = manifest ?? throw new ArgumentNullException("manifest");
            this.clock = clock ?? throw new ArgumentNullException("clock");
        }

        public AutoTestAssertionEvidence Pass(string name, string actual, string details = null) { return AddAssertion(name, true, null, actual, details); }
        public AutoTestAssertionEvidence Fail(string name, string expected, string actual, string details = null) { return AddAssertion(name, false, expected, actual, details); }
        public AutoTestAssertionEvidence AreEqual(string name, object expected, object actual) { return AddAssertion(name, Equals(expected, actual), Convert.ToString(expected), Convert.ToString(actual), null); }
        public AutoTestAssertionEvidence IsTrue(string name, bool actual, string details = null) { return AddAssertion(name, actual, "True", actual.ToString(), details); }
        public AutoTestAssertionEvidence Assert(string name, bool success, string expected, string actual, string details = null) { return AddAssertion(name, success, expected, actual, details); }
        public AutoTestAssertionEvidence OperationSucceeded(string name, AutoTestOperationEnvelope envelope)
        {
            AutoTestAssertionEvidence assertion = AddAssertion(name, envelope != null && envelope.Success, "Completed", envelope == null ? "Missing" : envelope.Status.ToString(), envelope == null || envelope.Error == null ? null : envelope.Error.Message);
            if (envelope != null)
            {
                assertion.OperationId = envelope.OperationId;
                assertion.CaseId = envelope.CaseId;
                assertion.StepId = envelope.StepId;
            }
            return assertion;
        }

        public AutoTestCaptureEvidence AddCapture(string label, string kind, string path, string operationId = null, string details = null)
        {
            AutoTestOperationContextSnapshot context = AutoTestOperationContext.Current;
            AutoTestCaptureEvidence value = new AutoTestCaptureEvidence { CaptureId = "cap_" + Guid.NewGuid().ToString("N"), Label = label,
                Kind = kind, Path = path, OperationId = operationId ?? context.OperationId, CaseId = context.CaseId, Details = details };
            manifest.Captures.Add(value);
            return value;
        }

        public void AddError(AutoTestOperationError error) { if (error != null) manifest.Errors.Add(error); }

        public AutoTestCaseEvidence AddCaseWindow(string caseId, long eventAfterSequence, long logAfterSequence, AutoTestOperationJournal operationJournal, AutoTestLogJournal logJournal, long eventThroughSequence = long.MaxValue, long logThroughSequence = long.MaxValue)
        {
            AutoTestCaseEvidence value = new AutoTestCaseEvidence
            {
                CaseId = caseId,
                EventAfterSequence = eventAfterSequence,
                LogAfterSequence = logAfterSequence,
                EventThroughSequence = eventThroughSequence,
                LogThroughSequence = logThroughSequence,
                Operations = operationJournal == null ? new AutoTestOperationEnvelope[0].ToList() : operationJournal.GetRecentOperations(500, true).Where(item => item.CaseId == caseId).ToList(),
                Events = operationJournal == null ? new AutoTestEventEvidence[0].ToList() : operationJournal.GetEvents(eventAfterSequence, 2000).Where(item => item.Sequence <= eventThroughSequence && item.CaseId == caseId).ToList(),
                Logs = logJournal == null ? new AutoTestLogEvidence[0].ToList() : logJournal.GetEntries(logAfterSequence, "info", manifest.RunId, null, null, 2000, logThroughSequence)
                    .Where(item => string.IsNullOrEmpty(item.CaseId) || string.Equals(item.CaseId, caseId, StringComparison.OrdinalIgnoreCase)).ToList()
            };
            manifest.Cases.Add(value);
            return value;
        }

        public AutoTestEvidenceManifest Complete(AutoTestOperationJournal operationJournal = null, AutoTestLogJournal logJournal = null)
        {
            if (operationJournal != null)
            {
                manifest.Operations = operationJournal.GetRecentOperations(500, true);
                manifest.Events = operationJournal.GetEvents(0, 2000);
                foreach (AutoTestOperationError operationError in manifest.Operations.Where(item => item.Error != null).Select(item => item.Error))
                {
                    if (!manifest.Errors.Any(item => item.Code == operationError.Code && item.Message == operationError.Message && item.ExceptionType == operationError.ExceptionType))
                        manifest.Errors.Add(operationError);
                }
            }
            if (logJournal != null) manifest.Logs = logJournal.GetEntries(0, "info", manifest.RunId, null, null, 2000);
            manifest.CompletedAtUtc = clock.UtcNow;
            manifest.Success = manifest.Assertions.All(item => item.Success) && manifest.Errors.Count == 0 &&
                manifest.Operations.Where(item => item.Required).All(item => item.Status == AutoTestOperationStatus.Completed);
            return manifest;
        }

        private AutoTestAssertionEvidence AddAssertion(string name, bool success, string expected, string actual, string details)
        {
            AutoTestOperationContextSnapshot context = AutoTestOperationContext.Current;
            AutoTestAssertionEvidence value = new AutoTestAssertionEvidence { AssertionId = "assert_" + Guid.NewGuid().ToString("N"), Name = name,
                Success = success, Expected = expected, Actual = actual, Details = details, OperationId = context.OperationId,
                CaseId = context.CaseId, StepId = context.StepId };
            manifest.Assertions.Add(value);
            return value;
        }
    }

    public static class AutoTestEvidenceSerializer
    {
        public static void Write(string path, AutoTestEvidenceManifest manifest)
        {
            using (FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
                new DataContractJsonSerializer(typeof(AutoTestEvidenceManifest)).WriteObject(stream, manifest);
        }

        public static AutoTestEvidenceManifest Read(string path)
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                return (AutoTestEvidenceManifest)new DataContractJsonSerializer(typeof(AutoTestEvidenceManifest)).ReadObject(stream);
        }
    }
}
