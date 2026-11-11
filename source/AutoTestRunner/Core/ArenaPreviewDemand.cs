namespace LogSyncMod.AutoTest
{
    // One generation attempt per immutable configuration; transient UI edits age out.
    public sealed class ArenaPreviewDemand
    {
        private readonly double due;
        private double lastSeen;
        private bool attempted;
        public ArenaPreviewDemand(double now) { due = now + .3; lastSeen = now; }
        public void Touch(double now) => lastSeen = now;
        public bool IsReady(double now) => !attempted && now >= due && now - lastSeen < .15;
        public bool TryBegin(double now)
        {
            if (!IsReady(now)) return false;
            attempted = true; return true;
        }
    }
}
