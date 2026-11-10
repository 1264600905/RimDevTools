using System;
using System.Collections.Generic;
using System.Linq;

namespace LogSyncMod.AutoTest.Scenarios
{
    internal enum ArenaCameraMode { Auto, Overview, Battle, Follow, Close }

    internal sealed class ArenaCameraSubject
    {
        internal string Id, Team, TargetId, AttackingId;
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
        private float clock, regionStarted, nextObservation, lastObservation;
        private bool forceObservation = true, fighting;
        private string preferredFocus;
        private Contact anchor;
        private int introductionStage = -1;
        private float introductionElapsed;
        private (float x, float z, float size)? introOverview, introLeft, introRight;
        private float introWidth, introHeight;
        private bool introOverlay;
        private (float x, float z)? overviewCenter;
        internal const float IntroductionHoldSeconds = 1.5f;
        internal const float IntroductionSeconds = 5.6f;
        internal int IntroductionStage => introductionStage;
        internal float IntroductionElapsed => introductionElapsed;
        internal void SetOverviewCenter(float x, float z)
        {
            overviewCenter = (x, z); introOverview = null; forceObservation = true;
        }
        internal void SetIntroduction(float? elapsed)
        {
            introductionElapsed = Math.Max(0, elapsed ?? 0);
            var t = introductionElapsed;
            var stage = !elapsed.HasValue ? -1 : t < .2f ? 0 : t < .8f ? 1 : t < 2.3f ? 2
                : t < 2.9f ? 3 : t < 4.4f ? 4 : t < 5.3f ? 5 : 6;
            if (stage != introductionStage) { introductionStage = stage; forceObservation = true; }
        }
        internal ArenaCameraMode Mode { get; private set; }
        internal ArenaCameraMode Shot { get; private set; } = ArenaCameraMode.Overview;
        internal string FocusId { get; private set; }
        internal string OpponentId { get; private set; }
        internal string Reason { get; private set; } = "开场阵形";
        internal int Switches { get; private set; }
        internal bool Compact => false;
        internal float MaximumSize => introductionStage >= 0 || Shot == ArenaCameraMode.Overview ? 28
            : Shot == ArenaCameraMode.Follow ? float.MaxValue : 20;

        internal void SetMode(ArenaCameraMode mode)
        {
            // The old close-up API uses the stable follow shot; automatic never rotates on a timer.
            Mode = mode == ArenaCameraMode.Close ? ArenaCameraMode.Follow : mode;
            preferredFocus = null; forceObservation = true;
        }

        internal void Select(string id)
        {
            if (Mode is not (ArenaCameraMode.Auto or ArenaCameraMode.Follow)) return;
            preferredFocus = id; forceObservation = true;
        }

        internal bool Advance(float realDelta, bool running, bool paused)
        {
            fighting = running;
            if (running && !paused) clock += Math.Max(0, realDelta);
            return forceObservation || running && !paused && clock >= nextObservation;
        }

