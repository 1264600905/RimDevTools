using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using HarmonyLib;
using LogSyncMod.AutoTest.Scenarios;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;

namespace LogSyncMod.AutoTest
{
    internal sealed class ScenarioGameBackend : IScenarioBackend
    {
        private readonly Map source;
        private readonly AutoTestPocketMapService maps;
        private readonly Func<XElement, bool> screenshot;
        private readonly Action<string, string> trace;
        private readonly Dictionary<string, List<Thing>> actors = new Dictionary<string, List<Thing>>();
        private readonly Dictionary<string, Faction> teams = new Dictionary<string, Faction>();
        private readonly HashSet<Thing> owned = new HashSet<Thing>();
        private readonly Dictionary<string, object> references = new Dictionary<string, object>();
        private AutoTestPocketMapHandle handle;
        private Ability casting;
        private int castBaseline;
        private bool castStarted;
        private int attackTick;
        public Map Map => handle?.Map;
        internal static Action NativeObservation;

        internal ScenarioGameBackend(Map source, AutoTestPocketMapService maps, Func<XElement, bool> screenshot, Action<string, string> trace)
        { this.source = source; this.maps = maps; this.screenshot = screenshot; this.trace = trace; }

        public void Prepare(ScenarioCase definition)
        {
            var c = XElement.Parse(definition.Xml);
            ValidateDefs(c, definition);
            foreach (var requirement in c.Element("Requirements")?.Elements() ?? Enumerable.Empty<XElement>())
                if (!LoadedModManager.RunningMods.Any(m => string.Equals(m.PackageIdPlayerFacing, A(requirement, "packageId"), StringComparison.OrdinalIgnoreCase)))
                    throw new ScenarioConfigurationException("Required mod/DLC is not loaded: " + requirement);
            foreach (var t in c.Element("Teams")?.Elements("Team") ?? Enumerable.Empty<XElement>())
            {
                var name = A(t, "factionDef");
                var faction = name == "player" ? Faction.OfPlayer : Find.FactionManager.AllFactions.FirstOrDefault(f => f.def.defName == name);
                if (faction == null) throw new ScenarioConfigurationException("No loaded faction: " + name);
                teams.Add(A(t, "id"), faction);
            }
            foreach (var r in c.Element("Teams")?.Elements("Relation") ?? Enumerable.Empty<XElement>())
            {
                if (!teams.TryGetValue(A(r, "a"), out var a) || !teams.TryGetValue(A(r, "b"), out var b)) throw new ScenarioConfigurationException("Unknown relation team");
                if (a.RelationKindWith(b).ToString() != A(r, "value")) throw new ScenarioConfigurationException("Requested relation differs from loaded factions; choose existing factions with the required relation (temporary factions arrive with group combat)");
            }
            var s = c.Element("Scene");
            handle = maps.Create(new AutoTestPocketMapRequest { SourceMap = source, MapSize = new IntVec3(I(s, "sizeX", 50), 1, I(s, "sizeZ", 50)) }, out var error);
            if (handle == null) throw new InvalidOperationException(error);
            Current.Game.CurrentMap = Map;
            Find.World.renderer.wantedMode = WorldRenderMode.None;
            Find.CameraDriver.JumpToCurrentMapLoc(Map.Center);
            Rand.PushState(I(c, "seed", 12345));
            try
            {
                foreach (var actor in c.Element("Actors").Elements()) CreateActor(actor);
            }
            finally { Rand.PopState(); }
            trace("scene.ready", "map=" + Map.uniqueID + "; unfogged=" + Map.AllCells.All(cell => !cell.Fogged(Map)) + "; actors=" + string.Join(",", actors.Select(a => a.Key + "=" + string.Join("|", a.Value.Select(v => v.GetUniqueLoadID())))));
        }

