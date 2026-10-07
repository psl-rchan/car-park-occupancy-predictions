using CarParkOccupancy.Api.Models;

namespace CarParkOccupancy.Api.Data;

/// <summary>
/// Reads occupancy snapshots. Prediction and the Razor UI go through
/// <see cref="ICarParkReadStore"/>, which aggregates these rows.
/// </summary>
public interface IOccupancySnapshotSource
{
    Task<IReadOnlyList<string>> GetCarParkCodesAsync(CancellationToken cancellationToken);

    Task<bool> ExistsAsync(string carParkCode, CancellationToken cancellationToken);

    Task<IReadOnlyList<OccupancySnapshot>> GetSnapshotsAsync(
        string carParkCode,
        string? countingCategory,
        DateTimeOffset fromInclusive,
        DateTimeOffset toInclusive,
        CancellationToken cancellationToken);
}
