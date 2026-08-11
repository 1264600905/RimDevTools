using System;
using System.Collections.Generic;
using System.Linq;

namespace LogSyncMod.AutoTest
{
    public static class AutoTestRuntimeApi
    {
        public static AutoTestRuntimeStatusDto GetStatus() { return AutoTestBootstrap.GetRuntimeStatus(); }

        public static IReadOnlyList<AutoTestSuiteDescriptorDto> ListSuites()
        {
            return new List<AutoTestSuiteDescriptorDto>
            {
                Suite("standard", "Standard", true),
                Suite("ui", "UI", true),
                Suite("combat", "Combat", true),
                Suite("view", "View", true),
                Suite("misc", "Misc", true)
            }.AsReadOnly();
        }

        public static AutoTestRunRequestResultDto RequestRun(AutoTestRunRequestDto request) { return AutoTestBootstrap.RequestRuntimeRun(request); }
        public static AutoTestCancelResultDto Cancel(string runId) { return AutoTestBootstrap.RequestRuntimeCancel(runId); }
        public static AutoTestEvidenceSnapshotDto GetEvidence(string runId) { return AutoTestBootstrap.GetRuntimeEvidence(runId); }

        private static AutoTestSuiteDescriptorDto Suite(string id, string label, bool filter)
        {
            return new AutoTestSuiteDescriptorDto { Id = id, Label = label, SupportsDefNameFilter = filter };
        }
    }
}