        // Only follow shots bypass the observation interval; other shots retain stabilization.
        internal void RefreshFollowPosition(string id, float x, float z)
        {
            if (Shot != ArenaCameraMode.Follow || id != FocusId && id != OpponentId || !subjects.TryGetValue(id, out var subject)) return;
            subject.X = x; subject.Z = z;
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
            contacts = FindContacts();
            if (introductionStage >= 0)
            {
                var teamId = introductionStage <= 2 ? "A" : "B";
                var team = roster.Where(p => p.Team == teamId).ToList();
                if (team.Count == 0) team = roster;
                var cx = team.Average(p => p.X); var cz = team.Average(p => p.Z);
                FocusId = team.OrderBy(p => Square(p.X - cx) + Square(p.Z - cz)).ThenBy(p => p.Id, StringComparer.Ordinal).First().Id;
                OpponentId = null;
                SetShot(introductionStage is 0 or 6 ? ArenaCameraMode.Overview : ArenaCameraMode.Close,
                    introductionStage switch { 0 => "双方阵形", 1 => "拉近左方", 2 => "左方登场特写", 3 => "移向右方", 4 => "右方登场特写", _ => "回到全局" });
                return;
            }
            if (!fighting) { SetShot(ArenaCameraMode.Overview, "开场阵形"); return; }

            if (Mode == ArenaCameraMode.Overview)
            {
                SetShot(ArenaCameraMode.Overview, "手动全局"); return;
            }
            if (Mode == ArenaCameraMode.Battle)
            {
                ChooseBattle(true); SetShot(ArenaCameraMode.Battle, "锁定交战区"); return;
            }
            if (preferredFocus != null)
            {
                if (subjects.TryGetValue(preferredFocus, out var pinned))
                {
                    FocusId = pinned.Id; OpponentId = EnemyFor(pinned)?.Id; anchor = null;
                    SetShot(ArenaCameraMode.Follow, "跟随人物，可点选切换"); return;
                }
                preferredFocus = null;
            }
            if (Mode == ArenaCameraMode.Follow)
            {
                var selected = FocusId != null && subjects.TryGetValue(FocusId, out var current) ? current
                    : contacts.OrderByDescending(c => c.Interest).FirstOrDefault()?.Actor ?? roster[0];
                FocusId = selected.Id; OpponentId = EnemyFor(selected)?.Id; anchor = null;
                SetShot(ArenaCameraMode.Follow, "跟随人物，可点选切换"); return;
            }
            if (Shot == ArenaCameraMode.Overview && (clock < 3 || contacts.Count == 0))
            {
                SetShot(ArenaCameraMode.Overview, "等待接敌"); return;
            }
            ChooseBattle(true);
            SetShot(ArenaCameraMode.Battle, "主战区，可点选关注人物");
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
            if (best == null)
            {
                if (FocusId == null || !subjects.ContainsKey(FocusId)) FocusId = roster[0].Id;
                OpponentId = null; anchor = null; return;
            }
            var currentContact = contacts.FirstOrDefault(c => c.Actor.Id == FocusId);
            var currentRegion = currentContact == null ? null : regions[((int)Math.Floor(currentContact.X / 12), (int)Math.Floor(currentContact.Z / 12))];
            if (currentRegion != null && (!allowNewRegion || clock - regionStarted < 12 || currentRegion.Interest * 2 >= best.Interest))
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
            // Keep an incoming attacker in view even when the followed pawn targets someone else.
            var attackers = roster.Where(p => p.Team != actor.Team && (p.TargetId == actor.Id || p.AttackingId == actor.Id)).ToList();
            var retained = attackers.FirstOrDefault(p => p.Id == OpponentId);
            if (retained != null) return retained;
            var attacker = attackers.OrderBy(p => Distance(actor, p)).ThenBy(p => p.Id, StringComparer.Ordinal).FirstOrDefault();
            if (attacker != null) return attacker;
            if (actor.TargetId != null && subjects.TryGetValue(actor.TargetId, out var target) && target.Team != actor.Team) return target;
            var nearby = contacts.FirstOrDefault(c => c.Actor.Id == actor.Id)?.Enemy;
            if (OpponentId != null && subjects.TryGetValue(OpponentId, out var previous) && previous.Team != actor.Team
                && (Distance(actor, previous) <= 24 || nearby == null)) return previous;
            return nearby
                ?? roster.Where(p => p.Team != actor.Team).OrderBy(p => Distance(actor, p)).FirstOrDefault();
        }

        private void SetShot(ArenaCameraMode shot, string reason)
        {
            if (Shot != shot) { Shot = shot; Switches++; }
            Reason = reason;
        }

        internal (float x, float z, float size)? Frame(float width, float height, bool overlay)
        {
            if (roster.Count == 0) return null;
            if (introductionStage >= 0) return IntroductionFrame(width, height, overlay);
            if (Shot == ArenaCameraMode.Overview) return OverviewFrame(width, height, overlay);
            var actor = FocusId != null && subjects.TryGetValue(FocusId, out var found) ? found : roster[0];
            if (Shot == ArenaCameraMode.Follow)
            {
                var followed = new List<(float x, float z)> { (actor.X, actor.Z) };
                if (OpponentId != null && subjects.TryGetValue(OpponentId, out var opponent) && opponent.Team != actor.Team)
                    followed.Add((opponent.X, opponent.Z));
                return GroupCombatPresentation.Frame(followed, width, height, overlay,
                    roster.Count <= 6 ? 8 : roster.Count <= 16 ? 11 : 14,
                    GroupCombatPresentation.PanelWidth(width), 160, MaximumSize);
            }
            var cx = anchor?.X ?? actor.X; var cz = anchor?.Z ?? actor.Z;
            var points = roster.Where(p => Square(p.X - cx) + Square(p.Z - cz) <= 13 * 13).Select(p => (p.X, p.Z)).ToList();
            if (points.Count == 0) points.Add((actor.X, actor.Z));
            // Use the engagement as the center. Recomputing a bounding-box midpoint
            // made every casualty and every unit crossing the radius move the camera.
            var rx = points.Max(p => Math.Abs(p.Item1 - cx));
            var rz = points.Max(p => Math.Abs(p.Item2 - cz));
            var framed = GroupCombatPresentation.Frame(new[] { (cx - rx, cz - rz), (cx + rx, cz + rz) },
                width, height, overlay, roster.Count <= 6 ? 9 : roster.Count <= 16 ? 12 : 16,
                GroupCombatPresentation.PanelWidth(width), 160, MaximumSize);
            return framed;
        }

