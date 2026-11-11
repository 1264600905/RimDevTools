using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace LogSyncMod.AutoTest
{
    internal sealed class Dialog_ArenaPresetPicker : Window
    {
        private readonly List<string> names;
        private readonly List<ArenaWorkbenchMember> pawns;
        private readonly List<string> details;
        private readonly List<bool> enabled;
        private readonly Action<List<int>> apply;
        private readonly bool multiple;
        private readonly string title;
        private readonly HashSet<int> selected = new();
        private readonly ArenaWorkbenchPortraits portraits = new();
        private string search = "", error;
        private Vector2 scroll;
        public override Vector2 InitialSize => new(Math.Min(1150, UI.screenWidth - 24), Math.Min(820, UI.screenHeight - 24));
        internal Dialog_ArenaPresetPicker(string title, List<string> names, List<ArenaWorkbenchMember> pawns, List<string> details, List<bool> enabled, bool multiple, Action<List<int>> apply)
        {
            this.names = names; this.pawns = pawns; this.details = details; this.enabled = enabled; this.multiple = multiple; this.apply = apply;
            this.title = title; doCloseX = true; forcePause = true; absorbInputAroundWindow = true; closeOnAccept = false;
        }
        public override void PostClose() { portraits.Dispose(); base.PostClose(); }
        public override void DoWindowContents(Rect rect)
        {
            ArenaWorkbenchUi.Label(new Rect(0, 0, rect.width - 35, 30), title, ArenaWorkbenchUi.Gold);
            search = Widgets.TextField(new Rect(0, 42, rect.width, 32), search);
            var items = Enumerable.Range(0, names.Count).Where(i => names[i].IndexOf(search, StringComparison.CurrentCultureIgnoreCase) >= 0).ToList();
            var list = new Rect(0, 88, rect.width, rect.height - 162);
            var columns = Math.Max(1, (int)(list.width / 390)); var width = (list.width - 18 - (columns - 1) * 12) / columns;
            var view = new Rect(0, 0, list.width - 18, (float)Math.Ceiling(items.Count / (double)columns) * 155);
            Widgets.BeginScrollView(list, ref scroll, view);
            for (var n = 0; n < items.Count; n++)
            {
                var i = items[n]; var y = n / columns * 155f; if (y + 147 < scroll.y || y > scroll.y + list.height) continue;
                var x = n % columns * (width + 12); var row = new Rect(x, y, width, 143);
                ArenaWorkbenchUi.Box(row, ArenaWorkbenchUi.PanelColor, selected.Contains(i) ? ArenaWorkbenchUi.Gold : ArenaWorkbenchUi.Border);
                var pawn = pawns[i]; var portrait = new Rect(x + 10, y + 10, 88, 120);
                if (pawn != null)
                {
                    var kind = DefDatabase<PawnKindDef>.GetNamedSilentFail(pawn.Read().Element("basePawnKind")?.Value ?? "");
                    if (ArenaWorkbenchPortraits.HumanBody(pawn))
                    {
                        var entry = portraits.Request(pawn, "Pirate", 170031);
                        if (entry.Texture != null) GUI.DrawTexture(portrait, entry.Texture, ScaleMode.ScaleToFit); else ArenaWorkbenchUi.DefIcon(portrait, kind);
                    }
                    else ArenaWorkbenchUi.DefIcon(portrait, kind);
                }
                ArenaWorkbenchUi.Label(new Rect(x + 113, y + 10, width - 125, 32), names[i], enabled[i] ? ArenaWorkbenchUi.Gold : ArenaWorkbenchUi.Muted);
                ArenaWorkbenchUi.Label(new Rect(x + 113, y + 49, width - 125, 54), details[i], ArenaWorkbenchUi.Muted, GameFont.Tiny);
                if (ArenaWorkbenchUi.Button(new Rect(x + 113, y + 104, width - 125, 28), !enabled[i] ? "已禁用" : selected.Contains(i) ? "已选 ✓" : "选择", selected.Contains(i), enabled[i]))
                { if (selected.Contains(i)) selected.Remove(i); else { if (!multiple) selected.Clear(); selected.Add(i); } }
            }
            Widgets.EndScrollView();
            ArenaWorkbenchUi.Label(new Rect(0, rect.height - 66, rect.width, 27), error ?? "已选 " + selected.Count + " 项 · 筛选保留选择，应用后留在原页面", ArenaWorkbenchUi.Muted, GameFont.Tiny);
            if (multiple && ArenaWorkbenchUi.Button(new Rect(0, rect.height - 32, 140, 30), "选择筛选结果")) foreach (var i in items.Where(i => enabled[i])) selected.Add(i);
            if (ArenaWorkbenchUi.Button(new Rect(rect.width - 255, rect.height - 32, 115, 30), "取消")) Close();
            if (ArenaWorkbenchUi.Button(new Rect(rect.width - 130, rect.height - 32, 130, 30), "应用选择", true, selected.Count > 0))
            { try { apply(selected.OrderBy(i => i).ToList()); Close(); } catch (Exception ex) { error = ex.Message; } }
            portraits.ProcessOne();
        }
    }
    internal sealed class Dialog_ArenaName : Window
    {
        private string name, error;
        private readonly Action<string> apply;
        private readonly string title;
        public override Vector2 InitialSize => new(480, 230);
        internal Dialog_ArenaName(string title, string name, Action<string> apply)
        { this.title = title; this.name = name; this.apply = apply; forcePause = true; doCloseX = true; closeOnAccept = false; absorbInputAroundWindow = true; }
        public override void DoWindowContents(Rect rect)
        {
            ArenaWorkbenchUi.Label(new Rect(0, 0, rect.width - 35, 30), title, ArenaWorkbenchUi.Gold);
            name = Widgets.TextField(new Rect(0, 42, rect.width, 32), name);
            ArenaWorkbenchUi.Label(new Rect(0, 83, rect.width, 30), error ?? "名称最多 120 个字符。", ArenaWorkbenchUi.Muted, GameFont.Tiny);
            if (ArenaWorkbenchUi.Button(new Rect(rect.width - 130, rect.height - 31, 130, 30), "保存", true))
            { try { if (string.IsNullOrWhiteSpace(name) || name.Length > 120) throw new InvalidOperationException("请输入有效名称。"); apply(name.Trim()); Close(); } catch (Exception ex) { error = ex.Message; } }
        }
    }
}
