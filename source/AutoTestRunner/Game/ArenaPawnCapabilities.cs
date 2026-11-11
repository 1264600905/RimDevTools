using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using RimWorld;
using Verse;

namespace LogSyncMod.AutoTest
{
    internal sealed class ArenaPawnCapabilities
    {
        internal ThingDef Race;
        internal PawnKindDef Kind;
        internal bool Human => Race?.race?.Humanlike == true;
        internal bool Equipment => Race?.race?.ToolUser == true;
        internal bool Genes => Human && ModsConfig.BiotechActive;
        internal bool Abilities => Race?.race?.ShouldHaveAbilityTracker == true || Kind?.abilities?.Count > 0 || Human || ModsConfig.BiotechActive && Race?.race?.IsMechanoid == true;
        internal bool Gender => Race?.race?.hasGenders == true;
        internal static ArenaPawnCapabilities For(XElement xml)
        {
            var kind = DefDatabase<PawnKindDef>.GetNamedSilentFail(xml.Element("basePawnKind")?.Value ?? "");
            return new ArenaPawnCapabilities { Kind = kind, Race = DefDatabase<ThingDef>.GetNamedSilentFail(xml.Element("race")?.Value ?? "") ?? kind?.race };
        }
        internal List<string> Unsupported(XElement xml)
        {
            var fields = new List<string>();
            if (!Human) fields.AddRange(new[] { "Traits", "Skills", "Appearance", "forcedTraits", "skills" });
            if (!Genes) fields.AddRange(new[] { "Genes", "Xenotype" });
            if (!Equipment) fields.AddRange(new[] { "Apparel", "Equipment" });
            if (!Abilities) fields.AddRange(new[] { "Abilities", "abilities" });
            return fields.Where(f => xml.Element(f) is XElement e && (e.HasElements || f is "Xenotype" or "Appearance" || (string)e.Attribute("mode") == "replace")).ToList();
        }
    }
}
