using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimWorld;
using Verse;

namespace LogSyncMod.AutoTest
{
    internal sealed class AutoTestCombatPawnTargetSpec
    {
        public AutoTestKind Kind;
        public AutoTestCaseType CaseType;
        public AutoTestManifestEntry ManifestEntry;
        public Def TargetDef;
        public PawnKindDef PawnKindDef;
        public ThingDef RaceDef;
        public ThingDef ItemDef;
        public BodyTypeDef ForcedBodyType;
        public Gender? ForcedGender;
        public bool WearAsApparel;
        public bool StripAllApparel;
        public bool StripAllEquipment;
        public bool KeepGeneratedLoadout;
        public string PawnRaceDefName;
        public string PawnRaceLabel;
        public string PawnBodyTypeDefName;
        public string PawnBodyTypeLabel;
        public string DefName;
        public string Label;
    }

    internal sealed class AutoTestCombatPawnService
    {
        public List<AutoTestCombatPawnTargetSpec> ResolveDefaultTargets(
            ModContentPack targetMod,
            Func<AutoTestCaseType, string, string, string, AutoTestManifestEntry> registerDiscoveredCase)
        {
            List<AutoTestCombatPawnTargetSpec> targets = new List<AutoTestCombatPawnTargetSpec>();
            if (targetMod == null || registerDiscoveredCase == null)
            {
                return targets;
            }

            AddTargetModApparelTargets(targets, targetMod, registerDiscoveredCase);
            AddTargetModPawnKindTargets(targets, targetMod, registerDiscoveredCase, AutoTestKind.AnimalCombat, AutoTestCaseType.AnimalCombat, def => def?.race?.race?.Animal == true);
            AddTargetModPawnKindTargets(targets, targetMod, registerDiscoveredCase, AutoTestKind.MechanoidCombat, AutoTestCaseType.MechanoidCombat, def => def?.race?.race?.IsMechanoid == true);
            AddTargetModPawnKindTargets(targets, targetMod, registerDiscoveredCase, AutoTestKind.AlienCombat, AutoTestCaseType.AlienCombat, def => IsAlienHumanlike(def?.race));
            return targets;
        }

        public bool TrySpawnConfiguredPawn(Map map, AutoTestCombatPawnTargetSpec spec, out Pawn pawn, out string errorMessage)
        {
            pawn = null;
            errorMessage = null;
            if (map == null || spec?.PawnKindDef == null)
            {
                errorMessage = "Combat target map or PawnKindDef was missing.";
                return false;
            }

            Faction playerFaction = Faction.OfPlayer ?? Find.FactionManager?.OfPlayer;
            Exception lastException = null;
            foreach (Faction generationFaction in EnumerateGenerationFactions(spec, playerFaction))
            {
                for (int attempt = 0; attempt < 4 && pawn == null; attempt++)
                {
                    try
                    {
                        PawnGenerationRequest request = new PawnGenerationRequest(
                            spec.PawnKindDef,
                            generationFaction,
                            PawnGenerationContext.NonPlayer,
                            map.Tile,
                            forceGenerateNewPawn: true,
                            canGeneratePawnRelations: false,
                            mustBeCapableOfViolence: false);
                        Pawn candidate = PawnGenerator.GeneratePawn(request);
                        if (candidate == null)
                        {
                            continue;
                        }

                        if (!PreparePawnForSpec(candidate, spec, out string prepareError))
                        {
                            candidate.Destroy(DestroyMode.Vanish);
                            lastException = new InvalidOperationException(prepareError ?? "Failed to prepare combat pawn.");
                            continue;
                        }

                        if (playerFaction != null && candidate.Faction != playerFaction)
                        {
                            try
                            {
                                candidate.SetFaction(playerFaction);
                            }
                            catch
                            {
                            }
                        }

                        pawn = candidate;
                    }
                    catch (Exception ex)
                    {
                        lastException = ex;
                    }
                }

                if (pawn != null)
                {
                    break;
                }
            }

            if (pawn != null)
            {
                return true;
            }

            errorMessage = lastException?.Message ?? "Pawn generation returned null.";
            return false;
        }

