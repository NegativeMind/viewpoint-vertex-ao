# CLAUDE.md

## Project Overview

Unity URP package for Viewpoint-Based Ambient Occlusion.
Renders the mesh from multiple viewpoints, computes per-vertex occlusion, and applies AO to materials.

## Repository Structure

```
viewpoint-based-AO/          ← repo root = package root
├── package.json             ← UPM package definition
├── Runtime/                 ← package source (C# + Shaders)
├── Editor/                  ← editor-only assembly
├── LICENSE
├── README.md
└── UnityProject~/           ← development Unity project
    ├── Assets/
    ├── Packages/
    │   └── manifest.json    ← references package via "file:../../"
    └── ProjectSettings/
```

### UnityProject~/ Notes

- The `~` suffix is a UPM convention that excludes the folder from package import scanning.
- Without it, UPM scanning recurses into the Unity project which references the same package, causing an infinite import loop.
- Open this folder in Unity Hub to work on the development project.

## Package Info

- **Package ID**: `com.negativemind.viewpoint-vertex-ao`
- **Assemblies**: `NegativeMind.ViewpointVertexAO.Runtime` / `NegativeMind.ViewpointVertexAO.Editor`
- **Namespace**: `NegativeMind.ViewpointVertexAO`
- **Unity**: 2022.3+
- **Dependencies**: URP 12.1.0+, ShaderGraph 12.1.0+, Mathematics 1.2.6+

## Key Classes

| Class | Role |
|---|---|
| `AOBehaviour` | MonoBehaviour entry point. Attach to a GameObject to compute and apply AO. |
| `SamplingLevel` | Enum defining sampling quality levels (number of viewpoints). |
| `CaptureResolution` | Enum defining the depth capture texture resolution per viewpoint (256–4096). |

## Shaders

| File | Shader name | Purpose |
|---|---|---|
| `AODepthCapture.shader` | `Hidden/ViewpointAO/AODepthCapture` | Depth-only capture shader. Invoked via `CommandBuffer.DrawMesh` once per viewpoint. Outputs normalized depth [0,1] (0 = near, 1 = far) on all platforms by compensating for `UNITY_REVERSED_Z`. Internal use only. |
| `ComputeVertexAO.shader` | `ViewpointAO/ComputeVertexAO` | AO accumulation per viewpoint (`Graphics.Blit`). Runs a Welford online average: R = mean visibility [0,1], G = in-cone sample count. Applies per-vertex normal cone filter via `_SpreadAngle`. Depth test compares `d_vertex` (from `_VP` matrix) against `_ExplicitDepth` (from `AODepthCapture`). Internal use only — not for end-user materials. |
| `RenderWithVertexAO.shader` | `ViewpointAO/RenderWithVertexAO` | Full URP PBR display with vertex AO applied. AO is fed into `surfaceData.occlusion` (affects indirect/ambient light only, matching URP/Lit Occlusion Map). Supports two modes via `_VERTEX_COLOR_AO` keyword: off = sample `_AOTex` via UV2, on = read AO from vertex color R channel. Includes full XR stereo instancing macros. |
| `PreviewVertexAO.shader` | `ViewpointAO/PreviewVertexAO` | Lightweight non-PBR debug display. Samples `_AOTex` via UV2 and outputs the raw AO value as grayscale (white = no occlusion, black = fully occluded). Includes full XR stereo instancing macros. |

## AO Computation Flow

```
Awake()
  ├─ Create ambientOcclusionMat  (ComputeVertexAO shader)
  ├─ Find AODepthCapture shader; disable component if not found
  └─ Create depthCaptureMat      (AODepthCapture shader)

Start()
  ├─ InitializeObjectAndGetBounds()  — collect MeshFilters (with MeshRenderer), compute bounds,
  │                                    validate: mesh exists, exactly 1 material, no lightmap (UV2 conflict)
  │                                    save + override shadowCastingMode → TwoSided
  ├─ GenerateSamplePositions()       — distribute viewpoints on a Fibonacci sphere
  │                                    (spreadAngle cone filter is applied per-vertex in the shader)
  ├─ CreateAoCamera()                — create disabled camera (used only for transform + matrices)
  │                                    allocate: depthCaptureRT (captureResolution² RFloat, depth=24),
  │                                              aoRenderTexture / aoRenderTextureForShader (ARGBHalf),
  │                                              vertexPositionsTexture / vertexNormalsTexture (RGBAFloat)
  ├─ VertexDataToTexture()           — pack vertex world positions and world normals into Texture2Ds
  ├─ ComputeAmbientOcclusion()       — for each viewpoint:
  │    ├─ position aoCamera, read worldToCameraMatrix (V) and projectionMatrix (P)
  │    ├─ adjust P for D3D/Metal: Y-flip rows + Z-remap [-1,1]→[0,1] → _VP = P_adj * V
  │    ├─ CommandBuffer.DrawMesh (all meshes) → depthCaptureRT via AODepthCapture shader
  │    │    (outputs normalized depth [0,1]: 0=near, 1=far; handles UNITY_REVERSED_Z)
  │    ├─ Graphics.Blit → aoRenderTexture via ComputeVertexAO shader
  │    │    (cone filter + depth visibility test vs _ExplicitDepth + Welford running average)
  │    └─ Graphics.CopyTexture(aoRenderTexture → aoRenderTextureForShader) for next iteration
  │    after loop: restore original shadowCastingMode on all renderers
  ├─ ReadAOResult()                  — GPU readback: R channel → copy to RGBA → Texture2D (RGBAHalf)
  ├─ BakeAO(aoTex)                   — per mesh:
  │    ├─ set mesh.uv2  (UV2 pointing into aoTex)
  │    ├─ set mesh.colors  (AO value in all channels — for custom shaders using COLOR semantic)
  │    └─ create + apply RenderWithVertexAO (or PreviewVertexAO if showDebug) material
  ├─ DisposeResources()              — release depthCaptureRT, destroy depthCaptureMat + aoCamera GO
  └─ Log total compute time

Update()
  └─ Set _AOScale on all aoMaterials[] each frame (enables runtime intensity tuning)

OnDrawGizmos()  [Editor only, when showDebug == true]
  ├─ Draw red spheres at each viewpoint position
  └─ Draw red lines from bounds center to each viewpoint

OnDestroy()
  └─ DisposeResources() fallback (in case baking was interrupted)
```

