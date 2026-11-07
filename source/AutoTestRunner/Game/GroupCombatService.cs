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
            var rosters = new Dictionary<string, List<ResolvedCombatRoster>>();
            var inputs = new List<ScenarioInput>();
            // Resolve the entire selected plan before any map or Pawn is created.
            foreach (var definition in compiled.Cases)
            {
                var xml = XElement.Parse(definition.Xml);
                rosters[definition.CaseId] = CombatRoster.ResolveGroups(xml, kind => (DefDatabase<PawnKindDef>.GetNamedSilentFail(kind) ?? throw new ScenarioConfigurationException("Unknown loaded PawnKind: " + kind, "COMBAT_POWER_INVALID")).combatPower, definition.Source);
                var package = (string)xml.Attribute("packageId"); xml.Attribute("packageId")?.Remove();
                var suite = new XElement("TestSuite", new XAttribute("schemaVersion", "1"), new XAttribute("id", "resolved"), new XAttribute("packageId", package ?? "ludeon.rimworld"), xml);
                inputs.Add(new ScenarioInput { Path = definition.Source, Sha256 = definition.InputHash, Xml = suite.ToString(SaveOptions.DisableFormatting) });
            }
            // Preserve matrix identities; validate each resolved Case without recompiling its IDs.
            for (int i = 0; i < compiled.Cases.Count; i++)
            {
                if (rosters[compiled.Cases[i].CaseId].Count == 0) continue;
                ScenarioLoader.Compile(new[] { inputs[i] }, true);
                compiled.Cases[i].Xml = XElement.Parse(inputs[i].Xml).Element("Case").ToString(SaveOptions.DisableFormatting);
            }
            report.PlannedRounds = GroupCombatPlan.Expand(compiled);
            foreach (var round in report.PlannedRounds) round.ResolvedRosters = rosters[compiled.Cases.Single(c => round.CaseId.StartsWith(c.CaseId + "/round-", StringComparison.Ordinal)).CaseId];
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
                global::LogSyncMod.TestSessionCompletion.Complete("groupcombat", report.RunId);
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
