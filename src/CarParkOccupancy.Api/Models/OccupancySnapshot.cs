namespace CarParkOccupancy.Api.Models;

/// <summary>
/// One occupancy reading from SQL Server, the sample generator, or a third-party HTTP API.
/// </summary>
public sealed class OccupancySnapshot
{
    public required string CarParkCode { get; init; }

    public required DateTimeOffset SnapshotTime { get; init; }

    public string? CountingCategory { get; init; }

    public int Capacity { get; init; }

    public int Occupied { get; init; }
}
