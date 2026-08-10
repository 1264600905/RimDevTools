using System;
using System.Linq;
using RimWorld;
using Verse;

namespace LogSyncMod.AutoTest
{
    internal static class AutoTestThingProbe
    {
        public static AutoTestTargetRef Target(Thing thing)
        {
            if (thing == null) return null;
            int mapId = thing.Map?.uniqueID ?? -1;
            string kind = thing is Pawn ? "pawn" : "thing";
            string id = thing is Pawn ? AutoTestTargetIds.Pawn(thing.thingIDNumber) : AutoTestTargetIds.Thing(thing.thingIDNumber);
            return new AutoTestTargetRef
            {
                Id = id,
                Kind = kind,
                Label = thing.LabelCap,
                DefName = thing.def?.defName ?? string.Empty,
                MapId = mapId,
                ThingId = thing.thingIDNumber,
                Fingerprint = AutoTestSelectionFingerprint.Create(new[] { kind, thing.thingIDNumber.ToString(), mapId.ToString(), thing.def?.defName ?? string.Empty })
            };
        }

        public static AutoTestThingSnapshot Capture(Thing thing)
        {
            if (thing == null) return new AutoTestThingSnapshot { Destroyed = true, Summary = "thing=missing" };
            AutoTestThingSnapshot snapshot = new AutoTestThingSnapshot
            {
                Target = Target(thing),
                Spawned = thing.Spawned,
                Destroyed = thing.Destroyed,
                MapId = thing.Map?.uniqueID ?? -1,
                Position = thing.PositionHeld.ToString(),
                StackCount = thing.stackCount,
                HitPoints = thing.def?.useHitPoints == true ? thing.HitPoints : 0,
                MaxHitPoints = thing.def?.useHitPoints == true ? thing.MaxHitPoints : 0,
                Faction = thing.Faction?.def?.defName ?? string.Empty
            };
            try { snapshot.OccupiedRect = thing.Spawned ? thing.OccupiedRect().ToString() : string.Empty; } catch { snapshot.OccupiedRect = string.Empty; }
            try { snapshot.Forbidden = thing.Spawned && thing.IsForbidden(Faction.OfPlayer); } catch { }
            try { snapshot.Burning = thing.Spawned && thing.IsBurning(); } catch { }
            try { snapshot.Selected = Find.Selector != null && Find.Selector.IsSelected(thing); } catch { }
            ThingWithComps withComps = thing as ThingWithComps;
            if (withComps?.AllComps != null) snapshot.CompTypeNames.AddRange(withComps.AllComps.Where(comp => comp != null).Select(comp => comp.GetType().FullName));
            snapshot.Summary = "id=" + snapshot.Target?.Id +
                " def=" + snapshot.Target?.DefName +
                " spawned=" + snapshot.Spawned +
                " destroyed=" + snapshot.Destroyed +
                " map=" + snapshot.MapId +
                " pos=" + snapshot.Position +
                " hp=" + snapshot.HitPoints + "/" + snapshot.MaxHitPoints +
                " selected=" + snapshot.Selected;
            return snapshot;
        }
    }
}
