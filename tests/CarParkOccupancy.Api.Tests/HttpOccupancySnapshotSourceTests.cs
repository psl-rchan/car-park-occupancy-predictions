using System.Net;
using System.Text;
using CarParkOccupancy.Api.Data;
using CarParkOccupancy.Api.Options;
using CarParkOccupancy.Api.Services.Prediction;
using Microsoft.Extensions.Logging.Abstractions;

namespace CarParkOccupancy.Api.Tests;

public sealed class HttpOccupancySnapshotSourceTests
{
    private const string Secret = "super-secret-value";

    [Fact]
    public async Task Maps_stakeholder_json_fields_and_sends_the_query()
    {
        const string json = """
            [
              {
                "CarParkCode": "CP001",
                "SnapshotTime": "2026-10-07T01:55:00+08:00",
                "CountingCategory": "PrivateCar",
                "Capacity": 200,
                "Occupied": 140
              },
              {
                "CarParkCode": "CP002",
                "SnapshotTime": "2026-10-07T01:55:00Z",
                "CountingCategory": null,
                "Capacity": "50",
                "Occupied": 10
              }
            ]
            """;

        using var fixture = Fixture.Json(json, authHeader: true);
        var from = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 10, 8, 0, 0, 0, TimeSpan.Zero);
        var snapshots = await fixture.Source.GetSnapshotsAsync("CP001", "PrivateCar", from, to, CancellationToken.None);

        var snapshot = Assert.Single(snapshots);
        Assert.Equal("CP001", snapshot.CarParkCode);
        Assert.Equal(new DateTimeOffset(2026, 10, 7, 1, 55, 0, TimeSpan.FromHours(8)), snapshot.SnapshotTime);
        Assert.Equal("PrivateCar", snapshot.CountingCategory);
        Assert.Equal(200, snapshot.Capacity);
        Assert.Equal(140, snapshot.Occupied);

        var other = Assert.Single(await fixture.Source.GetSnapshotsAsync("CP002", null, from, to, CancellationToken.None));
        Assert.Equal(50, other.Capacity);
        Assert.Equal(10, other.Occupied);
        Assert.Null(other.CountingCategory);

        var request = fixture.Handler.Captured[0];
        Assert.StartsWith("https://occupancy.example/v1/occupancy-snapshots?", request.Uri.AbsoluteUri, StringComparison.Ordinal);
        Assert.Contains("carParkCode=CP001", request.Uri.Query, StringComparison.Ordinal);
        Assert.Contains("countingCategory=PrivateCar", request.Uri.Query, StringComparison.Ordinal);
        Assert.Contains("from=", request.Uri.Query, StringComparison.Ordinal);
        Assert.Contains("to=", request.Uri.Query, StringComparison.Ordinal);
        Assert.Equal(Secret, request.AuthHeader);
        Assert.DoesNotContain(Secret, request.Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Maps_camel_case_collection_custom_names_and_local_snapshot_time()
    {
        const string json = """
            {
              "rows": [
                {
                  "park": "CP010",
                  "taken": "2026-10-06T10:00:00",
                  "kind": "Lorry",
                  "spaces": 80,
                  "used": 20
                }
              ]
            }
            """;

        var options = Fixture.DefaultOptions();
        options.SnapshotTimeIsUtc = false;
        options.Http.Json = new HttpSnapshotJsonNames
        {
            Collection = "rows",
            CarParkCode = "park",
            SnapshotTime = "taken",
            CountingCategory = "kind",
            Capacity = "spaces",
            Occupied = "used"
        };

        using var fixture = Fixture.Json(json, options);
        var snapshots = await fixture.Source.GetSnapshotsAsync(
            "cp010",
            "lorry",
            new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 8, 0, 0, 0, TimeSpan.Zero),
            CancellationToken.None);

        var snapshot = Assert.Single(snapshots);
        Assert.Equal("CP010", snapshot.CarParkCode);
        Assert.Equal(TimeSpan.FromHours(8), snapshot.SnapshotTime.Offset);
        Assert.Equal(new DateTimeOffset(2026, 10, 6, 10, 0, 0, TimeSpan.FromHours(8)), snapshot.SnapshotTime);
        Assert.Equal("Lorry", snapshot.CountingCategory);
        Assert.Equal(80, snapshot.Capacity);
        Assert.Equal(20, snapshot.Occupied);
        Assert.Null(Assert.Single(fixture.Handler.Captured).AuthHeader);
    }

