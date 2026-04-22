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

- **Package ID**: `com.negativemind.viewpoint-based-ao`
- **Assemblies**: `ViewpointBasedAO.Runtime` / `ViewpointBasedAO.Editor`
- **Namespace**: `ViewpointBasedAO`
- **Unity**: 2022.3+
- **Dependencies**: URP 12.1.0+, ShaderGraph 12.1.0+, Mathematics 1.2.6+

## Key Classes

| Class | Role |
|---|---|
| `ViewpointAOBehaviour` | MonoBehaviour entry point. Attach to a GameObject to compute and apply AO. |
| `ViewpointAORendererFeature` | URP Renderer Feature. Handles per-camera blit. |
| `ViewpointAORendererPass` | ScriptableRenderPass implementation. |
| `ViewpointAOSettings` | Settings data for the Renderer Feature. |
| `AOSamplingLevel` | Enum defining sampling quality levels. |

## Shaders

| File | Shader name | Purpose |
|---|---|---|
| `ComputeVertexAO.shader` | `ViewpointAO/ComputeVertexAO` | AO accumulation per viewpoint (camera blit). Internal use only — not for end-user materials. |
| `RenderWithVertexAO.shader` | `ViewpointAO/RenderWithVertexAO` | Full URP PBR display with vertex AO applied. Supports both modes via `_VERTEX_COLOR_AO` keyword: off = sample `_AOTex` via UV2, on = read AO from vertex color R channel. |
| `PreviewVertexAO.shader` | `ViewpointAO/PreviewVertexAO` | Lightweight non-PBR display. Samples `_AOTex` via UV2 and shows vertex AO value directly without full lighting calculation. |

## AO Computation Flow

```
Awake()
  ├─ Find UniversalRendererData via reflection (m_RendererDataList)
  ├─ Reserve a free layer for the AO camera
  └─ Dynamically add ViewpointAORendererFeature to the renderer

Start()
  ├─ InitializeObjectAndGetBounds()  — collect MeshFilters, compute bounds
  ├─ GenerateSamplePositions()       — distribute viewpoints on a Fibonacci sphere
  ├─ CreateAoCamera()                — create dedicated AO camera + RenderTextures
  ├─ VertexPositionsToTexture()      — pack vertex world positions into a Texture2D
  ├─ ComputeAmbientOcclusion()       — render from each viewpoint, accumulate AO
  ├─ ReadAOResult()                  — read accumulated RenderTexture → Texture2D (RGBAHalf)
  ├─ BakeAO(aoTex)                   — simultaneously:
  │    ├─ set mesh.uv2 (UV2 pointing into the linear AO texture)
  │    ├─ set mesh.colors (AO value in all channels)
  │    └─ apply VertAOLit material (_AOTex = aoTex, read via UV2)
  └─ DisposeResources()              — remove RendererFeature, destroy AO camera
```

## Known Constraints

- `ViewpointAOBehaviour.Awake()` uses reflection to retrieve `UniversalRendererData`. If URP is not assigned in Graphics Settings the component disables itself.
- AO is computed once in `Start()` — no real-time updates.
- `BakeAO()` always writes to both UV2 texture and vertex colors simultaneously. Display uses `VertAOLit` shader (UV2 path). Vertex colors are available for custom shaders needing the AO value in `COLOR` semantic.
- The AO texture format is `RGBAHalf` (values in `[0, 2.0]`). `VertAOLit` reads this range directly — no normalization needed.
- `UnityProject~/` is tracked by git. `Library/`, `Temp/`, and `Logs/` should be in `.gitignore`.
