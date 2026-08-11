using System;
using System.Linq;
using System.Reflection;
using Verse;

namespace LogSyncMod.AutoTest
{
    internal static class AutoTestReadinessProbe
    {
        public static AutoTestReadinessSnapshot Capture()
        {
            AutoTestReadinessInput input = new AutoTestReadinessInput();
            try { input.HasCurrentGame = Current.Game != null; } catch { }
            try { input.MapCount = Find.Maps == null ? 0 : Find.Maps.Count; } catch { }
            try { input.HasCurrentMap = Find.CurrentMap != null; } catch { }
            try { input.ProgramState = Current.ProgramState.ToString(); } catch { input.ProgramState = "Unknown"; }
            try { input.LongEventPending = ReadLongEventPending(); } catch { input.LongEventPending = true; }
            try { input.TickManagerReady = Find.TickManager != null; } catch { }
            try { input.WindowStackReady = Find.WindowStack != null; } catch { }
            try { input.SelectorReady = Find.Selector != null; } catch { }
            try { input.ScreenFadeClear = ReadScreenFadeClear(); } catch { input.ScreenFadeClear = false; }
            return AutoTestReadinessEvaluator.Evaluate(input);
        }

        private static bool ReadLongEventPending()
        {
            PropertyInfo property = typeof(LongEventHandler).GetProperty("AnyEventNow", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (property != null && property.PropertyType == typeof(bool)) return (bool)property.GetValue(null, null);
            FieldInfo field = typeof(LongEventHandler).GetField("currentEvent", BindingFlags.NonPublic | BindingFlags.Static);
            return field != null && field.GetValue(null) != null;
        }

        private static bool ReadScreenFadeClear()
        {
            if (Find.WindowStack?.Windows != null && Find.WindowStack.Windows.Any(window =>
                window != null && (window.GetType().Name.IndexOf("Fade", StringComparison.OrdinalIgnoreCase) >= 0 ||
                window.GetType().Name.IndexOf("Fader", StringComparison.OrdinalIgnoreCase) >= 0))) return false;

            Type faderType = typeof(Current).Assembly.GetType("Verse.ScreenFader");
            if (faderType == null) return true;
            foreach (string memberName in new[] { "IsFading", "Fading", "IsBlack" })
            {
                PropertyInfo property = faderType.GetProperty(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (property != null && property.PropertyType == typeof(bool) && (bool)property.GetValue(null, null)) return false;
            }
            return true;
        }
    }
}
