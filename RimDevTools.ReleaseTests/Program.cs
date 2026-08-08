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
RequireFile(Path.Combine(".tools", "RimDevTools.exe"));
RequireFile(Path.Combine(".tools", "rimsearcher.exe"));
RequireFile(Path.Combine(".tools", "Data", "tool-mods.manifest.json"));
RequireFile(Path.Combine(".tools", "Data", "LogSyncMod", "About", "About.xml"));
RequireFile(Path.Combine(".tools", "Data", "LogSyncMod", "1.6", "Assemblies", "LogSyncMod.dll"));
RequireFile(Path.Combine(".tools", "Data", "LogSyncMod", "1.6", "Assemblies", "LogSyncMod.AutoTest.dll"));
RequireFile(Path.Combine(".tools", "Data", "RimSearcher_DataMod", "About", "About.xml"));
RequireFile(Path.Combine(".tools", "Data", "RimSearcher_DataMod", "Assemblies", "RimSearcher.DataMod.dll"));
RequireFile(Path.Combine(".tools", "Data", "RimSearcher_DataMod", "Native", "e_sqlite3.dll"));
RequireFile(Path.Combine(".tools", "Ilspy", "ilspycmd.exe"));
RequireFile(Path.Combine(".tools", "Data", "licenses", "RimSearcher-LICENSE.txt"));
RequireFile(Path.Combine(".tools", "Data", "licenses", "ILSpy-LICENSE.txt"));
RequireFile(Path.Combine(".tools", "Data", "licenses", "third-party-notices.txt"));
RequireFile(Path.Combine(".tools", "modlist", "defaultMods.xml"));
Assert(File.Exists(Path.Combine(tools, "log", "精选日志.log")) && new FileInfo(Path.Combine(tools, "log", "精选日志.log")).Length == 0, "release starts with an empty selected log");
RequireDirectory(Path.Combine(".tools", "log", "AutoTestOutput"));
RequireDirectory(Path.Combine(".tools", "db"));
Assert(!Directory.Exists(Path.Combine(tools, "components", "ProjectTemplate")), "release does not contain a nested project template");
Assert(!File.Exists(Path.Combine(release, "components", "ProjectTemplate", "manifest.json")), "release does not copy the build manifest into the project");
Assert(!Directory.EnumerateFileSystemEntries(Path.Combine(tools, "db")).Any(), "release does not copy project databases");
Assert(!Directory.EnumerateFileSystemEntries(Path.Combine(tools, "log", "AutoTestOutput")).Any(), "release does not copy automatic test history");
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

using (var manifestDocument = JsonDocument.Parse(File.ReadAllText(Path.Combine(data, "tool-mods.manifest.json"))))
{
    var manifest = manifestDocument.RootElement;
    Assert(manifest.GetProperty("schemaVersion").GetInt32() == 1 && manifest.GetProperty("components").GetArrayLength() == 2, "tool Mod manifest declares both components");
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

var dataModAbout = XDocument.Load(Path.Combine(data, "RimSearcher_DataMod", "About", "About.xml"));
Assert(dataModAbout.Root?.Element("packageId")?.Value.Equals("kearril.RimSearcherDataMod", StringComparison.OrdinalIgnoreCase) == true, "packaged DataMod has the expected packageId");
var dataModVersion = AssemblyName.GetAssemblyName(Path.Combine(data, "RimSearcher_DataMod", "Assemblies", "RimSearcher.DataMod.dll")).Version;
var rimSearcherVersion = FileVersionInfo.GetVersionInfo(Path.Combine(tools, "rimsearcher.exe")).FileVersion;
Assert(SameProductVersion(dataModVersion, rimSearcherVersion), "packaged RimSearcher and DataMod versions match");

using (var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(release, "mod_config.jsonc")), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }))
{
    var root = config.RootElement;
    Assert(root.GetProperty("rimsearcher_db_path").GetString() == ".tools\\db\\defs.db", "packaged configuration points at the isolated database directory");
    Assert(root.GetProperty("ilspycmd_path").GetString() == "Ilspy\\ilspycmd.exe", "packaged configuration points at the packaged ILSpy directory");
    Assert(!root.GetProperty("isolated_desktop").GetBoolean(), "packaged configuration uses the normal desktop by default");
}

var versionOutput = Run(Path.Combine(tools, "RimDevTools.exe"), release, "--version");
using (var document = JsonDocument.Parse(versionOutput))
{
    var root = document.RootElement;
    Assert(root.GetProperty("ok").GetBoolean() && root.GetProperty("componentsCompatible").GetBoolean(), "packaged RimDevTools starts with compatible CLI/DataMod components");
    Assert(!string.IsNullOrWhiteSpace(root.GetProperty("rimSearcherVersion").GetString()) && !string.IsNullOrWhiteSpace(root.GetProperty("dataModVersion").GetString()), "version output reads actual packaged component versions");
}

var help = Run(Path.Combine(tools, "RimDevTools.exe"), release, "mods", "install-tools", "--help");
Assert(help.Contains("--mods-path", StringComparison.Ordinal) && help.Contains("--dry-run", StringComparison.Ordinal), "packaged tool Mod command exposes path and dry-run options");
var removedInit = RunResult(Path.Combine(tools, "RimDevTools.exe"), release, "init", "1.6", "--output", Path.Combine(Path.GetTempPath(), "removed-init"));
Assert(removedInit.ExitCode == 2 && removedInit.Stdout.Contains("UNKNOWN_COMMAND", StringComparison.Ordinal), "packaged CLI no longer exposes init");

var temporaryMods = Path.Combine(Path.GetTempPath(), "RimDevToolsReleaseTests", Guid.NewGuid().ToString("N"), "Mods");
Directory.CreateDirectory(temporaryMods);
try
{
    var dryRun = Run(Path.Combine(tools, "RimDevTools.exe"), release, "mods", "install-tools", "--mods-path", temporaryMods, "--dry-run");
    using var dryJson = JsonDocument.Parse(dryRun);
    Assert(dryJson.RootElement.GetProperty("ok").GetBoolean() && dryJson.RootElement.GetProperty("result").GetProperty("components").GetArrayLength() == 2, "packaged tool Mod dry-run plans both components in an explicit Mods directory");
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

bool SameProductVersion(Version? assemblyVersion, string? fileVersion)
    => assemblyVersion is not null && Version.TryParse(fileVersion, out var parsed)
        && assemblyVersion.Major == parsed.Major && assemblyVersion.Minor == parsed.Minor && assemblyVersion.Build == parsed.Build;

void Assert(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException($"FAIL: {name}");
    passed++;
    Console.WriteLine($"PASS: {name}");
}
