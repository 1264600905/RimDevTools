using LogSyncMod.AutoTest.Scenarios;

internal static class ArenaCameraTests
{
    internal static void Run(Action<bool, string> check)
    {
        var actors = Enumerable.Range(0, 8).SelectMany(i => new[] {
            new ArenaCameraSubject { Id = "A" + i, Team = "A", X = 40 + i % 4, Z = 45 + i / 4 },
            new ArenaCameraSubject { Id = "B" + i, Team = "B", X = 48 + i % 4, Z = 45 + i / 4 }
        }).ToList();
        var director = new ArenaCameraDirector();
        void Advance(float seconds, bool paused = false)
        { if (director.Advance(seconds, true, paused)) director.Observe(actors); }

        Advance(0);
        check(director.Shot == ArenaCameraMode.Overview, "camera establishes the armies before combat");
        Advance(4);
        check(director.Shot == ArenaCameraMode.Battle, "camera enters the active battle region");
        var switches = director.Switches;
        for (var second = 0; second < 90; second++) Advance(1);
        check(director.Switches == switches && !director.Compact, "unchanged combat never rotates shots or resizes panels");
        Advance(300, paused: true);
        check(director.Switches == switches, "pausing does not change camera decisions");

        director.Select("B0"); Advance(0); Advance(60);
        check(director.FocusId == "B0" && director.Shot == ArenaCameraMode.Follow, "selecting a participant enables stable follow coverage");
        actors.RemoveAll(p => p.Id == "B0"); Advance(.4f);
        check(director.FocusId != "B0" && director.FocusId != null, "a lost selected participant returns to automatic battle coverage");
        var engagedSize = director.Frame(1920, 1080, true).Value.size;
        actors.Add(new ArenaCameraSubject { Id = "stray", Team = "A", X = 99, Z = 99 });
        Advance(.4f);
        var frame = director.Frame(1920, 1080, true).Value;
        check(frame.x < 65 && frame.z < 65 && frame.size == engagedSize, "an isolated participant does not move or expand the main battle view");

        switches = director.Switches;
        actors = new() {
            new ArenaCameraSubject { Id = "duelist-A", Team = "A", X = 40, Z = 50 },
            new ArenaCameraSubject { Id = "duelist-B", Team = "B", X = 45, Z = 50 }
        };
        for (var second = 0; second < 50; second++)
        {
            actors[1].X = second % 2 == 0 ? 51.9f : 52.1f;
            Advance(1);
        }
        check(director.Shot == ArenaCameraMode.Battle && director.Switches == switches,
            "single survivors and opponents crossing twelve cells never alternate close and follow shots");
        director.SetMode(ArenaCameraMode.Overview); Advance(0); Advance(60);
        check(director.Shot == ArenaCameraMode.Overview && !director.Compact, "manual overview keeps the fixed panel layout");
        director.SetMode(ArenaCameraMode.Close); Advance(0);
        check(director.Mode == ArenaCameraMode.Follow && director.Shot == ArenaCameraMode.Follow, "historical close mode uses the stable follow shot");

        director.SetMode(ArenaCameraMode.Battle); Advance(0); Advance(60);
        check(director.Mode == ArenaCameraMode.Battle && director.Shot == ArenaCameraMode.Battle,
            "manual battle-region mode remains available and does not rotate away");
        director.SetMode(ArenaCameraMode.Follow); director.Select("duelist-A"); Advance(0);
        check(director.Mode == ArenaCameraMode.Follow && director.FocusId == "duelist-A"
            && director.Frame(1920, 1080, true).Value.size == 8, "manual follow frames the participant closer in a small fight");

        TestIntroduction(check);
        TestZoom(check);
        TestPan(check);
        TestSmallFights(check);
    }

