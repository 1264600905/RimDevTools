using System;
using System.Collections.Generic;
using System.Linq;
using LogSyncMod.AutoTest.Scenarios;
using RimWorld;
using UnityEngine;
using Verse;

namespace LogSyncMod.AutoTest
{
    internal static class GroupCombatOverlay
    {
        private static GroupCombatOverlayData data = new();
        private static GroupCombatRoundResult snapshot => data.Snapshot;
        private static readonly Queue<string> pendingPortraits = new();
        private static readonly HashSet<string> scheduledPortraits = new(StringComparer.Ordinal);
        private static int lastPortraitFrame = -1;
        private static int portraitRenderCount, targetRestoreFailures, maximumPortraitsPerFrame;
        internal static bool PortraitsReady => snapshot != null && pendingPortraits.Count == 0;
        internal static object CacheStatus => new { data.RefreshCount, portraitRenderCount, targetRestoreFailures, maximumPortraitsPerFrame, portraits = portraits.Count, pending = pendingPortraits.Count };
        private static bool verifyInput;
        internal static object InputVerification { get; private set; }
        internal static void RequestInputVerification() { verifyInput = true; InputVerification = null; }
        private static readonly Dictionary<string, RenderTexture> portraits = new Dictionary<string, RenderTexture>();
        private static readonly Dictionary<string, GUIStyle> textStyles = new Dictionary<string, GUIStyle>();
        internal static void Clear()
        {
            data = new(); pendingPortraits.Clear(); scheduledPortraits.Clear(); verifyInput = false;
            lastPortraitFrame = -1; portraitRenderCount = targetRestoreFailures = maximumPortraitsPerFrame = 0;
            foreach (var texture in portraits.Values) { texture.Release(); UnityEngine.Object.Destroy(texture); }
            portraits.Clear();
        }
        internal static void Frame()
        {
            if (!CombatFightService.OverlayEnabled || CombatFightService.Display == null) return;
            if (data.Refresh(CombatFightService.Display, CombatFightService.Phase, Time.realtimeSinceStartup))
            {
                // Freeze each pawn's first portrait for this round. Wounds/temporary
                // hediffs do not invalidate these identity images during combat.
                // Visible members first, then keep all others for later Top10 entries.
                foreach (var team in data.Teams)
                {
                    foreach (var group in team.LivingGroups) SchedulePortrait(group.Representative.Id);
                    foreach (var row in team.Output) SchedulePortrait(row.Unit.Id);
                    foreach (var row in team.Taken) SchedulePortrait(row.Unit.Id);
                }
                foreach (var unit in snapshot.Units) SchedulePortrait(unit.Id);
            }
            if (lastPortraitFrame == Time.frameCount) return;
            lastPortraitFrame = Time.frameCount;
            var rendered = 0;
            while (pendingPortraits.Count > 0 && rendered < 2)
            {
                var id = pendingPortraits.Dequeue();
                if (!CombatFightService.Pawns.TryGetValue(id, out var pawn) || pawn.Destroyed) continue;
                var previous = RenderTexture.active;
                portraits.Add(id, PawnPortraitRendering.Copy(pawn));
                if (!ReferenceEquals(RenderTexture.active, previous)) targetRestoreFailures++;
                portraitRenderCount++; rendered++;
            }
            maximumPortraitsPerFrame = Math.Max(maximumPortraitsPerFrame, rendered);
        }

        private static void SchedulePortrait(string id)
        {
            if (scheduledPortraits.Contains(id) || !CombatFightService.Pawns.TryGetValue(id, out var pawn) || pawn.Destroyed || !pawn.RaceProps.Humanlike) return;
            scheduledPortraits.Add(id); pendingPortraits.Enqueue(id);
        }

