using System.Diagnostics;
using System.Reflection;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using RimDevTools;

[assembly: SupportedOSPlatform("windows")]

var releaseToolVersion = typeof(CliApplication).Assembly.GetName().Version?.ToString() ?? "unknown";
var defaultRelease = Path.Combine(Environment.CurrentDirectory, "artifacts", $"RimDevTools-{releaseToolVersion}-Template-RimWorld-1.6");
var release = Path.GetFullPath(args.Length > 0 ? args[0] : defaultRelease);
var tools = Path.Combine(release, ".tools");
var data = Path.Combine(tools, "Data");

var passed = 0;
var package = ToolPackageManifest.Load(release);
package.VerifyFiles(release);
Assert(package.ToolVersion == releaseToolVersion, "tool manifest matches built CLI version");
Assert(package.SupportedGameVersions.SequenceEqual(["1.6", "1.7"]), "tool manifest supports both game versions");
var actualManaged = Directory.EnumerateFiles(release, "*", SearchOption.AllDirectories)
    .Select(path => Path.GetRelativePath(release, path).Replace('\\', '/')).Where(ToolPackageManifest.IsManagedPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
Assert(actualManaged.SetEquals(package.Files.Select(file => file.Path)), "tool manifest covers exactly all managed files");
foreach (var version in package.SupportedGameVersions)
foreach (var name in new[] { "LogSyncMod.dll", "AutoTestRunner.dll" })
    Assert(File.ReadAllBytes(Path.Combine(data, "LogSyncMod", version, "Assemblies", name)).SequenceEqual(File.ReadAllBytes(Path.Combine(Environment.CurrentDirectory, version, "Assemblies", name))), $"common payload {version}/{name} matches source artifact");
if (!File.Exists(Path.Combine(data, "project-template.manifest.json")))
{
    var expected = package.Files.Select(file => file.Path).Append(ToolPackageManifest.RelativePath).Append("mod_config.jsonc").ToHashSet(StringComparer.OrdinalIgnoreCase);
    var actual = Directory.EnumerateFiles(release, "*", SearchOption.AllDirectories).Select(path => Path.GetRelativePath(release, path).Replace('\\', '/')).ToHashSet(StringComparer.OrdinalIgnoreCase);
    Assert(actual.SetEquals(expected), "Tools package contains only managed payload, manifest and config reference");
    foreach (var forbidden in new[] { ".gitignore", "source", "About", "Defs", "Languages", "README.md", ".mcp.json", ".tools/ProjectData", ".tools/log" })
        Assert(!File.Exists(Path.Combine(release, forbidden)) && !Directory.Exists(Path.Combine(release, forbidden)), $"Tools excludes {forbidden}");
    var reference = ProjectConfig.Load(Path.Combine(release, "mod_config.jsonc"));
    Assert(reference.ExistingProjectMode && reference.ConfigVersion == 2 && string.IsNullOrEmpty(reference.GameVersion) && string.IsNullOrEmpty(reference.RimWorldDir)
        && reference.CsprojPaths.Count == 0 && reference.SyncItems.Count == 0, "Tools config is a manual reference without machine or template defaults");
    using var versionJson = JsonDocument.Parse(Run(Path.Combine(tools, "RimDevTools.exe"), release, "--version"));
    Assert(versionJson.RootElement.GetProperty("templateGameVersion").GetString() == "unpackaged", "Tools has no template identity");
    foreach (var client in new[] { ".agents", ".claude" })
        Assert(Directory.EnumerateDirectories(Path.Combine(release, client, "skills")).Select(Path.GetFileName).SequenceEqual(["rimdevtools"]), "Tools includes only core skill for " + client);
    var removed = RunResult(Path.Combine(tools, "RimDevTools.exe"), release, "init", "--existing");
    Assert(removed.ExitCode == 2 && removed.Stdout.Contains("UNKNOWN_COMMAND"), "Tools has no first-time integration command");
    Console.WriteLine($"PASS {passed} Tools release tests");
    return;
}

var templateMetadataPath = Path.Combine(data, "project-template.manifest.json");
using var templateMetadataDocument = JsonDocument.Parse(File.ReadAllText(templateMetadataPath));
var templateMetadata = templateMetadataDocument.RootElement;
var templateGameVersion = templateMetadata.GetProperty("templateGameVersion").GetString() ?? throw new InvalidDataException("templateGameVersion is missing");
var templateSchemaVersion = templateMetadata.GetProperty("templateSchemaVersion").GetInt32();
var templateRoot = Path.Combine(Environment.CurrentDirectory, "components", "ProjectTemplate");
var template = Path.GetFullPath(Path.Combine(templateRoot, templateGameVersion));
var templateConfigName = templateGameVersion == "1.6" ? "mod_config.jsonc" : $"mod_config.{templateGameVersion}.jsonc";
var templateConfig = Path.GetFullPath(Path.Combine(templateRoot, templateConfigName));
RequireFile("mod_config.jsonc");
RequireFile("LICENSE");
Assert(!File.Exists(Path.Combine(release, ".codex", "config.toml")), "release does not contain the obsolete Codex config");
RequireFile(".mcp.json");
RequireFile(Path.Combine(".pi", "mcp.json"));
RequireFile("opencode.jsonc");
RequireFile(Path.Combine(".agents", "skills", "rimsage", "SKILL.md"));
RequireFile(Path.Combine(".agents", "skills", "rimsage", "agents", "openai.yaml"));
RequireFile(Path.Combine(".agents", "skills", "rimsage", "references", "tool-reference.md"));
RequireFile(Path.Combine(".claude", "skills", "rimsage", "SKILL.md"));
RequireFile(Path.Combine(".claude", "skills", "rimsage", "references", "tool-reference.md"));
RequireFile(Path.Combine(".tools", "RimDevTools.exe"));
RequireFile(Path.Combine(".tools", "Data", "tool-mods.manifest.json"));
RequireFile(Path.Combine(".tools", "Data", "project-template.manifest.json"));
RequireFile(Path.Combine(".tools", "Data", "LogSyncMod", "About", "About.xml"));
RequireFile(Path.Combine(".tools", "Data", "LogSyncMod", "1.6", "Assemblies", "LogSyncMod.dll"));
RequireFile(Path.Combine(".tools", "Data", "LogSyncMod", "1.6", "Assemblies", "AutoTestRunner.dll"));
RequireFile(Path.Combine(".tools", "Data", "LogSyncMod", "1.7", "Assemblies", "LogSyncMod.dll"));
RequireFile(Path.Combine(".tools", "Data", "LogSyncMod", "1.7", "Assemblies", "AutoTestRunner.dll"));
RequireFile(Path.Combine(".tools", "Data", "LogSyncMod", "1.7", "Defs", "Internal", "DebugToolDefs.xml"));
RequireFile(Path.Combine(".tools", "Data", "LogSyncMod", "1.7", "Defs", "Internal", "TestDefs.xml"));
RequireFile(Path.Combine(".tools", "Ilspy", "ilspycmd.exe"));
RequireFile(Path.Combine(".tools", "Data", "licenses", "ILSpy-LICENSE.txt"));
RequireFile(Path.Combine(".tools", "Data", "licenses", "third-party-notices.txt"));
RequireFile(Path.Combine(".tools", "modlist", "defaultMods.xml"));
Assert(File.Exists(Path.Combine(tools, "log", "精选日志.log")) && new FileInfo(Path.Combine(tools, "log", "精选日志.log")).Length == 0, "release starts with an empty selected log");
RequireDirectory(Path.Combine(".tools", "log", "AutoTestOutput"));
Assert(!Directory.Exists(Path.Combine(tools, "components", "ProjectTemplate")), "release does not contain a nested project template");
Assert(!File.Exists(Path.Combine(release, "components", "ProjectTemplate", "manifest.json")), "release does not copy the build manifest into the project");
Assert(!Directory.EnumerateFileSystemEntries(Path.Combine(tools, "log", "AutoTestOutput")).Any(), "release does not copy automatic test history");
Assert(!Directory.EnumerateFileSystemEntries(Path.Combine(release, "参考文档")).Any(), "release does not copy local reference material");
Assert(File.ReadAllText(Path.Combine(release, "LICENSE")).Contains("Copyright (c) 2026 白金trigger", StringComparison.Ordinal), "release contains the MIT license for 白金trigger");
Assert(!Directory.Exists(Path.Combine(release, "UnityProjects")), "release does not contain a Unity shader project");
Assert(!Directory.Exists(Path.Combine(release, "source", "MyCustomMod", "Shader")), "release does not contain shader example source");
Assert(!Directory.Exists(Path.Combine(release, "AssetBundles")), "release does not contain shader asset bundles");
Assert(!File.Exists(Path.Combine(release, templateGameVersion, "Assemblies", "MyCustomMod.dll")), "release does not contain a stale template DLL");
Assert(File.ReadAllBytes(Path.Combine(release, "mod_config.jsonc")).SequenceEqual(File.ReadAllBytes(templateConfig)), "release uses the matching template mod_config.jsonc");
Assert(!File.Exists(Path.Combine(template, "mod_config.jsonc")), $"{templateGameVersion} template does not contain a duplicate mod_config.jsonc");

var excludedTemplateDirectories = new HashSet<string>([".git", ".vs", ".tools", ".rimdev", "artifacts", "bin", "obj"], StringComparer.OrdinalIgnoreCase);
var templateFiles = RelativeEntries(template, files: true)
    .Where(path => !path.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries).Any(excludedTemplateDirectories.Contains))
    .Where(path => !path.Equals(Path.Combine(".claude", "settings.local.json"), StringComparison.OrdinalIgnoreCase))
    .ToArray();
