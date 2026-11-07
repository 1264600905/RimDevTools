using System.Xml.Linq;
using LogSyncMod.AutoTest.Scenarios;

internal static class GroupCombatInheritanceTests
{
    internal static void Run(Action<bool, string> check)
    {
        var root = XElement.Parse(GroupCombatTests.Xml);
        var basis = root.Element("Case")!;
        basis.SetAttributeValue("abstract", true);
        var id = (string)basis.Attribute("id")!;
        basis.Element("testDefName")!.Remove();
        root.Add(XElement.Parse($"<Case id='Original' extends='{id}'><testDefName>Original</testDefName></Case>"));
        root.Add(XElement.Parse($"""
            <Case id='Changed' extends='{id}'><testDefName>Changed</testDefName>
              <Actors><PawnGroup id='riflemen' count='7'><basePawnKind>Bulbfreak</basePawnKind></PawnGroup></Actors>
              <Patches>
                <SetAttribute xpath="GroupCombat" name="durationSeconds" value="90"/>
                <ReplaceNode xpath="Actors/PawnGroup[@id='riflemen']/basePawnKind"><Value><basePawnKind>Fingerspike</basePawnKind></Value></ReplaceNode>
                <AddNode xpath="GroupCombat"><Value><End confirmationTicks="600"/></Value></AddNode>
                <RemoveNode xpath="GroupCombat/Experiments"/>
              </Patches>
            </Case>
            """));
        ScenarioPlan Compile(XElement suite) => ScenarioLoader.Compile(new[] { new ScenarioInput { Path = "inheritance.autotest.xml", Sha256 = "hash", Xml = suite.ToString() } }, true);
        void Reject(XElement suite, string message)
        {
            try { Compile(suite); check(false, message); }
            catch (ScenarioConfigurationException) { check(true, message); }
        }
        var plan = Compile(root);
        var original = XElement.Parse(plan.Cases.Single(c => c.TestDefName == "Original").Xml);
        var changed = XElement.Parse(plan.Cases.Single(c => c.TestDefName == "Changed").Xml);
        check(plan.Cases.Count == 2 && plan.Cases.All(c => !c.Xml.Contains("Patches") && !c.Xml.Contains("extends=")), "abstract battles are omitted and derivatives freeze fully expanded XML");
        var left = changed.Element("Actors")!.Elements().First();
        check((string?)left.Attribute("count") == "7" && (string?)left.Attribute("team") == "A" && left.Element("basePawnKind")!.Value == "Fingerspike", "battle inheritance merges actor IDs while preserving formation and team");
        check((string?)original.Element("Actors")!.Elements().First().Attribute("count") == "20" && original.Element("Actors")!.Elements().First().Element("basePawnKind")!.Value == "Colonist", "patching one battle cannot mutate its base or sibling");
        check((string?)changed.Element("GroupCombat")!.Attribute("durationSeconds") == "90" && changed.Element("GroupCombat")!.Element("Experiments") == null && (string?)changed.Element("GroupCombat")!.Element("End")!.Attribute("confirmationTicks") == "600", "patches support attribute, replacement, addition and removal operations");
        check(XNode.DeepEquals(original.Element("Actors")!.Elements().Last(), changed.Element("Actors")!.Elements().Last()), "replacing A preserves the complete B roster");
        var multi = new XElement(root); multi.Add(XElement.Parse("<Case id='Third' extends='Changed'><testDefName>Third</testDefName><Patches><SetText xpath='Actors/PawnGroup[@id=\"riflemen\"]/basePawnKind' value='Bulbfreak'/></Patches></Case>"));
        check(Compile(multi).Cases.Single(c => c.TestDefName == "Third").Xml.Contains("Bulbfreak"), "multi-level inheritance applies ancestor patches once");
        var cycle = new XElement(root); cycle.Elements("Case").First().SetAttributeValue("extends", "Changed"); Reject(cycle, "battle inheritance cycles rejected");
        var missing = new XElement(root); missing.Elements("Case").Last().SetAttributeValue("extends", "missing"); Reject(missing, "unknown base battles rejected");
        var identity = new XElement(root); identity.Elements("Case").Last().Element("testDefName")!.Remove(); Reject(identity, "derivatives require independent stable test names");
        foreach (var xpath in new[] { "Missing", "..", "/Case", "Actors/@id", "[" })
        {
            var invalid = new XElement(root); invalid.Elements("Case").Last().Element("Patches")!.Elements().First().SetAttributeValue("xpath", xpath);
            Reject(invalid, "invalid, empty or out-of-scope patch rejected: " + xpath);
        }
        var invalidValue = new XElement(root); invalidValue.Elements("Case").Last().Element("Patches")!.Elements().First().SetAttributeValue("value", "bad-number"); Reject(invalidValue, "patched values still pass normal schema validation");
        var duplicate = new XElement(root); duplicate.Elements("Case").Last().Add(new XElement("Actors")); Reject(duplicate, "inheritance cannot hide duplicate singleton blocks");
        var injection = new XElement(root); injection.Elements("Case").Last().Element("Patches")!.Add(XElement.Parse("<AddNode xpath='Actors'><Value><Pawn id='injected' Class='Bad'/></Value></AddNode>")); Reject(injection, "patch payload cannot bypass the runtime XML schema");
    }
}
