using CarParkOccupancy.Api.Models;
using CarParkOccupancy.Api.Options;
using CarParkOccupancy.Api.Ui;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CarParkOccupancy.Api.Pages;

public sealed class IndexModel : PredictionPageModel
{
    public IndexModel(CarParkApiClient api, IOptions<CarParkDataOptions> dataOptions)
        : base(api)
    {
        HighOccupancyThresholdPercent = dataOptions.Value.HighOccupancyThresholdPercent;
    }

    public IReadOnlyList<ParkPredictionRow> Rows { get; private set; } = [];

    public DateTimeOffset? GeneratedAt { get; private set; }

    public string MethodSummary { get; private set; } = "";

    public string Scale { get; private set; } = "0-100";

    public double HighOccupancyThresholdPercent { get; }

    public string HighOccupancyThresholdLabel =>
        HighOccupancyCharts.FormatThreshold(HighOccupancyThresholdPercent);

    public string ThresholdMarkLeft => HighOccupancyCharts.MarkLeft(HighOccupancyThresholdPercent);

    public IReadOnlyList<PredictedHighOccupancyPark> PredictedHigh { get; private set; } = [];

    public IReadOnlyList<CurrentHighOccupancyPark> CurrentHigh { get; private set; } = [];

    public int PredictedHighEveryHourCount => PredictedHigh.Count(park => park.HighInEveryHorizon);

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        try
        {
            var codes = await Api.GetCarParkCodesAsync(cancellationToken);
            if (codes.Count == 0)
            {
                Rows = [];
                return Page();
            }

            var batch = await Api.PredictManyAsync(codes, cancellationToken);
            GeneratedAt = batch.GeneratedAt;
            Rows = Map(codes, batch);
            var methods = Rows
                .Select(row => row.Method)
                .Where(method => !string.IsNullOrWhiteSpace(method))
                .Distinct(StringComparer.Ordinal)
                .Cast<string>()
                .ToArray();
            MethodSummary = string.Join(", ", methods);
            Scale = Rows.Select(row => row.Scale).FirstOrDefault(scale => !string.IsNullOrWhiteSpace(scale)) ?? "0-100";
            var inputs = Rows.Select(row => new ParkOccupancyInput
            {
                Code = row.Code,
                Found = row.Found,
                LatestOccupancyPercent = row.LatestOccupancyPercent,
                LatestSnapshotTime = row.LatestSnapshotTime,
                Predictions = row.Predictions
            }).ToArray();
            PredictedHigh = HighOccupancyCharts.Predicted(inputs, Horizons, HighOccupancyThresholdPercent);
            CurrentHigh = HighOccupancyCharts.Current(inputs, HighOccupancyThresholdPercent);
            return Page();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return FailTimeout();
        }
        catch (CarParkApiException ex)
        {
            return Fail(ex);
        }
        catch (HttpRequestException)
        {
            return FailTransport();
        }
    }

    private static IReadOnlyList<ParkPredictionRow> Map(IReadOnlyList<string> codes, BatchPredictionResponse batch)
    {
        var lookup = new Dictionary<string, BatchPredictionItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in batch.Results)
        {
            lookup[item.CarParkCode] = item;
        }

        var rows = new List<ParkPredictionRow>(codes.Count);
        foreach (var code in codes)
        {
            lookup.TryGetValue(code, out var item);
            if (item is null || !item.Found || item.Prediction is null)
            {
                rows.Add(new ParkPredictionRow
                {
                    Code = code,
                    Found = false,
                    Error = item is not null && !string.IsNullOrWhiteSpace(item.Error)
                    ? item.Error
                    : "搵唔到 · Not found"
                });
                continue;
            }

            rows.Add(new ParkPredictionRow
            {
                Code = item.Prediction.CarParkCode,
                Found = true,
                Method = item.Prediction.Method,
                Scale = item.Prediction.OccupancyPercentScale,
                LatestOccupancyPercent = item.Prediction.LatestOccupancyPercent,
                LatestSnapshotTime = item.Prediction.LatestSnapshotTime,
                Predictions = item.Prediction.Predictions
            });
        }

        return rows;
    }

    public sealed class ParkPredictionRow
    {
        public required string Code { get; init; }

        public bool Found { get; init; }

        public string? Error { get; init; }

        public string? Method { get; init; }

        public string? Scale { get; init; }

        public double? LatestOccupancyPercent { get; init; }

        public DateTimeOffset? LatestSnapshotTime { get; init; }

        public IReadOnlyList<HourlyPrediction> Predictions { get; init; } = [];
    }
}
