namespace ViewpointBasedAO {
    /// <summary>
    /// Number of viewpoints used for AO sampling. Higher values produce better quality at the cost of longer bake time.
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
