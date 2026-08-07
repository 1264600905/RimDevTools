using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;

var release = Path.GetFullPath(args.Length > 0 ? args[0] : Path.Combine(Environment.CurrentDirectory, "artifacts", "RimDevTools-Package"));
var tools = Path.Combine(release, ".tools");
var projectTemplates = Path.Combine(tools, "components", "ProjectTemplate");
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
RequireFile(Path.Combine(".tools", "modlist", "deadmanswitch-core.xml"));
RequireFile(Path.Combine(".tools", "components", "ProjectTemplate", "manifest.json"));
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
    Assert(root.GetProperty("isolated_desktop").GetBoolean(), "packaged configuration enables isolated desktop by default");
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

using (var manifestDocument = JsonDocument.Parse(File.ReadAllText(Path.Combine(projectTemplates, "manifest.json"))))
{
    var manifest = manifestDocument.RootElement;
    Assert(manifest.GetProperty("schemaVersion").GetInt32() == 1, "project template manifest uses the supported schema");
    var supportedVersions = manifest.GetProperty("supportedVersions").EnumerateArray().Select(value => value.GetString()).ToArray();
    Assert(supportedVersions.SequenceEqual(new[] { "1.6" }), "project template manifest advertises only the available 1.6 template");
    var requiredDirectories = manifest.GetProperty("requiredDirectories").EnumerateArray().Select(value => value.GetString()!).ToArray();
    var initRoot = Path.Combine(Path.GetTempPath(), "RimDevToolsReleaseTests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(initRoot);
    try
    {
        foreach (var version in new[] { "1.6" })
        {
            var template = Path.Combine(projectTemplates, version);
            Assert(!Directory.Exists(Path.Combine(template, ".tools")) && !File.Exists(Path.Combine(template, ".tools")), $"{version} template does not duplicate shared .tools");

            var output = Path.Combine(initRoot, $"project-{version}");
            var init = RunResult(Path.Combine(tools, "RimDevTools.exe"), release, "init", version, "--output", output);
            Assert(init.ExitCode == 0, $"init {version} exits successfully");
            using (var initJson = JsonDocument.Parse(init.Stdout))
            {
                var result = initJson.RootElement.GetProperty("result");
                Assert(initJson.RootElement.GetProperty("ok").GetBoolean() && result.GetProperty("version").GetString() == version, $"init {version} returns structured success JSON");
            }

            AssertTemplateMatchesOutput(template, tools, output, requiredDirectories, version);
            using (var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "mod_config.jsonc")), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }))
                Assert(config.RootElement.GetProperty("game_version").GetString() == version && config.RootElement.GetProperty("isolated_desktop").GetBoolean(), $"init {version} keeps its version-specific JSONC");
            Assert(XDocument.Load(Path.Combine(output, "LoadFolders.xml")).Root?.Element($"v{version}") is not null, $"init {version} keeps its version-specific LoadFolders XML");
            Assert(XDocument.Load(Path.Combine(output, "About", "About.xml")).Root?.Element("supportedVersions")?.Elements("li").Single().Value == version, $"init {version} keeps its version-specific About XML");
            Assert(XDocument.Load(Path.Combine(output, "source", "MyCustomMod", "MyCustomMod.csproj")).Root is not null, $"init {version} produces parseable csproj XML");
            Assert(File.ReadAllText(Path.Combine(output, "source", "MyCustomMod", "MyCustomMod.sln")).Contains("MyCustomMod.csproj", StringComparison.Ordinal), $"init {version} produces a valid solution contract");
            Assert(File.Exists(Path.Combine(output, ".decompilerserver", "DecompilerServer.exe")), $"init {version} includes DecompilerServer");
            Assert(File.ReadAllText(Path.Combine(output, ".codex", "config.toml")).Contains("command = \".decompilerserver/DecompilerServer.exe\"", StringComparison.Ordinal), $"init {version} configures Codex MCP with a project-relative executable path");
            foreach (var skill in new[] { "rimdevtools", "rimsearcher", "decompiler-mcp", "about-write-description", "rimworld-modding", "rimworld-development-engineer" })
            {
                var skillRoot = Path.Combine(output, ".agents", "skills", skill);
                Assert(File.Exists(Path.Combine(skillRoot, "SKILL.md")), $"init {version} includes the discoverable {skill} skill");
                Assert(File.Exists(Path.Combine(skillRoot, "agents", "openai.yaml")), $"init {version} includes UI metadata for {skill}");
                Assert(Directory.EnumerateFiles(Path.Combine(skillRoot, "references")).Any(), $"init {version} includes detailed references for {skill}");
            }
            var engineerSkill = File.ReadAllText(Path.Combine(output, ".agents", "skills", "rimworld-development-engineer", "SKILL.md"));
            Assert(engineerSkill.Contains("use DecompilerServer first", StringComparison.OrdinalIgnoreCase)
                && engineerSkill.Contains("Never run `ref pull` merely to inspect code", StringComparison.Ordinal), $"init {version} prioritizes DecompilerServer and keeps ref pull as fallback");
            var moddingSkillRoot = Path.Combine(output, ".agents", "skills", "rimworld-modding");
            Assert(File.Exists(Path.Combine(moddingSkillRoot, "LICENSE")), $"init {version} preserves the imported modding skill license");
            using (JsonDocument.Parse(File.ReadAllText(Path.Combine(moddingSkillRoot, "tests", "evals.json")))) { }
            Assert(Directory.EnumerateFiles(Path.Combine(moddingSkillRoot, "assets", "templates"), "*.xml").All(path => XDocument.Load(path).Root is not null), $"init {version} includes parseable RimWorld XML skill templates");

            var doctor = RunResult(Path.Combine(output, ".tools", "RimDevTools.exe"), output, "doctor");
            Assert(doctor.ExitCode == 0 && JsonDocument.Parse(doctor.Stdout).RootElement.GetProperty("ok").GetBoolean(), $"doctor discovers initialized {version} project");
            var initializedVersion = RunResult(Path.Combine(output, ".tools", "RimDevTools.exe"), output, "--version");
            Assert(initializedVersion.ExitCode == 0 && JsonDocument.Parse(initializedVersion.Stdout).RootElement.GetProperty("componentsCompatible").GetBoolean(), $"initialized {version} tools start with compatible components");
            Assert(!Directory.EnumerateFileSystemEntries(Path.Combine(output, ".tools", "db")).Any(), $"init {version} does not contain a database");
            Assert(!Directory.EnumerateFileSystemEntries(Path.Combine(output, ".tools", "log", "AutoTestOutput")).Any(), $"init {version} does not contain automatic test output");
            Assert(Directory.EnumerateFiles(Path.Combine(output, ".tools", "log")).All(path => new FileInfo(path).Length == 0), $"init {version} does not contain historical logs");
        }

        var unsupportedOutput = Path.Combine(initRoot, "unsupported");
        var unsupported = RunResult(Path.Combine(tools, "RimDevTools.exe"), release, "init", "1.5", "--output", unsupportedOutput);
        Assert(unsupported.ExitCode == 2 && JsonDocument.Parse(unsupported.Stdout).RootElement.GetProperty("error").GetProperty("code").GetString() == "VERSION_UNSUPPORTED" && !Directory.Exists(unsupportedOutput), "packaged init rejects unsupported versions before writing");

        var nonemptyOutput = Path.Combine(initRoot, "nonempty");
        Directory.CreateDirectory(nonemptyOutput);
        File.WriteAllText(Path.Combine(nonemptyOutput, "keep.txt"), "user");
        var nonempty = RunResult(Path.Combine(tools, "RimDevTools.exe"), release, "init", "1.6", "--output", nonemptyOutput);
        Assert(nonempty.ExitCode == 2 && JsonDocument.Parse(nonempty.Stdout).RootElement.GetProperty("error").GetProperty("code").GetString() == "INIT_TARGET_NOT_EMPTY" && File.ReadAllText(Path.Combine(nonemptyOutput, "keep.txt")) == "user", "packaged init preserves a nonempty target");
    }
    finally
    {
        if (Directory.Exists(initRoot)) Directory.Delete(initRoot, recursive: true);
    }
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
    if (result.ExitCode != 0) throw new InvalidOperationException($"{Path.GetFileName(executable)} exited {result.ExitCode}: {result.Stderr}");
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

