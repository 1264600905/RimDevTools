using System.Linq;
using RimWorld;
using Verse;

namespace LogSyncMod.AutoTest
{
    public class GenStep_LogSyncAutoTestFlatSoil : GenStep
    {
        public override int SeedPart => 1284375612;

        public override void Generate(Map map, GenStepParams parms)
        {
            if (map == null)
            {
                return;
            }

            using (map.pathing.DisableIncrementalScope())
            {
                foreach (Thing thing in map.listerThings.AllThings.ToList())
                {
                    if (thing == null || thing.Destroyed)
                    {
                        continue;
                    }

                    thing.Destroy(DestroyMode.Vanish);
                }

                foreach (IntVec3 cell in map.AllCells)
                {
                    map.terrainGrid.SetTerrain(cell, TerrainDefOf.Soil);
                    map.roofGrid.SetRoof(cell, null);

                    if (map.snowGrid != null)
                    {
                        map.snowGrid.SetDepth(cell, 0f);
                    }
                }
            }
        }
    }
}
