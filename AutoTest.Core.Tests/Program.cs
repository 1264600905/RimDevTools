using LogSyncMod.AutoTest;

var suite = new CoreTestSuite();
suite.Run();

internal sealed class CoreTestSuite
{
    private int passed;

    public void Run()
    {
        TestOperationRunner();
        TestOperationContext();
        TestJournal();
        TestWaitHandle();
        TestLogJournal();
        TestEvidence();
        TestBoundedBuffer();
        TestEvidenceRoundTrip();
        TestGeneDependencyResolver();
        TestResultStatusSemantics();
        TestPawnFactionPlanner();
        TestBodyTypeMatchPolicy();
        TestTargetIds();
        TestSelectionFingerprint();
        TestReadiness();
        TestSemanticValueGraph();
        TestErrorPolicy();
        TestRuntimeQueue();
        TestRuntimeHistory();
        TestCaseEvidenceRetention();
        TestDomainAssertions();
        TestRecipeExecutionPolicies();
        TestCombatDistancePolicy();
        Console.WriteLine($"AutoTest.Core.Tests passed: {passed}");
    }

    private void TestCombatDistancePolicy()
    {
        var shortRange = AutoTestCombatDistancePolicy.Calculate(new AutoTestCombatDistanceRequest
        {
            VerbMinRange = 0f,
            VerbRange = 7.9f,
            MinimumSeparationMargin = 0.5f,
            MaximumRangeMargin = 0.5f,
            MinimumDistanceFloor = 2f,
            MapSafeMaximum = 30f
        });
        Assert(shortRange.IsValid && Math.Abs(shortRange.MinValidDistance - 2f) < 0.001f &&
            Math.Abs(shortRange.MaxValidDistance - 7.4f) < 0.001f &&
            shortRange.PreferredDistance > 6f && shortRange.PreferredDistance < 6.5f,
            "combat distance policy keeps short-range weapons below the old five-cell floor");

        var indirect = AutoTestCombatDistancePolicy.Calculate(new AutoTestCombatDistanceRequest
        {
            VerbMinRange = 19.9f,
            VerbRange = 89.9f,
            MinimumSeparationMargin = 0.5f,
            MaximumRangeMargin = 0.5f,
            MinimumDistanceFloor = 5f,
            MapSafeMaximum = 25f
        });
        Assert(indirect.IsValid && Math.Abs(indirect.MinValidDistance - 20.4f) < 0.001f &&
            Math.Abs(indirect.MaxValidDistance - 25f) < 0.001f &&
            Math.Abs(indirect.PreferredDistance - 25f) < 0.001f,
            "combat distance policy respects minRange and clamps indirect fire to map capacity");

        var impossible = AutoTestCombatDistancePolicy.Calculate(new AutoTestCombatDistanceRequest
        {
            VerbMinRange = 19.9f,
            VerbRange = 89.9f,
            MinimumSeparationMargin = 0.5f,
            MaximumRangeMargin = 0.5f,
            MinimumDistanceFloor = 5f,
            MapSafeMaximum = 18f
        });
        Assert(!impossible.IsValid && impossible.FailureReason == "no_legal_distance_interval",
            "combat distance policy reports maps that cannot contain a legal firing interval");
    }

