using System;
using System.Linq;
using RimWorld;
using Verse;

namespace LogSyncMod.AutoTest
{
    internal sealed partial class AutoTestRunner
    {
        private void EnqueueExtendedInfoCardDefs()
        {
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading
                .Where(IsTargetPlantDef)
                .OrderBy(def => def.defName))
            {
                pendingItems.Enqueue(CreateWorkItem(
                    AutoTestKind.Plant,
                    thingDef: def,
                    specDef: ResolveBestMatchingSpec(AutoTestKind.Plant, def)));
            }

            foreach (PawnKindDef def in DefDatabase<PawnKindDef>.AllDefsListForReading
                .Where(IsTargetPawnKindInfoCardDef)
                .OrderBy(def => def.defName))
            {
                AutoTestKind kind = ResolvePawnInfoCardKind(def);
                pendingItems.Enqueue(CreateWorkItem(
                    kind,
                    pawnKindDef: def,
                    specDef: ResolveBestMatchingSpec(kind, def)));
            }

            foreach (GeneDef def in DefDatabase<GeneDef>.AllDefsListForReading
                .Where(def => def != null && def.modContentPack == targetMod && MatchesSpecFilters(AutoTestKind.GeneInfoCard, def))
                .OrderBy(def => def.defName))
            {
                pendingItems.Enqueue(CreateWorkItem(
                    AutoTestKind.GeneInfoCard,
                    geneDef: def,
                    specDef: ResolveBestMatchingSpec(AutoTestKind.GeneInfoCard, def)));
            }

            foreach (XenotypeDef def in DefDatabase<XenotypeDef>.AllDefsListForReading
                .Where(def => def != null && def.modContentPack == targetMod && MatchesSpecFilters(AutoTestKind.XenotypeInfoCard, def))
                .OrderBy(def => def.defName))
            {
                pendingItems.Enqueue(CreateWorkItem(
                    AutoTestKind.XenotypeInfoCard,
                    xenotypeDef: def,
                    specDef: ResolveBestMatchingSpec(AutoTestKind.XenotypeInfoCard, def)));
            }
        }

        private bool IsTargetPlantDef(ThingDef def)
        {
            return def != null &&
                   def.modContentPack == targetMod &&
                   def.plant != null &&
                   def.thingClass != null &&
                   typeof(Plant).IsAssignableFrom(def.thingClass) &&
                   MatchesSpecFilters(AutoTestKind.Plant, def);
        }

        private bool IsTargetPawnKindInfoCardDef(PawnKindDef def)
        {
            if (def == null || def.modContentPack != targetMod || def.race?.race == null)
            {
                return false;
            }

            AutoTestKind kind = ResolvePawnInfoCardKind(def);
            return MatchesSpecFilters(kind, def);
        }

        private static AutoTestKind ResolvePawnInfoCardKind(PawnKindDef def)
        {
            if (def?.race?.race?.IsMechanoid == true)
            {
                return AutoTestKind.MechanoidInfoCard;
            }

            if (def?.race?.race?.Animal == true)
            {
                return AutoTestKind.AnimalInfoCard;
            }

            return AutoTestKind.PawnInfoCard;
        }

        private static bool IsPawnInfoCardKind(AutoTestKind kind)
        {
            return kind == AutoTestKind.AnimalInfoCard ||
                   kind == AutoTestKind.MechanoidInfoCard ||
                   kind == AutoTestKind.PawnInfoCard;
        }

