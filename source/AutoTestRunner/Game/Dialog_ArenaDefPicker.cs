using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace LogSyncMod.AutoTest
{
    internal sealed class ArenaDefChoice
    {
        internal Def Definition;
        internal int? Degree;
        internal int? GroupMakerIndex;
        internal string Description;
        internal string Category;
        internal string Label;
        internal string Summary;
        internal string Key => Definition.GetType().Name + ":" + Definition.defName + ":" + Degree + ":" + GroupMakerIndex;
        internal string Mod => Definition.modContentPack?.Name ?? "核心 / 未知来源";
        internal XElement Xml => new("Def", new XAttribute("type", Definition.GetType().Name), new XAttribute("name", Definition.defName), Degree.HasValue ? new XAttribute("degree", Degree.Value) : null, GroupMakerIndex.HasValue ? new XAttribute("groupMakerIndex", GroupMakerIndex.Value) : null);
    }
    internal sealed class Dialog_ArenaDefPicker : Window
    {
        private readonly string title;
        private readonly List<ArenaDefChoice> choices;
        private readonly bool multiple;
        private readonly Action<List<ArenaDefChoice>> apply;
        private readonly HashSet<string> selected;
        private string search = "", mod, category;
        private bool selectedOnly;
        private bool aiCanUseOnly;
        private ArenaDefChoice focused;
        private Vector2 scroll;
        private List<ArenaDefChoice> filtered;
        private bool dirty = true;
        private readonly Func<ArenaDefChoice, string> rejection;
        private readonly Action cleanup;
        public override Vector2 InitialSize => new(Math.Min(1100, UI.screenWidth - 24), Math.Min(800, UI.screenHeight - 24));
        internal Dialog_ArenaDefPicker(string title, IEnumerable<ArenaDefChoice> choices, bool multiple,
            IEnumerable<string> selected, Action<List<ArenaDefChoice>> apply, Func<ArenaDefChoice, string> rejection = null, Action cleanup = null)
        {
            this.title = title; this.choices = choices.OrderBy(x => x.Label, StringComparer.CurrentCulture).ThenBy(x => x.Key).ToList();
            this.multiple = multiple; this.selected = new HashSet<string>(selected ?? Enumerable.Empty<string>()); this.apply = apply;
            this.rejection = rejection; this.cleanup = cleanup;
            doCloseX = true; closeOnAccept = false; forcePause = true; absorbInputAroundWindow = true;
        }
        public override void PostClose() { try { cleanup?.Invoke(); } finally { base.PostClose(); } }
        private void Filter()
        {
            if (!dirty) return;
            var words = search.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            filtered = choices.Where(x => (mod == null || x.Mod == mod) && (category == null || x.Category == category) && (!selectedOnly || selected.Contains(x.Key))
                && (!aiCanUseOnly || x.Definition is AbilityDef ability && ability.aiCanUse)
                && words.All(w => (x.Label + " " + x.Definition.defName + " " + x.Mod).IndexOf(w, StringComparison.CurrentCultureIgnoreCase) >= 0)).ToList();
            dirty = false;
        }
        private void Dropdown(Rect r, string text, IEnumerable<string> values, Action<string> changed)
        {
            if (!ArenaWorkbenchUi.Button(r, text)) return;
            Find.WindowStack.Add(new FloatMenu(new[] { new FloatMenuOption("全部", () => { changed(null); dirty = true; scroll = Vector2.zero; }) }
                .Concat(values.Where(s => s != null).Distinct().OrderBy(s => s).Select(s => new FloatMenuOption(s, () => { changed(s); dirty = true; scroll = Vector2.zero; }))).ToList()));
        }
        public override void DoWindowContents(Rect rect)
        {
            var oldFont = Text.Font; Text.Font = GameFont.Medium; Widgets.Label(new Rect(0, 0, rect.width - 50, 32), title); Text.Font = GameFont.Small;
            var next = Widgets.TextField(new Rect(0, 42, rect.width - 8, 30), search);
            if (next != search) { search = next; dirty = true; scroll = Vector2.zero; }
            if (search.Length == 0) TooltipHandler.TipRegion(new Rect(0, 42, rect.width, 30), "搜索名称、DefName、模组；空格分隔多个关键词");
            var side = 185f;
            Dropdown(new Rect(0, 83, side, 30), mod ?? "全部模组", choices.Select(x => x.Mod), s => mod = s);
            Dropdown(new Rect(side + 10, 83, 190, 30), category ?? "全部分类", choices.Select(x => x.Category), s => category = s);
            var only = selectedOnly; Widgets.CheckboxLabeled(new Rect(side + 215, 86, 160, 27), "仅看已选", ref only);
            if (only != selectedOnly) { selectedOnly = only; dirty = true; scroll = Vector2.zero; }
            if (choices.Any(c => c.Definition is AbilityDef))
            {
                var ai = aiCanUseOnly; Widgets.CheckboxLabeled(new Rect(side + 385, 86, 205, 27), "AIcanuse = true", ref ai);
                if (ai != aiCanUseOnly) { aiCanUseOnly = ai; dirty = true; scroll = Vector2.zero; }
            }
            Filter();
            var detailW = Math.Min(255, rect.width * .27f);
            var list = new Rect(0, 125, rect.width - detailW - 15, rect.height - 190);
            var view = new Rect(0, 0, list.width - 18, filtered.Count * 59);
            Widgets.BeginScrollView(list, ref scroll, view);
            var start = Math.Max(0, (int)(scroll.y / 59)); var end = Math.Min(filtered.Count, start + (int)(list.height / 59) + 2);
            for (var i = start; i < end; i++)
            {
                var choice = filtered[i]; var row = new Rect(0, i * 59, view.width, 56);
                var on = selected.Contains(choice.Key); var before = on;
                var reason = rejection?.Invoke(choice);
                var enabled = GUI.enabled; GUI.enabled &= reason == null;
                if (on) Widgets.DrawBoxSolid(row, new Color(.35f, .3f, .2f, .5f));
                Widgets.DrawHighlightIfMouseover(row);
                Widgets.Checkbox(new Vector2(5, row.y + 16), ref on);
                try { ArenaWorkbenchUi.DefIcon(new Rect(37, row.y + 7, 40, 40), choice.Definition); } catch { }
                Widgets.Label(new Rect(85, row.y + 3, row.width - 90, 27), choice.Label);
                var font = Text.Font; Text.Font = GameFont.Tiny; Widgets.Label(new Rect(85, row.y + 29, row.width - 90, 23), choice.Summary ?? choice.Mod + " · " + choice.Category + " · " + choice.Definition.defName); Text.Font = font;
                TooltipHandler.TipRegion(row, choice.Label + "\n" + choice.Mod + "\n" + (choice.Description ?? choice.Definition.description ?? ""));
                if (Widgets.ButtonInvisible(new Rect(34, row.y, row.width - 34, row.height))) { on = !before; focused = choice; }
                if (on != before)
                {
                    if (!multiple) selected.Clear();
                    if (on) selected.Add(choice.Key); else selected.Remove(choice.Key);
                    focused = choice; dirty = selectedOnly;
                }
                GUI.enabled = enabled;
                if (reason != null) { TooltipHandler.TipRegion(row, reason); ArenaWorkbenchUi.Label(new Rect(row.width - 125, row.y + 28, 120, 22), "无法穿戴", new Color(.9f, .58f, .45f), GameFont.Tiny); }
            }
            Widgets.EndScrollView();
            var detail = new Rect(rect.width - detailW, 125, detailW, rect.height - 190); ArenaWorkbenchUi.Panel(detail);
            focused ??= filtered.FirstOrDefault();
            if (focused != null)
            {
                try { ArenaWorkbenchUi.DefIcon(new Rect(detail.x + 18, detail.y + 16, 76, 76), focused.Definition); } catch { }
                Widgets.Label(new Rect(detail.x + 12, detail.y + 105, detail.width - 24, 55), focused.Label);
                Text.Font = GameFont.Tiny;
                Widgets.Label(new Rect(detail.x + 12, detail.y + 163, detail.width - 24, 70), focused.Definition.defName + "\n" + focused.Mod + " / " + focused.Category);
                Widgets.Label(new Rect(detail.x + 12, detail.y + 240, detail.width - 24, Math.Max(30, detail.height - 250)), focused.Description ?? focused.Definition.description ?? "");
                Text.Font = GameFont.Small;
            }
            var y = rect.height - 50;
            Widgets.Label(new Rect(0, y + 5, 270, 30), filtered.Count + " 项结果 · 已选 " + selected.Count);
            if (multiple && ArenaWorkbenchUi.Button(new Rect(260, y, 110, 30), "全选筛选结果")) { foreach (var c in filtered.Where(c => rejection?.Invoke(c) == null)) selected.Add(c.Key); dirty = true; }
            if (ArenaWorkbenchUi.Button(new Rect(rect.width - 380, y, 130, 30), "复制所选 XML")) GUIUtility.systemCopyBuffer = new XElement("Definitions", choices.Where(x => selected.Contains(x.Key)).Select(x => x.Xml)).ToString();
            if (ArenaWorkbenchUi.Button(new Rect(rect.width - 240, y, 110, 30), "取消")) Close();
            if (ArenaWorkbenchUi.Button(new Rect(rect.width - 120, y, 120, 30), "应用选择", active: multiple || selected.Count > 0))
            {
                try { var picked = choices.Where(x => selected.Contains(x.Key)).ToList(); var invalid = picked.Select(c => rejection?.Invoke(c)).FirstOrDefault(x => x != null); if (invalid != null) throw new InvalidOperationException(invalid); apply(picked); Close(); }
                catch (Exception ex) { Messages.Message(ex.Message, MessageTypeDefOf.RejectInput, false); }
            }
            Text.Font = oldFont;
        }
        internal static List<ArenaDefChoice> Catalog(string type)
        {
            if (type == "battleGroup")
            {
                var groups = new List<ArenaDefChoice>();
                foreach (var faction in DefDatabase<FactionDef>.AllDefsListForReading)
                for (var i = 0; i < (faction.pawnGroupMakers?.Count ?? 0); i++)
                {
                    var maker = faction.pawnGroupMakers[i];
                    if (maker.kindDef?.defName != "Combat" || !ArenaBattleGroupCatalog.Options(maker).Any()) continue;
                    groups.Add(new ArenaDefChoice { Definition = faction, GroupMakerIndex = i, Category = "战斗组",
                        Label = faction.LabelCap + " · 战斗组 #" + i,
                        Summary = string.Join("、", ArenaBattleGroupCatalog.Options(maker).Take(5).Select(o => o.kind.LabelCap.ToString())),
                        Description = ArenaBattleGroupCatalog.Summary(maker) });
                }
                return groups;
            }
            IEnumerable<Def> defs = type switch {
                "item" or "weapon" or "apparel" or "race" or "stuff" => DefDatabase<ThingDef>.AllDefsListForReading,
                "trait" => DefDatabase<TraitDef>.AllDefsListForReading,
                "gene" => DefDatabase<GeneDef>.AllDefsListForReading,
                "ability" => DefDatabase<AbilityDef>.AllDefsListForReading,
                "hediff" => DefDatabase<HediffDef>.AllDefsListForReading,
                "pawnKind" => DefDatabase<PawnKindDef>.AllDefsListForReading,
                "faction" => DefDatabase<FactionDef>.AllDefsListForReading,
                "xenotype" => DefDatabase<XenotypeDef>.AllDefsListForReading,
                "weather" => DefDatabase<WeatherDef>.AllDefsListForReading,
                _ => Enumerable.Empty<Def>()
            };
            var result = new List<ArenaDefChoice>();
            foreach (var d in defs)
            {
                if (d is PawnKindDef pawnKind && !ArenaBattleGroupCatalog.LivingKind(pawnKind)) continue;
                if (d is ThingDef thing && (type == "weapon" && !thing.IsWeapon || type == "apparel" && !thing.IsApparel || type == "race" && thing.race == null || type == "stuff" && thing.stuffProps == null)) continue;
                if (d is TraitDef trait)
                {
                    foreach (var degree in trait.degreeDatas) result.Add(new ArenaDefChoice { Definition = d, Degree = degree.degree, Label = degree.label ?? d.label ?? d.defName, Category = "特性 · " + degree.degree });
                    continue;
                }
                result.Add(new ArenaDefChoice { Definition = d, Label = string.IsNullOrWhiteSpace(d.label) ? d.defName : d.LabelCap,
                    Description = d is AbilityDef ability ? (d.description ?? "") + "\n\nAIcanuse = " + ability.aiCanUse.ToString().ToLowerInvariant() + "\n该字段来自游戏定义，仅展示，不修改原定义。" : null,
                    Category = d is ThingDef item ? item.IsRangedWeapon ? "远程武器" : item.IsWeapon ? "近战武器" : item.IsApparel ? "服装 / 护甲" : item.race != null ? item.race.Humanlike ? "人类种族" : "其他种族" : "材质"
                    : d is PawnKindDef kind ? kind.RaceProps.Humanlike ? "人类" : kind.RaceProps.IsMechanoid ? "机械族" : "动物 / 其他"
                    : d is AbilityDef a ? "AIcanuse=" + a.aiCanUse.ToString().ToLowerInvariant() : type });
            }
            return result;
        }
    }
}
