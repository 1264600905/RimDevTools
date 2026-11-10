using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LogSyncMod.AutoTest.Scenarios;
using RimWorld;
using UnityEngine;
using Verse;

namespace LogSyncMod.AutoTest
{
    internal sealed class Dialog_ManualAutoTests : Window
    {
        internal readonly ManualTestCatalog Catalog;
        internal ManualProjectContext Project;
        internal readonly HashSet<string> SelectedTargets;
        internal readonly HashSet<string> SelectedCategories;
        internal readonly ManualTestBrowserFilter Filter;
        internal bool FilterDirty = true;
        internal IReadOnlyList<ManualTestTarget> Visible => visible;
        private static ManualTestSelection draft;
        private static ManualTestBrowserFilter lastFilter;
        private Vector2 targetScroll, categoryScroll;
        private List<ManualTestTarget> visible = new List<ManualTestTarget>();
        private List<ManualTestChoice> preview = new List<ManualTestChoice>();
        private Dictionary<string, int> categoryCounts = new Dictionary<string, int>();
        private bool previewDirty = true;
        private string error, templateStatus, anchor;
        private readonly HashSet<string> collapsed = new HashSet<string>();
        private const float RowHeight = 52;
        private static readonly Color Muted = new Color(.66f, .71f, .76f);
        private static readonly Color Accent = new Color(.55f, .8f, .93f);
        public override Vector2 InitialSize => new Vector2(Math.Min(1180f, UI.screenWidth - 32f), Math.Min(820f, UI.screenHeight - 32f));

        internal Dialog_ManualAutoTests(ManualTestCatalog catalog, ManualProjectContext project)
        {
            Catalog = catalog; Project = project;
            var last = draft ?? ManualTestService.LastSelection();
            SelectedTargets = new HashSet<string>(last?.Targets ?? new List<string>(), StringComparer.Ordinal);
            SelectedCategories = new HashSet<string>(last?.Categories ?? catalog.Categories, StringComparer.Ordinal);
            var f = lastFilter ?? new ManualTestBrowserFilter();
            Filter = new ManualTestBrowserFilter { Search = f.Search, Package = f.Package, Type = f.Type, ThingCategory = f.ThingCategory,
                Source = f.Source, ThirdPartyOnly = f.ThirdPartyOnly, SelectedOnly = f.SelectedOnly, TestableOnly = f.TestableOnly };
            forcePause = !ManualTestService.Busy; doCloseX = true; closeOnAccept = false; absorbInputAroundWindow = true; draggable = true;
            ReadTemplateStatus();
        }

        public override void PostClose()
        {
            draft = Selection(); lastFilter = Filter;
            base.PostClose();
        }

        internal ManualTestSelection Selection() => new ManualTestSelection { Targets = SelectedTargets.OrderBy(s => s, StringComparer.Ordinal).ToList(), Categories = SelectedCategories.OrderBy(s => s, StringComparer.Ordinal).ToList() };
        internal void SelectTargets(IEnumerable<string> keys, bool selected)
        {
            ManualTestBrowser.Select(SelectedTargets, keys, selected);
            FilterDirty = true; previewDirty = true;
        }
        internal void SelectCategories(IEnumerable<string> categories, bool selected)
        {
            ManualTestBrowser.Select(SelectedCategories, categories, selected); previewDirty = true;
        }
        private void ChangedFilter() { FilterDirty = true; targetScroll = Vector2.zero; anchor = null; }
        internal void RefreshFilter()
        {
            visible = ManualTestBrowser.Filter(Catalog.Targets, Filter, SelectedTargets, Catalog.TestableTargets,
                t => Filter.ThingCategory == null || Catalog.Definitions[t.Key] is ThingDef thing &&
                    (thing.thingCategories ?? new List<ThingCategoryDef>()).Any(c => c.defName == Filter.ThingCategory || c.Parents.Any(p => p.defName == Filter.ThingCategory)));
            FilterDirty = false;
        }
        internal List<ManualTestChoice> Preview()
        {
            if (!previewDirty) return preview;
            try { preview = Catalog.Preview(Selection()); error = null; }
            catch (Exception ex) { preview.Clear(); error = ex.Message; }
            categoryCounts = Catalog.Capabilities.Where(c => c.Applicable && SelectedTargets.Contains(c.TargetKey))
                .GroupBy(c => c.CaseType).ToDictionary(g => g.Key, g => g.Count());
            previewDirty = false; return preview;
        }
        internal ManualTestInvocation Start()
        {
            var invocation = ManualTestService.Submit(Project, Catalog, Selection());
            Close(); return invocation;
        }
        private void ReadTemplateStatus()
        {
            try
            {
                var path = TemporaryPawnData.PathFor(Project.Root);
                templateStatus = File.Exists(path) ? "已暂存角色 · " + TemporaryPawnData.Load(Project.Root).SourceIdentity : "内置默认角色";
            }
            catch (Exception ex) { templateStatus = "角色模板不可用：" + ex.Message; }
        }
        private static void Label(Rect rect, string text, Color? color = null, GameFont font = GameFont.Small)
        {
            var oldColor = GUI.color; var oldFont = Text.Font; var oldAnchor = Text.Anchor;
            GUI.color = color ?? Color.white; Text.Font = font; Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.LabelEllipses(rect, text ?? "");
            GUI.color = oldColor; Text.Font = oldFont; Text.Anchor = oldAnchor;
        }
        private static bool Button(Rect rect, string text, bool selected = false, bool active = true)
        {
            var color = GUI.color; if (selected) GUI.color = Accent;
            var clicked = Widgets.ButtonText(rect, text, active: active);
            GUI.color = color; return clicked;
        }

