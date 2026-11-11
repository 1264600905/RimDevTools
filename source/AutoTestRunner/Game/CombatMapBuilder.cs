using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Xml.Linq;
using LogSyncMod.AutoTest.Scenarios;
using Verse;

namespace LogSyncMod.AutoTest
{
    internal sealed class CombatMapBuilder
    {
        private readonly Map map;
        private readonly CombatMapPlan plan;
        private readonly int clearance;
        private readonly int seed;
        private readonly double density;
        private int cleared, carved;
        private readonly List<CellRect> spawnZones = new();
        private CombatMapBuilder(Map map, XElement scene, int seed)
        {
            this.map = map; this.seed = seed;
            density = ScenarioLoader.D(scene, "featureDensity", 1);
            clearance = ScenarioLoader.I(scene, "spawnClearance", 6);
            plan = CombatMapCatalog.Build(ScenarioLoader.A(scene, "battlefield"), map.Size.x, map.Size.z, seed, density);
        }
        internal static CombatMapBuilder Generate(Map map, XElement scene, int seed)
        {
            if (scene.Attribute("battlefield") == null) return null;
            var builder = new CombatMapBuilder(map, scene, seed); builder.Generate(); return builder;
        }
        private void Generate()
        {
            var terrains = new Dictionary<string, TerrainDef>(); var things = new Dictionary<string, ThingDef>();
            TerrainDef Terrain(string id) { if (!terrains.TryGetValue(id, out var d)) terrains.Add(id, d = DefDatabase<TerrainDef>.GetNamed(id)); return d; }
            ThingDef Thing(string id) { if (!things.TryGetValue(id, out var d)) things.Add(id, d = DefDatabase<ThingDef>.GetNamed(id)); return d; }
            using (map.pathing.DisableIncrementalScope())
            {
                if (plan.Style.Id != "open")
                {
                    foreach (var thing in map.listerThings.AllThings.ToList()) if (thing is not Pawn && !thing.Destroyed) thing.Destroy(DestroyMode.Vanish);
                    var terrain = Terrain(plan.Style.Terrain);
                    foreach (var cell in map.AllCells)
                    {
                        map.terrainGrid.SetTerrain(cell, terrain); map.roofGrid.SetRoof(cell, null);
                        map.snowGrid?.SetDepth(cell, plan.Style.Snow);
                    }
                }
                foreach (var feature in plan.Cells)
                {
                    var cell = new IntVec3(feature.X, 0, feature.Z);
                    if (feature.Terrain != null) map.terrainGrid.SetTerrain(cell, Terrain(feature.Terrain));
                    if (feature.Thing == null) continue;
                    var thing = ThingMaker.MakeThing(Thing(feature.Thing), feature.Stuff == null ? null : Thing(feature.Stuff));
                    GenSpawn.Spawn(thing, cell, map, WipeMode.Vanish);
                }
            }
        }
        internal void PrepareSpawns(XElement actors, Action<string, string> trace)
        {
            var started = Stopwatch.GetTimestamp(); var centers = new List<IntVec3>();
            foreach (var actor in actors.Elements().Where(e => e.Name == "Pawn" || e.Name == "PawnGroup"))
            {
                var x = ScenarioLoader.I(actor, "x", ScenarioLoader.I(actor, "originX", 20));
                var z = ScenarioLoader.I(actor, "z", ScenarioLoader.I(actor, "originZ", 20));
                var extent = actor.Name == "PawnGroup" ? PawnGroupFormation.Extents(actor, true) : (x: 0, z: 0);
                var rect = CellRect.FromLimits(Math.Max(0, x - clearance), Math.Max(0, z - clearance),
                    Math.Min(map.Size.x - 1, x + extent.x + clearance), Math.Min(map.Size.z - 1, z + extent.z + clearance));
                spawnZones.Add(rect); centers.Add(new IntVec3(x + extent.x / 2, 0, z + extent.z / 2));
            }
            using (map.pathing.DisableIncrementalScope()) foreach (var rect in spawnZones) foreach (var cell in rect) Clear(cell, false);
            // One bounded grid flood per formation. Ordinary game pathing is rebuilt once
            // after generation; no new scans or terrain mutations occur during combat.
            var repaired = false;
            foreach (var center in centers.Skip(1))
            {
                if (Connected(centers[0], center)) continue;
                repaired = true;
                using (map.pathing.DisableIncrementalScope())
                {
                    var current = centers[0];
                    while (current.x != center.x) { ClearLane(current); current.x += Math.Sign(center.x - current.x); }
                    while (current.z != center.z) { ClearLane(current); current.z += Math.Sign(center.z - current.z); }
                    ClearLane(center);
                }
                if (!Connected(centers[0], center)) throw new InvalidOperationException("Battlefield spawn areas remain disconnected after clearing");
            }
            map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();
            var blocked = spawnZones.Sum(rect => rect.Count(c => !c.Standable(map)));
            if (blocked != 0) throw new InvalidOperationException("Battlefield spawn clearing left " + blocked + " blocked cells");
            trace("group.map_generated", System.Text.Json.JsonSerializer.Serialize(new {
                style = plan.Style.Id, seed, density, width = map.Size.x, height = map.Size.z, clearance,
                features = plan.Cells.Count(c => c.Thing != null), covers = plan.Cells.Count(c => c.Thing == "Sandbags"),
                buildings = plan.Buildings, routeCovers = plan.RouteCovers,
                spawnZones = spawnZones.Count, cleared, carved, repaired, connected = true, blockedSpawnCells = blocked,
                preparationMs = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency
            }));
        }
        private void ClearLane(IntVec3 center)
        {
            for (var dx = -1; dx <= 1; dx++) for (var dz = -1; dz <= 1; dz++)
            { var cell = center + new IntVec3(dx, 0, dz); if (cell.InBounds(map)) Clear(cell, true); }
        }
        private void Clear(IntVec3 cell, bool corridor)
        {
            foreach (var thing in map.thingGrid.ThingsListAtFast(cell).ToArray())
            {
                if (thing is Pawn || thing.Destroyed) continue;
                thing.Destroy(DestroyMode.Vanish); if (corridor) carved++; else cleared++;
            }
            map.roofGrid.SetRoof(cell, null); map.snowGrid?.SetDepth(cell, 0);
            if (!cell.Walkable(map)) map.terrainGrid.SetTerrain(cell, DefDatabase<TerrainDef>.GetNamed(plan.Style.Terrain));
        }
        private bool Connected(IntVec3 from, IntVec3 to)
        {
            var width = map.Size.x; var visited = new bool[width * map.Size.z]; var queue = new Queue<IntVec3>();
            queue.Enqueue(from); visited[from.z * width + from.x] = true;
            while (queue.Count > 0)
            {
                var current = queue.Dequeue(); if (current == to) return true;
                foreach (var offset in GenAdj.CardinalDirections)
                {
                    var next = current + offset;
                    if (!next.InBounds(map)) continue;
                    var index = next.z * width + next.x;
                    if (visited[index] || !next.Standable(map)) continue;
                    visited[index] = true; queue.Enqueue(next);
                }
            }
            return false;
        }
    }
}
