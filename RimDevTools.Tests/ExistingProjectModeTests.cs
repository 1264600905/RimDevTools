using System.Text.Json;
using System.Text.Json.Nodes;
using RimDevTools;

internal sealed partial class TestSuite
{
    private async Task TestExistingProjectModeAsync()
    {
        var environment = CreateProjectEnvironment("existing-project", createGameExecutable: true);
        using var scope = UseEnvironment(environment);
        var projectPath = Path.Combine(environment.Root, "source", "TestProject", "TestProject.csproj");
        File.WriteAllText(projectPath, File.ReadAllText(projectPath).Replace("<AssemblyName>TestProject</AssemblyName>", "<AssemblyName>MyCustomMod</AssemblyName>")
            .Replace("<OutputPath>bin\\</OutputPath>", "<OutputPath>..\\..\\Assemblies\\</OutputPath>")
            .Replace("</Project>", "<ItemGroup><Compile Include=\"Legacy.cs\" /></ItemGroup></Project>"));
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(projectPath)!, "Legacy.cs"), "public class LegacyEntry { }");
        Directory.CreateDirectory(Path.Combine(environment.Root, "Assemblies"));
        var initial = Config(environment, modName: "MyCustomModLegacy", extra: "\"sync_items\":[\"About\",\"Assemblies\"]");
        File.WriteAllText(environment.ConfigPath, initial);
        Assert(!ProjectConfig.Load(environment.ConfigPath).ExistingProjectMode, "missing mode defaults to template behavior without rewriting config");
        var blocked = await DeployCommand.ExecuteAsync(["deploy", "--dry-run"]);
        Assert(!blocked.Ok && blocked.Error?.Code == "PROJECT_TEMPLATE_NOT_CONFIGURED", "legacy template-like name is blocked before opting in");

        var optedIn = initial[..^1] + ",\"existing_project_mode\":true}";
        File.WriteAllText(environment.ConfigPath, optedIn);
        Assert(ProjectConfig.Load(environment.ConfigPath).ExistingProjectMode, "boolean true enables existing-project mode");
        var deployed = await DeployCommand.ExecuteAsync(["deploy"]);
        var target = Path.Combine(environment.Game, "Mods", "MyCustomModLegacy");
        Assert(deployed.Ok && File.ReadAllBytes(Path.Combine(target, "About", "About.xml")).SequenceEqual(File.ReadAllBytes(Path.Combine(environment.Root, "About", "About.xml")))
            && File.Exists(Path.Combine(target, "Assemblies", "MyCustomMod.dll")), "changing only the mode field builds and deploys the legacy DLL into game-loadable Assemblies");
        Assert(File.ReadAllText(environment.ConfigPath) == optedIn, "deployment preserves existing config text");
        var support = Path.Combine(environment.Root, "src", "Support");
        Directory.CreateDirectory(support);
        File.WriteAllText(Path.Combine(support, "Support.csproj"), File.ReadAllText(projectPath).Replace("MyCustomMod", "LegacySupport"));
        File.WriteAllText(Path.Combine(support, "Legacy.cs"), "public class LegacySupport { }");
        var multiple = JsonNode.Parse(optedIn)!.AsObject();
        multiple["csproj_paths"] = JsonSerializer.SerializeToNode(new[] { "source/TestProject/TestProject.csproj", "src/Support/Support.csproj" });
        File.WriteAllText(environment.ConfigPath, multiple.ToJsonString());
        var multipleDeploy = await DeployCommand.ExecuteAsync(["deploy"]);
        Assert(multipleDeploy.Ok && File.Exists(Path.Combine(target, "Assemblies", "MyCustomMod.dll")) && File.Exists(Path.Combine(target, "Assemblies", "LegacySupport.dll")), "existing mode builds and deploys multiple explicitly configured source directories");
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(projectPath)!, "Legacy.cs"), "this is not valid C#");
        var failedBuild = await DeployCommand.ExecuteAsync(["deploy"]);
        Assert(!failedBuild.Ok && failedBuild.Error?.Code == "BUILD_FAILED" && File.Exists(Path.Combine(target, "Assemblies", "MyCustomMod.dll")), "existing mode retains build failure and preserves deployed DLL");

        var doctor = await CaptureAsync(() => DoctorCommand.RunAsync(["doctor", "--verbose"]));
        using (var json = JsonDocument.Parse(doctor.Output))
        {
            var checks = json.RootElement.GetProperty("result").GetProperty("checks").EnumerateArray().ToArray();
            Assert(checks.Single(c => c.GetProperty("id").GetString() == "project.mode").GetProperty("details").GetProperty("existingProjectMode").GetBoolean()
                && checks.Single(c => c.GetProperty("id").GetString() == "project.template_configuration").GetProperty("status").GetString() == "available", "doctor reports existing mode consistently with deploy");
        }
        var context = ProjectDiscovery.Find(environment.Root, out _)!;
        context.Config.ModName = ProjectTemplatePreflight.TemplateModName;
        context.Config.LogKeywords = [];
        context.Config.CsprojPaths = ["source/MyCustomMod/Legacy.csproj"];
        File.WriteAllText(Path.Combine(environment.Root, "About", "About.xml"), $"<ModMetaData><name>{ProjectTemplatePreflight.TemplateDisplayName}</name><packageId>{ProjectTemplatePreflight.TemplatePackageId}</packageId></ModMetaData>");
        Assert(ProjectTemplatePreflight.Inspect(context).Count == 0, "existing mode accepts template-like metadata, paths and empty deploy log keywords");
        context.Config.ExistingProjectMode = false;
        Assert(ProjectTemplatePreflight.Inspect(context).Count >= 4, "explicit false restores template gates");

        var xmlConfig = Config(environment, modName: "LegacyXml", extra: "\"existing_project_mode\":true,\"csproj_paths\":[],\"sync_items\":[\"About\"],\"log_keywords\":[]");
        File.WriteAllText(environment.ConfigPath, xmlConfig);
        var xmlDeploy = await DeployCommand.ExecuteAsync(["deploy"]);
        Assert(xmlDeploy.Ok && File.Exists(Path.Combine(environment.Game, "Mods", "LegacyXml", "About", "About.xml")), "pure XML existing project deploys without keywords or a C# project");
        var settings = await CaptureAsync(() => TestCommand.SettingsAsync(["test", "settings"]));
        Assert(JsonErrorCode(settings.Output) is not ("PROJECT_TEMPLATE_NOT_CONFIGURED" or "PROJECT_INVALID"), "test settings shares the relaxed template gate");

        File.WriteAllText(environment.ConfigPath, xmlConfig.Replace("\"sync_items\":[\"About\"]", "\"sync_items\":[\"../outside\"]", StringComparison.Ordinal));
        var unsafeDeploy = await DeployCommand.ExecuteAsync(["deploy", "--dry-run"]);
        Assert(!unsafeDeploy.Ok && unsafeDeploy.Error?.Code == "SYNC_PATH_INVALID", "existing mode retains path traversal protection");
        File.WriteAllText(environment.ConfigPath, xmlConfig.Replace("\"csproj_paths\":[]", "\"csproj_paths\":[\"missing.csproj\"]", StringComparison.Ordinal));
        var missing = await DeployCommand.ExecuteAsync(["deploy"]);
        Assert(!missing.Ok && missing.Error?.Code == "CSPROJ_NOT_FOUND", "existing mode retains missing-project failure");
        File.WriteAllText(environment.ConfigPath, xmlConfig);
        foreach (var about in new[] { "<broken", "<Other />", "<ModMetaData><name>Legacy</name></ModMetaData>" })
        {
            File.WriteAllText(Path.Combine(environment.Root, "About", "About.xml"), about);
            var invalid = await DeployCommand.ExecuteAsync(["deploy"]);
            Assert(!invalid.Ok && invalid.Error?.Code == "PROJECT_INVALID", "existing mode still rejects unreadable or incomplete About metadata");
        }
        File.Delete(Path.Combine(environment.Root, "About", "About.xml"));
        var missingAbout = await DeployCommand.ExecuteAsync(["deploy"]);
        Assert(!missingAbout.Ok && missingAbout.Error?.Code == "PROJECT_INVALID", "existing mode requires About metadata");
        foreach (var invalidMode in new[] { "\"true\"", "1", "null" })
        {
            File.WriteAllText(environment.ConfigPath, xmlConfig.Replace("\"existing_project_mode\":true", "\"existing_project_mode\":" + invalidMode, StringComparison.Ordinal));
            AssertThrows<JsonException>(() => ProjectConfig.Load(environment.ConfigPath), "mode rejects non-boolean JSON values");
        }
        Assert(!ToolPackageManifest.IsManagedPath("../escape") && !ToolPackageManifest.IsManagedPath(".tools/Data/project-template.manifest.json")
            && !ToolPackageManifest.IsManagedPath(".tools/ProjectData/state.json") && !ToolPackageManifest.IsManagedPath(".agents/skills/other/SKILL.md")
            && !ToolPackageManifest.IsManagedPath(".tools/Ilspy/../outside"), "managed tool paths exclude templates, user state, other skills and traversal");
    }
}