        private void StartDefInfoCardSession(AutoTestWorkItem item)
        {
            Def targetDef = (Def)item.GeneDef ?? item.XenotypeDef;
            AutoTestResult result = CreateResult(item.ManifestEntry, targetDef?.GetType().Name ?? nameof(Def), targetDef?.defName, targetDef?.label);
            results.Add(result);

            string caseId = item.CaseId ?? item.ManifestEntry?.CaseId ?? result.CaseId;
            InfoCardOperationAdapter adapter = new InfoCardOperationAdapter(runId, caseId, operationRunner, operationJournal, logJournal, evidenceBuilder, evidenceClock);
            IDisposable caseScope = adapter.BeginCase();

            if (!ModsConfig.BiotechActive)
            {
                result.Status = "skipped";
                result.Stage = "capability";
                result.ErrorMessage = "Biotech is not active; Gene/Xenotype InfoCard is unavailable.";
                result.Notes = AppendNote(result.Notes, "Capability=BiotechInactive");
                adapter.AssertTrue("biotech.capability_reported", true, result.ErrorMessage);
                adapter.CompleteCase(true, result.ErrorMessage);
                caseScope.Dispose();
                CompleteSemanticCase(result);
                return;
            }

            AutoTestOperationEnvelope prepare = adapter.Run("prepare_def_target", delegate { return targetDef; }, def => def?.defName ?? "null");
            adapter.AssertOperation("target.prepared", prepare);
            if (!prepare.Success || targetDef == null)
            {
                result.Status = "failed_prepare";
                result.Stage = "prepare_def_target";
                result.ErrorMessage = prepare.Error?.Message ?? "InfoCard Def target was null.";
                adapter.AssertTrue("target.preparation_assertion", false, result.ErrorMessage, prepare.OperationId);
                adapter.CompleteCase(false, result.ErrorMessage);
                caseScope.Dispose();
                CompleteSemanticCase(result);
                return;
            }

            activeSession = new ActiveSession
            {
                Item = item,
                Result = result,
                InfoCardDef = targetDef,
                AllowScreenshots = ShouldCaptureScreenshots(item.SpecDef),
                RemainingUiFrames = PreOpenSettleRepaintFrames,
                Phase = SessionPhase.WaitingForPreOpenSettle,
                EvidenceAdapter = adapter,
                EvidenceCaseScope = caseScope
            };
            result.Stage = "info_open";
        }