    private void TestRecipeExecutionPolicies()
    {
        var standard = AutoTestRecipeExecutionClassifier.Classify(new AutoTestRecipeClassificationInput());
        Assert(standard.Kind == AutoTestRecipeExecutionKind.Standard && standard.Adapter == "StandardRecipeExecutionAdapter", "ordinary recipes use the standard adapter");

        var gestation = AutoTestRecipeExecutionClassifier.Classify(new AutoTestRecipeClassificationInput { GestationCycles = 2, ProductIsPawn = true, WorktableIsMechGestator = true });
        Assert(gestation.Kind == AutoTestRecipeExecutionKind.MechGestation && gestation.Reason.Contains("gestationCycles"), "gestation recipes are classified before product creation");

        var resurrection = AutoTestRecipeExecutionClassifier.Classify(new AutoTestRecipeClassificationInput { MechResurrection = true, HasCorpseIngredient = true });
        Assert(resurrection.Kind == AutoTestRecipeExecutionKind.MechResurrection, "mech resurrection recipes use the resurrection adapter");

        var pawnProduct = AutoTestRecipeExecutionClassifier.Classify(new AutoTestRecipeClassificationInput { ProductIsPawn = true });
        Assert(pawnProduct.Kind == AutoTestRecipeExecutionKind.PawnProduct && pawnProduct.Adapter == "UnsupportedSpecialRecipeExecutionAdapter", "unsafe pawn products never fall through to MakeRecipeProducts");

        Assert(AutoTestRetryPolicy.CanRetry(AutoTestFailureKind.TransientPawnGeneration, 1) &&
            AutoTestRetryPolicy.CanRetry(AutoTestFailureKind.TransientUiTiming, 1) &&
            !AutoTestRetryPolicy.CanRetry(AutoTestFailureKind.DeterministicFailure, 1) &&
            !AutoTestRetryPolicy.CanRetry(AutoTestFailureKind.TransientPawnGeneration, 2), "retry policy allows one retry only for explicit transient failures");

        var part = AutoTestHediffPartPolicy.Select(new[]
        {
            new AutoTestHediffPartCandidate<string> { Value = "other", Source = AutoTestHediffPartSourceKind.OtherInstallRecipe, StableName = "A" },
            new AutoTestHediffPartCandidate<string> { Value = "target-z", Source = AutoTestHediffPartSourceKind.TargetModInstallRecipe, StableName = "Z" },
            new AutoTestHediffPartCandidate<string> { Value = "default", Source = AutoTestHediffPartSourceKind.DefaultInstallPart, StableName = "Default" }
        });
        Assert(part.Value == "default", "hediff part policy prioritizes defaultInstallPart over recipe candidates");

        var pawns = AutoTestHediffPawnPolicy.Order(new[]
        {
            new AutoTestHediffPawnCandidate<string> { Value = "colonist", Source = AutoTestHediffPawnCandidateSource.VanillaColonist, StableName = "Colonist" },
            new AutoTestHediffPawnCandidate<string> { Value = "native-b", Source = AutoTestHediffPawnCandidateSource.DirectStartingHediff, StableName = "B" },
            new AutoTestHediffPawnCandidate<string> { Value = "native-a", Source = AutoTestHediffPawnCandidateSource.DirectStartingHediff, StableName = "A" }
        });
        Assert(pawns.Select(candidate => candidate.Value).SequenceEqual(new[] { "native-a", "native-b", "colonist" }), "hediff pawn candidates use source priority and stable DefName ordering");

        var placement = AutoTestProductPlacementVerifier.Verify(
            new Dictionary<string, int> { ["Mass"] = 200 },
            new Dictionary<string, int> { ["Mass"] = 200 },
            new[]
            {
                new AutoTestProductStackSnapshot { DefName = "Mass", StackCount = 75, StackLimit = 75 },
                new AutoTestProductStackSnapshot { DefName = "Mass", StackCount = 75, StackLimit = 75 },
                new AutoTestProductStackSnapshot { DefName = "Mass", StackCount = 50, StackLimit = 75 }
            });
        Assert(placement.Success, "standard product placement accepts split stacks with preserved total");

        var oversized = AutoTestProductPlacementVerifier.Verify(
            new Dictionary<string, int> { ["Mass"] = 80 },
            new Dictionary<string, int> { ["Mass"] = 80 },
            new[] { new AutoTestProductStackSnapshot { DefName = "Mass", StackCount = 80, StackLimit = 75 } });
        Assert(!oversized.Success && oversized.ErrorMessage.Contains("stackLimit"), "standard product placement rejects oversized stacks");

        Assert(AutoTestRecipeResultPolicy.FailureStatus(AutoTestFailureKind.UnsupportedPath) == AutoTestResultStatus.Unsupported &&
            AutoTestRecipeResultPolicy.FailureStatus(AutoTestFailureKind.DeterministicFailure) == "failed_recipe_produce" &&
            AutoTestRecipeResultPolicy.FailureStatus(AutoTestFailureKind.UnexpectedSevereLog) == "failed_recipe_produce",
            "recipe result policy distinguishes unsupported paths from execution failures");
    }

    private void TestOperationRunner()
    {
        var clock = new FakeClock();
        var journal = new AutoTestOperationJournal();
        using (AutoTestOperationContext.PushRun("run"))
        {
            var runner = new AutoTestOperationRunner("run", clock, journal);
            var success = runner.Run("cap", "case", "success", () => 42, value => value.ToString());
            Assert(success.Success && success.Status == AutoTestOperationStatus.Completed && success.ResultSummary == "42", "operation success envelope");

            var failed = runner.Run("cap", "case", "failed", () => throw new InvalidOperationException("boom"));
            Assert(failed.Status == AutoTestOperationStatus.Failed && failed.Error.Code == "operation.failed" && failed.Error.Message == "boom", "operation exception conversion");

            var timedOut = runner.Run("cap", "case", "timeout", () => throw new TimeoutException("late"));
            Assert(timedOut.Status == AutoTestOperationStatus.TimedOut && timedOut.Error.Code == "operation.timed_out", "operation timeout conversion");

            var cancelled = runner.Run("cap", "case", "cancel", () => throw new OperationCanceledException("stop"));
            Assert(cancelled.Status == AutoTestOperationStatus.Cancelled && cancelled.Error.Code == "operation.cancelled", "operation cancellation conversion");
        }
    }

