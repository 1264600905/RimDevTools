using System.Text.Json;
using System.Text.Json.Nodes;
using RimDevTools;

internal static class ScenarioResultTests
{
    internal static void Run(Action<bool, string> check)
    {
        const string target = "{\"PackageId\":\"demo.mod\",\"DefType\":\"AbilityDef\",\"DefName\":\"Ability\",\"CaseType\":\"SkillPocketMap\",\"Variant\":\"\",\"EvidenceSteps\":[\"effect\"]}";
        var definition = "{\"CaseId\":\"Rule/default/1\",\"TestDefName\":\"Rule\",\"CoverageMode\":\"replace\",\"Coverage\":[" + target + "]}";
        var outcome = "{\"CaseId\":\"Rule/default/1\",\"TestDefName\":\"Rule\",\"Status\":\"passed\",\"Coverage\":[" + target + "],\"Trace\":[{\"Stage\":\"step.completed\",\"Details\":\"effect\"},{\"Stage\":\"coverage.target_used\",\"Details\":\"demo.mod|abilitydef|ability|skillpocketmap|\"}]}";
        var json = "{\"runMode\":\"MixedScenario\",\"manifestPlanned\":1,\"manifestExecuted\":1,\"batchSize\":1,\"verifiedCoverageKeys\":[\"demo.mod|abilitydef|ability|skillpocketmap|\"],\"scenarioSelection\":{\"Cases\":[" + definition + "],\"OrdinaryCaseIds\":[],\"ReplacementLimitExceeded\":false,\"Replacements\":[{\"OrdinaryCaseId\":\"ordinary\",\"ScenarioCaseId\":\"Rule/default/1\",\"TestDefName\":\"Rule\",\"Target\":" + target + "}]},\"results\":[{\"caseId\":\"Rule/default/1\",\"status\":\"passed\",\"scenario\":" + outcome + "}]}";
        string? Validate(string value) { using var d = JsonDocument.Parse(value); return ScenarioResultValidator.Validate(d.RootElement); }
        void Reject(Action<JsonNode> mutation, string message)
        { var node = JsonNode.Parse(json)!; mutation(node); check(Validate(node.ToJsonString()) != null, message); }
        check(Validate(json) == null, "mixed scenario result reconciles physical execution and logical coverage");
        Reject(n => n["verifiedCoverageKeys"]![0] = "demo.mod|abilitydef|untested|skillpocketmap|", "CLI rejects fabricated verified target");
        Reject(n => n["results"]![0]!["status"] = "unsupported", "unsupported scenario cannot satisfy replacement coverage");
        Reject(n => n["results"]![0]!["scenario"]!["Trace"] = new JsonArray(), "CLI rejects coverage without completed predicate evidence");
        Reject(n => n["results"]![0]!["scenario"]!["Trace"]!.AsArray().RemoveAt(1), "CLI rejects a covered Def that was only configured but never exercised");
        Reject(n => n["results"]![0]!["scenario"]!["TestDefName"] = "Different", "CLI rejects outcome for another test definition");
        Reject(n => n["results"]!.AsArray().Add(n["results"]![0]!.DeepClone()), "CLI rejects duplicate physical results");
        Reject(n => n["scenarioSelection"]!["OrdinaryCaseIds"]!.AsArray().Add("unexecuted"), "CLI rejects missing ordinary case in mixed plan");
        Reject(n => n["scenarioSelection"]!["ReplacementLimitExceeded"] = true, "CLI checks replacement overflow against physical count");
        Reject(n => n["runMode"] = "ExternalScenario", "scenario-only cannot claim ordinary replacements");
        Reject(n => n["scenarioSelection"]!["Replacements"]![0]!["Target"]!["Variant"] = "other", "CLI checks exact variant in replacement table");
        var failed = JsonNode.Parse(json)!; failed["results"]![0]!["status"] = "failed"; failed["verifiedCoverageKeys"] = new JsonArray();
        check(Validate(failed.ToJsonString()) == null, "failed physical scenario remains recorded with no verified coverage");
    }
}
