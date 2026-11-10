using System;
using System.Linq;
using LogSyncMod.AutoTest.Scenarios;
using UnityEngine;
using Verse;
using static LogSyncMod.AutoTest.ArenaBroadcastUI;

namespace LogSyncMod.AutoTest
{
    internal static class ArenaChallengeView
    {
        private static int historyPage;
        internal static void Reset() { historyPage = -1; }
        internal static string Outcome(ArenaChallengePair pair) => pair.Status switch {
            "won" => "挑战成功", "lost" => "魔王守擂成功", "limitReached" => "抵达极限 · 未攻克",
            "unresolvedDraw" => "平局未决", "running" => "挑战继续", _ => "等待挑战"
        };
        internal static string ValueLabel(ArenaTournamentInput input, decimal? value)
            => value?.ToString("0.##") + ((input.Growth?.Target == "count" || input.Growth == null && input.RosterMode == "count") ? " 人" : " CP预算");
        private static string LimitLabel(string reason) => reason switch {
            "maxValue" => "下一档超过上限", "COMBAT_ROSTER_LIMIT_EXCEEDED" => "下一档超过人数上限",
            "COMBAT_FORMATION_LIMIT_EXCEEDED" => "场地无法容纳下一档", _ => "达到增长限制"
        };

        private static void Fighter(ResolvedCombatRoster roster, float x, bool boss, bool won, bool revealed, bool draw, float elapsed)
        {
            var accent = boss ? Boss : Challenger;
            x += (1 - ArenaPresentation.Progress(elapsed, boss ? 0 : .2f, .55f)) * (boss ? -35 : 35);
            Panel(x, 232, 566, 316, revealed && won ? Gold : accent);
            Box(x + 18, 250, 210, 278, Color.Lerp(new Color(.055f, .073f, .103f), accent, .08f));
            Portrait(x + 32, 283, 184, roster.Id, elapsed);
            Label(x + 32, 256, 180, 30, boss ? "BOSS  /  魔王" : "CHALLENGER  /  挑战者", 15, accent);
            Label(x + 250, 263, 290, 64, roster.DisplayName, 31);
            Label(x + 250, 336, 290, 42, roster.ResolvedCount + " 人出战", 26);
            Label(x + 250, 386, 290, 34, "阵容总 CP  " + roster.ActualCombatPower.ToString("0.##"), 18, Muted);
            Box(x + 250, 452, 286, 1, new Color(.17f, .23f, .31f));
            Label(x + 250, 468, 286, 40, !revealed ? "对决落幕" : draw ? "平分秋色" : won ? "本场胜者  /  VICTORY" : "本场落败", 22, revealed && won ? Gold : Muted);
        }

