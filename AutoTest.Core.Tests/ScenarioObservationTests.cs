using System.Xml.Linq;
using LogSyncMod.AutoTest;
using LogSyncMod.AutoTest.Scenarios;

internal static class ScenarioObservationTests
{
    internal static void Run(Action<bool, string> check)
    {
        ScenarioCase Compile(string steps, string success = "<StepSucceeded ref='check'/>") => ScenarioLoader.Compile(new[] {
            new ScenarioInput { Path = "observation.autotest.xml", Xml = "<TestSuite schemaVersion='1' id='observations'><Case id='c'><testDefName>Observation</testDefName><Scene/><Actors><Pawn id='p'><basePawnKind>SpaceRefugee</basePawnKind></Pawn></Actors><Steps>" + steps + "</Steps><Success>" + success + "</Success></Case></TestSuite>" }
        }).Cases.Single();
        ScenarioExecution Start(string steps, Backend backend, out Clock clock, string success = "<StepSucceeded ref='check'/>")
        { clock = new Clock(); return new ScenarioExecution(Compile(steps, success), backend, clock); }
        const string alive = "<ActorState actor='p' state='alive'/>";
        const string hediff = "<HasHediff actor='p' def='AlcoholHigh'/>";
        const string debug = "<DebugAction id='check' actionId='native.fillNeeds'><Select mode='single' actor='p'/><Expect>";

        var backend = new Backend { Matches = _ => false };
        var engine = Start(debug + alive + "</Expect></DebugAction>", backend, out var clock);
        engine.Poll(1);
        check(engine.Completed && engine.Outcome.Status == "failed" && engine.Outcome.Error.Contains("postcondition")
            && !engine.Outcome.Trace.Any(t => t.Stage == "step.completed") && backend.Cleaned == 1,
            "a false DebugAction Expect fails before step completion and cleans up");
        check(engine.Outcome.Diagnostics.Single().StepId == "check" && engine.Outcome.Diagnostics.Single().Element.EndsWith("Expect[0]/ActorState[0]"),
            "postcondition diagnostics identify the exact Expect leaf");
        backend = new Backend { Matches = _ => false };
        backend.Action = _ => backend.Matches = _ => true;
        engine = Start(debug + "<All>" + alive + "</All></Expect></DebugAction>", backend, out clock);
        engine.Poll(1); engine.Poll(2);
        check(engine.Outcome.Status == "passed" && backend.Executions == 1
            && engine.Outcome.Trace.Any(t => t.Stage == "predicate.observation" && t.Details.Contains("ActorState")),
            "nested Expect checks post-action state and records predicate evidence");
        backend = new Backend { Matches = _ => false };
        backend.Action = _ => backend.Matches = _ => true;
        engine = Start("<DebugAction id='check' actionId='native.addHediff' def='AlcoholHigh'><Select mode='single' actor='p'/><Expect><HasHediff actor='p' def='AlcoholHigh' transition='added' since='check'/></Expect></DebugAction>", backend, out clock);
        engine.Poll(1); engine.Poll(2);
        check(engine.Outcome.Status == "passed", "an action can explicitly assert its own added effect without attributing it to a cast");
        backend = new Backend { ThrowOnSample = false };
        backend.Action = _ => backend.ThrowOnSample = true;
        engine = Start(debug + alive + "</Expect></DebugAction>", backend, out clock);
        engine.Poll(1);
        check(engine.Outcome.ErrorCode == "scenario.probe" && backend.Cleaned == 1, "Expect probe errors remain failures");

        backend = new Backend();
        engine = Start("<Wait id='check' observation='always' durationTicks='3'>" + alive + "</Wait>", backend, out clock);
        engine.Poll(1);
        backend.Matches = _ => false; engine.Observe(2);
        backend.Matches = _ => true; engine.Observe(2);
        check(!engine.Completed && backend.Cleaned == 0, "continuous failure does not clean up inside a native callback");
        engine.Poll(4);
        check(engine.Completed && engine.Outcome.Status == "failed" && engine.Outcome.ErrorCode == "scenario.execution"
            && engine.Outcome.Diagnostics.Single().Element.EndsWith("ActorState[0]") && backend.Cleaned == 1,
            "always fails after an observed same-tick interruption even when the predicate recovers");
        check(engine.Outcome.Trace.Single(t => t.Stage == "predicate.interrupted").Tick == 2, "interruption records the event tick");

        backend = new Backend();
        engine = Start("<Wait id='check' stableTicks='3'>" + alive + "</Wait>", backend, out clock);
        engine.Poll(1); backend.Matches = _ => false; engine.Observe(2);
        backend.Matches = _ => true; engine.Observe(2); engine.Poll(3);
        check(!engine.Outcome.Trace.Any(t => t.Stage == "step.completed"), "an event interruption resets stableTicks");
        engine.Poll(4); engine.Poll(5);
        check(engine.Outcome.Status == "passed", "a fresh uninterrupted stability window can pass");

        var eventuallyNot = "<Wait id='check' observation='eventually' timeoutRealSeconds='2'><Not>" + hediff + "</Not></Wait>";
        backend = new Backend(); engine = Start(eventuallyNot, backend, out clock);
        engine.Poll(1); engine.Poll(2); backend.Matches = _ => false; engine.Poll(3); engine.Poll(4);
        check(engine.Outcome.Status == "passed", "eventually Not passes when an initially present effect disappears");
        backend = new Backend(); engine = Start(eventuallyNot, backend, out clock);
        engine.Poll(1); backend.Matches = _ => false; engine.Observe(2); backend.Matches = _ => true; engine.Observe(2);
        engine.Poll(2); engine.Poll(3);
        check(engine.Outcome.Status == "passed", "eventually Not retains a transient absence between polls");
        backend = new Backend(); engine = Start(eventuallyNot, backend, out clock);
        engine.Poll(1); clock.Ms = 2000; engine.Poll(2);
        check(engine.Outcome.ErrorCode == "scenario.timeout", "eventually Not does not pass when the effect never disappears");
        backend = new Backend { TargetCount = 0, Matches = _ => false }; engine = Start(eventuallyNot, backend, out clock);
        engine.Poll(1); clock.Ms = 2000; engine.Poll(2);
        check(engine.Outcome.ErrorCode == "scenario.timeout", "eventually Not still rejects an empty explicit target set");

        const string second = "<HasHediff actor='p' def='Metalblood'/>";
        backend = new Backend { Matches = e => (string)e.Attribute("def") == "AlcoholHigh" };
        engine = Start("<Wait id='check' observation='eventually' timeoutRealSeconds='2'><All>" + hediff + second + "</All></Wait>", backend, out clock);
        engine.Poll(1); backend.Matches = e => (string)e.Attribute("def") == "Metalblood"; engine.Observe(2); engine.Poll(2);
        check(!engine.Outcome.Trace.Any(t => t.Stage == "step.completed"), "eventually All requires one observation where the complete expression matches");
        backend.Matches = _ => true; engine.Observe(3); backend.Matches = _ => false; engine.Poll(3); engine.Poll(4);
        check(engine.Outcome.Status == "passed", "eventually All retains a simultaneous transient match");
        backend = new Backend();
        engine = Start("<Wait id='check' observation='eventually' stableTicks='3'>" + alive.Replace("/>", " observation='current'/>") + "</Wait>", backend, out clock);
        engine.Poll(1); backend.Matches = _ => false; engine.Observe(2); engine.Poll(3);
        check(!engine.Outcome.Trace.Any(t => t.Stage == "step.completed"), "a leaf current override remains current inside an eventually Wait");

        // A historical event since an earlier checkpoint must survive entry into a later Wait.
        backend = new Backend { Matches = _ => false };
        engine = Start("<Checkpoint id='before'/><Wait id='check'><HasHediff actor='p' def='AlcoholHigh' transition='added' since='before' observation='eventually'/></Wait>", backend, out clock);
        engine.Poll(1); backend.Matches = _ => true; engine.Observe(2); backend.Matches = _ => false; engine.Poll(2); engine.Poll(3);
        check(engine.Outcome.Status == "passed", "explicit since histories still retain events before a Wait starts");
        backend = new Backend { Matches = _ => false };
        engine = Start("<Checkpoint id='before'/><Wait id='check' observation='eventually' timeoutRealSeconds='2'>" + hediff + "</Wait>", backend, out clock);
        engine.Poll(1); backend.Matches = _ => true; engine.Observe(2); backend.Matches = _ => false; engine.Poll(2); clock.Ms = 2000; engine.Poll(3);
        check(engine.Outcome.ErrorCode == "scenario.timeout", "default eventually history starts at the Wait, not scene preparation");
        backend = new Backend();
        engine = Start("<Checkpoint id='before'/><Wait id='check' observation='eventually' timeoutRealSeconds='2'><Not><StepSucceeded ref='before'/></Not></Wait>", backend, out clock);
        engine.Poll(1); engine.Poll(2); clock.Ms = 2000; engine.Poll(3);
        check(engine.Outcome.ErrorCode == "scenario.timeout", "StepSucceeded-only expressions cannot remember matches before their Wait starts");
        foreach (var step in new[] { "<Checkpoint id='prepared'/>", "<Require id='prepared'>" + alive + "</Require>" })
        {
            try { Compile(step, alive); check(false, "reserved prepared step rejected"); }
            catch (ScenarioConfigurationException ex) { check(ex.Message.Contains("reserved") && ex.ElementPath.EndsWith(step.StartsWith("<Checkpoint") ? "Checkpoint" : "Require"), "reserved prepared step is rejected with source location"); }
        }
        var invalid = Compile("<Checkpoint id='check'/>");
        invalid.Xml = invalid.Xml.Replace("id=\"check\"", "id=\"prepared\"");
        backend = new Backend(); engine = new ScenarioExecution(invalid, backend, new Clock()); engine.Poll(1);
        check(engine.Outcome.ErrorCode == "scenario.configuration" && backend.Cleaned == 1, "runtime also refuses to overwrite an established baseline");
    }

    private sealed class Clock : IAutoTestClock
    {
        public long Ms;
        public DateTime UtcNow => DateTime.UnixEpoch.AddMilliseconds(Ms);
        public long RealtimeMilliseconds => Ms;
    }
    private sealed class Backend : IScenarioBackend
    {
        public Func<XElement, bool> Matches = _ => true;
        public Action<XElement> Action;
        public int TargetCount = 1, Cleaned, Executions;
        public bool ThrowOnSample;
        public void Prepare(ScenarioCase definition) { }
        public ScenarioSample Sample(XElement e)
        {
            if (ThrowOnSample) throw new InvalidOperationException("postcondition probe fault");
            return new ScenarioSample { TargetCount = TargetCount, Matches = Matches(e) ? new HashSet<string> { "p" } : new HashSet<string>() };
        }
        public bool Execute(XElement step, bool first) { Executions++; Action?.Invoke(step); return true; }
        public void Cleanup() { Cleaned++; }
    }
}
