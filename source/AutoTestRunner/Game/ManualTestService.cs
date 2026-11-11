using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using LogSyncMod.AutoTest.Scenarios;
using RimWorld;
using Verse;

namespace LogSyncMod.AutoTest
{
    internal sealed class ManualThingSnapshot
    {
        public string DefName { get; set; }
        public string Stuff { get; set; }
        public string Quality { get; set; }
        public string Color { get; set; }
        public int? HitPoints { get; set; }
        public int StackCount { get; set; }
    }
    internal sealed class ManualTestInvocation
    {
        public string InvocationId { get; set; }
        public string InputProject { get; set; }
        public string ProjectIdentity { get; set; }
        public string PawnHash { get; set; }
        public string PawnTemplateXml { get; set; }
        public ManualTestSelection Selection { get; set; }
        public List<ManualTestChoice> Plan { get; set; }
        public List<ManualTestTarget> Targets { get; set; }
        public Dictionary<string, TemporaryPawnSnapshot> InstancePawns { get; set; } = new Dictionary<string, TemporaryPawnSnapshot>();
        public Dictionary<string, ManualThingSnapshot> InstanceThings { get; set; } = new Dictionary<string, ManualThingSnapshot>();
        public List<AutoTestResult> Results = new List<AutoTestResult>();
        public List<AutoTestEvidenceManifest> Evidence = new List<AutoTestEvidenceManifest>();
        public string Status { get; set; } = "queued";
        public string Error { get; set; }
        public string StartedAtUtc { get; set; }
        public string CompletedAtUtc { get; set; }
    }

    internal static class ManualTestService
    {
        private static readonly ManualArtifactSession session = new ManualArtifactSession();
        internal static string GameLaunchId => session.GameLaunchId;
        internal static string ManualSessionId => session.ManualSessionId;
        internal static TestArtifactDirectory Artifacts => session.Artifacts;
        internal static AutoTestRunArtifacts RunArtifacts { get; private set; }
        internal static readonly List<ManualTestInvocation> Invocations = new List<ManualTestInvocation>();
        private static readonly Queue<Tuple<ManualTestInvocation, TemporaryPawnSnapshot>> pending = new Queue<Tuple<ManualTestInvocation, TemporaryPawnSnapshot>>();
        private static ManualTestInvocation current;
        private static TemporaryPawnSnapshot pawnSnapshot;
        private static Queue<ManualTestChoice> choices;
        private static AutoTestMainMap studio;
        private static AutoTestRunner runner;
        private static IDisposable logGuard;
        private static TimeSpeed originalSpeed;
        private static bool cancelling;
        private static bool polling;
        private static int taskIndex;
        internal static bool Busy => current != null || pending.Count > 0;
        internal static Map SourceMap => studio?.Original;
        internal static string Progress => current == null ? "空闲" : "手动批次 " + current.InvocationId.Substring(0, 8) + "：已记录 " + current.Results.Count + "，剩余 " + choices?.Count;
        internal static bool ShouldForceAbility(Ability ability) => runner?.ShouldForceAbilityForSkillTest(ability) == true;
        internal static ManualTestSelection LastSelection()
        {
            var path = SelectionPath;
            return File.Exists(path) ? JsonSerializer.Deserialize<ManualTestSelection>(File.ReadAllText(path)) : null;
        }
        private static string SelectionPath => Path.Combine(GenFilePaths.ConfigFolderPath, "RimDevTools.ManualTestSelection.json");

