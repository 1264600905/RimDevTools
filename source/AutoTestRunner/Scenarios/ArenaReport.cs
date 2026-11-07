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
                s.AppendLine().AppendLine("|对局|类型|双方|胜者|胜利分数|尝试次数|").AppendLine("|---|---|---|---|---:|---:|");
                foreach (var m in t.Matches) s.AppendLine($"|{m.Id}|{m.Kind}|{m.Left} / {m.Right}|{m.Winner ?? "待定"}|{m.Score?.Value}|{m.Attempts.Count}|");
                s.AppendLine();
            }
            if (report.Error != null) s.AppendLine(report.Error);
            return s.ToString();
        }
    }
    public sealed class ArenaBracketNode
    {
        public string Id, Label;
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
                var node = new ArenaBracketNode { Id = "entrant/" + r.Id, Label = r.DisplayName + " ×" + r.ResolvedCount + "\nCP " + r.UnitCombatPower, X = 8, Y = 8 + layout.Nodes.Count * 64 };
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
                var node = new ArenaBracketNode { Id = m.Id, X = x, Y = y, Active = m.Winner == null, Completed = m.Winner != null, Label = kind + (m.Winner == null ? " · 本场\n" + leftName + " / " + rightName : "\n" + winnerName + " · " + m.Score.Value.ToString("F2")) };
                layout.Nodes.Add(node); layout.Edges.Add(new() { From = a.Id, To = node.Id }); layout.Edges.Add(new() { From = b.Id, To = node.Id });
                if (m.Winner != null) { tips[m.Winner] = node; tips.Remove(m.Winner == m.Left ? m.Right : m.Left); }
            }
            var last = layout.Nodes.Last();
            var championName = tournament.Rosters.FirstOrDefault(r => r.Id == tournament.Champion)?.DisplayName ?? tournament.Champion;
            var future = new ArenaBracketNode { Id = "future", X = layout.Nodes.Max(n => n.X) + 242, Y = last.Y, Label = tournament.Champion == null ? "后续晋级配对\n待定" : "冠军\n" + championName, Completed = tournament.Champion != null };
            if (tournament.Matches.Count > 0) layout.Edges.Add(new() { From = last.Id, To = future.Id });
            layout.Nodes.Add(future);
            layout.Width = layout.Nodes.Max(n => n.X) + 218; layout.Height = layout.Nodes.Max(n => n.Y) + 64;
            return layout;
        }
    }
}
