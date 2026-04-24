Shader "Hidden/ViewpointAO/AODepthCapture"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        ZWrite On
        Cull Back

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 depth      : TEXCOORD0; // .x = clip z, .y = clip w
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.depth = o.positionCS.zw;
                return o;
            }

            // Output normalized linear depth [0,1] where 0 = near, 1 = far.
            // Matches the d_vertex convention in ComputeVertexAO.shader.
            float frag(Varyings i) : SV_Target
            {
                float d = i.depth.x / i.depth.y;
                #if UNITY_REVERSED_Z
                    return 1.0 - d;  // GPU: 1=near,0=far  →  output: 0=near,1=far
                #else
                    return d * 0.5 + 0.5;  // NDC [-1,1]  →  [0,1]
                #endif
            }
            ENDHLSL
        }
    }
}