        private void StartPawnInfoCardSession(AutoTestWorkItem item)
        {
            PawnKindDef pawnKind = item.PawnKindDef;
            AutoTestResult result = CreateResult(item.ManifestEntry, nameof(PawnKindDef), pawnKind?.defName, pawnKind?.label);
            results.Add(result);

            string caseId = item.CaseId ?? item.ManifestEntry?.CaseId ?? result.CaseId;
            InfoCardOperationAdapter adapter = new InfoCardOperationAdapter(runId, caseId, operationRunner, operationJournal, logJournal, evidenceBuilder, evidenceClock);
            IDisposable caseScope = adapter.BeginCase();
            bool noSpace = false;
            Pawn preparedPawn = null;

            AutoTestOperationEnvelope prepare = adapter.Run("generate_pawn_target", delegate
            {
                if (pawnKind == null)
                {
                    throw new InvalidOperationException("PawnKindDef was null.");
                }

                if (!TryAllocateThingRoot(1, 1, out IntVec3 rootCell))
                {
                    noSpace = true;
                    return null;
                }

                Faction faction = ResolveInfoCardPawnFaction(item.Kind);
                Exception lastError = null;
                for (int attempt = 0; attempt < 4 && preparedPawn == null; attempt++)
                {
                    try
                    {
                        PawnGenerationRequest request = new PawnGenerationRequest(
                            pawnKind,
                            faction,
                            PawnGenerationContext.NonPlayer,
                            map.Tile,
                            forceGenerateNewPawn: true,
                            canGeneratePawnRelations: false,
                            mustBeCapableOfViolence: false);
                        preparedPawn = PawnGenerator.GeneratePawn(request);
                    }
                    catch (Exception ex)
                    {
                        lastError = ex;
                    }
                }

                if (preparedPawn == null)
                {
                    throw new InvalidOperationException("Pawn generation failed after four attempts.", lastError);
                }

                preparedPawn.apparel?.DestroyAll();
                preparedPawn.equipment?.DestroyAllEquipment();
                preparedPawn.inventory?.innerContainer?.ClearAndDestroyContents();

                ClearArea(rootCell, 1, 1);
                GenSpawn.Spawn(preparedPawn, rootCell, map, WipeMode.Vanish);
                preparedPawn.jobs?.StopAll();
                preparedPawn.pather?.StopDead();
                Find.Selector.ClearSelection();
                Find.Selector.Select(preparedPawn, false, false);

                result.Label = pawnKind.LabelCap;
                result.Notes = AppendNote(result.Notes, "PawnInstance=" + preparedPawn.LabelShortCap);
                result.Notes = AppendNote(result.Notes, "PawnKind=" + pawnKind.defName);
                result.Notes = AppendNote(result.Notes, "Race=" + (preparedPawn.def?.defName ?? "unknown"));
                result.Notes = AppendNote(result.Notes, "LifeStageDef=" + (preparedPawn.ageTracker?.CurLifeStage?.defName ?? "unknown"));
                result.Notes = AppendNote(result.Notes, "LifeStageIndex=" + (preparedPawn.ageTracker?.CurLifeStageIndex ?? -1));
                result.Notes = AppendNote(result.Notes, "AgeBiological=" + (preparedPawn.ageTracker?.AgeBiologicalYearsFloat.ToString("0.###") ?? "unknown"));
                result.Notes = AppendNote(result.Notes, "BodyType=" + (preparedPawn.story?.bodyType?.defName ?? "none"));
                result.Notes = AppendNote(result.Notes, "Gender=" + preparedPawn.gender);
                return preparedPawn;
            }, pawn => pawn == null ? "no_layout_space" : pawn.ThingID);
            adapter.AssertOperation("target.prepared", prepare);

            if (!prepare.Success)
            {
                result.Status = "failed_pawn_generation";
                result.Stage = "generate_pawn_target";
                result.ErrorMessage = prepare.Error?.Message ?? "Pawn generation failed.";
                adapter.CompleteCase(false, result.ErrorMessage);
                caseScope.Dispose();
                preparedPawn?.Destroy(DestroyMode.Vanish);
                CompleteSemanticCase(result);
                return;
            }

            if (noSpace)
            {
                result.Status = "skipped";
                result.Stage = "layout";
                result.ErrorMessage = "No free layout space remained on the current map.";
                adapter.AssertTrue("layout.skip_reported", true, result.ErrorMessage, prepare.OperationId);
                adapter.CompleteCase(true, result.ErrorMessage);
                caseScope.Dispose();
                CompleteSemanticCase(result);
                return;
            }

            activeSession = new ActiveSession
            {
                Item = item,
                Result = result,
                SpawnedThing = preparedPawn,
                TestPawn = preparedPawn,
                InfoCardDef = preparedPawn.def,
                AllowScreenshots = ShouldCaptureScreenshots(item.SpecDef),
                RemainingUiFrames = PreOpenSettleRepaintFrames,
                Phase = SessionPhase.WaitingForPreOpenSettle,
                EvidenceAdapter = adapter,
                EvidenceCaseScope = caseScope
            };
            activeSession.RequiredInfoCardTabs.Add(Dialog_InfoCard.InfoCardTab.Stats);
            if (preparedPawn.RaceProps?.Humanlike == true)
            {
                activeSession.RequiredInfoCardTabs.Add(Dialog_InfoCard.InfoCardTab.Character);
            }
            activeSession.RequiredInfoCardTabs.Add(Dialog_InfoCard.InfoCardTab.Health);
            result.Stage = "info_open";
        }

        private static Faction ResolveInfoCardPawnFaction(AutoTestKind kind)
        {
            if (kind == AutoTestKind.MechanoidInfoCard)
            {
                return Faction.OfMechanoids;
            }

            if (kind == AutoTestKind.PawnInfoCard)
            {
                return Faction.OfPlayer ?? Find.FactionManager?.OfPlayer;
            }

            return null;
        }
    }
}
