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
    internal sealed partial class Dialog_ArenaWorkbench
    {
        private string geneLayer = "false";
        private readonly Dictionary<string, List<ArenaDefChoice>> definitionCache = new();
        private void Pick(string type, Action<Def> apply, Func<ArenaDefChoice, bool> filter = null)
        { PickMany(type, values => { if (values.Count > 0) apply(values[0].Definition); }, false, filter); }
        private void PickMany(string type, Action<List<ArenaDefChoice>> apply, bool multiple = true, Func<ArenaDefChoice, bool> filter = null)
        {
            if (!definitionCache.TryGetValue(type, out var list)) definitionCache[type] = list = Dialog_ArenaDefPicker.Catalog(type);
            var names = new Dictionary<string, string> { ["weapon"] = "武器", ["apparel"] = "服装与护甲", ["trait"] = "特性", ["gene"] = "基因", ["hediff"] = "Hediff / 健康状态", ["ability"] = "能力", ["race"] = "种族", ["pawnKind"] = "兵种", ["stuff"] = "材质", ["faction"] = "派系", ["xenotype"] = "人种 / 异种", ["weather"] = "天气", ["battleGroup"] = "派系战斗组" };
            Find.WindowStack.Add(new Dialog_ArenaDefPicker("选择" + names[type], list.Where(x => filter == null || filter(x)), multiple, null, values => { apply(values); Changed(); }));
        }
        private void EditMember(Action<XElement> edit)
        {
            if (Member == null) return;
            var candidate = ArenaWorkbench.Copy(Member); ArenaWorkbench.ReadAndChange(candidate, edit);
            var xml = candidate.Read(); ArenaXenotypeSelection.EnsureGenes(xml); candidate.PawnXml = xml.ToString();
            ArenaWorkbench.ValidateMember(candidate);
            Member.PawnXml = candidate.PawnXml; Changed();
        }
        private static XElement Section(XElement p, string name)
        {
            var e = p.Element(name);
            if (e == null) { e = new XElement(name, new XAttribute("mode", "patch")); p.Add(e); }
            else if ((string)e.Attribute("mode") == "inherit") e.SetAttributeValue("mode", "patch");
            return e;
        }
        private static void Scalar(XElement p, string name, string value)
        { var old = p.Element(name); if (value == null) old?.Remove(); else if (old != null) old.Value = value; else p.Add(new XElement(name, value)); }
        private static string LabelOf<T>(string def) where T : Def, new() => def == null ? "原生 / 未指定" : DefDatabase<T>.GetNamedSilentFail(def)?.LabelCap.ToString() ?? def;
        private static XElement ItemNode(ThingDef def, string tag)
        {
            var node = new XElement(tag, new XAttribute("def", def.defName));
            if (def.MadeFromStuff) node.Add(new XAttribute("stuff", GenStuff.DefaultStuffFor(def).defName));
            if (def.comps?.Any(c => c.compClass == typeof(CompQuality)) == true) node.Add(new XAttribute("quality", "Normal"));
            return node;
        }
        private bool IconRemove(Form form, Def def, string name)
        {
            if (def != null) { try { ArenaWorkbenchUi.DefIcon(new Rect(form.X, form.Y, 28, 28), def); } catch { } }
            Label(new Rect(form.X + 35, form.Y, form.Width - 76, 30), name);
            var clicked = Button(new Rect(form.X + form.Width - 35, form.Y, 32, 28), "×"); form.Y += 36; return clicked;
        }
        private void DrawHediffs(Form form, XElement p)
        {
            var index = 0;
            foreach (var item in p.Element("Hediffs")?.Elements().ToList() ?? new List<XElement>())
            {
                var n = index++; var def = (string)item.Attribute("def");
                if (IconRemove(form, DefDatabase<HediffDef>.GetNamedSilentFail(def), LabelOf<HediffDef>(def))) { item.Remove(); continue; }
                if (item.Name == "Remove") continue;
                var sev = (double?)item.Attribute("severity");
                if (form.Check("指定严重度 · " + n, sev.HasValue)) item.SetAttributeValue("severity", form.Number("hediff" + n, "严重度", sev ?? .5, 0, float.MaxValue));
                else item.Attribute("severity")?.Remove();
                if (form.Action("身体部位：" + ((string)item.Attribute("part") ?? "全身 / 不指定")))
                {
                    var copy = new XElement(item);
                    void Set(string value) => EditMember(xml => xml.Element("Hediffs")?.Elements().FirstOrDefault(e => XNode.DeepEquals(e, copy))?.SetAttributeValue("part", value));
                    var race = DefDatabase<ThingDef>.GetNamedSilentFail(p.Element("race")?.Value ?? "") ?? DefDatabase<PawnKindDef>.GetNamedSilentFail(p.Element("basePawnKind")?.Value ?? "")?.race;
                    var options = new List<FloatMenuOption> { new("全身 / 不指定", () => Set(null)) };
                    foreach (var part in race?.race?.body?.AllParts ?? Enumerable.Empty<BodyPartRecord>())
                    {
                        string Path(BodyPartRecord r) => r.parent == null ? r.def.defName : Path(r.parent) + "/" + r.def.defName + "[" + r.parent.parts.IndexOf(r) + "]";
                        var path = Path(part); options.Add(new FloatMenuOption(part.LabelCap + " · " + path, () => Set(path)));
                    }
                    Find.WindowStack.Add(new FloatMenu(options));
                }
                var duration = (int?)item.Attribute("durationTicks");
                if (form.Check("指定持续 ticks · " + n, duration.HasValue)) item.SetAttributeValue("durationTicks", (int)form.Number("hediffDuration" + n, "持续 ticks", duration ?? 600, 1, int.MaxValue, true));
                else item.Attribute("durationTicks")?.Remove();
            }
        }
        private static XElement AddSkill(XElement p, string name)
        { var node = new XElement("Set", new XAttribute("def", name), new XAttribute("level", 0)); Section(p, "Skills").Add(node); return node; }
    }

    internal sealed class Dialog_ArenaXml : Window
    {
        private string value, error; private Vector2 scroll; private readonly Action<string> apply;
        public override Vector2 InitialSize => new(Math.Min(1050, UI.screenWidth - 24), Math.Min(800, UI.screenHeight - 24));
        internal Dialog_ArenaXml(string value, Action<string> apply) { this.value = value; this.apply = apply; forcePause = true; doCloseX = true; closeOnAccept = false; absorbInputAroundWindow = true; }
        public override void DoWindowContents(Rect rect)
        {
            Widgets.Label(new Rect(0, 0, rect.width, 29), "编辑 PawnTemplate XML · 保留现有语法，应用前校验");
            var area = new Rect(0, 40, rect.width, rect.height - 130); var view = new Rect(0, 0, area.width - 18, Math.Max(area.height, Text.CalcHeight(value, area.width - 30) + 40));
            Widgets.BeginScrollView(area, ref scroll, view); value = Widgets.TextArea(view, value); Widgets.EndScrollView();
            Widgets.Label(new Rect(0, rect.height - 79, rect.width, 35), error ?? "可复制给 AI 修改，再将独立 PawnTemplate 粘贴回来。");
            if (ArenaWorkbenchUi.Button(new Rect(0, rect.height - 37, 155, 30), "复制 XML")) GUIUtility.systemCopyBuffer = value;
            if (ArenaWorkbenchUi.Button(new Rect(166, rect.height - 37, 155, 30), "粘贴剪贴板")) value = GUIUtility.systemCopyBuffer;
            if (ArenaWorkbenchUi.Button(new Rect(rect.width - 150, rect.height - 37, 150, 30), "校验并应用")) { try { apply(value); Close(); } catch (Exception ex) { error = ex.Message; } }
        }
    }
}
