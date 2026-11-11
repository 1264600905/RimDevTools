using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using RimDevTools;

internal static class ScenarioPreflightTests
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        // Disposable source fixtures, not copies of logs, results or other test artifacts.
        var root = Path.Combine(Path.GetTempPath(), "RimDevToolsTests", Guid.NewGuid().ToString("N"));
        var previousDirectory = Environment.CurrentDirectory;
        Directory.CreateDirectory(root);
        try
        {
            Environment.CurrentDirectory = root;
            File.WriteAllText(Path.Combine(root, "mod_config.jsonc"), "{\"config_version\":2,\"game_version\":\"1.7\"}");
            check(CliArgs.Validate("test run", ["test", "run", "--mode", "auto", "--scenario-path", "input.xml", "--validate-only"]) == null, "CLI accepts the validate-only option");
            const string xml = """
                <TestSuite schemaVersion="1" id="validation">
                  <Case id="a">
                    <testDefName>Validation_Rule</testDefName>
                    <Scene sizeX="50"/>
                    <Actors><Pawn id="p"><basePawnKind>SpaceRefugee</basePawnKind></Pawn></Actors>
                    <Steps><Require id="ready"><ActorState actor="p" state="alive"/></Require></Steps>
                    <Success><StepSucceeded ref="ready"/></Success>
                  </Case>
                </TestSuite>
                """;
            var input = Path.Combine(root, "source.autotest.xml");
            File.WriteAllText(input, xml);
            var valid = ScenarioPreflight.Validate(input, "auto", [], out var plan);
            check(valid.Valid && valid.CaseCount == 1 && plan?.Cases.Single().TestDefName == "Validation_Rule" && valid.SelectedTestDefNames.SequenceEqual(["Validation_Rule"]), "preflight compiles and retains the actual execution plan");
            check(valid.RuntimeChecks.Length > 0, "successful static validation explicitly retains game-only checks");

            File.WriteAllText(input, xml.Replace("sizeX", "sizX"));
            var invalid = ScenarioPreflight.Validate(input, "auto", [], out plan);
            var issue = invalid.Diagnostics.Single();
            check(!invalid.Valid && plan == null && issue.File == input && issue.Line == 4 && issue.Element == "TestSuite/Case/Scene" && issue.Suggestion.Contains("sizeX"), "unknown attributes include original file, line, element and allowed fields");
            File.WriteAllText(input, xml.Replace("</TestSuite>", "</Broken>"));
            invalid = ScenarioPreflight.Validate(input, "auto", [], out _);
            check(!invalid.Valid && invalid.Diagnostics.Single() is { File: not null, Line: > 0, Column: > 0 }, "malformed XML has a structured source location");
            File.WriteAllText(input, "<!DOCTYPE TestSuite [<!ENTITY x SYSTEM 'file:///C:/windows/win.ini'>]>" + xml);
            check(!ScenarioPreflight.Validate(input, "auto", [], out _).Valid, "static preflight prohibits DTD and external entities");
            foreach (var broken in new[] {
                xml.Replace("sizeX=\"50\"", "sizeX=\"29\""),
                xml.Replace("sizeX=\"50\"", "sizeX=\"NaN\""),
                xml.Replace("actor=\"p\"", "actor=\"missing\""),
                xml.Replace("<ActorState", "<ActorState quantifier=\"invalid\""),
                xml.Replace("id=\"ready\"", "id=\"other\"")
            })
            {
                File.WriteAllText(input, broken);
                check(!ScenarioPreflight.Validate(input, "auto", [], out _).Valid, "preflight reuses compiler bounds, types, references and enum validation");
            }
            File.WriteAllText(input, xml);
            check(!ScenarioPreflight.Validate(input, "auto", ["missing"], out _).Valid, "test-name selection is validated before game launch");
            check(!ScenarioPreflight.Validate(input, "groupcombat", ["Validation_Rule"], out _).Valid, "regression cases cannot enter a groupcombat run");
            check(!ScenarioPreflight.Validate(Path.Combine(root, "missing.xml"), "auto", [], out _).Valid, "missing input returns an actionable diagnostic");
            check(!ScenarioPreflight.Validate("\0", "auto", [], out _).Valid, "invalid paths are converted into validation diagnostics");

            File.WriteAllText(input, xml.Replace("<ActorState actor=\"p\" state=\"alive\"/>", "<HasHediff actor=\"p\" def=\"Metalblood\" since=\"later\"/>").Replace("</Steps>", "<Checkpoint id=\"later\"/></Steps>"));
            var baselineReport = ScenarioPreflight.Validate(input, "auto", [], out _);
            check(!baselineReport.Valid && baselineReport.Diagnostics.Single().Suggestion.Contains("since"), "a baseline used before it is reached is rejected with advice");

            File.WriteAllText(input, xml);
            var before = Snapshot(root);
            var success = await CaptureAsync(["test", "run", "--mode", "auto", "--scenario-path", "source.autotest.xml", "--validate-only"]);
            using (var doc = JsonDocument.Parse(success.Output))
                check(success.Code == 0 && doc.RootElement.GetProperty("result").GetProperty("validation").GetProperty("valid").GetBoolean(), "validate-only works without About, Steam, game, test host or ModsConfig");
            check(before.SequenceEqual(Snapshot(root)), "validate-only does not alter source fixtures or allocate game artifacts");
            var conflict = await CaptureAsync(["test", "run", "--mode", "auto", "--scenario-path", input, "--validate-only", "--deploy"]);
            check(conflict.Code == ExitCodes.InvalidArguments, "validate-only rejects action flags instead of silently ignoring deployment");
            File.WriteAllText(input, xml.Replace("sizeX", "sizX"));
            before = Snapshot(root);
            var failure = await CaptureAsync(["test", "run", "--mode", "auto", "--scenario-path", input, "--deploy"]);
            using (var doc = JsonDocument.Parse(failure.Output))
                check(failure.Code == ExitCodes.InvalidArguments && doc.RootElement.GetProperty("error").GetProperty("details").GetProperty("validation").GetProperty("valid").GetBoolean() == false, "ordinary run reports XML errors before template, environment and deployment gates");
            check(before.SequenceEqual(Snapshot(root)), "invalid XML blocks an ordinary deploy run without file side effects");

            var repository = new DirectoryInfo(AppContext.BaseDirectory);
            while (repository != null && !Directory.Exists(Path.Combine(repository.FullName, "components", "ProjectTemplate"))) repository = repository.Parent;
            if (repository == null) throw new InvalidOperationException("Cannot locate template source fixtures");
            var assets = Path.Combine(repository.FullName, "components", "ProjectTemplate", "1.7", ".agents", "skills", "rimworld-xml-test", "assets");
            foreach (var file in Directory.GetFiles(assets, "*.xml"))
            {
                var doc = XDocument.Load(file);
                var arena = doc.Root!.Name == "ArenaSuite";
                var group = doc.Descendants("Case").Any(c => (string?)c.Attribute("kind") == "groupCombat" || (string?)c.Attribute("caseType") == "GroupCombat");
                var names = arena || group ? doc.Descendants("testDefName").Select(e => e.Value).ToArray() : Array.Empty<string>();
                var report = ScenarioPreflight.Validate(file, arena ? "arena" : group ? "groupcombat" : "auto", names, out _);
                check(report.Valid && (!group || report.RoundCount > 0) && (!arena || report.TournamentCount > 0), "current template preflight: " + Path.GetFileName(file));
            }
            foreach (var gameVersion in new[] { "1.6", "1.7" })
            {
                var currentAssets = Path.Combine(repository.FullName, "components", "ProjectTemplate", gameVersion, ".claude", "skills", "rimworld-xml-test", "assets");
                var arenaTemplate = Path.Combine(currentAssets, "arena-template-groups.autotest.xml");
                var groupTemplate = Path.Combine(currentAssets, "groupcombat-template-groups.autotest.xml");
                var arenaReportCurrent = ScenarioPreflight.Validate(arenaTemplate, "arena", ["Example_TemplateGroupsKnockout"], out var arenaPlan);
                var arenaTournament = arenaPlan?.Arena?.Tournaments.SingleOrDefault(t => t.Id == "knockout");
                var arenaSharedXml = arenaTournament == null ? null : XElement.Parse(arenaTournament.SharedXml);
                check(arenaReportCurrent.Valid && arenaTournament?.Entrants[0].Ai == "defendPoint"
                    && arenaSharedXml?.Descendants("Scene").SingleOrDefault(s => (string?)s.Attribute("id") == "studio")?.Attribute("battlefield")?.Value == "cover",
                    $"{gameVersion} Arena template validates requirements, fixed battlefield and per-entrant AI");

                var groupReportCurrent = ScenarioPreflight.Validate(groupTemplate, "groupcombat", ["Example_TemplateGroupsCombat"], out var groupPlan);
                var groupCase = groupPlan?.Cases.Single();
                var groupXml = groupCase == null ? null : XElement.Parse(groupCase.Xml);
                check(groupReportCurrent.Valid && groupReportCurrent.RoundCount > 0
                    && groupXml?.Descendants("SquadTemplate").Any(s => (string?)s.Attribute("source") == "groups") == true
                    && groupXml?.Element("Scene")?.Attribute("battlefield")?.Value == "mountainRidges"
                    && groupXml?.Element("Teams")?.Elements("Team").Single(t => (string?)t.Attribute("id") == "A").Attribute("ai")?.Value == "lure"
                    && groupXml?.Descendants("PawnGroup").Any(p => (string?)p.Attribute("formation") == "grid") == true,
                    $"{gameVersion} groupcombat template validates group squads, fixed battlefield, team AI and formation");
            }
            var arenaXml = File.ReadAllText(Path.Combine(assets, "arena.autotest.xml"));
            File.WriteAllText(input, arenaXml.Replace("<Output width=", "<Output widht="));
            var arenaReport = ScenarioPreflight.Validate(input, "arena", ["Example_AnimalArena"], out _);
            check(!arenaReport.Valid && arenaReport.Diagnostics.Single() is { File: not null, Line: > 1, Element: "ArenaSuite/Tournament/Output" } && arenaReport.Diagnostics.Single().Suggestion.Contains("width"), "arena errors retain original line numbers and specific allowed attributes");
            File.WriteAllText(input, arenaXml);
            File.WriteAllText(Path.Combine(root, "regression.autotest.xml"), xml);
            arenaReport = ScenarioPreflight.Validate(root, "arena", ["Example_AnimalArena"], out _);
            check(arenaReport.Valid && arenaReport.SkippedFiles == 1 && arenaReport.Warnings.Any(), "arena directory selection reports skipped inputs as advice");

            // Synthetic validator inputs; never copies or edits a real game's evidence.
            var resultPath = Path.Combine(root, "preview_Result.json");
            var resultInput = JsonSerializer.SerializeToNode(new {
                packageId = "preview.fixture", runId = "diagnostics", completeness = "Complete",
                manifestMissing = 0, manifestExecuted = 1, manifestPlanned = 1,
                results = new[] { new { caseId = "Rule/default/1", status = "failed", scenario = new {
                    TestDefName = "Rule", Diagnostics = new[] { new LogSyncMod.AutoTest.Scenarios.ScenarioFailureDiagnostic {
                        Code = "scenario.timeout", File = input, Line = 6, Element = "Case[0]/Steps[0]/Wait[0]/HasHediff[0]", StepId = "wrong", Xml = "<HasHediff actor='pawn' def='Metalblood'/>", Suggestion = "Check the assertion prerequisites"
                    } }
                } } }
            })!;
            foreach (var evidenceFailed in new[] { false, true })
            {
                if (evidenceFailed)
                {
                    resultInput["evidencePath"] = "preview.evidence.json";
                    File.WriteAllText(Path.Combine(root, "preview.evidence.json"), "{\"schemaVersion\":1,\"runId\":\"diagnostics\",\"packageId\":\"preview.fixture\",\"success\":false}");
                }
                File.WriteAllText(resultPath, resultInput.ToJsonString());
                var result = AutoResultValidator.Validate(root, "preview.fixture", "diagnostics", new Dictionary<string, AutoResultFingerprint>());
                var details = JsonSerializer.SerializeToElement(result.Details, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                var diagnostic = details.GetProperty("scenarioFailures")[0].GetProperty("diagnostics")[0];
                check(!result.Ok && result.Code == (evidenceFailed ? "AUTO_EVIDENCE_FAILED" : "AUTO_RESULT_FAILED") && diagnostic.GetProperty("file").GetString() == input && diagnostic.GetProperty("line").GetInt32() == 6 && diagnostic.GetProperty("stepId").GetString() == "wrong",
                    "CLI forwards assertion locations through " + result.Code);
            }
            var firstResult = resultInput["results"]![0]!.DeepClone();
            resultInput["results"] = new JsonArray(Enumerable.Range(0, 21).Select(i => {
                var item = firstResult.DeepClone(); item["caseId"] = "Rule/" + i + "/1"; return item;
            }).ToArray());
            File.WriteAllText(resultPath, resultInput.ToJsonString());
            foreach (var verbose in new[] { false, true })
            {
                var result = AutoResultValidator.Validate(root, "preview.fixture", "diagnostics", new Dictionary<string, AutoResultFingerprint>(), verbose);
                var details = JsonSerializer.SerializeToElement(result.Details);
                check(details.GetProperty("scenarioFailures").GetArrayLength() == (verbose ? 21 : 20) && details.GetProperty("scenarioFailuresTruncated").GetBoolean() == !verbose,
                    "runtime diagnostic previews report truncation and verbose retains every failed case");
            }
        }
        finally
        {
            Environment.CurrentDirectory = previousDirectory;
            Directory.Delete(root, true);
        }
    }

    private static string[] Snapshot(string root) => Directory.GetFiles(root, "*", SearchOption.AllDirectories).Order().Select(f => Path.GetRelativePath(root, f) + ":" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(f))))
        .Concat(Directory.GetDirectories(root, "*", SearchOption.AllDirectories).Order().Select(d => Path.GetRelativePath(root, d) + "/")).ToArray();

    private static async Task<(int Code, string Output)> CaptureAsync(string[] args)
    {
        var original = Console.Out;
        using var output = new StringWriter();
        try { Console.SetOut(output); return (await CliApplication.RunAsync(args), output.ToString()); }
        finally { Console.SetOut(original); }
    }
}
