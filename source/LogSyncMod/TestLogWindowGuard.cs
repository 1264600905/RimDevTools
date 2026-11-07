using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace LogSyncMod
{
    // Shared by CLI quick/manual/XML, ordinary auto and external scenarios. Logging is untouched.
    public static class TestLogWindowGuard
    {
        private static readonly Type LogWindowType = AccessTools.TypeByName("LudeonTK.EditWindow_Log");
        private static readonly FieldInfo AutoOpenField = AccessTools.Field("LudeonTK.EditWindow_Log:canAutoOpen");
        private static bool? originalAutoOpen;
        private static int externalSessions;
        public static bool Active => externalSessions > 0 || LogSyncMod.settings != null && (LogSyncMod.settings.testSessionActive || LogSyncMod.settings.autoTestEnabled);
        public static IDisposable RegisterExternalSession() { externalSessions++; return new SessionLease(); }
        private sealed class SessionLease : IDisposable
        {
            private bool disposed;
            public void Dispose() { if (!disposed) { disposed = true; externalSessions--; } }
        }
        public static bool IsLogWindow(Window window) => window != null && (LogWindowType?.IsInstanceOfType(window) == true || window.GetType().Name == "EditWindow_Log");

        public static void Maintain()
        {
            if (!Active)
            {
                if (originalAutoOpen.HasValue && AutoOpenField != null) AutoOpenField.SetValue(null, originalAutoOpen.Value);
                originalAutoOpen = null;
                return;
            }
            if (AutoOpenField != null)
            {
                if (!originalAutoOpen.HasValue) originalAutoOpen = (bool)AutoOpenField.GetValue(null);
                AutoOpenField.SetValue(null, false);
            }
            var stack = Find.WindowStack;
            if (stack == null) return;
            foreach (var window in stack.Windows.Where(IsLogWindow).ToList())
                if (stack.TryRemove(window, doCloseSound: false)) Log.Message("[LogSyncAutoTest] Automatically closed debug log window during test.");
        }
    }

    [HarmonyPatch(typeof(WindowStack), nameof(WindowStack.Add))]
    internal static class TestLogWindowAddGuardPatch
    {
        private static bool Prefix(Window window) => !TestLogWindowGuard.Active || !TestLogWindowGuard.IsLogWindow(window);
    }
}
