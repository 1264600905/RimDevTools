using System.Collections.Generic;
using System.Runtime.Serialization;

namespace LogSyncMod.AutoTest
{
    [DataContract]
    public sealed class AutoTestReadinessInput
    {
        [DataMember(Name = "hasCurrentGame", Order = 1)] public bool HasCurrentGame { get; set; }
        [DataMember(Name = "mapCount", Order = 2)] public int MapCount { get; set; }
        [DataMember(Name = "hasCurrentMap", Order = 3)] public bool HasCurrentMap { get; set; }
        [DataMember(Name = "programState", Order = 4)] public string ProgramState { get; set; }
        [DataMember(Name = "longEventPending", Order = 5)] public bool LongEventPending { get; set; }
        [DataMember(Name = "tickManagerReady", Order = 6)] public bool TickManagerReady { get; set; }
        [DataMember(Name = "windowStackReady", Order = 7)] public bool WindowStackReady { get; set; }
        [DataMember(Name = "selectorReady", Order = 8)] public bool SelectorReady { get; set; }
        [DataMember(Name = "screenFadeClear", Order = 9)] public bool ScreenFadeClear { get; set; }
    }

    [DataContract]
    public sealed class AutoTestReadinessSnapshot
    {
        [DataMember(Name = "hasCurrentGame", Order = 1)] public bool HasCurrentGame { get; set; }
        [DataMember(Name = "mapCount", Order = 2)] public int MapCount { get; set; }
        [DataMember(Name = "hasCurrentMap", Order = 3)] public bool HasCurrentMap { get; set; }
        [DataMember(Name = "programState", Order = 4)] public string ProgramState { get; set; }
        [DataMember(Name = "longEventPending", Order = 5)] public bool LongEventPending { get; set; }
        [DataMember(Name = "tickManagerReady", Order = 6)] public bool TickManagerReady { get; set; }
        [DataMember(Name = "windowStackReady", Order = 7)] public bool WindowStackReady { get; set; }
        [DataMember(Name = "selectorReady", Order = 8)] public bool SelectorReady { get; set; }
        [DataMember(Name = "screenFadeClear", Order = 9)] public bool ScreenFadeClear { get; set; }
        [DataMember(Name = "gameDataReady", Order = 10)] public bool GameDataReady { get; set; }
        [DataMember(Name = "mapDataReady", Order = 11)] public bool MapDataReady { get; set; }
        [DataMember(Name = "playable", Order = 12)] public bool Playable { get; set; }
        [DataMember(Name = "visualReady", Order = 13)] public bool VisualReady { get; set; }
        [DataMember(Name = "automationReady", Order = 14)] public bool AutomationReady { get; set; }
        [DataMember(Name = "summary", Order = 15)] public string Summary { get; set; }
    }

    [DataContract]
    public sealed class AutoTestSemanticGraphOptions
    {
        public AutoTestSemanticGraphOptions()
        {
            MaxDepth = 4;
            MaxChildrenPerNode = 100;
            MaxStringLength = 4096;
            MaxCollectionItems = 100;
        }

        [DataMember(Name = "maxDepth", Order = 1)] public int MaxDepth { get; set; }
        [DataMember(Name = "maxChildrenPerNode", Order = 2)] public int MaxChildrenPerNode { get; set; }
        [DataMember(Name = "maxStringLength", Order = 3)] public int MaxStringLength { get; set; }
        [DataMember(Name = "maxCollectionItems", Order = 4)] public int MaxCollectionItems { get; set; }
    }

    [DataContract]
    public sealed class AutoTestSemanticNode
    {
        public AutoTestSemanticNode() { Children = new List<AutoTestSemanticNode>(); }
        [DataMember(Name = "name", Order = 1)] public string Name { get; set; }
        [DataMember(Name = "path", Order = 2)] public string Path { get; set; }
        [DataMember(Name = "valueKind", Order = 3)] public string ValueKind { get; set; }
        [DataMember(Name = "typeName", Order = 4)] public string TypeName { get; set; }
        [DataMember(Name = "value", Order = 5)] public string Value { get; set; }
        [DataMember(Name = "truncated", Order = 6)] public bool Truncated { get; set; }
        [DataMember(Name = "isReference", Order = 7)] public bool IsReference { get; set; }
        [DataMember(Name = "children", Order = 8)] public List<AutoTestSemanticNode> Children { get; set; }
    }

