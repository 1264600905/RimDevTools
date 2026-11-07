using System.Xml.Linq;
using LogSyncMod.AutoTest.Scenarios;

internal static class ScenarioViewTests
{
    internal static void Run(Action<bool, string> check)
    {
        const string xml = "<TestSuite schemaVersion='1' id='views'><Case id='a' caseType='LoadoutView'><testDefName>View_Loadout</testDefName><Scene/><Actors><Pawn id='p'><basePawnKind>SpaceRefugee</basePawnKind><Age biologicalYears='25.5'/><Equipment><SetPrimary def='Gun_Revolver' quality='Excellent'/></Equipment></Pawn></Actors><Steps><View id='image' actor='p' directions='South,West,North,East' pose='weaponReady' loadout='preserve' cameraSize='11' stableFrames='5'/></Steps><Success><StepSucceeded ref='image'/></Success></Case></TestSuite>";
        ScenarioPlan Compile(string value) => ScenarioLoader.Compile(new[] { new ScenarioInput { Path = "views.xml", Sha256 = "frozen", Xml = value } });
        void Reject(string value, string description) { try { Compile(value); check(false, description); } catch (ScenarioConfigurationException e) { check(e.Message.Contains("views.xml"), description); } }
        var plan = Compile(xml);
        check(ScenarioViews.Types.Length == 16 && ScenarioViews.Types.All(ScenarioTypes.NonUi.Contains), "all sixteen planned view capabilities use canonical shared types");
        check(ScenarioViews.Directions(XElement.Parse(plan.Cases.Single().Xml).Element("Steps").Element("View")).SequenceEqual(new[] { "South", "West", "North", "East" }), "view directions retain explicit order and the fourth view");
        check(plan.Cases.Single().CaseId == "View_Loadout/default/1" && plan.Cases.Single().Xml.Contains("biologicalYears=\"25.5\""), "view inputs freeze fractional age, stable rule identity and per-frame configuration");
        Reject(xml.Replace("South,West,North,East", "South,South"), "duplicate capture direction rejected");
        Reject(xml.Replace("South,West,North,East", "South,Up"), "invalid capture direction rejected");
        Reject(xml.Replace("loadout='preserve'", "loadout='isolate'"), "isolation without explicit target cannot silently strip the pawn");
        Reject(xml.Replace("pose='weaponReady'", "pose='attack'"), "unknown pose rejected");
        Reject(xml.Replace("<Scene/>", "<Scene type='pocketMap'/>"), "three-view scenarios use the existing studio rather than a pocket map");
        Reject(xml.Replace("<Pawn id='p'>", "<Pawn id='p' x='50' z='25'>"), "view actors cannot escape the snapshotted studio area");
        Reject(xml.Replace("cameraSize='11'", "cameraSize='NaN'"), "nonfinite camera size rejected");
        Reject(xml.Replace("cameraSize='11'", "cameraSize='1'"), "unsafe camera size rejected");
        Reject(xml.Replace("stableFrames='5'", "stableFrames='0'"), "captures must settle for actual rendered frames");
        Reject(xml.Replace("biologicalYears='25.5'", "biologicalYears='-1'"), "negative biological age rejected");
        Reject(xml.Replace("<View", "<SetPlantGrowth id='growth' actor='p' value='1.1'/><View"), "out of range plant stage rejected before game launch");
        Reject(xml.Replace("quality='Excellent'", "quality='Excellent' color='red'"), "item colors must be explicit deterministic RGBA values");
        Reject(xml.Replace("<View", "<Require id='bad'><PawnAppearance actor='p' property='privateField' value='x'/></Require><View"), "appearance probes use closed properties rather than arbitrary field access");
        check(Compile(xml.Replace("<View", "<Require id='genes'><HasGene actor='p' def='Skin_Blue' active='true' xenogene='true'/></Require><View")).Cases.Count == 1, "gene probe freezes activity and endogene/xenogene constraints");
    }
}
