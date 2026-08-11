using System.Runtime.Serialization;

namespace LogSyncMod.AutoTest
{
    [DataContract]
    public sealed class AutoTestTargetRef
    {
        [DataMember(Name = "id", Order = 1)] public string Id { get; set; }
        [DataMember(Name = "kind", Order = 2)] public string Kind { get; set; }
        [DataMember(Name = "label", Order = 3)] public string Label { get; set; }
        [DataMember(Name = "defName", Order = 4)] public string DefName { get; set; }
        [DataMember(Name = "mapId", Order = 5)] public int MapId { get; set; }
        [DataMember(Name = "thingId", Order = 6)] public int ThingId { get; set; }
        [DataMember(Name = "fingerprint", Order = 7)] public string Fingerprint { get; set; }
    }

    [DataContract]
    public sealed class AutoTestTargetResolution
    {
        [DataMember(Name = "status", Order = 1)] public string Status { get; set; }
        [DataMember(Name = "target", Order = 2)] public AutoTestTargetRef Target { get; set; }
        [DataMember(Name = "message", Order = 3)] public string Message { get; set; }
        [IgnoreDataMember] public object ResolvedObject { get; set; }
        [IgnoreDataMember] public bool Resolved { get { return string.Equals(Status, "resolved", System.StringComparison.Ordinal); } }
    }

    [DataContract]
    public sealed class AutoTestGizmoRef
    {
        [DataMember(Name = "id", Order = 1)] public string Id { get; set; }
        [DataMember(Name = "selectionFingerprint", Order = 2)] public string SelectionFingerprint { get; set; }
        [DataMember(Name = "ordinal", Order = 3)] public int Ordinal { get; set; }
        [DataMember(Name = "semanticFingerprint", Order = 4)] public string SemanticFingerprint { get; set; }
        [DataMember(Name = "label", Order = 5)] public string Label { get; set; }
        [DataMember(Name = "disabled", Order = 6)] public bool Disabled { get; set; }
        [DataMember(Name = "disabledReason", Order = 7)] public string DisabledReason { get; set; }
    }
}
