using System;
using System.Linq;
using System.Xml.Linq;
using LogSyncMod.AutoTest;
using LogSyncMod.AutoTest.Scenarios;

internal static class ArenaWorkbenchTests
{
    internal static void Run(Action<bool, string> check)
    {
        var doc = ArenaWorkbench.Create();
        var xml = ArenaWorkbench.Export(doc);
        var plan = ArenaWorkbench.Compile(doc);
        check(plan.Tournaments.Count == 1 && plan.Tournaments[0].Format == "knockout", "workbench default exports a real runnable ArenaSuite");
        check(xml.Element("Templates").Elements().Count() == 3, "every mixed member group is exported as its own pawn template");
        check(plan.Tournaments[0].SquadTemplates["squad_0"].PawnGroups.Count == 2, "GUI mixed teams use existing source=groups");
        check(plan.Tournaments[0].Entrants[0].Count == 12, "mixed team count comes from its constituent groups");
        check(xml.Element("Tournament").Element("Entrants").Elements("Entrant").All(e => (string)e.Attribute("ai") == null), "inherited workbench team AI omits the override and follows Battle aiPolicy");
        check(XElement.Parse(plan.Tournaments[0].SquadTemplates["squad_0"].PawnGroups[1]).Element("Equipment").Element("SetPrimary").Attribute("stuff").Value == "Steel", "gear and valid stuff survive template resolution");
        var untouched = doc.Teams[0].Members[0].PawnXml;
        var duplicate = ArenaWorkbench.Duplicate(doc.Teams[0]);
        ArenaWorkbench.ReadAndChange(duplicate.Members[0], x => x.Element("Skills").Element("Set").SetAttributeValue("level", 20));
        check(doc.Teams[0].Members[0].PawnXml == untouched && duplicate.Id != doc.Teams[0].Id, "team duplication has new identity and independent nested pawn XML");
        var count = doc.Teams[0].MemberCount;
        var split = ArenaWorkbench.Split(doc.Teams[0], 0);
        check(split.Count == 1 && doc.Teams[0].Members[0].Count == 7 && doc.Teams[0].MemberCount == count, "splitting one pawn conserves the roster population");
        ArenaWorkbench.ReadAndChange(split, x => x.Element("Equipment").Element("SetPrimary").SetAttributeValue("def", "Gun_SniperRifle"));
        check(doc.Teams[0].Members[0].PawnXml == untouched, "single-pawn edit does not affect the rest of its original group");
        Reject(() => ArenaWorkbench.Split(doc.Teams[0], 1), "cannot split a one-person group");
        var clone = ArenaWorkbench.Copy(doc);
        clone.Teams[1].Enabled = false;
        check(ArenaWorkbench.Export(clone).Element("Tournament").Element("Entrants").Elements().Count() == 1, "disabled teams are not submitted to this tournament");
        Reject(() => ArenaWorkbench.Compile(clone), "one selected team is rejected before runtime");
        check(ArenaWorkbench.Export(clone, false).Element("Tournament").Element("Entrants").Elements().Count() == 2, "complete export can retain disabled team definitions");
        foreach (var preset in ArenaWorkbench.BuiltInSchedules())
        {
            var input = ArenaWorkbench.Create(); input.Schedule = preset;
            if (preset.GrowthTarget == "budget")
            {
                input.Teams[0].Source = "kind"; input.Teams[0].PawnKind = "Wolf_Timber";
                input.Teams[0].Mode = "combatPower"; input.Teams[0].Budget = 1000;
            }
            if (preset.Format == "groupcombat")
            {
                var compiledGroup = ArenaWorkbench.CompileGroup(input);
                var groupCase = XElement.Parse(compiledGroup.Cases.Single().Xml);
                check(compiledGroup.Purpose == "groupcombat" && GroupCombatPlan.Expand(compiledGroup).Count == 1
                    && (string)groupCase.Element("Teams").Elements("Team").First().Attribute("ai") == null,
                    "groupcombat preset uses the independent executor and inherited team AI");
            }
            else
            {
                input.Teams[0].Ai = "defendPoint";
                var suite = ArenaWorkbench.Export(input);
                var compiled = ArenaWorkbench.Compile(input).Tournaments.Single();
                check(compiled.Format == preset.Format && compiled.Seed == preset.Seed && compiled.Entrants[0].Ai == "defendPoint"
                    && (string)suite.Element("Tournament").Element("Entrants").Elements("Entrant").First().Attribute("ai") == "defendPoint",
                    "workbench preset exports per-team AI through existing loader: " + preset.Name);
            }
        }
        var rich = ArenaWorkbench.Create();
        rich.Teams[0].Ai = "defendPoint";
        rich.Schedule.Ai = "lure";
        var aiRoundTrip = ArenaWorkbench.Compile(rich).Tournaments.Single();
        check(aiRoundTrip.Entrants[0].Ai == "defendPoint" && aiRoundTrip.Entrants[1].Ai == null
            && (string)XElement.Parse(ArenaLoader.CompileBattle(aiRoundTrip,
                new() { CombatRoster.Resolve(rich.Teams[0].Id, "Colonist", 75, null, 12), CombatRoster.Resolve(rich.Teams[1].Id, "Colonist", 75, null, 10) }, null, 0).Xml)
                .Element("Teams").Elements("Team").First().Attribute("ai") == "defendPoint",
            "team AI override survives XML compilation while unset teams inherit the default");
        ArenaWorkbench.ReadAndChange(rich.Teams[0].Members[0], p => {
            p.Add(new XElement("Traits", new XAttribute("mode", "replace"), new XElement("Set", new XAttribute("def", "Tough"), new XAttribute("degree", 0))));
            p.Add(new XElement("Genes", new XAttribute("mode", "patch"), new XElement("Add", new XAttribute("def", "Robust"), new XAttribute("xenogene", true))));
            p.Add(new XElement("Abilities", new XAttribute("mode", "patch"), new XElement("Ensure", new XAttribute("def", "Stun"))));
            p.Add(new XElement("Hediffs", new XAttribute("mode", "patch"), new XElement("Ensure", new XAttribute("def", "BionicEye"), new XAttribute("severity", .5), new XAttribute("part", "Torso/Neck[0]/Head[0]/Eye[0]"), new XAttribute("durationTicks", 600))));
            p.Add(new XElement("Appearance", new XElement("hairColor", "#112233")));
            p.Add(new XElement("Resources", new XElement("Psyfocus", new XAttribute("value", .7))));
        });
        var roundTrip = ArenaWorkbench.Compile(ArenaWorkbench.Copy(rich)).Tournaments.Single();
        var frozen = XElement.Parse(roundTrip.SquadTemplates["squad_0"].PawnGroups[0]);
        check((string)frozen.Element("Genes").Element("Add").Attribute("xenogene") == "true", "xenogene placement is preserved in generated XML");
        check((string)frozen.Element("Traits").Element("Set").Attribute("degree") == "0", "trait degrees survive draft persistence and export");
        check((string)frozen.Element("Abilities").Element("Ensure").Attribute("def") == "Stun", "abilities reach actual roster input");
        check((string)frozen.Element("Hediffs").Element("Ensure").Attribute("part") == "Torso/Neck[0]/Head[0]/Eye[0]", "precise body part paths survive export");
        check((int)frozen.Element("Hediffs").Element("Ensure").Attribute("durationTicks") == 600, "hediff durations survive export");
        check(frozen.Element("Appearance") != null && frozen.Element("Resources") != null, "non-GUI appearance/resource fields are not lost");
        var bad = ArenaWorkbench.Copy(rich); bad.Schedule.Survival = 10;
        Reject(() => ArenaWorkbench.Compile(bad), "non-100 score totals cannot start");
        bad = ArenaWorkbench.Copy(rich); bad.Teams[0].Members[0].Count = 201;
        Reject(() => ArenaWorkbench.Compile(bad), "oversized custom team is rejected before native generation");
        bad = ArenaWorkbench.Copy(rich); bad.Schedule.Format = "limitChallenge"; bad.Schedule.GrowthTarget = "budget"; bad.Schedule.GrowthCap = 10000;
        Reject(() => ArenaWorkbench.Compile(bad), "budget growth does not silently override a fixed custom squad");
        bad = ArenaWorkbench.Copy(rich); bad.Teams[1].Id = bad.Teams[0].Id;
        Reject(() => ArenaWorkbench.Compile(bad), "duplicate team identities are rejected");
        bad = ArenaWorkbench.Copy(rich); bad.Teams[0].Members[0].PawnXml = "<PawnTemplate id='p'><basePawnKind>Colonist</basePawnKind><Unsupported/></PawnTemplate>";
        Reject(() => ArenaWorkbench.Compile(bad), "GUI input stays subject to strict XML schema");
        Reject(() => ArenaWorkbench.ReadXml("<!DOCTYPE x [<!ENTITY data SYSTEM 'file:///missing'>]><PawnTemplate>&data;</PawnTemplate>"), "workbench XML editor rejects DTD and external entities");
        var library = ArenaWorkbench.ReadXml(ArenaWorkbench.LibraryXml(rich));
        check(library.Element("PawnPresets").Elements().Count() == 2 && library.Element("TeamPresets").Elements().Count() == 1
            && library.Element("SchedulePresets").Elements().Count() == 4, "preset export contains the three independent preset libraries");
        var faction = ArenaWorkbench.Create(); faction.Teams[0].Source = "faction"; faction.Teams[0].Mode = "combatPower";
        var fp = ArenaWorkbench.Compile(faction).Tournaments.Single();
        check(fp.SquadTemplates["squad_0"].Source == "faction" && fp.Entrants[0].Budget == 3000, "faction mixed roster uses existing native budget allocation");
        check(!ArenaWorkbench.PawnTemplate(rich.Teams[0].Members[0], "copied").ToString().Contains("ArenaStudio"), "role XML uses the supported PawnTemplate format");

        var randomDoc = ArenaWorkbench.Create();
        check(randomDoc.Schedule.Seed == -1, "new workbench defaults to a random seed sentinel");
        var requested = ArenaWorkbench.Compile(randomDoc);
        var originalXml = requested.Inputs[0].Xml; var originalHash = requested.Inputs[0].Sha256;
        var frozenRandom = ArenaLoader.FreezeSeeds(requested, () => 8675309);
        check(frozenRandom.Tournaments[0].Seed == 8675309 && frozenRandom.ResolvedSeeds[ArenaWorkbench.TestName] == 8675309,
            "seed=-1 resolves once at invocation freeze");
        check(frozenRandom.Inputs[0].Xml == originalXml && frozenRandom.Inputs[0].Sha256 == originalHash
            && originalXml.Contains("seed=\"-1\""), "seed resolution retains original XML and source hash");
        var recompiledRandom = ArenaLoader.Compile(frozenRandom.Inputs, frozenRandom.SelectedTestDefNames, frozenRandom.ResolvedSeeds);
        check(recompiledRandom.Tournaments[0].Seed == 8675309, "game recompile preserves the CLI-frozen seed");
        var refrozen = ArenaLoader.FreezeSeeds(recompiledRandom, () => throw new InvalidOperationException("must not draw twice"));
        check(refrozen.Tournaments[0].Seed == 8675309, "replays and recompilation cannot reroll a frozen tournament");
        randomDoc.Schedule.Seed = 123;
        check(ArenaLoader.FreezeSeeds(ArenaWorkbench.Compile(randomDoc), () => throw new InvalidOperationException("fixed seed"))
            .Tournaments[0].Seed == 123, "explicit UI and XML seeds remain deterministic");
        Reject(() => ArenaLoader.Compile(frozenRandom.Inputs, frozenRandom.SelectedTestDefNames,
            new System.Collections.Generic.Dictionary<string,int> { [ArenaWorkbench.TestName] = -1 }), "negative resolved seed is rejected");
        var zero = ArenaWorkbench.Create();
        zero.Teams.Add(new ArenaWorkbenchTeam { Name = "empty draft" });
        check(zero.Teams.Last().IsEmpty, "zero-member teams are valid in the editor");
        Reject(() => ArenaWorkbench.ForStorage(zero, true), "explicit save rejects empty draft teams");
        var pruned = ArenaWorkbench.ForStorage(zero, false);
        check(pruned.Teams.Count == 2 && zero.Teams.Count == 3, "auto-save drops empty teams without modifying the live draft");
        check(ArenaWorkbench.Compile(zero).Tournaments[0].Entrants.Count == 2, "zero-member teams are excluded from tournament export");
        zero.Teams[0].Members[0].Count = 0;
        var partial = ArenaWorkbench.Compile(zero).Tournaments[0];
        check(partial.Entrants[0].Count == 4 && partial.SquadTemplates["squad_0"].PawnGroups.Count == 1,
            "zero-sized member groups are excluded while the rest of the team remains valid");
        zero.Teams[0].Members.Clear();
        check(ArenaWorkbench.ForStorage(zero, false).Teams.Count == 1, "legacy saved empty teams are pruned on load");
        var nativeGroup = ArenaWorkbench.Create(); nativeGroup.Teams[0].Source = "faction"; nativeGroup.Teams[0].GroupMakerIndex = 3;
        check(ArenaWorkbench.Compile(nativeGroup).Tournaments[0].SquadTemplates["squad_0"].MakerIndex == 3,
            "selected mod faction group keeps the real pawnGroupMakers index");
        var defaults = new ArenaWorkbenchSchedule { Seed = -1, Width = 900, Survival = 1, Replays = 9, Timeout = 999 };
        ArenaWorkbench.ApplyUiDefaults(defaults);
        check(defaults.Width == 140 && defaults.Survival == 45 && defaults.Replays == 2 && defaults.Timeout == 300 && defaults.Seed == -1,
            "removed schedule fields use fixed defaults without overwriting the user's seed");
        var demand = new ArenaPreviewDemand(0);
        check(!demand.TryBegin(.2), "automatic portraits debounce edits");
        demand.Touch(.31);
        check(demand.TryBegin(.31), "visible stable portraits generate without a refresh button");
        demand.Touch(2);
        check(!demand.TryBegin(2), "failed or successful portrait generation is attempted only once per configuration");
        var stale = new ArenaPreviewDemand(0);
        check(!stale.TryBegin(1), "off-screen and superseded configurations do not generate obsolete portraits");
        var edited = new ArenaPreviewDemand(1); edited.Touch(1.31);
        check(edited.TryBegin(1.31), "a changed pawn configuration schedules a fresh portrait");

        var oldUi = ArenaWorkbench.Create(); oldUi.SchemaVersion = 1; oldUi.Schedule.Seed = 12345;
        ArenaWorkbench.UpgradeUi(oldUi);
        check(oldUi.SchemaVersion == 2 && oldUi.Schedule.Seed == -1, "old default seed migrates to the new random UI default once");
        oldUi.Schedule.Seed = 42; ArenaWorkbench.UpgradeUi(oldUi);
        check(oldUi.Schedule.Seed == 42, "reopening the upgraded UI preserves user-entered fixed seeds");

        void Reject(Action action, string name)
        {
            try { action(); throw new InvalidOperationException("Unexpected accepted input: " + name); }
            catch (Exception ex) when (ex is ScenarioConfigurationException || ex is System.Xml.XmlException) { check(true, name); }
        }
    }
}