    private static void TestSmallFights(Action<bool, string> check)
    {
        var battleSizes = new List<float>();
        var followSizes = new List<float>();
        var portraitSizes = new List<float>();
        foreach (var perTeam in new[] { 1, 4, 20 })
        {
            var actors = Enumerable.Range(0, perTeam).SelectMany(i => new[] {
                new ArenaCameraSubject { Id = "A" + i, Team = "A", X = 40 + i % 3 * .1f, Z = 50 + i / 3 * .1f },
                new ArenaCameraSubject { Id = "B" + i, Team = "B", X = 44 + i % 3 * .1f, Z = 50 + i / 3 * .1f }
            }).ToArray();
            var director = new ArenaCameraDirector();
            void Observe() { if (director.Advance(0, true, false)) director.Observe(actors); }
            director.SetIntroduction(.8f); Observe();
            var left = director.Frame(1920, 1080, true).Value;
            director.SetIntroduction(2.9f); Observe();
            var right = director.Frame(1920, 1080, true).Value;
            portraitSizes.Add(left.size);
            check(left.size == right.size, "small fights keep a consistent zoom when the introduction pans between teams");
            director.SetIntroduction(null); director.SetMode(ArenaCameraMode.Battle); Observe();
            battleSizes.Add(director.Frame(1920, 1080, true).Value.size);
            director.SetMode(ArenaCameraMode.Follow); director.Select("A0"); Observe();
            followSizes.Add(director.Frame(1920, 1080, true).Value.size);
            if (perTeam == 20)
            {
                actors = actors.Take(4).ToArray();
                director.Advance(.4f, true, false); director.Observe(actors);
                check(director.Frame(1920, 1080, true).Value.size == followSizes[0],
                    "a large fight's survivors receive the same closer follow framing as a small fight");
                director.SetMode(ArenaCameraMode.Battle); Observe();
                check(director.Frame(1920, 1080, true).Value.size == battleSizes[0],
                    "battle framing adapts when only a few standing participants remain");
            }
        }
        check(portraitSizes[0] < portraitSizes[1] && portraitSizes[1] < portraitSizes[2],
            "single participants and small squads receive closer opening portraits");
        check(battleSizes[0] < battleSizes[1] && battleSizes[1] < battleSizes[2],
            "small and medium fights use closer battle framing than armies");
        check(followSizes[0] < followSizes[1] && followSizes[1] < followSizes[2],
            "follow framing tightens for smaller fights without changing camera modes");

        var ranged = new ArenaCameraDirector();
        ranged.SetMode(ArenaCameraMode.Battle); ranged.Advance(0, true, false);
        ranged.Observe(new[] {
            new ArenaCameraSubject { Id = "A", Team = "A", X = 40, Z = 50 },
            new ArenaCameraSubject { Id = "B", Team = "B", X = 64, Z = 50 }
        });
        check(ranged.Frame(1920, 1080, true).Value.size > battleSizes[0],
            "a small ranged fight still widens to retain engagement context");
    }

    private static void TestIntroduction(Action<bool, string> check)
    {
        // Exercise the same army sizes as the real-game camera fixture.
        var armies = Enumerable.Range(0, 200).Select(i => new ArenaCameraSubject {
            Id = "A" + i, Team = "A", X = 12 + i % 25, Z = 48 + i / 25
        }).Concat(Enumerable.Range(0, 100).Select(i => new ArenaCameraSubject {
            Id = "B" + i, Team = "B", X = 92 + i % 20, Z = 49 + i / 20
        })).ToArray();
        foreach (var viewport in new[] { (1920f, 1080f, true), (1280f, 720f, true), (1920f, 1080f, false) })
        {
            var intro = new ArenaCameraDirector();
            intro.SetOverviewCenter(70, 60);
            (float x, float z, float size) At(float t)
            {
                intro.SetIntroduction(t);
                if (intro.Advance(0, false, true)) intro.Observe(armies);
                return intro.Frame(viewport.Item1, viewport.Item2, viewport.Item3).Value;
            }
            var wide = At(0); var left = At(.8f); var right = At(2.9f);
            check(wide.x == 70 && wide.z == 60, "global introduction is anchored to the map center for asymmetric armies");
            check(wide.size > left.size && left.size == right.size && left.x < wide.x && right.x > wide.x,
                "introduction starts wide and frames both formations at the same close scale");
            check(At(.5f).x > left.x && At(.5f).x < wide.x && At(.5f).size > left.size,
                "the first transition moves and zooms together");
            check(At(.8f) == left && At(2.299f) == left && At(2.9f) == right && At(4.399f) == right,
                "both close-ups stay completely still for the full 1.5 seconds");
            var middle = At(2.6f);
            check(middle.x > left.x && middle.x < right.x && Math.Abs(middle.size - left.size) < .001f,
                "crossing to the second side only pans and never changes scale");
            foreach (var boundary in new[] { .2f, .8f, 2.3f, 2.9f, 4.4f, 5.3f })
            {
                var before = At(boundary - .0001f); var after = At(boundary + .0001f);
                check(Math.Abs(before.x - after.x) < .01f && Math.Abs(before.z - after.z) < .01f && Math.Abs(before.size - after.size) < .01f,
                    "introduction remains continuous across every phase boundary");
            }
            check(At(5.3f) == wide && At(5.599f) == wide, "the global view settles before the countdown");
            intro.SetIntroduction(null); intro.Advance(0, false, true); intro.Observe(armies);
            check(intro.Frame(viewport.Item1, viewport.Item2, viewport.Item3).Value == wide,
                "countdown inherits exactly the final global composition");
        }
        check(ArenaCameraDirector.IntroductionHoldSeconds == 1.5f && ArenaCameraDirector.IntroductionSeconds == 5.6f,
            "the continuous opening includes two 1.5-second holds");
    }