        public override void DoWindowContents(Rect rect)
        {
            if (ManualTestService.Busy) forcePause = false;
            var font = Text.Font; var color = GUI.color; var textAnchor = Text.Anchor;
            try
            {
                Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft;
                Label(new Rect(0, 0, 230, 34), "LogSyncMod · 自动测试", font: GameFont.Medium);
                Label(new Rect(245, 4, rect.width - 290, 26), "选择目标  →  选择测试类别  →  查看数量并运行", Muted);
                var split = rect.width * .64f;
                Widgets.DrawBoxSolid(new Rect(0, 42, rect.width, 56), new Color(1, 1, 1, .035f));
                Label(new Rect(12, 46, split - 150, 22), "输入项目 · " + Project.Identity);
                Label(new Rect(12, 70, split - 150, 20), Project.Root, Muted, GameFont.Tiny);
                TooltipHandler.TipRegion(new Rect(12, 46, split - 150, 44), Project.Root);
                if (Button(new Rect(split - 130, 55, 112, 28), "切换项目"))
                    Find.WindowStack.Add(new Dialog_BindTestProject(p => { Project = p; ReadTemplateStatus(); }));
                Label(new Rect(split + 12, 46, rect.width - split - 140, 22), "测试角色");
                Label(new Rect(split + 12, 70, rect.width - split - 140, 20), templateStatus, Muted, GameFont.Tiny);
                TooltipHandler.TipRegion(new Rect(split + 12, 46, rect.width - split - 140, 44), templateStatus + "\n通过 LogSyncMod / 导出角色模板（框选）生成模板，再在此选择。地图实例使用该实例自己的数据。");
                if (Button(new Rect(rect.width - 120, 55, 108, 28), "选择模板")) PickPawnTemplate();

                var bodyHeight = rect.height - 218;
                var left = new Rect(0, 110, split - 6, bodyHeight);
                var right = new Rect(split + 6, 110, rect.width - split - 6, bodyHeight);
                Widgets.DrawMenuSection(left); Widgets.DrawMenuSection(right);
                DrawTargetPane(left);
                var plan = Preview(); DrawCategoryPane(right);
                DrawFooter(new Rect(0, rect.height - 98, rect.width, 98), plan);
            }
            finally { Text.Font = font; GUI.color = color; Text.Anchor = textAnchor; }
        }