        private void CreateActor(XElement actor)
        {
            var id = A(actor, "id");
            if (actor.Name == "DefRef") { references.Add(id, ResolveDef(A(actor, "defType"), A(actor, "defName"))); return; }
            if (actor.Name == "TerrainArea")
            {
                var def = Def<TerrainDef>(A(actor, "def"));
                foreach (var cell in CellRect.FromLimits(I(actor, "x"), I(actor, "z"), I(actor, "x") + I(actor, "width", 1) - 1, I(actor, "z") + I(actor, "height", 1) - 1))
                { if (!cell.InBounds(Map)) throw new ScenarioConfigurationException("Terrain outside arena: " + id); Map.terrainGrid.SetTerrain(cell, def); }
                references.Add(id, def); return;
            }
            var group = new List<Thing>();
            actors.Add(id, group);
            var count = actor.Name == "PawnGroup" ? I(actor, "count", 1) : 1;
            for (var n = 0; n < count; n++)
            {
                Thing thing;
                if (actor.Name == "Pawn" || actor.Name == "PawnGroup" || actor.Name == "Corpse") thing = GeneratePawn(actor);
                else
                {
                    thing = MakeThing(A(actor, "def"), A(actor, "stuff"));
                    owned.Add(thing);
                    if (actor.Name == "Building" && !(thing is Building)) throw new ScenarioConfigurationException("Building actor requires building Def");
                    if (thing is Pawn || thing is Corpse) throw new ScenarioConfigurationException("Use Pawn/Corpse actors for pawn lifecycle");
                    if (thing.def.CanHaveFaction && A(actor, "team") != null) thing.SetFaction(teams[A(actor, "team")]);
                    thing.stackCount = I(actor, "count", 1);
                    ApplyQuality(thing, A(actor, "quality"));
                }
                var cell = new IntVec3(I(actor, "x", I(actor, "originX", 20)) + n * I(actor, "spacing", 2), 0, I(actor, "z", I(actor, "originZ", 20)));
                if (!cell.InBounds(Map) || !cell.Standable(Map)) throw new ScenarioConfigurationException("Actor cell unavailable: " + id + " " + cell);
                if (A(actor, "spawn", "true") == "true") GenSpawn.Spawn(thing, cell, Map, WipeMode.Vanish);
                if (actor.Name == "Corpse")
                {
                    var pawn = (Pawn)thing;
                    pawn.Kill(null);
                    var corpse = pawn.Corpse ?? throw new InvalidOperationException("Native death produced no corpse");
                    actors.Add(id + ".innerPawn", new List<Thing> { pawn });
                    owned.Add(corpse);
                    thing = corpse;
                    if (corpse.InnerPawn != pawn) throw new InvalidOperationException("Corpse inner pawn identity changed");
                }
                group.Add(thing);
                if (actor.Name == "PawnGroup") actors.Add(id + "[" + n + "]", new List<Thing> { thing });
            }
        }

