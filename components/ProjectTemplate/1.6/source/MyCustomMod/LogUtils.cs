using Verse;
using System;
using System.Collections.Generic;

namespace MyCustomMod
{
    /// <summary>
    /// 日志工具类，统一管理项目日志输出。
    /// 仅当 settings.enableLog 启用时输出信息/警告/调试/分组日志；错误与异常总是输出。
    /// </summary>
    public static class LogUtils
    {
        private const string Prefix = "[MyCustomMod]";

        // 记录各种 key 对应的上一次打印的 realTime
        private static readonly Dictionary<string, float> lastLogTimes = new Dictionary<string, float>();

        public static void Message(string msg)
        {
            if (!(MyCustomModMod.settings?.enableLog ?? false)) return;
            Log.Message(Prefix + " " + msg);
        }

        public static void Warning(string msg)
        {
            if (!(MyCustomModMod.settings?.enableLog ?? false)) return;
            Log.Warning(Prefix + " " + msg);
        }

        public static void Error(string msg)
        {
            Log.Error(Prefix + " " + msg);
        }

        public static void Message(string className, string msg)
        {
            if (!(MyCustomModMod.settings?.enableLog ?? false)) return;
            Log.Message($"{Prefix} [{className}] {msg}");
        }

        public static void Warning(string className, string msg)
        {
            if (!(MyCustomModMod.settings?.enableLog ?? false)) return;
            Log.Warning($"{Prefix} [{className}] {msg}");
        }

        public static void Error(string className, string msg)
        {
            Log.Error($"{Prefix} [{className}] {msg}");
        }

        /// <summary>
        /// 防高频日志打印（限流打印）。常用于 Tick() 或 UI OnGUI() 中，避免瞬间刷屏。
        /// </summary>
        /// <param name="key">区分不同日志流的唯一键（通常可以用调用方法名或特定字符串）</param>
        /// <param name="msg">要打印的信息</param>
        /// <param name="intervalSeconds">限流间隔（秒），默认 1 秒</param>
        /// <param name="className">类名（可选）</param>
        public static void ThrottledMessage(string key, string msg, float intervalSeconds = 1f, string className = null)
        {
            if (!(MyCustomModMod.settings?.enableLog ?? false)) return;

            float currentTime = UnityEngine.Time.realtimeSinceStartup;

            if (!lastLogTimes.TryGetValue(key, out float lastTime) || (currentTime - lastTime) >= intervalSeconds)
            {
                lastLogTimes[key] = currentTime;
                
                if (string.IsNullOrEmpty(className))
                {
                    Log.Message($"{Prefix} {msg}");
                }
                else
                {
                    Log.Message($"{Prefix} [{className}] {msg}");
                }
            }
        }
    }
}


