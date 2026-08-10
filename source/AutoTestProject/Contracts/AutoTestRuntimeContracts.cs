using System.Collections.Generic;
using System.Runtime.Serialization;

namespace LogSyncMod.AutoTest
{
    [DataContract]
    public sealed class AutoTestRuntimeStatusDto
    {
        [DataMember(Name = "state", Order = 1)] public string State { get; set; }
        [DataMember(Name = "runId", Order = 2)] public string RunId { get; set; }
        [DataMember(Name = "packageId", Order = 3)] public string PackageId { get; set; }
        [DataMember(Name = "completed", Order = 4)] public bool Completed { get; set; }
        [DataMember(Name = "resultCount", Order = 5)] public int ResultCount { get; set; }
        [DataMember(Name = "summary", Order = 6)] public string Summary { get; set; }
    }

    [DataContract]
    public sealed class AutoTestSuiteDescriptorDto
    {
        [DataMember(Name = "id", Order = 1)] public string Id { get; set; }
        [DataMember(Name = "label", Order = 2)] public string Label { get; set; }
        [DataMember(Name = "supportsDefNameFilter", Order = 3)] public bool SupportsDefNameFilter { get; set; }
    }

    [DataContract]
    public sealed class AutoTestRunRequestDto
    {
        public AutoTestRunRequestDto() { Scopes = new List<string>(); DefNames = new List<string>(); }
        [DataMember(Name = "runId", Order = 1)] public string RunId { get; set; }
        [DataMember(Name = "packageId", Order = 2)] public string PackageId { get; set; }
        [DataMember(Name = "scopes", Order = 3)] public List<string> Scopes { get; set; }
        [DataMember(Name = "defNames", Order = 4)] public List<string> DefNames { get; set; }
        [DataMember(Name = "captureScreenshots", Order = 5)] public bool CaptureScreenshots { get; set; }
        [DataMember(Name = "mode", Order = 6)] public string Mode { get; set; }
        [DataMember(Name = "outputPath", Order = 7)] public string OutputPath { get; set; }
    }

    [DataContract]
    public sealed class AutoTestRunRequestResultDto
    {
        [DataMember(Name = "accepted", Order = 1)] public bool Accepted { get; set; }
        [DataMember(Name = "runId", Order = 2)] public string RunId { get; set; }
        [DataMember(Name = "errorCode", Order = 3)] public string ErrorCode { get; set; }
        [DataMember(Name = "message", Order = 4)] public string Message { get; set; }
    }

    [DataContract]
    public sealed class AutoTestCancelResultDto
    {
        [DataMember(Name = "accepted", Order = 1)] public bool Accepted { get; set; }
        [DataMember(Name = "runId", Order = 2)] public string RunId { get; set; }
        [DataMember(Name = "errorCode", Order = 3)] public string ErrorCode { get; set; }
        [DataMember(Name = "message", Order = 4)] public string Message { get; set; }
    }

    [DataContract]
    public sealed class AutoTestEvidenceSnapshotDto
    {
        [DataMember(Name = "found", Order = 1)] public bool Found { get; set; }
        [DataMember(Name = "runId", Order = 2)] public string RunId { get; set; }
        [DataMember(Name = "success", Order = 3)] public bool Success { get; set; }
        [DataMember(Name = "operationCount", Order = 4)] public int OperationCount { get; set; }
        [DataMember(Name = "assertionCount", Order = 5)] public int AssertionCount { get; set; }
        [DataMember(Name = "failedAssertionCount", Order = 6)] public int FailedAssertionCount { get; set; }
        [DataMember(Name = "errorCount", Order = 7)] public int ErrorCount { get; set; }
        [DataMember(Name = "caseCount", Order = 8)] public int CaseCount { get; set; }
        [DataMember(Name = "errorCode", Order = 9)] public string ErrorCode { get; set; }
    }
}
