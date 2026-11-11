using System;
using System.Linq;
using System.Xml.Linq;
using Verse;

namespace LogSyncMod.AutoTest
{
    internal sealed partial class Dialog_ArenaWorkbench
    {
        private void NormalizeApparel()
        {
            foreach (var team in document.Teams.Where(t => t.Enabled && t.Source == "groups" && !t.IsEmpty))
            foreach (var member in team.Members.Where(m => m.Count > 0))
            {
                var xml = member.Read(); ArenaXenotypeSelection.EnsureGenes(xml);
                member.PawnXml = ArenaWorkbenchApparel.Normalize(xml.ToString(), team.Faction, document.Schedule.Seed);
            }
        }
        private void OpenApparelPicker(string slot, XElement removed)
        {
            var target = Member;
            var session = new ArenaWorkbenchApparel(target.Read(), Team.Faction, document.Schedule.Seed);
            try
            {
                var choices = Dialog_ArenaDefPicker.Catalog("apparel").Where(c => slot == "附加" || c.Definition is ThingDef d && ApparelSlot(d) == slot);
                Find.WindowStack.Add(new Dialog_ArenaDefPicker("选择" + slot + "装备 · 原生穿戴检查", choices, false, null, picked => {
                    if (picked.Count != 1) return;
                    var result = session.Choose((ThingDef)picked[0].Definition, removed, d => ItemNode(d, "Add"), out var replaced);
                    target.PawnXml = result.ToString(); Changed();
                    notice = replaced.Count == 0 ? "装备已更新，预览将自动刷新。" : "按原生穿戴规则替换冲突服装：" + string.Join("、", replaced);
                }, rejection: c => session.Rejection((ThingDef)c.Definition), cleanup: session.Dispose));
            }
            catch { session.Dispose(); throw; }
        }
    }
}
