using System.Text.Json.Serialization;

namespace CarParkOccupancy.Api.Models;

public static class BaselineSources
{
    public const string SameWeekdayHour = "SameWeekdayHour";

    public const string SameHour = "SameHour";

    public const string Overall = "Overall";

    public const string RecentOnly = "RecentOnly";

    public const string None = "None";
}

public sealed class OccupancyPredictionRequest
{
    public required string CarParkCode { get; init; }

    public string? CountingCategory { get; init; }

    public required DateTimeOffset AsOf { get; init; }

    public required IReadOnlyList<int> HorizonsHours { get; init; }

    public IReadOnlyList<OccupancyObservation> History { get; init; } = [];
}

/// <summary>
/// Prediction payload returned by GET /api/carparks/{code}/predictions.
/// </summary>
public sealed record CarParkPredictionResult
{
    public required string CarParkCode { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CountingCategory { get; init; }

    /// <summary>Reference instant the horizons are measured from.</summary>
    public required DateTimeOffset GeneratedAt { get; init; }

    public required string Timezone { get; init; }

    /// <summary>Wire scale for every occupancy percent field in this payload. Always "0-100".</summary>
    public string OccupancyPercentScale { get; init; } = "0-100";

    /// <summary>Algorithm that produced the numeric predictions.</summary>
    public required string Method { get; init; }

    /// <summary>Predictor selected by configuration or the request.</summary>
    public required string Predictor { get; init; }

    public bool LiveModelInvoked { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FallbackReason { get; init; }

    public int RecentSampleCount { get; init; }

    public int RecentTrendHours { get; init; }

    public DateTimeOffset? LatestSnapshotTime { get; init; }

    public int? LatestCapacity { get; init; }

    public int? LatestOccupied { get; init; }

    /// <summary>Latest observed occupancy on a 0–100 percent scale, or null when unknown.</summary>
    public double? LatestOccupancyPercent { get; init; }

    public IReadOnlyList<HourlyPrediction> Predictions { get; init; } = [];
}

public sealed record HourlyPrediction
{
    public int HorizonHours { get; init; }

    public DateTimeOffset TargetTime { get; init; }

    /// <summary>
    /// Predicted occupancy as a percent of capacity on a 0–100 scale
    /// (0 empty, 100 full). This is not a 0–1 fraction. Null when no usable history exists.
    /// </summary>
    public double? PredictedOccupancyPercent { get; init; }

    /// <summary>Same-slot historical average on a 0–100 scale, before the recent trend is applied.</summary>
    public double? BaselinePercent { get; init; }

    /// <summary>Recent average minus the historical average for the reference hour, on a 0–100 point scale.</summary>
    public double? RecentTrendPercent { get; init; }

    /// <summary>Trend actually added at this horizon after decay.</summary>
    public double? AppliedTrendPercent { get; init; }

    public int HistoricalSampleCount { get; init; }

    public string BaselineSource { get; init; } = BaselineSources.None;
}

public sealed class CarParkListResponse
{
    public IReadOnlyList<string> CarParks { get; init; } = [];
}

public sealed class BatchPredictionRequest
{
    public List<string>? CarParkCodes { get; set; }

    public string? CountingCategory { get; set; }

    public DateTimeOffset? AsOf { get; set; }

    public List<int>? Horizons { get; set; }

    public string? Predictor { get; set; }
}

public sealed class BatchPredictionResponse
{
    public DateTimeOffset GeneratedAt { get; init; }

    public IReadOnlyList<BatchPredictionItem> Results { get; init; } = [];
}

public sealed class BatchPredictionItem
{
    public required string CarParkCode { get; init; }

    public bool Found { get; init; }

    public CarParkPredictionResult? Prediction { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; init; }
}

public sealed class HealthResponse
{
    public string Status { get; init; } = "ok";

    public string Service { get; init; } = "CarParkOccupancy.Api";

    /// <summary>Target framework moniker for this build.</summary>
    public string Framework { get; init; } = "net10.0";
}
