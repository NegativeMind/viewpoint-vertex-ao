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
| `AORendererFeature` | URP Renderer Feature. Handles per-camera blit into the AO render texture. |
| `AORendererPass` | ScriptableRenderPass implementation. Uses `RTHandle` API (URP 14). |
| `AOSettings` | Settings data for the Renderer Feature. |
| `SamplingLevel` | Enum defining sampling quality levels (number of viewpoints). |

## Shaders

| File | Shader name | Purpose |
|---|---|---|
| `ComputeVertexAO.shader` | `ViewpointAO/ComputeVertexAO` | AO accumulation per viewpoint (camera blit). Runs a Welford online average: R = mean visibility [0,1], G = in-cone sample count. Applies per-vertex normal cone filter via `_SpreadAngle`. Internal use only — not for end-user materials. |
| `RenderWithVertexAO.shader` | `ViewpointAO/RenderWithVertexAO` | Full URP PBR display with vertex AO applied. AO is fed into `surfaceData.occlusion` (affects indirect/ambient light only, matching URP/Lit Occlusion Map). Supports two modes via `_VERTEX_COLOR_AO` keyword: off = sample `_AOTex` via UV2, on = read AO from vertex color R channel. |
| `PreviewVertexAO.shader` | `ViewpointAO/PreviewVertexAO` | Lightweight non-PBR debug display. Samples `_AOTex` via UV2 and outputs the raw AO value as grayscale (white = no occlusion, black = fully occluded). |

## AO Computation Flow

```
Awake()
  ├─ Find UniversalRendererData via reflection (m_RendererDataList)
  ├─ Reserve a free layer for the AO camera
  └─ Dynamically add AORendererFeature to the renderer

Start()
  ├─ InitializeObjectAndGetBounds()  — collect MeshFilters, compute bounds
  ├─ GenerateSamplePositions()       — distribute viewpoints on a Fibonacci sphere (full sphere,
  │                                    spreadAngle cone filter is applied per-vertex in the shader)
  ├─ CreateAoCamera()                — create dedicated AO camera + RenderTextures
  ├─ VertexDataToTexture()           — pack vertex world positions AND world normals into Texture2Ds
  ├─ ComputeAmbientOcclusion()       — for each viewpoint:
  │    ├─ set _uNormal, _SpreadAngle (once), _CameraWorldPos, _VP (per viewpoint)
  │    ├─ aoCamera.Render() → blit via ComputeVertexAO.shader
  │    │    (cone filter + depth visibility test + Welford running average)
  │    └─ CopyTexture(aoRenderTexture → aoRenderTextureForShader) for next iteration
  ├─ ReadAOResult()                  — read R channel (normalized [0,1]) → Texture2D (RGBAHalf)
  ├─ BakeAO(aoTex)                   — simultaneously:
  │    ├─ set mesh.uv2 (UV2 pointing into the AO texture)
  │    ├─ set mesh.colors (AO value in all channels)
  │    └─ apply RenderWithVertexAO material (_AOTex = aoTex, read via UV2)
  └─ DisposeResources()              — remove RendererFeature, destroy AO camera
```

## Known Constraints

- `AOBehaviour.Awake()` uses reflection to retrieve `UniversalRendererData`. If URP is not assigned in Graphics Settings the component disables itself.
- AO is computed once in `Start()` — no real-time updates.
- `GenerateSamplePositions()` always distributes viewpoints over the full sphere. The `spreadAngle` property controls the per-vertex normal cone filter inside the shader, not the viewpoint distribution range.
- `BakeAO()` always writes to both UV2 texture and vertex colors simultaneously. Display uses `RenderWithVertexAO` (UV2 path). Vertex colors are available for custom shaders needing the AO value in the `COLOR` semantic.
- The AO texture format is `RGBAHalf` with values in `[0, 1]`. The running average is computed in the shader; no CPU-side normalization is needed.
- `BakeAO()` calls `mat.CopyPropertiesFromMaterial(renderer.sharedMaterial)` then immediately re-assigns `mat.shader = targetShader` because `CopyPropertiesFromMaterial` also copies the source material's shader.
- `AORendererPass` uses the URP 14 `RTHandle` API. `FrameCleanup` has been replaced with `OnCameraCleanup`. Temporary RTs use `cmd.GetTemporaryRT`/`ReleaseTemporaryRT` (int-based, not deprecated). `cmd.Blit` (CommandBuffer) is used instead of `Blitter.BlitCameraTexture` because `ComputeVertexAO.shader` requires its own vertex shader to be called.
- `UnityProject~/` is tracked by git. `Library/`, `Temp/`, and `Logs/` should be in `.gitignore`.
