using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace LogSyncMod.AutoTest.Scenarios
{
    // Poll on simulation ticks AND frames: waits use real time, stability uses distinct game ticks.
    public sealed class ScenarioExecution
    {
        private readonly IScenarioBackend backend;
        private readonly IAutoTestClock clock;
        private readonly ScenarioCase definition;
        private readonly XElement root;
        private readonly List<XElement> steps;
        private readonly List<XElement> leaves;
        private readonly Dictionary<string, Dictionary<XElement, ScenarioSample>> baselines = new Dictionary<string, Dictionary<XElement, ScenarioSample>>();
        private readonly Dictionary<string, Dictionary<XElement, bool>> ever = new Dictionary<string, Dictionary<XElement, bool>>();
        private readonly HashSet<string> succeeded = new HashSet<string>();
        private readonly HashSet<XElement> seen = new HashSet<XElement>();
        private readonly HashSet<string> observedTransitions = new HashSet<string>();
        private readonly Dictionary<string, HashSet<XElement>> interventionChanges = new Dictionary<string, HashSet<XElement>>();
        private long started, stepStarted;
        private int index, stableStart = -1, observationStart;
        private bool prepared, stepEntered, observing, cleaning;
        private XElement activeNode, failedCondition, probeFailure;
        public ScenarioOutcome Outcome { get; }
        public bool Completed { get; private set; }

        public ScenarioExecution(ScenarioCase definition, IScenarioBackend backend, IAutoTestClock clock)
        {
            this.definition = definition;
            this.backend = backend;
            this.clock = clock;
            root = XElement.Parse(definition.Xml);
            activeNode = root;
            steps = root.Element("Steps").Elements().ToList();
            leaves = root.Descendants().Where(e => e.Parent != null && new[] { "Require", "Wait", "All", "Any", "Not", "Success" }.Contains(e.Parent.Name.LocalName) && !new[] { "All", "Any", "Not", "StepSucceeded" }.Contains(e.Name.LocalName)).ToList();
            Outcome = new ScenarioOutcome { TestDefName = definition.TestDefName, CaseId = definition.CaseId, Source = definition.Source, InputHash = definition.InputHash, TargetDefNames = root.Element("Targets")?.Elements("Def").Select(e => ScenarioLoader.A(e, "defName")).ToList() ?? new List<string>() };
            Outcome.CaseType = definition.CaseType;
            Outcome.CoverageMode = definition.CoverageMode;
            Outcome.Coverage = definition.Coverage;
        }

        public void Poll(int tick, bool cancel = false)
        {
            if (Completed) return;
            try
            {
                if (cancel) throw new OperationCanceledException("Scenario cancelled");
                if (!prepared)
                {
                    started = clock.RealtimeMilliseconds;
                    backend.Prepare(definition);
                    prepared = true;
                    Mark("prepared", tick);
                    Baseline("prepared");
                }
                if (clock.RealtimeMilliseconds - started >= ScenarioLoader.D(root, "timeoutRealSeconds", 60) * 1000)
                {
                    activeNode = root; failedCondition = null;
                    throw new TimeoutException("Case realtime deadline exceeded");
                }
                Observe(tick);
                if (index >= steps.Count)
                {
                    activeNode = root.Element("Success"); failedCondition = null;
                    if (!Evaluate(root.Element("Success"))) throw new InvalidOperationException("Success predicates not satisfied: " + root.Element("Success"));
                    (backend as IScenarioCompletionBackend)?.ValidateCompletion();
                    End("passed", null, null, tick);
                    return;
                }
                var step = steps[index];
                activeNode = step;
                var first = !stepEntered;
                if (first)
                {
                    failedCondition = null;
                    stepEntered = true;
                    stepStarted = clock.RealtimeMilliseconds;
                    stableStart = -1;
                    observationStart = tick;
                    seen.Clear();
                    Baseline(ScenarioLoader.A(step, "id"));
                    Mark("step.started", tick, step.ToString(SaveOptions.DisableFormatting));
                }
                else if (clock.RealtimeMilliseconds - stepStarted >= ScenarioLoader.D(step, "timeoutRealSeconds", 15) * 1000) throw new TimeoutException("Step realtime deadline exceeded: " + step);
                var type = step.Name.LocalName;
                bool done;
                if (type == "Require")
                {
                    done = Evaluate(step);
                    if (!done) throw new InvalidOperationException("Precondition failed: " + step);
                }
                else if (type == "Wait")
                {
                    bool match = Evaluate(step);
                    var mode = ScenarioLoader.A(step, "observation", "current");
                    if (mode == "always" && !match) throw new InvalidOperationException("Continuous predicate violated: " + step);
                    if (match && stableStart < 0) stableStart = tick;
                    if (!match) stableStart = -1;
                    done = mode == "always" ? tick - observationStart >= ScenarioLoader.I(step, "durationTicks", 1) :
                        match && tick - stableStart >= Math.Max(0, ScenarioLoader.I(step, "stableTicks", 1) - 1);
                }
                else if (type == "Checkpoint") done = true;
                else if (type == "DebugAction" && first)
                {
                    var before = leaves.ToDictionary(e => e, ReadSample);
                    observing = true; // Native hooks must not attribute this intervention to a cast.
                    try { done = backend.Execute(step, true); }
                    finally
                    {
                        observing = false;
                        foreach (var leaf in leaves.Where(e => ScenarioLoader.A(e, "transition") != null))
                        {
                            var after = ReadSample(leaf);
                            if (before[leaf].Matches.SetEquals(after.Matches) && before[leaf].Existing.SetEquals(after.Existing)) continue;
                            foreach (var baseline in baselines.Keys) { interventionChanges[baseline].Add(leaf); ever[baseline][leaf] = false; }
                            seen.Remove(leaf);
                            Mark("debug.interference", tick, "Explicit intervention changed observed objects; take a new checkpoint before attributing later transitions: " + leaf);
                        }
                    }
                }
                else done = backend.Execute(step, first);
                if (done)
                {
                    if (type == "Require" || type == "Wait")
                        foreach (var leaf in step.Descendants().Where(leaves.Contains))
                        {
                            var observation = ReadSample(leaf);
                            Mark("predicate.observation", tick, leaf.ToString(SaveOptions.DisableFormatting) + "; currentMatches=" + string.Join(",", observation.Matches) + "; " + observation.Details);
                        }
                    succeeded.Add(ScenarioLoader.A(step, "id"));
                    Mark("step.completed", tick, ScenarioLoader.A(step, "id"));
                    stepEntered = false;
                    index++;
                }
                else if (clock.RealtimeMilliseconds - stepStarted >= ScenarioLoader.D(step, "timeoutRealSeconds", 15) * 1000) throw new TimeoutException("Step realtime deadline exceeded: " + step);
            }
            catch (OperationCanceledException ex) { End("cancelled", "scenario.cancelled", ex.ToString(), tick, ex); }
            catch (TimeoutException ex) { End("failed", "scenario.timeout", ex.ToString(), tick, ex); }
            catch (ScenarioConfigurationException ex) { End("failed", "scenario.configuration", ex.ToString(), tick, ex); }
            catch (ScenarioProbeException ex) { End("failed", "scenario.probe", ex.ToString(), tick, ex); }
            catch (Exception ex) { End("failed", prepared ? "scenario.execution" : "scenario.prepare", ex.ToString(), tick, ex); }
        }

        public void Observe(int tick)
        {
            if (!prepared || Completed || observing || cleaning) return;
            observing = true;
            try
            {
                foreach (var baseline in baselines)
                foreach (var leaf in leaves)
                {
                    if (interventionChanges[baseline.Key].Contains(leaf)) continue;
                    var current = ReadSample(leaf);
                    if (Match(leaf, current, baseline.Value[leaf]))
                    {
                        ever[baseline.Key][leaf] = true;
                        if (ScenarioLoader.A(leaf, "transition") != null && ScenarioLoader.A(leaf, "since", "prepared") == baseline.Key)
                        {
                            var key = baseline.Key + ":" + leaf.ToString(SaveOptions.DisableFormatting);
                            if (observedTransitions.Add(key)) Mark("effect.observed", tick, key + "; matches=" + string.Join(",", current.Matches));
                        }
                    }
                }
            }
            finally { observing = false; }
        }

        public void Abort(string code, string error, int tick) { if (!Completed) End("failed", code, error, tick); }

        private void Baseline(string id)
        {
            baselines[id] = leaves.ToDictionary(e => e, ReadSample);
            ever[id] = leaves.ToDictionary(e => e, e => false);
            interventionChanges[id] = new HashSet<XElement>();
        }

        private bool Evaluate(XElement e) => Evaluate(e, out failedCondition);

        private bool Evaluate(XElement e, out XElement failure)
        {
            failure = null;
            if (e.Name == "StepSucceeded")
            {
                var passed = succeeded.Contains(ScenarioLoader.A(e, "ref"));
                if (!passed) failure = e;
                return passed;
            }
            if (e.Name == "Not")
            {
                var passed = e.Descendants().Where(leaf => leaves.Contains(leaf)).All(leaf => ReadSample(leaf).TargetCount > 0) && !Evaluate(e.Elements().Single(), out _);
                if (!passed) failure = e;
                return passed;
            }
            if (e.Name == "Any")
            {
                foreach (var child in e.Elements()) if (Evaluate(child, out _)) return true;
                failure = e;
                return false;
            }
            if (e.HasElements)
            {
                foreach (var child in e.Elements()) if (!Evaluate(child, out failure)) return false;
                return true;
            }
            var wait = e.Ancestors("Wait").FirstOrDefault();
            var eventually = ScenarioLoader.A(e, "observation", ScenarioLoader.A(wait, "observation")) == "eventually";
            var baselineId = ScenarioLoader.A(e, "since", eventually && wait != null ? ScenarioLoader.A(wait, "id") : "prepared");
            if (!baselines.TryGetValue(baselineId, out var baseline)) throw new InvalidOperationException("Baseline has not been reached: " + baselineId);
            if (interventionChanges[baselineId].Contains(e)) throw new InvalidOperationException("DebugAction changed this transition after baseline '" + baselineId + "'; it cannot be attributed to the tested ability. Establish a new checkpoint: " + e);
            var sample = ReadSample(e);
            var match = Match(e, sample, baseline[e]);
            if (match) seen.Add(e);
            var satisfied = eventually ? match || seen.Contains(e) || ever[baselineId][e] : match;
            if (!satisfied) failure = e;
            return satisfied;
        }

        public static bool Match(XElement condition, ScenarioSample sample, ScenarioSample baseline)
        {
            if (sample.TargetCount == 0) return false;
            var transition = ScenarioLoader.A(condition, "transition");
            var matches = transition == "added" ? sample.Matches.Except(baseline.Existing.Concat(baseline.Matches)) : transition == "removed" ? baseline.Matches.Except(sample.Existing.Concat(sample.Matches)) : sample.Matches;
            var owners = transition == "removed" ? baseline.Owners : sample.Owners;
            var count = matches.Select(id => owners.TryGetValue(id, out var owner) ? owner : id).Distinct().Count();
            var quantifier = ScenarioLoader.A(condition, "quantifier", "any");
            return quantifier == "count" ? count == ScenarioLoader.I(condition, "equals") : quantifier == "all" ? count == sample.TargetCount : count > 0;
        }

        private ScenarioSample ReadSample(XElement condition)
        {
            try { return backend.Sample(condition) ?? throw new InvalidOperationException("Probe returned null"); }
            catch (ScenarioConfigurationException) { probeFailure = condition; throw; }
            catch (Exception ex) { probeFailure = condition; throw new ScenarioProbeException("Probe failed: " + condition, ex); }
        }

        private void End(string status, string code, string error, int tick, Exception exception = null)
        {
            Outcome.Status = status;
            Outcome.ErrorCode = code;
            Outcome.Error = error;
            if (status == "failed") AddDiagnostic(code, exception?.Message ?? error, probeFailure ?? failedCondition ?? activeNode, exception as ScenarioConfigurationException);
            cleaning = true;
            try { backend.Cleanup(); Mark("cleanup.completed", tick); }
            catch (Exception ex)
            {
                Outcome.Status = "failed"; Outcome.ErrorCode = "scenario.cleanup"; Outcome.Error += "\nCleanup: " + ex;
                AddDiagnostic("scenario.cleanup", ex.Message, root);
            }
            Mark(Outcome.Status, tick, Outcome.Error);
            Completed = true;
        }

        private void AddDiagnostic(string code, string message, XElement node, ScenarioConfigurationException configuration = null)
        {
            node = node ?? root;
            var key = ScenarioLoader.ElementKey(node);
            var hasExactLocation = definition.SourceLocations.TryGetValue(key, out var exact);
            var source = hasExactLocation ? exact : definition.Source;
            // SourceLocations is the frozen path:line:element contract; the path may contain Windows drive colons.
            var location = Regex.Match(source ?? "", @"^(.*):(\d+):(.*)$");
            var diagnostic = new ScenarioFailureDiagnostic {
                Code = code, Message = message,
                File = configuration?.FilePath ?? (location.Success ? location.Groups[1].Value : null),
                Line = configuration != null && configuration.LineNumber > 0 ? (int?)configuration.LineNumber
                    : hasExactLocation && location.Success && int.TryParse(location.Groups[2].Value, out var line) && line > 0 ? (int?)line : null,
                Element = configuration?.ElementPath ?? key,
                StepId = ScenarioLoader.A(node.AncestorsAndSelf().FirstOrDefault(e => e.Parent?.Name == "Steps"), "id"),
                Xml = node.ToString(SaveOptions.DisableFormatting),
                Suggestion = configuration?.Suggestion ?? (code == "scenario.timeout"
                    ? "核对该步骤的前提、目标对象和观察条件，确认预期变化会发生；需要时调整观察窗口或稳定时间。"
                    : code == "scenario.probe" || code == "scenario.prepare" || code == "scenario.configuration"
                    ? "核对该节点引用的 Def、组件、模组依赖和原生前提，并查看原始错误详情。"
                    : code == "scenario.cleanup" ? "检查本轮清理错误和原始日志；此位置仅表示所属 Case。"
                    : "对照该节点的断言与实际状态，修正准备步骤、目标引用或预期值。")
            };
            Outcome.Diagnostics.Add(diagnostic);
            Outcome.Error = (diagnostic.File ?? source) + (diagnostic.Line.HasValue ? ":" + diagnostic.Line : "") + " " + diagnostic.Element + "\n" + Outcome.Error;
        }

        public void Mark(string stage, int tick, string details = null) => Outcome.Trace.Add(new ScenarioTrace { Tick = tick, Stage = stage, Details = details });
    }
}
