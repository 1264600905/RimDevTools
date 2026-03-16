using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace LogSyncMod.AutoTest
{
    internal static class AutoTestReportWriter
    {
        public static void Write(string outputDirectory, string packageId, List<AutoTestResult> results, string modName)
        {
            try
            {
                Directory.CreateDirectory(outputDirectory);
                string safePackageId = Sanitize(packageId);
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string basePath = Path.Combine(outputDirectory, safePackageId + "_" + stamp);

                File.WriteAllText(basePath + "_Report.md", BuildMarkdown(packageId, modName, results), Encoding.UTF8);
                File.WriteAllText(basePath + "_Result.json", BuildJson(packageId, modName, results), Encoding.UTF8);
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
            builder.AppendLine("| Category | DefName | Status | Stage | Notes |");
            builder.AppendLine("| --- | --- | --- | --- | --- |");

            foreach (AutoTestResult result in results)
            {
                builder.AppendLine(string.Format(
                    "| {0} | `{1}` | {2} | {3} | {4} |",
                    EscapePipe(result.Category),
                    EscapePipe(result.DefName),
                    EscapePipe(result.Status),
                    EscapePipe(result.Stage),
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

        private static string Trim(string text, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length <= maxLength) return text ?? string.Empty;
            return text.Substring(0, maxLength) + "...";
        }
    }
}
