namespace ViewpointBasedAO {
    /// <summary>
    /// Ambient Occlusionのサンプリングレベルを定義するenum
    /// </summary>
    public enum AOSamplingLevel {
        VeryLow = 16,
        Low = 36,
        Medium = 64,
        High = 144,
        VeryHigh = 256,
        TooMuch = 1024,
        WayTooMuch = 2048
    }
}
