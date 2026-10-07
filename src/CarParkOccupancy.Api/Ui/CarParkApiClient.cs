using System.Net.Http.Json;
using System.Text.Json;
using CarParkOccupancy.Api.Models;
using CarParkOccupancy.Api.Services.Prediction;

namespace CarParkOccupancy.Api.Ui;

public sealed class CarParkApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly IHttpContextAccessor _httpContext;

    public CarParkApiClient(HttpClient http, IHttpContextAccessor httpContext)
    {
        _http = http;
        _httpContext = httpContext;
    }

    public async Task<IReadOnlyList<string>> GetCarParkCodesAsync(CancellationToken cancellationToken)
    {
        var list = await SendAsync<CarParkListResponse>(HttpMethod.Get, "api/carparks", null, cancellationToken);
        return list.CarParks;
    }

    public Task<CarParkPredictionResult> GetPredictionAsync(string code, CancellationToken cancellationToken)
    {
        var path = "api/carparks/" + Uri.EscapeDataString(code) + "/predictions";
        return SendAsync<CarParkPredictionResult>(HttpMethod.Get, path, null, cancellationToken);
    }

    public async Task<BatchPredictionResponse> PredictManyAsync(
        IReadOnlyList<string> codes,
        CancellationToken cancellationToken)
    {
        var results = new List<BatchPredictionItem>();
        DateTimeOffset? generatedAt = null;

        for (var offset = 0; offset < codes.Count; offset += HorizonParser.MaxBatchSize)
        {
            var slice = codes.Skip(offset).Take(HorizonParser.MaxBatchSize).ToList();
            var batch = await SendAsync<BatchPredictionResponse>(
                HttpMethod.Post,
                "api/carparks/predictions",
                new BatchPredictionRequest
                {
                    CarParkCodes = slice,
                    Horizons = HorizonParser.DefaultHorizons.ToList()
                },
                cancellationToken);

            generatedAt ??= batch.GeneratedAt;
            results.AddRange(batch.Results);
        }

        return new BatchPredictionResponse
        {
            GeneratedAt = generatedAt ?? DateTimeOffset.UtcNow,
            Results = results
        };
    }

    private async Task<T> SendAsync<T>(
        HttpMethod method,
        string relativeUrl,
        object? body,
        CancellationToken cancellationToken)
    {
        EnsureBaseAddress();

        using var request = new HttpRequestMessage(method, relativeUrl);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new CarParkApiException((int)response.StatusCode, ReadProblemMessage(payload, (int)response.StatusCode));
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<T>(payload, JsonOptions);
            if (parsed is null)
            {
                throw new CarParkApiException(
                    StatusCodes.Status502BadGateway,
                    "The prediction API returned an empty response.");
            }

            return parsed;
        }
        catch (JsonException)
        {
            throw new CarParkApiException(
                StatusCodes.Status502BadGateway,
                "The prediction API returned a response the page could not read.");
        }
    }

    private void EnsureBaseAddress()
    {
        if (_http.BaseAddress is not null)
        {
            return;
        }

        var request = _httpContext.HttpContext?.Request
            ?? throw new CarParkApiException(
                StatusCodes.Status500InternalServerError,
                "The page could not determine the prediction API address.");

        var prefix = request.PathBase.HasValue ? request.PathBase.Value : string.Empty;
        _http.BaseAddress = new Uri($"{request.Scheme}://{request.Host}{prefix}/");
    }

    private static string ReadProblemMessage(string payload, int statusCode)
    {
        if (!string.IsNullOrWhiteSpace(payload))
        {
            try
            {
                var problem = JsonSerializer.Deserialize<ProblemPayload>(payload, JsonOptions);
                if (!string.IsNullOrWhiteSpace(problem?.Title))
                {
                    return problem.Title;
                }

                if (!string.IsNullOrWhiteSpace(problem?.Detail))
                {
                    return problem.Detail;
                }
            }
            catch (JsonException)
            {
                // The body was not problem+json. Fall through to the status line.
            }
        }

        return $"The prediction API returned HTTP {statusCode}.";
    }

    private sealed class ProblemPayload
    {
        public string? Title { get; set; }

        public string? Detail { get; set; }
    }
}
