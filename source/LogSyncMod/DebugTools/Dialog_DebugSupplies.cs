using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace LogSyncMod
{
    internal sealed class Dialog_DebugSupplies : Window
    {
        private static readonly Color Accent = new Color(0.38f, 0.78f, 0.74f);
        private static readonly Color Muted = new Color(0.68f, 0.73f, 0.77f);
        private const float RowHeight = 42f;
        private readonly Map map;
        private readonly List<DebugSupplyEntry> entries;
        private readonly List<Tuple<ThingCategoryDef, int>> categories = new List<Tuple<ThingCategoryDef, int>>();
        private List<DebugSupplyEntry> visible;
        private Vector2 scroll, categoryScroll;
        private string search = "";
        private string modFilter;
        private ThingCategoryDef category;
        private bool thirdPartyOnly;
        private int view = 1;
        private bool filterDirty = true;
        private bool resetMissingOverrides;

        public override Vector2 InitialSize => new Vector2(Math.Min(1120f, UI.screenWidth - 32f), Math.Min(760f, UI.screenHeight - 32f));

        public Dialog_DebugSupplies(Map map)
        {
            this.map = map;
            entries = DebugSupplyPreset.CreateEntries();
            forcePause = true;
            absorbInputAroundWindow = true;
            closeOnAccept = false;
            doCloseX = true;
            draggable = true;
            onlyDrawInDevMode = true;
            HashSet<ThingCategoryDef> used = new HashSet<ThingCategoryDef>();
            foreach (DebugSupplyEntry entry in entries)
            {
                foreach (ThingCategoryDef cat in entry.Def.thingCategories ?? new List<ThingCategoryDef>())
                {
                    used.Add(cat);
                    foreach (ThingCategoryDef parent in cat.Parents) used.Add(parent);
                }
            }
            foreach (ThingCategoryDef root in used.Where(c => c.parent == null).OrderBy(c => c.label))
                AddCategoryChildren(root, -1, used);
        }

        private void AddCategoryChildren(ThingCategoryDef cat, int depth, HashSet<ThingCategoryDef> used)
        {
            if (depth >= 0) categories.Add(Tuple.Create(cat, depth));
            foreach (ThingCategoryDef child in cat.childCategories.Where(used.Contains).OrderBy(c => c.label))
                AddCategoryChildren(child, depth + 1, used);
        }

        private static string T(string key) => ("LogSyncMod." + key).Translate();

        public override void DoWindowContents(Rect inRect)
        {
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            Color oldColor = GUI.color;
            try
            {
                Text.Font = GameFont.Medium;
                Widgets.Label(new Rect(0f, 0f, inRect.width - 36f, 34f), T("DebugSuppliesTitle"));
                Text.Font = GameFont.Small;
                GUI.color = Muted;
                Widgets.Label(new Rect(0f, 38f, inRect.width, 24f), T("DebugSuppliesSubtitle").Truncate(inRect.width));
                GUI.color = Color.white;
                DrawFilters(inRect.width);
                if (filterDirty) RefreshFilter();

                bool showSidebar = inRect.width >= 900f;
                Rect body = new Rect(0f, 152f, inRect.width, Math.Max(80f, inRect.height - 270f));
                if (showSidebar)
                {
                    DrawCategories(new Rect(body.x, body.y, 178f, body.height));
                    body.xMin += 190f;
                }
                DrawItems(body);
                DrawFooter(inRect, body.yMax);
            }
            finally
            {
                Text.Font = oldFont;
                Text.Anchor = oldAnchor;
                GUI.color = oldColor;
            }
        }

        private void DrawFilters(float width)
        {
            float modWidth = Math.Min(240f, width * 0.3f);
            Rect searchRect = new Rect(0f, 72f, width - modWidth - 10f, 30f);
            string next = Widgets.TextField(searchRect, search);
            if (next != search) { search = next; filterDirty = true; scroll = Vector2.zero; }
            if (string.IsNullOrEmpty(search) && !searchRect.Contains(Event.current.mousePosition))
            {
                GUI.color = Muted;
                Widgets.Label(searchRect.ContractedBy(6f, 2f), T("DebugSearchHint").Truncate(searchRect.width - 12f));
                GUI.color = Color.white;
            }
            TooltipHandler.TipRegion(searchRect, T("DebugSearchHint"));
            string modName = modFilter == null ? T("DebugAllMods") : entries.First(e => e.PackageId == modFilter).ModName;
            if (Widgets.ButtonText(new Rect(width - modWidth, 72f, modWidth, 30f), modName.Truncate(modWidth - 25f))) OpenModMenu();

            string[] labels = { T("DebugAllItems"), T("DebugDefaultItems"), T("DebugSelectedItems") };
            float tabWidth = Math.Min(120f, width * 0.17f);
            for (int i = 0; i < labels.Length; i++)
            {
                Rect tab = new Rect(i * (tabWidth + 6f), 112f, tabWidth, 30f);
                if (view == i) Widgets.DrawBoxSolid(tab, new Color(Accent.r, Accent.g, Accent.b, 0.18f));
                if (Widgets.ButtonText(tab, labels[i], drawBackground: view != i))
                {
                    view = i;
                    filterDirty = true;
                    scroll = Vector2.zero;
                }
                if (view == i) Widgets.DrawBoxSolid(new Rect(tab.x, tab.yMax - 2f, tab.width, 2f), Accent);
            }
            float x = (tabWidth + 6f) * 3f + 10f;
            if (width < 900f)
            {
                float catWidth = Math.Min(170f, width - x - 165f);
                if (Widgets.ButtonText(new Rect(x, 112f, catWidth, 30f), (category?.LabelCap.ToString() ?? T("DebugAllCategories")).Truncate(catWidth - 15f)))
                    OpenCategoryMenu();
                x += catWidth + 10f;
            }
            bool nextThirdParty = thirdPartyOnly;
            Widgets.CheckboxLabeled(new Rect(x, 114f, width - x, 26f), T("DebugThirdPartyOnly"), ref nextThirdParty);
            if (nextThirdParty != thirdPartyOnly) { thirdPartyOnly = nextThirdParty; filterDirty = true; }
        }

        private void OpenModMenu()
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>
            {
                new FloatMenuOption(T("DebugAllMods"), () => { modFilter = null; filterDirty = true; scroll = Vector2.zero; })
            };
            foreach (var group in entries.GroupBy(e => e.PackageId).OrderBy(g => g.First().ModName))
            {
                string packageId = group.Key;
                options.Add(new FloatMenuOption(group.First().ModName + " (" + packageId + ")",
                    () => { modFilter = packageId; filterDirty = true; scroll = Vector2.zero; }));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void OpenCategoryMenu()
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>
            {
                new FloatMenuOption(T("DebugAllCategories"), () => SetCategory(null))
            };
            foreach (var item in categories)
            {
                ThingCategoryDef cat = item.Item1;
                options.Add(new FloatMenuOption(new string(' ', item.Item2 * 2) + cat.LabelCap, () => SetCategory(cat)));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void SetCategory(ThingCategoryDef value)
        {
            category = value;
            filterDirty = true;
            scroll = Vector2.zero;
        }

        private void RefreshFilter()
        {
            string[] words = search.ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            visible = entries.Where(e => (view != 1 || e.DefaultIncluded) && (view != 2 || e.Included) &&
                (modFilter == null || e.PackageId == modFilter) && (!thirdPartyOnly || e.ThirdParty) &&
                (category == null || category.ContainedInThisOrDescendant(e.Def)) && words.All(e.SearchText.Contains)).ToList();
            filterDirty = false;
        }

        private void DrawCategories(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            Rect inner = rect.ContractedBy(8f);
            Rect content = new Rect(0f, 0f, inner.width - 16f, (categories.Count + 1) * 30f);
            Widgets.BeginScrollView(inner, ref categoryScroll, content);
            DrawCategoryRow(new Rect(0f, 0f, content.width, 28f), null, 0);
            for (int i = 0; i < categories.Count; i++)
                DrawCategoryRow(new Rect(0f, (i + 1) * 30f, content.width, 28f), categories[i].Item1, categories[i].Item2);
            Widgets.EndScrollView();
        }

        private void DrawCategoryRow(Rect rect, ThingCategoryDef cat, int depth)
        {
            if (category == cat) Widgets.DrawBoxSolid(rect, new Color(Accent.r, Accent.g, Accent.b, 0.18f));
            Rect text = rect;
            text.xMin += 6f + Math.Min(depth, 3) * 12f;
            string label = cat?.LabelCap.ToString() ?? T("DebugAllCategories");
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(text, label.Truncate(text.width));
            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.DrawHighlightIfMouseover(rect);
            TooltipHandler.TipRegion(rect, label);
            if (Widgets.ButtonInvisible(rect)) SetCategory(cat);
        }

        private static void Columns(Rect rect, out Rect name, out Rect source, out Rect stack, out Rect quantity, out Rect info)
        {
            info = new Rect(rect.xMax - 27f, rect.y + 9f, 24f, 24f);
            quantity = new Rect(info.x - 91f, rect.y + 6f, 80f, 30f);
            stack = new Rect(quantity.x - 79f, rect.y, 66f, rect.height);
            float remaining = stack.x - rect.x - 88f;
            float sourceWidth = Mathf.Clamp(remaining * 0.4f, 100f, 200f);
            source = new Rect(stack.x - sourceWidth - 12f, rect.y, sourceWidth, rect.height);
            name = new Rect(rect.x + 76f, rect.y, source.x - rect.x - 88f, rect.height);
        }

        private void DrawItems(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            Rect header = new Rect(rect.x + 8f, rect.y, rect.width - 32f, 36f);
            Columns(header, out Rect name, out Rect source, out Rect stack, out Rect quantity, out Rect info);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Muted;
            Widgets.Label(name, T("DebugItemName"));
            Widgets.Label(source, T("DebugItemSource"));
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(stack, T("DebugStackLimit"));
            Widgets.Label(new Rect(quantity.x, header.y, quantity.width, header.height), T("DebugQuantity"));
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            Rect viewport = new Rect(rect.x + 4f, rect.y + 36f, rect.width - 8f, rect.height - 40f);
            Rect content = new Rect(0f, 0f, viewport.width - 16f, Math.Max(viewport.height, visible.Count * RowHeight));
            Widgets.BeginScrollView(viewport, ref scroll, content);
            int first = Math.Max(0, (int)(scroll.y / RowHeight) - 1);
            int last = Math.Min(visible.Count, (int)((scroll.y + viewport.height) / RowHeight) + 2);
            for (int i = first; i < last; i++) DrawItem(new Rect(4f, i * RowHeight, content.width - 8f, RowHeight), visible[i], i);
            if (visible.Count == 0)
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = Muted;
                Widgets.Label(new Rect(0f, 0f, content.width, 90f), T("DebugNoItems"));
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;
            }
            Widgets.EndScrollView();
        }

        private void DrawItem(Rect rect, DebugSupplyEntry entry, int index)
        {
            if (entry.Included) Widgets.DrawBoxSolid(rect, new Color(Accent.r, Accent.g, Accent.b, 0.08f));
            else if (index % 2 == 0) Widgets.DrawBoxSolid(rect, new Color(1f, 1f, 1f, 0.025f));
            Widgets.DrawHighlightIfMouseover(rect);
            bool included = entry.Included;
            Widgets.Checkbox(rect.x + 5f, rect.y + 9f, ref included);
            if (included != entry.Included) { entry.Included = included; filterDirty = true; }
            Widgets.ThingIcon(new Rect(rect.x + 36f, rect.y + 5f, 32f, 32f), entry.Def, entry.Stuff);
            Columns(rect, out Rect name, out Rect source, out Rect stack, out Rect quantity, out Rect info);
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(name, entry.Def.LabelCap.ToString().Truncate(name.width));
            TooltipHandler.TipRegion(name, entry.Def.LabelCap + "\n" + entry.Def.defName);
            if (Widgets.ButtonInvisible(name)) { entry.Included = !entry.Included; filterDirty = true; }
            Text.Font = GameFont.Tiny;
            GUI.color = Muted;
            Widgets.Label(source, entry.ModName.Truncate(source.width));
            TooltipHandler.TipRegion(source, entry.ModName + "\n" + entry.PackageId);
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(stack, entry.Def.stackLimit.ToString(CultureInfo.InvariantCulture));
            Text.Anchor = TextAnchor.UpperLeft;
            bool invalid = entry.Included && !entry.TryGetQuantity(out _);
            if (invalid) GUI.color = new Color(1f, 0.55f, 0.45f);
            entry.QuantityText = Widgets.TextField(quantity, entry.QuantityText);
            GUI.color = Color.white;
            if (invalid) TooltipHandler.TipRegion(quantity, T("DebugInvalidQuantity"));
            if (entry.Stuff == null) Widgets.InfoCardButton(info, entry.Def);
            else Widgets.InfoCardButton(info.x, info.y, entry.Def, entry.Stuff);
        }

        private void DrawFooter(Rect inRect, float bodyBottom)
        {
            float y = bodyBottom + 8f;
            if (Widgets.ButtonText(new Rect(0f, y, 148f, 28f), T("DebugSelectResults"))) SetVisible(true);
            if (Widgets.ButtonText(new Rect(156f, y, 148f, 28f), T("DebugDeselectResults"))) SetVisible(false);
            if (Widgets.ButtonText(new Rect(inRect.width - 132f, y, 132f, 28f), T("DebugResetDefaults")))
            {
                foreach (DebugSupplyEntry entry in entries)
                {
                    entry.Included = entry.DefaultIncluded;
                    entry.QuantityText = entry.DefaultQuantity.ToString(CultureInfo.InvariantCulture);
                }
                resetMissingOverrides = true;
                filterDirty = true;
            }
            List<DebugSupplyEntry> chosen = entries.Where(e => e.Included).ToList();
            long total = 0, stacks = 0;
            bool valid = true;
            foreach (DebugSupplyEntry entry in chosen)
            {
                if (!entry.TryGetQuantity(out int count)) { valid = false; continue; }
                total += count;
                stacks += ((long)count + entry.Def.stackLimit - 1) / entry.Def.stackLimit;
            }
            GUI.color = valid ? Muted : new Color(1f, 0.55f, 0.45f);
            Widgets.Label(new Rect(0f, bodyBottom + 44f, inRect.width, 25f), valid
                ? "LogSyncMod.DebugSupplySummary".Translate(chosen.Count, total, stacks).ToString()
                : T("DebugInvalidQuantity"));
            GUI.color = Color.white;
            y = inRect.height - 38f;
            if (Widgets.ButtonText(new Rect(0f, y, 100f, 36f), T("DebugCancel"))) Close();
            if (Widgets.ButtonText(new Rect(inRect.width - 316f, y, 138f, 36f), T("DebugSavePreset"), active: valid))
            {
                DebugSupplyPreset.Save(entries, resetMissingOverrides);
                resetMissingOverrides = false;
                Messages.Message(T("DebugPresetSaved"), MessageTypeDefOf.NeutralEvent, historical: false);
            }
            GUI.color = Accent;
            if (Widgets.ButtonText(new Rect(inRect.width - 166f, y, 166f, 36f), T("DebugChooseLocation"), active: valid && chosen.Count > 0))
            {
                DebugSupplyPreset.Save(entries, resetMissingOverrides);
                List<DebugSupplyRequest> requests = chosen.Select(e => new DebugSupplyRequest(e.Def, e.Stuff, int.Parse(e.QuantityText, CultureInfo.InvariantCulture))).ToList();
                Close();
                DebugSupplySpawner.Start(map, requests);
            }
            GUI.color = Color.white;
        }

        private void SetVisible(bool included)
        {
            foreach (DebugSupplyEntry entry in visible) entry.Included = included;
            filterDirty = true;
        }
    }
}
