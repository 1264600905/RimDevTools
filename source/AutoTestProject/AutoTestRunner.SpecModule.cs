using System;
using System.Collections.Generic;
using System.Linq;
using LogSyncMod;
using RimWorld;
using Verse;

namespace LogSyncMod.AutoTest
{
    internal sealed partial class AutoTestRunner
    {
        private readonly List<LogSyncAutoTestSpecDef> activeSpecDefs = new List<LogSyncAutoTestSpecDef>();

        private void LoadActiveSpecs()
        {
            activeSpecDefs.Clear();

            foreach (LogSyncAutoTestSpecDef spec in DefDatabase<LogSyncAutoTestSpecDef>.AllDefsListForReading
                .Where(spec => spec != null && spec.enabled && spec.modContentPack == targetMod)
                .OrderBy(spec => spec.defName))
            {
                activeSpecDefs.Add(spec);
            }

            if (settings.UseXmlDrivenAutoTest())
            {
                Log.Message("[LogSyncAutoTest] XML-driven mode enabled. ActiveSpecCount=" + activeSpecDefs.Count);
                if (activeSpecDefs.Count == 0)
                {
                    Log.Warning("[LogSyncAutoTest] XML-driven mode is enabled, but no LogSyncAutoTestSpecDef was found under target mod " + (targetPackageId ?? "unknown") + ".");
                }
            }
        }

        private bool MatchesSpecFilters(AutoTestKind kind, Def def)
        {
            if (!settings.UseXmlDrivenAutoTest())
            {
                return true;
            }

            return ResolveBestMatchingSpec(kind, def) != null;
        }

        private LogSyncAutoTestSpecDef ResolveBestMatchingSpec(AutoTestKind kind, Def def)
        {
            if (!settings.UseXmlDrivenAutoTest() || def == null)
            {
                return null;
            }

            AutoTestScope targetScope = ResolveScopeForKind(kind);
            string categoryKey = ResolveCategoryKey(kind);
            string defName = def.defName ?? string.Empty;

            return activeSpecDefs
                .Where(spec => SpecMatchesTarget(spec, targetScope, categoryKey, defName))
                .OrderByDescending(spec => ScoreSpecMatch(spec, targetScope, categoryKey, defName))
                .ThenBy(spec => spec.defName)
                .FirstOrDefault();
        }

        private bool ShouldCaptureScreenshots(LogSyncAutoTestSpecDef spec)
        {
            if (!settings.UseXmlDrivenAutoTest())
            {
                return settings.autoTestCaptureScreenshots;
            }

            return spec != null && spec.captureScreenshots;
        }

        private bool ShouldCaptureProjectileScreenshots(LogSyncAutoTestSpecDef spec, AutoTestKind kind)
        {
            if (!settings.UseXmlDrivenAutoTest())
            {
                return kind == AutoTestKind.RangedWeaponPocketMap
                    ? settings.autoTestCaptureScreenshots
                    : false;
            }

            if (spec == null)
            {
                return false;
            }

            return spec.captureProjectileScreenshots ||
                (kind == AutoTestKind.TurretPocketMap && spec.captureTurretProjectileScreenshots);
        }

        private List<int> ResolveProjectileScreenshotTicks(LogSyncAutoTestSpecDef spec)
        {
            List<int> ticks = spec?.projectileScreenshotTicks == null
                ? new List<int>()
                : spec.projectileScreenshotTicks
                    .Where(value => value >= 0)
                    .Distinct()
                    .OrderBy(value => value)
                    .ToList();

            if (ticks.Count == 0 && spec?.turretProjectileScreenshotTicks != null)
            {
                ticks = spec.turretProjectileScreenshotTicks
                    .Where(value => value >= 0)
                    .Distinct()
                    .OrderBy(value => value)
                    .ToList();
            }

            if (ticks.Count == 0)
            {
                ticks.Add(20);
                ticks.Add(40);
            }

            return ticks;
        }

        private static AutoTestScope ResolveScopeForKind(AutoTestKind kind)
        {
            switch (kind)
            {
                case AutoTestKind.Recipe:
                    return AutoTestScope.Recipes;
                case AutoTestKind.Hediff:
                    return AutoTestScope.Hediffs;
                case AutoTestKind.TurretPocketMap:
                    return AutoTestScope.Turrets;
                case AutoTestKind.RangedWeaponPocketMap:
                    return AutoTestScope.RangedWeapons;
                case AutoTestKind.MeleeWeaponPocketMap:
                    return AutoTestScope.MeleeWeapons;
                default:
                    return AutoTestScope.Content;
            }
        }

        private static string ResolveCategoryKey(AutoTestKind kind)
        {
            switch (kind)
            {
                case AutoTestKind.Building:
                    return "Building";
                case AutoTestKind.Item:
                    return "Item";
                case AutoTestKind.Terrain:
                    return "Terrain";
                case AutoTestKind.Recipe:
                    return "Recipe";
                case AutoTestKind.Hediff:
                    return "Hediff";
                case AutoTestKind.TurretPocketMap:
                    return "Turret";
                case AutoTestKind.RangedWeaponPocketMap:
                    return "RangedWeapon";
                case AutoTestKind.MeleeWeaponPocketMap:
                    return "MeleeWeapon";
                default:
                    return kind.ToString();
            }
        }

        private static bool SpecMatchesTarget(LogSyncAutoTestSpecDef spec, AutoTestScope scope, string categoryKey, string defName)
        {
            if (spec == null)
            {
                return false;
            }

            if (spec.scope != AutoTestScope.All && spec.scope != scope)
            {
                return false;
            }

            bool hasCategoryFilters = spec.categories != null && spec.categories.Count > 0;
            bool hasDefNameFilters = spec.defNames != null && spec.defNames.Count > 0;
            if (!hasCategoryFilters && !hasDefNameFilters)
            {
                return true;
            }

            bool categoryMatched = hasCategoryFilters && spec.categories.Any(value => string.Equals(value, categoryKey, StringComparison.OrdinalIgnoreCase));
            bool defNameMatched = hasDefNameFilters && spec.defNames.Any(value => string.Equals(value, defName, StringComparison.OrdinalIgnoreCase));
            return categoryMatched || defNameMatched;
        }

        private static int ScoreSpecMatch(LogSyncAutoTestSpecDef spec, AutoTestScope scope, string categoryKey, string defName)
        {
            if (spec == null)
            {
                return int.MinValue;
            }

            int score = 0;
            if (spec.scope == scope)
            {
                score += 100;
            }

            if (spec.defNames != null && spec.defNames.Any(value => string.Equals(value, defName, StringComparison.OrdinalIgnoreCase)))
            {
                score += 1000;
            }

            if (spec.categories != null && spec.categories.Any(value => string.Equals(value, categoryKey, StringComparison.OrdinalIgnoreCase)))
            {
                score += 100;
            }

            if ((spec.categories == null || spec.categories.Count == 0) &&
                (spec.defNames == null || spec.defNames.Count == 0))
            {
                score += 10;
            }

            return score;
        }
    }
}
