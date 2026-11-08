using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;
using LogSyncMod.AutoTest.Scenarios;

internal static class ArenaTests
{
    internal static void Run(Action<bool, string> check)
    {
        void Reject(Action action, string name)
        {
            try { action(); check(false, name); }
            catch (Exception ex) when (ex is ScenarioConfigurationException or FormatException or OverflowException or System.Xml.XmlException) { check(true, name); }
        }
        var budget = CombatRoster.Resolve("wolf", "Wolf", 85, 3000);
        check(budget.ResolvedCount == 35 && budget.ActualCombatPower == 2975 && budget.UnusedBudget == 25, "budget resolves maximal affordable count and preserves unused combatPower");
        check(CombatRoster.Resolve("r", "K", .1m, .3m).ResolvedCount == 3 && CombatRoster.Power(.1f) == .1m, "budget arithmetic uses loaded float round-trip decimals at exact boundaries");
        check(CombatRoster.Resolve("r", "K", .3m, .899999999999999999m).ResolvedCount == 2, "budget arithmetic never uses tolerance to create an unaffordable pawn");
        Reject(() => CombatRoster.Resolve("r", "K", 100, 99), "budget rejects zero affordable units");
        Reject(() => CombatRoster.Resolve("r", "K", 1, 101), "budget rejects excessive rosters without clamping");
        Reject(() => CombatRoster.Power(float.NaN), "allocation rejects NaN combatPower");
        check(CombatRoster.Resolve("r", "K", 100, 3000).ResolvedCount == 30 && CombatRoster.Resolve("r", "K", 3000, 3000).ResolvedCount == 1, "exact budget division covers CP 100 and CP equal to full budget");
        Reject(() => CombatRoster.Resolve("r", "K", .1m, decimal.MaxValue), "huge budget arithmetic overflow fails before allocating a roster");
        Reject(() => CombatRoster.Resolve("r", "K", decimal.MaxValue, null, 2), "count-mode total combatPower overflow fails explicitly");
        check(CombatRoster.Resolve("r", "K", decimal.MaxValue, decimal.MaxValue).ResolvedCount == 1, "maximality check treats overflowing next-unit cost as unaffordable without rejecting a valid roster");
        Reject(() => CombatRoster.Power(float.PositiveInfinity), "arena rejects infinite loaded combatPower");
        Reject(() => CombatRoster.Power(-10), "arena rejects negative loaded combatPower");
        Reject(() => CombatRoster.Positive("-1"), "budget rejects negative points");
        Reject(() => CombatRoster.Power(0), "arena rejects zero combatPower even in count mode");
        Reject(() => CombatRoster.Positive("Infinity"), "budget rejects non-finite input");
        const string groupXml = "<TestSuite schemaVersion='1' id='budget'><Case id='r' kind='groupCombat'><testDefName>Budget</testDefName><Scene type='mainMap' sizeX='100' sizeZ='100'/><Teams><Team id='A'/><Team id='B'/></Teams><Actors><PawnGroup id='a' team='A' combatPowerBudget='3000' originX='10' originZ='20' formation='grid' columns='10'><basePawnKind>Wolf_Timber</basePawnKind></PawnGroup><PawnGroup id='b' team='B' count='2' originX='60' originZ='20'><basePawnKind>Mercenary_Gunner</basePawnKind></PawnGroup></Actors><GroupCombat/></Case></TestSuite>";
        ScenarioPlan Group(string text) => ScenarioLoader.Compile(new[] { new ScenarioInput { Path = "group.xml", Sha256 = "h", Xml = text } }, true);
        var groupPlan = Group(groupXml);
        var resolvedCase = XElement.Parse(groupPlan.Cases.Single().Xml);
        var loadedReads = new List<string>();
        var resolvedGroups = CombatRoster.ResolveGroups(resolvedCase, kind => { loadedReads.Add(kind); return 75; }, "source");
        check(resolvedGroups.Single().ResolvedCount == 40 && ScenarioLoader.I(resolvedCase.Element("Actors").Element("PawnGroup"), "count") == 40 && loadedReads.SequenceEqual(new[] { "Wolf_Timber" }), "group budget resolves only budget actors from loaded defs, preserving count-only eligibility");
        Reject(() => Group(groupXml.Replace("combatPowerBudget='3000'", "combatPowerBudget='3000' count='2'")), "group strict parser rejects count and combatPowerBudget together");
        Reject(() => Group(groupXml.Replace("combatPowerBudget='3000'", "combatPowerBudget='NaN'")), "group strict parser rejects non-finite combatPower budgets");
        void ResolveAndValidate(string text, float power)
        {
            var original = Group(text); var xmlCase = XElement.Parse(original.Cases.Single().Xml);
            CombatRoster.ResolveGroups(xmlCase, _ => power, "source"); xmlCase.Attribute("packageId")?.Remove();
            var suite = new XElement("TestSuite", new XAttribute("schemaVersion", 1), new XAttribute("id", "resolved"), xmlCase);
            Group(suite.ToString(SaveOptions.DisableFormatting));
        }
        Reject(() => ResolveAndValidate(groupXml.Replace("originX='10'", "originX='95'"), 75), "resolved budget formations are revalidated against map edges before spawning");
        Reject(() => ResolveAndValidate(groupXml, 20), "resolved group budget refuses more than 100 units before spawning");
        check(Group(groupXml.Replace("combatPowerBudget='3000'", "")).Cases.Count == 1, "group count default remains one when count and budget are both absent");
        const string xml = "<ArenaSuite schemaVersion='1' id='suite'><Tournament id='main'><testDefName>ArenaTest</testDefName><Scene type='mainMap' sizeX='100' sizeZ='100'/><Roster mode='combatPower' budget='3000'/><Entrants><Entrant id='a' pawnKind='Wolf_Timber'/><Entrant id='b' pawnKind='Mercenary_Gunner'/></Entrants></Tournament></ArenaSuite>";
        ArenaPlan Parse(string text) => ArenaLoader.Compile(new[] { new ScenarioInput { Path = "arena.xml", Sha256 = "h", Xml = text } }, new[] { "ArenaTest" });
        var plan = Parse(xml);
        check(Math.Abs(ArenaShowcase.OverallArmor(.6, new[] { (1d, Array.Empty<double>()) }) - .6) < .00001,
            "overall armor preserves unarmored natural protection");
        check(Math.Abs(ArenaShowcase.OverallArmor(0, new[] { (.5, new[] { 1d }), (.5, Array.Empty<double>()) }) - .5) < .00001,
            "overall armor includes apparel only on covered body parts");
        check(Math.Abs(ArenaShowcase.OverallArmor(0, new[] { (1d, new[] { 1d, 1d }) }) - 1.5) < .00001
            && Math.Abs(ArenaShowcase.OverallArmor(.6, new[] { (1d, new[] { 1d }) }) - 1.3) < .00001,
            "overall armor combines overlapping apparel and natural protection as native gear UI does");
        check(ArenaShowcase.OverallArmor(0, new[] { (1d, new[] { 5d }) }) == 2,
            "overall armor caps each layer and summary at native two-hundred-percent limit");
        ArenaTournamentInput WithShowcase(string content) => Parse(xml.Replace("</Tournament>", "<Presentation>" + content + "</Presentation></Tournament>")).Tournaments.Single();
        var customShowcase = WithShowcase("<Showcase pageSeconds='7'><Page seconds='2.5' caption='先看后排！'><Entrant ref='b'/></Page></Showcase>");
        check(customShowcase.ShowcasePages.Count == 2 && customShowcase.ShowcasePages[0].EntrantIds.SequenceEqual(new[] { "b" })
            && customShowcase.ShowcasePages[0].Seconds == 2.5 && customShowcase.ShowcasePages[0].Caption == "先看后排！"
            && customShowcase.ShowcasePages[1].EntrantIds.SequenceEqual(new[] { "a" }) && customShowcase.ShowcasePages[1].Seconds == 7
            && customShowcase.Entrants.Select(e => e.Id).SequenceEqual(new[] { "a", "b" }),
            "XML showcase freezes explicit order, duration and caption, appends omitted entrants and preserves tournament order");
        check(plan.Tournaments[0].ShowcasePages.Single().EntrantIds.SequenceEqual(new[] { "a", "b" })
            && plan.Tournaments[0].ShowcasePages[0].Seconds == 5 && WithShowcase("<Showcase pageSeconds='8'/>").ShowcasePages[0].Seconds == 8,
            "missing XML showcase preserves five-second pages while a global duration applies without explicit pages");
        var quoted = WithShowcase("<Showcase><Page caption='勇气 &amp; 力量 &lt;并肩&gt;'><Entrant ref='a'/><Entrant ref='b'/></Page></Showcase>");
        check(quoted.ShowcasePages.Single().Caption == "勇气 & 力量 <并肩>", "XML page captions preserve escaped literal characters");
        check(WithShowcase("<Showcase><Page><Entrant ref='b'/></Page><Page><Entrant ref='a'/></Page></Showcase>").ShowcasePages.Count == 2,
            "showcase supports multiple ordered pages with default durations");
        foreach (var invalid in new[] { "0", "-1", "0.5", "61", "NaN", "Infinity" })
        {
            Reject(() => WithShowcase("<Showcase pageSeconds='" + invalid + "'/>"), "XML rejects invalid showcase default duration: " + invalid);
            Reject(() => WithShowcase("<Showcase><Page seconds='" + invalid + "'><Entrant ref='a'/></Page></Showcase>"), "XML rejects invalid showcase page duration: " + invalid);
        }
        foreach (var bad in new[] {
            "<Showcase><Page/></Showcase>",
            "<Showcase><Page><Entrant ref='missing'/></Page></Showcase>",
            "<Showcase><Page><Entrant ref='a'/><Entrant ref='a'/></Page></Showcase>",
            "<Showcase><Page><Entrant ref='a'/></Page><Page><Entrant ref='a'/></Page></Showcase>",
            "<Showcase><Page><Entrant/></Page></Showcase>",
            "<Showcase typo='1'/>",
            "<Showcase><Page typo='1'><Entrant ref='a'/></Page></Showcase>",
            "<Showcase><Page><Entrant ref='a' typo='1'/></Page></Showcase>",
            "<Showcase><Page caption='第一句&#10;第二句'><Entrant ref='a'/></Page></Showcase>",
            "<Showcase><Page caption='" + new string('字', 81) + "'><Entrant ref='a'/></Page></Showcase>",
            "<Showcase/><Showcase/>"
        }) Reject(() => WithShowcase(bad), "XML showcase rejects invalid page references, copy or schema: " + bad);
        var manyEntrants = xml.Replace("</Entrants>", string.Concat(Enumerable.Range(0, 4).Select(i => "<Entrant id='extra" + i + "' pawnKind='ExtraKind" + i + "'/>")) + "</Entrants>");
        var sixRefs = "<Page><Entrant ref='a'/><Entrant ref='b'/>" + string.Concat(Enumerable.Range(0, 4).Select(i => "<Entrant ref='extra" + i + "'/>")) + "</Page>";
        Reject(() => Parse(manyEntrants.Replace("</Tournament>", "<Presentation><Showcase>" + sixRefs + "</Showcase></Presentation></Tournament>")), "XML rejects more than five entrants on one showcase page");
        check(Parse(manyEntrants).Tournaments[0].ShowcasePages.Select(p => p.EntrantIds.Count).SequenceEqual(new[] { 5, 1 }),
            "XML omitted page plan automatically partitions a partial last page");
        var customPlan = Parse(xml.Replace("</Tournament>", "<Presentation><Showcase pageSeconds='60'><Page><Entrant ref='a'/></Page><Page><Entrant ref='b'/></Page></Showcase></Presentation></Tournament>"));
        check(customPlan.DefaultDeadlineSeconds - plan.DefaultDeadlineSeconds == 115, "arena total deadline includes the sum of configured showcase page durations");
        check(ArenaShowcase.PageCount(2) == 1 && ArenaShowcase.PageCount(5) == 1 && ArenaShowcase.PageCount(6) == 2
            && ArenaShowcase.PageCount(25) == 5 && ArenaShowcase.PageCount(64) == 13 && ArenaShowcase.Duration(25) == 25,
            "entrant showcase displays at most five entrants per page for five seconds, including the partial final page");
        check(ArenaShowcase.Fill(10, 40) == .25 && ArenaShowcase.Fill(40, 40) == 1 && ArenaShowcase.Fill(0, 40) == 0
            && ArenaShowcase.Fill(0, 0) == 0 && ArenaShowcase.Fill(-1, 40) == 0 && ArenaShowcase.Fill(50, 40) == 1
            && ArenaShowcase.Fill(double.NaN, 40) == 0,
            "entrant bars share a tournament-wide scale and safely handle zero armor and unavailable metrics");
        check(new[] { 0d, .199999, .2, .399999, .4, .599999, .6, .799999, .8, .949999, .95, 1 }
                .Select(ArenaShowcase.ProgressTier).SequenceEqual(new[] { 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5 })
            && ArenaShowcase.ProgressTier(double.NaN) == 0,
            "showcase bars use six consistent rarity tiers, promoting at each threshold and handling zero metrics");
        check(ArenaShowcase.ProgressTier(ArenaShowcase.Fill(50, 100)) == ArenaShowcase.ProgressTier(ArenaShowcase.Fill(5, 10))
            && ArenaShowcase.ProgressTier(ArenaShowcase.Fill(47, 62)) == ArenaShowcase.ProgressTier(ArenaShowcase.Fill(470, 620)),
            "the same relative progress always receives the same tier across entrants and metric scales");
        check(plan.Purpose == "arena" && plan.Tournaments[0].Budget == 3000 && plan.Tournaments[0].MaxReplays == 2 && plan.DefaultDeadlineSeconds > 600, "arena has independent parser, frozen defaults and bounded tournament deadline");
        check(plan.Tournaments[0].BracketSeconds == 3 && plan.Tournaments[0].AdvanceStartSeconds == 3,
            "arena defaults to a three-second countdown and prepares its start from the beginning");
        foreach (var lead in new[] { 0, 1, 4, 60 })
            check(Parse(xml.Replace("</Tournament>", "<Presentation advanceStartSeconds='" + lead + "'/></Tournament>")).Tournaments[0].AdvanceStartSeconds == lead,
                "arena freezes an explicit start preparation lead: " + lead);
        foreach (var invalid in new[] { "-1", "61", "NaN", "Infinity" })
            Reject(() => Parse(xml.Replace("</Tournament>", "<Presentation advanceStartSeconds='" + invalid + "'/></Tournament>")), "arena rejects invalid start preparation lead: " + invalid);
        check(ArenaPresentation.CountdownLabel(0, 3) == "3" && ArenaPresentation.CountdownLabel(1, 3) == "2"
            && ArenaPresentation.CountdownLabel(2.99, 3) == "1" && ArenaPresentation.CountdownLabel(3, 3) == "开始",
            "arena countdown runs 3, 2, 1, Start without a hidden hold");
        check(ArenaPresentation.CapturePreparationElapsed(5, 3) == 2 && ArenaPresentation.CapturePreparationElapsed(5, 0) == 5
            && ArenaPresentation.CapturePreparationElapsed(3, 3) == 0 && ArenaPresentation.CapturePreparationElapsed(1, 3) == 0,
            "arena starts preparation during countdown, supports disabled overlap and clamps long leads to countdown start");
        var mixedBudget = Parse(xml.Replace("id='a' pawnKind", "id='a' budget='3200' pawnKind")).Tournaments.Single();
        check(mixedBudget.Budget == 3000 && mixedBudget.Entrants[0].Budget == 3200 && mixedBudget.Entrants[1].Budget == null
            && CombatRoster.Resolve("a", "AlphaThrumbo", 800, mixedBudget.Entrants[0].Budget ?? mixedBudget.Budget).ResolvedCount == 4
            && CombatRoster.Resolve("b", "Thrumbo", 500, mixedBudget.Entrants[1].Budget ?? mixedBudget.Budget).ResolvedCount == 6,
            "arena per-entrant budget overrides allocate four 800CP units while other entrants inherit 3000CP");
        foreach (var invalidBudget in new[] { "0", "-1", "NaN", "Infinity", "" })
            Reject(() => Parse(xml.Replace("id='a' pawnKind", "id='a' budget='" + invalidBudget + "' pawnKind")), "arena rejects invalid entrant budget: " + invalidBudget);
        Reject(() => Parse(xml.Replace("mode='combatPower' budget='3000'", "mode='count' count='20'").Replace("id='a' pawnKind", "id='a' budget='3200' pawnKind")), "arena count mode rejects entrant budget overrides");
        check(Parse(xml.Replace("mode='combatPower' budget='3000'", "mode='count' count='20'").Replace("id='a' pawnKind", "id='a' count='3' pawnKind")).Tournaments[0].Entrants[0].Count == 3, "arena count mode supports per-entrant counts");
        Reject(() => Parse(xml.Replace("id='a' pawnKind", "id='a' count='3' pawnKind")), "arena budget mode rejects count overrides");
        Reject(() => Parse(xml.Replace("Mercenary_Gunner", "Wolf_Timber")), "arena rejects duplicate PawnKinds");
        Reject(() => Parse(xml.Replace("id='b'", "id='a'")), "arena rejects duplicate entrant IDs");
        Reject(() => Parse(xml.Replace("<Entrants>", "<Entrants typo='true'>")), "arena strict schema rejects unknown fields");
        Reject(() => Parse(xml.Replace("</Tournament>", "<Battle durationSeconds='NaN'/></Tournament>")), "arena validates finite simulated durations");
        Reject(() => Parse(xml.Replace("</Tournament>", "<Scoring timeReferenceSeconds='0'/></Tournament>")), "arena rejects zero scoring time reference");
        check(Parse(xml.Replace("</Tournament>", "<Scoring survivalWeight='40' suppressionWeight='30' exchangeWeight='20' speedWeight='10'/></Tournament>")).Tournaments[0].SuppressionWeight == 30,
            "arena accepts four configurable weights totaling one hundred");
        foreach (var badScore in new[] { "survivalWeight='100' speedWeight='20'", "exchangeWeight='-1'", "suppressionWeight='101'", "speedWeight='NaN'", "exchangeWeight='0'" })
            Reject(() => Parse(xml.Replace("</Tournament>", "<Scoring " + badScore + "/></Tournament>")), "arena rejects invalid or legacy non-percent scoring weights: " + badScore);
        Reject(() => Parse(xml.Replace("</Tournament>", "<Output width='1920'/></Tournament>")), "arena requires paired output dimensions");
        Reject(() => Parse("<!DOCTYPE ArenaSuite [<!ENTITY x SYSTEM 'file:///C:/Windows/win.ini'>]>" + xml), "arena disables DTD and external entity resolution");
        Reject(() => ScenarioLoader.Compile(plan.Inputs), "ordinary regression entry refuses ArenaSuite");
        var input = plan.Tournaments.Single();
        var verticalRosters = new List<ResolvedCombatRoster> { CombatRoster.Resolve("a", "Wolf_Timber", 100, null, 27), CombatRoster.Resolve("b", "Bear_Polar", 200, null, 3) };
        var verticalBattle = XElement.Parse(ArenaLoader.CompileBattle(input, verticalRosters, null, 0).Xml);
        var verticalActors = verticalBattle.Element("Actors").Elements("PawnGroup").ToList();
        check(PawnGroupFormation.Extents(verticalActors[0], true) == (4, 18)
            && PawnGroupFormation.Extents(verticalActors[1], true) == (0, 4)
            && verticalActors.All(a => ScenarioLoader.I(a, "originZ") + PawnGroupFormation.Extents(a, true).z / 2 == 50),
            "arena centers both full and partial vertical formations using the same geometry as group combat");
        GroupCombatRoundResult ScoreBattle()
        {
            var result = new GroupCombatRoundResult { ElapsedTicks = 2700, EndReason = "timeLimitRatio", MeasurementRequirementsMet = true,
                Teams = new() { new() { Id = "A", Outcome = "win", Initial = 20, EnemyDamage = 300, DamageTaken = 100 }, new() { Id = "B", Outcome = "loss", Initial = 10 } } };
            result.Units.AddRange(Enumerable.Range(0, 20).Select(i => new GroupCombatUnit { Id = "A" + i, Team = "A", State = i < 15 ? "standing" : i < 18 ? "downed" : "dead" }));
            result.Units.AddRange(Enumerable.Range(0, 10).Select(i => new GroupCombatUnit { Id = "B" + i, Team = "B", State = i < 2 ? "standing" : "downed" }));
            return result;
        }
        var scoredBattle = ScoreBattle(); var score = ArenaScore.Calculate(input, scoredBattle);
        check(score.Value == 77.9375m && score.PreservationPoints == 35.4375m && score.SuppressionPoints == 20 && score.ExchangePoints == 15 && score.SpeedPoints == 7.5m
            && score.IsRankable && score.Algorithm == "victory-quality-v2", "percent score independently accounts for original survivors, downed quarter credit, suppression, damage exchange and speed");
        scoredBattle.Units.Add(new GroupCombatUnit { Id = "summonA", Team = "A", JoinedDuringBattle = true });
        scoredBattle.Units.Add(new GroupCombatUnit { Id = "summonB", Team = "B", JoinedDuringBattle = true });
        check(ArenaScore.Calculate(input, scoredBattle).Value == score.Value, "reinforcements do not inflate original preservation or reduce original enemy suppression");
        var roundTrip = ScoreBattle(); roundTrip.Teams[0].EnemyDamage = 249.8938446044921875; roundTrip.Teams[0].DamageTaken = 40.712345678912345;
        var canonicalScore = ArenaScore.Calculate(input, roundTrip);
        roundTrip.Teams[0].EnemyDamage = double.Parse(roundTrip.Teams[0].EnemyDamage.ToString("G15", System.Globalization.CultureInfo.InvariantCulture), System.Globalization.CultureInfo.InvariantCulture);
        roundTrip.Teams[0].DamageTaken = double.Parse(roundTrip.Teams[0].DamageTaken.ToString("G15", System.Globalization.CultureInfo.InvariantCulture), System.Globalization.CultureInfo.InvariantCulture);
        check(ArenaScore.Calculate(input, roundTrip).Value == canonicalScore.Value, "game JSON precision round trip cannot alter unrounded score or extra-match ordering");
        scoredBattle.Teams[0].DamageTaken = 300;
        check(ArenaScore.Calculate(input, scoredBattle).ExchangePoints == 10, "exchange uses the entire team damage ledger, including all recorded incoming friendly, self and environmental damage");
        scoredBattle.Teams[0].EnemyDamage = scoredBattle.Teams[0].DamageTaken = 0;
        check(ArenaScore.Calculate(input, scoredBattle).ExchangePoints == 10 && ArenaScore.Calculate(input, scoredBattle).NeutralExchange, "zero recorded damage on both sides receives an explicit neutral exchange score");
        scoredBattle.Teams[0].EnemyDamage = scoredBattle.Teams[0].DamageTaken = double.MaxValue;
        check(ArenaScore.Calculate(input, scoredBattle).ExchangePoints == 10, "finite extreme damage sums cannot overflow the exchange ratio");
        scoredBattle.Teams[0].DamageTaken = 0;
        check(ArenaScore.Calculate(input, scoredBattle).ExchangePoints == 20, "damage with no recorded cost earns the exchange maximum");
        scoredBattle.Teams[0].EnemyDamage = double.NaN;
        Reject(() => ArenaScore.Calculate(input, scoredBattle), "invalid damage cannot produce a rankable score");
        var confirmation = ScoreBattle(); confirmation.EndReason = "allUnavailable";
        foreach (var u in confirmation.Units.Where(u => u.Team == "B")) u.State = "downed";
        confirmation.Teams[1].EliminatedAtTick = 2400;
        var beforeWait = ArenaScore.Calculate(input, confirmation); confirmation.ElapsedTicks += 600;
        check(ArenaScore.Calculate(input, confirmation).Value == beforeWait.Value && beforeWait.EffectiveElapsedTicks == 2400,
            "changing the final defeat-confirmation wait cannot change victory quality");
        confirmation.EndReason = "timeLimitRatio";
        check(ArenaScore.Calculate(input, confirmation).EffectiveElapsedTicks == confirmation.ElapsedTicks, "time-limit wins retain their full combat clock even if enemy elimination history exists");
        confirmation.ElapsedTicks = 12000;
        foreach (var u in confirmation.Units.Where(u => u.Team == "A")) u.State = "dead";
        confirmation.Units.Add(new GroupCombatUnit { Id = "summon", Team = "A", JoinedDuringBattle = true });
        var summonScore = ArenaScore.Calculate(input, confirmation);
        check(summonScore.PreservationPoints == 0 && summonScore.SpeedPoints == 0 && summonScore.Value >= 0 && summonScore.Value <= 100,
            "summon-only victory grants no original survival points and late victories no speed points");
        var perfect = ScoreBattle(); perfect.ElapsedTicks = 0;
        foreach (var u in perfect.Units) u.State = u.Team == "A" ? "standing" : "dead";
        perfect.Teams[0].DamageTaken = 0;
        check(ArenaScore.Calculate(input, perfect).Value == 100, "perfect victory is capped at exactly one hundred");
        foreach (var quality in new[] { "partial", "complete", "incomplete", "unknown" })
        {
            perfect.MeasurementQuality = quality;
            check(ArenaScore.Calculate(input, perfect).IsRankable == (quality is "partial" or "complete"), "rankability reflects the recorded damage quality: " + quality);
        }
        perfect.MeasurementQuality = "partial"; perfect.MeasurementRequirementsMet = false;
        check(!ArenaScore.Calculate(input, perfect).IsRankable, "failed damage measurement produces only a provisional score");
        var unrankableScheduler = new ArenaScheduler(Tournament(input, 2)); unrankableScheduler.Next();
        try { unrankableScheduler.Resolve(unrankableScheduler.Active.Left, ArenaScore.Calculate(input, perfect)); check(false, "unrankable scores block tournament advancement"); }
        catch (InvalidOperationException) { check(true, "unrankable scores block tournament advancement"); }
        for (int n = 2; n <= 64; n++)
        {
            var tournament = Tournament(input, n); var schedule = new ArenaScheduler(tournament);
            var eliminated = new HashSet<string>(); var pairs = new HashSet<string>(); var count = 0; var valid = true;
            ArenaMatch match;
            while ((match = schedule.Next()) != null)
            {
                valid &= !eliminated.Contains(match.Left) && !eliminated.Contains(match.Right) && pairs.Add(JsonSerializer.Serialize(match.PairKey));
                eliminated.Add(match.Right); schedule.Resolve(match.Left, new ArenaScore { IsRankable = true, Value = 100 - count++ });
                if (count > 64) { valid = false; break; }
            }
            check(valid && count == n - 1 && schedule.Champion != null && !eliminated.Contains(schedule.Champion), "knockout invariants: " + n + " entrants, N-1 unique unordered pairs, no eliminated entrant returns");
            tournament.Champion = schedule.Champion;
            var standings = ArenaStandings.Build(tournament);
            var final = tournament.Matches.Last();
            check(standings.Count == Math.Min(5, n) && standings.Select(p => p.EntrantId).Distinct().Count() == standings.Count
                && standings.Select(p => p.Rank).SequenceEqual(Enumerable.Range(1, standings.Count))
                && standings[0].EntrantId == schedule.Champion && standings[1].EntrantId == (final.Winner == final.Left ? final.Right : final.Left),
                "top five preserve champion, runner-up and distinct sequential places: " + n);
            check(standings.Zip(standings.Skip(1), (a, b) => a.EliminationStage >= b.EliminationStage
                && (a.EliminationStage != b.EliminationStage || a.LastVictoryScore >= b.LastVictoryScore)).All(x => x),
                "placing favors later elimination stages before latest winning scores: " + n);
            var reversed = JsonSerializer.Deserialize<ArenaTournamentResult>(JsonSerializer.Serialize(tournament)); reversed.Rosters.Reverse();
            check(JsonSerializer.Serialize(standings) == JsonSerializer.Serialize(ArenaStandings.Build(reversed)), "placing is independent of entrant XML order: " + n);
            if (n is 5 or 20 or 64)
            {
                tournament.Champion = schedule.Champion;
                var post = ArenaBracketLayout.BuildPostBattle(tournament);
                var byId = post.Nodes.ToDictionary(node => node.Id);
                check(post.Nodes.Count == 2 * n && post.Nodes.Where(node => node.Id.StartsWith("entrant/")).Select(node => node.EntrantId).Distinct().Count() == n,
                    "post-battle tree preserves every entrant and completed match: " + n);
                check(post.Nodes.All(a => post.Nodes.All(b => a == b || a.X != b.X || Math.Abs(a.Y - b.Y) >= 56))
                    && post.Edges.All(e => byId[e.From].X < byId[e.To].X), "post-battle layout avoids overlapping cards and backward edges: " + n);
                check(tournament.Matches.All(m => byId[m.Id].EntrantId == m.Winner) && byId["future"].EntrantId == tournament.Champion,
                    "advancement and champion cards retain winner color identity: " + n);
            }
        }
        var placingFixture = Tournament(input, 8);
        void PlaceMatch(int round, string kind, string leftId, string rightId, string winnerId, decimal points)
            => placingFixture.Matches.Add(new ArenaMatch { Round = round, Kind = kind, Left = leftId, Right = rightId, Winner = winnerId, Score = new ArenaScore { Value = points, IsRankable = true } });
        PlaceMatch(1, "round", "e0", "e1", "e0", 90); PlaceMatch(1, "round", "e2", "e3", "e2", 10);
        PlaceMatch(1, "round", "e4", "e5", "e4", 80); PlaceMatch(1, "round", "e6", "e7", "e6", 20);
        PlaceMatch(2, "round", "e0", "e2", "e0", 15); PlaceMatch(2, "round", "e4", "e6", "e4", 5);
        PlaceMatch(3, "round", "e0", "e4", "e4", 1); placingFixture.Champion = "e4";
        var placed = ArenaStandings.Build(placingFixture);
        check(placed.Take(4).Select(p => p.EntrantId).SequenceEqual(new[] { "e4", "e0", "e6", "e2" }),
            "champion and runner-up outrank higher-scoring semifinal losers, whose latest victory breaks their tie");
        var finalLoss = placingFixture.Matches.Single(m => m.Left == "e4" && m.Right == "e6"); finalLoss.Kind = "extra";
        check(ArenaStandings.Build(placingFixture)[2].EntrantId == "e6", "extra-match elimination is later than ordinary losses in the same round");
        var five = Tournament(input, 5); var s5 = new ArenaScheduler(five);
        var qualification = s5.Next(); check(qualification.Kind == "qualification" && qualification.Round == 0, "odd initial entrant count begins with a real qualification match");
        s5.Resolve(qualification.Left, new ArenaScore { IsRankable = true, Value = 10 });
        check(s5.Next().Kind == "round", "qualification winner returns to full even round");
        var six = Tournament(input, 6); var s6 = new ArenaScheduler(six); var wins = new List<string>();
        for (int i = 0; i < 3; i++) { var m = s6.Next(); wins.Add(m.Left); s6.Resolve(m.Left, new ArenaScore { IsRankable = true, Value = i == 0 ? 100 : i }); }
        var extra = s6.Next();
        check(extra.Kind == "extra" && extra.PairKey.Order().SequenceEqual(wins.Skip(1).Order()), "odd winners select the two lowest latest victory scores after the full round");
        s6.Resolve(extra.Left, new ArenaScore { IsRankable = true, Value = .125m });
        check(s6.Next().Kind == "round", "extra-match winner advances to the next ordinary round");
        string Trace(int seed)
        {
            var t = Tournament(input, 8); t.Input.Seed = seed; var scheduler = new ArenaScheduler(t); var keys = new List<string>(); ArenaMatch m;
            while ((m = scheduler.Next()) != null) { keys.Add(m.Id + JsonSerializer.Serialize(m.PairKey)); scheduler.Resolve(m.Left, new ArenaScore { IsRankable = true, Value = 1 }); }
            return string.Join(";", keys);
        }
        check(Trace(12345) == Trace(12345) && Trace(12345) != Trace(23456), "fixed hashing gives reproducible seed-dependent bracket ordering");
        check(ArenaScheduler.Stable("a|b", "c") != ArenaScheduler.Stable("a", "b|c"), "structured pair identities cannot collide through separators");
        var orderA = Tournament(input, 7); var orderB = Tournament(input, 7); orderB.Rosters.Reverse();
        var scheduleA = new ArenaScheduler(orderA); var scheduleB = new ArenaScheduler(orderB);
        var stableOrder = true;
        while (true)
        {
            var a = scheduleA.Next(); var b = scheduleB.Next();
            if (a == null || b == null) { stableOrder &= a == null && b == null; break; }
            stableOrder &= a.Left == b.Left && a.Right == b.Right && a.Kind == b.Kind;
            scheduleA.Resolve(a.Left, new ArenaScore { IsRankable = true, Value = 50 }); scheduleB.Resolve(b.Left, new ArenaScore { IsRankable = true, Value = 50 });
        }
        check(stableOrder && scheduleA.Champion == scheduleB.Champion, "equal counts and scores remain stable when XML entrant order is reversed");
        var counts = Tournament(input, 4); counts.Rosters[0].ResolvedCount = 1; counts.Rosters[1].ResolvedCount = 30; counts.Rosters[2].ResolvedCount = 31; counts.Rosters[3].ResolvedCount = 100;
        check(new ArenaScheduler(counts).Next().PairKey.SequenceEqual(new[] { "e1", "e2" }), "greedy pairing chooses globally closest initial roster counts");
        var left = new GroupCombatTeamResult { Id = "A", Initial = 20, Standing = 10, Alive = 10 }; var right = new GroupCombatTeamResult { Id = "B", Initial = 2, Standing = 2, Alive = 2 };
        var battle = new GroupCombatRoundResult { ElapsedTicks = 600, Teams = new() { left, right }, Units = Enumerable.Range(0, 20).Select(i => new GroupCombatUnit { Id = "a" + i, Team = "A", State = i < 10 ? "standing" : "downed" }).Concat(Enumerable.Range(0, 2).Select(i => new GroupCombatUnit { Id = "b" + i, Team = "B" })).ToList() };
        check(new ArenaEndRule(0).Evaluate(battle, 600) && right.Outcome == "win", "arena time-limit compares standing proportions rather than larger absolute survivor counts");
        left.Standing = left.Alive = 0; left.Downed = 20; left.PendingRevival = 1; battle.ElapsedTicks = 1;
        check(!new ArenaEndRule(0).Evaluate(battle, 600), "known pending revival delays arena defeat");
        left.PendingRevival = 0; left.Joined = 1; left.Standing = left.Alive = 1;
        check(!new ArenaEndRule(0).Evaluate(battle, 600), "standing summon keeps its side in the arena fight");
        left.Joined = 0; left.Standing = left.Alive = 0; left.Downed = 0; left.Escaped = 20;
        check(new ArenaEndRule(0).Evaluate(battle, 600) && right.Outcome == "win", "all escaped original units are unavailable for arena defeat determination");
        right.Standing = right.Alive = 0; right.Downed = 2;
        check(new ArenaEndRule(0).Evaluate(battle, 600) && battle.Teams.All(t => t.Outcome == "draw"), "same-tick mutual arena defeat yields a draw");
        var confirm = new ArenaEndRule(300); battle.ElapsedTicks = 1;
        check(!confirm.Evaluate(battle, 600), "arena defeat starts continuous confirmation window");
        battle.ElapsedTicks = 301;
        check(confirm.Evaluate(battle, 301) && battle.EndReason == "allUnavailable", "confirmed defeat takes priority at the exact simulated time limit");
        var reviveRule = new ArenaEndRule(300);
        left.Standing = left.Alive = 0; left.Escaped = left.Initial; right.Standing = right.Alive = right.Initial; right.Downed = 0;
        battle.ElapsedTicks = 1; check(!reviveRule.Evaluate(battle, 1000), "arena begins defeat confirmation for unavailable side");
        left.Standing = left.Alive = 1; left.Escaped--; battle.ElapsedTicks = 301;
        check(!reviveRule.Evaluate(battle, 1000) && left.EliminatedAtTick == null, "revival cancels the continuous defeat confirmation");
        left.Standing = left.Alive = 0; left.Escaped++; battle.ElapsedTicks = 302;
        check(!reviveRule.Evaluate(battle, 1000) && left.EliminatedAtTick == 302, "a second defeat starts a new confirmation window after revival");
        left.Standing = left.Alive = 1; left.Escaped--; left.Joined++; battle.ElapsedTicks = 602;
        check(!reviveRule.Evaluate(battle, 1000), "newly registered standing participant cancels arena defeat");
        var layout = ArenaBracketLayout.Build(Tournament(input, 64));
        check(layout.Nodes.Count == 65 && layout.Width > 0 && layout.Height >= 64 * 64, "64-entrant bracket retains all entrants and a future placeholder for scrollable overview");
        var kinds = Enumerable.Range(0, 64).Select(i => "Kind" + i).ToArray();
        var hues = ArenaPresentation.IdentityHues(kinds);
        var reversedHues = ArenaPresentation.IdentityHues(kinds.Reverse());
        check(hues.All(pair => pair.Value == reversedHues[pair.Key]) && hues.Values.All(h => h >= 0 && h < 1)
            && hues.Values.All(a => hues.Values.All(b => a == b || Math.Min(Math.Abs(a - b), 1 - Math.Abs(a - b)) >= .65f / 64)),
            "64 identity hues are stable across roster ordering and separated even after collision resolution");
        check(ArenaPresentation.Progress(-1, 0, .5f) == 0 && ArenaPresentation.Progress(10, 0, .5f) == 1
            && ArenaPresentation.RevealBSeconds > .5f && ArenaPresentation.AdvanceSeconds > ArenaPresentation.RevealBSeconds + .5f
            && ArenaPresentation.PostBattleSeconds > ArenaPresentation.AnimationSeconds,
            "post-battle A/B reveals precede advancement and leave a stable capture interval");
    }
    private static ArenaTournamentResult Tournament(ArenaTournamentInput source, int count)
    {
        var input = JsonSerializer.Deserialize<ArenaTournamentInput>(JsonSerializer.Serialize(source));
        return new ArenaTournamentResult { Input = input, Rosters = Enumerable.Range(0, count).Select(i => new ResolvedCombatRoster { Id = "e" + i, DisplayName = "Entrant " + i, PawnKind = "K" + i, ResolvedCount = 10 + i % 3, UnitCombatPower = 100 }).ToList() };
    }
}
