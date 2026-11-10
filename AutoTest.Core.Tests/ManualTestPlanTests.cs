using LogSyncMod.AutoTest;

internal static class ManualTestPlanTests
{
    internal static void Run(Action<bool, string> check)
    {
        ManualTestBrowserTests.Run(check);
        var a = new ManualTestTarget { PackageId = "Core", DefType = "ThingDef", DefName = "Rifle" };
        var b = new ManualTestTarget { PackageId = "Other", DefType = "PawnKindDef", DefName = "Wolf" };
        var categories = new[] { "RangedWeaponPocketMap", "AnimalCombat", "ItemInfoCard", "AnimalThreeView", "SkillPocketMap" };
        var capabilities = new[] {
            new ManualTestChoice { TargetKey = a.Key, CaseType = "RangedWeaponPocketMap", NativeDefName = "Rifle", Applicable = true },
            new ManualTestChoice { TargetKey = a.Key, CaseType = "ItemInfoCard", NativeDefName = "Rifle", Applicable = true },
            new ManualTestChoice { TargetKey = b.Key, CaseType = "AnimalCombat", NativeDefName = "Wolf", Applicable = true },
            new ManualTestChoice { TargetKey = b.Key, CaseType = "AnimalThreeView", NativeDefName = "Wolf@Adult", Applicable = true },
            new ManualTestChoice { TargetKey = b.Key, CaseType = "AnimalThreeView", NativeDefName = "Wolf@Young", Applicable = true }
        };
        var selection = new ManualTestSelection { Targets = new List<string> { a.Key, b.Key }, Categories = new List<string> { "ItemInfoCard", "AnimalThreeView", "RangedWeaponPocketMap" } };
        var plan = ManualTestPlan.Build(selection, new[] { a, b }, categories, capabilities);
        check(plan.Count(p => p.Applicable) == 4 && plan.Count(p => !p.Applicable) == 3, "manual unified category set expands only applicable native variants and records every inapplicable combination");
        check(plan.Where(p => !p.Applicable).All(p => !string.IsNullOrWhiteSpace(p.Reason)), "manual inapplicable combinations have explicit reasons rather than green results");
        var frozen = ManualTestPlan.Freeze(selection); selection.Targets.Clear(); selection.Categories.Clear();
        check(frozen.Targets.Count == 2 && frozen.Categories.Count == 3, "manual invocation selection is immutable with respect to later window edits");
        check(ManualTestPlan.Groups.Length == 3 && ManualTestPlan.Group("SkillPocketMap") == "战斗测试" && ManualTestPlan.Group("TerrainPatchView") == "三视图测试" && ManualTestPlan.Group("AbilityGizmoUi") == "UI 测试", "manual catalog has exactly three display groups and includes skills and non-pawn views");
        try { ManualTestPlan.Build(new ManualTestSelection { Targets = new List<string> { "removed" }, Categories = new List<string> { "ItemInfoCard" } }, new[] { a, b }, categories, capabilities); check(false, "removed target diagnosed"); }
        catch (InvalidOperationException) { check(true, "restored selection marks removed targets instead of silently replacing or clearing them"); }
        try { ManualTestPlan.Build(new ManualTestSelection { Targets = new List<string> { a.Key }, Categories = new List<string> { "GroupCombat" } }, new[] { a, b }, categories, capabilities); check(false, "group combat excluded"); }
        catch (InvalidOperationException) { check(true, "manual ordinary selection cannot enable group combat or a hand-authored XML capability"); }
    }
}
