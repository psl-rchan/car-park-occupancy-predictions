using CarParkOccupancy.Api.Models;

namespace CarParkOccupancy.Api.Data;

/// <summary>
/// Generated snapshots for a Development preview when SQL Server is not configured.
/// Enable with CarParkData:UseSampleSnapshots. A configured connection string always wins.
/// </summary>
public sealed class SampleCarParkReadStore : ICarParkReadStore
{
    public const int ParkCount = 50;

    public Task<IReadOnlyList<string>> GetCarParkCodesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<string> codes = Enumerable.Range(1, ParkCount).Select(CodeFor).ToArray();
        return Task.FromResult(codes);
    }

    public Task<bool> ExistsAsync(string carParkCode, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(TryIndex(carParkCode, out _));
    }

    public Task<IReadOnlyList<OccupancyObservation>> GetHistoryAsync(
        string carParkCode,
        string? countingCategory,
        DateTimeOffset asOf,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryIndex(carParkCode, out var index))
        {
            return Task.FromResult<IReadOnlyList<OccupancyObservation>>([]);
        }

        var basePercent = 20 + ((index * 13) % 61);
        var observations = new List<OccupancyObservation>(72);
        for (var hoursAgo = 1; hoursAgo <= 72; hoursAgo++)
        {
            var wave = (int)Math.Round(8 * Math.Sin((hoursAgo + index) / 5.0));
            var percent = Math.Clamp(basePercent + wave, 5, 98);
            observations.Add(new OccupancyObservation
            {
                SnapshotTime = asOf.AddHours(-hoursAgo),
                Capacity = 200,
                Occupied = percent * 2
            });
        }

        return Task.FromResult<IReadOnlyList<OccupancyObservation>>(observations);
    }

    public static string CodeFor(int index) => $"CP{index:00}";

    private static bool TryIndex(string? code, out int index)
    {
        index = 0;
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        var trimmed = code.Trim();
        if (trimmed.Length != 4 || !trimmed.StartsWith("CP", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return int.TryParse(trimmed.AsSpan(2), out index) && index is >= 1 and <= ParkCount;
    }
}
