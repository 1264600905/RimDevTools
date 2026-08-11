using System;
using RimWorld;

namespace LogSyncMod.AutoTest
{
    internal static class AutoTestAbilityProbe
    {
        public static AutoTestAbilitySnapshot Capture(Ability ability)
        {
            if (ability == null) return new AutoTestAbilitySnapshot { Disabled = true, DisabledReason = "ability missing", TargetingSummary = "missing" };
            bool disabled = true;
            string reason = string.Empty;
            try { disabled = ability.GizmoDisabled(out reason); } catch (Exception ex) { reason = ex.Message; }
            bool available = false;
            try { available = ability.CanCast; } catch { }
            AutoTestAbilitySnapshot result = new AutoTestAbilitySnapshot
            {
                DefName = ability.def?.defName ?? string.Empty,
                Available = available,
                Disabled = disabled,
                DisabledReason = reason ?? string.Empty,
                CooldownTicks = ReadNullableInt(ability, "CooldownTicksRemaining", "cooldownTicksRemaining", "cooldownTicks"),
                Charges = ReadNullableInt(ability, "RemainingCharges", "Charges", "charges", "remainingCharges")
            };
            object targetParams = ability.verb?.targetParams;
            result.TargetingSummary = targetParams == null ? "targetParams=none" :
                "pawns=" + ReadBool(targetParams, "canTargetPawns") +
                " locations=" + ReadBool(targetParams, "canTargetLocations") +
                " buildings=" + ReadBool(targetParams, "canTargetBuildings") +
                " hostile=" + (ability.def?.hostile ?? false);
            return result;
        }

        private static int? ReadNullableInt(object instance, params string[] names)
        {
            object value = AutoTestPawnProbe.ReadRawMember(instance, names);
            if (value == null) return null;
            try { return Convert.ToInt32(value); } catch { return null; }
        }

        private static bool ReadBool(object instance, params string[] names)
        {
            object value = AutoTestPawnProbe.ReadRawMember(instance, names);
            try { return value != null && Convert.ToBoolean(value); } catch { return false; }
        }
    }
}
