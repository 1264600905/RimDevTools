using System;
using System.Collections.Generic;
using System.Linq;

namespace LogSyncMod.AutoTest
{
    public sealed class ManualTestTarget
    {
        public string PackageId { get; set; }
        public string DefType { get; set; }
        public string DefName { get; set; }
        public string Label { get; set; }
        public string ModName { get; set; }
        public string InstanceId { get; set; }
        public string Key => (PackageId ?? "").ToLowerInvariant() + "|" + DefType + "|" + DefName + (InstanceId == null ? "" : "|instance:" + InstanceId);
    }

    public sealed class ManualTestChoice
    {
        public string TargetKey { get; set; }
        public string CaseType { get; set; }
        public string NativeDefName { get; set; }
        public string Reason { get; set; }
        public bool Applicable { get; set; }
    }

    public sealed class ManualTestSelection
    {
        public List<string> Targets { get; set; } = new List<string>();
        public List<string> Categories { get; set; } = new List<string>();
    }

    public static class ManualTestPlan
    {
        public static readonly string[] Groups = { "战斗测试", "UI 测试", "三视图测试" };
        public static string Group(string category) => category.EndsWith("InfoCard", StringComparison.Ordinal) || category == "RecipeBillUi" || category == "AbilityGizmoUi" ? Groups[1] : category.EndsWith("View", StringComparison.Ordinal) ? Groups[2] : Groups[0];
        public static string Label(string category) => category switch {
            "BuildingInfoCard" => "建筑信息卡", "ItemInfoCard" => "物品信息卡", "PlantInfoCard" => "植物信息卡", "TerrainInfoCard" => "地形信息卡", "RecipeInfoCard" => "配方信息卡", "RecipeBillUi" => "工作台账单界面", "RecipeExecution" => "配方实际生产", "AbilityGizmoUi" => "技能按钮界面", "HediffInfoCard" => "健康效果信息卡", "AnimalInfoCard" => "动物信息卡", "MechanoidInfoCard" => "机械族信息卡", "PawnInfoCard" => "角色信息卡", "GeneInfoCard" => "基因信息卡", "XenotypeInfoCard" => "异种信息卡",
            "TurretPocketMap" => "炮塔战斗", "RangedWeaponPocketMap" => "远程武器战斗", "MeleeWeaponPocketMap" => "近战武器战斗", "SkillPocketMap" => "技能效果测试", "ApparelCombat" => "服装防护战斗", "AnimalCombat" => "动物战斗", "MechanoidCombat" => "机械族战斗", "AlienCombat" => "异族战斗",
            "HumanoidThreeView" => "人类三视图", "AnimalThreeView" => "动物三视图", "MechanoidThreeView" => "机械族三视图", "AlienThreeView" => "异族三视图", "ApparelThreeView" => "服装三视图", "WeaponThreeView" => "武器三视图", "PlantGrowthView" => "植物生长视图", "BuildingView" => "建筑视图", "TerrainPatchView" => "地形区域视图", "PawnLifeStageThreeView" => "生命阶段三视图", "HairThreeView" => "发型三视图", "HeadTypeThreeView" => "头型三视图", "BodyTypeThreeView" => "体型三视图", "GeneThreeView" => "基因三视图", "XenotypeThreeView" => "异种三视图", "LoadoutView" => "整套配装展示", _ => category };

        public static List<ManualTestChoice> Build(ManualTestSelection selection, IEnumerable<ManualTestTarget> targets, IEnumerable<string> categories, IEnumerable<ManualTestChoice> capabilities)
        {
            var byTarget = targets.ToDictionary(t => t.Key, StringComparer.Ordinal);
            var knownCategories = new HashSet<string>(categories, StringComparer.Ordinal);
            if (selection.Targets.Any(t => !byTarget.ContainsKey(t))) throw new InvalidOperationException("Previously selected target is no longer loaded: " + selection.Targets.First(t => !byTarget.ContainsKey(t)));
            if (selection.Categories.Any(c => !knownCategories.Contains(c))) throw new InvalidOperationException("Previously selected category is unavailable: " + selection.Categories.First(c => !knownCategories.Contains(c)));
            var available = capabilities.ToLookup(c => c.TargetKey + "\n" + c.CaseType, StringComparer.Ordinal);
            var result = new List<ManualTestChoice>();
            foreach (var target in selection.Targets.Distinct(StringComparer.Ordinal))
            foreach (var category in selection.Categories.Distinct(StringComparer.Ordinal))
            {
                var matching = available[target + "\n" + category].ToList();
                if (matching.Count == 0) result.Add(new ManualTestChoice { TargetKey = target, CaseType = category, Applicable = false, Reason = "该目标没有此类内置测试" });
                else result.AddRange(matching.Select(c => new ManualTestChoice { TargetKey = target, CaseType = category, NativeDefName = c.NativeDefName, Applicable = c.Applicable, Reason = c.Reason }));
            }
            return result;
        }

        public static ManualTestSelection Freeze(ManualTestSelection selection) => new ManualTestSelection { Targets = selection.Targets.Distinct(StringComparer.Ordinal).ToList(), Categories = selection.Categories.Distinct(StringComparer.Ordinal).ToList() };
    }
}
