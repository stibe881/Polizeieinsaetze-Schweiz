// Terrain: three tiled detail layers (grass/soil, rock, paved) blended by baked weights (Uv2.x = rock, Uv2.y = paved), land-use tint from the vertex colour,
// snow above the snow line (altitude + slope), macro variation against visible tiling. Shadow/depth passes from URP Lit.
Shader "SPS/Terrain"
{
    Properties
    {
        _GrassMap ("Grass / soil", 2D) = "white" {}
        _RockMap ("Rock / scree", 2D) = "white" {}
        _PavedMap ("Paved / urban ground", 2D) = "white" {}
        _SnowLine ("Snow line (m a.s.l.)", Float) = 2400
        _Tiling ("Detail tiling (per UV unit)", Float) = 0.45
        _Wet ("Wetness", Range(0,1)) = 0
        _SnowCover ("Snow cover 0..1 (weather)", Range(0,1)) = 0
        // URP Lit compatible names for the borrowed passes
        _BaseMap ("Unused", 2D) = "white" {}
        _BaseColor ("Unused", Color) = (1,1,1,1)
        _Cutoff ("Unused", Float) = 0.5
        _Cull ("Cull", Float) = 2
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry-10" "UniversalMaterialType" = "Lit" }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_GrassMap); SAMPLER(sampler_GrassMap);
            TEXTURE2D(_RockMap);  SAMPLER(sampler_RockMap);
            TEXTURE2D(_PavedMap); SAMPLER(sampler_PavedMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _Cutoff;
                half _Cull;
                float _SnowLine;
                float _Tiling;
                half _Wet;
                half _SnowCover;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; float2 uv2 : TEXCOORD1; half4 color : COLOR; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half3 normalWS : TEXCOORD1; float2 uv : TEXCOORD2; float2 uv2 : TEXCOORD3; half4 color : COLOR; half fogFactor : TEXCOORD4;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                VertexPositionInputs vp = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs vn = GetVertexNormalInputs(IN.normalOS);
                OUT.positionCS = vp.positionCS; OUT.positionWS = vp.positionWS; OUT.normalWS = vn.normalWS; OUT.uv = IN.uv; OUT.uv2 = IN.uv2; OUT.color = IN.color;
                OUT.fogFactor = ComputeFogFactor(vp.positionCS.z);
                return OUT;
            }

            float Hash21(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float VNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p); f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash21(i), Hash21(i + float2(1, 0)), f.x), lerp(Hash21(i + float2(0, 1)), Hash21(i + float2(1, 1)), f.x), f.y);
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 uv = IN.uv * _Tiling;
                half3 g1 = SAMPLE_TEXTURE2D(_GrassMap, sampler_GrassMap, uv).rgb;
                half3 g2 = SAMPLE_TEXTURE2D(_GrassMap, sampler_GrassMap, uv * 0.137 + 0.31).rgb;       // large-scale repeat breaker
                half3 grass = g1 * (0.65 + 0.7 * g2.g);
                half3 rock = SAMPLE_TEXTURE2D(_RockMap, sampler_RockMap, uv * 0.7).rgb;
                half3 paved = SAMPLE_TEXTURE2D(_PavedMap, sampler_PavedMap, uv).rgb;
                float wRock = saturate(IN.uv2.x), wPaved = saturate(IN.uv2.y), wGrass = saturate(1.0 - wRock - wPaved);
                half3 tint = SRGBToLinear(IN.color.rgb);
                half3 albedo = (grass * wGrass * tint + rock * wRock * lerp(tint, half3(1,1,1), 0.5) + paved * wPaved * tint);

                half3 n = normalize(IN.normalWS);
                // snow: altitude line modulated by noise, steep faces keep less snow, weather adds cover down to low altitude
                float h = IN.positionWS.y; // NOTE: world y == metres a.s.l. (the floating origin never shifts y)
                float noise = VNoise(IN.positionWS.xz * 0.05) * 140.0 - 70.0;
                float snowLine = _SnowLine + noise - _SnowCover * 1500.0;
                float snow = saturate((h - snowLine) / 120.0) * saturate((n.y - 0.45) * 3.0);
                albedo = lerp(albedo, half3(0.88, 0.9, 0.95), snow);
                albedo *= lerp(1.0, 0.7, _Wet);

                InputData inputData = (InputData)0;
                inputData.positionWS = IN.positionWS; inputData.positionCS = IN.positionCS;
                inputData.normalWS = NormalizeNormalPerPixel(n);
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(IN.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                inputData.fogCoord = InitializeInputDataFog(float4(IN.positionWS, 1.0), IN.fogFactor);
                inputData.vertexLighting = half3(0, 0, 0);
                inputData.bakedGI = SampleSH(inputData.normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData s = (SurfaceData)0;
                s.albedo = albedo; s.metallic = 0; s.specular = half3(0,0,0);
                s.smoothness = saturate(0.08 + wPaved * 0.12 + snow * 0.25 + _Wet * 0.5);
                s.occlusion = 1; s.emission = half3(0,0,0); s.alpha = 1; s.normalTS = half3(0,0,1);
                half4 color = UniversalFragmentPBR(inputData, s);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                color.a = 1;
                return color;
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/SHADOWCASTER"
        UsePass "Universal Render Pipeline/Lit/DEPTHONLY"
        UsePass "Universal Render Pipeline/Lit/DEPTHNORMALS"
    }
    FallBack "Universal Render Pipeline/Lit"
}
