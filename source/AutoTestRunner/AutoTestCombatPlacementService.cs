using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RimWorld;
using Verse;

namespace LogSyncMod.AutoTest
{
    internal sealed class AutoTestCombatPlacementRequest
    {
        public Map Map;
        public Thing Attacker;
        public IntVec3 AttackerCell;
        public VerbProperties VerbProps;
        public float EffectiveRange;
        public int TargetCount;
        public IntVec3 PreferredAnchor;
        public IList<IntVec3> PreferredCells;
        public CellRect SafeRect;
        public bool RequireLineOfSight;
        public bool RequireWalkable = true;
        public float MinimumSeparationMargin = 0.5f;
        public float MaximumRangeMargin = 0.5f;
        public float MinimumDistanceFloor = 2f;
        public Func<IntVec3, bool> AdditionalValidator = null;
    }

    internal sealed class AutoTestCombatPlacementResult
    {
        public readonly List<IntVec3> SelectedCells = new List<IntVec3>();
        public readonly Dictionary<string, int> CandidateDiagnostics = new Dictionary<string, int>(StringComparer.Ordinal);
        public float MinValidDistance;
        public float MaxValidDistance;
        public float PreferredDistance;
        public bool PreferredCellWasValid;
        public bool EnemyRepositioned;
        public string RepositionReason = "none";
        public string FailureReason = string.Empty;

        public string DiagnosticsSummary
        {
            get
            {
                return CandidateDiagnostics.Count == 0
                    ? "none"
                    : string.Join(",", CandidateDiagnostics.OrderBy(pair => pair.Key).Select(pair => pair.Key + "=" + pair.Value).ToArray());
            }
        }

        public string ActualDistancesSummary(IntVec3 attackerCell)
        {
            return string.Join(",", SelectedCells
                .Select(cell => cell.DistanceTo(attackerCell).ToString("0.00", CultureInfo.InvariantCulture))
                .ToArray());
        }
    }

    internal sealed class AutoTestCombatPlacementService
    {
        private sealed class Candidate
        {
            public IntVec3 Cell;
            public float Distance;
            public int PreferredIndex;
        }

