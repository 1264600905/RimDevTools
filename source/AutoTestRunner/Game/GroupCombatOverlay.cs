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
        private static GroupCombatRoundResult snapshot;
        private static float nextRefresh;
        private static bool verifyInput;
        internal static object InputVerification { get; private set; }
        internal static void RequestInputVerification() { verifyInput = true; InputVerification = null; }
        private static readonly Dictionary<string, string> portraitStates = new Dictionary<string, string>();
        private static readonly Dictionary<string, RenderTexture> portraits = new Dictionary<string, RenderTexture>();
        private static readonly Dictionary<string, GUIStyle> textStyles = new Dictionary<string, GUIStyle>();
        internal static void Clear()
        {
            snapshot = null; nextRefresh = 0; portraitStates.Clear(); verifyInput = false;
            foreach (var texture in portraits.Values) { texture.Release(); UnityEngine.Object.Destroy(texture); }
            portraits.Clear();
        }
        internal static void Draw()
        {
            if (!GroupCombatService.OverlayEnabled || GroupCombatService.Display == null) return;
            if (Time.realtimeSinceStartup >= nextRefresh || GroupCombatService.Phase != "running")
            {
                snapshot = GroupCombatService.Display; nextRefresh = Time.realtimeSinceStartup + .25f;
                // Keep every initial human portrait, including members who enter the Top10
                // only after death or destruction. Native portrait cache entries can expire.
                foreach (var unit in snapshot.Units) CachePortrait(unit);
            }
            if (snapshot == null) return;
            var previousFont = Text.Font; var previousColor = GUI.color; Text.Font = GameFont.Tiny;
            try
            {
                var width = UI.screenWidth; var height = UI.screenHeight;
                var panelWidth = GroupCombatPresentation.PanelWidth(width);
                var panelHeight = GroupCombatPresentation.PanelHeight(height);
                Widgets.DrawWindowBackground(new Rect(8, 34, width - 16, 70));
                DrawText(new Rect(150, 36, width - 300, 38), snapshot.Name, width >= 1280 ? 24 : 18, TextAnchor.MiddleCenter);
                DrawText(new Rect(150, 73, width - 300, 24), snapshot.SimulatedSeconds.ToString("F1") + " 模拟秒  ·  " + (GroupCombatService.Measuring && Find.TickManager.Paused ? "已暂停" : Label(GroupCombatService.Phase)), 16, TextAnchor.MiddleCenter);
                Text.Font = GameFont.Small;
                if (Widgets.ButtonText(new Rect(width - 142, 54, 120, 32), "取消群战")) GroupCombatService.Cancel();
                for (var i = 0; i < snapshot.Teams.Count; i++)
                {
                    var x = i == 0 ? 8 : width - panelWidth - 8;
                    DrawTeam(new Rect(x, GroupCombatPresentation.PanelTop, panelWidth, panelHeight), snapshot.Teams[i], i);
                }
                if (GroupCombatService.ShowingIntro) DrawIntro(width, height, panelWidth);
                if (GroupCombatService.ShowingSettlement) DrawSettlement(width, height, panelWidth);
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
                            DrawTeam(new Rect(x, GroupCombatPresentation.PanelTop, panelWidth, panelHeight), snapshot.Teams[i], i);
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
        private static void DrawText(Rect rect, string value, float size, TextAnchor anchor = TextAnchor.UpperLeft)
        {
            var fontSize = Mathf.RoundToInt(size);
            var key = fontSize + ":" + (int)anchor;
            if (!textStyles.TryGetValue(key, out var style))
            {
                // Clone the native dynamic font; do not mutate global RimWorld text styles.
                style = new GUIStyle(Text.fontStyles[1]) { fontSize = fontSize, alignment = anchor, wordWrap = false, clipping = TextClipping.Clip };
                textStyles.Add(key, style);
            }
            GUI.Label(rect, value, style);
        }
        private static void DrawTeam(Rect rect, GroupCombatTeamResult team, int index)
        {
            Widgets.DrawBoxSolid(rect, new Color(.055f, .075f, .09f, .62f));
            var previousColor = GUI.color; GUI.color = new Color(.65f, .72f, .8f, .5f); Widgets.DrawBox(rect); GUI.color = previousColor;
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, rect.width, 3), TeamColor(index));
            // Pure drawing only: no Window, scroll view, tooltip or invisible button may
            // capture pointer events. Both rankings fit in parallel columns at every size.
            var members = snapshot.Units.Where(u => u.Team == team.Id).ToList();
            var scale = GroupCombatPresentation.ContentScale(rect.width, rect.height);
            var padding = 7 * scale;
            GUI.BeginGroup(new Rect(rect.x + padding, rect.y + padding, rect.width - padding * 2, rect.height - padding * 2));
            try
            {
                var width = rect.width - padding * 2; var height = rect.height - padding * 2;
                GUI.color = TeamColor(index); DrawText(new Rect(0, 0, width, 23 * scale), TeamLabel(index), 18 * scale); GUI.color = previousColor;
                DrawText(new Rect(0, 24 * scale, width, 20 * scale), team.Name, 12 * scale);
                DrawText(new Rect(0, 44 * scale, width, 20 * scale), "存活 " + team.Alive + "/" + team.Total + "   倒地 " + team.Downed + "   死亡 " + team.Dead, 12 * scale);
                var groups = members.Where(u => u.Alive).GroupBy(u => u.Kind + "|" + u.Group).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).ToList();
                var y = 66 * scale;
                if (team.Joined > 0)
                {
                    DrawText(new Rect(0, y, width, 20 * scale), "初始 " + team.Initial + "   新增 " + team.Joined + "   待复活 " + team.PendingRevival, 12 * scale); y += 21 * scale;
                }
                foreach (var group in groups.Take(5))
                {
                    var representative = group.First(); DrawPortrait(new Rect(0, y, 20 * scale, 20 * scale), representative);
                    DrawName(new Rect(24 * scale, y, width - 24 * scale, 20 * scale), (representative.KindLabel ?? representative.Kind) + " ×" + group.Count(), 12 * scale, representative.State); y += 21 * scale;
                }
                if (groups.Count > 5 || groups.Count == 0)
                {
                    DrawText(new Rect(0, y, width, 20 * scale), groups.Count > 5 ? "其余 " + (groups.Count - 5) + " 种（完整阵容见结果）" : "零存活", 12 * scale); y += 21 * scale;
                }
                y += 8 * scale;
                var rankWidth = (width - 8 * scale) / 2;
                var rowHeight = Mathf.Min(39 * scale, (height - y - 86 * scale) / 10);
                Ranking(0, rankWidth, y, rowHeight, scale, "累计输出 Top10", members, u => u.EnemyDamage, new Color(.25f, .65f, .9f), snapshot.Units.Max(u => u.EnemyDamage));
                Ranking(rankWidth + 8 * scale, rankWidth, y, rowHeight, scale, "承伤 Top10", members, u => u.DamageTaken, new Color(.9f, .45f, .25f), snapshot.Units.Max(u => u.DamageTaken));
                var footer = height - 60 * scale;
                DrawText(new Rect(0, footer, width, 20 * scale), "平均输出 " + team.MeanOutput.ToString("F1") + "   存活人均承伤 " + team.MeanDamageTaken.ToString("F1"), 12 * scale);
                DrawText(new Rect(0, footer + 20 * scale, width, 20 * scale), "累计输出 " + team.EnemyDamage.ToString("F1") + "   总承伤 " + team.DamageTaken.ToString("F1"), 12 * scale);
                DrawText(new Rect(0, footer + 40 * scale, width, 20 * scale), Label(team.Outcome) + "  ·  排名按存活期间累计伤害", 11 * scale);
            }
            finally { GUI.EndGroup(); }
        }
        private static void Ranking(float x, float width, float y, float rowHeight, float uiScale, string label, List<GroupCombatUnit> members, Func<GroupCombatUnit, double> value, Color color, double maximum)
        {
            DrawText(new Rect(x, y, width, 22 * uiScale), label, 12 * uiScale); y += 24 * uiScale;
            var rank = 0;
            foreach (var unit in members.OrderByDescending(value).ThenBy(u => u.Id, StringComparer.Ordinal).Take(10))
            {
                var icon = Mathf.Min(25 * uiScale, rowHeight - 2);
                DrawPortrait(new Rect(x, y, icon, icon), unit);
                var bar = new Rect(x + icon + 2, y, width - icon - 2, rowHeight - 1);
                Widgets.DrawBoxSolid(bar, new Color(.1f, .1f, .1f, .25f));
                Widgets.DrawBoxSolid(new Rect(bar.x, bar.y, (float)(maximum > 0 ? value(unit) / maximum : 0) * bar.width, bar.height), color * .6f);
                var name = (++rank) + "." + unit.Name;
                if (rowHeight >= 32 * uiScale)
                {
                    DrawName(new Rect(bar.x, y, bar.width, 19 * uiScale), name, 12 * uiScale, unit.State);
                    DrawText(new Rect(bar.x, y + 17 * uiScale, bar.width, rowHeight - 17 * uiScale), value(unit).ToString("F1") + " " + Label(unit.State), 12 * uiScale);
                }
                else
                {
                    // Compact screens keep every rank visible on one line.
                    DrawName(new Rect(bar.x, y, bar.width, rowHeight), rank + "." + unit.Name.Substring(0, Math.Min(3, unit.Name.Length)) + " " + value(unit).ToString("F0"), Mathf.Min(12 * uiScale, rowHeight - 2), unit.State);
                }
                y += rowHeight;
            }
        }
        private static void DrawIntro(float width, float height, float panelWidth)
        {
            var label = GroupCombatService.CountdownLabel;
            var size = label == "开始" ? 88 : 138;
            var center = new Rect(width / 2 - 180, height / 2 - 110, 360, 200);
            var previousColor = GUI.color;
            GUI.color = new Color(0, 0, 0, .8f); DrawText(new Rect(center.x + 4, center.y + 4, center.width, center.height), label, size, TextAnchor.MiddleCenter);
            GUI.color = label == "开始" ? new Color(1f, .8f, .3f) : Color.white; DrawText(center, label, size, TextAnchor.MiddleCenter); GUI.color = previousColor;
            var availableWidth = width - panelWidth * 2 - 48;
            var rosterWidth = Mathf.Min(960, availableWidth);
            var rosterHeight = Mathf.Min(176, height / 2 - 210);
            var rosterTop = height / 2 + 100;
            DrawText(new Rect((width - rosterWidth) / 2, rosterTop - 32, rosterWidth, 28), "双方阵容", 20, TextAnchor.MiddleCenter);
            for (var i = 0; i < snapshot.Teams.Count; i++)
            {
                var rect = new Rect((width - rosterWidth) / 2 + i * (rosterWidth + 12) / 2, rosterTop, (rosterWidth - 12) / 2, rosterHeight);
                DrawIntroRoster(rect, snapshot.Teams[i], i);
            }
        }
        private static void DrawIntroRoster(Rect rect, GroupCombatTeamResult team, int index)
        {
            Widgets.DrawBoxSolid(rect, new Color(.035f, .05f, .07f, .85f));
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, rect.width, 3), TeamColor(index));
            var previousColor = GUI.color; GUI.color = TeamColor(index);
            DrawText(new Rect(rect.x + 8, rect.y + 8, rect.width - 16, 28), TeamLabel(index) + "  ·  " + team.Initial + " 名参战", Mathf.Min(20, rect.width / 10), TextAnchor.MiddleCenter); GUI.color = previousColor;
            DrawText(new Rect(rect.x + 8, rect.y + 36, rect.width - 16, 24), team.Name, Mathf.Min(16, rect.width / 16), TextAnchor.MiddleCenter);
            var groups = snapshot.Units.Where(u => u.Team == team.Id).GroupBy(u => u.Kind + "|" + u.Group).ToList();
            if (groups.Count == 0) return;
            var columns = Mathf.Min(groups.Count, Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(groups.Count * rect.width / (rect.height * 2)))));
            var rows = Mathf.CeilToInt((float)groups.Count / columns);
            var cellWidth = (rect.width - 16) / columns; var cellHeight = (rect.height - 66) / rows;
            for (var i = 0; i < groups.Count; i++)
            {
                var cell = new Rect(rect.x + 8 + i % columns * cellWidth, rect.y + 64 + i / columns * cellHeight, cellWidth, cellHeight);
                var icon = Mathf.Min(56, Mathf.Min(cell.height - 4, cell.width * .3f));
                DrawPortrait(new Rect(cell.x, cell.y + (cell.height - icon) / 2, icon, icon), groups[i].First());
                DrawName(new Rect(cell.x + icon + 5, cell.y + (cell.height - 24) / 2, cell.width - icon - 5, 24), (groups[i].First().KindLabel ?? groups[i].First().Kind) + " ×" + groups[i].Count(), Mathf.Clamp(cellWidth / 17, 10, 20), groups[i].First().State);
            }
        }
        private static void DrawSettlement(float width, float height, float panelWidth)
        {
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
                var members = snapshot.Units.Where(u => u.Team == team.Id).ToList();
                var living = GroupCombatPresentation.SurvivorTotals(members);
                var x = rect.x + 14 + i * cardWidth / 2;
                var w = cardWidth / 2 - 28;
                var previous = GUI.color; GUI.color = team.Outcome == "win" ? NameColor("standing") : team.Outcome == "loss" ? NameColor("dead") : Color.white;
                DrawText(new Rect(x, rect.y + 88, w, 32), TeamLabel(i) + " · " + Label(team.Outcome), font * 1.3f, TextAnchor.MiddleCenter); GUI.color = previous;
                var representative = members.OrderByDescending(u => u.EnemyDamage).FirstOrDefault();
                if (representative != null) DrawPortrait(new Rect(x + (w - 52) / 2, rect.y + 126, 52, 52), representative);
                var lines = new[] {
                    "存活 " + team.Alive + "/" + team.Total + " · 倒地 " + team.Downed + " · 死亡 " + team.Dead,
                    "全队累计输出  " + team.EnemyDamage.ToString("F1"),
                    "全队累计承伤  " + team.DamageTaken.ToString("F1"),
                    "存活成员输出  " + living.output.ToString("F1"),
                    "存活成员承伤  " + living.taken.ToString("F1")
                };
                for (var row = 0; row < lines.Length; row++)
                    DrawText(new Rect(x, rect.y + 188 + row * 31, w, 28), lines[row], font, TextAnchor.MiddleCenter);
            }
        }
        private static string Label(string state) => state switch { "standing" => "存活", "downed" => "倒地", "dead" => "死亡", "escaped" => "离场", "removed" => "移除", "win" => "胜利", "loss" => "落败", "draw" => "平局", "undetermined" => "进行中", "prepared" or "preparing" => "准备", "countdown" => "即将开战", "running" => "战斗", "finishing" => "结算", _ => state };
        private static void DrawPortrait(Rect rect, GroupCombatUnit unit)
        {
            if (portraits.TryGetValue(unit.Id, out var texture)) { GUI.DrawTexture(rect, texture); return; }
            if (!GroupCombatService.Pawns.TryGetValue(unit.Id, out var pawn)) return;
            if (!pawn.RaceProps.Humanlike)
            {
                if (pawn.Destroyed) Widgets.DefIcon(rect, pawn.def); else Widgets.ThingIcon(rect, pawn);
            }
        }
        private static void CachePortrait(GroupCombatUnit unit)
        {
            if (!GroupCombatService.Pawns.TryGetValue(unit.Id, out var pawn) || pawn.Destroyed || !pawn.RaceProps.Humanlike) return;
            var state = pawn.story?.hairDef?.defName + "|" + pawn.story?.bodyType?.defName + "|" + string.Join(",", pawn.apparel?.WornApparel.Select(a => a.GetUniqueLoadID() + ":" + a.DrawColor) ?? Enumerable.Empty<string>()) + "|" + pawn.equipment?.Primary?.GetUniqueLoadID() + "|" + pawn.health.hediffSet.hediffs.Count;
            if (portraitStates.TryGetValue(unit.Id, out var old) && old == state) return;
            PortraitsCache.SetDirty(pawn);
            var source = PortraitsCache.Get(pawn, new Vector2(64, 64), Rot4.South, cameraZoom: 1.1f);
            if (!portraits.TryGetValue(unit.Id, out var texture))
            {
                texture = new RenderTexture(64, 64, 0); texture.Create(); portraits.Add(unit.Id, texture);
            }
            Graphics.Blit(source, texture); portraitStates[unit.Id] = state;
        }
    }
}
