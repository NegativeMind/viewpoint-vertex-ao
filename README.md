# Viewpoint-Based Ambient Occlusion for Unity URP

A Unity URP package that computes per-vertex Ambient Occlusion by rendering the mesh from multiple viewpoints and accumulating depth-based visibility. AO is baked once at runtime and applied as a URP PBR material with an occlusion map, matching the visual result of URP/Lit with an Occlusion Map texture.

## Requirements

- Unity 2022.3 or later
- Universal Render Pipeline (URP) 14.x

## Installation

### Package Manager UI (recommended)

1. Open **Window > Package Manager**.
2. Click the **+** button in the top-left corner and choose **Add package from git URL…**
3. Enter the following URL and click **Add**:

```
https://github.com/NegativeMind/viewpoint-based-AO.git
```

To install a specific version, append the tag:

```
https://github.com/NegativeMind/viewpoint-based-AO.git#v1.0.0
```


## Usage

1. **Attach** `ViewpointAOBehaviour` to any GameObject that has `MeshFilter` + `MeshRenderer` components (child objects are included automatically).

2. **Configure** the component in the Inspector:

   | Property | Description |
   |---|---|
   | `Spread Angle` | Cone half-angle around each vertex normal. `1.0` = full hemisphere, `0.0` = equatorial ring. Controls how much of the sphere contributes to each vertex's AO. |
   | `Sampling Level` | Number of viewpoints. Higher = better quality, longer bake time. |
   | `AO Scale` | Blend factor between no-occlusion (`0`) and full occlusion (`1`). |
   | `Show Debug` | Replaces the material with a grayscale preview of the raw AO values. |

3. **Play** the scene. AO is computed once during `Start()` and applied immediately.

### Notes

- The mesh's material is replaced at runtime. The original material's properties (base texture, color, metallic, smoothness) are copied to the new material via `CopyPropertiesFromMaterial`.
- A layer slot (8–31, or a layer named `"AOLayer"`) is reserved temporarily during computation and restored afterwards.
- AO is not recalculated at runtime after `Start()`. Re-enter Play mode to recompute.
- The `_VERTEX_COLOR_AO` shader keyword enables reading AO from vertex colors instead of UV2, for use with custom shaders that need AO in the `COLOR` semantic.


## References

### Paper

**Viewpoint-Based Ambient Occlusion**
Rudomin, I., Hernández, B., & Barrera, R. (2006).
*IEEE Computer Graphics and Applications*, 26(6), 60–69.
https://www.researchgate.net/publication/5501367_Viewpoint-Based_Ambient_Occlusion

The technique samples the hemisphere above each vertex's surface normal by rendering the scene from N viewpoints distributed on a sphere. Each viewpoint votes on whether a vertex is visible, and the mean visibility is taken as the AO value.

### Original Unity Implementation

This package is based on **Unity-GeoAO** by Xavier Martinez (nezix):
https://github.com/nezix/Unity-GeoAO

Ported to URP 14, refactored to use a running-average accumulation shader, and extended with per-vertex normal cone filtering.

### URP Blit Renderer Feature

The `ViewpointAORendererFeature` and `ViewpointAORendererPass` are derived from **URP_BlitRenderFeature** by Cyanilux:
https://github.com/Cyanilux/URP_BlitRenderFeature

## Algorithm

```
1. Viewpoint Distribution (Fibonacci Sphere)
   - N viewpoints are distributed evenly on a sphere around the mesh
     using the golden-angle Fibonacci spiral:
       theta = goldenAngle * i
       z     = lerp(+zRange, -zRange, i / N)
       pos   = center + (cos(theta), sin(theta), z) * sqrt(1 - z²) * radius

2. Per-Viewpoint Rendering
   - The AO camera renders the scene with a depth buffer from each viewpoint.
   - A fullscreen blit runs ComputeVertexAO.shader, which for each texel
     (corresponding to one vertex) does:

     a. Cone filter: only count the viewpoint if it lies within the
        cone around the vertex normal:
          cosThreshold = cos(spreadAngle * PI/2)
          inCone       = dot(normalize(cameraPos - vertexPos), normal) >= cosThreshold

     b. Depth visibility test: project the vertex into the current
        camera's clip space and compare its Z to the depth buffer:
          visible = |vertex.z - depthBuffer.z| <= epsilon ? 1.0 : 0.0

     c. Welford running average (avoids storing all N results):
          new_count = old_count + 1
          new_ao    = old_ao + (visible - old_ao) / new_count

3. Result Readback
   - After all viewpoints, the R channel holds the mean visibility in [0, 1].
   - 1 = fully visible (no occlusion), 0 = fully occluded.

4. Baking
   - UV2 is generated so each vertex maps to its texel in the AO texture.
   - Vertex colors are written with the AO value in all channels (RGBA).
   - The mesh's material is replaced with RenderWithVertexAO (PBR) or
     PreviewVertexAO (grayscale debug), both reading from UV2.
   - AO is fed into surfaceData.occlusion, which darkens only indirect
     (ambient/GI) lighting — identical to URP/Lit with an Occlusion Map.
```
