using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using LogSyncMod.AutoTest.Scenarios;
using RimWorld;
using Verse;

namespace LogSyncMod.AutoTest
{
    // Original group benchmark scheduler. Native fight state belongs to CombatFightService.
    internal static class GroupCombatService
    {
        private static string consumedRunId, outputPath;
        private static GroupCombatRunReport report;
        private static Queue<GroupCombatRoundInput> pending;
        private static IDisposable logGuard;
        private static bool waiting;
        private static bool manualSession;
        private static TimeSpeed originalSpeed;
        private static readonly ManualArtifactSession manualArtifacts = new();
        internal static bool Busy => report != null && report.CompletedAtUtc == null;
        internal static void Cancel()
        {
            if (!Busy) return;
            report.Status = "cancelled"; report.Error = "Cancelled by user"; pending?.Clear(); CombatFightService.Cancel();
        }
        internal static void BeginManual(ScenarioPlan plan, ManualProjectContext project)
        {
            if (Busy || ArenaService.Busy || CombatFightService.Busy || ManualTestService.Busy || GroupCombatDamageCapture.ProbeActive
                || AutoTestBootstrap.GetRuntimeStatus().State is "running" or "queued" || global::LogSyncMod.LogSyncMod.settings is { testSessionActive: true })
                throw new InvalidOperationException("已有测试会话正在运行。");
            if (Find.CurrentMap == null || LongEventHandler.AnyEventNowOrWaiting) throw new InvalidOperationException("请等待地图加载完成。");
            var compiled = ScenarioLoader.Compile(plan.Inputs, true); ScenarioLoader.SelectRules(compiled, plan.SelectedTestDefNames);
            outputPath = manualArtifacts.Ensure(project.Root).AutoTestOutputPath; Directory.CreateDirectory(outputPath);
            originalSpeed = Find.TickManager.CurTimeSpeed;
            report = new GroupCombatRunReport { RunId = Guid.NewGuid().ToString("N"), StartedAtUtc = DateTime.UtcNow.ToString("O"), Inputs = plan.Inputs,
                GameVersion = VersionControl.CurrentVersionStringWithRev, LoadedMods = LoadedModManager.RunningMods.Select(m => m.PackageIdPlayerFacing + "@" + m.ModMetaData?.ModVersion).ToList() };
            manualSession = true; waiting = false;
            pending = new Queue<GroupCombatRoundInput>();
            logGuard = global::LogSyncMod.TestLogWindowGuard.RegisterExternalSession();
            try { ResolveGroupBudgets(compiled); pending = new Queue<GroupCombatRoundInput>(report.PlannedRounds); Write(); }
            catch (Exception ex) { report.Error = ex.ToString(); Finish(); throw; }
        }
        internal static IEnumerable<GroupCombatRoundResult> Rounds => report?.Rounds ?? Enumerable.Empty<GroupCombatRoundResult>();
        private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
        internal static void Frame()
        {
            try
            {
                var settings = global::LogSyncMod.LogSyncMod.settings;
                if (report == null || report.CompletedAtUtc != null)
                {
                    if (settings is not { testSessionActive: true, testSessionKind: "groupcombat" } || settings.autoTestRunId == consumedRunId || Current.ProgramState != ProgramState.Playing || LongEventHandler.AnyEventNowOrWaiting || Find.CurrentMap == null || CombatFightService.Busy || GroupCombatDamageCapture.ProbeActive) return;
                    consumedRunId = settings.autoTestRunId; outputPath = settings.autoTestOutputPath;
                    manualSession = false;
                    report = new GroupCombatRunReport { RunId = consumedRunId, StartedAtUtc = DateTime.UtcNow.ToString("O"), GameVersion = VersionControl.CurrentVersionStringWithRev, LoadedMods = LoadedModManager.RunningMods.Select(m => m.PackageIdPlayerFacing + "@" + m.ModMetaData?.ModVersion).ToList() };
                    logGuard = global::LogSyncMod.TestLogWindowGuard.RegisterExternalSession(); waiting = false;
                    var frozen = JsonSerializer.Deserialize<ScenarioPlan>(Encoding.UTF8.GetString(Convert.FromBase64String(settings.groupCombatPlan)));
                    if (frozen?.Purpose != "groupcombat" || frozen.SchemaVersion != 1) throw new ScenarioConfigurationException("Invalid frozen group combat plan");
                    var compiled = ScenarioLoader.Compile(frozen.Inputs, groupCombat: true);
                    ScenarioLoader.SelectRules(compiled, frozen.SelectedCaseIds?.Count > 0 ? frozen.SelectedCaseIds : frozen.SelectedTestDefNames);
                    report.Inputs = frozen.Inputs; ResolveGroupBudgets(compiled);
                    pending = new Queue<GroupCombatRoundInput>(report.PlannedRounds); Write();
                }
                if (waiting || CombatFightService.Busy) return;
                if (report.Error != null || pending.Count == 0) { Finish(); return; }
                waiting = true;
                CombatFightService.BeginGroupFight(report.RunId, outputPath, pending.Dequeue(), report.Rounds.Count + 1, OnFightCompleted, Write);
            }
            catch (Exception ex)
            {
                Log.Error("[LogSyncGroupCombat] " + ex);
                if (report != null) report.Error = ex.ToString();
                if (CombatFightService.Busy) CombatFightService.Cancel(); else Finish();
            }
        }
        private static void OnFightCompleted(GroupCombatRoundResult result)
        {
            waiting = false; report.Rounds.Add(result);
            if (result.ExecutionStatus != "completed" || !result.CleanupComplete)
            {
                report.Status = result.ExecutionStatus == "cancelled" ? "cancelled" : "error";
                report.Error ??= result.Error ?? "Group combat execution or cleanup failed"; pending.Clear();
            }
            Write();
        }
        private static void ResolveGroupBudgets(ScenarioPlan compiled)
        {
            report.PlannedRounds = GroupCombatPlan.Expand(compiled);
            var squadCache = new Dictionary<string, ResolvedCombatRoster>(StringComparer.Ordinal);
            foreach (var round in report.PlannedRounds)
            {
                round.ResolvedRosters = new List<ResolvedCombatRoster>();
                var xml = XElement.Parse(round.Xml);
                foreach (var actor in xml.Element("Actors").Elements().Where(a => a.Name == "PawnGroup" || a.Name == "Pawn"))
                {
                    var template = actor.Attribute("squad") == null ? null : SquadTemplates.Parse(actor.Element("SquadTemplate"), round.Source);
                    var oldExtent = PawnGroupFormation.Extents(actor, true).x;
                    var cacheKey = round.CaseId.Substring(0, round.CaseId.LastIndexOf("/round-", StringComparison.Ordinal)) + ":" + round.Seed + ":" + ScenarioLoader.A(actor, "id");
                    if (!squadCache.TryGetValue(cacheKey, out var roster))
                    {
                        roster = template == null ? SquadGeneration.ResolveActor(ScenarioLoader.A(actor, "id"), actor, null,
                            actor.Attribute("combatPowerBudget") == null ? null : CombatRoster.Positive(ScenarioLoader.A(actor, "combatPowerBudget")),
                            ScenarioLoader.I(actor, "count", 1), ScenarioLoader.A(actor, "costMode", "pawnKind"), round.Seed)
                            : SquadGeneration.Resolve(ScenarioLoader.A(actor, "id"), template,
                            actor.Attribute("combatPowerBudget") == null ? null : CombatRoster.Positive(ScenarioLoader.A(actor, "combatPowerBudget")),
                            ScenarioLoader.I(actor, "count", 1), ScenarioLoader.A(actor, "costMode", "pawnKind"),
                            SquadTemplates.MemberSeed(round.Seed, ScenarioLoader.A(actor, "id"), 0), Find.CurrentMap.Tile);
                        squadCache.Add(cacheKey, roster);
                    }
                    roster.Team = ScenarioLoader.A(actor, "team"); roster.Source = round.Source; roster.DisplayName = template?.Id ?? ScenarioLoader.A(actor, "id");
                    actor.Attribute("squad")?.Remove(); actor.Attribute("combatPowerBudget")?.Remove(); actor.Attribute("costMode")?.Remove();
                    actor.Element("SquadTemplate")?.Remove();
                    if (actor.Name == "PawnGroup") actor.SetAttributeValue("count", roster.ResolvedCount);
                    if (actor.Element("basePawnKind") == null) actor.Add(new XElement("basePawnKind", roster.PawnKind));
                    if (round.Swapped && actor.Name == "PawnGroup") actor.SetAttributeValue("originX", ScenarioLoader.I(actor, "originX", 20) - PawnGroupFormation.Extents(actor, true).x + oldExtent);
                    round.ResolvedRosters.Add(roster);
                }
                xml.Attribute("packageId")?.Remove();
                var suite = new XElement("TestSuite", new XAttribute("schemaVersion", 1), new XAttribute("id", "resolvedSquads"), xml);
                ScenarioLoader.Compile(new[] { new ScenarioInput { Path = round.Source, Sha256 = round.InputHash, Xml = suite.ToString(SaveOptions.DisableFormatting) } }, true);
                round.Xml = xml.ToString(SaveOptions.DisableFormatting);
            }
        }

