using System;
using System.Collections.Generic;
using System.Linq;
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
        private long started, stepStarted;
        private int index, stableStart = -1, observationStart;
        private bool prepared, stepEntered, observing, cleaning;
        public ScenarioOutcome Outcome { get; }
        public bool Completed { get; private set; }

        public ScenarioExecution(ScenarioCase definition, IScenarioBackend backend, IAutoTestClock clock)
        {
            this.definition = definition;
            this.backend = backend;
            this.clock = clock;
            root = XElement.Parse(definition.Xml);
            steps = root.Element("Steps").Elements().ToList();
            leaves = root.Descendants().Where(e => e.Parent != null && new[] { "Require", "Wait", "All", "Any", "Not", "Success" }.Contains(e.Parent.Name.LocalName) && !new[] { "All", "Any", "Not", "StepSucceeded" }.Contains(e.Name.LocalName)).ToList();
            Outcome = new ScenarioOutcome { TestDefName = definition.TestDefName, CaseId = definition.CaseId, Source = definition.Source, InputHash = definition.InputHash, TargetDefNames = root.Element("Targets")?.Elements("Def").Select(e => ScenarioLoader.A(e, "defName")).ToList() ?? new List<string>() };
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
                if (clock.RealtimeMilliseconds - started >= ScenarioLoader.D(root, "timeoutRealSeconds", 60) * 1000) throw new TimeoutException("Case realtime deadline exceeded");
                Observe(tick);
                if (index >= steps.Count)
                {
                    if (!Evaluate(root.Element("Success"))) throw new InvalidOperationException("Success predicates not satisfied: " + root.Element("Success"));
                    End("passed", null, null, tick);
                    return;
                }
                var step = steps[index];
                var first = !stepEntered;
                if (first)
                {
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
                else done = backend.Execute(step, first);
                if (done)
                {
                    succeeded.Add(ScenarioLoader.A(step, "id"));
                    Mark("step.completed", tick, ScenarioLoader.A(step, "id"));
                    stepEntered = false;
                    index++;
                }
                else if (clock.RealtimeMilliseconds - stepStarted >= ScenarioLoader.D(step, "timeoutRealSeconds", 15) * 1000) throw new TimeoutException("Step realtime deadline exceeded: " + step);
            }
            catch (OperationCanceledException ex) { End("cancelled", "scenario.cancelled", ex.ToString(), tick); }
            catch (TimeoutException ex) { End("failed", "scenario.timeout", ex.ToString(), tick); }
            catch (ScenarioConfigurationException ex) { End("failed", "scenario.configuration", ex.ToString(), tick); }
            catch (ScenarioProbeException ex) { End("failed", "scenario.probe", ex.ToString(), tick); }
            catch (Exception ex) { End("failed", prepared ? "scenario.execution" : "scenario.prepare", ex.ToString(), tick); }
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
        }

        private bool Evaluate(XElement e)
        {
            if (e.Name == "StepSucceeded") return succeeded.Contains(ScenarioLoader.A(e, "ref"));
            if (e.Name == "Not") return e.Descendants().Where(leaf => leaves.Contains(leaf)).All(leaf => ReadSample(leaf).TargetCount > 0) && !Evaluate(e.Elements().Single());
            if (e.Name == "Any") return e.Elements().Any(Evaluate);
            if (e.HasElements) return e.Elements().All(Evaluate);
            var wait = e.Ancestors("Wait").FirstOrDefault();
            var eventually = ScenarioLoader.A(e, "observation", ScenarioLoader.A(wait, "observation")) == "eventually";
            var baselineId = ScenarioLoader.A(e, "since", eventually && wait != null ? ScenarioLoader.A(wait, "id") : "prepared");
            if (!baselines.TryGetValue(baselineId, out var baseline)) throw new InvalidOperationException("Baseline has not been reached: " + baselineId);
            var sample = ReadSample(e);
            var match = Match(e, sample, baseline[e]);
            if (match) seen.Add(e);
            return eventually ? match || seen.Contains(e) || ever[baselineId][e] : match;
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
            catch (ScenarioConfigurationException) { throw; }
            catch (Exception ex) { throw new ScenarioProbeException("Probe failed: " + condition, ex); }
        }

        private void End(string status, string code, string error, int tick)
        {
            Outcome.Status = status;
            Outcome.ErrorCode = code;
            Outcome.Error = error;
            cleaning = true;
            try { backend.Cleanup(); Mark("cleanup.completed", tick); }
            catch (Exception ex) { Outcome.Status = "failed"; Outcome.ErrorCode = "scenario.cleanup"; Outcome.Error += "\nCleanup: " + ex; }
            Mark(Outcome.Status, tick, Outcome.Error);
            Completed = true;
        }

        public void Mark(string stage, int tick, string details = null) => Outcome.Trace.Add(new ScenarioTrace { Tick = tick, Stage = stage, Details = details });
    }
}
