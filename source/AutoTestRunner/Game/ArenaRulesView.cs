using System;
using LogSyncMod.AutoTest.Scenarios;
using UnityEngine;
using static LogSyncMod.AutoTest.ArenaBroadcastUI;

namespace LogSyncMod.AutoTest
{
    internal static class ArenaRulesView
    {
        internal static void Draw(ArenaTournamentInput input, float elapsed)
        {
            var knockout = input.Format == "knockout";
            var limit = input.Format == "limitChallenge";
            var mode = knockout ? "擂台淘汰赛" : limit ? "极限挑战赛" : "魔王轮战赛";
            Background("ARENA  /  比赛规则", mode, input.Name);
            Label(64, 232, 1400, 72, knockout ? "一路晋级，只留最后的赢家。" : limit ? "越战越强，直到攻破魔王。" : "迎战每一位挑战者，守住魔王的荣耀。", 38, Gold);
            string[] titles, descriptions;
            if (knockout)
            {
                titles = new[] { "胜者向前", "每一战都要漂亮", "决出最终冠军" };
                descriptions = new[] { "两两交锋，胜者晋级，败者止步。\n首轮人数为奇数时，先打一场资格赛。", "晋级队伍为奇数时，最近一战\n胜利评分最低的两队加赛。", "每场双方以完整阵容出战。\n不断晋级，直到最后一支队伍夺冠。" };
            }
            else if (!limit)
            {
                titles = new[] { "逐一挑战", "败北也不退场", "以战绩见证实力" };
                descriptions = new[] { "每位挑战者依次迎战全部魔王。\n每一组对决，都是全新的开始。", "魔王和挑战者均不会被淘汰。\n每场恢复完整阵容，继续下一战。", "最终公布每位魔王的胜场数，\n以及获胜对局的平均胜利评分。" };
            }
            else
            {
                var g = input.Growth;
                var unit = g.Target == "count" ? "人数" : "CP 预算";
                var growth = g.Mode == "add" ? "每次战败，" + unit + "增加 " + g.Increment.ToString("0.##") + "。"
                    : "每次战败，" + unit + "变为 " + g.Multiplier.ToString("0.##") + " 倍。";
                titles = new[] { "向魔王发起挑战", g.Mode == "add" ? "失败，就再添援军" : "失败，就成倍壮大", "攻克，或抵达极限" };
                descriptions = new[] { "每位挑战者依次迎战全部魔王。\n更换魔王时，从初始阵容重新开始。", growth + "\n" + (g.Target == "count" && g.Mode != "add" ? "人数不足整数时向上取整。" : "带着更强的阵容，再战同一位魔王。"),
                    "获胜即完成本组挑战；上限为 " + g.MaxValue.ToString("0.##") + (g.Target == "count" ? " 人。" : " CP。") + "\n下一档超过上限时，挑战就此止步。" };
            }
            for (var i = 0; i < 3; i++)
            {
                var x = 64 + i * 498;
                Panel(x, 348, 476, 294, i == 1 ? Challenger : Gold);
                Label(x + 26, 368, 100, 65, "0" + (i + 1), 48, new Color(.23f, .32f, .43f));
                Label(x + 26, 438, 424, 45, titles[i], 27);
                Label(x + 26, 500, 424, 102, descriptions[i], 21, Muted);
            }
            Label(66, 686, 1450, 60, (input.MaxReplays > 0 ? "平局以原阵容重赛，最多 " + input.MaxReplays + " 次；仍无胜负，" : "平局不再重赛，")
                + (knockout ? "本届赛事暂停。" : "本组记为未决，继续其余挑战。"), 20, Muted);
            Box(64, 800, 1472, 1, new Color(.17f, .22f, .29f));
            Label(66, 814, 1260, 36, "读懂规则，见证下一场对决。", 18, Muted);
            Label(1340, 814, 196, 36, "即将登场", 18, Gold, TextAnchor.MiddleRight);
        }
    }
}
