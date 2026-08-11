using System;
using System.Collections.Generic;
using System.Linq;

namespace LogSyncMod.AutoTest
{
    internal enum AutoTestRecipeExecutionKind
    {
        Standard,
        MechGestation,
        MechResurrection,
        PawnProduct,
        UnsupportedCustom
    }

    internal enum AutoTestFailureKind
    {
        DeterministicFailure,
        UnsupportedPath,
        TransientPawnGeneration,
        TransientUiTiming,
        UnexpectedSevereLog
    }

    internal sealed class AutoTestRecipeClassificationInput
    {
        public int GestationCycles;
        public bool MechResurrection;
        public bool ProductIsPawn;
        public bool HasCorpseIngredient;
        public bool WorktableIsMechGestator;
        public bool RecipeLooksLikeBaseMechanoidRecipe;
        public bool RecipeLooksCustom;
        public string ExplicitAdapter;
    }

    internal sealed class AutoTestRecipeClassification
    {
        public AutoTestRecipeExecutionKind Kind;
        public string Adapter;
        public string Reason;
    }

    internal static class AutoTestRecipeExecutionClassifier
    {
        public static AutoTestRecipeClassification Classify(AutoTestRecipeClassificationInput input)
        {
            if (input == null)
            {
                throw new ArgumentNullException(nameof(input));
            }

            string explicitAdapter = (input.ExplicitAdapter ?? string.Empty).Trim();
            if (explicitAdapter.Length > 0)
            {
                if (explicitAdapter.Equals("Standard", StringComparison.OrdinalIgnoreCase))
                    return Result(AutoTestRecipeExecutionKind.Standard, "StandardRecipeExecutionAdapter", "ExplicitAdapter=Standard");
                if (explicitAdapter.Equals("MechGestation", StringComparison.OrdinalIgnoreCase))
                    return Result(AutoTestRecipeExecutionKind.MechGestation, "MechGestationRecipeExecutionAdapter", "ExplicitAdapter=MechGestation");
                if (explicitAdapter.Equals("MechResurrection", StringComparison.OrdinalIgnoreCase))
                    return Result(AutoTestRecipeExecutionKind.MechResurrection, "MechResurrectionRecipeExecutionAdapter", "ExplicitAdapter=MechResurrection");
                return Result(AutoTestRecipeExecutionKind.UnsupportedCustom, "UnsupportedSpecialRecipeExecutionAdapter", "UnknownExplicitAdapter=" + explicitAdapter);
            }

            if (input.MechResurrection || (input.HasCorpseIngredient && input.WorktableIsMechGestator))
                return Result(AutoTestRecipeExecutionKind.MechResurrection, "MechResurrectionRecipeExecutionAdapter",
                    input.MechResurrection ? "RecipeDef.mechResurrection" : "CorpseIngredient+MechGestator");

            if (input.GestationCycles > 0 || (input.ProductIsPawn && input.WorktableIsMechGestator) || input.RecipeLooksLikeBaseMechanoidRecipe)
                return Result(AutoTestRecipeExecutionKind.MechGestation, "MechGestationRecipeExecutionAdapter",
                    input.GestationCycles > 0 ? "RecipeDef.gestationCycles>0" :
                    (input.ProductIsPawn && input.WorktableIsMechGestator ? "PawnProduct+MechGestator" : "BaseMechanoidRecipeShape"));

            if (input.ProductIsPawn)
                return Result(AutoTestRecipeExecutionKind.PawnProduct, "UnsupportedSpecialRecipeExecutionAdapter", "PawnProductWithoutSafeGestatorPath");

            if (input.RecipeLooksCustom)
                return Result(AutoTestRecipeExecutionKind.UnsupportedCustom, "UnsupportedSpecialRecipeExecutionAdapter", "CustomRecipeRequiresExplicitAdapter");

            return Result(AutoTestRecipeExecutionKind.Standard, "StandardRecipeExecutionAdapter", "OrdinaryRecipeProducts");
        }

        private static AutoTestRecipeClassification Result(AutoTestRecipeExecutionKind kind, string adapter, string reason)
        {
            return new AutoTestRecipeClassification { Kind = kind, Adapter = adapter, Reason = reason };
        }
    }

