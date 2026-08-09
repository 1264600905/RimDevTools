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
        Console.WriteLine($"AutoTest.Core.Tests passed: {passed}");
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
        Assert(outcome.TimedOut && outcome.Attempts == 2 && outcome.LastSnapshot == "not-ready", "wait timeout retains snapshot");

        attempts = 0;
        var interval = new AutoTestWaitHandle(new AutoTestWaitOptions { ConditionId = "interval", TimeoutMs = 1000, MinimumPollIntervalTicks = 3 },
            () => { attempts++; return new AutoTestProbeResult(); }, clock);
        interval.Poll(10);
        interval.Poll(11);
        interval.Poll(12);
        interval.Poll(13);
        Assert(attempts == 2, "wait honors tick poll interval");

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
        var caseWindow = windowBuilder.AddCaseWindow("case", eventCursor, logCursor, windowJournal, windowLogs);
        Assert(caseWindow.Operations.Count == 1 && caseWindow.Events.All(item => item.Sequence > eventCursor && item.CaseId == "case") &&
            caseWindow.Logs.Count == 1 && caseWindow.Logs[0].Message == "inside", "case evidence uses event and log cursors");
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
        new AutoTestEvidenceBuilder(manifest, clock).Pass("roundtrip", "ok");
        var path = Path.Combine(Path.GetTempPath(), "autotest-evidence-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            AutoTestEvidenceSerializer.Write(path, manifest);
            var restored = AutoTestEvidenceSerializer.Read(path);
            Assert(restored.SchemaVersion == 1 && restored.RunId == "run" && restored.Assertions.Count == 1, "evidence JSON round trip");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
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
}
