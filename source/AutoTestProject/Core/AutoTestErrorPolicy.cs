using System;
using System.Collections.Generic;
using System.Linq;

namespace LogSyncMod.AutoTest
{
    public sealed class AutoTestErrorPolicy
    {
        public AutoTestErrorPolicyResult Evaluate(IEnumerable<AutoTestLogEvidence> logs, IEnumerable<string> allowedErrorPatterns = null, IEnumerable<string> allowedWarningPatterns = null)
        {
            List<string> errorRules = Normalize(allowedErrorPatterns);
            List<string> warningRules = Normalize(allowedWarningPatterns);
            AutoTestErrorPolicyResult result = new AutoTestErrorPolicyResult();

            foreach (AutoTestLogEvidence log in (logs ?? Enumerable.Empty<AutoTestLogEvidence>()).OrderBy(item => item.Sequence))
            {
                if (log == null) continue;
                string level = AutoTestLogJournal.NormalizeLevel(log.Level);
                if (level == "warning")
                {
                    if (!Matches(log.Message, warningRules, out string warningRule)) result.Warnings.Add(log);
                    else result.AllowedRules.Add("warning:" + warningRule);
                    continue;
                }
                if (level != "error" && level != "fatal") continue;
                bool autoTestDiagnostic = IsAutoTestDiagnostic(log.Message);
                string rule = null;
                bool allowlisted = Matches(log.Message, errorRules, out rule);
                if (autoTestDiagnostic || allowlisted)
                {
                    result.AllowedSevereLogs.Add(log);
                    result.AllowedRules.Add(autoTestDiagnostic ? "autotest-diagnostic" : "error:" + rule);
                }
                else result.UnexpectedSevereLogs.Add(log);
            }

            result.AllowedRules = result.AllowedRules.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            result.Success = result.UnexpectedSevereLogs.Count == 0;
            result.Summary = "unexpectedSevere=" + result.UnexpectedSevereLogs.Count +
                " allowedSevere=" + result.AllowedSevereLogs.Count +
                " warnings=" + result.Warnings.Count +
                " rules=" + string.Join(",", result.AllowedRules.ToArray());
            return result;
        }

        public AutoTestErrorPolicyResult Evaluate(AutoTestLogJournal journal, long afterSequence, string runId, string caseId, IEnumerable<string> allowedErrorPatterns = null, IEnumerable<string> allowedWarningPatterns = null)
        {
            // A game-tick Error/Fatal often occurs outside an operation scope and therefore
            // has no CaseId. The authoritative case boundary is the cursor captured when the
            // case started; filtering by CaseId here would silently miss those asynchronous
            // errors. Keep the run filter and evaluate the complete cursor window.
            List<AutoTestLogEvidence> logs = journal == null ? new List<AutoTestLogEvidence>() : journal.GetEntries(afterSequence, "info", runId, null, null, 2000);
            return Evaluate(logs, allowedErrorPatterns, allowedWarningPatterns);
        }

        private static List<string> Normalize(IEnumerable<string> values)
        {
            return (values ?? Enumerable.Empty<string>()).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static bool Matches(string message, List<string> rules, out string matched)
        {
            message = message ?? string.Empty;
            matched = rules.FirstOrDefault(rule => message.IndexOf(rule, StringComparison.OrdinalIgnoreCase) >= 0);
            return matched != null;
        }

        private static bool IsAutoTestDiagnostic(string message)
        {
            string text = message ?? string.Empty;
            return text.StartsWith("[LogSyncAutoTest]", StringComparison.OrdinalIgnoreCase) &&
                (text.IndexOf("FAILED", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("failure", StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }
}
