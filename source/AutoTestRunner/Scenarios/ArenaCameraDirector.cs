using System;
using System.Collections.Generic;
using System.Linq;

namespace LogSyncMod.AutoTest.Scenarios
{
    internal enum ArenaCameraMode { Auto, Overview, Battle, Follow, Close }

    internal sealed class ArenaCameraSubject
    {
        internal string Id, Team, TargetId;
        internal float X, Z, Health = 1;
        internal double DamageDealt, DamageTaken;
    }

    // Chooses an observation shot only. It never changes simulation speed, jobs or results.
    internal sealed class ArenaCameraDirector
    {
        private sealed class History
        {
            internal double Dealt, Taken, Recent;
        }
        private sealed class Contact
        {
            internal ArenaCameraSubject Actor, Enemy;
            internal float X, Z, Distance;
            internal double Interest;
        }
        private sealed class Region
        {
            internal readonly List<Contact> Contacts = new();
            internal double Interest;
        }

        private readonly Dictionary<string, History> history = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ArenaCameraSubject> subjects = new(StringComparer.Ordinal);
        private List<ArenaCameraSubject> roster = new();
        private List<Contact> contacts = new();
        private float clock, shotStarted, regionStarted, nextObservation, nextOverview = 22, lastObservation;
        private bool forceObservation = true, fighting;
        private string preferredFocus, lastFollowTeam;
        private Contact anchor;
        private int introductionStage = -1;
        private readonly Dictionary<string, int> initialCounts = new(StringComparer.Ordinal);
        internal const float IntroductionSeconds = 4;
        internal int IntroductionStage => introductionStage;
        internal void SetIntroduction(float? elapsed)
        {
            var stage = elapsed.HasValue ? elapsed.Value < 2 ? 0 : 1 : -1;
            if (stage != introductionStage) { introductionStage = stage; forceObservation = true; }
        }
        internal ArenaCameraMode Mode { get; private set; }
        internal ArenaCameraMode Shot { get; private set; } = ArenaCameraMode.Overview;
        internal string FocusId { get; private set; }
        internal string OpponentId { get; private set; }
        internal string Reason { get; private set; } = "开场阵形";
        internal int Switches { get; private set; }
        internal bool Compact => Shot is ArenaCameraMode.Follow or ArenaCameraMode.Close;
        internal float MaximumSize => Shot switch { ArenaCameraMode.Close => 10, ArenaCameraMode.Follow => 16, ArenaCameraMode.Battle => 20, _ => 28 };

        internal void SetMode(ArenaCameraMode mode)
        {
            Mode = mode; preferredFocus = null; forceObservation = true;
            // Returning to automatic always re-establishes the battle's context first.
            if (mode == ArenaCameraMode.Auto) { if (fighting) SetShot(ArenaCameraMode.Battle, "恢复自动导播"); nextOverview = clock + 20; }
        }

        internal void Select(string id)
        {
            if (Mode is not (ArenaCameraMode.Follow or ArenaCameraMode.Close)) return;
            preferredFocus = id; forceObservation = true;
        }

        internal bool Advance(float realDelta, bool running, bool paused)
        {
            fighting = running;
            if (running && !paused) clock += Math.Max(0, realDelta);
            return forceObservation || running && !paused && clock >= nextObservation;
        }