        public AutoTestCombatPlacementResult FindTargets(AutoTestCombatPlacementRequest request)
        {
            AutoTestCombatPlacementResult result = new AutoTestCombatPlacementResult();
            if (request == null || request.Map == null || request.TargetCount <= 0)
            {
                result.FailureReason = "placement_request_incomplete";
                return result;
            }

            if (request.SafeRect.Width <= 0 || request.SafeRect.Height <= 0)
            {
                result.FailureReason = "safe_rect_empty";
                return result;
            }

            float mapSafeMaximum = CalculateMapSafeMaximum(request.AttackerCell, request.SafeRect);
            float verbRange = request.EffectiveRange > 0f
                ? request.EffectiveRange
                : request.VerbProps?.range ?? 0f;
            AutoTestCombatDistanceResult distance = AutoTestCombatDistancePolicy.Calculate(new AutoTestCombatDistanceRequest
            {
                VerbMinRange = request.VerbProps?.minRange ?? 0f,
                VerbRange = verbRange,
                MinimumSeparationMargin = request.MinimumSeparationMargin,
                MaximumRangeMargin = request.MaximumRangeMargin,
                MinimumDistanceFloor = request.MinimumDistanceFloor,
                MapSafeMaximum = mapSafeMaximum
            });
            result.MinValidDistance = distance.MinValidDistance;
            result.MaxValidDistance = distance.MaxValidDistance;
            result.PreferredDistance = distance.PreferredDistance;
            if (!distance.IsValid)
            {
                result.FailureReason = distance.FailureReason;
                return result;
            }

            List<IntVec3> preferredCells = BuildPreferredCells(request);
            Dictionary<IntVec3, int> preferredOrder = new Dictionary<IntVec3, int>();
            for (int index = 0; index < preferredCells.Count; index++)
            {
                if (!preferredOrder.ContainsKey(preferredCells[index]))
                {
                    preferredOrder.Add(preferredCells[index], index);
                }
            }

            result.PreferredCellWasValid = IsCandidateValid(request, request.PreferredAnchor, distance, result, recordDiagnostics: false);
            List<Candidate> validCandidates = new List<Candidate>();
            for (int x = request.SafeRect.minX; x <= request.SafeRect.maxX; x++)
            {
                for (int z = request.SafeRect.minZ; z <= request.SafeRect.maxZ; z++)
                {
                    IntVec3 cell = new IntVec3(x, 0, z);
                    Increment(result, "examined");
                    if (!IsCandidateValid(request, cell, distance, result, recordDiagnostics: true))
                    {
                        continue;
                    }

                    validCandidates.Add(new Candidate
                    {
                        Cell = cell,
                        Distance = cell.DistanceTo(request.AttackerCell),
                        PreferredIndex = preferredOrder.TryGetValue(cell, out int preferredIndex) ? preferredIndex : int.MaxValue
                    });
                }
            }

            IEnumerable<Candidate> ordered = validCandidates
                .OrderBy(candidate => candidate.PreferredIndex == int.MaxValue ? 1 : 0)
                .ThenBy(candidate => candidate.PreferredIndex)
                .ThenBy(candidate => candidate.Cell.DistanceToSquared(request.PreferredAnchor))
                .ThenBy(candidate => Math.Abs(candidate.Distance - distance.PreferredDistance))
                .ThenBy(candidate => Math.Abs(candidate.Cell.z - request.PreferredAnchor.z))
                .ThenBy(candidate => candidate.Cell.x)
                .ThenBy(candidate => candidate.Cell.z);

            foreach (Candidate candidate in ordered)
            {
                if (result.SelectedCells.Count >= request.TargetCount)
                {
                    break;
                }

                result.SelectedCells.Add(candidate.Cell);
            }

            if (result.SelectedCells.Count == 0)
            {
                result.FailureReason = "no_valid_candidates";
                result.RepositionReason = result.FailureReason;
                return result;
            }

            HashSet<IntVec3> originalCells = new HashSet<IntVec3>(preferredCells);
            result.EnemyRepositioned = result.SelectedCells.Any(cell => !originalCells.Contains(cell));
            if (result.SelectedCells.Count < request.TargetCount)
            {
                result.EnemyRepositioned = true;
                result.RepositionReason = "insufficient_valid_candidates:" + result.SelectedCells.Count + "/" + request.TargetCount;
            }
            else if (!result.PreferredCellWasValid)
            {
                result.EnemyRepositioned = true;
                result.RepositionReason = DeterminePreferredRejectionReason(request, distance);
            }
            else if (result.EnemyRepositioned)
            {
                result.RepositionReason = "preferred_layout_partially_invalid";
            }

            return result;
        }

        public AutoTestCombatPlacementResult FindCloseCombatTargets(
            Map map,
            Thing attacker,
            int targetCount,
            CellRect safeRect)
        {
            IntVec3 center = attacker?.Position ?? IntVec3.Invalid;
            List<IntVec3> surround = new List<IntVec3>
            {
                center + IntVec3.East,
                center + IntVec3.West,
                center + IntVec3.North,
                center + IntVec3.South,
                center + IntVec3.NorthEast,
                center + IntVec3.NorthWest,
                center + IntVec3.SouthEast,
                center + IntVec3.SouthWest
            };
            return FindTargets(new AutoTestCombatPlacementRequest
            {
                Map = map,
                Attacker = attacker,
                AttackerCell = center,
                VerbProps = new VerbProperties { minRange = 0f, range = 2.25f, requireLineOfSight = false },
                EffectiveRange = 2.25f,
                TargetCount = targetCount,
                PreferredAnchor = surround[0],
                PreferredCells = surround,
                SafeRect = safeRect,
                RequireLineOfSight = false,
                RequireWalkable = true,
                MinimumSeparationMargin = 0f,
                MaximumRangeMargin = 0f,
                MinimumDistanceFloor = 1f
            });
        }

