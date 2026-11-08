using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace LogSyncMod.AutoTest
{
    internal static class PawnPortraitRendering
    {
        // In 1.7 assigning RenderTexture.active also redirects subsequent IMGUI draws.
        internal readonly struct TargetScope : IDisposable
        {
            private readonly RenderTexture previous;
            internal TargetScope(RenderTexture previous) { this.previous = previous; }
            public void Dispose() { RenderTexture.active = previous; }
        }

        internal static RenderTexture Copy(Pawn pawn)
        {
            using var scope = new TargetScope(RenderTexture.active);
            RenderTexture texture = null;
            try
            {
                var source = PortraitsCache.Get(pawn, new Vector2(128, 128), Rot4.South, cameraZoom: 1.1f);
                texture = new RenderTexture(128, 128, 0); texture.Create();
                Graphics.Blit(source, texture);
                return texture;
            }
            catch
            {
                if (texture != null) { texture.Release(); UnityEngine.Object.Destroy(texture); }
                throw;
            }
        }

        internal static void Render(Pawn pawn, RenderTexture texture)
        {
            using var scope = new TargetScope(RenderTexture.active);
            PawnCacheCameraManager.PawnCacheRenderer.RenderPawn(pawn, texture, Vector3.zero, 1f, 0, Rot4.South,
                renderHead: true, renderHeadgear: true, renderClothes: true, portrait: true);
        }
    }
}