    private void TestOperationContext()
    {
        Assert(AutoTestOperationContext.Current.RunId == null, "operation context starts empty");
        using (AutoTestOperationContext.PushRun("run"))
        {
            Assert(AutoTestOperationContext.Current.RunId == "run", "operation context run scope");
            using (AutoTestOperationContext.PushCase("case"))
            using (AutoTestOperationContext.PushStep("step", "cap"))
            using (AutoTestOperationContext.PushOperation("op"))
                Assert(AutoTestOperationContext.Current.CaseId == "case" && AutoTestOperationContext.Current.StepId == "step" && AutoTestOperationContext.Current.OperationId == "op", "operation context nested scope");
            Assert(AutoTestOperationContext.Current.CaseId == null, "operation context nested restore");
        }
        Assert(AutoTestOperationContext.Current.RunId == null, "operation context run restore");
    }

    private void TestJournal()
    {
        var clock = new FakeClock();
        var journal = new AutoTestOperationJournal();
        using (AutoTestOperationContext.PushRun("run"))
        {
            var runner = new AutoTestOperationRunner("run", clock, journal);
            var first = runner.Run("cap", "case", "one", () => { });
            var cursor = journal.LastEventSequence;
            var second = runner.Run("cap", "case", "two", () => { });
            var after = journal.GetEvents(cursor, 10);
            Assert(after.Count == 2 && after.All(item => item.OperationId == second.OperationId), "event afterSequence query");
            Assert(journal.GetOperation(first.OperationId).Status == AutoTestOperationStatus.Completed, "operation journal terminal snapshot");
            Assert(!journal.RecordTerminal(first), "operation journal rejects duplicate terminal");
        }
    }

    private void TestWaitHandle()
    {
        var clock = new FakeClock();
        var attempts = 0;
        var wait = new AutoTestWaitHandle(new AutoTestWaitOptions { ConditionId = "third", TimeoutMs = 1000, RequiredStablePolls = 1, MinimumPollIntervalTicks = 1 },
            () => new AutoTestProbeResult { Satisfied = ++attempts == 3, Snapshot = attempts.ToString() }, clock);
        Assert(!wait.Poll(1).Terminal && !wait.Poll(2).Terminal && wait.Poll(3).Satisfied, "wait succeeds on third poll");

        attempts = 0;
        var stable = new AutoTestWaitHandle(new AutoTestWaitOptions { ConditionId = "stable", TimeoutMs = 1000, RequiredStablePolls = 3, MinimumPollIntervalTicks = 1 },
            () => new AutoTestProbeResult { Satisfied = true, Snapshot = (++attempts).ToString() }, clock);
        Assert(!stable.Poll(1).Terminal && !stable.Poll(2).Terminal && stable.Poll(3).Satisfied, "wait requires stable polls");

        attempts = 0;
        var recovering = new AutoTestWaitHandle(new AutoTestWaitOptions { ConditionId = "recover", TimeoutMs = 1000 }, () =>
        {
            attempts++;
            if (attempts == 1) throw new TimeoutException("probe transient");
            return new AutoTestProbeResult { Satisfied = true, Snapshot = "ready" };
        }, clock);
        Assert(recovering.Poll(1).ProbeFailureCount == 1 && recovering.Poll(2).Satisfied, "wait recovers from probe exception");

        var timeoutClock = new FakeClock();
        var timeout = new AutoTestWaitHandle(new AutoTestWaitOptions { ConditionId = "never", TimeoutMs = 50 },
            () => new AutoTestProbeResult { Satisfied = false, Snapshot = "not-ready" }, timeoutClock);
        timeout.Poll(1);
        timeoutClock.Advance(60);
        var outcome = timeout.Poll(2);
        Assert(outcome.TimedOut && outcome.Attempts == 1 && outcome.LastSnapshot == "not-ready", "wait timeout retains snapshot");

        attempts = 0;
        var interval = new AutoTestWaitHandle(new AutoTestWaitOptions { ConditionId = "interval", TimeoutMs = 1000, MinimumPollIntervalTicks = 3 },
            () => { attempts++; return new AutoTestProbeResult(); }, clock);
        interval.Poll(10);
        interval.Poll(11);
        interval.Poll(12);
        interval.Poll(13);
        Assert(attempts == 2, "wait honors tick poll interval");

        var pausedClock = new FakeClock();
        var paused = new AutoTestWaitHandle(new AutoTestWaitOptions { ConditionId = "paused", TimeoutMs = 50, MinimumPollIntervalTicks = 3 },
            () => new AutoTestProbeResult { Satisfied = false }, pausedClock);
        paused.Poll(10);
        pausedClock.Advance(60);
        var pausedOutcome = paused.Poll(10);
        Assert(pausedOutcome.TimedOut && pausedOutcome.Attempts == 1, "wait timeout is enforced while game tick is paused");

        var lateClock = new FakeClock();
        var late = new AutoTestWaitHandle(new AutoTestWaitOptions { ConditionId = "late", TimeoutMs = 50, MinimumPollIntervalTicks = 0 }, () =>
        {
            lateClock.Advance(60);
            return new AutoTestProbeResult { Satisfied = true, Snapshot = "late-ready" };
        }, lateClock);
        var lateOutcome = late.Poll(1);
        Assert(lateOutcome.TimedOut && !lateOutcome.Satisfied && lateOutcome.Attempts == 1, "wait rejects probe success after realtime deadline");

        var journal = new AutoTestOperationJournal();
        AutoTestWaitHandle journaled;
        using (AutoTestOperationContext.PushRun("run"))
        using (AutoTestOperationContext.PushCase("case"))
        using (AutoTestOperationContext.PushStep("wait_step", "cap"))
            journaled = new AutoTestWaitHandle(new AutoTestWaitOptions { ConditionId = "journaled", TimeoutMs = 1000 },
                () => new AutoTestProbeResult { Satisfied = true, Snapshot = "ready" }, clock, journal);
        journaled.Poll(1);
        var waitOperation = journal.GetRecentOperations(10, true).Single();
        Assert(waitOperation.StepId == "wait_step" && waitOperation.Status == AutoTestOperationStatus.Completed &&
            journal.GetEvents(0, 20).Where(item => item.EventType.StartsWith("wait.")).All(item => item.OperationId == waitOperation.OperationId), "wait publishes correlated operation lifecycle");
    }

