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
        private static Vector2 scroll;
        private static float championStarted;
        private static bool championCaptured;
        private static bool focusBracket, overview;
        private static TimeSpeed championOriginalSpeed;
        private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
        internal static bool Busy => report != null && report.CompletedAtUtc == null;
        internal static void Cancel() { cancel = true; CombatFightService.Cancel(); }
        internal static object Status => new { busy = Busy, status = report?.Status, runId = report?.RunId, tournament = tournament?.Input.TestDefName, match = match?.Id, attempt = match?.Attempts.Count + 1, artifacts = new { autoTestOutputPath = outputPath } };
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
                    logGuard = global::LogSyncMod.TestLogWindowGuard.RegisterExternalSession(); cancel = false; waiting = false; match = null; tournament = null;
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
                            var roster = CombatRoster.Resolve(entrant.Id, entrant.PawnKind, CombatRoster.Power(kind.combatPower), input.Budget, entrant.Count ?? input.Count);
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
                    championStarted = Time.realtimeSinceStartup; championCaptured = false; focusBracket = true;
                    var directory = Path.Combine(outputPath, "arena_" + report.RunId + "_Screenshots"); Directory.CreateDirectory(directory);
                    tournament.ChampionScreenshot = Path.Combine(directory, "tournament_" + (report.Tournaments.IndexOf(tournament) + 1).ToString("D3") + "_champion.png");
                    Write(); return;
                }
                var fight = ArenaLoader.CompileBattle(tournament.Input, tournament.Rosters, match, match.Attempts.Count);
                fight.ResolvedRosters = new() { tournament.Rosters.Single(r => r.Id == match.Left), tournament.Rosters.Single(r => r.Id == match.Right) };
                waiting = true; scroll = Vector2.zero; focusBracket = true; overview = false; Write();
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
            if (winner == null)
            {
                if (match.Attempts.Count > tournament.Input.MaxReplays) { report.Status = "unresolvedDraw"; report.Error = "Draw replay limit exhausted: " + match.Id; }
                Write(); return;
            }
            var id = winner.Id == "A" ? match.Left : match.Right;
            var standing = result.Units.Count(u => !u.JoinedDuringBattle && u.Team == winner.Id && u.State == "standing");
            var score = ArenaScore.Calculate(tournament.Input, winner.Initial, standing, result.ElapsedTicks);
            scheduler.Resolve(id, score); match = null; Write();
        }
        private static void Finish(string status, string error)
        {
            if (report == null || report.CompletedAtUtc != null) return;
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
            if (tournament?.Status == "champion")
            {
                DrawBracket();
                if (!championCaptured && Event.current.type == EventType.Repaint && Time.realtimeSinceStartup - championStarted >= 1 && AutoTestScreenshotService.PrepareCaptureFrame())
                {
                    var output = XElement.Parse(tournament.Input.Xml).Element("Output");
                    var width = ScenarioLoader.I(output, "width", Screen.width); var height = ScenarioLoader.I(output, "height", Screen.height);
                    if (width == Screen.width && height == Screen.height) ScreenCapture.CaptureScreenshot(tournament.ChampionScreenshot);
                    else Find.CameraDriver.StartCoroutine(GroupCombatScreenshot.Capture(tournament.ChampionScreenshot, width, height));
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
            if (CombatFightService.ShowingSettlement && match != null)
            {
                var fight = CombatFightService.Display; var winner = fight.Teams.SingleOrDefault(t => t.Outcome == "win");
                var label = winner == null ? "平局 · 重赛或未决停止" : "胜利评分 " + ArenaScore.Calculate(tournament.Input, winner.Initial, fight.Units.Count(u => u.Team == winner.Id && !u.JoinedDuringBattle && u.State == "standing"), fight.ElapsedTicks).Value.ToString("F2");
                Widgets.DrawWindowBackground(new Rect(UI.screenWidth / 2 - 200, UI.screenHeight - 96, 400, 44));
                Widgets.Label(new Rect(UI.screenWidth / 2 - 190, UI.screenHeight - 88, 380, 32), label);
            }
        }
        internal static void DrawBracket()
        {
            if (tournament == null || match == null && tournament.Champion == null) return;
            var oldFont = Text.Font; var oldAnchor = Text.Anchor; var oldColor = GUI.color;
            try
            {
                var w = UI.screenWidth; var h = UI.screenHeight;
                Widgets.DrawWindowBackground(new Rect(8, 8, w - 16, h - 16));
                Text.Font = GameFont.Medium; Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(new Rect(20, 18, w - 180, 40), tournament.Input.Name + " · 擂台对决天梯");
                Text.Font = GameFont.Small;
                if (Widgets.ButtonText(new Rect(w - 150, 22, 122, 32), "取消擂台")) Cancel();
                var left = tournament.Rosters.Single(r => r.Id == (match?.Left ?? tournament.Champion)); var right = tournament.Rosters.Single(r => r.Id == (match?.Right ?? tournament.Champion));
                var type = match?.Kind == "qualification" ? "资格赛" : match?.Kind == "extra" ? "低分加赛" : "第 " + match?.Round + " 轮";
                Widgets.Label(new Rect(20, 62, w - 40, 34), match == null ? "冠军：" + left.DisplayName + " · " + (tournament.Rosters.Count - 1) + " 场淘汰对决完成" : type + " · " + left.DisplayName + " ×" + left.ResolvedCount + "  vs  " + right.DisplayName + " ×" + right.ResolvedCount + " · " + (match.Attempts.Count == 0 ? "首战" : "重赛 " + match.Attempts.Count + "/" + tournament.Input.MaxReplays));
                Widgets.Label(new Rect(20, 96, w - 40, 30), "combatPower 配兵 · " + tournament.Input.RosterMode + (tournament.Input.Budget.HasValue ? " · 每方预算 " + tournament.Input.Budget : "") + " · 绿色为已晋级，金色为本场，后续配对待定");
                var layout = ArenaBracketLayout.Build(tournament);
                var viewport = new Rect(22, 138, w - 44, h - 194);
                var scale = Math.Min(1.5f, Math.Min(viewport.width / layout.Width, viewport.height / layout.Height));
                if (tournament.Rosters.Count > 8)
                {
                    if (Widgets.ButtonText(new Rect(w - 274, 99, 122, 28), "定位本场")) { focusBracket = true; overview = false; }
                    if (Widgets.ButtonText(new Rect(w - 144, 99, 122, 28), overview ? "详细天梯" : "全局总览")) overview = !overview;
                    if (focusBracket)
                    {
                        var node = layout.Nodes.FirstOrDefault(n => n.Active) ?? layout.Nodes.Last();
                        scroll = new Vector2(Math.Max(0, Math.Min(node.X - viewport.width / 2 + 101, layout.Width - viewport.width)), Math.Max(0, Math.Min(node.Y - viewport.height / 2 + 24, layout.Height - viewport.height)));
                        focusBracket = false;
                    }
                }
                if (tournament.Rosters.Count <= 8 || overview)
                {
                    DrawTree(layout, new Vector2(viewport.x + (viewport.width - layout.Width * scale) / 2, viewport.y + (viewport.height - layout.Height * scale) / 2), scale);
                }
                else
                {
                    Widgets.BeginScrollView(viewport, ref scroll, new Rect(0, 0, Math.Max(viewport.width - 20, layout.Width), layout.Height));
                    DrawTree(layout, Vector2.zero, 1); Widgets.EndScrollView();
                }
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(new Rect(22, h - 50, w - 44, 30), "比赛前暂停展示 · 全部队伍每场重新满编 · 倒地不计存活 · N 位参赛者决出冠军需要 N−1 场胜负");
            }
            finally { Text.Font = oldFont; Text.Anchor = oldAnchor; GUI.color = oldColor; }
        }
        private static void DrawTree(ArenaBracketLayout layout, Vector2 origin, float scale)
        {
            foreach (var edge in layout.Edges)
            {
                var a = layout.Nodes.Single(n => n.Id == edge.From); var b = layout.Nodes.Single(n => n.Id == edge.To);
                var p = origin + new Vector2((a.X + 202) * scale, (a.Y + 24) * scale); var q = origin + new Vector2(b.X * scale, (b.Y + 24) * scale);
                Widgets.DrawLine(p, new Vector2(q.x - 14 * scale, p.y), Color.gray, 2);
                Widgets.DrawLine(new Vector2(q.x - 14 * scale, p.y), new Vector2(q.x - 14 * scale, q.y), Color.gray, 2);
                Widgets.DrawLine(new Vector2(q.x - 14 * scale, q.y), q, Color.gray, 2);
            }
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = scale >= 1.25f ? GameFont.Medium : GameFont.Small;
            foreach (var n in layout.Nodes)
            {
                var rect = new Rect(origin.x + n.X * scale, origin.y + n.Y * scale, 202 * scale, 48 * scale);
                Widgets.DrawBoxSolid(rect, n.Active ? new Color(.45f, .34f, .09f) : n.Completed ? new Color(.12f, .29f, .19f) : new Color(.18f, .19f, .22f));
                Widgets.DrawBox(rect); Widgets.Label(rect.ContractedBy(3), n.Label);
            }
        }
    }
}
