using System.Collections.Generic;
using Verse;

namespace LogSyncMod
{
    public class LogSyncSettings : ModSettings
    {
        public string logPath = "D:/RimWorldLogs.log";
        public string targetModIdsInput = ""; // 用于在UI中编辑
        public List<string> targetModIds = new List<string>();
        
        public string keywordsInput = ""; // 用于在UI中编辑
        public List<string> keywords = new List<string>();

        public bool syncMessage = false;
        public bool syncWarning = true;
        public bool syncError = true;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref logPath, "logPath", "D:/RimWorldLogs.log");
            Scribe_Collections.Look(ref targetModIds, "targetModIds", LookMode.Value);
            Scribe_Collections.Look(ref keywords, "keywords", LookMode.Value);
            Scribe_Values.Look(ref syncMessage, "syncMessage", false);
            Scribe_Values.Look(ref syncWarning, "syncWarning", true);
            Scribe_Values.Look(ref syncError, "syncError", true);

            if (targetModIds == null) targetModIds = new List<string>();
            if (keywords == null) keywords = new List<string>();
            
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                targetModIdsInput = string.Join(",", targetModIds);
                keywordsInput = string.Join(",", keywords);
            }
        }

        public void ApplyInputs()
        {
            targetModIds.Clear();
            foreach (var s in targetModIdsInput.Split(','))
            {
                string trimmed = s.Trim().ToLower();
                if (!string.IsNullOrEmpty(trimmed)) targetModIds.Add(trimmed);
            }

            keywords.Clear();
            foreach (var s in keywordsInput.Split(','))
            {
                string trimmed = s.Trim();
                if (!string.IsNullOrEmpty(trimmed)) keywords.Add(trimmed);
            }
        }
    }
}
