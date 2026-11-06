using System;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace LogSyncMod.AutoTest
{
    internal sealed class AutoTestPocketMapRequest
    {
        public Map SourceMap;
        public IntVec3 MapSize;
        public string MapGeneratorDefName;
        public Action<Map> OnGenerated;
    }

    internal sealed class AutoTestPocketMapHandle
    {
        public PocketMapParent Parent;
        public Map Map;
        public MapGeneratorDef GeneratorDef;
        public Map SourceMap;
    }

    internal sealed class AutoTestPocketMapService
    {
        private readonly AutoTestPocketMapCleanupWatchdog cleanupWatchdog = new AutoTestPocketMapCleanupWatchdog(new AutoTestGameClock());
        public int PendingCleanupCount { get { return cleanupWatchdog.PendingCount; } }

        public AutoTestPocketMapHandle Create(AutoTestPocketMapRequest request, out string errorMessage)
        {
            errorMessage = null;
            if (request == null)
            {
                errorMessage = "Pocket map request was null.";
                return null;
            }

            try
            {
                MapGeneratorDef generatorDef = DefDatabase<MapGeneratorDef>.GetNamed(request.MapGeneratorDefName ?? "LogSync_AutoTestPocketMap");
                PocketMapParent pocketMapParent = WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.PocketMap) as PocketMapParent;
                if (pocketMapParent == null)
                {
                    errorMessage = "Failed to create PocketMapParent.";
                    return null;
                }

                pocketMapParent.sourceMap = request.SourceMap;
                pocketMapParent.mapGenerator = generatorDef;

                Map generatedMap = MapGenerator.GenerateMap(
                    request.MapSize,
                    pocketMapParent,
                    generatorDef,
                    null,
                    request.OnGenerated,
                    isPocketMap: true);

                if (generatedMap == null)
                {
                    errorMessage = "Managed pocket map generation returned null Map.";
                    if (!pocketMapParent.Destroyed)
                    {
                        pocketMapParent.Destroy();
                    }

                    return null;
                }

                // Synthetic arenas must be visible without player-owned pawns.
                // The generator's Fog step runs before this point; keep factions unchanged.
                foreach (IntVec3 cell in generatedMap.AllCells)
                {
                    generatedMap.fogGrid.Unfog(cell);
                }

                Find.World.pocketMaps.Add(pocketMapParent);
                return new AutoTestPocketMapHandle
                {
                    Parent = generatedMap.Parent as PocketMapParent ?? pocketMapParent,
                    Map = generatedMap,
                    GeneratorDef = generatorDef,
                    SourceMap = request.SourceMap
                };
            }
            catch (Exception ex)
            {
                errorMessage = ex.ToString();
                return null;
            }
        }

        public bool IsAlive(Map map, MapParent parent)
        {
            if (map == null || !Find.Maps.Contains(map))
            {
                return false;
            }

            if (parent != null && parent.Destroyed)
            {
                return false;
            }

            return true;
        }

        public void Destroy(Map map, MapParent parent)
        {
            if (map == null && parent == null) return;
            Map sourceMap = ((parent ?? map?.Parent) as PocketMapParent)?.sourceMap;
            string id = map != null ? "map:" + map.uniqueID : "parent:" + parent.ID;
            AutoTestPocketMapCleanupAttempt attempt = cleanupWatchdog.Request(id,
                () => map != null ? !Find.Maps.Contains(map) : parent == null || parent.Destroyed,
                force => RestoreSourceView(map, sourceMap, force),
                () => DestroyNow(map, parent));
            if (attempt != null && !attempt.Removed)
                Log.Warning("[LogSyncAutoTest] Pocket map cleanup deferred; a 10-second realtime source-view recovery is armed. " + id + " " + attempt.Error?.Message);
        }

        public void PollCleanupWatchdog()
        {
            foreach (AutoTestPocketMapCleanupAttempt attempt in cleanupWatchdog.Poll())
                Log.Warning("[LogSyncAutoTest] Pocket map cleanup watchdog: forced source map/camera after 10 realtime seconds. " +
                    attempt.MapId + " removed=" + attempt.Removed + " error=" + attempt.Error?.Message);
        }

        private static void RestoreSourceView(Map map, Map sourceMap, bool force)
        {
            if (Current.Game == null) return;
            if (!force && Current.Game.CurrentMap != map) return;
            Map destination = sourceMap != null && Find.Maps.Contains(sourceMap) && !(sourceMap.Parent is PocketMapParent) ? sourceMap :
                Find.Maps.FirstOrDefault(candidate => candidate != null && candidate != map && !(candidate.Parent is PocketMapParent));
            if (destination == null) throw new InvalidOperationException("No surviving source map is available for pocket map cleanup recovery.");
            Current.Game.CurrentMap = destination;
            if (Find.World?.renderer != null) Find.World.renderer.wantedMode = WorldRenderMode.None;
            Find.CameraDriver?.JumpToCurrentMapLoc(destination.Center);
        }

        private static void DestroyNow(Map map, MapParent parent)
        {
            Exception failure = null;
            try
            {
                if (map != null && Find.Maps.Contains(map))
                {
                    PocketMapUtility.DestroyPocketMap(map);
                    if (!Find.Maps.Contains(map)) return;
                }
            }
            catch (Exception ex) { failure = ex; }

            try
            {
                if (parent != null && !parent.Destroyed)
                {
                    parent.Destroy();
                }
            }
            catch (Exception ex) { if (failure == null) failure = ex; }
            if (map != null && Find.Maps.Contains(map) || map == null && parent != null && !parent.Destroyed)
                throw new InvalidOperationException("Pocket map remains registered after cleanup.", failure);
        }
    }
}
