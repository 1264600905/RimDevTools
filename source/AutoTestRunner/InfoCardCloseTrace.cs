using System.Collections.Generic;
using System.Linq;
using Verse;

namespace LogSyncMod.AutoTest
{
    internal static class InfoCardCloseTrace
    {
        private static readonly List<string> reasons = new List<string>();

        private static Dialog_InfoCard trackedWindow;
        private static string trackedDefName;
        private static int sequence;

        public static bool IsTracking => trackedWindow != null || !string.IsNullOrEmpty(trackedDefName);

        public static void Begin(string defName)
        {
            reasons.Clear();
            trackedWindow = null;
            trackedDefName = defName ?? string.Empty;
            sequence = 0;
        }

        public static void Attach(Dialog_InfoCard window)
        {
            trackedWindow = window;
        }

        public static bool Matches(Window window)
        {
            return trackedWindow != null && ReferenceEquals(trackedWindow, window);
        }

        public static void AddReason(string reason)
        {
            if (!IsTracking || string.IsNullOrEmpty(reason)) return;

            sequence++;
            reasons.Add(sequence.ToString("D2") + ":" + reason);
            Log.Message("[LogSyncAutoTest] InfoCardTrace " + trackedDefName + " " + reasons[reasons.Count - 1]);
        }

        public static string Describe()
        {
            if (reasons.Count == 0) return "none";
            return string.Join(" | ", reasons.ToArray());
        }

        public static void Stop()
        {
            trackedWindow = null;
            trackedDefName = null;
            reasons.Clear();
            sequence = 0;
        }
    }
}