var releaseFiles = RelativeEntries(release, files: true)
    .Where(path => !path.Equals(".tools", StringComparison.OrdinalIgnoreCase) && !path.StartsWith($".tools{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
var expectedRootFiles = templateFiles.Append("mod_config.jsonc").OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
Assert(expectedRootFiles.SequenceEqual(releaseFiles, StringComparer.OrdinalIgnoreCase), $"release root equals the {templateGameVersion} template plus one config file");

const string endpoint = "https://mcp.rimsage.com/mcp";
using (var claudeConfig = JsonDocument.Parse(File.ReadAllText(Path.Combine(release, ".mcp.json"))))
{
    var server = claudeConfig.RootElement.GetProperty("mcpServers").GetProperty("rimsage");
    Assert(server.GetProperty("type").GetString() == "http" && server.GetProperty("url").GetString() == endpoint, "Claude config declares the RimSage HTTP service");
}
using (var opencodeConfig = JsonDocument.Parse(File.ReadAllText(Path.Combine(release, "opencode.jsonc")), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }))
{
    var server = opencodeConfig.RootElement.GetProperty("mcp").GetProperty("rimsage");
    Assert(server.GetProperty("type").GetString() == "remote" && server.GetProperty("url").GetString() == endpoint && server.GetProperty("enabled").GetBoolean(), "opencode config declares the enabled RimSage remote service");
}
using (var piConfig = JsonDocument.Parse(File.ReadAllText(Path.Combine(release, ".pi", "mcp.json"))))
{
    var server = piConfig.RootElement.GetProperty("mcpServers").GetProperty("rimsage");
    Assert(server.GetProperty("directTools").GetBoolean() && server.GetProperty("lifecycle").GetString() == "eager", "pi config declares the eager direct-tools RimSage service");
}
Assert(File.ReadAllBytes(Path.Combine(release, ".agents", "skills", "rimsage", "SKILL.md")).SequenceEqual(File.ReadAllBytes(Path.Combine(release, ".claude", "skills", "rimsage", "SKILL.md"))), "RimSage skill instructions stay synchronized across clients");
Assert(File.ReadAllBytes(Path.Combine(release, ".agents", "skills", "rimsage", "references", "tool-reference.md")).SequenceEqual(File.ReadAllBytes(Path.Combine(release, ".claude", "skills", "rimsage", "references", "tool-reference.md"))), "RimSage tool references stay synchronized across clients");

using (var manifestDocument = JsonDocument.Parse(File.ReadAllText(Path.Combine(data, "tool-mods.manifest.json"))))
{
    var manifest = manifestDocument.RootElement;
    Assert(manifest.GetProperty("schemaVersion").GetInt32() == 1 && manifest.GetProperty("components").GetArrayLength() == 1, "tool Mod manifest declares one component");
    foreach (var component in manifest.GetProperty("components").EnumerateArray())
    {
        var root = Path.Combine(data, component.GetProperty("folder").GetString()!);
        Assert(DirectoryHasher.Compute(root) == component.GetProperty("contentHash").GetString(), $"tool Mod hash matches manifest for {component.GetProperty("folder").GetString()}");
    }
}

var logSyncAbout = XDocument.Load(Path.Combine(data, "LogSyncMod", "About", "About.xml"));
Assert(logSyncAbout.Root?.Element("packageId")?.Value.Equals("trigger.logsyncmod", StringComparison.OrdinalIgnoreCase) == true, "packaged LogSyncMod has the expected packageId");
Assert(logSyncAbout.Root?.Element("modVersion")?.Value == "1.2.7", "packaged LogSyncMod declares version 1.2.7");
var supportedVersions = logSyncAbout.Root?.Element("supportedVersions")?.Elements("li").Select(element => element.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
Assert(supportedVersions?.SetEquals(["1.6", "1.7"]) == true, "packaged LogSyncMod declares both RimWorld versions");
var logSyncAssembly = AssemblyName.GetAssemblyName(Path.Combine(data, "LogSyncMod", "1.6", "Assemblies", "LogSyncMod.dll")).Version;
var autoTestAssembly = AssemblyName.GetAssemblyName(Path.Combine(data, "LogSyncMod", "1.6", "Assemblies", "AutoTestRunner.dll")).Version;
Assert(logSyncAssembly is not null && autoTestAssembly is not null && logSyncAssembly.Major == autoTestAssembly.Major && logSyncAssembly.Minor == autoTestAssembly.Minor && logSyncAssembly.Build == autoTestAssembly.Build, "packaged LogSync assemblies share a product version");
var logSyncAssembly17 = AssemblyName.GetAssemblyName(Path.Combine(data, "LogSyncMod", "1.7", "Assemblies", "LogSyncMod.dll")).Version;
Assert(logSyncAssembly17 is not null && logSyncAssembly17.Major == 1 && logSyncAssembly17.Minor == 2 && logSyncAssembly17.Build == 7, "packaged LogSyncMod includes the current RimWorld 1.7 build");
var logSyncAssembly17Path = Path.Combine(data, "LogSyncMod", "1.7", "Assemblies", "LogSyncMod.dll");
Assert(File.ReadAllBytes(logSyncAssembly17Path).SequenceEqual(File.ReadAllBytes(Path.Combine(Environment.CurrentDirectory, "1.7", "Assemblies", "LogSyncMod.dll"))), "packaged RimWorld 1.7 assembly matches the current source build");
foreach (var version in new[] { "1.6", "1.7" })
foreach (var name in new[] { "LogSyncMod.dll", "AutoTestRunner.dll" })
    Assert(File.ReadAllBytes(Path.Combine(data, "LogSyncMod", version, "Assemblies", name)).SequenceEqual(File.ReadAllBytes(Path.Combine(Environment.CurrentDirectory, version, "Assemblies", name))), $"packaged {version} {name} equals the selected source artifact");
Assert(!Directory.Exists(Path.Combine(tools, "ProjectData")) && File.ReadAllText(Path.Combine(release, ".gitignore")).Contains("/.tools/ProjectData/", StringComparison.Ordinal), "release excludes and ignores project runtime data");
var officialLibraries = new HashSet<string>(["Assembly-CSharp.dll", "UnityStubs.dll", "Waylith.dll", "Waylith.AssetBake.dll", "steam_api64.dll"], StringComparer.OrdinalIgnoreCase);
Assert(!Directory.GetFiles(release, "*", SearchOption.AllDirectories).Any(path => officialLibraries.Contains(Path.GetFileName(path))), "release contains no official library copies");
Assert(!Directory.GetFiles(release, "*", SearchOption.AllDirectories).Any(path => Path.GetFileName(path) is "deployment-receipt.json" or "steamworkshops-search-cache.json" or "steamworkshops-bindings.json"), "release contains no receipts, search caches or bindings");

var templateAbout = XDocument.Load(Path.Combine(release, "About", "About.xml"));
Assert(templateAbout.Root?.Element("name")?.Value == ProjectTemplatePreflight.TemplateDisplayName && templateAbout.Root?.Element("packageId")?.Value == ProjectTemplatePreflight.TemplatePackageId, "release uses the branded template name and packageId");
Assert(templateAbout.Root?.Element("incompatibleWith")?.Elements("li").Any(item => item.Value.Equals("ludeon.rimworld", StringComparison.OrdinalIgnoreCase)) == true, "release template is incompatible with Core until it is configured");
Assert(templateAbout.Root?.Element("supportedVersions")?.Elements("li").Select(item => item.Value).SequenceEqual([templateGameVersion]) == true, "release About metadata matches the packaged template game version");
var templateDescription = templateAbout.Root?.Element("description")?.Value ?? "";
Assert(templateDescription.Contains("[h2][b]快速开始[/b][/h2]", StringComparison.Ordinal) && !templateDescription.Contains("------", StringComparison.Ordinal) && System.Text.Encoding.UTF8.GetByteCount(templateDescription) <= 8000, "release template provides a Chinese quick-start guide within Steam description limits");
var templateProject = XDocument.Load(Path.Combine(release, "source", "MyCustomMod", "MyCustomMod.csproj"));
var releaseOutputPath = templateProject.Descendants().FirstOrDefault(element => element.Name.LocalName == "OutputPath")?.Value.Trim();
Assert(releaseOutputPath == $@"..\..\{templateGameVersion}\Assemblies\", "template builds write the DLL to the matching deployed Assemblies directory");

using (var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(release, "mod_config.jsonc")), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }))
{
    var root = config.RootElement;
    Assert(!root.GetProperty("existing_project_mode").GetBoolean(), "new templates explicitly use template mode");
    Assert(root.GetProperty("mod_name").GetString() == ProjectTemplatePreflight.TemplateModName, "packaged configuration uses the template deployment name");
    Assert(root.GetProperty("game_version").GetString() == templateGameVersion, "packaged configuration matches the template game version");
    Assert(root.GetProperty("ilspycmd_path").GetString() == "Ilspy\\ilspycmd.exe", "packaged configuration points at the packaged ILSpy directory");
    Assert(!root.GetProperty("isolated_desktop").GetBoolean(), "packaged configuration uses the normal desktop by default");
    Assert(!root.GetProperty("sync_source").GetBoolean(), "packaged configuration disables source synchronization by default");
    Assert(root.GetProperty("project_paths").GetProperty("reference_docs").GetString() == "参考文档"
        && root.GetProperty("project_paths").GetProperty("planning_docs").GetString() == "策划案", "packaged configuration exposes configurable project semantic paths");
    Assert(!root.TryGetProperty("shader_bundle", out _) && !root.GetProperty("sync_items").EnumerateArray().Any(item => item.GetString() == "AssetBundles"), "packaged configuration does not include shader build settings or asset bundle synchronization");
}

