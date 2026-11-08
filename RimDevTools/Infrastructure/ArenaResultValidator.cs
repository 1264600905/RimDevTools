using System.Text.Json;
using LogSyncMod.AutoTest.Scenarios;

namespace RimDevTools;

public static class ArenaResultValidator
{
    public static AutoResultValidation Validate(string outputPath, string runId, ArenaPlan? expected = null)
    {
        var path = Path.Combine(outputPath, "arena_" + runId + "_Result.json");
        try
        {
            if (!File.Exists(path)) return new(false, "ARENA_RESULT_MISSING", "缺少本轮擂台结果", new { path });
            var report = JsonSerializer.Deserialize<ArenaRunReport>(File.ReadAllText(path));
            if (report == null || report.SchemaVersion != 1 || report.Purpose != "arena" || report.RunId != runId) return new(false, "ARENA_RESULT_INVALID", "擂台结果身份不符", new { path });
            if (report.Status != "completed") return new(false, report.Status == "unresolvedDraw" ? "ARENA_UNRESOLVED_DRAW" : report.ErrorCode ?? "ARENA_EXECUTION_INCOMPLETE", report.Error ?? report.Status, new { path, report.Status, tournaments = report.Tournaments.Select(t => new { t.Input.TestDefName, t.Status, t.Champion, matches = t.Matches.Count }) });
            var error = ValidateIntegrity(report, expected);
            if (error != null) return new(false, "ARENA_RESULT_INVALID", error, new { path });
            return new(true, "", "擂台测试完成，配兵、晋级和评分校验通过", new { path, reportPath = Path.Combine(outputPath, "arena_" + runId + "_Report.md"), tournaments = report.Tournaments.Select(t => new { t.Input.TestDefName, t.Champion, entrants = t.Rosters.Select(r => new { r.Id, r.PawnKind, r.UnitCombatPower, r.ResolvedCount, r.RequestedBudget, r.ActualCombatPower, r.UnusedBudget }), matches = t.Matches.Count, attempts = t.Matches.Sum(m => m.Attempts.Count), cleanupComplete = t.Matches.All(m => m.Attempts.All(a => a.CleanupComplete)) }) });
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException or ArgumentException or NullReferenceException or OverflowException or ScenarioConfigurationException or System.Xml.XmlException)
        { return new(false, "ARENA_RESULT_INVALID", ex.Message, new { path }); }
    }
    public static string? ValidateIntegrity(ArenaRunReport report, ArenaPlan? expected = null, bool verifyArtifactFiles = true)
    {
        // Rebuild from the CLI-frozen XML rather than trusting result-side configuration.
        var plan = expected == null ? ArenaLoader.Compile(report.Plan.Inputs, report.Plan.SelectedTestDefNames) : ArenaLoader.Compile(expected.Inputs, expected.SelectedTestDefNames);
        if (report.Status != "completed" || report.CompletedAtUtc == null || report.Tournaments.Count != plan.Tournaments.Count || report.LoadedMods.Count == 0) return "赛事计划、完成时间或已加载模组快照缺失";
        for (int ti = 0; ti < plan.Tournaments.Count; ti++)
        {
            var input = plan.Tournaments[ti]; var t = report.Tournaments[ti];
            if (JsonSerializer.Serialize(t.Input) != JsonSerializer.Serialize(input) || t.Status != "completed" || t.Error != null || t.Rosters.Count != input.Entrants.Count || t.Matches.Count != input.Entrants.Count - 1) return "赛事输入、参赛名单或 N−1 淘汰场数不一致";
            foreach (var e in input.Entrants)
            {
                var r = t.Rosters.SingleOrDefault(r => r.Id == e.Id);
                var budget = e.Budget ?? input.Budget;
                if (r == null || r.PawnKind != e.PawnKind || r.UnitCombatPower <= 0 || r.Mode != input.RosterMode || r.RequestedBudget != budget || r.ResolvedCount < 1 || r.ResolvedCount > 100) return "配兵身份或 combatPower 快照无效";
                var count = budget.HasValue ? decimal.Floor(budget.Value / r.UnitCombatPower) : e.Count ?? input.Count;
                if (r.ResolvedCount != count || budget.HasValue && (r.ActualCombatPower > budget || budget.Value - r.ActualCombatPower >= r.UnitCombatPower)) return "预算配兵不满足最大整数人数";
            }
            var alive = t.Rosters.Select(r => r.Id).ToList();
            var scores = new Dictionary<string, decimal>(StringComparer.Ordinal);
            var previous = new Dictionary<string, string>(StringComparer.Ordinal);
            var queue = new Queue<string[]>(); var round = 0; var initialOdd = alive.Count % 2 == 1;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            string kind = "";
            foreach (var m in t.Matches)
            {
                if (alive.Count < 2 || m.Id != input.TestDefName + "/match-" + (seen.Count + 1)) return "淘汰后继续对局或对局编号无效";
                if (queue.Count == 0)
                {
                    if (initialOdd) { queue.Enqueue(Closest(alive, t)); kind = "qualification"; initialOdd = false; }
                    else if (alive.Count % 2 == 1)
                    {
                        kind = "extra";
                        var lowest = alive.OrderBy(id => scores[id]).ThenBy(id => ArenaScheduler.Stable(input.Seed, id), StringComparer.Ordinal).ThenBy(id => id, StringComparer.Ordinal).Take(2).ToArray();
                        queue.Enqueue(ArenaScheduler.Pair(lowest[0], lowest[1]));
                    }
                    else
                    {
                        round++; kind = "round"; var remaining = alive.ToList();
                        while (remaining.Count > 0) { var pair = Closest(remaining, t); queue.Enqueue(pair); remaining.Remove(pair[0]); remaining.Remove(pair[1]); }
                    }
                }
                var pairExpected = queue.Dequeue();
                var key = JsonSerializer.Serialize(ArenaScheduler.Pair(m.Left, m.Right));
                var leftFirst = (Convert.ToUInt32(ArenaScheduler.Stable(input.Seed, pairExpected).Substring(0, 8), 16) & 1) == 0;
                var predecessors = pairExpected.Where(previous.ContainsKey).Select(id => previous[id]).Distinct().ToArray();
                if (!seen.Add(key) || !pairExpected.SequenceEqual(m.PairKey) || m.Left != pairExpected[leftFirst ? 0 : 1] || m.Right != pairExpected[leftFirst ? 1 : 0] || m.Kind != kind || m.Round != round || !m.Predecessors.SequenceEqual(predecessors) || m.Winner != m.Left && m.Winner != m.Right || m.Attempts.Count < 1 || m.Attempts.Count > input.MaxReplays + 1) return "配对、换边去重、轮次、前驱或胜者无效";
                for (int ai = 0; ai < m.Attempts.Count; ai++)
                {
                    var a = m.Attempts[ai]; var battle = ArenaLoader.CompileBattle(input, t.Rosters, m, ai);
                    if (a.CaseId != battle.CaseId || a.TestDefName != input.TestDefName || a.InputHash != input.InputHash || a.Seed != battle.Seed || a.ExecutionStatus != "completed" || !a.CleanupComplete || a.IsPocketMap || a.Teams.Count != 2 || a.Teams.Select(x => x.Id).Order().SequenceEqual(new[] { "A", "B" }) == false) return "战斗身份、种子或清理不符";
                    foreach (var side in new[] { "A", "B" })
                    {
                        var roster = t.Rosters.Single(r => r.Id == (side == "A" ? m.Left : m.Right));
                        if (a.Teams.Single(x => x.Id == side).Initial != roster.ResolvedCount || a.Units.Any(u => u.Team == side && !u.JoinedDuringBattle && u.Kind != roster.PawnKind)) return "实际初始单位与冻结配兵不符";
                    }
                    var ledgerError = GroupCombatResultValidator.ValidateIntegrity(new GroupCombatRunReport { Rounds = new() { a } });
                    if (ledgerError != null) return ledgerError;
                    var endError = ValidateEnd(a, input);
                    if (endError != null) return endError;
                    var wins = a.Teams.Where(x => x.Outcome == "win").ToList();
                    if (ai < m.Attempts.Count - 1 && wins.Count != 0 || ai == m.Attempts.Count - 1 && (wins.Count != 1 || (wins[0].Id == "A" ? m.Left : m.Right) != m.Winner)) return "平局重赛或晋级胜者不符";
                    if (a.Screenshots.Count != 3 || !a.Screenshots.Any(p => p.EndsWith("_bracket.png", StringComparison.Ordinal)) || !a.Screenshots.Any(p => p.EndsWith("_initial.png", StringComparison.Ordinal)) || !a.Screenshots.Any(p => p.EndsWith("_final.png", StringComparison.Ordinal)) || verifyArtifactFiles && a.Screenshots.Any(p => !File.Exists(p))) return "缺少赛前预览、初始战场或战斗结算截图";
                }
                if (!m.BracketScreenshots.SequenceEqual(m.Attempts.SelectMany(a => a.Screenshots.Where(p => p.EndsWith("_bracket.png", StringComparison.Ordinal))))) return "对阵图截图与每次尝试不一致";
                // Older schema-1 reports have no post-battle captures. When present,
                // require a distinct, completed presentation for every attempt.
                if (m.PostBattleScreenshots.Count > 0 && (m.PostBattleScreenshots.Count != m.Attempts.Count || m.PostBattleScreenshots.Distinct(StringComparer.Ordinal).Count() != m.Attempts.Count || m.PostBattleScreenshots.Any(p => !p.EndsWith("_postbattle.png", StringComparison.Ordinal) || verifyArtifactFiles && !File.Exists(p)))) return "赛后天梯截图与每次尝试不一致";
                var last = m.Attempts.Last(); var winner = last.Teams.Single(x => x.Outcome == "win");
                var standing = last.Units.Count(u => u.Team == winner.Id && !u.JoinedDuringBattle && u.State == "standing");
                // Independent decimal arithmetic, using simulated ticks from the final snapshot.
                var ratio = (decimal)standing / winner.Initial; var speed = Math.Max(0, 1 - (decimal)last.ElapsedTicks / 60 / input.TimeReferenceSeconds);
                var value = input.SurvivalWeight * ratio + input.SpeedWeight * speed;
                if (m.Score == null || m.Score.Initial != winner.Initial || m.Score.Standing != standing || m.Score.ElapsedTicks != last.ElapsedTicks || m.Score.SurvivalRatio != ratio || m.Score.SpeedFactor != speed || m.Score.Value != value) return "胜利评分无法按原始存活人数和模拟 tick 复算";
                scores[m.Winner] = value; previous[m.Winner] = m.Id; alive.Remove(m.Winner == m.Left ? m.Right : m.Left);
            }
            if (string.IsNullOrEmpty(t.ChampionScreenshot) || verifyArtifactFiles && !File.Exists(t.ChampionScreenshot)) return "缺少冠军截图";
            if (queue.Count != 0 || alive.Count != 1 || t.Champion != alive[0]) return "冠军不是唯一最终存活参赛者";
        }
        return null;
    }
    private static string[] Closest(List<string> ids, ArenaTournamentResult t)
    {
        var pairs = new List<string[]>();
        for (int i = 0; i < ids.Count; i++) for (int j = i + 1; j < ids.Count; j++) pairs.Add(ArenaScheduler.Pair(ids[i], ids[j]));
        return pairs.OrderBy(p => Math.Abs(t.Rosters.Single(r => r.Id == p[0]).ResolvedCount - t.Rosters.Single(r => r.Id == p[1]).ResolvedCount)).ThenBy(p => ArenaScheduler.Stable(t.Input.Seed, p), StringComparer.Ordinal).ThenBy(p => p[0], StringComparer.Ordinal).ThenBy(p => p[1], StringComparer.Ordinal).First();
    }
    private static string? ValidateEnd(GroupCombatRoundResult a, ArenaTournamentInput input)
    {
        var defeated = a.Teams.Where(t => t.Standing == 0 && t.PendingRevival == 0 && t.EliminatedAtTick.HasValue && t.EliminatedAtTick >= 0 && a.ElapsedTicks - t.EliminatedAtTick >= input.ConfirmationTicks).ToList();
        string[] winners;
        if (a.EndReason == "allUnavailable")
        {
            if (defeated.Count == 0) return "全灭确认未达到要求";
            winners = a.Teams.Except(defeated).Select(t => t.Id).ToArray();
        }
        else if (a.EndReason == "timeLimitRatio")
        {
            if (a.ElapsedTicks < (int)Math.Ceiling(input.DurationSeconds * 60)) return "未到模拟时间上限";
            var l = a.Teams.Single(t => t.Id == "A"); var r = a.Teams.Single(t => t.Id == "B");
            var nL = a.Units.Count(u => u.Team == "A" && !u.JoinedDuringBattle && u.State == "standing");
            var nR = a.Units.Count(u => u.Team == "B" && !u.JoinedDuringBattle && u.State == "standing");
            var cmp = ((long)nL * r.Initial).CompareTo((long)nR * l.Initial);
            winners = cmp > 0 ? new[] { "A" } : cmp < 0 ? new[] { "B" } : new[] { "A", "B" };
        }
        else return "未知擂台结算规则";
        return a.Teams.Any(t => t.Outcome != (winners.Length == 1 ? winners.Contains(t.Id) ? "win" : "loss" : "draw")) ? "结算胜负与存活比例/全灭不一致" : null;
    }
}
