using CarParkOccupancy.Api.Models;
using CarParkOccupancy.Api.Options;
using Microsoft.Extensions.Options;

namespace CarParkOccupancy.Api.Services.Prediction;

/// <summary>
/// Predicts occupancy from the same local weekday and clock hour, then shifts that
/// average by the recent trend. Percent values use a 0–100 scale.
/// </summary>
/// <remarks>
/// Snapshots at or before <c>asOf - RecentTrendHours</c> form the historical baseline.
/// The last <c>RecentTrendHours</c> are held out so the trend is not counted twice.
/// Within each pool, snapshots are grouped into local clock hours. Each hour is one
/// sample (sum of occupied spaces / sum of capacity), so a finer polling interval
/// does not outweigh a coarser one.
/// For each horizon, the baseline prefers that weekday and hour, then the same hour
/// on any weekday, then the overall historical average. The trend is
/// recent average minus the historical average of the reference hour, multiplied by
/// <c>TrendDecay ^ (horizon - 1)</c>. The result is clamped to 0–100.
/// </remarks>
public sealed class HistoricalBaselinePredictor : IOccupancyPredictor
{
    public const string PredictorName = "HistoricalBaseline";

    private readonly PredictionOptions _options;
    private readonly TimeZoneInfo _timeZone;

    public HistoricalBaselinePredictor(IOptions<PredictionOptions> options)
        : this(options.Value)
    {
    }

    public HistoricalBaselinePredictor(PredictionOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _timeZone = TimeZoneResolver.Resolve(options.Timezone);
    }

    public string Name => PredictorName;

    public Task<CarParkPredictionResult> PredictAsync(
        OccupancyPredictionRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Predict(request));
    }

    public CarParkPredictionResult Predict(OccupancyPredictionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.HorizonsHours is null || request.HorizonsHours.Count == 0)
        {
            throw new ArgumentException("At least one horizon is required.", nameof(request));
        }

        if (request.HorizonsHours.Any(horizon => horizon is < HorizonParser.MinHorizonHours or > HorizonParser.MaxHorizonHours))
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Horizons must be between 1 and 6.");
        }

        var recentHours = _options.RecentTrendHours;
        var decay = _options.TrendDecay;
        var asOf = request.AsOf;
        var cutoff = asOf.AddHours(-recentHours);
        var history = (request.History ?? [])
            .Where(observation => observation.SnapshotTime <= asOf && observation.Capacity > 0 && observation.Occupied >= 0)
            .ToList();

        var historical = Bucket(history.Where(observation => observation.SnapshotTime <= cutoff));
        var recent = Bucket(history.Where(observation => observation.SnapshotTime > cutoff));
        var recentAverage = Average(recent);
        var anchor = Average(FilterSlot(historical, asOf, matchWeekday: true));
        double? trend = recentAverage is not null && anchor is not null
            ? recentAverage.Value - anchor.Value
            : null;

        var latest = history.OrderByDescending(observation => observation.SnapshotTime).FirstOrDefault();
        var predictions = new List<HourlyPrediction>();
        foreach (var horizon in request.HorizonsHours.Distinct().OrderBy(horizon => horizon))
        {
            var target = asOf.AddHours(horizon);
            var (baseline, sampleCount, source) = ResolveBaseline(historical, target);
            double? predicted = null;
            double? applied = null;

            if (baseline is not null)
            {
                applied = (trend ?? 0) * Math.Pow(decay, horizon - 1);
                predicted = ClampPercent(baseline.Value + applied.Value);
            }
            else if (recentAverage is not null)
            {
                predicted = ClampPercent(recentAverage.Value);
                source = BaselineSources.RecentOnly;
                sampleCount = 0;
                applied = 0;
            }

            predictions.Add(new HourlyPrediction
            {
                HorizonHours = horizon,
                TargetTime = target,
                PredictedOccupancyPercent = Round1(predicted),
                BaselinePercent = Round1(baseline),
                RecentTrendPercent = Round1(trend),
                AppliedTrendPercent = Round1(applied),
                HistoricalSampleCount = sampleCount,
                BaselineSource = source
            });
        }

        return new CarParkPredictionResult
        {
            CarParkCode = request.CarParkCode,
            CountingCategory = request.CountingCategory,
            GeneratedAt = asOf,
            Timezone = _timeZone.Id,
            OccupancyPercentScale = "0-100",
            Method = PredictorName,
            Predictor = PredictorName,
            LiveModelInvoked = false,
            RecentSampleCount = recent.Count,
            RecentTrendHours = recentHours,
            LatestSnapshotTime = latest?.SnapshotTime,
            LatestCapacity = latest?.Capacity,
            LatestOccupied = latest?.Occupied,
            LatestOccupancyPercent = Round1(latest?.OccupancyPercent),
            Predictions = predictions
        };
    }

    private (double? Baseline, int SampleCount, string Source) ResolveBaseline(
        IReadOnlyList<HourSample> historical,
        DateTimeOffset target)
    {
        var sameSlot = FilterSlot(historical, target, matchWeekday: true);
        if (sameSlot.Count > 0)
        {
            return (Average(sameSlot), sameSlot.Count, BaselineSources.SameWeekdayHour);
        }

        var sameHour = FilterSlot(historical, target, matchWeekday: false);
        if (sameHour.Count > 0)
        {
            return (Average(sameHour), sameHour.Count, BaselineSources.SameHour);
        }

        if (historical.Count > 0)
        {
            return (Average(historical), historical.Count, BaselineSources.Overall);
        }

        return (null, 0, BaselineSources.None);
    }

    private List<HourSample> FilterSlot(IReadOnlyList<HourSample> samples, DateTimeOffset instant, bool matchWeekday)
    {
        var local = TimeZoneInfo.ConvertTime(instant, _timeZone);
        return samples.Where(sample =>
                sample.Hour == local.Hour
                && (!matchWeekday || sample.DayOfWeek == local.DayOfWeek))
            .ToList();
    }

    private List<HourSample> Bucket(IEnumerable<OccupancyObservation> observations)
    {
        return observations
            .GroupBy(observation =>
            {
                var local = TimeZoneInfo.ConvertTime(observation.SnapshotTime, _timeZone);
                return (Date: DateOnly.FromDateTime(local.DateTime), local.Hour, local.DayOfWeek);
            })
            .Select(group =>
            {
                var occupied = group.Sum(observation => (long)observation.Occupied);
                var capacity = group.Sum(observation => (long)observation.Capacity);
                if (capacity <= 0)
                {
                    return (HourSample?)null;
                }

                var percent = Math.Clamp(occupied * 100.0 / capacity, 0, 100);
                return new HourSample(group.Key.DayOfWeek, group.Key.Hour, percent);
            })
            .Where(sample => sample is not null)
            .Select(sample => sample!.Value)
            .ToList();
    }

    private static double? Average(IReadOnlyList<HourSample> samples)
    {
        if (samples.Count == 0)
        {
            return null;
        }

        return samples.Average(sample => sample.OccupancyPercent);
    }

    private static double ClampPercent(double value) => Math.Clamp(value, 0, 100);

    private static double? Round1(double? value) =>
        value is null ? null : Math.Round(value.Value, 1, MidpointRounding.AwayFromZero);

    private readonly record struct HourSample(DayOfWeek DayOfWeek, int Hour, double OccupancyPercent);
}
