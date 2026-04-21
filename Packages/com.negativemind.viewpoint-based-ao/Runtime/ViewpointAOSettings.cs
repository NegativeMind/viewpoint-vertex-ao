using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace ViewpointBasedAO {
    /// <summary>
    /// ViewpointAO Renderer Featureの設定クラス
    /// </summary>
    [System.Serializable]
    public class ViewpointAOSettings {
        public RenderPassEvent Event = RenderPassEvent.AfterRenderingOpaques;

        public string cameraName;

        public Material blitMaterial = null;
        public int blitMaterialPassIndex = 0;
        public bool setInverseViewMatrix = false;
        public bool requireDepthNormals = false;
        public bool requireDepth = false;

        public Target srcType = Target.CameraColor;
        public string srcTextureId = "_CameraColorTexture";
        public RenderTexture srcTextureObject;

        public Target dstType = Target.CameraColor;
        public string dstTextureId = "_BlitPassTexture";
        public RenderTexture dstTextureObject;

        public bool overrideGraphicsFormat = false;
        public UnityEngine.Experimental.Rendering.GraphicsFormat graphicsFormat;
    }

    /// <summary>
    /// レンダーターゲットの種類を定義するenum
    /// </summary>
    public enum Target {
        CameraColor,
        TextureID,
        RenderTextureObject
    }
}
