using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ViewpointBasedAO {
	/*
	 * Blit Renderer Feature                                                https://github.com/Cyanilux/URP_BlitRenderFeature
	 * ------------------------------------------------------------------------------------------------------------------------
	 * Based on the Blit from the UniversalRenderingExamples
	 * https://github.com/Unity-Technologies/UniversalRenderingExamples/tree/master/Assets/Scripts/Runtime/RenderPasses
	 *
	 * Extended to allow for :
	 * - Specific access to selecting a source and destination (via current camera's color / texture id / render texture object
	 * - Automatic switching to using _AfterPostProcessTexture for After Rendering event, in order to correctly handle the blit after post processing is applied
	 * - Setting a _InverseView matrix (cameraToWorldMatrix), for shaders that might need it to handle calculations from screen space to world.
	 * 		e.g. Reconstruct world pos from depth : https://www.cyanilux.com/tutorials/depth/#blit-perspective
	 * - (URP v10) Enabling generation of DepthNormals (_CameraNormalsTexture)
	 * 		This will only include shaders who have a DepthNormals pass (mostly Lit Shaders / Graphs)
			(workaround for Unlit Shaders / Graphs: https://gist.github.com/Cyanilux/be5a796cf6ddb20f20a586b94be93f2b)
	 * ------------------------------------------------------------------------------------------------------------------------
	 * @Cyanilux
	*/
	// [CreateAssetMenu (menuName = "Rendering/ViewpointAORendererFeature")]
	/// <summary>
	/// URP用のViewpointAO Renderer Feature
	/// </summary>
	public class ViewpointAORendererFeature : ScriptableRendererFeature {

		public ViewpointAOSettings settings = new ViewpointAOSettings ();
		public ViewpointAORendererPass geoAOPass;
		RenderTargetIdentifier srcIdentifier, dstIdentifier;

		public override void Create () {
			var passIndex = settings.blitMaterial != null ? settings.blitMaterial.passCount - 1 : 1;
			settings.blitMaterialPassIndex = Mathf.Clamp (settings.blitMaterialPassIndex, -1, passIndex);
			geoAOPass = new ViewpointAORendererPass (settings.Event, settings, name);

			if (settings.Event == RenderPassEvent.AfterRenderingPostProcessing) {
				Debug.LogWarning ("Note that the \"After Rendering Post Processing\"'s Color target doesn't seem to work? (or might work, but doesn't contain the post processing) :( -- Use \"After Rendering\" instead!");
			}

			if (settings.graphicsFormat == UnityEngine.Experimental.Rendering.GraphicsFormat.None) {
				settings.graphicsFormat = SystemInfo.GetGraphicsFormat (UnityEngine.Experimental.Rendering.DefaultFormat.LDR);
			}

			UpdateSrcIdentifier ();
			UpdateDstIdentifier ();
		}

		void UpdateSrcIdentifier () {
			srcIdentifier = UpdateIdentifier (settings.srcType, settings.srcTextureId, settings.srcTextureObject);
		}

		void UpdateDstIdentifier () {
			dstIdentifier = UpdateIdentifier (settings.dstType, settings.dstTextureId, settings.dstTextureObject);
		}

		RenderTargetIdentifier UpdateIdentifier (Target type, string s, RenderTexture obj) {
			if (type == Target.RenderTextureObject) {
				return obj;
			} else if (type == Target.TextureID) {
				//RenderTargetHandle m_RTHandle = new RenderTargetHandle();
				//m_RTHandle.Init(s);
				//return m_RTHandle.Identifier();
				return s;
			}
			return new RenderTargetIdentifier ();
		}

		public override void AddRenderPasses (ScriptableRenderer renderer, ref RenderingData renderingData) {

			if (settings.cameraName != renderingData.cameraData.camera.name) {
				return;
			}

			if (settings.blitMaterial == null) {
				Debug.LogWarningFormat ("Missing Blit Material. {0} blit pass will not execute. Check for missing reference in the assigned renderer.", GetType ().Name);
				return;
			}

			if (settings.Event == RenderPassEvent.AfterRenderingPostProcessing) {

			} else if (settings.Event == RenderPassEvent.AfterRendering && renderingData.postProcessingEnabled) {
				// If event is AfterRendering, and src/dst is using CameraColor, switch to _AfterPostProcessTexture instead.
				if (settings.srcType == Target.CameraColor) {
					settings.srcType = Target.TextureID;
					settings.srcTextureId = "_AfterPostProcessTexture";
					UpdateSrcIdentifier ();
				}
				if (settings.dstType == Target.CameraColor) {
					settings.dstType = Target.TextureID;
					settings.dstTextureId = "_AfterPostProcessTexture";
					UpdateDstIdentifier ();
				}
			} else {
				// If src/dst is using _AfterPostProcessTexture, switch back to CameraColor
				if (settings.srcType == Target.TextureID && settings.srcTextureId == "_AfterPostProcessTexture") {
					settings.srcType = Target.CameraColor;
					settings.srcTextureId = "";
					UpdateSrcIdentifier ();
				}
				if (settings.dstType == Target.TextureID && settings.dstTextureId == "_AfterPostProcessTexture") {
					settings.dstType = Target.CameraColor;
					settings.dstTextureId = "";
					UpdateDstIdentifier ();
				}
			}
		}

		public override void SetupRenderPasses (ScriptableRenderer renderer, in RenderingData renderingData) {

			if (settings.cameraName != renderingData.cameraData.camera.name) {
				return;
			}

			if (settings.blitMaterial == null) {
				Debug.LogWarningFormat ("Missing Blit Material. {0} blit pass will not execute. Check for missing reference in the assigned renderer.", GetType ().Name);
				return;
			}

			// RenderTextureObject の場合は毎回 dst を更新する (動的変更に対応)
			if (settings.dstType == Target.RenderTextureObject) {
				UpdateDstIdentifier ();
			}

			if (settings.Event == RenderPassEvent.AfterRenderingPostProcessing) { } else if (settings.Event == RenderPassEvent.AfterRendering && renderingData.postProcessingEnabled) {
				// If event is AfterRendering, and src/dst is using CameraColor, switch to _AfterPostProcessTexture instead.
				if (settings.srcType == Target.CameraColor) {
					settings.srcType = Target.TextureID;
					settings.srcTextureId = "_AfterPostProcessTexture";
					UpdateSrcIdentifier ();
				}
				if (settings.dstType == Target.CameraColor) {
					settings.dstType = Target.TextureID;
					settings.dstTextureId = "_AfterPostProcessTexture";
					UpdateDstIdentifier ();
				}
			} else {
				// If src/dst is using _AfterPostProcessTexture, switch back to CameraColor
				if (settings.srcType == Target.TextureID && settings.srcTextureId == "_AfterPostProcessTexture") {
					settings.srcType = Target.CameraColor;
					settings.srcTextureId = "";
					UpdateSrcIdentifier ();
				}
				if (settings.dstType == Target.TextureID && settings.dstTextureId == "_AfterPostProcessTexture") {
					settings.dstType = Target.CameraColor;
					settings.dstTextureId = "";
					UpdateDstIdentifier ();
				}
			}

			var src = (settings.srcType == Target.CameraColor) ? renderer.cameraColorTargetHandle : srcIdentifier;
			var dest = (settings.dstType == Target.CameraColor) ? renderer.cameraColorTargetHandle : dstIdentifier;

			geoAOPass.Setup (src, dest);
			renderer.EnqueuePass (geoAOPass);
		}
	}

} // namespace ViewpointBasedAO
