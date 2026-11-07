using System.Reflection;
using System.Text.Json;
using LogSyncMod.AutoTest;

internal static class AutoTestReportTests
{
    internal static void Run(Action<bool, string> assert)
    {
        var artifacts = new AutoTestRunArtifacts
        {
            ReportPath = Path.Combine(Path.GetTempPath(), "report-memory", "Screenshots", "Report.md"),
            ResultPath = Path.Combine(Path.GetTempPath(), "report-memory", "Result.json"),
            EvidencePath = Path.Combine(Path.GetTempPath(), "report-memory", "Screenshots", "Evidence.json")
        };
        var statuses = new[] { AutoTestResultStatus.Passed, AutoTestResultStatus.PassedAfterRetry, AutoTestResultStatus.Skipped, AutoTestResultStatus.Unsupported, AutoTestResultStatus.CompatibilityWarning };
        var results = statuses.Select((status, i) => new AutoTestResult
        {
            CaseId = "nonfailure-" + i, CaseType = "SkillPocketMap", DefName = "HiddenDef" + i, Status = status,
            ScreenshotPaths = [ImagePath(artifacts, "HiddenDef" + i + ".png"), ImagePath(artifacts, "fail/HiddenDef" + i + ".png")]
        }).ToList();
        var first = new AutoTestResult { CaseId = "case-1", CaseType = "SkillPocketMap", DefName = "BrokenSkill", Status = "failed", Stage = "cast",
            ScreenshotPaths = [ImagePath(artifacts, "BrokenSkill.png"), ImagePath(artifacts, "fail/BrokenSkill.png")], ErrorMessage = "cast not accepted" };
        var second = new AutoTestResult { CaseId = "case-2", CaseType = "BuildingInfoCard", DefName = "BrokenBuilding", Status = "failed",
            ScreenshotPath = ImagePath(artifacts, "BrokenBuilding.png") };
        results.Add(first);
        results.Add(second);
        const string message = "Exception ticking hediff Axolotl_WaterInvisibility: NullReferenceException at HediffCompWaterTreatToSelf";
        var logs = Enumerable.Range(1, 13).Select(i => new AutoTestLogEvidence { Sequence = i, RunId = "run", Level = "error", Message = message, RepeatCount = 1, SourcePackageId = "hentailoliteam.axolotl" }).ToList();
        logs[0].RepeatCount = 4;
        var manifest = new AutoTestEvidenceManifest();
        manifest.Cases.Add(new AutoTestCaseEvidence { CaseId = first.CaseId, Logs = logs });
        manifest.Cases.Add(new AutoTestCaseEvidence { CaseId = second.CaseId, Logs = [new AutoTestLogEvidence { Sequence = 1, RunId = "run", Level = "error", Message = message, RepeatCount = 4, SourcePackageId = "hentailoliteam.axolotl" }] });
        manifest.Assertions.Add(new AutoTestAssertionEvidence { CaseId = first.CaseId, Name = "logs.no_unexpected_severe", Success = false, Actual = "unexpectedSevere=13", Details = string.Join(" | ", logs.Select(log => log.Message)) });
        manifest.Assertions.Add(new AutoTestAssertionEvidence { CaseId = first.CaseId, Name = "skill.cast_accepted", Success = false, Expected = "cast accepted", Actual = "false", Details = "cast-specific diagnostic" });
        var summary = new AutoTestRunSummary { Complete = true, PlannedCount = results.Count, ExecutedCount = results.Count, BatchSize = 999 };
        var report = Render("BuildMarkdown", artifacts, results, summary, manifest);
        foreach (var result in results.Take(statuses.Length))
            assert(!report.Contains(result.DefName, StringComparison.Ordinal), "Markdown excludes nonfailure content and retry screenshots: " + result.Status);
        assert(report.Contains("`BrokenSkill`", StringComparison.Ordinal) && report.Contains("`BrokenBuilding`", StringComparison.Ordinal), "Results retains all failed cases");
        assert(report.Contains("![BrokenSkill](BrokenSkill.png)", StringComparison.Ordinal)
            && report.Contains("![BrokenBuilding](BrokenBuilding.png)", StringComparison.Ordinal), "Screenshot Gallery contains failures including the single-path fallback");
        assert(report.Contains("![BrokenSkill](fail/BrokenSkill.png)", StringComparison.Ordinal), "failure screenshot section retains failed case diagnostics");
        assert(Count(report, message) == 1, "identical error text is printed once across assertions and multiple failed cases");
        assert(report.Contains("- Occurrences: 16", StringComparison.Ordinal), "deduplicated count sums RepeatCount without double-counting shared physical sequences");
        assert(report.Contains("[Log 1](#log-1) [error] — 16 occurrence(s)", StringComparison.Ordinal)
            && report.Contains("[Log 1](#log-1) [error] — 4 occurrence(s)", StringComparison.Ordinal), "each failed case links to the shared log with its own occurrence count");
        assert(report.Contains("cast-specific diagnostic", StringComparison.Ordinal) && report.Contains("unexpectedSevere=13", StringComparison.Ordinal), "non-log assertion details and original severe counts are retained");
        assert(report.Contains("- Total: 7", StringComparison.Ordinal) && report.Contains("- Passed: 2", StringComparison.Ordinal)
            && report.Contains("- Unsupported: 1", StringComparison.Ordinal), "summary still describes the complete test run");
        using (var json = JsonDocument.Parse(Render("BuildJson", artifacts, results, summary, manifest)))
            assert(json.RootElement.GetProperty("results").GetArrayLength() == 7, "machine-readable Results remains complete after Markdown filtering");
        assert(manifest.Cases[0].Logs.Count == 13 && logs[0].RepeatCount == 4 && manifest.Assertions[0].Details.Contains(message, StringComparison.Ordinal), "report rendering never mutates original evidence, assertion details or repeat counts");

        // Equal rendered text does not erase different severities, sources or stack traces.
        manifest.Cases[0].Logs.Add(new AutoTestLogEvidence { Sequence = 14, Level = "warning", Message = message, SourcePackageId = "hentailoliteam.axolotl" });
        manifest.Cases[0].Logs.Add(new AutoTestLogEvidence { Sequence = 15, Level = "error", Message = message, SourcePackageId = "other.mod" });
        manifest.Cases[0].Logs.Add(new AutoTestLogEvidence { Sequence = 16, Level = "error", Message = message + " different stack", SourcePackageId = "hentailoliteam.axolotl" });
        var distinct = Render("BuildMarkdown", artifacts, results, summary, manifest);
        assert(Count(distinct, "### Log ") == 4, "deduplication preserves different severity, source and diagnostic text");
        var noFailures = Render("BuildMarkdown", artifacts, results.Take(statuses.Length).ToList(), summary, manifest);
        assert(noFailures.Contains("No failed cases.", StringComparison.Ordinal) && !noFailures.Contains("![", StringComparison.Ordinal)
            && !noFailures.Contains("## Failure Details", StringComparison.Ordinal) && !noFailures.Contains("## Deduplicated Logs", StringComparison.Ordinal), "all-passing or diagnostic-only reports have no case/gallery/log detail");
        var noEvidence = Render("BuildMarkdown", artifacts, [first], null, null);
        assert(noEvidence.Contains("No structured failed assertion", StringComparison.Ordinal) && noEvidence.Contains("- <none>", StringComparison.Ordinal), "legacy failures without structured evidence still render");

        // No new occurrence identity exists in legacy data: every distinct row still counts.
        var legacy = new AutoTestEvidenceManifest();
        legacy.Cases.Add(new AutoTestCaseEvidence { CaseId = first.CaseId, Logs = [new() { Level = "exception", Message = "legacy\r\nstack" }, new() { Level = "error", Message = "legacy\nstack" }] });
        var legacyReport = Render("BuildMarkdown", artifacts, [first], null, legacy);
        assert(Count(legacyReport, "### Log ") == 1 && legacyReport.Contains("- Occurrences: 2", StringComparison.Ordinal), "legacy rows normalize line endings and severity aliases while retaining counts");
        var matrix = Render("BuildMarkdown", artifacts, [new AutoTestResult { CaseId = "Rule/a/1", DefName = "Rule", CaseType = "ExternalScenario", Status = "failed" }, new AutoTestResult { CaseId = "Rule/b/1", DefName = "Rule", CaseType = "ExternalScenario", Status = "failed" }], null, null);
        assert(matrix.Contains("| CaseId |", StringComparison.Ordinal) && matrix.Contains("Failure Details: Rule (Rule/a/1)", StringComparison.Ordinal) && matrix.Contains("Failure Details: Rule (Rule/b/1)", StringComparison.Ordinal), "matrix report rows and failure anchors retain independent execution identities");
    }

    private static string ImagePath(AutoTestRunArtifacts artifacts, string path) => Path.Combine(Path.GetDirectoryName(artifacts.ReportPath)!, path.Replace('/', Path.DirectorySeparatorChar));
    private static int Count(string text, string value) => (text.Length - text.Replace(value, "", StringComparison.Ordinal).Length) / value.Length;
    private static string Render(string method, AutoTestRunArtifacts artifacts, List<AutoTestResult> results, AutoTestRunSummary summary, AutoTestEvidenceManifest evidence)
    {
        object[] args = method == "BuildJson" ? [artifacts, "pkg", "run", "mod", results, summary, evidence] : [artifacts, "pkg", "mod", results, summary, evidence];
        return (string)typeof(AutoTestReportWriter).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args)!;
    }
}
