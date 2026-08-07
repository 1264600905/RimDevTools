Shader "CTGAS/NanoShield_Enhanced"
{
    Properties
    {
        // ---- 基础颜色 ----
        _BaseColor ("基础颜色", Color) = (0.1, 0.6, 1, 0.3)
        _GridColor ("网格颜色", Color) = (0.3, 0.9, 1, 0.9)
        _RimColor ("边缘发光颜色", Color) = (0.5, 1, 1, 1)

        // ---- 六边形网格 ----
        _HexDensity ("蜂窝密度", Float) = 8.0
        _HexLineWidth ("网格线宽", Range(0.01, 0.15)) = 0.05
        _HexHighlightWidth ("网格高亮线宽", Range(0.0, 0.05)) = 0.01
        _HexHighlightColor ("网格高亮颜色", Color) = (0.8, 1, 1, 1)

        // ---- 球形旋转 ----
        _RotationSpeed ("旋转速度", Float) = 1.0
        _RotationAxisX ("旋转轴X权重", Range(-1, 1)) = 0.2
        _RotationAxisY ("旋转轴Y权重", Range(-1, 1)) = 0.8
        _SphereBulge ("球面隆起", Range(0.5, 2.0)) = 1.0

        // ---- 流动与脉冲 ----
        _FlowSpeed ("流动速度", Float) = 1.0
        _PulseSpeed ("脉冲速度", Float) = 2.0
        _PulseAmount ("脉冲强度", Range(0, 0.5)) = 0.1

        // ---- 边缘与透明 ----
        _RimPower ("边缘发光强度", Range(0.5, 5)) = 3.0
        _EdgeSoftness ("边缘柔化", Range(0.001, 0.1)) = 0.02
        _Opacity ("整体透明度", Range(0, 1)) = 0.85
        _CenterAlpha ("中心透明度", Range(0, 1)) = 0.1

        // ---- 击打反馈 ----
        _FlashIntensity ("闪烁强度系数", Range(0, 1)) = 0
        [HDR] _ImpactColor ("击打亮色", Color) = (1, 1, 1, 1)
        _ShockwaveSpeed ("冲击波速度", Float) = 3.0
        _ShockwaveWidth ("冲击波宽度", Range(0.01, 0.2)) = 0.08
        _ShockwaveFade ("冲击波衰减", Range(0.1, 5.0)) = 2.0

        // ---- 破碎效果 ----
        _BreakProgress ("破碎进度", Range(0, 1)) = 0
        _MaxBreakRadius ("最大破碎半径", Range(0.1, 1.0)) = 0.5
        _BreakNoiseScale ("破碎噪点尺寸", Float) = 5.0
        _BreakEdgeSharpness ("破碎边缘锐度", Range(0.5, 5.0)) = 2.0
        _BreakGlowColor ("破碎辉光", Color) = (1, 0.8, 0.4, 1)

        // ---- 脚本驱动参数（在运行时通过 Material.SetVector 设置） ----
        [HideInInspector] _ImpactPos ("击打位置 (xy)", Vector) = (0.5, 0.5, 0, 0)
        [HideInInspector] _ImpactTime ("击打时间", Float) = -10.0
        [HideInInspector] _BreakOrigin ("破裂原点 (xy)", Vector) = (0.5, 0.5, 0, 0)
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            // 基础颜色
            float4 _BaseColor;
            float4 _GridColor;
            float4 _RimColor;

            // 六边形
            float _HexDensity;
            float _HexLineWidth;
            float _HexHighlightWidth;
            float4 _HexHighlightColor;

            // 球形旋转
            float _RotationSpeed;
            float _RotationAxisX;
            float _RotationAxisY;
            float _SphereBulge;

            // 流动与脉冲
            float _FlowSpeed;
            float _PulseSpeed;
            float _PulseAmount;

            // 边缘与透明
            float _RimPower;
            float _EdgeSoftness;
            float _Opacity;
            float _CenterAlpha;

            // 击打反馈
            float _FlashIntensity;
            float4 _ImpactColor;
            float _ShockwaveSpeed;
            float _ShockwaveWidth;
            float _ShockwaveFade;

            // 破碎
            float _BreakProgress;
            float _MaxBreakRadius;
            float _BreakNoiseScale;
            float _BreakEdgeSharpness;
            float4 _BreakGlowColor;

            // 脚本控制参数
            float4 _ImpactPos;      // xy 为击中位置
            float _ImpactTime;
            float4 _BreakOrigin;    // xy 为破裂起始点

            static const float PI = 3.14159265;
            static const float PHI = 1.61803399;
            static const float HALF_PI = 1.5707963;
            static const float TWO_PI = 6.2831853;

            float4 quatFromAxisAngle(float3 axis, float angle)
            {
                axis = normalize(axis);
                float halfAngle = angle * 0.5;
                float s = sin(halfAngle);
                return float4(axis * s, cos(halfAngle));
            }

            float3 rotateByQuaternion(float3 v, float4 q)
            {
                return v + 2.0 * cross(q.xyz, cross(q.xyz, v) + q.w * v);
            }

            float3 hemisphereDirFromOffset(float2 offset, float radius)
            {
                float2 p = offset / radius;
                float z = sqrt(saturate(1.0 - dot(p, p)));
                return normalize(float3(p, z));
            }

            float3 hemisphereDirFromUV(float2 uv, float radius)
            {
                return hemisphereDirFromOffset(uv - 0.5, radius);
            }

            float3 axisAngleRotate(float3 p, float3 axis, float angle)
            {
                axis = normalize(axis);
                return lerp(dot(axis, p) * axis, p, cos(angle)) + sin(angle) * cross(axis, p);
            }

            float3 safeSign(float3 v)
            {
                return step(0.0, v) * 2.0 - 1.0;
            }

            void getIcosahedronFaceData(float3 p, out float3 face, out float3 a, out float3 b, out float3 c)
            {
                float3 signMask = safeSign(p);
                float3 absP = abs(p);

                float3 icoSeed = float3(PHI, 1.0, 0.0);
                float3 nearestVertex = icoSeed;
                if (dot(absP, icoSeed.yzx - nearestVertex) > 0.0) nearestVertex = icoSeed.yzx;
                if (dot(absP, icoSeed.zxy - nearestVertex) > 0.0) nearestVertex = icoSeed.zxy;
                a = normalize(nearestVertex) * signMask;

                float3 faceSeed = icoSeed.xxx;
                float3 dodecaCandidate = float3(icoSeed.z, icoSeed.y, icoSeed.x + icoSeed.y);
                if (dot(absP, dodecaCandidate - faceSeed) > 0.0) faceSeed = dodecaCandidate;
                if (dot(absP, dodecaCandidate.yzx - faceSeed) > 0.0) faceSeed = dodecaCandidate.yzx;
                if (dot(absP, dodecaCandidate.zxy - faceSeed) > 0.0) faceSeed = dodecaCandidate.zxy;
                face = normalize(faceSeed) * signMask;

                float faceTurn = TWO_PI / 3.0;
                b = normalize(axisAngleRotate(a, face, -faceTurn));
                c = normalize(axisAngleRotate(a, face, faceTurn));
            }

            float sphereLineAB(float3 p, float3 a, float3 b)
            {
                p = normalize(p);
                return dot(p, cross(a, b)) / max(length(a - b), 0.0001);
            }

            float sphericalHexSignedDistance(float3 p, float3 a, float3 b, float3 c, float inset)
            {
                float otherInset = 1.0 - inset;

                float3 ab0 = normalize(lerp(a, b, inset));
                float3 ab1 = normalize(lerp(a, b, otherInset));
                float3 bc0 = normalize(lerp(b, c, inset));
                float3 bc1 = normalize(lerp(b, c, otherInset));
                float3 ca0 = normalize(lerp(c, a, inset));
                float3 ca1 = normalize(lerp(c, a, otherInset));

                float d0 = sphereLineAB(p, ab0, ab1);
                float d1 = sphereLineAB(p, ab1, bc0);
                float d2 = sphereLineAB(p, bc0, bc1);
                float d3 = sphereLineAB(p, bc1, ca0);
                float d4 = sphereLineAB(p, ca0, ca1);
                float d5 = sphereLineAB(p, ca1, ab0);

                return max(max(max(d0, d1), max(d2, d3)), max(d4, d5));
            }

            float sphericalHexLine(float signedDistance, float lineWidth)
            {
                float edge = abs(signedDistance);
                float aa = max(fwidth(edge), 0.0006);
                return 1.0 - smoothstep(lineWidth, lineWidth + aa * 1.5, edge);
            }

            float angularDistance01(float3 a, float3 b)
            {
                return acos(clamp(dot(a, b), -1.0, 1.0)) / HALF_PI;
            }

            // 简单伪随机
            float rand(float2 co)
            {
                return frac(sin(dot(co, float2(12.9898, 78.233))) * 43758.5453);
            }

            float directionNoise(float3 dir, float scale)
            {
                float3 sampleCell = floor((dir * 0.5 + 0.5) * scale);
                return rand(sampleCell.xy + sampleCell.z * 17.371);
            }

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                // ---- 1. 圆形裁剪 ----
                float2 center = float2(0.5, 0.5);
                float2 uvOffset = i.uv - center;          // 范围 [-0.5, 0.5]
                float dist = length(uvOffset);
                float circleRadius = 0.48;                // 稍微收缩，避免边界溢出

                float alphaEdge = 1.0 - smoothstep(circleRadius - _EdgeSoftness, circleRadius, dist);
                if (alphaEdge < 0.001) discard;

                // ---- 2. 半球方向 + 四元数旋转 ----
                float2 spherePos = uvOffset / circleRadius;
                float radial = saturate(length(spherePos));
                float sphereZ = pow(sqrt(saturate(1.0 - dot(spherePos, spherePos))), _SphereBulge);
                float3 surfNormal = normalize(float3(spherePos, sphereZ));

                float axisZ = sqrt(saturate(1.0 - saturate(_RotationAxisX * _RotationAxisX + _RotationAxisY * _RotationAxisY)));
                float3 rotationAxis = normalize(float3(_RotationAxisX, _RotationAxisY, axisZ + 0.001));
                float4 shieldRotation = quatFromAxisAngle(rotationAxis, _Time.y * _RotationSpeed);
                float3 rotatedNormal = normalize(rotateByQuaternion(surfNormal, shieldRotation));

                // ---- 3. 基于二十面体面片的球面六边形网格 ----
                float3 faceCenter;
                float3 faceA;
                float3 faceB;
                float3 faceC;
                getIcosahedronFaceData(rotatedNormal, faceCenter, faceA, faceB, faceC);

                // 参考 geodesic / icosahedral 的做法，在每个球面三角面片内直接构造六边形边线。
                // 改为单一主轮廓 + 同轮廓软辉光，避免多层 inset 叠加造成透明重影。
                float density01 = saturate((_HexDensity - 1.0) / 11.0);
                float primaryInset = lerp(0.318, 1.0 / 3.0, density01);
                float sdPrimary = sphericalHexSignedDistance(rotatedNormal, faceA, faceB, faceC, primaryInset);

                float crispLineWidth = max(_HexLineWidth * 0.22, 0.0015);
                float glowLineWidth = max(_HexLineWidth * 0.52, crispLineWidth + 0.001);
                float crispMask = sphericalHexLine(sdPrimary, crispLineWidth);
                float glowMask = saturate(sphericalHexLine(sdPrimary, glowLineWidth) - crispMask * 0.72);
                float baseHex = saturate(crispMask + glowMask * lerp(0.65, 0.35, density01));

                float faceHash = rand(faceCenter.xy * 13.7 + faceCenter.z * 7.3);
                float scanPhase = _Time.y * (_FlowSpeed * 1.4 + 0.2) + dot(rotatedNormal, normalize(faceCenter + faceA)) * 24.0 + faceHash * TWO_PI;
                float scan = 0.5 + 0.5 * sin(scanPhase);
                float highlightWidth = max(_HexHighlightWidth, 0.001);
                float highlightCore = sphericalHexLine(sdPrimary, highlightWidth * 0.38);
                float highlightHalo = saturate(sphericalHexLine(sdPrimary, highlightWidth * 0.9 + crispLineWidth * 0.35) - highlightCore * 0.6);
                float highlightHex = saturate((highlightCore + highlightHalo * 0.55) * smoothstep(0.45, 1.0, scan));

                float pulse = 0.5 + 0.5 * sin(_Time.y * _PulseSpeed + dot(rotatedNormal, faceCenter) * 18.0 + faceHash * TWO_PI);

                // ---- 4. 击打反馈（局部命中闪烁 + 冲击波）----
                float timeSinceImpact = _Time.y - _ImpactTime;
                float impactActive = step(0.0, timeSinceImpact) * step(timeSinceImpact, 2.0);
                float3 impactDir = hemisphereDirFromUV(_ImpactPos.xy, circleRadius);
                float impactDist = angularDistance01(rotatedNormal, impactDir);
                float impactCore = exp(-impactDist * 24.0) * exp(-timeSinceImpact * 7.5);
                float shockRadius = timeSinceImpact * _ShockwaveSpeed * 0.22;
                float shockBand = abs(impactDist - shockRadius);
                float shockAA = max(fwidth(shockBand), 0.002);
                float shockwave = (1.0 - smoothstep(_ShockwaveWidth, _ShockwaveWidth + shockAA, shockBand)) * exp(-timeSinceImpact * _ShockwaveFade);
                float flashEffect = impactActive * _FlashIntensity * (impactCore + shockwave);

                // ---- 5. 破碎效果（仅在破碎进度 > 0 时启用） ----
                float breakEnabled = step(0.001, _BreakProgress);
                float3 breakOriginDir = hemisphereDirFromUV(_BreakOrigin.xy, circleRadius);
                float breakDist = angularDistance01(rotatedNormal, breakOriginDir);
                float breakNoise = (directionNoise(rotatedNormal, max(_BreakNoiseScale, 1.0) * 4.0) - 0.5) * 0.12;
                float breakFront = _BreakProgress * _MaxBreakRadius;
                float breakSoftness = max(0.015, 0.08 / max(_BreakEdgeSharpness, 0.001));
                float brokenMask = breakEnabled * (1.0 - smoothstep(breakFront - breakSoftness, breakFront + breakSoftness, breakDist + breakNoise));
                float breakEdgeMask = breakEnabled * (1.0 - smoothstep(0.01, 0.05, abs(breakDist + breakNoise - breakFront)));

                // ---- 6. 边缘发光（基于法线） ----
                // 视线方向固定为 (0,0,1)
                float rim = 1.0 - saturate(surfNormal.z);     // 侧面法线z越小，边缘越强
                rim = pow(rim, _RimPower);
                rim = saturate(rim);

                // ---- 7. 颜色合成 ----
                float lineMask = saturate(crispMask * 0.9 + glowMask * 0.45 + highlightHex);
                float energyFill = _BaseColor.a * (0.12 + 0.10 * pulse);
                float3 color = _BaseColor.rgb * energyFill;
                color += _GridColor.rgb * crispMask * (0.85 + 0.20 * pulse);
                color += _GridColor.rgb * glowMask * (0.18 + 0.08 * pulse);
                color += _HexHighlightColor.rgb * highlightHex * (0.35 + 0.55 * pulse);
                color += _RimColor.rgb * rim * (0.35 + 0.2 * pulse);
                color += _ImpactColor.rgb * flashEffect;
                color += _BreakGlowColor.rgb * breakEdgeMask * (0.8 + 0.4 * rim);
                color *= lerp(1.0, 0.08, brokenMask);
                color *= (1.0 + _PulseAmount * (pulse - 0.5) * 1.4);

                // ---- 8. 透明度计算 ----
                float centerFade = lerp(_CenterAlpha, 1.0, smoothstep(0.0, 0.65, radial));
                float alpha = energyFill * 0.55;
                alpha += crispMask * _GridColor.a * 0.62;
                alpha += glowMask * _GridColor.a * 0.16;
                alpha += highlightHex * _HexHighlightColor.a * 0.34;
                alpha += rim * _RimColor.a * 0.28;
                alpha += flashEffect * 0.22;
                alpha += breakEdgeMask * 0.15;
                alpha *= centerFade;
                alpha *= lerp(1.0, 0.03, brokenMask);
                alpha *= _Opacity * alphaEdge;
                alpha = saturate(alpha);

                return float4(color, alpha);
            }
            ENDCG
        }
    }

    FallBack "Transparent/VertexLit"
}