        private Pawn GeneratePawn(XElement actor)
        {
            var original = Def<PawnKindDef>(actor.Element("basePawnKind")?.Value.Trim());
            var before = KindFingerprint(original);
            // Fixed internal shallow clone, never an XML-selected type/member. Only replaced fields below are written.
            var kind = (PawnKindDef)typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(original, null);
            if (actor.Element("race") != null) kind.race = Def<ThingDef>(actor.Element("race").Value.Trim());
            if (actor.Element("minGenerationAge") != null) kind.minGenerationAge = int.Parse(actor.Element("minGenerationAge").Value, CultureInfo.InvariantCulture);
            if (actor.Element("maxGenerationAge") != null) kind.maxGenerationAge = int.Parse(actor.Element("maxGenerationAge").Value, CultureInfo.InvariantCulture);
            if (kind.minGenerationAge < 0 || kind.maxGenerationAge < kind.minGenerationAge) throw new ScenarioConfigurationException("Invalid generation age range");
            if (actor.Element("gender") != null) kind.fixedGender = Enum.Parse<Gender>(actor.Element("gender").Value, true);
            if (actor.Element("apparelTags") != null) kind.apparelTags = actor.Element("apparelTags").Elements("li").Select(e => e.Value.Trim()).ToList();
            if (actor.Element("weaponTags") != null) kind.weaponTags = actor.Element("weaponTags").Elements("li").Select(e => e.Value.Trim()).ToList();
            if (actor.Element("apparelMoney") != null) kind.apparelMoney = FloatRange.FromString(actor.Element("apparelMoney").Value);
            if (actor.Element("weaponMoney") != null) kind.weaponMoney = FloatRange.FromString(actor.Element("weaponMoney").Value);
            var faction = A(actor, "team") == null ? null : teams[A(actor, "team")];
            var explicitSkills = (actor.Element("Skills")?.Elements("Set").Select(e => A(e, "def")) ?? Enumerable.Empty<string>()).Concat(actor.Element("skills")?.Elements("li").Select(e => e.Element("skill")?.Value) ?? Enumerable.Empty<string>()).Select(Def<SkillDef>).ToList();
            float? age = actor.Element("minGenerationAge") != null && actor.Element("maxGenerationAge") != null ? Rand.Range((float)kind.minGenerationAge, kind.maxGenerationAge) : (float?)null;
            var pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, faction, forceGenerateNewPawn: true, canGeneratePawnRelations: false,
                fixedBiologicalAge: age, validatorPreGear: p => explicitSkills.All(s => p.skills != null && !p.skills.GetSkill(s).TotallyDisabled)));
            if (pawn == null) throw new InvalidOperationException("PawnGenerator returned null");
            owned.Add(pawn);
            // The generated pawn retains its native registered kind when race was not overridden.
            if (kind.race == original.race) pawn.kindDef = original;
            trace("pawn.inherited", Describe(pawn));
            ApplyPawnConfig(pawn, actor);
            if (pawn.Faction != faction) throw new InvalidOperationException("Pawn generator changed requested faction");
            if (KindFingerprint(original) != before) throw new InvalidOperationException("Shared PawnKind was modified");
            trace("pawn.configured", Describe(pawn) + "; baseDefUnchanged=true");
            return pawn;
        }

        private void ApplyPawnConfig(Pawn pawn, XElement a)
        {
            foreach (var item in a.Element("forcedTraits")?.Elements("li") ?? Enumerable.Empty<XElement>())
                SetTrait(pawn, item.Element("def")?.Value, int.Parse(item.Element("degree")?.Value ?? "0", CultureInfo.InvariantCulture));
            foreach (var item in a.Element("skills")?.Elements("li") ?? Enumerable.Empty<XElement>())
            {
                var range = IntRange.FromString(item.Element("range")?.Value ?? "0");
                SetSkill(pawn, item.Element("skill")?.Value, range.RandomInRange, item.Element("passion")?.Value);
            }
            foreach (var item in a.Element("abilities")?.Elements("li") ?? Enumerable.Empty<XElement>()) EnsureAbility(pawn, item.Value.Trim());
            foreach (var patch in a.Elements().Where(e => new[] { "Apparel", "Equipment", "Traits", "Skills", "Hediffs", "Abilities" }.Contains(e.Name.LocalName)))
            {
                var type = patch.Name.LocalName;
                var mode = A(patch, "mode", "patch");
                if (mode == "inherit") { if (patch.HasElements) throw new ScenarioConfigurationException("inherit must not contain modifications"); continue; }
                if (mode == "replace")
                {
                    if (type == "Apparel") { Need(pawn.apparel, type); pawn.apparel.DestroyAll(); }
                    if (type == "Equipment") { Need(pawn.equipment, type); pawn.equipment.DestroyAllEquipment(); }
                    if (type == "Traits") { Need(pawn.story, type); foreach (var t in pawn.story.traits.allTraits.ToList()) pawn.story.traits.RemoveTrait(t); }
                    if (type == "Skills") { Need(pawn.skills, type); foreach (var s in pawn.skills.skills) { s.Level = 0; s.passion = Passion.None; } }
                    if (type == "Hediffs") foreach (var h in pawn.health.hediffSet.hediffs.ToList()) pawn.health.RemoveHediff(h);
                    if (type == "Abilities") { Need(pawn.abilities, type); foreach (var ab in pawn.abilities.abilities.ToList()) pawn.abilities.RemoveAbility(ab.def); }
                }
                foreach (var item in patch.Elements())
                {
                    var def = A(item, "def");
                    if (type == "Apparel")
                    {
                        Need(pawn.apparel, type);
                        if (item.Name == "Replace")
                        {
                            var old = pawn.apparel.WornApparel.Where(app => A(item, "matchDef") != null ? app.def.defName == A(item, "matchDef") : app.def.apparel.layers.Any(layer => layer.defName == A(item, "matchLayer"))).ToList();
                            if (old.Count == 0 && A(item, "onMissing", "fail") == "fail") throw new ScenarioConfigurationException("Apparel replacement has no match: " + item);
                            foreach (var apparel in old) apparel.Destroy();
                            def = A(item, "withDef");
                        }
                        if (item.Name == "Remove") { foreach (var old in pawn.apparel.WornApparel.Where(app => app.def.defName == def).ToList()) old.Destroy(); continue; }
                        var apparelNew = MakeThing(def, A(item, "stuff")) as Apparel ?? throw new ScenarioConfigurationException("Not apparel: " + def);
                        owned.Add(apparelNew); ApplyQuality(apparelNew, A(item, "quality"));
                        if (!ApparelUtility.HasPartsToWear(pawn, apparelNew.def)) throw new ScenarioConfigurationException("Pawn cannot wear " + def);
                        // Do not silently replace unrelated clothing.
                        if (pawn.apparel.WornApparel.Any(old => !ApparelUtility.CanWearTogether(old.def, apparelNew.def, pawn.RaceProps.body))) throw new ScenarioConfigurationException("Explicit apparel conflicts with inherited clothing: " + def);
                        pawn.apparel.Wear(apparelNew, false);
                        if (!pawn.apparel.WornApparel.Contains(apparelNew)) throw new InvalidOperationException("Wear failed: " + def);
                    }
                    else if (type == "Equipment") Equip(pawn, item);
                    else if (type == "Traits") { Need(pawn.story, type); if (item.Name == "Remove") { foreach (var t in pawn.story.traits.allTraits.Where(t => t.def.defName == def).ToList()) pawn.story.traits.RemoveTrait(t); } else SetTrait(pawn, def, I(item, "degree")); }
                    else if (type == "Skills") SetSkill(pawn, def, I(item, "level"), A(item, "passion"));
                    else if (type == "Hediffs") { if (item.Name == "Remove") RemoveHediff(pawn, item); else AddHediff(pawn, item); }
                    else if (type == "Abilities") { Need(pawn.abilities, type); if (item.Name == "Remove") pawn.abilities.RemoveAbility(Def<AbilityDef>(def)); else EnsureAbility(pawn, def); }
                }
            }
            var appearance = a.Element("Appearance");
            if (appearance != null)
            {
                Need(pawn.story, "Appearance");
                if (appearance.Element("hair") != null) pawn.story.hairDef = Def<HairDef>(appearance.Element("hair").Value);
                if (appearance.Element("bodyType") != null) pawn.story.bodyType = Def<BodyTypeDef>(appearance.Element("bodyType").Value);
                if (appearance.Element("headType") != null) pawn.story.headType = Def<HeadTypeDef>(appearance.Element("headType").Value);
                if (appearance.Element("hairColor") != null) pawn.story.HairColor = ParseColor(appearance.Element("hairColor").Value);
                if (appearance.Element("skinColor") != null) pawn.story.skinColorOverride = ParseColor(appearance.Element("skinColor").Value);
                pawn.Drawer.renderer.SetAllGraphicsDirty();
            }
        }

        private static void Need(object tracker, string name) { if (tracker == null) throw new ScenarioConfigurationException("Pawn has no " + name + " tracker"); }
        private static Color ParseColor(string value) => ColorUtility.TryParseHtmlString(value, out var color) ? color : throw new ScenarioConfigurationException("Color must be #RRGGBB: " + value);
        private static void SetTrait(Pawn p, string name, int degree)
        {
            Need(p.story, "Traits"); var def = Def<TraitDef>(name);
            if (!def.degreeDatas.Any(d => d.degree == degree)) throw new ScenarioConfigurationException("Invalid trait degree: " + name);
            foreach (var t in p.story.traits.allTraits.Where(t => t.def == def).ToList()) p.story.traits.RemoveTrait(t);
            p.story.traits.GainTrait(new Trait(def, degree));
            if (!p.story.traits.HasTrait(def, degree)) throw new InvalidOperationException("Trait rejected: " + name);
        }
        private static void SetSkill(Pawn p, string name, int level, string passion)
        {
            Need(p.skills, "Skills"); if (level < 0 || level > 20) throw new ScenarioConfigurationException("Skill level must be 0..20");
            var skill = p.skills.GetSkill(Def<SkillDef>(name));
            if (skill.TotallyDisabled) throw new ScenarioConfigurationException("Skill disabled by native pawn constraints: " + name);
            skill.Level = level; if (passion != null) skill.passion = Enum.Parse<Passion>(passion, true);
            if (skill.Level != level) throw new InvalidOperationException("Skill rejected: " + name);
        }
        private void Equip(Pawn p, XElement item)
        {
            Need(p.equipment, "Equipment");
            var weapon = MakeThing(A(item, "def"), A(item, "stuff")) as ThingWithComps ?? throw new ScenarioConfigurationException("Weapon requires ThingWithComps");
            owned.Add(weapon); ApplyQuality(weapon, A(item, "quality"));
            if (!weapon.def.IsWeapon) throw new ScenarioConfigurationException("Not a weapon: " + weapon.def.defName);
            p.equipment.DestroyAllEquipment(); p.equipment.AddEquipment(weapon);
            if (p.equipment.Primary != weapon) throw new InvalidOperationException("Equipment rejected");
        }
        private static void EnsureAbility(Pawn p, string name)
        { Need(p.abilities, "Abilities"); var def = Def<AbilityDef>(name); p.abilities.GainAbility(def); if (p.abilities.GetAbility(def) == null) throw new InvalidOperationException("Ability rejected"); }
        private static BodyPartRecord Part(Pawn p, XElement e)
        {
            var name = A(e, "part"); if (name == null) return null;
            var parts = p.RaceProps.body.AllParts.Where(part => PartPath(part) == name || part.def.defName == name).ToList();
            if (parts.Count != 1) throw new ScenarioConfigurationException("Body part must resolve uniquely (use full path): " + name);
            if (p.health.hediffSet.PartIsMissing(parts[0])) throw new ScenarioConfigurationException("Body part is missing: " + name);
            return parts[0];
        }
        private static string PartPath(BodyPartRecord p) => p.parent == null ? p.def.defName : PartPath(p.parent) + "/" + p.def.defName + "[" + p.parent.parts.IndexOf(p) + "]";
        private static void AddHediff(Pawn p, XElement e)
        {
            var def = Def<HediffDef>(A(e, "def")); var part = Part(p, e);
            var h = p.health.hediffSet.hediffs.FirstOrDefault(h => h.def == def && h.Part == part);
            if (h == null) { h = HediffMaker.MakeHediff(def, p, part); p.health.AddHediff(h, part); }
            if (e.Attribute("severity") != null) h.Severity = (float)ScenarioLoader.D(e, "severity");
            if (e.Attribute("durationTicks") != null)
            { var comp = h.TryGetComp<HediffComp_Disappears>() ?? throw new ScenarioConfigurationException("Hediff has no disappearing comp: " + def.defName); comp.ticksToDisappear = I(e, "durationTicks"); }
            if (!p.health.hediffSet.hediffs.Contains(h) || e.Attribute("severity") != null && Math.Abs(h.Severity - ScenarioLoader.D(e, "severity")) > .001) throw new InvalidOperationException("Hediff exact override rejected: " + def.defName);
        }
        private static void RemoveHediff(Pawn p, XElement e)
        { var part = Part(p, e); foreach (var h in p.health.hediffSet.hediffs.Where(h => h.def.defName == A(e, "def") && (e.Attribute("part") == null || h.Part == part)).ToList()) p.health.RemoveHediff(h); }

        public bool Execute(XElement step, bool first)
        {
            var type = step.Name.LocalName;
            if (type == "Screenshot") return screenshot(step);
            if (type == "CastAbility")
            {
                var pawn = Pawn(A(step, "actor"));
                if (first)
                {
                    if (A(step, "force", "false") != "false") throw new ScenarioConfigurationException("External scenes do not force abilities");
                    casting = pawn.abilities?.GetAbility(Def<AbilityDef>(A(step, "def"))) ?? throw new ScenarioConfigurationException("Actor lacks ability");
                    var target = new LocalTargetInfo(One(A(step, "target")));
                    if (!casting.CanCast || !casting.CanQueueCast || !casting.CanApplyOn(target) || !casting.verb.ValidateTarget(target, false)) throw new InvalidOperationException("Native ability prerequisites/target rejected: " + casting.def.defName + " " + casting.CanCast.Reason);
                    castBaseline = casting.lastCastTick; castStarted = false;
                    trace("cast.accepted", casting.def.defName + "; cooldown=" + casting.CooldownTicksRemaining + "; charges=" + casting.RemainingCharges);
                    // Native job API avoids opening a confirmation dialog, while retaining its validation and resource rules.
                    if (casting.def.verbProperties.nonInterruptingSelfCast) casting.verb.TryStartCastOn(pawn);
                    else pawn.jobs.TryTakeOrderedJob(casting.GetJob(target, LocalTargetInfo.Invalid), JobTag.Misc);
                }
                if (!castStarted && (pawn.CurJob?.ability == casting || casting.verb.state == VerbState.Bursting || casting.lastCastTick != castBaseline))
                { castStarted = true; trace("cast.started", casting.def.defName); }
                if (casting.lastCastTick != castBaseline)
                { trace("cast.completed", casting.def.defName + "; lastCastTick=" + casting.lastCastTick + "; cooldown=" + casting.CooldownTicksRemaining); return true; }
                return false;
            }
            if (type == "Attack")
            {
                var p = Pawn(A(step, "actor")); var target = One(A(step, "target"));
                if (first)
                {
                    attackTick = GenTicks.TicksGame;
                    if (A(step, "melee", "false") == "true") p.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.AttackMelee, target), JobTag.Misc);
                    else if (!p.TryStartAttack(target)) throw new InvalidOperationException("Native attack rejected");
                    trace("attack.started", p.GetUniqueLoadID() + " -> " + target.GetUniqueLoadID());
                }
                return GenTicks.TicksGame > attackTick;
            }
            if (!first) return true;
            if (type == "Equip") Equip(Pawn(A(step, "actor")), step);
            else if (type == "DropEquipment")
            {
                var p = Pawn(A(step, "actor"));
                if (p.equipment?.Primary == null || !p.equipment.TryDropEquipment(p.equipment.Primary, out var dropped, p.Position, false)) throw new InvalidOperationException("Native equipment drop rejected");
                trace("equipment.dropped", dropped.GetUniqueLoadID());
            }
            else if (type == "CreateThing")
            {
                var t = MakeThing(A(step, "def"), A(step, "stuff")); owned.Add(t); t.stackCount = I(step, "count", 1);
                if (t is Pawn || t is Corpse) throw new ScenarioConfigurationException("CreateThing cannot bypass Pawn/Corpse generation");
                var cell = new IntVec3(I(step, "x", Map.Center.x), 0, I(step, "z", Map.Center.z));
                if (!cell.InBounds(Map) || !cell.Standable(Map)) throw new ScenarioConfigurationException("CreateThing cell unavailable");
                GenSpawn.Spawn(t, cell, Map, WipeMode.Vanish);
                trace("thing.created", t.GetUniqueLoadID());
            }
            else if (type == "AddHediff") AddHediff(Pawn(A(step, "actor")), step);
            else if (type == "RemoveHediff") RemoveHediff(Pawn(A(step, "actor")), step);
            else if (type == "Destroy") foreach (var thing in Select(A(step, "actor"))) thing.Destroy();
            else if (type == "Kill") Pawn(A(step, "actor")).Kill(null);
            else if (type == "Pause") { AutoTestBootstrap.SetTemporaryPauseBypass(true); Find.TickManager.CurTimeSpeed = TimeSpeed.Paused; }
            else if (type == "Spawn")
            {
                var t = One(A(step, "actor")); if (t.Spawned || t.Destroyed) throw new InvalidOperationException("Spawn requires a prepared unspawned actor");
                GenSpawn.Spawn(t, Map.Center, Map, WipeMode.Vanish);
            }
            else throw new ScenarioConfigurationException("Unsupported action: " + type);
            trace("action." + type, step.ToString(SaveOptions.DisableFormatting));
            return true;
        }

        public ScenarioSample Sample(XElement c)
        {
            var name = c.Name.LocalName;
            var sample = new ScenarioSample();
            var selected = A(c, "actor") != null ? Select(A(c, "actor")) : A(c, "group") != null ? Select(A(c, "group")) : new List<Thing>();
            sample.TargetCount = selected.Count;
            if (name == "ActorCount") { if (selected.Count == I(c, "equals")) sample.Matches.Add("count"); return sample; }
            if (name == "ThingExists")
            {
                // Include carried/equipped things: dropping an old item never becomes a creation event.
                var things = A(c, "actor") == null ? Map.listerThings.AllThings.Concat(owned).Concat(Map.mapPawns.AllPawns.SelectMany(p => p.equipment?.AllEquipmentListForReading.Cast<Thing>() ?? Enumerable.Empty<Thing>())).Concat(Map.mapPawns.AllPawns.SelectMany(p => p.apparel?.WornApparel.Cast<Thing>() ?? Enumerable.Empty<Thing>())).Concat(Map.mapPawns.AllPawns.SelectMany(p => p.inventory?.innerContainer.Cast<Thing>() ?? Enumerable.Empty<Thing>())).Distinct() : selected;
                sample.TargetCount = Math.Max(1, A(c, "actor") == null ? 1 : selected.Count);
                foreach (var t in things.Where(t => !t.Destroyed && (A(c, "def") == null || t.def.defName == A(c, "def")))) sample.Matches.Add(t.GetUniqueLoadID());
                return sample;
            }
            foreach (var thing in selected)
            {
                var p = thing as Pawn;
                bool match;
                if (name == "HasHediff")
                {
                    Need(p, "Pawn");
                    foreach (var h in p.health.hediffSet.hediffs.Where(h => h.def.defName == A(c, "def")))
                    {
                        var key = h.GetUniqueLoadID();
                        sample.Existing.Add(key); sample.Owners[key] = p.GetUniqueLoadID();
                        if (h.Severity >= ScenarioLoader.D(c, "severityMin", 0) && h.Severity <= ScenarioLoader.D(c, "severityMax", double.MaxValue)) sample.Matches.Add(key);
                    }
                    continue;
                }
                else if (name == "ActorState") match = A(c, "state") switch { "alive" => p != null && !p.Dead && !p.Destroyed, "dead" => p != null && p.Dead, "downed" => p?.Downed == true, "standing" => p != null && !p.Downed && !p.Dead, "spawned" => thing.Spawned, "destroyed" => thing.Destroyed, _ => throw new ScenarioConfigurationException("Unknown actor state") };
                else if (name == "CorpseState") match = thing is Corpse corpse && A(c, "state") switch { "valid" => corpse.InnerPawn?.Dead == true, "spawned" => corpse.Spawned && corpse.InnerPawn?.Dead == true, "destroyed" => corpse.Destroyed, _ => throw new ScenarioConfigurationException("Unknown corpse state") };
                else if (name == "Equipped") match = p?.equipment?.Primary?.def.defName == A(c, "def");
                else if (name == "Wearing") match = p?.apparel?.WornApparel.Any(app => app.def.defName == A(c, "def")) == true;
                else if (name == "HasTrait") match = p?.story?.traits.HasTrait(Def<TraitDef>(A(c, "def")), I(c, "degree")) == true;
                else if (name == "SkillLevel") { var skill = p?.skills?.GetSkill(Def<SkillDef>(A(c, "def"))); match = skill != null && skill.Level >= ScenarioLoader.D(c, "min", 0) && skill.Level <= ScenarioLoader.D(c, "max", 20); }
                else if (name == "HasAbility") match = p?.abilities?.GetAbility(Def<AbilityDef>(A(c, "def"))) != null;
                else throw new ScenarioConfigurationException("Unsupported probe: " + name);
                if (match) sample.Matches.Add(thing.GetUniqueLoadID());
            }
            return sample;
        }

        public void Cleanup()
        {
            AutoTestBootstrap.SetTemporaryPauseBypass(false);
            var errors = new List<Exception>();
            if (source != null && Find.Maps.Contains(source)) { Current.Game.CurrentMap = source; Find.CameraDriver.JumpToCurrentMapLoc(source.Center); }
            // Remove only owned objects; destroy map first so native death/despawn hooks have their normal map.
            if (handle != null) { try { maps.Destroy(handle.Map, handle.Parent); } catch (Exception ex) { errors.Add(ex); } }
            foreach (var thing in owned.ToList())
            {
                try { if (thing is Pawn p && Find.WorldPawns.Contains(p)) Find.WorldPawns.RemovePawn(p); if (!thing.Destroyed) thing.Destroy(DestroyMode.Vanish); }
                catch (Exception ex) { errors.Add(ex); }
            }
            trace("cleanup.ownership", "owned=" + owned.Count + "; live=" + owned.Count(t => !t.Destroyed) + "; arenaRegistered=" + (handle != null && Find.Maps.Contains(handle.Map)) + "; sourceRestored=" + (Find.CurrentMap == source));
            actors.Clear(); references.Clear(); teams.Clear();
            if (errors.Count > 0) throw new AggregateException(errors);
        }

        internal Thing One(string id) => Select(id).Count == 1 ? Select(id)[0] : throw new ScenarioConfigurationException("A single actor is required: " + id);
        private Pawn Pawn(string id) => One(id) as Pawn ?? throw new ScenarioConfigurationException("Pawn actor required: " + id);
        private List<Thing> Select(string id) => actors.TryGetValue(id, out var result) ? result : throw new ScenarioConfigurationException("Actor is a Def/terrain reference or missing: " + id);
        private static string A(XElement e, string name, string fallback = null) => ScenarioLoader.A(e, name, fallback);
        private static int I(XElement e, string name, int fallback = 0) => ScenarioLoader.I(e, name, fallback);
        private static T Def<T>(string name) where T : Def, new() => DefDatabase<T>.GetNamedSilentFail(name ?? "") ?? throw new ScenarioConfigurationException("Missing " + typeof(T).Name + ": " + name);
        private static Thing MakeThing(string name, string stuff)
        {
            var def = Def<ThingDef>(name); var material = stuff == null ? (def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null) : Def<ThingDef>(stuff);
            if (material != null && (!def.MadeFromStuff || !GenStuff.AllowedStuffsFor(def).Contains(material))) throw new ScenarioConfigurationException("Invalid stuff for " + name);
            return ThingMaker.MakeThing(def, material);
        }
        private static void ApplyQuality(Thing t, string quality)
        { if (quality != null) { var comp = t.TryGetComp<CompQuality>() ?? throw new ScenarioConfigurationException("Thing has no quality: " + t.def.defName); comp.SetQuality(Enum.Parse<QualityCategory>(quality, true), ArtGenerationContext.Outsider); } }
        private static string KindFingerprint(PawnKindDef k) => k.race.defName + ":" + k.minGenerationAge + ":" + k.maxGenerationAge + ":" + k.fixedGender + ":" + k.apparelMoney + ":" + k.weaponMoney + ":" + string.Join(",", k.apparelTags ?? new List<string>()) + ":" + string.Join(",", k.weaponTags ?? new List<string>());
        private static string Describe(Pawn p) => p.GetUniqueLoadID() + "; kind=" + p.kindDef.defName + "; faction=" + p.Faction?.def.defName + "; apparel=" + string.Join(",", p.apparel?.WornApparel.Select(a => a.def.defName) ?? Enumerable.Empty<string>()) + "; weapon=" + p.equipment?.Primary?.def.defName + "; hediffs=" + string.Join(",", p.health.hediffSet.hediffs.Select(h => h.def.defName + "=" + h.Severity));

        private static object ResolveDef(string type, string name) => type switch {
            "ThingDef" => Def<ThingDef>(name), "PawnKindDef" => Def<PawnKindDef>(name), "AbilityDef" => Def<AbilityDef>(name), "RecipeDef" => Def<RecipeDef>(name), "HediffDef" => Def<HediffDef>(name), "TerrainDef" => Def<TerrainDef>(name), "GeneDef" => Def<GeneDef>(name), "XenotypeDef" => Def<XenotypeDef>(name), "HairDef" => Def<HairDef>(name), "HeadTypeDef" => Def<HeadTypeDef>(name), "BodyTypeDef" => Def<BodyTypeDef>(name), _ => throw new ScenarioConfigurationException("Unsupported Def reference type: " + type) };
        private static void ValidateDefs(XElement c, ScenarioCase definition)
        {
            foreach (var e in c.Descendants())
            {
                try
                {
                foreach (var attr in e.Attributes().Where(a => new[] { "def", "matchDef", "withDef", "stuff", "defName" }.Contains(a.Name.LocalName)))
                {
                    var type = attr.Name == "defName" ? A(e, "defType") : attr.Name == "stuff" || attr.Name == "matchDef" || attr.Name == "withDef" ? "ThingDef" :
                        new[] { "HasHediff", "AddHediff", "RemoveHediff" }.Contains(e.Name.LocalName) || e.Parent?.Name == "Hediffs" ? "HediffDef" :
                        e.Name == "HasAbility" || e.Name == "CastAbility" || e.Parent?.Name == "Abilities" ? "AbilityDef" :
                        e.Name == "SkillLevel" || e.Parent?.Name == "Skills" ? "SkillDef" : e.Name == "HasTrait" || e.Parent?.Name == "Traits" ? "TraitDef" : e.Name == "TerrainArea" ? "TerrainDef" : "ThingDef";
                    if (type == "SkillDef") Def<SkillDef>(attr.Value); else if (type == "TraitDef") Def<TraitDef>(attr.Value); else ResolveDef(type, attr.Value);
                }
                if (e.Name == "basePawnKind") Def<PawnKindDef>(e.Value.Trim());
                if (e.Name == "race") Def<ThingDef>(e.Value.Trim());
                if (e.Name == "hair") Def<HairDef>(e.Value.Trim());
                if (e.Name == "headType") Def<HeadTypeDef>(e.Value.Trim());
                if (e.Name == "bodyType") Def<BodyTypeDef>(e.Value.Trim());
                if (e.Name == "li" && e.Parent?.Name == "abilities") Def<AbilityDef>(e.Value.Trim());
                if (e.Name == "skill" && e.Parent?.Parent?.Name == "skills") Def<SkillDef>(e.Value.Trim());
                if (e.Name == "def" && e.Parent?.Parent?.Name == "forcedTraits") Def<TraitDef>(e.Value.Trim());
                if (e.Attribute("part") != null)
                {
                    var actor = e.Ancestors().FirstOrDefault(a => new[] { "Pawn", "PawnGroup", "Corpse" }.Contains(a.Name.LocalName));
                    if (actor == null && A(e, "actor") != null)
                    {
                        var actorId = A(e, "actor").Split('[')[0].Replace(".innerPawn", "");
                        actor = c.Element("Actors").Elements().FirstOrDefault(a => A(a, "id") == actorId);
                    }
                    if (actor == null) throw new ScenarioConfigurationException("Body part requires a pawn actor");
                    var race = actor.Element("race") == null ? Def<PawnKindDef>(actor.Element("basePawnKind")?.Value).race : Def<ThingDef>(actor.Element("race").Value);
                    if (race.race.body.AllParts.Count(p => p.def.defName == A(e, "part") || PartPath(p) == A(e, "part")) != 1) throw new ScenarioConfigurationException("Body part must resolve uniquely: " + A(e, "part"));
                }
                }
                catch (ScenarioConfigurationException ex)
                {
                    var location = definition.SourceLocations.TryGetValue(ScenarioLoader.ElementKey(e), out var exact) ? exact : definition.Source;
                    throw new ScenarioConfigurationException(location + ": " + ex.Message);
                }
            }
        }
    }

    // Observe even add+remove in one tick; no input-controlled reflection or arbitrary debug calls.
    [HarmonyPatch]
    internal static class ScenarioHealthObservationPatch
    {
        private static IEnumerable<MethodBase> TargetMethods() => typeof(Pawn_HealthTracker).GetMethods().Where(m => m.Name == "AddHediff" || m.Name == "RemoveHediff");
        private static void Postfix() { ScenarioGameBackend.NativeObservation?.Invoke(); }
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.SpawnSetup))]
    internal static class ScenarioSpawnObservationPatch
    {
        private static void Postfix() { ScenarioGameBackend.NativeObservation?.Invoke(); }
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.Destroy))]
    internal static class ScenarioDestroyObservationPatch
    {
        private static void Postfix() { ScenarioGameBackend.NativeObservation?.Invoke(); }
    }
}