    internal static class AutoTestRetryPolicy
    {
        public static bool CanRetry(AutoTestFailureKind failureKind, int attemptsAlreadyMade)
        {
            return attemptsAlreadyMade == 1 &&
                (failureKind == AutoTestFailureKind.TransientPawnGeneration || failureKind == AutoTestFailureKind.TransientUiTiming);
        }
    }

    internal static class AutoTestRecipeResultPolicy
    {
        public static string FailureStatus(AutoTestFailureKind failureKind)
        {
            return failureKind == AutoTestFailureKind.UnsupportedPath ? AutoTestResultStatus.Unsupported : "failed_recipe_produce";
        }
    }

    internal sealed class AutoTestProductStackSnapshot
    {
        public string DefName;
        public int StackCount;
        public int StackLimit;
    }

    internal sealed class AutoTestProductPlacementVerification
    {
        public bool Success;
        public string ErrorMessage;
    }

    internal static class AutoTestProductPlacementVerifier
    {
        public static AutoTestProductPlacementVerification Verify(
            IDictionary<string, int> expectedTotals,
            IDictionary<string, int> placedTotals,
            IEnumerable<AutoTestProductStackSnapshot> stacks)
        {
            foreach (KeyValuePair<string, int> expected in expectedTotals ?? new Dictionary<string, int>())
            {
                int actual = placedTotals != null && placedTotals.TryGetValue(expected.Key, out int count) ? count : 0;
                if (actual != expected.Value)
                    return Failed("Product total mismatch for " + expected.Key + ": expected=" + expected.Value + " actual=" + actual);
            }

            AutoTestProductStackSnapshot oversized = (stacks ?? Enumerable.Empty<AutoTestProductStackSnapshot>())
                .FirstOrDefault(stack => stack != null && stack.StackCount > stack.StackLimit);
            if (oversized != null)
                return Failed("Product stack exceeds stackLimit for " + oversized.DefName + ": stack=" + oversized.StackCount + " limit=" + oversized.StackLimit);

            return new AutoTestProductPlacementVerification { Success = true };
        }

        private static AutoTestProductPlacementVerification Failed(string message)
        {
            return new AutoTestProductPlacementVerification { ErrorMessage = message };
        }
    }

    internal enum AutoTestHediffPartSourceKind
    {
        DefaultInstallPart,
        TargetModInstallRecipe,
        OtherInstallRecipe,
        WholeBody
    }

    internal sealed class AutoTestHediffPartCandidate<T>
    {
        public T Value;
        public AutoTestHediffPartSourceKind Source;
        public string StableName;
    }

    internal static class AutoTestHediffPartPolicy
    {
        public static AutoTestHediffPartCandidate<T> Select<T>(IEnumerable<AutoTestHediffPartCandidate<T>> candidates)
        {
            return (candidates ?? Enumerable.Empty<AutoTestHediffPartCandidate<T>>())
                .Where(candidate => candidate != null)
                .OrderBy(candidate => Rank(candidate.Source))
                .ThenBy(candidate => candidate.StableName ?? string.Empty, StringComparer.Ordinal)
                .FirstOrDefault();
        }

        private static int Rank(AutoTestHediffPartSourceKind source)
        {
            switch (source)
            {
                case AutoTestHediffPartSourceKind.DefaultInstallPart: return 0;
                case AutoTestHediffPartSourceKind.TargetModInstallRecipe: return 1;
                case AutoTestHediffPartSourceKind.OtherInstallRecipe: return 2;
                default: return 3;
            }
        }
    }

    internal enum AutoTestHediffPawnCandidateSource
    {
        ExplicitSpec,
        DirectStartingHediff,
        NativePrerequisiteEnvironment,
        SameModHumanlike,
        SameModAnimalOrMechanoid,
        VanillaColonist
    }

    internal sealed class AutoTestHediffPawnCandidate<T>
    {
        public T Value;
        public AutoTestHediffPawnCandidateSource Source;
        public string StableName;
    }

    internal static class AutoTestHediffPawnPolicy
    {
        public static List<AutoTestHediffPawnCandidate<T>> Order<T>(IEnumerable<AutoTestHediffPawnCandidate<T>> candidates)
        {
            return (candidates ?? Enumerable.Empty<AutoTestHediffPawnCandidate<T>>())
                .Where(candidate => candidate != null)
                .OrderBy(candidate => (int)candidate.Source)
                .ThenBy(candidate => candidate.StableName ?? string.Empty, StringComparer.Ordinal)
                .ToList();
        }
    }
}
