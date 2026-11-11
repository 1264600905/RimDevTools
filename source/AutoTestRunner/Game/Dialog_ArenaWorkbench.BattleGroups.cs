using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace LogSyncMod.AutoTest
{
    internal sealed partial class Dialog_ArenaWorkbench
    {
        private Vector2 battleGroupScroll;
        private void DrawBattleGroupMembers(Rect area)
        {
            var faction = DefDatabase<FactionDef>.GetNamedSilentFail(Team.Faction);
            var makers = faction?.pawnGroupMakers;
            var index = Team.GroupMakerIndex;
            var groups = makers?.Select((m, i) => (maker: m, index: i)).Where(v => v.maker.kindDef?.defName == "Combat" && ArenaBattleGroupCatalog.Options(v.maker).Any() && (!index.HasValue || index == v.index)).ToList();
            if (groups == null || groups.Count == 0) { Label(area, "选择一个派系战斗组后，这里显示候选成员。", muted); return; }
            var options = groups.SelectMany(g => ArenaBattleGroupCatalog.Options(g.maker).Select(o => (option: o, index: g.index))).ToList();
            Label(new Rect(area.x, area.y, area.width, 28), "战斗组候选成员 · 原生选择权重", gold);
            var list = new Rect(area.x, area.y + 39, area.width, Math.Max(30, area.height - 39));
            var columns = Math.Max(1, (int)(list.width / 370));
            var width = (list.width - 18 - (columns - 1) * 10) / columns;
            var view = new Rect(0, 0, list.width - 18, (float)Math.Ceiling(options.Count / (double)columns) * 164);
            Widgets.BeginScrollView(list, ref battleGroupScroll, view);
            for (var i = 0; i < options.Count; i++)
            {
                var y = (i / columns) * 164f;
                if (y + 155 < battleGroupScroll.y || y > battleGroupScroll.y + list.height) continue;
                var x = (i % columns) * (width + 10); var pair = options[i]; var option = pair.option; var kind = option.kind;
                var template = new XElement("PawnTemplate", new XAttribute("id", "pawn"), new XElement("basePawnKind", kind.defName));
                if (Team.Xenotype != null && kind.RaceProps.Humanlike) template.Add(new XElement("Xenotype", new XAttribute("def", Team.Xenotype)));
                var sample = new ArenaWorkbenchMember { Name = kind.LabelCap, Count = 1, PawnXml = template.ToString() };
                var card = new Rect(x, y, width, 155); ArenaWorkbenchUi.Box(card, ArenaWorkbenchUi.Field, ArenaWorkbenchUi.Border);
                var portrait = new Rect(x + 7, y + 8, 83, 104);
                var entry = DrawPortrait(portrait, sample, false, Team.Faction);
                Label(new Rect(x + 101, y + 8, width - 110, 28), kind.LabelCap, gold);
                Label(new Rect(x + 101, y + 37, width - 110, 24), "权重 " + option.selectionWeight.ToString("0.##") + " · 组 #" + pair.index, muted, GameFont.Tiny);
                var weapons = ArenaBattleGroupCatalog.Weapons(kind);
                var weapon = weapons.Count == 0 ? "无 / 原生武器" : string.Join("、", weapons.Select(w => w.LabelCap.ToString()));
                if (entry?.Weapon != null) ArenaWorkbenchUi.DefIcon(new Rect(x + 101, y + 67, 24, 24), DefDatabase<ThingDef>.GetNamedSilentFail(entry.Weapon));
                Label(new Rect(x + 101, y + 66, width - 110, 35), "武器候选：" + weapon, muted, GameFont.Tiny);
                TooltipHandler.TipRegion(new Rect(x + 101, y + 66, width - 110, 35), "原生标签与价格范围内的候选，最多 3 种：" + weapon);
                if (Button(new Rect(x + 10, y + 117, width - 20, 28), "复制为可修改的角色模板"))
                {
                    Protect(() => {
                        var captured = entry; var release = false;
                        try
                        {
                            if (captured?.CapturedXml == null) { captured = ArenaWorkbenchPortraits.Capture(sample, Team.Faction, document.Schedule.Seed, showHeadgear); release = true; }
                            if (captured.CapturedXml == null) throw new InvalidOperationException(captured.Error ?? "成员生成失败，未创建模板。");
                            document.PawnPresets.Add(new ArenaWorkbenchMember { Name = kind.LabelCap, Count = 1, PawnXml = captured.CapturedXml });
                            notice = "已复制“" + kind.LabelCap + "”的实际配装、技能等数据到角色模板。可在人员模式使用副本并修改。";
                        }
                        finally { if (release) ArenaWorkbenchPortraits.Release(captured); }
                    });
                }
            }
            Widgets.EndScrollView();
        }
    }
}
