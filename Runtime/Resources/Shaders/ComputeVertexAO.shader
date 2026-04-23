

Shader "ViewpointAO/ComputeVertexAO"
{
    //Shader used to compute AO value for each vertex
    
    Properties
    {
        _AOTex ("AO Texture to blend", 2D) = "white" { }
        _AOTex2 ("AO Texture to blend", 2D) = "white" { }
        _uCount ("Total samples", int) = 128
        _curCount ("Current sample", int) = 0
        _uVertex ("Vertex texture", 2D) = "white" { }
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        ZWrite Off
        Pass
        {
            Cull Off
            Fog
            {
                Mode off
            }

            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag
            // #include "UnityCG.cginc"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            sampler2D _AOTex2;
            uniform sampler2D _uVertex;
            uniform sampler2D _uNormal;
            uniform float _uCount;
            uniform int _curCount;
            uniform float _SpreadAngle;
            uniform float3 _CameraWorldPos;

            float4x4 _VP;
            float4x4 _InverseView;


            float3 depthFromDepthTexture(float2 uv)
            {

                const float2 p11_22 = float2(unity_CameraProjection._11, unity_CameraProjection._22);
                const float2 p13_31 = float2(unity_CameraProjection._13, unity_CameraProjection._23);
                const float isOrtho = unity_OrthoParams.w;
                const float near = _ProjectionParams.y;
                const float far = _ProjectionParams.z;

                #if UNITY_REVERSED_Z
                    float d = SampleSceneDepth(uv);
                #else
                    // Adjust Z to match NDC for OpenGL ([-1, 1])
                    float d = lerp(UNITY_NEAR_CLIP_VALUE, 1, SampleSceneDepth(uv));
                #endif

                #if UNITY_REVERSED_Z
                    d = 1 - d;
                #endif

                // Does not seem to work
                // float3 worldPos = ComputeWorldSpacePosition(uv, d, UNITY_MATRIX_I_VP);

                float zOrtho = lerp(near, far, d);

                float zPers = near * far / lerp(far, near, d);
                float vz = lerp(zPers, zOrtho, isOrtho);

                float3 vpos = float3((uv * 2 - 1 - p13_31) / p11_22 * lerp(vz, 1, isOrtho), -vz);
                float4 wpos = mul(_InverseView, float4(vpos, 1));

                return wpos.xyz;
            }

            struct v2p
            {
                // float4 p : POSITION;
                float4 srcPos : TEXCOORD0;
                float4 posCS : POSITION;
            };

            struct Attributes
            {
                // The positionOS variable contains the vertex positions in object
                // space.
                float4 positionOS : POSITION;
            };

            v2p vert(Attributes v)
            {
                v2p o; // Shader output

                //
                o.posCS = TransformObjectToHClip(v.positionOS.xyz);
                o.srcPos = ComputeScreenPos(o.posCS);

                return o;
            }

            half4 frag(v2p i) : SV_TARGET
            {
                float2 uv = i.srcPos.xy / i.srcPos.w;

                float3 vertex = tex2D(_uVertex, uv).xyz;
                float3 normalRaw = tex2D(_uNormal, uv).xyz;
                float3 normal = length(normalRaw) > 0.001 ? normalize(normalRaw) : float3(0, 1, 0);
                float3 viewDir = normalize(_CameraWorldPos - vertex);

                // spreadAngle [0,1]: 1 = full hemisphere, <1 = tighter cone around surface normal
                float cosThreshold = cos(_SpreadAngle * 1.5707963f);
                float inCone = dot(viewDir, normal) >= cosThreshold ? 1.0 : 0.0;

                float4 vertexPos = mul(_VP, float4(vertex, 1.0));
                float4 posInCamDepth = ComputeScreenPos(vertexPos);
                posInCamDepth.xyz = posInCamDepth.xyz / posInCamDepth.w;

                float z = depthFromDepthTexture(posInCamDepth).z;
                float visible = abs(vertex.z - z) <= 0.01 ? 1.0 : 0.0;

                // R = running average of visibility [0,1], G = in-cone sample count
                float src_ao    = tex2D(_AOTex2, uv).r;
                float src_count = tex2D(_AOTex2, uv).g;
                if (_curCount == 0) { src_ao = 1.0; src_count = 0.0; }

                if (inCone > 0.5)
                {
                    float new_count = src_count + 1.0;
                    src_ao    = src_ao + (visible - src_ao) / new_count;
                    src_count = new_count;
                }

                return float4(src_ao, src_count, 0, src_ao);
            }

            ENDHLSL
        }
    }
}
