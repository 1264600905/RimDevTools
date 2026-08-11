using System;
using System.Collections.Generic;

namespace LogSyncMod.AutoTest
{
    public static class AutoTestResultStatus
    {
        public const string Passed = "passed";
        public const string PassedAfterRetry = "passed_after_retry";
        public const string Failed = "failed";
        public const string Skipped = "skipped";
        public const string Unsupported = "unsupported";
        public const string CompatibilityWarning = "compatibility_warning";

        private static readonly HashSet<string> KnownStatuses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Passed,
            PassedAfterRetry,
            Failed,
            Skipped,
            Unsupported,
            CompatibilityWarning
        };

        public static bool IsKnown(string status)
        {
            return !string.IsNullOrWhiteSpace(status) && KnownStatuses.Contains(status);
        }

        public static bool IsFailure(string status)
        {
            return string.Equals(Canonicalize(status), Failed, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsSuccess(string status)
        {
            string canonical = Canonicalize(status);
            return string.Equals(canonical, Passed, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(canonical, PassedAfterRetry, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(canonical, Skipped, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(canonical, Unsupported, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(canonical, CompatibilityWarning, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsPassed(string status)
        {
            string canonical = Canonicalize(status);
            return string.Equals(canonical, Passed, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(canonical, PassedAfterRetry, StringComparison.OrdinalIgnoreCase);
        }

        public static bool RequiresReason(string status)
        {
            string canonical = Canonicalize(status);
            return !string.Equals(canonical, Passed, StringComparison.OrdinalIgnoreCase);
        }

        public static string Canonicalize(string status)
        {
            if (string.IsNullOrWhiteSpace(status)) return Failed;
            string normalized = status.Trim().ToLowerInvariant();
            return KnownStatuses.Contains(normalized) ? normalized : Failed;
        }
    }
}
