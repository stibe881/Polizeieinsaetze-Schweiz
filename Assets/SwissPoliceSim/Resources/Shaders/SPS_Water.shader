// Water: transparent, two scrolling normal layers, Fresnel reflection of the environment, sun glint, depth tint by view angle.
Shader "SPS/Water"
{
    Properties
    {
        _BumpMap ("Normal map", 2D) = "bump" {}
        _ShallowColor ("Shallow", Color) = (0.20, 0.42, 0.45, 1)
        _DeepColor ("Deep", Color) = (0.03, 0.12, 0.20, 1)
        _Opacity ("Opacity", Range(0,1)) = 0.86
        _Flow ("Flow direction xy, speed z, scale w", Vector) = (0.6, 0.3, 0.05, 0.12)
        _Smoothness ("Smoothness", Range(0,1)) = 0.92
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-100" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor; half4 _DeepColor; half _Opacity; float4 _Flow; half _Smoothness;
            CBUFFER_END
            float _SpsTime;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half3 normalWS : TEXCOORD1; float2 uv : TEXCOORD2; half fogFactor : TEXCOORD3; half4 color : COLOR; };

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                VertexPositionInputs vp = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = vp.positionCS; OUT.positionWS = vp.positionWS; OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.uv = IN.uv; OUT.color = IN.color; OUT.fogFactor = ComputeFogFactor(vp.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 flow = _Flow.xy * _Flow.z * _SpsTime;
                float2 uv1 = IN.uv * _Flow.w + flow;
                float2 uv2 = IN.uv * _Flow.w * 1.9 - flow * 0.7 + 0.37;
                half3 n1 = UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uv1));
                half3 n2 = UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uv2));
                half3 nTS = normalize(half3(n1.xy + n2.xy, n1.z * n2.z));
                half3 normalWS = normalize(half3(nTS.x * 0.35, 1.0, nTS.y * 0.35));       // water surface is horizontal
                float3 V = GetWorldSpaceNormalizeViewDir(IN.positionWS);
                float fres = pow(1.0 - saturate(dot(normalWS, V)), 4.0);
                fres = lerp(0.03, 1.0, fres);
                float3 refl = reflect(-V, normalWS);
                half3 env = GlossyEnvironmentReflection(refl, 1.0 - _Smoothness, 1.0);
                half3 body = lerp(_ShallowColor.rgb, _DeepColor.rgb, saturate(1.0 - dot(normalWS, V)));

                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light l = GetMainLight(shadowCoord);
                half3 H = normalize(l.direction + V);
                half spec = pow(saturate(dot(normalWS, H)), 220.0) * 2.5 * l.shadowAttenuation;
                half3 diffuse = body * (l.color * saturate(dot(normalWS, l.direction)) * l.shadowAttenuation * 0.6 + SampleSH(normalWS));
                half3 col = lerp(diffuse, env, fres) + l.color * spec;
                col = MixFog(col, IN.fogFactor);
                return half4(col, saturate(_Opacity + fres * 0.3));
            }
            ENDHLSL
        }
    }
    FallBack Off
}
