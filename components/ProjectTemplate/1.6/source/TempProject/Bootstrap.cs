using Verse;

namespace TempProject
{
    [StaticConstructorOnStartup]
    public static class Bootstrap
    {
        static Bootstrap()
        {
            Log.Message("[TempProject] Initialized.");
        }
    }
}
