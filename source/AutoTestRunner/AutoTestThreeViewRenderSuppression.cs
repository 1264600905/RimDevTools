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
