using System;
using System.IO;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using LogSyncMod.AutoTest.Scenarios;
using Verse;

namespace LogSyncMod.AutoTest
{
    internal sealed class ManualProjectContext
    {
        internal string Root;
        internal string Identity;
        internal string GameVersion;
    }

    internal static class ManualProjectBinding
    {
        private static ManualProjectContext explicitContext;

        internal static ManualProjectContext Resolve()
        {
            if (explicitContext != null) return Validate(explicitContext.Root, explicitContext.Identity);
            var path = Path.Combine(GenFilePaths.ConfigFolderPath, "RimDevTools.LastAutoTestProject.json");
            if (!File.Exists(path)) throw new ScenarioConfigurationException("尚未绑定测试项目，请在本入口填写 RimDevTools 项目根目录。");
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var data = document.RootElement;
            if (data.GetProperty("schemaVersion").GetInt32() != 1) throw new ScenarioConfigurationException("最近自动测试项目记录的版本不受支持。");
            return Validate(data.GetProperty("projectRoot").GetString(), data.GetProperty("projectIdentity").GetString());
        }

        internal static ManualProjectContext Bind(string root)
        {
            var context = Validate(root, null);
            explicitContext = context;
            // Explicit input context only. Never overwrite the last automatic run's project identity.
            return context;
        }

        private static ManualProjectContext Validate(string root, string expectedIdentity)
        {
            TemporaryPawnData.PathFor(root);
            root = Path.GetFullPath(root);
            using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "mod_config.jsonc")), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var version = config.RootElement.GetProperty("game_version").GetString();
            if (version != "1.7") throw new ScenarioConfigurationException("角色暂存和手动测试仅支持 1.7 项目。");
            using var reader = XmlReader.Create(Path.Combine(root, "About", "About.xml"), ScenarioLoader.ReaderSettings());
            var identity = XElement.Load(reader).Element("packageId")?.Value.Trim();
            if (string.IsNullOrWhiteSpace(identity) || expectedIdentity != null && !string.Equals(identity, expectedIdentity, StringComparison.OrdinalIgnoreCase)) throw new ScenarioConfigurationException("项目 packageId 与已记录身份不一致，请明确重新绑定项目。");
            return new ManualProjectContext { Root = root, Identity = identity, GameVersion = version };
        }
    }
}