    [DataContract]
    public sealed class AutoTestThingSnapshot
    {
        public AutoTestThingSnapshot() { CompTypeNames = new List<string>(); }
        [DataMember(Name = "target", Order = 1)] public AutoTestTargetRef Target { get; set; }
        [DataMember(Name = "spawned", Order = 2)] public bool Spawned { get; set; }
        [DataMember(Name = "destroyed", Order = 3)] public bool Destroyed { get; set; }
        [DataMember(Name = "mapId", Order = 4)] public int MapId { get; set; }
        [DataMember(Name = "position", Order = 5)] public string Position { get; set; }
        [DataMember(Name = "occupiedRect", Order = 6)] public string OccupiedRect { get; set; }
        [DataMember(Name = "stackCount", Order = 7)] public int StackCount { get; set; }
        [DataMember(Name = "hitPoints", Order = 8)] public int HitPoints { get; set; }
        [DataMember(Name = "maxHitPoints", Order = 9)] public int MaxHitPoints { get; set; }
        [DataMember(Name = "faction", Order = 10)] public string Faction { get; set; }
        [DataMember(Name = "forbidden", Order = 11)] public bool Forbidden { get; set; }
        [DataMember(Name = "burning", Order = 12)] public bool Burning { get; set; }
        [DataMember(Name = "selected", Order = 13)] public bool Selected { get; set; }
        [DataMember(Name = "compTypeNames", Order = 14)] public List<string> CompTypeNames { get; set; }
        [DataMember(Name = "summary", Order = 15)] public string Summary { get; set; }
    }

    [DataContract]
    public sealed class AutoTestHediffSnapshot
    {
        [DataMember(Name = "defName", Order = 1)] public string DefName { get; set; }
        [DataMember(Name = "severity", Order = 2)] public float Severity { get; set; }
        [DataMember(Name = "stage", Order = 3)] public string Stage { get; set; }
        [DataMember(Name = "ticksToDisappear", Order = 4)] public int? TicksToDisappear { get; set; }
    }

    [DataContract]
    public sealed class AutoTestAbilitySnapshot
    {
        [DataMember(Name = "defName", Order = 1)] public string DefName { get; set; }
        [DataMember(Name = "available", Order = 2)] public bool Available { get; set; }
        [DataMember(Name = "disabled", Order = 3)] public bool Disabled { get; set; }
        [DataMember(Name = "disabledReason", Order = 4)] public string DisabledReason { get; set; }
        [DataMember(Name = "cooldownTicks", Order = 5)] public int? CooldownTicks { get; set; }
        [DataMember(Name = "charges", Order = 6)] public int? Charges { get; set; }
        [DataMember(Name = "targetingSummary", Order = 7)] public string TargetingSummary { get; set; }
    }

    [DataContract]
    public sealed class AutoTestPawnSnapshot
    {
        public AutoTestPawnSnapshot()
        {
            Hediffs = new List<AutoTestHediffSnapshot>();
            EquipmentDefNames = new List<string>();
            ApparelDefNames = new List<string>();
            InventoryDefNames = new List<string>();
            Abilities = new List<AutoTestAbilitySnapshot>();
        }
        [DataMember(Name = "target", Order = 1)] public AutoTestTargetRef Target { get; set; }
        [DataMember(Name = "alive", Order = 2)] public bool Alive { get; set; }
        [DataMember(Name = "dead", Order = 3)] public bool Dead { get; set; }
        [DataMember(Name = "downed", Order = 4)] public bool Downed { get; set; }
        [DataMember(Name = "destroyed", Order = 5)] public bool Destroyed { get; set; }
        [DataMember(Name = "drafted", Order = 6)] public bool Drafted { get; set; }
        [DataMember(Name = "mapId", Order = 7)] public int MapId { get; set; }
        [DataMember(Name = "position", Order = 8)] public string Position { get; set; }
        [DataMember(Name = "currentJobDefName", Order = 9)] public string CurrentJobDefName { get; set; }
        [DataMember(Name = "currentJobTarget", Order = 10)] public string CurrentJobTarget { get; set; }
        [DataMember(Name = "mentalStateDefName", Order = 11)] public string MentalStateDefName { get; set; }
        [DataMember(Name = "healthPercent", Order = 12)] public float HealthPercent { get; set; }
        [DataMember(Name = "hediffs", Order = 13)] public List<AutoTestHediffSnapshot> Hediffs { get; set; }
        [DataMember(Name = "equipmentDefNames", Order = 14)] public List<string> EquipmentDefNames { get; set; }
        [DataMember(Name = "apparelDefNames", Order = 15)] public List<string> ApparelDefNames { get; set; }
        [DataMember(Name = "inventoryDefNames", Order = 16)] public List<string> InventoryDefNames { get; set; }
        [DataMember(Name = "abilities", Order = 17)] public List<AutoTestAbilitySnapshot> Abilities { get; set; }
        [DataMember(Name = "summary", Order = 18)] public string Summary { get; set; }
    }

