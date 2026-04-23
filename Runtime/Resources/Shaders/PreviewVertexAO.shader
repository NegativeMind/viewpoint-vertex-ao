
Shader "ViewpointAO/PreviewVertexAO"
{
    Properties
    {
        _AOScale ("AO Scale", Range(0.0, 1.0)) = 1.0
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

            TEXTURE2D(_AOTex); SAMPLER(sampler_AOTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _AOTex_ST;
                float  _AOScale;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv2        : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                half   aoVal       : TEXCOORD0;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionHCS = TransformObjectToHClip(v.positionOS.xyz);
                o.aoVal = SAMPLE_TEXTURE2D_LOD(_AOTex, sampler_AOTex, v.uv2, 0).r;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                return half4(i.aoVal, i.aoVal, i.aoVal, 1.0h);
            }

            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}