        internal void Observe(IEnumerable<ArenaCameraSubject> values)
        {
            forceObservation = false; nextObservation = clock + .35f;
            roster = values.OrderBy(p => p.Id, StringComparer.Ordinal).ToList();
            subjects.Clear();
            var decay = Math.Exp(-Math.Max(0, clock - lastObservation) / 3);
            lastObservation = clock;
            foreach (var actor in roster)
            {
                subjects.Add(actor.Id, actor);
                if (!history.TryGetValue(actor.Id, out var past)) history.Add(actor.Id, past = new History());
                past.Recent = past.Recent * decay + Math.Min(1000, Math.Max(0, actor.DamageDealt - past.Dealt) + Math.Max(0, actor.DamageTaken - past.Taken));
                past.Dealt = actor.DamageDealt; past.Taken = actor.DamageTaken;
            }
            if (roster.Count == 0) return;
            if (initialCounts.Count == 0)
                foreach (var team in roster.GroupBy(p => p.Team)) initialCounts.Add(team.Key, team.Count());
            contacts = FindContacts();
            if (introductionStage >= 0)
            {
                var teamId = introductionStage == 0 ? "A" : "B";
                var team = roster.Where(p => p.Team == teamId).ToList();
                if (team.Count == 0) team = roster;
                var cx = team.Average(p => p.X); var cz = team.Average(p => p.Z);
                FocusId = team.OrderBy(p => Square(p.X - cx) + Square(p.Z - cz)).ThenBy(p => p.Id, StringComparer.Ordinal).First().Id;
                OpponentId = null;
                SetShot(ArenaCameraMode.Close, introductionStage == 0 ? "左方登场特写" : "右方登场特写");
                return;
            }
            if (!fighting) { SetShot(ArenaCameraMode.Overview, "开场阵形"); return; }

            var targetValid = FocusId != null && subjects.ContainsKey(FocusId);
            if (Mode != ArenaCameraMode.Auto)
            {
                if (Mode == ArenaCameraMode.Overview) { SetShot(Mode, "手动锁定"); return; }
                if (Mode == ArenaCameraMode.Battle) { ChooseBattle(true); SetShot(Mode, "手动锁定主战区"); return; }
                var selected = preferredFocus != null && subjects.TryGetValue(preferredFocus, out var pinned) ? pinned
                    : targetValid ? subjects[FocusId] : BestContact()?.Actor ?? roster[0];
                Focus(selected, Mode);
                return;
            }

            if (clock < 3) { SetShot(ArenaCameraMode.Overview, "开场阵形"); return; }

            // A lone fighter deserves sustained screen time, including a last survivor.
            var singles = roster.GroupBy(p => p.Team).Where(g => g.Count() == 1).Select(g => g.First()).ToList();
            if (singles.Count > 0 && roster.Select(p => p.Team).Distinct().Count() > 1)
            {
                var actor = singles.FirstOrDefault(p => p.Id == FocusId)
                    ?? singles.OrderBy(p => initialCounts[p.Team]).ThenByDescending(p => history[p.Id].Recent).First();
                var beat = (clock - 3) % 22;
                if (beat < 15) Focus(actor, ArenaCameraMode.Follow);
                else if (beat < 18) Focus(actor, ArenaCameraMode.Close);
                else { ChooseBattle(true); SetShot(ArenaCameraMode.Battle, "单体交战，回看战区"); }
                return;
            }

            if (contacts.Count == 0)
            {
                SetShot(ArenaCameraMode.Overview, "等待接敌"); return;
            }

            // Keep a decisive small engagement on screen instead of rotating away mid-duel.
            if (roster.Count <= 6 && roster.Select(p => p.Team).Distinct().Count() > 1)
            {
                var subject = targetValid ? subjects[FocusId] : BestContact()?.Actor;
                if (subject != null) Focus(subject, ArenaCameraMode.Close);
                return;
            }

            var age = clock - shotStarted;
            if (Shot == ArenaCameraMode.Overview)
            {
                if (age >= 2.5f) { ChooseBattle(true); SetShot(ArenaCameraMode.Battle, "主战区交火"); }
                return;
            }
            if (Shot == ArenaCameraMode.Battle && clock >= nextOverview && age >= 5)
            {
                nextOverview = clock + 22; SetShot(ArenaCameraMode.Overview, "回看全局"); return;
            }
            if (Shot == ArenaCameraMode.Battle)
            {
                ChooseBattle(!targetValid || age >= 8);
                if (age >= 12)
                {
                    var contact = BestContact(preferOtherTeam: true);
                    if (contact != null) { lastFollowTeam = contact.Actor.Team; Focus(contact.Actor, ArenaCameraMode.Follow); }
                }
            }
            else if (!targetValid)
            {
                ChooseBattle(true); SetShot(ArenaCameraMode.Battle, "重新寻找交战目标");
            }
            else
            {
                var actor = subjects[FocusId];
                var enemy = EnemyFor(actor);
                if (enemy == null || Distance(actor, enemy) > 32)
                { ChooseBattle(true); SetShot(ArenaCameraMode.Battle, "目标脱离交战"); }
                else if (Shot == ArenaCameraMode.Follow && age >= 6)
                {
                    if (Distance(actor, enemy) <= 12) Focus(actor, ArenaCameraMode.Close);
                    else { ChooseBattle(true); SetShot(ArenaCameraMode.Battle, "主战区交火"); }
                }
                else if (Shot == ArenaCameraMode.Close && age >= 4.5f)
                { ChooseBattle(true); SetShot(ArenaCameraMode.Battle, "主战区交火"); }
                else OpponentId = enemy.Id;
            }
        }

