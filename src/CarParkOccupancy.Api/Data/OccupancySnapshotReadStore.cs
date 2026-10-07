using CarParkOccupancy.Api.Models;
using CarParkOccupancy.Api.Options;
using Microsoft.Extensions.Options;

namespace CarParkOccupancy.Api.Data;

/// <summary>
/// Adapts <see cref="IOccupancySnapshotSource"/> to the history shape used by prediction.
/// Rows that share a timestamp are summed, including across counting categories when no category was requested.
/// </summary>
public sealed class OccupancySnapshotReadStore : ICarParkReadStore
{
    private readonly IOccupancySnapshotSource _source;
    private readonly CarParkDataOptions _options;
    private readonly ILogger<OccupancySnapshotReadStore> _logger;

    public OccupancySnapshotReadStore(
        IOccupancySnapshotSource source,
        IOptions<CarParkDataOptions> options,
        ILogger<OccupancySnapshotReadStore> logger)
    {
        _source = source;
        _options = options.Value;
        _logger = logger;
    }

    public Task<IReadOnlyList<string>> GetCarParkCodesAsync(CancellationToken cancellationToken) =>
        _source.GetCarParkCodesAsync(cancellationToken);

    public Task<bool> ExistsAsync(string carParkCode, CancellationToken cancellationToken) =>
        _source.ExistsAsync(carParkCode, cancellationToken);

    public async Task<IReadOnlyList<OccupancyObservation>> GetHistoryAsync(
        string carParkCode,
        string? countingCategory,
        DateTimeOffset asOf,
        CancellationToken cancellationToken)
    {
        var lookbackDays = Math.Max(1, _options.HistoryLookbackDays);
        var snapshots = await _source.GetSnapshotsAsync(
            carParkCode,
            countingCategory,
            asOf.AddDays(-lookbackDays),
            asOf,
            cancellationToken);

        var observations = snapshots
            .GroupBy(snapshot => snapshot.SnapshotTime.UtcDateTime)
            .Select(group => new OccupancyObservation
            {
                SnapshotTime = group.First().SnapshotTime,
                Occupied = ClampToInt(group.Sum(snapshot => (long)snapshot.Occupied)),
                Capacity = ClampToInt(group.Sum(snapshot => (long)snapshot.Capacity))
            })
            .OrderBy(observation => observation.SnapshotTime)
            .ToArray();

        _logger.LogDebug(
            "Prepared {Count} occupancy observations for car park {CarParkCode}.",
            observations.Length,
            carParkCode);

        return observations;
    }

    private static int ClampToInt(long value)
    {
        if (value > int.MaxValue)
        {
            return int.MaxValue;
        }

        if (value < int.MinValue)
        {
            return int.MinValue;
        }

        return (int)value;
    }
}