var versionOutput = Run(Path.Combine(tools, "RimDevTools.exe"), release, "--version");
using (var document = JsonDocument.Parse(versionOutput))
{
    var root = document.RootElement;
    Assert(root.GetProperty("ok").GetBoolean()
        && !string.IsNullOrWhiteSpace(root.GetProperty("rimDevToolsVersion").GetString())
        && root.GetProperty("templateGameVersion").GetString() == templateGameVersion
        && root.GetProperty("templateSchemaVersion").GetInt32() == templateSchemaVersion
        && root.EnumerateObject().Select(property => property.Name).SequenceEqual(["ok", "command", "rimDevToolsVersion", "templateGameVersion", "templateSchemaVersion"]), "packaged version output reports RimDevTools and its template metadata");
}

var help = Run(Path.Combine(tools, "RimDevTools.exe"), release, "mods", "install-tools", "--help");
foreach (var subcommand in new[] { "upload", "edit", "search", "subscribe", "status" })
{
    var workshopHelp = Run(Path.Combine(tools, "RimDevTools.exe"), release, "steamworkshops", subcommand, "--help");
    Assert(workshopHelp.Contains("Usage:", StringComparison.Ordinal), $"packaged Workshop {subcommand} help is present");
    if (subcommand == "search") Assert(workshopHelp.Contains("search next", StringComparison.Ordinal) && workshopHelp.Contains("pages 1-3", StringComparison.Ordinal) && workshopHelp.Contains("pages 1-5", StringComparison.Ordinal), "packaged custom pagination and next help");
}
Assert(Run(Path.Combine(tools, "RimDevTools.exe"), release, "deploy", "--help").Contains("--upload", StringComparison.Ordinal), "packaged combined deployment/upload help");
Assert(help.Contains("--mods-path", StringComparison.Ordinal) && help.Contains("--dry-run", StringComparison.Ordinal), "packaged tool Mod command exposes path and dry-run options");
var cleanupHelp = Run(Path.Combine(tools, "RimDevTools.exe"), release, "test", "cleanup", "--help");
Assert(cleanupHelp.Contains("--dry-run", StringComparison.Ordinal) && cleanupHelp.Contains("--status", StringComparison.Ordinal) && cleanupHelp.Contains("activeMods", StringComparison.Ordinal), "packaged CLI exposes safe transaction cleanup and status help");
var modCopyHelp = Run(Path.Combine(tools, "RimDevTools.exe"), release, "mod", "copy", "--help");
Assert(modCopyHelp.Contains("--include-ignored", StringComparison.Ordinal)
    && modCopyHelp.Contains("--overwrite-existing", StringComparison.Ordinal)
    && modCopyHelp.Contains("--dry-run", StringComparison.Ordinal)
    && modCopyHelp.Contains("project_paths.reference_docs", StringComparison.Ordinal), "packaged CLI exposes reference copy options and the configured destination");