        internal static ManualTestInvocation Submit(ManualProjectContext project, ManualTestCatalog available, ManualTestSelection selection)
        {
            if (ArenaService.Busy || GroupCombatService.Busy || CombatFightService.Busy) throw new InvalidOperationException("群战赛事正在运行，请先结束。");
            if (AutoTestBootstrap.GetRuntimeStatus().State is "running" or "queued" || global::LogSyncMod.LogSyncMod.settings is { testSessionActive: true } settings && settings.testSessionKind != "manual") throw new InvalidOperationException("已有自动测试/CLI 测试会话正在运行。");
            var frozenSelection = ManualTestPlan.Freeze(selection);
            var plan = available.Preview(frozenSelection);
            if (!plan.Any(c => c.Applicable)) throw new InvalidOperationException("没有适用的目标与测试类别组合。");
            TemporaryPawnSnapshot staged = null;
            var path = TemporaryPawnData.PathFor(project.Root);
            if (File.Exists(path)) staged = TemporaryPawnData.Load(project.Root);
            var invocation = new ManualTestInvocation {
                InvocationId = Guid.NewGuid().ToString("N"), InputProject = project.Root, ProjectIdentity = project.Identity,
                PawnHash = staged?.Sha256, PawnTemplateXml = staged?.TemplateXml, Selection = frozenSelection, Plan = plan,
                Targets = available.Targets.Where(t => frozenSelection.Targets.Contains(t.Key)).Select(t => new ManualTestTarget { PackageId = t.PackageId, ModName = t.ModName, DefType = t.DefType, DefName = t.DefName, Label = t.Label, InstanceId = t.InstanceId }).ToList(),
                StartedAtUtc = DateTime.UtcNow.ToString("O")
            };
            foreach (var target in invocation.Targets.Where(t => t.InstanceId != null))
            {
                if (!available.Instances.TryGetValue(target.Key, out var thing) || thing.Destroyed || !thing.Spawned) throw new InvalidOperationException("所选实例已失效：" + target.Label);
                if (thing is Pawn pawn) invocation.InstancePawns.Add(target.Key, TemporaryPawnCapture.Capture(pawn));
                else
                {
                    if (thing.def.useHitPoints && thing.HitPoints <= 0) throw new InvalidOperationException("所选实例已经损坏：" + target.Label);
                    var style = new ManualThingSnapshot { DefName = thing.def.defName, Stuff = thing.Stuff?.defName, HitPoints = thing.def.useHitPoints ? thing.HitPoints : null, StackCount = thing.stackCount };
                    if (thing.TryGetQuality(out var quality)) style.Quality = quality.ToString();
                    if (thing.TryGetComp<CompColorable>() is CompColorable color && color.Active) style.Color = "#" + UnityEngine.ColorUtility.ToHtmlStringRGBA(color.Color);
                    invocation.InstanceThings.Add(target.Key, style);
                }
            }
            if (Artifacts == null)
            {
                session.Ensure(project.Root);
                RunArtifacts = AutoTestReportWriter.CreateRunArtifacts(Artifacts.AutoTestOutputPath, "manual", ManualSessionId);
                RunArtifacts.IsManual = true;
            }
            Invocations.Add(invocation);
            try { Write(); AtomicWrite(SelectionPath, JsonSerializer.Serialize(frozenSelection)); }
            catch { Invocations.Remove(invocation); throw; }
            pending.Enqueue(Tuple.Create(invocation, staged));
            logGuard ??= global::LogSyncMod.TestLogWindowGuard.RegisterExternalSession();
            return invocation;
        }

        internal static void Cancel()
        {
            cancelling = true;
            runner?.RequestCancel(null);
            while (pending.Count > 0) { var item = pending.Dequeue().Item1; item.Status = "cancelled"; item.CompletedAtUtc = DateTime.UtcNow.ToString("O"); }
            if (current == null) { cancelling = false; logGuard?.Dispose(); logGuard = null; }
            Write();
        }

        internal static void Frame()
        {
            if (polling || !Busy) return;
            polling = true;
            try
            {
                if (current == null)
                {
                    var next = pending.Dequeue(); current = next.Item1; pawnSnapshot = next.Item2;
                    choices = new Queue<ManualTestChoice>(current.Plan.Where(c => c.Applicable)); current.Status = "running"; taskIndex = 0;
                    foreach (var excluded in current.Plan.Where(c => !c.Applicable)) current.Results.Add(new AutoTestResult { CaseId = current.InvocationId + "/excluded/" + current.Results.Count, CaseType = excluded.CaseType, DefName = excluded.TargetKey, Status = "unsupported", ErrorMessage = excluded.Reason });
                    logGuard ??= global::LogSyncMod.TestLogWindowGuard.RegisterExternalSession(); originalSpeed = Find.TickManager.CurTimeSpeed;
                    studio = new AutoTestMainMap(Find.CurrentMap, 170031, new IntVec3(120, 1, 120));
                }
                if (runner == null)
                {
                    if (choices.Count == 0 || cancelling) { FinishInvocation(); return; }
                    var choice = choices.Dequeue(); taskIndex++;
                    var target = current.Targets.Single(t => t.Key == choice.TargetKey);
                    current.InstancePawns.TryGetValue(target.Key, out var instancePawn); current.InstanceThings.TryGetValue(target.Key, out var instanceThing);
                    var id = current.InvocationId + "/" + taskIndex;
                    var settings = AutoTestRunner.ManualSettings(target.PackageId, new[] { target.DefName }, id, Artifacts.AutoTestOutputPath);
                    runner = new AutoTestRunner(settings, studio.Map, manualOptions: new ManualRunnerOptions {
                        Choice = choice, PawnSnapshot = instancePawn ?? pawnSnapshot, InstancePawn = instancePawn != null, ThingSnapshot = instanceThing, Artifacts = RunArtifacts,
                        Completed = (results, evidence) => { current.Results.AddRange(results); current.Evidence.Add(evidence); Write(); }
                    });
                    using (runner.ManualPawnScope()) runner.Initialize();
                }
                if (!AutoTestBootstrap.TemporaryPauseBypass) Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
                runner.PollPocketMapCleanupWatchdog();
                if (runner.Completed) runner = null;
            }
            catch (Exception ex)
            {
                if (current != null) { current.Error = ex.ToString(); current.Results.Add(new AutoTestResult { CaseId = current.InvocationId + "/host-error/" + taskIndex, CaseType = "ManualHost", Status = "failed", Stage = "host", ErrorMessage = ex.ToString() }); }
                if (runner != null && !runner.Completed) { runner.RequestCancel(null); try { runner.Tick(); } catch { } }
                runner = null;
                // A broken setup is terminal for this invocation; subsequent submitted invocations still run.
                FinishInvocation();
            }
            finally { polling = false; }
        }

