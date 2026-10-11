// Swiss Police Simulator – vertex-colour PBR shader for URP (Unity 6 / URP 17).
// Albedo = _BaseMap * _BaseColor * vertex colour (authored in sRGB). Optional alpha clip, double-sided, emission, animated signal lenses (Uv2) and rain wetness.
// Shadow / depth passes are borrowed from URP Lit (same property names: _BaseMap, _BaseColor, _Cutoff, _Cull, _ALPHATEST_ON).
Shader "SPS/Lit"
{
    Properties
    {
        [MainTexture] _BaseMap ("Albedo", 2D) = "white" {}
        [MainColor] _BaseColor ("Tint", Color) = (1,1,1,1)
        [Toggle] _UseVertexColor ("Use vertex colour", Float) = 1
        _Smoothness ("Smoothness", Range(0,1)) = 0.2
        _Metallic ("Metallic", Range(0,1)) = 0
        [HDR] _EmissionColor ("Emission", Color) = (0,0,0,1)
        _Cutoff ("Alpha clip threshold", Range(0,1)) = 0.5
        [Toggle(_ALPHATEST_ON)] _AlphaClip ("Alpha clip", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
        [Toggle] _SignalLens ("Traffic signal lens (Uv2 driven)", Float) = 0
        _Wet ("Wetness (rain)", Range(0,1)) = 0
        [Toggle] _Pbr ("Photo PBR set (world-space triplanar)", Float) = 0
        [NoScaleOffset] _NormalMap ("Normal (GL)", 2D) = "bump" {}
        [NoScaleOffset] _RoughMap ("Roughness", 2D) = "white" {}
        [NoScaleOffset] _AOMap ("Ambient occlusion", 2D) = "white" {}
        _TriScale ("Texture repeats per metre", Float) = 0.25
        _NormalStrength ("Normal strength", Range(0,2)) = 1
        _PbrBoost ("Albedo boost", Range(0.5,3)) = 1
        _SmoothScale ("Smoothness scale (PBR)", Range(0,1.5)) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" "UniversalMaterialType" = "Lit" }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_NormalMap); TEXTURE2D(_RoughMap); TEXTURE2D(_AOMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _EmissionColor;
                half _UseVertexColor;
                half _Smoothness;
                half _Metallic;
                half _Cutoff;
                half _Cull;
                half _SignalLens;
                half _Wet;
                half _Pbr;
                half _TriScale;
                half _NormalStrength;
                half _PbrBoost;
                half _SmoothScale;
            CBUFFER_END

            float _SpsTime;          // global: seconds, drives the signal lenses
            float _SpsNight;         // global: 0 day … 1 night (window glow)

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float2 uv2        : TEXCOORD1;
                half4  color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3  normalWS   : TEXCOORD1;
                float2 uv         : TEXCOORD2;
                float2 uv2        : TEXCOORD3;
                half4  color      : COLOR;
                half   fogFactor  : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                VertexPositionInputs vp = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs vn = GetVertexNormalInputs(IN.normalOS);
                OUT.positionCS = vp.positionCS;
                OUT.positionWS = vp.positionWS;
                OUT.normalWS = vn.normalWS;
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.uv2 = IN.uv2;
                OUT.color = IN.color;
                OUT.fogFactor = ComputeFogFactor(vp.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN, half facing : VFACE) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                half3 vcol = lerp(half3(1,1,1), SRGBToLinear(IN.color.rgb), _UseVertexColor);
                half3 albedo = tex.rgb * _BaseColor.rgb * vcol;
                half3 nGeo = normalize(IN.normalWS) * ((facing > 0) ? 1 : -1);
                half3 pbrNormal = nGeo; half pbrRough = 1; half pbrAO = 1;
                if (_Pbr > 0.5)
                {
                    // world-space triplanar mapping: independent of the mesh UVs, seamless across tiles (origin shifts are multiples of 500 m)
                    float3 pw = IN.positionWS;
                    half3 bw = pow(abs(nGeo), 4); bw /= (bw.x + bw.y + bw.z);
                    float2 uvX = pw.zy * _TriScale, uvY = pw.xz * _TriScale, uvZ = pw.xy * _TriScale;
                    half3 c = half3(0,0,0); half r = 0, ao = 0;
                    half3 tx = half3(0,0,1), ty = half3(0,0,1), tz = half3(0,0,1);
                    if (bw.x > 0.04) { c += SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvX).rgb * bw.x; r += SAMPLE_TEXTURE2D(_RoughMap, sampler_BaseMap, uvX).r * bw.x; ao += SAMPLE_TEXTURE2D(_AOMap, sampler_BaseMap, uvX).r * bw.x; tx = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_BaseMap, uvX), _NormalStrength); }
                    if (bw.y > 0.04) { c += SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvY).rgb * bw.y; r += SAMPLE_TEXTURE2D(_RoughMap, sampler_BaseMap, uvY).r * bw.y; ao += SAMPLE_TEXTURE2D(_AOMap, sampler_BaseMap, uvY).r * bw.y; ty = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_BaseMap, uvY), _NormalStrength); }
                    if (bw.z > 0.04) { c += SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvZ).rgb * bw.z; r += SAMPLE_TEXTURE2D(_RoughMap, sampler_BaseMap, uvZ).r * bw.z; ao += SAMPLE_TEXTURE2D(_AOMap, sampler_BaseMap, uvZ).r * bw.z; tz = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_BaseMap, uvZ), _NormalStrength); }
                    half wsum = (bw.x > 0.04 ? bw.x : 0) + (bw.y > 0.04 ? bw.y : 0) + (bw.z > 0.04 ? bw.z : 0); c /= wsum; r /= wsum; ao /= wsum;
                    tx = half3(tx.xy + nGeo.zy, abs(tx.z) * nGeo.x); ty = half3(ty.xy + nGeo.xz, abs(ty.z) * nGeo.y); tz = half3(tz.xy + nGeo.xy, abs(tz.z) * nGeo.z);
                    pbrNormal = normalize(tx.zyx * bw.x + ty.xzy * bw.y + tz.xyz * bw.z);
                    albedo = c * _PbrBoost * _BaseColor.rgb * vcol; pbrRough = r; pbrAO = ao;
                }
                half alpha = tex.a * _BaseColor.a;
                #if defined(_ALPHATEST_ON)
                    clip(alpha - _Cutoff);
                #endif

                half3 emission = _EmissionColor.rgb;
                if (_SignalLens > 0.5)
                {
                    // Uv2.x = lens (0 red, 1 amber, 2 green), Uv2.y = group + phase/1000. Same cycle as Core SignalModel (60 s: green 24, amber 3, red, red+amber last second).
                    float group = floor(IN.uv2.y + 0.0001);
                    float offset = frac(IN.uv2.y + 0.0001) * 1000.0;
                    float t = fmod(_SpsTime + offset + group * 30.0, 60.0);
                    float lens = IN.uv2.x;
                    bool lit = (lens > 1.5 && t < 24.0) || (lens > 0.5 && lens < 1.5 && ((t >= 24.0 && t < 27.0) || t >= 59.0)) || (lens < 0.5 && t >= 27.0);
                    emission += lit ? albedo * 6.0 : albedo * 0.0;
                    albedo *= lit ? 1.0 : 0.15;
                }

                half3 normalWS = pbrNormal;

                // rain: darker, glossier surfaces
                half baseSmooth = _Pbr > 0.5 ? saturate((1.0 - pbrRough) * _SmoothScale) : _Smoothness;
                half smoothV = saturate(lerp(baseSmooth, max(baseSmooth, 0.88), _Wet));
                albedo *= lerp(1.0, 0.72, _Wet);

                InputData inputData = (InputData)0;
                inputData.positionWS = IN.positionWS;
                inputData.positionCS = IN.positionCS;
                inputData.normalWS = NormalizeNormalPerPixel(normalWS);
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(IN.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                inputData.fogCoord = InitializeInputDataFog(float4(IN.positionWS, 1.0), IN.fogFactor);
                inputData.vertexLighting = half3(0, 0, 0);
                inputData.bakedGI = SampleSH(inputData.normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData s = (SurfaceData)0;
                s.albedo = albedo;
                s.metallic = _Metallic;
                s.specular = half3(0, 0, 0);
                s.smoothness = smoothV;
                s.occlusion = pbrAO;
                s.emission = emission;
                s.alpha = alpha;
                s.normalTS = half3(0, 0, 1);

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
