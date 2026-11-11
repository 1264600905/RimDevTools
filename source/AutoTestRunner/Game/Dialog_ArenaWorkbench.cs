using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using LogSyncMod.AutoTest.Scenarios;
using RimWorld;
using UnityEngine;
using Verse;

namespace LogSyncMod.AutoTest
{
    internal sealed partial class Dialog_ArenaWorkbench : Window
    {
        private readonly ArenaWorkbenchDocument document;
        private readonly Action<ArenaWorkbenchDocument> saveEdits;
        private readonly string editScope;
        private int page, teamIndex, memberIndex, libraryPage;
        private Vector2 scheduleScroll, rosterScroll, editorScroll, libraryScroll;
        private string notice, validation;
        private readonly Dictionary<string, string> numberBuffers = new();
        private readonly HashSet<string> invalidNumbers = new();
        private float scheduleContentHeight = 1590;
        private float rosterViewportHeight = 350;
        private readonly Color gold = ArenaWorkbenchUi.Gold, muted = ArenaWorkbenchUi.Muted;
        private ArenaWorkbenchTeam Team => document.Teams.ElementAtOrDefault(teamIndex);
        private ArenaWorkbenchMember Member => Team?.Members.ElementAtOrDefault(memberIndex);
        private bool Executing => ArenaService.Busy || GroupCombatService.Busy || CombatFightService.Busy || ManualTestService.Busy;
        protected override float Margin => 12;
        public override Vector2 InitialSize => new(UI.screenWidth - 12, UI.screenHeight - 12);
        internal Dialog_ArenaWorkbench(ArenaWorkbenchDocument value, string editScope = null, int initialPage = 0, Action<ArenaWorkbenchDocument> saveEdits = null)
        {
            document = value; this.editScope = editScope; this.saveEdits = saveEdits; page = initialPage; doCloseX = true; closeOnAccept = false; absorbInputAroundWindow = true;
            forcePause = !Executing; resizeable = false; draggable = false;
        }
        public override void PostClose()
        {
            DisposePreview();
            try { if (saveEdits == null) ArenaWorkbenchStorage.Save(document, false); }
            catch (Exception ex) { Log.Warning("[LogSyncMod] 群战工作台配置保存失败：" + ex.Message); }
            base.PostClose();
        }
        private void Protect(Action action)
        {
            try { action(); }
            catch (Exception ex) { notice = ex.Message; Log.Warning("[LogSyncMod] 群战工作台：" + ex.Message); }
        }
        private void Changed() { validation = null; }
        private void SelectTeam(int index) { teamIndex = index; memberIndex = 0; rosterScroll = Vector2.zero; numberBuffers.Clear(); }
        private void SelectMember(int index)
        {
            memberIndex = index; numberBuffers.Clear();
            if (index * 82 < rosterScroll.y) rosterScroll.y = index * 82;
            else if (index * 82 + 74 > rosterScroll.y + rosterViewportHeight) rosterScroll.y = Math.Max(0, index * 82 + 74 - rosterViewportHeight);
        }
        private void Label(Rect rect, string text, Color? color = null, GameFont font = GameFont.Small, TextAnchor anchor = TextAnchor.MiddleLeft)
            => ArenaWorkbenchUi.Label(rect, text, color, font, anchor);
        private bool Button(Rect rect, string text, bool selected = false, bool active = true)
            => ArenaWorkbenchUi.Button(rect, text, selected, active);
        private void CopyXml(string xml)
        { GUIUtility.systemCopyBuffer = xml; notice = "XML 已复制到剪贴板，可直接交给 AI。"; }
        private void CopyAll() => Protect(() => CopyXml(ArenaWorkbench.Export(document).ToString()));
        public override void DoWindowContents(Rect rect)
        {
            DrainChanges();
            invalidNumbers.Clear();
            portraitToggleDrawn = false;
            forcePause = !Executing;
            if (teamIndex >= document.Teams.Count) teamIndex = Math.Max(0, document.Teams.Count - 1);
            if (Team != null && memberIndex >= Team.Members.Count) memberIndex = Math.Max(0, Team.Members.Count - 1);
            var font = Text.Font; var anchor = Text.Anchor; var color = GUI.color;
            try
            {
                ArenaWorkbenchUi.Box(rect, ArenaWorkbenchUi.Background);
                Widgets.DrawBoxSolid(new Rect(0, 0, rect.width, 2), gold);
                Label(new Rect(6, 0, 390, 38), editScope == null ? "LogSyncMod · 群战工作台" : "编辑“" + editScope + "” · 独立草稿", gold, GameFont.Medium);
                if (Button(new Rect(rect.width - 340, 3, 145, 30), "复制完整 XML")) CopyAll();
                if (Button(new Rect(rect.width - 185, 3, 145, 30), saveEdits == null ? "保存工作台" : "校验并保存修改")) { if (saveEdits == null) Protect(() => { ArenaWorkbenchStorage.Save(document); notice = "工作台、编队与预设已保存。"; }); else SaveDraft(); }
                var names = new[] { "赛程设置", "编队管理", "角色编辑", "模板预设" };
                for (var i = 0; i < names.Length; i++)
                    if (Button(new Rect(i * 155, 45, 149, 32), names[i], page == i, saveEdits == null || (editScope == "角色模板" ? i == 2 : editScope is "编队模板" or "赛事参赛编队" ? i is 1 or 2 : i < 3))) { page = i; numberBuffers.Clear(); }
                var body = new Rect(0, 88, rect.width, Math.Max(80, rect.height - 176));
                var enabled = GUI.enabled; GUI.enabled &= !Executing;
                try
                {
                    if (page == 0) DrawSchedule(body);
                    else if (page == 1) DrawRoster(body);
                    else if (page == 2) DrawEditor(body);
                    else DrawLibrary(body);
                }
                finally { GUI.enabled = enabled; }
                DrawFooter(new Rect(0, rect.height - 78, rect.width, 78));
                if (!Executing) portraits.ProcessOne();
            }
            finally { Text.Font = font; Text.Anchor = anchor; GUI.color = color; }
        }
        private void DrawFooter(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(1, 1, 1, .04f));
            var teams = document.Teams.Where(t => t.Enabled && !t.IsEmpty).ToList();
            var bosses = teams.Count(t => t.Role == "boss");
            Label(new Rect(12, rect.y + 4, rect.width - 470, 25), document.Schedule.Format == "groupcombat" ? "群战测试 · 两支编队直接对战" : document.Schedule.Format == "knockout" ? teams.Count + " 支队伍 · " + Math.Max(0, teams.Count - 1) + " 场淘汰对决（不含重赛）"
                : bosses + " 支魔王 × " + (teams.Count - bosses) + " 支挑战者 · 每场重新满编", gold);
            Label(new Rect(12, rect.y + 31, rect.width - 24, 40), notice ?? validation ?? "自动更新角色预览 · 空编队不计入赛事", muted, GameFont.Tiny);
            if (saveEdits != null)
            {
                if (Button(new Rect(rect.width - 330, rect.y + 6, 140, 31), "取消修改")) Close();
                if (Button(new Rect(rect.width - 180, rect.y + 6, 165, 31), "校验并保存修改", true, invalidNumbers.Count == 0)) SaveDraft();
                return;
            }
            if (Executing)
            {
                if (ArenaService.Busy || GroupCombatService.Busy)
                { if (Button(new Rect(rect.width - 195, rect.y + 6, 180, 31), "取消当前赛事")) { if (ArenaService.Busy) ArenaService.Cancel(); else GroupCombatService.Cancel(); } }
                else Label(new Rect(rect.width - 365, rect.y + 6, 350, 31), "其他测试正在运行，结束后可配置赛事。", muted);
            }
            else
            {
                if (!portraitToggleDrawn) HeadgearToggle(new Rect(rect.width - 535, rect.y + 6, 154, 31));
                if (Button(new Rect(rect.width - 365, rect.y + 6, 160, 31), "检查配置", active: invalidNumbers.Count == 0)) Protect(() => { NormalizeApparel(); ValidateCurrentEvent(); validation = "XML 结构与当前已加载的定义校验通过。"; notice = null; });
                if (Button(new Rect(rect.width - 195, rect.y + 6, 180, 31), "开始赛事", true, invalidNumbers.Count == 0)) Start();
                if (invalidNumbers.Count > 0) Label(new Rect(12, rect.y + 31, rect.width - 24, 40), "请修正红线标记的数字输入：" + string.Join("、", invalidNumbers), gold, GameFont.Tiny);
            }
        }
        private void SaveDraft()
        {
            Protect(() => { if (invalidNumbers.Count > 0) throw new InvalidOperationException("请修正数字输入。"); NormalizeApparel(); ArenaWorkbench.ValidateStructure(document); saveEdits(document); Close(); });
        }
        private sealed class Form
        {
            internal float Y;
            private readonly Dialog_ArenaWorkbench owner;
            private readonly float x, width;
            internal float X => x;
            internal float Width => width;
            internal Form(Dialog_ArenaWorkbench owner, float x, float width, float y = 8) { this.owner = owner; this.x = x; this.width = width; Y = y; }
            internal void Title(string label) { owner.Label(new Rect(x, Y + 8, width, 29), label, owner.gold); Y += 43; }
            internal void Note(string label, float height = 38) { var old = Text.Font; Text.Font = GameFont.Tiny; Widgets.Label(new Rect(x, Y, width, height), label); Text.Font = old; Y += height + 6; }
            private Rect Row(string label)
            { owner.Label(new Rect(x, Y, width * .49f - 6, 30), label, owner.muted); var r = new Rect(x + width * .49f, Y, width * .51f, 28); Y += 35; return r; }
            internal string TextField(string label, string value) => Widgets.TextField(Row(label), value ?? "");
            internal double Number(string key, string label, double value, double min, double max, bool integer = false)
            {
                var rect = Row(label);
                if (!owner.numberBuffers.TryGetValue(key, out var text)) text = value.ToString(CultureInfo.InvariantCulture);
                var next = Widgets.TextField(rect, text);
                owner.numberBuffers[key] = next;
                if (!double.TryParse(next, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) || !double.IsFinite(n) || n < min || n > max || integer && n != Math.Truncate(n))
                { owner.invalidNumbers.Add(label); Widgets.DrawBoxSolid(new Rect(rect.x, rect.yMax - 2, rect.width, 2), new Color(.9f, .35f, .2f)); return value; }
                if (n != value) owner.Changed(); return n;
            }
            internal string Choice(string label, string value, params string[] choices)
            {
                var key = owner.page + ":" + x + ":" + Y + ":" + label;
                var rect = Row(label); var result = value;
                var selected = Array.FindIndex(choices, s => s.Split('|')[0] == value);
                if (owner.Button(rect, selected < 0 ? value ?? "原生 / 不覆盖" : choices[selected].Split('|').Last()))
                    Find.WindowStack.Add(new FloatMenu(choices.Select(item => { var parts = item.Split('|'); return new FloatMenuOption(parts.Last(), () => { owner.pendingChanges.Enqueue(() => { owner.choiceResults[key] = parts[0]; owner.Changed(); }); }); }).ToList()));
                if (owner.choiceResults.Remove(key, out var choice)) result = choice;
                return result;
            }
            internal bool Check(string label, bool value)
            {
                Widgets.Checkbox(new Vector2(x, Y + 5), ref value, 19);
                owner.Label(new Rect(x + 30, Y, width - 30, 29), label, owner.muted);
                Y += 35; return value;
            }
            internal bool Action(string text, bool selected = false) { var result = owner.Button(new Rect(x, Y, width, 28), text, selected); Y += 35; return result; }
        }
        private readonly Dictionary<string, string> choiceResults = new();
        private readonly Queue<Action> pendingChanges = new();
        private void DrainChanges() { while (pendingChanges.Count > 0) pendingChanges.Dequeue()(); }

    }
}
