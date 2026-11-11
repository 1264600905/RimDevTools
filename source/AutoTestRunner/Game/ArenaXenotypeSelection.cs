using System.Linq;
using System.Xml.Linq;
using RimWorld;
using Verse;

namespace LogSyncMod.AutoTest
{
    internal static class ArenaXenotypeSelection
    {
        internal static void EnsureGenes(XElement xml)
        {
            var xenotype = DefDatabase<XenotypeDef>.GetNamedSilentFail((string)xml.Element("Xenotype")?.Attribute("def") ?? "");
            if (xenotype == null) return;
            ArenaIdentityPolicy.EnsureInnateGenes(xml, xenotype.genes.Select(g => g.defName), xenotype.inheritable);
        }
        internal static bool Required(XElement xml, string def) => DefDatabase<XenotypeDef>.GetNamedSilentFail((string)xml.Element("Xenotype")?.Attribute("def") ?? "")?.genes.Any(g => g.defName == def) == true;
    }
}