        private List<Contact> FindContacts()
        {
            var grid = new Dictionary<(int x, int z), List<ArenaCameraSubject>>();
            foreach (var actor in roster)
            {
                var key = ((int)Math.Floor(actor.X / 12), (int)Math.Floor(actor.Z / 12));
                if (!grid.TryGetValue(key, out var cell)) grid.Add(key, cell = new());
                cell.Add(actor);
            }
            var result = new List<Contact>();
            foreach (var actor in roster)
            {
                ArenaCameraSubject enemy = null;
                if (actor.TargetId != null && subjects.TryGetValue(actor.TargetId, out var target) && target.Team != actor.Team) enemy = target;
                if (enemy == null)
                {
                    var cx = (int)Math.Floor(actor.X / 12); var cz = (int)Math.Floor(actor.Z / 12);
                    var closest = float.MaxValue;
                    for (var x = cx - 2; x <= cx + 2; x++)
                    for (var z = cz - 2; z <= cz + 2; z++)
                    {
                        if (!grid.TryGetValue((x, z), out var cell)) continue;
                        // Bound work in crowded armies; exact combat targeting remains native.
                        var checkedEnemies = 0;
                        foreach (var candidate in cell)
                        {
                            if (candidate.Team == actor.Team) continue;
                            var distance = Distance(actor, candidate);
                            if (distance < closest) { closest = distance; enemy = candidate; }
                            if (++checkedEnemies == 8) break;
                        }
                    }
                }
                if (enemy == null) continue;
                var separation = Distance(actor, enemy);
                if (separation > 40 && (actor.TargetId != enemy.Id || history[actor.Id].Recent < 1)) continue;
                // For long-range fire, show the shooter and attack direction, not empty ground halfway to the target.
                var centerWeight = separation <= 26 ? .5f : 8 / separation;
                result.Add(new Contact { Actor = actor, Enemy = enemy, X = actor.X + (enemy.X - actor.X) * centerWeight, Z = actor.Z + (enemy.Z - actor.Z) * centerWeight,
                    Distance = separation, Interest = 1 + 4 / (1 + separation / 8) + Math.Log(1 + history[actor.Id].Recent) * 2
                        + (history[actor.Id].Recent > 0 ? Math.Clamp(1 - actor.Health, 0, 1) : 0) });
            }
            return result;
        }

        private Contact BestContact(bool preferOtherTeam = false)
        {
            return contacts.OrderByDescending(c => c.Interest * (preferOtherTeam && c.Actor.Team != lastFollowTeam ? 1.35 : 1))
                .ThenBy(c => c.Actor.Id, StringComparer.Ordinal).FirstOrDefault();
        }

        private void ChooseBattle(bool allowNewRegion)
        {
            var regions = new Dictionary<(int x, int z), Region>();
            foreach (var contact in contacts)
            {
                var key = ((int)Math.Floor(contact.X / 12), (int)Math.Floor(contact.Z / 12));
                if (!regions.TryGetValue(key, out var region)) regions.Add(key, region = new());
                region.Contacts.Add(contact); region.Interest += contact.Interest;
            }
            var best = regions.Values.OrderByDescending(r => r.Interest).FirstOrDefault();
            if (best == null) { FocusId = roster[0].Id; OpponentId = null; anchor = null; return; }
            var currentContact = contacts.FirstOrDefault(c => c.Actor.Id == FocusId);
            var currentRegion = currentContact == null ? null : regions[((int)Math.Floor(currentContact.X / 12), (int)Math.Floor(currentContact.Z / 12))];
            if (currentRegion != null && (!allowNewRegion || clock - regionStarted < 8 || currentRegion.Interest * 1.5 >= best.Interest))
                anchor = currentContact;
            else
            {
                anchor = best.Contacts.OrderByDescending(c => c.Interest).ThenBy(c => c.Actor.Id, StringComparer.Ordinal).First();
                regionStarted = clock;
            }
            FocusId = anchor.Actor.Id; OpponentId = anchor.Enemy.Id;
        }

