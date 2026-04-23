

Shader "ViewpointAO/PreviewVertexAO"
{
    Properties
    {
        _MainTex ("Base (RGB)", 2D) = "white" { }
        _AOScale ("AO Scale", Range(0.0, 5.0)) = 1.0
        _AOTex ("AO Texture", 2D) = "white" { }
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_AOTex);   SAMPLER(sampler_AOTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _AOTex_ST;
                float _AOScale;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float2 uv2 : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half aoVal : TEXCOORD1;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionHCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                // Sample per-vertex AO at exact texel; interpolate the scalar, not the UV
                o.aoVal = SAMPLE_TEXTURE2D_LOD(_AOTex, sampler_AOTex, v.uv2, 0).r;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                // aoVal [0,1]: 1=可視, 0=遮蔽 → baseColor に直接乗算
                half3 albedo = c.rgb * lerp(1.0h, i.aoVal, _AOScale);
                return half4(albedo, c.a);
            }

            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}
