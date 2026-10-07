using CarParkOccupancy.Api.Models;

namespace CarParkOccupancy.Api.Services.Prediction;

public interface IOccupancyPredictor
{
    /// <summary>Name used in configuration and the predictor query parameter.</summary>
    string Name { get; }

    Task<CarParkPredictionResult> PredictAsync(
        OccupancyPredictionRequest request,
        CancellationToken cancellationToken = default);
}
