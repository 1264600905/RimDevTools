using System.Xml.Linq;
using LogSyncMod.AutoTest;
using LogSyncMod.AutoTest.Scenarios;

internal static class ScenarioTests
{
    internal static void Run(Action<bool, string> check)
    {
        const string body = "<Scene type='pocketMap'/><Actors><Pawn id='p'><basePawnKind>SpaceRefugee</basePawnKind></Pawn></Actors><Steps><Require id='ready'><ActorState actor='p' state='alive'/></Require></Steps><Success><StepSucceeded ref='ready'/></Success>";
        string Suite(string name = "Rule_A", string extra = "", string caseBody = body) => "<TestSuite schemaVersion='1' id='suite'><Case id='a'><testDefName>" + name + "</testDefName>" + caseBody + "</Case>" + extra + "</TestSuite>";
        ScenarioInput Input(string xml, string path = "中文路径/测试.autotest.xml") => new() { Path = path, Sha256 = "frozen-hash", Xml = xml };
        ScenarioPlan Compile(string xml) => ScenarioLoader.Compile(new[] { Input(xml) });
        void Reject(string xml, string reason)
        {
            try { Compile(xml); check(false, reason); }
            catch (ScenarioConfigurationException ex) { check(ex.Message.Contains("测试.autotest.xml"), reason); }
        }
        var plan = Compile(Suite());
        check(plan.Cases.Single().TestDefName == "Rule_A" && plan.Cases.Single().CaseId == "Rule_A/default/1", "scenario stable rule and instance identities");
        check(plan.Cases[0].InputHash == "frozen-hash" && plan.Cases[0].Source.Contains("中文路径"), "scenario keeps source and frozen hash");
        var relocated = ScenarioLoader.Compile(new[] { Input(Suite(), "new/location.xml") });
        check(relocated.Cases[0].CaseId == plan.Cases[0].CaseId, "moving a rule does not change its identity");
        Reject(Suite(""), "empty testDefName is rejected");
        Reject(Suite().Replace("<testDefName>Rule_A</testDefName>", ""), "missing testDefName is rejected");
        Reject(Suite(extra: "<Case id='b'><testDefName>rule_a</testDefName>" + body + "</Case>"), "case-insensitive rule collision is rejected");
        try { ScenarioLoader.Compile(new[] { Input(Suite()), Input(Suite(), "second.xml") }); check(false, "cross-file collision"); }
        catch (ScenarioConfigurationException ex) { check(ex.Message.Contains("second.xml") && ex.Message.Contains("中文路径"), "cross-file collision locates both definitions"); }
        Reject(Suite().Replace("<Scene", "<Scene unsupported='1'"), "unknown attributes are rejected");
        Reject(Suite().Replace("<Actors>", "<Actors><Thing id='x' def='Steel' Class='Anything'/>"), "Class injection is rejected");
        Reject(Suite().Replace("<Scene type='pocketMap'/>", "<Scene type='mainMap'/>"), "future main map backend is explicitly unsupported");
        Reject(Suite().Replace("actor='p'", "actor='missing'"), "static missing actor reference rejected");
        Reject(Suite().Replace("<Actors>", "<Actors><Thing id='p' def='Steel'/>"), "duplicate actor id rejected");
        Reject(Suite().Replace("<Steps>", "<Steps><Checkpoint id='ready'/>"), "duplicate step id rejected");
        Reject(Suite().Replace("<Require id='ready'>", "<Wait id='ready' stableTicks='NaN'>").Replace("</Require>", "</Wait>"), "invalid numeric attribute rejected");
        Reject(Suite().Replace("<Scene", "<Scene sizeX='3'"), "map dimension bounds validated");
        Reject(Suite().Replace("<Pawn id='p'", "<Pawn count='5' id='p'"), "single pawn cannot silently ignore a group count");
        Reject(Suite().Replace("<Pawn id='p'>", "<Pawn id='p'><Abilities><Ensure def='MetalbloodInjection' severity='1'/></Abilities>"), "ability patches reject ignored health attributes");
        Reject(Suite().Replace("<Scene type='pocketMap'/>", "<Scene/><Scene/>"), "duplicate singleton fields are rejected");
        Reject(Suite().Replace("<Case", "<Templates><PawnTemplate id='t'/><PawnTemplate id='t'/></Templates><Case"), "duplicate template IDs are rejected with a source location");
        Reject("<!DOCTYPE TestSuite [<!ENTITY x SYSTEM 'file:///C:/windows/win.ini'>]>" + Suite().Replace("Rule_A", "&x;"), "DTD and external entities rejected");
        var cycles = Suite().Replace("<Case", "<Templates><PawnTemplate id='a' extends='b'/><PawnTemplate id='b' extends='a'/></Templates><Case");
        Reject(cycles, "template cycles rejected");
        var inherited = Suite().Replace("<Case", "<Templates><PawnTemplate id='a'><basePawnKind>SpaceRefugee</basePawnKind></PawnTemplate><PawnTemplate id='b' extends='a'><gender>Female</gender></PawnTemplate></Templates><Case")
            .Replace("<Pawn id='p'><basePawnKind>SpaceRefugee</basePawnKind></Pawn>", "<Pawn id='p' template='b'/><Pawn id='q' template='a'/>");
        var actors = XElement.Parse(Compile(inherited).Cases[0].Xml).Element("Actors").Elements().ToList();
        check(actors.Count == 2 && actors.All(e => e.Element("basePawnKind")?.Value == "SpaceRefugee") && actors[0].Element("gender")?.Value == "Female", "all actor templates expand without skipping replacement siblings");
        check(ScenarioTypes.NonUi.Length == 29 && ScenarioTypes.NonUi.Distinct().Count() == 29, "shared inventory covers all planned non-UI types");

        var clock = new TestClock(); var backend = new Backend();
        var engine = new ScenarioExecution(plan.Cases[0], backend, clock);
        engine.Poll(1); engine.Poll(2);
        check(engine.Completed && engine.Outcome.Status == "passed" && backend.Cleaned == 1, "scenario executes precondition, success and cleanup");
        var waitXml = Suite(caseBody: body.Replace("<Require id='ready'>", "<Wait id='ready' stableTicks='3' timeoutRealSeconds='2'>").Replace("</Require>", "</Wait>"));
        clock = new TestClock(); backend = new Backend(); engine = new ScenarioExecution(Compile(waitXml).Cases[0], backend, clock);
        engine.Poll(10); engine.Poll(10); engine.Poll(10);
        check(!engine.Completed, "repeated frame polls do not count as stable ticks");
        engine.Poll(11); engine.Poll(12); engine.Poll(13);
        check(engine.Completed && engine.Outcome.Status == "passed", "distinct simulation ticks satisfy stability");
        clock = new TestClock(); backend = new Backend { Match = false }; engine = new ScenarioExecution(Compile(waitXml).Cases[0], backend, clock);
        engine.Poll(10); clock.Ms = 2000; engine.Poll(10);
        check(engine.Completed && engine.Outcome.ErrorCode == "scenario.timeout" && backend.Cleaned == 1, "wait times out with paused simulation and cleans up");
        check(engine.Outcome.Diagnostics.Single().StepId == "ready" && engine.Outcome.Diagnostics.Single().Element.EndsWith("ActorState[0]") && engine.Outcome.Diagnostics.Single().Line == 1,
            "timeout points to the unsatisfied condition and owning Wait in the frozen source");
        clock = new TestClock(); backend = new Backend { Match = false }; engine = new ScenarioExecution(Compile(waitXml).Cases[0], backend, clock);
        engine.Poll(10); clock.Ms = 2000; backend.Match = true; engine.Poll(11);
        check(engine.Completed && engine.Outcome.ErrorCode == "scenario.timeout", "late success cannot bypass the step deadline");
        clock = new TestClock(); backend = new Backend { ProbeThrows = true }; engine = new ScenarioExecution(plan.Cases[0], backend, clock);
        engine.Poll(1);
        check(engine.Completed && engine.Outcome.ErrorCode == "scenario.probe" && engine.Outcome.Error.Contains("probe fault") && backend.Cleaned == 1, "probe exception retains diagnostics and cleans up");
        clock = new TestClock(); backend = new Backend { PrepareThrows = true }; engine = new ScenarioExecution(plan.Cases[0], backend, clock); engine.Poll(1);
        check(engine.Outcome.ErrorCode == "scenario.prepare" && backend.Cleaned == 1, "partial preparation failure cleans ownership");
        backend = new Backend(); engine = new ScenarioExecution(plan.Cases[0], backend, clock); engine.Poll(1, true);
        check(engine.Outcome.Status == "cancelled" && backend.Cleaned == 1, "explicit cancellation is distinct and cleans up");
        backend = new Backend { CleanupThrows = true }; engine = new ScenarioExecution(plan.Cases[0], backend, clock); engine.Poll(1); engine.Poll(2);
        check(engine.Outcome.ErrorCode == "scenario.cleanup" && engine.Outcome.Status == "failed", "cleanup error cannot turn green");
        backend = new Backend { CompletionThrows = true }; engine = new ScenarioExecution(plan.Cases[0], backend, clock); engine.Poll(1); engine.Poll(2);
        check(engine.Outcome.Status == "failed" && engine.Outcome.ErrorCode == "scenario.configuration" && backend.Cleaned == 1, "native completion gate rejects unused coverage before success and still cleans up");
        var diagnosticXml = Suite(caseBody: """
            <Scene/>
            <Actors><Pawn id='p'><basePawnKind>SpaceRefugee</basePawnKind></Pawn></Actors>
            <Steps><Require id='assert'><All>
              <ActorState actor='p' state='alive'/>
              <ActorState actor='p' state='dead'/>
            </All></Require></Steps>
            <Success><StepSucceeded ref='assert'/></Success>
            """);
        var diagnosticPlan = ScenarioLoader.Compile(new[] { Input(diagnosticXml, @"D:\测试目录\断言.xml") });
        clock = new TestClock(); backend = new Backend { MatchesCondition = e => ScenarioLoader.A(e, "state") != "dead" };
        engine = new ScenarioExecution(diagnosticPlan.Cases.Single(), backend, clock); engine.Poll(1);
        var diagnostic = engine.Outcome.Diagnostics.Single();
        check(diagnostic.File == @"D:\测试目录\断言.xml" && diagnostic.Line == 5 && diagnostic.Element.EndsWith("All[0]/ActorState[1]") && diagnostic.StepId == "assert" && diagnostic.Xml.Contains("dead"),
            "nested assertions locate the actual false sibling with Windows path, original line and XML");
        diagnosticPlan.Cases.Single().SourceLocations.Clear();
        engine = new ScenarioExecution(diagnosticPlan.Cases.Single(), backend, new TestClock()); engine.Poll(1);
        check(engine.Outcome.Diagnostics.Single().Line == null && engine.Outcome.Diagnostics.Single().Element.EndsWith("ActorState[1]"),
            "missing node mappings do not misreport the Case declaration as the assertion line");
        var anyXml = Suite(caseBody: body.Replace("<ActorState actor='p' state='alive'/>", "<Any><ActorState actor='p' state='dead'/><ActorState actor='p' state='alive'/></Any>")
            .Replace("</Steps>", "<Wait id='stable' stableTicks='120' timeoutRealSeconds='2'><ActorState actor='p' state='alive'/></Wait></Steps>"));
        clock = new TestClock(); engine = new ScenarioExecution(Compile(anyXml).Cases.Single(), backend, clock);
        engine.Poll(1); engine.Poll(2); clock.Ms = 2000; engine.Poll(2);
        check(engine.Outcome.Diagnostics.Single().StepId == "stable" && engine.Outcome.Diagnostics.Single().Element.EndsWith("Wait[0]"),
            "a successful Any branch and matched stability wait do not blame a previously false condition");
        var notXml = Suite(caseBody: body.Replace("<ActorState actor='p' state='alive'/>", "<Not><ActorState actor='p' state='alive'/></Not>"));
        engine = new ScenarioExecution(Compile(notXml).Cases.Single(), backend, new TestClock()); engine.Poll(1);
        check(engine.Outcome.Diagnostics.Single().Element.EndsWith("Not[0]"), "failed negation points to Not instead of blaming its true child");
        var deadlineXml = waitXml.Replace("<Case id=", "<Case timeoutRealSeconds='1' id=");
        clock = new TestClock(); engine = new ScenarioExecution(Compile(deadlineXml).Cases.Single(), new Backend { Match = false }, clock);
        engine.Poll(1); clock.Ms = 1000; engine.Poll(1);
        check(engine.Outcome.Diagnostics.Single().Element == "Case[0]", "case deadline points to Case rather than a stale failed predicate");
        engine = new ScenarioExecution(plan.Cases.Single(), new Backend { ProbeThrows = true }, new TestClock()); engine.Poll(1);
        check(engine.Outcome.Diagnostics.Single().Element.EndsWith("ActorState[0]"), "probe exceptions retain the exact sampled condition");
        engine = new ScenarioExecution(plan.Cases.Single(), new Backend { Match = false, CleanupThrows = true }, new TestClock()); engine.Poll(1);
        check(engine.Outcome.Diagnostics.Count == 2 && engine.Outcome.Diagnostics[0].StepId == "ready" && engine.Outcome.Diagnostics[1].Code == "scenario.cleanup",
            "cleanup failure preserves the original assertion diagnostic");
        var failureFixture = Path.Combine("AutoTest.Core.Tests", "Fixtures", "ScenarioFailures.autotest.xml");
        if (File.Exists(failureFixture))
        {
            var failures = ScenarioLoader.Load(failureFixture);
            ScenarioLoader.SelectRules(failures, new[] { "Batch1_ExpectedAssertionTimeout" });
            clock = new TestClock(); engine = new ScenarioExecution(failures.Cases.Single(), new Backend { Match = false }, clock);
            engine.Poll(1); clock.Ms = 2000; engine.Poll(1);
            diagnostic = engine.Outcome.Diagnostics.Single();
            check(diagnostic.File == Path.GetFullPath(failureFixture) && diagnostic.Line == 6 && diagnostic.StepId == "wrong" && diagnostic.Element == "Case[0]/Steps[0]/Wait[0]/HasHediff[0]" && diagnostic.Code == "scenario.timeout",
                "the real assertion-timeout fixture reports line 6 and its HasHediff node");
            using var serialized = new MemoryStream();
            new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(ScenarioOutcome)).WriteObject(serialized, engine.Outcome);
            using var roundTrip = System.Text.Json.JsonDocument.Parse(serialized.ToArray());
            check(roundTrip.RootElement.GetProperty("Diagnostics")[0].GetProperty("Line").GetInt32() == 6,
                "game report serialization retains structured runtime diagnostics");
        }
        var condition = XElement.Parse("<HasHediff quantifier='all'/>");
        check(!ScenarioExecution.Match(condition, new ScenarioSample(), new ScenarioSample()), "all on zero targets does not pass vacuously");
        var old = new ScenarioSample { TargetCount = 1, Matches = new HashSet<string> { "old-item" } };
        check(!ScenarioExecution.Match(XElement.Parse("<ThingExists transition='added'/>"), old, old), "old item movement is not new creation");
        var belowThreshold = new ScenarioSample { TargetCount = 1, Existing = new HashSet<string> { "old-item" } };
        check(!ScenarioExecution.Match(XElement.Parse("<HasHediff transition='added'/>"), old, belowThreshold), "severity change on an existing hediff is not an added hediff");
        var now = new ScenarioSample { TargetCount = 1, Matches = new HashSet<string> { "new-item" } };
        check(ScenarioExecution.Match(XElement.Parse("<ThingExists transition='added'/>"), now, old), "new object identity is an added transition");
        var transientXml = Suite(caseBody: body.Replace("<Require id='ready'><ActorState actor='p' state='alive'/></Require>", "<Checkpoint id='before'/><Wait id='ready' timeoutRealSeconds='2'><HasHediff actor='p' def='Metalblood' transition='added' since='before' observation='eventually'/></Wait>"));
        clock = new TestClock(); backend = new Backend { Match = false }; engine = new ScenarioExecution(Compile(transientXml).Cases[0], backend, clock); engine.Poll(1);
        backend.Match = true; engine.Observe(2); backend.Match = false; engine.Observe(2); engine.Poll(2); engine.Poll(3);
        check(engine.Completed && engine.Outcome.Status == "passed", "event observation retains a same-tick transient effect");
        var interventionXml = Suite(caseBody: body.Replace("<Require id='ready'><ActorState actor='p' state='alive'/></Require>", "<Checkpoint id='before'/><DebugAction id='inject' actionId='native.addHediff' def='Metalblood'><Select mode='single' actor='p'/></DebugAction><Wait id='ready'><HasHediff actor='p' def='Metalblood' transition='added' since='before'/></Wait>"));
        clock = new TestClock(); backend = new Backend { Match = false, DebugMakesMatch = true }; engine = new ScenarioExecution(Compile(interventionXml).Cases[0], backend, clock);
        for (int tick = 0; tick < 5 && !engine.Completed; tick++) engine.Poll(tick);
        check(engine.Outcome.Status == "failed" && engine.Outcome.Error.Contains("cannot be attributed") && engine.Outcome.Trace.Any(t => t.Stage == "debug.interference"), "debug-injected effect is recorded and never attributed to a previous cast baseline");
        var basePath = Path.Combine("AutoTest.Core.Tests", "Fixtures", "Scenarios");
        if (Directory.Exists(basePath))
        {
            var fixture = ScenarioLoader.Load(basePath);
            check(fixture.Inputs.Count == 1 && fixture.Cases.Count == 2 && fixture.Inputs[0].Sha256.Length == 64, "nonrecursive Chinese-path fixture directory freezes file list and byte hashes");
        }
    }
    private sealed class TestClock : IAutoTestClock { public long Ms; public DateTime UtcNow => DateTime.UnixEpoch.AddMilliseconds(Ms); public long RealtimeMilliseconds => Ms; }
    private sealed class Backend : IScenarioBackend, IScenarioCompletionBackend
    {
        public bool Match = true, ProbeThrows, PrepareThrows, CleanupThrows, DebugMakesMatch, CompletionThrows; public int Cleaned;
        public Func<XElement, bool> MatchesCondition;
        public void Prepare(ScenarioCase c) { if (PrepareThrows) throw new Exception("prepare fault"); }
        public ScenarioSample Sample(XElement c) { if (ProbeThrows) throw new Exception("probe fault"); return new ScenarioSample { TargetCount = 1, Matches = (MatchesCondition?.Invoke(c) ?? Match) ? new HashSet<string> { "p" } : new HashSet<string>() }; }
        public bool Execute(XElement step, bool first) { if (step.Name == "DebugAction" && DebugMakesMatch) Match = true; return true; }
        public void Cleanup() { Cleaned++; if (CleanupThrows) throw new Exception("cleanup fault"); }
        public void ValidateCompletion() { if (CompletionThrows) throw new ScenarioConfigurationException("unused coverage target"); }
    }
}
