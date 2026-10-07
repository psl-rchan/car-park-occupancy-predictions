using CarParkOccupancy.Api.Models;
using CarParkOccupancy.Api.Options;
using Microsoft.Extensions.Options;

namespace CarParkOccupancy.Api.Services.Prediction;

/// <summary>
/// Selects the Vertex AI Gemini predictor. Version 1 does not call Vertex.
/// Missing project, location, or model settings fall back to <see cref="HistoricalBaselinePredictor"/>.
/// Configured settings also stay on the baseline so the API builds and runs without Gemini credentials.
/// </summary>
public sealed class GeminiVertexPredictor : IOccupancyPredictor
{
    public const string PredictorName = "GeminiVertex";

    public const string NotConfiguredReason =
        "Vertex AI is not configured. Set Prediction:Vertex:ProjectId, Location, and Model. Fell back to HistoricalBaseline.";

    public const string StubReason =
        "GeminiVertexPredictor is a v1 stub and does not call Vertex AI, so the API runs without Gemini credentials. Values were produced by HistoricalBaseline.";

    private readonly HistoricalBaselinePredictor _baseline;
    private readonly VertexAiOptions _vertex;

    public GeminiVertexPredictor(HistoricalBaselinePredictor baseline, IOptions<PredictionOptions> options)
        : this(baseline, options.Value)
    {
    }

    public GeminiVertexPredictor(HistoricalBaselinePredictor baseline, PredictionOptions options)
    {
        _baseline = baseline ?? throw new ArgumentNullException(nameof(baseline));
        ArgumentNullException.ThrowIfNull(options);
        _vertex = options.Vertex ?? new VertexAiOptions();
    }

    public string Name => PredictorName;

    public bool IsConfigured => _vertex.IsConfigured;

    public async Task<CarParkPredictionResult> PredictAsync(
        OccupancyPredictionRequest request,
        CancellationToken cancellationToken = default)
    {
        var baseline = await _baseline.PredictAsync(request, cancellationToken);
        return baseline with
        {
            Predictor = PredictorName,
            LiveModelInvoked = false,
            FallbackReason = _vertex.IsConfigured ? StubReason : NotConfiguredReason
        };
    }
}
