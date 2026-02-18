using Verse;
using System;

namespace TempProject
{
    /// <summary>
    /// 日志工具类，统一管理项目日志输出。
    /// 仅当 settings.enableLog 启用时输出信息/警告/调试/分组日志；错误与异常总是输出。
    /// </summary>
    public static class LogUtils
    {
        private const string Prefix = "[TempProject]";

        public static void Message(string msg)
        {
            if (!(TempProjectMod.settings?.enableLog ?? false)) return;
            Log.Message(Prefix + " " + msg);
        }

        public static void Warning(string msg)
        {
            if (!(TempProjectMod.settings?.enableLog ?? false)) return;
            Log.Warning(Prefix + " " + msg);
        }

        public static void Error(string msg)
        {
            Log.Error(Prefix + " " + msg);
        }

        public static void Message(string className, string msg)
        {
            if (!(TempProjectMod.settings?.enableLog ?? false)) return;
            Log.Message($"{Prefix} [{className}] {msg}");
        }

        public static void Warning(string className, string msg)
        {
            if (!(TempProjectMod.settings?.enableLog ?? false)) return;
            Log.Warning($"{Prefix} [{className}] {msg}");
        }

        public static void Error(string className, string msg)
        {
            Log.Error($"{Prefix} [{className}] {msg}");
        }
    }
}