    [DataContract]
    public sealed class AutoTestCombatSnapshot
    {
        public AutoTestCombatSnapshot() { ActiveProjectileDefNames = new List<string>(); }
        [DataMember(Name = "attackerId", Order = 1)] public string AttackerId { get; set; }
        [DataMember(Name = "targetId", Order = 2)] public string TargetId { get; set; }
        [DataMember(Name = "tick", Order = 3)] public int Tick { get; set; }
        [DataMember(Name = "targetHealthPercent", Order = 4)] public float TargetHealthPercent { get; set; }
        [DataMember(Name = "targetHitPoints", Order = 5)] public int TargetHitPoints { get; set; }
        [DataMember(Name = "targetDowned", Order = 6)] public bool TargetDowned { get; set; }
        [DataMember(Name = "targetDead", Order = 7)] public bool TargetDead { get; set; }
        [DataMember(Name = "currentTargetId", Order = 8)] public string CurrentTargetId { get; set; }
        [DataMember(Name = "currentJobDefName", Order = 9)] public string CurrentJobDefName { get; set; }
        [DataMember(Name = "activeProjectileCount", Order = 10)] public int ActiveProjectileCount { get; set; }
        [DataMember(Name = "activeProjectileDefNames", Order = 11)] public List<string> ActiveProjectileDefNames { get; set; }
        [DataMember(Name = "supported", Order = 12)] public bool Supported { get; set; }
        [DataMember(Name = "unsupportedReason", Order = 13)] public string UnsupportedReason { get; set; }
        [DataMember(Name = "summary", Order = 14)] public string Summary { get; set; }
    }

    [DataContract]
    public sealed class AutoTestWindowSnapshot
    {
        public AutoTestWindowSnapshot() { WindowTypes = new List<string>(); }
        [DataMember(Name = "captureId", Order = 1)] public string CaptureId { get; set; }
        [DataMember(Name = "windowTypes", Order = 2)] public List<string> WindowTypes { get; set; }
        [DataMember(Name = "topmostWindowType", Order = 3)] public string TopmostWindowType { get; set; }
        [DataMember(Name = "isOpen", Order = 4)] public bool IsOpen { get; set; }
        [DataMember(Name = "windowTarget", Order = 5)] public AutoTestTargetRef WindowTarget { get; set; }
        [DataMember(Name = "windowRect", Order = 6)] public string WindowRect { get; set; }
        [DataMember(Name = "closeOnAccept", Order = 7)] public bool CloseOnAccept { get; set; }
        [DataMember(Name = "closeOnCancel", Order = 8)] public bool CloseOnCancel { get; set; }
        [DataMember(Name = "closeOnClickedOutside", Order = 9)] public bool CloseOnClickedOutside { get; set; }
        [DataMember(Name = "doCloseX", Order = 10)] public bool DoCloseX { get; set; }
        [DataMember(Name = "boundDefName", Order = 11)] public string BoundDefName { get; set; }
        [DataMember(Name = "boundBillId", Order = 12)] public string BoundBillId { get; set; }
        [DataMember(Name = "boundRecipeDefName", Order = 13)] public string BoundRecipeDefName { get; set; }
        [DataMember(Name = "summary", Order = 14)] public string Summary { get; set; }
    }

    [DataContract]
    public sealed class AutoTestSelectionSnapshot
    {
        public AutoTestSelectionSnapshot() { Targets = new List<AutoTestTargetRef>(); Gizmos = new List<AutoTestGizmoRef>(); }
        [DataMember(Name = "selectionId", Order = 1)] public string SelectionId { get; set; }
        [DataMember(Name = "fingerprint", Order = 2)] public string Fingerprint { get; set; }
        [DataMember(Name = "targets", Order = 3)] public List<AutoTestTargetRef> Targets { get; set; }
        [DataMember(Name = "gizmos", Order = 4)] public List<AutoTestGizmoRef> Gizmos { get; set; }
        [DataMember(Name = "summary", Order = 5)] public string Summary { get; set; }
    }
}
