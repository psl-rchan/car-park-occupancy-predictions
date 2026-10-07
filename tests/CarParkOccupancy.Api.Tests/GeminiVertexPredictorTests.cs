using CarParkOccupancy.Api.Models;
using CarParkOccupancy.Api.Options;
using CarParkOccupancy.Api.Services.Prediction;

namespace CarParkOccupancy.Api.Tests;

public sealed class GeminiVertexPredictorTests
{
    private static DateTimeOffset Hkt(int year, int month, int day, int hour) =>
        new(year, month, day, hour, 0, 0, TimeSpan.FromHours(8));

    private static OccupancyPredictionRequest Request() =>
        new()
        {
            CarParkCode = "CP001",
            AsOf = Hkt(2026, 10, 7, 10),
            HorizonsHours = [1],
            History =
            [
                new OccupancyObservation
                {
                    SnapshotTime = Hkt(2026, 9, 30, 11),
                    Occupied = 40,
                    Capacity = 100
                }
            ]
        };

    [Fact]
    public async Task Falls_back_to_baseline_when_vertex_is_not_configured()
    {
        var options = new PredictionOptions
        {
            Timezone = "Asia/Hong_Kong",
            TrendDecay = 0.5,
            RecentTrendHours = 6,
            Vertex = new VertexAiOptions()
        };
        var baseline = new HistoricalBaselinePredictor(options);
        var gemini = new GeminiVertexPredictor(baseline, options);

        var expected = baseline.Predict(Request());
        var actual = await gemini.PredictAsync(Request());

        Assert.False(gemini.IsConfigured);
        Assert.Equal(HistoricalBaselinePredictor.PredictorName, actual.Method);
        Assert.Equal(GeminiVertexPredictor.PredictorName, actual.Predictor);
        Assert.False(actual.LiveModelInvoked);
        Assert.Equal(GeminiVertexPredictor.NotConfiguredReason, actual.FallbackReason);
        Assert.Equal(expected.Predictions[0].PredictedOccupancyPercent, actual.Predictions[0].PredictedOccupancyPercent);
    }

    [Fact]
    public async Task Stays_on_baseline_when_vertex_is_configured_without_calling_a_model()
    {
        var options = new PredictionOptions
        {
            Timezone = "Asia/Hong_Kong",
            TrendDecay = 0.5,
            RecentTrendHours = 6,
            Vertex = new VertexAiOptions
            {
                ProjectId = "example-project",
                Location = "asia-east1",
                Model = "gemini-2.5-pro"
            }
        };
        var baseline = new HistoricalBaselinePredictor(options);
        var gemini = new GeminiVertexPredictor(baseline, options);

        var actual = await gemini.PredictAsync(Request());

        Assert.True(gemini.IsConfigured);
        Assert.Equal(HistoricalBaselinePredictor.PredictorName, actual.Method);
        Assert.Equal(GeminiVertexPredictor.PredictorName, actual.Predictor);
        Assert.False(actual.LiveModelInvoked);
        Assert.Equal(GeminiVertexPredictor.StubReason, actual.FallbackReason);
        Assert.Equal(40, actual.Predictions[0].PredictedOccupancyPercent);
    }
}
