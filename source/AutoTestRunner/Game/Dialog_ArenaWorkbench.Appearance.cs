using System;
using System.Collections.Generic;
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
        private readonly ArenaWorkbenchPortraits portraits = new();
        private Vector2 identityScroll, skillsScroll;
        private float editorContentHeight = 1150;
        private bool showHeadgear;
        private bool portraitToggleDrawn;
        private void DisposePreview() => portraits.Dispose();
        private ArenaWorkbenchPortraits.Entry DrawPortrait(Rect rect, ArenaWorkbenchMember member, bool detail, string faction = null)
        {
            ArenaWorkbenchUi.Box(rect, new Color(.065f, .085f, .098f));
            var xml = member.Read();
            if (!ArenaWorkbenchPortraits.HumanBody(member))
            {
                var race = DefDatabase<ThingDef>.GetNamedSilentFail(xml.Element("race")?.Value ?? "")
                    ?? DefDatabase<PawnKindDef>.GetNamedSilentFail(xml.Element("basePawnKind")?.Value ?? "")?.race;
                ArenaWorkbenchUi.DefIcon(rect.ContractedBy(Math.Min(rect.width * .15f, 20)), race); return null;
            }
            var entry = portraits.Request(member, faction ?? Team?.Faction ?? "Pirate", document.Schedule.Seed, showHeadgear);
            if (entry.NormalizedXml != null && !XNode.DeepEquals(xml, ArenaWorkbench.ReadXml(entry.NormalizedXml))) member.PawnXml = entry.NormalizedXml;
            if (entry.Texture != null) GUI.DrawTexture(rect, entry.Texture, ScaleMode.ScaleToFit);
            else
            {
                var kind = DefDatabase<PawnKindDef>.GetNamedSilentFail(member.Read().Element("basePawnKind")?.Value ?? "");
                ArenaWorkbenchUi.DefIcon(rect.ContractedBy(Math.Min(rect.width * .2f, 28)), kind);
                if (detail) Label(new Rect(rect.x + 4, rect.yMax - 27, rect.width - 8, 23), entry.Error == null ? "正在生成预览…" : "预览暂不可用", muted, GameFont.Tiny, TextAnchor.MiddleCenter);
            }
            if (entry.Error != null) TooltipHandler.TipRegion(rect, entry.Error);
            return entry;
        }
        private void HeadgearToggle(Rect rect)
        {
            portraitToggleDrawn = true;
            if (Button(rect, showHeadgear ? "显示头盔：开" : "显示头盔：关", showHeadgear)) showHeadgear = !showHeadgear;
        }
        private void DrawPreview(Rect rect)
        {
            if (Member == null || Team.Source != "groups") { Label(rect, "选择一个成员查看角色", muted); return; }
            var portrait = new Rect(rect.x, rect.y, rect.width, Math.Min(rect.width * 1.15f, rect.height - 115));
            DrawPortrait(portrait, Member, true);
            Label(new Rect(rect.x, portrait.yMax + 12, rect.width, 31), Member.Name, gold);
            var p = Member.Read(); var weapon = (string)p.Element("Equipment")?.Element("SetPrimary")?.Attribute("def");
            var def = DefDatabase<ThingDef>.GetNamedSilentFail(weapon ?? "");
            if (def != null) ArenaWorkbenchUi.DefIcon(new Rect(rect.x + 3, portrait.yMax + 54, 28, 28), def);
            Label(new Rect(rect.x + 38, portrait.yMax + 52, rect.width - 38, 30), weapon == null ? "继承兵种原生武器" : LabelOf<ThingDef>(weapon), muted);
            Label(new Rect(rect.x, portrait.yMax + 88, rect.width, 25), "修改配置后自动刷新", muted, GameFont.Tiny);
            HeadgearToggle(new Rect(rect.x, portrait.yMax + 121, rect.width, 29));
        }
        private void DrawEditor(Rect area)
        {
            if (Member == null || Team.Source != "groups")
            {
                Label(new Rect(area.x + 24, area.y + 25, area.width - 48, 45), "请在人员模式中选择一个角色，再编辑其配装、技能与基因。", muted);
                if (Button(new Rect(area.x + 24, area.y + 85, 220, 34), "前往编队管理 →")) page = 1;
                return;
            }
            var p = Member.Read(); var before = p.ToString();
            ArenaXenotypeSelection.EnsureGenes(p);
            var capabilities = ArenaPawnCapabilities.For(p);
            var leftW = Math.Min(275, area.width * .225f); var rightW = Math.Min(340, area.width * .28f);
            var left = new Rect(area.x, area.y, leftW, area.height);
            var middle = new Rect(left.xMax + 18, area.y, area.width - leftW - rightW - 36, area.height);
            var right = new Rect(middle.xMax + 18, area.y, rightW, area.height);
            ArenaWorkbenchUi.Panel(left); ArenaWorkbenchUi.Panel(middle); ArenaWorkbenchUi.Panel(right);
            Label(new Rect(left.x + 16, left.y + 10, left.width - 32, 29), "角色预览", gold);
            var portraitH = Math.Min(300, Math.Max(170, area.height * .36f));
            DrawPortrait(new Rect(left.x + 14, left.y + 49, left.width - 28, portraitH), Member, true);
            HeadgearToggle(new Rect(left.x + 14, left.y + portraitH + 57, left.width - 28, 29));
            var identity = new Rect(left.x + 14, left.y + portraitH + 101, left.width - 28, left.height - portraitH - 114);
            var iv = new Rect(0, 0, identity.width - 16, 465);
            Widgets.BeginScrollView(identity, ref identityScroll, iv);
            Label(new Rect(0, 0, iv.width, 23), "成员组名称", muted, GameFont.Tiny);
            Member.Name = Widgets.TextField(new Rect(0, 28, iv.width, 30), Member.Name);
            var f = new Form(this, 0, iv.width, 72);
            if (capabilities.Gender)
            {
            var g = p.Element("gender")?.Value ?? "inherit";
            var ng = f.Choice("性别", g, "inherit|原生生成", "Female|女性", "Male|男性", "None|无性别");
            if (ng != g) Scalar(p, "gender", ng == "inherit" ? null : ng);
            }
            var age = p.Element("Age"); var ageValue = (double?)age?.Attribute("biologicalYears");
            if (f.Check("指定生理年龄", ageValue.HasValue))
            { var years = f.Number("pawnAge", "年龄（年）", ageValue ?? 28, 0, 100000); if (age == null) { age = new XElement("Age"); p.Add(age); } age.SetAttributeValue("biologicalYears", years); }
            else if (age != null) { age.Attribute("biologicalYears")?.Remove(); if (!age.HasAttributes) age.Remove(); }
            f.Title("人种 / 非人单位");
            if (f.Action(capabilities.Human ? "人种：" + (p.Element("Xenotype") == null ? "继承原生" : LabelOf<XenotypeDef>((string)p.Element("Xenotype").Attribute("def"))) : "非人单位：" + capabilities.Kind.LabelCap))
                Find.WindowStack.Add(new Dialog_ArenaIdentity(Member.Read(), value => { Member.PawnXml = value.ToString(); Changed(); }));
            f.Note("兵种在编队管理中设置。", 30);
            Widgets.EndScrollView();
            Label(new Rect(middle.x + 18, middle.y + 10, middle.width - 185, 31), "武器与装备", gold);
            if (Button(new Rect(middle.xMax - 158, middle.y + 10, 142, 30), "复制角色 XML")) CopyXml(ArenaWorkbench.PawnTemplate(Member, "pawn").ToString());
            var content = new Rect(middle.x + 16, middle.y + 52, middle.width - 32, middle.height - 67);
            var view = new Rect(0, 0, content.width - 18, Math.Max(content.height, editorContentHeight));
            Widgets.BeginScrollView(content, ref editorScroll, view);
            var width = view.width; var y = 0f;
            if (capabilities.Equipment) DrawEquipmentTable(p, width, ref y);
            if (capabilities.Human)
            {
            SectionHeader(p, "Traits", "特性", width, ref y);
            DrawChips(p, "Traits", width, ref y);
            if (Button(new Rect(0, y, width, 31), "＋ 从特性库选择…")) PickMany("trait", values => {
                if (values.GroupBy(v => v.Definition.defName).Any(v => v.Count() > 1)) throw new ScenarioConfigurationException("同一特性只能选一个档位。");
                EditMember(x => { var section = Section(x, "Traits"); foreach (var c in values) { section.Elements().Where(e => (string)e.Attribute("def") == c.Definition.defName).Remove(); section.Add(new XElement("Set", new XAttribute("def", c.Definition.defName), new XAttribute("degree", c.Degree ?? 0))); } });
            });
            y += 47;
            }
            if (capabilities.Genes)
            {
            SectionHeader(p, "Genes", "基因", width, ref y);
            if (Button(new Rect(0, y, 116, 30), "内源基因", geneLayer == "false")) geneLayer = "false";
            if (Button(new Rect(126, y, 116, 30), "外源基因", geneLayer == "true")) geneLayer = "true";
            y += 40; DrawChips(p, "Genes", width, ref y, e => e.Name == "Remove" || ((string)e.Attribute("xenogene") ?? "false") == geneLayer);
            if (Button(new Rect(0, y, width, 31), "＋ 从基因库选择…"))
            {
                var layer = geneLayer; PickMany("gene", values => EditMember(x => { var section = Section(x, "Genes"); foreach (var c in values)
                if (!section.Elements("Add").Any(e => (string)e.Attribute("def") == c.Definition.defName && ((string)e.Attribute("xenogene") ?? "false") == layer)) section.Add(new XElement("Add", new XAttribute("def", c.Definition.defName), new XAttribute("xenogene", layer))); }));
            }
            }
            y += 48; SectionHeader(p, "Hediffs", "健康状态与身体改造", width, ref y);
            var hf = new Form(this, 0, width, y); DrawHediffs(hf, p); y = hf.Y;
            if (Button(new Rect(0, y, width, 31), "＋ 选择 Hediff / 身体改造…")) PickMany("hediff", values => EditMember(x => { var section = Section(x, "Hediffs"); foreach (var c in values) section.Add(new XElement("Ensure", new XAttribute("def", c.Definition.defName), new XAttribute("severity", .5))); }));
            if (capabilities.Abilities)
            {
            y += 48; SectionHeader(p, "Abilities", "初始能力", width, ref y); DrawChips(p, "Abilities", width, ref y);
            if (Button(new Rect(0, y, width, 31), "＋ 从能力库选择…")) PickMany("ability", values => EditMember(x => { var section = Section(x, "Abilities"); foreach (var c in values) if (!section.Elements().Any(e => (string)e.Attribute("def") == c.Definition.defName)) section.Add(new XElement("Ensure", new XAttribute("def", c.Definition.defName))); }));
            }
            y += 49;
            var unsupported = capabilities.Unsupported(p);
            if (unsupported.Count > 0)
            {
                Label(new Rect(0, y, width, 35), "不支持的历史配置：" + string.Join("、", unsupported), gold, GameFont.Tiny); y += 41;
                if (Button(new Rect(0, y, width, 29), "清除这些不支持的字段")) foreach (var field in unsupported) p.Element(field)?.Remove();
                y += 39;
            }
            if (Button(new Rect(0, y, width, 31), "编辑 / 粘贴角色 XML…")) Find.WindowStack.Add(new Dialog_ArenaXml(Member.PawnXml, value => {
                var xml = ArenaWorkbench.ReadXml(value); TemporaryPawnData.Create(xml, "workbench", Array.Empty<string>(), Array.Empty<string>()); ArenaWorkbenchDefinitions.ValidatePawn(xml);
                // PawnKind remains a roster concern even when advanced XML is pasted.
                if (xml.Element("basePawnKind")?.Value != Member.Read().Element("basePawnKind")?.Value) throw new ScenarioConfigurationException("请在编队管理或人种 / 非人单位弹窗中修改兵种。");
                Member.PawnXml = xml.ToString(); Changed();
            }));
            editorContentHeight = y + 50;
            Widgets.EndScrollView();
            if (p.ToString() != before) { Member.PawnXml = p.ToString(); Changed(); }
            if (capabilities.Human) DrawSkills(right);
            else
            {
                Label(new Rect(right.x + 16, right.y + 20, right.width - 32, 65), "此单位没有人类技能与特性。仅展示原生支持的编辑内容。", muted);
                if (Button(new Rect(right.x + 16, right.y + 110, right.width - 32, 32), "另存为角色模板")) SaveCurrentPawnPreset();
            }
        }
        private void SectionHeader(XElement p, string name, string label, float width, ref float y)
        {
            Widgets.DrawBoxSolid(new Rect(0, y, width, 1), ArenaWorkbenchUi.Border);
            Label(new Rect(0, y + 11, width - 130, 29), label, gold);
            var mode = (string)p.Element(name)?.Attribute("mode") ?? "inherit";
            if (Button(new Rect(width - 123, y + 10, 123, 27), mode == "replace" ? "完全替换 ▾" : mode == "patch" ? "覆盖指定项 ▾" : "原生继承 ▾"))
                Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption> {
                    new("原生继承", () => EditMember(x => x.Element(name)?.Remove())),
                    new("覆盖指定项", () => EditMember(x => Section(x, name).SetAttributeValue("mode", "patch"))),
                    new("完全替换", () => EditMember(x => Section(x, name).SetAttributeValue("mode", "replace"))) }));
            y += 52;
        }
        private void DrawEquipmentTable(XElement p, float width, ref float y)
        {
            var used = new HashSet<XElement>();
            EquipmentRow("武器", "Equipment", p.Element("Equipment")?.Element("SetPrimary"), p, width, ref y);
            var nodes = p.Element("Apparel")?.Elements().Where(e => e.Name == "Add" || e.Name == "Replace").ToList() ?? new List<XElement>();
            var slots = new[] { "头部", "护甲", "贴身", "腿部", "腰带" };
            foreach (var slot in slots)
            {
                bool Matches(XElement item) => DefDatabase<ThingDef>.GetNamedSilentFail((string)item.Attribute("def") ?? (string)item.Attribute("withDef")) is ThingDef d && ApparelSlot(d) == slot;
                var node = nodes.FirstOrDefault(n => !used.Contains(n) && Matches(n));
                if (node != null) used.Add(node);
                EquipmentRow(slot, "Apparel", node, p, width, ref y);
            }
            foreach (var node in nodes.Where(n => !used.Contains(n))) EquipmentRow("附加", "Apparel", node, p, width, ref y);
            y += 8;
            if (Button(new Rect(0, y, (width - 8) / 2, 29), "原生配装")) { p.Element("Equipment")?.Remove(); p.Element("Apparel")?.Remove(); }
            if (Button(new Rect((width + 8) / 2, y, (width - 8) / 2, 29), "清空配装")) { p.Element("Equipment")?.Remove(); p.Element("Apparel")?.Remove(); p.Add(new XElement("Equipment", new XAttribute("mode", "replace")), new XElement("Apparel", new XAttribute("mode", "replace"))); }
            y += 48;
        }
        private static string ApparelSlot(ThingDef d)
        {
            if (d?.apparel == null) return "附加";
            if (d.apparel.layers.Any(l => l.defName == "Belt")) return "腰带";
            if (d.apparel.bodyPartGroups.Any(g => g.defName.Contains("Head"))) return "头部";
            if (d.apparel.layers.Any(l => l.defName is "Shell" or "Middle")) return "护甲";
            return d.apparel.bodyPartGroups.Any(g => g.defName.Contains("Leg")) ? "腿部" : "贴身";
        }
        private void EquipmentRow(string label, string section, XElement node, XElement p, float width, ref float y)
        {
            var def = DefDatabase<ThingDef>.GetNamedSilentFail((string)node?.Attribute("def") ?? (string)node?.Attribute("withDef") ?? "");
            Label(new Rect(0, y, 43, 33), label, muted, GameFont.Tiny);
            var field = new Rect(50, y, Math.Max(60, width - 186), 33);
            ArenaWorkbenchUi.Box(field, ArenaWorkbenchUi.Field, ArenaWorkbenchUi.Border);
            ArenaWorkbenchUi.DefIcon(new Rect(field.x + 6, y + 4, 25, 25), def);
            Label(new Rect(field.x + 39, y + 2, field.width - 44, 29), node == null ? (p.Element(section) == null ? "继承原生" : "未指定") : def?.LabelCap.ToString() ?? (string)node.Attribute("def"));
            if (GUI.enabled && Widgets.ButtonInvisible(field))
            {
                var original = node == null ? null : new XElement(node);
                if (section == "Apparel") Protect(() => OpenApparelPicker(label, original));
                else Pick("weapon", selected => EditMember(x => {
                    var s = Section(x, section);
                    if (original != null) s.Elements().FirstOrDefault(e => XNode.DeepEquals(e, original))?.Remove();
                    if (section == "Equipment") s.Elements("SetPrimary").Remove();
                    s.Add(ItemNode((ThingDef)selected, section == "Equipment" ? "SetPrimary" : "Add"));
                }));
            }
            var qr = new Rect(width - 128, y, 94, 33);
            var q = (string)node?.Attribute("quality");
            if (Button(qr, QualityLabel(q) + " ▾", active: node != null && def?.comps?.Any(c => c.compClass == typeof(CompQuality)) == true))
            {
                var original = new XElement(node);
                Find.WindowStack.Add(new FloatMenu(new[] { "Awful", "Poor", "Normal", "Good", "Excellent", "Masterwork", "Legendary" }.Select(value => new FloatMenuOption(QualityLabel(value), () => EditMember(x => x.Element(section)?.Elements().FirstOrDefault(e => XNode.DeepEquals(e, original))?.SetAttributeValue("quality", value)))).ToList()));
            }
            if (Button(new Rect(width - 28, y, 27, 33), "×", active: node != null)) node.Remove();
            y += 42;
            if (node?.Parent != null && def?.MadeFromStuff == true)
            {
                var original = new XElement(node);
                if (Button(new Rect(50, y, width - 50, 26), "材质 · " + LabelOf<ThingDef>((string)node.Attribute("stuff")) + " ▾"))
                    Pick("stuff", d => EditMember(x => x.Element(section)?.Elements().FirstOrDefault(e => XNode.DeepEquals(e, original))?.SetAttributeValue("stuff", d.defName)),
                        c => c.Definition is ThingDef stuff && stuff.stuffProps.categories.Any(cat => def.stuffCategories.Contains(cat)));
                y += 34;
            }
        }
        private static string QualityLabel(string q) => q switch { "Awful" => "极差", "Poor" => "较差", "Normal" => "普通", "Good" => "优秀", "Excellent" => "精良", "Masterwork" => "大师", "Legendary" => "传说", _ => "原生" };
        private void DrawChips(XElement p, string section, float width, ref float y, Func<XElement, bool> filter = null)
        {
            var nodes = p.Element(section)?.Elements().Where(e => filter == null || filter(e)).ToList() ?? new List<XElement>();
            var x = 0f;
            foreach (var e in nodes)
            {
                var name = (string)e.Attribute("def");
                Def def = section == "Traits" ? DefDatabase<TraitDef>.GetNamedSilentFail(name) : section == "Genes" ? DefDatabase<GeneDef>.GetNamedSilentFail(name) : DefDatabase<AbilityDef>.GetNamedSilentFail(name);
                var text = def?.LabelCap.ToString() ?? name;
                if (def is AbilityDef ability) text += " · AIcanuse=" + ability.aiCanUse.ToString().ToLowerInvariant();
                if (def is TraitDef t) text = t.degreeDatas.FirstOrDefault(d => d.degree == ((int?)e.Attribute("degree") ?? 0))?.label ?? text;
                var w = Math.Min(width, Math.Max(110, Text.CalcSize(text).x + 70));
                if (x + w > width) { x = 0; y += 38; }
                var rect = new Rect(x, y, w, 30); ArenaWorkbenchUi.Box(rect, new Color(.14f, .19f, .215f));
                ArenaWorkbenchUi.DefIcon(new Rect(x + 5, y + 3, 24, 24), def); Label(new Rect(x + 35, y, w - 59, 29), text);
                var requiredGene = section == "Genes" && ArenaXenotypeSelection.Required(p, name);
                if (Button(new Rect(x + w - 24, y + 2, 22, 26), requiredGene ? "·" : "×", active: !requiredGene)) e.Remove();
                if (requiredGene) TooltipHandler.TipRegion(rect, "所选人种自带基因；更换人种时更新，不能单独删除。");
                x += w + 7;
            }
            if (nodes.Count == 0) { Label(new Rect(0, y, width, 27), "尚未指定，沿用原生生成结果", muted, GameFont.Tiny); y += 35; }
            else y += 42;
        }
        private void DrawSkills(Rect rect)
        {
            Label(new Rect(rect.x + 16, rect.y + 10, rect.width - 32, 29), "技能", gold);
            var by = rect.y + 49; var bw = (rect.width - 48) / 3;
            var p = Member.Read(); var before = p.ToString(); var skills = DefDatabase<SkillDef>.AllDefsListForReading;
            foreach (var (value, index) in new[] { 10, 20, 0 }.Select((v, i) => (v, i)))
                if (Button(new Rect(rect.x + 16 + index * (bw + 8), by, bw, 29), value == 0 ? "清零" : "全部 " + value)) foreach (var d in skills) { var e = p.Element("Skills")?.Elements("Set").FirstOrDefault(e => (string)e.Attribute("def") == d.defName) ?? AddSkill(p, d.defName); e.SetAttributeValue("level", value); }
            var area = new Rect(rect.x + 14, by + 48, rect.width - 28, rect.height - 225); var view = new Rect(0, 0, area.width - 16, skills.Count * 37 + 45);
            Widgets.BeginScrollView(area, ref skillsScroll, view);
            for (var i = 0; i < skills.Count; i++)
            {
                var def = skills[i]; var node = p.Element("Skills")?.Elements("Set").FirstOrDefault(e => (string)e.Attribute("def") == def.defName);
                var level = (int?)node?.Attribute("level") ?? 0; var passion = (string)node?.Attribute("passion") ?? "None"; var y = i * 37;
                Label(new Rect(0, y, 48, 29), def.LabelCap);
                var pr = new Rect(50, y + 3, 23, 24);
                var old = GUI.color; GUI.color = passion == "None" ? muted * .5f : gold;
                GUI.DrawTexture(pr, passion == "Major" ? SkillUI.PassionMajorIcon : SkillUI.PassionMinorIcon, ScaleMode.ScaleToFit); GUI.color = old;
                if (GUI.enabled && Widgets.ButtonInvisible(pr)) { node ??= AddSkill(p, def.defName); node.SetAttributeValue("passion", passion == "None" ? "Minor" : passion == "Minor" ? "Major" : "None"); }
                var meter = new Rect(82, y + 8, Math.Max(12, view.width - 189), 13); Widgets.DrawBoxSolid(meter, ArenaWorkbenchUi.Field); Widgets.DrawBoxSolid(new Rect(meter.x, meter.y, meter.width * level / 20f, meter.height), new Color(.35f, .5f, .48f));
                if (Button(new Rect(view.width - 101, y + 1, 24, 27), "−")) { node ??= AddSkill(p, def.defName); node.SetAttributeValue("level", Math.Max(0, level - 1)); }
                var next = Widgets.TextField(new Rect(view.width - 72, y + 1, 41, 27), node == null ? "—" : level.ToString());
                if (int.TryParse(next, out var n) && n is >= 0 and <= 20 && (node == null || n != level)) { node ??= AddSkill(p, def.defName); node.SetAttributeValue("level", n); }
                if (Button(new Rect(view.width - 26, y + 1, 24, 27), "+")) { node ??= AddSkill(p, def.defName); node.SetAttributeValue("level", Math.Min(20, level + 1)); }
                TooltipHandler.TipRegion(new Rect(0, y, view.width, 30), "点击火焰切换热情；— 表示继承原生技能。等级范围 0–20。");
            }
            if (Button(new Rect(0, skills.Count * 37 + 5, view.width, 28), "全部恢复继承")) p.Element("Skills")?.Remove();
            Widgets.EndScrollView();
            if (p.ToString() != before) { Member.PawnXml = p.ToString(); Changed(); }
            if (Button(new Rect(rect.x + 16, rect.yMax - 111, rect.width - 32, 33), "另存为角色模板", true)) { var m = ArenaWorkbench.Copy(Member); m.Count = 1; document.PawnPresets.Add(m); notice = "角色模板已添加。"; }
            if (saveEdits == null && Button(new Rect(rect.x + 16, rect.yMax - 64, rect.width - 32, 33), "完成，返回编队")) page = 1;
        }
    }
}
