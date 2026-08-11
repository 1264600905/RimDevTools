using System;

namespace LogSyncMod.AutoTest
{
    internal sealed class AutoTestCombatDistanceRequest
    {
        public float VerbMinRange;
        public float VerbRange;
        public float MinimumSeparationMargin;
        public float MaximumRangeMargin;
        public float MinimumDistanceFloor = 2f;
        public float MapSafeMaximum;
        public float PreferredRangeFactor = 0.8f;
    }

    internal sealed class AutoTestCombatDistanceResult
    {
        public bool IsValid;
        public float MinValidDistance;
        public float MaxValidDistance;
        public float PreferredDistance;
        public string FailureReason;
    }

    internal static class AutoTestCombatDistancePolicy
    {
        public static AutoTestCombatDistanceResult Calculate(AutoTestCombatDistanceRequest request)
        {
            AutoTestCombatDistanceResult result = new AutoTestCombatDistanceResult();
            if (request == null)
            {
                result.FailureReason = "distance_request_missing";
                return result;
            }

            float verbRange = Math.Max(0f, request.VerbRange);
            float mapMaximum = Math.Max(0f, request.MapSafeMaximum);
            float minimumMargin = Math.Max(0f, request.MinimumSeparationMargin);
            float maximumMargin = Math.Max(0f, request.MaximumRangeMargin);
            result.MinValidDistance = Math.Max(
                Math.Max(0f, request.MinimumDistanceFloor),
                Math.Max(0f, request.VerbMinRange) + minimumMargin);
            result.MaxValidDistance = Math.Min(verbRange - maximumMargin, mapMaximum);

            if (verbRange <= 0f)
            {
                result.FailureReason = "verb_range_not_positive";
                return result;
            }

            if (mapMaximum <= 0f)
            {
                result.FailureReason = "map_safe_maximum_not_positive";
                return result;
            }

            if (result.MaxValidDistance < result.MinValidDistance)
            {
                result.FailureReason = "no_legal_distance_interval";
                return result;
            }

            float preferredFactor = request.PreferredRangeFactor;
            if (preferredFactor <= 0f || preferredFactor > 1f)
            {
                preferredFactor = 0.8f;
            }

            float preferred = verbRange * preferredFactor;
            result.PreferredDistance = Math.Max(result.MinValidDistance, Math.Min(preferred, result.MaxValidDistance));
            result.IsValid = true;
            return result;
        }
    }
}
