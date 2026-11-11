using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Linq;
using LogSyncMod.AutoTest.Scenarios;
using Verse;

namespace LogSyncMod.AutoTest
{
    internal static class ArenaWorkbenchStorage
    {
        private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
        private static string PathFor => Path.Combine(GenFilePaths.ConfigFolderPath, "LogSyncMod.ArenaWorkbench.json");
        internal static ArenaWorkbenchDocument Load()
        {
            if (!File.Exists(PathFor)) return ArenaWorkbench.Create();
            if (new FileInfo(PathFor).Length > 16 * 1024 * 1024) throw new InvalidDataException("群战工作台配置超过 16 MiB。");
            var doc = JsonSerializer.Deserialize<ArenaWorkbenchDocument>(File.ReadAllText(PathFor));
            if (doc == null || doc.SchemaVersion != 1 && doc.SchemaVersion != 2 || doc.Schedule == null || doc.Teams == null || doc.PawnPresets == null || doc.TeamPresets == null || doc.SchedulePresets == null)
                throw new InvalidDataException("群战工作台配置不完整；原文件保留在 " + PathFor);
            ArenaWorkbench.ValidateStructure(doc);
            doc = ArenaWorkbench.ForStorage(doc, false);
            ArenaWorkbench.UpgradeUi(doc);
            foreach (var team in doc.Teams.Concat(doc.TeamPresets))
            {
                if (team == null || team.Members == null || string.IsNullOrWhiteSpace(team.Id) || string.IsNullOrWhiteSpace(team.Name))
                    throw new InvalidDataException("编队配置不完整；原文件保留在 " + PathFor);
                foreach (var member in team.Members) ValidateMember(member);
            }
            foreach (var member in doc.PawnPresets) ValidateMember(member);
            if (doc.SchedulePresets.Any(s => s == null || string.IsNullOrWhiteSpace(s.Name))) throw new InvalidDataException("赛程预设不完整。");
            return doc;
        }
        private static void ValidateMember(ArenaWorkbenchMember member)
        {
            if (member == null || member.Count < 1 || member.Count > 200 || string.IsNullOrWhiteSpace(member.Name)) throw new InvalidDataException("角色组配置不完整。");
            TemporaryPawnData.Create(member.Read(), "workbench.load", Array.Empty<string>(), Array.Empty<string>());
        }
        internal static void Save(ArenaWorkbenchDocument document, bool rejectEmpty = true)
        {
            var xml = JsonSerializer.Serialize(ArenaWorkbench.ForStorage(document, rejectEmpty), Json);
            if (Encoding.UTF8.GetByteCount(xml) > 16 * 1024 * 1024) throw new InvalidDataException("工作台配置超过 16 MiB，请减少预设。");
            var temp = PathFor + ".tmp";
            try { File.WriteAllText(temp, xml, new UTF8Encoding(false)); File.Move(temp, PathFor, true); }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
}
