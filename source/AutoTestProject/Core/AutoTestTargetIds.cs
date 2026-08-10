using System;

namespace LogSyncMod.AutoTest
{
    public static class AutoTestTargetIds
    {
        public static string Map(int uniqueId) { return "map:" + uniqueId; }
        public static string Thing(int thingId) { return "thing:" + thingId; }
        public static string Pawn(int thingId) { return "pawn:" + thingId; }
        public static string Window(string captureId, int ordinal, string typeFingerprint)
        {
            return "window:" + Escape(captureId) + ":" + Math.Max(0, ordinal) + ":" + Escape(typeFingerprint);
        }
        public static string Selection(string fingerprint) { return "selection:" + Escape(fingerprint); }
        public static string Case(string caseId) { return "case:" + Escape(caseId); }
        public static string Operation(string operationId) { return "operation:" + Escape(operationId); }
        public static string Capture(string captureId) { return "capture:" + Escape(captureId); }

        public static bool TryParseMap(string id, out int uniqueId) { return TryParseInt(id, "map:", out uniqueId); }
        public static bool TryParseThing(string id, out int thingId) { return TryParseInt(id, "thing:", out thingId); }
        public static bool TryParsePawn(string id, out int thingId) { return TryParseInt(id, "pawn:", out thingId); }

        public static bool TryParseWindow(string id, out string captureId, out int ordinal, out string typeFingerprint)
        {
            captureId = null;
            ordinal = -1;
            typeFingerprint = null;
            if (string.IsNullOrEmpty(id) || !id.StartsWith("window:", StringComparison.Ordinal)) return false;
            string[] parts = id.Split(':');
            if (parts.Length != 4 || !int.TryParse(parts[2], out ordinal) || ordinal < 0) return false;
            captureId = Unescape(parts[1]);
            typeFingerprint = Unescape(parts[3]);
            return captureId.Length > 0 && typeFingerprint.Length > 0;
        }

        private static bool TryParseInt(string id, string prefix, out int value)
        {
            value = 0;
            return !string.IsNullOrEmpty(id) && id.StartsWith(prefix, StringComparison.Ordinal) &&
                int.TryParse(id.Substring(prefix.Length), out value) && value >= 0;
        }

        private static string Escape(string value)
        {
            return Uri.EscapeDataString(value ?? string.Empty).Replace("%3A", "%3a");
        }

        private static string Unescape(string value) { return Uri.UnescapeDataString(value ?? string.Empty); }
    }
}
