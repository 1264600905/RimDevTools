using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using LogSyncMod.AutoTest.Scenarios;

internal static class SquadTests
{
    internal static void Run(Action<bool, string> check)
    {
        const string template = "<SquadTemplate id='raid' source='faction' factionDef='Pirate'><GroupMaker mode='random' kindDef='Combat'/><Xenotypes mode='inherit'/></SquadTemplate>";
        const string custom = "<SquadTemplate id='raid' source='custom' factionDef='Pirate'><Composition mode='weighted' allocator='native'><Option pawnKind='Mercenary_Gunner' weight='3'/><Option pawnKind='Mercenary_Heavy' weight='1'/></Composition><Xenotypes mode='weighted'><Option def='Baseliner' weight='1'/><Option def='Yttakin' weight='2'/></Xenotypes></SquadTemplate>";
        SquadTemplate Parse(string xml) => SquadTemplates.Parse(XElement.Parse(xml), "squads.xml");
        void Reject(Action action, string name) { try { action(); check(false, name); } catch (ScenarioConfigurationException) { check(true, name); } }
        var random = Parse(template);
        check(random.Source == "faction" && random.GroupKind == "Combat" && random.ReferencePoints == 5000, "squad defaults use native Combat selection with explicit count-reference points");
        var specific = Parse(template.Replace("mode='random'", "mode='specific' index='2' referencePoints='1200'"));
        check(specific.MakerIndex == 2 && specific.ReferencePoints == 1200, "specific squad maker index and count-reference points survive freezing");
        var weighted = Parse(custom);
        check(weighted.Options.Count == 2 && weighted.Options[0].Weight == 3 && weighted.Xenotypes[1].Def == "Yttakin", "custom squad preserves weighted kind and xenotype pools");
        var fixedSquad = Parse(custom.Replace("mode='weighted' allocator='native'", "mode='fixed'").Replace("<Option pawnKind='Mercenary_Gunner' weight='3'/>", "<Member pawnKind='Mercenary_Gunner' count='3'/>").Replace("<Option pawnKind='Mercenary_Heavy' weight='1'/>", "<Member pawnKind='Mercenary_Heavy' count='1'/>"));
        check(fixedSquad.Options.Sum(o => o.Count) == 4, "fixed composition records exact per-kind headcounts");
        var largeFixedXml = "<SquadTemplate id='large' source='custom' factionDef='Pirate'><Composition mode='fixed'><Member pawnKind='Mercenary_Gunner' count='200'/></Composition></SquadTemplate>";
        check(Parse(largeFixedXml).Options.Single().Count == 200, "fixed mixed squad accepts exactly 200 members");
        Reject(() => Parse(largeFixedXml.Replace("count='200'", "count='201'")), "fixed mixed squad rejects more than 200 members");
        foreach (var invalid in new[] {
            template.Replace("mode='random'", "mode='specific'"), template.Replace("mode='random'", "mode='random' index='0'"),
            template.Replace("mode='random'", "mode='specific' index='0' fingerprint='bad'"), template.Replace("kindDef='Combat'", "kindDef='Trader'"),
            template.Replace("mode='random'", "mode='random' referencePoints='NaN'"), template.Replace("<Xenotypes mode='inherit'/>", "<Xenotypes mode='weighted'/>") ,
            template.Replace("<Xenotypes mode='inherit'/>", "<Xenotypes mode='fixed' def='Yttakin'><Option def='Baseliner' weight='1'/></Xenotypes>"),
            template.Replace("<Xenotypes mode='inherit'/>", "<Xenotypes mode='inherit' def='Yttakin'/>") ,
            template.Replace("source='faction'", "source='custom'"), custom.Replace("weight='3'", "weight='0'"),
            custom.Replace("pawnKind='Mercenary_Heavy'", "pawnKind='Mercenary_Gunner'"), custom.Replace("def='Yttakin'", "def='Baseliner'"),
            custom.Replace("allocator='native'", "allocator='typo'"), custom.Replace("source='custom'", "source='typo'"),
            template.Replace("source='faction'", "source='faction' typo='true'") })
            Reject(() => Parse(invalid), "squad strict schema rejects incompatible, ambiguous or invalid configuration");
        var generator = Parse("<SquadTemplate id='g' source='generator' factionDef='Pirate'><Generator id='extension.example'><Param name='limit' value='4'/></Generator></SquadTemplate>");
        check(generator.GeneratorId == "extension.example" && generator.Parameters["limit"] == "4", "squad extension generator freezes ID and typed-by-provider parameters without CLR type resolution");
        const string arena = "<ArenaSuite schemaVersion='1' id='suite'><SquadTemplates>TEMPLATE</SquadTemplates><Tournament id='main'><testDefName>SquadArena</testDefName><Scene type='mainMap' sizeX='100' sizeZ='100'/><Roster mode='count' count='8' costMode='native'/><Entrants><Entrant id='a' pawnKind='Wolf_Timber'/><Entrant id='b' squad='raid' count='12'/></Entrants></Tournament></ArenaSuite>";
        ArenaPlan Arena(string xml) => ArenaLoader.Compile(new[] { new ScenarioInput { Path = "squad-arena.xml", Sha256 = "h", Xml = xml } }, new[] { "SquadArena" });
        var plan = Arena(arena.Replace("TEMPLATE", template));
        check(plan.Tournaments[0].Count == 8 && plan.Tournaments[0].Entrants[1].Count == 12 && plan.Tournaments[0].CostMode == "native", "arena squads support total count budget and per-entrant count override");
        check(plan.Tournaments[0].SquadTemplates["raid"].FactionDef == "Pirate", "arena freezes referenced squad definitions in the tournament plan");
        Reject(() => Arena(arena.Replace("TEMPLATE", template).Replace("squad='raid'", "squad='missing'")), "arena rejects missing squad references before launching");
        Reject(() => Arena(arena.Replace("TEMPLATE", template).Replace("squad='raid'", "squad='raid' pawnKind='Wolf_Timber'")), "arena rejects simultaneous squad and pawnKind selectors");
        Reject(() => Arena(arena.Replace("TEMPLATE", template).Replace("squad='raid'", "squad='raid' xenotype='Yttakin'")), "arena requires squad xenotype policy rather than conflicting entrant overrides");
        Reject(() => Arena(arena.Replace("TEMPLATE", template).Replace("costMode='native'", "costMode='typo'")), "arena rejects unknown combat-power cost basis");
        var cp = Arena(arena.Replace("TEMPLATE", template).Replace("mode='count' count='8'", "mode='combatPower' budget='5000'").Replace("count='12'", "budget='3200'"));
        check(cp.Tournaments[0].Budget == 5000 && cp.Tournaments[0].Entrants[1].Budget == 3200, "mixed squad and single-kind arena retain CP budgets and per-entrant overrides");
        const string group = "<TestSuite schemaVersion='1' id='group'><SquadTemplates>TEMPLATE</SquadTemplates><Case id='g' kind='groupCombat'><testDefName>SquadGroup</testDefName><Scene type='mainMap' sizeX='100' sizeZ='100'/><Teams><Team id='A'/><Team id='B'/></Teams><Actors><PawnGroup id='a' team='A' squad='raid' count='12' originX='10' originZ='10' formation='grid' columns='6'/><PawnGroup id='b' team='B' count='2' originX='70' originZ='10'><basePawnKind>Wolf_Timber</basePawnKind></PawnGroup></Actors><GroupCombat/></Case></TestSuite>";
        ScenarioPlan Group(string xml) => ScenarioLoader.Compile(new[] { new ScenarioInput { Path = "squad-group.xml", Sha256 = "h", Xml = xml } }, true);
        var g = Group(group.Replace("TEMPLATE", template)); var frozen = XElement.Parse(g.Cases[0].Xml).Element("Actors").Element("PawnGroup");
        check(frozen.Element("SquadTemplate") != null && ScenarioLoader.I(frozen, "count") == 12, "ordinary group combat freezes the same squad template with an exact member budget");
        check(Group(group.Replace("TEMPLATE", custom).Replace("squad='raid' count='12'", "squad='raid' combatPowerBudget='5000' costMode='native'")).Cases.Count == 1, "ordinary group combat supports CP budgets for custom weighted squads");
        Reject(() => Group(group.Replace("TEMPLATE", template).Replace("squad='raid' count='12'", "squad='raid'")), "group squad requires an explicit count or CP budget");
        Reject(() => Group(group.Replace("TEMPLATE", template).Replace("squad='raid' count='12'", "squad='raid' count='12' combatPowerBudget='5000'")), "group squad forbids mixing count and CP budgets");
        Reject(() => Group(group.Replace("TEMPLATE", template).Replace("squad='raid' count='12'", "squad='missing' count='12'")), "group squad rejects unresolved references");
        Reject(() => SquadTemplates.ParseAll(XElement.Parse("<ArenaSuite><SquadTemplates>" + template + template + "</SquadTemplates></ArenaSuite>"), "duplicate.xml"), "squad template IDs must be unique");
        var member = new ResolvedSquadMember { PawnKind = "Mercenary_Gunner", Xenotype = "Yttakin", BaseCombatPower = 100, XenotypeFactor = 1.5m, Cost = 150,
            PawnTemplateXml = "<PawnTemplate id='p'><basePawnKind>Mercenary_Gunner</basePawnKind><Xenotype def='Yttakin'/><Apparel mode='replace'/><Equipment mode='replace'/></PawnTemplate>" };
        var roster = new ResolvedCombatRoster { Id = "r", SquadId = "raid", FactionDef = "Pirate", CostMode = "native", Mode = "count", PawnKind = "Mercenary_Gunner", ResolvedCount = 1, UnitCombatPower = 150, Members = new() { member } };
        check(SquadTemplates.ValidateRoster(roster, weighted, null, 1, "native") == null, "squad integrity accepts exact headcount with native xenotype-adjusted costs");
        check(SquadTemplates.ValidateRoster(roster, weighted, null, 2, "native") != null, "squad integrity rejects a forged member budget");
        member.Cost = 100; roster.UnitCombatPower = 100;
        check(SquadTemplates.ValidateRoster(roster, weighted, null, 1, "native") != null, "native squad costs cannot omit the xenotype factor");
        roster.CostMode = "pawnKind";
        check(SquadTemplates.ValidateRoster(roster, weighted, null, 1, "pawnKind") == null, "base CP cost mode explicitly excludes xenotype multiplier");
        roster.Mode = "combatPower"; roster.RequestedBudget = 99;
        check(SquadTemplates.ValidateRoster(roster, weighted, 99, 1, "pawnKind") != null, "squad integrity rejects a CP budget overspend");
        roster.Mode = "count"; roster.RequestedBudget = null;
        var snapshot = TemporaryPawnData.Serialize(TemporaryPawnData.Parse(member.PawnTemplateXml));
        var unit = new GroupCombatUnit { Id = "left[0]", Kind = member.PawnKind, InitialSnapshot = snapshot };
        check(SquadTemplates.ValidateUnits(roster, new[] { unit }, "left") == null, "actual squad member kind, xenotype and outfit are matched by stable member ID");
        unit.InitialSnapshot = TemporaryPawnData.Serialize(TemporaryPawnData.Parse(member.PawnTemplateXml.Replace("Yttakin", "Baseliner")));
        check(SquadTemplates.ValidateUnits(roster, new[] { unit }, "left") != null, "squad integrity rejects a generated pawn with the wrong xenotype");
        unit.InitialSnapshot = TemporaryPawnData.Serialize(TemporaryPawnData.Parse(member.PawnTemplateXml.Replace("<Equipment mode='replace'/>", "<Equipment mode='replace'><SetPrimary def='Gun_Autopistol'/></Equipment>")));
        check(SquadTemplates.ValidateUnits(roster, new[] { unit }, "left") != null, "squad integrity rejects a generated pawn with an unfrozen weapon");
        check(SquadTemplates.MemberSeed(123, "a", 0) == SquadTemplates.MemberSeed(123, "a", 0) && SquadTemplates.MemberSeed(123, "a", 0) != SquadTemplates.MemberSeed(123, "b", 0), "squad member seeds are deterministic and independent of entrant ordering");
        var fixturePath = Path.Combine("AutoTest.Core.Tests", "Fixtures", "GroupCombatSquads.autotest.xml");
        var fixture = ScenarioLoader.Compile(new[] { new ScenarioInput { Path = fixturePath, Sha256 = "fixture", Xml = File.ReadAllText(fixturePath) } }, true);
        ScenarioLoader.SelectRules(fixture, fixture.Cases.Select(c => c.TestDefName).ToArray());
        check(fixture.Cases.Count == 3 && GroupCombatPlan.Expand(fixture).Count == 6, "mixed squad fixture expands three inherited cases into six swapped battles");
        var fixedActor = XElement.Parse(fixture.Cases.Single(c => c.TestDefName == "GroupCombat_SquadsFixedCount").Xml)
            .Element("Actors").Elements("PawnGroup").Single(e => (string)e.Attribute("id") == "pirates");
        check((string)fixedActor.Attribute("team") == "B" && (string)fixedActor.Attribute("count") == "4"
            && (string)fixedActor.Element("SquadTemplate").Attribute("id") == "fixedRaid", "inherited fixed squad replaces its template and count while preserving team and formation");
    }
}
