using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using RimWorld;
using UnityEngine;
using LogSyncMod.AutoTest.Scenarios;
using Verse;

namespace LogSyncMod.AutoTest
{
    internal static class ManualPawnGeneration
    {
        [ThreadStatic] private static ManualRunnerOptions current;
        internal static IDisposable Use(ManualRunnerOptions options) { var previous = current; current = options; return new Scope(() => current = previous); }
        internal static bool HasTemplate => current?.PawnSnapshot != null;
        internal static bool InstancePawn => current?.InstancePawn == true;
        internal static PawnKindDef BaseKind => HasTemplate ? DefDatabase<PawnKindDef>.GetNamed(XElement.Parse(current.PawnSnapshot.TemplateXml).Element("basePawnKind").Value.Trim()) : null;
        internal static bool IsStaged(Pawn pawn) => pawn != null && current?.GeneratedPawns.Contains(pawn) == true;

        internal static Pawn Presenter(PawnGenerationRequest request)
        {
            if (!HasTemplate) return PawnGenerator.GeneratePawn(request);
            var pawn = ScenarioGameBackend.GenerateTemporaryPawn(current.PawnSnapshot, request.Faction, null, out var objects);
            current.GeneratedObjects.AddRange(objects); current.GeneratedPawns.Add(pawn);
            return pawn;
        }

        internal static Thing MakeThing(ThingDef def, ThingDef stuff = null)
        {
            var style = current?.ThingSnapshot;
            if (style == null || style.DefName != def.defName) return ThingMaker.MakeThing(def, stuff);
            var thing = ThingMaker.MakeThing(def, style.Stuff == null ? null : DefDatabase<ThingDef>.GetNamed(style.Stuff));
            if (style.Quality != null) thing.TryGetComp<CompQuality>().SetQuality(Enum.Parse<QualityCategory>(style.Quality), ArtGenerationContext.Outsider);
            if (style.Color != null)
            {
                if (!ColorUtility.TryParseHtmlString(style.Color, out var color)) throw new InvalidOperationException("Invalid frozen instance color");
                (thing.TryGetComp<CompColorable>() ?? throw new InvalidOperationException("Instance color component cannot be reproduced")).SetColor(color);
            }
            if (style.HitPoints.HasValue) thing.HitPoints = Math.Min(style.HitPoints.Value, thing.MaxHitPoints);
            thing.stackCount = style.StackCount;
            return thing;
        }

        private sealed class Scope : IDisposable
        {
            private Action restore;
            internal Scope(Action restore) { this.restore = restore; }
            public void Dispose() { var action = restore; restore = null; action?.Invoke(); }
        }
    }
}