        private static void Finish()
        {
            if (report == null || report.CompletedAtUtc != null) return;
            report.Status = report.Status == "cancelled" ? "cancelled" : report.Error != null || report.Rounds.Count != report.PlannedRounds.Count ? "error" : "completed";
            report.CompletedAtUtc = DateTime.UtcNow.ToString("O");
            try { Write(); }
            finally
            {
                logGuard?.Dispose(); logGuard = null; pending = null;
                global::LogSyncMod.TestAudioController.Restore("groupcombat_completed");
                Log.Message("[LogSyncGroupCombat] COMPLETED runId=" + report.RunId + " status=" + report.Status);
                if (manualSession) { manualSession = false; Find.TickManager.CurTimeSpeed = originalSpeed; Messages.Message("群战测试已结束：" + report.Status + "。结果：" + outputPath, MessageTypeDefOf.TaskCompletion, false); }
                else global::LogSyncMod.TestSessionCompletion.Complete("groupcombat", report.RunId);
            }
        }
        private static void Write()
        {
            if (report == null || outputPath == null) return;
            Directory.CreateDirectory(outputPath);
            report.Experiments = GroupCombatExperimentSummary.Calculate(report.Rounds);
            report.ActiveRound = waiting ? CombatFightService.Display : null;
            var stem = Path.Combine(outputPath, "groupcombat_" + report.RunId);
            AtomicWrite(stem + "_Result.json", JsonSerializer.Serialize(report, Json));
            AtomicWrite(stem + "_Report.md", GroupCombatReport.Write(report));
        }
        private static void AtomicWrite(string path, string value)
        {
            var temporary = path + ".tmp";
            try { File.WriteAllText(temporary, value, new UTF8Encoding(false)); File.Move(temporary, path, true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
