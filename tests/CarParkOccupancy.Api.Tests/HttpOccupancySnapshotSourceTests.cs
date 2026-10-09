using System.Net;
using System.Text;
using System.Text.Json;
using CarParkOccupancy.Api.Data;
using CarParkOccupancy.Api.Options;
using CarParkOccupancy.Api.Services.Prediction;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;

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
        Assert.Equal("http://localhost:5095/api/carparks/CP001/occupancy", request.Uri.GetLeftPart(UriPartial.Path));
        Assert.Equal(
            $"?from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}&page=1&pageSize=1000&sampleEverySeconds=300&category=PrivateCar",
            request.Uri.Query);
        Assert.DoesNotContain("carParkCode=", request.Uri.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("countingCategory=", request.Uri.Query, StringComparison.Ordinal);
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
              "items": [
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
              { "carParkCode": "CP002", "carparkNumber": 2 },
              { "CarParkCode": "cp001", "carparkNumber": 1 },
              { "CARPARKCODE": "CP001", "carparkNumber": 1 }
            ]
            """;

        using var fixture = Fixture.Json(json);
        var codes = await fixture.Source.GetCarParkCodesAsync(CancellationToken.None);

        Assert.Equal(["cp001", "CP002"], codes);
        Assert.Equal("http://localhost:5095/api/carparks", Assert.Single(fixture.Handler.Captured).Uri.AbsoluteUri);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Exists_uses_latest_snapshots(bool exists)
    {
        var json = exists
            ? """[{"carParkCode":"CP001","snapshotTime":"2026-10-07T01:00:00Z","capacity":100,"occupied":37}]"""
            : "[]";
        using var fixture = Fixture.Json(json);

        Assert.Equal(exists, await fixture.Source.ExistsAsync("CP001", CancellationToken.None));
        Assert.Equal("http://localhost:5095/api/carparks/CP001/occupancy/latest",
            Assert.Single(fixture.Handler.Captured).Uri.AbsoluteUri);
    }

    [Fact]
    public async Task Clamps_history_to_end_of_yesterday_utc_and_filters_rows()
    {
        var today = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero);
        var yesterday = today.AddMilliseconds(-1);
        var json = JsonSerializer.Serialize(new[]
        {
            new { carParkCode = "CP001", snapshotTime = yesterday, countingCategory = "PrivateCar", capacity = 100, occupied = 37 },
            new { carParkCode = "CP001", snapshotTime = today, countingCategory = "PrivateCar", capacity = 100, occupied = 99 },
            new { carParkCode = "CP002", snapshotTime = yesterday, countingCategory = "PrivateCar", capacity = 100, occupied = 99 },
            new { carParkCode = "CP001", snapshotTime = yesterday, countingCategory = "Lorry", capacity = 100, occupied = 99 },
            new { carParkCode = "CP001", snapshotTime = today.AddDays(-3), countingCategory = "PrivateCar", capacity = 100, occupied = 99 }
        });
        using var fixture = Fixture.Json(json);

        var snapshots = await fixture.Source.GetSnapshotsAsync("CP001", "PrivateCar",
            today.AddDays(-2).ToOffset(TimeSpan.FromHours(8)), today.ToOffset(TimeSpan.FromHours(8)), CancellationToken.None);

        Assert.Equal(yesterday, Assert.Single(snapshots).SnapshotTime);
        Assert.Contains($"to={Uri.EscapeDataString(yesterday.ToString("O"))}",
            Assert.Single(fixture.Handler.Captured).Uri.Query, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Today_or_future_window_does_not_call_api(int days)
    {
        var from = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(days);
        using var fixture = Fixture.Json("[]");

        Assert.Empty(await fixture.Source.GetSnapshotsAsync("CP001", null, from, from.AddHours(6), CancellationToken.None));
        Assert.Empty(fixture.Handler.Captured);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Merges_paged_items_until_short_page(bool totalItemCount)
    {
        var options = Fixture.DefaultOptions();
        options.Http.PageSize = 2;
        using var fixture = Fixture.Pages(
        [
            $$"""{"page":1,"pageSize":2,"itemCount":{{(totalItemCount ? 3 : 2)}},"items":[{"carParkCode":"CP001","snapshotTime":"2026-10-06T01:00:00Z","capacity":100,"occupied":10},{"carParkCode":"CP001","snapshotTime":"2026-10-06T02:00:00Z","capacity":100,"occupied":20}]}""",
            $$"""{"page":2,"pageSize":2,"itemCount":{{(totalItemCount ? 3 : 1)}},"items":[{"carParkCode":"CP001","snapshotTime":"2026-10-06T03:00:00Z","capacity":100,"occupied":30}]}"""
        ], options);

        var snapshots = await fixture.Source.GetSnapshotsAsync("CP001", null,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Equal([10, 20, 30], snapshots.Select(snapshot => snapshot.Occupied));
        Assert.Equal(2, fixture.Handler.Captured.Count);
        Assert.Contains("page=1&pageSize=2&sampleEverySeconds=300", fixture.Handler.Captured[0].Uri.Query);
        Assert.Contains("page=2&pageSize=2&sampleEverySeconds=300", fixture.Handler.Captured[1].Uri.Query);
        Assert.DoesNotContain("category=", fixture.Handler.Captured[0].Uri.Query);
    }

    [Fact]
    public async Task Stops_at_max_pages()
    {
        var options = Fixture.DefaultOptions();
        options.Http.PageSize = 1;
        options.Http.MaxPages = 2;
        using var fixture = Fixture.Json(
            """{"itemCount":1,"items":[{"carParkCode":"CP001","snapshotTime":"2026-10-06T01:00:00Z","capacity":100,"occupied":10}]}""", options);

        Assert.Equal(2, (await fixture.Source.GetSnapshotsAsync("CP001", null,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UtcNow, CancellationToken.None)).Count);
        Assert.Equal(2, fixture.Handler.Captured.Count);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[{}]")]
    [InlineData("{not-json")]
    public async Task Invalid_car_park_list_is_502(string json)
    {
        using var fixture = Fixture.Json(json);
        var error = await Assert.ThrowsAsync<CarParkDataUnavailableException>(() =>
            fixture.Source.GetCarParkCodesAsync(CancellationToken.None));
        Assert.Equal(CarParkDataUnavailableException.BadGatewayStatus, error.StatusCode);
    }

    [Theory]
    [InlineData("\"invalid\"")]
    [InlineData("-1")]
    [InlineData("null")]
    public async Task Invalid_page_item_count_is_502(string itemCount)
    {
        using var fixture = Fixture.Json($$"""{"itemCount":{{itemCount}},"items":[]}""");
        var error = await Assert.ThrowsAsync<CarParkDataUnavailableException>(() =>
            fixture.Source.GetSnapshotsAsync("CP001", null, DateTimeOffset.UnixEpoch, DateTimeOffset.UtcNow, CancellationToken.None));
        Assert.Equal(CarParkDataUnavailableException.BadGatewayStatus, error.StatusCode);
    }

    [Fact]
    public async Task Escapes_code_and_configurable_category_query_parameter()
    {
        var options = Fixture.DefaultOptions();
        options.Http.CategoryQueryParameter = "vehicleType";
        using var fixture = Fixture.Json("[]", options);

        await fixture.Source.GetSnapshotsAsync("CP 001&x", "Private Car&Lorry",
            DateTimeOffset.UnixEpoch, DateTimeOffset.UtcNow, CancellationToken.None);

        var uri = Assert.Single(fixture.Handler.Captured).Uri;
        Assert.Equal("/api/carparks/CP%20001%26x/occupancy", uri.AbsolutePath);
        Assert.Contains("vehicleType=Private%20Car%26Lorry", uri.Query);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/api/carparks")]
    [InlineData("//remote.example/api")]
    [InlineData("https://remote.example/api")]
    [InlineData("../api/carparks")]
    [InlineData("api\\carparks")]
    [InlineData("api/carparks?x=1")]
    [InlineData("api/carparks#x")]
    [InlineData("api/\ncarparks")]
    public void Options_reject_non_relative_paths(string path)
    {
        Action<HttpOccupancySourceOptions>[] setters =
        [
            options => options.CarParksPath = path,
            options => options.HistoryPathTemplate = path,
            options => options.LatestPathTemplate = path,
            options => options.LatestAllPath = path
        ];
        foreach (var setPath in setters)
        {
            var options = Fixture.DefaultOptions();
            setPath(options.Http);
            var result = Validator().Validate(null, options);
            Assert.False(result.Succeeded);
            Assert.Contains("relative path", result.FailureMessage);
        }
    }

    [Fact]
    public void Options_require_code_templates_and_valid_paging()
    {
        Assert.True(Validator().Validate(null, Fixture.DefaultOptions()).Succeeded);
        Action<HttpOccupancySourceOptions>[] invalidSettings =
        [
            options => options.HistoryPathTemplate = "api/carparks/occupancy",
            options => options.LatestPathTemplate = "api/carparks/latest",
            options => options.PageSize = 0,
            options => options.PageSize = 1001,
            options => options.SampleEverySeconds = 0,
            options => options.MaxPages = 0,
            options => options.CategoryQueryParameter = ""
        ];
        foreach (var setInvalid in invalidSettings)
        {
            var options = Fixture.DefaultOptions();
            setInvalid(options.Http);
            Assert.False(Validator().Validate(null, options).Succeeded);
        }
    }

    private static CarParkDataOptionsValidator Validator() =>
        new(new ConfigurationBuilder().Build(), new HostingEnvironment { EnvironmentName = Environments.Development });

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

        public static Fixture Pages(string[] pages, CarParkDataOptions options) =>
            new(StubHandler.Pages(pages), options);

        public static CarParkDataOptions DefaultOptions() => new()
        {
            HistoryLookbackDays = 400,
            SnapshotTimeIsUtc = true,
            Http = new HttpOccupancySourceOptions
            {
                BaseUrl = "http://localhost:5095",
                CarParksPath = "api/carparks",
                HistoryPathTemplate = "api/carparks/{code}/occupancy",
                LatestPathTemplate = "api/carparks/{code}/occupancy/latest",
                LatestAllPath = "api/occupancy/latest",
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

        public static StubHandler Pages(string[] pages)
        {
            var page = 0;
            return new((_, _) => Task.FromResult(JsonResponse(pages[page++])));
        }

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
