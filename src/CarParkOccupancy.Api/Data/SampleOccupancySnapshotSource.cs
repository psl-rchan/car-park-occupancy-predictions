using CarParkOccupancy.Api.Models;

namespace CarParkOccupancy.Api.Data;

/// <summary>
/// Generated snapshots for a local UI when SQL Server and the third-party API are not configured.
/// Enable with <c>CarParkData:Source=Sample</c>, or keep the Development
/// <c>CarParkData:UseSampleSnapshots</c> path when no connection string is set.
/// </summary>
public sealed class SampleOccupancySnapshotSource : IOccupancySnapshotSource
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

    public Task<IReadOnlyList<OccupancySnapshot>> GetSnapshotsAsync(
        string carParkCode,
        string? countingCategory,
        DateTimeOffset fromInclusive,
        DateTimeOffset toInclusive,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryIndex(carParkCode, out var index))
        {
            return Task.FromResult<IReadOnlyList<OccupancySnapshot>>([]);
        }

        var basePercent = 20 + ((index * 13) % 61);
        var snapshots = new List<OccupancySnapshot>(72);
        for (var hoursAgo = 1; hoursAgo <= 72; hoursAgo++)
        {
            var snapshotTime = toInclusive.AddHours(-hoursAgo);
            if (snapshotTime < fromInclusive || snapshotTime > toInclusive)
            {
                continue;
            }

            var wave = (int)Math.Round(8 * Math.Sin((hoursAgo + index) / 5.0));
            var percent = Math.Clamp(basePercent + wave, 5, 98);
            snapshots.Add(new OccupancySnapshot
            {
                CarParkCode = CodeFor(index),
                SnapshotTime = snapshotTime,
                CountingCategory = null,
                Capacity = 200,
                Occupied = percent * 2
            });
        }

        return Task.FromResult<IReadOnlyList<OccupancySnapshot>>(snapshots);
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
