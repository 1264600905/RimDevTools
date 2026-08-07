using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;

var release = Path.GetFullPath(args.Length > 0 ? args[0] : Path.Combine(Environment.CurrentDirectory, "artifacts", "RimDevTools-Package"));
var tools = Path.Combine(release, ".tools");
var passed = 0;

RequireFile("mod_config.jsonc");
RequireFile(Path.Combine(".tools", "RimDevTools.exe"));
RequireFile(Path.Combine(".tools", "rimsearcher.exe"));
RequireFile(Path.Combine(".tools", "Data", "RimSearcher_DataMod", "About", "About.xml"));
RequireFile(Path.Combine(".tools", "Data", "RimSearcher_DataMod", "Assemblies", "RimSearcher.DataMod.dll"));
RequireFile(Path.Combine(".tools", "Data", "RimSearcher_DataMod", "Native", "e_sqlite3.dll"));
RequireFile(Path.Combine(".tools", "Ilspy", "ilspycmd.exe"));
RequireFile(Path.Combine(".tools", "Data", "licenses", "RimSearcher-LICENSE.txt"));
RequireFile(Path.Combine(".tools", "Data", "licenses", "ILSpy-LICENSE.txt"));
RequireFile(Path.Combine(".tools", "Data", "licenses", "third-party-notices.txt"));
RequireFile(Path.Combine(".tools", "modlist", "defaultMods.xml"));
RequireDirectory(Path.Combine(".tools", "log", "AutoTestOutput"));
RequireDirectory(Path.Combine(".tools", "db"));
Assert(File.Exists(Path.Combine(tools, "log", "精选日志.log")) && new FileInfo(Path.Combine(tools, "log", "精选日志.log")).Length == 0, "release starts with an empty selected log");
Assert(!Directory.EnumerateFileSystemEntries(Path.Combine(tools, "log", "AutoTestOutput")).Any(), "release does not copy previous automatic test output");
Assert(!Directory.EnumerateFileSystemEntries(Path.Combine(tools, "db")).Any(), "release does not copy project databases");

var rimDevToolsExecutable = new FileInfo(Path.Combine(tools, "RimDevTools.exe"));
var rimSearcherExecutable = new FileInfo(Path.Combine(tools, "rimsearcher.exe"));
Assert(!File.Exists(Path.Combine(tools, "RimDevTools.dll")) && !File.Exists(Path.Combine(tools, "RimDevTools.runtimeconfig.json")) && !File.Exists(Path.Combine(tools, "RimDevTools.pdb")), "RimDevTools is a framework-dependent single-file publish");
Assert(!File.Exists(Path.Combine(tools, "rimsearcher.dll")) && !File.Exists(Path.Combine(tools, "rimsearcher.runtimeconfig.json")) && !File.Exists(Path.Combine(tools, "rimsearcher.pdb")), "RimSearcher is a framework-dependent single-file publish");
Assert(rimDevToolsExecutable.Length < 32L * 1024 * 1024, "RimDevTools does not embed the .NET runtime");
Assert(rimSearcherExecutable.Length < 32L * 1024 * 1024, "RimSearcher does not embed the .NET runtime");
var about = XDocument.Load(Path.Combine(tools, "Data", "RimSearcher_DataMod", "About", "About.xml"));
Assert(about.Root?.Element("packageId")?.Value.Equals("kearril.RimSearcherDataMod", StringComparison.OrdinalIgnoreCase) == true, "packaged DataMod has the expected packageId");

using (var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(release, "mod_config.jsonc")), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }))
{
    var root = config.RootElement;
    Assert(root.GetProperty("rimsearcher_db_path").GetString() == ".tools\\db\\defs.db", "packaged configuration points at the isolated database directory");
    Assert(root.GetProperty("ilspycmd_path").GetString() == "Ilspy\\ilspycmd.exe", "packaged configuration points at the packaged ILSpy directory");
    Assert(root.GetProperty("retention").GetProperty("max_auto_test_runs").GetInt32() == 3, "packaged configuration keeps three automatic test runs by default");
}

var versionOutput = Run(Path.Combine(tools, "RimDevTools.exe"), release, "--version");
using (var document = JsonDocument.Parse(versionOutput))
{
    var root = document.RootElement;
    Assert(root.GetProperty("ok").GetBoolean(), "packaged RimDevTools starts successfully");
    Assert(root.GetProperty("componentsCompatible").GetBoolean(), "packaged RimSearcher and DataMod versions match");
    Assert(!string.IsNullOrWhiteSpace(root.GetProperty("rimSearcherVersion").GetString()) && !string.IsNullOrWhiteSpace(root.GetProperty("dataModVersion").GetString()), "version output reads actual packaged components");
}

var rimSearcherHelp = Run(Path.Combine(tools, "rimsearcher.exe"), release, "--help");
Assert(rimSearcherHelp.Contains("--db <path>", StringComparison.Ordinal), "packaged rimsearcher exposes project database option");
var dataModVersion = AssemblyName.GetAssemblyName(Path.Combine(tools, "Data", "RimSearcher_DataMod", "Assemblies", "RimSearcher.DataMod.dll")).Version;
var rimSearcherVersion = FileVersionInfo.GetVersionInfo(Path.Combine(tools, "rimsearcher.exe")).FileVersion;
Assert(SameProductVersion(dataModVersion, rimSearcherVersion), "component file versions are product-compatible");

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
    var startInfo = new ProcessStartInfo(executable) { WorkingDirectory = workingDirectory, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
    foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
    using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Unable to start {executable}");
    var stdout = process.StandardOutput.ReadToEnd();
    var stderr = process.StandardError.ReadToEnd();
    process.WaitForExit();
    if (process.ExitCode != 0) throw new InvalidOperationException($"{Path.GetFileName(executable)} exited {process.ExitCode}: {stderr}");
    return stdout;
}

bool SameProductVersion(Version? assemblyVersion, string? fileVersion)
    => assemblyVersion is not null && Version.TryParse(fileVersion, out var parsed)
        && assemblyVersion.Major == parsed.Major && assemblyVersion.Minor == parsed.Minor && assemblyVersion.Build == parsed.Build;

void Assert(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException($"FAIL: {name}");
    passed++;
    Console.WriteLine($"PASS: {name}");
}
