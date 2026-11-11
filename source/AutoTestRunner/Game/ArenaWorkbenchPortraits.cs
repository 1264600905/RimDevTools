using System;
using System.Collections.Generic;
using System.Linq;
using LogSyncMod.AutoTest.Scenarios;
using RimWorld;
using UnityEngine;
using Verse;

namespace LogSyncMod.AutoTest
{
    // Cache only textures, never living pawns. Demand comes from visible UI rows.
    internal sealed class ArenaWorkbenchPortraits : IDisposable
    {
        internal sealed class Entry
        {
            internal string Xml, Faction, Error, CapturedXml, Weapon, NormalizedXml;
            internal bool Headgear;
            internal int Seed;
            internal ArenaPreviewDemand Demand;
            internal long Priority;
            internal RenderTexture Texture;
        }
        private readonly Dictionary<string, Entry> entries = new();
        private long priority;
        private int processedFrame = -1;
        internal Entry Request(ArenaWorkbenchMember member, string faction, int seed, bool headgear = false)
        {
            var canonical = member.Read().ToString(System.Xml.Linq.SaveOptions.DisableFormatting);
            var key = canonical + "\n" + faction + "\n" + seed + "\n" + headgear;
            if (!entries.TryGetValue(key, out var entry))
            {
                entry = new Entry { Xml = canonical, Faction = faction, Headgear = headgear, Seed = seed == -1 ? 170031 : seed, Demand = new ArenaPreviewDemand(Time.realtimeSinceStartup) };
                entries.Add(key, entry);
            }
            entry.Demand.Touch(Time.realtimeSinceStartup); entry.Priority = ++priority;
            return entry;
        }
        internal void ProcessOne()
        {
            if (Event.current?.type != EventType.Repaint || processedFrame == Time.frameCount || LongEventHandler.AnyEventNowOrWaiting) return;
            processedFrame = Time.frameCount;
            var now = Time.realtimeSinceStartup;
            var entry = entries.Values.Where(e => e.Demand.IsReady(now)).OrderByDescending(e => e.Priority).FirstOrDefault();
            if (entry != null && entry.Demand.TryBegin(now)) Generate(entry);
            foreach (var old in entries.OrderByDescending(e => e.Value.Priority).Skip(48).Select(e => e.Key).ToList())
            { Release(entries[old]); entries.Remove(old); }
        }
        internal static bool HumanBody(ArenaWorkbenchMember member)
        {
            var xml = member.Read();
            var race = DefDatabase<ThingDef>.GetNamedSilentFail(xml.Element("race")?.Value ?? "")
                ?? DefDatabase<PawnKindDef>.GetNamedSilentFail(xml.Element("basePawnKind")?.Value ?? "")?.race;
            return race?.race?.body == BodyDefOf.Human;
        }
        internal static Entry Capture(ArenaWorkbenchMember member, string faction, int seed, bool headgear)
        {
            var e = new Entry { Xml = member.PawnXml, Faction = faction, Seed = seed == -1 ? 170031 : seed, Headgear = headgear };
            Generate(e); return e;
        }
        private static void Generate(Entry entry)
        {
            var owned = new List<Thing>();
            using var scope = new PawnPortraitRendering.TargetScope(RenderTexture.active);
            Rand.PushState(entry.Seed);
            try
            {
                entry.NormalizedXml = ArenaWorkbenchApparel.Normalize(entry.Xml, entry.Faction, entry.Seed);
                var xml = ArenaWorkbench.ReadXml(entry.NormalizedXml);
                ArenaXenotypeSelection.EnsureGenes(xml); entry.NormalizedXml = xml.ToString();
                ArenaWorkbenchDefinitions.ValidatePawn(xml);
                var snapshot = TemporaryPawnData.Create(xml, "arena.preview", Array.Empty<string>(), Array.Empty<string>());
                using var faction = new SquadGeneration.FactionScope(entry.Faction, "角色预览");
                var pawn = ScenarioGameBackend.GenerateTemporaryPawn(snapshot, faction.Faction, null, out owned, forceConfiguredXenotype: true);
                entry.CapturedXml = TemporaryPawnCapture.Capture(pawn).TemplateXml;
                entry.Weapon = pawn.equipment?.Primary?.def.defName;
                if (pawn.RaceProps.body == BodyDefOf.Human)
                {
                    pawn.Drawer.renderer.SetAllGraphicsDirty(); pawn.Drawer.renderer.EnsureGraphicsInitialized();
                    entry.Texture = new RenderTexture(256, 256, 24); entry.Texture.Create();
                    PawnPortraitRendering.Render(pawn, entry.Texture, entry.Headgear);
                }
            }
            catch (Exception ex)
            {
                Release(entry); entry.Error = "角色预览暂不可用：" + ex.Message;
                Log.Warning("[LogSyncMod] 自动角色预览失败，配置变化前不再重试：" + ex);
            }
            finally
            {
                Rand.PopState();
                foreach (var thing in owned)
                {
                    try { if (thing is Pawn pawn && Find.WorldPawns.Contains(pawn)) Find.WorldPawns.RemovePawn(pawn); if (!thing.Destroyed) thing.Destroy(DestroyMode.Vanish); }
                    catch (Exception ex) { Log.Warning("[LogSyncMod] 预览对象清理失败：" + ex); }
                }
            }
        }
        internal static void Release(Entry e)
        { if (e.Texture != null) { e.Texture.Release(); UnityEngine.Object.Destroy(e.Texture); e.Texture = null; } }
        public void Dispose() { foreach (var e in entries.Values) Release(e); entries.Clear(); }
    }

}
