using System;
using System.Collections.Generic;
using System.Linq;

namespace LogSyncMod.AutoTest
{
    public sealed class AutoTestAssertions
    {
        private readonly AutoTestEvidenceBuilder evidence;

        public AutoTestAssertions(AutoTestEvidenceBuilder evidence) { this.evidence = evidence ?? throw new ArgumentNullException("evidence"); }

        public AutoTestAssertionEvidence Equal(string name, object expected, object actual, string details = null)
        {
            return Add(name, Equals(expected, actual), expected, actual, details);
        }

        public AutoTestAssertionEvidence True(string name, bool actual, string expectedDescription, string actualDescription, string details = null)
        {
            return Add(name, actual, expectedDescription ?? "true", actualDescription ?? actual.ToString(), details);
        }

        public AutoTestAssertionEvidence TargetExists(string name, AutoTestTargetRef target, string details = null)
        {
            return Add(name, target != null && !string.IsNullOrWhiteSpace(target.Id), "stable target id", target == null ? "missing" : target.Id, details);
        }

        public AutoTestAssertionEvidence ThingSpawned(string name, AutoTestThingSnapshot snapshot, string details = null)
        {
            return Add(name, snapshot != null && snapshot.Spawned && !snapshot.Destroyed, "spawned=true, destroyed=false", snapshot?.Summary ?? "missing snapshot", details);
        }

        public AutoTestAssertionEvidence ThingOnExpectedMap(string name, AutoTestThingSnapshot snapshot, int expectedMapId, string details = null)
        {
            return Add(name, snapshot != null && snapshot.MapId == expectedMapId, expectedMapId, snapshot == null ? "missing" : snapshot.MapId.ToString(), details);
        }

        public AutoTestAssertionEvidence WindowOpenForTarget(string name, AutoTestWindowSnapshot snapshot, string expectedDefName, string details = null)
        {
            bool success = snapshot != null && snapshot.IsOpen && (string.IsNullOrEmpty(expectedDefName) || string.Equals(snapshot.BoundDefName, expectedDefName, StringComparison.OrdinalIgnoreCase));
            return Add(name, success, "open window bound to " + expectedDefName, snapshot?.Summary ?? "missing window", details);
        }

        public AutoTestAssertionEvidence WindowClosed(string name, AutoTestWindowSnapshot snapshot, string details = null)
        {
            return Add(name, snapshot != null && !snapshot.IsOpen, "window closed", snapshot?.Summary ?? "missing window snapshot", details);
        }

        public AutoTestAssertionEvidence PawnHasHediff(string name, AutoTestPawnSnapshot snapshot, string defName, string details = null)
        {
            bool success = snapshot?.Hediffs != null && snapshot.Hediffs.Any(item => string.Equals(item.DefName, defName, StringComparison.OrdinalIgnoreCase));
            return Add(name, success, "hediff " + defName, snapshot == null ? "missing" : string.Join(",", snapshot.Hediffs.Select(item => item.DefName).ToArray()), details);
        }

        public AutoTestAssertionEvidence HealthDecreased(string name, AutoTestCombatSnapshot before, AutoTestCombatSnapshot after, string details = null)
        {
            bool success = before != null && after != null && (after.TargetHealthPercent < before.TargetHealthPercent || after.TargetHitPoints < before.TargetHitPoints || after.TargetDead || after.TargetDowned);
            return Add(name, success, "health or hit points decreased", "before=" + (before?.Summary ?? "missing") + "; after=" + (after?.Summary ?? "missing"), details);
        }

        public AutoTestAssertionEvidence ProjectileObserved(string name, AutoTestCombatSnapshot snapshot, bool required, string details = null)
        {
            bool success = !required || (snapshot != null && snapshot.ActiveProjectileCount > 0);
            return Add(name, success, required ? "projectile count > 0" : "not applicable", snapshot?.Summary ?? "missing", details);
        }

        public AutoTestAssertionEvidence Add(string name, bool success, object expected, object actual, string details = null)
        {
            return evidence.Assert(name, success, Convert.ToString(expected), Convert.ToString(actual), details);
        }
    }
}