        private static IEnumerable<Faction> EnumerateGenerationFactions(AutoTestCombatPawnTargetSpec spec, Faction playerFaction)
        {
            if (playerFaction != null)
            {
                yield return playerFaction;
            }

            if (spec?.RaceDef?.race?.IsMechanoid == true)
            {
                if (Faction.OfMechanoids != null)
                {
                    yield return Faction.OfMechanoids;
                }
            }

            yield return null;
        }

        private static void AddTargetModPawnKindTargets(
            ICollection<AutoTestCombatPawnTargetSpec> targets,
            ModContentPack targetMod,
            Func<AutoTestCaseType, string, string, string, AutoTestManifestEntry> registerDiscoveredCase,
            AutoTestKind kind,
            AutoTestCaseType caseType,
            Func<PawnKindDef, bool> predicate)
        {
            foreach (PawnKindDef pawnKindDef in DefDatabase<PawnKindDef>.AllDefsListForReading
                .Where(def => def != null && def.modContentPack == targetMod && predicate(def))
                .OrderBy(def => def.defName))
            {
                targets.Add(new AutoTestCombatPawnTargetSpec
                {
                    Kind = kind,
                    CaseType = caseType,
                    ManifestEntry = registerDiscoveredCase(caseType, nameof(PawnKindDef), pawnKindDef.defName, pawnKindDef.label),
                    TargetDef = pawnKindDef,
                    PawnKindDef = pawnKindDef,
                    RaceDef = pawnKindDef.race,
                    KeepGeneratedLoadout = kind == AutoTestKind.MechanoidCombat,
                    StripAllApparel = kind == AutoTestKind.AlienCombat,
                    StripAllEquipment = kind == AutoTestKind.AlienCombat,
                    PawnRaceDefName = pawnKindDef.race?.defName,
                    PawnRaceLabel = pawnKindDef.race?.label ?? pawnKindDef.race?.defName ?? string.Empty,
                    PawnBodyTypeDefName = string.Empty,
                    PawnBodyTypeLabel = string.Empty,
                    DefName = pawnKindDef.defName,
                    Label = pawnKindDef.label
                });
            }
        }

        private static void AddTargetModApparelTargets(
            ICollection<AutoTestCombatPawnTargetSpec> targets,
            ModContentPack targetMod,
            Func<AutoTestCaseType, string, string, string, AutoTestManifestEntry> registerDiscoveredCase)
        {
            PawnKindDef humanPawnKind = ResolveDefaultHumanPawnKind();
            List<PawnKindDef> alienPawnKinds = ResolvePreferredAlienPawnKinds(targetMod);

            foreach (ThingDef apparelDef in DefDatabase<ThingDef>.AllDefsListForReading
                .Where(def => def != null && def.modContentPack == targetMod && def.IsApparel)
                .OrderBy(def => def.defName))
            {
                List<BodyTypeDef> supportedBodyTypes = ResolveSupportedApparelBodyTypes(apparelDef);
                if (supportedBodyTypes.Count == 0)
                {
                    supportedBodyTypes.Add(ResolveDeterministicBodyType(ResolvePreferredGenderForVariant(apparelDef, null)));
                }

                foreach (BodyTypeDef bodyType in supportedBodyTypes.Distinct())
                {
                    Gender? forcedGender = ResolvePreferredGenderForVariant(apparelDef, bodyType);
                    if (!TryResolvePresenterPawnKindForApparel(apparelDef, bodyType, forcedGender, humanPawnKind, alienPawnKinds, out PawnKindDef pawnKind))
                    {
                        continue;
                    }

                    string manifestDefName = BuildVariantManifestKey(apparelDef.defName, pawnKind?.defName, bodyType?.defName);
                    string displayBodyType = bodyType?.label ?? bodyType?.defName ?? "Default";
                    string label = string.IsNullOrWhiteSpace(apparelDef.label)
                        ? apparelDef.defName
                        : apparelDef.label + " [" + displayBodyType + "]";

                    targets.Add(new AutoTestCombatPawnTargetSpec
                    {
                        Kind = AutoTestKind.ApparelCombat,
                        CaseType = AutoTestCaseType.ApparelCombat,
                        ManifestEntry = registerDiscoveredCase(AutoTestCaseType.ApparelCombat, nameof(ThingDef), manifestDefName, label),
                        TargetDef = apparelDef,
                        PawnKindDef = pawnKind,
                        RaceDef = pawnKind?.race,
                        ItemDef = apparelDef,
                        ForcedBodyType = bodyType,
                        ForcedGender = forcedGender,
                        WearAsApparel = true,
                        StripAllApparel = true,
                        StripAllEquipment = true,
                        KeepGeneratedLoadout = false,
                        PawnRaceDefName = pawnKind?.race?.defName,
                        PawnRaceLabel = pawnKind?.race?.label ?? pawnKind?.race?.defName ?? string.Empty,
                        PawnBodyTypeDefName = bodyType?.defName ?? string.Empty,
                        PawnBodyTypeLabel = displayBodyType,
                        DefName = apparelDef.defName,
                        Label = apparelDef.label
                    });
                }
            }
        }

