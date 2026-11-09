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
        check(director.Shot == ArenaCameraMode.Overview, "arena camera opens on the armies");
        Advance(4);
        check(director.Shot == ArenaCameraMode.Battle, "arena camera enters the active battle region after opening");
        Advance(12);
        check(director.Shot == ArenaCameraMode.Follow && director.Compact, "automatic camera follows an engaged participant and narrows the HUD");
        Advance(6);
        check(director.Shot == ArenaCameraMode.Close && director.Frame(1920, 1080, true).Value.size <= 10, "nearby opponents receive a genuinely closer camera");
        var switches = director.Switches;
        Advance(300, paused: true);
        check(director.Switches == switches && director.Shot == ArenaCameraMode.Close, "pausing does not consume automatic shot durations");
        Advance(4.5f);
        check(director.Shot == ArenaCameraMode.Battle, "close-up returns to the battle region after its dwell time");
        Advance(5);
        check(director.Shot == ArenaCameraMode.Overview, "automatic director periodically re-establishes the overview");

        director.SetMode(ArenaCameraMode.Follow); director.Select("B0"); Advance(0);
        Advance(60);
        check(director.Mode == ArenaCameraMode.Follow && director.FocusId == "B0", "manual follow stays pinned beyond automatic switching deadlines");
        actors.RemoveAll(p => p.Id == "B0"); Advance(.4f);
        check(director.FocusId != "B0" && director.FocusId != null && director.Mode == ArenaCameraMode.Follow, "lost manual targets are replaced without releasing the chosen mode");

        director.SetMode(ArenaCameraMode.Auto); Advance(0);
        actors.Add(new ArenaCameraSubject { Id = "stray", Team = "A", X = 99, Z = 99 });
        Advance(.4f);
        var frame = director.Frame(1920, 1080, true).Value;
        check(frame.x < 65 && frame.z < 65 && frame.size <= 20, "an isolated edge participant cannot pull the battle-region shot away from combat");

        actors = new() {
            new ArenaCameraSubject { Id = "duelist-A", Team = "A", X = 40, Z = 50 },
            new ArenaCameraSubject { Id = "duelist-B", Team = "B", X = 45, Z = 50 }
        };
        Advance(.4f);
        check(director.Shot == ArenaCameraMode.Follow, "single survivors receive sustained follow coverage");
        actors[1].X = 70;
        director.SetMode(ArenaCameraMode.Close); director.Select("duelist-A"); Advance(0);
        check(director.Mode == ArenaCameraMode.Close && director.Shot == ArenaCameraMode.Follow, "distant opponents widen a requested close-up while preserving manual mode");
        director.SetMode(ArenaCameraMode.Overview); Advance(0);
        Advance(60);
        check(director.Mode == ArenaCameraMode.Overview && director.Shot == ArenaCameraMode.Overview && !director.Compact, "manual overview holds and restores the full statistics panels");

        var intro = new ArenaCameraDirector();
        var duel = new[] { new ArenaCameraSubject { Id = "left", Team = "A", X = 20, Z = 50 }, new ArenaCameraSubject { Id = "right", Team = "B", X = 80, Z = 50 } };
        intro.SetIntroduction(0); intro.Advance(0, false, true); intro.Observe(duel);
        check(intro.FocusId == "left" && intro.Shot == ArenaCameraMode.Close && intro.Frame(1920, 1080, true).Value.x == 20, "left introduction frames its own participant without the distant opponent");
        intro.SetIntroduction(2); intro.Advance(0, false, true); intro.Observe(duel);
        check(intro.FocusId == "right" && intro.Shot == ArenaCameraMode.Close && intro.Frame(1920, 1080, true).Value.x == 80, "right introduction receives a separate close-up while simulation stays paused");
        intro.SetIntroduction(null); intro.Advance(0, false, true); intro.Observe(duel);
        check(ArenaCameraDirector.IntroductionSeconds == 4 && intro.Shot == ArenaCameraMode.Overview && intro.Frame(1920, 1080, true).Value.size > 20, "two seconds per side return directly to the countdown overview without another transition stage");

        var soloDirector = new ArenaCameraDirector();
        var army = Enumerable.Range(0, 10).Select(i => new ArenaCameraSubject { Id = "army" + i, Team = "B", X = 45 + i % 3, Z = 50 + i / 3 }).Prepend(new ArenaCameraSubject { Id = "solo", Team = "A", X = 40, Z = 50 }).ToArray();
        var followSeconds = 0; var closeSeconds = 0;
        for (var second = 0; second < 47; second++)
        {
            soloDirector.Advance(1, true, false); soloDirector.Observe(army);
            if (second < 3) continue;
            if (soloDirector.Shot == ArenaCameraMode.Follow) { followSeconds++; check(soloDirector.FocusId == "solo", "single-unit coverage follows the lone side"); }
            if (soloDirector.Shot == ArenaCameraMode.Close) closeSeconds++;
        }
        check(followSeconds >= 28 && closeSeconds > 0, "single-unit battles spend the majority of their cycle following while retaining close and contextual shots");
    }
}
