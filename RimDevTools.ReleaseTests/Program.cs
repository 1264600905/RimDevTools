using System.Diagnostics;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Xml.Linq;
using RimDevTools;

[assembly: SupportedOSPlatform("windows")]

var release = Path.GetFullPath(args.Length > 0 ? args[0] : Path.Combine(Environment.CurrentDirectory, "artifacts", "RimDevTools-Package"));
var tools = Path.Combine(release, ".tools");
var data = Path.Combine(tools, "Data");
var template = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "components", "ProjectTemplate", "1.6"));
var templateConfig = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "components", "ProjectTemplate", "mod_config.jsonc"));
var passed = 0;

RequireFile("mod_config.jsonc");
RequireFile("LICENSE");
RequireFile(Path.Combine(".codex", "config.toml"));
RequireFile(".mcp.json");
RequireFile("opencode.jsonc");
RequireFile(Path.Combine(".agents", "skills", "rimsage", "SKILL.md"));
RequireFile(Path.Combine(".agents", "skills", "rimsage", "agents", "openai.yaml"));
RequireFile(Path.Combine(".agents", "skills", "rimsage", "references", "tool-reference.md"));
RequireFile(Path.Combine(".claude", "skills", "rimsage", "SKILL.md"));
RequireFile(Path.Combine(".claude", "skills", "rimsage", "references", "tool-reference.md"));
RequireFile(Path.Combine(".tools", "RimDevTools.exe"));
RequireFile(Path.Combine(".tools", "Data", "tool-mods.manifest.json"));
RequireFile(Path.Combine(".tools", "Data", "LogSyncMod", "About", "About.xml"));
RequireFile(Path.Combine(".tools", "Data", "LogSyncMod", "1.6", "Assemblies", "LogSyncMod.dll"));
RequireFile(Path.Combine(".tools", "Data", "LogSyncMod", "1.6", "Assemblies", "LogSyncMod.AutoTest.dll"));
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
Assert(!File.Exists(Path.Combine(release, "1.6", "Assemblies", "MyCustomMod.dll")), "release does not contain a stale template DLL");
Assert(File.ReadAllBytes(Path.Combine(release, "mod_config.jsonc")).SequenceEqual(File.ReadAllBytes(templateConfig)), "release uses the single shared template mod_config.jsonc");
Assert(!File.Exists(Path.Combine(template, "mod_config.jsonc")), "1.6 template does not contain a duplicate mod_config.jsonc");

var templateFiles = RelativeEntries(template, files: true)
    .Where(path => !path.Equals(Path.Combine(".claude", "settings.local.json"), StringComparison.OrdinalIgnoreCase))
    .ToArray();
