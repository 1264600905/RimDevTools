using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace LogSyncMod.AutoTest
{
    [DataContract]
    public enum AutoTestOperationStatus
    {
        [EnumMember] Pending,
        [EnumMember] Running,
        [EnumMember] Completed,
        [EnumMember] Failed,
        [EnumMember] TimedOut,
        [EnumMember] Cancelled
    }

    [DataContract]
    public sealed class AutoTestOperationError
    {
        [DataMember(Name = "code", Order = 1)] public string Code { get; set; }
        [DataMember(Name = "message", Order = 2)] public string Message { get; set; }
        [DataMember(Name = "exceptionType", Order = 3)] public string ExceptionType { get; set; }
        [DataMember(Name = "stackTrace", Order = 4)] public string StackTrace { get; set; }
    }

    [DataContract]
    public sealed class AutoTestOperationEnvelope
    {
        public AutoTestOperationEnvelope()
        {
            Warnings = new List<string>();
            Required = true;
        }

        [DataMember(Name = "operationId", Order = 1)] public string OperationId { get; set; }
        [DataMember(Name = "runId", Order = 2)] public string RunId { get; set; }
        [DataMember(Name = "caseId", Order = 3)] public string CaseId { get; set; }
        [DataMember(Name = "stepId", Order = 4)] public string StepId { get; set; }
        [DataMember(Name = "capabilityId", Order = 5)] public string CapabilityId { get; set; }
        [IgnoreDataMember] public AutoTestOperationStatus Status { get; set; }
        [DataMember(Name = "status", Order = 6)]
        public string SerializedStatus
        {
            get { return Status.ToString(); }
            set
            {
                AutoTestOperationStatus parsed;
                Status = Enum.TryParse(value, true, out parsed) ? parsed : AutoTestOperationStatus.Pending;
            }
        }
        [DataMember(Name = "success", Order = 7)] public bool Success { get; set; }
        [DataMember(Name = "required", Order = 8)] public bool Required { get; set; }
        [DataMember(Name = "startedAtUtc", Order = 9)] public DateTime StartedAtUtc { get; set; }
        [DataMember(Name = "completedAtUtc", Order = 10)] public DateTime? CompletedAtUtc { get; set; }
        [DataMember(Name = "durationMs", Order = 11)] public long DurationMs { get; set; }
        [DataMember(Name = "resultSummary", Order = 12)] public string ResultSummary { get; set; }
        [DataMember(Name = "error", Order = 13)] public AutoTestOperationError Error { get; set; }
        [DataMember(Name = "warnings", Order = 14)] public List<string> Warnings { get; set; }
    }
}
