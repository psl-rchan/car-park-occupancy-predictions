namespace CarParkOccupancy.Api.Options;

public sealed class CarParkDataOptions
{
    public const string SectionName = "CarParkData";

    /// <summary>
    /// Sql, Http, or Sample. Empty keeps Sql, except the Development sample path
    /// when <see cref="UseSampleSnapshots"/> is set and no connection string is configured.
    /// </summary>
    public string Source { get; set; } = "";

    /// <summary>
    /// Development preview when <see cref="Source"/> is Sql or empty and
    /// <c>ConnectionStrings:CarParkDb</c> is empty. Ignored for an explicit Http source.
    /// </summary>
    public bool UseSampleSnapshots { get; set; }

    public string Schema { get; set; } = "dbo";

    public string TableName { get; set; } = "CarParkOccupancySnapshot";

    public CarParkColumnOptions Columns { get; set; } = new();

    /// <summary>
    /// When false, SnapshotTime values with an unspecified kind are read as
    /// local time in <see cref="PredictionOptions.Timezone"/>.
    /// </summary>
    public bool SnapshotTimeIsUtc { get; set; }

    public int HistoryLookbackDays { get; set; } = 400;

    public int CommandTimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// A car park is high occupancy when its percent of capacity is at least this value.
    /// The dashboard charts and legend read it. Default is 80 on the 0–100 scale.
    /// </summary>
    public double HighOccupancyThresholdPercent { get; set; } = 80;

    public HttpOccupancySourceOptions Http { get; set; } = new();
}

public sealed class HttpOccupancySourceOptions
{
    /// <summary>
    /// Absolute http or https URL of the car-park-occupancy-data-api.
    /// </summary>
    public string BaseUrl { get; set; } = "http://localhost:5095";

    public string CarParksPath { get; set; } = "api/carparks";

    public string HistoryPathTemplate { get; set; } = "api/carparks/{code}/occupancy";

    public string LatestPathTemplate { get; set; } = "api/carparks/{code}/occupancy/latest";

    public string LatestAllPath { get; set; } = "api/occupancy/latest";

    public string CategoryQueryParameter { get; set; } = "category";

    public int PageSize { get; set; } = 1000;

    public int SampleEverySeconds { get; set; } = 300;

    public int MaxPages { get; set; } = 50;

    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Optional header name, for example <c>X-Api-Key</c> or <c>Authorization</c>.
    /// Set <c>CarParkData__Http__AuthHeaderName</c>. Leave empty when the API has no auth.
    /// </summary>
    public string AuthHeaderName { get; set; } = "";

    /// <summary>
    /// Optional header value. Set <c>CarParkData__Http__AuthHeaderValue</c> from the environment
    /// or user secrets. The committed value must stay empty.
    /// </summary>
    public string AuthHeaderValue { get; set; } = "";

    public HttpSnapshotJsonNames Json { get; set; } = new();
}

public sealed class HttpSnapshotJsonNames
{
    public string CarParkCode { get; set; } = "CarParkCode";

    public string SnapshotTime { get; set; } = "SnapshotTime";

    public string CountingCategory { get; set; } = "CountingCategory";

    public string Capacity { get; set; } = "Capacity";

    public string Occupied { get; set; } = "Occupied";

    /// <summary>
    /// Property that holds the snapshot array when the body is an object. A bare JSON array is also accepted.
    /// </summary>
    public string Collection { get; set; } = "items";
}

public sealed class CarParkColumnOptions
{
    public string CarParkCode { get; set; } = "CarParkCode";

    public string SnapshotTime { get; set; } = "SnapshotTime";

    public string CountingCategory { get; set; } = "CountingCategory";

    public string Capacity { get; set; } = "Capacity";

    /// <summary>
    /// Occupied-space count. Occupancy percent is Occupied / Capacity * 100.
    /// </summary>
    public string Occupied { get; set; } = "Occupied";
}
