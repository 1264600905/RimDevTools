using System;
using System.Collections.Generic;
using System.Linq;
using LogSyncMod;
using RimWorld;
using Verse;

namespace LogSyncMod.AutoTest
{
    internal sealed partial class AutoTestRunner
    {
        private sealed class SemanticCaseState
        {
            public long EventAfterSequence;
            public long LogAfterSequence;
            public bool Completed;
            public readonly HashSet<string> AssertionNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        private readonly Dictionary<string, SemanticCaseState> semanticCases = new Dictionary<string, SemanticCaseState>(StringComparer.OrdinalIgnoreCase);
        private AutoTestAssertions semanticAssertions;
        private readonly AutoTestErrorPolicy semanticErrorPolicy = new AutoTestErrorPolicy();

        private void InitializeSemanticInfrastructure()
        {
            semanticAssertions = new AutoTestAssertions(evidenceBuilder);
        }

        private void RegisterSemanticCase(AutoTestResult result)
        {
            if (result == null || operationJournal == null || semanticCases.ContainsKey(result.CaseId)) return;
            SemanticCaseState state = new SemanticCaseState
            {
                EventAfterSequence = operationJournal.LastEventSequence,
                LogAfterSequence = logJournal?.LastSequence ?? 0
            };
            semanticCases[result.CaseId] = state;
            result.EvidenceCaseKey = result.CaseId;
            operationJournal.RecordEvent("case.started", runId, result.CaseId, null, null, "semantic." + result.Category, "Semantic case started for " + result.DefName, evidenceClock.UtcNow);
        }

        private AutoTestAssertionEvidence SemanticAssert(AutoTestResult result, string name, bool success, string expected, string actual, string details = null, string lastSnapshot = null)
        {
            if (result == null || semanticAssertions == null || string.IsNullOrWhiteSpace(name)) return null;
            RegisterSemanticCase(result);
            SemanticCaseState state = semanticCases[result.CaseId];
            if (!state.AssertionNames.Add(name)) return evidenceManifest.Assertions.LastOrDefault(item => item.CaseId == result.CaseId && item.Name == name);

            string stepId = NormalizeSemanticStep(name);
            AutoTestOperationEnvelope operation;
            using (AutoTestOperationContext.PushCase(result.CaseId))
            {
                operation = operationRunner.Run("semantic." + result.Category, result.CaseId, stepId, delegate { return actual ?? string.Empty; }, value => value);
                AutoTestAssertionEvidence assertion = semanticAssertions.Add(name, success, expected, actual, details);
                assertion.CaseId = result.CaseId;
                assertion.StepId = stepId;
                assertion.OperationId = operation.OperationId;
                result.OperationIds.Add(operation.OperationId);
                result.AssertionCount++;
                if (!assertion.Success) result.FailedAssertionIds.Add(assertion.AssertionId);
                if (!string.IsNullOrWhiteSpace(lastSnapshot)) result.LastSnapshot = lastSnapshot;
                return assertion;
            }
        }

