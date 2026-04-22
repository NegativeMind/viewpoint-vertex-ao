using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ViewpointBasedAO {
    /// <summary>
    /// GameObjectにアタッチするだけでGeometric Ambient Occlusionを計算・適用するコンポーネント。
    /// URP RendererDataの検出・RendererFeatureの追加・レイヤー確保を自動で行う。
    /// </summary>
    public class ViewpointAOBehaviour : MonoBehaviour {
        [Range (0.0f, 1.0f)] public float aoScale = 1f;
        [Range (0.0f, 1.0f)] public float spreadAngle = 0.8f;
        public AOSamplingLevel samplingLevel = AOSamplingLevel.High;
        public bool showDebug = false;

        MeshFilter[] meshFilters;
        int[] savedLayer;
        ShadowCastingMode[] savedShadowMode;
        Vector3[] rayDirection;
        Bounds allBounds;

        RenderTexture aoRenderTexture;
        RenderTexture aoRenderTextureForShader;
        Texture2D vertexPositionsTexture;
        Material ambientOcclusionMat;

        int allVertexCount = 0;
        int vertByRow = 256;
        float radSurface;

        Camera aoCamera;
        GameObject aoCameraGO;
        int aoLayerIndex = -1;

        UniversalRendererData rendererData;
        ViewpointAORendererFeature dynamicFeature;

        const string cameraName = "ViewpointAOCamera";
        const string computeShaderName = "ViewpointAO/ComputeVertexAO";
        const string renderShaderName = "ViewpointAO/RenderWithVertexAO";
        const string previewShaderName = "ViewpointAO/PreviewVertexAO";

        void Awake () {
            rendererData = FindRendererData ();
            if (rendererData == null) {
                Debug.LogError ("[ViewpointAO] URP の UniversalRendererData が見つかりません。プロジェクトが URP を使用しているか確認してください。");
                enabled = false;
                return;
            }

            aoLayerIndex = FindAvailableLayer ();
            if (aoLayerIndex < 0) {
                Debug.LogError ("[ViewpointAO] AO 計算用の空きレイヤーがありません (Layer 8-31 がすべて使用中)。");
                enabled = false;
                return;
            }

            ambientOcclusionMat = new Material (Shader.Find (computeShaderName));

            // RendererFeature を動的に追加
            dynamicFeature = ScriptableObject.CreateInstance<ViewpointAORendererFeature> ();
            dynamicFeature.name = "ViewpointAO_Dynamic";
            ConfigureFeatureSettings (dynamicFeature, null);
            rendererData.rendererFeatures.Add (dynamicFeature);
            rendererData.SetDirty ();
        }

        void Start () {
            float t0 = Time.realtimeSinceStartup;

            InitializeObjectAndGetBounds ();
            GenerateSamplePositions ();
            CreateAoCamera ();
            VertexPositionsToTexture ();
            ComputeAmbientOcclusion ();
            var aoTex = ReadAOResult ();
            BakeAO (aoTex);
            DisposeResources ();

            Debug.Log ("[ViewpointAO] Compute times: " + (Time.realtimeSinceStartup - t0).ToString ("F3") + " seconds");
        }

        // ---------------------------------------------------------------------------------
        // セットアップ
        // ---------------------------------------------------------------------------------

        static UniversalRendererData FindRendererData () {
            var rpa = GraphicsSettings.currentRenderPipeline ??
                GraphicsSettings.renderPipelineAsset;
            var pipeline = rpa as UniversalRenderPipelineAsset;
            if (pipeline == null) {
                Debug.LogError ($"[ViewpointAO] URP が見つかりません。Project Settings > Graphics で URP アセットを設定してください。({(rpa == null ? "null" : rpa.GetType().Name)})");
                return null;
            }

            // URP の非公開フィールド m_RendererDataList をリフレクションで取得
            var field = typeof (UniversalRenderPipelineAsset)
                .GetField ("m_RendererDataList", BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null) {
                Debug.LogError ("[ViewpointAO] m_RendererDataList フィールドが見つかりません。URP のバージョンが非対応の可能性があります。");
                return null;
            }

            var dataList = field.GetValue (pipeline) as ScriptableRendererData[];
            if (dataList == null) {
                Debug.LogError ("[ViewpointAO] m_RendererDataList のキャストに失敗しました。");
                return null;
            }

            var result = dataList.OfType<UniversalRendererData> ().FirstOrDefault ();
            if (result == null)
                Debug.LogError ($"[ViewpointAO] RendererDataList に UniversalRendererData がありません。件数: {dataList.Length}, 型: {string.Join(", ", dataList.Select(d => d?.GetType().Name ?? "null"))}");
            return result;
        }

        // "AOLayer" がプロジェクトに存在すれば優先、なければ空きレイヤー(8-31)を使用
        static int FindAvailableLayer () {
            int named = LayerMask.NameToLayer ("AOLayer");
            if (named >= 0) return named;

            for (int i = 8; i < 32; i++) {
                if (string.IsNullOrEmpty (LayerMask.LayerToName (i))) return i;
            }
            return -1;
        }

        void ConfigureFeatureSettings (ViewpointAORendererFeature feature, RenderTexture dstTexture) {
            var s = feature.settings;
            s.blitMaterial = ambientOcclusionMat;
            s.setInverseViewMatrix = true;
            s.dstType = Target.RenderTextureObject;
            s.dstTextureObject = dstTexture;
            s.cameraName = cameraName;
            s.requireDepth = true;
            s.overrideGraphicsFormat = true;
            s.graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat;
            feature.Create ();
        }

        // ---------------------------------------------------------------------------------
        // AO 計算
        // ---------------------------------------------------------------------------------

        void InitializeObjectAndGetBounds () {
            // アタッチした GameObject 配下の MeshFilter を全取得
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
            float zRange = spreadAngle * (1.0f - 1.0f / (int) samplingLevel);
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
            // 専用 GameObject にカメラを作成し、AO 計算後に破棄する
            aoCameraGO = new GameObject (cameraName);
            aoCamera = aoCameraGO.AddComponent<Camera> ();

            aoCamera.enabled = true;
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

            var camData = aoCamera.GetUniversalAdditionalCameraData ();
            camData.renderShadows = false;
            camData.requiresColorOption = CameraOverrideOption.On;
            camData.requiresDepthOption = CameraOverrideOption.On;
            camData.renderPostProcessing = true;

            int height = Mathf.CeilToInt (allVertexCount / (float) vertByRow);

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

            // RenderTexture をセット後 Create() を再呼び出しして反映
            ConfigureFeatureSettings (dynamicFeature, aoRenderTexture);
            rendererData.SetDirty ();
        }

        void VertexPositionsToTexture () {
            int size = vertexPositionsTexture.width * vertexPositionsTexture.height;
            Color[] vertexInfo = new Color[size];

            int id = 0;
            foreach (var mf in meshFilters) {
                var t = mf.transform;
                foreach (var v in mf.sharedMesh.vertices) {
                    Vector3 wp = t.TransformPoint (v);
                    vertexInfo[id++] = new Color (wp.x, wp.y, wp.z, 0f);
                }
            }

            vertexPositionsTexture.SetPixels (vertexInfo);
            vertexPositionsTexture.Apply (false, false);
        }

        void ComputeAmbientOcclusion () {
            ambientOcclusionMat.SetInt ("_uCount", (int) samplingLevel);
            ambientOcclusionMat.SetTexture ("_AOTex2", aoRenderTextureForShader);
            ambientOcclusionMat.SetTexture ("_uVertex", vertexPositionsTexture);

            for (int i = 0; i < meshFilters.Length; i++)
                meshFilters[i].gameObject.layer = aoLayerIndex;

            for (int i = 0; i < (int) samplingLevel; i++) {
                aoCamera.transform.position = rayDirection[i];
                aoCamera.transform.LookAt (allBounds.center);

                Matrix4x4 V = aoCamera.worldToCameraMatrix;
                Matrix4x4 P = aoCamera.projectionMatrix;

                bool d3d = SystemInfo.graphicsDeviceVersion.IndexOf ("Direct3D") > -1;
                bool metal = SystemInfo.graphicsDeviceVersion.IndexOf ("Metal") > -1;
                if (d3d || metal) {
                    for (int a = 0; a < 4; a++) P[1, a] = -P[1, a];
                    for (int a = 0; a < 4; a++) P[2, a] = P[2, a] * 0.5f + P[3, a] * 0.5f;
                }

                ambientOcclusionMat.SetMatrix ("_VP", P * V);
                ambientOcclusionMat.SetInt ("_curCount", i);
                aoCamera.Render ();

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
            var aoTex = new Texture2D (texW, texH, TextureFormat.RGBAHalf, false) {
                anisoLevel = 0,
                filterMode = FilterMode.Point
            };
            aoTex.ReadPixels (new Rect (0, 0, texW, texH), 0, 0);
            aoTex.Apply (false, false);
            RenderTexture.active = null;
            return aoTex;
        }

        void BakeAO (Texture2D aoTex) {
            float invW = 1f / aoTex.width;
            float invH = 1f / aoTex.height;
            int idVert = 0;

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
                var mat = new Material (Shader.Find (showDebug ? previewShaderName : renderShaderName));
                if (!showDebug) {
                    var origMat = renderer.sharedMaterial;
                    if (origMat != null) {
                        if (origMat.HasTexture ("_BaseMap"))
                            mat.SetTexture ("_BaseMap", origMat.GetTexture ("_BaseMap"));
                        else if (origMat.HasTexture ("_MainTex"))
                            mat.SetTexture ("_BaseMap", origMat.GetTexture ("_MainTex"));
                        if (origMat.HasColor ("_BaseColor"))
                            mat.SetColor ("_BaseColor", origMat.GetColor ("_BaseColor"));
                        else if (origMat.HasColor ("_Color"))
                            mat.SetColor ("_BaseColor", origMat.GetColor ("_Color"));
                    }
                }
                mat.SetFloat ("_AOScale", aoScale);
                mat.SetTexture ("_AOTex", aoTex);
                renderer.material = mat;
            }
        }

        void Update () {

        }

        void DisposeResources () {
            // 動的に追加した RendererFeature を削除
            if (dynamicFeature != null) {
                rendererData.rendererFeatures.Remove (dynamicFeature);
                Object.DestroyImmediate (dynamicFeature);
                dynamicFeature = null;
                rendererData.SetDirty ();
            }

            // AO 計算用の一時カメラを破棄
            if (aoCameraGO != null) {
                Destroy (aoCameraGO);
                aoCameraGO = null;
                aoCamera = null;
            }
        }

        void OnDestroy () {
            // 途中で破棄された場合のフォールバッククリーンアップ
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