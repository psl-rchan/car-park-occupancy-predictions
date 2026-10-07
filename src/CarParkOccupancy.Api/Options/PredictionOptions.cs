namespace CarParkOccupancy.Api.Options;

public sealed class PredictionOptions
{
    public const string SectionName = "Prediction";

    public string DefaultPredictor { get; set; } = "HistoricalBaseline";

    public string Timezone { get; set; } = "Asia/Hong_Kong";

    /// <summary>Hours of recent snapshots used to measure the trend. Excluded from the historical baseline.</summary>
    public int RecentTrendHours { get; set; } = 6;

    /// <summary>
    /// Multiplier applied to the recent trend at horizon 1, then raised to (horizon - 1).
    /// 1 keeps the full trend at every horizon. 0 drops the trend after the first hour.
    /// </summary>
    public double TrendDecay { get; set; } = 0.7;

    public VertexAiOptions Vertex { get; set; } = new();
}

public sealed class VertexAiOptions
{
    public string ProjectId { get; set; } = "";

    public string Location { get; set; } = "";

    public string Model { get; set; } = "";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ProjectId)
        && !string.IsNullOrWhiteSpace(Location)
        && !string.IsNullOrWhiteSpace(Model);
}
