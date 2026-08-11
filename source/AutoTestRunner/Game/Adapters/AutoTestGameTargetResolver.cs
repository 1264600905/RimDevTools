using System;
using System.Linq;
using Verse;

namespace LogSyncMod.AutoTest
{
    internal sealed class AutoTestGameTargetResolver
    {
        public AutoTestTargetResolution Resolve(AutoTestTargetRef target)
        {
            if (target == null) return Missing("missing", "Target reference is null.");
            if (target.Kind == "map")
            {
                Map map = Find.Maps?.FirstOrDefault(item => item != null && item.uniqueID == target.MapId);
                if (map == null) return Missing("missing", "Map is not present.");
                return new AutoTestTargetResolution { Status = "resolved", Target = AutoTestMapProbe.Capture(map), ResolvedObject = map };
            }
            if (target.Kind == "thing" || target.Kind == "pawn")
            {
                Thing thing = Find.Maps?.SelectMany(map => map?.listerThings?.AllThings ?? Enumerable.Empty<Thing>()).FirstOrDefault(item => item != null && item.thingIDNumber == target.ThingId);
                if (thing == null || thing.Destroyed) return Missing("missing", "Thing is not present.");
                if (target.Kind == "pawn" && !(thing is Pawn)) return Missing("wrong_kind", "Target is not a Pawn.");
                if (target.Kind == "thing" && thing is Pawn) return Missing("wrong_kind", "Target is a Pawn.");
                if (target.MapId >= 0 && thing.Map?.uniqueID != target.MapId) return Missing("stale_map", "Target moved to another map.");
                if (!string.IsNullOrEmpty(target.DefName) && !string.Equals(target.DefName, thing.def?.defName, StringComparison.OrdinalIgnoreCase)) return Missing("fingerprint_mismatch", "Target defName changed.");
                return new AutoTestTargetResolution { Status = "resolved", Target = AutoTestThingProbe.Target(thing), ResolvedObject = thing };
            }
            return Missing("unsupported", "Target kind is unsupported: " + target.Kind);
        }

        private static AutoTestTargetResolution Missing(string status, string message) { return new AutoTestTargetResolution { Status = status, Message = message }; }
    }
}