        private static string BuildVariantManifestKey(string defName, string pawnKindDefName, string bodyTypeDefName)
        {
            return (defName ?? "UnknownDef") + "@" + (pawnKindDefName ?? "UnknownPawn") + "@" + (bodyTypeDefName ?? "Default");
        }

        private static PawnKindDef ResolveDefaultHumanPawnKind()
        {
            return ResolvePawnKind(new[] { "Colonist" }, def => def?.race == ThingDefOf.Human);
        }

        private static List<PawnKindDef> ResolvePreferredAlienPawnKinds(ModContentPack targetMod)
        {
            return DefDatabase<PawnKindDef>.AllDefsListForReading
                .Where(def => def != null && IsAlienHumanlike(def.race))
                .OrderByDescending(def => def.modContentPack == targetMod ? 1 : 0)
                .ThenBy(def => def.defName)
                .ToList();
        }

        private static PawnKindDef ResolvePawnKind(IEnumerable<string> preferredDefNames, Func<PawnKindDef, bool> fallbackPredicate)
        {
            foreach (string preferredDefName in preferredDefNames ?? Enumerable.Empty<string>())
            {
                PawnKindDef preferred = DefDatabase<PawnKindDef>.GetNamedSilentFail(preferredDefName);
                if (preferred != null)
                {
                    return preferred;
                }
            }

            return DefDatabase<PawnKindDef>.AllDefsListForReading
                .Where(def => def != null && fallbackPredicate(def))
                .OrderBy(def => def.defName)
                .FirstOrDefault();
        }

