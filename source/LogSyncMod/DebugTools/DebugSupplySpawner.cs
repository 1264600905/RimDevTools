using System;
using System.Collections.Generic;
using System.Linq;
using LudeonTK;
using RimWorld;
using Verse;

namespace LogSyncMod
{
    internal sealed class DebugSupplyRequest
    {
        public readonly ThingDef Def;
        public readonly ThingDef Stuff;
        public readonly int Quantity;

        public DebugSupplyRequest(ThingDef def, ThingDef stuff, int quantity)
        {
            Def = def;
            Stuff = stuff;
            Quantity = quantity;
        }
    }

    internal static class DebugSupplySpawner
    {
        internal static void Start(Map map, List<DebugSupplyRequest> requests)
        {
            DebugTools.curTool = new DebugTool("LogSyncMod.DebugPlacementTool".Translate(), () =>
            {
                IntVec3 center = UI.MouseCell();
                DebugTools.curTool = null;
                if (!Prefs.DevMode || map == null || Find.CurrentMap != map || !center.InBounds(map))
                {
                    Messages.Message("LogSyncMod.DebugWrongMap".Translate(), MessageTypeDefOf.RejectInput, historical: false);
                    return;
                }
                LongEventHandler.QueueLongEvent(() => Spawn(map, center, requests), "LogSyncMod.DebugGenerating", false,
                    ex =>
                    {
                        Log.Error("[LogSyncMod] Debug supplies failed: " + ex);
                        Messages.Message("LogSyncMod.DebugSpawnFailed".Translate(), MessageTypeDefOf.RejectInput, historical: false);
                    });
            });
        }

        private static bool SafeCell(IntVec3 cell, Map map)
        {
            return cell.InBounds(map) && !cell.Fogged(map) && cell.Standable(map) && cell.GetEdifice(map) == null;
        }

        private static bool HasRoom(IntVec3 cell, Map map, Thing thing)
        {
            return cell.GetItemCount(map) < cell.GetMaxItemsAllowedInCell(map) || cell.GetThingList(map)
                .Any(t => t.def.category == ThingCategory.Item && t.stackCount < t.def.stackLimit && t.CanStackWith(thing));
        }

        private static void Spawn(Map map, IntVec3 center, List<DebugSupplyRequest> requests)
        {
            // Sorting once keeps fallback placement bounded by this map, including very large material lists.
            List<IntVec3> cells = map.AllCells.Where(c => SafeCell(c, map))
                .OrderBy(c => (c - center).LengthHorizontalSquared).ToList();
            long totalPlaced = 0;
            int fullKinds = 0;
            List<string> failures = new List<string>();
            foreach (DebugSupplyRequest request in requests)
            {
                int remaining = request.Quantity;
                int cellIndex = 0;
                string error = null;
                Thing pending = null;
                try
                {
                    while (remaining > 0 && cellIndex < cells.Count)
                    {
                        int stackSize = Math.Min(remaining, Math.Max(1, request.Def.stackLimit));
                        pending = ThingMaker.MakeThing(request.Def, request.Stuff);
                        pending.stackCount = stackSize;
                        int placed = 0;
                        while (cellIndex < cells.Count && !pending.Destroyed && !pending.Spawned)
                        {
                            IntVec3 cell = cells[cellIndex];
                            if (!SafeCell(cell, map) || !HasRoom(cell, map, pending)) { cellIndex++; continue; }
                            bool complete = GenPlace.TryPlaceThing(pending, cell, map, ThingPlaceMode.Near,
                                (thing, count) => { placed += count; totalPlaced += count; remaining -= count; },
                                c => SafeCell(c, map));
                            if (complete) break;
                            cellIndex++;
                        }
                        if (!pending.Destroyed && !pending.Spawned) pending.Destroy(DestroyMode.Vanish);
                        pending = null;
                        if (placed < stackSize) break;
                    }
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    Log.Warning("[LogSyncMod] Cannot spawn " + request.Def.defName + ": " + ex);
                }
                finally
                {
                    if (pending != null && !pending.Destroyed && !pending.Spawned) pending.Destroy(DestroyMode.Vanish);
                }
                if (remaining == 0) fullKinds++;
                else
                {
                    string detail = request.Def.LabelCap + " (" + request.Def.defName + "): " +
                        (request.Quantity - remaining) + "/" + request.Quantity + (error == null ? "" : " — " + error);
                    failures.Add(detail);
                }
            }
            string message = "LogSyncMod.DebugSpawnResult".Translate(fullKinds, requests.Count, totalPlaced);
            if (failures.Count > 0)
            {
                message += "\n" + "LogSyncMod.DebugSpawnPartial".Translate(failures.Count) + "\n" + string.Join("\n", failures.Take(5));
                Log.Warning("[LogSyncMod] Debug supplies partially placed:\n" + string.Join("\n", failures));
            }
            Messages.Message(message, failures.Count == 0 ? MessageTypeDefOf.NeutralEvent : MessageTypeDefOf.CautionInput, historical: false);
        }
    }
}
