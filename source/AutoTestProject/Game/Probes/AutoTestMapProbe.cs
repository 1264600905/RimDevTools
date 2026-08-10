using Verse;

namespace LogSyncMod.AutoTest
{
    internal static class AutoTestMapProbe
    {
        public static AutoTestTargetRef Capture(Map map)
        {
            if (map == null) return null;
            return new AutoTestTargetRef
            {
                Id = AutoTestTargetIds.Map(map.uniqueID),
                Kind = "map",
                Label = map.info?.parent?.LabelCap ?? map.ToString(),
                DefName = map.Parent?.def?.defName ?? string.Empty,
                MapId = map.uniqueID,
                ThingId = 0,
                Fingerprint = AutoTestSelectionFingerprint.Create(new[] { map.uniqueID.ToString(), map.Size.ToString() })
            };
        }
    }
}
