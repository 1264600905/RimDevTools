using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace LogSyncMod.AutoTest
{
    internal sealed class ManualTestCatalog
    {
        internal readonly List<ManualTestTarget> Targets = new List<ManualTestTarget>();
        internal readonly List<ManualTestChoice> Capabilities = new List<ManualTestChoice>();
        internal readonly Dictionary<string, Def> Definitions = new Dictionary<string, Def>(StringComparer.Ordinal);
        internal readonly Dictionary<string, Thing> Instances = new Dictionary<string, Thing>(StringComparer.Ordinal);
        internal readonly List<string> Categories = Enum.GetNames(typeof(AutoTestCaseType)).ToList();
        internal readonly HashSet<string> TestableTargets = new HashSet<string>(StringComparer.Ordinal);

        internal ManualTestCatalog(Map map)
        {
            void Add<T>() where T : Def, new()
            {
                foreach (var def in DefDatabase<T>.AllDefsListForReading)
                {
                    if (def.modContentPack == null) continue;
                    var target = new ManualTestTarget { DefType = typeof(T).Name, DefName = def.defName, PackageId = def.modContentPack.PackageIdPlayerFacing, ModName = def.modContentPack.Name, Label = string.IsNullOrWhiteSpace(def.label) ? def.defName : def.label };
                    if (Definitions.ContainsKey(target.Key)) continue;
                    Definitions.Add(target.Key, def); Targets.Add(target);
                }
            }
            Add<ThingDef>(); Add<PawnKindDef>(); Add<AbilityDef>(); Add<RecipeDef>(); Add<HediffDef>(); Add<TerrainDef>(); Add<GeneDef>(); Add<XenotypeDef>(); Add<HairDef>(); Add<HeadTypeDef>(); Add<BodyTypeDef>();
            foreach (var target in Targets.Where(t => Definitions[t.Key] is ThingDef def && def.IsApparel && !def.destroyable))
                foreach (var type in new[] { "ApparelThreeView", "ApparelCombat" }) Capabilities.Add(new ManualTestChoice { TargetKey = target.Key, CaseType = type, Applicable = false, Reason = "原生 Def 不可销毁，内置装备测试无法保证清理，不创建展示探针" });
            foreach (var mod in LoadedModManager.RunningMods)
            foreach (var item in AutoTestRunner.DescribeManualCases(mod, map))
            {
                var name = item.DefName.Split('@')[0];
                var target = Targets.FirstOrDefault(t => t.PackageId == mod.PackageIdPlayerFacing && t.DefName == name && (t.DefType == item.DefType || item.DefType.StartsWith(t.DefType + "_", StringComparison.Ordinal)));
                if (target != null) { Capabilities.Add(new ManualTestChoice { TargetKey = target.Key, CaseType = item.CaseType, NativeDefName = item.DefName, Applicable = true, Reason = "内置执行器；运行时检查生成和兼容条件" }); TestableTargets.Add(target.Key); }
            }
        }

        internal List<ManualTestChoice> Preview(ManualTestSelection selection) => ManualTestPlan.Build(selection, Targets, Categories, Capabilities);

        internal void RefreshInstances(Map map)
        {
            Targets.RemoveAll(t => t.InstanceId != null);
            foreach (var key in Instances.Keys) { Definitions.Remove(key); TestableTargets.Remove(key); }
            Capabilities.RemoveAll(c => Instances.ContainsKey(c.TargetKey)); Instances.Clear();
            if (map == null) return;
            var definitions = Targets.ToDictionary(t => t.Key, StringComparer.Ordinal);
            var capabilities = Capabilities.ToLookup(c => c.TargetKey, StringComparer.Ordinal);
            foreach (var thing in map.listerThings.AllThings.Where(t => !t.Destroyed && t.Spawned))
            {
                var def = thing is Pawn pawn ? (Def)pawn.kindDef : thing.def;
                var key = (def.modContentPack?.PackageIdPlayerFacing ?? "").ToLowerInvariant() + "|" + (thing is Pawn ? "PawnKindDef" : "ThingDef") + "|" + def.defName;
                if (!definitions.TryGetValue(key, out var basis)) continue;
                var target = new ManualTestTarget { PackageId = basis.PackageId, ModName = basis.ModName, DefType = basis.DefType, DefName = basis.DefName, Label = thing.LabelCap + " [当前实例]", InstanceId = thing.GetUniqueLoadID() };
                var matches = capabilities[basis.Key];
                Targets.Add(target); Definitions.Add(target.Key, def); Instances.Add(target.Key, thing);
                foreach (var match in matches) Capabilities.Add(new ManualTestChoice { TargetKey = target.Key, CaseType = match.CaseType, NativeDefName = match.NativeDefName, Applicable = match.Applicable, Reason = match.Reason });
                if (matches.Any(c => c.Applicable)) TestableTargets.Add(target.Key);
            }
        }
    }
}
