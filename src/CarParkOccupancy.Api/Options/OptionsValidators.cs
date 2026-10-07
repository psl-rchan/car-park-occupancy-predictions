using CarParkOccupancy.Api.Data;
using CarParkOccupancy.Api.Services.Prediction;
using Microsoft.Extensions.Options;

namespace CarParkOccupancy.Api.Options;

public sealed class CarParkDataOptionsValidator : IValidateOptions<CarParkDataOptions>
{
    public ValidateOptionsResult Validate(string? name, CarParkDataOptions options)
    {
        try
        {
            _ = new SqlObjectNames(options);
        }
        catch (ArgumentException ex)
        {
            return ValidateOptionsResult.Fail(ex.Message);
        }

        if (options.HistoryLookbackDays < 1)
        {
            return ValidateOptionsResult.Fail("CarParkData:HistoryLookbackDays must be at least 1.");
        }

        if (options.CommandTimeoutSeconds < 1)
        {
            return ValidateOptionsResult.Fail("CarParkData:CommandTimeoutSeconds must be at least 1.");
        }

        return ValidateOptionsResult.Success;
    }
}

public sealed class PredictionOptionsValidator : IValidateOptions<PredictionOptions>
{
    public ValidateOptionsResult Validate(string? name, PredictionOptions options)
    {
        try
        {
            _ = TimeZoneResolver.Resolve(options.Timezone);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return ValidateOptionsResult.Fail($"Prediction:Timezone '{options.Timezone}' is not a valid time zone.");
        }

        var predictor = options.DefaultPredictor?.Trim() ?? string.Empty;
        if (!predictor.Equals(HistoricalBaselinePredictor.PredictorName, StringComparison.OrdinalIgnoreCase)
            && !predictor.Equals(GeminiVertexPredictor.PredictorName, StringComparison.OrdinalIgnoreCase))
        {
            return ValidateOptionsResult.Fail(
                "Prediction:DefaultPredictor must be HistoricalBaseline or GeminiVertex.");
        }

        if (options.RecentTrendHours is < 1 or > 168)
        {
            return ValidateOptionsResult.Fail("Prediction:RecentTrendHours must be between 1 and 168.");
        }

        if (double.IsNaN(options.TrendDecay) || options.TrendDecay is < 0 or > 1)
        {
            return ValidateOptionsResult.Fail("Prediction:TrendDecay must be between 0 and 1.");
        }

        return ValidateOptionsResult.Success;
    }
}