var testRunHelp = Run(Path.Combine(tools, "RimDevTools.exe"), release, "test", "run", "--help");
Assert(testRunHelp.Contains("--batch-size", StringComparison.Ordinal)
    && testRunHelp.Contains("defaults to 40", StringComparison.Ordinal)
    && testRunHelp.Contains("including Standard", StringComparison.Ordinal)
    && testRunHelp.Contains("uniformly at random without replacement", StringComparison.Ordinal)
    && testRunHelp.Contains("Explicit --defname runs ignore", StringComparison.Ordinal), "packaged CLI exposes the automatic-test batch contract");
var removedInit = RunResult(Path.Combine(tools, "RimDevTools.exe"), release, "init", "1.6", "--output", Path.Combine(Path.GetTempPath(), "removed-init"));
Assert(removedInit.ExitCode == 2 && removedInit.Stdout.Contains("UNKNOWN_COMMAND", StringComparison.Ordinal), "packaged CLI no longer exposes init");
var removedRef = RunResult(Path.Combine(tools, "RimDevTools.exe"), release, "ref", "search", "anything");
Assert(removedRef.ExitCode == 2 && removedRef.Stdout.Contains("UNKNOWN_COMMAND", StringComparison.Ordinal), "packaged CLI no longer exposes the old reference command namespace");

