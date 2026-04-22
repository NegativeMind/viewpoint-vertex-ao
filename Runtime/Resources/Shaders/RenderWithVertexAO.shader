// MIT License — Copyright (c) 2017 Xavier Martinez, modified for URP 14 PBR lighting

Shader "ViewpointAO/RenderWithVertexAO"
{
    Properties
    {
        _BaseMap   ("Base (RGB)", 2D)          = "white" {}
        _BaseColor ("Base Color", Color)        = (1,1,1,1)
        _Metallic  ("Metallic",  Range(0,1))   = 0
        _Smoothness("Smoothness",Range(0,1))   = 0.5
        _AOTex     ("AO Texture", 2D)          = "white" {}
        _AOColor   ("AO Color",  Color)         = (0,0,0,1)
        _AOScale   ("AO Scale",  Range(0,5))   = 1.0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        // ── Forward Lit ──────────────────────────────────────────────────────────
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma shader_feature_local _VERTEX_COLOR_AO

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_AOTex);   SAMPLER(sampler_AOTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float4 _AOTex_ST;
                float4 _AOColor;
                float  _AOScale;
                float  _Metallic;
                float  _Smoothness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float2 uv2        : TEXCOORD1;
                #if defined(_VERTEX_COLOR_AO)
                float4 color      : COLOR;
                #endif
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float2 uv         : TEXCOORD2;
                half   aoVal      : TEXCOORD3;
                float  fogFactor  : TEXCOORD4;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs posInputs  = GetVertexPositionInputs(v.positionOS.xyz);
                VertexNormalInputs   normInputs = GetVertexNormalInputs(v.normalOS);
                o.positionCS = posInputs.positionCS;
                o.positionWS = posInputs.positionWS;
                o.normalWS   = normInputs.normalWS;
                o.uv         = TRANSFORM_TEX(v.uv, _BaseMap);
                #if defined(_VERTEX_COLOR_AO)
                o.aoVal      = v.color.r; // [0,1] from vertex color R channel
                #else
                o.aoVal      = SAMPLE_TEXTURE2D_LOD(_AOTex, sampler_AOTex, v.uv2, 0).r; // [0,1]
                #endif
                o.fogFactor  = ComputeFogFactor(posInputs.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 baseColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;

                // aoVal [0,1]: 1=可視, 0=遮蔽 → baseColor に直接乗算
                half3 albedo = baseColor.rgb * lerp(1.0h, i.aoVal, (half)_AOScale);

                InputData inputData = (InputData)0;
                inputData.positionWS              = i.positionWS;
                inputData.normalWS                = normalize(i.normalWS);
                inputData.viewDirectionWS         = GetWorldSpaceNormalizeViewDir(i.positionWS);
                inputData.shadowCoord             = TransformWorldToShadowCoord(i.positionWS);
                inputData.fogCoord                = i.fogFactor;
                inputData.bakedGI                 = SampleSH(inputData.normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                inputData.shadowMask              = unity_ProbesOcclusion;

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo      = albedo;
                surfaceData.alpha       = baseColor.a;
                surfaceData.metallic    = _Metallic;
                surfaceData.smoothness  = _Smoothness;
                surfaceData.normalTS    = half3(0, 0, 1);
                surfaceData.occlusion   = 1.0h;

                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                color.rgb = MixFog(color.rgb, i.fogFactor);
                return color;
            }
            ENDHLSL
        }

        // ── Shadow Caster ────────────────────────────────────────────────────────
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Back

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float4 _AOTex_ST;
                float4 _AOColor;
                float  _AOScale;
                float  _Metallic;
                float  _Smoothness;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };

            float4 vert(Attributes v) : SV_POSITION
            {
                float3 pw = TransformObjectToWorld(v.positionOS.xyz);
                float3 nw = TransformObjectToWorldNormal(v.normalOS);
                return TransformWorldToHClip(ApplyShadowBias(pw, nw, _LightDirection));
            }
            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }

        // ── Depth Only ───────────────────────────────────────────────────────────
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R Cull Back

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float4 _AOTex_ST;
                float4 _AOColor;
                float  _AOScale;
                float  _Metallic;
                float  _Smoothness;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            float4 vert(Attributes v) : SV_POSITION { return TransformObjectToHClip(v.positionOS.xyz); }
            half4  frag()             : SV_Target    { return 0; }
            ENDHLSL
        }
    }
}
