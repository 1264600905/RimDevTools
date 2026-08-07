using UnityEngine;
using Verse;

namespace MyCustomMod.Rendering
{
    public static class MinimalShaderDemoDrawer
    {
        private const string ShaderPath = "CTGAS/NanoShield_Enhanced";
        private static bool loggedSettingsDisabled;
        private static bool loggedDrawReady;
        private static bool loggedDrawExecuted;
        private static MaterialPropertyBlock propertyBlock;

        private static readonly int GameTimeId = Shader.PropertyToID("_Time");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int GridColorId = Shader.PropertyToID("_GridColor");
        private static readonly int RimColorId = Shader.PropertyToID("_RimColor");
        private static readonly int OpacityId = Shader.PropertyToID("_Opacity");
        private static readonly int HexDensityId = Shader.PropertyToID("_HexDensity");
        private static readonly int FlowSpeedId = Shader.PropertyToID("_FlowSpeed");
        private static readonly int PulseSpeedId = Shader.PropertyToID("_PulseSpeed");
        public static void DrawOnPawn(Pawn pawn)
        {
            if (pawn == null || !pawn.Spawned || pawn.Dead)
            {
                return;
            }

            if (!(MyCustomModMod.settings?.enableShaderDemo ?? false))
            {
                if (!loggedSettingsDisabled)
                {
                    loggedSettingsDisabled = true;
                    LogUtils.Message(nameof(MinimalShaderDemoDrawer), "Shader demo 已关闭，未执行绘制。");
                }
                return;
            }

            Material material = ShaderMaterialCache.GetMaterial(ShaderPath, 2950);
            if (material == null)
            {
                LogUtils.Warning(nameof(MinimalShaderDemoDrawer), "材质创建失败，跳过绘制。");
                return;
            }

            if (!loggedDrawReady)
            {
                loggedDrawReady = true;
                LogUtils.Message(nameof(MinimalShaderDemoDrawer), $"开始准备绘制 Shader demo，pawn={pawn.LabelShortCap}");
            }

            Vector3 drawPos = pawn.Drawer.DrawPos;
            drawPos.y = AltitudeLayer.MoteOverhead.AltitudeFor();

            Mesh mesh = MeshPool.GetMeshSetForSize(1.45f, 1.45f).MeshAt(Rot4.South);

            if (propertyBlock == null)
            {
                propertyBlock = new MaterialPropertyBlock();
            }
            propertyBlock.Clear();
            propertyBlock.SetFloat(GameTimeId, Find.TickManager.TicksGame / 60f);
            propertyBlock.SetColor(BaseColorId, new Color(0.15f, 0.55f, 1f, 0.32f));
            propertyBlock.SetColor(GridColorId, new Color(0.4f, 0.92f, 1f, 0.9f));
            propertyBlock.SetColor(RimColorId, new Color(0.7f, 1f, 1f, 1f));
            propertyBlock.SetFloat(OpacityId, 0.7f);
            propertyBlock.SetFloat(HexDensityId, 8f);
            propertyBlock.SetFloat(FlowSpeedId, 0.8f);
            propertyBlock.SetFloat(PulseSpeedId, 1.4f);

            Graphics.DrawMesh(mesh, Matrix4x4.TRS(drawPos, Quaternion.identity, Vector3.one), material, 0, null, 0, propertyBlock);

            if (!loggedDrawExecuted)
            {
                loggedDrawExecuted = true;
                LogUtils.Message(nameof(MinimalShaderDemoDrawer), $"已执行 DrawMesh，pawn={pawn.LabelShortCap}, pos={drawPos}");
            }
        }
    }
}
