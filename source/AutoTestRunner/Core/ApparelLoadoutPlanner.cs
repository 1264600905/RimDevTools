using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using LogSyncMod.AutoTest.Scenarios;

namespace LogSyncMod.AutoTest
{
    public static class ApparelLoadoutPlanner
    {
        // The caller supplies the native body's wearability/layer checks. This only
        // materializes supported XML edits into one explicit, replayable loadout.
        public static XElement Resolve(XElement inherited, XElement patch, Func<string, string, bool> together,
            Func<string, string> rejection, Func<string, string, bool> onLayer)
        {
            var mode = (string)patch?.Attribute("mode") ?? "inherit";
            var result = mode == "replace" ? new List<XElement>() : inherited?.Elements().Select(e => new XElement(e)).ToList() ?? new();
            var specified = new HashSet<XElement>();
            if (mode != "inherit") foreach (var change in patch?.Elements() ?? Enumerable.Empty<XElement>())
            {
                void RemoveMatches(Func<XElement, bool> match)
                { foreach (var e in result.Where(match).ToList()) { result.Remove(e); specified.Remove(e); } }
                if (change.Name == "Remove") { RemoveMatches(e => (string)e.Attribute("def") == (string)change.Attribute("def")); continue; }
                var def = (string)change.Attribute("def");
                if (change.Name == "Replace")
                {
                    bool Matches(XElement e) => change.Attribute("matchDef") != null ? (string)e.Attribute("def") == (string)change.Attribute("matchDef") : onLayer((string)e.Attribute("def"), (string)change.Attribute("matchLayer"));
                    if (!result.Any(Matches) && ((string)change.Attribute("onMissing") ?? "fail") == "fail") throw new ScenarioConfigurationException("服装替换规则没有匹配对象。");
                    RemoveMatches(Matches); def = (string)change.Attribute("withDef");
                }
                var reason = rejection(def);
                if (reason != null) throw new ScenarioConfigurationException(reason);
                if (specified.Any(e => !together((string)e.Attribute("def"), def))) throw new ScenarioConfigurationException("已指定的服装互相冲突：" + def);
                RemoveMatches(e => !together((string)e.Attribute("def"), def));
                var item = new XElement("Add", new XAttribute("def", def), change.Attributes().Where(a => new[] { "stuff", "quality", "color" }.Contains(a.Name.LocalName)).Select(a => new XAttribute(a)));
                result.Add(item); specified.Add(item);
            }
            return new XElement("Apparel", new XAttribute("mode", "replace"), result);
        }
    }
}