        private static bool PreparePawnForSpec(Pawn pawn, AutoTestCombatPawnTargetSpec spec, out string errorMessage)
        {
            errorMessage = null;
            if (pawn == null)
            {
                errorMessage = "Combat pawn was null.";
                return false;
            }

            try
            {
                if (spec?.ForcedGender.HasValue == true)
                {
                    pawn.gender = spec.ForcedGender.Value;
                }

                if (spec?.ForcedBodyType != null && pawn.story != null)
                {
                    pawn.story.bodyType = spec.ForcedBodyType;
                }

                if (spec?.StripAllApparel == true)
                {
                    pawn.apparel?.DestroyAll();
                }

                if (spec?.StripAllEquipment == true)
                {
                    pawn.equipment?.DestroyAllEquipment();
                }

                if (spec?.WearAsApparel == true && spec.ItemDef != null)
                {
                    Apparel apparel = ThingMaker.MakeThing(spec.ItemDef, ResolveStuff(spec.ItemDef)) as Apparel;
                    if (apparel == null)
                    {
                        errorMessage = "Thing could not be created as apparel: " + spec.ItemDef.defName;
                        return false;
                    }

                    if (!apparel.PawnCanWear(pawn))
                    {
                        apparel.Destroy(DestroyMode.Vanish);
                        errorMessage = "Pawn cannot wear apparel: " + spec.ItemDef.defName;
                        return false;
                    }

                    if (pawn.apparel != null && !pawn.apparel.CanWearWithoutDroppingAnything(apparel.def))
                    {
                        pawn.apparel.DestroyAll();
                    }

                    pawn.apparel?.Wear(apparel, false, false);
                }

                RefreshPawnGraphics(pawn);
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        private static Gender? ResolvePreferredGenderForVariant(ThingDef apparelDef, BodyTypeDef bodyType)
        {
            if (bodyType != null)
            {
                if (string.Equals(bodyType.defName, "Female", StringComparison.OrdinalIgnoreCase))
                {
                    return Gender.Female;
                }

                if (string.Equals(bodyType.defName, "Male", StringComparison.OrdinalIgnoreCase))
                {
                    return Gender.Male;
                }
            }

            if (apparelDef?.apparel != null && apparelDef.apparel.gender != Gender.None)
            {
                return apparelDef.apparel.gender;
            }

            return null;
        }

        private static BodyTypeDef ResolveDeterministicBodyType(Gender? gender)
        {
            string preferredDefName = gender == Gender.Female ? "Female" : "Male";
            return DefDatabase<BodyTypeDef>.GetNamedSilentFail(preferredDefName)
                ?? DefDatabase<BodyTypeDef>.GetNamedSilentFail("Thin")
                ?? DefDatabase<BodyTypeDef>.AllDefsListForReading.OrderBy(def => def.defName).FirstOrDefault();
        }

        private static List<BodyTypeDef> ResolveSupportedApparelBodyTypes(ThingDef apparelDef)
        {
            HashSet<BodyTypeDef> bodyTypes = new HashSet<BodyTypeDef>();
            WornGraphicData wornGraphicData = apparelDef?.apparel?.wornGraphicData;
            if (wornGraphicData != null)
            {
                TryAddSupportedBodyType(bodyTypes, "Male", wornGraphicData.male);
                TryAddSupportedBodyType(bodyTypes, "Female", wornGraphicData.female);
                TryAddSupportedBodyType(bodyTypes, "Thin", wornGraphicData.thin);
                TryAddSupportedBodyType(bodyTypes, "Fat", wornGraphicData.fat);
                TryAddSupportedBodyType(bodyTypes, "Hulk", wornGraphicData.hulk);
                TryAddDirectionBodyTypes(bodyTypes, wornGraphicData.north);
                TryAddDirectionBodyTypes(bodyTypes, wornGraphicData.south);
                TryAddDirectionBodyTypes(bodyTypes, wornGraphicData.east);
                TryAddDirectionBodyTypes(bodyTypes, wornGraphicData.west);
            }

            foreach (BodyTypeDef inferredBodyType in ResolveTextureBackedApparelBodyTypes(apparelDef))
            {
                if (inferredBodyType != null)
                {
                    bodyTypes.Add(inferredBodyType);
                }
            }

            return bodyTypes
                .Where(def => def != null)
                .OrderBy(def => def.defName)
                .ToList();
        }

        private static IEnumerable<BodyTypeDef> ResolveTextureBackedApparelBodyTypes(ThingDef apparelDef)
        {
            string wornGraphicPath = apparelDef?.apparel?.wornGraphicPath;
            string modRootDirectory = apparelDef?.modContentPack?.RootDir;
            if (string.IsNullOrWhiteSpace(wornGraphicPath) || string.IsNullOrWhiteSpace(modRootDirectory))
            {
                return Enumerable.Empty<BodyTypeDef>();
            }

            try
            {
                string normalizedGraphicPath = wornGraphicPath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
                string graphicDirectory = Path.GetDirectoryName(normalizedGraphicPath);
                string graphicBaseName = Path.GetFileName(normalizedGraphicPath);
                if (string.IsNullOrWhiteSpace(graphicBaseName))
                {
                    return Enumerable.Empty<BodyTypeDef>();
                }

                string texturesRoot = Path.Combine(modRootDirectory, "Textures");
                string searchDirectory = string.IsNullOrWhiteSpace(graphicDirectory)
                    ? texturesRoot
                    : Path.Combine(texturesRoot, graphicDirectory);
                if (!Directory.Exists(searchDirectory))
                {
                    return Enumerable.Empty<BodyTypeDef>();
                }

                HashSet<BodyTypeDef> inferredBodyTypes = new HashSet<BodyTypeDef>();
                foreach (string filePath in Directory.EnumerateFiles(searchDirectory, graphicBaseName + "*.*", SearchOption.TopDirectoryOnly))
                {
                    string fileName = Path.GetFileNameWithoutExtension(filePath);
                    if (!fileName.StartsWith(graphicBaseName + "_", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string suffix = fileName.Substring(graphicBaseName.Length + 1);
                    int separatorIndex = suffix.IndexOf('_');
                    string token = separatorIndex >= 0 ? suffix.Substring(0, separatorIndex) : suffix;
                    BodyTypeDef bodyType = ResolveBodyTypeByGraphicToken(token);
                    if (bodyType != null)
                    {
                        inferredBodyTypes.Add(bodyType);
                    }
                }

                return inferredBodyTypes
                    .Where(def => def != null)
                    .OrderBy(def => def.defName)
                    .ToList();
            }
            catch
            {
                return Enumerable.Empty<BodyTypeDef>();
            }
        }

        private static BodyTypeDef ResolveBodyTypeByGraphicToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return null;
            }

            switch (token.Trim().ToLowerInvariant())
            {
                case "male":
                    return DefDatabase<BodyTypeDef>.GetNamedSilentFail("Male");
                case "female":
                    return DefDatabase<BodyTypeDef>.GetNamedSilentFail("Female");
                case "thin":
                    return DefDatabase<BodyTypeDef>.GetNamedSilentFail("Thin");
                case "fat":
                    return DefDatabase<BodyTypeDef>.GetNamedSilentFail("Fat");
                case "hulk":
                    return DefDatabase<BodyTypeDef>.GetNamedSilentFail("Hulk");
                default:
                    return null;
            }
        }

        private static void TryAddDirectionBodyTypes(ICollection<BodyTypeDef> bodyTypes, WornGraphicDirectionData directionData)
        {
            if (bodyTypes == null)
            {
                return;
            }

            TryAddSupportedBodyType(bodyTypes, "Male", directionData.male);
            TryAddSupportedBodyType(bodyTypes, "Female", directionData.female);
            TryAddSupportedBodyType(bodyTypes, "Thin", directionData.thin);
            TryAddSupportedBodyType(bodyTypes, "Fat", directionData.fat);
            TryAddSupportedBodyType(bodyTypes, "Hulk", directionData.hulk);
        }

        private static void TryAddSupportedBodyType(ICollection<BodyTypeDef> bodyTypes, string defName, object bodyTypeData)
        {
            if (bodyTypes == null || bodyTypeData == null)
            {
                return;
            }

            BodyTypeDef bodyTypeDef = DefDatabase<BodyTypeDef>.GetNamedSilentFail(defName);
            if (bodyTypeDef != null)
            {
                bodyTypes.Add(bodyTypeDef);
            }
        }

        private static bool TryResolvePresenterPawnKindForApparel(
            ThingDef apparelDef,
            BodyTypeDef bodyType,
            Gender? forcedGender,
            PawnKindDef humanPawnKind,
            IEnumerable<PawnKindDef> alienPawnKinds,
            out PawnKindDef pawnKind)
        {
            pawnKind = null;
            if (CanPawnKindWearApparel(humanPawnKind, apparelDef, bodyType, forcedGender))
            {
                pawnKind = humanPawnKind;
                return true;
            }

            foreach (PawnKindDef alienPawnKind in alienPawnKinds ?? Enumerable.Empty<PawnKindDef>())
            {
                if (CanPawnKindWearApparel(alienPawnKind, apparelDef, bodyType, forcedGender))
                {
                    pawnKind = alienPawnKind;
                    return true;
                }
            }

            return false;
        }

        private static bool CanPawnKindWearApparel(PawnKindDef pawnKindDef, ThingDef apparelDef, BodyTypeDef bodyType, Gender? forcedGender)
        {
            if (pawnKindDef == null || apparelDef == null || !apparelDef.IsApparel)
            {
                return false;
            }

            Pawn pawn = null;
            Apparel apparel = null;
            try
            {
                if (!TryGenerateProbePawn(pawnKindDef, out pawn))
                {
                    return false;
                }

                if (forcedGender.HasValue)
                {
                    pawn.gender = forcedGender.Value;
                }

                if (bodyType != null && pawn.story != null)
                {
                    pawn.story.bodyType = bodyType;
                }

                apparel = ThingMaker.MakeThing(apparelDef, ResolveStuff(apparelDef)) as Apparel;
                if (apparel == null)
                {
                    return false;
                }

                return apparel.PawnCanWear(pawn) && (pawn.apparel == null || pawn.apparel.CanWearWithoutDroppingAnything(apparel.def));
            }
            catch
            {
                return false;
            }
            finally
            {
                try
                {
                    apparel?.Destroy(DestroyMode.Vanish);
                }
                catch
                {
                }

                try
                {
                    pawn?.Destroy(DestroyMode.Vanish);
                }
                catch
                {
                }
            }
        }

        private static bool TryGenerateProbePawn(PawnKindDef pawnKindDef, out Pawn pawn)
        {
            pawn = null;
            if (pawnKindDef == null)
            {
                return false;
            }

            try
            {
                PawnGenerationRequest request = new PawnGenerationRequest(
                    pawnKindDef,
                    Faction.OfPlayer ?? Find.FactionManager?.OfPlayer,
                    PawnGenerationContext.NonPlayer,
                    -1,
                    forceGenerateNewPawn: true,
                    canGeneratePawnRelations: false,
                    mustBeCapableOfViolence: false);

                pawn = PawnGenerator.GeneratePawn(request);
                if (pawn?.RaceProps?.Humanlike == true)
                {
                    pawn.apparel?.DestroyAll();
                    pawn.equipment?.DestroyAllEquipment();
                }

                return pawn != null;
            }
            catch
            {
                pawn = null;
                return false;
            }
        }

        private static ThingDef ResolveStuff(ThingDef def)
        {
            if (def == null || !def.MadeFromStuff)
            {
                return null;
            }

            if (def.defaultStuff != null)
            {
                return def.defaultStuff;
            }

            if (def.stuffCategories == null || def.stuffCategories.Count == 0)
            {
                return null;
            }

            return DefDatabase<ThingDef>.AllDefsListForReading
                .Where(candidate =>
                    candidate != null &&
                    candidate.IsStuff &&
                    candidate.stuffProps?.categories != null &&
                    candidate.stuffProps.categories.Any(category => def.stuffCategories.Contains(category)))
                .OrderBy(candidate => candidate.defName)
                .FirstOrDefault();
        }

        private static void RefreshPawnGraphics(Pawn pawn)
        {
            try
            {
                pawn?.style?.Notify_StyleItemChanged();
            }
            catch
            {
            }

            try
            {
                pawn?.Drawer?.renderer?.SetAllGraphicsDirty();
            }
            catch
            {
            }
        }

        private static bool IsAlienHumanlike(ThingDef raceDef)
        {
            if (raceDef == null || raceDef == ThingDefOf.Human || raceDef.modContentPack == null)
            {
                return false;
            }

            if (!(raceDef.race?.Humanlike ?? false))
            {
                return false;
            }

            string packageId = raceDef.modContentPack.PackageIdPlayerFacing ?? string.Empty;
            return !packageId.StartsWith("ludeon.", StringComparison.OrdinalIgnoreCase);
        }
    }
}
