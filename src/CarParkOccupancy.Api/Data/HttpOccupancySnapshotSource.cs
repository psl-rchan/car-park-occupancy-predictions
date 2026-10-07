using System.Globalization;
using System.Net.Http.Headers;
using CarParkOccupancy.Api.Models;
using CarParkOccupancy.Api.Options;
using CarParkOccupancy.Api.Services.Prediction;
using Microsoft.Extensions.Options;

namespace CarParkOccupancy.Api.Data;

/// <summary>
/// Typed HTTP client for a third-party occupancy API. The provider has not published
/// a URL or contract yet, so the base URL, optional auth header, path, and JSON names
/// are configuration. Secrets stay in environment variables or user secrets.
/// </summary>
public sealed class HttpOccupancySnapshotSource : IOccupancySnapshotSource
{
    public const string CarParkCodeParameter = "carParkCode";

    public const string CountingCategoryParameter = "countingCategory";

    public const string FromParameter = "from";

    public const string ToParameter = "to";

    private readonly HttpClient _http;
    private readonly CarParkDataOptions _options;
    private readonly TimeZoneInfo _timeZone;
    private readonly ILogger<HttpOccupancySnapshotSource> _logger;

    public HttpOccupancySnapshotSource(
        HttpClient http,
        IOptions<CarParkDataOptions> options,
        IOptions<PredictionOptions> predictionOptions,
        ILogger<HttpOccupancySnapshotSource> logger)
    {
        _http = http;
        _options = options.Value;
        _timeZone = TimeZoneResolver.Resolve(predictionOptions.Value.Timezone);
        _logger = logger;
        _http.Timeout = TimeSpan.FromSeconds(Math.Clamp(_options.Http.TimeoutSeconds, 1, 600));
    }

    public async Task<IReadOnlyList<string>> GetCarParkCodesAsync(CancellationToken cancellationToken)
    {
        var (from, to) = LookbackWindow(DateTimeOffset.UtcNow);
        var snapshots = await FetchAsync(null, null, from, to, cancellationToken);
        return snapshots
            .Where(snapshot => snapshot.SnapshotTime >= from && snapshot.SnapshotTime <= to)
            .Select(snapshot => snapshot.CarParkCode)
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<bool> ExistsAsync(string carParkCode, CancellationToken cancellationToken)
    {
        var (from, to) = LookbackWindow(DateTimeOffset.UtcNow);
        var snapshots = await GetSnapshotsAsync(carParkCode, null, from, to, cancellationToken);
        return snapshots.Count > 0;
    }

    public async Task<IReadOnlyList<OccupancySnapshot>> GetSnapshotsAsync(
        string carParkCode,
        string? countingCategory,
        DateTimeOffset fromInclusive,
        DateTimeOffset toInclusive,
        CancellationToken cancellationToken)
    {
        var code = carParkCode.Trim();
        var category = string.IsNullOrWhiteSpace(countingCategory) ? null : countingCategory.Trim();
        var snapshots = await FetchAsync(code, category, fromInclusive, toInclusive, cancellationToken);
        return snapshots
            .Where(snapshot => snapshot.CarParkCode.Equals(code, StringComparison.OrdinalIgnoreCase))
            .Where(snapshot => category is null
                || string.Equals(snapshot.CountingCategory, category, StringComparison.OrdinalIgnoreCase))
            .Where(snapshot => snapshot.SnapshotTime >= fromInclusive && snapshot.SnapshotTime <= toInclusive)
            .ToArray();
    }

    private (DateTimeOffset From, DateTimeOffset To) LookbackWindow(DateTimeOffset to)
    {
        var days = Math.Max(1, _options.HistoryLookbackDays);
        return (to.AddDays(-days), to.AddDays(1));
    }

    private async Task<IReadOnlyList<OccupancySnapshot>> FetchAsync(
        string? carParkCode,
        string? countingCategory,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken cancellationToken)
    {
        var requestUri = BuildRequestUri(carParkCode, countingCategory, from, to);
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        AddAuthHeader(request);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning(ex, "Occupancy HTTP API timed out.");
            throw new CarParkDataUnavailableException("The occupancy HTTP API timed out.", ex);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Occupancy HTTP API could not be reached.");
            throw new CarParkDataUnavailableException("The occupancy HTTP API could not be reached.", ex);
        }

        using (response)
        {
            var status = (int)response.StatusCode;
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Occupancy HTTP API returned HTTP {StatusCode}.", status);
                if (status is 502 or 503 or 504)
                {
                    throw new CarParkDataUnavailableException(
                        $"The occupancy HTTP API is unavailable (HTTP {status}).");
                }

                throw new CarParkDataUnavailableException(
                    $"The occupancy HTTP API returned HTTP {status}.",
                    CarParkDataUnavailableException.BadGatewayStatus);
            }

            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            var snapshots = OccupancySnapshotJson.Read(
                payload,
                _options.Http.Json,
                _options.SnapshotTimeIsUtc,
                _timeZone);

            _logger.LogDebug("Occupancy HTTP API returned {Count} snapshots.", snapshots.Count);
            return snapshots;
        }
    }

    private Uri BuildRequestUri(
        string? carParkCode,
        string? countingCategory,
        DateTimeOffset? from,
        DateTimeOffset? to)
    {
        var baseUrl = _options.Http.BaseUrl?.Trim() ?? string.Empty;
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttps && baseUri.Scheme != Uri.UriSchemeHttp))
        {
            throw new CarParkDataUnavailableException(
                "CarParkData:Http:BaseUrl is not configured. The third-party occupancy API URL is not set yet.");
        }

        var path = (_options.Http.SnapshotsPath ?? string.Empty).Trim().TrimStart('/');
        var root = baseUri.AbsoluteUri.EndsWith('/') ? baseUri : new Uri(baseUri.AbsoluteUri + "/");
        var combined = new Uri(root, path);
        var builder = new UriBuilder(combined);
        var pairs = new List<string>(4);
        AddQuery(pairs, CarParkCodeParameter, carParkCode);
        AddQuery(pairs, CountingCategoryParameter, countingCategory);
        AddQuery(pairs, FromParameter, FormatInstant(from));
        AddQuery(pairs, ToParameter, FormatInstant(to));
        builder.Query = string.Join("&", pairs);
        return builder.Uri;
    }

    private void AddAuthHeader(HttpRequestMessage request)
    {
        var name = _options.Http.AuthHeaderName?.Trim() ?? string.Empty;
        var value = _options.Http.AuthHeaderValue ?? string.Empty;
        if (name.Length == 0 || value.Length == 0)
        {
            return;
        }

        if (!IsToken(name) || value.Contains('\r') || value.Contains('\n'))
        {
            throw new CarParkDataUnavailableException(
                "CarParkData:Http:AuthHeaderName or AuthHeaderValue is not a valid HTTP header.");
        }

        request.Headers.TryAddWithoutValidation(name, value);
    }

    private static void AddQuery(List<string> pairs, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        pairs.Add(Uri.EscapeDataString(name) + "=" + Uri.EscapeDataString(value.Trim()));
    }

    private static string? FormatInstant(DateTimeOffset? instant) =>
        instant?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static bool IsToken(string value)
    {
        if (value.Length == 0)
        {
            return false;
        }

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

        return true;
    }
}
