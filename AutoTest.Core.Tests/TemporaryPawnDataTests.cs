using System.Xml.Linq;
using LogSyncMod.AutoTest.Scenarios;

internal static class TemporaryPawnDataTests
{
    internal static void Run(Action<bool, string> check)
    {
        const string partial = "<PawnTemplate id='temporaryPawn'><basePawnKind>SpaceRefugee</basePawnKind><Equipment><SetPrimary def='Gun_Revolver' quality='Excellent'/></Equipment></PawnTemplate>";
        var snapshot = TemporaryPawnData.Create(XElement.Parse(partial), "pawn-source", new[] { "basePawnKind", "Equipment" }, new[] { "Cooldowns", "Mod private state" });
        var encoded = TemporaryPawnData.Serialize(snapshot);
        var decoded = TemporaryPawnData.Parse(encoded);
        check(decoded.Sha256 == snapshot.Sha256 && decoded.TemplateXml == snapshot.TemplateXml && decoded.UnrecordedFields.SequenceEqual(snapshot.UnrecordedFields), "temporary Pawn round-trip preserves frozen supported data, hash and unrecorded scope");
        check(TemporaryPawnData.Parse(partial).TemplateXml.Contains("Gun_Revolver"), "a partial Pawn template is usable without exporting a complete game object graph");
        void Reject(string value, string message) { try { TemporaryPawnData.Parse(value); check(false, message); } catch (ScenarioConfigurationException) { check(true, message); } }
        Reject(encoded.Replace("Gun_Revolver", "Gun_Autopistol"), "editing a frozen snapshot without updating its hash is diagnosed");
        Reject(encoded.Replace("schemaVersion=\"1\"", "schemaVersion=\"2\""), "unsupported temporary Pawn schema is diagnosed");
        Reject(partial.Replace("<Equipment>", "<PrivateState/><Equipment>"), "partial templates still reject explicit unsupported fields");
        var resources = partial.Replace("</PawnTemplate>", "<Resources><GeneResource def='Hemogenic' value='0.25'/><Psyfocus value='0.5'/><AxolotlQi value='150'/></Resources></PawnTemplate>");
        check(TemporaryPawnData.Parse(resources).TemplateXml.Contains("GeneResource"), "staged templates share the closed native gene, psyfocus and Axolotl resource contract");
        Reject(resources.Replace("value='0.5'", "value='1.5'"), "staged psyfocus rejects an impossible resource fraction");
        Reject(resources.Replace("<GeneResource def='Hemogenic' value='0.25'/>", "<GeneResource def='Hemogenic' value='0.25'/><GeneResource def='Hemogenic' value='0.75'/>"), "duplicate staged resource assignments are diagnosed");
        Reject(partial.Replace("<basePawnKind>SpaceRefugee</basePawnKind>", ""), "partial template requires a valid generation base");
        Reject("<!DOCTYPE PawnTemplate [<!ENTITY ex SYSTEM 'file:///C:/Windows/win.ini'>]>" + partial, "temporary Pawn parser prohibits external entities");
        var prefix = Path.Combine(Path.GetTempPath(), "RimDevToolsPawnInputTests");
        var root = Path.Combine(prefix, Guid.NewGuid().ToString("N"), "中文项目");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "About"));
            File.WriteAllText(Path.Combine(root, "About", "About.xml"), "<ModMetaData><packageId>tests.pawn</packageId></ModMetaData>");
            File.WriteAllText(Path.Combine(root, "mod_config.jsonc"), "{\"game_version\":\"1.7\"}");
            var path = TemporaryPawnData.Save(root, snapshot);
            check(path == Path.Combine(root, ".tools", "ProjectData", "temporary-test-pawn.xml") && TemporaryPawnData.Load(root).Sha256 == snapshot.Sha256, "temporary Pawn uses the bound project's direct ProjectData file and reloads from disk");
            using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var changed = TemporaryPawnData.Create(XElement.Parse(partial.Replace("Gun_Revolver", "Gun_Autopistol")), "another", null, null);
                try { TemporaryPawnData.Save(root, changed); check(false, "failed atomic replacement preserves previous template"); }
                catch (IOException) { check(TemporaryPawnData.Load(root).Sha256 == snapshot.Sha256, "failed atomic replacement preserves previous template"); }
            }
            check(Directory.GetFiles(Path.GetDirectoryName(path), "*.tmp.*").Length == 0, "failed project-data replacement leaves no temporary input files");
            var frozen = TemporaryPawnData.Load(root);
            var next = TemporaryPawnData.Create(XElement.Parse(partial.Replace("Gun_Revolver", "Gun_Autopistol")), "next", null, null);
            TemporaryPawnData.Save(root, next);
            check(TemporaryPawnData.Load(root).Sha256 == next.Sha256 && frozen.Sha256 == snapshot.Sha256 && frozen.TemplateXml.Contains("Gun_Revolver"), "updating staged Pawn data cannot change an invocation's already frozen template");
            var projectB = Path.Combine(Path.GetDirectoryName(root), "项目 B");
            Directory.CreateDirectory(Path.Combine(projectB, "About")); File.WriteAllText(Path.Combine(projectB, "About", "About.xml"), "<ModMetaData><packageId>tests.pawn.b</packageId></ModMetaData>"); File.WriteAllText(Path.Combine(projectB, "mod_config.jsonc"), "{}");
            TemporaryPawnData.Save(projectB, snapshot);
            check(TemporaryPawnData.Load(projectB).Sha256 == snapshot.Sha256 && TemporaryPawnData.Load(root).Sha256 == next.Sha256, "switching input projects isolates staged Pawn files without rewriting the other project's data");
            var session = new LogSyncMod.AutoTest.ManualArtifactSession(); var first = session.Ensure(root); var sessionId = session.ManualSessionId;
            check(ReferenceEquals(first, session.Ensure(projectB)) && session.ManualSessionId == sessionId && first.DirectoryPath.StartsWith(root + Path.DirectorySeparatorChar), "one game-launch manual session keeps its first formal output path when the input project changes");
            var restarted = new LogSyncMod.AutoTest.ManualArtifactSession(); var later = restarted.Ensure(projectB);
            check(restarted.GameLaunchId != session.GameLaunchId && restarted.ManualSessionId != sessionId && later.DirectoryPath != first.DirectoryPath && File.Exists(TemporaryPawnData.PathFor(root)), "a restarted launch allocates a new manual session while persisted Pawn input remains available");
        }
        finally
        {
            var full = Path.GetFullPath(Path.GetDirectoryName(root));
            if (!full.StartsWith(Path.GetFullPath(prefix) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Test input cleanup escaped its owned root");
            if (Directory.Exists(full)) Directory.Delete(full, true);
        }
    }
}
