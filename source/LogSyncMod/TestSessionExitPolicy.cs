using System;

namespace LogSyncMod
{
    // Pure completion gate, shared by every command-launched test kind. A normal game or
    // a stale/foreign completion cannot close the player's session.
    public sealed class TestSessionExitPolicy
    {
        public string RunId { get; private set; }
        public string Kind { get; private set; }
        public bool Pending { get; private set; }
        private long requestedAt;
        private long heartbeatAtRequest;
        private bool consumed;
        public bool Request(string kind, string runId, bool sessionActive, string sessionKind, string sessionRunId, long now, long heartbeatSequence)
        {
            if (!sessionActive || string.IsNullOrWhiteSpace(runId) || runId != sessionRunId || kind != sessionKind || kind != "quick" && kind != "auto" && kind != "groupcombat" && kind != "manual") return false;
            if (Pending) return RunId == runId && Kind == kind;
            RunId = runId; Kind = kind; requestedAt = now; heartbeatAtRequest = heartbeatSequence; Pending = true; consumed = false; return true;
        }
        public bool ShouldExit(bool sessionActive, string sessionKind, string sessionRunId, long now, long heartbeatSequence)
        {
            if (!Pending || consumed || !sessionActive || RunId != sessionRunId || Kind != sessionKind || now - requestedAt < 1000 || heartbeatSequence <= heartbeatAtRequest) return false;
            consumed = true; return true;
        }
    }
}
