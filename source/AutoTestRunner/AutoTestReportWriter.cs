using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text;

namespace LogSyncMod.AutoTest
{
    internal sealed class AutoTestRunArtifacts
    {
        public string OutputDirectory;
        public string SafePackageId;
        public string Stamp;
        public string BasePath;
        public string ReportPath;
        public string ResultPath;
        public string ManifestPath;
        public string EvidencePath;
        public string ScreenshotDirectory;
    }

    internal static class AutoTestReportWriter
    {
        private const int MaxArchivedRuns = 2;

        public static AutoTestRunArtifacts CreateRunArtifacts(string outputDirectory, string packageId, string runId)
        {
            Directory.CreateDirectory(outputDirectory);

            string safePackageId = Sanitize(packageId);
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string basePath = Path.Combine(outputDirectory, safePackageId + "_" + Sanitize(runId));
            string runBaseName = Path.GetFileName(basePath);
            string screenshotDirectory = basePath + "_Screenshots";

            return new AutoTestRunArtifacts
            {
                OutputDirectory = outputDirectory,
                SafePackageId = safePackageId,
                Stamp = stamp,
                BasePath = basePath,
                ReportPath = Path.Combine(screenshotDirectory, runBaseName + "_Report.md"),
                ResultPath = basePath + "_Result.json",
                ManifestPath = Path.Combine(screenshotDirectory, runBaseName + "_Manifest.json"),
                EvidencePath = Path.Combine(screenshotDirectory, runBaseName + "_Evidence.json"),
                ScreenshotDirectory = screenshotDirectory
            };
        }

        public static void Write(
            AutoTestRunArtifacts artifacts,
            string packageId,
            string runId,
            List<AutoTestResult> results,
            string modName,
            List<AutoTestManifestEntry> discoveredManifest,
            List<AutoTestManifestEntry> plannedManifest,
            AutoTestRunSummary runSummary,
            AutoTestEvidenceManifest evidence)
        {
            try
            {
                if (artifacts == null)
                {
                    throw new ArgumentNullException("artifacts");
                }

                Directory.CreateDirectory(artifacts.OutputDirectory);
                Directory.CreateDirectory(artifacts.ScreenshotDirectory);
                if (evidence != null) AutoTestEvidenceSerializer.Write(artifacts.EvidencePath, evidence);
                File.WriteAllText(artifacts.ReportPath, BuildMarkdown(artifacts, packageId, modName, results, runSummary, evidence), Encoding.UTF8);
                File.WriteAllText(artifacts.ResultPath, BuildJson(artifacts, packageId, runId, modName, results, runSummary, evidence), Encoding.UTF8);
                File.WriteAllText(artifacts.ManifestPath, BuildManifestJson(packageId, modName, discoveredManifest, plannedManifest, runSummary), Encoding.UTF8);
                PruneArchives(artifacts.OutputDirectory, artifacts.SafePackageId);
            }
            catch (Exception ex)
            {
                Verse.Log.Error("[LogSyncAutoTest] Failed to write reports: " + ex);
            }
        }

