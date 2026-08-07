using Verse;

namespace TemplateMod
{
    public sealed class CompProperties_Template : CompProperties
    {
        public int intervalTicks = 60;

        public CompProperties_Template()
        {
            compClass = typeof(CompTemplate);
        }
    }

    public sealed class CompTemplate : ThingComp
    {
        private int elapsedTicks;

        private CompProperties_Template Props => (CompProperties_Template)props;

        public override void CompTick()
        {
            base.CompTick();
            if (!parent.Spawned || Props.intervalTicks <= 0)
            {
                return;
            }

            elapsedTicks++;
            if (elapsedTicks >= Props.intervalTicks)
            {
                elapsedTicks = 0;
                PerformIntervalAction();
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref elapsedTicks, "elapsedTicks", 0);
        }

        private void PerformIntervalAction()
        {
            // Implement bounded behavior. Prefer rare/long ticks when suitable.
        }
    }
}
