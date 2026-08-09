using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Verse;

namespace LogSyncMod.AutoTest
{
    public static class AutoTestRuntimeObservation
    {
        public static AutoTestEnvironmentEvidence Capture(string packageId, Map map)
        {
            List<string> mods = LoadedModManager.RunningMods
                .Select(mod => mod.PackageIdPlayerFacing ?? string.Empty)
                .Where(value => !string.IsNullOrEmpty(value))
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new AutoTestEnvironmentEvidence
            {
                RimWorldVersion = ReadStaticValue(typeof(Log).Assembly.GetType("RimWorld.VersionControl"), "CurrentVersionString"),
                AutoTestAssemblyVersion = typeof(AutoTestRuntimeObservation).Assembly.GetName().Version.ToString(),
                LogSyncAssemblyVersion = typeof(global::LogSyncMod.LogSyncMod).Assembly.GetName().Version.ToString(),
                PackageId = packageId,
                MapId = ReadInstanceValue(map, "uniqueID"),
                MapName = map == null ? string.Empty : (map.info?.parent?.LabelCap ?? map.ToString()),
                Language = ReadStaticValue(typeof(LanguageDatabase), "activeLanguage"),
                DevMode = Prefs.DevMode,
                TimeSpeed = Find.TickManager == null ? string.Empty : Find.TickManager.CurTimeSpeed.ToString(),
                Paused = Find.TickManager != null && Find.TickManager.Paused,
                ActiveModPackageIds = mods,
                ActiveModsHash = Hash(string.Join("\n", mods.ToArray()))
            };
        }

        private static string ReadStaticValue(Type type, string name)
        {
            if (type == null) return string.Empty;
            try
            {
                PropertyInfo property = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (property != null) return Convert.ToString(property.GetValue(null, null));
                FieldInfo field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                return field == null ? string.Empty : Convert.ToString(field.GetValue(null));
            }
            catch { return string.Empty; }
        }

        private static string ReadInstanceValue(object instance, string name)
        {
            if (instance == null) return string.Empty;
            try
            {
                Type type = instance.GetType();
                PropertyInfo property = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (property != null) return Convert.ToString(property.GetValue(instance, null));
                FieldInfo field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                return field == null ? instance.GetHashCode().ToString() : Convert.ToString(field.GetValue(instance));
            }
            catch { return instance.GetHashCode().ToString(); }
        }

        private static string Hash(string value)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty));
                return string.Concat(bytes.Select(item => item.ToString("x2")).ToArray());
            }
        }
    }
}