    private void TestLogJournal()
    {
        var logs = new AutoTestLogJournal();
        var now = DateTime.UtcNow;
        logs.Record("Warning", "same", "pkg", "run", "case", "step", "op", "cap", now);
        logs.Record("warn", "same", "pkg", "run", "case", "step", "op", "cap", now.AddSeconds(1));
        logs.Record("Error", "bad", "pkg", "run", "case", "step", "op2", "cap", now.AddSeconds(2));
        var all = logs.GetEntries(0, "info", "run");
        Assert(all.Count == 2 && all[0].RepeatCount == 2, "log journal folds repeats");
        Assert(logs.GetEntries(0, "error", "run").Single().OperationId == "op2", "log level and operation correlation");
        Assert(logs.GetEntries(all[0].Sequence, "info", "run").Count == 1, "log cursor filtering");
    }

    private void TestEvidence()
    {
        var clock = new FakeClock();
        var manifest = NewManifest(clock);
        var journal = new AutoTestOperationJournal();
        var runner = new AutoTestOperationRunner("run", clock, journal);
        var builder = new AutoTestEvidenceBuilder(manifest, clock);
        using (AutoTestOperationContext.PushRun("run"))
        using (AutoTestOperationContext.PushCase("case"))
        {
            var operation = runner.Run("cap", "case", "step", () => { });
            builder.OperationSucceeded("operation", operation);
            builder.IsTrue("truth", true);
        }
        Assert(builder.Complete(journal).Success, "evidence succeeds when assertions and operations pass");

        var failedManifest = NewManifest(clock);
        var failedBuilder = new AutoTestEvidenceBuilder(failedManifest, clock);
        failedBuilder.Fail("bad", "yes", "no");
        Assert(!failedBuilder.Complete().Success, "failed assertion fails evidence");

        var errorManifest = NewManifest(clock);
        var errorBuilder = new AutoTestEvidenceBuilder(errorManifest, clock);
        errorBuilder.AddError(new AutoTestOperationError { Code = "error", Message = "bad" });
        Assert(!errorBuilder.Complete().Success, "evidence error fails evidence");

        var windowManifest = NewManifest(clock);
        var windowBuilder = new AutoTestEvidenceBuilder(windowManifest, clock);
        var windowJournal = new AutoTestOperationJournal();
        var windowLogs = new AutoTestLogJournal();
        windowJournal.RecordEvent("run.started", "run", null, null, null, "run", "before");
        windowLogs.Record("info", "before", null, "run", null, null, null, null);
        var eventCursor = windowJournal.LastEventSequence;
        var logCursor = windowLogs.LastSequence;
        new AutoTestOperationRunner("run", clock, windowJournal).Run("cap", "case", "step", () => { });
        windowJournal.RecordEvent("case.completed", "run", "case", null, null, "cap", "done");
        windowLogs.Record("info", "inside", null, "run", "case", "step", null, "cap");
        windowLogs.Record("error", "other case severe", null, "run", "other", null, null, null);
        windowLogs.Record("error", "async severe", null, "run", null, null, null, null);
        long logThrough = windowLogs.LastSequence;
        windowLogs.Record("error", "too late", null, "run", null, null, null, null);
        var caseWindow = windowBuilder.AddCaseWindow("case", eventCursor, logCursor, windowJournal, windowLogs, windowJournal.LastEventSequence, logThrough);
        Assert(caseWindow.Operations.Count == 1 && caseWindow.Events.All(item => item.Sequence > eventCursor && item.CaseId == "case") &&
            caseWindow.Logs.Select(item => item.Message).SequenceEqual(new[] { "inside", "async severe" }), "case evidence uses bounded cursors and retains uncorrelated severe logs");
    }

