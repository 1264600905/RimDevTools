#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LogSyncMod.AutoTest.Scenarios
{
    public static class ArenaReport
    {
        public static string Write(ArenaRunReport report)
        {
            var s = new StringBuilder("# 擂台测试\n\n").AppendLine("Run: " + report.RunId + " · " + report.Status).AppendLine();
            foreach (var t in report.Tournaments)
            {
                s.AppendLine("## " + t.Input.Name).AppendLine().AppendLine("冠军：" + (t.Champion ?? "尚未决出")).AppendLine();
                s.AppendLine("|参赛者|PawnKind|单体 combatPower|人数|预算|实际战力|剩余|").AppendLine("|---|---|---:|---:|---:|---:|---:|");
                foreach (var r in t.Rosters) s.AppendLine($"|{r.Id}|{r.PawnKind}|{r.UnitCombatPower}|{r.ResolvedCount}|{r.RequestedBudget}|{r.ActualCombatPower}|{r.UnusedBudget}|");
                s.AppendLine().AppendLine("评分：百分制胜利质量；保全/压制按初始成员，倒地保全权重 0.25；交换按已记录伤害，速度扣除最终胜负确认等待。")
                    .AppendLine("|对局|类型|双方|胜者|总分 / 100|保全|压制|交换|速度|有效秒数|伤害口径|可排序|尝试次数|")
                    .AppendLine("|---|---|---|---|---:|---:|---:|---:|---:|---:|---|---|---:|");
                foreach (var m in t.Matches) s.AppendLine($"|{m.Id}|{m.Kind}|{m.Left} / {m.Right}|{m.Winner ?? "待定"}|{m.Score?.Value:F2}|{m.Score?.PreservationPoints:F2}|{m.Score?.SuppressionPoints:F2}|{m.Score?.ExchangePoints:F2}|{m.Score?.SpeedPoints:F2}|{m.Score?.EffectiveElapsedTicks / 60d:F2}|{m.Score?.DamageQuality}{(m.Score?.NeutralExchange == true ? " / 无伤害取中性分" : "")}|{m.Score?.IsRankable}|{m.Attempts.Count}|");
                s.AppendLine();
            }
            if (report.Error != null) s.AppendLine(report.Error);
            return s.ToString();
        }
    }
    public sealed class ArenaBracketNode
    {
        public string Id, Label;
        public string EntrantId;
        public float X, Y;
        public bool Active, Completed;
    }
    public sealed class ArenaBracketEdge { public string From, To; }
    public sealed class ArenaBracketLayout
    {
        public List<ArenaBracketNode> Nodes = new();
        public List<ArenaBracketEdge> Edges = new();
        public float Width, Height;
        public static ArenaBracketLayout Build(ArenaTournamentResult tournament)
        {
            var layout = new ArenaBracketLayout(); var tips = new Dictionary<string, ArenaBracketNode>();
            foreach (var r in tournament.Rosters)
            {
                var node = new ArenaBracketNode { Id = "entrant/" + r.Id, EntrantId = r.Id, Label = r.DisplayName + " ×" + r.ResolvedCount + "\nCP " + r.UnitCombatPower, X = 8, Y = 8 + layout.Nodes.Count * 64 };
                tips[r.Id] = node; layout.Nodes.Add(node);
            }
            foreach (var m in tournament.Matches)
            {
                var a = tips[m.Left]; var b = tips[m.Right];
                var x = Math.Max(a.X, b.X) + 242; var y = (a.Y + b.Y) / 2;
                while (layout.Nodes.Any(n => n.X == x && Math.Abs(n.Y - y) < 56)) y += 56;
                var leftName = tournament.Rosters.Single(r => r.Id == m.Left).DisplayName ?? m.Left;
                var rightName = tournament.Rosters.Single(r => r.Id == m.Right).DisplayName ?? m.Right;
                var winnerName = m.Winner == null ? "" : tournament.Rosters.Single(r => r.Id == m.Winner).DisplayName ?? m.Winner;
                var kind = m.Kind == "qualification" ? "资格赛" : m.Kind == "extra" ? "低分加赛" : "第" + m.Round + "轮";
                var node = new ArenaBracketNode { Id = m.Id, EntrantId = m.Winner, X = x, Y = y, Active = m.Winner == null, Completed = m.Winner != null, Label = kind + (m.Winner == null ? " · 本场\n" + leftName + " / " + rightName : "\n" + winnerName + " · " + m.Score.Value.ToString("F2")) };
                layout.Nodes.Add(node); layout.Edges.Add(new() { From = a.Id, To = node.Id }); layout.Edges.Add(new() { From = b.Id, To = node.Id });
                if (m.Winner != null) { tips[m.Winner] = node; tips.Remove(m.Winner == m.Left ? m.Right : m.Left); }
            }
            var last = layout.Nodes.Last();
            var championName = tournament.Rosters.FirstOrDefault(r => r.Id == tournament.Champion)?.DisplayName ?? tournament.Champion;
            var future = new ArenaBracketNode { Id = "future", EntrantId = tournament.Champion, X = layout.Nodes.Max(n => n.X) + 242, Y = last.Y, Label = tournament.Champion == null ? "后续晋级配对\n待定" : "冠军\n" + championName, Completed = tournament.Champion != null };
            if (tournament.Matches.Count > 0) layout.Edges.Add(new() { From = last.Id, To = future.Id });
            layout.Nodes.Add(future);
            layout.Width = layout.Nodes.Max(n => n.X) + 218; layout.Height = layout.Nodes.Max(n => n.Y) + 64;
            return layout;
        }

        public static ArenaBracketLayout BuildPostBattle(ArenaTournamentResult tournament)
        {
            var layout = Build(tournament);
            var nodes = layout.Nodes.ToDictionary(n => n.Id);
            var children = layout.Edges.GroupBy(e => e.To).ToDictionary(g => g.Key, g => g.Select(e => nodes[e.From]).ToList());
            var consumed = layout.Edges.Select(e => e.From).ToHashSet();
            float nextY = 8;
            void Place(ArenaBracketNode node)
            {
                if (children.TryGetValue(node.Id, out var inputs))
                {
                    foreach (var input in inputs) Place(input);
                    node.Y = inputs.Average(n => n.Y);
                    node.X = inputs.Max(n => n.X) + 242;
                }
                else { node.Y = nextY; nextY += 76; }
            }
            // Lay out each known elimination subtree contiguously. No future
            // opponents are invented for the scheduler's dynamic pairings.
            foreach (var root in layout.Nodes.Where(n => !consumed.Contains(n.Id))) Place(root);
            layout.Width = layout.Nodes.Max(n => n.X) + 218;
            layout.Height = layout.Nodes.Max(n => n.Y) + 76;
            return layout;
        }
    }
}