        private void DrawTargetPane(Rect panel)
        {
            var x = panel.x + 12; var w = panel.width - 24; var y = panel.y;
            Label(new Rect(x, y + 7, w * .6f, 26), "1  选择目标", Accent);
            var tabWidth = (w - 12) / 3;
            var sources = new[] { "definitions", "instances", "all" }; var labels = new[] { "模组定义", "当前地图实例", "全部" };
            for (var i = 0; i < sources.Length; i++)
                if (Button(new Rect(x + i * (tabWidth + 6), y + 40, tabWidth, 28), labels[i], Filter.Source == sources[i])) { Filter.Source = sources[i]; ChangedFilter(); }
            GUI.SetNextControlName("ManualTestSearch");
            var search = Widgets.TextField(new Rect(x, y + 78, w - 38, 30), Filter.Search);
            if (search != Filter.Search) { Filter.Search = search; ChangedFilter(); }
            if (string.IsNullOrEmpty(Filter.Search) && GUI.GetNameOfFocusedControl() != "ManualTestSearch")
                Label(new Rect(x + 8, y + 79, w - 58, 28), "搜索名称、DefName、模组…", Muted);
            if (Button(new Rect(x + w - 30, y + 78, 30, 30), "×", active: Filter.Search.Length > 0)) { Filter.Search = ""; GUI.FocusControl(null); ChangedFilter(); }
            var modWidth = (w - 12) * .44f; var typeWidth = (w - 12) * .27f; var categoryWidth = w - 12 - modWidth - typeWidth;
            var modLabel = Filter.Package == null ? "全部模组" : Catalog.Targets.FirstOrDefault(t => t.PackageId == Filter.Package)?.ModName ?? Filter.Package;
            if (Button(new Rect(x, y + 118, modWidth, 28), modLabel))
                Pick(Catalog.Targets.GroupBy(t => t.PackageId).Select(g => (g.Key, g.First().ModName + "  (" + g.Count() + ")")), "全部模组", value => { Filter.Package = value; ChangedFilter(); });
            if (Button(new Rect(x + modWidth + 6, y + 118, typeWidth, 28), Filter.Type == null ? "全部类型" : ManualTestBrowser.TypeLabel(Filter.Type)))
                Pick(Catalog.Targets.Select(t => t.DefType).Distinct().Select(t => (t, ManualTestBrowser.TypeLabel(t))), "全部类型", value => { Filter.Type = value; ChangedFilter(); });
            var categoryLabel = Filter.ThingCategory == null ? "物品分类" : DefDatabase<ThingCategoryDef>.GetNamedSilentFail(Filter.ThingCategory)?.LabelCap.ToString() ?? Filter.ThingCategory;
            if (Button(new Rect(x + modWidth + typeWidth + 12, y + 118, categoryWidth, 28), categoryLabel))
                Pick(DefDatabase<ThingCategoryDef>.AllDefsListForReading.Select(t => (t.defName, t.LabelCap.ToString())), "全部分类", value => { Filter.ThingCategory = value; ChangedFilter(); });
            var optionWidth = w / 3;
            var third = Filter.ThirdPartyOnly; var selected = Filter.SelectedOnly; var testable = Filter.TestableOnly;
            Widgets.CheckboxLabeled(new Rect(x, y + 155, optionWidth - 8, 25), "仅第三方", ref third);
            Widgets.CheckboxLabeled(new Rect(x + optionWidth, y + 155, optionWidth - 8, 25), "仅已选", ref selected);
            Widgets.CheckboxLabeled(new Rect(x + optionWidth * 2, y + 155, optionWidth - 2, 25), "有可用测试", ref testable);
            if (third != Filter.ThirdPartyOnly || selected != Filter.SelectedOnly || testable != Filter.TestableOnly)
            { Filter.ThirdPartyOnly = third; Filter.SelectedOnly = selected; Filter.TestableOnly = testable; ChangedFilter(); }
            if (FilterDirty) RefreshFilter();
            if (Button(new Rect(x, y + 190, 105, 27), "全选结果", active: visible.Count > 0)) SelectTargets(visible.Select(t => t.Key), true);
            if (Button(new Rect(x + 113, y + 190, 105, 27), "取消结果", active: visible.Any(t => SelectedTargets.Contains(t.Key)))) SelectTargets(visible.Select(t => t.Key), false);
            Label(new Rect(x + 230, y + 190, w - 230, 27), visible.Count + " 项 · 已选 " + SelectedTargets.Count, Muted);
            var list = new Rect(x, y + 226, w, Math.Max(30, panel.height - 238));
            if (visible.Count == 0)
            {
                Label(new Rect(list.x + 8, list.y + 12, list.width - 16, 28), "没有符合筛选条件的目标", Muted);
                if (Button(new Rect(list.x + 8, list.y + 50, 130, 28), "重置筛选")) ResetFilter();
            }
            else DrawTargets(list);
        }
        internal void ResetFilter()
        {
            Filter.Search = ""; Filter.Package = null; Filter.Type = null; Filter.ThingCategory = null;
            Filter.ThirdPartyOnly = false; Filter.SelectedOnly = false; Filter.TestableOnly = true; Filter.Source = "definitions"; ChangedFilter();
        }
        internal void ToggleTarget(string key, bool range = false)
        {
            var value = !SelectedTargets.Contains(key);
            SelectTargets(range ? ManualTestBrowser.Range(visible, anchor, key) : new[] { key }, value); anchor = key;
        }
        private void DrawTargets(Rect area)
        {
            var view = new Rect(0, 0, area.width - 18, visible.Count * RowHeight);
            Widgets.BeginScrollView(area, ref targetScroll, view);
            var start = Math.Max(0, (int)(targetScroll.y / RowHeight)); var end = Math.Min(visible.Count, start + (int)(area.height / RowHeight) + 2);
            for (var i = start; i < end; i++)
            {
                var target = visible[i]; var row = new Rect(0, i * RowHeight, view.width, RowHeight - 2);
                var selected = SelectedTargets.Contains(target.Key); var previous = selected;
                if (selected) Widgets.DrawBoxSolid(row, new Color(.25f, .48f, .6f, .24f));
                else if (i % 2 == 0) Widgets.DrawBoxSolid(row, new Color(1, 1, 1, .035f));
                Widgets.DrawHighlightIfMouseover(row);
                Widgets.Checkbox(new Vector2(6, row.y + 13), ref selected);
                Widgets.DefIcon(new Rect(38, row.y + 7, 36, 36), Catalog.Definitions[target.Key]);
                Label(new Rect(84, row.y + 1, row.width - 92, 26), target.Label);
                Label(new Rect(84, row.y + 26, row.width - 92, 20), target.ModName + " · " + target.DefName, Muted, GameFont.Tiny);
                var click = Widgets.ButtonInvisible(new Rect(34, row.y, row.width - 34, row.height));
                if (selected != previous || click) ToggleTarget(target.Key, Event.current.shift);
                TooltipHandler.TipRegion(row, target.Label + "\n" + ManualTestBrowser.TypeLabel(target.DefType) + " · " + target.ModName + "\n" + target.DefName + "\n" + target.PackageId + (target.InstanceId == null ? "\n创建新的测试对象，不改动地图上的物品。" : "\n读取所选实例的数据，使用副本测试。") + "\nShift + 点击可连续选择。");
            }
            Widgets.EndScrollView();
        }

