using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RimWorld;
using Verse;

namespace LogSyncMod
{
    public sealed class DebugSupplyOverride : IExposable
    {
        public string defName;
        public bool included;
        public int quantity = 1000;
        public string sourceModName;
        public string sourcePackageId;

        public void ExposeData()
        {
            Scribe_Values.Look(ref defName, "defName");
            Scribe_Values.Look(ref included, "included", false);
            Scribe_Values.Look(ref quantity, "quantity", 1000);
            Scribe_Values.Look(ref sourceModName, "sourceModName");
            Scribe_Values.Look(ref sourcePackageId, "sourcePackageId");
        }

        internal DebugSupplyOverride Copy() => (DebugSupplyOverride)MemberwiseClone();
    }

    public sealed class DebugSupplyProfile : IExposable
    {
        public string id = Guid.NewGuid().ToString("N");
        public string name = "";
        public bool useDefaultRules;
        public List<DebugSupplyOverride> items = new List<DebugSupplyOverride>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id");
            Scribe_Values.Look(ref name, "name", "");
            Scribe_Values.Look(ref useDefaultRules, "useDefaultRules", false);
            Scribe_Collections.Look(ref items, "items", LookMode.Deep);
            if (items == null) items = new List<DebugSupplyOverride>();
        }

        internal DebugSupplyProfile Copy() => new DebugSupplyProfile
        {
            id = id, name = name, useDefaultRules = useDefaultRules,
            items = items.Where(i => i != null).Select(i => i.Copy()).ToList()
        };
    }

    internal sealed class DebugSupplyProfileSummary
    {
        public int Kinds;
        public long Quantity;
        public int MissingKinds;
        public readonly List<string> Mods = new List<string>();

        internal string ShortText => "LogSyncMod.DebugProfileSummary".Translate(Kinds, Quantity, Mods.Count);
        internal string Details => ShortText + "\n" + string.Join("\n", Mods) +
            (MissingKinds == 0 ? "" : "\n" + "LogSyncMod.DebugMissingItems".Translate(MissingKinds).ToString());
    }

    internal sealed class DebugSupplyEntry
    {
        public ThingDef Def;
        public ThingDef Stuff;
        public string ModName;
        public string PackageId;
        public string SearchText;
        public bool ThirdParty;
        public bool DefaultIncluded;
        public int DefaultQuantity;
        public bool Included;
        public string QuantityText;

        public bool TryGetQuantity(out int value) => int.TryParse(QuantityText, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value > 0;
    }

    internal static class DebugSupplyPreset
    {
        private static readonly Dictionary<string, int> ExtraDefaults = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            { "Cloth", 1000 }, { "ComponentIndustrial", 300 }, { "ComponentSpacer", 100 }, { "MealLavish", 100 }
        };

        internal static List<DebugSupplyEntry> CreateEntries()
        {
            List<DebugSupplyEntry> entries = new List<DebugSupplyEntry>();
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (def.category != ThingCategory.Item || def.thingClass == null || def.thingClass.IsAbstract ||
                    def.stackLimit < 1 || !DebugThingPlaceHelper.IsDebugSpawnable(def)) continue;
                ThingDef stuff = null;
                if (def.MadeFromStuff)
                {
                    // Invalid custom definitions must not prevent browsing every other mod's items.
                    try { stuff = GenStuff.DefaultStuffFor(def); }
                    catch { continue; }
                    if (stuff?.stuffProps == null || !stuff.stuffProps.CanMake(def)) continue;
                }
                bool extra = ExtraDefaults.TryGetValue(def.defName, out int quantity);
                bool rawDefault = def.stackLimit >= 75 && ThingCategoryDefOf.ResourcesRaw.ContainedInThisOrDescendant(def);
                bool included = extra || rawDefault;
                if (!extra) quantity = rawDefault ? 1000 : (int)Math.Min(int.MaxValue, (long)def.stackLimit * 3);
                ModContentPack mod = def.modContentPack;
                DebugSupplyEntry entry = new DebugSupplyEntry
                {
                    Def = def, Stuff = stuff,
                    ModName = mod?.Name ?? "LogSyncMod.DebugUnknownSource".Translate().ToString(),
                    PackageId = mod?.PackageId ?? "", ThirdParty = mod != null && !mod.IsOfficialMod && !mod.IsCoreMod,
                    DefaultIncluded = included, DefaultQuantity = quantity,
                    Included = included, QuantityText = quantity.ToString(CultureInfo.InvariantCulture)
                };
                entry.SearchText = (def.LabelCap + " " + def.defName + " " + entry.ModName + " " + entry.PackageId).ToString().ToLowerInvariant();
                entries.Add(entry);
            }
            return entries.OrderBy(e => e.Def.LabelCap.ToString(), StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(e => e.ModName).ThenBy(e => e.Def.defName).ToList();
        }

        internal static List<DebugSupplyProfile> CreateProfiles()
        {
            List<DebugSupplyProfile> profiles = LogSyncMod.settings.debugSupplyProfiles
                .Where(p => p != null && !string.IsNullOrEmpty(p.id)).Select(p => p.Copy()).ToList();
            if (profiles.Count == 0)
            {
                profiles.Add(new DebugSupplyProfile
                {
                    name = "LogSyncMod.DebugDefaultProfile".Translate(), useDefaultRules = true,
                    items = LogSyncMod.settings.debugSupplyOverrides.Where(i => i != null).Select(i => i.Copy()).ToList()
                });
            }
            return profiles;
        }

        internal static void Apply(DebugSupplyProfile profile, List<DebugSupplyEntry> entries)
        {
            Dictionary<string, DebugSupplyOverride> saved = ItemLookup(profile);
            foreach (DebugSupplyEntry entry in entries)
            {
                entry.Included = profile.useDefaultRules && entry.DefaultIncluded;
                int quantity = entry.DefaultQuantity;
                if (saved.TryGetValue(entry.Def.defName, out DebugSupplyOverride item))
                {
                    entry.Included = item.included;
                    if (item.quantity > 0) quantity = item.quantity;
                }
                entry.QuantityText = quantity.ToString(CultureInfo.InvariantCulture);
            }
        }

        private static Dictionary<string, DebugSupplyOverride> ItemLookup(DebugSupplyProfile profile) => profile.items
            .Where(i => i != null && !string.IsNullOrEmpty(i.defName)).GroupBy(i => i.defName).ToDictionary(g => g.Key, g => g.Last());

        internal static void Capture(DebugSupplyProfile profile, List<DebugSupplyEntry> entries)
        {
            HashSet<string> known = new HashSet<string>(entries.Select(e => e.Def.defName));
            List<DebugSupplyOverride> saved = profile.items
                .Where(o => o != null && !string.IsNullOrEmpty(o.defName) && !known.Contains(o.defName)).ToList();
            foreach (DebugSupplyEntry entry in entries)
            {
                if (!entry.TryGetQuantity(out int quantity)) quantity = entry.DefaultQuantity;
                if (entry.Included != (profile.useDefaultRules && entry.DefaultIncluded) || quantity != entry.DefaultQuantity)
                    saved.Add(new DebugSupplyOverride
                    {
                        defName = entry.Def.defName, included = entry.Included, quantity = quantity,
                        sourceModName = entry.ModName, sourcePackageId = entry.PackageId
                    });
            }
            profile.items = saved;
        }

        internal static DebugSupplyProfileSummary Summarize(DebugSupplyProfile profile, List<DebugSupplyEntry> entries)
        {
            var saved = ItemLookup(profile);
            var known = new HashSet<string>(entries.Select(e => e.Def.defName));
            var selected = new List<Tuple<string, string, int>>();
            foreach (DebugSupplyEntry entry in entries)
            {
                bool included = profile.useDefaultRules && entry.DefaultIncluded;
                int quantity = entry.DefaultQuantity;
                if (saved.TryGetValue(entry.Def.defName, out DebugSupplyOverride item))
                {
                    included = item.included;
                    if (item.quantity > 0) quantity = item.quantity;
                }
                if (included) selected.Add(Tuple.Create(entry.PackageId, entry.ModName, quantity));
            }
            DebugSupplyProfileSummary summary = new DebugSupplyProfileSummary();
            foreach (DebugSupplyOverride missing in saved.Values.Where(i => i.included && !known.Contains(i.defName)))
            {
                selected.Add(Tuple.Create(missing.sourcePackageId ?? "", missing.sourceModName ?? "LogSyncMod.DebugUnknownSource".Translate().ToString(), Math.Max(0, missing.quantity)));
                summary.MissingKinds++;
            }
            summary.Kinds = selected.Count;
            summary.Quantity = selected.Sum(s => (long)s.Item3);
            foreach (var group in selected.GroupBy(s => s.Item1).OrderBy(g => g.First().Item2))
                summary.Mods.Add("LogSyncMod.DebugProfileModSummary".Translate(group.First().Item2, group.Count(), group.Sum(s => (long)s.Item3)));
            return summary;
        }

        internal static void Save(List<DebugSupplyProfile> profiles, DebugSupplyProfile active)
        {
            LogSyncMod.settings.debugSupplyProfiles = profiles.Select(p => p.Copy()).ToList();
            LogSyncMod.settings.debugSupplyActiveProfileId = active.id;
            LogSyncMod.settings.debugSupplyOverrides.Clear();
            LoadedModManager.GetMod<LogSyncMod>().WriteSettings();
        }
    }
}
