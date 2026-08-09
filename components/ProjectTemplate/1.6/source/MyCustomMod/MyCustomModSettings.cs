using Verse;

namespace MyCustomMod
{
    public class MyCustomModSettings : ModSettings
    {
        public bool enableLog = true;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref enableLog, nameof(enableLog), true);
        }
    }
}
