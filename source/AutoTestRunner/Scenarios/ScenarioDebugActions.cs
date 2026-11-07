#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace LogSyncMod.AutoTest.Scenarios
{
    public static class ScenarioDebugActions
    {
        // Built-in adapters coexist with explicit XML method bindings; neither opens menus.
        public static readonly IReadOnlyDictionary<string, string> Parameters = new Dictionary<string, string> {
            { "native.fillNeeds", "" }, { "native.fillHemogen", "value" }, { "native.setPsyfocus", "value" },
            { "native.addHediff", "def severity part durationTicks" }, { "native.removeHediff", "def part" },
            { "native.refuel", "" }, { "native.chargeBattery", "" }, { "native.healInjuries", "" },
            { "axolotl.fillQi", "value" }
        };

        public static void Validate(XElement step, string path, XElement binding = null)
        {
            var action = ScenarioLoader.A(step, "actionId");
            if (binding == null) binding = ScenarioActionBindings.Find(step);
            var builtIn = Parameters.TryGetValue(action ?? "", out var allowed);
            if (!builtIn && binding == null) ScenarioLoader.Fail(step, path, "unregistered/non-interactive DebugAction: " + action);
            if (!builtIn) { allowed = ""; ScenarioActionBindings.ValidateCall(step, binding, path); }
            else if (step.Elements("Arg").Any()) ScenarioLoader.Fail(step, path, "built-in DebugAction uses its declared attributes, not Arg");
            foreach (var a in step.Attributes().Where(a => a.Name != "id" && a.Name != "actionId"))
                if (!allowed.Split(' ').Contains(a.Name.ToString())) ScenarioLoader.Fail(step, path, "parameter is not supported by " + action + ": " + a.Name);
            if (action == "native.addHediff" || action == "native.removeHediff")
                if (string.IsNullOrWhiteSpace(ScenarioLoader.A(step, "def"))) ScenarioLoader.Fail(step, path, "health action requires def");
            var select = step.Element("Select");
            if (select == null) ScenarioLoader.Fail(step, path, "DebugAction requires exactly one Select");
            var mode = ScenarioLoader.A(select, "mode");
            if (mode == "single")
            {
                if (ScenarioLoader.A(select, "actor") == null || select.Attributes().Any(a => a.Name != "mode" && a.Name != "actor"))
                    ScenarioLoader.Fail(select, path, "single selection requires only actor");
            }
            else if (mode == "rectangle")
            {
                if (select.Attribute("actor") != null) ScenarioLoader.Fail(select, path, "rectangle selection uses coordinates, not actor");
                foreach (var n in new[] { "minX", "minZ", "maxX", "maxZ" })
                    if (select.Attribute(n) == null) ScenarioLoader.Fail(select, path, "rectangle requires " + n);
                if (ScenarioLoader.I(select, "minX") > ScenarioLoader.I(select, "maxX") || ScenarioLoader.I(select, "minZ") > ScenarioLoader.I(select, "maxZ")) ScenarioLoader.Fail(select, path, "reversed rectangle");
                var filter = ScenarioLoader.A(select, "filter");
                if (!new[] { "allScenePawns", "enemies", "allies" }.Contains(filter)) ScenarioLoader.Fail(select, path, "unknown scene-pawn filter");
                if (filter != "allScenePawns" && select.Attribute("relativeTo") == null) ScenarioLoader.Fail(select, path, "enemy/ally filters require relativeTo actor");
                if (filter == "allScenePawns" && select.Attribute("relativeTo") != null) ScenarioLoader.Fail(select, path, "allScenePawns does not use relativeTo");
            }
            else ScenarioLoader.Fail(select, path, "Select mode must be single/rectangle");
            if (step.Attribute("value") != null && (ScenarioLoader.D(step, "value") < 0 || ScenarioLoader.D(step, "value") > 1)) ScenarioLoader.Fail(step, path, "resource fraction must be 0..1");
        }
    }
}
