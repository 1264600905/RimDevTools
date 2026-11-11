using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using LogSyncMod.AutoTest.Scenarios;
using RimWorld;
using Verse;

namespace LogSyncMod.AutoTest
{
    internal sealed partial class ScenarioGameBackend
    {
        // Manual presenters/workers and group-combat members use the same generation and exact
        // override code as external scenes. The caller owns the returned fresh objects.
        internal static Pawn GenerateTemporaryPawn(TemporaryPawnSnapshot snapshot, Faction faction, Action<string, string> trace, out List<Thing> generated,
            bool restoreColorState = false, bool forceConfiguredXenotype = false, ResolvedSquadMember restoreMember = null, bool continueArena = false)
        {
            var frozen = TemporaryPawnData.Parse(TemporaryPawnData.Serialize(snapshot));
            var backend = new ScenarioGameBackend(null, null, null, trace ?? ((_, __) => { }));
            backend.continueArena = continueArena;
            backend.teams.Add("temporary", faction);
            var actor = XElement.Parse(frozen.TemplateXml); actor.Name = "Pawn";
            actor.SetAttributeValue("id", "temporaryPawn"); actor.SetAttributeValue("team", "temporary");
            generated = new List<Thing>();
            try
            {
                var pawn = backend.GeneratePawn(actor, restoreColorState, forceConfiguredXenotype, restoreMember);
                generated.AddRange(backend.owned);
                return pawn;
            }
            catch
            {
                var errors = new List<Exception>();
                foreach (var thing in backend.owned)
                {
                    try { if (thing is Pawn pawn && Find.WorldPawns.Contains(pawn)) Find.WorldPawns.RemovePawn(pawn); if (!thing.Destroyed) thing.Destroy(DestroyMode.Vanish); }
                    catch (Exception ex) { errors.Add(ex); }
                }
                if (errors.Count > 0) trace?.Invoke("pawn.template_cleanup_error", string.Join("; ", errors.Select(e => e.Message)));
                throw;
            }
        }
    }
}