        private void DrawCategoryPane(Rect panel)
        {
            var x = panel.x + 12; var w = panel.width - 24; var y = panel.y;
            Label(new Rect(x, y + 7, w, 26), "2  选择测试类别", Accent);
            var width = (w - 12) / 3; var quickLabels = new[] { "只选战斗", "只选界面", "只选视图" };
            for (var i = 0; i < ManualTestPlan.Groups.Length; i++)
            {
                var group = ManualTestPlan.Groups[i];
                if (Button(new Rect(x + i * (width + 6), y + 40, width, 28), quickLabels[i]))
                {
                    SelectCategories(SelectedCategories, false); SelectCategories(Catalog.Categories.Where(c => ManualTestPlan.Group(c) == group), true);
                    collapsed.Clear(); foreach (var other in ManualTestPlan.Groups.Where(g => g != group)) collapsed.Add(other); categoryScroll = Vector2.zero;
                }
            }
            Label(new Rect(x, y + 78, w, 28), SelectedTargets.Count == 0 ? "先选目标，右侧数字显示可执行数量" : "类别右侧为可执行项数，灰色表示不适用", Muted, GameFont.Tiny);
            var area = new Rect(x, y + 113, w, Math.Max(30, panel.height - 153));
            var height = ManualTestPlan.Groups.Sum(g => 38 + (collapsed.Contains(g) ? 0 : Catalog.Categories.Count(c => ManualTestPlan.Group(c) == g) * 30));
            var view = new Rect(0, 0, area.width - 18, height);
            Widgets.BeginScrollView(area, ref categoryScroll, view);
            float rowY = 0;
            foreach (var group in ManualTestPlan.Groups)
            {
                var children = Catalog.Categories.Where(c => ManualTestPlan.Group(c) == group).ToList();
                var count = children.Count(SelectedCategories.Contains); var all = count == children.Count; var before = all;
                Widgets.DrawBoxSolid(new Rect(0, rowY, view.width, 32), new Color(1, 1, 1, .045f));
                Widgets.Checkbox(new Vector2(5, rowY + 4), ref all);
                if (all != before) SelectCategories(children, all);
                Label(new Rect(39, rowY, view.width - 45, 32), (collapsed.Contains(group) ? "+  " : "−  ") + group + "  " + count + "/" + children.Count, Accent);
                if (Widgets.ButtonInvisible(new Rect(34, rowY, view.width - 34, 32))) { if (!collapsed.Add(group)) collapsed.Remove(group); }
                rowY += 38;
                if (collapsed.Contains(group)) continue;
                foreach (var category in children)
                {
                    var selected = SelectedCategories.Contains(category); var old = selected;
                    var applicable = categoryCounts.TryGetValue(category, out var value) ? value : 0;
                    Widgets.Checkbox(new Vector2(18, rowY + 2), ref selected, 22);
                    Label(new Rect(50, rowY, view.width - 92, 26), ManualTestPlan.Label(category), SelectedTargets.Count == 0 || applicable > 0 ? Color.white : Muted);
                    Label(new Rect(view.width - 36, rowY, 34, 26), applicable.ToString(), applicable > 0 ? Accent : Muted);
                    if (Widgets.ButtonInvisible(new Rect(45, rowY, view.width - 45, 28))) selected = !selected;
                    if (selected != old) SelectCategories(new[] { category }, selected);
                    TooltipHandler.TipRegion(new Rect(16, rowY, view.width - 16, 28), ManualTestPlan.Label(category) + "\n" + category + "\n当前选择可执行 " + applicable + " 项。");
                    rowY += 30;
                }
            }
            Widgets.EndScrollView();
            if (Button(new Rect(x, panel.yMax - 34, (w - 8) / 2, 26), "全选类别")) SelectCategories(Catalog.Categories, true);
            if (Button(new Rect(x + (w + 8) / 2, panel.yMax - 34, (w - 8) / 2, 26), "清空类别")) SelectCategories(SelectedCategories, false);
        }
        private void DrawFooter(Rect area, List<ManualTestChoice> plan)
        {
            var count = plan.Count(p => p.Applicable); var excluded = plan.Count - count;
            var hidden = SelectedTargets.Count - visible.Count(t => SelectedTargets.Contains(t.Key));
            Label(new Rect(0, area.y, area.width - 220, 28), "已选 " + SelectedTargets.Count + " 个目标   ·   可运行 " + count + " 项   ·   不适用 " + excluded + " 项", Accent);
            var hint = error ?? (SelectedTargets.Count == 0 ? "在左侧点击一行选择目标，Shift + 点击可连选。" : hidden > 0 ? "另有 " + hidden + " 个已选目标不在当前筛选内，开始时会一并运行。" : "仅执行适用组合；不适用项会在结果中注明原因。");
            Label(new Rect(0, area.y + 30, area.width - 220, 26), hint, error == null ? Muted : new Color(1, .55f, .5f), GameFont.Tiny);
            TooltipHandler.TipRegion(new Rect(0, area.y, area.width - 220, 56), error ?? string.Join("\n", plan.Where(p => !p.Applicable).Take(20).Select(p => p.TargetKey + " / " + ManualTestPlan.Label(p.CaseType) + "：" + p.Reason)));
            Label(new Rect(0, area.y + 66, area.width - 570, 26), ManualTestService.Busy ? ManualTestService.Progress : "就绪 · 测试在独立场景中运行", Muted, GameFont.Tiny);
            if (Button(new Rect(area.width - 210, area.y + 4, 210, 42), (ManualTestService.Busy ? "加入队列" : "开始测试") + "（" + count + " 项）", true, count > 0 && error == null))
            { try { Start(); } catch (Exception ex) { error = ex.Message; } }
            if (Button(new Rect(area.width - 552, area.y + 64, 124, 28), "清空目标", active: SelectedTargets.Count > 0)) SelectTargets(SelectedTargets, false);
            if (Button(new Rect(area.width - 418, area.y + 64, 134, 28), "取消执行批次", active: ManualTestService.Busy)) ManualTestService.Cancel();
            if (Button(new Rect(area.width - 274, area.y + 64, 134, 28), "重置筛选")) ResetFilter();
            if (Button(new Rect(area.width - 130, area.y + 64, 130, 28), "关闭")) Close();
        }
        private void PickPawnTemplate()
        {
            try
            {
                var files = TemporaryPawnData.ExportedFiles(Project.Root);
                if (files.Count == 0) { Messages.Message("尚无导出的模板。请在 LogSyncMod 调试菜单中使用“导出角色模板（框选）”。", MessageTypeDefOf.RejectInput, false); return; }
                var options = files.Select(path => new FloatMenuOption(Path.GetFileNameWithoutExtension(path), () => {
                    try { TemporaryPawnData.Save(Project.Root, TemporaryPawnData.Parse(File.ReadAllText(path), path)); ReadTemplateStatus(); }
                    catch (Exception ex) { error = "角色模板加载失败：" + ex.Message; }
                })).ToList();
                Find.WindowStack.Add(new FloatMenu(options));
            }
            catch (Exception ex) { error = "角色模板列表读取失败：" + ex.Message; }
        }
        private static void Pick(IEnumerable<(string Key, string Label)> values, string all, Action<string> set)
        {
            var items = new List<FloatMenuOption> { new FloatMenuOption(all, () => set(null)) };
            items.AddRange(values.OrderBy(v => v.Label, StringComparer.CurrentCultureIgnoreCase).Select(v => new FloatMenuOption(v.Label, () => set(v.Key))));
            Find.WindowStack.Add(new FloatMenu(items));
        }
    }
}
