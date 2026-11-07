using System;

namespace LogSyncMod.AutoTest
{
    // One instance is held for the lifetime of the game process, independently of saves/projects.
    public sealed class ManualArtifactSession
    {
        public string GameLaunchId { get; } = Guid.NewGuid().ToString("N");
        public string ManualSessionId { get; private set; }
        public TestArtifactDirectory Artifacts { get; private set; }
        public TestArtifactDirectory Ensure(string inputProject)
        {
            if (Artifacts != null) return Artifacts;
            var paths = TestArtifactDirectory.Allocate(inputProject, "manual", GameLaunchId);
            ManualSessionId = Guid.NewGuid().ToString("N"); Artifacts = paths;
            return paths;
        }
    }
}
