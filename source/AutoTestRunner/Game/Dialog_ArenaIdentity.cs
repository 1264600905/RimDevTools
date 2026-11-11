using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace LogSyncMod.AutoTest
{
    internal sealed class Dialog_ArenaIdentity : Window
    {
        private readonly XElement original;
        private readonly Action<XElement> apply;
        private bool nonHuman;
        private string search = "", mod, error;
        private Vector2 scroll;
        private Def selected;
        private ThingDef humanRace;
        public override Vector2 InitialSize => new(Math.Min(1050, UI.screenWidth - 24), Math.Min(780, UI.screenHeight - 24));
        internal Dialog_ArenaIdentity(XElement original, Action<XElement> apply)
        {
            this.original = new XElement(original); this.apply = apply;
            var caps = ArenaPawnCapabilities.For(original); nonHuman = !caps.Human;
            humanRace = caps.Human ? caps.Race : ThingDefOf.Human;
            doCloseX = true; closeOnAccept = false; forcePause = true; absorbInputAroundWindow = true;
        }
        public override void DoWindowContents(Rect rect)
        {
            ArenaWorkbenchUi.Label(new Rect(0, 0, rect.width - 45, 30), "选择人种 / 非人单位", ArenaWorkbenchUi.Gold, GameFont.Medium);
            var w = (rect.width - 10) / 2;
            if (ArenaWorkbenchUi.Button(new Rect(0, 42, w, 34), "人种 · 人类", !nonHuman)) { nonHuman = false; selected = null; scroll = Vector2.zero; }
            if (ArenaWorkbenchUi.Button(new Rect(w + 10, 42, w, 34), "非人单位 · 动物 / 机械族", nonHuman)) { nonHuman = true; selected = null; scroll = Vector2.zero; }
            search = Widgets.TextField(new Rect(0, 89, rect.width - 225, 30), search);
            var all = nonHuman ? Dialog_ArenaDefPicker.Catalog("pawnKind").Where(c => c.Definition is PawnKindDef k && !k.RaceProps.Humanlike).Select(c => c.Definition).ToList()
                : ModsConfig.BiotechActive ? DefDatabase<XenotypeDef>.AllDefsListForReading.Cast<Def>().ToList() : new List<Def> { ThingDefOf.Human };
            if (ArenaWorkbenchUi.Button(new Rect(rect.width - 212, 89, 212, 30), mod ?? "全部模组"))
                Find.WindowStack.Add(new FloatMenu(new[] { new FloatMenuOption("全部模组", () => mod = null) }.Concat(all.Select(d => d.modContentPack?.Name ?? "核心").Distinct().OrderBy(s => s).Select(s => new FloatMenuOption(s, () => mod = s))).ToList()));
            var top = 132f;
            if (!nonHuman)
            {
                if (ArenaWorkbenchUi.Button(new Rect(0, top, rect.width, 30), "人类种族：" + humanRace.LabelCap))
                    Find.WindowStack.Add(new Dialog_ArenaDefPicker("选择人类种族", Dialog_ArenaDefPicker.Catalog("race").Where(c => c.Definition is ThingDef d && d.race.Humanlike), false, null, values => { if (values.Count > 0) humanRace = (ThingDef)values[0].Definition; }));
                top += 43;
            }
            var filtered = all.Where(d => (mod == null || (d.modContentPack?.Name ?? "核心") == mod) && (string.IsNullOrWhiteSpace(search) || (d.label + " " + d.defName + " " + d.modContentPack?.Name).IndexOf(search, StringComparison.CurrentCultureIgnoreCase) >= 0)).OrderBy(d => d.label).ToList();
            var area = new Rect(0, top, rect.width, rect.height - top - 86);
            var view = new Rect(0, 0, area.width - 18, filtered.Count * 62);
            Widgets.BeginScrollView(area, ref scroll, view);
            var start = Math.Max(0, (int)(scroll.y / 62));
            for (var i = start; i < Math.Min(filtered.Count, start + (int)(area.height / 62) + 2); i++)
            {
                var def = filtered[i]; var row = new Rect(0, i * 62, view.width, 56);
                ArenaWorkbenchUi.Box(row, def == selected ? new Color(.2f, .22f, .18f) : ArenaWorkbenchUi.Field, def == selected ? ArenaWorkbenchUi.Gold : ArenaWorkbenchUi.Border);
                ArenaWorkbenchUi.DefIcon(new Rect(9, row.y + 7, 40, 40), def);
                ArenaWorkbenchUi.Label(new Rect(62, row.y + 1, row.width - 76, 27), def.LabelCap);
                var info = def is XenotypeDef x ? "自带基因：" + string.Join("、", x.genes.Take(5).Select(g => g.LabelCap.ToString())) : "非人单位 · " + def.defName;
                ArenaWorkbenchUi.Label(new Rect(62, row.y + 28, row.width - 76, 24), info, ArenaWorkbenchUi.Muted, GameFont.Tiny);
                TooltipHandler.TipRegion(row, def.description ?? "");
                if (Widgets.ButtonInvisible(row)) selected = def;
            }
            Widgets.EndScrollView();
            ArenaWorkbenchUi.Label(new Rect(0, rect.height - 76, rect.width, 32), error ?? (nonHuman ? "非人选择同时更新兵种，清理不适用的人类配置。" : "应用时自动写入人种自带基因，保留其他自定义基因。"), ArenaWorkbenchUi.Muted, GameFont.Tiny);
            if (ArenaWorkbenchUi.Button(new Rect(rect.width - 140, rect.height - 34, 140, 31), "应用选择", true, selected != null))
            {
                try
                {
                    XElement result;
                    if (selected is XenotypeDef xeno)
                    {
                        var old = DefDatabase<XenotypeDef>.GetNamedSilentFail((string)original.Element("Xenotype")?.Attribute("def") ?? "");
                        result = ArenaIdentityPolicy.SelectHuman(original, humanRace.defName, xeno.defName, xeno.genes.Select(g => g.defName), xeno.inheritable, old?.genes.Select(g => g.defName));
                        if (ArenaPawnCapabilities.For(original).Kind?.RaceProps.Humanlike != true) result.SetElementValue("basePawnKind", "Colonist");
                    }
                    else if (selected is ThingDef race)
                    {
                        result = new XElement(original); result.SetElementValue("race", race.defName); result.Element("Xenotype")?.Remove(); result.Element("Genes")?.Remove();
                        if (ArenaPawnCapabilities.For(original).Kind?.RaceProps.Humanlike != true) result.SetElementValue("basePawnKind", "Colonist");
                    }
                    else
                    {
                        var kind = (PawnKindDef)selected;
                        result = ArenaIdentityPolicy.SelectNonHuman(original, kind.defName, kind.race.defName, kind.RaceProps.ToolUser, kind.RaceProps.ShouldHaveAbilityTracker || !kind.abilities.NullOrEmpty() || ModsConfig.BiotechActive && kind.RaceProps.IsMechanoid);
                    }
                    ArenaWorkbenchDefinitions.ValidatePawn(result); apply(result); Close();
                }
                catch (Exception ex) { error = ex.Message; }
            }
        }
    }
}
