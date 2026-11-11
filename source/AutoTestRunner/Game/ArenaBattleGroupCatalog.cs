using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace LogSyncMod.AutoTest
{
    internal static class ArenaBattleGroupCatalog
    {
        internal static bool LivingKind(PawnKindDef kind) => kind?.race?.race != null && !kind.race.IsCorpse && typeof(Pawn).IsAssignableFrom(kind.race.thingClass);
        internal static IEnumerable<PawnGenOption> Options(PawnGroupMaker maker) => (maker?.options ?? new List<PawnGenOption>())
            .Where(o => LivingKind(o?.kind) && float.IsFinite(o.selectionWeight) && o.selectionWeight > 0 && o.kind.isFighter);
        internal static List<ThingDef> Weapons(PawnKindDef kind) => !kind.RaceProps.ToolUser || kind.weaponTags.NullOrEmpty() ? new()
            : DefDatabase<ThingDef>.AllDefsListForReading.Where(d => d.IsWeapon && !d.weaponTags.NullOrEmpty() && d.weaponTags.Any(kind.weaponTags.Contains)
                && d.generateCommonality > 0 && d.generateAllowChance > 0 && (d.MadeFromStuff || d.BaseMarketValue <= kind.weaponMoney.max))
                .OrderByDescending(d => d.generateCommonality).ThenBy(d => d.defName, StringComparer.Ordinal).Take(3).ToList();
        internal static string Summary(PawnGroupMaker maker)
        {
            var options = Options(maker).ToList();
            return "战斗单位（最多显示前 5 个）\n" + string.Join("\n", options.Take(5).Select(o => {
                var weapons = Weapons(o.kind);
                return o.kind.LabelCap + " · 权重 " + o.selectionWeight.ToString("0.##") + "\n武器候选：" + (weapons.Count == 0 ? "无 / 原生配置" : string.Join("、", weapons.Select(w => w.LabelCap.ToString())));
            })) + (options.Count > 5 ? "\n…另有 " + (options.Count - 5) + " 种" : "") + "\n武器按原生标签及价格上限筛选，最多 3 种；实际装备由生成器确定。";
        }
    }
}
