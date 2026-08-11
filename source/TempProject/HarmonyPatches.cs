using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Diagnostics;
using System.Text.RegularExpressions;
using HarmonyLib;
using Verse;

namespace LogSyncMod
{
    // 拦截 Log 的所有主要方法
    [HarmonyPatch(typeof(Log))]
    public static class LogPatches
    {
        public static event Action<LogSyncCapturedEntry> LogCaptured;

        [ThreadStatic]
        private static bool publishingCapturedLog;

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

        private sealed class PendingSourceReference
        {
            public PendingSourceReference(string idTag, string logType, string dedupeKey, string text, string timestampText)
            {
                IdTag = idTag;
                LogType = logType;
                DedupeKey = dedupeKey;
                Text = text;
                TimestampText = timestampText;
            }

            public string IdTag { get; }
            public string LogType { get; }
            public string DedupeKey { get; }
            public string Text { get; }
            public string TimestampText { get; }
        }

        private static readonly Queue<QueuedLogEntry> recentLogs = new Queue<QueuedLogEntry>();
        private static readonly HashSet<string> processedLogs = new HashSet<string>();
        private static readonly List<QueuedLogEntry> overflowLogs = new List<QueuedLogEntry>();
        private static readonly HashSet<string> overflowLogKeys = new HashSet<string>();
        private static readonly object fileLock = new object();
        private static readonly object xmlIndexLock = new object();
        private static readonly object assemblyLookupLock = new object();
        private static readonly Dictionary<string, string> defNameToXmlPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> ambiguousDefNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Assembly> stackCallAssemblyCache = new Dictionary<string, Assembly>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Assembly> typeAssemblyCache = new Dictionary<string, Assembly>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Type> resolvedTypeCache = new Dictionary<string, Type>(StringComparer.Ordinal);
        private static PendingSourceReference pendingSourceReference;
        private static bool xmlIndexBuilt;
        private static bool hasWrittenOverflowMarker;

