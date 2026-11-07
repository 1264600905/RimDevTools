#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;

namespace LogSyncMod.AutoTest.Scenarios
{
    public static class ScenarioPlanner
    {
        public static bool InRange(ScenarioCase definition, ISet<string> keys) => definition.Coverage.Concat(definition.SelectionTargets).Any(t => keys.Contains(t.Key));
        // The caller validates loaded Defs and whether each key is a supported ordinary capability.
        // Range misses remain valid candidates; malformed keys never become range misses.
        public static ScenarioSelection Select(IEnumerable<ScenarioCase> definitions,
            IEnumerable<ScenarioOrdinaryCandidate> ordinary, int limit, bool scenarioOnly = false)
        {
            if (limit < 1) throw new ArgumentOutOfRangeException(nameof(limit));
            var cases = definitions.ToList();
            var candidates = ordinary.ToList();
            var claims = new Dictionary<string, ScenarioCase>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in cases.Where(c => c.CoverageMode == "replace"))
                foreach (var target in c.Coverage)
                {
                    if (claims.TryGetValue(target.Key, out var prior))
                        throw new ScenarioConfigurationException(c.Source + ": replacement conflict for " + target.Key +
                            "; " + prior.CaseId + " (" + prior.Source + ") and " + c.CaseId);
                    claims.Add(target.Key, c);
                }
            var result = new ScenarioSelection();
            var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!scenarioOnly)
                foreach (var candidate in candidates)
                    if (claims.TryGetValue(candidate.Target.Key, out var rule))
                    {
                        selected.Add(rule.CaseId);
                        result.Replacements.Add(new ScenarioReplacement {
                            OrdinaryCaseId = candidate.CaseId, ScenarioCaseId = rule.CaseId,
                            TestDefName = rule.TestDefName, Target = candidate.Target });
                    }
            result.Cases.AddRange(cases.Where(c => selected.Contains(c.CaseId)));
            result.ReplacementLimitExceeded = selected.Count > limit;
            var removed = new HashSet<string>(result.Replacements.Select(r => r.OrdinaryCaseId), StringComparer.OrdinalIgnoreCase);
            var range = new HashSet<string>(candidates.Select(c => c.Target.Key), StringComparer.OrdinalIgnoreCase);
            // Replacements first; deterministic ordinary order then in-range supplements; out-of-range XML last.
            if (!scenarioOnly)
                foreach (var c in candidates.Where(c => !removed.Contains(c.CaseId)))
                    if (result.Cases.Count + result.OrdinaryCaseIds.Count < limit) result.OrdinaryCaseIds.Add(c.CaseId);
            foreach (var c in cases.Where(c => !selected.Contains(c.CaseId))
                .OrderBy(c => scenarioOnly || InRange(c, range) ? 0 : 1))
            {
                if (result.Cases.Count + result.OrdinaryCaseIds.Count >= limit) continue;
                selected.Add(c.CaseId); result.Cases.Add(c);
            }
            foreach (var c in cases)
                result.Decisions.Add(new ScenarioSelectionDecision { CaseId = c.CaseId, Selected = selected.Contains(c.CaseId),
                    Reason = !selected.Contains(c.CaseId) ? "batch-limit" : result.Replacements.Any(r => r.ScenarioCaseId == c.CaseId) ? "replacement-priority" : "supplement" });
            foreach (var c in candidates)
                result.Decisions.Add(new ScenarioSelectionDecision { CaseId = c.CaseId,
                    Selected = result.OrdinaryCaseIds.Contains(c.CaseId),
                    Reason = scenarioOnly ? "scenario-only" : removed.Contains(c.CaseId) ? "replaced" : result.OrdinaryCaseIds.Contains(c.CaseId) ? "ordinary" : "batch-limit" });
            return result;
        }
    }
}
