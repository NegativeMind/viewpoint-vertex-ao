namespace NegativeMind.ViewpointVertexAO {
    /// <summary>
    /// Resolution of the per-viewpoint depth capture texture.
    /// Higher values resolve finer geometry details and improve AO accuracy at the cost of more GPU memory.
    /// The texture is square; memory cost scales as resolution².
    /// </summary>
    public enum CaptureResolution {
        Low = 256, // ~0.26 MB  — fast, coarse detail
        Medium = 512, // ~1.05 MB  — balanced
        High = 1024, // ~4.19 MB  — fine detail, recommended for complex meshes
        VeryHigh = 2048, // ~16.8 MB  — very fine detail
        TooMuch = 4096, // ~67.1 MB  — maximum quality
    }
}