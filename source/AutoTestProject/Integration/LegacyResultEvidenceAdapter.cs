using System;
using System.Collections.Generic;

namespace LogSyncMod.AutoTest
{
    internal static class LegacyResultEvidenceAdapter
    {
        public static void AddResults(AutoTestEvidenceBuilder builder, IEnumerable<AutoTestResult> results)
        {
            if (builder == null || results == null) return;
            foreach (AutoTestResult result in results)
            {
                if (result == null) continue;
                string name = "legacy_result." + (result.CaseId ?? result.DefName ?? Guid.NewGuid().ToString("N"));
                bool success = string.Equals(result.Status, "passed", StringComparison.OrdinalIgnoreCase);
                if (success) builder.Pass(name, result.Status, result.Stage);
                else builder.Fail(name, "passed", result.Status, (result.Stage ?? string.Empty) + ": " + (result.ErrorMessage ?? string.Empty));
            }
        }
    }
}
