using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using HarmonyLib;
using LogSyncMod.AutoTest.Scenarios;
using RimWorld;
using Verse;
using Verse.AI;

namespace LogSyncMod.AutoTest
{
    internal sealed partial class ScenarioGameBackend
    {
        // Scoped fixture temperature only; loaded generator/biome Defs remain read-only.
        internal static readonly Dictionary<Map, float> OutdoorTemperatures = new Dictionary<Map, float>();

        private static IEnumerable<IntVec3> Rectangle(XElement e) => CellRect.FromLimits(I(e, "x"), I(e, "z"), I(e, "x") + I(e, "width") - 1, I(e, "z") + I(e, "height") - 1).Cells;

        private void PrepareEnvironment(XElement scene)
        {
            var env = scene.Element("Environment");
            if (env?.Attribute("temperature") != null) OutdoorTemperatures[Map] = (float)ScenarioLoader.D(env, "temperature");
            if (env?.Attribute("weather") != null)
            {
                var weather = Def<WeatherDef>(A(env, "weather"));
                Map.weatherManager.TransitionTo(weather);
                Map.weatherManager.curWeather = weather; Map.weatherManager.lastWeather = weather;
            }
            if (env?.Attribute("roof") != null)
                foreach (var cell in SceneCells) Map.roofGrid.SetRoof(cell, A(env, "roof") == "none" ? null : Def<RoofDef>(A(env, "roof")));
            foreach (var rect in scene.Element("Layout")?.Elements() ?? Enumerable.Empty<XElement>())
                foreach (var cell in Rectangle(rect))
                {
                    if (!cell.InBounds(Map)) throw new ScenarioConfigurationException("Layout cell outside arena");
                    if (rect.Name == "TerrainRect") Map.terrainGrid.SetTerrain(cell, Def<TerrainDef>(A(rect, "def")));
                    else if (rect.Name == "RoofRect") Map.roofGrid.SetRoof(cell, A(rect, "def", "none") == "none" ? null : Def<RoofDef>(A(rect, "def")));
                    else
                    {
                        var thing = MakeThing(A(rect, "def"), A(rect, "stuff")); owned.Add(thing);
                        if (thing is Pawn || thing is Corpse || thing.def.Size.x != 1 || thing.def.Size.z != 1) throw new ScenarioConfigurationException("ThingRect requires a one-cell non-Pawn/non-Corpse Def");
                        var rotation = A(rect, "rotation", "North") switch { "North" => Rot4.North, "East" => Rot4.East, "South" => Rot4.South, "West" => Rot4.West, _ => throw new ScenarioConfigurationException("Unknown layout rotation") };
                        GenSpawn.Spawn(thing, cell, Map, rotation, WipeMode.Vanish);
                    }
                }
            Map.skyManager.SkyManagerUpdate();
        }

        private void ValidateEnvironment(XElement scene)
        {
            var env = scene.Element("Environment");
            if (env?.Attribute("temperature") != null && Math.Abs(Map.mapTemperature.OutdoorTemp - ScenarioLoader.D(env, "temperature")) > .01)
                throw new InvalidOperationException("Outdoor fixture temperature did not apply");
            if (env?.Attribute("weather") != null && Map.weatherManager.curWeather.defName != A(env, "weather")) throw new InvalidOperationException("Weather did not apply");
            foreach (var rect in scene.Element("Layout")?.Elements() ?? Enumerable.Empty<XElement>())
                foreach (var cell in Rectangle(rect))
                {
                    if (rect.Name == "TerrainRect" && Map.terrainGrid.TerrainAt(cell).defName != A(rect, "def")) throw new InvalidOperationException("Layout terrain mismatch");
                    if (rect.Name == "RoofRect" && (Map.roofGrid.RoofAt(cell)?.defName ?? "none") != A(rect, "def", "none")) throw new InvalidOperationException("Layout roof mismatch");
                    if (rect.Name == "ThingRect" && !cell.GetThingList(Map).Any(t => t.def.defName == A(rect, "def"))) throw new InvalidOperationException("Layout object mismatch");
                }
            foreach (var r in scene.Element("Validation")?.Elements("Reachable") ?? Enumerable.Empty<XElement>())
            {
                var from = new IntVec3(I(r, "fromX"), 0, I(r, "fromZ")); var to = new IntVec3(I(r, "toX"), 0, I(r, "toZ"));
                if (!from.InBounds(Map) || !to.InBounds(Map) || !Map.reachability.CanReach(from, new LocalTargetInfo(to), PathEndMode.OnCell, TraverseParms.For(TraverseMode.PassDoors))) throw new InvalidOperationException("Scene reachability validation failed: " + r);
            }
            if (A(env, "naturalSun", "false") == "true" && (!Map.Tile.Valid || Map.Biome.disableSkyLighting || Map.info.disableSunShadows || Map.weatherManager.curWeather.maxGlow < .99f))
                throw new ScenarioConfigurationException("Natural sun unavailable for loaded generator: tile=" + Map.Tile + ", biome=" + Map.Biome.defName + ", weather=" + Map.weatherManager.curWeather.defName + ", disableSkyLighting=" + Map.Biome.disableSkyLighting + ", disableShadows=" + Map.info.disableSunShadows + ". Choose a native outdoor generator/weather; sky brightness is never forced.");
            trace("scene.environment", "generator=" + Map.generatorDef.defName + "; size=" + Map.Size + "; tile=" + Map.Tile + "; biome=" + Map.Biome.defName + "; weather=" + Map.weatherManager.curWeather.defName + "; temperature=" + Map.mapTemperature.OutdoorTemp + "; naturalSun=" + A(env, "naturalSun", "false") + "; sunGlow=" + GenCelestial.CurCelestialSunGlow(Map) + "; skyGlow=" + Map.skyManager.CurSkyGlow + "; roofed=" + Map.AllCells.Count(c => c.Roofed(Map)) + "; fogged=" + Map.AllCells.Count(c => c.Fogged(Map)));
        }

        private bool SampleEnvironment(XElement c)
        {
            return A(c, "property") switch {
                "weather" => Map.weatherManager.curWeather.defName == A(c, "def"),
                "temperature" => InRange(Map.mapTemperature.OutdoorTemp, c),
                "skyGlow" => InRange(Map.skyManager.CurSkyGlow, c),
                "sunGlow" => InRange(GenCelestial.CurCelestialSunGlow(Map), c),
                "unfogged" => SceneCells.All(cell => !cell.Fogged(Map)),
                _ => throw new ScenarioConfigurationException("Unknown environment probe") };
        }
    }

    [HarmonyPatch(typeof(MapTemperature), "get_OutdoorTemp")]
    internal static class ScenarioOutdoorTemperaturePatch
    {
        private static bool Prefix(Map ___map, ref float __result)
        { if (!ScenarioGameBackend.OutdoorTemperatures.TryGetValue(___map, out var value)) return true; __result = value; return false; }
    }
    [HarmonyPatch(typeof(MapTemperature), "get_SeasonalTemp")]
    internal static class ScenarioSeasonalTemperaturePatch
    {
        private static bool Prefix(Map ___map, ref float __result)
        { if (!ScenarioGameBackend.OutdoorTemperatures.TryGetValue(___map, out var value)) return true; __result = value; return false; }
    }
}