        internal static void Draw(ArenaTournamentResult tournament, ArenaMatch match, float elapsed)
        {
            var input = tournament.Input;
            var limit = input.Format == "limitChallenge";
            var pair = tournament.Challenges.Single(p => p.Id == match.PairId);
            var revealed = elapsed >= ArenaPresentation.AdvanceSeconds;
            var result = !revealed ? "胜负揭晓" : match.Winner == null ? "未分胜负" : match.Winner == match.Left ? "魔王守擂" : "挑战者获胜";
            Background("ARENA  /  " + (limit ? "极限挑战" : "魔王轮战") + "  /  第 " + match.Round + " 组",
                result, limit ? "第 " + (match.Level + 1) + " 档  ·  " + ValueLabel(input, match.ChallengeValue) : "每位挑战者，迎战每一位魔王");
            Fighter(match.ResolvedRosters[0], 64, true, match.Winner == match.Left, revealed, match.Winner == null, elapsed);
            Fighter(match.ResolvedRosters[1], 970, false, match.Winner == match.Right, revealed, match.Winner == null, elapsed);
            Label(646, 288, 308, 80, revealed && match.Score != null ? match.Score.Value.ToString("F2") : "VS", 62, Gold, TextAnchor.MiddleCenter);
            Label(646, 368, 308, 35, revealed && match.Score != null ? "胜利评分 / 100" : "势均力敌，等待揭晓", 17, Muted, TextAnchor.MiddleCenter);
            Label(646, 424, 308, 64, !revealed ? "" : match.Winner == null ? pair.Status == "unresolvedDraw" ? "本组未决\n继续其他挑战" : "原阵容再战" : Outcome(pair), 23,
                pair.Status == "won" ? Green : Gold, TextAnchor.MiddleCenter);
            var next = !revealed ? "每一次交锋，都留下新的战绩。" : NextAction(tournament, match, pair);
            Box(64, 572, 1472, 58, new Color(.085f, .115f, .15f));
            Label(84, 580, 130, 40, "接下来", 17, Gold);
            Label(218, 580, 1294, 40, next, 22);
            var records = limit ? tournament.Matches.Where(m => m.PairId == pair.Id).ToList() : tournament.Matches;
            const int pageSize = 4;
            var pages = Math.Max(1, (records.Count + pageSize - 1) / pageSize);
            if (historyPage < 0) historyPage = pages - 1;
            historyPage = Math.Min(historyPage, pages - 1);
            Label(64, 652, 1090, 36, limit ? "挑战足迹" : "本届交锋", 22);
            if (Widgets.ButtonText(R(1324, 652, 48, 30), "‹")) historyPage = Math.Max(0, historyPage - 1);
            Label(1378, 650, 100, 34, (historyPage + 1) + " / " + pages, 17, Muted, TextAnchor.MiddleCenter);
            if (Widgets.ButtonText(R(1488, 652, 48, 30), "›")) historyPage = Math.Min(pages - 1, historyPage + 1);
            var visible = records.Skip(historyPage * pageSize).Take(pageSize).ToList();
            for (var i = 0; i < visible.Count; i++)
            {
                var m = visible[i]; var x = 64 + i * 374;
                Panel(x, 704, 350, 109, m == match ? Gold : new Color(.17f, .24f, .32f));
                Portrait(x + 10, 724, 58, m.Left); Portrait(x + 78, 724, 58, m.Right);
                Label(x + 148, 714, 190, 34, limit ? "第 " + (m.Level + 1) + " 档 · " + ValueLabel(input, m.ChallengeValue) : "第 " + m.Round + " 组", 17, Muted);
                Label(x + 148, 754, 190, 34, m == match && !revealed ? "揭晓中" : m.Winner == null ? "平局" : (m.Winner == m.Left ? "魔王胜" : "挑战者胜") + " · " + m.Score.Value.ToString("F1"), 19, m.Winner == m.Right ? Challenger : Gold);
            }
            Label(64, 838, 1472, 30, "每场恢复完整阵容  ·  平局原档重赛" + (match.Score?.UsedDamageFallback == true ? "  ·  本场伤害交换项采用中性评分" : ""), 16, Muted);
        }

        private static string NextAction(ArenaTournamentResult tournament, ArenaMatch match, ArenaChallengePair pair)
        {
            if (match.Winner == null && pair.Status != "unresolvedDraw") return "双方原阵容重赛，再决胜负";
            if (pair.Status == "running" && tournament.Input.Format == "limitChallenge")
                return "挑战继续  ·  " + ValueLabel(tournament.Input, match.ChallengeValue) + "  →  " + ValueLabel(tournament.Input, ArenaChallengeRules.NextValue(tournament.Input.Growth, match.ChallengeValue.Value));
            var next = tournament.Challenges.SkipWhile(p => p.Id != pair.Id).Skip(1).FirstOrDefault();
            if (next == null) return "全部挑战落幕，即将公布最终成绩";
            return tournament.Rosters.Single(r => r.Id == next.Challenger).DisplayName + "  VS  " + tournament.Rosters.Single(r => r.Id == next.Boss).DisplayName
                + (tournament.Input.Format == "limitChallenge" ? "  ·  从初始阵容开始" : "");
        }

