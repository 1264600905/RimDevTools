using System;
using System.Linq;
using System.Xml.Linq;
using LogSyncMod.AutoTest;
using LogSyncMod.AutoTest.Scenarios;

internal static class ArenaIdentityModeTests
{
    internal static void Run(Action<bool, string> check)
    {
        var input = XElement.Parse("<PawnTemplate id='p'><basePawnKind>Colonist</basePawnKind><Xenotype def='Old'/><Genes mode='replace'><Add def='OldGene' xenogene='true'/><Add def='Custom'/><Remove def='Wing'/></Genes><Traits mode='replace'/><Skills mode='patch'><Set def='Shooting' level='12'/></Skills><Apparel mode='replace'/><Equipment mode='replace'/><Abilities mode='patch'><Ensure def='Ability'/></Abilities></PawnTemplate>");
        var source = input.ToString();
        var human = ArenaIdentityPolicy.SelectHuman(input, "Human", "Flying", new[] { "Wing", "Flight" }, false, new[] { "OldGene" });
        check((string)human.Element("Xenotype").Attribute("def") == "Flying" && human.Element("race").Value == "Human", "identity dialog applies human race and xenotype together");
        check(human.Element("Genes").Elements("Add").Any(e => (string)e.Attribute("def") == "Custom")
            && !human.Element("Genes").Elements("Add").Any(e => (string)e.Attribute("def") == "OldGene"), "changing xenotype keeps custom genes and replaces previous innate genes");
        check(human.Element("Genes").Elements("Add").Where(e => new[] { "Wing", "Flight" }.Contains((string)e.Attribute("def"))).All(e => (string)e.Attribute("xenogene") == "true"), "non-inheritable xenotype innate genes enter the xenogene collection");
        check(!human.Element("Genes").Elements("Remove").Any(e => (string)e.Attribute("def") == "Wing"), "old removal rules cannot erase new innate genes");
        var inheritable = ArenaIdentityPolicy.SelectHuman(input, "Human", "Inherited", new[] { "Wing" }, true, new[] { "OldGene" });
        check((string)inheritable.Element("Genes").Elements("Add").Single(e => (string)e.Attribute("def") == "Wing").Attribute("xenogene") == "false", "inheritable xenotypes use endogenes");
        var nonHuman = ArenaIdentityPolicy.SelectNonHuman(input, "TrispikeKind", "Trispike", false, false);
        check(nonHuman.Element("basePawnKind").Value == "TrispikeKind" && nonHuman.Element("race").Value == "Trispike", "nonhuman tab sets a matching real pawnkind and race");
        check(!new[] { "Xenotype", "Genes", "Traits", "Skills", "Apparel", "Equipment", "Abilities" }.Any(f => nonHuman.Element(f) != null), "nonhuman identity removes unsupported human configuration and cannot request a gene tracker");
        var mech = ArenaIdentityPolicy.SelectNonHuman(input, "Mech", "MechRace", true, true);
        check(mech.Element("Equipment") != null && mech.Element("Abilities") != null && mech.Element("Genes") == null, "tool-using nonhuman units retain supported equipment and abilities only");
        check(input.ToString() == source, "identity selection is an isolated draft until applied");
        human.Element("Genes").Elements("Add").Where(e => (string)e.Attribute("def") == "Wing").Remove();
        human.Element("Genes").Add(new XElement("Remove", new XAttribute("def", "Wing")));
        ArenaIdentityPolicy.EnsureInnateGenes(human, new[] { "Wing", "Flight" }, false);
        check(human.Element("Genes").Elements("Add").Any(e => (string)e.Attribute("def") == "Wing") && !human.Element("Genes").Elements("Remove").Any(), "editing a replace-mode gene list restores mandatory xenotype genes");
        var implicitGenes = XElement.Parse("<PawnTemplate id='p'><basePawnKind>Colonist</basePawnKind><Xenotype def='Flying'/></PawnTemplate>");
        ArenaIdentityPolicy.EnsureInnateGenes(implicitGenes, new[] { "Wing" }, false);
        check(implicitGenes.Element("Genes").Elements("Add").Any(e => (string)e.Attribute("def") == "Wing"), "selected xenotype innate genes remain visible without a custom gene section");
        check(!ArenaIdentityPolicy.AcceptNewParticipant(true, false, true, false), "unregistered corpse inner pawns are not counted as reinforcements");
        check(!ArenaIdentityPolicy.AcceptNewParticipant(false, true, false, false), "destroyed transient pawns are not counted as new units");
        check(ArenaIdentityPolicy.AcceptNewParticipant(true, false, true, true), "existing roster deaths retain their original participant identity");
        check(ArenaIdentityPolicy.AcceptNewParticipant(false, false, false, false), "living native summons are still discovered normally");
        var presets = ArenaWorkbench.BuiltInSchedules();
        check(presets.Count == 4 && presets.Select(p => p.Name).SequenceEqual(new[] { "群战测试", "擂台赛", "魔王挑战赛", "极限挑战赛" }), "schedule UI has exactly the four user-defined preset modes");
        foreach (var preset in presets)
        {
            var doc = ArenaWorkbench.Create(); doc.Schedule = preset; ArenaWorkbench.ValidateEvent(doc);
            check(true, "fixed mode produces supported execution input: " + preset.Name);
        }
        var group = ArenaWorkbench.Create(); group.Schedule = presets[0];
        var groupPlan = ArenaWorkbench.CompileGroup(group);
        check(GroupCombatPlan.Expand(groupPlan).Count == 1 && groupPlan.Purpose == "groupcombat", "group test runs one direct fight rather than an arena knockout tournament");
        var runtime = ArenaWorkbench.CompileGroup(group, true);
        check(GroupCombatPlan.Expand(runtime).Single().Seed >= 0 && (string)ArenaWorkbench.Export(group).Element("Case").Attribute("seed") == "-1", "manual group test resolves its random seed without changing the editor sentinel");
        group.Teams.Add(ArenaWorkbench.Duplicate(group.Teams[0]));
        Reject(() => ArenaWorkbench.CompileGroup(group), "direct group test rejects more than the two teams supported by the native ledger");
        group.Teams[2].Enabled = false;
        check(ArenaWorkbench.CompileGroup(group).Cases.Count == 1, "disabled extra squads do not enter direct group test");
        var captured = ArenaWorkbench.CaptureEvent(group); var restored = ArenaWorkbench.Create(); ArenaWorkbench.ApplyEvent(restored, captured);
        check(restored.Schedule.Format == "groupcombat" && restored.Teams.Count == 3 && !restored.Teams[2].Enabled, "complete event presets preserve independent group-test mode and disabled teams");
        void Reject(Action action, string text)
        { try { action(); throw new InvalidOperationException("Unexpected accepted input"); } catch (ScenarioConfigurationException) { check(true, text); } }
    }
}
