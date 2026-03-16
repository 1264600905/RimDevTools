using Verse;

namespace LogSyncMod
{
    public static class LogSyncSmokeTest
    {
        public const string StartMarker = "[LogSyncSmoke] START";
        public const string DuplicateMessage = "[LogSyncSmoke] DUPLICATE_MESSAGE";
        public const string DuplicateKeywordMessage = "[LogSyncSmoke] DUPLICATE_KEYWORD_MESSAGE 冒烟测试";
        public const string WarningMessage = "[LogSyncSmoke] WARNING_MESSAGE";
        public const string ErrorMessage = "[LogSyncSmoke] ERROR_MESSAGE";
        public const string Keyword = "冒烟测试";
        public const string KeywordMessage = "[LogSyncSmoke] KEYWORD_MESSAGE 冒烟测试";
        public const string EndMarker = "[LogSyncSmoke] END";

        private static bool hasScheduled;
        private static bool hasRun;

        public static void Schedule()
        {
            if (hasScheduled || LogSyncMod.settings == null || !LogSyncMod.settings.enableSmokeTest) return;

            hasScheduled = true;

            LongEventHandler.ExecuteWhenFinished(() =>
            {
                var timer = new System.Timers.Timer(2000);
                timer.AutoReset = false;
                timer.Elapsed += (sender, args) =>
                {
                    Run();
                    timer.Dispose();
                };
                timer.Start();
            });
        }

        private static void Run()
        {
            if (hasRun || LogSyncMod.settings == null || !LogSyncMod.settings.enableSmokeTest) return;

            hasRun = true;

            Log.Message(StartMarker);
            Log.Message(DuplicateMessage);
            Log.Message(DuplicateMessage);
            Log.Message(DuplicateKeywordMessage);
            Log.Message(DuplicateKeywordMessage);
            Log.Warning(WarningMessage);
            Log.Error(ErrorMessage);
            Log.Message(KeywordMessage);
            Log.Message(EndMarker);
        }
    }
}
