using System.Collections.Generic;
using System.Linq;
using Verse;

namespace LogSyncMod
{
    public class LogSyncAutoTestSpecDef : Def
    {
        public bool enabled = true;
        public AutoTestScope scope = AutoTestScope.Standard;
        public List<string> categories = new List<string>();
        public List<string> defNames = new List<string>();
        public bool captureScreenshots = false;
        public bool captureTurretProjectileScreenshots = false;
        public bool captureProjectileScreenshots = false;
        public List<int> turretProjectileScreenshotTicks = new List<int> { 20, 30, 40 };
        public List<int> projectileScreenshotTicks = new List<int> { 20, 40 };

        public override void ResolveReferences()
        {
            base.ResolveReferences();
            categories = NormalizeStrings(categories);
            defNames = NormalizeStrings(defNames);
            turretProjectileScreenshotTicks = NormalizeTicks(turretProjectileScreenshotTicks);
            projectileScreenshotTicks = NormalizeTicks(projectileScreenshotTicks);
        }

        private static List<string> NormalizeStrings(List<string> values)
        {
            if (values == null)
            {
                return new List<string>();
            }

            return values
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct()
                .ToList();
        }

        private static List<int> NormalizeTicks(List<int> values)
        {
            List<int> normalized = values == null
                ? new List<int>()
                : values
                    .Where(value => value >= 0)
                    .Distinct()
                    .OrderBy(value => value)
                    .ToList();

            if (normalized.Count == 0)
            {
                normalized.Add(20);
                normalized.Add(30);
                normalized.Add(40);
            }

            return normalized;
        }
    }
}
