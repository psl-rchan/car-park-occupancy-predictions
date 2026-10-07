using CarParkOccupancy.Api.Data;
using CarParkOccupancy.Api.Services.Prediction;
using Microsoft.Extensions.Options;

namespace CarParkOccupancy.Api.Options;

public sealed class CarParkDataOptionsValidator : IValidateOptions<CarParkDataOptions>
{
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;

    public CarParkDataOptionsValidator(IConfiguration configuration, IHostEnvironment environment)
    {
        _configuration = configuration;
        _environment = environment;
    }

    public ValidateOptionsResult Validate(string? name, CarParkDataOptions options)
    {
        var connectionString = _configuration.GetConnectionString(SqlConnectionFactory.ConnectionStringName);
        if (!OccupancySnapshotSourceSelector.TryResolve(
                options.Source,
                options.UseSampleSnapshots,
                connectionString,
                _environment.IsDevelopment(),
                out var kind,
                out var error))
        {
            return ValidateOptionsResult.Fail(error!);
        }

        if (kind == OccupancySnapshotSourceKind.Sql)
        {
            try
            {
                _ = new SqlObjectNames(options);
            }
            catch (ArgumentException ex)
            {
                return ValidateOptionsResult.Fail(ex.Message);
            }
        }

        if (options.HistoryLookbackDays < 1)
        {
            return ValidateOptionsResult.Fail("CarParkData:HistoryLookbackDays must be at least 1.");
        }

        if (options.CommandTimeoutSeconds < 1)
        {
            return ValidateOptionsResult.Fail("CarParkData:CommandTimeoutSeconds must be at least 1.");
        }

        var threshold = options.HighOccupancyThresholdPercent;
        if (double.IsNaN(threshold) || double.IsInfinity(threshold) || threshold is < 0 or > 100)
        {
            return ValidateOptionsResult.Fail(
                "CarParkData:HighOccupancyThresholdPercent must be a number from 0 to 100.");
        }

        var httpError = ValidateHttp(options.Http);
        if (httpError is not null)
        {
            return ValidateOptionsResult.Fail(httpError);
        }

        return ValidateOptionsResult.Success;
    }

    private static string? ValidateHttp(HttpOccupancySourceOptions? http)
    {
        if (http is null)
        {
            return "CarParkData:Http is required.";
        }

        if (http.TimeoutSeconds is < 1 or > 600)
        {
            return "CarParkData:Http:TimeoutSeconds must be between 1 and 600.";
        }

        var baseUrl = http.BaseUrl?.Trim() ?? string.Empty;
        if (baseUrl.Length > 0
            && (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)))
        {
            return "CarParkData:Http:BaseUrl must be an absolute http or https URL when it is set.";
        }

        if (string.IsNullOrWhiteSpace(http.SnapshotsPath)
            || http.SnapshotsPath.Contains("://", StringComparison.Ordinal)
            || http.SnapshotsPath.Contains('?', StringComparison.Ordinal)
            || http.SnapshotsPath.Contains('#', StringComparison.Ordinal)
            || http.SnapshotsPath.Contains("..", StringComparison.Ordinal)
            || http.SnapshotsPath.Any(char.IsControl))
        {
            return "CarParkData:Http:SnapshotsPath must be a relative path.";
        }

        var headerName = http.AuthHeaderName?.Trim() ?? string.Empty;
        if (headerName.Length > 0 && !IsHeaderToken(headerName))
        {
            return "CarParkData:Http:AuthHeaderName must be an HTTP header token.";
        }

        if ((http.AuthHeaderValue ?? string.Empty).Contains('\r') || (http.AuthHeaderValue ?? string.Empty).Contains('\n'))
        {
            return "CarParkData:Http:AuthHeaderValue must not contain line breaks.";
        }

        var json = http.Json ?? new HttpSnapshotJsonNames();
        if (string.IsNullOrWhiteSpace(json.CarParkCode)
            || string.IsNullOrWhiteSpace(json.SnapshotTime)
            || string.IsNullOrWhiteSpace(json.CountingCategory)
            || string.IsNullOrWhiteSpace(json.Capacity)
            || string.IsNullOrWhiteSpace(json.Occupied)
            || string.IsNullOrWhiteSpace(json.Collection))
        {
            return "CarParkData:Http:Json property names must not be empty.";
        }

        return null;
    }

    private static bool IsHeaderToken(string value)
    {
        foreach (var character in value)
        {
            var isTokenChar = character is (>= 'a' and <= 'z')
                or (>= 'A' and <= 'Z')
                or (>= '0' and <= '9')
                or '!' or '#' or '$' or '%' or '&' or '\'' or '*' or '+' or '-' or '.'
                or '^' or '_' or '`' or '|' or '~';
            if (!isTokenChar)
            {
                return false;
            }
        }

        return value.Length > 0;
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
