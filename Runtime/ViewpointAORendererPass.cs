using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ViewpointBasedAO {

    /// <summary>
    /// ViewpointAO用のScriptableRenderPass
    /// </summary>
    public class ViewpointAORendererPass : ScriptableRenderPass {

        public Material blitMaterial = null;
        public FilterMode filterMode { get; set; }

        private readonly ViewpointAOSettings settings;
        private RTHandle m_Source;
        private RTHandle m_Destination;
        private string m_ProfilerTag;

        private static readonly int k_TempColorTexId = Shader.PropertyToID ("_TemporaryColorTexture");

        public ViewpointAORendererPass (RenderPassEvent renderPassEvent, ViewpointAOSettings settings, string tag) {
            this.renderPassEvent = renderPassEvent;
            this.settings = settings;
            blitMaterial = settings.blitMaterial;
            m_ProfilerTag = tag;
        }

        public void Setup (RTHandle source, RTHandle destination) {
            m_Source = source;
            m_Destination = destination;

            if (settings.requireDepthNormals)
                ConfigureInput (ScriptableRenderPassInput.Normal);
            if (settings.requireDepth)
                ConfigureInput (ScriptableRenderPassInput.Depth);
        }

        public override void Execute (ScriptableRenderContext context, ref RenderingData renderingData) {
            CommandBuffer cmd = CommandBufferPool.Get (m_ProfilerTag);

            RenderTextureDescriptor opaqueDesc = renderingData.cameraData.cameraTargetDescriptor;
            opaqueDesc.depthBufferBits = 0;

            if (settings.setInverseViewMatrix)
                Shader.SetGlobalMatrix ("_InverseView", renderingData.cameraData.camera.cameraToWorldMatrix);

            if (settings.dstType == Target.TextureID) {
                if (settings.overrideGraphicsFormat)
                    opaqueDesc.graphicsFormat = settings.graphicsFormat;
                cmd.GetTemporaryRT (Shader.PropertyToID (settings.dstTextureId), opaqueDesc, filterMode);
            }

            // Can't read and write to same color target — use a temporary RT
            bool sameTarget = m_Source == m_Destination ||
                (settings.srcType == settings.dstType && settings.srcType == Target.CameraColor);

            if (sameTarget) {
                cmd.GetTemporaryRT (k_TempColorTexId, opaqueDesc, filterMode);
                cmd.Blit (m_Source, new RenderTargetIdentifier (k_TempColorTexId), blitMaterial, settings.blitMaterialPassIndex);
                cmd.Blit (new RenderTargetIdentifier (k_TempColorTexId), m_Destination);
            } else {
                cmd.Blit (m_Source, m_Destination, blitMaterial, settings.blitMaterialPassIndex);
            }

            context.ExecuteCommandBuffer (cmd);
            CommandBufferPool.Release (cmd);
        }

        public override void OnCameraCleanup (CommandBuffer cmd) {
            if (settings.dstType == Target.TextureID)
                cmd.ReleaseTemporaryRT (Shader.PropertyToID (settings.dstTextureId));

            bool sameTarget = m_Source == m_Destination ||
                (settings.srcType == settings.dstType && settings.srcType == Target.CameraColor);
            if (sameTarget)
                cmd.ReleaseTemporaryRT (k_TempColorTexId);
        }
    }

} // namespace ViewpointBasedAO
