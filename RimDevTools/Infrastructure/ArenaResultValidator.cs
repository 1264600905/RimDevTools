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
            if (report.Tournaments.SelectMany(t => t.Matches).SelectMany(m => m.Attempts).Any(a => a.RequireCompleteAttribution && (!a.MeasurementRequirementsMet || a.MeasurementQuality != "complete")))
                return new(false, "ARENA_MEASUREMENT_REQUIREMENTS_NOT_MET", "赛事已执行并清理，但未达到显式的完整归属测量要求", new { path });
            return new(true, "", "赛事完成，配兵、赛程和评分校验通过", new { path, reportPath = Path.Combine(outputPath, "arena_" + runId + "_Report.md"), tournaments = report.Tournaments.Select(t => new { t.Input.TestDefName, t.Input.Format, t.Champion, t.Challenges, bossStandings = t.Summary?.Bosses, entrants = t.Rosters.Select(r => new { r.Id, r.PawnKind, r.UnitCombatPower, r.ResolvedCount, r.RequestedBudget, r.ActualCombatPower, r.UnusedBudget }), matches = t.Matches.Count, damageFallbackMatches = t.Matches.Count(m => m.Score?.UsedDamageFallback == true), attempts = t.Matches.Sum(m => m.Attempts.Count), cleanupComplete = t.Matches.All(m => m.Attempts.All(a => a.CleanupComplete)) }) });
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
            if (t.Rules == null) return "缺少规则展示记录";
            if (t.Rules is { } rules && (!rules.Completed || rules.MinimumSeconds != 5
                || !double.IsFinite(rules.LoadingSeconds) || rules.LoadingSeconds < 0
                || !double.IsFinite(rules.VisibleSeconds) || rules.VisibleSeconds < Math.Max(5, rules.LoadingSeconds)
                || string.IsNullOrEmpty(rules.Screenshot) || !rules.Screenshot.EndsWith("_rules.png", StringComparison.Ordinal)
                || verifyArtifactFiles && !File.Exists(rules.Screenshot))) return "规则页未完整展示、少于现实五秒或加载计时无效";
            if (JsonSerializer.Serialize(t.Input) != JsonSerializer.Serialize(input) || t.Status != "completed" || t.Error != null || t.Rosters.Count != input.Entrants.Count || input.Format == "knockout" && t.Matches.Count != input.Entrants.Count - 1) return "赛事输入、参赛名单或 N−1 淘汰场数不一致";
            foreach (var e in input.Entrants)
            {
                var r = t.Rosters.SingleOrDefault(r => r.Id == e.Id);
                var budget = ArenaChallengeRules.Budget(input, e);
                if (e.Squad != null)
                {
                    var squadError = SquadTemplates.ValidateRoster(r!, input.SquadTemplates[e.Squad], budget, e.Count ?? input.Count, ArenaChallengeRules.CostMode(input, e));
                    if (squadError != null) return squadError;
                    continue;
                }
                if (r == null || r.PawnKind != e.PawnKind || r.UnitCombatPower <= 0 || r.Mode != ArenaChallengeRules.RosterMode(input, e) || r.RequestedBudget != budget || r.ResolvedCount < 1 || r.ResolvedCount > CombatRoster.MaxTeamMembers) return "配兵身份或 combatPower 快照无效";
                if (e.CostMode != null && r.CostMode != e.CostMode) return "参赛者独立成本口径与冻结名单不符";
                var config = new System.Xml.Linq.XElement("PawnTemplate", new System.Xml.Linq.XElement("basePawnKind", e.PawnKind));
                if (e.Xenotype != null) config.Add(new System.Xml.Linq.XElement("Xenotype", new System.Xml.Linq.XAttribute("def", e.Xenotype)));
                var generatedError = CombatRoster.ValidateGenerated(r, config, budget, e.Count ?? input.Count, ArenaChallengeRules.CostMode(input, e));
                if (generatedError != null) return generatedError;
            }
            if (input.Format != "knockout")
            {
                var challengeError = ValidateChallenges(input, t, verifyArtifactFiles);
                if (challengeError != null) return challengeError;
                continue;
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
                    if (initialOdd)
                    {
                        var ordered = input.MatchmakingMode == "random" ? RandomOrder(alive, input.Seed, 0, "qualification") : null;
                        queue.Enqueue(ordered == null ? Closest(alive, t) : ArenaScheduler.Pair(ordered[0], ordered[1])); kind = "qualification"; initialOdd = false;
                    }
                    else if (alive.Count % 2 == 1)
                    {
                        kind = "extra";
                        var lowest = alive.OrderBy(id => scores[id]).ThenBy(id => ArenaScheduler.Stable(input.Seed, id), StringComparer.Ordinal).ThenBy(id => id, StringComparer.Ordinal).Take(2).ToArray();
                        queue.Enqueue(ArenaScheduler.Pair(lowest[0], lowest[1]));
                    }
                    else
                    {
                        round++; kind = "round";
                        var random = input.MatchmakingMode == "random";
                        var remaining = random ? RandomOrder(alive, input.Seed, round, kind) : alive.ToList();
                        while (remaining.Count > 0) { var pair = random ? ArenaScheduler.Pair(remaining[0], remaining[1]) : Closest(remaining, t); queue.Enqueue(pair); remaining.Remove(pair[0]); remaining.Remove(pair[1]); }
                    }
                }
                var pairExpected = queue.Dequeue();
                var key = JsonSerializer.Serialize(ArenaScheduler.Pair(m.Left, m.Right));
                var leftFirst = (Convert.ToUInt32(ArenaScheduler.Stable(input.Seed, pairExpected).Substring(0, 8), 16) & 1) == 0;
                var predecessors = pairExpected.Where(previous.ContainsKey).Select(id => previous[id]).Distinct().ToArray();
                if (!seen.Add(key) || !pairExpected.SequenceEqual(m.PairKey) || m.Left != pairExpected[leftFirst ? 0 : 1] || m.Right != pairExpected[leftFirst ? 1 : 0] || m.Kind != kind || m.Round != round || !m.Predecessors.SequenceEqual(predecessors) || m.Winner != m.Left && m.Winner != m.Right || m.Attempts.Count < 1 || m.Attempts.Count > input.MaxReplays + 1) return "配对、换边去重、轮次、前驱或胜者无效";
                var matchError = ValidateMatch(input, t.Rosters, m, verifyArtifactFiles);
                if (matchError != null) return matchError;
                scores[m.Winner] = m.Score!.Value; previous[m.Winner] = m.Id; alive.Remove(m.Winner == m.Left ? m.Right : m.Left);
            }
            if (string.IsNullOrEmpty(t.ChampionScreenshot) || verifyArtifactFiles && !File.Exists(t.ChampionScreenshot)) return "缺少冠军截图";
            if (queue.Count != 0 || alive.Count != 1 || t.Champion != alive[0]) return "冠军不是唯一最终存活参赛者";
            var top = t.TopFive;
            if (top == null || !top.Completed || top.DurationSeconds != ArenaStandings.DisplaySeconds || !double.IsFinite(top.VisibleSeconds)
                || top.VisibleSeconds < top.DurationSeconds || top.SimulationTicks != 0 || top.Screenshot != t.ChampionScreenshot)
                return "前五展示缺失、未满十秒或展示期间推进了模拟";
            // Reconstruct placing independently; the tournament already passed all
            // knockout checks above. Extra matches follow the round at the same depth.
            var placing = t.Rosters.Select(roster => {
                var loss = t.Matches.SingleOrDefault(m => (m.Left == roster.Id || m.Right == roster.Id) && m.Winner != roster.Id);
                var wins = t.Matches.Where(m => m.Winner == roster.Id).ToList();
                return new ArenaPlacement { EntrantId = roster.Id, EliminationStage = loss == null ? int.MaxValue : loss.Round * 2 + (loss.Kind == "extra" ? 1 : 0),
                    Wins = wins.Count, LastVictoryScore = wins.LastOrDefault()?.Score.Value ?? 0 };
            }).OrderByDescending(p => p.EliminationStage).ThenByDescending(p => p.LastVictoryScore)
                .ThenBy(p => ArenaScheduler.Stable(input.Seed, p.EntrantId), StringComparer.Ordinal).ThenBy(p => p.EntrantId, StringComparer.Ordinal)
                .Take(5).Select((p, index) => { p.Rank = index + 1; return p; }).ToList();
            if (JsonSerializer.Serialize(top.Entries) != JsonSerializer.Serialize(placing)) return "前五名次、晋级深度或最近胜利评分与完整赛果不符";
        }
        return null;
    }
    private static decimal? NextChallengeValue(ArenaGrowth growth, decimal value)
    {
        decimal next;
        try { next = growth.Mode == "add" ? checked(value + growth.Increment) : checked(value * growth.Multiplier); }
        catch (OverflowException) { return null; }
        if (growth.Target == "count") next = decimal.Ceiling(next);
        if (next <= value) throw new ScenarioConfigurationException("挑战值未严格增长");
        return next <= growth.MaxValue ? next : null;
    }

    private static string? ValidateChallengeRoster(ArenaTournamentInput input, ArenaEntrant entrant, ResolvedCombatRoster baseline,
        ResolvedCombatRoster roster, string pairId, int level, decimal value)
    {
        if (roster == null || roster.Id != entrant.Id) return "本档挑战者名册身份不符";
        if (level == 0 || input.Format == "bossRoundRobin")
            return JsonSerializer.Serialize(roster) == JsonSerializer.Serialize(baseline) ? null : "首档/轮战名册与基准不符";
        var seed = SquadTemplates.MemberSeed(input.Seed, pairId, level);
        if (input.Growth.Target == "count")
        {
            var expected = JsonSerializer.Deserialize<ResolvedCombatRoster>(JsonSerializer.Serialize(baseline))!;
            expected.ResolvedCount = (int)value;
            if (baseline.Members.Count > 0)
            {
                expected.Members = Enumerable.Range(0, (int)value).Select(i => {
                    var member = JsonSerializer.Deserialize<ResolvedSquadMember>(JsonSerializer.Serialize(baseline.Members[i % baseline.Members.Count]))!;
                    if (i >= baseline.Members.Count) member.Seed = SquadTemplates.MemberSeed(seed, baseline.Id, i);
                    return member;
                }).ToList();
                expected.UnitCombatPower = expected.Members.Sum(m => m.Cost) / value;
            }
            return JsonSerializer.Serialize(roster) == JsonSerializer.Serialize(expected) ? null : "人数增长、混编比例、成员种子或配装不符";
        }
        if (entrant.Squad != null)
        {
            var error = SquadTemplates.ValidateRoster(roster, input.SquadTemplates[entrant.Squad], value, input.Count, ArenaChallengeRules.CostMode(input, entrant));
            if (error != null) return error;
            if (roster.DisplayName != baseline.DisplayName || roster.Source != baseline.Source
                || roster.Members.Where((m, i) => m.Seed != SquadTemplates.MemberSeed(seed, entrant.Id, i)).Any()) return "预算档位名册的种子或来源不符";
            return null;
        }
        if (baseline.GenerationPolicy == "frozen")
        {
            var error = CombatRoster.ValidateGenerated(roster, System.Xml.Linq.XElement.Parse(baseline.TemplateXml), value, input.Count, ArenaChallengeRules.CostMode(input, entrant));
            if (error != null) return error;
            if (roster.Members.Where((m, i) => m.Seed != SquadTemplates.MemberSeed(seed, entrant.Id, i)).Any()) return "预算档位成员种子不符";
            return null;
        }
        var count = decimal.Floor(value / baseline.UnitCombatPower);
        if (count < 1 || count > CombatRoster.MaxTeamMembers) return "超人数上限的预算档位参与战斗";
        var resolved = JsonSerializer.Deserialize<ResolvedCombatRoster>(JsonSerializer.Serialize(baseline))!;
        resolved.RequestedBudget = value; resolved.ResolvedCount = (int)count;
        return JsonSerializer.Serialize(roster) == JsonSerializer.Serialize(resolved) ? null : "预算增长未生成最大整数人数或改变了单位成本";
    }

    private static string? ValidateChallenges(ArenaTournamentInput input, ArenaTournamentResult t, bool verifyArtifactFiles)
    {
        var pairs = input.Entrants.Where(e => e.Role == "challenger")
            .SelectMany(c => input.Entrants.Where(e => e.Role == "boss").Select(b => (challenger: c, boss: b))).ToList();
        if (t.Champion != null || t.TopFive != null || t.ChampionScreenshot != null || t.Challenges.Count != pairs.Count)
            return "挑战赛不能使用淘汰冠军，或交叉配对数量不符";
        var index = 0;
        for (var pi = 0; pi < pairs.Count; pi++)
        {
            var pair = t.Challenges[pi]; var expected = pairs[pi];
            var initial = ArenaChallengeRules.InitialValue(input, expected.challenger);
            if (pair.Id != input.TestDefName + "/pair-" + (pi + 1) || pair.Boss != expected.boss.Id || pair.Challenger != expected.challenger.Id
                || pair.InitialValue != initial || pair.Matches.Count == 0 || input.Format == "bossRoundRobin" && pair.Matches.Count != 1) return "挑战组身份、顺序、初始值或场数不符";
            var value = initial;
            for (var level = 0; level < pair.Matches.Count; level++)
            {
                if (index >= t.Matches.Count) return "挑战组引用了不存在的对局";
                var m = t.Matches[index++];
                if (m.Id != input.TestDefName + "/match-" + index || pair.Matches[level] != m.Id || m.PairId != pair.Id
                    || m.Left != pair.Boss || m.Right != pair.Challenger || m.Level != level || m.Round != pi + 1 || m.Kind != input.Format
                    || m.ChallengeValue != value || !m.Predecessors.SequenceEqual(level == 0 ? Array.Empty<string>() : new[] { pair.Matches[level - 1] })
                    || m.Winner != null && m.Winner != pair.Boss && m.Winner != pair.Challenger || m.ResolvedRosters.Count != 2)
                    return "挑战档位、乘数、次序、双方角色或前驱不符";
                var boss = t.Rosters.Single(r => r.Id == pair.Boss); var baseline = t.Rosters.Single(r => r.Id == pair.Challenger);
                if (JsonSerializer.Serialize(m.ResolvedRosters[0]) != JsonSerializer.Serialize(boss)) return "魔王名册发生变化";
                var rosterError = ValidateChallengeRoster(input, expected.challenger, baseline, m.ResolvedRosters[1], pair.Id, level, value);
                if (rosterError != null) return rosterError;
                var matchError = ValidateMatch(input, m.ResolvedRosters, m, verifyArtifactFiles);
                if (matchError != null) return matchError;
                if (level < pair.Matches.Count - 1)
                {
                    if (m.Winner != pair.Boss || input.Format != "limitChallenge") return "胜利、平局或轮战后继续增长";
                    var next = NextChallengeValue(input.Growth, value);
                    if (next == null) return "超过增长上限仍然开战";
                    value = next.Value;
                }
            }
            var last = t.Matches[index - 1];
            if (last.Winner == pair.Challenger)
            {
                if (pair.Status != "won" || pair.WinningValue != value || pair.LimitReason != null || pair.LimitRoster != null) return "首次获胜终态不符";
            }
            else if (last.Winner == null)
            {
                if (pair.Status != "unresolvedDraw" || pair.WinningValue != null || pair.LimitReason != null || pair.LimitRoster != null) return "平局未决终态不符";
            }
            else if (input.Format == "bossRoundRobin")
            {
                if (pair.Status != "lost" || pair.WinningValue != null || pair.LimitReason != null || pair.LimitRoster != null) return "轮战败局终态不符";
            }
            else
            {
                if (pair.Status != "limitReached" || pair.WinningValue != null) return "增长赛未正确结束";
                var next = NextChallengeValue(input.Growth, value);
                if (next == null)
                {
                    if (pair.LimitReason != "maxValue" || pair.LimitRoster != null) return "增长上限原因不符";
                }
                else
                {
                    var baseline = t.Rosters.Single(r => r.Id == pair.Challenger);
                    if (pair.LimitReason == "COMBAT_ROSTER_LIMIT_EXCEEDED")
                    {
                        if (input.Growth.Target != "budget") return "人数增长不能超出已校验的人数上限";
                        if (baseline.SquadId == null)
                        {
                            if (baseline.GenerationPolicy == "frozen")
                            {
                                var rejected = pair.LimitRoster;
                                if (rejected == null || rejected.Id != baseline.Id) return "缺少逐成员超员配兵证据";
                                var rejectedError = CombatRoster.ValidateGenerated(rejected, System.Xml.Linq.XElement.Parse(baseline.TemplateXml), next, input.Count, baseline.CostMode, rejectedLimit: true);
                                if (rejectedError != null) return rejectedError;
                            }
                            else if (pair.LimitRoster != null || decimal.Floor(next.Value / baseline.UnitCombatPower) <= CombatRoster.MaxTeamMembers) return "伪造预算人数上限";
                        }
                        else
                        {
                            var rejected = pair.LimitRoster;
                            if (rejected == null || rejected.Id != baseline.Id || rejected.SquadId != baseline.SquadId
                                || rejected.RequestedBudget != next || rejected.Members.Count <= CombatRoster.MaxTeamMembers || rejected.ResolvedCount != rejected.Members.Count
                                || rejected.ActualCombatPower > next || rejected.UnitCombatPower != rejected.ActualCombatPower / rejected.ResolvedCount
                                || rejected.Members.Any(m => m.PawnTemplateXml != null)) return "缺少超员混编的原生配兵快照";
                            // The rejected allocation has not spawned Pawns. Check its native
                            // cost/pool evidence with a bounded projection and synthetic kind tags.
                            var projected = JsonSerializer.Deserialize<ResolvedCombatRoster>(JsonSerializer.Serialize(rejected))!;
                            var template = input.SquadTemplates[baseline.SquadId];
                            for (var offset = 0; offset < rejected.Members.Count; offset += CombatRoster.MaxTeamMembers)
                            {
                                projected.Members = rejected.Members.Skip(offset).Take(CombatRoster.MaxTeamMembers).Select(m => {
                                    var member = JsonSerializer.Deserialize<ResolvedSquadMember>(JsonSerializer.Serialize(m))!;
                                    member.PawnTemplateXml = new System.Xml.Linq.XElement("PawnTemplate", new System.Xml.Linq.XElement("basePawnKind", m.PawnKind),
                                        m.Xenotype == null ? null : new System.Xml.Linq.XElement("Xenotype", new System.Xml.Linq.XAttribute("def", m.Xenotype))).ToString();
                                    return member;
                                }).ToList();
                                projected.ResolvedCount = projected.Members.Count; projected.PawnKind = projected.Members[0].PawnKind;
                                projected.UnitCombatPower = projected.ActualCombatPower / projected.ResolvedCount;
                                var allocationError = SquadTemplates.ValidateRoster(projected, template, next, input.Count, ArenaChallengeRules.CostMode(input, expected.challenger));
                                if (allocationError != null) return allocationError;
                            }
                        }
                    }
                    else if (pair.LimitReason == "COMBAT_FORMATION_LIMIT_EXCEEDED")
                    {
                        var rosterError = ValidateChallengeRoster(input, expected.challenger, baseline, pair.LimitRoster!, pair.Id, pair.Matches.Count, next.Value);
                        if (rosterError != null) return rosterError;
                        try
                        {
                            ArenaLoader.CompileBattle(input, new() { t.Rosters.Single(r => r.Id == pair.Boss), pair.LimitRoster! }, last, 0);
                            return "伪造阵型上限";
                        }
                        catch (ScenarioConfigurationException ex) when (ex.Code == "COMBAT_FORMATION_LIMIT_EXCEEDED") { }
                    }
                    else return "未知或提前结束的增长限制";
                }
            }
        }
        if (index != t.Matches.Count) return "挑战组之外存在额外对局";
        var summary = t.Summary;
        if (summary == null || !summary.Completed) return "缺少最终成绩展示";
        var standings = input.Entrants.Where(e => e.Role == "boss").Select(e => {
            var victories = t.Matches.Where(m => m.Winner == e.Id).ToList(); var sum = victories.Sum(m => m.Score.Value);
            return new ArenaBossStanding { EntrantId = e.Id, Wins = victories.Count, Losses = t.Matches.Count(m => m.Left == e.Id && m.Winner == m.Right),
                Unresolved = t.Challenges.Count(p => p.Boss == e.Id && p.Status == "unresolvedDraw"), ScoreSum = sum,
                AverageVictoryScore = victories.Count == 0 ? null : sum / victories.Count };
        }).ToList();
        if (JsonSerializer.Serialize(summary.Bosses) != JsonSerializer.Serialize(standings)) return "魔王胜场、平均分或评分样本不符";
        var entries = input.Format == "bossRoundRobin" ? input.Entrants.Where(e => e.Role == "boss").Select(e => e.Id).ToList() : t.Challenges.Select(p => p.Id).ToList();
        if (summary.Pages.Count != (entries.Count + ArenaChallengeRules.SummaryPageSize - 1) / ArenaChallengeRules.SummaryPageSize
            || summary.Pages.Select(p => p.Screenshot).Distinct(StringComparer.Ordinal).Count() != summary.Pages.Count) return "成绩页缺失或截图重复";
        for (var i = 0; i < summary.Pages.Count; i++)
        {
            var page = summary.Pages[i];
            if (!page.EntryIds.SequenceEqual(entries.Skip(i * ArenaChallengeRules.SummaryPageSize).Take(ArenaChallengeRules.SummaryPageSize))
                || !double.IsFinite(page.VisibleSeconds) || page.VisibleSeconds < ArenaStandings.DisplaySeconds || page.SimulationTicks != 0
                || string.IsNullOrEmpty(page.Screenshot) || !page.Screenshot.EndsWith(".png", StringComparison.Ordinal) || verifyArtifactFiles && !File.Exists(page.Screenshot)) return "成绩页内容、停留时长、模拟 tick 或截图不符";
        }
        return null;
    }

    private static string? ValidateMatch(ArenaTournamentInput input, List<ResolvedCombatRoster> rosters, ArenaMatch m, bool verifyArtifactFiles)
    {
        if (m.Attempts.Count < 1 || m.Attempts.Count > input.MaxReplays + 1) return "对局尝试次数无效";
        for (int ai = 0; ai < m.Attempts.Count; ai++)
        {
            var a = m.Attempts[ai]; var battle = ArenaLoader.CompileBattle(input, rosters, m, ai);
            var generator = (string?)System.Xml.Linq.XElement.Parse(battle.Xml).Element("Scene")?.Attribute("generatorDef") ?? "LogSync_AutoTestStudioMap";
            if (generator == "LogSync_AutoTestStudioMap" && a.StudioIsolation == null) return "缺少测试场地隔离记录";
            if (a.StudioIsolation is { } isolation && (!isolation.WorldTileUnchanged || isolation.RemainingThingsBeforeActors != 0 || isolation.NonSoilCellsBeforeActors != 0))
                return "测试场地被额外生成内容污染或源世界地块被修改";
            if (a.ArenaMaintenance == null) return "缺少擂台环境维护记录";
            if (a.ArenaMaintenance is { } maintenance)
            {
                if (maintenance.ClearCorpses != input.ClearCorpses || maintenance.ClearDroppedItems != input.ClearDroppedItems
                    || !maintenance.CalendarRestored || maintenance.NoonResetTicks.Count == 0 || maintenance.NoonResetTicks[0] != 0
                    || maintenance.NoonResetTicks.Count != maintenance.NoonResetHours.Count
                    || maintenance.NoonResetTicks.Count != a.ElapsedTicks / 5000 + 1
                    || maintenance.NoonResetTicks.Where((tick, i) => tick != i * 5000 || tick > a.ElapsedTicks).Any()
                    || maintenance.NoonResetHours.Any(hour => !float.IsFinite(hour) || Math.Abs(hour - 12) > .001f)
                    || !input.ClearCorpses && maintenance.ClearedCorpses != 0 || !input.ClearDroppedItems && maintenance.ClearedItems != 0)
                    return "擂台清理配置、正午重设或日历恢复不符";
            }
            if (a.CaseId != battle.CaseId || a.TestDefName != input.TestDefName || a.InputHash != input.InputHash || a.Seed != battle.Seed || a.ExecutionStatus != "completed" || !a.CleanupComplete || a.IsPocketMap || a.Teams.Count != 2 || a.Teams.Select(x => x.Id).Order().SequenceEqual(new[] { "A", "B" }) == false) return "战斗身份、种子或清理不符";
            foreach (var side in new[] { "A", "B" })
            {
                var roster = rosters.Single(r => r.Id == (side == "A" ? m.Left : m.Right));
                if (a.Teams.Single(x => x.Id == side).Initial != roster.ResolvedCount || (roster.Members.Count == 0 && a.Units.Any(u => u.Team == side && !u.JoinedDuringBattle && u.Kind != roster.PawnKind))) return "实际初始单位与冻结配兵不符";
                if (roster.Members.Count > 0)
                {
                    var memberError = SquadTemplates.ValidateUnits(roster, a.Units.Where(u => u.Team == side), side == "A" ? "left" : "right");
                    if (memberError != null) return memberError;
                }
            }
            if (JsonSerializer.Serialize(a.ResolvedRosters) != JsonSerializer.Serialize(new[] { rosters.Single(r => r.Id == m.Left), rosters.Single(r => r.Id == m.Right) })) return "战斗名册与本档快照不符";
            var preparationError = CombatPreparation.Validate(a, System.Xml.Linq.XElement.Parse(battle.Xml));
            if (preparationError != null) return preparationError;
            var ledgerError = GroupCombatResultValidator.ValidateIntegrity(new GroupCombatRunReport { Rounds = new() { a } });
            if (ledgerError != null) return ledgerError;
            var metrics = System.Xml.Linq.XElement.Parse(battle.Xml).Element("GroupCombat")?.Element("Metrics");
            if (metrics != null && a.RequireCompleteAttribution != (ScenarioLoader.A(metrics, "requireCompleteAttribution", "false") == "true"))
                return "战斗测量要求与冻结 XML 不符";
            if (metrics != null && a.RequireCompleteAttribution && a.MeasurementRequirementsMet && a.MeasurementQuality != "complete")
                return "完整归属要求被错误标记为已满足";
            var endError = ValidateEnd(a, input);
            if (endError != null) return endError;
            var wins = a.Teams.Where(x => x.Outcome == "win").ToList();
            if (ai < m.Attempts.Count - 1 && wins.Count != 0 || ai == m.Attempts.Count - 1 && (m.Winner == null ? wins.Count != 0 || m.Attempts.Count != input.MaxReplays + 1 : wins.Count != 1 || (wins[0].Id == "A" ? m.Left : m.Right) != m.Winner)) return "平局重赛或晋级胜者不符";
            if (a.Screenshots.Count != 3 || !a.Screenshots.Any(p => p.EndsWith("_bracket.png", StringComparison.Ordinal)) || !a.Screenshots.Any(p => p.EndsWith("_initial.png", StringComparison.Ordinal)) || !a.Screenshots.Any(p => p.EndsWith("_final.png", StringComparison.Ordinal)) || verifyArtifactFiles && a.Screenshots.Any(p => !File.Exists(p))) return "缺少赛前预览、初始战场或战斗结算截图";
        }
        if (m.PreBattles.Count != m.Attempts.Count || m.PostBattleScreenshots.Count != m.Attempts.Count) return "赛事缺少每场完整展示";
        if (!m.BracketScreenshots.SequenceEqual(m.Attempts.SelectMany(a => a.Screenshots.Where(p => p.EndsWith("_bracket.png", StringComparison.Ordinal))))) return "对阵图截图与每次尝试不一致";
        if (m.PostBattleScreenshots.Count > 0 && (m.PostBattleScreenshots.Count != m.Attempts.Count || m.PostBattleScreenshots.Distinct(StringComparer.Ordinal).Count() != m.Attempts.Count || m.PostBattleScreenshots.Any(p => !p.EndsWith("_postbattle.png", StringComparison.Ordinal) || verifyArtifactFiles && !File.Exists(p)))) return "赛后天梯截图与每次尝试不一致";
        if (m.PreBattles.Count > 0 && (m.PreBattles.Count != m.Attempts.Count
            || m.PreBattles.Select(p => p.Screenshot).Distinct(StringComparer.Ordinal).Count() != m.Attempts.Count
            || m.PreBattles.Any(p => !p.Completed || p.DurationSeconds != ArenaPresentation.PreBattleSeconds
                || !double.IsFinite(p.VisibleSeconds) || p.VisibleSeconds < p.DurationSeconds || p.SimulationTicks != 0
                || !p.EntrantIds.SequenceEqual(new[] { m.Left, m.Right })
                || string.IsNullOrEmpty(p.Screenshot) || !p.Screenshot.EndsWith("_prebattle.png", StringComparison.Ordinal)
                || verifyArtifactFiles && !File.Exists(p.Screenshot)))) return "赛前对阵展示缺失、双方不符、未满五秒或展示期间推进了模拟";
        if (m.Winner == null) return m.Score == null ? null : "平局不能有胜利评分";
        var last = m.Attempts.Last(); var winner = last.Teams.Single(x => x.Outcome == "win");
        var enemy = last.Teams.Single(x => x.Outcome == "loss");
        var damageFallback = !last.MeasurementRequirementsMet || last.MeasurementQuality is not ("partial" or "complete");
        if (!double.IsFinite(winner.EnemyDamage) || !double.IsFinite(winner.DamageTaken) || winner.EnemyDamage < 0 || winner.DamageTaken < 0) return "评分伤害输入无效";
        // Recompute each component independently from the final ledger, not the runtime scorer.
        var standing = last.Units.Count(u => u.Team == winner.Id && !u.JoinedDuringBattle && u.State == "standing");
        var downed = last.Units.Count(u => u.Team == winner.Id && !u.JoinedDuringBattle && u.State == "downed");
        var enemyStanding = last.Units.Count(u => u.Team == enemy.Id && !u.JoinedDuringBattle && u.State == "standing");
        var effectiveTicks = last.EndReason == "allUnavailable" && enemy.EliminatedAtTick.HasValue ? enemy.EliminatedAtTick.Value : last.ElapsedTicks;
        if (effectiveTicks < 0 || effectiveTicks > last.ElapsedTicks) return "评分有效战斗时钟无效";
        var preservation = (standing + .25m * downed) / winner.Initial;
        var suppression = 1 - (decimal)enemyStanding / enemy.Initial;
        var neutral = damageFallback || winner.EnemyDamage == 0 && winner.DamageTaken == 0;
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        var dealt = double.Parse(winner.EnemyDamage.ToString("G15", culture), culture);
        var taken = double.Parse(winner.DamageTaken.ToString("G15", culture), culture);
        if (!double.IsFinite(dealt)) dealt = winner.EnemyDamage;
        if (!double.IsFinite(taken)) taken = winner.DamageTaken;
        var largest = Math.Max(dealt, taken);
        var scale = largest > 1e20 || largest > 0 && largest < 1e-10 ? largest : 1;
        var dealtDecimal = decimal.Parse((dealt / scale).ToString("G15", culture), System.Globalization.NumberStyles.Float, culture);
        var takenDecimal = decimal.Parse((taken / scale).ToString("G15", culture), System.Globalization.NumberStyles.Float, culture);
        var exchange = neutral ? .5m : dealtDecimal / (dealtDecimal + takenDecimal);
        var speed = Math.Max(0, 1 - (decimal)effectiveTicks / 60 / input.TimeReferenceSeconds);
        var computed = new ArenaScore {
            Algorithm = "victory-quality-v2", Initial = winner.Initial, Standing = standing, Downed = downed,
            EnemyInitial = enemy.Initial, EnemyStanding = enemyStanding,
            ElapsedTicks = last.ElapsedTicks, EffectiveElapsedTicks = effectiveTicks,
            EnemyDamage = winner.EnemyDamage, DamageTaken = winner.DamageTaken,
            PreservationRatio = preservation, SuppressionRatio = suppression, ExchangeRatio = exchange, SpeedFactor = speed,
            PreservationPoints = input.SurvivalWeight * preservation, SuppressionPoints = input.SuppressionWeight * suppression,
            ExchangePoints = input.ExchangeWeight * exchange, SpeedPoints = input.SpeedWeight * speed,
            DamageQuality = last.MeasurementQuality, NeutralExchange = neutral, UsedDamageFallback = damageFallback, IsRankable = true
        };
        computed.Value = computed.PreservationPoints + computed.SuppressionPoints + computed.ExchangePoints + computed.SpeedPoints;
        if (m.Score == null || computed.Value < 0 || computed.Value > 100) return "缺少有效的百分制评分";
        foreach (var property in typeof(ArenaScore).GetProperties())
            if (!Equals(property.GetValue(m.Score), property.GetValue(computed)))
                return "百分制评分无法按原始账本复算: " + m.Id + " / " + property.Name + "，记录=" + property.GetValue(m.Score) + "，复算=" + property.GetValue(computed);
        return null;
    }
    private static List<string> RandomOrder(List<string> ids, int seed, int round, string kind) => ids
        .OrderBy(id => ArenaScheduler.Stable(seed, "arena.matchmaking", round, kind, id), StringComparer.Ordinal)
        .ThenBy(id => id, StringComparer.Ordinal).ToList();
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