    [Fact]
    public async Task Sums_categories_that_share_a_timestamp_for_prediction_history()
    {
        const string json = """
            {
              "snapshots": [
                {
                  "carParkCode": "CP001",
                  "snapshotTime": "2026-10-07T01:00:00Z",
                  "countingCategory": "PrivateCar",
                  "capacity": 200,
                  "occupied": 140
                },
                {
                  "carParkCode": "CP001",
                  "snapshotTime": "2026-10-07T01:00:00+00:00",
                  "countingCategory": "Lorry",
                  "capacity": 50,
                  "occupied": 10
                }
              ]
            }
            """;

        using var fixture = Fixture.Json(json);
        var store = new OccupancySnapshotReadStore(
            fixture.Source,
            Microsoft.Extensions.Options.Options.Create(fixture.Options),
            NullLogger<OccupancySnapshotReadStore>.Instance);
        var history = await store.GetHistoryAsync(
            "CP001",
            null,
            new DateTimeOffset(2026, 10, 7, 2, 0, 0, TimeSpan.Zero),
            CancellationToken.None);

        var observation = Assert.Single(history);
        Assert.Equal(250, observation.Capacity);
        Assert.Equal(150, observation.Occupied);
        Assert.Equal(60, observation.OccupancyPercent);
    }

    [Fact]
    public async Task Lists_distinct_car_park_codes()
    {
        const string json = """
            [
              {
                "CarParkCode": "CP002",
                "SnapshotTime": "2026-10-07T01:00:00Z",
                "CountingCategory": "PrivateCar",
                "Capacity": 10,
                "Occupied": 1
              },
              {
                "CarParkCode": "cp001",
                "SnapshotTime": "2026-10-07T01:00:00Z",
                "CountingCategory": "PrivateCar",
                "Capacity": 10,
                "Occupied": 1
              },
              {
                "CarParkCode": "CP001",
                "SnapshotTime": "2026-10-07T02:00:00Z",
                "CountingCategory": "PrivateCar",
                "Capacity": 10,
                "Occupied": 1
              }
            ]
            """;

        using var fixture = Fixture.Json(json);
        var codes = await fixture.Source.GetCarParkCodesAsync(CancellationToken.None);

        Assert.Equal(["cp001", "CP002"], codes);
    }

    [Fact]
    public async Task Missing_base_url_is_503_and_does_not_call_the_remote_api()
    {
        var options = Fixture.DefaultOptions();
        options.Http.BaseUrl = "";
        options.Http.AuthHeaderName = "X-Api-Key";
        options.Http.AuthHeaderValue = Secret;
        using var fixture = Fixture.Json("[]", options);

        var exception = await Assert.ThrowsAsync<CarParkDataUnavailableException>(() =>
            fixture.Source.GetCarParkCodesAsync(CancellationToken.None));

        Assert.Equal(CarParkDataUnavailableException.ServiceUnavailableStatus, exception.StatusCode);
        Assert.Contains("BaseUrl", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, exception.Message, StringComparison.Ordinal);
        Assert.Empty(fixture.Handler.Captured);
    }

