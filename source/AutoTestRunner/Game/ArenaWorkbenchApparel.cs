using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using LogSyncMod.AutoTest.Scenarios;
using RimWorld;
using Verse;

namespace LogSyncMod.AutoTest
{
    // One isolated native pawn supplies body/age checks and inherited clothing. The
    // picker owns its lifetime; neither the map nor the player's pawns are touched.
    internal sealed class ArenaWorkbenchApparel : IDisposable
    {
        private readonly List<Thing> owned = new();
        private readonly SquadGeneration.FactionScope faction;
        private readonly XElement input;
        private readonly Pawn pawn;
        private readonly Dictionary<string, string> rejections = new();
        internal ArenaWorkbenchApparel(XElement input, string factionDef, int seed)
        {
            this.input = new XElement(input);
            faction = new SquadGeneration.FactionScope(factionDef, "穿戴检查");
            var probe = new XElement(input); probe.Element("Apparel")?.Remove();
            Rand.PushState(seed == -1 ? 170031 : seed);
            try
            {
                var snapshot = TemporaryPawnData.Create(probe, "apparel.check", Array.Empty<string>(), Array.Empty<string>());
                pawn = ScenarioGameBackend.GenerateTemporaryPawn(snapshot, faction.Faction, null, out var generated, forceConfiguredXenotype: true);
                owned.AddRange(generated);
            }
            catch { Dispose(); throw; }
            finally { Rand.PopState(); }
        }
        internal string Rejection(ThingDef def)
        {
            if (def == null) return "该定义不是服装。";
            if (!rejections.TryGetValue(def.defName, out var reason)) rejections.Add(def.defName, reason = Check(def));
            return reason;
        }
        private string Check(ThingDef def)
        {
            if (def?.IsApparel != true) return "该定义不是服装。";
            if (pawn.apparel == null) return "此角色不支持服装。";
            if (!ApparelUtility.HasPartsToWear(pawn, def)) return "缺少该服装需要的身体部位。";
            Apparel apparel = null;
            try
            {
                apparel = ThingMaker.MakeThing(def, def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null) as Apparel;
                return apparel == null || !apparel.PawnCanWear(pawn, ignoreGender: true) ? "角色的成长阶段或原生穿戴规则不允许该服装。" : null;
            }
            catch (Exception ex) { return "原生穿戴检查失败：" + ex.Message; }
            finally { apparel?.Destroy(DestroyMode.Vanish); }
        }
        private XElement ResolveCurrent()
        {
            var inherited = XElement.Parse(TemporaryPawnCapture.Capture(pawn).TemplateXml).Element("Apparel");
            return ApparelLoadoutPlanner.Resolve(inherited, input.Element("Apparel"),
                (a, b) => ApparelUtility.CanWearTogether(DefDatabase<ThingDef>.GetNamed(a), DefDatabase<ThingDef>.GetNamed(b), pawn.RaceProps.body),
                def => Rejection(DefDatabase<ThingDef>.GetNamed(def)),
                (def, layer) => DefDatabase<ThingDef>.GetNamed(def).apparel.layers.Any(l => l.defName == layer));
        }
        internal XElement Materialize()
        {
            var result = new XElement(input);
            result.Element("Apparel")?.Remove();
            result.Add(ResolveCurrent());
            return result;
        }
        internal XElement Choose(ThingDef selected, XElement removed, Func<ThingDef, XElement> createItem, out List<string> replaced)
        {
            var reason = Rejection(selected);
            if (reason != null) throw new ScenarioConfigurationException(reason);
            var result = Materialize(); var items = result.Element("Apparel");
            var removedDef = (string)removed?.Attribute("def") ?? (string)removed?.Attribute("withDef");
            if (removedDef != null) items.Elements().Where(e => (string)e.Attribute("def") == removedDef).Remove();
            var conflicts = items.Elements().Where(e => !ApparelUtility.CanWearTogether(DefDatabase<ThingDef>.GetNamed((string)e.Attribute("def")), selected, pawn.RaceProps.body)).ToList();
            replaced = conflicts.Select(e => DefDatabase<ThingDef>.GetNamed((string)e.Attribute("def")).LabelCap.ToString()).ToList();
            foreach (var conflict in conflicts) conflict.Remove();
            items.Add(createItem(selected)); return result;
        }
        internal static string Normalize(string xml, string faction, int seed)
        {
            var input = ArenaWorkbench.ReadXml(xml); var patch = input.Element("Apparel");
            if (patch == null || (string)patch.Attribute("mode") is "replace" or "inherit" || !patch.HasElements) return xml;
            using var session = new ArenaWorkbenchApparel(input, faction, seed);
            return session.Materialize().ToString();
        }
        public void Dispose()
        {
            foreach (var thing in owned) { if (thing is Pawn p && Find.WorldPawns.Contains(p)) Find.WorldPawns.RemovePawn(p); if (!thing.Destroyed) thing.Destroy(DestroyMode.Vanish); }
            owned.Clear(); faction?.Dispose();
        }
    }
}
