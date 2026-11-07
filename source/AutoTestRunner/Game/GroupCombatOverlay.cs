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
        private static readonly Dictionary<string, Vector2> scroll = new Dictionary<string, Vector2>();
        private static readonly Dictionary<string, string> portraitStates = new Dictionary<string, string>();
        private static readonly Dictionary<string, RenderTexture> portraits = new Dictionary<string, RenderTexture>();
        internal static void Clear()
        {
            snapshot = null; nextRefresh = 0; scroll.Clear(); portraitStates.Clear();
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
                var panelWidth = Mathf.Clamp((width - 28) / 3.3f, 220, 440);
                var panelHeight = height - 150;
                Widgets.DrawWindowBackground(new Rect(8, 34, width - 16, 44));
                Widgets.Label(new Rect(18, 38, width - 170, 38), snapshot.Name + "  |  " + snapshot.TestDefName + "  |  " + snapshot.SimulatedSeconds.ToString("F1") + " 模拟秒  |  " + Label(GroupCombatService.Phase));
                if (Widgets.ButtonText(new Rect(width - 142, 42, 120, 28), "取消群战")) GroupCombatService.Cancel();
                for (var i = 0; i < snapshot.Teams.Count; i++)
                {
                    var x = i == 0 ? 8 : width - panelWidth - 8;
                    var y = 88;
                    DrawTeam(new Rect(x, y, panelWidth, panelHeight), snapshot.Teams[i]);
                }
            }
            finally { Text.Font = previousFont; GUI.color = previousColor; }
        }
        private static void DrawTeam(Rect rect, GroupCombatTeamResult team)
        {
            Widgets.DrawBoxSolid(rect, new Color(.055f, .075f, .09f, .62f));
            var previousColor = GUI.color; GUI.color = new Color(.65f, .72f, .8f, .5f); Widgets.DrawBox(rect); GUI.color = previousColor;
            var viewport = new Rect(rect.x + 8, rect.y + 8, rect.width - 16, rect.height - 16);
            var content = new Rect(0, 0, viewport.width - 18, 970);
            if (!scroll.TryGetValue(team.Id, out var position)) position = Vector2.zero;
            Widgets.BeginScrollView(viewport, ref position, content); scroll[team.Id] = position;
            var members = snapshot.Units.Where(u => u.Team == team.Id).ToList();
            var y = 0f; Widgets.Label(new Rect(0, y, content.width, 25), team.Name + "   存活 " + team.Alive + "/" + team.Initial + "  倒地 " + team.Downed); y += 27;
            var groups = members.Where(u => u.Alive).GroupBy(u => u.Kind + "|" + u.Group).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).ToList();
            foreach (var group in groups.Take(5))
            {
                var representative = group.First(); DrawPortrait(new Rect(0, y, 32, 32), representative);
                Widgets.Label(new Rect(36, y + 4, content.width - 36, 28), (representative.KindLabel ?? representative.Kind) + " / " + representative.GroupName + " X" + group.Count()); y += 34;
            }
            if (groups.Count > 5) { Widgets.Label(new Rect(0, y, content.width, 22), "其余 " + (groups.Count - 5) + " 种（完整阵容见结果）"); y += 23; }
            if (groups.Count == 0) { Widgets.Label(new Rect(0, y, content.width, 22), "零存活"); y += 23; }
            y = Ranking(content.width, y, "输出 Top10（对敌引擎结算伤害）", members, u => u.EnemyDamage, new Color(.25f, .65f, .9f), snapshot.Units.Max(u => u.EnemyDamage));
            y = Ranking(content.width, y + 8, "承伤 Top10（Pawn 引擎结算伤害）", members, u => u.DamageTaken, new Color(.9f, .45f, .25f), snapshot.Units.Max(u => u.DamageTaken));
            Widgets.Label(new Rect(0, y + 10, content.width, 80), "人均 DPS " + (snapshot.ElapsedTicks == 0 ? "未开始" : team.MeanDps.ToString("F2")) + "\n总承伤 " + team.DamageTaken.ToString("F1") + "  人均承伤 " + team.MeanDamageTaken.ToString("F1") + "\n团队 DPS " + team.Dps.ToString("F2") + "  " + Label(team.Outcome));
            Widgets.EndScrollView();
        }
        private static float Ranking(float width, float y, string label, List<GroupCombatUnit> members, Func<GroupCombatUnit, double> value, Color color, double scale)
        {
            Widgets.Label(new Rect(0, y, width, 23), label); y += 25;
            var rank = 0;
            foreach (var unit in members.OrderByDescending(value).ThenBy(u => u.Id, StringComparer.Ordinal).Take(10))
            {
                DrawPortrait(new Rect(0, y, 28, 28), unit);
                var bar = new Rect(31, y + 1, width - 31, 26);
                Widgets.DrawBoxSolid(bar, new Color(.1f, .1f, .1f, .25f));
                Widgets.DrawBoxSolid(new Rect(bar.x, bar.y, (float)(scale > 0 ? value(unit) / scale : 0) * bar.width, bar.height), color * .6f);
                Widgets.Label(bar, (++rank) + ". " + unit.Name + " " + value(unit).ToString("F1") + " [" + Label(unit.State) + "]"); y += 29;
            }
            return y;
        }
        private static string Label(string state) => state switch { "standing" => "站立", "downed" => "倒地", "dead" => "阵亡", "escaped" => "离场", "removed" => "移除", "win" => "胜利", "loss" => "落败", "draw" => "平局", "undetermined" => "进行中", "prepared" or "preparing" => "准备", "running" => "战斗", "finishing" => "结算", _ => state };
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
