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

        public void ExposeData()
        {
            Scribe_Values.Look(ref defName, "defName");
            Scribe_Values.Look(ref included, "included", false);
            Scribe_Values.Look(ref quantity, "quantity", 1000);
        }
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
            Dictionary<string, DebugSupplyOverride> overrides = (LogSyncMod.settings.debugSupplyOverrides ?? new List<DebugSupplyOverride>())
                .Where(o => o != null && !string.IsNullOrEmpty(o.defName)).GroupBy(o => o.defName).ToDictionary(g => g.Key, g => g.Last());
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
                bool included = extra || def.stackLimit >= 75 && ThingCategoryDefOf.ResourcesRaw.ContainedInThisOrDescendant(def);
                if (!extra) quantity = 1000;
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
                if (overrides.TryGetValue(def.defName, out DebugSupplyOverride saved))
                {
                    entry.Included = saved.included;
                    entry.QuantityText = Math.Max(1, saved.quantity).ToString(CultureInfo.InvariantCulture);
                }
                entries.Add(entry);
            }
            return entries.OrderBy(e => e.Def.LabelCap.ToString(), StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(e => e.ModName).ThenBy(e => e.Def.defName).ToList();
        }

        internal static void Save(List<DebugSupplyEntry> entries, bool resetMissingOverrides)
        {
            HashSet<string> known = new HashSet<string>(entries.Select(e => e.Def.defName));
            List<DebugSupplyOverride> saved = (resetMissingOverrides ? new List<DebugSupplyOverride>() : LogSyncMod.settings.debugSupplyOverrides ?? new List<DebugSupplyOverride>())
                .Where(o => o != null && !string.IsNullOrEmpty(o.defName) && !known.Contains(o.defName)).ToList();
            foreach (DebugSupplyEntry entry in entries)
            {
                if (!entry.TryGetQuantity(out int quantity)) quantity = entry.DefaultQuantity;
                if (entry.Included != entry.DefaultIncluded || quantity != entry.DefaultQuantity)
                    saved.Add(new DebugSupplyOverride { defName = entry.Def.defName, included = entry.Included, quantity = quantity });
            }
            LogSyncMod.settings.debugSupplyOverrides = saved;
            LoadedModManager.GetMod<LogSyncMod>().WriteSettings();
        }
    }
}
