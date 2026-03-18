using System;
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
            try
            {
                if (map != null)
                {
                    PocketMapUtility.DestroyPocketMap(map);
                    return;
                }
            }
            catch
            {
            }

            try
            {
                if (parent != null && !parent.Destroyed)
                {
                    parent.Destroy();
                }
            }
            catch
            {
            }
        }
    }
}
