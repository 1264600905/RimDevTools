#nullable disable
using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace LogSyncMod.AutoTest
{
    public sealed class TestArtifactDirectory
    {
        public string DirectoryPath { get; set; }
        public string SyncLogPath { get; set; }
        public string AutoTestOutputPath { get; set; }

        public static TestArtifactDirectory Allocate(string projectRoot, string kind = "auto", string gameLaunchId = null)
        {
            var root = Path.Combine(Path.GetFullPath(projectRoot), ".tools", "log");
            Directory.CreateDirectory(root);
            var prefix = DateTime.Now.ToString("[yyyy-MM, HH-mm]");
            for (var index = 0; index < 1000; index++)
            {
                var path = Path.Combine(root, index == 0 ? prefix : prefix + "_" + index.ToString("00"));
                if (Directory.Exists(path)) continue;
                Directory.CreateDirectory(path);
                var log = Path.Combine(path, "精选日志.log");
                using (new FileStream(log, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) { }
                File.WriteAllText(Path.Combine(path, "session.json"), JsonSerializer.Serialize(new { schemaVersion = 1, kind, gameLaunchId }), new UTF8Encoding(false));
                return new TestArtifactDirectory { DirectoryPath = path, SyncLogPath = log, AutoTestOutputPath = Path.Combine(path, "AutoTestOutput") };
            }
            throw new IOException("Cannot allocate a unique RimDevTools test artifact directory: " + prefix);
        }

        public static bool IsManual(string directory) => SessionKind(directory) == "manual";
        public static bool IsIndependent(string directory) => SessionKind(directory) is "manual" or "groupcombat";

        private static string SessionKind(string directory)
        {
            var path = Path.Combine(directory, "session.json");
            if (!File.Exists(path)) return null;
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            return json.RootElement.TryGetProperty("kind", out var kind) ? kind.GetString() : null;
        }
    }
}
