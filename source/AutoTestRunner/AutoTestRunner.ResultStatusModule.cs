using System;

namespace LogSyncMod.AutoTest
{
    internal sealed partial class AutoTestRunner
    {
        private static void CompletePassed(AutoTestResult result, string stage = null, string notes = null)
        {
            CompleteResult(result, AutoTestResultStatus.Passed, null, stage, null, notes);
        }

        private static void CompletePassedAfterRetry(AutoTestResult result, string reasonCode, string stage, string notes)
        {
            CompleteResult(result, AutoTestResultStatus.PassedAfterRetry, reasonCode, stage, null, notes);
        }

        private static void CompleteFailed(AutoTestResult result, string reasonCode, string stage, string errorMessage, string notes = null)
        {
            CompleteResult(result, AutoTestResultStatus.Failed, reasonCode, stage, errorMessage, notes);
        }

        private static void CompleteSkipped(AutoTestResult result, string reasonCode, string stage, string errorMessage, string notes = null)
        {
            CompleteResult(result, AutoTestResultStatus.Skipped, reasonCode, stage, errorMessage, notes);
        }

        private static void CompleteUnsupported(AutoTestResult result, string reasonCode, string stage, string errorMessage, string notes)
        {
            CompleteResult(result, AutoTestResultStatus.Unsupported, reasonCode, stage, errorMessage, notes);
        }

        private static void CompleteCompatibilityWarning(AutoTestResult result, string reasonCode, string stage, string errorMessage, string notes)
        {
            CompleteResult(result, AutoTestResultStatus.CompatibilityWarning, reasonCode, stage, errorMessage, notes);
        }

        private static void CompleteResult(AutoTestResult result, string status, string reasonCode, string stage, string errorMessage, string notes)
        {
            if (result == null) return;
            result.Status = AutoTestResultStatus.Canonicalize(status);
            if (!string.IsNullOrWhiteSpace(stage)) result.Stage = stage;
            result.ErrorMessage = errorMessage ?? string.Empty;
            result.ResultReasonCode = reasonCode ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(notes)) result.Notes = AppendNote(result.Notes, notes);
            if (AutoTestResultStatus.RequiresReason(result.Status))
            {
                if (string.IsNullOrWhiteSpace(result.ResultReasonCode))
                    result.ResultReasonCode = !string.IsNullOrWhiteSpace(result.Stage) ? result.Stage : result.Status;
                AppendResultReasonNote(result);
            }
        }

        private static void NormalizeResultStatus(AutoTestResult result)
        {
            if (result == null) return;
            string originalStatus = result.Status ?? string.Empty;
            if (!AutoTestResultStatus.IsKnown(originalStatus))
            {
                if (string.IsNullOrWhiteSpace(result.ResultReasonCode))
                    result.ResultReasonCode = string.IsNullOrWhiteSpace(originalStatus) ? "missing_terminal_status" : originalStatus;
                result.Notes = AppendNote(result.Notes, "OriginalStatus=" + (string.IsNullOrWhiteSpace(originalStatus) ? "<empty>" : originalStatus));
                result.Status = AutoTestResultStatus.Failed;
            }
            else
            {
                result.Status = AutoTestResultStatus.Canonicalize(originalStatus);
            }

            if (AutoTestResultStatus.RequiresReason(result.Status))
            {
                if (string.IsNullOrWhiteSpace(result.ResultReasonCode))
                    result.ResultReasonCode = !string.IsNullOrWhiteSpace(result.Stage) ? result.Stage : result.Status;
                AppendResultReasonNote(result);
            }
        }

        private static void AppendResultReasonNote(AutoTestResult result)
        {
            if (result == null || string.IsNullOrWhiteSpace(result.ResultReasonCode)) return;
            string note = "ResultReasonCode=" + result.ResultReasonCode;
            if ((result.Notes ?? string.Empty).IndexOf(note, StringComparison.OrdinalIgnoreCase) < 0)
                result.Notes = AppendNote(result.Notes, note);
        }
    }
}
