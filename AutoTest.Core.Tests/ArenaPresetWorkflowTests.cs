using System;
using System.Linq;
using System.Xml.Linq;
using LogSyncMod.AutoTest;
using LogSyncMod.AutoTest.Scenarios;

internal static class ArenaPresetWorkflowTests
{
    internal static void Run(Action<bool, string> check)
    {
        var doc = ArenaWorkbench.Create();
        doc.Teams[1].Enabled = false;
        doc.Teams.Add(ArenaWorkbench.Duplicate(doc.Teams[0]));
        doc.Schedule.Seed = 123; doc.Schedule.Name = "完整赛事";
        var captured = ArenaWorkbench.CaptureEvent(doc);
        check(captured.Teams.Count == 3 && !captured.Teams[1].Enabled && captured.Schedule.Seed == 123,
            "event preset retains all teams including disabled entrants and their seed");
        var expectedPawn = captured.Teams[0].Members[0].PawnXml;
        doc.Teams[0].Members[0].PawnXml = "<PawnTemplate id='p'><basePawnKind>Rat</basePawnKind></PawnTemplate>";
        doc.Schedule.Seed = -1;
        check(captured.Teams[0].Members[0].PawnXml == expectedPawn && captured.Schedule.Seed == 123,
            "editing the current event cannot mutate a saved event snapshot");
        var target = ArenaWorkbench.Create();
        var library = target.PawnPresets; var squads = target.TeamPresets;
        ArenaWorkbench.ApplyEvent(target, captured);
        check(target.Teams.Count == 3 && target.Schedule.Seed == 123 && ReferenceEquals(library, target.PawnPresets) && ReferenceEquals(squads, target.TeamPresets),
            "loading a full event replaces only current event data and preserves preset libraries");
        target.Teams[0].Members[0].Name = "本届修改";
        check(captured.Teams[0].Members[0].Name != "本届修改", "loaded event members are independent editable copies");
        captured.Enabled = false; var oldTarget = target.Teams;
        Reject(() => ArenaWorkbench.ApplyEvent(target, captured), "disabled event presets cannot be loaded");
        check(ReferenceEquals(oldTarget, target.Teams), "rejected preset load leaves the active event unchanged");
        captured.Enabled = true;
        var eventXml = ArenaWorkbench.ReadXml(ArenaWorkbench.EventXml(captured));
        check(eventXml.Name == "ArenaSuite" && eventXml.Element("Tournament").Element("Entrants").Elements().Count() == 3
            && eventXml.Element("Templates").HasElements, "event export contains actual team rosters and pawn template dependencies");
        var addTarget = new ArenaWorkbenchTeam();
        var before = doc.PawnPresets[0].PawnXml;
        ArenaWorkbench.AddPawnPresets(addTarget, doc.PawnPresets);
        check(addTarget.Members.Count == 2 && addTarget.Members.All(m => m.Count == 1), "multi-select adds every selected role with an initial count of one");
        addTarget.Members[0].PawnXml = "<PawnTemplate id='p'><basePawnKind>Rat</basePawnKind></PawnTemplate>";
        check(doc.PawnPresets[0].PawnXml == before, "added member edit does not mutate its source preset");
        var bad = ArenaWorkbench.Copy(doc.PawnPresets[0]); bad.PawnXml = "<Invalid/>";
        var currentCount = addTarget.Members.Count;
        Reject(() => ArenaWorkbench.AddPawnPresets(addTarget, new[] { doc.PawnPresets[0], bad }), "invalid multi-select input is rejected before adding any roles");
        check(addTarget.Members.Count == currentCount, "failed multi-add is atomic and keeps the existing editor roster");
        var full = new ArenaWorkbenchTeam { Members = new() { ArenaWorkbench.Copy(doc.PawnPresets[0]) } }; full.Members[0].Count = 200;
        Reject(() => ArenaWorkbench.AddPawnPresets(full, new[] { doc.PawnPresets[1] }), "multi-add cannot overflow the native 200-person team limit");
        check(full.Members.Count == 1, "over-limit additions leave the roster untouched");
        var faction = new ArenaWorkbenchTeam { Source = "faction" };
        Reject(() => ArenaWorkbench.AddPawnPresets(faction, doc.PawnPresets), "role templates cannot silently switch a battle-group team into personnel mode");
        var stored = ArenaWorkbench.Create(); stored.EventPresets.Add(captured);
        var libraryXml = ArenaWorkbench.ReadXml(ArenaWorkbench.LibraryXml(stored));
        check(libraryXml.Element("EventPresets").Elements("Preset").Single().Element("ArenaSuite") != null, "preset library XML includes full event snapshots");
        var persisted = ArenaWorkbench.Copy(stored);
        check(persisted.EventPresets.Single().Teams.Count == 3, "full event presets survive draft JSON persistence");
        var invalid = ArenaWorkbench.Create(); invalid.Teams[0].Count = -1;
        Reject(() => ArenaWorkbench.ValidateStructure(invalid), "negative legacy team size is rejected");
        invalid = ArenaWorkbench.Create(); invalid.Teams[0].GroupReferencePoints = float.NaN;
        Reject(() => ArenaWorkbench.ValidateStructure(invalid), "nonfinite battle-group reference points are rejected");
        invalid = ArenaWorkbench.Create(); invalid.PawnPresets[0].Name = new string('x', 121);
        Reject(() => ArenaWorkbench.ValidateStructure(invalid), "overlong role names cannot be persisted");
        invalid = ArenaWorkbench.Create(); invalid.Schedule.Duration = double.PositiveInfinity;
        Reject(() => ArenaWorkbench.ValidateStructure(invalid), "nonfinite match deadlines cannot be persisted");
        invalid = ArenaWorkbench.Create(); invalid.EventPresets.Add(new ArenaWorkbenchEventPreset { Teams = null });
        Reject(() => ArenaWorkbench.ValidateStructure(invalid), "malformed event preset data is rejected");
        var pruned = ArenaWorkbench.Create(); var e = ArenaWorkbench.CaptureEvent(pruned); e.Teams.Add(new ArenaWorkbenchTeam()); pruned.EventPresets.Add(e);
        check(ArenaWorkbench.ForStorage(pruned, false).EventPresets[0].Teams.Count == 2 && e.Teams.Count == 3,
            "autosave prunes empty teams inside saved events without changing the live draft");

        void Reject(Action action, string text)
        { try { action(); throw new InvalidOperationException("Accepted invalid preset: " + text); } catch (ScenarioConfigurationException) { check(true, text); } }
    }
}