        private static List<IntVec3> BuildPreferredCells(AutoTestCombatPlacementRequest request)
        {
            if (request.PreferredCells != null && request.PreferredCells.Count > 0)
            {
                return request.PreferredCells.Where(cell => cell.IsValid).ToList();
            }

            List<IntVec3> cells = new List<IntVec3>();
            int radius = Math.Max(request.TargetCount + 2, 4);
            cells.Add(request.PreferredAnchor);
            for (int offset = 1; offset <= radius; offset++)
            {
                cells.Add(request.PreferredAnchor + new IntVec3(0, 0, -offset));
                cells.Add(request.PreferredAnchor + new IntVec3(0, 0, offset));
            }

            return cells;
        }

        private static bool IsCandidateValid(
            AutoTestCombatPlacementRequest request,
            IntVec3 candidate,
            AutoTestCombatDistanceResult distance,
            AutoTestCombatPlacementResult result,
            bool recordDiagnostics)
        {
            if (!candidate.IsValid || !candidate.InBounds(request.Map))
            {
                if (recordDiagnostics) Increment(result, "out_of_bounds");
                return false;
            }

            if (!request.SafeRect.Contains(candidate))
            {
                if (recordDiagnostics) Increment(result, "outside_safe_rect");
                return false;
            }

            if (request.Attacker != null && request.Attacker.OccupiedRect().Contains(candidate))
            {
                if (recordDiagnostics) Increment(result, "attacker_occupied");
                return false;
            }

            float actualDistance = candidate.DistanceTo(request.AttackerCell);
            if (actualDistance < distance.MinValidDistance)
            {
                if (recordDiagnostics) Increment(result, "below_min_range");
                return false;
            }

            if (actualDistance > distance.MaxValidDistance)
            {
                if (recordDiagnostics) Increment(result, "above_max_range");
                return false;
            }

            if (request.RequireWalkable && (!candidate.Walkable(request.Map) || candidate.GetFirstPawn(request.Map) != null))
            {
                if (recordDiagnostics) Increment(result, "not_walkable_or_occupied");
                return false;
            }

            if (request.RequireLineOfSight && !GenSight.LineOfSight(request.AttackerCell, candidate, request.Map))
            {
                if (recordDiagnostics) Increment(result, "line_of_sight_blocked");
                return false;
            }

            if (request.AdditionalValidator != null && !request.AdditionalValidator(candidate))
            {
                if (recordDiagnostics) Increment(result, "additional_validator_rejected");
                return false;
            }

            if (recordDiagnostics) Increment(result, "accepted");
            return true;
        }

        private static string DeterminePreferredRejectionReason(
            AutoTestCombatPlacementRequest request,
            AutoTestCombatDistanceResult distance)
        {
            IntVec3 cell = request.PreferredAnchor;
            if (!cell.IsValid || !cell.InBounds(request.Map)) return "preferred_out_of_bounds";
            if (!request.SafeRect.Contains(cell)) return "preferred_outside_safe_rect";
            float actualDistance = cell.DistanceTo(request.AttackerCell);
            if (actualDistance < distance.MinValidDistance) return "preferred_below_min_range";
            if (actualDistance > distance.MaxValidDistance) return "preferred_above_max_range";
            if (request.RequireWalkable && (!cell.Walkable(request.Map) || cell.GetFirstPawn(request.Map) != null)) return "preferred_not_walkable_or_occupied";
            if (request.RequireLineOfSight && !GenSight.LineOfSight(request.AttackerCell, cell, request.Map)) return "preferred_line_of_sight_blocked";
            if (request.AdditionalValidator != null && !request.AdditionalValidator(cell)) return "preferred_additional_validator_rejected";
            return "preferred_layout_partially_invalid";
        }

        private static float CalculateMapSafeMaximum(IntVec3 attackerCell, CellRect safeRect)
        {
            IntVec3[] corners =
            {
                new IntVec3(safeRect.minX, 0, safeRect.minZ),
                new IntVec3(safeRect.minX, 0, safeRect.maxZ),
                new IntVec3(safeRect.maxX, 0, safeRect.minZ),
                new IntVec3(safeRect.maxX, 0, safeRect.maxZ)
            };
            return corners.Max(cell => cell.DistanceTo(attackerCell));
        }

        private static void Increment(AutoTestCombatPlacementResult result, string key)
        {
            result.CandidateDiagnostics.TryGetValue(key, out int current);
            result.CandidateDiagnostics[key] = current + 1;
        }
    }
}
