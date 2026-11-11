using System;
using System.Collections.Generic;
using LogSyncMod.AutoTest;

internal static class ArenaContinuityTests
{
    internal static void Run(Action<bool, string> check)
    {
        var warnings = new List<string>();
        var restored = new List<string>();
        ArenaContinuity.TryOptional("skill", () => throw new InvalidOperationException("mod changed aptitude"), warnings.Add);
        ArenaContinuity.TryOptional("equipment", () => restored.Add("weapon"), warnings.Add);
        check(warnings.Count == 1 && warnings[0].Contains("skill") && warnings[0].Contains("mod changed aptitude")
            && restored.Count == 1, "arena retains a failed optional override as a diagnostic and continues independent restoration");
        check(!ArenaContinuity.PresentationReady(false, 19.9, warnings.Add), "arena gives an unavailable screenshot its bounded capture window");
        check(ArenaContinuity.PresentationReady(false, 20, warnings.Add) && warnings.Count == 2,
            "arena advances after screenshot failure instead of cancelling the tournament");
        check(ArenaContinuity.PresentationReady(true, 1, warnings.Add) && warnings.Count == 2,
            "ready arena captures continue without adding warnings");
    }
}