    private static void TestPan(Action<bool, string> check)
    {
        var pan = new ArenaCameraPan();
        for (var i = 0; i < 600; i++)
            pan.Observe(50 + (i % 2 == 0 ? 2.5f : -2.5f), 50, 50, 50, .016f);
        check(pan.X == 50 && pan.Z == 50 && pan.Retargets == 0,
            "nearby combat movement leaves the camera completely stationary");
        for (var i = 0; i < 30; i++) pan.Observe(i % 2 == 0 ? 60 : 40, 50, 50, 50, .016f);
        check(pan.Retargets == 0, "rapid target changes cannot drag the camera back and forth");
        for (var i = 0; i < 60; i++) pan.Observe(62, 50, 50, 50, .016f);
        check(pan.X == 62 && pan.Retargets == 1, "sustained movement outside the quiet area retains automatic following");
        var runner = new ArenaCameraPan();
        var following = new GroupCombatCameraMotion(); following.Set(50, 50, 9);
        for (var i = 0; i < 625; i++)
        {
            runner.Observe(50 + i * .08f, 50, following.X, following.Z, .016f);
            following.Step(runner.X, runner.Z, 9, .016f, positionSeconds: 1.1f, maximumMoveSpeed: 10, smoothZoom: false);
        }
        check(runner.Retargets >= 6 && following.X > 90 && 99.92f - following.X < 9,
            $"a continuously running participant remains followed instead of resetting the stability timer forever (retargets={runner.Retargets}, cameraX={following.X})");
        var motion = new GroupCombatCameraMotion(); motion.Set(50, 50, 20);
        var previous = motion.X;
        for (var i = 0; i < 300; i++)
        {
            motion.Step(pan.X, pan.Z, 20, .016f, positionSeconds: 1.1f, maximumMoveSpeed: 10);
            check(motion.X >= previous && motion.X - previous <= .161f && motion.Size == 20,
                "a confirmed new battle center pans in one direction at bounded speed without zooming");
            previous = motion.X;
        }
        check(motion.X > 61.7f, "stabilization still reaches a moving battle");
        pan.Reset(70, 60);
        pan.Observe(70, 60, 110, 50, .016f);
        check(pan.X == 70 && pan.Z == 60, "returning from the introduction discards the old follow destination");
        motion.Step(70, 60, 28, .016f, positionSeconds: 1.1f, maximumMoveSpeed: 10, smoothZoom: false);
        check(motion.Size == 28 && motion.X < 70, "widening the battle view sets zoom immediately while preserving smooth position");
        motion.Step(70, 60, 15, .016f, smoothZoom: false);
        check(motion.Size == 15, "switching to follow sets the tighter zoom in the same frame");
    }

    private static void TestZoom(Action<bool, string> check)
    {
        var zoom = new ArenaCameraZoom();
        var size = 20f;
        for (var i = 0; i < 40; i++) size = zoom.Target(size, 16, 20, .1f);
        check(size == 20, "a brief smaller framing does not pull the camera inward");
        size = zoom.Target(size, 20, 20, .1f);
        for (var i = 0; i < 40; i++) size = zoom.Target(size, 16, 20, .1f);
        check(size == 20, "expansion resets the contraction delay");
        for (var i = 0; i < 15; i++) size = zoom.Target(size, 16, 20, .1f);
        check(size == 18, "sustained compact fighting only contracts by a small step");
        check(zoom.Target(size, 19, 20, .1f) == 20, "expanding fighting can immediately request a wider view");
        var motion = new GroupCombatCameraMotion();
        motion.Set(30, 50, 11); motion.Step(100, 50, 11, .016f);
        check(motion.X > 30 && motion.X < 100 && motion.Size == 11, "timed opening hands off to smooth panning without an extra zoom");
    }
}
