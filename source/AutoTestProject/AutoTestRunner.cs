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

    internal sealed partial class AutoTestRunner
    {
        // --- 常量和字段 ---
        private const int MaxConcurrentTurretTests = 3;
        private const int PreOpenSettleRepaintFrames = 10;
        private const int OpenStableFrameRequirement = 3;
        private const int OpenTimeoutRepaintFrames = 180;
        private const int WaitBeforeCloseRepaintFrames = 12;
        private const int CloseTimeoutRepaintFrames = 180;
        private const float TurretScreenshotRangeScale = 1.2f;
        private const float DefaultTurretScreenshotAspect = 16f / 9f;
        private const float TurretScreenshotMinRootSize = 6f;
        private const float TurretScreenshotMaxRootSize = 18f;
        private const float TurretScreenshotCellMargin = 3f;
        private const float TurretScreenshotCameraPlusMinRootSize = 3.25f;
        private const float TurretScreenshotCameraPlusMaxRootSize = 14f;
        private const float TurretScreenshotCameraPlusCellMargin = 1.5f;
        private static readonly FieldInfo ProjectileUsedTargetField = typeof(Projectile).GetField("usedTarget", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo ProjectileIntendedTargetField = typeof(Projectile).GetField("intendedTarget", BindingFlags.Instance | BindingFlags.NonPublic);

        private readonly LogSyncSettings settings;
        private readonly Map map; // 主地图，用于UI测试等
        private readonly Queue<AutoTestWorkItem> pendingItems = new Queue<AutoTestWorkItem>();
        private readonly List<AutoTestResult> results = new List<AutoTestResult>();
        private readonly Dictionary<ThingDef, Thing> placedRecipeWorktables = new Dictionary<ThingDef, Thing>();

        // 新增炮塔测试相关字段
        private readonly List<TurretTestSession> activeTurretSessions = new List<TurretTestSession>();
        private readonly Queue<ThingDef> pendingTurretDefs = new Queue<ThingDef>();
        private readonly List<RangedWeaponTestSession> activeRangedWeaponSessions = new List<RangedWeaponTestSession>();
        private readonly Queue<ThingDef> pendingRangedWeaponDefs = new Queue<ThingDef>();
        private readonly List<MeleeWeaponTestSession> activeMeleeWeaponSessions = new List<MeleeWeaponTestSession>();
        private readonly Queue<ThingDef> pendingMeleeWeaponDefs = new Queue<ThingDef>();
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
        private AutoTestScreenshotService screenshotService;
        private bool? cameraPlusActive;

        public AutoTestRunner(LogSyncSettings settings, Map map)
        {
            this.settings = settings;
            this.map = map;
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
                    return true;
                }
                return false;
            });

            activeRangedWeaponSessions.RemoveAll(session => {
                if (session.Phase == RangedWeaponTestPhase.Done) {
                    results.Add(session.Result);
                    return true;
                }
                return false;
            });

            activeMeleeWeaponSessions.RemoveAll(session => {
                if (session.Phase == MeleeWeaponTestPhase.Done) {
                    results.Add(session.Result);
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

        private AutoTestResult CreateResult(string category, string defType, string defName, string label)
        {
            return new AutoTestResult
            {
                PackageId = targetPackageId,
                Category = category,
                DefType = defType,
                DefName = defName ?? string.Empty,
                Label = label ?? string.Empty,
                Status = "pending",
                Stage = "collect",
                ErrorMessage = string.Empty,
                ScreenshotPath = string.Empty,
                Notes = string.Empty
            };
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

            AutoTestReportWriter.Write(runArtifacts, targetPackageId ?? "unknown", results, targetMod?.Name ?? "Unknown");
            Log.Message(string.Format(
                "[LogSyncAutoTest] COMPLETED packageId={0} total={1} passed={2} failed={3}",
                targetPackageId ?? "unknown",
                results.Count,
                results.Count(result => result.Status == "passed"),
                results.Count(result => result.Status != "passed" && result.Status != "skipped")));
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

            return screenshotService.TryCaptureMap(
                session.Result,
                allowCapture,
                force,
                screenshotService.ResolveTurretResultIndex(results, activeTurretSessions.IndexOf(session)),
                stageSuffix,
                () => FocusTurretScreenshotMap(session));
        }

        private bool TryCaptureTurretFailureScreenshot(TurretTestSession session)
        {
            if (session == null || screenshotService == null || session.TestMap == null || !Find.Maps.Contains(session.TestMap))
            {
                return false;
            }

            return screenshotService.TryCaptureMap(
                session.Result,
                allowCapture: true,
                force: true,
                resultIndex: screenshotService.ResolveTurretResultIndex(results, activeTurretSessions.IndexOf(session)),
                stageSuffix: session.Result?.Status ?? "failed",
                prepare: () => FocusPocketMapOverview(session.TestMap),
                relativeSubdirectory: "fail");
        }

        private bool FocusTurretScreenshotMap(TurretTestSession session)
        {
            if (session?.TestMap == null || !Find.Maps.Contains(session.TestMap))
            {
                return false;
            }

            try
            {
                if (Current.Game != null)
                {
                    Current.Game.CurrentMap = session.TestMap;
                }

                CameraDriver cameraDriver = Find.CameraDriver;
                if (cameraDriver == null)
                {
                    return false;
                }

                float aspect = ResolveTurretScreenshotAspect(cameraDriver);
                if (TryResolveTurretScreenshotView(session, aspect, out Vector3 rootPos, out float rootSize))
                {
                    IntVec3 focusCell = new IntVec3(
                        Mathf.Clamp(Mathf.RoundToInt(rootPos.x), 0, session.TestMap.Size.x - 1),
                        0,
                        Mathf.Clamp(Mathf.RoundToInt(rootPos.z), 0, session.TestMap.Size.z - 1));
                    cameraDriver.JumpToCurrentMapLoc(focusCell);
                    cameraDriver.SetRootPosAndSize(rootPos, rootSize);
                }
                else
                {
                    IntVec3 focusCell = session.SpawnedTurret?.Position ?? session.TestMap.Center;
                    cameraDriver.JumpToCurrentMapLoc(focusCell);
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

        private string BuildThingSummary(IEnumerable<Thing> things)
        {
            return string.Join(
                ", ",
                things.Where(thing => thing != null)
                    .Select(thing => thing.def.defName + "x" + thing.stackCount)
                    .ToArray());
        }

        private bool FocusPocketMapOverview(Map targetMap)
        {
            if (targetMap == null || !Find.Maps.Contains(targetMap))
            {
                return false;
            }

            try
            {
                if (Current.Game != null)
                {
                    Current.Game.CurrentMap = targetMap;
                }

                CameraDriver cameraDriver = Find.CameraDriver;
                if (cameraDriver == null)
                {
                    return false;
                }

                float aspect = ResolveTurretScreenshotAspect(cameraDriver);
                Vector3 rootPos = ToMapWorldCenter(targetMap.Center);
                float normalizedAspect = aspect > 0.01f ? aspect : DefaultTurretScreenshotAspect;
                float rootSize = Mathf.Max(targetMap.Size.z * 0.58f, (targetMap.Size.x / normalizedAspect) * 0.58f);
                rootSize = Mathf.Max(rootSize, TurretScreenshotMaxRootSize);
                cameraDriver.JumpToCurrentMapLoc(targetMap.Center);
                cameraDriver.SetRootPosAndSize(rootPos, rootSize);
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
            float minRootSize = useCameraPlusZoom ? TurretScreenshotCameraPlusMinRootSize : TurretScreenshotMinRootSize;
            float maxRootSize = useCameraPlusZoom ? TurretScreenshotCameraPlusMaxRootSize : TurretScreenshotMaxRootSize;
            float cellMargin = useCameraPlusZoom ? TurretScreenshotCameraPlusCellMargin : TurretScreenshotCellMargin;
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
            float requiredRootSize = Mathf.Max(halfHeight, halfWidth / normalizedAspect) * TurretScreenshotRangeScale;
            rootSize = Mathf.Clamp(requiredRootSize, minRootSize, maxRootSize);
            return true;
        }

        private bool TryResolveProjectileFocusView(TurretTestSession session, Thing projectileThing, float aspect, out Vector3 rootPos, out float rootSize)
        {
            rootPos = default;
            rootSize = TurretScreenshotMinRootSize;
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
            float minRootSize = useCameraPlusZoom ? TurretScreenshotCameraPlusMinRootSize : TurretScreenshotMinRootSize;
            float maxRootSize = useCameraPlusZoom ? TurretScreenshotCameraPlusMaxRootSize : TurretScreenshotMaxRootSize;
            float cellMargin = useCameraPlusZoom ? TurretScreenshotCameraPlusCellMargin : TurretScreenshotCellMargin;

            rootPos = (turretPos + targetPos) * 0.5f;
            float normalizedAspect = aspect > 0.01f ? aspect : DefaultTurretScreenshotAspect;
            float halfWidth = Mathf.Abs(turretPos.x - targetPos.x) * 0.5f + cellMargin;
            float halfHeight = Mathf.Abs(turretPos.z - targetPos.z) * 0.5f + cellMargin;
            float requiredRootSize = Mathf.Max(halfHeight, halfWidth / normalizedAspect) * TurretScreenshotRangeScale;
            rootSize = Mathf.Clamp(requiredRootSize, minRootSize, maxRootSize);
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
            return new Vector3(cell.x + 0.5f, 0f, cell.z + 0.5f);
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

        private static string SafeDefName(Def def)
        {
            return def?.defName ?? "null";
        }

    }
}
