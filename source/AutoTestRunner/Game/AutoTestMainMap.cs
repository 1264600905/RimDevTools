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
        internal Map Map;
        internal MapParent Parent;
        internal Map Original => original;

        internal AutoTestMainMap(Map source, int seed, IntVec3 size, string generator = "LogSync_AutoTestStudioMap")
        {
            original = source; cameraPosition = Find.CameraDriver.GetComponent<Camera>().transform.position; cameraSize = Find.CameraDriver.RootSize;
            selection = Find.Selector.SelectedObjectsListForReading.OfType<Thing>().ToList();
            try
            {
                var candidates = new List<PlanetTile>(); Find.WorldGrid.GetTileNeighbors(source.Tile, candidates);
                var tile = candidates.FirstOrDefault(t => t.Valid && Find.WorldGrid[t].PrimaryBiome?.impassable == false && !Find.WorldObjects.AllWorldObjects.OfType<MapParent>().Any(p => p.Tile == t), PlanetTile.Invalid);
                if (!tile.Valid) throw new InvalidOperationException("No free neighboring surface tile for the temporary ordinary test map");
                Parent = (MapParent)WorldObjectMaker.MakeWorldObject(DefDatabase<WorldObjectDef>.GetNamed("LogSync_AutoTestStudioParent"));
                Parent.Tile = tile; Parent.forceRemoveWorldObjectWhenMapRemoved = true;
                Find.WorldObjects.Add(Parent);
                using (var requestedSeed = new ScenarioMapSeed(seed))
                {
                    Map = MapGenerator.GenerateMap(size, Parent, DefDatabase<MapGeneratorDef>.GetNamed(generator), null, null, isPocketMap: false);
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
                Current.Game.CurrentMap = original; Find.World.renderer.wantedMode = WorldRenderMode.None;
                Find.Selector.ClearSelection(); foreach (var thing in selection.Where(t => !t.Destroyed && t.Spawned && t.Map == original)) Find.Selector.Select(thing, false, false);
                Find.CameraDriver.SetRootPosAndSize(cameraPosition, cameraSize);
            }
            else if (Map != null && Current.Game.CurrentMap == Map) { Current.Game.CurrentMap = Find.Maps.FirstOrDefault(m => m != Map); Find.World.renderer.wantedMode = WorldRenderMode.Planet; }
            var pawns = Map?.mapPawns.AllPawns.ToList() ?? new List<Pawn>();
            if (Map != null && Find.Maps.Contains(Map)) Current.Game.DeinitAndRemoveMap(Map, false);
            foreach (var pawn in pawns) { if (Find.WorldPawns.Contains(pawn)) Find.WorldPawns.RemovePawn(pawn); if (!pawn.Destroyed) pawn.Destroy(DestroyMode.Vanish); }
            if (Parent != null && !Parent.Destroyed) Parent.Destroy();
            if (Map != null && Find.Maps.Contains(Map) || Parent != null && Find.WorldObjects.AllWorldObjects.Contains(Parent)) throw new InvalidOperationException("Temporary main-map cleanup left registered objects");
        }
    }
}
