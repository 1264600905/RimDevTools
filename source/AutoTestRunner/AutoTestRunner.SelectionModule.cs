using System.Collections.Generic;
using LogSyncMod;
using Verse;

namespace LogSyncMod.AutoTest
{
    internal sealed partial class AutoTestRunner
    {
        private bool MatchesTargetDefNameFilter(Def def)
            => def == null || settings.MatchesAutoTestTargetDefName(def.defName);

        private bool ShouldCaptureScreenshots()
            => settings.autoTestCaptureScreenshots;

        private bool ShouldCaptureProjectileScreenshots(AutoTestKind kind)
            => kind == AutoTestKind.RangedWeaponPocketMap && settings.autoTestCaptureScreenshots;

        private static List<int> ResolveProjectileScreenshotTicks()
            => new List<int> { 20, 40 };
    }
}
