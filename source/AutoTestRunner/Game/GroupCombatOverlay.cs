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
                var panelHeight = Mathf.Min(height - 158, 650);
                Widgets.DrawWindowBackground(new Rect(8, 34, width - 16, 44));
                Widgets.Label(new Rect(18, 38, width - 170, 38), snapshot.Name + "  |  " + snapshot.TestDefName + "  |  " + snapshot.SimulatedSeconds.ToString("F1") + " 模拟秒  |  " + (GroupCombatService.Measuring && Find.TickManager.Paused ? "已暂停" : Label(GroupCombatService.Phase)));
                if (Widgets.ButtonText(new Rect(width - 142, 42, 120, 28), "取消群战")) GroupCombatService.Cancel();
                for (var i = 0; i < snapshot.Teams.Count; i++)
                {
                    var x = i == 0 ? 8 : width - panelWidth - 8;
                    var y = 88;
                    DrawTeam(new Rect(x, y, panelWidth, panelHeight), snapshot.Teams[i]);
                }
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
                            DrawTeam(new Rect(x, 88, panelWidth, panelHeight), snapshot.Teams[i]);
                            passed &= Event.current.type == kind;
                        }
                        InputVerification = new { passed, checks = 6, scrollViews = 0 };
                    }
                    finally { Event.current = original; }
                }
            }
            finally { Text.Font = previousFont; GUI.color = previousColor; }
        }
        private static void DrawTeam(Rect rect, GroupCombatTeamResult team)
        {
            Widgets.DrawBoxSolid(rect, new Color(.055f, .075f, .09f, .62f));
            var previousColor = GUI.color; GUI.color = new Color(.65f, .72f, .8f, .5f); Widgets.DrawBox(rect); GUI.color = previousColor;
            // Pure drawing only: no Window, scroll view, tooltip or invisible button may
            // capture pointer events. Both rankings fit in parallel columns at every size.
            var members = snapshot.Units.Where(u => u.Team == team.Id).ToList();
            GUI.BeginGroup(new Rect(rect.x + 7, rect.y + 6, rect.width - 14, rect.height - 12));
            try
            {
                var width = rect.width - 14; var height = rect.height - 12;
                Widgets.Label(new Rect(0, 0, width, 24), team.Name + "  存活 " + team.Alive + "/" + team.Initial + "  倒地 " + team.Downed);
                var groups = members.Where(u => u.Alive).GroupBy(u => u.Kind + "|" + u.Group).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).ToList();
                var y = 25f;
                foreach (var group in groups.Take(5))
                {
                    var representative = group.First(); DrawPortrait(new Rect(0, y, 20, 20), representative);
                    Widgets.Label(new Rect(24, y, width - 24, 20), (representative.KindLabel ?? representative.Kind) + " / " + representative.GroupName + " ×" + group.Count()); y += 21;
                }
                Widgets.Label(new Rect(0, y, width, 20), groups.Count > 5 ? "其余 " + (groups.Count - 5) + " 种（完整阵容见结果）" : groups.Count == 0 ? "零存活" : ""); y += 21;
                var rankWidth = (width - 6) / 2;
                var rowHeight = Mathf.Min(39, (height - y - 78) / 10);
                Ranking(0, rankWidth, y, rowHeight, "输出 Top10", members, u => u.EnemyDamage, new Color(.25f, .65f, .9f), snapshot.Units.Max(u => u.EnemyDamage));
                Ranking(rankWidth + 6, rankWidth, y, rowHeight, "承伤 Top10", members, u => u.DamageTaken, new Color(.9f, .45f, .25f), snapshot.Units.Max(u => u.DamageTaken));
                Widgets.Label(new Rect(0, height - 54, width, 54), "存活人均 DPS " + team.MeanDps.ToString("F2") + "  承伤 " + team.MeanDamageTaken.ToString("F1") + "\n团队 DPS " + team.Dps.ToString("F2") + "  总承伤 " + team.DamageTaken.ToString("F1") + "\n" + Label(team.Outcome) + "  ·  个人 DPS 按存活时间");
            }
            finally { GUI.EndGroup(); }
        }
        private static void Ranking(float x, float width, float y, float rowHeight, string label, List<GroupCombatUnit> members, Func<GroupCombatUnit, double> value, Color color, double scale)
        {
            Widgets.Label(new Rect(x, y, width, 22), label); y += 24;
            var rank = 0;
            foreach (var unit in members.OrderByDescending(value).ThenBy(u => u.Id, StringComparer.Ordinal).Take(10))
            {
                var icon = Mathf.Min(25, rowHeight - 2);
                DrawPortrait(new Rect(x, y, icon, icon), unit);
                var bar = new Rect(x + icon + 2, y, width - icon - 2, rowHeight - 1);
                Widgets.DrawBoxSolid(bar, new Color(.1f, .1f, .1f, .25f));
                Widgets.DrawBoxSolid(new Rect(bar.x, bar.y, (float)(scale > 0 ? value(unit) / scale : 0) * bar.width, bar.height), color * .6f);
                var name = (++rank) + "." + unit.Name;
                if (rowHeight >= 30)
                {
                    Widgets.Label(new Rect(bar.x, y - 2, bar.width, 18), name);
                    Widgets.Label(new Rect(bar.x, y + 13, bar.width, rowHeight - 12), value(unit).ToString("F1") + " " + Label(unit.State));
                }
                else
                {
                    // Compact screens keep every rank visible on one line.
                    Widgets.Label(new Rect(bar.x, y, bar.width, rowHeight), rank + "." + unit.Name.Substring(0, Math.Min(3, unit.Name.Length)) + " " + value(unit).ToString("F0"));
                }
                y += rowHeight;
            }
        }
        private static string Label(string state) => state switch { "standing" => "存活", "downed" => "倒地", "dead" => "死亡", "escaped" => "离场", "removed" => "移除", "win" => "胜利", "loss" => "落败", "draw" => "平局", "undetermined" => "进行中", "prepared" or "preparing" => "准备", "running" => "战斗", "finishing" => "结算", _ => state };
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
