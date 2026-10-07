using System.Globalization;
using CarParkOccupancy.Api.Models;

namespace CarParkOccupancy.Api.Ui;

/// <summary>
/// Picks car parks for the dashboard high-occupancy charts.
/// A value is high when it is at least the configured percent of capacity.
/// </summary>
public static class HighOccupancyCharts
{
    public static bool IsAtOrAbove(double? percent, double threshold)
    {
        if (percent is not double value || !double.IsFinite(value) || !double.IsFinite(threshold))
        {
            return false;
        }

        return value >= threshold;
    }

    public static string FormatThreshold(double threshold) =>
        (!double.IsFinite(threshold) ? 0 : threshold).ToString("0.##", CultureInfo.InvariantCulture);

    public static string MarkLeft(double threshold)
    {
        var clamped = double.IsFinite(threshold) ? Math.Clamp(threshold, 0, 100) : 0;
        return clamped.ToString("0.##", CultureInfo.InvariantCulture) + "%";
    }

    public static IReadOnlyList<PredictedHighOccupancyPark> Predicted(
        IEnumerable<ParkOccupancyInput> parks,
        IReadOnlyList<int> horizons,
        double threshold)
    {
        ArgumentNullException.ThrowIfNull(parks);
        ArgumentNullException.ThrowIfNull(horizons);

        var rows = new List<PredictedHighOccupancyPark>();
        foreach (var park in parks)
        {
            if (!park.Found)
            {
                continue;
            }

            var hours = new List<PredictedHourPoint>(horizons.Count);
            double? max = null;
            var maxHorizon = 0;
            var highCount = 0;
            var knownCount = 0;
            foreach (var horizon in horizons)
            {
                var percent = OccupancyDisplay.AtHorizon(park.Predictions, horizon)?.PredictedOccupancyPercent;
                var known = percent is double value && double.IsFinite(value);
                var high = IsAtOrAbove(percent, threshold);
                if (known)
                {
                    knownCount++;
                    if (max is null || percent!.Value > max.Value)
                    {
                        max = percent!.Value;
                        maxHorizon = horizon;
                    }
                }

                if (high)
                {
                    highCount++;
                }

                hours.Add(new PredictedHourPoint
                {
                    HorizonHours = horizon,
                    PredictedOccupancyPercent = known ? percent : null,
                    IsHigh = high
                });
            }

            if (highCount == 0 || max is null)
            {
                continue;
            }

            rows.Add(new PredictedHighOccupancyPark
            {
                Code = park.Code,
                MaxPredictedPercent = max.Value,
                MaxHorizonHours = maxHorizon,
                HighInEveryHorizon = horizons.Count > 0 && knownCount == horizons.Count && highCount == horizons.Count,
                Hours = hours
            });
        }

        return rows
            .OrderByDescending(row => row.MaxPredictedPercent)
            .ThenBy(row => row.Code, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<CurrentHighOccupancyPark> Current(
        IEnumerable<ParkOccupancyInput> parks,
        double threshold)
    {
        ArgumentNullException.ThrowIfNull(parks);

        return parks
            .Where(park => park.Found && park.LatestOccupancyPercent is double latest && IsAtOrAbove(latest, threshold))
            .Select(park => new CurrentHighOccupancyPark
            {
                Code = park.Code,
                OccupancyPercent = park.LatestOccupancyPercent!.Value,
                SnapshotTime = park.LatestSnapshotTime
            })
            .OrderByDescending(row => row.OccupancyPercent)
            .ThenBy(row => row.Code, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}

public sealed record ParkOccupancyInput
{
    public required string Code { get; init; }

    public bool Found { get; init; }

    public double? LatestOccupancyPercent { get; init; }

    public DateTimeOffset? LatestSnapshotTime { get; init; }

    public IReadOnlyList<HourlyPrediction> Predictions { get; init; } = [];
}

public sealed record PredictedHighOccupancyPark
{
    public required string Code { get; init; }

    public required double MaxPredictedPercent { get; init; }

    public required int MaxHorizonHours { get; init; }

    public required bool HighInEveryHorizon { get; init; }

    public required IReadOnlyList<PredictedHourPoint> Hours { get; init; }

    public string Span => HighInEveryHorizon ? "all" : "any";
}

public sealed record PredictedHourPoint
{
    public int HorizonHours { get; init; }

    public double? PredictedOccupancyPercent { get; init; }

    public bool IsHigh { get; init; }
}

public sealed record CurrentHighOccupancyPark
{
    public required string Code { get; init; }

    public required double OccupancyPercent { get; init; }

    public DateTimeOffset? SnapshotTime { get; init; }
}
