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
    // 拦截 Log 的所有主要方法
    [HarmonyPatch(typeof(Log))]
    public static class LogPatches
    {
        private sealed class QueuedLogEntry
        {
            public QueuedLogEntry(string dedupeKey, string outputLine)
            {
                DedupeKey = dedupeKey;
                OutputLine = outputLine;
            }

            public string DedupeKey { get; }
            public string OutputLine { get; }
        }

        private static readonly Queue<QueuedLogEntry> recentLogs = new Queue<QueuedLogEntry>();
        private static readonly HashSet<string> processedLogs = new HashSet<string>();
        private static readonly object fileLock = new object();
        private const int MaxLogLines = 500;

        // 拦截所有 Message 的重载
        [HarmonyPatch(nameof(Log.Message), new Type[] { typeof(string) })]
        [HarmonyPrefix]
        public static void MessagePrefix(string text) => ProcessLog(text, "Message");

        // 拦截所有 Warning 的重载
        [HarmonyPatch(nameof(Log.Warning), new Type[] { typeof(string) })]
        [HarmonyPrefix]
        public static void WarningPrefix(string text) => ProcessLog(text, "Warning");

        // 拦截所有 Error 的重载
        [HarmonyPatch(nameof(Log.Error), new Type[] { typeof(string) })]
        [HarmonyPrefix]
        public static void ErrorPrefix(string text) => ProcessLog(text, "Error");

        private static void ProcessLog(string text, string type)
        {
            if (string.IsNullOrEmpty(text)) return;

            // 检查设置开关
            if (type == "Message" && !LogSyncMod.settings.syncMessage) return;
            if (type == "Warning" && !LogSyncMod.settings.syncWarning) return;
            if (type == "Error" && !LogSyncMod.settings.syncError) return;

            // 1. 检查关键词
            bool keywordMatched = false;
            if (LogSyncMod.settings.keywords != null && LogSyncMod.settings.keywords.Count > 0)
            {
                foreach (var keyword in LogSyncMod.settings.keywords)
                {
                    if (!string.IsNullOrEmpty(keyword) && text.Contains(keyword))
                    {
                        keywordMatched = true;
                        break;
                    }
                }
            }

            // 2. 识别模组来源
            string sourceMod = GetSourceModPackageId();
            string sourceModLower = sourceMod?.ToLower();
            
            // 3. 过滤逻辑：
            // 如果没设关键词，也没设目标ID，则记录所有符合级别的
            // 如果设了目标ID，则必须匹配
            // 如果设了关键词，匹配关键词也记录
            bool modMatched = false;
            if (LogSyncMod.settings.targetModIds == null || LogSyncMod.settings.targetModIds.Count == 0)
            {
                modMatched = true; // 记录所有
            }
            else
            {
                modMatched = !string.IsNullOrEmpty(sourceModLower) && LogSyncMod.settings.targetModIds.Contains(sourceModLower);
            }

            if (keywordMatched || modMatched)
            {
                WriteToFile(text, type, sourceMod);
            }
        }

        public static void InitializeFile()
        {
            lock (fileLock)
            {
                try
                {
                    recentLogs.Clear();
                    processedLogs.Clear();

                    string path = LogSyncMod.settings.logPath;
                    if (string.IsNullOrEmpty(path)) return;

                    string directory = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    File.WriteAllText(path, $"--- Log Sync Initialized at {DateTime.Now} ---" + Environment.NewLine);
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.Log("[LogSyncMod] Failed to init file: " + ex.Message);
                }
            }
        }

        private static string GetSourceModPackageId()
        {
            StackTrace stackTrace = new StackTrace();
            for (int i = 2; i < stackTrace.FrameCount; i++)
            {
                MethodBase method = stackTrace.GetFrame(i).GetMethod();
                if (method == null) continue;
                
                Assembly assembly = method.DeclaringType?.Assembly;
                if (assembly == null) continue;

                if (assembly == typeof(Log).Assembly || 
                    assembly == typeof(Harmony).Assembly || 
                    assembly == typeof(LogSyncMod).Assembly) 
                    continue;

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
            string idTag = string.IsNullOrEmpty(sourceMod) ? "Unknown" : sourceMod;
            string dedupeKey = $"[{idTag}] [{type}] {text}";
            string outputLine = $"[{idTag}] [{type}] [{DateTime.Now:HH:mm:ss.fff}] {text}";

            EmitDebug($"Checking log: {dedupeKey}");

            lock (fileLock)
            {
                try
                {
                    string path = LogSyncMod.settings.logPath;
                    if (string.IsNullOrEmpty(path)) return;

                    if (processedLogs.Contains(dedupeKey))
                    {
                        EmitDebug("-> Result: DUPLICATE. Skipping.");
                        return;
                    }

                    EmitDebug("-> Result: UNIQUE. Writing to file.");

                    recentLogs.Enqueue(new QueuedLogEntry(dedupeKey, outputLine));
                    processedLogs.Add(dedupeKey);

                    while (recentLogs.Count > MaxLogLines)
                    {
                        QueuedLogEntry removed = recentLogs.Dequeue();
                        processedLogs.Remove(removed.DedupeKey);
                    }

                    File.WriteAllLines(path, recentLogs.Select(entry => entry.OutputLine).ToArray());
                    EmitDebug($"-> Write successful. Queue size: {recentLogs.Count}, HashSet size: {processedLogs.Count}");
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.Log("[LogSyncMod] Write Error: " + ex.Message);
                }
            }
        }

        private static void EmitDebug(string message)
        {
            if (LogSyncMod.settings == null || !LogSyncMod.settings.enableVerboseDebug) return;
            UnityEngine.Debug.Log("[LogSyncDebug] " + message);
        }
    }
}
