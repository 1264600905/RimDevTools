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

            return new AutoTestRunArtifacts
            {
                OutputDirectory = outputDirectory,
                SafePackageId = safePackageId,
                Stamp = stamp,
                BasePath = basePath,
                ReportPath = basePath + "_Report.md",
                ResultPath = basePath + "_Result.json",
                ManifestPath = basePath + "_Manifest.json",
                EvidencePath = basePath + "_Evidence.json",
                ScreenshotDirectory = basePath + "_Screenshots"
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
                if (evidence != null) AutoTestEvidenceSerializer.Write(artifacts.EvidencePath, evidence);
                File.WriteAllText(artifacts.ReportPath, BuildMarkdown(artifacts, packageId, modName, results, runSummary), Encoding.UTF8);
                File.WriteAllText(artifacts.ResultPath, BuildJson(artifacts, packageId, runId, modName, results, runSummary, evidence), Encoding.UTF8);
                File.WriteAllText(artifacts.ManifestPath, BuildManifestJson(packageId, modName, discoveredManifest, plannedManifest, runSummary), Encoding.UTF8);
                PruneArchives(artifacts.OutputDirectory, artifacts.SafePackageId);
            }
            catch (Exception ex)
            {
                Verse.Log.Error("[LogSyncAutoTest] Failed to write reports: " + ex);
            }
        }

        private static string BuildMarkdown(AutoTestRunArtifacts artifacts, string packageId, string modName, List<AutoTestResult> results, AutoTestRunSummary runSummary)
        {
            StringBuilder builder = new StringBuilder();
            int passed = results.Count(result => result.Status == "passed");
            int skipped = results.Count(result => result.Status == "skipped");
            int failed = results.Count(result => result.Status != "passed" && result.Status != "skipped");

            builder.AppendLine("# Auto Test Report");
            builder.AppendLine();
            builder.AppendLine("- PackageId: `" + packageId + "`");
            builder.AppendLine("- ModName: " + modName);
            builder.AppendLine("- GeneratedAt: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            builder.AppendLine("- Total: " + results.Count);
            builder.AppendLine("- Passed: " + passed);
            builder.AppendLine("- Failed: " + failed);
            builder.AppendLine("- Skipped: " + skipped);
            if (runSummary != null)
            {
                builder.AppendLine("- RunMode: " + runSummary.RunMode);
                builder.AppendLine("- Completeness: " + (runSummary.Complete ? "Complete" : "Incomplete"));
                builder.AppendLine("- ManifestDiscovered: " + runSummary.DiscoveredCount);
                builder.AppendLine("- ManifestPlanned: " + runSummary.PlannedCount);
                builder.AppendLine("- ManifestExecuted: " + runSummary.ExecutedCount);
                builder.AppendLine("- ManifestMissing: " + runSummary.MissingCount);
                builder.AppendLine("- MissingDefNames: " + (runSummary.MissingDefNames.Count == 0 ? "<none>" : string.Join(", ", runSummary.MissingDefNames.Select(value => "`" + value + "`"))));
            }
            builder.AppendLine();
            builder.AppendLine("## Results");
            builder.AppendLine();
            builder.AppendLine("| CaseType | Category | DefName | Status | Stage | Screenshot | Notes |");
            builder.AppendLine("| --- | --- | --- | --- | --- | --- | --- |");

            foreach (AutoTestResult result in results)
            {
                string screenshotValue = result.ScreenshotPaths != null && result.ScreenshotPaths.Count > 0
                    ? string.Join("<br>", result.ScreenshotPaths.Select(path => BuildMarkdownLink(artifacts, path)).ToArray())
                    : BuildMarkdownLink(artifacts, result.ScreenshotPath);
                builder.AppendLine(string.Format(
                    "| {0} | {1} | `{2}` | {3} | {4} | {5} | {6} |",
                    EscapePipe(result.CaseType),
                    EscapePipe(result.Category),
                    EscapePipe(result.DefName),
                    EscapePipe(result.Status),
                    EscapePipe(result.Stage),
                    screenshotValue,
                    EscapePipe(string.IsNullOrWhiteSpace(result.ErrorMessage) ? result.Notes : Trim(result.ErrorMessage, 160))));
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

            foreach (IGrouping<string, AutoTestResult> group in results
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
                    builder.AppendLine("- Status: " + result.Status);
                    builder.AppendLine();

                    foreach (string screenshotPath in result.ScreenshotPaths.Where(IsMainGalleryScreenshotPath))
                    {
                        string relativePath = BuildRelativeImagePath(artifacts, screenshotPath);
                        builder.AppendLine("![" + EscapeImageAltText(result.DefName) + "](" + relativePath + ")");
                        builder.AppendLine();
                    }
                }
            }

            List<(AutoTestResult Result, string ScreenshotPath)> failureScreenshots = results
                .SelectMany(result => (result.ScreenshotPaths ?? new List<string>())
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

            return builder.ToString();
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
                int severeLogs = evidence.Logs.Count(item => item.Level == "error" || item.Level == "fatal");
                builder.AppendLine("  \"evidenceSchemaVersion\": " + evidence.SchemaVersion + ",");
                builder.AppendLine("  \"evidencePath\": \"" + EscapeJson(artifacts.EvidencePath) + "\",");
                builder.AppendLine("  \"operationCount\": " + evidence.Operations.Count + ",");
                builder.AppendLine("  \"assertionCount\": " + evidence.Assertions.Count + ",");
                builder.AppendLine("  \"failedAssertionCount\": " + failedAssertions + ",");
                builder.AppendLine("  \"capturedLogCount\": " + evidence.Logs.Count + ",");
                builder.AppendLine("  \"severeLogCount\": " + severeLogs + ",");
            }
            if (runSummary != null)
            {
                builder.AppendLine("  \"runMode\": \"" + EscapeJson(runSummary.RunMode) + "\",");
                builder.AppendLine("  \"completeness\": \"" + (runSummary.Complete ? "Complete" : "Incomplete") + "\",");
                builder.AppendLine("  \"manifestDiscovered\": " + runSummary.DiscoveredCount + ",");
                builder.AppendLine("  \"manifestPlanned\": " + runSummary.PlannedCount + ",");
                builder.AppendLine("  \"manifestExecuted\": " + runSummary.ExecutedCount + ",");
                builder.AppendLine("  \"manifestMissing\": " + runSummary.MissingCount + ",");
                builder.AppendLine("  \"missingDefNames\": " + BuildJsonArray(runSummary.MissingDefNames) + ",");
            }
            builder.AppendLine("  \"results\": [");

            for (int i = 0; i < results.Count; i++)
            {
                AutoTestResult result = results[i];
                builder.AppendLine("    {");
                builder.AppendLine("      \"caseId\": \"" + EscapeJson(result.CaseId) + "\",");
                builder.AppendLine("      \"caseType\": \"" + EscapeJson(result.CaseType) + "\",");
                builder.AppendLine("      \"scope\": \"" + EscapeJson(result.Scope) + "\",");
                builder.AppendLine("      \"category\": \"" + EscapeJson(result.Category) + "\",");
                builder.AppendLine("      \"defType\": \"" + EscapeJson(result.DefType) + "\",");
                builder.AppendLine("      \"defName\": \"" + EscapeJson(result.DefName) + "\",");
                builder.AppendLine("      \"label\": \"" + EscapeJson(result.Label) + "\",");
                builder.AppendLine("      \"status\": \"" + EscapeJson(result.Status) + "\",");
                builder.AppendLine("      \"stage\": \"" + EscapeJson(result.Stage) + "\",");
                builder.AppendLine("      \"errorMessage\": \"" + EscapeJson(result.ErrorMessage) + "\",");
                builder.AppendLine("      \"screenshotPath\": \"" + EscapeJson(result.ScreenshotPath) + "\",");
                builder.AppendLine("      \"screenshotPaths\": " + BuildJsonArray(result.ScreenshotPaths) + ",");
                builder.AppendLine("      \"notes\": \"" + EscapeJson(result.Notes) + "\"");
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
            return text
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n");
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

            return result.ScreenshotPaths != null && result.ScreenshotPaths.Any(IsMainGalleryScreenshotPath);
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