        private (float x, float z, float size) OverviewFrame(float width, float height, bool overlay)
        {
            // Trim large armies' outliers consistently before and after the introduction.
            var xs = roster.Select(p => p.X).OrderBy(v => v).ToList();
            var zs = roster.Select(p => p.Z).OrderBy(v => v).ToList();
            var trim = roster.Count >= 20 ? roster.Count / 20 : 0;
            var frame = GroupCombatPresentation.Frame(new[] { (xs[trim], zs[trim]), (xs[xs.Count - trim - 1], zs[zs.Count - trim - 1]) },
                width, height, overlay, 16, GroupCombatPresentation.PanelWidth(width), 160, 28);
            return overviewCenter.HasValue ? (overviewCenter.Value.x, overviewCenter.Value.z, frame.size) : frame;
        }

        private (float x, float z, float size) IntroductionFrame(float width, float height, bool overlay)
        {
            if (!introOverview.HasValue || introWidth != width || introHeight != height || introOverlay != overlay)
            {
                introWidth = width; introHeight = height; introOverlay = overlay;
                introOverview = OverviewFrame(width, height, overlay);
                (float x, float z, float size) Portrait(string teamId)
                {
                    var team = roster.Where(p => p.Team == teamId).ToList();
                    if (team.Count == 0) team = roster;
                    var x = team.Average(p => p.X); var z = team.Average(p => p.Z);
                    // Frame a representative group around the formation's center.
                    var core = team.OrderBy(p => Square(p.X - x) + Square(p.Z - z)).Take(12);
                    // Side panels are hidden throughout the introduction.
                    var minimum = team.Count <= 2 ? 6 : team.Count <= 8 ? 8 : 10;
                    return GroupCombatPresentation.Frame(core.Select(p => (p.X, p.Z)), width, height, overlay, minimum, 0, 160, 12);
                }
                introLeft = Portrait("A"); introRight = Portrait("B");
                var size = Math.Max(introLeft.Value.size, introRight.Value.size);
                // Both sides use the same scale; crossing the arena only pans.
                var left = introLeft.Value; var right = introRight.Value;
                introLeft = (left.x, left.z + (size - left.size) * (overlay ? GroupCombatPresentation.PanelTop + 6 - 160 : 16 - 160) / height, size);
                introRight = (right.x, right.z + (size - right.size) * (overlay ? GroupCombatPresentation.PanelTop + 6 - 160 : 16 - 160) / height, size);
            }
            var wide = introOverview.Value; var a = introLeft.Value; var b = introRight.Value;
            return introductionStage switch {
                0 => wide,
                1 => Blend(wide, a, (introductionElapsed - .2f) / .6f),
                2 => a,
                3 => Blend(a, b, (introductionElapsed - 2.3f) / .6f),
                4 => b,
                5 => Blend(b, wide, (introductionElapsed - 4.4f) / .9f),
                _ => wide
            };
        }

        private static (float x, float z, float size) Blend((float x, float z, float size) a, (float x, float z, float size) b, float progress)
        {
            var t = Math.Clamp(progress, 0, 1);
            t = t * t * (3 - 2 * t);
            return (a.x + (b.x - a.x) * t, a.z + (b.z - a.z) * t, a.size + (b.size - a.size) * t);
        }

        internal static string Label(ArenaCameraMode mode) => mode switch {
            ArenaCameraMode.Auto => "自动", ArenaCameraMode.Overview => "全局", ArenaCameraMode.Battle => "交战区", ArenaCameraMode.Follow => "跟随", _ => "近景"
        };
        private static float Square(float value) => value * value;
        private static float Distance(ArenaCameraSubject a, ArenaCameraSubject b) => (float)Math.Sqrt(Square(a.X - b.X) + Square(a.Z - b.Z));
    }
}
