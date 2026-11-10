using System;
using System.Collections.Generic;
using System.Linq;

namespace LogSyncMod.AutoTest
{
    public sealed class ManualTestBrowserFilter
    {
        public string Search = "", Package, Type, ThingCategory;
        public string Source = "definitions";
        public bool ThirdPartyOnly, SelectedOnly, TestableOnly = true;
    }

    public static class ManualTestBrowser
    {
        public static List<ManualTestTarget> Filter(IEnumerable<ManualTestTarget> targets, ManualTestBrowserFilter filter,
            ISet<string> selected, ISet<string> testable, Func<ManualTestTarget, bool> categoryMatch = null)
        {
            var words = (filter.Search ?? "").Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            return targets.Where(t =>
                (filter.Source == "all" || (filter.Source == "instances") == (t.InstanceId != null)) &&
                (filter.Package == null || string.Equals(t.PackageId, filter.Package, StringComparison.OrdinalIgnoreCase)) &&
                (filter.Type == null || t.DefType == filter.Type) &&
                (!filter.ThirdPartyOnly || !(t.PackageId ?? "").StartsWith("ludeon.", StringComparison.OrdinalIgnoreCase)) &&
                (!filter.SelectedOnly || selected.Contains(t.Key)) && (!filter.TestableOnly || testable.Contains(t.Key)) &&
                (categoryMatch == null || categoryMatch(t)) &&
                words.All(word => (t.Label + " " + t.DefName + " " + t.PackageId + " " + t.ModName).IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0))
                .OrderBy(t => t.Label, StringComparer.CurrentCultureIgnoreCase).ThenBy(t => t.Key, StringComparer.Ordinal).ToList();
        }

        public static void Select(ISet<string> selected, IEnumerable<string> keys, bool value)
        {
            // Take a snapshot: deselecting a visible set must not erase hidden selections or invalidate iteration.
            foreach (var key in keys.ToList()) { if (value) selected.Add(key); else selected.Remove(key); }
        }

        public static IEnumerable<string> Range(IReadOnlyList<ManualTestTarget> visible, string anchor, string target)
        {
            var start = -1; var end = -1;
            for (var i = 0; i < visible.Count; i++) { if (visible[i].Key == anchor) start = i; if (visible[i].Key == target) end = i; }
            if (end < 0) return Array.Empty<string>();
            if (start < 0) start = end;
            return visible.Skip(Math.Min(start, end)).Take(Math.Abs(start - end) + 1).Select(t => t.Key).ToList();
        }

        public static string TypeLabel(string type) => type switch {
            "ThingDef" => "物品 / 建筑", "PawnKindDef" => "角色 / 生物", "AbilityDef" => "技能", "RecipeDef" => "配方",
            "HediffDef" => "健康效果", "TerrainDef" => "地形", "GeneDef" => "基因", "XenotypeDef" => "异种",
            "HairDef" => "发型", "HeadTypeDef" => "头型", "BodyTypeDef" => "体型", _ => type
        };
    }
}
