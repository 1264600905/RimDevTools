using System;

namespace LogSyncMod
{
    public sealed class LogSyncCapturedEntry
    {
        public DateTime TimestampUtc { get; set; }
        public string Level { get; set; }
        public string Text { get; set; }
        public string SourcePackageId { get; set; }
    }
}
