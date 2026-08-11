using System;
using System.Collections.Generic;
using System.Linq;

namespace LogSyncMod.AutoTest
{
    public enum AutoTestPawnFactionMode
    {
        Native,
        PlayerCompatibility,
        Mechanoid,
        FactionlessFallback
    }

    public sealed class AutoTestPawnFactionDecision
    {
        public AutoTestPawnFactionMode RequestedMode { get; set; }
        public AutoTestPawnFactionMode EffectiveMode { get; set; }
        public string FactionDefName { get; set; }
        public string Reason { get; set; }
    }

    public static class AutoTestPawnFactionPlanner
    {
        public static AutoTestPawnFactionDecision Resolve(
            AutoTestPawnFactionMode requestedMode,
            bool isMechanoid,
            string defaultFactionDefName,
            IEnumerable<string> availableFactionDefNames,
            string playerFactionDefName)
        {
            HashSet<string> available = new HashSet<string>(
                (availableFactionDefNames ?? Enumerable.Empty<string>()).Where(value => !string.IsNullOrWhiteSpace(value)),
                StringComparer.OrdinalIgnoreCase);

            if (isMechanoid)
            {
                return new AutoTestPawnFactionDecision
                {
                    RequestedMode = requestedMode,
                    EffectiveMode = AutoTestPawnFactionMode.Mechanoid,
                    FactionDefName = "Mechanoid",
                    Reason = "PawnKind race is mechanoid."
                };
            }

            if (requestedMode == AutoTestPawnFactionMode.PlayerCompatibility)
            {
                return new AutoTestPawnFactionDecision
                {
                    RequestedMode = requestedMode,
                    EffectiveMode = AutoTestPawnFactionMode.PlayerCompatibility,
                    FactionDefName = playerFactionDefName ?? string.Empty,
                    Reason = "Test spec explicitly requested PlayerCompatibility."
                };
            }

            if (!string.IsNullOrWhiteSpace(defaultFactionDefName) && available.Contains(defaultFactionDefName))
            {
                return new AutoTestPawnFactionDecision
                {
                    RequestedMode = requestedMode,
                    EffectiveMode = AutoTestPawnFactionMode.Native,
                    FactionDefName = defaultFactionDefName,
                    Reason = "PawnKind default faction exists in the current game."
                };
            }

            return new AutoTestPawnFactionDecision
            {
                RequestedMode = requestedMode,
                EffectiveMode = AutoTestPawnFactionMode.FactionlessFallback,
                FactionDefName = defaultFactionDefName ?? string.Empty,
                Reason = string.IsNullOrWhiteSpace(defaultFactionDefName)
                    ? "PawnKind has no default faction; native generation is factionless."
                    : "PawnKind default faction has no current-game instance; native generation falls back to null."
            };
        }
    }

    public enum AutoTestBodyTypeTargetKind
    {
        PawnKind,
        BodyType,
        Apparel,
        Weapon,
        Gene,
        Xenotype,
        Other
    }

    public static class AutoTestBodyTypeMatchPolicy
    {
        public static bool RequireMatch(AutoTestBodyTypeTargetKind targetKind, bool explicitBodyTypeSelected, bool staticallyChangesBodyType)
        {
            switch (targetKind)
            {
                case AutoTestBodyTypeTargetKind.BodyType:
                    return true;
                case AutoTestBodyTypeTargetKind.Apparel:
                case AutoTestBodyTypeTargetKind.Weapon:
                    return explicitBodyTypeSelected;
                case AutoTestBodyTypeTargetKind.PawnKind:
                    return explicitBodyTypeSelected;
                case AutoTestBodyTypeTargetKind.Gene:
                case AutoTestBodyTypeTargetKind.Xenotype:
                    return staticallyChangesBodyType;
                default:
                    return false;
            }
        }
    }

    public static class AutoTestApparelBodyTypeVariantPolicy
    {
        public static IReadOnlyList<string> Resolve(
            IEnumerable<string> configuredBodyTypeDefNames,
            IEnumerable<string> textureBackedBodyTypeDefNames)
        {
            List<string> configured = Normalize(configuredBodyTypeDefNames);
            List<string> textureBacked = Normalize(textureBackedBodyTypeDefNames);
            return textureBacked.Count > 0 ? textureBacked : configured;
        }

        private static List<string> Normalize(IEnumerable<string> values)
        {
            return (values ?? Enumerable.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
