using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace LogSyncMod.AutoTest
{
    public static class AutoTestSelectionFingerprint
    {
        public static string Create(IEnumerable<string> orderedTargetIds)
        {
            string canonical = string.Join("\n", (orderedTargetIds ?? Enumerable.Empty<string>()).Select(value => value ?? string.Empty).ToArray());
            return Hash(canonical);
        }

        public static string CreateGizmoId(string selectionFingerprint, int ordinal, string semanticValue)
        {
            string selection = NormalizeFingerprint(selectionFingerprint);
            string semantic = Hash(semanticValue ?? string.Empty);
            return "gizmo:" + selection + ":" + Math.Max(0, ordinal) + ":" + semantic;
        }

        public static bool TryReadSelectionFingerprint(string gizmoId, out string selectionFingerprint)
        {
            selectionFingerprint = null;
            if (string.IsNullOrEmpty(gizmoId) || !gizmoId.StartsWith("gizmo:", StringComparison.Ordinal)) return false;
            string[] parts = gizmoId.Split(':');
            if (parts.Length != 4 || parts[1].Length == 0 || !int.TryParse(parts[2], out int ordinal) || ordinal < 0) return false;
            selectionFingerprint = parts[1];
            return true;
        }

        public static bool MatchesSelection(string gizmoId, string currentSelectionFingerprint)
        {
            return TryReadSelectionFingerprint(gizmoId, out string embedded) &&
                string.Equals(embedded, NormalizeFingerprint(currentSelectionFingerprint), StringComparison.Ordinal);
        }

        private static string NormalizeFingerprint(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? Hash(string.Empty) : value.Trim().ToLowerInvariant();
        }

        private static string Hash(string value)
        {
            using (SHA256 algorithm = SHA256.Create())
            {
                byte[] bytes = algorithm.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty));
                return BitConverter.ToString(bytes, 0, 12).Replace("-", string.Empty).ToLowerInvariant();
            }
        }
    }
}
