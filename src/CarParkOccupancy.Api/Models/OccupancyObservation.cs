namespace CarParkOccupancy.Api.Models;

public sealed class OccupancyObservation
{
    public required DateTimeOffset SnapshotTime { get; init; }

    public int Occupied { get; init; }

    public int Capacity { get; init; }

    /// <summary>
    /// Occupancy as a percent of capacity on a 0–100 scale, or null when capacity is not positive.
    /// Values above 100 are clamped to 100.
    /// </summary>
    public double? OccupancyPercent
    {
        get
        {
            if (Capacity <= 0)
            {
                return null;
            }

            var raw = Occupied * 100.0 / Capacity;
            if (double.IsNaN(raw) || double.IsInfinity(raw))
            {
                return null;
            }

            return Math.Clamp(raw, 0, 100);
        }
    }
}
