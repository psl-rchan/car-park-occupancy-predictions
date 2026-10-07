using CarParkOccupancy.Api.Options;
using Microsoft.Extensions.Options;

namespace CarParkOccupancy.Api.Services.Prediction;

public sealed class OccupancyPredictorResolver
{
    private readonly PredictionOptions _options;
    private readonly Dictionary<string, IOccupancyPredictor> _predictors;

    public OccupancyPredictorResolver(
        HistoricalBaselinePredictor baseline,
        GeminiVertexPredictor gemini,
        IOptions<PredictionOptions> options)
    {
        _options = options.Value;
        _predictors = new Dictionary<string, IOccupancyPredictor>(StringComparer.OrdinalIgnoreCase)
        {
            [baseline.Name] = baseline,
            [gemini.Name] = gemini
        };
    }

    public IOccupancyPredictor Resolve(string? requestedName)
    {
        var selected = string.IsNullOrWhiteSpace(requestedName)
            ? _options.DefaultPredictor
            : requestedName.Trim();

        if (!string.IsNullOrWhiteSpace(selected) && _predictors.TryGetValue(selected, out var predictor))
        {
            return predictor;
        }

        throw new UnknownPredictorException(
            $"Unknown predictor '{selected}'. Use {HistoricalBaselinePredictor.PredictorName} or {GeminiVertexPredictor.PredictorName}.");
    }
}