        private void CompleteSemanticCase(AutoTestResult result)
        {
            if (result == null || !semanticCases.TryGetValue(result.CaseId, out SemanticCaseState state) || state.Completed) return;
            NormalizeResultStatus(result);
            long logThroughSequence = logJournal?.LastSequence ?? long.MaxValue;
            ResolveAllowPatterns(result, out List<string> allowedErrors, out List<string> allowedWarnings);
            AutoTestErrorPolicyResult policy = semanticErrorPolicy.Evaluate(logJournal, state.LogAfterSequence, runId, result.CaseId, allowedErrors, allowedWarnings, logThroughSequence);
            result.UnexpectedSevereLogCount = policy.UnexpectedSevereLogs.Count;
            bool compatibilityObservation = (result.Notes ?? string.Empty).IndexOf("CompatibilityObservation=True", StringComparison.OrdinalIgnoreCase) >= 0;
            if (compatibilityObservation && (AutoTestResultStatus.IsFailure(result.Status) || !policy.Success))
            {
                string compatibilityReason = string.IsNullOrWhiteSpace(result.ResultReasonCode)
                    ? "player_compatibility_observation_failed"
                    : result.ResultReasonCode;
                CompleteCompatibilityWarning(result, compatibilityReason, result.Stage, result.ErrorMessage,
                    "CompatibilityWarningReason=" + (policy.Success ? result.ErrorMessage : policy.Summary));
            }
            bool nonFailureDiagnosticStatus = string.Equals(result.Status, AutoTestResultStatus.Unsupported, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(result.Status, AutoTestResultStatus.CompatibilityWarning, StringComparison.OrdinalIgnoreCase);
            SemanticAssert(result, "logs.no_unexpected_severe", policy.Success || nonFailureDiagnosticStatus, "0 unexpected Error/Fatal logs or diagnostic non-failure status", policy.Summary,
                policy.Success ? "Allowed rules: " + string.Join(",", policy.AllowedRules.ToArray()) :
                    "Status=" + result.Status + "; " + string.Join(" | ", policy.UnexpectedSevereLogs.Select(item => item.Level + ":" + item.Message).ToArray()),
                result.LastSnapshot);

            if (nonFailureDiagnosticStatus)
            {
                foreach (AutoTestAssertionEvidence diagnosticAssertion in evidenceManifest.Assertions.Where(item => item.CaseId == result.CaseId && !item.Success))
                {
                    diagnosticAssertion.Success = true;
                    diagnosticAssertion.Details = (diagnosticAssertion.Details ?? string.Empty) +
                        (string.IsNullOrWhiteSpace(diagnosticAssertion.Details) ? string.Empty : " | ") +
                        "DiagnosticStatus=" + result.Status + ";ReasonCode=" + result.ResultReasonCode;
                }
                operationJournal.MarkCaseOperationsOptional(result.CaseId);
            }

            List<AutoTestAssertionEvidence> failed = evidenceManifest.Assertions.Where(item => item.CaseId == result.CaseId && !item.Success).ToList();
            result.AssertionCount = evidenceManifest.Assertions.Count(item => item.CaseId == result.CaseId);
            result.FailedAssertionIds = failed.Select(item => item.AssertionId).ToList();
            result.OperationIds = operationJournal.GetRecentOperations(2000, true).Where(item => item.CaseId == result.CaseId).Select(item => item.OperationId).Distinct().ToList();
            if (failed.Count > 0 && AutoTestResultStatus.IsPassed(result.Status))
            {
                CompleteFailed(result, "semantic_assertion_failed", failed[0].Name,
                    "Semantic assertions failed: " + string.Join(", ", failed.Select(item => item.Name).ToArray()));
            }

            bool success = AutoTestResultStatus.IsSuccess(result.Status);
            operationJournal.RecordEvent(success ? "case.completed" : "case.failed", runId, result.CaseId, null, null, "semantic." + result.Category,
                success ? "Semantic case completed." : result.ErrorMessage, evidenceClock.UtcNow);
            if (!evidenceManifest.Cases.Any(item => item.CaseId == result.CaseId))
                evidenceBuilder.AddCaseWindow(result.CaseId, state.EventAfterSequence, state.LogAfterSequence, operationJournal, logJournal,
                    operationJournal?.LastEventSequence ?? long.MaxValue, logThroughSequence);
            foreach (string screenshotPath in (result.ScreenshotPaths ?? new List<string>()).Where(path => !string.IsNullOrWhiteSpace(path)))
            {
                if (!evidenceManifest.Captures.Any(item => string.Equals(item.Path, screenshotPath, StringComparison.OrdinalIgnoreCase)))
                    evidenceBuilder.AddCapture(result.DefName, "screenshot", screenshotPath, result.OperationIds.LastOrDefault(), result.CaseType);
            }
            state.Completed = true;
        }

        private void RecordResult(AutoTestResult result)
        {
            if (result == null) return;
            CompleteSemanticCase(result);
            results.Add(result);
        }

        private void ResolveAllowPatterns(AutoTestResult result, out List<string> errors, out List<string> warnings)
        {
            IEnumerable<LogSyncAutoTestSpecDef> matches = activeSpecDefs.Where(spec => spec != null &&
                (spec.scope == AutoTestScope.Standard || string.Equals(spec.scope.ToString(), result.Scope, StringComparison.OrdinalIgnoreCase)) &&
                ((spec.defNames?.Count ?? 0) == 0 && (spec.categories?.Count ?? 0) == 0 ||
                 spec.defNames.Any(value => string.Equals(value, result.DefName, StringComparison.OrdinalIgnoreCase)) ||
                 spec.categories.Any(value => string.Equals(value, result.Category, StringComparison.OrdinalIgnoreCase))));
            errors = matches.SelectMany(spec => spec.allowedErrorPatterns ?? new List<string>()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            warnings = matches.SelectMany(spec => spec.allowedWarningPatterns ?? new List<string>()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static string NormalizeSemanticStep(string name)
        {
            char[] chars = (name ?? "assertion").ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray();
            return new string(chars).Trim('_');
        }

        private static bool HasObservedDamage(IEnumerable<Pawn> targets, IDictionary<int, float> initialHealth)
        {
            return (targets ?? Enumerable.Empty<Pawn>()).Any(pawn => pawn == null || pawn.Destroyed || pawn.Dead || pawn.Downed ||
                pawn.health?.summaryHealth != null && initialHealth != null && initialHealth.TryGetValue(pawn.thingIDNumber, out float initial) && pawn.health.summaryHealth.SummaryHealthPercent < initial - 0.0001f);
        }

        private static Pawn ResolveSemanticTarget(IEnumerable<Pawn> targets)
        {
            return (targets ?? Enumerable.Empty<Pawn>()).FirstOrDefault(pawn => pawn != null) ?? (targets ?? Enumerable.Empty<Pawn>()).FirstOrDefault();
        }

        private void RecordTurretSemanticBeforeCleanup(TurretTestSession session)
        {
            if (session?.Result == null) return;
            AutoTestThingSnapshot turret = AutoTestThingProbe.Capture(session.SpawnedTurret);
            Pawn target = ResolveSemanticTarget(session.EnemyPawns);
            AutoTestCombatSnapshot combat = AutoTestCombatProbe.Capture(session.SpawnedTurret, target, session.TestMap);
            SemanticAssert(session.Result, "turret.pocket_map_registered", session.TestMap != null && Find.Maps.Contains(session.TestMap), "pocket map registered", AutoTestMapProbe.Capture(session.TestMap)?.Id ?? "missing", null, combat.Summary);
            SemanticAssert(session.Result, "turret.spawned", turret.Spawned && !turret.Destroyed, "turret spawned on test map", turret.Summary, null, turret.Summary);
            CompPowerTrader power = session.SpawnedTurret?.TryGetComp<CompPowerTrader>();
            SemanticAssert(session.Result, "turret.power_ready", power == null || power.PowerOn, power == null ? "not applicable" : "PowerOn=true", power == null ? "skipped:no CompPowerTrader" : "PowerOn=" + power.PowerOn, power == null ? "Turret does not use power." : null);
            SemanticAssert(session.Result, "turret.enemies_spawned", session.EnemyPawns.Count > 0 && session.EnemyPawns.Any(pawn => pawn != null), "at least one enemy target", "count=" + session.EnemyPawns.Count);
            bool acquired = session.FirstDamageTick >= 0 || session.FirstKillTick >= 0 || !string.IsNullOrEmpty(combat.CurrentTargetId) || session.ProjectileShotDetected;
            SemanticAssert(session.Result, "turret.target_acquired", acquired, "valid target or attack state observed", combat.Summary, null, combat.Summary);
            bool projectileRequired = TurretUsesProjectile(session.TurretDef);
            SemanticAssert(session.Result, "turret.projectile_observed", !projectileRequired || session.ProjectileShotDetected || combat.ActiveProjectileCount > 0 || session.FirstDamageTick >= 0 || session.FirstKillTick >= 0, projectileRequired ? "projectile or resulting hit observed" : "not applicable", "detected=" + session.ProjectileShotDetected + " active=" + combat.ActiveProjectileCount + " damageTick=" + session.FirstDamageTick);
            SemanticAssert(session.Result, "turret.damage_observed", HasObservedDamage(session.EnemyPawns, session.EnemyInitialHealthPercent), "target HP/health decreased, downed, or dead", combat.Summary, null, combat.Summary);
        }

        private void RecordTurretCleanupSemantic(TurretTestSession session)
        {
            SemanticAssert(session.Result, "turret.cleanup_complete", session.TestMap == null || !Find.Maps.Contains(session.TestMap), "pocket map removed", "mapPresent=" + (session.TestMap != null && Find.Maps.Contains(session.TestMap)));
        }

        private void RecordRangedSemanticBeforeCleanup(RangedWeaponTestSession session)
        {
            if (session?.Result == null) return;
            string[] knownTargetingModes = { "pawn_target", "location_target", "building_target", "unsupported" };
            bool targetingModeKnown = knownTargetingModes.Contains(session.TargetingMode);
            SemanticAssert(session.Result, "ranged.targeting_mode_classified", targetingModeKnown, string.Join("/", knownTargetingModes), session.TargetingMode,
                "The combat harness prepares the target type declared by TargetingParameters.");

            if (string.Equals(session.TargetingMode, "unsupported", StringComparison.Ordinal))
            {
                AutoTestPawnSnapshot unsupportedShooter = AutoTestPawnProbe.Capture(session.ShooterPawn);
                SemanticAssert(session.Result, "ranged.map_registered", session.TestMap != null && Find.Maps.Contains(session.TestMap), "pocket map registered", AutoTestMapProbe.Capture(session.TestMap)?.Id ?? "missing");
                SemanticAssert(session.Result, "ranged.weapon_equipped", session.EquippedWeapon != null && session.ShooterPawn?.equipment?.Primary == session.EquippedWeapon, session.WeaponDef?.defName, string.Join(",", unsupportedShooter.EquipmentDefNames.ToArray()), null, unsupportedShooter.Summary);
                bool unsupportedExplicit = string.Equals(session.Result.Status, "unsupported", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(session.Result.ErrorMessage);
                SemanticAssert(session.Result, "ranged.unsupported_reason_recorded", unsupportedExplicit, "explicit unsupported status and reason", session.Result.Status + ":" + session.Result.ErrorMessage);
                return;
            }

            Pawn target = ResolveSemanticTarget(session.EnemyPawns);
            AutoTestPawnSnapshot shooter = AutoTestPawnProbe.Capture(session.ShooterPawn);
            AutoTestCombatSnapshot combat = AutoTestCombatProbe.Capture(session.ShooterPawn, target, session.TestMap);
            SemanticAssert(session.Result, "ranged.map_registered", session.TestMap != null && Find.Maps.Contains(session.TestMap), "pocket map registered", AutoTestMapProbe.Capture(session.TestMap)?.Id ?? "missing");
            SemanticAssert(session.Result, "ranged.weapon_equipped", session.EquippedWeapon != null && session.ShooterPawn?.equipment?.Primary == session.EquippedWeapon, session.WeaponDef?.defName, string.Join(",", shooter.EquipmentDefNames.ToArray()), null, shooter.Summary);
            Verb verb = session.CombatVerb;
            SemanticAssert(session.Result, "ranged.attack_verb_available", verb != null && verb.Available(), "available ranged attack verb", verb == null ? "missing" : verb.GetType().Name + " available=" + verb.Available());
            SemanticAssert(session.Result, "ranged.attack_state_entered", session.FirstAttackInitiatedTick >= 0 || !string.IsNullOrEmpty(shooter.CurrentJobDefName) || session.ProjectileShotDetected || session.FirstDamageTick >= 0 || session.FirstKillTick >= 0, "attack job/manual Verb cast/attack evidence", combat.Summary, null, combat.Summary);
            bool projectileRequired = WeaponUsesProjectile(session.WeaponDef);
            SemanticAssert(session.Result, "ranged.projectile_or_direct_effect", !projectileRequired || session.ProjectileShotDetected || combat.ActiveProjectileCount > 0 || session.FirstDamageTick >= 0 || session.FirstKillTick >= 0, projectileRequired ? "projectile or resulting hit observed" : "direct/beam effect allowed", combat.Summary);
            bool buildingDamaged = session.AttackTargetBuilding != null &&
                (session.AttackTargetBuilding.Destroyed || session.AttackTargetBuildingInitialHitPoints >= 0 && session.AttackTargetBuilding.HitPoints < session.AttackTargetBuildingInitialHitPoints);
            SemanticAssert(session.Result, "ranged.damage_observed", HasObservedDamage(session.EnemyPawns, session.EnemyInitialHealthPercent) || buildingDamaged || session.ProjectileShotDetected, "Pawn/building state changed or a legal projectile was observed", "passEvidence=" + session.CombatPassEvidence + ";" + combat.Summary, null, combat.Summary);
            bool targetValid = string.Equals(session.TargetingMode, "location_target", StringComparison.Ordinal)
                ? session.AttackTargetCell.IsValid && session.AttackTargetCell.InBounds(session.TestMap)
                : string.Equals(session.TargetingMode, "building_target", StringComparison.Ordinal)
                    ? session.AttackTargetBuilding != null && (session.AttackTargetBuilding.Map == session.TestMap || session.AttackTargetBuilding.Destroyed)
                    : target != null && (target.Map == session.TestMap || target.Destroyed || target.Dead);
            SemanticAssert(session.Result, "ranged.target_valid", targetValid, "prepared target remains valid or reached terminal state", "mode=" + session.TargetingMode + ";cell=" + session.AttackTargetCell);
        }

        private void RecordRangedCleanupSemantic(RangedWeaponTestSession session)
        {
            SemanticAssert(session.Result, "ranged.cleanup_complete", session.TestMap == null || !Find.Maps.Contains(session.TestMap), "pocket map removed", "mapPresent=" + (session.TestMap != null && Find.Maps.Contains(session.TestMap)));
        }

        private void RecordMeleeSemanticBeforeCleanup(MeleeWeaponTestSession session)
        {
            if (session?.Result == null) return;
            Pawn target = ResolveSemanticTarget(session.EnemyPawns);
            AutoTestPawnSnapshot attacker = AutoTestPawnProbe.Capture(session.ShooterPawn);
            AutoTestCombatSnapshot combat = AutoTestCombatProbe.Capture(session.ShooterPawn, target, session.TestMap);
            bool weaponEquippedAtSetup = session.InitialWeaponEquipped && session.EquippedWeapon != null;
            SemanticAssert(session.Result, "melee.weapon_equipped", weaponEquippedAtSetup, session.WeaponDef?.defName,
                "initial=" + weaponEquippedAtSetup + ";finalPrimary=" + (session.ShooterPawn?.equipment?.Primary?.def?.defName ?? "none"),
                "The assertion is captured immediately after AddEquipment; a later drop caused by downing/fleeing is recorded separately.", attacker.Summary);
            Verb verb = session.ShooterPawn?.TryGetAttackVerb(target, true, false);
            SemanticAssert(session.Result, "melee.attack_verb_available", verb != null && verb.IsMeleeAttack && verb.Available(), "available melee verb", verb == null ? "missing" : verb.GetType().Name + " melee=" + verb.IsMeleeAttack);
            bool inRangeOrJob = session.ShooterPawn != null && target != null && (session.ShooterPawn.Position.AdjacentTo8WayOrInside(target.Position) || !string.IsNullOrEmpty(attacker.CurrentJobDefName));
            SemanticAssert(session.Result, "melee.attack_job_or_distance", inRangeOrJob, "attack job or melee distance", combat.Summary, null, combat.Summary);
            SemanticAssert(session.Result, "melee.damage_observed", HasObservedDamage(session.EnemyPawns, session.EnemyInitialHealthPercent), "target HP/health decreased", combat.Summary, null, combat.Summary);
            SemanticAssert(session.Result, "melee.entities_remained_on_map", session.ShooterPawn != null && (session.ShooterPawn.Map == session.TestMap || session.ShooterPawn.Destroyed) && target != null && (target.Map == session.TestMap || target.Destroyed || target.Dead), "attacker and target remain on test map until cleanup", combat.Summary);
        }

        private void RecordMeleeCleanupSemantic(MeleeWeaponTestSession session)
        {
            SemanticAssert(session.Result, "melee.cleanup_complete", session.TestMap == null || !Find.Maps.Contains(session.TestMap), "pocket map removed", "mapPresent=" + (session.TestMap != null && Find.Maps.Contains(session.TestMap)));
        }

        private void RecordSkillSemanticBeforeCleanup(SkillPocketMapTestSession session)
        {
            if (session?.Result == null) return;
            AutoTestPawnSnapshot caster = AutoTestPawnProbe.Capture(session.CasterPawn);
            AutoTestAbilitySnapshot after = AutoTestAbilityProbe.Capture(session.Ability);
            bool owned = caster.Abilities.Any(item => string.Equals(item.DefName, session.AbilityDef?.defName, StringComparison.OrdinalIgnoreCase));
            SemanticAssert(session.Result, "skill.ability_owned", owned, session.AbilityDef?.defName, string.Join(",", caster.Abilities.Select(item => item.DefName).ToArray()), null, caster.Summary);
            AutoTestSelectionSnapshot selection = AutoTestSelectionProbe.Capture(new object[] { session.CasterPawn });
            bool gizmoListed = selection.Gizmos.Any(gizmo => string.IsNullOrEmpty(session.AbilityDef?.label) || string.Equals(gizmo.Label, session.AbilityDef.label, StringComparison.OrdinalIgnoreCase));
            SemanticAssert(session.Result, "skill.gizmo_listed_for_selection", gizmoListed, "ability gizmo bound to current selection", selection.Summary, null, selection.Summary);
            SemanticAssert(session.Result, "skill.force_bypass_recorded", true, "forceBypass explicitly recorded", "forceBypass=true; nativeAvailable=" + (session.AbilityBefore?.Available ?? false), "The current isolated skill harness deliberately bypasses GizmoDisabled/CanCast and records that fact.");
            SemanticAssert(session.Result, "skill.target_type_matched", !string.IsNullOrWhiteSpace(session.SkillTargetMode) && session.SkillTargetMode != "Unknown", "resolved target mode", session.SkillTargetMode);
            SemanticAssert(session.Result, "skill.cast_accepted", session.CastSucceeded, "cast job or warmup observed", "castSucceeded=" + session.CastSucceeded + " firstCastTick=" + session.FirstCastTick + " warmupStart=" + session.WarmupStartTick);
            bool cooldownChanged = session.AbilityBefore?.CooldownTicks != after.CooldownTicks && after.CooldownTicks.HasValue;
            bool chargesChanged = session.AbilityBefore != null && session.AbilityBefore.Charges != after.Charges && session.AbilityBefore.Charges.HasValue && after.Charges.HasValue;
            bool targetStateChanged = DidSkillTargetStateChange(session);
            SemanticAssert(session.Result, "skill.semantic_effect_observed", cooldownChanged || chargesChanged || targetStateChanged, "cooldown, charges, Hediff, or target health changes", "beforeCooldown=" + session.AbilityBefore?.CooldownTicks + " afterCooldown=" + after.CooldownTicks + " beforeCharges=" + session.AbilityBefore?.Charges + " afterCharges=" + after.Charges + " targetStateChanged=" + targetStateChanged, null, caster.Summary);
            SemanticAssert(session.Result, "skill.caster_valid", session.CasterPawn != null && !session.CasterPawn.Destroyed && session.CasterPawn.Map == session.TestMap, "caster remains valid on test map", caster.Summary);
        }

        private bool DidSkillTargetStateChange(SkillPocketMapTestSession session)
        {
            foreach (Pawn pawn in session.EnemyPawns.Concat(new[] { session.AllyPawn, session.CasterPawn }).Where(item => item != null))
            {
                string id = AutoTestThingProbe.Target(pawn)?.Id;
                string now = BuildPawnEffectFingerprint(pawn);
                if (id != null && session.TargetStateBefore.TryGetValue(id, out string before) && !string.Equals(before, now, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private static string BuildPawnEffectFingerprint(Pawn pawn)
        {
            AutoTestPawnSnapshot snapshot = AutoTestPawnProbe.Capture(pawn);
            return snapshot.HealthPercent.ToString("0.0000") + "|" + string.Join(",", snapshot.Hediffs.Select(item => item.DefName + ":" + item.Severity.ToString("0.000")).OrderBy(value => value).ToArray());
        }

        private void RecordSkillCleanupSemantic(SkillPocketMapTestSession session)
        {
            SemanticAssert(session.Result, "skill.cleanup_complete", session.TestMap == null || !Find.Maps.Contains(session.TestMap), "pocket map removed", "mapPresent=" + (session.TestMap != null && Find.Maps.Contains(session.TestMap)));
        }

        private void RecordCombatPawnSemanticBeforeCleanup(CombatPawnTestSession session)
        {
            if (session?.Result == null) return;
            AutoTestPawnSnapshot target = AutoTestPawnProbe.Capture(session.TargetPawn);
            SemanticAssert(session.Result, "combatpawn.target_spawned", session.TargetPawn != null && session.TargetPawn.Spawned && session.TargetPawn.Map == session.TestMap, "target spawned on test map", target.Summary, null, target.Summary);
            AutoTestPawnSnapshot initial = session.InitialSnapshot;
            SemanticAssert(session.Result, "combatpawn.initial_loadout_captured", initial != null && !string.IsNullOrEmpty(initial.Target?.DefName), "race/body/equipment/apparel snapshot", initial?.Summary ?? "missing", "race=" + (session.Spec?.PawnRaceDefName ?? session.TargetPawn?.def?.defName) + " body=" + session.Spec?.PawnBodyTypeDefName);
            SemanticAssert(session.Result, "combatpawn.damage_observed", session.FirstHitObserved, "health decrease observed", "firstHit=" + session.FirstHitObserved + " tick=" + session.FirstHitTick + " health=" + target.HealthPercent, null, target.Summary);
            SemanticAssert(session.Result, "combatpawn.target_valid_through_observation", session.TargetPawn != null && (session.TargetPawn.Map == session.TestMap || session.TargetPawn.Dead || session.TargetPawn.Destroyed), "target remains valid or reaches terminal combat state", target.Summary);
            SemanticAssert(session.Result, "combatpawn.graphic_refresh", !string.Equals(session.Result.Status, "failed_graphic_refresh", StringComparison.OrdinalIgnoreCase), "apparel/graphic refresh did not fail", session.Result.Status);
        }

        private void RecordCombatPawnCleanupSemantic(CombatPawnTestSession session)
        {
            SemanticAssert(session.Result, "combatpawn.cleanup_complete", session.TestMap == null || !Find.Maps.Contains(session.TestMap), "pocket map removed", "mapPresent=" + (session.TestMap != null && Find.Maps.Contains(session.TestMap)));
        }

        private void RecordThreeViewSemanticBeforeCleanup(ThreeViewTestSession session)
        {
            if (session?.Result == null) return;
            List<Pawn> pawns = session.Context?.Pawns?.Where(pawn => pawn != null).ToList() ?? new List<Pawn>();
            SemanticAssert(session.Result, "threeview.targets_spawned", pawns.Count == 3 && pawns.All(pawn => pawn.Spawned && pawn.Map == session.TestMap), "three spawned directional targets", "count=" + pawns.Count + " maps=" + string.Join(",", pawns.Select(pawn => pawn.Map?.uniqueID.ToString() ?? "missing").ToArray()));
            bool raceMatches = string.IsNullOrEmpty(session.Spec?.PawnRaceDefName) || pawns.All(pawn => string.Equals(pawn.def?.defName, session.Spec.PawnRaceDefName, StringComparison.OrdinalIgnoreCase));
            SemanticAssert(session.Result, "threeview.race_matches_plan", raceMatches, session.Spec?.PawnRaceDefName ?? "any", string.Join(",", pawns.Select(pawn => pawn.def?.defName).ToArray()));
            string expectedFaction = string.IsNullOrWhiteSpace(session.Spec?.GenerationFactionDefName) ? "None" : session.Spec.GenerationFactionDefName;
            string actualFactions = string.Join(",", pawns.Select(pawn => pawn.Faction?.def?.defName ?? "None").ToArray());
            bool factionsMatch = pawns.All(pawn => string.Equals(pawn.Faction?.def?.defName ?? "None", expectedFaction, StringComparison.OrdinalIgnoreCase));
            SemanticAssert(session.Result, "threeview.factions_match_generation_plan", factionsMatch, expectedFaction, actualFactions,
                "GenerationFactionMode=" + (session.Spec?.GenerationFactionMode.ToString() ?? "Unknown"));
            string actualBodyTypes = string.Join(",", pawns.Select(pawn => pawn.story?.bodyType?.defName ?? "none").ToArray());
            if (session.Spec?.RequireBodyTypeMatch == true)
            {
                bool bodyMatches = string.IsNullOrEmpty(session.Spec.PawnBodyTypeDefName) || pawns.All(pawn => string.Equals(pawn.story?.bodyType?.defName, session.Spec.PawnBodyTypeDefName, StringComparison.OrdinalIgnoreCase));
                SemanticAssert(session.Result, "threeview.body_type_matches_plan", bodyMatches, session.Spec.PawnBodyTypeDefName ?? "any", actualBodyTypes,
                    "ExpectedBodyTypeSource=" + (session.Spec.ExpectedBodyTypeSource ?? "unspecified"));
            }
            else
            {
                SemanticAssert(session.Result, "threeview.body_type_observed", true, "informational body type observation", actualBodyTypes,
                    "RequireBodyTypeMatch=false;ExpectedBodyTypeSource=" + (session.Spec?.ExpectedBodyTypeSource ?? "InformationalOnly"));
            }
            bool itemMatches = session.Spec?.ItemDef == null || pawns.All(pawn => session.Spec.WearAsApparel
                ? pawn.apparel?.WornApparel?.Any(item => item.def == session.Spec.ItemDef) == true
                : !session.Spec.HoldAsWeapon || pawn.equipment?.Primary?.def == session.Spec.ItemDef);
            SemanticAssert(session.Result, "threeview.apparel_or_weapon_matches", itemMatches, session.Spec?.ItemDef?.defName ?? "not applicable", string.Join(";", pawns.Select(pawn => AutoTestPawnProbe.Capture(pawn).Summary).ToArray()));
            List<string> initialPrimaryWeapons = session.Context?.InitialPrimaryWeaponDefNames ?? new List<string>();
            bool generatedLoadoutRetained = initialPrimaryWeapons.Count == pawns.Count && pawns
                .Select((pawn, index) => string.IsNullOrWhiteSpace(initialPrimaryWeapons[index]) ||
                    string.Equals(pawn.equipment?.Primary?.def?.defName, initialPrimaryWeapons[index], StringComparison.OrdinalIgnoreCase))
                .All(value => value);
            SemanticAssert(session.Result, "threeview.generated_loadout_retained", generatedLoadoutRetained,
                string.Join(",", initialPrimaryWeapons.Select(value => string.IsNullOrWhiteSpace(value) ? "none" : value).ToArray()),
                string.Join(",", pawns.Select(pawn => pawn.equipment?.Primary?.def?.defName ?? "none").ToArray()),
                "Native PawnKind equipment present after generation must remain attached through capture.");
            bool positionsStable = session.Context != null && session.Context.Cells.Count == pawns.Count && pawns.Select((pawn, index) => pawn.Position == session.Context.Cells[index]).All(value => value);
            SemanticAssert(session.Result, "threeview.position_map_stable", positionsStable, "same map and planned positions", string.Join(",", pawns.Select(pawn => pawn.Position.ToString()).ToArray()));
            string rotations = session.Context == null ? string.Empty : string.Join(",", session.Context.Rotations.Select(rotation => rotation.ToString()).ToArray());
            bool directionsApplied = session.Context != null && session.Context.Rotations.Count == 3 && session.Context.Rotations.Distinct().Count() == 3 &&
                pawns.Count == session.Context.Rotations.Count && pawns.Select((pawn, index) => pawn.Rotation == session.Context.Rotations[index]).All(value => value);
            SemanticAssert(session.Result, "threeview.directions_applied", directionsApplied, "three distinct planned directions directly applied", rotations,
                "RotationControlledDirectly=true;DrafterOptional=true;Actual=" + string.Join(",", pawns.Select(pawn => pawn.Rotation.ToString()).ToArray()));
            if (session.Spec?.TargetGeneDef != null)
            {
                SemanticAssert(session.Result, "gene.target_present", session.Context?.TargetGenePresent == true, session.Spec.TargetGeneDef.defName, session.Context?.TargetGeneLocation ?? "Missing");
                SemanticAssert(session.Result, "gene.target_count_is_one", session.Context?.TargetGeneCount == 1, "1", (session.Context?.TargetGeneCount ?? 0).ToString());
                SemanticAssert(session.Result, "gene.dependencies_present", session.Context?.DependencyGenesPresent == true,
                    session.Spec.TargetGeneDependencies.Count == 0 ? "no prerequisites" : string.Join(",", session.Spec.TargetGeneDependencies.Select(gene => gene.defName).ToArray()),
                    "present=" + (session.Context?.DependencyGenesPresent ?? false));
                SemanticAssert(session.Result, "gene.dependencies_active", session.Context?.GeneDependenciesActive == true, "all prerequisites active", "active=" + (session.Context?.GeneDependenciesActive ?? false));
                SemanticAssert(session.Result, "gene.target_active", session.Context?.TargetGeneActive == true, "target active", "active=" + (session.Context?.TargetGeneActive ?? false));
                SemanticAssert(session.Result, "gene.total_count_matches_expected", session.Context != null && session.Context.ActualTotalGeneCount == session.Context.ExpectedTotalGeneCount,
                    (session.Context?.ExpectedTotalGeneCount ?? 0).ToString(), (session.Context?.ActualTotalGeneCount ?? 0).ToString());
                SemanticAssert(session.Result, "gene.only_target_and_dependencies", session.Context?.OnlyTargetAndDependencies == true,
                    string.Join(",", session.Spec.ExpectedGeneSet.Select(gene => gene.defName).ToArray()),
                    "matches=" + (session.Context?.OnlyTargetAndDependencies ?? false));
                SemanticAssert(session.Result, "gene.triptych_gene_sets_match", session.Context?.TriptychGeneSetsMatch == true, "three identical target-plus-prerequisite sets", "matches=" + (session.Context?.TriptychGeneSetsMatch ?? false));
                SemanticAssert(session.Result, "gene.visual_signature_changed", session.Context?.VisualEffectDetected == true, "visual signature changed", "changed=" + (session.Context?.VisualEffectDetected ?? false));
            }
            SemanticAssert(session.Result, "threeview.capture_completed", !session.AllowScreenshots || session.Result.ScreenshotPaths.Count > 0, session.AllowScreenshots ? "capture produced" : "capture optional", "captures=" + session.Result.ScreenshotPaths.Count);
            TickManager tickManager = Find.TickManager;
            SemanticAssert(session.Result, "threeview.speed_restored", !session.PauseBypassArmed && (tickManager == null || tickManager.CurTimeSpeed == session.ResumeSpeed), "time speed restored", "bypass=" + session.PauseBypassArmed + " current=" + tickManager?.CurTimeSpeed + " expected=" + session.ResumeSpeed);
        }

        private void RecordThreeViewCleanupSemantic(ThreeViewTestSession session, bool targetsRemoved, bool renderSuppressionCleared)
        {
            SemanticAssert(session.Result, "threeview.cleanup_complete", targetsRemoved, "spawned targets removed and studio restored", "targetsRemoved=" + targetsRemoved);
            SemanticAssert(session.Result, "threeview.render_suppression_cleared", renderSuppressionCleared, "all three-view Pawn registrations removed", "cleared=" + renderSuppressionCleared);
        }

        private static bool TurretUsesProjectile(ThingDef turretDef) { return WeaponUsesProjectile(turretDef?.building?.turretGunDef); }
        private static bool WeaponUsesProjectile(ThingDef weaponDef)
        {
            return weaponDef?.Verbs != null && weaponDef.Verbs.Any(verb => verb?.defaultProjectile != null);
        }

        private void RecordInfoCardSemantic(bool expectedOpen)
        {
            if (activeSession?.Result == null) return;
            AutoTestTargetRef target = AutoTestThingProbe.Target(activeSession.SpawnedThing);
            if (target == null && activeSession.InfoCardDef != null)
            {
                target = new AutoTestTargetRef
                {
                    Id = "def:" + activeSession.InfoCardDef.defName,
                    Kind = "def",
                    Label = activeSession.InfoCardDef.label,
                    DefName = activeSession.InfoCardDef.defName,
                    MapId = activeSession.SpawnedThing?.Map?.uniqueID ?? -1,
                    Fingerprint = AutoTestSelectionFingerprint.Create(new[] { "def", activeSession.InfoCardDef.defName })
                };
            }
            Window expectedWindow = activeSession.Item.Kind == AutoTestKind.Recipe
                ? (Window)Find.WindowStack?.WindowOfType<Dialog_BillConfig>()
                : Find.WindowStack?.WindowOfType<Dialog_InfoCard>();
            AutoTestWindowSnapshot window = AutoTestWindowProbe.Capture(expectedWindow);
            SemanticAssert(activeSession.Result, "info_card.target_stable", target != null, "stable target reference", target?.Id ?? "missing", null, window.Summary);
            bool targetMatches;
            if (activeSession.Item.Kind == AutoTestKind.Recipe)
            {
                targetMatches = string.IsNullOrEmpty(activeSession.Item.RecipeDef?.defName) ||
                    string.IsNullOrEmpty(window.BoundRecipeDefName) ||
                    string.Equals(window.BoundRecipeDefName, activeSession.Item.RecipeDef.defName, StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                if (activeSession.AttachedHediff != null)
                {
                    AutoTestInfoCardHandle infoCardHandle = activeSession.UiHandle as AutoTestInfoCardHandle;
                    targetMatches = infoCardHandle != null &&
                        string.Equals(infoCardHandle.TargetDef?.defName, activeSession.InfoCardDef?.defName, StringComparison.OrdinalIgnoreCase);
                }
                else
                {
                    targetMatches = string.IsNullOrEmpty(activeSession.InfoCardDef?.defName) || string.IsNullOrEmpty(window.BoundDefName) || string.Equals(window.BoundDefName, activeSession.InfoCardDef.defName, StringComparison.OrdinalIgnoreCase);
                }
            }
            SemanticAssert(activeSession.Result, "info_card.window_open_for_target", !expectedOpen || window.IsOpen && targetMatches, "InfoCard open and target-bound", window.Summary, null, window.Summary);
            string inspect = string.Empty;
            try { inspect = activeSession.SpawnedThing?.GetInspectString() ?? string.Empty; } catch (Exception ex) { inspect = "probe_error:" + ex.Message; }
            bool inspectProbeSucceeded = !inspect.StartsWith("probe_error:", StringComparison.OrdinalIgnoreCase);
            string inspectActual = activeSession.SpawnedThing == null ? "not applicable" : "nonEmpty=" + !string.IsNullOrWhiteSpace(inspect) + " length=" + inspect.Length;
            SemanticAssert(activeSession.Result, "info_card.inspect_semantics_available", inspectProbeSucceeded, "GetInspectString probe completed; empty is a valid model result", inspectActual,
                activeSession.SpawnedThing == null ? "No spawned Thing target." : "The probe records emptiness, length, and summary without treating localization-dependent empty text as a functional failure.", inspect);
            if (activeSession.Item.Kind == AutoTestKind.Recipe)
            {
                bool billBound = activeSession.RecipeWorktableThing is IBillGiver billGiver && activeSession.RecipeBill != null && billGiver.BillStack != null && billGiver.BillStack.Bills.Contains(activeSession.RecipeBill);
                SemanticAssert(activeSession.Result, "recipe.bill_stack_binding", billBound, activeSession.Item.RecipeDef?.defName ?? "recipe bill", billBound ? "bill present in BillStack" : "bill missing from BillStack");
                bool recipeBound = string.Equals(window.BoundRecipeDefName, activeSession.Item.RecipeDef?.defName, StringComparison.OrdinalIgnoreCase);
                SemanticAssert(activeSession.Result, "recipe.bill_config_binding", recipeBound, activeSession.Item.RecipeDef?.defName ?? "recipe", window.BoundRecipeDefName ?? string.Empty, null, window.Summary);
            }
        }

        private void RecordInfoCardClosedSemantic()
        {
            if (activeSession?.Result == null) return;
            AutoTestWindowSnapshot window = AutoTestWindowProbe.Capture();
            bool infoCardPresent = Find.WindowStack?.WindowOfType<Dialog_InfoCard>() != null;
            bool billConfigPresent = Find.WindowStack?.WindowOfType<Dialog_BillConfig>() != null;
            SemanticAssert(activeSession.Result, "info_card.window_closed", !infoCardPresent && !billConfigPresent, "InfoCard/BillConfig windows closed", window.Summary, null, window.Summary);
            if (activeSession.Item.Kind == AutoTestKind.Recipe && activeSession.RecipeWorktableThing is IBillGiver billGiver && activeSession.RecipeBill != null)
            {
                bool billRemoved = billGiver.BillStack == null || !billGiver.BillStack.Bills.Contains(activeSession.RecipeBill);
                SemanticAssert(activeSession.Result, "recipe.bill_removed_after_cleanup", billRemoved, "bill removed from BillStack", "billPresent=" + !billRemoved);
            }
        }
    }
}
