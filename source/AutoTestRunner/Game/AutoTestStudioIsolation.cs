using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using LogSyncMod.AutoTest.Scenarios;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace LogSyncMod.AutoTest
{
    // Ordinary maps inherit tile mutators and biome gen steps even when a custom
    // generator is supplied. Keep the owned blank studio independent of those
    // additions throughout generation and runtime, without changing world tiles.
    internal static class AutoTestStudioIsolation
    {
        private sealed class State
        {
            internal Tile Tile;
            internal ArenaStudioIsolationResult Result;
        }
        private static readonly ConditionalWeakTable<Map, State> states = new();
        internal static bool Owns(Map map) => map != null && !map.IsPocketMap
            && map.Parent?.def?.defName == "LogSync_AutoTestStudioParent" && map.generatorDef?.defName == "LogSync_AutoTestStudioMap";

        private static State For(Map map) => states.GetValue(map, m => {
            var source = Find.WorldGrid[m.Tile];
            return new State {
                Tile = new SurfaceTile(m.Tile) { PrimaryBiome = source.PrimaryBiome, elevation = source.elevation,
                    hilliness = source.hilliness, temperature = source.temperature, rainfall = source.rainfall,
                    swampiness = source.swampiness, pollution = source.pollution },
                Result = new ArenaStudioIsolationResult { OriginalTileMutators = source.Mutators.Select(d => d.defName).ToList() }
            };
        });
        internal static Tile TileFor(Map map) => For(map).Tile;
        internal static void Filter(Map map, ref IEnumerable<GenStepWithParams> steps)
        {
            if (!Owns(map)) return;
            var state = For(map);
            var requested = map.generatorDef.genSteps;
            foreach (var step in steps.Where(s => !requested.Contains(s.def)))
                if (!state.Result.SuppressedGenSteps.Contains(step.def.defName)) state.Result.SuppressedGenSteps.Add(step.def.defName);
            steps = requested.Select(d => new GenStepWithParams(d, default)).ToList();
        }
        internal static ArenaStudioIsolationResult Inspect(Map map)
        {
            if (!Owns(map)) return null;
            var state = For(map);
            state.Result.WorldTileUnchanged = Find.WorldGrid[map.Tile].Mutators.Select(d => d.defName).SequenceEqual(state.Result.OriginalTileMutators);
            state.Result.RemainingThingsBeforeActors = map.listerThings.AllThings.Count;
            state.Result.NonSoilCellsBeforeActors = map.AllCells.Count(c => map.terrainGrid.TerrainAt(c) != TerrainDefOf.Soil);
            if (!state.Result.WorldTileUnchanged || state.Result.RemainingThingsBeforeActors != 0 || state.Result.NonSoilCellsBeforeActors != 0)
                throw new InvalidOperationException("The blank test studio contains unexpected generated content or its source tile changed");
            return state.Result;
        }
    }

    [HarmonyPatch(typeof(Map), nameof(Map.TileInfo), MethodType.Getter)]
    internal static class AutoTestStudioTilePatch
    {
        private static bool Prefix(Map __instance, ref Tile __result)
        {
            if (!AutoTestStudioIsolation.Owns(__instance)) return true;
            __result = AutoTestStudioIsolation.TileFor(__instance);
            return false;
        }
    }
    [HarmonyPatch(typeof(MapGenerator), nameof(MapGenerator.GenerateContentsIntoMap))]
    internal static class AutoTestStudioContentsPatch
    {
        private static void Prefix(Map map, ref IEnumerable<GenStepWithParams> genStepDefs) => AutoTestStudioIsolation.Filter(map, ref genStepDefs);
    }
    [HarmonyPatch(typeof(MapGenerator), nameof(MapGenerator.MapGeneratorPostInit))]
    internal static class AutoTestStudioPostInitPatch
    {
        private static void Prefix(Map map, ref IEnumerable<GenStepWithParams> genStepDefs) => AutoTestStudioIsolation.Filter(map, ref genStepDefs);
    }
}
