using System.Text.Json;
using LogSyncMod.AutoTest;

namespace RimDevTools;

public static class ScenarioResultValidator
{
    // Validate physical execution separately from logical coverage. A replacement is not another pass row.
    public static string? Validate(JsonElement root)
    {
        if (!root.TryGetProperty("runMode", out var mode) || mode.GetString() is not ("ExternalScenario" or "MixedScenario")) return null;
        try
        {
            var selection = root.GetProperty("scenarioSelection");
            var cases = selection.GetProperty("Cases").EnumerateArray().ToList();
            var expected = cases.ToDictionary(c => Text(c, "CaseId"), StringComparer.OrdinalIgnoreCase);
            var results = root.GetProperty("results").EnumerateArray().ToList();
            var actual = results.ToDictionary(c => Text(c, "caseId"), StringComparer.OrdinalIgnoreCase);
            var ordinary = selection.GetProperty("OrdinaryCaseIds").EnumerateArray().Select(e => e.GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (ordinary.Count != selection.GetProperty("OrdinaryCaseIds").GetArrayLength() || ordinary.Overlaps(expected.Keys)) return "Duplicate physical plan identities";
            if (!actual.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(ordinary.Concat(expected.Keys))) return "Executed physical cases differ from the frozen selection";
            if (root.GetProperty("manifestPlanned").GetInt32() != actual.Count || root.GetProperty("manifestExecuted").GetInt32() != actual.Count) return "Physical manifest counts do not match unique results";
            var verified = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in expected)
            {
                var result = actual[pair.Key]; var outcome = result.GetProperty("scenario");
                if (Text(outcome, "CaseId") != pair.Key || Text(outcome, "TestDefName") != Text(pair.Value, "TestDefName")) return "Scenario outcome identity differs from its selected definition";
                var plannedCoverage = pair.Value.GetProperty("Coverage").EnumerateArray().ToDictionary(Key, StringComparer.OrdinalIgnoreCase);
                var coverage = outcome.GetProperty("Coverage").EnumerateArray().ToList();
                if (!plannedCoverage.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(coverage.Select(Key)) || coverage.Count != plannedCoverage.Count) return "Scenario outcome changes its frozen coverage claims";
                if (!AutoTestResultStatus.IsPassed(Text(result, "status"))) continue;
                if (Text(outcome, "Status") != Text(result, "status")) return "Scenario and semantic result statuses disagree";
                var steps = outcome.GetProperty("Trace").EnumerateArray().Where(t => Text(t, "Stage") == "step.completed").Select(t => Text(t, "Details")).ToHashSet(StringComparer.Ordinal);
                var nativeTargets = outcome.GetProperty("Trace").EnumerateArray().Where(t => Text(t, "Stage") == "coverage.target_used").Select(t => Text(t, "Details")).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var t in coverage)
                {
                    var evidence = plannedCoverage[Key(t)].GetProperty("EvidenceSteps").EnumerateArray().Select(e => e.GetString()!).ToList();
                    if (evidence.Count == 0 || evidence.Any(e => !steps.Contains(e))) return "Coverage claimed without completed predicate evidence";
                    if (!nativeTargets.Contains(Key(t))) return "Coverage target has no corresponding native execution/observation";
                    verified.Add(Key(t));
                }
            }
            var claimed = root.GetProperty("verifiedCoverageKeys").EnumerateArray().Select(e => e.GetString()!).ToList();
            if (claimed.Count != verified.Count || !verified.SetEquals(claimed)) return "Verified coverage contains skipped, failed, unsupported or unexecuted claims";
            var replacedOrdinary = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var replacementCases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var replacement in selection.GetProperty("Replacements").EnumerateArray())
            {
                var ordinaryId = Text(replacement, "OrdinaryCaseId"); var caseId = Text(replacement, "ScenarioCaseId");
                if (mode.GetString() == "ExternalScenario" || actual.ContainsKey(ordinaryId) || !replacedOrdinary.Add(ordinaryId) || !expected.TryGetValue(caseId, out var definition)) return "Invalid or duplicate replacement execution mapping";
                if (Text(definition, "CoverageMode") != "replace" || Text(replacement, "TestDefName") != Text(definition, "TestDefName") || !definition.GetProperty("Coverage").EnumerateArray().Any(t => Key(t) == Key(replacement.GetProperty("Target")))) return "Replacement mapping differs from the selected rule";
                replacementCases.Add(caseId);
            }
            int cap = root.GetProperty("batchSize").GetInt32();
            if (cap < 1 || actual.Count > cap && (ordinary.Count != 0 || !replacementCases.SetEquals(expected.Keys))) return "Batch cap exceeded by non-priority cases";
            if (selection.GetProperty("ReplacementLimitExceeded").GetBoolean() != (replacementCases.Count > cap)) return "Replacement overflow flag differs from physical count";
            return null;
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or ArgumentException or FormatException)
        { return "Malformed external-scenario result: " + ex.Message; }
    }

    private static string Text(JsonElement e, string property) => e.GetProperty(property).GetString() ?? throw new InvalidOperationException("Null " + property);
    private static string Key(JsonElement t) => string.Join("|", Text(t, "PackageId"), Text(t, "DefType"), Text(t, "DefName"), Text(t, "CaseType"), Text(t, "Variant")).ToLowerInvariant();
}
