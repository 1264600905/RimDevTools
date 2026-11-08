using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace LogSyncMod.AutoTest
{
    internal static class CombatOverlayProbe
    {
        internal static object Run()
        {
            if (ManualTestService.Busy || CombatFightService.Busy || global::LogSyncMod.LogSyncMod.settings is not { testSessionActive: true, testSessionKind: "manual" })
                throw new InvalidOperationException("Overlay rendering verification requires an idle CLI manual session");
            var pawn = Find.CurrentMap.mapPawns.AllPawnsSpawned.First(p => p.RaceProps.Humanlike && !p.Dead);
            var previous = RenderTexture.active;
            var textures = new List<RenderTexture>();
            var checks = new Dictionary<string, bool>();
            try
            {
                RenderTexture.active = null;
                var portrait = PawnPortraitRendering.Copy(pawn); textures.Add(portrait);
                checks["copy_restores_screen_target"] = RenderTexture.active == null && portrait.IsCreated();
                var sentinel = new RenderTexture(16, 16, 0); sentinel.Create(); textures.Add(sentinel);
                RenderTexture.active = sentinel;
                var second = PawnPortraitRendering.Copy(pawn); textures.Add(second);
                checks["copy_restores_existing_target"] = ReferenceEquals(RenderTexture.active, sentinel) && second.IsCreated();
                PawnPortraitRendering.Render(pawn, second);
                checks["showcase_render_restores_target"] = ReferenceEquals(RenderTexture.active, sentinel);
                try
                {
                    using var scope = new PawnPortraitRendering.TargetScope(RenderTexture.active);
                    Graphics.Blit(portrait, second);
                    throw new InvalidOperationException("controlled render failure");
                }
                catch (InvalidOperationException) { checks["exception_restores_target"] = ReferenceEquals(RenderTexture.active, sentinel); }
            }
            finally
            {
                RenderTexture.active = previous;
                foreach (var texture in textures) { texture.Release(); UnityEngine.Object.Destroy(texture); }
            }
            checks["target_and_textures_cleaned"] = ReferenceEquals(RenderTexture.active, previous) && textures.All(t => !t.IsCreated());
            return new { allPassed = checks.Values.All(v => v), checks };
        }
    }
}
