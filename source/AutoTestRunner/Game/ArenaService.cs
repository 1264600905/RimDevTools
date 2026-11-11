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
        private static IArenaSchedule scheduler;
        private static int summaryPage;
        private static bool ShowingSummary => tournament?.Status == "summary";
        private static string FinalScreenshot => ShowingSummary ? tournament.Summary.Pages[summaryPage].Screenshot : tournament.ChampionScreenshot;
        private static ArenaMatch match;
        private static IDisposable logGuard;
        private static ArenaBackgroundCleanup backgroundCleanup;
        private static bool manualSession;
        internal static bool ManualActive => manualSession && Busy;
        private static TimeSpeed manualOriginalSpeed;
        private static readonly ManualArtifactSession manualArtifacts = new();
        internal static void BeginManual(ArenaPlan plan, ManualProjectContext project)
        {
            if (Busy || GroupCombatService.Busy || CombatFightService.Busy || ManualTestService.Busy || GroupCombatDamageCapture.ProbeActive
                || AutoTestBootstrap.GetRuntimeStatus().State is "running" or "queued"
                || global::LogSyncMod.LogSyncMod.settings is { testSessionActive: true })
                throw new InvalidOperationException("已有测试会话正在运行，请先结束。");
            if (Find.CurrentMap == null || LongEventHandler.AnyEventNowOrWaiting) throw new InvalidOperationException("请在地图加载完成后开始赛事。");
            // Recompile the submitted input before allocating a session or touching game state.
            var frozen = ArenaLoader.FreezeSeeds(ArenaLoader.Compile(plan.Inputs, plan.SelectedTestDefNames, plan.ResolvedSeeds));
            outputPath = manualArtifacts.Ensure(project.Root).AutoTestOutputPath;
            Directory.CreateDirectory(outputPath);
            manualOriginalSpeed = Find.TickManager.CurTimeSpeed;
            manualSession = true;
            report = new ArenaRunReport { RunId = Guid.NewGuid().ToString("N"), StartedAtUtc = DateTime.UtcNow.ToString("O"),
                GameVersion = VersionControl.CurrentVersionStringWithRev, Plan = frozen,
                LoadedMods = LoadedModManager.RunningMods.Select(m => m.PackageIdPlayerFacing + "@" + m.ModMetaData?.ModVersion).ToList() };
            foreach (var input in frozen.Tournaments) report.Tournaments.Add(new ArenaTournamentResult { Input = input });
            cancel = false; waiting = false; match = null; tournament = null; postBattleMatch = null;
            showcaseActive = false; rulesActive = false; preBattle = null; backgroundCleanup = null;
            logGuard = global::LogSyncMod.TestLogWindowGuard.RegisterExternalSession();
            try { Write(); }
            catch { report = null; manualSession = false; logGuard.Dispose(); logGuard = null; throw; }
        }
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
        private static bool rulesActive, rulesCaptured, entrantsPreparing;
        private static long rulesStarted;
        private static float RulesElapsed => rulesStarted < 0 ? 0 : (float)((System.Diagnostics.Stopwatch.GetTimestamp() - rulesStarted) / (double)System.Diagnostics.Stopwatch.Frequency);
        private static int rulesPaintedFrame;
        private static TimeSpeed rulesOriginalSpeed;
        private static float showcasePageStarted;
        private static int showcasePage, showcaseStartTick;
        private static TimeSpeed showcaseOriginalSpeed;
        private static ArenaPreBattleResult preBattle;
        private static float preBattleStarted;
        private static int preBattleStartTick;
        private static bool preBattleCaptured;
        private static TimeSpeed preBattleOriginalSpeed;
        private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
        internal static bool Busy => report != null && report.CompletedAtUtc == null;
        internal static void Cancel() { cancel = true; CombatFightService.Cancel(); }
        internal static object Status => new {
            busy = Busy, status = report?.Status, runId = report?.RunId, tournament = tournament?.Input.TestDefName,
            match = match?.Id, attempt = match?.Attempts.Count + 1,
            format = tournament?.Input.Format, pairId = match?.PairId, level = match?.Level, challengeValue = match?.ChallengeValue,
            roles = tournament?.Input.Entrants.Select(e => new { e.Id, e.Role }),
            completedPairs = tournament?.Challenges.Count(p => p.Status != "pending" && p.Status != "running"),
            presentation = rulesActive ? "rules" : showcaseActive ? "showcase" : preBattle != null ? "preBattle" : postBattleMatch != null ? "postBattle" : ShowingSummary ? "summary" : tournament?.Status == "champion" ? "topFive" : null,
            presentationMatch = preBattle != null ? match?.Id : postBattleMatch?.Id,
            presentationSeconds = rulesActive ? RulesElapsed : showcaseActive ? showcasePageStarted < 0 ? 0 : Time.realtimeSinceStartup - showcasePageStarted : preBattle != null ? preBattleStarted < 0 ? 0 : Time.realtimeSinceStartup - preBattleStarted : postBattleMatch != null ? Time.realtimeSinceStartup - postBattleStarted : (ShowingSummary || tournament?.Status == "champion") && championStarted >= 0 ? Time.realtimeSinceStartup - championStarted : 0,
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
                    manualSession = false;
                    report = new ArenaRunReport { RunId = consumedRunId, StartedAtUtc = DateTime.UtcNow.ToString("O"), GameVersion = VersionControl.CurrentVersionStringWithRev, LoadedMods = LoadedModManager.RunningMods.Select(m => m.PackageIdPlayerFacing + "@" + m.ModMetaData?.ModVersion).ToList() };
                    logGuard = global::LogSyncMod.TestLogWindowGuard.RegisterExternalSession(); cancel = false; waiting = false; match = null; tournament = null; postBattleMatch = null; showcaseActive = false; rulesActive = false; preBattle = null;
                    var frozen = JsonSerializer.Deserialize<ArenaPlan>(Encoding.UTF8.GetString(Convert.FromBase64String(settings.arenaPlan)));
                    if (frozen?.Purpose != "arena" || frozen.SchemaVersion != 1) throw new ScenarioConfigurationException("Invalid frozen arena plan");
                    report.Plan = ArenaLoader.FreezeSeeds(ArenaLoader.Compile(frozen.Inputs, frozen.SelectedTestDefNames, frozen.ResolvedSeeds));
                    report.Plan.SkippedFiles = frozen.SkippedFiles;
                    backgroundCleanup = new ArenaBackgroundCleanup(Find.CurrentMap);
                    report.BackgroundCleanup = backgroundCleanup.Result;
                    foreach (var input in report.Plan.Tournaments)
                        report.Tournaments.Add(new ArenaTournamentResult { Input = input });
                    tournament = null; Write();
                }
                backgroundCleanup?.Poll();
                if (waiting || CombatFightService.Busy) return;
                if (cancel) { Finish("cancelled", "Cancelled by user"); return; }
                if (rulesActive)
                {
                    if (!entrantsPreparing && rulesStarted >= 0 && RulesElapsed > 20
                        && (!rulesCaptured || !AutoTestViewComposite.IsReady(tournament.Rules.Screenshot)))
                        throw new IOException("Rules screenshot did not become ready: " + tournament.Rules.Screenshot);
                    if (rulesStarted < 0 || Time.frameCount <= rulesPaintedFrame + 1 || !rulesCaptured
                        || !AutoTestViewComposite.IsReady(tournament.Rules.Screenshot)) return;
                    if (!entrantsPreparing) { entrantsPreparing = true; PrepareEntrants(); }
                    var prepared = ArenaEntrantShowcase.InitializeFrame();
                    var elapsed = RulesElapsed;
                    if (prepared && tournament.Rules.LoadingSeconds == 0) tournament.Rules.LoadingSeconds = elapsed;
                    if (!ArenaPresentation.RulesReady(elapsed, prepared)) return;
                    tournament.Rules.VisibleSeconds = elapsed; tournament.Rules.Completed = true;
                    scheduler = tournament.Input.Format == "knockout" ? new ArenaScheduler(tournament) : new ArenaChallengeScheduler(tournament);
                    rulesActive = false;
                    showcaseOriginalSpeed = rulesOriginalSpeed;
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    showcaseActive = true; showcasePage = 0; StartShowcasePage();
                    return;
                }
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
                if (preBattle != null)
                {
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    if (preBattleStarted < 0) return;
                    var elapsed = Time.realtimeSinceStartup - preBattleStarted;
                    var ready = preBattleCaptured && AutoTestViewComposite.IsReady(preBattle.Screenshot);
                    if (elapsed > 20 && !ready) throw new IOException("Pre-battle screenshot did not become ready: " + preBattle.Screenshot);
                    if (ready && elapsed >= ArenaPresentation.PreBattleSeconds)
                    {
                        preBattle.VisibleSeconds = elapsed;
                        preBattle.SimulationTicks = Find.TickManager.TicksGame - preBattleStartTick;
                        preBattle.Completed = true;
                        preBattle = null;
                        Find.TickManager.CurTimeSpeed = preBattleOriginalSpeed;
                        Write();
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
                if (ShowingSummary || tournament?.Status == "champion")
                {
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    if (championStarted < 0) return;
                    if (Time.realtimeSinceStartup - championStarted > 20 && (!championCaptured || !AutoTestViewComposite.IsReady(FinalScreenshot))) throw new IOException("Champion screenshot did not become ready");
                    if (championCaptured && AutoTestViewComposite.IsReady(FinalScreenshot) && Time.realtimeSinceStartup - championStarted >= ArenaStandings.DisplaySeconds)
                    {
                        if (ShowingSummary)
                        {
                            var page = tournament.Summary.Pages[summaryPage];
                            page.VisibleSeconds = Time.realtimeSinceStartup - championStarted;
                            page.SimulationTicks = Find.TickManager.TicksGame - championStartTick;
                            if (++summaryPage < tournament.Summary.Pages.Count)
                            { championStarted = -1; championCaptured = false; Write(); return; }
                            tournament.Summary.Completed = true;
                        }
                        else
                        {
                            tournament.TopFive.VisibleSeconds = Time.realtimeSinceStartup - championStarted;
                            tournament.TopFive.SimulationTicks = Find.TickManager.TicksGame - championStartTick;
                            tournament.TopFive.Completed = true;
                        }
                        tournament.Status = "completed";
                        ArenaEntrantShowcase.Clear(); Find.TickManager.CurTimeSpeed = championOriginalSpeed; Write();
                    }
                    return;
                }
                if (tournament == null || tournament.Status == "completed")
                {
                    tournament = report.Tournaments.FirstOrDefault(t => t.Status != "completed");
                    if (tournament == null) { Finish("completed", null); return; }
                    match = null;
                    rulesOriginalSpeed = Find.TickManager.CurTimeSpeed;
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    rulesActive = true; rulesCaptured = false; entrantsPreparing = false; rulesStarted = -1;
                    var directory = Path.Combine(outputPath, "arena_" + report.RunId + "_Screenshots"); Directory.CreateDirectory(directory);
                    tournament.Rules = new ArenaRulesPageResult { Screenshot = Path.Combine(directory,
                        "tournament_" + (report.Tournaments.IndexOf(tournament) + 1).ToString("D3") + "_rules.png") };
                    Write();
                    return;
                }
                if (match == null) match = scheduler.Next();
                if (match != null && tournament.Input.Format != "knockout")
                {
                    try { ArenaChallengeRoster.Prepare(tournament, match); }
                    catch (ScenarioConfigurationException ex) when (match.Level > 0 && ex.Code is "COMBAT_ROSTER_LIMIT_EXCEEDED" or "COMBAT_FORMATION_LIMIT_EXCEEDED")
                    {
                        var rejected = ex.Data["RejectedRoster"] as ResolvedCombatRoster ?? match.ResolvedRosters.LastOrDefault();
                        ((ArenaChallengeScheduler)scheduler).ReachRosterLimit(ex.Code, rejected);
                        match = null; Write(); return;
                    }
                }
                if (match == null)
                {
                    tournament.Champion = scheduler.Champion; tournament.Status = tournament.Input.Format == "knockout" ? "champion" : "summary";
                    championOriginalSpeed = Find.TickManager.CurTimeSpeed; Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    championStarted = -1; championCaptured = false;
                    var directory = Path.Combine(outputPath, "arena_" + report.RunId + "_Screenshots"); Directory.CreateDirectory(directory);
                    tournament.ChampionScreenshot = Path.Combine(directory, "tournament_" + (report.Tournaments.IndexOf(tournament) + 1).ToString("D3") + "_champion.png");
                    if (ShowingSummary)
                    {
                        tournament.ChampionScreenshot = null;
                        summaryPage = 0;
                        tournament.Summary = new ArenaChallengeSummary { Bosses = ArenaChallengeRules.Standings(tournament) };
                        var entries = ArenaChallengeRules.SummaryEntries(tournament);
                        for (var offset = 0; offset < entries.Count; offset += ArenaChallengeRules.SummaryPageSize)
                            tournament.Summary.Pages.Add(new ArenaSummaryPage {
                                EntryIds = entries.Skip(offset).Take(ArenaChallengeRules.SummaryPageSize).ToList(),
                                Screenshot = Path.Combine(directory, "tournament_" + (report.Tournaments.IndexOf(tournament) + 1).ToString("D3") + "_summary_" + (tournament.Summary.Pages.Count + 1).ToString("D3") + ".png")
                            });
                    }
                    else tournament.TopFive = new ArenaTopFiveResult { Entries = ArenaStandings.Build(tournament), Screenshot = tournament.ChampionScreenshot };
                    Write(); return;
                }
                if (match.PreBattles.Count == match.Attempts.Count)
                {
                    preBattleOriginalSpeed = Find.TickManager.CurTimeSpeed;
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    preBattleStarted = -1; preBattleCaptured = false;
                    ArenaEntrantShowcase.ResetMatchupSelection();
                    var directory = Path.Combine(outputPath, "arena_" + report.RunId + "_Screenshots"); Directory.CreateDirectory(directory);
                    preBattle = new ArenaPreBattleResult {
                        EntrantIds = new() { match.Left, match.Right },
                        Screenshot = Path.Combine(directory, "tournament_" + (report.Tournaments.IndexOf(tournament) + 1).ToString("D3") + "_match_" + (tournament.Matches.IndexOf(match) + 1).ToString("D4") + "_attempt_" + (match.Attempts.Count + 1).ToString("D2") + "_prebattle.png")
                    };
                    match.PreBattles.Add(preBattle);
                    Write(); return;
                }
                var rosters = tournament.Input.Format == "knockout" ? tournament.Rosters : match.ResolvedRosters;
                var fight = ArenaLoader.CompileBattle(tournament.Input, rosters, match, match.Attempts.Count);
                fight.ResolvedRosters = new() { rosters.Single(r => r.Id == match.Left), rosters.Single(r => r.Id == match.Right) };
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
        private static void PrepareEntrants()
        {
            var input = tournament.Input;
            foreach (var entrant in input.Entrants)
            {
                ResolvedCombatRoster roster;
                var budget = ArenaChallengeRules.Budget(input, entrant);
                var costMode = ArenaChallengeRules.CostMode(input, entrant);
                if (entrant.Squad != null)
                {
                    roster = SquadGeneration.Resolve(entrant.Id, input.SquadTemplates[entrant.Squad], budget,
                        entrant.Count ?? input.Count, costMode, SquadTemplates.MemberSeed(input.Seed, entrant.Id, 0), Find.CurrentMap.Tile);
                    roster.DisplayName = entrant.Name ?? entrant.Squad;
                }
                else
                {
                    var kind = DefDatabase<PawnKindDef>.GetNamedSilentFail(entrant.PawnKind);
                    if (kind == null) throw new ScenarioConfigurationException("Unknown loaded PawnKind: " + entrant.PawnKind, "COMBAT_POWER_INVALID");
                    if (entrant.Xenotype != null && (!ModsConfig.BiotechActive || !kind.RaceProps.Humanlike
                        || DefDatabase<XenotypeDef>.GetNamedSilentFail(entrant.Xenotype) == null))
                        throw new ScenarioConfigurationException("Invalid arena xenotype for " + entrant.Id + ": " + entrant.Xenotype);
                    var config = new System.Xml.Linq.XElement("PawnTemplate", new System.Xml.Linq.XElement("basePawnKind", entrant.PawnKind));
                    if (entrant.Xenotype != null) config.Add(new System.Xml.Linq.XElement("Xenotype", new System.Xml.Linq.XAttribute("def", entrant.Xenotype)));
                    roster = SquadGeneration.ResolveActor(entrant.Id, config, null, budget, entrant.Count ?? input.Count, costMode, input.Seed);
                    roster.DisplayName = entrant.Name ?? kind.LabelCap.ToString(); roster.CostMode = costMode;
                }
                roster.Source = input.Source;
                tournament.Rosters.Add(roster);
            }
            // Validate the largest formations against every entrant before battle.
            foreach (var roster in tournament.Rosters)
            {
                var other = tournament.Rosters.First(r => r.Id != roster.Id);
                ArenaLoader.CompileBattle(input, tournament.Rosters, new ArenaMatch { Id = "validation", Left = roster.Id, Right = other.Id }, 0);
            }
            ArenaEntrantShowcase.Begin(tournament);
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
                if (match.Attempts.Count > tournament.Input.MaxReplays)
                {
                    if (scheduler is ArenaChallengeScheduler challenges) { challenges.ResolveDraw(); match = null; }
                    else { report.Status = "unresolvedDraw"; report.Error = "Draw replay limit exhausted: " + match.Id; }
                }
            }
            else
            {
                var id = winner.Id == "A" ? match.Left : match.Right;
                var score = ScoreFor(result);
                if (score.UsedDamageFallback)
                    Log.Warning("[LogSyncArena] Damage measurement incomplete; using neutral damage exchange and advancing the battle winner: " + match.Id);
                scheduler.Resolve(id, score); match = null;
            }
            postBattleMatch = completedMatch; postBattleStarted = Time.realtimeSinceStartup; postBattleCaptured = false;
            postBattleOriginalSpeed = Find.TickManager.CurTimeSpeed; Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
            var directory = Path.Combine(outputPath, "arena_" + report.RunId + "_Screenshots"); Directory.CreateDirectory(directory);
            postBattleScreenshot = Path.Combine(directory, "tournament_" + (report.Tournaments.IndexOf(tournament) + 1).ToString("D3") + "_match_" + tournament.Matches.Count.ToString("D4") + "_attempt_" + completedMatch.Attempts.Count.ToString("D2") + "_postbattle.png");
            completedMatch.PostBattleScreenshots.Add(postBattleScreenshot);
            if (tournament.Input.Format == "knockout") ArenaPostBattleView.Begin(tournament, completedMatch);
            else ArenaChallengeView.Reset();
            Write();
        }
        private static void Finish(string status, string error)
        {
            if (report == null || report.CompletedAtUtc != null) return;
            if (rulesActive) { Find.TickManager.CurTimeSpeed = rulesOriginalSpeed; rulesActive = false; }
            if (showcaseActive) { Find.TickManager.CurTimeSpeed = showcaseOriginalSpeed; showcaseActive = false; }
            if (preBattle != null) { Find.TickManager.CurTimeSpeed = preBattleOriginalSpeed; preBattle = null; }
            ArenaEntrantShowcase.Clear();
            backgroundCleanup = null;
            if (postBattleMatch != null) { Find.TickManager.CurTimeSpeed = postBattleOriginalSpeed; postBattleMatch = null; }
            if (ShowingSummary || tournament?.Status == "champion") Find.TickManager.CurTimeSpeed = championOriginalSpeed;
            report.Status = status; report.Error = error; report.CompletedAtUtc = DateTime.UtcNow.ToString("O");
            foreach (var item in report.Tournaments.Where(t => t.Status != "completed"))
            { item.Status = item == tournament ? status : "notRun"; item.Error = item == tournament ? error : "Stopped before this tournament"; }
            var persisted = false;
            try { Write(); persisted = true; }
            finally
            {
                logGuard?.Dispose(); logGuard = null;
                global::LogSyncMod.TestAudioController.Restore("arena_completed");
                Log.Message("[LogSyncArena] COMPLETED runId=" + report.RunId + " status=" + report.Status);
                // Only a fully completed schedule exits. Errors, cancellation and draws stay open.
                if (manualSession)
                {
                    Find.TickManager.CurTimeSpeed = manualOriginalSpeed;
                    manualSession = false;
                    Messages.Message("群战赛事已结束：" + report.Status + "。结果：" + outputPath, MessageTypeDefOf.TaskCompletion, false);
                }
                else if (persisted && report.Status == "completed") global::LogSyncMod.TestSessionCompletion.Complete("arena", report.RunId, report.Status);
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
            if (rulesActive)
            {
                ArenaRulesView.Draw(tournament.Input, RulesElapsed);
                if (rulesStarted < 0 && Event.current.type == EventType.Repaint)
                { rulesStarted = System.Diagnostics.Stopwatch.GetTimestamp(); rulesPaintedFrame = Time.frameCount; }
                if (!rulesCaptured && Event.current.type == EventType.Repaint && AutoTestScreenshotService.PrepareCaptureFrame())
                { CapturePresentation(tournament.Rules.Screenshot); rulesCaptured = true; }
                return;
            }
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
            if (preBattle != null)
            {
                if (preBattleStarted < 0 && Event.current.type == EventType.Repaint)
                { preBattleStarted = Time.realtimeSinceStartup; preBattleStartTick = Find.TickManager.TicksGame; }
                var elapsed = preBattleStarted < 0 ? 0 : Time.realtimeSinceStartup - preBattleStarted;
                ArenaEntrantShowcase.Draw(0, elapsed, matchup: match);
                if (!preBattleCaptured && Event.current.type == EventType.Repaint && elapsed >= 1 && AutoTestScreenshotService.PrepareCaptureFrame())
                {
                    CapturePresentation(preBattle.Screenshot);
                    preBattleCaptured = true;
                }
                return;
            }
            if (postBattleMatch != null)
            {
                var elapsed = Time.realtimeSinceStartup - postBattleStarted;
                if (tournament.Input.Format == "knockout") ArenaPostBattleView.Draw(elapsed);
                else ArenaChallengeView.Draw(tournament, postBattleMatch, elapsed);
                if (!postBattleCaptured && Event.current.type == EventType.Repaint && elapsed >= ArenaPresentation.AnimationSeconds && AutoTestScreenshotService.PrepareCaptureFrame())
                {
                    CapturePresentation(postBattleScreenshot);
                    postBattleCaptured = true;
                }
                return;
            }
            if (ShowingSummary || tournament?.Status == "champion")
            {
                if (championStarted < 0 && Event.current.type == EventType.Repaint)
                { championStarted = Time.realtimeSinceStartup; championStartTick = Find.TickManager.TicksGame; }
                if (ShowingSummary) ArenaChallengeView.DrawSummary(tournament, summaryPage);
                else ArenaEntrantShowcase.Draw(0, championStarted < 0 ? 0 : Time.realtimeSinceStartup - championStarted, standings: true);
                if (!championCaptured && Event.current.type == EventType.Repaint && Time.realtimeSinceStartup - championStarted >= 1 && AutoTestScreenshotService.PrepareCaptureFrame())
                {
                    CapturePresentation(FinalScreenshot);
                    championCaptured = true;
                }
                return;
            }
            if (!CombatFightService.IsArena) return;
            if (CombatFightService.Measuring && match != null)
            {
                var label = tournament.Input.Format != "knockout" ? ArenaPresentation.MatchTitle(match) + " · " + tournament.Challenges.Count(p => p.Status != "pending" && p.Status != "running") + "/" + tournament.Challenges.Count + " 组完成" : "擂台 · " + (tournament.Matches.Count) + "/" + (tournament.Rosters.Count - 1) + " 场 · " + (match.Kind == "extra" ? "低分加赛" : match.Kind == "qualification" ? "资格赛" : "第 " + match.Round + " 轮");
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
