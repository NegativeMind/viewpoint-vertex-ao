# Unity GeoAO

[![Unity Version](https://img.shields.io/badge/Unity-2021.3%2B-blue.svg)](https://unity3d.com/get-unity/download)
[![URP](https://img.shields.io/badge/URP-12.1.0%2B-green.svg)](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@latest)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

A Unity package for calculating geometric ambient occlusion (AO) from mesh geometry in real-time. This package estimates vertex-level ambient occlusion by sampling the mesh from multiple camera viewpoints, providing an approximation of occlusion without expensive ray tracing.

## Features

- ✨ **Real-time AO calculation** from mesh geometry
- 🎯 **Multiple quality levels** for performance/quality balance
- 🔧 **URP integration** with custom renderer features
- 🎨 **Flexible display options** (texture-based or vertex colors)
- 📊 **Performance monitoring** and optimization tools
- 🎮 **Easy setup** with custom editor interface

## Quick Start

### Installation

#### Via Package Manager
1. Open Package Manager (Window → Package Manager)
2. Click "+" and select "Add package from git URL"
3. Enter: `https://github.com/NegativeMind/Unity-GeoAO.git`

#### Manual Installation
1. Download/clone this repository
2. Copy to your project's `Packages` folder as `com.negativemind.unity-geoao`

### Basic Setup

1. **Add Component**: Create a GameObject and add `GeoAOBehaviour`
2. **Configure Settings**: 
   - Assign your URP Forward Renderer Data
   - Set Mesh Parent to the transform containing your meshes
3. **Add Renderer Feature**: Add `GeoAORendererFeature` named "AOBlit" to your renderer
4. **Play**: AO calculation starts automatically

## Technical Overview

This package uses a multi-viewpoint sampling approach:

1. **Bounds Calculation**: Analyzes target mesh bounds
2. **Sample Generation**: Creates sampling points using golden angle distribution  
3. **Multi-view Rendering**: Renders depth from multiple camera positions
4. **AO Accumulation**: Combines depth information to calculate occlusion
5. **Result Application**: Applies AO as textures or vertex colors

## Requirements

- Unity 2021.3 or later
- Universal Render Pipeline (URP) 12.1.0+
- URP Forward Renderer Data asset

## Performance

- **Sampling Levels**: Low (32), Medium (64), High (128), Ultra (256) samples
- **Real-time**: Suitable for dynamic scenes with proper optimization
- **Pre-calculation**: Recommended for static scenes in production

## Contributing

Contributions are welcome! Please feel free to submit pull requests, report issues, or suggest improvements.

## License

This project is licensed under the MIT License - see the [LICENSE.md](LICENSE.md) file for details.

## Credits

Developed by [NegativeMind](https://github.com/NegativeMind)