    private void TestBoundedBuffer()
    {
        var buffer = new AutoTestBoundedBuffer<int>(2);
        buffer.Add(1); buffer.Add(2); buffer.Add(3);
        Assert(buffer.Snapshot().SequenceEqual(new[] { 2, 3 }), "bounded buffer removes oldest entry");
    }

    private void TestEvidenceRoundTrip()
    {
        var clock = new FakeClock();
        var manifest = NewManifest(clock);
        const string escapedText = "中文, \"quoted\" \\ path\nnext line";
        var builder = new AutoTestEvidenceBuilder(manifest, clock);
        builder.Pass("roundtrip", escapedText, escapedText);
        manifest.Logs.Add(new AutoTestLogEvidence
        {
            Sequence = 1,
            TimestampUtc = clock.UtcNow,
            Level = "info",
            Message = escapedText,
            RepeatCount = 1,
            RunId = manifest.RunId
        });
        var path = Path.Combine(Path.GetTempPath(), "autotest-evidence-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            AutoTestEvidenceSerializer.Write(path, manifest);
            string json = File.ReadAllText(path);
            var restored = AutoTestEvidenceSerializer.Read(path);
            Assert(json.Contains(Environment.NewLine) && json.Contains(Environment.NewLine + "  \"runId\"") &&
                json.Contains(Environment.NewLine + "  \"assertions\"") && json.Contains(Environment.NewLine + "    {"),
                "evidence JSON is indented");
            Assert(restored.SchemaVersion == 1 && restored.RunId == "run" && restored.Assertions.Count == 1 && restored.Logs.Count == 1 &&
                restored.Assertions[0].Actual == escapedText && restored.Assertions[0].Details == escapedText && restored.Logs[0].Message == escapedText &&
                restored.StartedAtUtc == manifest.StartedAtUtc,
                "evidence JSON formatting preserves escaped strings and round trips");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private void TestGeneDependencyResolver()
    {
        var a = new GeneDependencyFixture("A");
        var b = new GeneDependencyFixture("B") { Prerequisite = a };
        var c = new GeneDependencyFixture("C") { Prerequisite = b };
        bool resolved = AutoTestGeneDependencyResolver.TryResolve(c, item => item.Prerequisite, item => item.Name, item => true,
            out List<GeneDependencyFixture> dependencies, out string errorCode, out _);
        Assert(resolved && errorCode == null && dependencies.Select(item => item.Name).SequenceEqual(new[] { "A", "B" }),
            "gene dependency resolver returns prerequisite-first order");

        a.Prerequisite = b;
        resolved = AutoTestGeneDependencyResolver.TryResolve(c, item => item.Prerequisite, item => item.Name, item => true,
            out dependencies, out errorCode, out _);
        Assert(!resolved && errorCode == "gene_dependency_cycle", "gene dependency resolver rejects cycles");

        a.Prerequisite = null;
        resolved = AutoTestGeneDependencyResolver.TryResolve(c, item => item.Prerequisite, item => item.Name, item => item.Name != "A",
            out dependencies, out errorCode, out _);
        Assert(!resolved && errorCode == "gene_dependency_missing", "gene dependency resolver rejects missing prerequisites");
    }

    private void TestResultStatusSemantics()
    {
        foreach (string status in new[]
        {
            AutoTestResultStatus.Passed,
            AutoTestResultStatus.PassedAfterRetry,
            AutoTestResultStatus.Skipped,
            AutoTestResultStatus.Unsupported,
            AutoTestResultStatus.CompatibilityWarning
        })
            Assert(AutoTestResultStatus.IsSuccess(status) && !AutoTestResultStatus.IsFailure(status), "result status is non-failing: " + status);
        Assert(AutoTestResultStatus.IsFailure(AutoTestResultStatus.Failed) && AutoTestResultStatus.Canonicalize("failed_spawn") == AutoTestResultStatus.Failed,
            "result status canonicalizes module-specific failure strings");

        var clock = new FakeClock();
        var journal = new AutoTestOperationJournal();
        var manifest = NewManifest(clock);
        using (AutoTestOperationContext.PushRun("run"))
        using (AutoTestOperationContext.PushCase("compatibility"))
            new AutoTestOperationRunner("run", clock, journal).Run("compatibility", "compatibility", "observe", () => throw new InvalidOperationException("compatibility failure"));
        journal.MarkCaseOperationsOptional("compatibility");
        AutoTestEvidenceManifest diagnosticEvidence = new AutoTestEvidenceBuilder(manifest, clock).Complete(journal);
        Assert(diagnosticEvidence.Success && diagnosticEvidence.Operations.Single().Required == false && diagnosticEvidence.Errors.Count == 0,
            "optional compatibility operation does not fail evidence");
    }

    private void TestPawnFactionPlanner()
    {
        AutoTestPawnFactionDecision mechanoid = AutoTestPawnFactionPlanner.Resolve(AutoTestPawnFactionMode.Native, true, null, Array.Empty<string>(), "PlayerColony");
        Assert(mechanoid.EffectiveMode == AutoTestPawnFactionMode.Mechanoid, "faction planner selects mechanoid context");

        AutoTestPawnFactionDecision native = AutoTestPawnFactionPlanner.Resolve(AutoTestPawnFactionMode.Native, false, "DMS_Army", new[] { "PlayerColony", "DMS_Army" }, "PlayerColony");
        Assert(native.EffectiveMode == AutoTestPawnFactionMode.Native && native.FactionDefName == "DMS_Army", "faction planner preserves PawnKind native faction");

        AutoTestPawnFactionDecision missing = AutoTestPawnFactionPlanner.Resolve(AutoTestPawnFactionMode.Native, false, "DMS_Army", new[] { "PlayerColony" }, "PlayerColony");
        Assert(missing.EffectiveMode == AutoTestPawnFactionMode.FactionlessFallback && missing.FactionDefName == "DMS_Army", "faction planner uses null when native faction instance is absent");

        AutoTestPawnFactionDecision compatibility = AutoTestPawnFactionPlanner.Resolve(AutoTestPawnFactionMode.PlayerCompatibility, false, "DMS_Army", new[] { "PlayerColony", "DMS_Army" }, "PlayerColony");
        Assert(compatibility.EffectiveMode == AutoTestPawnFactionMode.PlayerCompatibility && compatibility.FactionDefName == "PlayerColony", "faction planner uses player only for explicit compatibility mode");
    }

    private void TestBodyTypeMatchPolicy()
    {
        var rows = new[]
        {
            (AutoTestBodyTypeTargetKind.BodyType, false, false, true),
            (AutoTestBodyTypeTargetKind.Apparel, true, false, true),
            (AutoTestBodyTypeTargetKind.Weapon, true, false, true),
            (AutoTestBodyTypeTargetKind.PawnKind, true, false, true),
            (AutoTestBodyTypeTargetKind.Gene, false, false, false),
            (AutoTestBodyTypeTargetKind.Gene, false, true, true),
            (AutoTestBodyTypeTargetKind.Xenotype, false, false, false),
            (AutoTestBodyTypeTargetKind.Xenotype, false, true, true)
        };
        foreach (var row in rows)
            Assert(AutoTestBodyTypeMatchPolicy.RequireMatch(row.Item1, row.Item2, row.Item3) == row.Item4,
                "body type policy: " + row.Item1 + "/explicit=" + row.Item2 + "/static=" + row.Item3);
    }

    private void TestTargetIds()
    {
        Assert(AutoTestTargetIds.TryParseMap(AutoTestTargetIds.Map(42), out int mapId) && mapId == 42, "target id map round trip");
        Assert(AutoTestTargetIds.TryParseThing(AutoTestTargetIds.Thing(17), out int thingId) && thingId == 17, "target id thing round trip");
        Assert(AutoTestTargetIds.TryParsePawn(AutoTestTargetIds.Pawn(9), out int pawnId) && pawnId == 9, "target id pawn round trip");
        string window = AutoTestTargetIds.Window("capture:one", 3, "Dialog_InfoCard");
        Assert(AutoTestTargetIds.TryParseWindow(window, out string captureId, out int ordinal, out string type) && captureId == "capture:one" && ordinal == 3 && type == "Dialog_InfoCard", "target id window round trip");
    }

    private void TestSelectionFingerprint()
    {
        string first = AutoTestSelectionFingerprint.Create(new[] { "thing:1", "pawn:2" });
        string same = AutoTestSelectionFingerprint.Create(new[] { "thing:1", "pawn:2" });
        string reordered = AutoTestSelectionFingerprint.Create(new[] { "pawn:2", "thing:1" });
        Assert(first == same && first != reordered, "selection fingerprint deterministic and order-sensitive");
        string gizmo = AutoTestSelectionFingerprint.CreateGizmoId(first, 2, "Command_Action|Cast");
        Assert(AutoTestSelectionFingerprint.TryReadSelectionFingerprint(gizmo, out string embedded) && embedded == first, "gizmo id embeds selection fingerprint");
        Assert(AutoTestSelectionFingerprint.MatchesSelection(gizmo, first) && !AutoTestSelectionFingerprint.MatchesSelection(gizmo, reordered), "stale gizmo selection rejected");
    }

    private void TestReadiness()
    {
        var ready = AutoTestReadinessEvaluator.Evaluate(new AutoTestReadinessInput
        {
            HasCurrentGame = true, MapCount = 1, HasCurrentMap = true, ProgramState = "Playing", LongEventPending = false,
            TickManagerReady = true, WindowStackReady = true, SelectorReady = true, ScreenFadeClear = true
        });
        Assert(ready.GameDataReady && ready.MapDataReady && ready.Playable && ready.VisualReady && ready.AutomationReady, "readiness marks stable playing game ready");
        var fading = AutoTestReadinessEvaluator.Evaluate(new AutoTestReadinessInput
        {
            HasCurrentGame = true, MapCount = 1, HasCurrentMap = true, ProgramState = "Playing", LongEventPending = false,
            TickManagerReady = true, WindowStackReady = true, SelectorReady = true, ScreenFadeClear = false
        });
        Assert(fading.Playable && !fading.VisualReady && !fading.AutomationReady, "readiness separates playable from visual readiness");
        var longEvent = AutoTestReadinessEvaluator.Evaluate(new AutoTestReadinessInput
        {
            HasCurrentGame = true, MapCount = 1, HasCurrentMap = true, ProgramState = "Playing", LongEventPending = true,
            TickManagerReady = true, WindowStackReady = true, SelectorReady = true, ScreenFadeClear = true
        });
        Assert(longEvent.MapDataReady && !longEvent.VisualReady && !longEvent.AutomationReady, "readiness preserves map data during long event");
    }

    private void TestSemanticValueGraph()
    {
        var cycle = new GraphFixture { Name = "root", Values = new List<int> { 1, 2, 3, 4 } };
        cycle.Self = cycle;
        var graph = new AutoTestSemanticValueGraph(new AutoTestSemanticGraphOptions { MaxDepth = 4, MaxChildrenPerNode = 20, MaxCollectionItems = 2, MaxStringLength = 64 });
        AutoTestSemanticNode node = graph.Describe(cycle);
        Assert(FindNode(node, "Self")?.IsReference == true, "semantic graph marks repeated references");
        Assert(FindNode(node, "Values")?.Truncated == true && FindNode(node, "Values").Children.Count == 2, "semantic graph enforces collection limit");
        Assert(FindNode(node, "Throwing")?.ValueKind == "error", "semantic graph records getter exceptions");
        Assert(FindNode(node, "PrivateValue")?.Value == "private", "semantic graph includes private fields safely");

        var shallow = new AutoTestSemanticValueGraph(new AutoTestSemanticGraphOptions { MaxDepth = 1, MaxChildrenPerNode = 20, MaxCollectionItems = 20, MaxStringLength = 64 }).Describe(cycle);
        Assert(shallow.Children.Any(child => child.Truncated), "semantic graph truncates at max depth");
    }

    private void TestErrorPolicy()
    {
        var logs = new List<AutoTestLogEvidence>
        {
            new AutoTestLogEvidence { Sequence = 1, Level = "warning", Message = "warn" },
            new AutoTestLogEvidence { Sequence = 2, Level = "error", Message = "allowed failure marker" },
            new AutoTestLogEvidence { Sequence = 3, Level = "fatal", Message = "unexpected fatal" }
        };
        AutoTestErrorPolicyResult result = new AutoTestErrorPolicy().Evaluate(logs, new[] { "allowed failure" }, null);
        Assert(!result.Success && result.AllowedSevereLogs.Count == 1 && result.UnexpectedSevereLogs.Count == 1 && result.Warnings.Count == 1, "error policy applies level and allowlist rules");
        var journal = new AutoTestLogJournal();
        journal.Record("error", "old", null, "run", "case", null, null, null);
        long cursor = journal.LastSequence;
        journal.Record("error", "new", null, "run", "case", null, null, null);
        journal.Record("error", "other case", null, "run", "other", null, null, null);
        journal.Record("error", "async uncorrelated", null, "run", null, null, null, null);
        AutoTestErrorPolicyResult cursorResult = new AutoTestErrorPolicy().Evaluate(journal, cursor, "run", "case");
        Assert(cursorResult.UnexpectedSevereLogs.Select(item => item.Message).SequenceEqual(new[] { "new", "async uncorrelated" }), "error policy isolates concurrent case logs and includes asynchronous logs");
        AutoTestErrorPolicyResult bounded = new AutoTestErrorPolicy().Evaluate(journal, cursor, "run", "case", null, null, cursor + 2);
        Assert(bounded.UnexpectedSevereLogs.Select(item => item.Message).SequenceEqual(new[] { "new" }), "error policy honors case end cursor");
    }

    private void TestRuntimeQueue()
    {
        Assert(AutoTestRuntimeQueue.MatchesQueuedRun("RUN-1", "run-1", true), "queued cancellation matches run id case-insensitively");
        Assert(!AutoTestRuntimeQueue.MatchesQueuedRun("run-2", "run-1", true) &&
            !AutoTestRuntimeQueue.MatchesQueuedRun("run-1", "run-1", false), "queued cancellation rejects stale or inactive requests");
    }

    private void TestRuntimeHistory()
    {
        var history = new AutoTestRuntimeHistory();
        history.Retain("timeout-run", new AutoTestEvidenceSnapshotDto { Found = true, RunId = "timeout-run", ErrorCount = 1 });
        AutoTestEvidenceSnapshotDto retained = history.GetEvidence("TIMEOUT-RUN");
        Assert(history.LastRunId == "timeout-run" && retained?.Found == true && retained.ErrorCount == 1, "runtime history retains timeout evidence for API lookup");
        Assert(history.GetEvidence("other-run") == null, "runtime history rejects unrelated run ids");
    }

    private void TestCaseEvidenceRetention()
    {
        var clock = new FakeClock();
        var manifest = NewManifest(clock);
        var journal = new AutoTestOperationJournal();
        var logs = new AutoTestLogJournal();
        var builder = new AutoTestEvidenceBuilder(manifest, clock);
        using (AutoTestOperationContext.PushRun("run"))
        using (AutoTestOperationContext.PushCase("early"))
        {
            new AutoTestOperationRunner("run", clock, journal).Run("cap", "early", "early_step", () => { });
        }
        long earlyEvent = journal.LastEventSequence;
        long earlyLog = logs.LastSequence;
        var early = builder.AddCaseWindow("early", 0, earlyLog, journal, logs, earlyEvent, logs.LastSequence);
        using (AutoTestOperationContext.PushCase("later"))
        {
            var runner = new AutoTestOperationRunner("run", clock, journal);
            for (int i = 0; i < 501; i++) runner.Run("cap", "later", "later_" + i, () => { });
        }
        Assert(early.Operations.Count == 1 && early.Operations[0].StepId == "early_step", "case evidence retains early operation timeline after global truncation");
    }

    private void TestDomainAssertions()
    {
        var manifest = NewManifest(new FakeClock());
        var assertions = new AutoTestAssertions(new AutoTestEvidenceBuilder(manifest, new FakeClock()));
        AutoTestAssertionEvidence passedAssertion = assertions.Add("expected_actual", true, "ready", "ready", "details");
        AutoTestAssertionEvidence failedAssertion = assertions.Add("expected_actual_failure", false, "ready", "missing", "details");
        Assert(passedAssertion.Expected == "ready" && passedAssertion.Actual == "ready" && passedAssertion.Success, "domain assertion preserves expected and actual on pass");
        Assert(failedAssertion.Expected == "ready" && failedAssertion.Actual == "missing" && !failedAssertion.Success, "domain assertion preserves expected and actual on failure");
    }

    private static AutoTestSemanticNode FindNode(AutoTestSemanticNode root, string name)
    {
        if (root == null) return null;
        if (root.Name == name) return root;
        foreach (AutoTestSemanticNode child in root.Children ?? new List<AutoTestSemanticNode>())
        {
            AutoTestSemanticNode found = FindNode(child, name);
            if (found != null) return found;
        }
        return null;
    }

    private static AutoTestEvidenceManifest NewManifest(FakeClock clock) => new AutoTestEvidenceManifest { RunId = "run", PackageId = "pkg", Suite = "auto", StartedAtUtc = clock.UtcNow };

    private void Assert(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + name);
        passed++;
        Console.WriteLine("PASS: " + name);
    }

    private sealed class FakeClock : IAutoTestClock
    {
        private readonly DateTime origin = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public long RealtimeMilliseconds { get; private set; }
        public DateTime UtcNow => origin.AddMilliseconds(RealtimeMilliseconds);
        public void Advance(long milliseconds) => RealtimeMilliseconds += milliseconds;
    }

    private sealed class GraphFixture
    {
        private string PrivateValue = "private";
        public string Name { get; set; }
        public GraphFixture Self { get; set; }
        public List<int> Values { get; set; }
        public string Throwing { get { throw new InvalidOperationException("getter failed"); } }
    }

    private sealed class GeneDependencyFixture
    {
        public GeneDependencyFixture(string name) { Name = name; }
        public string Name { get; }
        public GeneDependencyFixture Prerequisite { get; set; }
    }
}
