using System.Collections.Generic;
using System.Runtime.Serialization;

namespace LogSyncMod.AutoTest
{
    [DataContract]
    public sealed class AutoTestNotificationItem
    {
        [DataMember(Name = "kind", Order = 1)] public string Kind { get; set; }
        [DataMember(Name = "id", Order = 2)] public string Id { get; set; }
        [DataMember(Name = "label", Order = 3)] public string Label { get; set; }
        [DataMember(Name = "text", Order = 4)] public string Text { get; set; }
        [DataMember(Name = "defName", Order = 5)] public string DefName { get; set; }
        [DataMember(Name = "targetSummary", Order = 6)] public string TargetSummary { get; set; }
        [DataMember(Name = "tick", Order = 7)] public int Tick { get; set; }
        [DataMember(Name = "dismissible", Order = 8)] public bool Dismissible { get; set; }
    }

    [DataContract]
    public sealed class AutoTestNotificationSnapshot
    {
        public AutoTestNotificationSnapshot()
        {
            Messages = new List<AutoTestNotificationItem>();
            Letters = new List<AutoTestNotificationItem>();
            Alerts = new List<AutoTestNotificationItem>();
        }
        [DataMember(Name = "available", Order = 1)] public bool Available { get; set; }
        [DataMember(Name = "unavailableReason", Order = 2)] public string UnavailableReason { get; set; }
        [DataMember(Name = "messages", Order = 3)] public List<AutoTestNotificationItem> Messages { get; set; }
        [DataMember(Name = "letters", Order = 4)] public List<AutoTestNotificationItem> Letters { get; set; }
        [DataMember(Name = "alerts", Order = 5)] public List<AutoTestNotificationItem> Alerts { get; set; }
        [DataMember(Name = "summary", Order = 6)] public string Summary { get; set; }
    }

    [DataContract]
    public sealed class AutoTestErrorPolicyResult
    {
        public AutoTestErrorPolicyResult()
        {
            UnexpectedSevereLogs = new List<AutoTestLogEvidence>();
            AllowedSevereLogs = new List<AutoTestLogEvidence>();
            AllowedRules = new List<string>();
            Warnings = new List<AutoTestLogEvidence>();
        }
        [DataMember(Name = "success", Order = 1)] public bool Success { get; set; }
        [DataMember(Name = "unexpectedSevereLogs", Order = 2)] public List<AutoTestLogEvidence> UnexpectedSevereLogs { get; set; }
        [DataMember(Name = "allowedSevereLogs", Order = 3)] public List<AutoTestLogEvidence> AllowedSevereLogs { get; set; }
        [DataMember(Name = "allowedRules", Order = 4)] public List<string> AllowedRules { get; set; }
        [DataMember(Name = "warnings", Order = 5)] public List<AutoTestLogEvidence> Warnings { get; set; }
        [DataMember(Name = "summary", Order = 6)] public string Summary { get; set; }
    }
}
