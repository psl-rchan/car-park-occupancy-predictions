namespace CarParkOccupancy.Api.Options;

public sealed class CarParkDataOptions
{
    public const string SectionName = "CarParkData";

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
