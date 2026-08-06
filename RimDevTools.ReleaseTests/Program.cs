using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;

var release = Path.GetFullPath(args.Length > 0 ? args[0] : Path.Combine(Environment.CurrentDirectory, "artifacts", "RimDevTools"));
var passed = 0;

RequireFile("RimDevTools.exe");
RequireFile("rimsearcher.exe");
RequireFile(Path.Combine("components", "RimSearcher_DataMod", "About", "About.xml"));
RequireFile(Path.Combine("components", "RimSearcher_DataMod", "Assemblies", "RimSearcher.DataMod.dll"));
RequireFile(Path.Combine("components", "RimSearcher_DataMod", "Native", "e_sqlite3.dll"));
RequireFile(Path.Combine("tools", "ilspycmd", "ilspycmd.exe"));
RequireFile(Path.Combine("licenses", "RimSearcher-LICENSE.txt"));
RequireFile(Path.Combine("licenses", "ILSpy-LICENSE.txt"));
RequireFile(Path.Combine("licenses", "third-party-notices.txt"));

Assert(!File.Exists(Path.Combine(release, "RimDevTools.dll")) && !File.Exists(Path.Combine(release, "RimDevTools.runtimeconfig.json")), "RimDevTools is a self-contained single-file publish");
var about = XDocument.Load(Path.Combine(release, "components", "RimSearcher_DataMod", "About", "About.xml"));
Assert(about.Root?.Element("packageId")?.Value.Equals("kearril.RimSearcherDataMod", StringComparison.OrdinalIgnoreCase) == true, "packaged DataMod has the expected packageId");

var versionOutput = Run(Path.Combine(release, "RimDevTools.exe"), "--version");
using (var document = JsonDocument.Parse(versionOutput))
{
    var root = document.RootElement;
    Assert(root.GetProperty("ok").GetBoolean(), "packaged RimDevTools starts successfully");
    Assert(root.GetProperty("componentsCompatible").GetBoolean(), "packaged RimSearcher and DataMod versions match");
    Assert(!string.IsNullOrWhiteSpace(root.GetProperty("rimSearcherVersion").GetString()) && !string.IsNullOrWhiteSpace(root.GetProperty("dataModVersion").GetString()), "version output reads actual packaged components");
}

var rimSearcherHelp = Run(Path.Combine(release, "rimsearcher.exe"), "--help");
Assert(rimSearcherHelp.Contains("--db <path>", StringComparison.Ordinal), "packaged rimsearcher exposes project database option");
var dataModVersion = AssemblyName.GetAssemblyName(Path.Combine(release, "components", "RimSearcher_DataMod", "Assemblies", "RimSearcher.DataMod.dll")).Version;
var rimSearcherVersion = FileVersionInfo.GetVersionInfo(Path.Combine(release, "rimsearcher.exe")).FileVersion;
Assert(SameProductVersion(dataModVersion, rimSearcherVersion), "component file versions are product-compatible");

Console.WriteLine($"PASS {passed} release tests");

void RequireFile(string relative)
{
    var path = Path.Combine(release, relative);
    Assert(File.Exists(path) && new FileInfo(path).Length > 0, $"release contains {relative}");
}

string Run(string executable, params string[] arguments)
{
    var startInfo = new ProcessStartInfo(executable) { WorkingDirectory = release, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
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