        internal static void OnGui()
        {
            if (runner != null) using (runner.ManualPawnScope()) runner.ProcessUi();
        }

        internal static void Tick()
        {
            if (runner == null || runner.Completed) return;
            try { using (runner.ManualPawnScope()) runner.Tick(); }
            catch (Exception ex)
            {
                current.Results.Add(new AutoTestResult { CaseId = current.InvocationId + "/task-error/" + taskIndex, CaseType = "ManualHost", Status = "failed", ErrorMessage = ex.ToString() });
                runner.RequestCancel(null);
            }
        }

        private static void FinishInvocation()
        {
            if (current == null) return;
            try { studio?.Dispose(); }
            catch (Exception ex) { current.Error = (current.Error ?? "") + "\nCleanup: " + ex; }
            finally { studio = null; runner = null; if (pending.Count == 0) { logGuard?.Dispose(); logGuard = null; } AutoTestBootstrap.SetTemporaryPauseBypass(false); Find.TickManager.CurTimeSpeed = originalSpeed; }
            current.Status = cancelling ? "cancelled" : current.Error != null || current.Results.Any(r => AutoTestResultStatus.IsFailure(r.Status)) ? "completed_with_failures" : "completed";
            current.CompletedAtUtc = DateTime.UtcNow.ToString("O");
            current = null; cancelling = false;
            try { Write(); }
            finally { if (pending.Count == 0) global::LogSyncMod.TestSessionCompletion.Complete("manual", global::LogSyncMod.LogSyncMod.settings?.autoTestRunId); }
        }

        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { WriteIndented = true, IncludeFields = true };
        internal static void Write()
        {
            if (Artifacts == null) return;
            if (!Directory.Exists(Artifacts.DirectoryPath) || !File.Exists(Path.Combine(Artifacts.DirectoryPath, "session.json"))) throw new IOException("手动测试会话原始目录已被清理，不重建旧证据；请重启游戏后由正式分配器建立新会话。原路径：" + Artifacts.DirectoryPath);
            Directory.CreateDirectory(RunArtifacts.ScreenshotDirectory);
            AtomicWrite(RunArtifacts.ResultPath, JsonSerializer.Serialize(new { schemaVersion = 1, source = "manual", gameLaunchId = GameLaunchId, manualSessionId = ManualSessionId, artifacts = Artifacts, invocations = Invocations }, Json));
            var report = new StringBuilder("# RimDevTools 手动测试汇总\n\n");
            report.AppendLine("GameLaunchId: `" + GameLaunchId + "`  \nManualSessionId: `" + ManualSessionId + "`\n");
            var log = new StringBuilder();
            foreach (var invocation in Invocations)
            {
                report.AppendLine("## " + invocation.InvocationId + "\n\n- 状态：" + invocation.Status + "\n- 输入项目：" + invocation.InputProject + "\n- 角色哈希：" + (invocation.PawnHash ?? "无暂存角色") + "\n");
                report.AppendLine("| CaseId | 类型 | 目标 | 结果 | 原因 |\n|---|---|---|---|---|");
                foreach (var result in invocation.Results) report.AppendLine("| " + Cell(result.CaseId) + " | " + Cell(result.CaseType) + " | " + Cell(result.DefName) + " | " + Cell(result.Status) + " | " + Cell((result.ErrorMessage ?? "").Split('\n')[0]) + " |");
                foreach (var result in invocation.Results.Where(r => r.Scenario != null))
                    foreach (var equipment in result.Scenario.Trace.Where(t => t.Stage == "view.equipment")) report.AppendLine("\n" + result.CaseId + " 配装：" + equipment.Details + "\n");
                foreach (var result in invocation.Results) foreach (var image in result.ScreenshotPaths ?? new List<string>()) report.AppendLine("\n![" + result.CaseId + "](" + Path.GetRelativePath(Path.GetDirectoryName(RunArtifacts.ReportPath), image).Replace('\\', '/') + ")\n");
                foreach (var evidence in invocation.Evidence.Where(e => e != null))
                foreach (var item in evidence.Logs) log.AppendLine("[Invocation=" + invocation.InvocationId + "] [Case=" + item.CaseId + "] [" + item.Level + "] [Occurrences=" + item.RepeatCount + "] " + item.Message);
            }
            AtomicWrite(RunArtifacts.ReportPath, report.ToString()); AtomicWrite(Artifacts.SyncLogPath, log.ToString());
        }

        private static void AtomicWrite(string path, string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)); var temporary = path + ".tmp";
            try { File.WriteAllText(temporary, content, new UTF8Encoding(false)); File.Move(temporary, path, true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        private static string Cell(string value) => (value ?? "").Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
    }
}