var releaseFiles = RelativeEntries(release, files: true)
    .Where(path => !path.Equals(".tools", StringComparison.OrdinalIgnoreCase) && !path.StartsWith($".tools{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
var expectedRootFiles = templateFiles.Append("mod_config.jsonc").OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
Assert(expectedRootFiles.SequenceEqual(releaseFiles, StringComparer.OrdinalIgnoreCase), "release root equals the 1.6 template plus one config file");

const string endpoint = "https://mcp.rimsage.com/mcp";
var codexConfig = File.ReadAllText(Path.Combine(release, ".codex", "config.toml")).Replace("\r\n", "\n").Trim();
Assert(codexConfig == $"[mcp_servers.rimsage]\nenabled = true\nurl = \"{endpoint}\"\ndefault_tools_approval_mode = \"approve\"", "Codex config declares the approved RimSage service");
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
Assert(logSyncAbout.Root?.Element("modVersion")?.Value == "1.1.0", "packaged LogSyncMod declares version 1.1.0");
var logSyncAssembly = AssemblyName.GetAssemblyName(Path.Combine(data, "LogSyncMod", "1.6", "Assemblies", "LogSyncMod.dll")).Version;
var autoTestAssembly = AssemblyName.GetAssemblyName(Path.Combine(data, "LogSyncMod", "1.6", "Assemblies", "LogSyncMod.AutoTest.dll")).Version;
Assert(logSyncAssembly is not null && autoTestAssembly is not null && logSyncAssembly.Major == autoTestAssembly.Major && logSyncAssembly.Minor == autoTestAssembly.Minor && logSyncAssembly.Build == autoTestAssembly.Build, "packaged LogSync assemblies share a product version");

var templateAbout = XDocument.Load(Path.Combine(release, "About", "About.xml"));
Assert(templateAbout.Root?.Element("name")?.Value == ProjectTemplatePreflight.TemplateDisplayName && templateAbout.Root?.Element("packageId")?.Value == ProjectTemplatePreflight.TemplatePackageId, "release uses the branded template name and packageId");
Assert(templateAbout.Root?.Element("incompatibleWith")?.Elements("li").Any(item => item.Value.Equals("ludeon.rimworld", StringComparison.OrdinalIgnoreCase)) == true, "release template is incompatible with Core until it is configured");
var templateDescription = templateAbout.Root?.Element("description")?.Value ?? "";
Assert(templateDescription.Contains("[h2][b]快速开始[/b][/h2]", StringComparison.Ordinal) && !templateDescription.Contains("------", StringComparison.Ordinal) && System.Text.Encoding.UTF8.GetByteCount(templateDescription) <= 8000, "release template provides a Chinese quick-start guide within Steam description limits");
var templateProject = XDocument.Load(Path.Combine(release, "source", "MyCustomMod", "MyCustomMod.csproj"));
var releaseOutputPath = templateProject.Descendants().FirstOrDefault(element => element.Name.LocalName == "PropertyGroup" && element.Attribute("Condition")?.Value.Contains("Release", StringComparison.OrdinalIgnoreCase) == true)?.Elements().FirstOrDefault(element => element.Name.LocalName == "OutputPath")?.Value.Trim();
Assert(releaseOutputPath == @"..\..\1.6\Assemblies\", "template Release builds write the DLL to the deployed Assemblies directory");

using (var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(release, "mod_config.jsonc")), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }))
{
    var root = config.RootElement;
    Assert(root.GetProperty("mod_name").GetString() == ProjectTemplatePreflight.TemplateModName, "packaged configuration uses the template deployment name");
    Assert(root.GetProperty("ilspycmd_path").GetString() == "Ilspy\\ilspycmd.exe", "packaged configuration points at the packaged ILSpy directory");
    Assert(!root.GetProperty("isolated_desktop").GetBoolean(), "packaged configuration uses the normal desktop by default");
    Assert(!root.GetProperty("sync_source").GetBoolean(), "packaged configuration disables source synchronization by default");
    Assert(!root.TryGetProperty("shader_bundle", out _) && !root.GetProperty("sync_items").EnumerateArray().Any(item => item.GetString() == "AssetBundles"), "packaged configuration does not include shader build settings or asset bundle synchronization");
}

var versionOutput = Run(Path.Combine(tools, "RimDevTools.exe"), release, "--version");
using (var document = JsonDocument.Parse(versionOutput))
{
    var root = document.RootElement;
    Assert(root.GetProperty("ok").GetBoolean()
        && !string.IsNullOrWhiteSpace(root.GetProperty("rimDevToolsVersion").GetString())
        && root.EnumerateObject().Select(property => property.Name).SequenceEqual(["ok", "command", "rimDevToolsVersion"]), "packaged version output only reports RimDevTools");
}

var help = Run(Path.Combine(tools, "RimDevTools.exe"), release, "mods", "install-tools", "--help");
Assert(help.Contains("--mods-path", StringComparison.Ordinal) && help.Contains("--dry-run", StringComparison.Ordinal), "packaged tool Mod command exposes path and dry-run options");
var cleanupHelp = Run(Path.Combine(tools, "RimDevTools.exe"), release, "test", "cleanup", "--help");
Assert(cleanupHelp.Contains("--dry-run", StringComparison.Ordinal) && cleanupHelp.Contains("activeMods", StringComparison.Ordinal), "packaged CLI exposes safe transaction cleanup help");
var removedInit = RunResult(Path.Combine(tools, "RimDevTools.exe"), release, "init", "1.6", "--output", Path.Combine(Path.GetTempPath(), "removed-init"));
Assert(removedInit.ExitCode == 2 && removedInit.Stdout.Contains("UNKNOWN_COMMAND", StringComparison.Ordinal), "packaged CLI no longer exposes init");

var temporaryMods = Path.Combine(Path.GetTempPath(), "RimDevToolsReleaseTests", Guid.NewGuid().ToString("N"), "Mods");
Directory.CreateDirectory(temporaryMods);
try
{
    var dryRun = Run(Path.Combine(tools, "RimDevTools.exe"), release, "mods", "install-tools", "--mods-path", temporaryMods, "--dry-run");
    using var dryJson = JsonDocument.Parse(dryRun);
    Assert(dryJson.RootElement.GetProperty("ok").GetBoolean() && dryJson.RootElement.GetProperty("result").GetProperty("components").GetArrayLength() == 1, "packaged tool Mod dry-run plans one component in an explicit Mods directory");
}
finally
{
    var root = Directory.GetParent(temporaryMods)?.Parent?.FullName;
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