var temporaryMods = Path.Combine(Path.GetTempPath(), "RimDevToolsReleaseTests", Guid.NewGuid().ToString("N"), "Mods");
Directory.CreateDirectory(temporaryMods);
try
{
    var gameRoot = Path.GetDirectoryName(temporaryMods)!;
    Directory.CreateDirectory(Path.Combine(gameRoot, "Data"));
    var managed = Path.Combine(gameRoot, "RimWorldWin64_Data", "Managed");
    Directory.CreateDirectory(managed);
    File.WriteAllText(Path.Combine(gameRoot, "RimWorldWin64.exe"), "test game");
    foreach (var assembly in RimWorldGameLayout.LegacyRequiredAssemblies)
        File.WriteAllText(Path.Combine(managed, assembly), "test assembly");
    File.WriteAllText(Path.Combine(gameRoot, "mod_config.jsonc"), "{\"config_version\":2,\"game_version\":\"1.6\"}");
    // Run the mutating-capable command from a fixture installation, keeping release inputs pristine.
    var testTools = Path.Combine(gameRoot, ".tools");
    Directory.CreateDirectory(testTools);
    File.Copy(Path.Combine(tools, "RimDevTools.exe"), Path.Combine(testTools, "RimDevTools.exe"));
    foreach (var input in Directory.EnumerateFiles(data, "*", SearchOption.AllDirectories))
    {
        var output = Path.Combine(testTools, "Data", Path.GetRelativePath(data, input));
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.Copy(input, output);
    }
    Directory.CreateDirectory(Path.Combine(testTools, "ProjectData"));
    File.WriteAllText(Path.Combine(testTools, "ProjectData", "tool-update-check.json"), JsonSerializer.Serialize(new { nextCheckUtc = DateTimeOffset.UtcNow.AddHours(24), latestVersion = releaseToolVersion }));
    var dryRun = Run(Path.Combine(testTools, "RimDevTools.exe"), gameRoot, "mods", "install-tools", "--mods-path", temporaryMods, "--dry-run");
    using var dryJson = JsonDocument.Parse(dryRun);
    Assert(dryJson.RootElement.GetProperty("ok").GetBoolean() && dryJson.RootElement.GetProperty("result").GetProperty("components").GetArrayLength() == 1, "packaged tool Mod dry-run plans one component in an explicit Mods directory");
}
finally
{
    var root = Path.GetDirectoryName(temporaryMods);
    if (root is not null && Directory.Exists(root)) Directory.Delete(root, true);
}