        private ArenaCameraSubject EnemyFor(ArenaCameraSubject actor)
        {
            if (actor.TargetId != null && subjects.TryGetValue(actor.TargetId, out var target) && target.Team != actor.Team) return target;
            var nearby = contacts.FirstOrDefault(c => c.Actor.Id == actor.Id)?.Enemy;
            if (OpponentId != null && subjects.TryGetValue(OpponentId, out var previous) && previous.Team != actor.Team
                && (Distance(actor, previous) <= 24 || nearby == null)) return previous;
            return nearby
                ?? roster.Where(p => p.Team != actor.Team).OrderBy(p => Distance(actor, p)).FirstOrDefault();
        }

        private void Focus(ArenaCameraSubject actor, ArenaCameraMode desired)
        {
            var enemy = EnemyFor(actor);
            FocusId = actor.Id; OpponentId = enemy?.Id;
            var separation = enemy == null ? float.MaxValue : Distance(actor, enemy);
            // A long-range shot must retain context rather than magnify an empty midpoint.
            SetShot(desired == ArenaCameraMode.Close && separation > 12 ? ArenaCameraMode.Follow : desired,
                separation > 12 && desired == ArenaCameraMode.Close ? "目标较远，跟随取景" : Mode == ArenaCameraMode.Auto ? "交战焦点" : "手动锁定，可点选人物");
        }

        private void SetShot(ArenaCameraMode shot, string reason)
        {
            if (Shot != shot) { Shot = shot; shotStarted = clock; Switches++; }
            Reason = reason;
        }

        internal (float x, float z, float size)? Frame(float width, float height, bool overlay)
        {
            if (roster.Count == 0) return null;
            var points = new List<(float x, float z)>();
            var minimum = 16f;
            if (Shot == ArenaCameraMode.Overview)
            {
                // Trim only large rosters' extremes, so a stray pawn cannot dominate the overview.
                var xs = roster.Select(p => p.X).OrderBy(v => v).ToList();
                var zs = roster.Select(p => p.Z).OrderBy(v => v).ToList();
                var trim = roster.Count >= 20 ? roster.Count / 20 : 0;
                points.Add((xs[trim], zs[trim])); points.Add((xs[xs.Count - trim - 1], zs[zs.Count - trim - 1]));
            }
            else if (FocusId != null && subjects.TryGetValue(FocusId, out var actor))
            {
                var enemy = introductionStage >= 0 ? null : EnemyFor(actor);
                if (Shot == ArenaCameraMode.Battle)
                {
                    minimum = 11;
                    var cx = anchor?.X ?? actor.X; var cz = anchor?.Z ?? actor.Z;
                    points.AddRange(roster.Where(p => Square(p.X - cx) + Square(p.Z - cz) <= 13 * 13).Select(p => (p.X, p.Z)));
                    if (points.Count == 0) points.Add((actor.X, actor.Z));
                }
                else
                {
                    minimum = Shot == ArenaCameraMode.Close ? 7 : 9;
                    points.Add((actor.X, actor.Z));
                    if (enemy != null)
                    {
                        var distance = Distance(actor, enemy);
                        var lead = distance <= (Shot == ArenaCameraMode.Close ? 12 : 22) ? 1 : 8 / Math.Max(1, distance);
                        points.Add((actor.X + (enemy.X - actor.X) * lead, actor.Z + (enemy.Z - actor.Z) * lead));
                    }
                }
            }
            if (points.Count == 0) points.Add((roster[0].X, roster[0].Z));
            var panel = Compact ? GroupCombatPresentation.CompactPanelWidth(width) : GroupCombatPresentation.PanelWidth(width);
            return GroupCombatPresentation.Frame(points, width, height, overlay, minimum, panel, 160, MaximumSize);
        }

        internal static string Label(ArenaCameraMode mode) => mode switch {
            ArenaCameraMode.Auto => "自动", ArenaCameraMode.Overview => "全局", ArenaCameraMode.Battle => "战区", ArenaCameraMode.Follow => "跟随", _ => "近景"
        };
        private static float Square(float value) => value * value;
        private static float Distance(ArenaCameraSubject a, ArenaCameraSubject b) => (float)Math.Sqrt(Square(a.X - b.X) + Square(a.Z - b.Z));
    }
}
