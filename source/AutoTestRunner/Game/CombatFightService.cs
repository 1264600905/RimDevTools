using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;
using HarmonyLib;
using LogSyncMod.AutoTest.Scenarios;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace LogSyncMod.AutoTest
{
    internal static class CombatFightService
    {
        private static string activeRunId;
        private static Action<GroupCombatRoundResult> completed;
        private static Action stateChanged;
        private static ArenaEndRule arenaEndRule;
        private static double arenaIntroSeconds, arenaSettlementSeconds, arenaAdvanceStartSeconds;
        internal static bool IsArena => arenaEndRule != null;
        private static string ArtifactPrefix => IsArena ? "arena" : "groupcombat";
        internal static void BeginArenaFight(string runId, string artifacts, GroupCombatRoundInput fight, ArenaTournamentInput tournament, Action<GroupCombatRoundResult> callback)
            => BeginFight(runId, artifacts, fight, callback, tournament, null, 0);
        internal static void BeginGroupFight(string runId, string artifacts, GroupCombatRoundInput fight, int index, Action<GroupCombatRoundResult> callback, Action changed)
            => BeginFight(runId, artifacts, fight, callback, null, changed, index);
        private static void BeginFight(string runId, string artifacts, GroupCombatRoundInput fight, Action<GroupCombatRoundResult> callback, ArenaTournamentInput tournament, Action changed, int captureIndex)
        {
            if (Busy || GroupCombatDamageCapture.ProbeActive) throw new InvalidOperationException("Another fight/probe owns the native context");
            completed = callback ?? throw new ArgumentNullException(nameof(callback)); stateChanged = changed;
            arenaEndRule = tournament == null ? null : new ArenaEndRule(tournament.ConfirmationTicks);
            arenaIntroSeconds = tournament?.BracketSeconds ?? GroupCombatPresentation.IntroDuration;
            arenaAdvanceStartSeconds = tournament?.AdvanceStartSeconds ?? 0;
            initialCaptureReady = false;
            arenaSettlementSeconds = tournament?.SettlementSeconds ?? GroupCombatPresentation.SettlementDuration;
            outputPath = artifacts; activeRunId = runId; cancel = false;
            if (captureIndex > 0) roundIndex = captureIndex - 1;
            originalSpeed = Find.TickManager.CurTimeSpeed;
            originalScreenWidth = Screen.width; originalScreenHeight = Screen.height; originalScreenMode = Screen.fullScreenMode;
            Screen.fullScreenMode = FullScreenMode.FullScreenWindow;
            try { StartRound(fight); }
            catch (Exception ex) { if (current != null) Freeze("error", "exception", ex.ToString()); else { completed = null; stateChanged = null; arenaEndRule = null; throw; } }
        }
        private static bool EvaluateEnd() => IsArena ? arenaEndRule.Evaluate(current, durationTicks) : endRule.Evaluate(current, durationTicks);
        private static GroupCombatRoundInput input;
        private static XElement roundConfig;
        private static GroupCombatRoundResult current;
        private static GroupCombatLedger ledger;
        private static GroupCombatEndRule endRule;
        private static AutoTestMainMap arena;
        private static ScenarioGameBackend backend;
        private static readonly Dictionary<string, Faction> factions = new Dictionary<string, Faction>();
        private static readonly Dictionary<string, string> teamAi = new Dictionary<string, string>();
        private static readonly Dictionary<string, Lord> teamLords = new Dictionary<string, Lord>();
        private static List<Faction> preexistingFactions;
        private static string preexistingRelations;
        private static readonly Dictionary<string, Pawn> pawns = new Dictionary<string, Pawn>();
        private static readonly Dictionary<Pawn, string> rosterIds = new Dictionary<Pawn, string>();
        private static readonly HashSet<Pawn> exited = new HashSet<Pawn>();
        private static readonly List<Pawn> lifecyclePawns = new List<Pawn>();
        private static TimeSpeed originalSpeed;
        private static int originalScreenWidth, originalScreenHeight, requestedWidth, requestedHeight;
        private static FullScreenMode originalScreenMode;
        private static int startTick, durationTicks, preparedFrame, roundIndex;
        private static float roundStartedRealtime, phaseStartedRealtime, lastFrameRealtime, pausedRealtime;
        private static bool overlayEnabled = true;
        private static string aiPolicy;
        private static string phase;
        private static string pendingCapture, outputPath;
        private static bool captureRequested, polling, cancel, initialCaptureReady;
        private static bool verifyTickFailure;
        private static float introStartedRealtime;
        private static float battleStartedRealtime;
        private static float settlementStartedRealtime;
        private static int settlementStartTick;
        private static string lastCountdownLabel;
        private static readonly Dictionary<Pawn, Rot4> introFacing = new Dictionary<Pawn, Rot4>();
        internal static bool Busy => completed != null;
        internal static bool Measuring => phase == "running";
        internal static Map Map => arena?.Map;
        internal static GroupCombatLedger Ledger => Measuring ? ledger : null;
        internal static IReadOnlyDictionary<string, Pawn> Pawns => pawns;
        internal static IEnumerable<Pawn> CameraPawns => pawns.Where(p => p.Value.Spawned && p.Value.Map == Map && !p.Value.Dead && !p.Value.Downed && ledger?.IsCameraTarget(p.Key) == true).Select(p => p.Value);
        internal static GroupCombatRoundResult Display => current;
        internal static bool IsCombatParticipant(Pawn pawn) => Measuring && pawn?.Spawned == true && pawn.Map == Map && RosterId(pawn) != null;
        internal static void TraceAi(string stage, string details) => Trace(stage, details);
        internal static string Phase => phase;
        internal static bool OverlayEnabled => overlayEnabled;
        internal static bool ShowingIntro => phase is "prepared" or "countdown" or "roster";
        internal static bool ShowingSettlement => phase == "finishing" && current?.ExecutionStatus == "completed";
        internal static string CountdownLabel => IsArena
            ? ShowingIntro || Measuring && Time.realtimeSinceStartup - battleStartedRealtime < .8f
                ? ArenaPresentation.CountdownLabel(phase == "prepared" ? 0 : Time.realtimeSinceStartup - introStartedRealtime, arenaIntroSeconds) : ""
            : ShowingIntro ? GroupCombatPresentation.CountdownLabel(phase == "prepared" ? 0 : Time.realtimeSinceStartup - introStartedRealtime) : "";
        internal static object Status => new { busy = Busy, phase, runId = activeRunId, caseId = input?.CaseId, mapId = Map?.uniqueID, elapsedTicks = current?.ElapsedTicks, totalEvents = current?.TotalEvents, speed = Find.TickManager?.CurTimeSpeed.ToString(), paused = Find.TickManager?.Paused, pausedRealtime, screen = new { width = Screen.width, height = Screen.height, windowWidth = Screen.windowWidth, windowHeight = Screen.windowHeight, fullScreen = Screen.fullScreen, mode = Screen.fullScreenMode.ToString() }, camera = GroupCombatCamera.Status, overlayInput = GroupCombatOverlay.InputVerification, artifacts = new { autoTestOutputPath = outputPath }, teamAi, actors = pawns.Select(p => new { id = p.Key, x = p.Value.Position.x, z = p.Value.Position.z, dead = p.Value.Dead, downed = p.Value.Downed, job = p.Value.CurJob?.def.defName, duty = p.Value.mindState?.duty?.def.defName }), rounds = GroupCombatService.Rounds.Select(r => new { r.CaseId, r.ExecutionStatus, r.CleanupComplete, r.EndReason, r.Error }) };
        internal static void Cancel() { if (Busy) cancel = true; }
        internal static void SetVerificationSpeed(string speed)
        {
            if (!Measuring || global::LogSyncMod.LogSyncMod.settings is not { testSessionActive: true, testSessionKind: "groupcombat" })
                throw new InvalidOperationException("Speed verification requires a running CLI groupcombat round");
            Find.TickManager.CurTimeSpeed = speed switch { "paused" => TimeSpeed.Paused, "normal" => TimeSpeed.Normal, "fast" => TimeSpeed.Fast, "superfast" => TimeSpeed.Superfast, _ => throw new ArgumentException("Unknown verification speed") };
        }
        internal static void VerifyExecutionFailure()
        {
            if (!Measuring || global::LogSyncMod.LogSyncMod.settings is not { testSessionActive: true, testSessionKind: "groupcombat" or "arena" })
                throw new InvalidOperationException("Execution-failure verification requires a running CLI combat fight");
            verifyTickFailure = true;
        }
        internal static void NotifyExit(Pawn pawn) { if (rosterIds.ContainsKey(pawn)) exited.Add(pawn); }
        internal static void NotifyDowned(Pawn pawn) { if (rosterIds.TryGetValue(pawn, out var id)) ledger?.MarkDowned(id); }
        internal static void NotifyDead(Pawn pawn) { if (rosterIds.TryGetValue(pawn, out var id)) ledger?.MarkDead(id); }
        internal static string RosterId(Thing thing) => thing is Pawn pawn && rosterIds.TryGetValue(pawn, out var id) ? id : null;
        internal static string Team(Faction faction) => factions.FirstOrDefault(f => f.Value == faction).Key;

        internal static void NotifySpawn(Pawn pawn)
        {
            if (!Measuring || pawn == null || pawn.MapHeld != Map) return;
            if (rosterIds.ContainsKey(pawn)) { exited.Remove(pawn); return; }
            var team = Team(pawn.Faction) ?? Team(pawn.GetLord()?.faction) ?? Team(GroupCombatLifecycleAdapters.OwnerFaction(pawn));
            if (team == null) return;
            var ticks = Math.Max(0, Find.TickManager.TicksGame - startTick);
            var id = "joined/" + pawn.GetUniqueLoadID();
            var unit = new GroupCombatUnit { Id = id, NativeId = pawn.GetUniqueLoadID(), Team = team, Group = "native/" + pawn.kindDef.defName, GroupName = "战斗新增", Kind = pawn.kindDef.defName, KindLabel = pawn.kindDef.LabelCap, Name = pawn.LabelShort, X = pawn.PositionHeld.x, Z = pawn.PositionHeld.z, State = pawn.Dead ? "dead" : pawn.Downed ? "downed" : "standing", WasDowned = pawn.Downed, WasDead = pawn.Dead };
            ledger.AddUnit(unit, ticks);
            pawns.Add(id, pawn); rosterIds.Add(pawn, id);
            SyncUnitNames();
            pawn.relations?.ClearAllRelations();
            Trace("group.join", "id=" + id + "; team=" + team + "; kind=" + unit.Kind + "; tick=" + ticks);
        }

        private static void SyncUnitNames()
        {
            foreach (var pair in pawns)
            {
                var name = ledger.UnitName(pair.Key);
                var pawn = pair.Value;
                if (pawn.LabelShort == name) continue;
                pawn.Name = pawn.Name is NameTriple full ? new NameTriple(full.First, name, full.Last) : new NameSingle(name);
            }
        }

        private static void DiscoverParticipants()
        {
            ThingOwnerUtility.GetAllThingsRecursively(Map, ThingRequest.ForGroup(ThingRequestGroup.Pawn), lifecyclePawns);
            foreach (var pawn in lifecyclePawns.ToList()) NotifySpawn(pawn);
            // Native split workers can add a Lord after SpawnSetup; wait until the tick
            // ends before filling in missing membership, avoiding double AddPawn calls.
            foreach (var pair in pawns.ToList())
            {
                var pawn = pair.Value;
                if (!pawn.Dead && pawn.MapHeld == Map && pawn.GetLord() == null && teamLords.TryGetValue(Team(pawn.Faction) ?? "", out var lord)) lord.AddPawn(pawn);
            }
        }

        internal static void Frame()
        {
            if (polling) return;
            polling = true;
            try
            {
                if (!Busy) return;
                GroupCombatOverlay.Frame();
                if (cancel && phase != "finishing") Freeze("cancelled", "cancelled", null);
                var now = Time.realtimeSinceStartup;
                if (Measuring && Find.TickManager.Paused) pausedRealtime += Math.Max(0, now - lastFrameRealtime);
                lastFrameRealtime = now;
                if (phase != "finishing" && now - roundStartedRealtime - pausedRealtime >= ScenarioLoader.D(roundConfig, "timeoutRealSeconds", 600)) Freeze("timeout", "realtimeLimit", "Group combat realtime deadline exceeded");
                if (ShowingIntro)
                {
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    FaceOpponents();
                }
                if (phase == "prepared" && CaptureFinished())
                {
                    phase = "countdown"; introStartedRealtime = now; lastCountdownLabel = null;
                    if (IsArena) CombatStartupProbe.CountdownStarted(arenaIntroSeconds, arenaAdvanceStartSeconds);
                    Trace("group.intro_facing", "total=" + introFacing.Count + "; correct=" + introFacing.Count(p => p.Key.Rotation == p.Value));
                }
                if (phase == "countdown" || IsArena && phase == "roster")
                {
                    var label = CountdownLabel;
                    if (label != lastCountdownLabel && label.Length > 0)
                    {
                        lastCountdownLabel = label;
                        Trace("group.countdown", "label=" + label + "; elapsedTicks=" + current.ElapsedTicks + "; events=" + current.TotalEvents + "; paused=" + Find.TickManager.Paused);
                    }
                }
                if (phase == "countdown" && now - introStartedRealtime >= (IsArena
                    ? ArenaPresentation.CapturePreparationElapsed(arenaIntroSeconds, arenaAdvanceStartSeconds)
                    : GroupCombatPresentation.IntroDuration))
                {
                    if (IsArena) { phase = "roster"; RequestCapture("initial"); }
                    else StartMeasurement();
                }
                if (phase == "roster")
                {
                    if (!initialCaptureReady) initialCaptureReady = CaptureFinished();
                    if (phase == "roster" && initialCaptureReady && now - introStartedRealtime >= arenaIntroSeconds) StartMeasurement();
                }
                else if (phase == "finishing")
                {
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    if (CaptureFinished() && (!ShowingSettlement || now - settlementStartedRealtime >= (IsArena ? arenaSettlementSeconds : GroupCombatPresentation.SettlementDuration))) FinishRound();
                }
            }
            catch (Exception ex)
            {
                Log.Error("[LogSyncGroupCombat] " + ex);
                if (input != null && phase != "finishing") Freeze("error", "exception", ex.ToString());
                else if (current != null) { current.ExecutionStatus = "error"; current.Error = ex.ToString(); FinishRound(); }
            }
            finally { polling = false; }
        }

        private static void StartMeasurement()
        {
            var measurementBegan = CombatStartupProbe.Now;
            foreach (var pawn in introFacing.Keys) pawn.jobs?.StopAll();
            introFacing.Clear();
            phase = "running"; startTick = Find.TickManager.TicksGame; current.ExecutionStatus = "running";
            battleStartedRealtime = Time.realtimeSinceStartup;
            var settingsNode = roundConfig.Element("GroupCombat");
            Find.TickManager.CurTimeSpeed = ScenarioLoader.A(settingsNode, "timeSpeed", "normal") switch { "fast" => TimeSpeed.Fast, "superfast" => TimeSpeed.Superfast, _ => TimeSpeed.Normal };
            CombatStartupProbe.SpeedSwitch(ScenarioLoader.A(settingsNode, "timeSpeed", "normal"), pawns.Values);
            Trace("group.start", "configuredSpeed=" + ScenarioLoader.A(settingsNode, "timeSpeed", "normal") + "; actualSpeed=" + Find.TickManager.CurTimeSpeed + "; pauseAllowed=true");
            Log.Message("[LogSyncGroupCombat] START caseId=" + input.CaseId + " map=" + Map.uniqueID + " participants=" + pawns.Count);
            CombatStartupProbe.MeasurementStarted(measurementBegan);
        }

        private static void StartRound(GroupCombatRoundInput next)
        {
            input = next; roundIndex++; phase = "preparing"; roundStartedRealtime = Time.realtimeSinceStartup;
            lastFrameRealtime = roundStartedRealtime; pausedRealtime = 0;
            current = new GroupCombatRoundResult { CaseId = input.CaseId, TestDefName = input.TestDefName, Name = input.Name, Seed = input.Seed, Swapped = input.Swapped, Source = input.Source, InputHash = input.InputHash, ResolvedRosters = input.ResolvedRosters };
            current.StartupTiming = CombatStartupProbe.Reset();
            GroupCombatAiDiagnostics.Reset();
            preexistingFactions = Find.FactionManager.AllFactions.ToList();
            preexistingRelations = RelationFingerprint();
            Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
            var c = roundConfig = XElement.Parse(input.Xml); var scene = c.Element("Scene"); var settings = c.Element("GroupCombat");
            overlayEnabled = ScenarioLoader.A(settings.Element("Overlay"), "enabled", "true") == "true";
            aiPolicy = ScenarioLoader.A(settings, "aiPolicy", "assaultColony");
            durationTicks = (int)Math.Ceiling(ScenarioLoader.D(settings, "durationSeconds", 180) * 60);
            var output = c.Element("Output"); requestedWidth = ScenarioLoader.I(output, "width", 0); requestedHeight = ScenarioLoader.I(output, "height", 0);
            arena = new AutoTestMainMap(Find.CurrentMap, input.Seed, new IntVec3(ScenarioLoader.I(scene, "sizeX", 50), 1, ScenarioLoader.I(scene, "sizeZ", 50)), ScenarioLoader.A(scene, "generatorDef", "LogSync_AutoTestStudioMap"));
            current.MapId = Map.uniqueID; current.IsPocketMap = Map.Parent is PocketMapParent; current.GeneratorDef = Map.generatorDef.defName;
            foreach (var team in c.Element("Teams").Elements("Team"))
            {
                var teamId = ScenarioLoader.A(team, "id");
                var teamRosters = input.ResolvedRosters.Where(r => r.Team == teamId || IsArena && r == input.ResolvedRosters[teamId == "A" ? 0 : 1]).ToList();
                var sourceFactions = teamRosters.Where(r => r.FactionDef != null).Select(r => r.FactionDef).Distinct().ToList();
                if (sourceFactions.Count > 1) throw new ScenarioConfigurationException("One team cannot mix squad generation factions");
                var faction = SquadGeneration.CreateFaction(sourceFactions.FirstOrDefault() ?? "LogSync_GroupCombatFaction", ScenarioLoader.A(team, "name", teamId));
                // Use the current session's already initialized ideology as a read-only basis;
                // no new ideology or leader is added to the player's world for a benchmark.
                if (Faction.OfPlayer?.ideos?.PrimaryIdeo != null) faction.ideos.SetPrimary(Faction.OfPlayer.ideos.PrimaryIdeo);
                // Only relations involving an owned faction are created; preexisting pairs stay untouched.
                foreach (var other in Find.FactionManager.AllFactions.ToList())
                {
                    var hostile = factions.Values.Contains(other) || factions.Count == 0 && other == Faction.OfPlayer;
                    faction.SetRelation(new FactionRelation { other = other, kind = hostile ? FactionRelationKind.Hostile : FactionRelationKind.Neutral, baseGoodwill = hostile ? -100 : 0 });
                    other.RelationWith(faction).baseGoodwill = hostile ? -100 : 0;
                }
                factions.Add(ScenarioLoader.A(team, "id"), faction); Find.FactionManager.Add(faction);
            }
            foreach (var a in factions.Values) foreach (var b in factions.Values.Where(f => f != a)) if (!a.HostileTo(b) || !b.HostileTo(a)) throw new InvalidOperationException("Temporary factions did not become mutually hostile");
            if (!factions.Values.First().HostileTo(Faction.OfPlayer) || !Faction.OfPlayer.HostileTo(factions.Values.First())) throw new InvalidOperationException("The left team must be hostile to the player");
            Trace("group.player_relation", "left=" + factions.Keys.First() + "; playerRelation=Hostile; bothDirections=true");
            backend = new ScenarioGameBackend(arena.Original, null, null, Trace);
            backend.PrepareGroupCombat(input, Map, factions);
            // Clear after the entire roster exists, including links added by mod generation.
            var generated = backend.GroupActors.Values.SelectMany(g => g).OfType<Pawn>().Distinct().ToList();
            var relationCount = generated.Sum(p => p.relations?.DirectRelations.Count ?? 0);
            foreach (var pawn in generated) pawn.relations?.ClearAllRelations();
            Trace("group.social_reset", "pawns=" + generated.Count + "; directBefore=" + relationCount + "; directAfter=" + generated.Sum(p => p.relations?.DirectRelations.Count ?? 0) + "; thinkTreesReplaced=false");
            var roster = new List<GroupCombatUnit>();
            foreach (var actor in c.Element("Actors").Elements().Where(a => a.Name == "Pawn" || a.Name == "PawnGroup"))
            {
                var actorId = ScenarioLoader.A(actor, "id"); var members = backend.GroupActors[actorId].Cast<Pawn>().ToList();
                for (var i = 0; i < members.Count; i++)
                {
                    var pawn = members[i]; var id = actorId + "[" + i + "]";
                    pawns.Add(id, pawn); rosterIds.Add(pawn, id);
                    var team = ScenarioLoader.A(actor, "team");
                    var squad = input.ResolvedRosters.FirstOrDefault(r => r.Id == actorId)
                        ?? (IsArena ? input.ResolvedRosters[team == "A" ? 0 : 1] : null);
                    var group = GroupCombatPlan.DisplayGroupKey(actor);
                    if (squad?.Members.Count > 0) group += ":" + pawn.kindDef.defName + ":" + pawn.genes?.Xenotype?.defName;
                    roster.Add(new GroupCombatUnit { Id = id, NativeId = pawn.GetUniqueLoadID(), Team = team, Group = group, GroupName = actorId, Kind = pawn.kindDef.defName, KindLabel = pawn.kindDef.LabelCap, Name = pawn.LabelShort, X = pawn.Position.x, Z = pawn.Position.z });
                }
            }
            ledger = new GroupCombatLedger(roster, c.Element("Teams").Elements("Team").Select(t => new KeyValuePair<string, string>(ScenarioLoader.A(t, "id"), ScenarioLoader.A(t, "name", ScenarioLoader.A(t, "id")))), ScenarioLoader.I(settings.Element("Metrics"), "detailLimit", 10000));
            SyncUnitNames();
            foreach (var unit in roster) unit.InitialSnapshot = TemporaryPawnData.Serialize(TemporaryPawnCapture.Capture(pawns[unit.Id]));
            endRule = new GroupCombatEndRule(ScenarioLoader.I(settings.Element("End"), "confirmationTicks", 300));
            foreach (var node in c.Element("Teams").Elements("Team"))
            {
                var id = ScenarioLoader.A(node, "id"); var faction = factions[id]; var members = pawns.Values.Where(p => p.Faction == faction).ToList();
                var ai = ScenarioLoader.A(node, "ai", aiPolicy); teamAi.Add(id, ai);
                LordJob job = ai switch {
                    "battle" or "seekAndAttack" or "assaultColony" or "native" => new LordJob_GroupCombatAssault(faction),
                    "assaultThings" => new LordJob_AssaultThings(faction, pawns.Values.Where(p => p.Faction != faction).Cast<Thing>().ToList(), damageFraction: 1f),
                    "defendPoint" => new LordJob_DefendPoint(new IntVec3(ScenarioLoader.I(node, "defendX", (int)members.Average(p => p.Position.x)), 0, ScenarioLoader.I(node, "defendZ", (int)members.Average(p => p.Position.z))), defendRadius: (float)ScenarioLoader.D(node, "defendRadius", 28), addFleeToil: false),
                    _ => null
                };
                if (job != null) teamLords.Add(id, LordMaker.MakeNewLord(faction, job, Map, members));
                Trace("group.ai", "team=" + id + "; ai=" + ai + "; controller=native; nativeLordJob=" + (job is LordJob_AssaultColony ? nameof(LordJob_AssaultColony) : job?.GetType().Name)
                    + "; duty=" + members.FirstOrDefault()?.mindState.duty?.def.defName + "; allowSelfTend=" + members.FirstOrDefault()?.GetLord()?.CurLordToil?.AllowSelfTend
                    + "; addFleeToil=" + job?.AddFleeToil + "; exitToils=" + members.FirstOrDefault()?.GetLord()?.Graph.lordToils.Count(t => t is LordToil_ExitMap || t is LordToil_PanicFlee)
                    + "; customJobScheduler=false; simultaneousStart=true");
            }
            current.Environment = "tile=" + Map.Tile + "; weather=" + Map.weatherManager.curWeather.defName + "; temperature=" + Map.mapTemperature.OutdoorTemp + "; sunGlow=" + GenCelestial.CurCelestialSunGlow(Map) + "; ai=" + string.Join(",", teamAi.Select(p => p.Key + "=" + p.Value));
            foreach (var pawn in pawns.Values)
            {
                var opponents = pawns.Values.Where(p => p.Faction != pawn.Faction).ToList();
                var enemyCenter = new Vector3((float)opponents.Average(p => p.Position.x), 0, (float)opponents.Average(p => p.Position.z));
                var facing = Rot4.FromAngleFlat((enemyCenter - pawn.Position.ToVector3()).AngleFlat());
                introFacing.Add(pawn, facing);
                pawn.jobs.StopAll(); pawn.pather?.StopDead();
                // The three-view settle logic uses the same display job and facing override.
                var displayJob = JobMaker.MakeJob(JobDefOf.Wait_Combat, pawn.Position);
                displayJob.expiryInterval = int.MaxValue; displayJob.canUseRangedWeapon = false; displayJob.overrideFacing = facing;
                pawn.jobs.StartJob(displayJob, JobCondition.InterruptForced);
                // Gear changes are normally rebuilt on a later tick. The paused intro
                // needs the configured outfit immediately, including its cached portrait.
                AutoTestThreeViewService.RefreshPawnGraphics(pawn);
            }
            FaceOpponents();
            GroupCombatCamera.Begin();
            phase = "prepared"; Refresh(0); RequestCapture(IsArena ? "bracket" : "initial"); Write();
        }

        private static void FaceOpponents()
        {
            foreach (var pair in introFacing)
            {
                if (!pair.Key.Spawned || pair.Key.Dead) continue;
                pair.Key.pather?.StopDead();
                pair.Key.rotationTracker?.FaceCell(pair.Key.Position + pair.Value.FacingCell);
                pair.Key.Rotation = pair.Value;
            }
        }

        internal static void Tick()
        {
            if (!Measuring) return;
            try
            {
                if (verifyTickFailure) { verifyTickFailure = false; throw new InvalidOperationException("Controlled native groupcombat tick failure for termination-path verification"); }
                var ticks = Math.Max(0, Find.TickManager.TicksGame - startTick);
                DiscoverParticipants();
                // This callback runs after the entire native simulation tick, so simultaneous
                // knockouts in that tick are evaluated together.
                Refresh(ticks);
                if (EvaluateEnd()) { Freeze("completed", current.EndReason, null); return; }
                if (ticks % 120 == 0) GroupCombatAiDiagnostics.Observe();
            }
            catch (Exception ex) { Freeze("error", "exception", ex.ToString()); }
        }

        private static void Refresh(int ticks, bool includeEventDetails = false)
        {
            if (ledger == null) return;
            foreach (var pair in pawns)
            {
                var p = pair.Value;
                ledger.SetState(pair.Key, p.Dead ? "dead" : exited.Contains(p) ? "escaped" : p.Destroyed || p.MapHeld != Map ? "removed" : p.Downed ? "downed" : "standing", ticks);
                var nativeRevival = p.Dead && !p.Destroyed && p.Corpse?.MapHeld == Map && p.health.hediffSet.hediffs.OfType<Hediff_DeathRefusal>().Any(h => h.InProgress || h.AIEnabled && !h.PlayerControlled && h.UsesLeft > 0);
                ledger.SetPendingRevival(pair.Key, nativeRevival || GroupCombatLifecycleAdapters.PendingRevival(p));
            }
            var sample = ledger.Snapshot(ticks, includeEventDetails);
            sample.CaseId = current.CaseId; sample.TestDefName = current.TestDefName; sample.Name = current.Name; sample.Seed = current.Seed; sample.Swapped = current.Swapped; sample.Source = current.Source; sample.InputHash = current.InputHash; sample.MapId = current.MapId; sample.IsPocketMap = current.IsPocketMap; sample.GeneratorDef = current.GeneratorDef; sample.Environment = current.Environment;
            sample.ResolvedRosters = current.ResolvedRosters;
            sample.StartupTiming = current.StartupTiming;
            sample.ScreenshotWidth = current.ScreenshotWidth; sample.ScreenshotHeight = current.ScreenshotHeight;
            sample.Diagnostics.AddRange(current.Diagnostics); sample.Screenshots = current.Screenshots; sample.ExecutionStatus = current.ExecutionStatus;
            sample.MeasurementLimits.Add("Main metric is native DamageResult.totalDamageDealt for Pawn damage, not raw weapon damage or capped absolute health loss.");
            sample.MeasurementLimits.Add("Direct Hediff edits, bleeding and damage bypassing Thing.TakeDamage are not attributed; no last-attacker inference. Native CompShield fully absorbed input is a separate shield channel, never main DPS. Other shield implementations have unknown absorbed quantity. Armor/item and building channels remain separate.");
            sample.MeasurementQuality = sample.Diagnostics.Any(d => d.StartsWith("damage.") && !d.StartsWith("damage.forwarded_result")) ? "incomplete" : "partial";
            sample.RequireCompleteAttribution = ScenarioLoader.A(roundConfig.Element("GroupCombat").Element("Metrics"), "requireCompleteAttribution", "false") == "true";
            sample.MeasurementRequirementsMet &= sample.MeasurementQuality != "incomplete" && (!sample.RequireCompleteAttribution || sample.MeasurementQuality == "complete");
            current = sample;
        }

        private static void Freeze(string executionStatus, string reason, string error)
        {
            if (phase == "finishing") return;
            if (ledger != null) { Refresh(phase == "running" ? Math.Max(0, Find.TickManager.TicksGame - startTick) : 0, includeEventDetails: true); if (executionStatus == "completed") EvaluateEnd(); }
            current.ExecutionStatus = executionStatus; current.EndReason = reason; current.Error = error;
            Trace("group.final_clock", "pausedRealSeconds=" + pausedRealtime.ToString("F3", CultureInfo.InvariantCulture));
            if (GroupCombatCamera.Active) Trace("group.camera", JsonSerializer.Serialize(GroupCombatCamera.Status));
            phase = "finishing"; settlementStartedRealtime = Time.realtimeSinceStartup; settlementStartTick = Find.TickManager.TicksGame;
            Find.TickManager.CurTimeSpeed = TimeSpeed.Paused; RequestCapture("final"); Write();
        }

        private static void RequestCapture(string label)
        {
            var directory = Path.Combine(outputPath, ArtifactPrefix + "_" + activeRunId + "_Screenshots"); Directory.CreateDirectory(directory);
            pendingCapture = Path.Combine(directory, roundIndex.ToString("D4") + "_" + label + ".png");
            captureRequested = false; preparedFrame = Time.frameCount; phaseStartedRealtime = Time.realtimeSinceStartup;
            if (label == "initial") CombatStartupProbe.CaptureRequested();
        }
        private static bool CaptureFinished()
        {
            // Finish the bounded portrait warmup while the roster is still paused.
            // Start the capture deadline after warmup, rather than timing out large teams.
            if (ShowingIntro && OverlayEnabled && !GroupCombatOverlay.PortraitsReady)
            {
                phaseStartedRealtime = Time.realtimeSinceStartup;
                return false;
            }
            if (captureRequested && AutoTestViewComposite.IsReady(pendingCapture))
            {
                if (phase == "roster") CombatStartupProbe.CaptureReady();
                return true;
            }
            if (Time.realtimeSinceStartup - phaseStartedRealtime < 20) return false;
            var error = "Screenshot did not become ready: " + pendingCapture;
            current.Diagnostics.Add(error);
            if (ShowingIntro) { Freeze("error", "captureFailure", error); return false; }
            if (current.ExecutionStatus == "completed") { current.ExecutionStatus = "error"; current.Error = error; }
            return true;
        }
        internal static void OnGui()
        {
            if (!Busy || input == null || Map == null) return;
            // Both modes use the existing roster/countdown presentation before
            // combat. Tournament trees are shown only after settlement.
            GroupCombatOverlay.Draw();
            if (Event.current.type == EventType.Repaint) CombatStartupProbe.Repaint(CountdownLabel);
            if (ShowingIntro && OverlayEnabled && !GroupCombatOverlay.PortraitsReady) return;
            if (Event.current.type != EventType.Repaint || captureRequested || pendingCapture == null || Time.frameCount - preparedFrame < 3 || Time.realtimeSinceStartup - phaseStartedRealtime < 1 || !Screen.fullScreen || !AutoTestScreenshotService.PrepareCaptureFrame()) return;
            current.ScreenshotWidth = requestedWidth > 0 ? requestedWidth : Screen.width; current.ScreenshotHeight = requestedHeight > 0 ? requestedHeight : Screen.height;
            var captureBegan = CombatStartupProbe.Now;
            if (current.ScreenshotWidth == Screen.width && current.ScreenshotHeight == Screen.height) ScreenCapture.CaptureScreenshot(pendingCapture);
            else Find.CameraDriver.StartCoroutine(GroupCombatScreenshot.Capture(pendingCapture, current.ScreenshotWidth, current.ScreenshotHeight));
            if (phase == "roster") CombatStartupProbe.CaptureCall(captureBegan);
            current.Screenshots.Add(pendingCapture); captureRequested = true;
        }

        private static void FinishRound()
        {
            Trace("group.overlay_cache", JsonSerializer.Serialize(GroupCombatOverlay.CacheStatus));
            CombatStartupProbe.Stop();
            if (ShowingSettlement) Trace("group.settlement", "realSeconds=" + (Time.realtimeSinceStartup - settlementStartedRealtime).ToString("F3", CultureInfo.InvariantCulture) + "; simulationTicks=" + (Find.TickManager.TicksGame - settlementStartTick));
            phase = "cleaning"; introFacing.Clear(); teamLords.Clear(); lifecyclePawns.Clear();
            GroupCombatCamera.End();
            var errors = new List<string>();
            try { backend?.Cleanup(); } catch (Exception ex) { errors.Add(ex.ToString()); }
            try { arena?.Dispose(); } catch (Exception ex) { errors.Add(ex.ToString()); }
            foreach (var faction in factions.Values)
            {
                try { AccessTools.Method(typeof(FactionManager), "Remove").Invoke(Find.FactionManager, new object[] { faction }); ((List<Faction>)AccessTools.Field(typeof(FactionManager), "toRemove").GetValue(Find.FactionManager)).Remove(faction); }
                catch (Exception ex) { errors.Add(ex.ToString()); }
            }
            if (preexistingFactions != null)
            {
                try
                {
                    if (preexistingRelations != RelationFingerprint()) errors.Add("Preexisting faction relations changed during the benchmark");
                    if (preexistingFactions.Any(f => factions.Values.Any(owned => f.RelationWith(owned, true) != null))) errors.Add("Temporary faction relation survived native faction removal");
                    current.Diagnostics.Add("group.cleanup_relations: preexistingUnchanged=" + (preexistingRelations == RelationFingerprint()) + "; temporaryRelationsRemoved=" + !preexistingFactions.Any(f => factions.Values.Any(owned => f.RelationWith(owned, true) != null)));
                }
                catch (Exception ex) { errors.Add(ex.ToString()); }
            }
            // Native map unloading may temporarily pass corpse inner pawns to WorldPawns.
            // Discard only this round's roster again after the map has gone.
            foreach (var pawn in pawns.Values)
            {
                try { if (Find.WorldPawns.Contains(pawn)) Find.WorldPawns.RemovePawn(pawn); if (!pawn.Destroyed) pawn.Destroy(DestroyMode.Vanish); if (Find.WorldPawns.Contains(pawn)) Find.WorldPawns.RemovePawn(pawn); }
                catch (Exception ex) { errors.Add(ex.ToString()); }
            }
            current.CleanupComplete = errors.Count == 0 && (arena?.Map == null || !Find.Maps.Contains(arena.Map)) && !factions.Values.Any(f => Find.FactionManager.AllFactions.Contains(f)) && pawns.Values.All(p => p.Destroyed && !Find.WorldPawns.Contains(p));
            if (!current.CleanupComplete) { current.ExecutionStatus = "cleanup_error"; current.Error = string.Join("\n", errors) + "; mapRegistered=" + (arena?.Map != null && Find.Maps.Contains(arena.Map)) + "; factionResidual=" + factions.Values.Count(f => Find.FactionManager.AllFactions.Contains(f)) + "; pawnResidual=" + string.Join(",", pawns.Where(p => !p.Value.Destroyed || Find.WorldPawns.Contains(p.Value)).Select(p => p.Key)); }
            Log.Message("[LogSyncGroupCombat] ROUND caseId=" + current.CaseId + " status=" + current.ExecutionStatus + " ticks=" + current.ElapsedTicks + " events=" + current.TotalEvents + " cleanup=" + current.CleanupComplete);
            ledger = null; arena = null; backend = null; input = null; verifyTickFailure = false; pawns.Clear(); rosterIds.Clear(); exited.Clear(); factions.Clear(); teamAi.Clear(); pendingCapture = null; GroupCombatOverlay.Clear(); GroupCombatDamageCapture.Clear(); GroupCombatAiDiagnostics.Reset();
            Find.TickManager.CurTimeSpeed = originalSpeed;
            var callback = completed; var result = current;
            if (Screen.width != originalScreenWidth || Screen.height != originalScreenHeight || Screen.fullScreenMode != originalScreenMode) Screen.SetResolution(originalScreenWidth, originalScreenHeight, originalScreenMode);
            completed = null; stateChanged = null; arenaEndRule = null; current = null; preexistingFactions = null; preexistingRelations = null; endRule = null; phase = "complete";
            callback(result);
        }

        private static void Trace(string stage, string details) { current?.Diagnostics.Add(stage + ": " + details); }
        private static string RelationFingerprint() => string.Join(";", preexistingFactions.SelectMany(a => preexistingFactions.Where(b => b != a).Select(b =>
        {
            var relation = a.RelationWith(b, true);
            return a.loadID + ">" + b.loadID + ":" + (relation == null ? "none" : relation.kind + "/" + relation.baseGoodwill);
        })));
        internal static void DamageDiagnostic(string message) { if (current != null && !current.Diagnostics.Contains(message)) current.Diagnostics.Add(message); }
        private static void Write() => stateChanged?.Invoke();
    }
}
