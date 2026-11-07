#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace LogSyncMod.AutoTest.Scenarios
{
    public static partial class ScenarioLoader
    {
        public static void SelectRules(ScenarioPlan plan, IEnumerable<string> requested)
        {
            var values = requested.ToList();
            if (values.Count == 0) return;
            var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var value in values)
            {
                var matches = plan.Cases.Where(c => string.Equals(c.TestDefName, value, StringComparison.OrdinalIgnoreCase)).ToList();
                if (matches.Count == 0) matches = plan.Cases.Where(c => string.Equals(c.Name, value, StringComparison.Ordinal)).ToList();
                var rules = matches.Select(c => c.TestDefName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (rules.Count != 1) throw new ScenarioConfigurationException("Test name must select one definition (matrix instances stay together): " + value + "; matches=" + string.Join(",", rules));
                selected.Add(rules[0]);
            }
            plan.SelectedTestDefNames = selected.OrderBy(v => v, StringComparer.OrdinalIgnoreCase).ToList();
            plan.Cases.RemoveAll(c => !selected.Contains(c.TestDefName));
        }

        private static IEnumerable<KeyValuePair<string, XElement>> ExpandMatrix(XElement c, string path)
        {
            var matrix = c.Element("Matrix");
            var rows = matrix?.Elements("Row").ToList();
            if (matrix != null && (rows.Count == 0 || rows.Count > 256)) Fail(matrix, path, "Matrix requires 1..256 explicit Row instances");
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows ?? new List<XElement> { new XElement("Row", new XAttribute("id", "default")) })
            {
                var id = A(row, "id");
                if (!Regex.IsMatch(id ?? "", @"^[A-Za-z0-9_.-]+$") || !ids.Add(id)) Fail(row, path, "invalid/duplicate matrix row id");
                var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var p in row.Elements("Param"))
                {
                    var name = A(p, "name");
                    if (!Regex.IsMatch(name ?? "", @"^[A-Za-z_][A-Za-z0-9_]*$") || parameters.ContainsKey(name)) Fail(p, path, "invalid/duplicate matrix parameter");
                    parameters.Add(name, A(p, "value"));
                }
                var used = new HashSet<string>();
                string Replace(string value) => Regex.Replace(value, @"\$\{([^}]+)\}", m => {
                    if (!parameters.TryGetValue(m.Groups[1].Value, out var replacement)) Fail(row, path, "undefined matrix parameter " + m.Value);
                    used.Add(m.Groups[1].Value);
                    // Values are replaced inside the parsed XML tree, never reparsed as markup.
                    if (replacement.Contains("${")) Fail(row, path, "recursive parameter values are unsupported");
                    return replacement;
                });
                var copy = Copy(c); copy.Element("Matrix")?.Remove();
                foreach (var e in copy.DescendantsAndSelf())
                {
                    foreach (var a in e.Attributes()) a.Value = Replace(a.Value);
                    if (!e.HasElements) e.Value = Replace(e.Value);
                }
                if (copy.Element("testDefName")?.Value != c.Element("testDefName")?.Value) Fail(row, path, "matrix cannot change testDefName");
                if (parameters.Keys.Any(p => !used.Contains(p))) Fail(row, path, "unused matrix parameter " + parameters.Keys.First(p => !used.Contains(p)));
                yield return new KeyValuePair<string, XElement>(id, copy);
            }
        }

        private static List<ScenarioCoverageTarget> ParseCoverage(XElement c, string path)
        {
            var coverage = c.Element("Coverage");
            var result = new List<ScenarioCoverageTarget>();
            if (coverage == null) return result;
            if (A(coverage, "mode", "supplement") != "supplement" && A(coverage, "mode") != "replace") Fail(coverage, path, "Coverage mode must be supplement/replace");
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var target in coverage.Elements("Target"))
            {
                var type = A(target, "caseType");
                if (!ScenarioTypes.NonUi.Contains(type) || type == "GroupCombat") Fail(target, path, "coverage requires an independent non-UI regression capability; UI is not covered by functional assertions");
                var t = new ScenarioCoverageTarget { PackageId = A(target, "packageId", A(c, "packageId")), DefType = A(target, "defType"), DefName = A(target, "defName"), CaseType = type, Variant = A(target, "variant", ""), EvidenceSteps = target.Elements("Evidence").Select(e => A(e, "step")).ToList() };
                if (t.EvidenceSteps.Count == 0) Fail(target, path, "coverage requires Evidence step references to real Require/Wait predicates");
                foreach (var stepId in t.EvidenceSteps)
                {
                    var step = c.Element("Steps").Elements().FirstOrDefault(e => A(e, "id") == stepId);
                    if (step == null || (step.Name != "Require" && step.Name != "Wait") || !step.Descendants().Any(e => e.Name != "All" && e.Name != "Any" && e.Name != "Not" && e.Name != "StepSucceeded"))
                        Fail(target, path, "coverage evidence must reference a Require/Wait containing a state/effect predicate: " + stepId);
                }
                if (!c.Element("Actors").DescendantsAndSelf().Attributes().Concat(c.Element("Steps").DescendantsAndSelf().Attributes()).Any(a => a.Value == t.DefName) &&
                    !c.Element("Actors").Descendants().Any(e => !e.HasElements && e.Value == t.DefName))
                    Fail(target, path, "coverage target is never used by scene actors or steps: " + t.DefName);
                if (!keys.Add(t.Key)) Fail(target, path, "duplicate coverage key " + t.Key);
                result.Add(t);
            }
            if (result.Count == 0) Fail(coverage, path, "Coverage requires at least one Target");
            return result;
        }
    }
}
