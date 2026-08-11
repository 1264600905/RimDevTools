using System;
using System.Collections.Generic;
using System.Linq;

namespace LogSyncMod.AutoTest
{
    public sealed class AutoTestOperationJournal
    {
        private readonly AutoTestBoundedBuffer<AutoTestOperationEnvelope> operations;
        private readonly AutoTestBoundedBuffer<AutoTestEventEvidence> events;
        private readonly Dictionary<string, AutoTestOperationEnvelope> byId = new Dictionary<string, AutoTestOperationEnvelope>(StringComparer.Ordinal);
        private readonly Queue<string> operationOrder = new Queue<string>();
        private readonly int operationCapacity;
        private readonly object gate = new object();
        private long nextSequence;

        public AutoTestOperationJournal(int operationCapacity = 500, int eventCapacity = 2000)
        {
            if (operationCapacity <= 0) throw new ArgumentOutOfRangeException("operationCapacity");
            this.operationCapacity = operationCapacity;
            operations = new AutoTestBoundedBuffer<AutoTestOperationEnvelope>(operationCapacity * 2);
            events = new AutoTestBoundedBuffer<AutoTestEventEvidence>(eventCapacity);
        }

        public void RecordStarted(AutoTestOperationEnvelope envelope)
        {
            if (envelope == null || string.IsNullOrEmpty(envelope.OperationId)) throw new ArgumentException("Operation id is required.");
            lock (gate)
            {
                if (byId.ContainsKey(envelope.OperationId)) throw new InvalidOperationException("Duplicate operation id: " + envelope.OperationId);
                while (byId.Count >= operationCapacity && operationOrder.Count > 0)
                {
                    string evicted = operationOrder.Dequeue();
                    byId.Remove(evicted);
                }
                AutoTestOperationEnvelope copy = Clone(envelope);
                byId[copy.OperationId] = copy;
                operationOrder.Enqueue(copy.OperationId);
                operations.Add(copy);
                RecordEventUnsafe("operation.started", copy.RunId, copy.CaseId, copy.StepId, copy.OperationId, copy.CapabilityId, copy.ResultSummary, copy.StartedAtUtc);
            }
        }

        public bool RecordTerminal(AutoTestOperationEnvelope envelope)
        {
            if (envelope == null) return false;
            lock (gate)
            {
                AutoTestOperationEnvelope current;
                if (!byId.TryGetValue(envelope.OperationId, out current)) return false;
                if (IsTerminal(current.Status))
                {
                    current.Warnings.Add("duplicate_terminal_ignored");
                    return false;
                }

                AutoTestOperationEnvelope copy = Clone(envelope);
                byId[copy.OperationId] = copy;
                operations.Add(copy);
                string eventType = copy.Status == AutoTestOperationStatus.Completed ? "operation.completed" :
                    copy.Status == AutoTestOperationStatus.TimedOut ? "operation.timed_out" :
                    copy.Status == AutoTestOperationStatus.Cancelled ? "operation.cancelled" : "operation.failed";
                RecordEventUnsafe(eventType, copy.RunId, copy.CaseId, copy.StepId, copy.OperationId, copy.CapabilityId,
                    copy.Error == null ? copy.ResultSummary : copy.Error.Code + ": " + copy.Error.Message,
                    copy.CompletedAtUtc ?? DateTime.UtcNow);
                return true;
            }
        }

        public AutoTestEventEvidence RecordEvent(string eventType, string runId, string caseId, string stepId, string operationId, string capabilityId, string message, DateTime? timestampUtc = null)
        {
            lock (gate) return RecordEventUnsafe(eventType, runId, caseId, stepId, operationId, capabilityId, message, timestampUtc ?? DateTime.UtcNow);
        }

        public AutoTestOperationEnvelope GetOperation(string operationId)
        {
            lock (gate)
            {
                AutoTestOperationEnvelope value;
                return byId.TryGetValue(operationId ?? string.Empty, out value) ? Clone(value) : null;
            }
        }

        public List<AutoTestOperationEnvelope> GetRecentOperations(int limit, bool includeDetails)
        {
            if (limit <= 0) return new List<AutoTestOperationEnvelope>();
            List<AutoTestOperationEnvelope> snapshot = operations.Snapshot();
            var latest = new Dictionary<string, AutoTestOperationEnvelope>(StringComparer.Ordinal);
            foreach (AutoTestOperationEnvelope item in snapshot) latest[item.OperationId] = item;
            return latest.Values.OrderByDescending(item => item.StartedAtUtc).Take(limit).Reverse().Select(Clone).ToList();
        }

        public void MarkCaseOperationsOptional(string caseId)
        {
            if (string.IsNullOrWhiteSpace(caseId)) return;
            lock (gate)
            {
                foreach (AutoTestOperationEnvelope operation in byId.Values.Where(item =>
                    string.Equals(item.CaseId, caseId, StringComparison.OrdinalIgnoreCase)))
                    operation.Required = false;
            }
        }

        public List<AutoTestEventEvidence> GetEvents(long afterSequence, int limit, string operationId = null)
        {
            if (limit <= 0) return new List<AutoTestEventEvidence>();
            return events.Snapshot()
                .Where(item => item.Sequence > afterSequence && (operationId == null || item.OperationId == operationId))
                .OrderBy(item => item.Sequence).Take(limit).Select(Clone).ToList();
        }

        public long LastEventSequence { get { lock (gate) return nextSequence; } }

        private AutoTestEventEvidence RecordEventUnsafe(string eventType, string runId, string caseId, string stepId, string operationId, string capabilityId, string message, DateTime timestampUtc)
        {
            AutoTestEventEvidence item = new AutoTestEventEvidence
            {
                Sequence = ++nextSequence,
                TimestampUtc = timestampUtc,
                EventType = eventType,
                RunId = runId,
                CaseId = caseId,
                StepId = stepId,
                OperationId = operationId,
                CapabilityId = capabilityId,
                Message = message
            };
            events.Add(item);
            return Clone(item);
        }

        private static bool IsTerminal(AutoTestOperationStatus status)
        {
            return status == AutoTestOperationStatus.Completed || status == AutoTestOperationStatus.Failed ||
                status == AutoTestOperationStatus.TimedOut || status == AutoTestOperationStatus.Cancelled;
        }

        internal static AutoTestOperationEnvelope Clone(AutoTestOperationEnvelope value)
        {
            if (value == null) return null;
            return new AutoTestOperationEnvelope
            {
                OperationId = value.OperationId, RunId = value.RunId, CaseId = value.CaseId, StepId = value.StepId,
                CapabilityId = value.CapabilityId, Status = value.Status, Success = value.Success, Required = value.Required,
                StartedAtUtc = value.StartedAtUtc, CompletedAtUtc = value.CompletedAtUtc, DurationMs = value.DurationMs,
                ResultSummary = value.ResultSummary,
                Error = value.Error == null ? null : new AutoTestOperationError { Code = value.Error.Code, Message = value.Error.Message, ExceptionType = value.Error.ExceptionType, StackTrace = value.Error.StackTrace },
                Warnings = value.Warnings == null ? new List<string>() : new List<string>(value.Warnings)
            };
        }

        private static AutoTestEventEvidence Clone(AutoTestEventEvidence value)
        {
            return new AutoTestEventEvidence { Sequence = value.Sequence, TimestampUtc = value.TimestampUtc, EventType = value.EventType,
                RunId = value.RunId, CaseId = value.CaseId, StepId = value.StepId, OperationId = value.OperationId,
                CapabilityId = value.CapabilityId, Message = value.Message };
        }
    }
}