Console.WriteLine($"PASS {passed} release tests");

void RequireFile(string relative)
{
    var path = Path.Combine(release, relative);
    Assert(File.Exists(path) && new FileInfo(path).Length > 0, $"release contains {relative}");
}

void RequireDirectory(string relative)
    => Assert(Directory.Exists(Path.Combine(release, relative)), $"release contains {relative}");

string Run(string executable, string workingDirectory, params string[] arguments)
{
    var result = RunResult(executable, workingDirectory, arguments);
    if (result.ExitCode != 0) throw new InvalidOperationException($"{Path.GetFileName(executable)} exited {result.ExitCode}: {result.Stderr}\n{result.Stdout}");
    return result.Stdout;
}

(int ExitCode, string Stdout, string Stderr) RunResult(string executable, string workingDirectory, params string[] arguments)
{
    var startInfo = new ProcessStartInfo(executable) { WorkingDirectory = workingDirectory, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
    foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
    using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Unable to start {executable}");
    var stdout = process.StandardOutput.ReadToEnd();
    var stderr = process.StandardError.ReadToEnd();
    process.WaitForExit();
    return (process.ExitCode, stdout, stderr);
}

string[] RelativeEntries(string root, bool files)
    => (files ? Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories) : Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
        .Select(path => Path.GetRelativePath(root, path))
        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
        .ToArray();

void Assert(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException($"FAIL: {name}");
    passed++;
    Console.WriteLine($"PASS: {name}");
}
