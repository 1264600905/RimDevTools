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
        internal string Search = "";
        internal string PackageFilter;
        internal string TypeFilter;
        internal string ThingCategoryFilter;
        internal bool ThirdPartyOnly;
        internal bool SelectedOnly;
        internal bool FilterDirty = true;
        private Vector2 targetScroll, categoryScroll;
        private List<ManualTestTarget> visible = new List<ManualTestTarget>();
        private List<ManualTestChoice> preview = new List<ManualTestChoice>();
        private bool previewDirty = true;
        private string error;
        private string templateStatus;
        private readonly HashSet<string> collapsed = new HashSet<string>();
        private const float RowHeight = 46;
        public override Vector2 InitialSize => new Vector2(Math.Min(1180f, UI.screenWidth - 32f), Math.Min(800f, UI.screenHeight - 32f));

        internal Dialog_ManualAutoTests(ManualTestCatalog catalog, ManualProjectContext project)
        {
            Catalog = catalog; Project = project;
            var last = ManualTestService.LastSelection();
            SelectedTargets = new HashSet<string>(last?.Targets ?? new List<string>(), StringComparer.Ordinal);
            SelectedCategories = new HashSet<string>(last?.Categories ?? catalog.Categories, StringComparer.Ordinal);
            forcePause = !ManualTestService.Busy; doCloseX = true; closeOnAccept = false; absorbInputAroundWindow = true; draggable = true;
            ReadTemplateStatus();
        }

        internal ManualTestSelection Selection() => new ManualTestSelection { Targets = SelectedTargets.OrderBy(s => s, StringComparer.Ordinal).ToList(), Categories = SelectedCategories.OrderBy(s => s, StringComparer.Ordinal).ToList() };

        internal void SelectTargets(IEnumerable<string> keys, bool selected)
        {
            foreach (var key in keys) { if (selected) SelectedTargets.Add(key); else SelectedTargets.Remove(key); }
            FilterDirty = true; previewDirty = true;
        }

        internal void SelectCategories(IEnumerable<string> categories, bool selected)
        {
            foreach (var category in categories) { if (selected) SelectedCategories.Add(category); else SelectedCategories.Remove(category); }
            previewDirty = true;
        }

        internal void RefreshFilter()
        {
            visible = Catalog.Targets.Where(t => (string.IsNullOrWhiteSpace(Search) || (t.Label + " " + t.DefName + " " + t.PackageId + " " + t.ModName).IndexOf(Search, StringComparison.OrdinalIgnoreCase) >= 0) &&
                (PackageFilter == null || t.PackageId == PackageFilter) && (TypeFilter == null || t.DefType == TypeFilter) &&
                (!ThirdPartyOnly || !t.PackageId.StartsWith("ludeon.", StringComparison.OrdinalIgnoreCase)) && (!SelectedOnly || SelectedTargets.Contains(t.Key)) &&
                (ThingCategoryFilter == null || Catalog.Definitions[t.Key] is ThingDef thing && (thing.thingCategories ?? new List<ThingCategoryDef>()).Any(c => c.defName == ThingCategoryFilter || c.Parents.Any(p => p.defName == ThingCategoryFilter))))
                .OrderBy(t => t.Label).ThenBy(t => t.Key, StringComparer.Ordinal).ToList();
            FilterDirty = false;
        }

        internal List<ManualTestChoice> Preview()
        {
            if (!previewDirty) return preview;
            try { preview = Catalog.Preview(Selection()); error = null; }
            catch (Exception ex) { preview.Clear(); error = ex.Message; }
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
                templateStatus = File.Exists(path) ? "暂存角色：" + TemporaryPawnData.Load(Project.Root).Sha256.Substring(0, 12) : "暂存角色：无，使用内置默认角色";
            }
            catch (Exception ex) { templateStatus = "暂存角色错误：" + ex.Message; }
        }

        public override void DoWindowContents(Rect rect)
        {
            if (ManualTestService.Busy) forcePause = false;
            var font = Text.Font; var color = GUI.color;
            try
            {
                Text.Font = GameFont.Medium; Widgets.Label(new Rect(0, 0, rect.width - 40, 34), "RimDevTools 普通自动测试"); Text.Font = GameFont.Small;
                Widgets.Label(new Rect(0, 38, rect.width - 160, 24), Project.Root);
                if (Widgets.ButtonText(new Rect(rect.width - 150, 36, 150, 28), "切换输入项目")) Find.WindowStack.Add(new Dialog_BindTestProject(p => { Project = p; ReadTemplateStatus(); }));
                var split = rect.width * .67f;
                Widgets.DrawMenuSection(new Rect(0, 76, split - 8, rect.height - 210));
                Widgets.DrawMenuSection(new Rect(split + 4, 76, rect.width - split - 4, rect.height - 210));
                Widgets.Label(new Rect(10, 82, split - 30, 26), "物品 / 目标 TAB");
                Widgets.Label(new Rect(split + 14, 82, rect.width - split - 25, 26), "测试类别 TAB");
                var search = Widgets.TextField(new Rect(10, 114, split - 28, 28), Search);
                if (search != Search) { Search = search; FilterDirty = true; }
                var buttonWidth = (split - 44) / 3;
                if (Widgets.ButtonText(new Rect(10, 150, buttonWidth, 28), PackageFilter ?? "全部模组")) Pick(Catalog.Targets.Select(t => t.PackageId), value => { PackageFilter = value; FilterDirty = true; });
                if (Widgets.ButtonText(new Rect(18 + buttonWidth, 150, buttonWidth, 28), TypeFilter ?? "全部类型")) Pick(Catalog.Targets.Select(t => t.DefType), value => { TypeFilter = value; FilterDirty = true; });
                if (Widgets.ButtonText(new Rect(26 + buttonWidth * 2, 150, buttonWidth, 28), ThingCategoryFilter ?? "全部分类")) Pick(DefDatabase<ThingCategoryDef>.AllDefsListForReading.Select(t => t.defName), value => { ThingCategoryFilter = value; FilterDirty = true; });
                var thirdParty = ThirdPartyOnly; var selectedOnly = SelectedOnly;
                Widgets.CheckboxLabeled(new Rect(10, 186, 150, 26), "仅第三方", ref thirdParty); Widgets.CheckboxLabeled(new Rect(180, 186, 150, 26), "仅已选", ref selectedOnly);
                if (thirdParty != ThirdPartyOnly || selectedOnly != SelectedOnly) { ThirdPartyOnly = thirdParty; SelectedOnly = selectedOnly; FilterDirty = true; }
                if (FilterDirty) RefreshFilter();
                if (Widgets.ButtonText(new Rect(10, 218, 150, 28), "全选当前筛选")) SelectTargets(visible.Select(t => t.Key), true);
                if (Widgets.ButtonText(new Rect(170, 218, 150, 28), "清空目标选择")) SelectTargets(SelectedTargets.ToList(), false);
                Widgets.Label(new Rect(330, 220, split - 350, 24), visible.Count + " 项");
                DrawTargets(new Rect(10, 254, split - 28, Math.Max(60, rect.height - 398)));
                DrawCategories(new Rect(split + 14, 114, rect.width - split - 25, Math.Max(90, rect.height - 258)));
                var plan = Preview(); var count = plan.Count(p => p.Applicable);
                Widgets.Label(new Rect(0, rect.height - 124, rect.width - 205, 24), "已选目标 " + SelectedTargets.Count + " / 类别 " + SelectedCategories.Count + " / 执行项 " + count + " / 不适用 " + plan.Count(p => !p.Applicable));
                Widgets.Label(new Rect(0, rect.height - 96, rect.width - 205, 44), error ?? templateStatus);
                Widgets.Label(new Rect(0, rect.height - 45, rect.width - 205, 32), ManualTestService.Progress);
                TooltipHandler.TipRegion(new Rect(0, rect.height - 124, rect.width - 205, 24), string.Join("\n", plan.Where(p => !p.Applicable).Take(30).Select(p => p.TargetKey + " / " + p.CaseType + "：" + p.Reason)));
                if (Widgets.ButtonText(new Rect(rect.width - 190, rect.height - 122, 190, 36), "开始测试", active: count > 0 && error == null))
                { try { Start(); } catch (Exception ex) { error = ex.Message; } }
                if (Widgets.ButtonText(new Rect(rect.width - 190, rect.height - 78, 190, 32), "取消正在执行的批次", active: ManualTestService.Busy)) ManualTestService.Cancel();
                if (Widgets.ButtonText(new Rect(rect.width - 190, rect.height - 38, 190, 32), "关闭")) Close();
            }
            finally { Text.Font = font; GUI.color = color; }
        }

        private void DrawTargets(Rect area)
        {
            var view = new Rect(0, 0, area.width - 18, visible.Count * RowHeight);
            Widgets.BeginScrollView(area, ref targetScroll, view);
            var start = Math.Max(0, (int)(targetScroll.y / RowHeight)); var end = Math.Min(visible.Count, start + (int)(area.height / RowHeight) + 2);
            for (var i = start; i < end; i++)
            {
                var target = visible[i]; var row = new Rect(0, i * RowHeight, view.width, RowHeight - 2);
                if (i % 2 == 0) Widgets.DrawBoxSolid(row, new Color(1, 1, 1, .035f));
                var selected = SelectedTargets.Contains(target.Key); var previous = selected;
                Widgets.Checkbox(new Vector2(4, row.y + 9), ref selected);
                if (selected != previous) SelectTargets(new[] { target.Key }, selected);
                Widgets.DefIcon(new Rect(34, row.y + 3, 34, 34), Catalog.Definitions[target.Key]);
                Widgets.Label(new Rect(78, row.y + 1, row.width - 82, 24), target.Label);
                var old = Text.Font; Text.Font = GameFont.Tiny; Widgets.Label(new Rect(78, row.y + 22, row.width - 82, 22), target.DefName + "  ·  " + target.PackageId); Text.Font = old;
                TooltipHandler.TipRegion(row, target.DefType + " / " + target.Key);
            }
            Widgets.EndScrollView();
        }

        private void DrawCategories(Rect area)
        {
            if (Widgets.ButtonText(new Rect(area.x, area.y, area.width / 2 - 4, 28), "类别全选")) SelectCategories(Catalog.Categories, true);
            if (Widgets.ButtonText(new Rect(area.x + area.width / 2 + 4, area.y, area.width / 2 - 4, 28), "清空类别")) SelectCategories(SelectedCategories.ToList(), false);
            area.yMin += 38;
            var height = ManualTestPlan.Groups.Sum(g => 38 + (collapsed.Contains(g) ? 0 : Catalog.Categories.Count(c => ManualTestPlan.Group(c) == g) * 30));
            Widgets.BeginScrollView(area, ref categoryScroll, new Rect(0, 0, area.width - 18, height));
            float y = 0;
            foreach (var group in ManualTestPlan.Groups)
            {
                var children = Catalog.Categories.Where(c => ManualTestPlan.Group(c) == group).ToList();
                var all = children.All(SelectedCategories.Contains); var previous = all; var partial = !all && children.Any(SelectedCategories.Contains);
                Widgets.CheckboxLabeled(new Rect(28, y, area.width - 52, 30), group + (partial ? " [部分]" : ""), ref all);
                if (all != previous) SelectCategories(children, all);
                if (Widgets.ButtonText(new Rect(0, y + 1, 24, 26), collapsed.Contains(group) ? "+" : "−")) { if (!collapsed.Add(group)) collapsed.Remove(group); }
                y += 38;
                if (collapsed.Contains(group)) continue;
                foreach (var category in children)
                {
                    var selected = SelectedCategories.Contains(category); var before = selected;
                    Widgets.CheckboxLabeled(new Rect(26, y, area.width - 50, 28), ManualTestPlan.Label(category), ref selected);
                    TooltipHandler.TipRegion(new Rect(26, y, area.width - 50, 28), category);
                    if (selected != before) SelectCategories(new[] { category }, selected);
                    y += 30;
                }
            }
            Widgets.EndScrollView();
        }

        private static void Pick(IEnumerable<string> values, Action<string> set)
        {
            var items = new List<FloatMenuOption> { new FloatMenuOption("全部", () => set(null)) };
            items.AddRange(values.Distinct().OrderBy(v => v).Select(v => new FloatMenuOption(v, () => set(v)))); Find.WindowStack.Add(new FloatMenu(items));
        }
    }
}
