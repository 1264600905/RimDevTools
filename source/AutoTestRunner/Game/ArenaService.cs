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
        private static ArenaBackgroundCleanup backgroundCleanup;
        private static bool waiting, cancel;
        private static float championStarted;
        private static bool championCaptured;
        private static int championStartTick;
        private static TimeSpeed championOriginalSpeed;
        private static ArenaMatch postBattleMatch;
        private static float postBattleStarted;
        private static bool postBattleCaptured;
        private static string postBattleScreenshot;
        private static TimeSpeed postBattleOriginalSpeed;
        private static bool showcaseActive, showcaseCaptured;
        private static float showcasePageStarted;
        private static int showcasePage, showcaseStartTick;
        private static TimeSpeed showcaseOriginalSpeed;
        private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
        internal static bool Busy => report != null && report.CompletedAtUtc == null;
        internal static void Cancel() { cancel = true; CombatFightService.Cancel(); }
        internal static object Status => new {
            busy = Busy, status = report?.Status, runId = report?.RunId, tournament = tournament?.Input.TestDefName,
            match = match?.Id, attempt = match?.Attempts.Count + 1,
            presentation = showcaseActive ? "showcase" : postBattleMatch != null ? "postBattle" : tournament?.Status == "champion" ? "topFive" : null,
            presentationMatch = postBattleMatch?.Id,
            presentationSeconds = showcaseActive ? showcasePageStarted < 0 ? 0 : Time.realtimeSinceStartup - showcasePageStarted : postBattleMatch != null ? Time.realtimeSinceStartup - postBattleStarted : tournament?.Status == "champion" && championStarted >= 0 ? Time.realtimeSinceStartup - championStarted : 0,
            showcasePage = showcaseActive ? showcasePage + 1 : 0, showcasePages = tournament?.Input.ShowcasePages.Count ?? 0,
            artifacts = new { autoTestOutputPath = outputPath }
        };
        internal static ArenaScore ScoreFor(GroupCombatRoundResult fight)
        {
            var winner = fight.Teams.SingleOrDefault(t => t.Outcome == "win");
            return tournament == null || winner == null ? null : ArenaScore.Calculate(tournament.Input, fight);
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
                    logGuard = global::LogSyncMod.TestLogWindowGuard.RegisterExternalSession(); cancel = false; waiting = false; match = null; tournament = null; postBattleMatch = null; showcaseActive = false;
                    var frozen = JsonSerializer.Deserialize<ArenaPlan>(Encoding.UTF8.GetString(Convert.FromBase64String(settings.arenaPlan)));
                    if (frozen?.Purpose != "arena" || frozen.SchemaVersion != 1) throw new ScenarioConfigurationException("Invalid frozen arena plan");
                    report.Plan = ArenaLoader.Compile(frozen.Inputs, frozen.SelectedTestDefNames);
                    report.Plan.SkippedFiles = frozen.SkippedFiles;
                    backgroundCleanup = new ArenaBackgroundCleanup(Find.CurrentMap);
                    report.BackgroundCleanup = backgroundCleanup.Result;
                    // Freeze every tournament's roster and native loadouts before any battle.
                    foreach (var input in report.Plan.Tournaments)
                    {
                        var result = new ArenaTournamentResult { Input = input }; report.Tournaments.Add(result); tournament = result;
                        foreach (var entrant in input.Entrants)
                        {
                            ResolvedCombatRoster roster;
                            if (entrant.Squad != null)
                            {
                                roster = SquadGeneration.Resolve(entrant.Id, input.SquadTemplates[entrant.Squad], entrant.Budget ?? input.Budget,
                                    entrant.Count ?? input.Count, input.CostMode, SquadTemplates.MemberSeed(input.Seed, entrant.Id, 0), Find.CurrentMap.Tile);
                                roster.DisplayName = entrant.Name ?? entrant.Squad;
                            }
                            else
                            {
                                var kind = DefDatabase<PawnKindDef>.GetNamedSilentFail(entrant.PawnKind);
                                if (kind == null) throw new ScenarioConfigurationException("Unknown loaded PawnKind: " + entrant.PawnKind, "COMBAT_POWER_INVALID");
                                if (entrant.Xenotype != null && (!ModsConfig.BiotechActive || !kind.RaceProps.Humanlike
                                    || DefDatabase<XenotypeDef>.GetNamedSilentFail(entrant.Xenotype) == null))
                                    throw new ScenarioConfigurationException("Invalid arena xenotype for " + entrant.Id + ": " + entrant.Xenotype);
                                var power = CombatRoster.Power(kind.combatPower);
                                if (input.CostMode == "native" && entrant.Xenotype != null) power *= CombatRoster.Power(DefDatabase<XenotypeDef>.GetNamed(entrant.Xenotype).combatPowerFactor);
                                roster = CombatRoster.Resolve(entrant.Id, entrant.PawnKind, power, entrant.Budget ?? input.Budget, entrant.Count ?? input.Count);
                                roster.DisplayName = entrant.Name ?? kind.LabelCap.ToString(); roster.CostMode = input.CostMode;
                            }
                            roster.Source = input.Source;
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
                backgroundCleanup?.Poll();
                if (waiting || CombatFightService.Busy) return;
                if (cancel) { Finish("cancelled", "Cancelled by user"); return; }
                if (showcaseActive)
                {
                    if (!ArenaEntrantShowcase.InitializeFrame()) return;
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    if (showcasePageStarted < 0) return;
                    var elapsed = Time.realtimeSinceStartup - showcasePageStarted;
                    var page = tournament.Showcase.Pages[showcasePage];
                    var ready = showcaseCaptured && AutoTestViewComposite.IsReady(page.Screenshot);
                    if (elapsed > 20 && !ready) throw new IOException("Entrant showcase screenshot did not become ready: " + page.Screenshot);
                    if (ready && elapsed >= page.DurationSeconds)
                    {
                        page.VisibleSeconds = elapsed;
                        page.SimulationTicks = Find.TickManager.TicksGame - showcaseStartTick;
                        showcasePage++;
                        if (showcasePage == tournament.Input.ShowcasePages.Count)
                        {
                            tournament.Showcase.Completed = true; showcaseActive = false;
                            Find.TickManager.CurTimeSpeed = showcaseOriginalSpeed;
                            ArenaEntrantShowcase.ReleasePreviews(); Write();
                        }
                        else StartShowcasePage();
                    }
                    return;
                }
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
                    if (championStarted < 0) return;
                    if (Time.realtimeSinceStartup - championStarted > 20 && (!championCaptured || !AutoTestViewComposite.IsReady(tournament.ChampionScreenshot))) throw new IOException("Champion screenshot did not become ready");
                    if (championCaptured && AutoTestViewComposite.IsReady(tournament.ChampionScreenshot) && Time.realtimeSinceStartup - championStarted >= ArenaStandings.DisplaySeconds)
                    {
                        tournament.TopFive.VisibleSeconds = Time.realtimeSinceStartup - championStarted;
                        tournament.TopFive.SimulationTicks = Find.TickManager.TicksGame - championStartTick;
                        tournament.TopFive.Completed = true; tournament.Status = "completed";
                        ArenaEntrantShowcase.Clear(); Find.TickManager.CurTimeSpeed = championOriginalSpeed; Write();
                    }
                    return;
                }
                if (tournament == null || tournament.Status == "completed")
                {
                    tournament = report.Tournaments.FirstOrDefault(t => t.Status != "completed");
                    if (tournament == null) { Finish("completed", null); return; }
                    scheduler = new ArenaScheduler(tournament); match = null;
                    showcaseOriginalSpeed = Find.TickManager.CurTimeSpeed;
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    showcaseActive = true; showcasePage = 0;
                    ArenaEntrantShowcase.Begin(tournament);
                    StartShowcasePage();
                    return;
                }
                if (match == null) match = scheduler.Next();
                if (match == null)
                {
                    tournament.Champion = scheduler.Champion; tournament.Status = "champion";
                    championOriginalSpeed = Find.TickManager.CurTimeSpeed; Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    championStarted = -1; championCaptured = false;
                    var directory = Path.Combine(outputPath, "arena_" + report.RunId + "_Screenshots"); Directory.CreateDirectory(directory);
                    tournament.ChampionScreenshot = Path.Combine(directory, "tournament_" + (report.Tournaments.IndexOf(tournament) + 1).ToString("D3") + "_champion.png");
                    tournament.TopFive = new ArenaTopFiveResult { Entries = ArenaStandings.Build(tournament), Screenshot = tournament.ChampionScreenshot };
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
                var score = ScoreFor(result);
                if (!score.IsRankable)
                {
                    match.Score = score; report.Status = "error"; report.ErrorCode = "ARENA_SCORING_DATA_INCOMPLETE";
                    report.Error = "Damage measurement failed; provisional score cannot determine advancement or low-score extra matches: " + match.Id;
                    Write(); return;
                }
                scheduler.Resolve(id, score); match = null;
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
            if (showcaseActive) { Find.TickManager.CurTimeSpeed = showcaseOriginalSpeed; showcaseActive = false; }
            ArenaEntrantShowcase.Clear();
            backgroundCleanup = null;
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
            if (showcaseActive)
            {
                if (ArenaEntrantShowcase.Ready && showcasePageStarted < 0 && Event.current.type == EventType.Repaint)
                { showcasePageStarted = Time.realtimeSinceStartup; showcaseStartTick = Find.TickManager.TicksGame; }
                var elapsed = showcasePageStarted < 0 ? 0 : Time.realtimeSinceStartup - showcasePageStarted;
                ArenaEntrantShowcase.Draw(showcasePage, elapsed);
                if (ArenaEntrantShowcase.Ready && !showcaseCaptured && Event.current.type == EventType.Repaint && elapsed >= Math.Min(1, tournament.Input.ShowcasePages[showcasePage].Seconds * .25) && AutoTestScreenshotService.PrepareCaptureFrame())
                {
                    CapturePresentation(tournament.Showcase.Pages[showcasePage].Screenshot);
                    showcaseCaptured = true;
                }
                return;
            }
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
                if (championStarted < 0 && Event.current.type == EventType.Repaint)
                { championStarted = Time.realtimeSinceStartup; championStartTick = Find.TickManager.TicksGame; }
                ArenaEntrantShowcase.Draw(0, championStarted < 0 ? 0 : Time.realtimeSinceStartup - championStarted, standings: true);
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
        private static void StartShowcasePage()
        {
            showcasePageStarted = -1; showcaseCaptured = false;
            var page = tournament.Input.ShowcasePages[showcasePage];
            var directory = Path.Combine(outputPath, "arena_" + report.RunId + "_Screenshots"); Directory.CreateDirectory(directory);
            tournament.Showcase.Pages.Add(new ArenaShowcasePage {
                EntrantIds = page.EntrantIds.ToList(), DurationSeconds = page.Seconds, Caption = page.Caption,
                Screenshot = Path.Combine(directory, "tournament_" + (report.Tournaments.IndexOf(tournament) + 1).ToString("D3") + "_entrants_" + (showcasePage + 1).ToString("D2") + ".png")
            });
            Write();
        }
    }
}
