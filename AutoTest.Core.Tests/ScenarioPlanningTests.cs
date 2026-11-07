using System.Xml.Linq;
using LogSyncMod.AutoTest.Scenarios;

internal static class ScenarioPlanningTests
{
    internal static void Run(Action<bool, string> check)
    {
        const string body = "<Scene/><Actors><Thing id='weapon' def='${weapon}'/></Actors><Steps><Require id='exists'><ThingExists actor='weapon' def='${weapon}'/></Require></Steps><Success><StepSucceeded ref='exists'/></Success>";
        const string coverage = "<Coverage mode='replace'><Target defType='ThingDef' defName='${weapon}' caseType='MeleeWeaponPocketMap'><Evidence step='exists'/></Target></Coverage>";
        const string rows = "<Matrix><Row id='knife'><Param name='weapon' value='MeleeWeapon_Knife'/></Row><Row id='club'><Param name='weapon' value='MeleeWeapon_Club'/></Row></Matrix>";
        string Suite(string extra = "", string scene = body) => "<TestSuite schemaVersion='1' id='tests'><Case id='a' caseType='MeleeWeaponPocketMap'><testDefName>Matrix_Weapons</testDefName>" + rows + extra + scene + "</Case></TestSuite>";
        ScenarioPlan Compile(string xml) => ScenarioLoader.Compile(new[] { new ScenarioInput { Path = "planning.xml", Sha256 = "hash", Xml = xml } });
        void Reject(string xml, string message)
        { try { Compile(xml); check(false, message); } catch (ScenarioConfigurationException ex) { check(ex.Message.Contains("planning.xml"), message); } }
        var plan = Compile(Suite(coverage));
        check(plan.Cases.Count == 2 && plan.Cases[0].CaseId == "Matrix_Weapons/knife/1" && plan.Cases[1].CaseId == "Matrix_Weapons/club/1", "matrix instances keep one stable test definition and distinct row identities");
        check(plan.Cases.All(c => !c.Xml.Contains("${") && XElement.Parse(c.Xml).Element("Matrix") == null) && plan.Cases[0].Coverage[0].DefName == "MeleeWeapon_Knife", "matrix replaces actor/action/coverage values before validation and freezes each physical case");
        check(plan.Cases[0].Coverage[0].Variant == "" && plan.Cases[0].Coverage[0].PackageId == "ludeon.rimworld", "coverage inherits package identity and empty variant is exact");
        var named = Compile(Suite(coverage)); ScenarioLoader.SelectRules(named, new[] { "matrix_weapons" });
        check(named.Cases.Count == 2 && named.SelectedTestDefNames.Single() == "Matrix_Weapons", "named selection resolves stable identity case-insensitively and retains all matrix instances");
        try { ScenarioLoader.SelectRules(named, new[] { "missing" }); check(false, "unknown rule name rejected"); } catch (ScenarioConfigurationException) { check(true, "unknown rule name rejected"); }
        var ambiguous = Compile(Suite().Replace("<Case id='a'", "<Case name='shared' id='a'").Replace("</TestSuite>", "<Case id='b' name='shared'><testDefName>OtherRule</testDefName>" + body.Replace("${weapon}", "Steel") + "</Case></TestSuite>"));
        try { ScenarioLoader.SelectRules(ambiguous, new[] { "shared" }); check(false, "ambiguous display name rejected"); } catch (ScenarioConfigurationException) { check(true, "ambiguous display name rejected"); }
        Reject(Suite(coverage).Replace("id='club'", "id='KNIFE'"), "matrix row IDs reject case-insensitive collision");
        Reject(Suite().Replace("${weapon}", "${undefined}"), "matrix unknown parameter is diagnosed");
        Reject(Suite().Replace("value='MeleeWeapon_Club'", "value='${weapon}'"), "matrix recursive expansion is rejected");
        Reject(Suite().Replace("name='weapon' value='MeleeWeapon_Club'", "name='unused' value='MeleeWeapon_Club'"), "matrix row must satisfy the same placeholders");
        Reject(Suite().Replace("<testDefName>Matrix_Weapons", "<testDefName>${weapon}"), "matrix cannot rename rule identity");
        Reject(Suite(coverage).Replace("<Evidence step='exists'/>", ""), "replacement cannot claim coverage with no predicate evidence");
        Reject(Suite(coverage).Replace("step='exists'", "step='missing'"), "coverage evidence points at an existing predicate step");
        Reject(Suite(coverage).Replace("<Target defType", "<Target defType").Replace("caseType='MeleeWeaponPocketMap'><Evidence", "caseType='ItemInfoCard'><Evidence"), "function coverage cannot replace a UI check");
        Reject(Suite(coverage).Replace("caseType='MeleeWeaponPocketMap'><Evidence", "caseType='AbilityGizmoUi'><Evidence"), "skill effect coverage cannot replace its independent Gizmo UI check");
        Reject(Suite(coverage).Replace("defName='${weapon}'", "defName='Untested'"), "coverage cannot claim a target unused by actors and actions");
        Reject(Suite(coverage).Replace("<Coverage mode='replace'>", "<Coverage mode='invalid'>"), "invalid coverage mode rejected");
        var debugBody = body.Replace("<Steps>", "<Steps><DebugAction id='prepare' actionId='native.fillNeeds'><Select mode='rectangle' minX='0' minZ='0' maxX='30' maxZ='30' filter='allScenePawns'/></DebugAction>");
        check(Compile(Suite(scene: debugBody)).Cases.Count == 2, "registered scene rectangle DebugAction compiles without an interactive menu");
        Reject(Suite(scene: debugBody.Replace("native.fillNeeds", "Anything.Call")), "unregistered debug actions rejected before launch");
        Reject(Suite(scene: debugBody.Replace("actionId='native.fillNeeds'", "actionId='native.fillNeeds' value='1'")), "debug parameters are specific to the registered adapter");
        Reject(Suite(scene: debugBody.Replace("filter='allScenePawns'", "filter='enemies'")), "hostility filters require an explicit reference actor");
        Reject(Suite(scene: debugBody.Replace("minX='0'", "minX='31'")), "reversed debug rectangle rejected");
        var duplicate = Suite(coverage).Replace("</TestSuite>", "<Case id='b'><testDefName>Conflict</testDefName>" + rows + coverage + body + "</Case></TestSuite>");
        Reject(duplicate, "two replace rules claiming the same complete target key fail before launch");

        var ordinary = plan.Cases.Select((c, i) => new ScenarioOrdinaryCandidate { CaseId = "ordinary" + i, Target = c.Coverage[0] }).ToList();
        ordinary.Add(new ScenarioOrdinaryCandidate { CaseId = "ordinaryUi", Target = new ScenarioCoverageTarget { PackageId = "ludeon.rimworld", DefType = "ThingDef", DefName = "MeleeWeapon_Knife", CaseType = "ItemInfoCard" } });
        var selected = ScenarioPlanner.Select(plan.Cases, ordinary, 1);
        check(selected.Cases.Count == 2 && selected.ReplacementLimitExceeded && selected.OrdinaryCaseIds.Count == 0 && selected.Replacements.Count == 2, "all replacements survive a lower physical batch cap");
        selected = ScenarioPlanner.Select(plan.Cases, ordinary, 3);
        check(selected.Cases.Count == 2 && selected.OrdinaryCaseIds.SequenceEqual(new[] { "ordinaryUi" }), "replacement removes only the exact non-UI key, preserving information card");
        var supplemental = Compile(Suite()).Cases;
        selected = ScenarioPlanner.Select(supplemental, ordinary, 4);
        check(selected.Cases.Count == 1 && selected.OrdinaryCaseIds.Count == 3 && selected.Decisions.Count(d => d.Reason == "batch-limit") == 1, "remaining capacity executes out-of-range XML instead of discarding it");
        selected = ScenarioPlanner.Select(supplemental, ordinary, 3);
        check(selected.Cases.Count == 0 && selected.Decisions.Where(d => !d.Selected).All(d => d.Reason == "batch-limit"), "out-of-range XML reports batch exclusion when full");
        selected = ScenarioPlanner.Select(plan.Cases, ordinary, 1, scenarioOnly: true);
        check(selected.Cases.Count == 1 && selected.Replacements.Count == 0 && selected.OrdinaryCaseIds.Count == 0, "scenario-only never reports ordinary tests replaced or full regression complete");
        ordinary[0].Target = new ScenarioCoverageTarget { PackageId = "other.mod", DefType = "ThingDef", DefName = "MeleeWeapon_Knife", CaseType = "MeleeWeaponPocketMap" };
        ordinary[1].Target = new ScenarioCoverageTarget { PackageId = "ludeon.rimworld", DefType = "ThingDef", DefName = "MeleeWeapon_Club", CaseType = "MeleeWeaponPocketMap", Variant = "alternate" };
        selected = ScenarioPlanner.Select(plan.Cases, ordinary, 10);
        check(selected.Replacements.Count == 0 && selected.OrdinaryCaseIds.Count == 3, "package and variant mismatches are never wildcard replacements");
        var joint = plan.Cases[0]; joint.Coverage.Add(plan.Cases[1].Coverage[0]);
        ordinary[0].Target = joint.Coverage[0]; ordinary[1].Target = joint.Coverage[1];
        selected = ScenarioPlanner.Select(new[] { joint }, ordinary, 1);
        check(selected.Cases.Count == 1 && selected.Replacements.Count == 2 && !selected.ReplacementLimitExceeded, "one joint physical case can replace two logical cases using one slot");
        var presetXml = Suite(scene: body.Replace("<Scene/>", "<Scene preset='outdoor'><Environment temperature='18'/></Scene>"))
            .Replace("<Case", "<ScenePresets><Scene id='outdoor' generatorDef='RegisteredGenerator'><Environment weather='Clear' temperature='23' naturalSun='true'/></Scene></ScenePresets><Case");
        var environment = XElement.Parse(Compile(presetXml).Cases[0].Xml).Element("Scene").Element("Environment");
        check((string)environment.Attribute("temperature") == "18" && (string)environment.Attribute("weather") == "Clear" && (string)environment.Attribute("naturalSun") == "true", "preset overrides retain unspecified native environment fields");
        Reject(presetXml.Replace("preset='outdoor'", "preset='missing'"), "unknown scene preset is diagnosed before launch");
        Reject(presetXml.Replace("<Environment temperature='18'/>", "<Layout><TerrainRect def='Soil' x='49' z='0' width='2' height='2'/></Layout>"), "out-of-bounds layout rectangle rejected");
        var fixtureRoot = Path.Combine("AutoTest.Core.Tests", "Fixtures");
        foreach (var file in Directory.GetFiles(fixtureRoot, "ScenarioBatch2*.autotest.xml").Concat(Directory.GetFiles(fixtureRoot, "ScenarioBatch2*.example.xml")))
        {
            var frozen = ScenarioLoader.Load(file);
            check(frozen.Cases.Count > 0 && frozen.Cases.Select(c => c.CaseId).Distinct().Count() == frozen.Cases.Count, "batch 2 runnable fixture compiles with distinct physical IDs: " + Path.GetFileName(file));
        }
    }
}
