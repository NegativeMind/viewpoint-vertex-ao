using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace NegativeMind.ViewpointVertexAO {
    /// <summary>
    /// MonoBehaviour entry point for Viewpoint-Based AO. Attach to a GameObject to compute and apply AO.
    /// Automatically detects URP RendererData, adds the RendererFeature, and reserves a layer.
    /// </summary>
    public class AOBehaviour : MonoBehaviour {

        [Range (0.0f, 1.0f)] public float spreadAngle = 0.8f;
        public SamplingLevel samplingLevel = SamplingLevel.High;
        [Range (0.0f, 1.0f)] public float aoScale = 1f;
        public bool showDebug = false;

        MeshFilter[] meshFilters;
        int[] savedLayer;
        ShadowCastingMode[] savedShadowMode;
        Vector3[] rayDirection;
        Bounds allBounds;

        RenderTexture aoRenderTexture;
        RenderTexture aoRenderTextureForShader;
        Texture2D vertexPositionsTexture;
        Texture2D vertexNormalsTexture;
        Material ambientOcclusionMat;
        Material[] aoMaterials;

        int allVertexCount = 0;
        int vertByRow = 256;
        float radSurface;

        Camera aoCamera;
        GameObject aoCameraGO;
        int aoLayerIndex = -1;

        RenderTexture depthCaptureRT;
        Material depthCaptureMat;

        const string cameraName = "ViewpointAOCamera";
        const string computeShaderName = "ViewpointAO/ComputeVertexAO";
        const string renderShaderName = "ViewpointAO/RenderWithVertexAO";
        const string previewShaderName = "ViewpointAO/PreviewVertexAO";

        void Awake () {
            aoLayerIndex = FindAvailableLayer ();
            if (aoLayerIndex < 0) {
                Debug.LogError ("[ViewpointAO] AO 計算用の空きレイヤーがありません (Layer 8-31 がすべて使用中)。");
                enabled = false;
                return;
            }

            ambientOcclusionMat = new Material (Shader.Find (computeShaderName));

            var depthCaptureShader = Shader.Find ("Hidden/ViewpointAO/AODepthCapture");
            if (depthCaptureShader == null) {
                Debug.LogError ("[ViewpointAO] AODepthCapture シェーダーが見つかりません。");
                enabled = false;
                return;
            }
            depthCaptureMat = new Material (depthCaptureShader);
        }

        void Start () {
            float t0 = Time.realtimeSinceStartup;

            InitializeObjectAndGetBounds ();
            GenerateSamplePositions ();
            CreateAoCamera ();
            VertexDataToTexture ();
            ComputeAmbientOcclusion ();
            var aoTex = ReadAOResult ();
            BakeAO (aoTex);
            DisposeResources ();

            Debug.Log ("[ViewpointAO] Compute times: " + (Time.realtimeSinceStartup - t0).ToString ("F3") + " seconds");
        }

        // ---------------------------------------------------------------------------------
        // Setup
        // ---------------------------------------------------------------------------------

        // Use the "AOLayer" layer if it exists in the project; otherwise find a free slot (8–31)
        static int FindAvailableLayer () {
            int named = LayerMask.NameToLayer ("AOLayer");
            if (named >= 0) return named;

            for (int i = 8; i < 32; i++) {
                if (string.IsNullOrEmpty (LayerMask.LayerToName (i))) return i;
            }
            return -1;
        }

        // ---------------------------------------------------------------------------------
        // AO Computation
        // ---------------------------------------------------------------------------------

        void InitializeObjectAndGetBounds () {
            // Collect all MeshFilters under the attached GameObject
            meshFilters = GetComponentsInChildren<MeshFilter> ()
                .Where (mf => mf.GetComponent<MeshRenderer> () != null)
                .ToArray ();

            if (meshFilters.Length == 0) {
                Debug.LogWarning ("[ViewpointAO] MeshFilter が見つかりません: " + gameObject.name);
                return;
            }

            savedLayer = new int[meshFilters.Length];
            savedShadowMode = new ShadowCastingMode[meshFilters.Length];

            for (int i = 0; i < meshFilters.Length; i++) {
                var mr = meshFilters[i].GetComponent<MeshRenderer> ();

                if (i == 0) allBounds = mr.bounds;
                else allBounds.Encapsulate (mr.bounds);

                savedLayer[i] = meshFilters[i].gameObject.layer;
                savedShadowMode[i] = mr.shadowCastingMode;
                mr.shadowCastingMode = ShadowCastingMode.TwoSided;
            }

            allVertexCount = meshFilters.Sum (mf => mf.sharedMesh.vertexCount);
        }

        void GenerateSamplePositions () {
            radSurface = Mathf.Max (allBounds.extents.x, Mathf.Max (allBounds.extents.y, allBounds.extents.z)) * 1.3f;

            float golden_angle = Mathf.PI * (3 - Mathf.Sqrt (5));
            // spreadAngle=1: full sphere (z +1→-1), spreadAngle=0: equatorial ring (z=0)
            float zRange = 1.0f - 1.0f / (int) samplingLevel;
            float start = zRange;
            float end = -zRange;

            rayDirection = new Vector3[(int) samplingLevel];
            for (int i = 0; i < (int) samplingLevel; i++) {
                float theta = golden_angle * i;
                float z = start + i * (end - start) / (int) samplingLevel;
                float radius = Mathf.Sqrt (1 - z * z);
                rayDirection[i] = allBounds.center + new Vector3 (
                    radius * Mathf.Cos (theta),
                    radius * Mathf.Sin (theta),
                    z
                ) * radSurface;
            }
        }

        void CreateAoCamera () {
            // Create a dedicated camera for AO computation; destroyed after baking
            aoCameraGO = new GameObject (cameraName);
            aoCamera = aoCameraGO.AddComponent<Camera> ();

            aoCamera.enabled = false;
            aoCamera.orthographic = true;
            aoCamera.cullingMask = 1 << aoLayerIndex;
            aoCamera.clearFlags = CameraClearFlags.Depth;
            aoCamera.nearClipPlane = 0.0001f;
            aoCamera.backgroundColor = Color.white;
            aoCamera.allowHDR = false;
            aoCamera.allowMSAA = false;
            aoCamera.allowDynamicResolution = false;
            aoCamera.depthTextureMode = DepthTextureMode.Depth;
            aoCamera.orthographicSize = radSurface * 1.1f;
            aoCamera.farClipPlane = radSurface * 2f;
            aoCamera.aspect = 1f;
            aoCamera.stereoTargetEye = StereoTargetEyeMask.None;

            var camData = aoCamera.GetUniversalAdditionalCameraData ();
            camData.allowXRRendering = false;
            camData.renderShadows = false;

            int height = Mathf.CeilToInt (allVertexCount / (float) vertByRow);

            // Depth capture RT: 256×256 square for scene depth rendering.
            // RFloat gives full precision; depth format (24) enables correct fragment ordering.
            depthCaptureRT = new RenderTexture (256, 256, 24, RenderTextureFormat.RFloat) {
                filterMode = FilterMode.Point,
                anisoLevel = 0
            };

            aoRenderTexture = new RenderTexture (vertByRow, height, 0, RenderTextureFormat.ARGBHalf) {
                anisoLevel = 0,
                filterMode = FilterMode.Point
            };
            aoRenderTextureForShader = new RenderTexture (vertByRow, height, 0, RenderTextureFormat.ARGBHalf) {
                anisoLevel = 0,
                filterMode = FilterMode.Point
            };
            vertexPositionsTexture = new Texture2D (vertByRow, height, TextureFormat.RGBAFloat, false) {
                anisoLevel = 0,
                filterMode = FilterMode.Point
            };
            vertexNormalsTexture = new Texture2D (vertByRow, height, TextureFormat.RGBAFloat, false) {
                anisoLevel = 0,
                filterMode = FilterMode.Point
            };
        }

        void VertexDataToTexture () {
            int size = vertexPositionsTexture.width * vertexPositionsTexture.height;
            Color[] positions = new Color[size];
            Color[] normals = new Color[size];

            int id = 0;
            foreach (var mf in meshFilters) {
                var t = mf.transform;
                var verts = mf.sharedMesh.vertices;
                var norms = mf.sharedMesh.normals;
                bool hasNormals = norms != null && norms.Length == verts.Length;
                for (int i = 0; i < verts.Length; i++) {
                    Vector3 wp = t.TransformPoint (verts[i]);
                    positions[id] = new Color (wp.x, wp.y, wp.z, 0f);
                    Vector3 wn = hasNormals ?
                        t.TransformDirection (norms[i]).normalized :
                        Vector3.up;
                    normals[id] = new Color (wn.x, wn.y, wn.z, 0f);
                    id++;
                }
            }

            vertexPositionsTexture.SetPixels (positions);
            vertexPositionsTexture.Apply (false, false);
            vertexNormalsTexture.SetPixels (normals);
            vertexNormalsTexture.Apply (false, false);
        }

        void ComputeAmbientOcclusion () {
            ambientOcclusionMat.SetInt ("_uCount", (int) samplingLevel);
            ambientOcclusionMat.SetTexture ("_AOTex2", aoRenderTextureForShader);
            ambientOcclusionMat.SetTexture ("_uVertex", vertexPositionsTexture);
            ambientOcclusionMat.SetTexture ("_uNormal", vertexNormalsTexture);
            ambientOcclusionMat.SetFloat ("_SpreadAngle", spreadAngle);

            bool d3d = SystemInfo.graphicsDeviceVersion.IndexOf ("Direct3D") > -1;
            bool metal = SystemInfo.graphicsDeviceVersion.IndexOf ("Metal") > -1;

            for (int i = 0; i < (int) samplingLevel; i++) {
                aoCamera.transform.position = rayDirection[i];
                aoCamera.transform.LookAt (allBounds.center);

                Matrix4x4 V = aoCamera.worldToCameraMatrix;
                Matrix4x4 P = aoCamera.projectionMatrix;
                if (d3d || metal) {
                    for (int a = 0; a < 4; a++) P[1, a] = -P[1, a];
                    for (int a = 0; a < 4; a++) P[2, a] = P[2, a] * 0.5f + P[3, a] * 0.5f;
                }

                // Render scene depth via CommandBuffer — works reliably in URP.
                // RenderWithShader is not guaranteed to invoke the replacement shader through URP.
                var cmd = new CommandBuffer { name = "AO Depth Capture" };
                cmd.SetRenderTarget (depthCaptureRT);
                cmd.ClearRenderTarget (true, true, Color.white);
                cmd.SetViewProjectionMatrices (aoCamera.worldToCameraMatrix, aoCamera.projectionMatrix);
                foreach (var mf in meshFilters) {
                    for (int sub = 0; sub < mf.sharedMesh.subMeshCount; sub++)
                        cmd.DrawMesh (mf.sharedMesh, mf.transform.localToWorldMatrix, depthCaptureMat, sub, 0);
                }
                Graphics.ExecuteCommandBuffer (cmd);
                cmd.Release ();

                ambientOcclusionMat.SetTexture ("_ExplicitDepth", depthCaptureRT);
                ambientOcclusionMat.SetMatrix ("_VP", P * V);
                ambientOcclusionMat.SetInt ("_curCount", i);
                ambientOcclusionMat.SetVector ("_CameraWorldPos", rayDirection[i]);

                // Compute AO directly — no URP renderer pass needed.
                Graphics.Blit (aoRenderTextureForShader, aoRenderTexture, ambientOcclusionMat);
                Graphics.CopyTexture (aoRenderTexture, aoRenderTextureForShader);
            }

            for (int i = 0; i < meshFilters.Length; i++) {
                meshFilters[i].gameObject.layer = savedLayer[i];
                meshFilters[i].GetComponent<MeshRenderer> ().shadowCastingMode = savedShadowMode[i];
            }
        }

        Texture2D ReadAOResult () {
            int texW = aoRenderTextureForShader.width;
            int texH = aoRenderTextureForShader.height;
            RenderTexture.active = aoRenderTextureForShader;
            var raw = new Texture2D (texW, texH, TextureFormat.RGBAHalf, false);
            raw.ReadPixels (new Rect (0, 0, texW, texH), 0, 0);
            raw.Apply (false, false);
            RenderTexture.active = null;

            // R = running average of visibility [0,1] (computed in shader); copy to all channels
            Color[] pixels = raw.GetPixels ();
            Object.DestroyImmediate (raw);
            for (int i = 0; i < pixels.Length; i++) {
                float v = pixels[i].r;
                pixels[i] = new Color (v, v, v, v);
            }

            var aoTex = new Texture2D (texW, texH, TextureFormat.RGBAHalf, false) {
                anisoLevel = 0,
                filterMode = FilterMode.Point
            };
            aoTex.SetPixels (pixels);
            aoTex.Apply (false, false);
            return aoTex;
        }

        void BakeAO (Texture2D aoTex) {
            float invW = 1f / aoTex.width;
            float invH = 1f / aoTex.height;
            int idVert = 0;

            aoMaterials = new Material[meshFilters.Length];

            for (int i = 0; i < meshFilters.Length; i++) {
                var mesh = meshFilters[i].mesh;
                int vertCount = mesh.vertexCount;
                var uv2 = new Vector2[vertCount];
                var colors = new Color[vertCount];

                for (int j = 0; j < vertCount; j++) {
                    int px = idVert % vertByRow;
                    int py = idVert / vertByRow;
                    uv2[j] = new Vector2 ((px + 0.5f) * invW, (py + 0.5f) * invH);
                    colors[j] = aoTex.GetPixel (px, py);
                    idVert++;
                }

                mesh.uv2 = uv2;
                mesh.colors = colors;

                var renderer = meshFilters[i].GetComponent<Renderer> ();
                var targetShader = Shader.Find (showDebug ? previewShaderName : renderShaderName);
                var mat = new Material (targetShader);
                if (!showDebug && renderer.sharedMaterial != null) {
                    mat.CopyPropertiesFromMaterial (renderer.sharedMaterial);
                    mat.shader = targetShader; // CopyPropertiesFromMaterial also copies the source shader
                }
                mat.SetFloat ("_AOScale", aoScale);
                mat.SetTexture ("_AOTex", aoTex);
                renderer.material = mat;
                aoMaterials[i] = mat;
            }
        }

        void Update () {
            if (aoMaterials == null) return;
            foreach (var mat in aoMaterials)
                mat.SetFloat ("_AOScale", aoScale);
        }

        void DisposeResources () {
            if (depthCaptureRT != null) {
                depthCaptureRT.Release ();
                depthCaptureRT = null;
            }

            if (depthCaptureMat != null) {
                Destroy (depthCaptureMat);
                depthCaptureMat = null;
            }

            if (aoCameraGO != null) {
                Destroy (aoCameraGO);
                aoCameraGO = null;
                aoCamera = null;
            }
        }

        void OnDestroy () {
            // Fallback cleanup if the component is destroyed before Start() finishes
            DisposeResources ();
        }

        // ---------------------------------------------------------------------------------
        // Gizmos
        // ---------------------------------------------------------------------------------

        void OnDrawGizmos () {
            if (!showDebug || rayDirection == null) return;
            Gizmos.color = Color.red;
            foreach (var pos in rayDirection) {
                Gizmos.DrawSphere (pos, 0.05f);
                Gizmos.DrawLine (allBounds.center, pos);
            }
        }
    }
}