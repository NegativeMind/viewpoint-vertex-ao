using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ViewpointBasedAO {
    // TODO URPのバージョンアップに合わせてAPI呼び出しを修正 https://zenn.dev/sakutaro/articles/convert_blitter

    /// <summary>
    /// ViewpointAO用のScriptableRenderPass
    /// </summary>
    public class ViewpointAORendererPass : ScriptableRenderPass {

        public Material blitMaterial = null;
        public FilterMode filterMode { get; set; }

        private ViewpointAOSettings settings;

        private RenderTargetIdentifier source { get; set; }
        private RenderTargetIdentifier destination { get; set; }

        RenderTargetHandle m_TemporaryColorTexture;
        RenderTargetHandle m_DestinationTexture;
        string m_ProfilerTag;

        public ViewpointAORendererPass (RenderPassEvent renderPassEvent, ViewpointAOSettings settings, string tag) {
            this.renderPassEvent = renderPassEvent;
            this.settings = settings;
            blitMaterial = settings.blitMaterial;
            m_ProfilerTag = tag;
            m_TemporaryColorTexture.Init ("_TemporaryColorTexture");
            if (settings.dstType == Target.TextureID) {
                m_DestinationTexture.Init (settings.dstTextureId);
            }
        }

        public void Setup (RenderTargetIdentifier source, RenderTargetIdentifier destination) {
            this.source = source;
            this.destination = destination;

#if UNITY_2020_1_OR_NEWER
            if (settings.requireDepthNormals)
                ConfigureInput (ScriptableRenderPassInput.Normal);

            if (settings.requireDepth)
                ConfigureInput (ScriptableRenderPassInput.Depth);
#endif
        }

        public override void Execute (ScriptableRenderContext context, ref RenderingData renderingData) {
            CommandBuffer cmd = CommandBufferPool.Get (m_ProfilerTag);

            RenderTextureDescriptor opaqueDesc = renderingData.cameraData.cameraTargetDescriptor;
            opaqueDesc.depthBufferBits = 0;

            if (settings.setInverseViewMatrix) {
                Shader.SetGlobalMatrix ("_InverseView", renderingData.cameraData.camera.cameraToWorldMatrix);
            }

            if (settings.dstType == Target.TextureID) {
                if (settings.overrideGraphicsFormat) {
                    opaqueDesc.graphicsFormat = settings.graphicsFormat;
                }
                cmd.GetTemporaryRT (m_DestinationTexture.id, opaqueDesc, filterMode);
            }

            // Debug.Log($"src = {source},     dst = {destination} blit material = {blitMaterial}");
            // Can't read and write to same color target, use a TemporaryRT
            if (source == destination || (settings.srcType == settings.dstType && settings.srcType == Target.CameraColor)) {

                cmd.GetTemporaryRT (m_TemporaryColorTexture.id, opaqueDesc, filterMode);
                Blit (cmd, source, m_TemporaryColorTexture.Identifier (), blitMaterial, settings.blitMaterialPassIndex);
                Blit (cmd, m_TemporaryColorTexture.Identifier (), destination);

                //Blitter.BlitCameraTexture (cmd, source, destination, blitMaterial, 0);

            } else {
                Blit (cmd, source, destination, blitMaterial, settings.blitMaterialPassIndex);
            }
            context.ExecuteCommandBuffer (cmd);
            CommandBufferPool.Release (cmd);
        }

        public override void FrameCleanup (CommandBuffer cmd) {
            if (settings.dstType == Target.TextureID) {
                cmd.ReleaseTemporaryRT (m_DestinationTexture.id);
            }
            if (source == destination || (settings.srcType == settings.dstType && settings.srcType == Target.CameraColor)) {
                cmd.ReleaseTemporaryRT (m_TemporaryColorTexture.id);
            }
        }
    }

} // namespace ViewpointBasedAO
