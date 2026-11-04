using System.Text.Json;
using System.Xml.Linq;
using RimDevTools;

internal sealed record TestWorkshopIdentity(string ProjectRoot, string TargetRoot, string PackageId, string Name,
    byte[] OriginalAbout, byte[] OriginalSourceId, byte[] OriginalTargetId, string? PublishedFileId, bool Finished);

// A recoverable local test transaction, separate from normal publication and timeout semantics.
internal sealed class SteamWorkshopsTestIdentityScope
{
    private const string FileName = "steamworkshops-test-identity.json";
    private TestWorkshopIdentity identity;
    private readonly Func<string[], Task<JsonElement>> run;
    private string PathFor => ProjectDataPaths.File(SteamWorkshopsLiveTests.Project, FileName);
    internal string PackageId => identity.PackageId;
    internal string? PublishedFileId => identity.PublishedFileId;
    private SteamWorkshopsTestIdentityScope(TestWorkshopIdentity identity, Func<string[], Task<JsonElement>> run) { this.identity = identity; this.run = run; }

    internal static async Task RecoverLocalAsync(Func<string[], Task<JsonElement>> run)
    {
        var path = ProjectDataPaths.File(SteamWorkshopsLiveTests.Project, FileName);
        if (!File.Exists(path)) return;
        var record = JsonSerializer.Deserialize<TestWorkshopIdentity>(File.ReadAllText(path), JsonDefaults.Storage) ?? throw new Exception("Invalid test identity transaction");
        var scope = new SteamWorkshopsTestIdentityScope(record, run);
        scope.ValidateRecord();
        await scope.RestoreMainAsync();
        if (record.Finished) File.Delete(path);
    }

    internal static SteamWorkshopsTestIdentityScope Begin(Func<string[], Task<JsonElement>> run)
    {
        var project = SteamWorkshopsLiveTests.Project; var target = SteamWorkshopsLiveTests.Target;
        var path = ProjectDataPaths.File(project, FileName);
        TestWorkshopIdentity identity;
        if (File.Exists(path))
        {
            identity = JsonSerializer.Deserialize<TestWorkshopIdentity>(File.ReadAllText(path), JsonDefaults.Storage) ?? throw new Exception("Invalid test identity transaction");
            if (identity.PublishedFileId is null) throw new Exception("Previous test identity has no known created ID. Inspect its timeout/creation evidence before explicitly retrying; normal upload has no timeout block.");
        }
        else
        {
            var suffix = Guid.NewGuid().ToString("N")[..12];
            identity = new(project, target, "trigger.cowgun.firstuploadtest." + suffix, "奶牛枪首次上传测试 " + suffix,
                File.ReadAllBytes(System.IO.Path.Combine(project, "About", "About.xml")),
                File.ReadAllBytes(System.IO.Path.Combine(project, "About", "PublishedFileId.txt")),
                File.ReadAllBytes(System.IO.Path.Combine(target, "About", "PublishedFileId.txt")), null, false);
        }
        var scope = new SteamWorkshopsTestIdentityScope(identity, run); scope.ValidateRecord(); scope.Save();
        var aboutPath = System.IO.Path.Combine(project, "About", "About.xml");
        var about = XDocument.Parse(System.Text.Encoding.UTF8.GetString(identity.OriginalAbout).TrimStart('\uFEFF'));
        about.Root!.SetElementValue("packageId", identity.PackageId); about.Root.SetElementValue("name", identity.Name);
        about.Save(aboutPath);
        foreach (var root in new[] { project, target })
        {
            var idPath = System.IO.Path.Combine(root, "About", "PublishedFileId.txt");
            if (identity.PublishedFileId is null) File.Delete(idPath); else AtomicFile.WriteAllText(idPath, identity.PublishedFileId);
        }
        return scope;
    }

    internal void CaptureCreatedId(JsonElement publish)
    {
        var id = publish.GetProperty("publishedFileId").GetString() ?? throw new Exception("Creation returned no id");
        if (id == "3833398574" || SteamWorkshopsCommand.ParseId(id) is null) throw new Exception("Unexpected created item id");
        if (identity.PublishedFileId is not null && identity.PublishedFileId != id) throw new Exception("Test id changed");
        var context = ProjectDiscovery.Find(identity.ProjectRoot, out _)!;
        if (context.Config.CurrentPackageId(identity.ProjectRoot) != identity.PackageId || DeploymentReceiptStore.KnownBinding(context) != id)
            throw new Exception("Returned id lacks the exact test identity's known binding");
        identity = identity with { PublishedFileId = id }; Save();
    }

    internal async Task RestoreMainAsync()
    {
        ValidateRecord();
        var aboutPath = System.IO.Path.Combine(identity.ProjectRoot, "About", "About.xml");
        var currentAbout = XDocument.Load(aboutPath);
        var currentPackage = currentAbout.Root?.Element("packageId")?.Value;
        if (currentPackage != identity.PackageId && currentPackage != "trigger.cowgun") throw new Exception("Source identity changed outside the test transaction; refusing overwrite");
        if (identity.PublishedFileId is null && currentPackage == identity.PackageId)
        {
            var context = ProjectDiscovery.Find(identity.ProjectRoot, out _)!;
            var known = DeploymentReceiptStore.KnownBinding(context);
            if (known is not null) { identity = identity with { PublishedFileId = known }; Save(); }
        }
        AtomicFile.WriteAllBytes(aboutPath, identity.OriginalAbout);
        AtomicFile.WriteAllBytes(System.IO.Path.Combine(identity.ProjectRoot, "About", "PublishedFileId.txt"), identity.OriginalSourceId);
        AtomicFile.WriteAllBytes(System.IO.Path.Combine(identity.TargetRoot, "About", "PublishedFileId.txt"), identity.OriginalTargetId);
        await run(["deploy"]);
        var status = await run(["steamworkshops", "status"]);
        if (status.GetProperty("publishedFileId").GetString() != "3833398574" || !status.GetProperty("bindingConsistent").GetBoolean())
            throw new Exception("Main identity restoration failed");
        if (identity.Finished && File.Exists(PathFor)) File.Delete(PathFor);
    }

    internal void MarkFinished() { identity = identity with { Finished = true }; Save(); }
    private void Save() => AtomicFile.WriteAllTextUsingMoveReplacement(PathFor, JsonSerializer.Serialize(identity, JsonDefaults.Storage));
    private void ValidateRecord()
    {
        if (identity.ProjectRoot != SteamWorkshopsLiveTests.Project || identity.TargetRoot != SteamWorkshopsLiveTests.Target
            || !identity.PackageId.StartsWith("trigger.cowgun.firstuploadtest.", StringComparison.Ordinal)
            || identity.PackageId.Length != "trigger.cowgun.firstuploadtest.".Length + 12
            || XDocument.Parse(System.Text.Encoding.UTF8.GetString(identity.OriginalAbout).TrimStart('\uFEFF')).Root?.Element("packageId")?.Value != "trigger.cowgun"
            || System.Text.Encoding.UTF8.GetString(identity.OriginalSourceId).TrimStart('\uFEFF').Trim() != "3833398574"
            || System.Text.Encoding.UTF8.GetString(identity.OriginalTargetId).TrimStart('\uFEFF').Trim() != "3833398574")
            throw new Exception("Test identity transaction failed fixed-path/main-identity checks");
    }
}
