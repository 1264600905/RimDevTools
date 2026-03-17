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
        public string ScreenshotDirectory;
    }

    internal static class AutoTestReportWriter
    {
        private const int MaxArchivedRuns = 2;

        public static AutoTestRunArtifacts CreateRunArtifacts(string outputDirectory, string packageId)
        {
            Directory.CreateDirectory(outputDirectory);

            string safePackageId = Sanitize(packageId);
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string basePath = Path.Combine(outputDirectory, safePackageId + "_" + stamp);

            return new AutoTestRunArtifacts
            {
                OutputDirectory = outputDirectory,
                SafePackageId = safePackageId,
                Stamp = stamp,
                BasePath = basePath,
                ReportPath = basePath + "_Report.md",
                ResultPath = basePath + "_Result.json",
                ScreenshotDirectory = basePath + "_Screenshots"
            };
        }

        public static void Write(AutoTestRunArtifacts artifacts, string packageId, List<AutoTestResult> results, string modName)
        {
            try
            {
                if (artifacts == null)
                {
                    throw new ArgumentNullException("artifacts");
                }

                Directory.CreateDirectory(artifacts.OutputDirectory);
                File.WriteAllText(artifacts.ReportPath, BuildMarkdown(packageId, modName, results), Encoding.UTF8);
                File.WriteAllText(artifacts.ResultPath, BuildJson(packageId, modName, results), Encoding.UTF8);
                PruneArchives(artifacts.OutputDirectory, artifacts.SafePackageId);
            }
            catch (Exception ex)
            {
                Verse.Log.Error("[LogSyncAutoTest] Failed to write reports: " + ex);
            }
        }

        private static string BuildMarkdown(string packageId, string modName, List<AutoTestResult> results)
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
            builder.AppendLine();
            builder.AppendLine("## Results");
            builder.AppendLine();
            builder.AppendLine("| Category | DefName | Status | Stage | Screenshot | Notes |");
            builder.AppendLine("| --- | --- | --- | --- | --- | --- |");

            foreach (AutoTestResult result in results)
            {
                string screenshotValue = result.ScreenshotPaths != null && result.ScreenshotPaths.Count > 0
                    ? string.Join("<br>", result.ScreenshotPaths.ToArray())
                    : result.ScreenshotPath;
                builder.AppendLine(string.Format(
                    "| {0} | `{1}` | {2} | {3} | {4} | {5} |",
                    EscapePipe(result.Category),
                    EscapePipe(result.DefName),
                    EscapePipe(result.Status),
                    EscapePipe(result.Stage),
                    EscapePipe(screenshotValue),
                    EscapePipe(string.IsNullOrWhiteSpace(result.ErrorMessage) ? result.Notes : Trim(result.ErrorMessage, 160))));
            }

            return builder.ToString();
        }

        private static string BuildJson(string packageId, string modName, List<AutoTestResult> results)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("{");
            builder.AppendLine("  \"packageId\": \"" + EscapeJson(packageId) + "\",");
            builder.AppendLine("  \"modName\": \"" + EscapeJson(modName) + "\",");
            builder.AppendLine("  \"generatedAt\": \"" + DateTime.Now.ToString("o") + "\",");
            builder.AppendLine("  \"results\": [");

            for (int i = 0; i < results.Count; i++)
            {
                AutoTestResult result = results[i];
                builder.AppendLine("    {");
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

        private static string BuildJsonArray(List<string> values)
        {
            if (values == null || values.Count == 0)
            {
                return "[]";
            }

            return "[" + string.Join(", ", values.Select(value => "\"" + EscapeJson(value) + "\"").ToArray()) + "]";
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
                "^" + Regex.Escape(safePackageId) + "_(?<stamp>\\d{8}_\\d{6})(?:_(?:Report\\.md|Result\\.json|Screenshots))$",
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
