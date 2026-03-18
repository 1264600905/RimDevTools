using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace LogSyncMod.AutoTest
{
    // 炮塔测试阶段枚举
    internal enum TurretTestPhase
    {
        Initializing,
        WaitingForMap,   // 新增：等待地图稳定注册
        SpawningPower,
        SpawningTurret,
        Reloading,      // 新增：自动补弹
        SpawningEnemies, // 新增：生成敌人
        Fighting,       // 新增：战斗监控
        WaitingAfterKill, // 新增：杀敌后延迟
        WaitingAfterDamage,
        CapturingScreenshot,
        CleaningUp,
        Done
    }

    internal enum RangedWeaponTestPhase
    {
        Initializing,
        WaitingForMap,
        SpawningShooter,
        EquippingWeapon,
        SpawningEnemies,
        Fighting,
        WaitingAfterHit,
        CapturingScreenshot,
        CleaningUp,
        Done
    }

    internal enum MeleeWeaponTestPhase
    {
        Initializing,
        WaitingForMap,
        SpawningShooter,
        EquippingWeapon,
        SpawningEnemies,
        Fighting,
        WaitingAfterHit,
        CapturingScreenshot,
        CleaningUp,
        Done
    }

    internal sealed class StageScreenshotRequest
    {
        public string StageSuffix;
        public bool Force;
        public bool AllowCapture;
        public Vector3? FocusRootPos;
        public float? FocusRootSize;
        public int FocusDelayTicks;
        public bool PrimeOnly;
    }

    internal sealed partial class AutoTestRunner
    {
        // --- 常量和字段 ---
        private const int MaxConcurrentTurretTests = 3;
        private const int PreOpenSettleRepaintFrames = 10;
        private const int OpenStableFrameRequirement = 3;
        private const int OpenTimeoutRepaintFrames = 180;
        private const int WaitBeforeCloseRepaintFrames = 12;
        private const int CloseTimeoutRepaintFrames = 180;
        private const float DefaultTurretScreenshotAspect = 16f / 9f;

        private static readonly FieldInfo ProjectileUsedTargetField = typeof(Projectile).GetField("usedTarget", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo ProjectileIntendedTargetField = typeof(Projectile).GetField("intendedTarget", BindingFlags.Instance | BindingFlags.NonPublic);

        private readonly LogSyncSettings settings;
        private readonly Map map; // 主地图，用于UI测试等
        private readonly Queue<AutoTestWorkItem> pendingItems = new Queue<AutoTestWorkItem>();
        private readonly List<AutoTestResult> results = new List<AutoTestResult>();
        private readonly List<AutoTestManifestEntry> discoveredManifest = new List<AutoTestManifestEntry>();
        private readonly List<AutoTestManifestEntry> plannedManifest = new List<AutoTestManifestEntry>();
        private readonly HashSet<string> discoveredCaseIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> plannedCaseIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<ThingDef, Thing> placedRecipeWorktables = new Dictionary<ThingDef, Thing>();

        // 新增炮塔测试相关字段
        private readonly List<TurretTestSession> activeTurretSessions = new List<TurretTestSession>();
        private readonly Queue<ThingDef> pendingTurretDefs = new Queue<ThingDef>();
        private readonly List<RangedWeaponTestSession> activeRangedWeaponSessions = new List<RangedWeaponTestSession>();
        private readonly Queue<ThingDef> pendingRangedWeaponDefs = new Queue<ThingDef>();
        private readonly List<MeleeWeaponTestSession> activeMeleeWeaponSessions = new List<MeleeWeaponTestSession>();
        private readonly Queue<ThingDef> pendingMeleeWeaponDefs = new Queue<ThingDef>();
        private int pocketMapCooldownUntilTick = -1;
        private ModContentPack targetMod;
        private Pawn sharedHediffPawn;
        private string targetPackageId;
        private string outputDirectory;
        private int nextThingX = 10;
        private int nextThingZ = 10;
        private int currentThingRowDepth;
        private int nextTerrainX;
        private int nextTerrainZ = 10;
        private bool completed;
        private ActiveSession activeSession;
        private AutoTestRunArtifacts runArtifacts;
        private AutoTestRunSummary runSummary;
        private AutoTestScreenshotService screenshotService;
        private readonly AutoTestPocketMapService pocketMapService;
        private bool? cameraPlusActive;
        private int nextDiscoverOrder = 1;
        private int nextPlannedOrder = 1;

        public AutoTestRunner(LogSyncSettings settings, Map map)
        {
            this.settings = settings;
            this.map = map;
            pocketMapService = new AutoTestPocketMapService();
        }

        public bool Completed => completed;

        public void Initialize()
        {
            targetPackageId = (settings.autoTestTargetPackageId ?? string.Empty).Trim().ToLowerInvariant();
            outputDirectory = ResolveOutputDirectory(settings);
            runArtifacts = AutoTestReportWriter.CreateRunArtifacts(outputDirectory, targetPackageId);
            screenshotService = new AutoTestScreenshotService(runArtifacts, AppendNote, TrimForNote);
            targetMod = LoadedModManager.RunningMods.FirstOrDefault(mod =>
                string.Equals(mod.PackageIdPlayerFacing, targetPackageId, StringComparison.OrdinalIgnoreCase));

            if (targetMod == null)
            {
                Log.Error("[LogSyncAutoTest] Target mod not found: " + targetPackageId);
                Finish();
                return;
            }

            LoadActiveSpecs();

            nextTerrainX = Math.Max(map.Size.x / 2, 10);

            if (settings.ShouldTestContent())
            {
                EnqueueThingDefs();
                EnqueueTerrainDefs();
            }

            if (settings.ShouldTestRecipes())
            {
                UnlockAllResearch();
                EnqueueRecipeDefs();
            }

            if (settings.ShouldTestHediffs())
            {
                EnqueueHediffDefs();
            }

            if (settings.ShouldTestTurrets())
            {
                BeginTurretTestSession();
            }

            if (settings.ShouldTestRangedWeapons())
            {
                BeginRangedWeaponTestSession();
            }

            if (settings.ShouldTestMeleeWeapons())
            {
                BeginMeleeWeaponTestSession();
            }

            ApplyBatchLimit();
            BuildPlannedManifest();

            int totalPending = pendingItems.Count + pendingTurretDefs.Count + pendingRangedWeaponDefs.Count + pendingMeleeWeaponDefs.Count;
            Log.Message(string.Format(
                "[LogSyncAutoTest] START packageId={0} map={1} scope={2} pending={3}",
                targetPackageId,
                map.info?.parent?.LabelCap ?? map.ToString(),
                settings.autoTestScope,
                totalPending));

            if (totalPending == 0)
            {
                Log.Warning("[LogSyncAutoTest] No matching auto-test targets were found for " + targetPackageId);
                Finish();
            }
        }

        public void Tick()
        {
            if (completed) return;

            if (activeSession == null && pendingItems.Count > 0)
            {
                StartNextSession();
            }

            if (activeSession != null || pendingItems.Count > 0)
            {
                return;
            }

            if (Find.TickManager.TicksGame > pocketMapCooldownUntilTick)
            {
                while (activeTurretSessions.Count < MaxConcurrentTurretTests && pendingTurretDefs.Count > 0)
                {
                    ThingDef turretDef = pendingTurretDefs.Dequeue();
                    StartNewTurretSession(turretDef);
                }

                while (activeRangedWeaponSessions.Count < MaxConcurrentTurretTests && pendingRangedWeaponDefs.Count > 0)
                {
                    ThingDef weaponDef = pendingRangedWeaponDefs.Dequeue();
                    StartNewRangedWeaponSession(weaponDef);
                }

                while (activeMeleeWeaponSessions.Count < MaxConcurrentTurretTests && pendingMeleeWeaponDefs.Count > 0)
                {
                    ThingDef weaponDef = pendingMeleeWeaponDefs.Dequeue();
                    StartNewMeleeWeaponSession(weaponDef);
                }
            }

            if (Find.TickManager.TicksGame % 500 == 0 && activeTurretSessions.Count > 0)
            {
                string statusReport = string.Join(", ", activeTurretSessions.Select(s => $"{s.TurretDef.defName}({s.Phase})"));
                Log.Message($"[LogSyncAutoTest] Heartbeat: {activeTurretSessions.Count} active sessions. Details: {statusReport}");
            }

            if (Find.TickManager.TicksGame % 500 == 0 && activeRangedWeaponSessions.Count > 0)
            {
                string statusReport = string.Join(", ", activeRangedWeaponSessions.Select(s => $"{s.WeaponDef.defName}({s.Phase})"));
                Log.Message($"[LogSyncAutoTest] Ranged heartbeat: {activeRangedWeaponSessions.Count} active sessions. Details: {statusReport}");
            }

            if (Find.TickManager.TicksGame % 500 == 0 && activeMeleeWeaponSessions.Count > 0)
            {
                string statusReport = string.Join(", ", activeMeleeWeaponSessions.Select(s => $"{s.WeaponDef.defName}({s.Phase})"));
                Log.Message($"[LogSyncAutoTest] Melee heartbeat: {activeMeleeWeaponSessions.Count} active sessions. Details: {statusReport}");
            }

            foreach (var session in activeTurretSessions)
            {
                session.Update();
            }

            foreach (var session in activeRangedWeaponSessions)
            {
                session.Update();
            }

            foreach (var session in activeMeleeWeaponSessions)
            {
                session.Update();
            }

            activeTurretSessions.RemoveAll(session => {
                if (session.Phase == TurretTestPhase.Done) {
                    results.Add(session.Result);
                    pocketMapCooldownUntilTick = Find.TickManager.TicksGame + 120;
                    return true;
                }
                return false;
            });

            activeRangedWeaponSessions.RemoveAll(session => {
                if (session.Phase == RangedWeaponTestPhase.Done) {
                    results.Add(session.Result);
                    pocketMapCooldownUntilTick = Find.TickManager.TicksGame + 120;
                    return true;
                }
                return false;
            });

            activeMeleeWeaponSessions.RemoveAll(session => {
                if (session.Phase == MeleeWeaponTestPhase.Done) {
                    results.Add(session.Result);
                    pocketMapCooldownUntilTick = Find.TickManager.TicksGame + 120;
                    return true;
                }
                return false;
            });

            if (pendingItems.Count == 0 &&
                activeSession == null &&
                pendingTurretDefs.Count == 0 &&
                pendingRangedWeaponDefs.Count == 0 &&
                pendingMeleeWeaponDefs.Count == 0 &&
                activeTurretSessions.Count == 0 &&
                activeRangedWeaponSessions.Count == 0 &&
                activeMeleeWeaponSessions.Count == 0)
            {
                Finish();
            }
        }

        private void AddImmediateResult(AutoTestResult result, string status, string stage, string errorMessage)
        {
            result.Status = status;
            result.Stage = stage;
            result.ErrorMessage = errorMessage ?? string.Empty;
            results.Add(result);
        }

        private void Finish()
        {
            if (completed) return;
            completed = true;
            CleanupSelectionAndWindows();
            CleanupSharedHediffPawn();

            runSummary = BuildRunSummary();
            AutoTestReportWriter.Write(runArtifacts, targetPackageId ?? "unknown", results, targetMod?.Name ?? "Unknown", discoveredManifest, plannedManifest, runSummary);
            Log.Message(string.Format(
                "[LogSyncAutoTest] COMPLETED packageId={0} total={1} passed={2} failed={3} discovered={4} planned={5} missing={6}",
                targetPackageId ?? "unknown",
                results.Count,
                results.Count(result => result.Status == "passed"),
                results.Count(result => result.Status != "passed" && result.Status != "skipped"),
                runSummary?.DiscoveredCount ?? 0,
                runSummary?.PlannedCount ?? 0,
                runSummary?.MissingCount ?? 0));
        }

        private void CleanupSharedHediffPawn()
        {
            if (sharedHediffPawn == null)
            {
                return;
            }

            try
            {
                CleanupTargetModHediffs(sharedHediffPawn);
                if (!sharedHediffPawn.Destroyed)
                {
                    sharedHediffPawn.Destroy(DestroyMode.Vanish);
                }
            }
            catch
            {
            }

            sharedHediffPawn = null;
        }

        private static string ResolveOutputDirectory(LogSyncSettings settings)
        {
            string configuredPath = (settings.autoTestOutputPath ?? string.Empty).Trim();
            if (!string.IsNullOrEmpty(configuredPath))
            {
                return configuredPath;
            }

            string logDirectory = Path.GetDirectoryName(settings.logPath ?? string.Empty);
            if (!string.IsNullOrEmpty(logDirectory))
            {
                return Path.Combine(logDirectory, "AutoTest");
            }

            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "RimWorldAutoTest");
        }

        private static string TrimForNote(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            string normalized = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return normalized.Length <= 160 ? normalized : normalized.Substring(0, 160) + "...";
        }

        private static string AppendNote(string existing, string next)
        {
            if (string.IsNullOrWhiteSpace(existing)) return next ?? string.Empty;
            if (string.IsNullOrWhiteSpace(next)) return existing;
            return existing + " | " + next;
        }

        private string BuildWindowSummary()
        {
            if (Find.WindowStack == null) return "WindowStack=null";
            return string.Join(", ", Find.WindowStack.Windows.Select(window => window.GetType().Name).ToArray());
        }

        private void ApplyBatchLimit()
        {
            if (IsFullCoverageRun())
            {
                Log.Message("[LogSyncAutoTest] Full coverage run detected. Batch limit is ignored.");
                return;
            }

            int limit = Math.Max(1, settings.autoTestBatchSize);
            int originalTotal = pendingItems.Count + pendingTurretDefs.Count + pendingRangedWeaponDefs.Count + pendingMeleeWeaponDefs.Count;
            if (originalTotal <= limit)
            {
                return;
            }

            var limitedItems = new Queue<AutoTestWorkItem>();
            var limitedTurrets = new Queue<ThingDef>();
            var limitedRangedWeapons = new Queue<ThingDef>();
            var limitedMeleeWeapons = new Queue<ThingDef>();
            int keptCount = 0;

            while (keptCount < limit)
            {
                bool itemAddedInRound = false;

                if (pendingItems.Count > 0)
                {
                    limitedItems.Enqueue(pendingItems.Dequeue());
                    keptCount++;
                    itemAddedInRound = true;
                    if (keptCount >= limit) break;
                }

                if (pendingTurretDefs.Count > 0)
                {
                    limitedTurrets.Enqueue(pendingTurretDefs.Dequeue());
                    keptCount++;
                    itemAddedInRound = true;
                    if (keptCount >= limit) break;
                }

                if (pendingRangedWeaponDefs.Count > 0)
                {
                    limitedRangedWeapons.Enqueue(pendingRangedWeaponDefs.Dequeue());
                    keptCount++;
                    itemAddedInRound = true;
                    if (keptCount >= limit) break;
                }

                if (pendingMeleeWeaponDefs.Count > 0)
                {
                    limitedMeleeWeapons.Enqueue(pendingMeleeWeaponDefs.Dequeue());
                    keptCount++;
                    itemAddedInRound = true;
                    if (keptCount >= limit) break;
                }

                if (!itemAddedInRound)
                {
                    break; // All queues are empty
                }
            }

            // Clear the remaining items in the original queues
            pendingItems.Clear();
            pendingTurretDefs.Clear();
            pendingRangedWeaponDefs.Clear();
            pendingMeleeWeaponDefs.Clear();

            // Repopulate with the limited items
            foreach (var item in limitedItems) pendingItems.Enqueue(item);
            foreach (var item in limitedTurrets) pendingTurretDefs.Enqueue(item);
            foreach (var item in limitedRangedWeapons) pendingRangedWeaponDefs.Enqueue(item);
            foreach (var item in limitedMeleeWeapons) pendingMeleeWeaponDefs.Enqueue(item);

            Log.Message(string.Format(
                "[LogSyncAutoTest] Batch limit applied. Original={0}, Kept={1}",
                originalTotal,
                keptCount));
        }

        private AutoTestCaseType ResolveCaseType(AutoTestKind kind)
        {
            switch (kind)
            {
                case AutoTestKind.Building:
                    return AutoTestCaseType.BuildingInfoCard;
                case AutoTestKind.Item:
                    return AutoTestCaseType.ItemInfoCard;
                case AutoTestKind.Terrain:
                    return AutoTestCaseType.TerrainInfoCard;
                case AutoTestKind.Recipe:
                    return AutoTestCaseType.RecipeInfoCard;
                case AutoTestKind.Hediff:
                    return AutoTestCaseType.HediffInfoCard;
                case AutoTestKind.TurretPocketMap:
                    return AutoTestCaseType.TurretPocketMap;
                case AutoTestKind.RangedWeaponPocketMap:
                    return AutoTestCaseType.RangedWeaponPocketMap;
                case AutoTestKind.MeleeWeaponPocketMap:
                    return AutoTestCaseType.MeleeWeaponPocketMap;
                default:
                    return AutoTestCaseType.ItemInfoCard;
            }
        }

        private string ResolveCategory(AutoTestCaseType caseType)
        {
            switch (caseType)
            {
                case AutoTestCaseType.BuildingInfoCard:
                    return "Building";
                case AutoTestCaseType.ItemInfoCard:
                    return "Item";
                case AutoTestCaseType.TerrainInfoCard:
                    return "Terrain";
                case AutoTestCaseType.RecipeInfoCard:
                    return "Recipe";
                case AutoTestCaseType.HediffInfoCard:
                    return "Hediff";
                case AutoTestCaseType.TurretPocketMap:
                    return "Turret";
                case AutoTestCaseType.RangedWeaponPocketMap:
                    return "RangedWeapon";
                case AutoTestCaseType.MeleeWeaponPocketMap:
                    return "MeleeWeapon";
                default:
                    return caseType.ToString();
            }
        }

        private AutoTestScope ResolveScope(AutoTestCaseType caseType)
        {
            switch (caseType)
            {
                case AutoTestCaseType.RecipeInfoCard:
                    return AutoTestScope.Recipes;
                case AutoTestCaseType.HediffInfoCard:
                    return AutoTestScope.Hediffs;
                case AutoTestCaseType.TurretPocketMap:
                    return AutoTestScope.Turrets;
                case AutoTestCaseType.RangedWeaponPocketMap:
                    return AutoTestScope.RangedWeapons;
                case AutoTestCaseType.MeleeWeaponPocketMap:
                    return AutoTestScope.MeleeWeapons;
                default:
                    return AutoTestScope.Content;
            }
        }

        private string BuildCaseId(AutoTestCaseType caseType, string defName)
        {
            return (targetPackageId ?? "unknown") + "|" + caseType + "|" + (defName ?? string.Empty);
        }

        private AutoTestManifestEntry RegisterDiscoveredCase(AutoTestCaseType caseType, string defType, string defName, string label)
        {
            string caseId = BuildCaseId(caseType, defName);
            if (!discoveredCaseIds.Add(caseId))
            {
                return discoveredManifest.First(manifestEntry => string.Equals(manifestEntry.CaseId, caseId, StringComparison.OrdinalIgnoreCase));
            }

            AutoTestManifestEntry entry = new AutoTestManifestEntry
            {
                CaseId = caseId,
                CaseType = caseType.ToString(),
                Category = ResolveCategory(caseType),
                Scope = ResolveScope(caseType).ToString(),
                DefType = defType ?? string.Empty,
                DefName = defName ?? string.Empty,
                Label = label ?? string.Empty,
                DiscoverOrder = nextDiscoverOrder++
            };

            discoveredManifest.Add(entry);
            return entry;
        }

        private AutoTestWorkItem CreateWorkItem(
            AutoTestKind kind,
            ThingDef thingDef = null,
            TerrainDef terrainDef = null,
            RecipeDef recipeDef = null,
            ThingDef worktableDef = null,
            HediffDef hediffDef = null,
            LogSyncAutoTestSpecDef specDef = null)
        {
            AutoTestCaseType caseType = ResolveCaseType(kind);
            Def targetDef = (Def)thingDef ?? terrainDef ?? (Def)recipeDef ?? hediffDef;
            string defType = targetDef?.GetType().Name ?? string.Empty;
            AutoTestManifestEntry entry = RegisterDiscoveredCase(caseType, defType, targetDef?.defName, targetDef?.label);

            return new AutoTestWorkItem
            {
                Kind = kind,
                CaseType = caseType,
                CaseId = entry.CaseId,
                ThingDef = thingDef,
                TerrainDef = terrainDef,
                RecipeDef = recipeDef,
                WorktableDef = worktableDef,
                HediffDef = hediffDef,
                SpecDef = specDef,
                ManifestEntry = entry
            };
        }

        private AutoTestResult CreateResult(AutoTestManifestEntry entry, string defType, string defName, string label)
        {
            AutoTestCaseType caseType;
            if (!Enum.TryParse(entry?.CaseType ?? string.Empty, out caseType))
            {
                caseType = AutoTestCaseType.ItemInfoCard;
            }

            return new AutoTestResult
            {
                PackageId = targetPackageId,
                CaseId = entry?.CaseId ?? BuildCaseId(caseType, defName),
                CaseType = caseType.ToString(),
                Scope = entry?.Scope ?? ResolveScope(caseType).ToString(),
                Category = entry?.Category ?? ResolveCategory(caseType),
                DefType = defType ?? string.Empty,
                DefName = defName ?? string.Empty,
                Label = label ?? string.Empty,
                Status = "pending",
                Stage = "collect",
                ErrorMessage = string.Empty,
                ScreenshotPath = string.Empty,
                Notes = string.Empty
            };
        }

        private AutoTestResult CreateResult(AutoTestCaseType caseType, string defType, string defName, string label)
        {
            AutoTestManifestEntry entry = RegisterDiscoveredCase(caseType, defType, defName, label);
            return CreateResult(entry, defType, defName, label);
        }

        private bool IsFullCoverageRun()
        {
            return !settings.UseXmlDrivenAutoTest() && settings.autoTestScope == AutoTestScope.All;
        }

        private AutoTestPocketMapHandle CreatePocketMapHandle(string mapGeneratorDefName, IntVec3 mapSize, Action<Map> onGenerated, out string errorMessage)
        {
            return pocketMapService.Create(new AutoTestPocketMapRequest
            {
                SourceMap = map,
                MapGeneratorDefName = mapGeneratorDefName,
                MapSize = mapSize,
                OnGenerated = onGenerated
            }, out errorMessage);
        }

        private void DestroyPocketMap(Map pocketMap, MapParent parent)
        {
            pocketMapService.Destroy(pocketMap, parent);
        }

        private string ResolveRunMode()
        {
            return IsFullCoverageRun() ? "Full" : "Targeted";
        }

        private void BuildPlannedManifest()
        {
            plannedManifest.Clear();
            plannedCaseIds.Clear();
            nextPlannedOrder = 1;

            foreach (AutoTestWorkItem item in pendingItems)
            {
                AddPlannedManifestEntry(item.ManifestEntry);
            }

            foreach (ThingDef def in pendingTurretDefs)
            {
                AddPlannedManifestEntry(RegisterDiscoveredCase(AutoTestCaseType.TurretPocketMap, nameof(ThingDef), def?.defName, def?.label));
            }

            foreach (ThingDef def in pendingRangedWeaponDefs)
            {
                AddPlannedManifestEntry(RegisterDiscoveredCase(AutoTestCaseType.RangedWeaponPocketMap, nameof(ThingDef), def?.defName, def?.label));
            }

            foreach (ThingDef def in pendingMeleeWeaponDefs)
            {
                AddPlannedManifestEntry(RegisterDiscoveredCase(AutoTestCaseType.MeleeWeaponPocketMap, nameof(ThingDef), def?.defName, def?.label));
            }

            foreach (AutoTestResult result in results)
            {
                AddPlannedManifestEntry(RegisterDiscoveredCase(
                    ParseCaseType(result.CaseType),
                    result.DefType,
                    result.DefName,
                    result.Label));
            }
        }

        private void AddPlannedManifestEntry(AutoTestManifestEntry entry)
        {
            if (entry == null || !plannedCaseIds.Add(entry.CaseId))
            {
                return;
            }

            entry.Planned = true;
            entry.PlannedOrder = nextPlannedOrder++;
            plannedManifest.Add(entry);
        }

        private AutoTestCaseType ParseCaseType(string text)
        {
            AutoTestCaseType caseType;
            return Enum.TryParse(text ?? string.Empty, out caseType)
                ? caseType
                : AutoTestCaseType.ItemInfoCard;
        }

        private AutoTestRunSummary BuildRunSummary()
        {
            AutoTestRunSummary summary = new AutoTestRunSummary();
            summary.RunMode = ResolveRunMode();
            summary.DiscoveredCount = discoveredManifest.Count;
            summary.PlannedCount = plannedManifest.Count;
            summary.ExecutedCount = results.Count;
            summary.PassedCount = results.Count(result => result.Status == "passed");
            summary.SkippedCount = results.Count(result => result.Status == "skipped");
            summary.FailedCount = results.Count(result => result.Status != "passed" && result.Status != "skipped");

            HashSet<string> executedCaseIds = new HashSet<string>(
                results
                    .Where(result => !string.IsNullOrWhiteSpace(result.CaseId))
                    .Select(result => result.CaseId),
                StringComparer.OrdinalIgnoreCase);

            foreach (AutoTestManifestEntry entry in plannedManifest)
            {
                if (!executedCaseIds.Contains(entry.CaseId))
                {
                    summary.MissingCaseIds.Add(entry.CaseId);
                }
            }

            summary.MissingCount = summary.MissingCaseIds.Count;
            summary.Complete = summary.MissingCount == 0 && summary.ExecutedCount >= summary.PlannedCount;
            return summary;
        }

        private bool TryCaptureTurretScreenshot(TurretTestSession session, bool force)
        {
            if (session == null)
            {
                return false;
            }

            if (force)
            {
                return TryCaptureTurretFailureScreenshot(session);
            }

            return TryCaptureTurretStageScreenshot(session, session.Result?.Status ?? session.Phase.ToString(), force: false, allowCapture: session.AllowScreenshots);
        }

        private bool TryCaptureTurretStageScreenshot(TurretTestSession session, string stageSuffix, bool force, bool allowCapture)
        {
            if (session == null || screenshotService == null)
            {
                return false;
            }

            if (session.TestMap == null || !Find.Maps.Contains(session.TestMap))
            {
                return false;
            }

            return screenshotService.TryCapture(new AutoTestScreenshotRequest
            {
                Result = session.Result,
                AllowCapture = allowCapture,
                Force = force,
                ResultIndex = screenshotService.ResolveTurretResultIndex(results, activeTurretSessions.IndexOf(session)),
                StageSuffix = stageSuffix,
                Prepare = () => FocusTurretScreenshotMap(session),
                Guard = () => session.TestMap != null && Find.Maps.Contains(session.TestMap),
                Profile = AutoTestScreenshotProfiles.TurretPrimary,
                SuccessNoteKey = "TurretScreenshotCaptured=True",
                FailureNoteKey = "TurretFailureScreenshotCaptured=True"
            });
        }

        private bool TryCaptureTurretFailureScreenshot(TurretTestSession session)
        {
            if (session == null || screenshotService == null || session.TestMap == null || !Find.Maps.Contains(session.TestMap))
            {
                return false;
            }

            return screenshotService.TryCapture(new AutoTestScreenshotRequest
            {
                Result = session.Result,
                AllowCapture = true,
                Force = true,
                ResultIndex = screenshotService.ResolveTurretResultIndex(results, activeTurretSessions.IndexOf(session)),
                StageSuffix = session.Result?.Status ?? "failed",
                RelativeSubdirectory = "fail",
                Prepare = () => FocusTurretScreenshotMap(session),
                Guard = () => session.TestMap != null && Find.Maps.Contains(session.TestMap),
                Profile = AutoTestScreenshotProfiles.TurretPrimary,
                SuccessNoteKey = "TurretScreenshotCaptured=True",
                FailureNoteKey = "TurretFailureScreenshotCaptured=True"
            });
        }

        private bool TryCaptureRangedWeaponScreenshot(RangedWeaponTestSession session, bool force)
        {
            if (session == null) return false;
            if (force) return TryCaptureRangedWeaponFailureScreenshot(session);
            return TryCaptureRangedWeaponStageScreenshot(session, session.Result?.Status ?? session.Phase.ToString(), force: false, allowCapture: session.AllowScreenshots);
        }

        private bool TryCaptureRangedWeaponStageScreenshot(RangedWeaponTestSession session, string stageSuffix, bool force, bool allowCapture)
        {
            if (session == null || screenshotService == null || session.TestMap == null || !Find.Maps.Contains(session.TestMap)) return false;
            return screenshotService.TryCapture(new AutoTestScreenshotRequest
            {
                Result = session.Result,
                AllowCapture = allowCapture,
                Force = force,
                ResultIndex = screenshotService.ResolveTurretResultIndex(results, activeRangedWeaponSessions.IndexOf(session)),
                StageSuffix = stageSuffix,
                Prepare = () => FocusRangedWeaponScreenshotMap(session),
                Guard = () => session.TestMap != null && Find.Maps.Contains(session.TestMap),
                Profile = AutoTestScreenshotProfiles.RangedPrimary,
                SuccessNoteKey = "TurretScreenshotCaptured=True",
                FailureNoteKey = "TurretFailureScreenshotCaptured=True"
            });
        }

        private bool TryCaptureRangedWeaponFailureScreenshot(RangedWeaponTestSession session)
        {
            if (session == null || screenshotService == null || session.TestMap == null || !Find.Maps.Contains(session.TestMap)) return false;
            return screenshotService.TryCapture(new AutoTestScreenshotRequest
            {
                Result = session.Result,
                AllowCapture = true,
                Force = true,
                ResultIndex = screenshotService.ResolveTurretResultIndex(results, activeRangedWeaponSessions.IndexOf(session)),
                StageSuffix = session.Result?.Status ?? "failed",
                RelativeSubdirectory = "fail",
                Prepare = () => FocusPocketMapOverview(session.TestMap),
                Guard = () => session.TestMap != null && Find.Maps.Contains(session.TestMap),
                Profile = AutoTestScreenshotProfiles.PocketMapOverview,
                SuccessNoteKey = "TurretScreenshotCaptured=True",
                FailureNoteKey = "TurretFailureScreenshotCaptured=True"
            });
        }

        private bool TryCaptureMeleeWeaponScreenshot(MeleeWeaponTestSession session, bool force)
        {
            if (session == null) return false;
            if (force) return TryCaptureMeleeWeaponFailureScreenshot(session);
            return TryCaptureMeleeWeaponStageScreenshot(session, session.Result?.Status ?? session.Phase.ToString(), force: false, allowCapture: session.AllowScreenshots);
        }

        private bool TryCaptureMeleeWeaponStageScreenshot(MeleeWeaponTestSession session, string stageSuffix, bool force, bool allowCapture)
        {
            if (session == null || screenshotService == null || session.TestMap == null || !Find.Maps.Contains(session.TestMap)) return false;
            return screenshotService.TryCapture(new AutoTestScreenshotRequest
            {
                Result = session.Result,
                AllowCapture = allowCapture,
                Force = force,
                ResultIndex = screenshotService.ResolveTurretResultIndex(results, activeMeleeWeaponSessions.IndexOf(session)),
                StageSuffix = stageSuffix,
                Prepare = () => FocusMeleeWeaponScreenshotMap(session),
                Guard = () => session.TestMap != null && Find.Maps.Contains(session.TestMap),
                Profile = AutoTestScreenshotProfiles.MeleePrimary,
                SuccessNoteKey = "TurretScreenshotCaptured=True",
                FailureNoteKey = "TurretFailureScreenshotCaptured=True"
            });
        }

        private bool TryCaptureMeleeWeaponFailureScreenshot(MeleeWeaponTestSession session)
        {
            if (session == null || screenshotService == null || session.TestMap == null || !Find.Maps.Contains(session.TestMap)) return false;
            return screenshotService.TryCapture(new AutoTestScreenshotRequest
            {
                Result = session.Result,
                AllowCapture = true,
                Force = true,
                ResultIndex = screenshotService.ResolveTurretResultIndex(results, activeMeleeWeaponSessions.IndexOf(session)),
                StageSuffix = session.Result?.Status ?? "failed",
                RelativeSubdirectory = "fail",
                Prepare = () => FocusPocketMapOverview(session.TestMap),
                Guard = () => session.TestMap != null && Find.Maps.Contains(session.TestMap),
                Profile = AutoTestScreenshotProfiles.PocketMapOverview,
                SuccessNoteKey = "TurretScreenshotCaptured=True",
                FailureNoteKey = "TurretFailureScreenshotCaptured=True"
            });
        }

        private bool TryCaptureMeleeActionScreenshot(MeleeWeaponTestSession session)
        {
            if (session == null || screenshotService == null) return false;

            return screenshotService.TryCapture(new AutoTestScreenshotRequest
            {
                Result = session.Result,
                AllowCapture = true,
                Force = true,
                ResultIndex = screenshotService.ResolveTurretResultIndex(results, activeMeleeWeaponSessions.IndexOf(session)),
                StageSuffix = "action_t15",
                Prepare = () => FocusMeleeAction(session),
                Guard = () => session.TestMap != null && Find.Maps.Contains(session.TestMap),
                Profile = AutoTestScreenshotProfiles.MeleeAction,
                SuccessNoteKey = "TurretScreenshotCaptured=True",
                FailureNoteKey = "TurretFailureScreenshotCaptured=True"
            });
        }

        private bool FocusTurretScreenshotMap(TurretTestSession session)
        {
            if (session?.TestMap == null || !Find.Maps.Contains(session.TestMap)) return false;

            try
            {
                if (Current.Game != null) Current.Game.CurrentMap = session.TestMap;
                CameraDriver cameraDriver = Find.CameraDriver;
                if (cameraDriver == null) return false;

                float aspect = ResolveTurretScreenshotAspect(cameraDriver);
                if (TryResolveTurretScreenshotView(session, aspect, out Vector3 targetPos, out float rootSize))
                {
                    // 显式重置 Y 轴高度并使用 SetRootPosAndSize，确保相机位于地图上方并重新计算正确的 Y 轴。
                    cameraDriver.SetRootPosAndSize(new Vector3(targetPos.x, AutoTestScreenshotProfiles.TurretPrimary.CameraHeight, targetPos.z), rootSize);
                }
                else
                {
                    Vector3 center = ToMapWorldCenter(session.SpawnedTurret?.Position ?? session.TestMap.Center);
                    cameraDriver.SetRootPosAndSize(new Vector3(center.x, AutoTestScreenshotProfiles.TurretPrimary.CameraHeight, center.z), AutoTestScreenshotProfiles.TurretPrimary.MinRootSize);
                }

                return true;
            }
            catch (Exception ex)
            {
                session.Result.Notes = AppendNote(session.Result.Notes, "TurretScreenshotFocusError=" + TrimForNote(ex.Message));
                Log.Warning("[LogSyncAutoTest] Turret screenshot focus failed for " + session.Result.DefName + ": " + ex.Message);
                return false;
            }
        }

        private bool FocusRangedWeaponScreenshotMap(RangedWeaponTestSession session)
        {
            if (session?.TestMap == null || !Find.Maps.Contains(session.TestMap)) return false;

            try
            {
                if (Current.Game != null) Current.Game.CurrentMap = session.TestMap;

                CameraDriver cameraDriver = Find.CameraDriver;
                if (cameraDriver == null) return false;

                float aspect = ResolveTurretScreenshotAspect(cameraDriver);
                if (TryResolveRangedWeaponScreenshotView(session, aspect, out Vector3 targetPos, out float rootSize))
                {
                    cameraDriver.SetRootPosAndSize(new Vector3(targetPos.x, AutoTestScreenshotProfiles.RangedPrimary.CameraHeight, targetPos.z), rootSize);
                }
                else
                {
                    Vector3 center = ToMapWorldCenter(session.ShooterPawn?.Position ?? session.TestMap.Center);
                    cameraDriver.SetRootPosAndSize(new Vector3(center.x, AutoTestScreenshotProfiles.RangedPrimary.CameraHeight, center.z), AutoTestScreenshotProfiles.RangedPrimary.MinRootSize);
                }

                return true;
            }
            catch (Exception ex)
            {
                session.Result.Notes = AppendNote(session.Result.Notes, "RangedScreenshotFocusError=" + TrimForNote(ex.Message));
                Log.Warning("[LogSyncAutoTest] Ranged screenshot focus failed for " + session.Result.DefName + ": " + ex.Message);
                return false;
            }
        }

        private bool FocusMeleeWeaponScreenshotMap(MeleeWeaponTestSession session)
        {
            if (session?.TestMap == null || !Find.Maps.Contains(session.TestMap)) return false;

            try
            {
                if (Current.Game != null) Current.Game.CurrentMap = session.TestMap;

                CameraDriver cameraDriver = Find.CameraDriver;
                if (cameraDriver == null) return false;

                if (TryResolveMeleeWeaponScreenshotView(session, out Vector3 targetPos, out float rootSize))
                {
                    cameraDriver.SetRootPosAndSize(new Vector3(targetPos.x, AutoTestScreenshotProfiles.MeleePrimary.CameraHeight, targetPos.z), rootSize);
                }
                else
                {
                    Vector3 center = ToMapWorldCenter(session.ShooterPawn?.Position ?? session.TestMap.Center);
                    cameraDriver.SetRootPosAndSize(new Vector3(center.x, AutoTestScreenshotProfiles.MeleePrimary.CameraHeight, center.z), AutoTestScreenshotProfiles.MeleePrimary.FixedRootSize ?? 11f);
                }

                return true;
            }
            catch (Exception ex)
            {
                session.Result.Notes = AppendNote(session.Result.Notes, "MeleeScreenshotFocusError=" + TrimForNote(ex.Message));
                Log.Warning("[LogSyncAutoTest] Melee screenshot focus failed for " + session.Result.DefName + ": " + ex.Message);
                return false;
            }
        }

        private void FocusMeleeAction(MeleeWeaponTestSession session)
        {
            if (session?.ShooterPawn == null || session.TestMap == null) return;

            try
            {
                if (Current.Game != null) Current.Game.CurrentMap = session.TestMap;

                CameraDriver cameraDriver = Find.CameraDriver;
                if (cameraDriver == null) return;

                Vector3 center = session.ShooterPawn.DrawPos;
                cameraDriver.SetRootPosAndSize(new Vector3(center.x, AutoTestScreenshotProfiles.MeleeAction.CameraHeight, center.z), AutoTestScreenshotProfiles.MeleeAction.FixedRootSize ?? 11f);
            }
            catch (Exception ex)
            {
                Log.Warning("[LogSyncAutoTest] Melee action focus failed: " + ex.Message);
            }
        }

        private bool FocusPocketMapOverview(Map targetMap)
        {
            if (targetMap == null || !Find.Maps.Contains(targetMap)) return false;

            try
            {
                if (Current.Game != null) Current.Game.CurrentMap = targetMap;
                CameraDriver cameraDriver = Find.CameraDriver;
                if (cameraDriver == null) return false;

                float aspect = ResolveTurretScreenshotAspect(cameraDriver);
                float normalizedAspect = aspect > 0.01f ? aspect : DefaultTurretScreenshotAspect;
                float rootSize = Mathf.Max(
                    targetMap.Size.z * AutoTestScreenshotProfiles.PocketMapOverview.OverviewScale,
                    (targetMap.Size.x / normalizedAspect) * AutoTestScreenshotProfiles.PocketMapOverview.OverviewScale);
                rootSize = Mathf.Max(rootSize, AutoTestScreenshotProfiles.PocketMapOverview.MinRootSize);

                cameraDriver.SetRootPosAndSize(new Vector3(targetMap.Center.x, AutoTestScreenshotProfiles.PocketMapOverview.CameraHeight, targetMap.Center.z), rootSize);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning("[LogSyncAutoTest] Pocket map overview focus failed: " + ex.Message);
                return false;
            }
        }

        private bool TryResolveTurretScreenshotView(TurretTestSession session, float aspect, out Vector3 rootPos, out float rootSize)
        {
            if (TryGetStageScreenshotOverride(session, out rootPos, out rootSize))
            {
                return true;
            }

            rootPos = default;
            bool useCameraPlusZoom = IsCameraPlusActive();
            ResolveScreenshotBounds(AutoTestScreenshotProfiles.TurretPrimary, useCameraPlusZoom, out float minRootSize, out float maxRootSize, out float cellMargin);
            rootSize = minRootSize;

            if (session?.SpawnedTurret == null || session.SpawnedTurret.Destroyed)
            {
                return false;
            }

            Vector3 turretPos = ToMapWorldCenter(session.SpawnedTurret.Position);
            Pawn targetPawn = ResolveTurretScreenshotTarget(session);
            if (targetPawn == null)
            {
                rootPos = turretPos;
                rootSize = minRootSize;
                return true;
            }

            Vector3 targetPos = ToMapWorldCenter(targetPawn.Position);
            rootPos = (turretPos + targetPos) * 0.5f;

            float halfWidth = Mathf.Abs(turretPos.x - targetPos.x) * 0.5f + cellMargin;
            float halfHeight = Mathf.Abs(turretPos.z - targetPos.z) * 0.5f + cellMargin;

            float normalizedAspect = aspect > 0.01f ? aspect : DefaultTurretScreenshotAspect;
            float requiredRootSize = Mathf.Max(halfHeight, halfWidth / normalizedAspect) * AutoTestScreenshotProfiles.TurretPrimary.RangeScale;
            rootSize = Mathf.Clamp(requiredRootSize, minRootSize, maxRootSize);
            return true;
        }

        private bool TryResolveProjectileFocusView(TurretTestSession session, Thing projectileThing, float aspect, out Vector3 rootPos, out float rootSize)
        {
            rootPos = default;
            rootSize = AutoTestScreenshotProfiles.TurretPrimary.MinRootSize;
            if (session?.SpawnedTurret == null || projectileThing == null)
            {
                return false;
            }

            LocalTargetInfo target = ResolveProjectileTargetInfo(projectileThing as Projectile);
            if (!target.IsValid)
            {
                return false;
            }

            Vector3 turretPos = ToMapWorldCenter(session.SpawnedTurret.Position);
            Vector3 targetPos = target.Thing != null
                ? target.Thing.DrawPos
                : ToMapWorldCenter(target.Cell);

            bool useCameraPlusZoom = IsCameraPlusActive();
            ResolveScreenshotBounds(AutoTestScreenshotProfiles.TurretPrimary, useCameraPlusZoom, out float minRootSize, out float maxRootSize, out float cellMargin);

            rootPos = (turretPos + targetPos) * 0.5f;
            float normalizedAspect = aspect > 0.01f ? aspect : DefaultTurretScreenshotAspect;
            float halfWidth = Mathf.Abs(turretPos.x - targetPos.x) * 0.5f + cellMargin;
            float halfHeight = Mathf.Abs(turretPos.z - targetPos.z) * 0.5f + cellMargin;
            float requiredRootSize = Mathf.Max(halfHeight, halfWidth / normalizedAspect) * AutoTestScreenshotProfiles.TurretPrimary.RangeScale;
            rootSize = Mathf.Clamp(requiredRootSize, minRootSize, maxRootSize);
            return true;
        }

        private bool TryResolveMeleeWeaponScreenshotView(MeleeWeaponTestSession session, out Vector3 rootPos, out float rootSize)
        {
            rootPos = default;
            rootSize = AutoTestScreenshotProfiles.MeleePrimary.FixedRootSize ?? 11f;

            if (session?.ShooterPawn == null || session.ShooterPawn.Destroyed)
            {
                return false;
            }

            Vector3 shooterPos = session.ShooterPawn.DrawPos;
            Pawn targetPawn = session.EnemyPawns?.Where(p => p != null && !p.Destroyed && p.Spawned && p.Map == session.TestMap).OrderBy(p => p.Position.DistanceToSquared(session.ShooterPawn.Position)).FirstOrDefault();

            if (targetPawn == null || targetPawn.Destroyed)
            {
                rootPos = shooterPos;
                return true;
            }

            Vector3 targetPos = targetPawn.DrawPos;
            rootPos = (shooterPos + targetPos) * 0.5f;
            return true;
        }

        private static Pawn ResolveTurretScreenshotTarget(TurretTestSession session)
        {
            if (session?.SpawnedTurret == null || session.EnemyPawns == null)
            {
                return null;
            }

            IntVec3 turretCell = session.SpawnedTurret.Position;
            return session.EnemyPawns
                .Where(pawn => pawn != null && !pawn.Destroyed && pawn.Spawned && pawn.Map == session.TestMap)
                .OrderBy(pawn => pawn.Position.DistanceToSquared(turretCell))
                .ThenByDescending(pawn => pawn.Downed ? 0 : 1)
                .FirstOrDefault();
        }

        private static Vector3 ToMapWorldCenter(IntVec3 cell)
        {
            return cell.ToVector3Shifted();
        }

        private static void ConfigureTestPawnCombatStats(Pawn pawn)
        {
            if (pawn?.skills == null)
            {
                return;
            }

            SkillRecord shootingSkill = pawn.skills.GetSkill(SkillDefOf.Shooting);
            if (shootingSkill != null)
            {
                shootingSkill.Level = 20;
            }

            SkillRecord meleeSkill = pawn.skills.GetSkill(SkillDefOf.Melee);
            if (meleeSkill != null)
            {
                meleeSkill.Level = 20;
            }
        }

        private static bool TryEnableSearchAndDestroy(Pawn pawn, out string status)
        {
            status = "Unavailable";
            if (pawn == null)
            {
                status = "PawnMissing";
                return false;
            }

            try
            {
                Assembly searchAndDestroyAssembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(assembly => string.Equals(assembly.GetName().Name, "SearchAndDestroy", StringComparison.OrdinalIgnoreCase));
                if (searchAndDestroyAssembly == null)
                {
                    status = "AssemblyMissing";
                    return false;
                }

                Type baseType = searchAndDestroyAssembly.GetType("SearchAndDestroy.Base");
                Type storageType = searchAndDestroyAssembly.GetType("SearchAndDestroy.Storage.ExtendedDataStorage");
                Type pawnDataType = searchAndDestroyAssembly.GetType("SearchAndDestroy.Storage.ExtendedPawnData");
                if (baseType == null || storageType == null || pawnDataType == null)
                {
                    status = "TypeMissing";
                    return false;
                }

                PropertyInfo instanceProperty = baseType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                PropertyInfo storageProperty = baseType.GetProperty("ExtendedDataStorage", BindingFlags.Public | BindingFlags.Instance);
                MethodInfo loadStorageMethod = baseType.GetMethod("LoadExtendedDataStorage", BindingFlags.Public | BindingFlags.Static);
                MethodInfo getExtendedDataMethod = storageType.GetMethod("GetExtendedDataFor", BindingFlags.Public | BindingFlags.Instance);
                FieldInfo enabledField = pawnDataType.GetField("SD_enabled", BindingFlags.Public | BindingFlags.Instance);
                if (instanceProperty == null || storageProperty == null || getExtendedDataMethod == null || enabledField == null)
                {
                    status = "MemberMissing";
                    return false;
                }

                object baseInstance = instanceProperty.GetValue(null, null);
                object storage = baseInstance == null ? null : storageProperty.GetValue(baseInstance, null);
                if (storage == null && loadStorageMethod != null)
                {
                    loadStorageMethod.Invoke(null, null);
                    baseInstance = instanceProperty.GetValue(null, null);
                    storage = baseInstance == null ? null : storageProperty.GetValue(baseInstance, null);
                }

                if (storage == null)
                {
                    status = "StorageMissing";
                    return false;
                }

                object pawnData = getExtendedDataMethod.Invoke(storage, new object[] { pawn });
                if (pawnData == null)
                {
                    status = "PawnDataMissing";
                    return false;
                }

                enabledField.SetValue(pawnData, true);
                status = "Enabled";
                return true;
            }
            catch (Exception ex)
            {
                status = "Error=" + TrimForNote(ex.Message);
                return false;
            }
        }

        private static LocalTargetInfo ResolveProjectileTargetInfo(Projectile projectile)
        {
            if (projectile == null)
            {
                return LocalTargetInfo.Invalid;
            }

            if (ProjectileIntendedTargetField?.GetValue(projectile) is LocalTargetInfo intendedTarget && intendedTarget.IsValid)
            {
                return intendedTarget;
            }

            if (ProjectileUsedTargetField?.GetValue(projectile) is LocalTargetInfo usedTarget && usedTarget.IsValid)
            {
                return usedTarget;
            }

            return LocalTargetInfo.Invalid;
        }

        private bool TryResolveRangedWeaponScreenshotView(RangedWeaponTestSession session, float aspect, out Vector3 rootPos, out float rootSize)
        {
            if (TryGetStageScreenshotOverride(session, out rootPos, out rootSize))
            {
                return true;
            }

            rootPos = default;
            bool useCameraPlusZoom = IsCameraPlusActive();
            ResolveScreenshotBounds(AutoTestScreenshotProfiles.RangedPrimary, useCameraPlusZoom, out float minRootSize, out float maxRootSize, out float cellMargin);
            if (session?.ShooterPawn == null || session.ShooterPawn.Destroyed)
            {
                rootSize = minRootSize;
                return false;
            }

            Vector3 shooterPos = ToMapWorldCenter(session.ShooterPawn.Position);
            Pawn targetPawn = session.ResolvePrimaryTarget();
            if (targetPawn == null)
            {
                rootPos = shooterPos;
                rootSize = minRootSize;
                return true;
            }

            Vector3 targetPos = ToMapWorldCenter(targetPawn.Position);
            rootPos = (shooterPos + targetPos) * 0.5f;

            float normalizedAspect = aspect > 0.01f ? aspect : DefaultTurretScreenshotAspect;
            float halfWidth = Mathf.Abs(shooterPos.x - targetPos.x) * 0.5f + cellMargin;
            float halfHeight = Mathf.Abs(shooterPos.z - targetPos.z) * 0.5f + cellMargin;
            rootSize = Mathf.Clamp(Mathf.Max(halfHeight, halfWidth / normalizedAspect) * AutoTestScreenshotProfiles.RangedPrimary.RangeScale, minRootSize, maxRootSize);
            return true;
        }

        private bool TryResolveRangedWeaponProjectileFocusView(RangedWeaponTestSession session, Thing projectileThing, float aspect, out Vector3 rootPos, out float rootSize)
        {
            rootPos = default;
            rootSize = AutoTestScreenshotProfiles.RangedPrimary.MinRootSize;
            if (session?.ShooterPawn == null || projectileThing == null)
            {
                return false;
            }

            LocalTargetInfo target = ResolveProjectileTargetInfo(projectileThing as Projectile);
            if (!target.IsValid)
            {
                return false;
            }

            Vector3 shooterPos = ToMapWorldCenter(session.ShooterPawn.Position);
            Vector3 targetPos = target.Thing != null ? target.Thing.DrawPos : ToMapWorldCenter(target.Cell);
            bool useCameraPlusZoom = IsCameraPlusActive();
            ResolveScreenshotBounds(AutoTestScreenshotProfiles.RangedPrimary, useCameraPlusZoom, out float minRootSize, out float maxRootSize, out float cellMargin);
            float normalizedAspect = aspect > 0.01f ? aspect : DefaultTurretScreenshotAspect;

            rootPos = (shooterPos + targetPos) * 0.5f;

            float halfWidth = Mathf.Abs(shooterPos.x - targetPos.x) * 0.5f + cellMargin;
            float halfHeight = Mathf.Abs(shooterPos.z - targetPos.z) * 0.5f + cellMargin;
            rootSize = Mathf.Clamp(Mathf.Max(halfHeight, halfWidth / normalizedAspect) * AutoTestScreenshotProfiles.RangedPrimary.RangeScale, minRootSize, maxRootSize);
            return true;
        }

        private static bool TryGetStageScreenshotOverride(TurretTestSession session, out Vector3 rootPos, out float rootSize)
        {
            rootPos = default;
            rootSize = 0f;

            if (session?.ActiveStageScreenshot?.FocusRootPos == null || session.ActiveStageScreenshot.FocusRootSize == null)
            {
                return false;
            }

            rootPos = session.ActiveStageScreenshot.FocusRootPos.Value;
            rootSize = session.ActiveStageScreenshot.FocusRootSize.Value;
            return true;
        }

        private static bool TryGetStageScreenshotOverride(RangedWeaponTestSession session, out Vector3 rootPos, out float rootSize)
        {
            rootPos = default;
            rootSize = 0f;

            if (session?.ActiveStageScreenshot?.FocusRootPos == null || session.ActiveStageScreenshot.FocusRootSize == null)
            {
                return false;
            }

            rootPos = session.ActiveStageScreenshot.FocusRootPos.Value;
            rootSize = session.ActiveStageScreenshot.FocusRootSize.Value;
            return true;
        }

        private static void ResolveScreenshotBounds(AutoTestScreenshotProfile profile, bool useCameraPlusZoom, out float minRootSize, out float maxRootSize, out float cellMargin)
        {
            profile = profile ?? AutoTestScreenshotProfiles.TurretPrimary;
            minRootSize = useCameraPlusZoom && profile.CameraPlusMinRootSize > 0f
                ? profile.CameraPlusMinRootSize
                : profile.MinRootSize;
            maxRootSize = useCameraPlusZoom && profile.CameraPlusMaxRootSize > 0f
                ? profile.CameraPlusMaxRootSize
                : profile.MaxRootSize;
            cellMargin = useCameraPlusZoom && profile.CameraPlusCellMargin > 0f
                ? profile.CameraPlusCellMargin
                : profile.CellMargin;
        }

        private static float ResolveTurretScreenshotAspect(CameraDriver cameraDriver)
        {
            if (cameraDriver == null)
            {
                return DefaultTurretScreenshotAspect;
            }

            Camera camera = cameraDriver.GetComponent<Camera>();
            return camera != null && camera.aspect > 0.01f
                ? camera.aspect
                : DefaultTurretScreenshotAspect;
        }

        private bool IsCameraPlusActive()
        {
            if (cameraPlusActive.HasValue)
            {
                return cameraPlusActive.Value;
            }

            cameraPlusActive = LoadedModManager.RunningMods.Any(mod =>
                string.Equals(mod.PackageIdPlayerFacing, "brrainz.cameraplus", StringComparison.OrdinalIgnoreCase));
            return cameraPlusActive.Value;
        }

        private string BuildThingSummary(IEnumerable<Thing> things)
        {
            return string.Join(
                ", ",
                things.Where(thing => thing != null)
                    .Select(thing => thing.def.defName + "x" + thing.stackCount)
                    .ToArray());
        }

        private static string SafeDefName(Def def)
        {
            return def?.defName ?? "null";
        }

    }
}
