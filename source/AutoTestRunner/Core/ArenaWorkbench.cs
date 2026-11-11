using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using LogSyncMod.AutoTest.Scenarios;

namespace LogSyncMod.AutoTest
{
    public sealed class ArenaWorkbenchMember
    {
        public string Name { get; set; } = "新角色组";
        public int Count { get; set; } = 1;
        public string PawnXml { get; set; } = "<PawnTemplate id='pawn'><basePawnKind>Colonist</basePawnKind></PawnTemplate>";
        public XElement Read() => ArenaWorkbench.ReadXml(PawnXml);
    }

    public sealed class ArenaWorkbenchTeam
    {
        public string Id { get; set; } = "team_" + Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "新编队";
        public bool Enabled { get; set; } = true;
        public string Role { get; set; } = "challenger";
        public string Ai { get; set; } = "inherit";
        public string Source { get; set; } = "groups";
        public string Faction { get; set; } = "Pirate";
        public string PawnKind { get; set; } = "Colonist";
        public string Xenotype { get; set; }
        public string Mode { get; set; } = "count";
        public int Count { get; set; } = 20;
        public decimal Budget { get; set; } = 3000;
        public string CostMode { get; set; } = "native";
        public int? GroupMakerIndex { get; set; }
        public float GroupReferencePoints { get; set; } = 5000;
        public List<ArenaWorkbenchMember> Members { get; set; } = new();
        public int MemberCount => Source == "groups" ? checked(Members.Sum(m => m.Count)) : Count;
        public bool IsEmpty => Source == "groups" ? Members == null || Members.All(m => m.Count == 0) : Mode == "count" ? Count == 0 : Budget == 0;
    }

    public sealed class ArenaWorkbenchSchedule
    {
        public string Name { get; set; } = "群战赛事";
        public string Format { get; set; } = "knockout";
        public int Seed { get; set; } = -1;
        public string Matchmaking { get; set; } = "closestCount";
        public string GrowthTarget { get; set; } = "count";
        public string GrowthMode { get; set; } = "multiply";
        public decimal GrowthStep { get; set; } = 1.5m;
        public decimal GrowthCap { get; set; } = 100;
        public double Duration { get; set; } = 180;
        public int Timeout { get; set; } = 300;
        public string Speed { get; set; } = "fast";
        public int ConfirmationTicks { get; set; } = 300;
        public int Replays { get; set; } = 2;
        public string Ai { get; set; } = "assaultColony";
        public int Width { get; set; } = 140;
        public int Height { get; set; } = 140;
        public string Battlefield { get; set; } = "open";
        public double FeatureDensity { get; set; } = 1;
        public int SpawnClearance { get; set; } = 6;
        public string Weather { get; set; } = "Clear";
        public double Temperature { get; set; } = 21;
        public string Roof { get; set; } = "none";
        public bool NaturalSun { get; set; } = true;
        public bool ClearCorpses { get; set; }
        public bool ClearItems { get; set; }
        public decimal Survival { get; set; } = 45;
        public decimal Suppression { get; set; } = 25;
        public decimal Exchange { get; set; } = 20;
        public decimal SpeedWeight { get; set; } = 10;
        public double TimeReference { get; set; } = 180;
        public double ShowcaseSeconds { get; set; } = 5;
        public double CountdownSeconds { get; set; } = 3;
        public double SettlementSeconds { get; set; } = 3;
        public double AdvanceSeconds { get; set; } = 3;
        public int OutputWidth { get; set; } = 1920;
        public int OutputHeight { get; set; } = 1080;
    }

    public sealed class ArenaWorkbenchDocument
    {
        public int SchemaVersion { get; set; } = 2;
        public ArenaWorkbenchSchedule Schedule { get; set; } = new();
        public List<ArenaWorkbenchTeam> Teams { get; set; } = new();
        public List<ArenaWorkbenchMember> PawnPresets { get; set; } = new();
        public List<ArenaWorkbenchTeam> TeamPresets { get; set; } = new();
        public List<ArenaWorkbenchSchedule> SchedulePresets { get; set; } = new();
        public List<ArenaWorkbenchEventPreset> EventPresets { get; set; } = new();
    }

