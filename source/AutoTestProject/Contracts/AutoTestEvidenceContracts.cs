using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace LogSyncMod.AutoTest
{
    [DataContract]
    public sealed class AutoTestEnvironmentEvidence
    {
        public AutoTestEnvironmentEvidence() { ActiveModPackageIds = new List<string>(); }
        [DataMember(Name = "rimWorldVersion", Order = 1)] public string RimWorldVersion { get; set; }
        [DataMember(Name = "autoTestAssemblyVersion", Order = 2)] public string AutoTestAssemblyVersion { get; set; }
        [DataMember(Name = "logSyncAssemblyVersion", Order = 3)] public string LogSyncAssemblyVersion { get; set; }
        [DataMember(Name = "packageId", Order = 4)] public string PackageId { get; set; }
        [DataMember(Name = "mapId", Order = 5)] public string MapId { get; set; }
        [DataMember(Name = "mapName", Order = 6)] public string MapName { get; set; }
        [DataMember(Name = "language", Order = 7)] public string Language { get; set; }
        [DataMember(Name = "devMode", Order = 8)] public bool DevMode { get; set; }
        [DataMember(Name = "timeSpeed", Order = 9)] public string TimeSpeed { get; set; }
        [DataMember(Name = "paused", Order = 10)] public bool Paused { get; set; }
        [DataMember(Name = "activeModPackageIds", Order = 11)] public List<string> ActiveModPackageIds { get; set; }
        [DataMember(Name = "activeModsHash", Order = 12)] public string ActiveModsHash { get; set; }
    }

    [DataContract]
    public sealed class AutoTestAssertionEvidence
    {
        [DataMember(Name = "assertionId", Order = 1)] public string AssertionId { get; set; }
        [DataMember(Name = "name", Order = 2)] public string Name { get; set; }
        [DataMember(Name = "success", Order = 3)] public bool Success { get; set; }
        [DataMember(Name = "expected", Order = 4)] public string Expected { get; set; }
        [DataMember(Name = "actual", Order = 5)] public string Actual { get; set; }
        [DataMember(Name = "details", Order = 6)] public string Details { get; set; }
        [DataMember(Name = "operationId", Order = 7)] public string OperationId { get; set; }
        [DataMember(Name = "caseId", Order = 8)] public string CaseId { get; set; }
        [DataMember(Name = "stepId", Order = 9)] public string StepId { get; set; }
    }

    [DataContract]
    public sealed class AutoTestCaptureEvidence
    {
        [DataMember(Name = "captureId", Order = 1)] public string CaptureId { get; set; }
        [DataMember(Name = "label", Order = 2)] public string Label { get; set; }
        [DataMember(Name = "kind", Order = 3)] public string Kind { get; set; }
        [DataMember(Name = "path", Order = 4)] public string Path { get; set; }
        [DataMember(Name = "operationId", Order = 5)] public string OperationId { get; set; }
        [DataMember(Name = "caseId", Order = 6)] public string CaseId { get; set; }
        [DataMember(Name = "details", Order = 7)] public string Details { get; set; }
    }

    [DataContract]
    public sealed class AutoTestEventEvidence
    {
        [DataMember(Name = "sequence", Order = 1)] public long Sequence { get; set; }
        [DataMember(Name = "timestampUtc", Order = 2)] public DateTime TimestampUtc { get; set; }
        [DataMember(Name = "eventType", Order = 3)] public string EventType { get; set; }
        [DataMember(Name = "runId", Order = 4)] public string RunId { get; set; }
        [DataMember(Name = "caseId", Order = 5)] public string CaseId { get; set; }
        [DataMember(Name = "stepId", Order = 6)] public string StepId { get; set; }
        [DataMember(Name = "operationId", Order = 7)] public string OperationId { get; set; }
        [DataMember(Name = "capabilityId", Order = 8)] public string CapabilityId { get; set; }
        [DataMember(Name = "message", Order = 9)] public string Message { get; set; }
    }

    [DataContract]
    public sealed class AutoTestLogEvidence
    {
        [DataMember(Name = "sequence", Order = 1)] public long Sequence { get; set; }
        [DataMember(Name = "timestampUtc", Order = 2)] public DateTime TimestampUtc { get; set; }
        [DataMember(Name = "level", Order = 3)] public string Level { get; set; }
        [DataMember(Name = "message", Order = 4)] public string Message { get; set; }
        [DataMember(Name = "sourcePackageId", Order = 5)] public string SourcePackageId { get; set; }
        [DataMember(Name = "repeatCount", Order = 6)] public int RepeatCount { get; set; }
        [DataMember(Name = "runId", Order = 7)] public string RunId { get; set; }
        [DataMember(Name = "caseId", Order = 8)] public string CaseId { get; set; }
        [DataMember(Name = "stepId", Order = 9)] public string StepId { get; set; }
        [DataMember(Name = "operationId", Order = 10)] public string OperationId { get; set; }
        [DataMember(Name = "capabilityId", Order = 11)] public string CapabilityId { get; set; }
    }

    [DataContract]
    public sealed class AutoTestCaseEvidence
    {
        public AutoTestCaseEvidence()
        {
            Operations = new List<AutoTestOperationEnvelope>();
            Events = new List<AutoTestEventEvidence>();
            Logs = new List<AutoTestLogEvidence>();
        }

        [DataMember(Name = "caseId", Order = 1)] public string CaseId { get; set; }
        [DataMember(Name = "eventAfterSequence", Order = 2)] public long EventAfterSequence { get; set; }
        [DataMember(Name = "logAfterSequence", Order = 3)] public long LogAfterSequence { get; set; }
        [DataMember(Name = "eventThroughSequence", Order = 4)] public long EventThroughSequence { get; set; }
        [DataMember(Name = "logThroughSequence", Order = 5)] public long LogThroughSequence { get; set; }
        [DataMember(Name = "operations", Order = 6)] public List<AutoTestOperationEnvelope> Operations { get; set; }
        [DataMember(Name = "events", Order = 7)] public List<AutoTestEventEvidence> Events { get; set; }
        [DataMember(Name = "logs", Order = 8)] public List<AutoTestLogEvidence> Logs { get; set; }
    }

    [DataContract]
    public sealed class AutoTestEvidenceManifest
    {
        public AutoTestEvidenceManifest()
        {
            SchemaVersion = 1;
            Operations = new List<AutoTestOperationEnvelope>();
            Assertions = new List<AutoTestAssertionEvidence>();
            Captures = new List<AutoTestCaptureEvidence>();
            Logs = new List<AutoTestLogEvidence>();
            Events = new List<AutoTestEventEvidence>();
            Errors = new List<AutoTestOperationError>();
            Cases = new List<AutoTestCaseEvidence>();
        }

        [DataMember(Name = "schemaVersion", Order = 1)] public int SchemaVersion { get; set; }
        [DataMember(Name = "runId", Order = 2)] public string RunId { get; set; }
        [DataMember(Name = "packageId", Order = 3)] public string PackageId { get; set; }
        [DataMember(Name = "suite", Order = 4)] public string Suite { get; set; }
        [DataMember(Name = "success", Order = 5)] public bool Success { get; set; }
        [DataMember(Name = "startedAtUtc", Order = 6)] public DateTime StartedAtUtc { get; set; }
        [DataMember(Name = "completedAtUtc", Order = 7)] public DateTime? CompletedAtUtc { get; set; }
        [DataMember(Name = "environment", Order = 8)] public AutoTestEnvironmentEvidence Environment { get; set; }
        [DataMember(Name = "operations", Order = 9)] public List<AutoTestOperationEnvelope> Operations { get; set; }
        [DataMember(Name = "assertions", Order = 10)] public List<AutoTestAssertionEvidence> Assertions { get; set; }
        [DataMember(Name = "captures", Order = 11)] public List<AutoTestCaptureEvidence> Captures { get; set; }
        [DataMember(Name = "logs", Order = 12)] public List<AutoTestLogEvidence> Logs { get; set; }
        [DataMember(Name = "events", Order = 13)] public List<AutoTestEventEvidence> Events { get; set; }
        [DataMember(Name = "errors", Order = 14)] public List<AutoTestOperationError> Errors { get; set; }
        [DataMember(Name = "cases", Order = 15)] public List<AutoTestCaseEvidence> Cases { get; set; }
        [DataMember(Name = "readiness", Order = 16, EmitDefaultValue = false)] public AutoTestReadinessSnapshot Readiness { get; set; }
    }
}