    [Fact]
    public async Task Remote_5xx_and_invalid_json_use_503_or_502()
    {
        using var unavailable = Fixture.Status(HttpStatusCode.ServiceUnavailable);
        var unavailableError = await Assert.ThrowsAsync<CarParkDataUnavailableException>(() =>
            unavailable.Source.GetCarParkCodesAsync(CancellationToken.None));
        Assert.Equal(CarParkDataUnavailableException.ServiceUnavailableStatus, unavailableError.StatusCode);
        Assert.Contains("HTTP 503", unavailableError.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, unavailableError.Message, StringComparison.Ordinal);

        using var badGateway = Fixture.Status(HttpStatusCode.InternalServerError);
        var badGatewayError = await Assert.ThrowsAsync<CarParkDataUnavailableException>(() =>
            badGateway.Source.GetCarParkCodesAsync(CancellationToken.None));
        Assert.Equal(CarParkDataUnavailableException.BadGatewayStatus, badGatewayError.StatusCode);
        Assert.Contains("HTTP 500", badGatewayError.Message, StringComparison.Ordinal);

        using var invalid = Fixture.Json("{not-json");
        var invalidError = await Assert.ThrowsAsync<CarParkDataUnavailableException>(() =>
            invalid.Source.GetSnapshotsAsync("CP001", null, DateTimeOffset.UnixEpoch, DateTimeOffset.UtcNow, CancellationToken.None));
        Assert.Equal(CarParkDataUnavailableException.BadGatewayStatus, invalidError.StatusCode);
        Assert.Contains("snapshot contract", invalidError.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Connection_failure_and_timeout_are_503()
    {
        using var offline = Fixture.Throw(new HttpRequestException("connection refused"));
        var offlineError = await Assert.ThrowsAsync<CarParkDataUnavailableException>(() =>
            offline.Source.ExistsAsync("CP001", CancellationToken.None));
        Assert.Equal(CarParkDataUnavailableException.ServiceUnavailableStatus, offlineError.StatusCode);
        Assert.Contains("could not be reached", offlineError.Message, StringComparison.Ordinal);

        using var timeout = Fixture.Delay(TimeSpan.FromSeconds(5));
        var timeoutError = await Assert.ThrowsAsync<CarParkDataUnavailableException>(() =>
            timeout.Source.GetCarParkCodesAsync(CancellationToken.None));
        Assert.Equal(CarParkDataUnavailableException.ServiceUnavailableStatus, timeoutError.StatusCode);
        Assert.Contains("timed out", timeoutError.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Source_selection_keeps_the_sample_path_and_accepts_http()
    {
        Assert.True(OccupancySnapshotSourceSelector.TryResolve(
            "Sql",
            useSampleSnapshots: true,
            connectionString: "",
            isDevelopment: true,
            out var sample,
            out var sampleError));
        Assert.Null(sampleError);
        Assert.Equal(OccupancySnapshotSourceKind.Sample, sample);

        Assert.True(OccupancySnapshotSourceSelector.TryResolve(
            "Sql",
            useSampleSnapshots: true,
            connectionString: "Server=db;",
            isDevelopment: true,
            out var sqlWins,
            out _));
        Assert.Equal(OccupancySnapshotSourceKind.Sql, sqlWins);

        Assert.True(OccupancySnapshotSourceSelector.TryResolve(
            "Http",
            useSampleSnapshots: true,
            connectionString: "",
            isDevelopment: true,
            out var http,
            out _));
        Assert.Equal(OccupancySnapshotSourceKind.Http, http);

        Assert.True(OccupancySnapshotSourceSelector.TryResolve(
            "Sample",
            useSampleSnapshots: false,
            connectionString: "Server=db;",
            isDevelopment: false,
            out var explicitSample,
            out _));
        Assert.Equal(OccupancySnapshotSourceKind.Sample, explicitSample);

        Assert.False(OccupancySnapshotSourceSelector.TryResolve(
            "Oracle",
            useSampleSnapshots: false,
            connectionString: "",
            isDevelopment: false,
            out _,
            out var error));
        Assert.Equal("CarParkData:Source must be Sql, Http, or Sample.", error);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly HttpClient _client;

        private Fixture(StubHandler handler, CarParkDataOptions options)
        {
            Handler = handler;
            Options = options;
            _client = new HttpClient(handler, disposeHandler: false);
            Source = new HttpOccupancySnapshotSource(
                _client,
                Microsoft.Extensions.Options.Options.Create(options),
                Microsoft.Extensions.Options.Options.Create(new PredictionOptions()),
                NullLogger<HttpOccupancySnapshotSource>.Instance);
        }

        public HttpOccupancySnapshotSource Source { get; }

        public StubHandler Handler { get; }

        public CarParkDataOptions Options { get; }

        public static Fixture Json(string json, CarParkDataOptions? options = null, bool authHeader = false)
        {
            options ??= DefaultOptions();
            if (authHeader)
            {
                options.Http.AuthHeaderName = "X-Api-Key";
                options.Http.AuthHeaderValue = Secret;
            }

            return new Fixture(StubHandler.Json(json), options);
        }

        public static Fixture Status(HttpStatusCode status)
        {
            var options = DefaultOptions();
            options.Http.AuthHeaderName = "Authorization";
            options.Http.AuthHeaderValue = Secret;
            return new Fixture(StubHandler.Status(status), options);
        }

        public static Fixture Throw(Exception exception)
        {
            return new Fixture(StubHandler.Throw(exception), DefaultOptions());
        }

        public static Fixture Delay(TimeSpan delay)
        {
            var options = DefaultOptions();
            options.Http.TimeoutSeconds = 1;
            return new Fixture(StubHandler.Delay(delay), options);
        }

        public static CarParkDataOptions DefaultOptions() => new()
        {
            HistoryLookbackDays = 400,
            SnapshotTimeIsUtc = false,
            Http = new HttpOccupancySourceOptions
            {
                BaseUrl = "https://occupancy.example/v1",
                SnapshotsPath = "occupancy-snapshots",
                TimeoutSeconds = 5
            }
        };

        public void Dispose() => _client.Dispose();
    }

    private sealed record CapturedRequest(Uri Uri, string? AuthHeader);

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond;

        private StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        {
            _respond = respond;
        }

        public List<CapturedRequest> Captured { get; } = [];

        public static StubHandler Json(string json) => new((_, _) => Task.FromResult(JsonResponse(json)));

        public static StubHandler Status(HttpStatusCode status) =>
            new((_, _) => Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent("upstream failure", Encoding.UTF8, "text/plain")
            }));

        public static StubHandler Throw(Exception exception) =>
            new((_, _) => Task.FromException<HttpResponseMessage>(exception));

        public static StubHandler Delay(TimeSpan delay) => new(async (_, cancellationToken) =>
        {
            await Task.Delay(delay, cancellationToken);
            return JsonResponse("[]");
        });

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string? auth = null;
            if (request.Headers.TryGetValues("X-Api-Key", out var apiKey))
            {
                auth = string.Join(',', apiKey);
            }
            else if (request.Headers.TryGetValues("Authorization", out var authorization))
            {
                auth = string.Join(',', authorization);
            }

            Captured.Add(new CapturedRequest(request.RequestUri!, auth));
            return await _respond(request, cancellationToken);
        }

        private static HttpResponseMessage JsonResponse(string json) =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
    }
}
