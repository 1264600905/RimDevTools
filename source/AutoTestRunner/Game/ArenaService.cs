using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using LogSyncMod.AutoTest.Scenarios;
using RimWorld;
using UnityEngine;
using Verse;

namespace LogSyncMod.AutoTest
{
    // Owns tournament progression. CombatFightService exclusively owns native battle state.
    internal static class ArenaService
    {
        private static string consumedRunId, outputPath;
        private static ArenaRunReport report;
        private static ArenaTournamentResult tournament;
        private static ArenaScheduler scheduler;
        private static ArenaMatch match;
        private static IDisposable logGuard;
        private static bool waiting, cancel;
        private static float championStarted;
        private static bool championCaptured;
        private static TimeSpeed championOriginalSpeed;
        private static ArenaMatch postBattleMatch;
        private static float postBattleStarted;
        private static bool postBattleCaptured;
        private static string postBattleScreenshot;
        private static TimeSpeed postBattleOriginalSpeed;
        private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
        internal static bool Busy => report != null && report.CompletedAtUtc == null;
        internal static void Cancel() { cancel = true; CombatFightService.Cancel(); }
        internal static object Status => new { busy = Busy, status = report?.Status, runId = report?.RunId, tournament = tournament?.Input.TestDefName, match = match?.Id, attempt = match?.Attempts.Count + 1, presentation = postBattleMatch != null ? "postBattle" : tournament?.Status == "champion" ? "champion" : null, presentationMatch = postBattleMatch?.Id, presentationSeconds = postBattleMatch == null ? 0 : Time.realtimeSinceStartup - postBattleStarted, artifacts = new { autoTestOutputPath = outputPath } };
        internal static ArenaScore ScoreFor(GroupCombatRoundResult fight)
        {
            var winner = fight.Teams.SingleOrDefault(t => t.Outcome == "win");
            return tournament == null || winner == null ? null : ArenaScore.Calculate(tournament.Input, winner.Initial,
                fight.Units.Count(u => u.Team == winner.Id && !u.JoinedDuringBattle && u.State == "standing"), fight.ElapsedTicks);
        }
        internal static void Frame()
        {
            try
            {
                var settings = global::LogSyncMod.LogSyncMod.settings;
                if (!Busy)
                {
                    if (settings is not { testSessionActive: true, testSessionKind: "arena" } || consumedRunId == settings.autoTestRunId || Current.ProgramState != ProgramState.Playing || LongEventHandler.AnyEventNowOrWaiting || Find.CurrentMap == null || CombatFightService.Busy || GroupCombatDamageCapture.ProbeActive) return;
                    consumedRunId = settings.autoTestRunId; outputPath = settings.autoTestOutputPath;
                    report = new ArenaRunReport { RunId = consumedRunId, StartedAtUtc = DateTime.UtcNow.ToString("O"), GameVersion = VersionControl.CurrentVersionStringWithRev, LoadedMods = LoadedModManager.RunningMods.Select(m => m.PackageIdPlayerFacing + "@" + m.ModMetaData?.ModVersion).ToList() };
                    logGuard = global::LogSyncMod.TestLogWindowGuard.RegisterExternalSession(); cancel = false; waiting = false; match = null; tournament = null; postBattleMatch = null;
                    var frozen = JsonSerializer.Deserialize<ArenaPlan>(Encoding.UTF8.GetString(Convert.FromBase64String(settings.arenaPlan)));
                    if (frozen?.Purpose != "arena" || frozen.SchemaVersion != 1) throw new ScenarioConfigurationException("Invalid frozen arena plan");
                    report.Plan = ArenaLoader.Compile(frozen.Inputs, frozen.SelectedTestDefNames);
                    report.Plan.SkippedFiles = frozen.SkippedFiles;
                    // Freeze every tournament's loaded Def values before generating any Pawn.
                    foreach (var input in report.Plan.Tournaments)
                    {
                        var result = new ArenaTournamentResult { Input = input }; report.Tournaments.Add(result); tournament = result;
                        foreach (var entrant in input.Entrants)
                        {
                            var kind = DefDatabase<PawnKindDef>.GetNamedSilentFail(entrant.PawnKind);
                            if (kind == null) throw new ScenarioConfigurationException("Unknown loaded PawnKind: " + entrant.PawnKind, "COMBAT_POWER_INVALID");
                            var roster = CombatRoster.Resolve(entrant.Id, entrant.PawnKind, CombatRoster.Power(kind.combatPower), entrant.Budget ?? input.Budget, entrant.Count ?? input.Count);
                            roster.DisplayName = entrant.Name ?? kind.LabelCap.ToString(); roster.Source = input.Source;
                            result.Rosters.Add(roster);
                        }
                        // Validate the largest formations against every entrant before battle.
                        foreach (var roster in result.Rosters)
                        {
                            var other = result.Rosters.First(r => r.Id != roster.Id);
                            ArenaLoader.CompileBattle(input, result.Rosters, new ArenaMatch { Id = "validation", Left = roster.Id, Right = other.Id }, 0);
                        }
                    }
                    tournament = null; Write();
                }
                if (waiting || CombatFightService.Busy) return;
                if (cancel) { Finish("cancelled", "Cancelled by user"); return; }
                if (postBattleMatch != null)
                {
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    var elapsed = Time.realtimeSinceStartup - postBattleStarted;
                    var ready = postBattleCaptured && AutoTestViewComposite.IsReady(postBattleScreenshot);
                    if (elapsed > 20 && !ready) throw new IOException("Post-battle screenshot did not become ready: " + postBattleScreenshot);
                    if (ready && elapsed >= ArenaPresentation.PostBattleSeconds)
                    {
                        Find.TickManager.CurTimeSpeed = postBattleOriginalSpeed;
                        postBattleMatch = null; Write();
                    }
                    return;
                }
                if (report.Status != "running") { Finish(report.Status, report.Error); return; }
                if (tournament?.Status == "champion")
                {
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    if (Time.realtimeSinceStartup - championStarted > 20 && (!championCaptured || !AutoTestViewComposite.IsReady(tournament.ChampionScreenshot))) throw new IOException("Champion screenshot did not become ready");
                    if (championCaptured && AutoTestViewComposite.IsReady(tournament.ChampionScreenshot) && Time.realtimeSinceStartup - championStarted >= tournament.Input.SettlementSeconds)
                    { tournament.Status = "completed"; Find.TickManager.CurTimeSpeed = championOriginalSpeed; Write(); }
                    return;
                }
                if (tournament == null || tournament.Status == "completed")
                {
                    tournament = report.Tournaments.FirstOrDefault(t => t.Status != "completed");
                    if (tournament == null) { Finish("completed", null); return; }
                    scheduler = new ArenaScheduler(tournament); match = null;
                }
                if (match == null) match = scheduler.Next();
                if (match == null)
                {
                    tournament.Champion = scheduler.Champion; tournament.Status = "champion";
                    championOriginalSpeed = Find.TickManager.CurTimeSpeed; Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    championStarted = Time.realtimeSinceStartup; championCaptured = false;
                    ArenaPostBattleView.Begin(tournament, tournament.Matches.Last());
                    var directory = Path.Combine(outputPath, "arena_" + report.RunId + "_Screenshots"); Directory.CreateDirectory(directory);
                    tournament.ChampionScreenshot = Path.Combine(directory, "tournament_" + (report.Tournaments.IndexOf(tournament) + 1).ToString("D3") + "_champion.png");
                    Write(); return;
                }
                var fight = ArenaLoader.CompileBattle(tournament.Input, tournament.Rosters, match, match.Attempts.Count);
                fight.ResolvedRosters = new() { tournament.Rosters.Single(r => r.Id == match.Left), tournament.Rosters.Single(r => r.Id == match.Right) };
                waiting = true; Write();
                CombatFightService.BeginArenaFight(report.RunId, outputPath, fight, tournament.Input, OnFightCompleted);
            }
            catch (Exception ex)
            {
                Log.Error("[LogSyncArena] " + ex);
                if (CombatFightService.Busy) { report.Error = ex.ToString(); report.Status = "error"; report.ErrorCode = (ex as ScenarioConfigurationException)?.Code; CombatFightService.Cancel(); }
                else { if (report != null) report.ErrorCode = (ex as ScenarioConfigurationException)?.Code; Finish("error", ex.ToString()); }
            }
        }
        private static void OnFightCompleted(GroupCombatRoundResult result)
        {
            waiting = false; match.Attempts.Add(result);
            match.BracketScreenshots.AddRange(result.Screenshots.Where(p => p.EndsWith("_bracket.png", StringComparison.Ordinal)));
            if (cancel || result.ExecutionStatus != "completed" || !result.CleanupComplete)
            { report.Status = cancel ? "cancelled" : "error"; report.Error ??= result.Error ?? "Battle execution/cleanup failed"; Write(); return; }
            var winner = result.Teams.SingleOrDefault(t => t.Outcome == "win");
            var completedMatch = match;
            if (winner == null)
            {
                if (match.Attempts.Count > tournament.Input.MaxReplays) { report.Status = "unresolvedDraw"; report.Error = "Draw replay limit exhausted: " + match.Id; }
            }
            else
            {
                var id = winner.Id == "A" ? match.Left : match.Right;
                scheduler.Resolve(id, ScoreFor(result)); match = null;
            }
            postBattleMatch = completedMatch; postBattleStarted = Time.realtimeSinceStartup; postBattleCaptured = false;
            postBattleOriginalSpeed = Find.TickManager.CurTimeSpeed; Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
            var directory = Path.Combine(outputPath, "arena_" + report.RunId + "_Screenshots"); Directory.CreateDirectory(directory);
            postBattleScreenshot = Path.Combine(directory, "tournament_" + (report.Tournaments.IndexOf(tournament) + 1).ToString("D3") + "_match_" + tournament.Matches.Count.ToString("D4") + "_attempt_" + completedMatch.Attempts.Count.ToString("D2") + "_postbattle.png");
            completedMatch.PostBattleScreenshots.Add(postBattleScreenshot);
            ArenaPostBattleView.Begin(tournament, completedMatch);
            Write();
        }
        private static void Finish(string status, string error)
        {
            if (report == null || report.CompletedAtUtc != null) return;
            if (postBattleMatch != null) { Find.TickManager.CurTimeSpeed = postBattleOriginalSpeed; postBattleMatch = null; }
            if (tournament?.Status == "champion") Find.TickManager.CurTimeSpeed = championOriginalSpeed;
            report.Status = status; report.Error = error; report.CompletedAtUtc = DateTime.UtcNow.ToString("O");
            foreach (var item in report.Tournaments.Where(t => t.Status != "completed"))
            { item.Status = item == tournament ? status : "notRun"; item.Error = item == tournament ? error : "Stopped before this tournament"; }
            try { Write(); }
            finally
            {
                logGuard?.Dispose(); logGuard = null;
                global::LogSyncMod.TestAudioController.Restore("arena_completed");
                Log.Message("[LogSyncArena] COMPLETED runId=" + report.RunId + " status=" + report.Status);
                global::LogSyncMod.TestSessionCompletion.Complete("arena", report.RunId);
            }
        }
        private static void Write()
        {
            if (report == null || outputPath == null) return;
            Directory.CreateDirectory(outputPath);
            var stem = Path.Combine(outputPath, "arena_" + report.RunId);
            Atomic(stem + "_Result.json", JsonSerializer.Serialize(report, Json));
            Atomic(stem + "_Report.md", ArenaReport.Write(report));
        }
        private static void Atomic(string path, string value)
        {
            var temp = path + ".tmp";
            try { File.WriteAllText(temp, value, new UTF8Encoding(false)); File.Move(temp, path, true); }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        internal static void OnGui()
        {
            if (!Busy) return;
            if (postBattleMatch != null)
            {
                var elapsed = Time.realtimeSinceStartup - postBattleStarted;
                ArenaPostBattleView.Draw(elapsed);
                if (!postBattleCaptured && Event.current.type == EventType.Repaint && elapsed >= ArenaPresentation.AnimationSeconds && AutoTestScreenshotService.PrepareCaptureFrame())
                {
                    CapturePresentation(postBattleScreenshot);
                    postBattleCaptured = true;
                }
                return;
            }
            if (tournament?.Status == "champion")
            {
                ArenaPostBattleView.Draw(ArenaPresentation.PostBattleSeconds, champion: true);
                if (!championCaptured && Event.current.type == EventType.Repaint && Time.realtimeSinceStartup - championStarted >= 1 && AutoTestScreenshotService.PrepareCaptureFrame())
                {
                    CapturePresentation(tournament.ChampionScreenshot);
                    championCaptured = true;
                }
                return;
            }
            if (!CombatFightService.IsArena) return;
            if (CombatFightService.Measuring && match != null)
            {
                var label = "擂台 · " + (tournament.Matches.Count) + "/" + (tournament.Rosters.Count - 1) + " 场 · " + (match.Kind == "extra" ? "低分加赛" : match.Kind == "qualification" ? "资格赛" : "第 " + match.Round + " 轮");
                Widgets.DrawWindowBackground(new Rect(UI.screenWidth / 2 - 200, 106, 400, 28));
                Widgets.Label(new Rect(UI.screenWidth / 2 - 190, 109, 380, 24), label);
            }
        }
        private static void CapturePresentation(string path)
        {
            var output = XElement.Parse(tournament.Input.Xml).Element("Output");
            var width = ScenarioLoader.I(output, "width", Screen.width); var height = ScenarioLoader.I(output, "height", Screen.height);
            if (width == Screen.width && height == Screen.height) ScreenCapture.CaptureScreenshot(path);
            else Find.CameraDriver.StartCoroutine(GroupCombatScreenshot.Capture(path, width, height));
        }
    }
}
