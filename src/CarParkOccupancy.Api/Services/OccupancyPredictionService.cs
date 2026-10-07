using CarParkOccupancy.Api.Data;
using CarParkOccupancy.Api.Models;
using CarParkOccupancy.Api.Services.Prediction;

namespace CarParkOccupancy.Api.Services;

public sealed class OccupancyPredictionService
{
    private readonly ICarParkReadStore _store;
    private readonly OccupancyPredictorResolver _predictors;

    public OccupancyPredictionService(ICarParkReadStore store, OccupancyPredictorResolver predictors)
    {
        _store = store;
        _predictors = predictors;
    }

    public async Task<CarParkPredictionResult?> PredictAsync(
        string carParkCode,
        string? countingCategory,
        DateTimeOffset asOf,
        IReadOnlyList<int> horizons,
        string? predictorName,
        CancellationToken cancellationToken)
    {
        var code = carParkCode.Trim();
        if (!await _store.ExistsAsync(code, cancellationToken))
        {
            return null;
        }

        var category = NormalizeCategory(countingCategory);
        var history = await _store.GetHistoryAsync(code, category, asOf, cancellationToken);
        var predictor = _predictors.Resolve(predictorName);
        return await predictor.PredictAsync(
            new OccupancyPredictionRequest
            {
                CarParkCode = code,
                CountingCategory = category,
                AsOf = asOf,
                HorizonsHours = horizons,
                History = history
            },
            cancellationToken);
    }

    public static string? NormalizeCategory(string? countingCategory)
    {
        if (string.IsNullOrWhiteSpace(countingCategory))
        {
            return null;
        }

        var trimmed = countingCategory.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
