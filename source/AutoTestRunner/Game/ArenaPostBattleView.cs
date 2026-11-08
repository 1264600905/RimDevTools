using System;
using System.Collections.Generic;
using System.Linq;
using LogSyncMod.AutoTest.Scenarios;
using UnityEngine;
using Verse;

namespace LogSyncMod.AutoTest
{
    // Presentation owns no scheduler state. Layout and identity colors are frozen
    // once per result, and all animation uses real time while the game is paused.
    internal static class ArenaPostBattleView
    {
        private static ArenaTournamentResult tournament;
        private static ArenaMatch match;
        private static ArenaBracketLayout layout;
        private static Dictionary<string, ArenaBracketNode> nodes;
        private static Dictionary<string, ResolvedCombatRoster> rosters;
        private static Dictionary<string, float> hues;
        private static ArenaBracketNode left, right, result;
        private static Vector2 scroll;
        private static bool overview, focus, manualNavigation;
        private static readonly Color Gold = new(1f, .79f, .28f);
        internal static void Begin(ArenaTournamentResult value, ArenaMatch completed)
        {
            tournament = value; match = completed;
            layout = ArenaBracketLayout.BuildPostBattle(value);
            nodes = layout.Nodes.ToDictionary(n => n.Id);
            rosters = value.Rosters.ToDictionary(r => r.Id);
            hues = ArenaPresentation.IdentityHues(value.Rosters.Select(r => r.PawnKind));
            result = nodes[completed.Id];
            var inputs = layout.Edges.Where(e => e.To == completed.Id).ToList();
            left = nodes[inputs[0].From]; right = nodes[inputs[1].From];
            scroll = Vector2.zero; overview = false; focus = true; manualNavigation = false;
        }
        private static Color Identity(string id) => id != null && rosters.TryGetValue(id, out var roster)
            ? Color.HSVToRGB(hues[roster.PawnKind], .62f, .94f) : new Color(.48f, .53f, .6f);
        private static void TextAt(Rect rect, string text, float size, Color color, TextAnchor anchor = TextAnchor.MiddleCenter)
        {
            var old = GUI.color; GUI.color = color;
            GroupCombatOverlay.DrawText(rect, text, size, anchor); GUI.color = old;
        }
        private static void Card(Rect rect, Color color, float emphasis = 0)
        {
            Widgets.DrawBoxSolid(rect, Color.Lerp(new Color(.055f, .07f, .09f), color, .12f + emphasis * .12f));
            var old = GUI.color; GUI.color = color; Widgets.DrawBox(rect, emphasis > .5f ? 2 : 1); GUI.color = old;
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, 4, rect.height), color);
        }
        internal static void Draw(float elapsed, bool champion = false)
        {
            if (layout == null) return;
            var oldColor = GUI.color; var oldFont = Text.Font; var oldAnchor = Text.Anchor;
            try
            {
                GUI.color = Color.white;
                var w = UI.screenWidth; var h = UI.screenHeight;
                Widgets.DrawBoxSolid(new Rect(8, 8, w - 16, h - 16), new Color(.025f, .035f, .05f, .99f));
                Widgets.DrawBoxSolid(new Rect(8, 8, w - 16, 3), champion ? Gold : Identity(match.Winner));
                TextAt(new Rect(26, 19, w - 205, 34), champion ? "冠军诞生 · " + rosters[tournament.Champion].DisplayName : "赛后天梯 · " + MatchKind(match), 27, champion ? Gold : Color.white, TextAnchor.MiddleLeft);
                Text.Font = GameFont.Small;
                if (Widgets.ButtonText(new Rect(w - 150, 20, 122, 32), "取消擂台")) ArenaService.Cancel();
                var winner = match.Winner == null ? null : rosters[match.Winner];
                var subtitle = elapsed < ArenaPresentation.AdvanceSeconds ? "本场对战回顾" : winner == null ? "本场平局 · " + (match.Attempts.Count > tournament.Input.MaxReplays ? "重赛次数已用尽" : "即将重赛") : winner.DisplayName + " 晋级  ·  胜利质量 " + match.Score.Value.ToString("F2") + " / 100";
                TextAt(new Rect(26, 57, w - 52, 27), subtitle, 17, elapsed >= ArenaPresentation.AdvanceSeconds && winner != null ? Gold : new Color(.7f, .76f, .83f), TextAnchor.MiddleLeft);
                var gap = 68f; var cardWidth = Mathf.Min(420, (w - 80 - gap) / 2);
                var cardHeight = h < 720 ? 72 : 88;
                var cardY = 96f;
                var startX = (w - cardWidth * 2 - gap) / 2;
                DrawSide(new Rect(startX, cardY, cardWidth, cardHeight), match.Left, "A队", elapsed, 0, -1);
                DrawSide(new Rect(startX + cardWidth + gap, cardY, cardWidth, cardHeight), match.Right, "B队", elapsed, ArenaPresentation.RevealBSeconds, 1);
                TextAt(new Rect(w / 2 - 32, cardY, 64, cardHeight), "VS", 25, new Color(.64f, .68f, .75f));
                var toolbarY = cardY + cardHeight + 12;
                TextAt(new Rect(26, toolbarY, w - 330, 28), "对象同色 · 暗色为已淘汰 · 后续配对待定", w < 1000 ? 12 : 15, new Color(.63f, .69f, .77f), TextAnchor.MiddleLeft);
                if (Widgets.ButtonText(new Rect(w - 285, toolbarY, 122, 28), "定位本场")) { overview = false; focus = true; manualNavigation = false; }
                if (Widgets.ButtonText(new Rect(w - 153, toolbarY, 122, 28), overview ? "详细天梯" : "全局总览")) { overview = !overview; manualNavigation = true; }
                var viewport = new Rect(26, toolbarY + 40, w - 52, h - toolbarY - 92);
                var scale = Mathf.Min(1.35f, Mathf.Min(viewport.width / layout.Width, viewport.height / layout.Height));
                if (overview || tournament.Rosters.Count <= 8 && scale >= .75f)
                    DrawTree(viewport.position + new Vector2((viewport.width - layout.Width * scale) / 2, (viewport.height - layout.Height * scale) / 2), scale, elapsed);
                else
                {
                    if (Event.current.type is EventType.ScrollWheel or EventType.MouseDown && viewport.Contains(Event.current.mousePosition)) manualNavigation = true;
                    if (focus || !manualNavigation && elapsed < ArenaPresentation.AnimationSeconds)
                    {
                        var target = elapsed < ArenaPresentation.RevealBSeconds ? left : elapsed < ArenaPresentation.AdvanceSeconds ? right : result;
                        var desired = new Vector2(Mathf.Clamp(target.X + 101 - viewport.width / 2, 0, Mathf.Max(0, layout.Width - viewport.width + 20)), Mathf.Clamp(target.Y + 28 - viewport.height / 2, 0, Mathf.Max(0, layout.Height - viewport.height + 20)));
                        scroll = focus ? desired : Vector2.Lerp(scroll, desired, .12f);
                        focus = false;
                    }
                    Widgets.BeginScrollView(viewport, ref scroll, new Rect(0, 0, Mathf.Max(viewport.width - 20, layout.Width), Mathf.Max(viewport.height - 20, layout.Height)));
                    DrawTree(Vector2.zero, 1, elapsed); Widgets.EndScrollView();
                }
                var footer = champion ? "冠军已决出 · " + tournament.Matches.Count + " 场淘汰对决完成" : elapsed < ArenaPresentation.AnimationSeconds ? "A队 → B队 → 胜者晋级" : winner == null ? "平局不晋级 · 双方重新满编后重赛" : "本场已结算 · 下一场双方重新满编";
                TextAt(new Rect(26, h - 43, w - 52, 25), footer, 15, champion ? Gold : new Color(.7f, .76f, .83f));
            }
            finally { GUI.color = oldColor; Text.Font = oldFont; Text.Anchor = oldAnchor; }
        }
        private static string MatchKind(ArenaMatch value) => value.Kind == "extra" ? "低分加赛" : value.Kind == "qualification" ? "资格赛" : "第 " + value.Round + " 轮";
        private static void DrawSide(Rect rect, string id, string side, float elapsed, float delay, int direction)
        {
            var progress = ArenaPresentation.Progress(elapsed, delay, .5f);
            if (progress <= 0) return;
            rect.x += direction * (1 - progress) * 40;
            var old = GUI.color; GUI.color = new Color(1, 1, 1, progress);
            var roster = rosters[id]; var color = Identity(id);
            var revealed = elapsed >= ArenaPresentation.AdvanceSeconds;
            var won = match.Winner == id;
            Card(rect, revealed && !won && match.Winner != null ? color * .55f : color, won && revealed ? 1 : 0);
            var iconSize = rect.height - 24;
            var kind = DefDatabase<PawnKindDef>.GetNamedSilentFail(roster.PawnKind);
            if (kind?.race != null) Widgets.DefIcon(new Rect(rect.x + 12, rect.y + 12, iconSize, iconSize), kind.race);
            var x = rect.x + iconSize + 24; var width = rect.width - iconSize - 36;
            TextAt(new Rect(x, rect.y + 7, width, 22), side + (revealed ? match.Winner == null ? " · 平局" : won ? " · 晋级" : " · 淘汰" : ""), 15, won && revealed ? Gold : color, TextAnchor.MiddleLeft);
            TextAt(new Rect(x, rect.y + 29, width, 27), roster.DisplayName + " ×" + roster.ResolvedCount, Mathf.Min(21, width / Mathf.Max(10, roster.DisplayName.Length + 5)), Color.white, TextAnchor.MiddleLeft);
            if (rect.height > 75) TextAt(new Rect(x, rect.y + 57, width, 22), "单体 " + roster.UnitCombatPower + " · 投入 " + roster.ActualCombatPower, 13, new Color(.7f, .76f, .83f), TextAnchor.MiddleLeft);
            GUI.color = old;
        }
        private static void DrawTree(Vector2 origin, float scale, float elapsed)
        {
            foreach (var edge in layout.Edges)
            {
                var a = nodes[edge.From]; var b = nodes[edge.To];
                var color = Identity(a.EntrantId);
                var isCurrent = edge.To == result.Id;
                var revealed = ArenaPresentation.Progress(elapsed, edge.From == left.Id ? 0 : ArenaPresentation.RevealBSeconds, .5f);
                var progress = isCurrent ? revealed : 1;
                if (isCurrent && match.Winner != null && a.EntrantId == match.Winner)
                    progress = ArenaPresentation.Progress(elapsed, ArenaPresentation.AdvanceSeconds, ArenaPresentation.AnimationSeconds - ArenaPresentation.AdvanceSeconds);
                var p = origin + new Vector2((a.X + 202) * scale, (a.Y + 28) * scale);
                var q = origin + new Vector2(b.X * scale, (b.Y + 28) * scale);
                DrawConnection(p, q, new Color(.23f, .27f, .33f), 1);
                var eliminated = match.Winner != null && a.EntrantId != match.Winner && isCurrent;
                DrawConnection(p, q, color * (eliminated ? .45f : .85f), progress);
            }
            foreach (var node in layout.Nodes)
            {
                var reveal = node.Id == left.Id ? ArenaPresentation.Progress(elapsed, 0, .5f) : node.Id == right.Id ? ArenaPresentation.Progress(elapsed, ArenaPresentation.RevealBSeconds, .5f) : node.Id == result.Id ? ArenaPresentation.Progress(elapsed, ArenaPresentation.AdvanceSeconds, ArenaPresentation.AnimationSeconds - ArenaPresentation.AdvanceSeconds) : 1;
                var color = Identity(node.EntrantId);
                var eliminated = node.EntrantId != null && tournament.Matches.Any(m => m.Winner != null && (m.Left == node.EntrantId || m.Right == node.EntrantId) && m.Winner != node.EntrantId);
                if (eliminated && (node.Id != left.Id && node.Id != right.Id || elapsed >= ArenaPresentation.AdvanceSeconds)) color *= .5f;
                var rect = new Rect(origin.x + node.X * scale, origin.y + node.Y * scale, 202 * scale, 56 * scale);
                Card(rect, Color.Lerp(new Color(.28f, .32f, .39f), color, reveal), node.Id == result.Id ? reveal : 0);
                var label = node.Id == result.Id ? elapsed < ArenaPresentation.AdvanceSeconds ? MatchKind(match) + "\n结果揭晓中"
                    : match.Winner == null ? "本场平局\n" + (match.Attempts.Count > tournament.Input.MaxReplays ? "重赛次数已用尽" : "等待重赛") : node.Label : node.Label;
                TextAt(rect.ContractedBy(5 * scale), label, Mathf.Clamp(14 * scale, 8, 20), Color.white);
                if (node.Id == "future" && tournament.Champion != null)
                {
                    var old = GUI.color; GUI.color = Gold; Widgets.DrawBox(rect, 2); GUI.color = old;
                }
            }
        }
        private static void DrawConnection(Vector2 start, Vector2 end, Color color, float progress)
        {
            var bend = (start.x + end.x) / 2;
            var points = new[] { start, new Vector2(bend, start.y), new Vector2(bend, end.y), end };
            var length = Enumerable.Range(0, 3).Sum(i => Vector2.Distance(points[i], points[i + 1]));
            var remaining = length * progress;
            for (var i = 0; i < 3 && remaining > 0; i++)
            {
                var segment = Vector2.Distance(points[i], points[i + 1]);
                if (segment <= 0) continue;
                Widgets.DrawLine(points[i], Vector2.Lerp(points[i], points[i + 1], Mathf.Min(1, remaining / segment)), color, 2);
                remaining -= segment;
            }
        }
    }
}
