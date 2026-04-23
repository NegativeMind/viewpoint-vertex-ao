using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ViewpointBasedAO {

	/// <summary>
	/// URP用のViewpointAO Renderer Feature
	/// </summary>
	public class ViewpointAORendererFeature : ScriptableRendererFeature {

		public ViewpointAOSettings settings = new ViewpointAOSettings ();
		public ViewpointAORendererPass geoAOPass;
		RTHandle srcIdentifier, dstIdentifier;

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
			srcIdentifier?.Release ();
			srcIdentifier = settings.srcType == Target.CameraColor ? null :
				AllocIdentifier (settings.srcType, settings.srcTextureId, settings.srcTextureObject);
		}

		void UpdateDstIdentifier () {
			dstIdentifier?.Release ();
			dstIdentifier = settings.dstType == Target.CameraColor ? null :
				AllocIdentifier (settings.dstType, settings.dstTextureId, settings.dstTextureObject);
		}

		RTHandle AllocIdentifier (Target type, string s, RenderTexture obj) {
			if (type == Target.RenderTextureObject)
				return RTHandles.Alloc (new RenderTargetIdentifier (obj));
			if (type == Target.TextureID)
				return RTHandles.Alloc (new RenderTargetIdentifier (s));
			return null;
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

			var src = settings.srcType == Target.CameraColor ? renderer.cameraColorTargetHandle : srcIdentifier;
			var dest = settings.dstType == Target.CameraColor ? renderer.cameraColorTargetHandle : dstIdentifier;

			geoAOPass.Setup (src, dest);
			renderer.EnqueuePass (geoAOPass);
		}

		protected override void Dispose (bool disposing) {
			srcIdentifier?.Release ();
			dstIdentifier?.Release ();
		}
	}

} // namespace ViewpointBasedAO