## Known Constraints

- AO is computed once in `Start()` — no real-time updates. Re-enter Play mode to recompute.
- `GenerateSamplePositions()` always distributes viewpoints over the full sphere. The `spreadAngle` property controls the per-vertex normal cone filter inside the shader, not the viewpoint distribution range.
- Depth capture uses `CommandBuffer.DrawMesh` + `Graphics.ExecuteCommandBuffer`, bypassing the URP pipeline entirely. `Camera.RenderWithShader` was deliberately avoided — it is not reliably supported in URP and may leave the render target untouched.
- `AODepthCapture.shader` uses `Cull Off` (not `Cull Back`). Sharp convex tips have face normals that are nearly perpendicular to the spike direction; with `Cull Back`, in-cone viewpoints at steep angles would back-cull the tip triangles, leaving `depthCaptureRT` as 1.0 (far) at the tip → false "occluded" result.
- `depthCaptureRT` uses `FilterMode.Point`. Bilinear filtering would blend the tip pixel with surrounding empty pixels (depth=1.0), pulling `d_scene` away from the actual tip depth and causing the same false occlusion on sharp features.
- `aoCamera` is created with `enabled = false`. It exists only to provide `worldToCameraMatrix` and `projectionMatrix` for each viewpoint. It never renders through URP.
- The `_VP` matrix passed to `ComputeVertexAO.shader` is `P_adjusted * V`, where `P_adjusted` remaps Unity's OpenGL-convention projection (z ∈ [-1,1]) to [0,1] on D3D/Metal via Y-flip + z*0.5+w*0.5. On non-D3D/Metal the shader does the remap in HLSL (`#if !defined(UNITY_REVERSED_Z)`).
- `AODepthCapture.shader` outputs [0,1] (0=near, 1=far) on all platforms by checking `UNITY_REVERSED_Z`. This matches the convention of `d_vertex` in `ComputeVertexAO.shader`.
- `BakeAO()` always writes to both UV2 and vertex colors simultaneously. The active display uses `RenderWithVertexAO` (UV2 path). Vertex colors are available for custom shaders using the `COLOR` semantic via the `_VERTEX_COLOR_AO` keyword.
- `RenderWithVertexAO.shader` includes the full URP lighting keyword set: `_ADDITIONAL_LIGHTS_VERTEX / _ADDITIONAL_LIGHTS` (point/spot lights), `_ADDITIONAL_LIGHT_SHADOWS`, `_REFLECTION_PROBE_BLENDING / _REFLECTION_PROBE_BOX_PROJECTION`, `LIGHTMAP_ON`, `DIRLIGHTMAP_COMBINED`, `LIGHTMAP_SHADOW_MIXING`, `SHADOWS_SHADOWMASK`. Without these pragmas only the main directional light would be processed.
- The AO texture format is `RGBAHalf` with values in [0,1]. The running average is computed in the shader (Welford method); no CPU-side normalization is needed.
- `BakeAO()` calls `mat.CopyPropertiesFromMaterial(renderer.sharedMaterial)` then immediately re-assigns `mat.shader = targetShader` because `CopyPropertiesFromMaterial` also copies the source material's shader.
- All three display/preview shaders include `#pragma multi_compile_instancing` and full URP stereo macros (`UNITY_VERTEX_INPUT_INSTANCE_ID`, `UNITY_VERTEX_OUTPUT_STEREO`, etc.) for XR / Single Pass Instanced VR support.
- `UnityProject~/` is tracked by git. `Library/`, `Temp/`, and `Logs/` should be in `.gitignore`.
