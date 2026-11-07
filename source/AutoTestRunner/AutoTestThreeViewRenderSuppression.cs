using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace LogSyncMod.AutoTest
{
    internal static class AutoTestThreeViewRenderSuppression
    {
        private static readonly object Gate = new object();
        private static readonly HashSet<int> RegisteredPawnIds = new HashSet<int>();
        private static readonly HashSet<int> StandingPresenters = new HashSet<int>();

        internal static void SetStandingPresentation(Pawn pawn, bool enabled)
        {
            if (pawn == null) return;
            lock (Gate) { if (enabled) StandingPresenters.Add(pawn.thingIDNumber); else StandingPresenters.Remove(pawn.thingIDNumber); }
        }

        internal static bool UsesStandingPresentation(Pawn pawn)
        { lock (Gate) return pawn != null && StandingPresenters.Contains(pawn.thingIDNumber); }

        public static int RegisteredCount
        {
            get
            {
                lock (Gate)
                {
                    return RegisteredPawnIds.Count;
                }
            }
        }

        public static void Register(Pawn pawn)
        {
            if (pawn == null)
            {
                return;
            }

            lock (Gate)
            {
                RegisteredPawnIds.Add(pawn.thingIDNumber);
            }

            ClearWoundCache(pawn);
        }

        public static void Unregister(Pawn pawn)
        {
            if (pawn == null)
            {
                return;
            }

            lock (Gate)
            {
                RegisteredPawnIds.Remove(pawn.thingIDNumber);
                StandingPresenters.Remove(pawn.thingIDNumber);
            }

            ClearWoundCache(pawn);
        }

        public static bool IsRegistered(Pawn pawn)
        {
            if (pawn == null)
            {
                return false;
            }

            lock (Gate)
            {
                return RegisteredPawnIds.Contains(pawn.thingIDNumber);
            }
        }

        private static void ClearWoundCache(Pawn pawn)
        {
            try
            {
                pawn.Drawer?.renderer?.WoundOverlays?.ClearCache();
            }
            catch
            {
            }
        }
    }

    [HarmonyPatch(typeof(PawnUtility), nameof(PawnUtility.GetPosture))]
    internal static class AutoTestThreeViewPresentationPosturePatch
    {
        private static void Postfix(Pawn p, ref PawnPosture __result)
        {
            // Photo presentation only: real health, Downed and age/life-stage data stay intact.
            if (AutoTestThreeViewRenderSuppression.UsesStandingPresentation(p)) __result = PawnPosture.Standing;
        }
    }


    [HarmonyPatch(typeof(FleshTypeDef), nameof(FleshTypeDef.ChooseWoundOverlay))]
    internal static class FleshTypeDef_ChooseWoundOverlay_AutoTestThreeViewPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Hediff hediff, ref FleshTypeDef.ResolvedWound __result)
        {
            if (__result == null || !(hediff is Hediff_MissingPart) || !AutoTestThreeViewRenderSuppression.IsRegistered(hediff.pawn))
            {
                return;
            }

            BodyPartRecord part = hediff.Part;
            bool missingEye = part?.def == BodyPartDefOf.Eye ||
                part?.def?.tags?.Contains(BodyPartTagDefOf.SightSource) == true;
            if (missingEye)
            {
                __result = null;
            }
        }
    }
}