void AssertTemplateMatchesOutput(string template, string sharedTools, string output, IReadOnlyList<string> requiredDirectories, string version)
{
    var templateFiles = RelativeEntries(template, files: true);
    var outputFiles = RelativeEntries(output, files: true);
    var toolFiles = SharedToolEntries(sharedTools, files: true);
    var expectedFiles = templateFiles.Concat(toolFiles.Select(path => Path.Combine(".tools", path))).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    Assert(expectedFiles.SequenceEqual(outputFiles, StringComparer.OrdinalIgnoreCase), $"init {version} contains exactly its template plus shared tooling files");
    var allBytesMatch = true;
    foreach (var relative in templateFiles)
        allBytesMatch &= File.ReadAllBytes(Path.Combine(template, relative)).SequenceEqual(File.ReadAllBytes(Path.Combine(output, relative)));
    foreach (var relative in toolFiles)
        allBytesMatch &= File.ReadAllBytes(Path.Combine(sharedTools, relative)).SequenceEqual(File.ReadAllBytes(Path.Combine(output, ".tools", relative)));
    Assert(allBytesMatch, $"init {version} copies template and shared tooling files byte-for-byte");

    var expectedDirectories = new HashSet<string>(RelativeEntries(template, files: false), StringComparer.OrdinalIgnoreCase);
    expectedDirectories.Add(".tools");
    foreach (var relative in SharedToolEntries(sharedTools, files: false)) expectedDirectories.Add(Path.Combine(".tools", relative));
    foreach (var declared in requiredDirectories)
    {
        var relative = declared.Replace("{version}", version, StringComparison.Ordinal).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        while (!string.IsNullOrEmpty(relative) && relative != ".")
        {
            expectedDirectories.Add(relative);
            relative = Path.GetDirectoryName(relative) ?? "";
        }
    }
    var outputDirectories = RelativeEntries(output, files: false);
    Assert(expectedDirectories.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).SequenceEqual(outputDirectories, StringComparer.OrdinalIgnoreCase), $"init {version} creates only manifest-declared directory paths");
}

string[] RelativeEntries(string root, bool files)
    => (files ? Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories) : Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
        .Select(path => Path.GetRelativePath(root, path))
        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
        .ToArray();

string[] SharedToolEntries(string root, bool files)
    => RelativeEntries(root, files)
        .Where(path => !path.Equals("components", StringComparison.OrdinalIgnoreCase) && !path.StartsWith($"components{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
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
