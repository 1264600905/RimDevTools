using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using LogSyncMod.AutoTest;
using LogSyncMod.AutoTest.Scenarios;

internal static class ApparelLoadoutPlannerTests
{
    internal static void Run(Action<bool, string> check)
    {
        var native = XElement.Parse("<Apparel><Add def='Shirt'/><Add def='FlakVest'/><Add def='Helmet'/></Apparel>");
        var layers = new Dictionary<string, string> { ["Shirt"] = "Skin", ["FlakVest"] = "Middle", ["Phoenix"] = "Middle", ["Helmet"] = "Head", ["Mask"] = "Head", ["ChildOutfit"] = "Skin" };
        XElement Plan(string xml) => ApparelLoadoutPlanner.Resolve(native, xml == null ? null : XElement.Parse(xml),
            (a, b) => layers[a] != layers[b], def => def == "ChildOutfit" ? "wrong age" : null, (def, layer) => layers[def] == layer);
        var source = native.ToString();
        var inherited = Plan(null);
        check(inherited.Elements().Count() == 3 && (string)inherited.Attribute("mode") == "replace", "native inherited clothing materializes to explicit XML");
        var phoenix = Plan("<Apparel mode='patch'><Add def='Phoenix' quality='Excellent' stuff='Steel'/></Apparel>");
        check(phoenix.Elements().Select(e => (string)e.Attribute("def")).SequenceEqual(new[] { "Shirt", "Helmet", "Phoenix" }), "explicit phoenix armor replaces inherited conflicting vest while retaining shirt and helmet");
        check((string)phoenix.Elements().Last().Attribute("quality") == "Excellent" && (string)phoenix.Elements().Last().Attribute("stuff") == "Steel", "native replacement preserves selected quality and material");
        check(native.ToString() == source, "clothing planning does not mutate the native snapshot");
        var replaced = Plan("<Apparel mode='replace'><Add def='Phoenix'/><Add def='Mask'/></Apparel>");
        check(replaced.Elements().Count() == 2 && !replaced.Elements().Any(e => (string)e.Attribute("def") == "Shirt"), "replace mode does not silently reintroduce inherited clothes");
        var layer = Plan("<Apparel mode='patch'><Replace matchLayer='Middle' withDef='Phoenix'/></Apparel>");
        check(layer.Elements().Any(e => (string)e.Attribute("def") == "Phoenix") && layer.Elements().Count() == 3, "existing layer replacement XML is retained");
        var removed = Plan("<Apparel mode='patch'><Remove def='Helmet'/><Add def='Mask'/></Apparel>");
        check(!removed.Elements().Any(e => (string)e.Attribute("def") == "Helmet"), "remove rules run before native loadout materialization");
        Reject("<Apparel mode='replace'><Add def='Helmet'/><Add def='Mask'/></Apparel>", "explicit conflicting choices cannot become an impossible loadout");
        Reject("<Apparel mode='replace'><Add def='ChildOutfit'/></Apparel>", "native age and wearability rejection prevents saving invalid clothes");
        Reject("<Apparel mode='patch'><Replace matchDef='Missing' withDef='Mask'/></Apparel>", "missing replace target retains existing fail semantics");
        var removePrevious = Plan("<Apparel mode='replace'><Add def='Helmet'/><Remove def='Helmet'/><Add def='Mask'/></Apparel>");
        check(removePrevious.Elements().Count() == 1, "removing an explicit item also removes its conflict constraint");
        void Reject(string xml, string text)
        { try { Plan(xml); throw new InvalidOperationException("Accepted invalid apparel"); } catch (ScenarioConfigurationException) { check(true, text); } }
    }
}
