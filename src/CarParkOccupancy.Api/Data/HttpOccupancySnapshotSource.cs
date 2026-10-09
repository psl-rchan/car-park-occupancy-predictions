using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using CarParkOccupancy.Api.Models;
using CarParkOccupancy.Api.Options;
using CarParkOccupancy.Api.Services.Prediction;
using Microsoft.Extensions.Options;

namespace CarParkOccupancy.Api.Data;

/// <summary>
/// Typed HTTP client for car-park-occupancy-data-api. History is restricted to dates before today UTC.
/// </summary>
public sealed class HttpOccupancySnapshotSource : IOccupancySnapshotSource
{
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
        var payload = await FetchAsync(BuildRequestUri(_options.Http.CarParksPath), cancellationToken);
        using var document = ParseDocument(payload);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw BadJson("The occupancy data API car park list must be an array.");
        }

        var codes = new List<string>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (!OccupancySnapshotJson.TryFind(item, "carParkCode", out var value)
                || value.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(value.GetString()))
            {
                throw BadJson("The occupancy data API car park list is missing 'carParkCode'.");
            }

            codes.Add(value.GetString()!.Trim());
        }

        return codes
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<bool> ExistsAsync(string carParkCode, CancellationToken cancellationToken)
    {
        var path = PathForCode(_options.Http.LatestPathTemplate, carParkCode.Trim());
        var payload = await FetchAsync(BuildRequestUri(path), cancellationToken);
        var snapshots = ReadSnapshots(payload);
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
        var utcToday = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero);
        if (fromInclusive >= utcToday || fromInclusive > toInclusive)
        {
            return [];
        }

        var to = toInclusive >= utcToday ? utcToday.AddMilliseconds(-1) : toInclusive.ToUniversalTime();
        if (fromInclusive > to)
        {
            return [];
        }

        var snapshots = new List<OccupancySnapshot>();
        var path = PathForCode(_options.Http.HistoryPathTemplate, code);
        for (var page = 1; page <= _options.Http.MaxPages; page++)
        {
            var pairs = new List<string>();
            AddQuery(pairs, FromParameter, FormatInstant(fromInclusive));
            AddQuery(pairs, ToParameter, FormatInstant(to));
            AddQuery(pairs, "page", page.ToString(CultureInfo.InvariantCulture));
            AddQuery(pairs, "pageSize", _options.Http.PageSize.ToString(CultureInfo.InvariantCulture));
            AddQuery(pairs, "sampleEverySeconds", _options.Http.SampleEverySeconds.ToString(CultureInfo.InvariantCulture));
            AddQuery(pairs, _options.Http.CategoryQueryParameter, category);
            var payload = await FetchAsync(BuildRequestUri(path, pairs), cancellationToken);
            var items = ReadSnapshots(payload);
            snapshots.AddRange(items);

            using var document = ParseDocument(payload);
            var itemCount = items.Count;
            if (OccupancySnapshotJson.TryFind(document.RootElement, "itemCount", out var count))
            {
                if (count.ValueKind != JsonValueKind.Number || !count.TryGetInt32(out itemCount) || itemCount < 0)
                {
                    throw BadJson("The occupancy data API 'itemCount' must be a non-negative integer.");
                }
            }

            if (items.Count == 0
                || (itemCount != _options.Http.PageSize && itemCount < (long)page * _options.Http.PageSize))
            {
                break;
            }

            if (page == _options.Http.MaxPages)
            {
                _logger.LogWarning("Occupancy data API history reached MaxPages ({MaxPages}); history may be truncated.",
                    _options.Http.MaxPages);
                break;
            }
        }

        return snapshots
            .Where(snapshot => snapshot.CarParkCode.Equals(code, StringComparison.OrdinalIgnoreCase))
            .Where(snapshot => category is null
                || string.Equals(snapshot.CountingCategory, category, StringComparison.OrdinalIgnoreCase))
            .Where(snapshot => snapshot.SnapshotTime >= fromInclusive && snapshot.SnapshotTime <= to)
            .ToArray();
    }

    private IReadOnlyList<OccupancySnapshot> ReadSnapshots(string payload) =>
        OccupancySnapshotJson.Read(payload, _options.Http.Json, _options.SnapshotTimeIsUtc, _timeZone);

    private async Task<string> FetchAsync(
        Uri requestUri,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        AddAuthHeader(request);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
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

            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
    }

    private Uri BuildRequestUri(string path, List<string>? pairs = null)
    {
        var baseUrl = _options.Http.BaseUrl?.Trim() ?? string.Empty;
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttps && baseUri.Scheme != Uri.UriSchemeHttp))
        {
            throw new CarParkDataUnavailableException(
                "CarParkData:Http:BaseUrl must be configured with the occupancy data API URL (default http://localhost:5095).");
        }

        var root = baseUri.AbsoluteUri.EndsWith('/') ? baseUri : new Uri(baseUri.AbsoluteUri + "/");
        var combined = new Uri(root, path);
        var builder = new UriBuilder(combined);
        builder.Query = pairs is null ? string.Empty : string.Join("&", pairs);
        return builder.Uri;
    }

    private static string PathForCode(string template, string code) =>
        template.Replace("{code}", Uri.EscapeDataString(code), StringComparison.Ordinal);

    private static JsonDocument ParseDocument(string payload)
    {
        try
        {
            return JsonDocument.Parse(payload);
        }
        catch (JsonException)
        {
            throw BadJson("The occupancy data API returned invalid JSON.");
        }
    }

    private static CarParkDataUnavailableException BadJson(string message) =>
        new(message, CarParkDataUnavailableException.BadGatewayStatus);

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