    // These are editors for existing XML, not an additional execution language.
    public static partial class ArenaWorkbench
    {
        public const string TestName = "ManualArena_Main";
        public static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value));
        public static XElement ReadXml(string xml)
        {
            if (xml == null || xml.Length > 4 * 1024 * 1024) throw new ScenarioConfigurationException("XML 为空或超过 4 MiB。");
            using var reader = XmlReader.Create(new StringReader(xml), ScenarioLoader.ReaderSettings());
            return XElement.Load(reader);
        }
        public static ArenaWorkbenchDocument Create()
        {
            ArenaWorkbenchMember Member(string name, string weapon, int count) => new() {
                Name = name, Count = count, PawnXml = new XElement("PawnTemplate", new XAttribute("id", "pawn"),
                    new XElement("basePawnKind", "Colonist"), new XElement("Age", new XAttribute("biologicalYears", 28)),
                    new XElement("Equipment", new XAttribute("mode", "replace"), new XElement("SetPrimary", new XAttribute("def", weapon), new XAttribute("quality", "Normal"))),
                    new XElement("Skills", new XAttribute("mode", "patch"), new XElement("Set", new XAttribute("def", "Shooting"), new XAttribute("level", 10)))) .ToString()
            };
            var rifle = Member("步枪手", "Gun_AssaultRifle", 8);
            var melee = Member("近战兵", "MeleeWeapon_LongSword", 4);
            ReadAndChange(melee, p => { p.Element("Equipment").Element("SetPrimary").SetAttributeValue("stuff", "Steel"); });
            var doc = new ArenaWorkbenchDocument { Teams = new() {
                new() { Name = "联合突击队", Members = new() { rifle, melee } },
                new() { Name = "守擂队", Role = "boss", Members = new() { Member("守擂步枪手", "Gun_AssaultRifle", 10) } }
            }, PawnPresets = new() { Copy(rifle), Copy(melee) } };
            doc.PawnPresets.ForEach(p => p.Count = 1);
            doc.TeamPresets.Add(Copy(doc.Teams[0]));
            doc.SchedulePresets = BuiltInSchedules();
            return doc;
        }
        public static List<ArenaWorkbenchSchedule> BuiltInSchedules() => new() {
            new() { Name = "群战测试", Format = "groupcombat" },
            new() { Name = "擂台赛", Format = "knockout" },
            new() { Name = "魔王挑战赛", Format = "bossRoundRobin" },
            new() { Name = "极限挑战赛", Format = "limitChallenge" }
        };
        public static void ReadAndChange(ArenaWorkbenchMember member, Action<XElement> edit)
        { var xml = member.Read(); edit(xml); member.PawnXml = xml.ToString(); }
        public static ArenaWorkbenchMember Split(ArenaWorkbenchTeam team, int index)
        {
            var group = team.Members[index];
            if (group.Count <= 1) throw new ScenarioConfigurationException("至少 2 人的成员组才能拆出单人。");
            var one = Copy(group); one.Count = 1; one.Name += " · 单独";
            group.Count--; team.Members.Insert(index + 1, one); return one;
        }
        public static ArenaWorkbenchTeam Duplicate(ArenaWorkbenchTeam team)
        { var clone = Copy(team); clone.Id = "team_" + Guid.NewGuid().ToString("N"); clone.Name += " · 副本"; return clone; }

        public static XElement PawnTemplate(ArenaWorkbenchMember member, string id)
        {
            var p = member.Read();
            if (p.Name != "PawnTemplate" || p.Attribute("extends") != null) throw new ScenarioConfigurationException("角色配置必须是独立的 PawnTemplate。");
            p.SetAttributeValue("id", id); return p;
        }
        public static XElement Export(ArenaWorkbenchDocument document, bool enabledOnly = true)
        {
            ValidateStructure(document);
            if (document.Schedule.Format == "groupcombat") return ExportGroup(document, enabledOnly);
            var s = document.Schedule ?? throw new ScenarioConfigurationException("缺少赛程。");
            var teams = document.Teams.Where(t => (!enabledOnly || t.Enabled) && !t.IsEmpty).ToList();
            var templates = new XElement("Templates");
            var squads = new XElement("SquadTemplates");
            var entrants = new XElement("Entrants");
            if (teams.Select(t => t.Id).Distinct(StringComparer.Ordinal).Count() != teams.Count) throw new ScenarioConfigurationException("编队 ID 重复。");
            for (var i = 0; i < teams.Count; i++)
            {
                var team = teams[i];
                if (team.Mode != "count" && team.Mode != "combatPower") throw new ScenarioConfigurationException("编队模式必须为 count/combatPower。");
                var entrant = new XElement("Entrant", new XAttribute("id", team.Id), new XAttribute("name", team.Name ?? team.Id), new XAttribute("mode", team.Mode), new XAttribute("costMode", team.CostMode),
                    team.Ai == "inherit" ? null : new XAttribute("ai", team.Ai));
                if (s.Format != "knockout") entrant.Add(new XAttribute("role", team.Role));
                if (team.Source == "groups")
                {
                    if (team.Members.Count == 0) throw new ScenarioConfigurationException("编队不能为空：" + team.Name);
                    var squad = new XElement("SquadTemplate", new XAttribute("id", "squad_" + i), new XAttribute("source", "groups"), new XAttribute("factionDef", team.Faction));
                    for (var j = 0; j < team.Members.Count; j++)
                    {
                        var member = team.Members[j];
                        if (member.Count == 0) continue;
                        var id = "pawn_" + i + "_" + j;
                        templates.Add(PawnTemplate(member, id));
                        squad.Add(new XElement("PawnGroup", new XAttribute("id", "group_" + j), new XAttribute("template", id), new XAttribute("count", member.Count)));
                    }
                    squads.Add(squad); entrant.Add(new XAttribute("squad", "squad_" + i));
                }
                else if (team.Source == "faction")
                {
                    squads.Add(new XElement("SquadTemplate", new XAttribute("id", "squad_" + i), new XAttribute("source", "faction"), new XAttribute("factionDef", team.Faction),
                        new XElement("GroupMaker", new XAttribute("mode", team.GroupMakerIndex.HasValue ? "specific" : "random"), new XAttribute("kindDef", "Combat"), new XAttribute("referencePoints", team.GroupReferencePoints), team.GroupMakerIndex.HasValue ? new XAttribute("index", team.GroupMakerIndex.Value) : null), new XElement("Xenotypes", new XAttribute("mode", team.Xenotype == null ? "inherit" : "fixed"), team.Xenotype == null ? null : new XAttribute("def", team.Xenotype))));
                    entrant.Add(new XAttribute("squad", "squad_" + i));
                }
                else if (team.Source == "kind")
                {
                    entrant.Add(new XAttribute("pawnKind", team.PawnKind));
                    if (team.Xenotype != null) entrant.Add(new XAttribute("xenotype", team.Xenotype));
                }
                else throw new ScenarioConfigurationException("未知编队来源：" + team.Source);
                entrant.Add(team.Mode == "combatPower" ? new XAttribute("budget", team.Budget) : new XAttribute("count", team.Source == "groups" ? team.MemberCount : team.Count));
                entrants.Add(entrant);
            }
            var t = new XElement("Tournament", new XAttribute("id", "main"), new XAttribute("name", s.Name), new XAttribute("format", s.Format), new XAttribute("seed", s.Seed),
                new XElement("testDefName", TestName),
                new XElement("Scene", new XAttribute("type", "mainMap"), new XAttribute("sizeX", s.Width), new XAttribute("sizeZ", s.Height),
                    new XAttribute("battlefield", s.Battlefield), new XAttribute("featureDensity", s.FeatureDensity), new XAttribute("spawnClearance", s.SpawnClearance),
                    new XElement("Environment", new XAttribute("weather", s.Weather), new XAttribute("temperature", s.Temperature), new XAttribute("roof", s.Roof), new XAttribute("naturalSun", s.NaturalSun))),
                new XElement("Roster", new XAttribute("mode", "count"), new XAttribute("count", 20), new XAttribute("costMode", "native")), entrants,
                new XElement("Battle", new XAttribute("durationSeconds", s.Duration), new XAttribute("timeoutRealSeconds", s.Timeout), new XAttribute("timeSpeed", s.Speed), new XAttribute("confirmationTicks", s.ConfirmationTicks), new XAttribute("aiPolicy", s.Ai)),
                new XElement("TieBreak", new XAttribute("onDraw", "replay"), new XAttribute("maxReplays", s.Replays)),
                new XElement("Scoring", new XAttribute("survivalWeight", s.Survival), new XAttribute("suppressionWeight", s.Suppression), new XAttribute("exchangeWeight", s.Exchange), new XAttribute("speedWeight", s.SpeedWeight), new XAttribute("timeReferenceSeconds", s.TimeReference)),
                new XElement("Cleanup", new XAttribute("corpses", s.ClearCorpses), new XAttribute("droppedItems", s.ClearItems)),
                new XElement("Presentation", new XAttribute("bracketSeconds", s.CountdownSeconds), new XAttribute("settlementSeconds", s.SettlementSeconds), new XAttribute("advanceStartSeconds", s.AdvanceSeconds), new XElement("Showcase", new XAttribute("pageSeconds", s.ShowcaseSeconds))),
                new XElement("Output", new XAttribute("width", s.OutputWidth), new XAttribute("height", s.OutputHeight)));
            if (s.Format == "knockout") t.Add(new XElement("Matchmaking", new XAttribute("mode", s.Matchmaking)));
            if (s.Format == "limitChallenge") t.Add(new XElement("Growth", new XAttribute("target", s.GrowthTarget), new XAttribute("mode", s.GrowthMode),
                new XAttribute(s.GrowthMode == "add" ? "increment" : "multiplier", s.GrowthStep), new XAttribute("maxValue", s.GrowthCap)));
            return new XElement("ArenaSuite", new XAttribute("schemaVersion", 1), new XAttribute("id", "manual.arena"), new XAttribute("gameVersion", "1.7"), templates, squads, t);
        }
        public static ArenaWorkbenchDocument ForStorage(ArenaWorkbenchDocument document, bool rejectEmpty)
        {
            ValidateStructure(document);
            if (rejectEmpty && document.Teams.Any(t => t.IsEmpty)) throw new ScenarioConfigurationException("空编队不能保存，请添加人员或删除空编队。");
            var result = Copy(document);
            result.Teams.RemoveAll(t => t.IsEmpty);
            result.TeamPresets.RemoveAll(t => t.IsEmpty);
            foreach (var team in result.Teams.Concat(result.TeamPresets)) team.Members.RemoveAll(m => m.Count == 0);
            foreach (var preset in result.EventPresets)
            {
                preset.Teams.RemoveAll(t => t.IsEmpty);
                foreach (var team in preset.Teams) team.Members.RemoveAll(m => m.Count == 0);
            }
            return result;
        }
        public static void UpgradeUi(ArenaWorkbenchDocument document)
        {
            if (document.SchemaVersion != 1 && document.SchemaVersion != 2) throw new ScenarioConfigurationException("不支持的工作台配置版本。");
            if (document.SchemaVersion == 1)
            {
                if (document.Schedule.Seed == 12345) document.Schedule.Seed = -1;
                var builtIns = new HashSet<string>(BuiltInSchedules().Select(p => p.Name));
                foreach (var preset in document.SchedulePresets.Where(p => p != null && builtIns.Contains(p.Name) && p.Seed == 12345)) preset.Seed = -1;
                document.SchemaVersion = 2;
            }
            ApplyUiDefaults(document.Schedule);
            document.SchedulePresets = BuiltInSchedules();
        }
        public static void ApplyUiDefaults(ArenaWorkbenchSchedule s)
        {
            var d = new ArenaWorkbenchSchedule();
            s.Timeout = d.Timeout; s.ConfirmationTicks = d.ConfirmationTicks; s.Replays = d.Replays;
            s.Survival = d.Survival; s.Suppression = d.Suppression; s.Exchange = d.Exchange; s.SpeedWeight = d.SpeedWeight; s.TimeReference = d.TimeReference;
            s.ShowcaseSeconds = d.ShowcaseSeconds; s.CountdownSeconds = d.CountdownSeconds; s.SettlementSeconds = d.SettlementSeconds; s.AdvanceSeconds = d.AdvanceSeconds;
            s.OutputWidth = d.OutputWidth; s.OutputHeight = d.OutputHeight;
            if (s.Width < 80 || s.Width > 250) s.Width = d.Width;
            if (s.Height < 80 || s.Height > 250) s.Height = d.Height;
            if (string.IsNullOrWhiteSpace(s.Battlefield)) s.Battlefield = d.Battlefield;
            s.Roof = d.Roof;
        }
        public static ArenaPlan Compile(ArenaWorkbenchDocument document) => CompileXml(Export(document).ToString());
        public static ArenaPlan CompileXml(string xml)
        {
            using var sha = SHA256.Create();
            var hash = Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(xml))).ToLowerInvariant();
            return ArenaLoader.Compile(new[] { new ScenarioInput { Path = "ArenaWorkbench.autotest.xml", Xml = xml, Sha256 = hash } }, new[] { TestName });
        }
        // A one-team export remains a real reusable ArenaSuite, even though it cannot start alone.
        public static string TeamXml(ArenaWorkbenchTeam team)
        { var doc = new ArenaWorkbenchDocument { Teams = new() { Copy(team) } }; return Export(doc, false).ToString(); }
        public static string LibraryXml(ArenaWorkbenchDocument document)
        {
            return new XElement("ArenaWorkbenchPresets", new XAttribute("schemaVersion", 1),
                new XElement("PawnPresets", document.PawnPresets.Select((p, i) => new XElement("Preset", new XAttribute("name", p.Name), PawnTemplate(p, "preset_" + i)))),
                new XElement("TeamPresets", document.TeamPresets.Select(t => new XElement("Preset", new XAttribute("name", t.Name), ReadXml(TeamXml(t))))),
                new XElement("SchedulePresets", document.SchedulePresets.Select(s => new XElement("Preset", new XAttribute("name", s.Name), Export(new ArenaWorkbenchDocument { Schedule = s }, false).Element("Tournament")))),
                new XElement("EventPresets", document.EventPresets.Select(e => new XElement("Preset", new XAttribute("name", e.Name), new XAttribute("enabled", e.Enabled), ReadXml(EventXml(e)))))).ToString();
        }
    }
}
