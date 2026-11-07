using System;
using System.Linq;
using System.Text.Json;
using LogSyncMod.AutoTest.Scenarios;
using UnityEngine;
using Verse;

namespace LogSyncMod.AutoTest
{
    // Authenticated native business API for test execution and data verification.
    // Window interaction remains a player operation; no external scenario rules are loaded here.
    internal static class ManualTestNativeApi
    {
        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true };
        internal static string Handle(string body)
        {
            try
            {
                using var json = JsonDocument.Parse(body); var request = json.RootElement;
                string Text(string field) => request.TryGetProperty(field, out var value) ? value.GetString() : null;
                string[] Values(string field) => request.TryGetProperty(field, out var value) ? value.EnumerateArray().Select(v => v.GetString()).ToArray() : Array.Empty<string>();
                if (Text("projectRoot") != null) ManualProjectBinding.Bind(Text("projectRoot"));
                var operation = Text("operation");
                var readiness = AutoTestReadinessProbe.Capture();
                if (operation != "status" && operation != "exit" && !readiness.AutomationReady) throw new InvalidOperationException("Runtime is not ready: " + readiness.Summary);
                object result;
                switch (operation)
                {
                    case "verify-pawn-data": result = TemporaryPawnDataProbe.Run(); break;
                    case "status":
                        result = new { busy = ManualTestService.Busy, progress = ManualTestService.Progress, readiness, dataVerification = TemporaryPawnDataProbe.LastResult, gameLaunchId = ManualTestService.GameLaunchId, manualSessionId = ManualTestService.ManualSessionId, artifacts = ManualTestService.Artifacts, invocations = ManualTestService.Invocations.Select(i => new { i.InvocationId, i.Status, resultCount = i.Results.Count, i.Error }) }; break;
                    case "catalog":
                        var search = Text("search") ?? "";
                        result = new { categories = ManualTestDebugActions.Catalog.Categories, targets = ManualTestDebugActions.Catalog.Targets.Where(t => (t.Key + " " + t.Label).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0).Take(100), capabilities = ManualTestDebugActions.Catalog.Capabilities.Where(c => c.TargetKey.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0).Take(300) }; break;
                    case "run":
                        var invocation = ManualTestService.Submit(ManualProjectBinding.Resolve(), ManualTestDebugActions.Catalog, new ManualTestSelection { Targets = Values("targetKeys").ToList(), Categories = Values("categories").ToList() });
                        result = new { invocationId = invocation.InvocationId, artifacts = ManualTestService.Artifacts }; break;
                    case "cancel": ManualTestService.Cancel(); result = new { requested = true }; break;
                    case "pawns": result = Find.Maps.SelectMany(m => m.mapPawns.AllPawnsSpawned).Select(p => new { id = p.GetUniqueLoadID(), name = p.LabelShort, kind = p.kindDef.defName, map = p.Map.uniqueID, player = p.Faction == RimWorld.Faction.OfPlayer }).ToList(); break;
                    case "capture":
                        var pawn = Find.Maps.SelectMany(m => m.mapPawns.AllPawnsSpawned).Single(p => p.GetUniqueLoadID() == Text("pawnId"));
                        var context = ManualProjectBinding.Resolve(); var capturedPath = ManualTestDebugActions.CaptureForProject(pawn, context); var staged = TemporaryPawnData.Load(context.Root);
                        result = new { path = capturedPath, hash = staged.Sha256, staged.RecordedFields, staged.UnrecordedFields }; break;
                    case "exit":
                        if (ManualTestService.Busy || global::LogSyncMod.LogSyncMod.settings is not { testSessionActive: true, testSessionKind: "manual" }) throw new InvalidOperationException("Exit is only available for an idle CLI manual test process");
                        LongEventHandler.ExecuteWhenFinished(Application.Quit); result = new { exiting = true }; break;
                    default: throw new InvalidOperationException("Unknown native manual-test operation");
                }
                return JsonSerializer.Serialize(new { ok = true, result }, Json);
            }
            catch (Exception ex) { return JsonSerializer.Serialize(new { ok = false, error = ex.Message, artifacts = ManualTestService.Artifacts }, Json); }
        }
    }
}
