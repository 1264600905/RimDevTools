using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace LogSyncMod.AutoTest
{
    internal sealed class AutoTestMainMap : IDisposable
    {
        private readonly Map original;
        private readonly Vector3 cameraPosition;
        private readonly float cameraSize;
        private readonly List<Thing> selection;
        private readonly WorldRenderMode originalWorldMode;
        internal Map Map;
        internal MapParent Parent;
        internal Map Original => original;

        internal AutoTestMainMap(Map source, int seed, IntVec3 size, string generator = "LogSync_AutoTestStudioMap")
        {
            original = source; cameraPosition = Find.CameraDriver.GetComponent<Camera>().transform.position; cameraSize = Find.CameraDriver.RootSize;
            originalWorldMode = Find.World.renderer.wantedMode;
            selection = Find.Selector.SelectedObjectsListForReading.OfType<Thing>().ToList();
            try
            {
                var candidates = new List<PlanetTile>();
                var anchor = source?.Tile ?? Find.WorldObjects.AllWorldObjects.Select(o => o.Tile).FirstOrDefault(t => t.Valid);
                if (!anchor.Valid) throw new InvalidOperationException("No surface anchor for the temporary ordinary test map");
                Find.WorldGrid.GetTileNeighbors(anchor, candidates);
                var tile = candidates.FirstOrDefault(t => t.Valid && Find.WorldGrid[t].PrimaryBiome?.impassable == false && !Find.WorldObjects.AllWorldObjects.OfType<MapParent>().Any(p => p.Tile == t), PlanetTile.Invalid);
                if (!tile.Valid) throw new InvalidOperationException("No free neighboring surface tile for the temporary ordinary test map");
                var generatorDef = DefDatabase<MapGeneratorDef>.GetNamed(generator);
                if (generatorDef.pocketMapProperties != null || generatorDef.genSteps == null || generatorDef.genSteps.Any(g => g?.genStep == null)) throw new InvalidOperationException("The requested generator is not a valid ordinary main-map generator: " + generator);
                Parent = (MapParent)WorldObjectMaker.MakeWorldObject(DefDatabase<WorldObjectDef>.GetNamed("LogSync_AutoTestStudioParent"));
                Parent.Tile = tile; Parent.forceRemoveWorldObjectWhenMapRemoved = true;
                Find.WorldObjects.Add(Parent);
                using (var requestedSeed = new ScenarioMapSeed(seed))
                {
                    Map = MapGenerator.GenerateMap(size, Parent, generatorDef, null, null, isPocketMap: false);
                    requestedSeed.Verify();
                }
                if (Map == null || Map.Parent is PocketMapParent) throw new InvalidOperationException("The test studio must be an ordinary main map");
                foreach (var cell in Map.AllCells) Map.fogGrid.Unfog(cell);
                Current.Game.CurrentMap = Map; Find.World.renderer.wantedMode = WorldRenderMode.None; Find.Selector.ClearSelection();
                Find.CameraDriver.JumpToCurrentMapLoc(Map.Center);
            }
            catch { Dispose(); throw; }
        }

        public void Dispose()
        {
            if (original != null && Find.Maps.Contains(original))
            {
                Current.Game.CurrentMap = original; Find.World.renderer.wantedMode = originalWorldMode;
                Find.Selector.ClearSelection(); foreach (var thing in selection.Where(t => !t.Destroyed && t.Spawned && t.Map == original)) Find.Selector.Select(thing, false, false);
                Find.CameraDriver.SetRootPosAndSize(cameraPosition, cameraSize);
            }
            else if (Map != null && Current.Game.CurrentMap == Map) { Current.Game.CurrentMap = Find.Maps.FirstOrDefault(m => m != Map); Find.World.renderer.wantedMode = WorldRenderMode.Planet; }
            var pawns = Map?.mapPawns.AllPawns.ToList() ?? new List<Pawn>();
            if (Map != null && Find.Maps.Contains(Map)) Current.Game.DeinitAndRemoveMap(Map, false);
            foreach (var pawn in pawns) { if (Find.WorldPawns.Contains(pawn)) Find.WorldPawns.RemovePawn(pawn); if (!pawn.Destroyed) pawn.Destroy(DestroyMode.Vanish); }
            if (Parent != null && !Parent.Destroyed) Parent.Destroy();
            if (Map != null && Find.Maps.Contains(Map) || Parent != null && Find.WorldObjects.AllWorldObjects.Contains(Parent)) throw new InvalidOperationException("Temporary main-map cleanup left registered objects");
            // A command-owned session with no remaining map must leave through the same
            // native completion gate as other CLI tests, after callers flush their results.
            if (Find.Maps.Count == 0 && global::LogSyncMod.LogSyncMod.settings is { testSessionActive: true } settings)
                global::LogSyncMod.TestSessionCompletion.Complete(settings.testSessionKind, settings.autoTestRunId);
        }
    }
}