        internal static void DrawSummary(ArenaTournamentResult tournament, int pageIndex)
        {
            var roundRobin = tournament.Input.Format == "bossRoundRobin";
            var ids = tournament.Summary.Pages[pageIndex].EntryIds;
            Background("ARENA  /  最终战绩  /  " + (pageIndex + 1) + " — " + tournament.Summary.Pages.Count,
                roundRobin ? "魔王战绩 · 守擂荣耀" : "挑战终章 · 极限见证", tournament.Input.Name);
            var rows = (ids.Count + 1) / 2;
            var height = Math.Min(266f, 584f / rows);
            var top = 238 + (584 - rows * height) / 2;
            for (var i = 0; i < ids.Count; i++)
            {
                var x = i == ids.Count - 1 && ids.Count % 2 == 1 ? 439 : 64 + i % 2 * 750;
                var y = top + i / 2 * height;
                var h = height - 18;
                Panel(x, y, 722, h, roundRobin ? Boss : Challenger);
                if (roundRobin)
                {
                    var standing = tournament.Summary.Bosses.Single(b => b.EntrantId == ids[i]);
                    var roster = tournament.Rosters.Single(r => r.Id == standing.EntrantId);
                    var size = Math.Min(142, h - 22);
                    Portrait(x + 14, y + (h - size) / 2, size, roster.Id);
                    var textX = x + size + 35;
                    Label(textX, y + 8, 700 - size - 35, 38, roster.DisplayName, 28, Gold);
                    Label(textX, y + h * .37f, 270, 38, standing.Wins + " 胜  " + standing.Losses + " 败  " + standing.Unresolved + " 未决", 22);
                    Label(textX, y + h * .68f, 306, 28, "平均胜利评分", 17, Muted);
                    Label(x + 544, y + h * .37f, 156, 65, standing.AverageVictoryScore?.ToString("F2") ?? "—", 43, Gold, TextAnchor.MiddleRight);
                }
                else
                {
                    var pair = tournament.Challenges.Single(p => p.Id == ids[i]);
                    var matches = tournament.Matches.Where(m => m.PairId == pair.Id).ToList(); var last = matches.Last();
                    var roster = last.ResolvedRosters[1]; var boss = last.ResolvedRosters[0];
                    var compact = h < 215; var icon = compact ? 66 : 92;
                    Portrait(x + 14, y + 12, icon, roster.Id); Portrait(x + 114, y + 12, icon, boss.Id);
                    Label(x + 224, y + 8, 476, 34, roster.DisplayName + "  →  " + boss.DisplayName, 24);
                    Label(x + 224, y + 44, 476, 32, Outcome(pair), 23, pair.Status == "won" ? Green : Gold);
                    var metricY = compact ? y + 80 : y + 122;
                    Label(x + 22, metricY, 682, 31, (pair.WinningValue.HasValue ? "首次获胜  " : "最终挑战  ") + ValueLabel(tournament.Input, pair.WinningValue ?? last.ChallengeValue)
                        + "  ·  " + matches.Count + " 档 / " + matches.Sum(m => m.Attempts.Count) + " 场", 20, Gold);
                    if (!compact) Label(x + 22, y + 166, 682, 32, "出战 " + roster.ResolvedCount + " 人  ·  阵容总 CP " + roster.ActualCombatPower.ToString("0.##")
                        + (pair.LimitReason == null ? "" : "  ·  " + LimitLabel(pair.LimitReason)), 18, Muted);
                    if (compact) TooltipHandler.TipRegion(R(x, y, 722, h), "出战 " + roster.ResolvedCount + " 人 · 阵容总 CP " + roster.ActualCombatPower.ToString("0.##")
                        + " · 初始值的 " + (last.ChallengeValue.Value / pair.InitialValue).ToString("0.##") + " 倍"
                        + (pair.LimitReason == null ? "" : "\n" + LimitLabel(pair.LimitReason)));
                }
            }
            Label(64, 838, 1472, 30, roundRobin ? "胜场属于每一次坚守  ·  平均评分只统计魔王的获胜对局" : "每组独立起步  ·  首次获胜值记录本次逐档挑战的突破时刻", 17, Muted);
        }
    }
}
