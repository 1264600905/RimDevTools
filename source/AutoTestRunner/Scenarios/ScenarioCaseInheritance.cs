#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;

namespace LogSyncMod.AutoTest.Scenarios
{
    public static partial class ScenarioLoader
    {
        private static void ExpandBattleCases(XElement root, string path, bool groupCombat)
        {
            var nodes = root.Elements("Case").ToList();
            if (!nodes.Any(n => n.Attribute("extends") != null || n.Attribute("abstract") != null || n.Element("Patches") != null)) return;
            if (!groupCombat) Fail(root, path, "Case inheritance and Patches require groupcombat");
            var definitions = new Dictionary<string, XElement>(StringComparer.Ordinal);
            foreach (var node in nodes)
            {
                var id = Required(node, "id", path);
                if (definitions.ContainsKey(id)) Fail(node, path, "duplicate Case id " + id);
                if (!new[] { "true", "false" }.Contains(A(node, "abstract", "false"))) Fail(node, path, "abstract must be true/false");
                if (node.Elements("Patches").Count() > 1) Fail(node, path, "duplicate Patches");
                var raw = Copy(node); raw.Attribute("extends")?.Remove(); raw.Attribute("abstract")?.Remove(); raw.Element("Patches")?.Remove();
                ValidateTree(raw, path, allowParameters: true);
                definitions.Add(id, node);
            }
            var expanded = new Dictionary<string, XElement>(StringComparer.Ordinal);
            XElement Expand(string id, HashSet<string> stack)
            {
                if (expanded.TryGetValue(id, out var cached)) return Copy(cached);
                if (!definitions.TryGetValue(id, out var node)) { Fail(root, path, "unknown base Case " + id); return null; }
                if (!stack.Add(id)) Fail(node, path, "Case inheritance cycle at " + id);
                var result = node.Attribute("extends") == null ? Copy(node) : MergeBattleCase(Expand(A(node, "extends"), stack), node, path);
                result.Attribute("extends")?.Remove(); result.Attribute("abstract")?.Remove();
                // Every runnable derivative has its own stable test identity.
                if (A(node, "abstract", "false") != "true" && node.Element("testDefName") == null) Fail(node, path, "derived runnable Case requires its own testDefName");
                ApplyBattlePatches(result, path);
                ValidateTree(result, path, allowParameters: true);
                stack.Remove(id); expanded[id] = Copy(result); return result;
            }
            foreach (var node in nodes)
            {
                var resolved = Expand(A(node, "id"), new HashSet<string>());
                if (A(node, "abstract", "false") == "true") node.Remove(); else node.ReplaceWith(resolved);
            }
        }

        private static XElement MergeBattleCase(XElement basis, XElement patch, string path)
        {
            var result = Copy(basis);
            foreach (var attr in patch.Attributes()) result.SetAttributeValue(attr.Name, attr.Value);
            if (!patch.HasElements)
            {
                if (!string.IsNullOrWhiteSpace(patch.Value)) result.Value = patch.Value;
                return result;
            }
            foreach (var child in patch.Elements())
            {
                var id = A(child, "id");
                var matches = result.Elements(child.Name).Where(e => id == null || A(e, "id") == id).ToList();
                if (matches.Count > 1) Fail(child, path, "ambiguous inherited list; use an id or an explicit Patch");
                if (matches.Count == 0) result.Add(Copy(child));
                else matches[0].ReplaceWith(MergeBattleCase(matches[0], child, path));
            }
            return result;
        }

        private static void ApplyBattlePatches(XElement battle, string path)
        {
            var patches = battle.Element("Patches");
            if (patches == null) return;
            if (patches.HasAttributes || patches.Nodes().OfType<XText>().Any(t => !string.IsNullOrWhiteSpace(t.Value))) Fail(patches, path, "Patches accepts only patch operations");
            patches.Remove();
            // Root the detached Case in a private document so XPath can never reach
            // another Case or mutate the base definition shared by sibling variants.
            var document = new XDocument(battle);
            try
            {
                foreach (var op in patches.Elements())
                {
                    var allowed = op.Name.LocalName switch {
                        "SetAttribute" => "xpath name value", "SetText" => "xpath value",
                        "ReplaceNode" or "AddNode" or "RemoveNode" => "xpath", _ => null
                    };
                    if (allowed == null) Fail(op, path, "unknown battle patch " + op.Name);
                    foreach (var attr in op.Attributes()) if (!allowed.Split(' ').Contains(attr.Name.ToString())) Fail(op, path, "unknown patch attribute " + attr.Name);
                    foreach (var attr in allowed.Split(' ')) if (op.Attribute(attr) == null) Fail(op, path, "patch requires " + attr);
                    var payload = op.Element("Value");
                    var hasPayload = op.Name == "AddNode" || op.Name == "ReplaceNode";
                    if (hasPayload ? payload == null || payload.HasAttributes || !payload.HasElements || op.Elements().Count() != 1 || payload.Nodes().OfType<XText>().Any(t => !string.IsNullOrWhiteSpace(t.Value)) : op.HasElements) Fail(op, path, "patch payload must be a Value containing XML elements");
                    if (op.Nodes().OfType<XText>().Any(t => !string.IsNullOrWhiteSpace(t.Value))) Fail(op, path, "patch text must use the value attribute");
                    List<XElement> targets;
                    try { targets = battle.XPathSelectElements(Required(op, "xpath", path)).ToList(); }
                    catch (Exception ex) when (ex is XPathException || ex is InvalidOperationException || ex is ArgumentException) { Fail(op, path, "invalid patch XPath: " + ex.Message); return; }
                    if (targets.Count == 0) Fail(op, path, "patch XPath matched no elements");
                    foreach (var target in targets)
                    {
                        if (target == battle || !target.Ancestors().Contains(battle)) Fail(op, path, "patch must target descendants of its own Case");
                        switch (op.Name.LocalName)
                        {
                            case "SetAttribute":
                                var attrName = A(op, "name");
                                try { XmlConvert.VerifyNCName(attrName); } catch (XmlException) { Fail(op, path, "invalid patch attribute name"); }
                                target.SetAttributeValue(attrName, A(op, "value")); break;
                            case "SetText":
                                if (target.HasElements) Fail(op, path, "SetText requires a leaf element");
                                target.Value = A(op, "value"); break;
                            case "AddNode": target.Add(payload.Elements().Select(Copy)); break;
                            case "ReplaceNode": target.ReplaceWith(payload.Elements().Select(Copy)); break;
                            case "RemoveNode": target.Remove(); break;
                        }
                    }
                }
            }
            finally { battle.Remove(); }
        }
    }
}