        private static string BuildMarkdown(AutoTestRunArtifacts artifacts, string packageId, string modName, List<AutoTestResult> results, AutoTestRunSummary runSummary, AutoTestEvidenceManifest evidence)
        {
            StringBuilder builder = new StringBuilder();
            List<AutoTestResult> failedResults = results.Where(result => AutoTestResultStatus.IsFailure(result.Status)).ToList();
            List<ReportLogGroup> reportLogs = BuildReportLogs(failedResults, evidence);
            int passed = results.Count(result => AutoTestResultStatus.IsPassed(result.Status));
            int passedAfterRetry = results.Count(result => string.Equals(result.Status, AutoTestResultStatus.PassedAfterRetry, StringComparison.OrdinalIgnoreCase));
            int skipped = results.Count(result => string.Equals(result.Status, AutoTestResultStatus.Skipped, StringComparison.OrdinalIgnoreCase));
            int unsupported = results.Count(result => string.Equals(result.Status, AutoTestResultStatus.Unsupported, StringComparison.OrdinalIgnoreCase));
            int compatibilityWarnings = results.Count(result => string.Equals(result.Status, AutoTestResultStatus.CompatibilityWarning, StringComparison.OrdinalIgnoreCase));
            int failed = results.Count(result => AutoTestResultStatus.IsFailure(result.Status));

            builder.AppendLine("# Auto Test Report");
            builder.AppendLine();
            builder.AppendLine("- PackageId: `" + packageId + "`");
            builder.AppendLine("- ModName: " + modName);
            builder.AppendLine("- GeneratedAt: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            builder.AppendLine("- Total: " + results.Count);
            builder.AppendLine("- Passed: " + passed);
            builder.AppendLine("- PassedAfterRetry: " + passedAfterRetry);
            builder.AppendLine("- Failed: " + failed);
            builder.AppendLine("- Skipped: " + skipped);
            builder.AppendLine("- Unsupported: " + unsupported);
            builder.AppendLine("- CompatibilityWarnings: " + compatibilityWarnings);
            if (evidence != null)
            {
                builder.AppendLine("- UnexpectedSevereLogs: " + results.Sum(result => result?.UnexpectedSevereLogCount ?? 0));
                builder.AppendLine("- CapturedSevereLogs: " + evidence.Logs.Count(item => item.Level == "error" || item.Level == "fatal"));
            }
            if (runSummary != null)
            {
                builder.AppendLine("- RunMode: " + runSummary.RunMode);
                builder.AppendLine("- Completeness: " + (runSummary.Complete ? "Complete" : "Incomplete"));
                builder.AppendLine("- ManifestDiscovered: " + runSummary.DiscoveredCount);
                builder.AppendLine("- ManifestPlanned: " + runSummary.PlannedCount);
                builder.AppendLine("- ManifestExecuted: " + runSummary.ExecutedCount);
                builder.AppendLine("- ManifestMissing: " + runSummary.MissingCount);
                builder.AppendLine("- BatchSize: " + runSummary.BatchSize);
                builder.AppendLine("- BatchLimitApplied: " + runSummary.BatchLimitApplied);
                builder.AppendLine("- MissingDefNames: " + (runSummary.MissingDefNames.Count == 0 ? "<none>" : string.Join(", ", runSummary.MissingDefNames.Select(value => "`" + value + "`"))));
            }
            builder.AppendLine("- ReportScope: failed cases only; full results: " + BuildMarkdownLink(artifacts, artifacts.ResultPath));
            builder.AppendLine();
            builder.AppendLine("## Results");
            builder.AppendLine();
            builder.AppendLine("| CaseType | Category | DefName | CaseId | Status | Stage | Screenshot | Notes |");
            builder.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- |");

            if (failedResults.Count == 0)
            {
                builder.AppendLine();
                builder.AppendLine("No failed cases.");
            }
            foreach (AutoTestResult result in failedResults)
            {
                string screenshotValue = ResultScreenshotPaths(result).Count > 0
                    ? string.Join("<br>", ResultScreenshotPaths(result).Select(path => BuildMarkdownLink(artifacts, path)).ToArray())
                    : string.Empty;
                builder.AppendLine(string.Format(
                    "| {0} | {1} | `{2}` | `{7}` | {3} | {4} | {5} | {6} |",
                    EscapePipe(result.CaseType),
                    EscapePipe(result.Category),
                    EscapePipe(result.DefName),
                    EscapePipe(result.Status),
                    EscapePipe(result.Stage),
                    screenshotValue,
                    EscapePipe(string.IsNullOrWhiteSpace(result.ErrorMessage) ? result.Notes : Trim(result.ErrorMessage, 160)),
                    EscapePipe(result.CaseId)));
            }

            if (runSummary != null && runSummary.MissingCaseIds.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("## Missing Cases");
                builder.AppendLine();
                foreach (string caseId in runSummary.MissingCaseIds)
                {
                    builder.AppendLine("- `" + caseId + "`");
                }
            }

            builder.AppendLine();
            builder.AppendLine("## Screenshot Gallery");
            builder.AppendLine();

            foreach (IGrouping<string, AutoTestResult> group in failedResults
                .Where(HasMainGalleryScreenshots)
                .GroupBy(result => result.CaseType ?? string.Empty)
                .OrderBy(group => group.Key))
            {
                builder.AppendLine("### " + (string.IsNullOrWhiteSpace(group.Key) ? "Other" : group.Key));
                builder.AppendLine();

                foreach (AutoTestResult result in group)
                {
                    builder.AppendLine("#### " + result.DefName);
                    builder.AppendLine();
                    builder.AppendLine("- Label: " + (string.IsNullOrWhiteSpace(result.Label) ? result.DefName : result.Label));
                    builder.AppendLine("- CaseId: `" + result.CaseId + "`");
                    builder.AppendLine("- Status: " + result.Status);
                    builder.AppendLine();

                    foreach (string screenshotPath in ResultScreenshotPaths(result).Where(IsMainGalleryScreenshotPath))
                    {
                        string relativePath = BuildRelativeImagePath(artifacts, screenshotPath);
                        builder.AppendLine("![" + EscapeImageAltText(result.DefName) + "](" + relativePath + ")");
                        builder.AppendLine();
                    }
                }
            }

            List<(AutoTestResult Result, string ScreenshotPath)> failureScreenshots = failedResults
                .SelectMany(result => ResultScreenshotPaths(result)
                    .Where(IsFailureScreenshotPath)
                    .Select(path => (Result: result, ScreenshotPath: path)))
                .ToList();

            if (failureScreenshots.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("## Failure Screenshots");
                builder.AppendLine();

                foreach ((AutoTestResult Result, string ScreenshotPath) failure in failureScreenshots)
                {
                    builder.AppendLine("### " + failure.Result.DefName);
                    builder.AppendLine();
                    builder.AppendLine("- CaseType: " + failure.Result.CaseType);
                    builder.AppendLine("- Label: " + (string.IsNullOrWhiteSpace(failure.Result.Label) ? failure.Result.DefName : failure.Result.Label));
                    builder.AppendLine("- Status: " + failure.Result.Status);
                    builder.AppendLine("- Stage: " + failure.Result.Stage);
                    builder.AppendLine();
                    builder.AppendLine("![" + EscapeImageAltText(failure.Result.DefName) + "](" + BuildRelativeImagePath(artifacts, failure.ScreenshotPath) + ")");
                    builder.AppendLine();
                }
            }

            foreach (AutoTestResult failedResult in failedResults)
            {
                List<AutoTestAssertionEvidence> assertions = evidence?.Assertions?.Where(item => item.CaseId == failedResult.CaseId && !item.Success).ToList() ?? new List<AutoTestAssertionEvidence>();
                AutoTestCaseEvidence caseEvidence = evidence?.Cases?.FirstOrDefault(item => item.CaseId == failedResult.CaseId);
                List<AutoTestOperationEnvelope> operations = caseEvidence?.Operations?.ToList() ?? new List<AutoTestOperationEnvelope>();
                string caseKey = ReportCaseKey(failedResult);
                List<ReportLogGroup> logs = reportLogs.Where(log => log.CaseOccurrences.ContainsKey(caseKey)).ToList();
                builder.AppendLine();
                builder.AppendLine("## Failure Details: " + failedResult.DefName + " (" + failedResult.CaseId + ")");
                builder.AppendLine();
                builder.AppendLine("### Failed Assertions");
                builder.AppendLine();
                if (assertions.Count == 0) builder.AppendLine("- No structured failed assertion was recorded; status=`" + failedResult.Status + "`.");
                foreach (AutoTestAssertionEvidence assertion in assertions)
                {
                    builder.AppendLine("- " + assertion.Name + " (`" + assertion.AssertionId + "`)");
                    builder.AppendLine("  - Expected: " + EscapePipe(assertion.Expected));
                    builder.AppendLine("  - Actual: " + EscapePipe(assertion.Actual));
                    builder.AppendLine("  - Details: " + (string.Equals(assertion.Name, "logs.no_unexpected_severe", StringComparison.OrdinalIgnoreCase) && logs.Count > 0
                        ? "See Related Logs below; full messages are listed once in Deduplicated Logs."
                        : EscapePipe(assertion.Details)));
                }
                builder.AppendLine("  - LastSnapshot: " + EscapePipe(failedResult.LastSnapshot));
                builder.AppendLine();
                builder.AppendLine("### Related Logs");
                builder.AppendLine();
                foreach (ReportLogGroup log in logs)
                    builder.AppendLine("- [Log " + log.Id + "](#log-" + log.Id + ") [" + log.Level + "] — " + log.CaseOccurrences[caseKey].Values.Sum() + " occurrence(s)");
                if (logs.Count == 0) builder.AppendLine("- <none>");
                builder.AppendLine();
                builder.AppendLine("### Operation Timeline");
                builder.AppendLine();
                builder.AppendLine("| Step | Status | DurationMs | Summary |");
                builder.AppendLine("| --- | --- | ---: | --- |");
                foreach (AutoTestOperationEnvelope operation in operations)
                    builder.AppendLine("| " + EscapePipe(operation.StepId) + " | " + operation.Status + " | " + operation.DurationMs + " | " + EscapePipe(operation.ResultSummary) + " |");
            }

            if (reportLogs.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("## Deduplicated Logs");
                builder.AppendLine();
                foreach (ReportLogGroup log in reportLogs)
                {
                    builder.AppendLine("### Log " + log.Id);
                    builder.AppendLine();
                    builder.AppendLine("- Level: " + log.Level);
                    builder.AppendLine("- SourcePackageId: " + (string.IsNullOrEmpty(log.SourcePackageId) ? "<unknown>" : log.SourcePackageId));
                    builder.AppendLine("- Occurrences: " + log.Occurrences.Values.Sum());
                    builder.AppendLine("- AffectedCases: " + string.Join(", ", log.CaseOccurrences.Keys.Select(key => "`" + EscapePipe(key) + "`").ToArray()));
                    builder.AppendLine();
                    foreach (string line in log.Message.Split('\n')) builder.AppendLine("    " + line);
                    builder.AppendLine();
                }
            }

            return builder.ToString();
        }

        private sealed class ReportLogGroup
        {
            public int Id;
            public string Level;
            public string Message;
            public string SourcePackageId;
            public readonly Dictionary<string, int> Occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
            public readonly Dictionary<string, Dictionary<string, int>> CaseOccurrences = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
        }

        private static string ReportCaseKey(AutoTestResult result)
        {
            return result.CaseId ?? (result.CaseType + ":" + result.DefName);
        }

        private static List<ReportLogGroup> BuildReportLogs(List<AutoTestResult> failedResults, AutoTestEvidenceManifest evidence)
        {
            List<ReportLogGroup> groups = new List<ReportLogGroup>();
            Dictionary<Tuple<string, string, string>, ReportLogGroup> index = new Dictionary<Tuple<string, string, string>, ReportLogGroup>();
            foreach (AutoTestResult result in failedResults)
            {
                string caseKey = ReportCaseKey(result);
                AutoTestCaseEvidence retained = evidence?.Cases?.FirstOrDefault(item => string.Equals(item.CaseId, result.CaseId, StringComparison.OrdinalIgnoreCase));
                IEnumerable<AutoTestLogEvidence> logs = retained?.Logs ?? evidence?.Logs?.Where(item => item != null && !string.IsNullOrEmpty(result.CaseId) && string.Equals(item.CaseId, result.CaseId, StringComparison.OrdinalIgnoreCase)) ?? Enumerable.Empty<AutoTestLogEvidence>();
                int position = 0;
                foreach (AutoTestLogEvidence log in logs)
                {
                    position++;
                    if (log == null) continue;
                    string level = AutoTestLogJournal.NormalizeLevel(log.Level);
                    if (level != "warning" && level != "error" && level != "fatal") continue;
                    string message = (log.Message ?? string.Empty).Replace("\r\n", "\n").Replace("\r", "\n").Trim();
                    string source = log.SourcePackageId ?? string.Empty;
                    Tuple<string, string, string> key = Tuple.Create(level, source.ToLowerInvariant(), message);
                    if (!index.TryGetValue(key, out ReportLogGroup group))
                    {
                        group = new ReportLogGroup { Id = groups.Count + 1, Level = level, Message = message, SourcePackageId = source };
                        index.Add(key, group);
                        groups.Add(group);
                    }
                    if (!group.CaseOccurrences.TryGetValue(caseKey, out Dictionary<string, int> caseOccurrences))
                    {
                        caseOccurrences = new Dictionary<string, int>(StringComparer.Ordinal);
                        group.CaseOccurrences.Add(caseKey, caseOccurrences);
                    }
                    // A run-scoped asynchronous log can be retained by multiple cases.
                    // Count its physical sequence once globally while retaining each case association.
                    string occurrence = log.Sequence > 0 ? (log.RunId ?? string.Empty) + ":" + log.Sequence : "unsequenced:" + caseKey + ":" + position;
                    int count = Math.Max(1, log.RepeatCount);
                    group.Occurrences[occurrence] = Math.Max(group.Occurrences.TryGetValue(occurrence, out int globalCount) ? globalCount : 0, count);
                    caseOccurrences[occurrence] = Math.Max(caseOccurrences.TryGetValue(occurrence, out int caseCount) ? caseCount : 0, count);
                }
            }
            return groups;
        }

        private static List<string> ResultScreenshotPaths(AutoTestResult result)
        {
            return (result.ScreenshotPaths ?? new List<string>()).Concat(new[] { result.ScreenshotPath })
                .Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static string SerializeScenario(object scenario)
        {
            using (var stream = new MemoryStream())
            {
                new System.Runtime.Serialization.Json.DataContractJsonSerializer(scenario.GetType()).WriteObject(stream, scenario);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        private static string BuildJson(AutoTestRunArtifacts artifacts, string packageId, string runId, string modName, List<AutoTestResult> results, AutoTestRunSummary runSummary, AutoTestEvidenceManifest evidence)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("{");
            builder.AppendLine("  \"packageId\": \"" + EscapeJson(packageId) + "\",");
            builder.AppendLine("  \"runId\": \"" + EscapeJson(runId) + "\",");
            builder.AppendLine("  \"modName\": \"" + EscapeJson(modName) + "\",");
            builder.AppendLine("  \"generatedAt\": \"" + DateTime.Now.ToString("o") + "\",");
            if (evidence != null)
            {
                int failedAssertions = evidence.Assertions.Count(item => !item.Success);
                int capturedSevereLogs = evidence.Logs.Count(item => item.Level == "error" || item.Level == "fatal");
                int unexpectedSevereLogs = results.Sum(item => item?.UnexpectedSevereLogCount ?? 0);
                builder.AppendLine("  \"evidenceSchemaVersion\": " + evidence.SchemaVersion + ",");
                builder.AppendLine("  \"evidencePath\": \"" + EscapeJson(artifacts.EvidencePath) + "\",");
                builder.AppendLine("  \"operationCount\": " + evidence.Operations.Count + ",");
                builder.AppendLine("  \"assertionCount\": " + evidence.Assertions.Count + ",");
                builder.AppendLine("  \"failedAssertionCount\": " + failedAssertions + ",");
                builder.AppendLine("  \"capturedLogCount\": " + evidence.Logs.Count + ",");
                builder.AppendLine("  \"severeLogCount\": " + unexpectedSevereLogs + ",");
                builder.AppendLine("  \"capturedSevereLogCount\": " + capturedSevereLogs + ",");
            }
            if (runSummary != null)
            {
                if (runSummary.ScenarioSelection != null) builder.AppendLine("  \"scenarioSelection\": " + SerializeScenario(runSummary.ScenarioSelection) + ",");
                builder.AppendLine("  \"verifiedCoverageKeys\": " + BuildJsonArray(runSummary.VerifiedCoverageKeys) + ",");
                builder.AppendLine("  \"runMode\": \"" + EscapeJson(runSummary.RunMode) + "\",");
                builder.AppendLine("  \"completeness\": \"" + (runSummary.Complete ? "Complete" : "Incomplete") + "\",");
                builder.AppendLine("  \"manifestDiscovered\": " + runSummary.DiscoveredCount + ",");
                builder.AppendLine("  \"manifestPlanned\": " + runSummary.PlannedCount + ",");
                builder.AppendLine("  \"manifestExecuted\": " + runSummary.ExecutedCount + ",");
                builder.AppendLine("  \"manifestMissing\": " + runSummary.MissingCount + ",");
                builder.AppendLine("  \"passedCount\": " + runSummary.PassedCount + ",");
                builder.AppendLine("  \"passedAfterRetryCount\": " + runSummary.PassedAfterRetryCount + ",");
                builder.AppendLine("  \"failedCount\": " + runSummary.FailedCount + ",");
                builder.AppendLine("  \"skippedCount\": " + runSummary.SkippedCount + ",");
                builder.AppendLine("  \"unsupportedCount\": " + runSummary.UnsupportedCount + ",");
                builder.AppendLine("  \"compatibilityWarningCount\": " + runSummary.CompatibilityWarningCount + ",");
                builder.AppendLine("  \"batchSize\": " + runSummary.BatchSize + ",");
                builder.AppendLine("  \"batchLimitApplied\": " + (runSummary.BatchLimitApplied ? "true" : "false") + ",");
                builder.AppendLine("  \"missingDefNames\": " + BuildJsonArray(runSummary.MissingDefNames) + ",");
            }
            builder.AppendLine("  \"results\": [");

            for (int i = 0; i < results.Count; i++)
            {
                AutoTestResult result = results[i];
                builder.AppendLine("    {");
                builder.AppendLine("      \"caseId\": \"" + EscapeJson(result.CaseId) + "\",");
                if (result.Scenario != null) builder.AppendLine("      \"scenario\": " + SerializeScenario(result.Scenario) + ",");
                builder.AppendLine("      \"caseType\": \"" + EscapeJson(result.CaseType) + "\",");
                builder.AppendLine("      \"scope\": \"" + EscapeJson(result.Scope) + "\",");
                builder.AppendLine("      \"category\": \"" + EscapeJson(result.Category) + "\",");
                builder.AppendLine("      \"defType\": \"" + EscapeJson(result.DefType) + "\",");
                builder.AppendLine("      \"defName\": \"" + EscapeJson(result.DefName) + "\",");
                builder.AppendLine("      \"label\": \"" + EscapeJson(result.Label) + "\",");
                builder.AppendLine("      \"status\": \"" + EscapeJson(result.Status) + "\",");
                builder.AppendLine("      \"stage\": \"" + EscapeJson(result.Stage) + "\",");
                builder.AppendLine("      \"errorMessage\": \"" + EscapeJson(result.ErrorMessage) + "\",");
                builder.AppendLine("      \"reasonCode\": \"" + EscapeJson(result.ResultReasonCode) + "\",");
                builder.AppendLine("      \"screenshotPath\": \"" + EscapeJson(result.ScreenshotPath) + "\",");
                builder.AppendLine("      \"screenshotPaths\": " + BuildJsonArray(result.ScreenshotPaths) + ",");
                builder.AppendLine("      \"notes\": \"" + EscapeJson(result.Notes) + "\",");
                builder.AppendLine("      \"assertionCount\": " + result.AssertionCount + ",");
                builder.AppendLine("      \"failedAssertionIds\": " + BuildJsonArray(result.FailedAssertionIds) + ",");
                builder.AppendLine("      \"operationIds\": " + BuildJsonArray(result.OperationIds) + ",");
                builder.AppendLine("      \"evidenceCaseKey\": \"" + EscapeJson(result.EvidenceCaseKey) + "\",");
                builder.AppendLine("      \"unexpectedSevereLogCount\": " + result.UnexpectedSevereLogCount + ",");
                builder.AppendLine("      \"lastSnapshot\": \"" + EscapeJson(result.LastSnapshot) + "\"");
                builder.Append("    }");
                if (i < results.Count - 1) builder.Append(",");
                builder.AppendLine();
            }

            builder.AppendLine("  ]");
            builder.AppendLine("}");
            return builder.ToString();
        }

        private static string BuildManifestJson(
            string packageId,
            string modName,
            List<AutoTestManifestEntry> discoveredManifest,
            List<AutoTestManifestEntry> plannedManifest,
            AutoTestRunSummary runSummary)
        {
            discoveredManifest = discoveredManifest ?? new List<AutoTestManifestEntry>();
            plannedManifest = plannedManifest ?? new List<AutoTestManifestEntry>();

            StringBuilder builder = new StringBuilder();
            builder.AppendLine("{");
            builder.AppendLine("  \"packageId\": \"" + EscapeJson(packageId) + "\",");
            builder.AppendLine("  \"modName\": \"" + EscapeJson(modName) + "\",");
            builder.AppendLine("  \"generatedAt\": \"" + DateTime.Now.ToString("o") + "\",");
            builder.AppendLine("  \"runMode\": \"" + EscapeJson(runSummary?.RunMode ?? string.Empty) + "\",");
            builder.AppendLine("  \"completeness\": \"" + (runSummary?.Complete == true ? "Complete" : "Incomplete") + "\",");
            builder.AppendLine("  \"batchSize\": " + (runSummary?.BatchSize ?? 40) + ",");
            builder.AppendLine("  \"batchLimitApplied\": " + (runSummary?.BatchLimitApplied == true ? "true" : "false") + ",");
            builder.AppendLine("  \"discovered\": " + BuildManifestEntryArray(discoveredManifest) + ",");
            builder.AppendLine("  \"planned\": " + BuildManifestEntryArray(plannedManifest) + ",");
            builder.AppendLine("  \"missingCaseIds\": " + BuildJsonArray(runSummary?.MissingCaseIds));
            builder.AppendLine("}");
            return builder.ToString();
        }

        private static string Sanitize(string text)
        {
            text = text ?? string.Empty;
            foreach (char invalidChar in Path.GetInvalidFileNameChars())
            {
                text = text.Replace(invalidChar, '_');
            }

            return string.IsNullOrWhiteSpace(text) ? "autotest" : text;
        }

        private static string EscapeJson(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            StringBuilder builder = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                switch (c)
                {
                    case '\\': builder.Append("\\\\"); break;
                    case '"': builder.Append("\\\""); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\t': builder.Append("\\t"); break;
                    case '\b': builder.Append("\\b"); break;
                    case '\f': builder.Append("\\f"); break;
                    default:
                        if (c < 0x20 || c == 0x7F)
                            builder.Append("\\u").Append(((int)c).ToString("x4"));
                        else
                            builder.Append(c);
                        break;
                }
            }
            return builder.ToString();
        }

        private static string EscapePipe(string text)
        {
            return (text ?? string.Empty).Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
        }

        private static string EscapeImageAltText(string text)
        {
            return (text ?? string.Empty).Replace("[", "\\[").Replace("]", "\\]");
        }

        private static string BuildMarkdownLink(AutoTestRunArtifacts artifacts, string screenshotPath)
        {
            string relativePath = BuildRelativeImagePath(artifacts, screenshotPath);
            string fileName = Path.GetFileName(screenshotPath ?? string.Empty);
            return string.IsNullOrWhiteSpace(relativePath)
                ? string.Empty
                : "[" + EscapePipe(fileName) + "](" + relativePath + ")";
        }

        private static string BuildRelativeImagePath(AutoTestRunArtifacts artifacts, string screenshotPath)
        {
            if (artifacts == null || string.IsNullOrWhiteSpace(artifacts.ReportPath) || string.IsNullOrWhiteSpace(screenshotPath))
            {
                return string.Empty;
            }

            try
            {
                string reportDirectory = Path.GetDirectoryName(artifacts.ReportPath);
                if (string.IsNullOrWhiteSpace(reportDirectory))
                {
                    return screenshotPath.Replace('\\', '/');
                }

                Uri reportUri = new Uri(AppendDirectorySeparator(reportDirectory));
                Uri screenshotUri = new Uri(screenshotPath);
                string relativePath = Uri.UnescapeDataString(reportUri.MakeRelativeUri(screenshotUri).ToString());
                return relativePath.Replace('\\', '/');
            }
            catch
            {
                return screenshotPath.Replace('\\', '/');
            }
        }

        private static string AppendDirectorySeparator(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return path ?? string.Empty;
            }

            char lastChar = path[path.Length - 1];
            if (lastChar == Path.DirectorySeparatorChar || lastChar == Path.AltDirectorySeparatorChar)
            {
                return path;
            }

            return path + Path.DirectorySeparatorChar;
        }

        private static bool HasMainGalleryScreenshots(AutoTestResult result)
        {
            if (result == null || string.Equals(result.CaseType, "MeleeWeaponPocketMap", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return ResultScreenshotPaths(result).Any(IsMainGalleryScreenshotPath);
        }

        private static bool IsMainGalleryScreenshotPath(string path)
        {
            return !string.IsNullOrWhiteSpace(path) && !IsFailureScreenshotPath(path);
        }

        private static bool IsFailureScreenshotPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            return path.IndexOf("\\fail\\", StringComparison.OrdinalIgnoreCase) >= 0 ||
                path.IndexOf("/fail/", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string BuildJsonArray(List<string> values)
        {
            if (values == null || values.Count == 0)
            {
                return "[]";
            }

            return "[" + string.Join(", ", values.Select(value => "\"" + EscapeJson(value) + "\"").ToArray()) + "]";
        }

        private static string BuildManifestEntryArray(List<AutoTestManifestEntry> entries)
        {
            if (entries == null || entries.Count == 0)
            {
                return "[]";
            }

            return "[" + string.Join(
                ", ",
                entries.Select(entry =>
                    "{" +
                    "\"caseId\":\"" + EscapeJson(entry.CaseId) + "\"," +
                    "\"caseType\":\"" + EscapeJson(entry.CaseType) + "\"," +
                    "\"category\":\"" + EscapeJson(entry.Category) + "\"," +
                    "\"scope\":\"" + EscapeJson(entry.Scope) + "\"," +
                    "\"defType\":\"" + EscapeJson(entry.DefType) + "\"," +
                    "\"defName\":\"" + EscapeJson(entry.DefName) + "\"," +
                    "\"label\":\"" + EscapeJson(entry.Label) + "\"," +
                    "\"discoverOrder\":" + entry.DiscoverOrder + "," +
                    "\"planned\":" + (entry.Planned ? "true" : "false") + "," +
                    "\"plannedOrder\":" + entry.PlannedOrder +
                    "}")
                    .ToArray()) + "]";
        }

        private static string Trim(string text, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length <= maxLength) return text ?? string.Empty;
            return text.Substring(0, maxLength) + "...";
        }

        private static void PruneArchives(string outputDirectory, string safePackageId)
        {
            if (string.IsNullOrWhiteSpace(outputDirectory) || string.IsNullOrWhiteSpace(safePackageId) || !Directory.Exists(outputDirectory))
            {
                return;
            }

            Regex runPattern = new Regex(
                "^" + Regex.Escape(safePackageId) + "_(?<stamp>\\d{8}_\\d{6})(?:_(?:Report\\.md|Result\\.json|Manifest\\.json|Evidence\\.json|Screenshots))$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            Dictionary<string, List<string>> groupedPaths = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (string filePath in Directory.GetFiles(outputDirectory))
            {
                Match match = runPattern.Match(Path.GetFileName(filePath));
                if (!match.Success) continue;

                string stamp = match.Groups["stamp"].Value;
                if (!groupedPaths.TryGetValue(stamp, out List<string> paths))
                {
                    paths = new List<string>();
                    groupedPaths[stamp] = paths;
                }

                paths.Add(filePath);
            }

            foreach (string directoryPath in Directory.GetDirectories(outputDirectory))
            {
                Match match = runPattern.Match(Path.GetFileName(directoryPath));
                if (!match.Success) continue;

                string stamp = match.Groups["stamp"].Value;
                if (!groupedPaths.TryGetValue(stamp, out List<string> paths))
                {
                    paths = new List<string>();
                    groupedPaths[stamp] = paths;
                }

                paths.Add(directoryPath);
            }

            foreach (KeyValuePair<string, List<string>> entry in groupedPaths
                .OrderByDescending(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Skip(MaxArchivedRuns))
            {
                foreach (string path in entry.Value)
                {
                    try
                    {
                        if (Directory.Exists(path))
                        {
                            Directory.Delete(path, true);
                        }
                        else if (File.Exists(path))
                        {
                            File.Delete(path);
                        }
                    }
                    catch (Exception ex)
                    {
                        Verse.Log.Warning("[LogSyncAutoTest] Failed to prune archive path: " + path + " Error=" + ex.Message);
                    }
                }
            }
        }
    }
}
