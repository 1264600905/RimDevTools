#nullable disable
using System;
using System.Linq;
using System.Xml.Linq;

namespace LogSyncMod.AutoTest.Scenarios
{
    public static class ScenarioViews
    {
        public static readonly string[] Types = {
            "HumanoidThreeView", "AnimalThreeView", "MechanoidThreeView", "AlienThreeView", "ApparelThreeView", "WeaponThreeView",
            "PlantGrowthView", "BuildingView", "TerrainPatchView", "PawnLifeStageThreeView", "HairThreeView", "HeadTypeThreeView",
            "BodyTypeThreeView", "GeneThreeView", "XenotypeThreeView", "LoadoutView"
        };
        public static string[] Directions(XElement view) => ScenarioLoader.A(view, "directions", "South,East,North").Split(',').Select(s => s.Trim()).ToArray();

        public static void Validate(XElement e, string path)
        {
            void Fail(string reason) => ScenarioLoader.Fail(e, path, reason);
            var name = e.Name.LocalName;
            if (name == "View")
            {
                var directions = Directions(e);
                if (directions.Length < 1 || directions.Length > 4 || directions.Distinct().Count() != directions.Length || directions.Any(d => !new[] { "North", "East", "South", "West" }.Contains(d))) Fail("View requires 1..4 unique cardinal directions");
                if (!new[] { "standing", "weaponReady", "auto" }.Contains(ScenarioLoader.A(e, "pose", "standing"))) Fail("View pose must be standing/weaponReady/auto");
                if (!new[] { "preserve", "isolate" }.Contains(ScenarioLoader.A(e, "loadout", "preserve"))) Fail("View loadout must be preserve/isolate");
                if (ScenarioLoader.A(e, "loadout") == "isolate" && string.IsNullOrWhiteSpace(ScenarioLoader.A(e, "targetDef"))) Fail("isolated View requires targetDef");
                if (!new[] { "true", "false" }.Contains(ScenarioLoader.A(e, "composite", "true"))) Fail("View composite must be true/false");
            }
            if (e.Attribute("cameraSize") != null && (ScenarioLoader.D(e, "cameraSize") < 11 || ScenarioLoader.D(e, "cameraSize") > 100)) Fail("cameraSize must be 11..100; smaller native roots can place the camera below Pawn render layers in 1.7");
            if (e.Attribute("stableFrames") != null && (ScenarioLoader.I(e, "stableFrames") < 3 || ScenarioLoader.I(e, "stableFrames") > 120)) Fail("stableFrames must be 3..120 repaint frames");
            if (e.Attribute("growth") != null && (ScenarioLoader.D(e, "growth") < 0 || ScenarioLoader.D(e, "growth") > 1) || name == "SetPlantGrowth" && (ScenarioLoader.D(e, "value") < 0 || ScenarioLoader.D(e, "value") > 1)) Fail("plant growth must be 0..1");
            if (e.Attribute("rotation") != null && !new[] { "North", "East", "South", "West" }.Contains(ScenarioLoader.A(e, "rotation"))) Fail("invalid cardinal rotation");
            if (name == "Age" && (!e.HasAttributes || e.Attributes().Any(a => ScenarioLoader.D(e, a.Name.LocalName) < 0))) Fail("Age requires nonnegative explicit biologicalYears/chronologicalYears");
            if (name == "PawnAppearance" && !new[] { "kind", "race", "gender", "hair", "bodyType", "headType", "hairColor", "skinColor", "lifeStage", "biologicalYears", "rotation" }.Contains(ScenarioLoader.A(e, "property"))) Fail("unknown PawnAppearance property");
            if (name == "HasGene" && e.Attribute("active") != null && !new[] { "true", "false" }.Contains(ScenarioLoader.A(e, "active"))) Fail("active must be true/false");
            if (new[] { "GeneResource", "Psyfocus", "AxolotlQi" }.Contains(name) && (ScenarioLoader.D(e, "value") < 0 || name == "Psyfocus" && ScenarioLoader.D(e, "value") > 1)) Fail("resource value is outside its explicit supported range");
            if (name == "Resources" && e.Elements("GeneResource").GroupBy(g => ScenarioLoader.A(g, "def")).Any(g => g.Count() > 1)) Fail("duplicate GeneResource definition");
            // Color and quality are checked again by the native backend against actual component support.
            if (e.Attribute("color") != null && !System.Text.RegularExpressions.Regex.IsMatch(ScenarioLoader.A(e, "color"), "^#[0-9a-fA-F]{6}([0-9a-fA-F]{2})?$")) Fail("color must be #RRGGBB or #RRGGBBAA");
        }
    }
}
