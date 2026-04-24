

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

                // Compare clip-space depth directly — avoids any dependency on _InverseView.
                // _VP (set from C#) maps to [0,1] for D3D/Metal (near=0, far=1).
                // SampleSceneDepth on D3D/Metal is reversed (near=1, far=0), so flip it.
                // On OpenGL _VP is not remapped, so NDC z ∈ [-1,1]; map to [0,1] to match.
                float d_vertex = vertexPos.z / vertexPos.w;
                #if defined(UNITY_REVERSED_Z)
                    float d_scene = 1.0 - SampleSceneDepth(posInCamDepth.xy);
                #else
                    d_vertex = d_vertex * 0.5 + 0.5;
                    float d_scene = SampleSceneDepth(posInCamDepth.xy);
                #endif
                float visible = abs(d_vertex - d_scene) <= 0.005 ? 1.0 : 0.0;

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
