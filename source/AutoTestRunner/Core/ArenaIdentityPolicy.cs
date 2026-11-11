using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace LogSyncMod.AutoTest
{
    public static class ArenaIdentityPolicy
    {
        public static void EnsureInnateGenes(XElement result, IEnumerable<string> genes, bool inheritable)
        {
            var required = genes.Distinct(StringComparer.Ordinal).ToList();
            if (required.Count == 0) return;
            var section = result.Element("Genes");
            if (section == null) { section = new XElement("Genes", new XAttribute("mode", "patch")); result.Add(section); }
            if ((string)section.Attribute("mode") == "inherit") section.SetAttributeValue("mode", "patch");
            foreach (var def in required)
            {
                section.Elements("Remove").Where(e => (string)e.Attribute("def") == def).Remove();
                if (!section.Elements("Add").Any(e => (string)e.Attribute("def") == def && ((string)e.Attribute("xenogene") ?? "false") == (!inheritable ? "true" : "false")))
                    section.Add(new XElement("Add", new XAttribute("def", def), new XAttribute("xenogene", !inheritable)));
            }
        }
        public static XElement SelectHuman(XElement original, string race, string xenotype, IEnumerable<string> genes, bool inheritable, IEnumerable<string> previousGenes)
        {
            var result = new XElement(original);
            result.SetElementValue("race", race);
            result.Element("Xenotype")?.Remove(); result.Add(new XElement("Xenotype", new XAttribute("def", xenotype)));
            var section = result.Element("Genes");
            if (section == null) { section = new XElement("Genes", new XAttribute("mode", "patch")); result.Add(section); }
            if ((string)section.Attribute("mode") == "inherit") section.SetAttributeValue("mode", "patch");
            var old = new HashSet<string>(previousGenes ?? Enumerable.Empty<string>());
            section.Elements("Add").Where(e => old.Contains((string)e.Attribute("def"))).Remove();
            foreach (var def in genes.Distinct(StringComparer.Ordinal))
            {
                section.Elements().Where(e => (string)e.Attribute("def") == def).Remove();
                section.Add(new XElement("Add", new XAttribute("def", def), new XAttribute("xenogene", !inheritable)));
            }
            return result;
        }
        public static XElement SelectNonHuman(XElement original, string pawnKind, string race, bool toolUser, bool abilities)
        {
            var result = new XElement(original); result.SetElementValue("basePawnKind", pawnKind); result.SetElementValue("race", race);
            result.Element("gender")?.Remove();
            foreach (var field in new[] { "Xenotype", "Genes", "Traits", "Skills", "forcedTraits", "skills", "Appearance" }) result.Element(field)?.Remove();
            if (!toolUser) { result.Element("Equipment")?.Remove(); result.Element("Apparel")?.Remove(); }
            if (!abilities) { result.Element("Abilities")?.Remove(); result.Element("abilities")?.Remove(); }
            result.Element("Resources")?.Remove();
            return result;
        }
        public static bool AcceptNewParticipant(bool dead, bool destroyed, bool corpseHeld, bool registered)
            => registered || !dead && !destroyed && !corpseHeld;
    }
}
