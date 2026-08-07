using Verse;

namespace MyCustomMod
{
    public class MyCustomModSettings : ModSettings
    {
        public bool enableLog = true;
        public bool enableShaderDemo = true;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref enableLog, nameof(enableLog), true);
            Scribe_Values.Look(ref enableShaderDemo, nameof(enableShaderDemo), true);
        }
    }
}
