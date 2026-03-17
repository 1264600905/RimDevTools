using Verse;

namespace LogSyncMod.AutoTest
{
    public class HediffCompProperties_LogSyncTurretTestTarget : HediffCompProperties
    {
        public int stunTicks = 600;

        public HediffCompProperties_LogSyncTurretTestTarget()
        {
            compClass = typeof(HediffComp_LogSyncTurretTestTarget);
        }
    }

    public class HediffComp_LogSyncTurretTestTarget : HediffComp
    {
        private bool initialStunApplied;
        private IntVec3 lastPosition = IntVec3.Invalid;

        private HediffCompProperties_LogSyncTurretTestTarget Props => (HediffCompProperties_LogSyncTurretTestTarget)props;

        public override void CompPostPostAdd(DamageInfo? dinfo)
        {
            base.CompPostPostAdd(dinfo);
            TryApplyInitialStun();
        }

        public override void CompPostTick(ref float severityAdjustment)
        {
            base.CompPostTick(ref severityAdjustment);

            Pawn pawn = Pawn;
            if (pawn == null || pawn.Destroyed || pawn.Dead || !pawn.Spawned)
            {
                return;
            }

            TryApplyInitialStun();

            if (!lastPosition.IsValid)
            {
                lastPosition = pawn.Position;
                return;
            }

            if (pawn.Position != lastPosition)
            {
                lastPosition = pawn.Position;
                ApplyStun();
            }
        }

        public override void Notify_Spawned()
        {
            base.Notify_Spawned();
            lastPosition = Pawn?.Position ?? IntVec3.Invalid;
            TryApplyInitialStun();
        }

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref initialStunApplied, "initialStunApplied", defaultValue: false);
            Scribe_Values.Look(ref lastPosition, "lastPosition");
        }

        private void TryApplyInitialStun()
        {
            Pawn pawn = Pawn;
            if (initialStunApplied || pawn == null || pawn.Destroyed || pawn.Dead || !pawn.Spawned)
            {
                return;
            }

            initialStunApplied = true;
            lastPosition = pawn.Position;
            ApplyStun();
        }

        private void ApplyStun()
        {
            Pawn pawn = Pawn;
            if (pawn == null || pawn.Destroyed || pawn.Dead || pawn.stances?.stunner == null)
            {
                return;
            }

            pawn.stances.stunner.StunFor(Props.stunTicks, null, addBattleLog: false, showMote: false);
        }
    }
}