        internal static void Draw()
        {
            if (!CombatFightService.OverlayEnabled) return;
            if (snapshot == null) return;
            var previousFont = Text.Font; var previousColor = GUI.color; Text.Font = GameFont.Tiny;
            try
            {
                var width = UI.screenWidth; var height = UI.screenHeight;
                var panelWidth = GroupCombatPresentation.PanelWidth(width);
                var panelHeight = GroupCombatPresentation.PanelHeight(height);
                Widgets.DrawWindowBackground(new Rect(8, 34, width - 16, 70));
                DrawText(new Rect(150, 36, width - 300, 38), snapshot.Name, width >= 1280 ? 24 : 18, TextAnchor.MiddleCenter);
                DrawText(new Rect(150, 73, width - 300, 24), data.TimeLabel + (CombatFightService.Measuring && Find.TickManager.Paused ? "已暂停" : Label(CombatFightService.Phase)), 16, TextAnchor.MiddleCenter);
                Text.Font = GameFont.Small;
                if (Widgets.ButtonText(new Rect(width - 142, 54, 120, 32), CombatFightService.IsArena ? "取消擂台" : "取消群战")) { if (CombatFightService.IsArena) ArenaService.Cancel(); else CombatFightService.Cancel(); }
                for (var i = 0; i < snapshot.Teams.Count; i++)
                {
                    var x = i == 0 ? 8 : width - panelWidth - 8;
                    DrawTeam(new Rect(x, GroupCombatPresentation.PanelTop, panelWidth, panelHeight), data.Teams[i], i);
                }
                if (CombatFightService.ShowingIntro) DrawIntro(width, height, panelWidth);
                else if (CombatFightService.IsArena && CombatFightService.Measuring && CombatFightService.CountdownLabel.Length > 0) DrawCountdown(width, height);
                if (CombatFightService.ShowingSettlement) DrawSettlement(width, height, panelWidth);
                if (verifyInput && Event.current.type == EventType.Repaint)
                {
                    verifyInput = false;
                    var original = Event.current;
                    var passed = true;
                    try
                    {
                        foreach (var kind in new[] { EventType.MouseDown, EventType.MouseUp, EventType.ScrollWheel })
                        for (var i = 0; i < snapshot.Teams.Count; i++)
                        {
                            var x = i == 0 ? 8 : width - panelWidth - 8;
                            Event.current = new Event { type = kind, button = 0, mousePosition = new Vector2(x + panelWidth / 2, 200), delta = new Vector2(0, 1) };
                            DrawTeam(new Rect(x, GroupCombatPresentation.PanelTop, panelWidth, panelHeight), data.Teams[i], i);
                            passed &= Event.current.type == kind;
                        }
                        InputVerification = new { passed, checks = 6, scrollViews = 0 };
                    }
                    finally { Event.current = original; }
                }
            }
            finally { Text.Font = previousFont; GUI.color = previousColor; }
        }
        private static Color TeamColor(int index) => index == 0 ? new Color(.3f, .72f, 1f) : new Color(1f, .57f, .3f);
        private static string TeamLabel(int index) => index == 0 ? "A队" : "B队";
        internal static Color NameColor(string state) => state switch
        {
            "standing" => new Color(.4f, 1f, .45f),
            "downed" or "dead" => new Color(1f, .32f, .32f),
            _ => Color.gray
        };
        private static void DrawName(Rect rect, string value, float size, string state)
        {
            var previous = GUI.color; GUI.color = NameColor(state);
            DrawText(rect, value, size); GUI.color = previous;
        }
        internal static void DrawText(Rect rect, string value, float size, TextAnchor anchor = TextAnchor.UpperLeft)
        {
            var fontSize = Mathf.RoundToInt(size);
            var key = fontSize + ":" + (int)anchor;
            if (!textStyles.TryGetValue(key, out var style))
            {
                // Clone the native dynamic font; do not mutate global RimWorld text styles.
                style = new GUIStyle(Text.fontStyles[1]) { fontSize = fontSize, alignment = anchor, wordWrap = false, clipping = TextClipping.Clip, richText = true };
                textStyles.Add(key, style);
            }
            GUI.Label(rect, value, style);
        }
        private static void DrawTeam(Rect rect, GroupCombatOverlayData.Team panel, int index)
        {
            var team = panel.Result;
            Widgets.DrawBoxSolid(rect, new Color(.055f, .075f, .09f, .62f));
            var previousColor = GUI.color; GUI.color = new Color(.65f, .72f, .8f, .5f); Widgets.DrawBox(rect); GUI.color = previousColor;
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, rect.width, 3), TeamColor(index));
            // Pure drawing only: no Window, scroll view, tooltip or invisible button may
            // capture pointer events. Both rankings fit in parallel columns at every size.
            var scale = GroupCombatPresentation.ContentScale(rect.width, rect.height);
            var padding = 7 * scale;
            GUI.BeginGroup(new Rect(rect.x + padding, rect.y + padding, rect.width - padding * 2, rect.height - padding * 2));
            try
            {
                var width = rect.width - padding * 2; var height = rect.height - padding * 2;
                GUI.color = TeamColor(index); DrawText(new Rect(0, 0, width, 23 * scale), TeamLabel(index), 18 * scale); GUI.color = previousColor;
                DrawText(new Rect(0, 24 * scale, width, 20 * scale), team.Name, 12 * scale);
                DrawText(new Rect(0, 44 * scale, width, 20 * scale), panel.Population, 12 * scale);
                var y = 66 * scale;
                if (panel.Reinforcements != null)
                {
                    DrawText(new Rect(0, y, width, 20 * scale), panel.Reinforcements, 12 * scale); y += 21 * scale;
                }
                foreach (var group in panel.LivingGroups)
                {
                    var representative = group.Representative; DrawPortrait(new Rect(0, y, 20 * scale, 20 * scale), representative);
                    var nameRect = new Rect(24 * scale, y, width - 24 * scale, 20 * scale);
                    if (group.Count == 1) DrawHealthName(nameRect, group.Label, 12 * scale, representative, nameRect.width * .6f);
                    else DrawName(nameRect, group.Label, 12 * scale, representative.State);
                    y += 21 * scale;
                }
                if (panel.RemainingKinds != null)
                {
                    DrawText(new Rect(0, y, width, 20 * scale), panel.RemainingKinds, 12 * scale); y += 21 * scale;
                }
                y += 8 * scale;
                var rankWidth = (width - 8 * scale) / 2;
                var rowHeight = Mathf.Min(39 * scale, (height - y - 86 * scale) / 10);
                Ranking(0, rankWidth, y, rowHeight, scale, "累计输出 Top10", panel.Output, new Color(.25f, .65f, .9f), data.MaximumOutput);
                Ranking(rankWidth + 8 * scale, rankWidth, y, rowHeight, scale, "承伤 Top10", panel.Taken, new Color(.9f, .45f, .25f), data.MaximumTaken);
                var footer = height - 60 * scale;
                DrawText(new Rect(0, footer, width, 20 * scale), panel.Mean, 12 * scale);
                DrawText(new Rect(0, footer + 20 * scale, width, 20 * scale), panel.Total, 12 * scale);
                DrawText(new Rect(0, footer + 40 * scale, width, 20 * scale), panel.Outcome, 11 * scale);
            }
            finally { GUI.EndGroup(); }
        }
        private static void Ranking(float x, float width, float y, float rowHeight, float uiScale, string label, GroupCombatOverlayData.Rank[] rows, Color color, double maximum)
        {
            DrawText(new Rect(x, y, width, 22 * uiScale), label, 12 * uiScale); y += 24 * uiScale;
            foreach (var row in rows)
            {
                var unit = row.Unit;
                var icon = Mathf.Min(25 * uiScale, rowHeight - 2);
                DrawPortrait(new Rect(x, y, icon, icon), unit);
                var bar = new Rect(x + icon + 2, y, width - icon - 2, rowHeight - 1);
                Widgets.DrawBoxSolid(bar, new Color(.1f, .1f, .1f, .25f));
                Widgets.DrawBoxSolid(new Rect(bar.x, bar.y, (float)(maximum > 0 ? row.Value / maximum : 0) * bar.width, bar.height), color * .6f);
                var name = row.Name;
                if (rowHeight >= 32 * uiScale)
                {
                    DrawHealthName(new Rect(bar.x, y, bar.width, 19 * uiScale), name, 12 * uiScale, unit);
                    DrawText(new Rect(bar.x, y + 17 * uiScale, bar.width, rowHeight - 17 * uiScale), row.DetailedValue, 12 * uiScale);
                }
                else
                {
                    // Keep damage in its own column: "Ivy 0" looked like an
                    // unnecessary generated name suffix before combat started.
                    var amount = row.CompactValue;
                    var fontSize = Mathf.Min(12 * uiScale, rowHeight - 2);
                    var valueWidth = Mathf.Min(bar.width * .45f, Mathf.Max(30 * uiScale, amount.Length * fontSize * .65f));
                    var nameWidth = bar.width - valueWidth - 8 * uiScale;
                    DrawHealthName(new Rect(bar.x, y, nameWidth, rowHeight), name, fontSize, unit);
                    DrawText(new Rect(bar.xMax - valueWidth, y, valueWidth, rowHeight), amount, fontSize, TextAnchor.MiddleRight);
                }
                y += rowHeight;
            }
        }
        private static void DrawHealthName(Rect rect, string name, float fontSize, GroupCombatUnit unit, float? barWidth = null)
        {
            var healthWidth = barWidth ?? Mathf.Min(55, rect.width * .35f);
            var nameRect = new Rect(rect.x, rect.y, Mathf.Max(1, rect.width - healthWidth - 4), rect.height);
            var shownName = name;
            var maxCharacters = Mathf.Max(1, Mathf.FloorToInt(nameRect.width / (fontSize * .7f)));
            if (shownName.Length > maxCharacters) shownName = shownName.Substring(0, Mathf.Max(0, maxCharacters - 1)) + "…";
            DrawName(nameRect, shownName, fontSize, unit.State);
            var health = unit.State == "dead" ? 0 : Mathf.Clamp01(unit.HealthPercent);
            var bar = new Rect(rect.xMax - healthWidth, rect.center.y - 3, healthWidth, 6);
            Widgets.DrawBoxSolid(bar, new Color(.12f, .14f, .15f, .95f));
            var color = unit.State == "downed" ? new Color(1f, .68f, .2f) : Color.Lerp(new Color(.94f, .22f, .2f), new Color(.3f, .85f, .4f), health);
            if (health > 0) Widgets.DrawBoxSolid(new Rect(bar.x, bar.y, bar.width * health, bar.height), color);
        }
        private static void DrawCountdown(float width, float height)
        {
            var label = CombatFightService.CountdownLabel;
            var size = label == "开始" ? 88 : 138;
            var center = new Rect(width / 2 - 180, height / 2 - 110, 360, 200);
            var previousColor = GUI.color;
            GUI.color = new Color(0, 0, 0, .8f); DrawText(new Rect(center.x + 4, center.y + 4, center.width, center.height), label, size, TextAnchor.MiddleCenter);
            GUI.color = label == "开始" ? new Color(1f, .8f, .3f) : Color.white; DrawText(center, label, size, TextAnchor.MiddleCenter); GUI.color = previousColor;
        }
        private static void DrawIntro(float width, float height, float panelWidth)
        {
            DrawCountdown(width, height);
            var availableWidth = width - panelWidth * 2 - 48;
            var rosterWidth = Mathf.Min(960, availableWidth);
            var rosterHeight = Mathf.Min(176, height / 2 - 210);
            var rosterTop = height / 2 + 100;
            DrawText(new Rect((width - rosterWidth) / 2, rosterTop - 32, rosterWidth, 28), "双方阵容", 20, TextAnchor.MiddleCenter);
            for (var i = 0; i < snapshot.Teams.Count; i++)
            {
                var rect = new Rect((width - rosterWidth) / 2 + i * (rosterWidth + 12) / 2, rosterTop, (rosterWidth - 12) / 2, rosterHeight);
                DrawIntroRoster(rect, data.Teams[i], i);
            }
        }
        private static void DrawIntroRoster(Rect rect, GroupCombatOverlayData.Team panel, int index)
        {
            var team = panel.Result;
            Widgets.DrawBoxSolid(rect, new Color(.035f, .05f, .07f, .85f));
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, rect.width, 3), TeamColor(index));
            var previousColor = GUI.color; GUI.color = TeamColor(index);
            DrawText(new Rect(rect.x + 8, rect.y + 8, rect.width - 16, 28), TeamLabel(index) + "  ·  " + team.Initial + " 名参战", Mathf.Min(20, rect.width / 10), TextAnchor.MiddleCenter); GUI.color = previousColor;
            DrawText(new Rect(rect.x + 8, rect.y + 36, rect.width - 16, 24), team.Name, Mathf.Min(16, rect.width / 16), TextAnchor.MiddleCenter);
            var groups = panel.IntroGroups;
            if (groups.Length == 0) return;
            var columns = Mathf.Min(groups.Length, Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(groups.Length * rect.width / (rect.height * 2)))));
            var rows = Mathf.CeilToInt((float)groups.Length / columns);
            var cellWidth = (rect.width - 16) / columns; var cellHeight = (rect.height - 66) / rows;
            for (var i = 0; i < groups.Length; i++)
            {
                var cell = new Rect(rect.x + 8 + i % columns * cellWidth, rect.y + 64 + i / columns * cellHeight, cellWidth, cellHeight);
                var icon = Mathf.Min(56, Mathf.Min(cell.height - 4, cell.width * .3f));
                DrawPortrait(new Rect(cell.x, cell.y + (cell.height - icon) / 2, icon, icon), groups[i].Representative);
                DrawName(new Rect(cell.x + icon + 5, cell.y + (cell.height - 24) / 2, cell.width - icon - 5, 24), groups[i].Label, Mathf.Clamp(cellWidth / 17, 10, 20), groups[i].Representative.State);
            }
        }
        private static void DrawSettlement(float width, float height, float panelWidth)
        {
            if (CombatFightService.IsArena) { DrawArenaSettlement(width, height, panelWidth); return; }
            var cardWidth = Mathf.Min(width - 32, Mathf.Min(900, Mathf.Max(600, width - panelWidth * 2 - 40)));
            var cardHeight = Mathf.Min(370, height - 230);
            var rect = new Rect((width - cardWidth) / 2, (height - cardHeight) / 2, cardWidth, cardHeight);
            Widgets.DrawBoxSolid(rect, new Color(.025f, .035f, .05f, .94f));
            var font = Mathf.Clamp(cardWidth / 39, 12, 21);
            DrawText(new Rect(rect.x, rect.y + 10, rect.width, 42), "战斗结算", font * 1.6f, TextAnchor.MiddleCenter);
            DrawText(new Rect(rect.x, rect.y + 54, rect.width, 26), snapshot.SimulatedSeconds.ToString("F1") + " 秒 · 3秒后结束", font * .8f, TextAnchor.MiddleCenter);
            for (var i = 0; i < snapshot.Teams.Count; i++)
            {
                var team = snapshot.Teams[i];
                var x = rect.x + 14 + i * cardWidth / 2;
                var w = cardWidth / 2 - 28;
                var previous = GUI.color; GUI.color = team.Outcome == "win" ? NameColor("standing") : team.Outcome == "loss" ? NameColor("dead") : Color.white;
                DrawText(new Rect(x, rect.y + 88, w, 32), TeamLabel(i) + " · " + Label(team.Outcome), font * 1.3f, TextAnchor.MiddleCenter); GUI.color = previous;
                var representative = data.Teams[i].Output.FirstOrDefault()?.Unit;
                if (representative != null) DrawPortrait(new Rect(x + (w - 52) / 2, rect.y + 126, 52, 52), representative);
                var lines = data.Teams[i].Settlement;
                for (var row = 0; row < lines.Length; row++)
                    DrawText(new Rect(x, rect.y + 188 + row * 31, w, 28), lines[row], font, TextAnchor.MiddleCenter);
            }
        }
        private static void DrawArenaSettlement(float width, float height, float panelWidth)
        {
            var cardWidth = Mathf.Min(width - 32, Mathf.Min(900, Mathf.Max(600, width - panelWidth * 2 - 40)));
            var cardHeight = Mathf.Min(562, height - 150);
            var rect = new Rect((width - cardWidth) / 2, (height - cardHeight) / 2, cardWidth, cardHeight);
            var scale = cardHeight / 562;
            var gold = new Color(1f, .79f, .28f);
            var previous = GUI.color;
            try
            {
                Widgets.DrawBoxSolid(rect, new Color(.025f, .035f, .05f, .97f));
                GUI.color = gold; Widgets.DrawBox(rect, 2); GUI.color = previous;
                DrawText(new Rect(rect.x, rect.y + 10 * scale, rect.width, 36 * scale), "擂台战斗结算", 27 * scale, TextAnchor.MiddleCenter);
                var winner = snapshot.Teams.SingleOrDefault(t => t.Outcome == "win");
                DrawText(new Rect(rect.x, rect.y + 48 * scale, rect.width, 32 * scale), winner == null ? "本场平局" : winner.Name + " · 胜利", 22 * scale, TextAnchor.MiddleCenter);
                var score = ArenaService.ScoreFor(snapshot);
                var hero = winner == null ? null : data.Teams.First(t => t.Result.Id == winner.Id).Hero;
                if (hero != null)
                {
                    var icon = new Rect(rect.center.x - 198 * scale, rect.y + 85 * scale, 120 * scale, 120 * scale);
                    if (portraits.ContainsKey(hero.Id)) DrawPortrait(icon, hero);
                    else
                    {
                        var kind = DefDatabase<PawnKindDef>.GetNamedSilentFail(hero.Kind);
                        if (kind?.race != null) Widgets.DefIcon(icon, kind.race);
                    }
                }
                var scoreRect = hero == null ? new Rect(rect.x, rect.y + 81 * scale, rect.width, 92 * scale)
                    : new Rect(rect.center.x - 54 * scale, rect.y + 81 * scale, 280 * scale, 92 * scale);
                GUI.color = new Color(0, 0, 0, .8f);
                DrawText(new Rect(scoreRect.x + 2, scoreRect.y + 3, scoreRect.width, scoreRect.height), score?.Value.ToString("F2") ?? "—", 76 * scale, TextAnchor.MiddleCenter);
                GUI.color = gold;
                DrawText(scoreRect, score?.Value.ToString("F2") ?? "—", 76 * scale, TextAnchor.MiddleCenter);
                DrawText(new Rect(scoreRect.x, rect.y + 173 * scale, scoreRect.width, 26 * scale), score == null ? "平局不计晋级分数" : score.IsRankable ? "胜利质量评分 / 100" : "暂定评分 · 不参与晋级", 18 * scale, TextAnchor.MiddleCenter);
                GUI.color = previous;
                DrawText(new Rect(rect.x, rect.y + 205 * scale, rect.width, 26 * scale), score == null ? "即将展示赛后天梯" : "初始成员站立 " + score.Standing + " / " + score.Initial + " · 倒地 " + score.Downed + "   ·   有效用时 " + (score.EffectiveElapsedTicks / 60d).ToString("F2") + " 秒", 16 * scale, TextAnchor.MiddleCenter);
                if (score != null)
                {
                    var points = new[] { score.PreservationPoints, score.SuppressionPoints, score.ExchangePoints, score.SpeedPoints };
                    var labels = new[] { "己方保全", "敌方压制", "伤害交换", "获胜速度" };
                    for (var i = 0; i < points.Length; i++)
                    {
                        var w = (rect.width - 28) / 4;
                        var item = new Rect(rect.x + 14 + i * w, rect.y + 240 * scale, w - 4, 44 * scale);
                        Widgets.DrawBoxSolid(item, new Color(.08f, .105f, .14f));
                        DrawText(item, labels[i] + "\n" + points[i].ToString("F2"), 15 * scale, TextAnchor.MiddleCenter);
                    }
                }
                for (var i = 0; i < snapshot.Teams.Count; i++)
                {
                    var team = snapshot.Teams[i];
                    var x = rect.x + 14 + i * cardWidth / 2;
                    var w = cardWidth / 2 - 28;
                    GUI.color = team.Outcome == "win" ? gold : Color.white;
                    DrawText(new Rect(x, rect.y + 306 * scale, w, 32 * scale), TeamLabel(i) + " · " + Label(team.Outcome), 22 * scale, TextAnchor.MiddleCenter);
                    GUI.color = previous;
                    var lines = data.Teams[i].Settlement;
                    for (var row = 0; row < lines.Length; row++)
                        DrawText(new Rect(x, rect.y + (348 + row * 30) * scale, w, 28 * scale), lines[row], Mathf.Min(19 * scale, w / 17), TextAnchor.MiddleCenter);
                }
                var footer = score == null ? "赛后天梯将显示重赛结果" : !score.IsRankable ? "伤害统计异常 · 本场暂停晋级" : score.NeutralExchange ? "双方无伤害记录 · 交换项取中性分" : "伤害交换按已记录伤害计算，不含全部流血与身体状态效果";
                DrawText(new Rect(rect.x + 8, rect.y + 524 * scale, rect.width - 16, 26 * scale), footer, 13 * scale, TextAnchor.MiddleCenter);
            }
            finally { GUI.color = previous; }
        }
        private static string Label(string state) => GroupCombatOverlayData.Label(state);
        private static void DrawPortrait(Rect rect, GroupCombatUnit unit)
        {
            if (portraits.TryGetValue(unit.Id, out var texture)) { GUI.DrawTexture(rect, texture); return; }
            if (!CombatFightService.Pawns.TryGetValue(unit.Id, out var pawn)) return;
            if (pawn.Destroyed || pawn.RaceProps.Humanlike) Widgets.DefIcon(rect, pawn.def);
            else Widgets.ThingIcon(rect, pawn);
        }
    }
}
