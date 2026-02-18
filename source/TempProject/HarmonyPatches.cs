using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Diagnostics;
using HarmonyLib;
using Verse;

namespace LogSyncMod
{
    [HarmonyPatch(typeof(Log))]
    public static class LogPatches
    {
        private static readonly Queue<string> recentLogs = new Queue<string>();
        private static readonly HashSet<string> processedLogs = new HashSet<string>();
        private static readonly object fileLock = new object();
        private const int MaxLogLines = 500;

        [HarmonyPatch(nameof(Log.Message), new Type[] { typeof(string) })]
        [HarmonyPrefix]
        public static void MessagePrefix(string text)
        {
            if (LogSyncMod.settings.syncMessage)
                ProcessLog(text, "Message");
        }

        [HarmonyPatch(nameof(Log.Warning), new Type[] { typeof(string) })]
        [HarmonyPrefix]
        public static void WarningPrefix(string text)
        {
            if (LogSyncMod.settings.syncWarning)
                ProcessLog(text, "Warning");
        }

        [HarmonyPatch(nameof(Log.Error), new Type[] { typeof(string) })]
        [HarmonyPrefix]
        public static void ErrorPrefix(string text)
        {
            if (LogSyncMod.settings.syncError)
                ProcessLog(text, "Error");
        }

        private static void ProcessLog(string text, string type)
        {
            if (string.IsNullOrEmpty(text)) return;

            // 1. 检查关键词（强制记录）
            bool keywordMatched = false;
            foreach (var keyword in LogSyncMod.settings.keywords)
            {
                if (text.Contains(keyword))
                {
                    keywordMatched = true;
                    break;
                }
            }

            // 2. 堆栈分析识别模组
            string sourceMod = GetSourceModPackageId();
            bool modMatched = !string.IsNullOrEmpty(sourceMod) && LogSyncMod.settings.targetModIds.Contains(sourceMod);

            // 3. 过滤逻辑
            if (keywordMatched || modMatched)
            {
                WriteToFile(text, type, sourceMod);
            }
        }

        private static string GetSourceModPackageId()
        {
            StackTrace stackTrace = new StackTrace();
            for (int i = 2; i < stackTrace.FrameCount; i++) // 跳过补丁自身的几层
            {
                MethodBase method = stackTrace.GetFrame(i).GetMethod();
                if (method == null) continue;
                
                Assembly assembly = method.DeclaringType?.Assembly;
                if (assembly == null) continue;

                // 排除原版、Harmony、此模组自身
                if (assembly == typeof(Log).Assembly || 
                    assembly == typeof(Harmony).Assembly || 
                    assembly == typeof(LogSyncMod).Assembly) 
                    continue;

                // 查找该程序集所属的 Mod
                foreach (var mod in LoadedModManager.RunningMods)
                {
                    if (mod.assemblies.loadedAssemblies.Contains(assembly))
                    {
                        return mod.PackageIdPlayerFacing;
                    }
                }
            }
            return null;
        }

        private static void WriteToFile(string text, string type, string sourceMod)
        {
            string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{type}] [{sourceMod ?? "Unknown"}] {text}";
            
            // 会话内去重检查（防止单次调用产生重复行）
            if (processedLogs.Contains(logEntry)) return;

            lock (fileLock)
            {
                try
                {
                    string path = LogSyncMod.settings.logPath;
                    if (string.IsNullOrEmpty(path)) return;

                    // 维护队列
                    recentLogs.Enqueue(logEntry);
                    processedLogs.Add(logEntry);
                    
                    while (recentLogs.Count > MaxLogLines)
                    {
                        string removed = recentLogs.Dequeue();
                        processedLogs.Remove(removed);
                    }

                    // 确保目录存在
                    string directory = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    // 覆盖写入最新的 N 行
                    File.WriteAllLines(path, recentLogs.ToArray());
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.Log("[LogSyncMod] Error writing to log file: " + ex.Message);
                }
            }
        }
    }
}
