#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace LogSyncMod.AutoTest.Scenarios
{
    public sealed class TemporaryPawnSnapshot
    {
        public int SchemaVersion { get; set; } = 1;
        public string Sha256 { get; set; }
        public string TemplateXml { get; set; }
        public string SourceIdentity { get; set; }
        public string CapturedAtUtc { get; set; }
        public List<string> RecordedFields { get; set; } = new List<string>();
        public List<string> UnrecordedFields { get; set; } = new List<string>();
    }

    // Functional project data, deliberately separate from run artifacts and hand-authored test rules.
    public static class TemporaryPawnData
    {
        public const string FileName = "temporary-test-pawn.xml";

        public static TemporaryPawnSnapshot Create(XElement template, string sourceIdentity, IEnumerable<string> recorded, IEnumerable<string> unrecorded)
        {
            var canonical = ValidateTemplate(template);
            return new TemporaryPawnSnapshot {
                TemplateXml = canonical, Sha256 = Hash(canonical), SourceIdentity = sourceIdentity,
                CapturedAtUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                RecordedFields = (recorded ?? Enumerable.Empty<string>()).Distinct().ToList(),
                UnrecordedFields = (unrecorded ?? Enumerable.Empty<string>()).Distinct().ToList()
            };
        }

        public static string Serialize(TemporaryPawnSnapshot snapshot)
        {
            var template = ValidateTemplate(XElement.Parse(snapshot.TemplateXml));
            if (snapshot.SchemaVersion != 1 || snapshot.Sha256 != Hash(template)) throw new ScenarioConfigurationException("Temporary Pawn snapshot version/hash does not match its frozen template");
            var root = new XElement("TemporaryTestPawn", new XAttribute("schemaVersion", 1), new XAttribute("sha256", snapshot.Sha256),
                new XAttribute("capturedAtUtc", snapshot.CapturedAtUtc ?? ""), new XAttribute("sourceIdentity", snapshot.SourceIdentity ?? ""),
                new XElement("RecordedFields", snapshot.RecordedFields.Select(f => new XElement("Field", f))),
                new XElement("UnrecordedFields", snapshot.UnrecordedFields.Select(f => new XElement("Field", f))), XElement.Parse(template));
            return root.ToString();
        }

        public static TemporaryPawnSnapshot Parse(string xml, string source = FileName)
        {
            try
            {
                using var reader = XmlReader.Create(new StringReader(xml), ScenarioLoader.ReaderSettings());
                var root = XElement.Load(reader);
                if (root.Name == "PawnTemplate") return Create(root, source, root.Elements().Select(e => e.Name.LocalName), new[] { "Unspecified fields use native PawnKind generation" });
                if (root.Name != "TemporaryTestPawn" || ScenarioLoader.A(root, "schemaVersion") != "1") throw new ScenarioConfigurationException("Unsupported temporary Pawn schema");
                if (root.Attributes().Any(a => !new[] { "schemaVersion", "sha256", "capturedAtUtc", "sourceIdentity" }.Contains(a.Name.LocalName)) || root.Elements().Any(e => !new[] { "PawnTemplate", "RecordedFields", "UnrecordedFields" }.Contains(e.Name.LocalName)) || root.Elements().GroupBy(e => e.Name).Any(g => g.Count() != 1)) throw new ScenarioConfigurationException("Unknown/duplicate temporary Pawn metadata");
                var canonical = ValidateTemplate(root.Element("PawnTemplate") ?? throw new ScenarioConfigurationException("PawnTemplate is missing"));
                var hash = Hash(canonical);
                if (!string.Equals(hash, ScenarioLoader.A(root, "sha256"), StringComparison.Ordinal)) throw new ScenarioConfigurationException("Temporary Pawn template hash mismatch");
                List<string> Fields(string section) {
                    var node = root.Element(section);
                    if (node != null && (node.HasAttributes || node.Elements().Any(f => f.Name != "Field" || f.HasAttributes || f.HasElements))) throw new ScenarioConfigurationException("Invalid temporary Pawn field inventory");
                    return node?.Elements("Field").Select(e => e.Value).ToList() ?? new List<string>();
                }
                return new TemporaryPawnSnapshot { TemplateXml = canonical, Sha256 = hash, SourceIdentity = ScenarioLoader.A(root, "sourceIdentity"), CapturedAtUtc = ScenarioLoader.A(root, "capturedAtUtc"), RecordedFields = Fields("RecordedFields"), UnrecordedFields = Fields("UnrecordedFields") };
            }
            catch (Exception ex) when (ex is XmlException || ex is ScenarioConfigurationException)
            { throw new ScenarioConfigurationException(source + ": " + ex.Message); }
        }

        public static TemporaryPawnSnapshot Load(string projectRoot)
        {
            var path = PathFor(projectRoot);
            if (!File.Exists(path)) throw new FileNotFoundException("No temporary test Pawn has been captured for this project", path);
            if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new ScenarioConfigurationException(path + ": temporary Pawn input exceeds 4 MiB");
            return Parse(File.ReadAllText(path), path);
        }

        public static string Save(string projectRoot, TemporaryPawnSnapshot snapshot)
        {
            var content = Serialize(snapshot);
            var path = PathFor(projectRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var temporary = path + ".tmp." + Guid.NewGuid().ToString("N");
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    var bytes = new UTF8Encoding(false).GetBytes(content); stream.Write(bytes, 0, bytes.Length); stream.Flush(true);
                }
                if (File.Exists(path)) File.Replace(temporary, path, null, true);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return path;
        }

        public static string PathFor(string projectRoot)
        {
            if (!Path.IsPathFullyQualified(projectRoot ?? "")) throw new ScenarioConfigurationException("An explicit absolute project root is required");
            var root = Path.GetFullPath(projectRoot);
            if (!Directory.Exists(root) || !File.Exists(Path.Combine(root, "mod_config.jsonc")) || !File.Exists(Path.Combine(root, "About", "About.xml"))) throw new ScenarioConfigurationException("Not a valid RimDevTools project: " + root);
            var path = Path.Combine(root, ".tools", "ProjectData", FileName);
            var cursor = path;
            while (cursor != null && cursor.Length >= root.Length)
            {
                if ((Directory.Exists(cursor) || File.Exists(cursor)) && (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0) throw new ScenarioConfigurationException("ProjectData must not redirect outside its project: " + cursor);
                cursor = Path.GetDirectoryName(cursor);
            }
            return path;
        }

        private static string ValidateTemplate(XElement template)
        {
            if (template.Name != "PawnTemplate" || template.Attribute("extends") != null) throw new ScenarioConfigurationException("Temporary Pawn must be a standalone PawnTemplate");
            var copy = new XElement(template); copy.SetAttributeValue("id", "temporaryPawn");
            foreach (var text in copy.DescendantNodes().OfType<XText>().Where(t => t.Parent.HasElements && string.IsNullOrWhiteSpace(t.Value)).ToList()) text.Remove();
            var actor = new XElement(copy); actor.Name = "Pawn"; actor.SetAttributeValue("id", "p");
            var suite = new XElement("TestSuite", new XAttribute("schemaVersion", 1), new XAttribute("id", "temporaryPawnValidation"),
                new XElement("Templates", copy),
                new XElement("Case", new XAttribute("id", "validate"), new XElement("testDefName", "TemporaryPawn_Validate"), new XElement("Scene"), new XElement("Actors", actor),
                    new XElement("Steps", new XElement("Require", new XAttribute("id", "exists"), new XElement("ActorState", new XAttribute("actor", "p"), new XAttribute("state", "alive")))),
                    new XElement("Success", new XElement("StepSucceeded", new XAttribute("ref", "exists")))));
            ScenarioLoader.Compile(new[] { new ScenarioInput { Path = FileName, Xml = suite.ToString(), Sha256 = "validation" } });
            return copy.ToString(SaveOptions.DisableFormatting);
        }

        private static string Hash(string value) { using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant(); }
    }
}