        private static readonly Regex ExceptionLoadingDefRegex = new Regex(@"Exception loading def from file\s+(?<path>[^:\r\n]+?\.xml)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex DllLoadRegex = new Regex(@"could not load library\s+(?<path>\S+?\.dll)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex FilePrefixRegex = new Regex(@"^\s*file:\s*(?<path>.+?\.(xml|dll))\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex StandalonePathRegex = new Regex(@"^\s*(?<path>.+?\.(xml|dll))\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex XmlDefNameRegex = new Regex(@"<defName>\s*(?<name>[^<]+)\s*</defName>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex ConfigErrorRegex = new Regex(@"Config error in\s+(?<name>[^:\r\n]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex StackTraceLineRegex = new Regex(@"^\s*at\s+(?<call>.+?)\s*\(", RegexOptions.Compiled);

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
            bool captureRequested = !publishingCapturedLog && LogCaptured != null;
            bool controlSignal = IsControlSignal(text);
            bool pendingSourceCandidate = pendingSourceReference != null && IsPotentialSourceReferenceLine(text);

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

            // 检查设置开关。普通 Message 默认可禁用，但关键词命中的 Message 仍允许进入同步流程。
            bool synchronizationRequested = true;
            if (type == "Message" && !LogSyncMod.settings.syncMessage && !controlSignal && !keywordMatched && !pendingSourceCandidate) synchronizationRequested = false;
            if (type == "Warning" && !LogSyncMod.settings.syncWarning) synchronizationRequested = false;
            if (type == "Error" && !LogSyncMod.settings.syncError) synchronizationRequested = false;
            if (!captureRequested && !synchronizationRequested) return;

            string sourceMod = GetSourceModPackageId();
            if (captureRequested) PublishCapturedLog(text, type, sourceMod);
            if (!synchronizationRequested) return;

            // 2. 识别模组来源
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

            if (controlSignal || pendingSourceCandidate || keywordMatched || modMatched)
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
                    overflowLogs.Clear();
                    overflowLogKeys.Clear();
                    pendingSourceReference = null;
                    hasWrittenOverflowMarker = false;
                    xmlIndexBuilt = false;
                    defNameToXmlPath.Clear();
                    ambiguousDefNames.Clear();
                    stackCallAssemblyCache.Clear();
                    typeAssemblyCache.Clear();
                    resolvedTypeCache.Clear();

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
            string timestampText = DateTime.Now.ToString("HH:mm:ss.fff");

            EmitDebug($"Checking log: {dedupeKey}");

            lock (fileLock)
            {
                try
                {
                    string path = LogSyncMod.settings.logPath;
                    if (string.IsNullOrEmpty(path)) return;

                    if (TryConsumePendingSourceReference(text, out string pendingSourceMarker))
                    {
                        EmitDebug("-> Consumed pending source reference.");
                        EnqueueResolvedLog(
                            pendingSourceReference.IdTag,
                            pendingSourceReference.LogType,
                            pendingSourceReference.DedupeKey,
                            pendingSourceReference.Text,
                            pendingSourceReference.TimestampText,
                            pendingSourceMarker
                        );
                        pendingSourceReference = null;
                        PersistLogFile(path);
                        return;
                    }

                    if (pendingSourceReference != null)
                    {
                        EmitDebug("-> Pending source reference not resolved. Flushing original line.");
                        EnqueueResolvedLog(
                            pendingSourceReference.IdTag,
                            pendingSourceReference.LogType,
                            pendingSourceReference.DedupeKey,
                            pendingSourceReference.Text,
                            pendingSourceReference.TimestampText,
                            null
                        );
                        pendingSourceReference = null;
                    }

                    if (processedLogs.Contains(dedupeKey))
                    {
                        EmitDebug("-> Result: DUPLICATE. Skipping.");
                        return;
                    }

                    EmitDebug("-> Result: UNIQUE. Writing to file.");

                    if (TryExtractSourceReference(text, out string sourceMarker))
                    {
                        EmitDebug("-> Source reference found.");
                        EnqueueResolvedLog(idTag, type, dedupeKey, text, timestampText, sourceMarker);
                    }
                    else if (ShouldAwaitSourceReference(type, text))
                    {
                        EmitDebug("-> Awaiting follow-up source reference.");
                        pendingSourceReference = new PendingSourceReference(idTag, type, dedupeKey, text, timestampText);
                    }
                    else
                    {
                        EnqueueResolvedLog(idTag, type, dedupeKey, text, timestampText, null);
                    }

                    PersistLogFile(path);
                    EmitDebug($"-> Write successful. Queue size: {recentLogs.Count}, HashSet size: {processedLogs.Count}");
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.Log("[LogSyncMod] Write Error: " + ex.Message);
                }
            }
        }

        private static void PublishCapturedLog(string text, string type, string sourceMod)
        {
            if (publishingCapturedLog) return;
            Action<LogSyncCapturedEntry> handlers = LogCaptured;
            if (handlers == null) return;

            publishingCapturedLog = true;
            try
            {
                LogSyncCapturedEntry entry = new LogSyncCapturedEntry
                {
                    TimestampUtc = DateTime.UtcNow,
                    Level = type,
                    Text = text,
                    SourcePackageId = sourceMod
                };

                foreach (Action<LogSyncCapturedEntry> handler in handlers.GetInvocationList())
                {
                    try { handler(entry); }
                    catch (Exception ex) { UnityEngine.Debug.Log("[LogSyncMod] LogCaptured subscriber failed: " + ex.Message); }
                }
            }
            finally
            {
                publishingCapturedLog = false;
            }
        }

        private static bool ShouldAwaitSourceReference(string type, string text)
        {
            if (type != "Warning" && type != "Error") return false;

            return text.IndexOf("[Def Error]", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf("Patch operation", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf("Exception loading def", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf("XML error:", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool TryExtractSourceReference(string text, out string sourceMarker)
        {
            if (TryBuildSourceMarker(ExceptionLoadingDefRegex.Match(text), out sourceMarker)) return true;
            if (TryBuildSourceMarker(DllLoadRegex.Match(text), out sourceMarker)) return true;
            if (TryBuildSourceMarker(FilePrefixRegex.Match(text), out sourceMarker)) return true;
            if (TryResolveXmlPathFromText(text, out string xmlPath))
            {
                sourceMarker = "[SourceFile] " + xmlPath;
                return true;
            }
            if (TryResolveAssemblyPathFromText(text, out string dllPath))
            {
                sourceMarker = "[SourceDll] " + dllPath;
                return true;
            }

            sourceMarker = null;
            return false;
        }

        private static bool TryConsumePendingSourceReference(string text, out string sourceMarker)
        {
            if (pendingSourceReference == null)
            {
                sourceMarker = null;
                return false;
            }

            if (TryBuildSourceMarker(FilePrefixRegex.Match(text), out sourceMarker)) return true;
            if (TryBuildSourceMarker(StandalonePathRegex.Match(text), out sourceMarker)) return true;

            sourceMarker = null;
            return false;
        }

        private static bool IsPotentialSourceReferenceLine(string text)
        {
            return FilePrefixRegex.IsMatch(text) || StandalonePathRegex.IsMatch(text);
        }

        private static bool TryResolveXmlPathFromText(string text, out string path)
        {
            string defName = TryExtractDefNameFromText(text);
            if (string.IsNullOrEmpty(defName))
            {
                path = null;
                return false;
            }

            EnsureXmlIndexBuilt();
            return defNameToXmlPath.TryGetValue(defName, out path);
        }

        private static string TryExtractDefNameFromText(string text)
        {
            Match xmlMatch = XmlDefNameRegex.Match(text);
            if (xmlMatch.Success)
            {
                return xmlMatch.Groups["name"].Value.Trim();
            }

            Match configMatch = ConfigErrorRegex.Match(text);
            if (configMatch.Success)
            {
                return configMatch.Groups["name"].Value.Trim();
            }

            return null;
        }

        private static bool TryResolveAssemblyPathFromText(string text, out string assemblyPath)
        {
            foreach (string line in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!TryExtractStackCall(line, out string stackCall)) continue;

                if (TryResolveAssemblyForStackCall(stackCall, out Assembly assembly))
                {
                    string location = NormalizePath(assembly.Location);
                    if (!string.IsNullOrEmpty(location))
                    {
                        assemblyPath = location;
                        return true;
                    }
                }
            }

            assemblyPath = null;
            return false;
        }

        private static bool TryExtractStackCall(string line, out string stackCall)
        {
            Match match = StackTraceLineRegex.Match(line);
            if (!match.Success)
            {
                stackCall = null;
                return false;
            }

            stackCall = match.Groups["call"].Value.Trim();
            if (string.IsNullOrEmpty(stackCall) ||
                stackCall.StartsWith("wrapper ", StringComparison.OrdinalIgnoreCase) ||
                stackCall.StartsWith("(wrapper ", StringComparison.OrdinalIgnoreCase))
            {
                stackCall = null;
                return false;
            }

            int lastDot = stackCall.LastIndexOf('.');
            if (lastDot <= 0)
            {
                stackCall = null;
                return false;
            }

            string typeName = stackCall.Substring(0, lastDot);
            if (typeName.StartsWith("System.", StringComparison.Ordinal) ||
                typeName.StartsWith("Verse.", StringComparison.Ordinal) ||
                typeName.StartsWith("RimWorld.", StringComparison.Ordinal) ||
                typeName.StartsWith("UnityEngine.", StringComparison.Ordinal) ||
                typeName.StartsWith("HarmonyLib.", StringComparison.Ordinal) ||
                typeName.StartsWith("Mono.", StringComparison.Ordinal))
            {
                stackCall = null;
                return false;
            }

            return true;
        }

        // 参考 VisualExceptions 的思路，先恢复具体栈帧方法，再回到程序集和模组。
        private static bool TryResolveAssemblyForStackCall(string stackCall, out Assembly assembly)
        {
            lock (assemblyLookupLock)
            {
                if (stackCallAssemblyCache.TryGetValue(stackCall, out assembly))
                {
                    return assembly != null;
                }
            }

            int lastDot = stackCall.LastIndexOf('.');
            if (lastDot <= 0)
            {
                CacheStackCallAssembly(stackCall, null);
                assembly = null;
                return false;
            }

            string typeName = stackCall.Substring(0, lastDot);
            string methodName = stackCall.Substring(lastDot + 1);

            foreach (string candidateTypeName in GetCandidateTypeNames(typeName))
            {
                if (!TryResolveType(candidateTypeName, out Type resolvedType)) continue;

                MethodBase method = TryResolveMethodOnType(resolvedType, methodName);
                assembly = method?.DeclaringType?.Assembly ?? resolvedType.Assembly;
                CacheStackCallAssembly(stackCall, assembly);
                return assembly != null;
            }

            if (TryResolveAssemblyForType(typeName, out assembly))
            {
                CacheStackCallAssembly(stackCall, assembly);
                return true;
            }

            CacheStackCallAssembly(stackCall, null);
            assembly = null;
            return false;
        }

        private static void CacheStackCallAssembly(string stackCall, Assembly assembly)
        {
            lock (assemblyLookupLock)
            {
                stackCallAssemblyCache[stackCall] = assembly;
            }
        }

        private static bool TryResolveAssemblyForType(string typeName, out Assembly assembly)
        {
            lock (assemblyLookupLock)
            {
                if (typeAssemblyCache.TryGetValue(typeName, out assembly))
                {
                    return assembly != null;
                }
            }

            foreach (ModContentPack mod in LoadedModManager.RunningMods)
            {
                if (mod.assemblies == null || mod.assemblies.loadedAssemblies == null) continue;

                foreach (Assembly loadedAssembly in mod.assemblies.loadedAssemblies)
                {
                    if (loadedAssembly == null) continue;

                    try
                    {
                        Type resolvedType = loadedAssembly.GetType(typeName, false, false);
                        if (resolvedType != null)
                        {
                            assembly = loadedAssembly;
                            lock (assemblyLookupLock)
                            {
                                typeAssemblyCache[typeName] = assembly;
                            }
                            return true;
                        }
                    }
                    catch
                    {
                    }
                }
            }

            lock (assemblyLookupLock)
            {
                typeAssemblyCache[typeName] = null;
            }
            assembly = null;
            return false;
        }

        private static bool TryResolveType(string typeName, out Type resolvedType)
        {
            lock (assemblyLookupLock)
            {
                if (resolvedTypeCache.TryGetValue(typeName, out resolvedType))
                {
                    return resolvedType != null;
                }
            }

            foreach (ModContentPack mod in LoadedModManager.RunningMods)
            {
                if (mod.assemblies == null || mod.assemblies.loadedAssemblies == null) continue;

                foreach (Assembly loadedAssembly in mod.assemblies.loadedAssemblies)
                {
                    if (loadedAssembly == null) continue;

                    try
                    {
                        resolvedType = loadedAssembly.GetType(typeName, false, false);
                        if (resolvedType == null)
                        {
                            resolvedType = FindCompatibleType(loadedAssembly, typeName);
                        }

                        if (resolvedType != null)
                        {
                            lock (assemblyLookupLock)
                            {
                                resolvedTypeCache[typeName] = resolvedType;
                                typeAssemblyCache[typeName] = loadedAssembly;
                            }
                            return true;
                        }
                    }
                    catch
                    {
                    }
                }
            }

            lock (assemblyLookupLock)
            {
                resolvedTypeCache[typeName] = null;
            }
            resolvedType = null;
            return false;
        }

        private static IEnumerable<string> GetCandidateTypeNames(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName)) yield break;

            yield return typeName;

            string stripped = StripGenericArguments(typeName);
            if (!string.Equals(stripped, typeName, StringComparison.Ordinal))
            {
                yield return stripped;
            }
        }

        private static string StripGenericArguments(string typeName)
        {
            int genericIndex = typeName.IndexOf('[');
            return genericIndex > 0 ? typeName.Substring(0, genericIndex) : typeName;
        }

        private static Type FindCompatibleType(Assembly assembly, string typeName)
        {
            string strippedName = StripGenericArguments(typeName);

            try
            {
                foreach (Type candidate in assembly.GetTypes())
                {
                    if (candidate == null || string.IsNullOrEmpty(candidate.FullName)) continue;

                    if (string.Equals(candidate.FullName, typeName, StringComparison.Ordinal) ||
                        string.Equals(candidate.FullName, strippedName, StringComparison.Ordinal) ||
                        string.Equals(StripGenericArguments(candidate.FullName), strippedName, StringComparison.Ordinal))
                    {
                        return candidate;
                    }
                }
            }
            catch (ReflectionTypeLoadException ex)
            {
                foreach (Type candidate in ex.Types)
                {
                    if (candidate == null || string.IsNullOrEmpty(candidate.FullName)) continue;

                    if (string.Equals(candidate.FullName, typeName, StringComparison.Ordinal) ||
                        string.Equals(candidate.FullName, strippedName, StringComparison.Ordinal) ||
                        string.Equals(StripGenericArguments(candidate.FullName), strippedName, StringComparison.Ordinal))
                    {
                        return candidate;
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private static MethodBase TryResolveMethodOnType(Type type, string methodName)
        {
            if (type == null || string.IsNullOrWhiteSpace(methodName)) return null;

            BindingFlags bindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
            if (methodName == ".ctor") return type.GetConstructors(bindingFlags).FirstOrDefault();
            if (methodName == ".cctor") return type.TypeInitializer;

            IEnumerable<MethodBase> candidates = type.GetMethods(bindingFlags).Cast<MethodBase>();
            MethodBase exact = candidates.FirstOrDefault(method => string.Equals(method.Name, methodName, StringComparison.Ordinal));
            if (exact != null) return exact;

            string strippedMethodName = StripGenericArguments(methodName);
            return candidates.FirstOrDefault(method => string.Equals(method.Name, strippedMethodName, StringComparison.Ordinal));
        }

        private static void EnsureXmlIndexBuilt()
        {
            if (xmlIndexBuilt) return;

            lock (xmlIndexLock)
            {
                if (xmlIndexBuilt) return;

                foreach (ModContentPack mod in LoadedModManager.RunningMods)
                {
                    try
                    {
                        string rootDir = GetModRootDirectory(mod);
                        if (string.IsNullOrEmpty(rootDir) || !Directory.Exists(rootDir)) continue;

                        foreach (string xmlFile in Directory.GetFiles(rootDir, "*.xml", SearchOption.AllDirectories))
                        {
                            IndexXmlFile(xmlFile);
                        }
                    }
                    catch (Exception ex)
                    {
                        EmitDebug("-> Failed to index XML files: " + ex.Message);
                    }
                }

                xmlIndexBuilt = true;
            }
        }

        private static string GetModRootDirectory(ModContentPack mod)
        {
            PropertyInfo rootDirProperty = mod.GetType().GetProperty("RootDir");
            if (rootDirProperty != null)
            {
                return rootDirProperty.GetValue(mod, null) as string;
            }

            return null;
        }

        private static void IndexXmlFile(string xmlFile)
        {
            string text;
            try
            {
                text = File.ReadAllText(xmlFile);
            }
            catch
            {
                return;
            }

            foreach (Match match in XmlDefNameRegex.Matches(text))
            {
                string defName = match.Groups["name"].Value.Trim();
                if (string.IsNullOrEmpty(defName) || ambiguousDefNames.Contains(defName)) continue;

                if (defNameToXmlPath.TryGetValue(defName, out string existingPath))
                {
                    if (!string.Equals(existingPath, xmlFile, StringComparison.OrdinalIgnoreCase))
                    {
                        defNameToXmlPath.Remove(defName);
                        ambiguousDefNames.Add(defName);
                    }
                }
                else
                {
                    defNameToXmlPath[defName] = xmlFile;
                }
            }
        }

        private static bool TryBuildSourceMarker(Match match, out string sourceMarker)
        {
            if (match.Success)
            {
                string path = match.Groups["path"].Value.Trim().Trim('"');
                string label = path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? "[SourceDll]" : "[SourceFile]";
                sourceMarker = $"{label} {path}";
                return true;
            }

            sourceMarker = null;
            return false;
        }

        private static void EnqueueResolvedLog(string idTag, string type, string dedupeKey, string text, string timestampText, string sourceMarker)
        {
            if (processedLogs.Contains(dedupeKey) || overflowLogKeys.Contains(dedupeKey)) return;

            string outputLine = BuildOutputLine(idTag, type, text, timestampText, sourceMarker);
            if (recentLogs.Count < GetLogMaxLines())
            {
                EnqueueLine(dedupeKey, outputLine);
                return;
            }

            if (type == "Error" || IsControlSignal(text))
            {
                AppendOverflowLine(dedupeKey, outputLine);
                return;
            }

            EmitDebug("-> Log limit reached. Dropping non-error line.");
        }

        private static bool TryResolveModLabelFromPath(string path, out string modLabel)
        {
            string normalizedPath = NormalizePath(path);
            if (string.IsNullOrEmpty(normalizedPath))
            {
                modLabel = null;
                return false;
            }

            foreach (ModContentPack mod in LoadedModManager.RunningMods)
            {
                string rootDir = NormalizePath(GetModRootDirectory(mod));
                if (string.IsNullOrEmpty(rootDir)) continue;

                if (normalizedPath.StartsWith(rootDir, StringComparison.OrdinalIgnoreCase))
                {
                    modLabel = $"{mod.Name} ({mod.PackageIdPlayerFacing})";
                    return true;
                }
            }

            modLabel = null;
            return false;
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            return path.Trim().Trim('"').Replace('/', '\\');
        }

        private static string BuildOutputLine(string idTag, string type, string text, string timestampText, string sourceMarker)
        {
            string prefix = $"[{idTag}] [{type}] [{timestampText}] ";
            string firstLine = text;
            string remainingText = "";

            int newlineIndex = text.IndexOfAny(new[] { '\r', '\n' });
            if (newlineIndex >= 0)
            {
                firstLine = text.Substring(0, newlineIndex);
                remainingText = text.Substring(newlineIndex).TrimStart('\r', '\n');
            }

            string outputLine = prefix + firstLine;
            if (string.IsNullOrEmpty(sourceMarker))
            {
                if (!string.IsNullOrEmpty(remainingText))
                    outputLine += Environment.NewLine + remainingText;
                return outputLine;
            }

            int separatorIndex = sourceMarker.IndexOf(' ');
            if (separatorIndex < 0 || separatorIndex >= sourceMarker.Length - 1)
            {
                if (!string.IsNullOrEmpty(remainingText))
                    outputLine += Environment.NewLine + remainingText;
                return outputLine;
            }

            string markerLabel = sourceMarker.Substring(1, separatorIndex - 2);
            string path = sourceMarker.Substring(separatorIndex + 1).Trim();
            outputLine += $" | {markerLabel}: {path}";

            if (TryResolveModLabelFromPath(path, out string modLabel))
            {
                outputLine += $" | SourceMod: {modLabel}";
            }

            if (!string.IsNullOrEmpty(remainingText))
            {
                outputLine += Environment.NewLine + remainingText;
            }

            return outputLine;
        }

        private static void EnqueueLine(string dedupeKey, string outputLine)
        {
            recentLogs.Enqueue(new QueuedLogEntry(dedupeKey, outputLine));
            processedLogs.Add(dedupeKey);
        }

        private static bool IsControlSignal(string text)
        {
            return text.StartsWith("[LogSyncAutoTest] COMPLETED", StringComparison.Ordinal) ||
                   text.StartsWith("[LogSyncAutoTest] HEARTBEAT", StringComparison.Ordinal) ||
                   text.StartsWith("[LogSyncMod] RUNTIME_READY_TO_CLOSE", StringComparison.Ordinal);
        }

        private static void AppendOverflowLine(string dedupeKey, string outputLine)
        {
            if (!hasWrittenOverflowMarker)
            {
                string marker = string.Format(
                    "--- Log line limit reached ({0}). Further Message/Warning logs are suppressed; Error and control logs continue below. ---",
                    GetLogMaxLines());
                overflowLogs.Add(new QueuedLogEntry("__overflow_marker__", marker));
                hasWrittenOverflowMarker = true;
            }

            overflowLogs.Add(new QueuedLogEntry(dedupeKey, outputLine));
            overflowLogKeys.Add(dedupeKey);
        }

        private static int GetLogMaxLines()
        {
            return LogSyncMod.settings?.GetLogMaxLines() ?? 500;
        }

        private static void PersistLogFile(string path)
        {
            File.WriteAllLines(path, recentLogs.Concat(overflowLogs).Select(entry => entry.OutputLine).ToArray());
        }

        private static void EmitDebug(string message)
        {
            if (LogSyncMod.settings == null || !LogSyncMod.settings.enableVerboseDebug) return;
            UnityEngine.Debug.Log("[LogSyncDebug] " + message);
        }
    }
}
