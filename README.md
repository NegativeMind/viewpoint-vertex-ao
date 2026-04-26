# Viewpoint-Based Per-Vertex Ambient Occlusion for Unity URP

[![Unity](https://img.shields.io/badge/Unity-2022.3%2B-black?logo=unity&logoColor=white)](https://unity.com/)
[![URP](https://img.shields.io/badge/URP-12.1.0%2B-blue?logo=unity&logoColor=white)](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@latest)
[![XR](https://img.shields.io/badge/XR-Single%20Pass%20Instanced-blueviolet)](https://docs.unity3d.com/Manual/SinglePassInstancing.html)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE.md)

![PBR with AO](Documentation~/pbr_with_ao.png)

A Unity URP package that computes per-vertex Ambient Occlusion by rendering the mesh from multiple viewpoints and accumulating depth-based visibility. AO is computed once at runtime on initialization and baked into the material — there is no per-frame overhead after that. The result matches the visual appearance of URP/Lit with an Occlusion Map. Because AO is stored in auto-generated UV2 coordinates and vertex colors, the mesh does not need pre-existing UV unwrapping.

## Requirements

- Unity 2022.3.0f1 or later
- Universal Render Pipeline (URP) 12.1.0

## Installation

### Package Manager UI (recommended)

1. Open **Window > Package Manager**.
2. Click the **+** button in the top-left corner and choose **Add package from git URL…**
3. Enter the following URL and click **Add**:

```
https://github.com/NegativeMind/viewpoint-vertex-ao.git
```


## Usage

1. **Attach** `AOBehaviour` to any GameObject that has `MeshFilter` + `MeshRenderer` components (child objects are included automatically).

2. **Configure** the component in the Inspector:

   | Property | Description |
   |---|---|
   | `Spread Angle` | Cone half-angle around each vertex normal. `1.0` = full hemisphere, `0.0` = equatorial ring. Controls how much of the sphere contributes to each vertex's AO. |
   | `Sampling Level` | Number of viewpoints. Higher = smoother AO, longer bake time. |
   | `Capture Resolution` | Resolution of the per-viewpoint depth capture texture (256–4096). Higher = finer geometry detail resolved, more GPU memory. Default is `High` (1024). |
   | `AO Scale` | Blend factor between no-occlusion (`0`) and full occlusion (`1`). |
   | `Show Debug` | Replaces the material with a grayscale preview of the raw AO values, and draws the viewpoint positions as gizmos in the Scene view. |

3. **Play** the scene. AO is computed once during `Start()` and applied immediately.

| Without AO | AO debug view (`Show Debug`) |
|---|---|
| ![PBR only](Documentation~/pbr_only.png) | ![AO only](Documentation~/ao_only.png) |

### Notes

- The mesh's material is replaced at runtime. The original material's properties (base texture, color, metallic, smoothness) are copied to the new material via `CopyPropertiesFromMaterial`.
- AO computation renders meshes explicitly with `CommandBuffer.DrawMesh`, so no dedicated layer setup is required.
- AO is not recalculated at runtime after `Start()`. Re-enter Play mode to recompute.
- The `_VERTEX_COLOR_AO` shader keyword enables reading AO from vertex colors instead of UV2, for use with custom shaders that need AO in the `COLOR` semantic.
- **VR / XR**: Single Pass Instanced stereo rendering is supported. All shaders include `#pragma multi_compile_instancing` and the required URP stereo macros. Depth capture bypasses the URP pipeline via `CommandBuffer.DrawMesh` to avoid XR interference with the internal depth texture.


## References

### Paper

**Viewpoint-Based Ambient Occlusion**
Rudomin, I., Hernández, B., & Barrera, R. (2006).
*IEEE Computer Graphics and Applications*, 26(6), 60–69.
https://www.researchgate.net/publication/5501367_Viewpoint-Based_Ambient_Occlusion

The technique samples the hemisphere above each vertex's surface normal by rendering the scene from N viewpoints distributed on a sphere. Each viewpoint votes on whether a vertex is visible, and the mean visibility is taken as the AO value.

### Original Unity Implementation

This package is based on **Unity-GeoAO** by Xavier Martinez (nezix) — MIT License:
https://github.com/nezix/Unity-GeoAO

Adapted for URP, refactored to use a running-average accumulation shader, and extended with per-vertex normal cone filtering.

See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for full license texts.

## Algorithm

N viewpoints are distributed on a sphere around the mesh using a Fibonacci spiral. The scene is rendered from each viewpoint with a depth buffer. For each vertex, the shader checks whether the viewpoint falls within the cone around the vertex normal (`Spread Angle`), and if so, tests visibility against the depth buffer. Visibility results are accumulated as a running average. The final value per vertex is a mean visibility in [0, 1] (1 = no occlusion, 0 = fully occluded), which is baked into a texture and applied to `surfaceData.occlusion` — darkening only indirect (ambient/GI) lighting, identical to URP/Lit with an Occlusion Map.
