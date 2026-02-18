using Verse;
using System;

namespace LogSyncMod
{
    public static class LogUtils
    {
        private const string Prefix = "[LogSyncMod]";

        public static void Message(string msg)
        {
            Log.Message(Prefix + " " + msg);
        }

        public static void Warning(string msg)
        {
            Log.Warning(Prefix + " " + msg);
        }

        public static void Error(string msg)
        {
            Log.Error(Prefix + " " + msg);
        }
    }
}
