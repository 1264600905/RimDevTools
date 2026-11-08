using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using HarmonyLib;
using LogSyncMod.AutoTest.Scenarios;
using UnityEngine;
using Verse;

namespace LogSyncMod.AutoTest
{
    // Observe the existing start sequence without changing its pauses, speed or AI.
    internal static class CombatStartupProbe
    {
        internal static double Now => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
        private static CombatStartupTiming result;
        private static double? labelAt, speedAt, lastUpdate, lastRepaint;
        private static Dictionary<Pawn, IntVec3> initialPositions;
        private static bool Observing => result != null && labelAt.HasValue && (!speedAt.HasValue || Now - speedAt.Value <= 10);
        private static CombatStartupWindow Window => speedAt.HasValue ? result.Running : result.Waiting;
        private static double Relative => Now - labelAt.Value;

        internal static CombatStartupTiming Reset()
        {
            labelAt = speedAt = lastUpdate = lastRepaint = null;
            initialPositions = null;
            return result = new CombatStartupTiming();
        }
        internal static void Stop() { result = null; initialPositions = null; }
        internal static void Repaint(string label)
        {
            if (result == null) return;
            if (!labelAt.HasValue && label == "开始") labelAt = Now;
            if (!Observing) return;
            var now = Now;
            result.UiThreadId = Thread.CurrentThread.ManagedThreadId;
            Window.Repaints++;
            if (lastRepaint.HasValue) Window.MaxRepaintGapSeconds = System.Math.Max(Window.MaxRepaintGapSeconds, now - lastRepaint.Value);
            lastRepaint = now;
            if (speedAt.HasValue && !result.FirstRepaintAfterSpeedSeconds.HasValue) result.FirstRepaintAfterSpeedSeconds = now - speedAt.Value;
        }
        internal static void UpdateBegin()
        {
            if (!Observing) return;
            var now = Now;
            result.UpdateThreadId = Thread.CurrentThread.ManagedThreadId;
            Window.Updates++;
            if (lastUpdate.HasValue) Window.MaxUpdateGapSeconds = System.Math.Max(Window.MaxUpdateGapSeconds, now - lastUpdate.Value);
            lastUpdate = now;
            if (speedAt.HasValue) result.ObservedRunningSeconds = now - speedAt.Value;
        }
        internal static void UpdateEnd(double began)
        {
            if (Observing) Window.MaxUpdateSeconds = System.Math.Max(Window.MaxUpdateSeconds, Now - began);
        }
        internal static void UiEnd(double began)
        {
            if (Observing) Window.MaxUiSeconds = System.Math.Max(Window.MaxUiSeconds, Now - began);
        }
        internal static void TickEnd(double began)
        {
            if (!Observing || !speedAt.HasValue) return;
            result.TickThreadId = Thread.CurrentThread.ManagedThreadId;
            result.FirstTickSeconds ??= Relative;
            result.Running.Ticks++;
            result.Running.MaxTickSeconds = System.Math.Max(result.Running.MaxTickSeconds, Now - began);
            if (!result.FirstMovementSeconds.HasValue && initialPositions.Any(p => p.Key.Spawned && p.Key.Position != p.Value)) result.FirstMovementSeconds = Relative;
        }
        internal static void CaptureRequested() { if (Observing) result.InitialCaptureRequestedSeconds = Relative; }
        internal static void CaptureCall(double began) { if (Observing) result.InitialCaptureCallSeconds = Now - began; }
        internal static void CaptureReady() { if (Observing) result.InitialCaptureReadySeconds ??= Relative; }
        internal static void SpeedSwitch(string speed, IEnumerable<Pawn> pawns)
        {
            if (result == null || !labelAt.HasValue) return;
            speedAt = Now;
            result.ConfiguredSpeed = speed;
            result.TickRateMultiplier = Find.TickManager.TickRateMultiplier;
            result.SpeedSwitchSeconds = speedAt.Value - labelAt.Value;
            initialPositions = pawns.ToDictionary(p => p, p => p.Position);
        }
        internal static void MeasurementStarted(double began) { if (Observing) result.StartMeasurementSeconds = Now - began; }
    }

    [HarmonyPatch(typeof(Root_Play), nameof(Root_Play.Update))]
    internal static class CombatStartupUpdateProbe
    {
        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        private static void Prefix(out double __state) { __state = CombatStartupProbe.Now; CombatStartupProbe.UpdateBegin(); }
        [HarmonyPostfix, HarmonyPriority(Priority.Last)]
        private static void Postfix(double __state) => CombatStartupProbe.UpdateEnd(__state);
    }
    [HarmonyPatch(typeof(WindowStack), nameof(WindowStack.WindowStackOnGUI))]
    internal static class CombatStartupUiProbe
    {
        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        private static void Prefix(out double __state) => __state = CombatStartupProbe.Now;
        [HarmonyPostfix, HarmonyPriority(Priority.Last)]
        private static void Postfix(double __state) => CombatStartupProbe.UiEnd(__state);
    }
    [HarmonyPatch(typeof(TickManager), nameof(TickManager.DoSingleTick))]
    internal static class CombatStartupTickProbe
    {
        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        private static void Prefix(out double __state) => __state = CombatStartupProbe.Now;
        [HarmonyPostfix, HarmonyPriority(Priority.Last)]
        private static void Postfix(double __state) => CombatStartupProbe.TickEnd(__state);
    }
}
