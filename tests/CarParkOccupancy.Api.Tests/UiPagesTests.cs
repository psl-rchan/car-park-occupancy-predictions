using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CarParkOccupancy.Api.Data;
using CarParkOccupancy.Api.Models;
using CarParkOccupancy.Api.Options;
using CarParkOccupancy.Api.Ui;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace CarParkOccupancy.Api.Tests;

[Collection("ApiHost")]
public sealed class UiPagesTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Home_explains_when_database_is_not_configured()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("data-testid=\"data-unavailable\"", html);
        Assert.Contains("ConnectionStrings:CarParkDb", html);
        Assert.Contains("停車場資料暫時讀唔到", html);
        Assert.Contains("Car park data is unavailable", html);
        Assert.DoesNotContain("data-testid=\"sample-banner\"", html);
        Assert.DoesNotContain("Password=", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Dashboard_and_detail_show_hours_1_through_6_from_the_api()
    {
        await using var factory = ApiFactory.WithStore(new FlatHistoryStore());
        using var client = factory.CreateClient();

        var apiResponse = await client.GetAsync("/api/carparks/CP001/predictions");
        apiResponse.EnsureSuccessStatusCode();
        var prediction = await apiResponse.Content.ReadFromJsonAsync<CarParkPredictionResult>(JsonOptions);
        Assert.NotNull(prediction);
        Assert.Equal(HistoricalBaselinePredictorName, prediction.Method);
        Assert.Equal([1, 2, 3, 4, 5, 6], prediction.Predictions.Select(item => item.HorizonHours).ToArray());
        Assert.All(prediction.Predictions, item => Assert.Equal(37.0, item.PredictedOccupancyPercent));

        var home = await client.GetAsync("/");
        home.EnsureSuccessStatusCode();
        var homeHtml = await home.Content.ReadAsStringAsync();
        Assert.Contains("data-testid=\"prediction-grid\"", homeHtml);
        Assert.Contains("data-testid=\"generated-at\"", homeHtml);
        Assert.Contains("data-testid=\"method\">" + prediction.Method, homeHtml);
        Assert.Contains("href=\"/carparks/CP001\"", homeHtml);
        Assert.Contains("href=\"/carparks/CP002\"", homeHtml);
        Assert.Contains("82.0%", homeHtml);
        Assert.Contains("data-testid=\"predicted-high-chart\"", homeHtml);
        Assert.Contains("data-testid=\"current-high-chart\"", homeHtml);
        Assert.Contains("data-testid=\"predicted-high-park\" data-code=\"CP002\" data-span=\"all\"", homeHtml);
        Assert.Contains("data-testid=\"current-high-park\" data-code=\"CP002\"", homeHtml);
        Assert.DoesNotContain("data-code=\"CP001\"", homeHtml);
        foreach (var item in prediction.Predictions)
        {
            Assert.Contains($"data-horizon=\"{item.HorizonHours}\"", homeHtml);
            Assert.Contains(OccupancyDisplay.FormatPercent(item.PredictedOccupancyPercent), homeHtml);
        }

        var detail = await client.GetAsync("/carparks/CP001");
        detail.EnsureSuccessStatusCode();
        var detailHtml = await detail.Content.ReadAsStringAsync();
        Assert.Contains("data-testid=\"occupancy-chart\"", detailHtml);
        Assert.Contains("data-testid=\"prediction-table\"", detailHtml);
        Assert.Contains("data-testid=\"generated-at\"", detailHtml);
        Assert.Contains("data-testid=\"method\">" + prediction.Method, detailHtml);
        Assert.Contains("HKT", detailHtml);
        foreach (var item in prediction.Predictions)
        {
            Assert.Contains($"data-horizon=\"{item.HorizonHours}\"", detailHtml);
            Assert.Contains(OccupancyDisplay.FormatPercent(item.PredictedOccupancyPercent), detailHtml);
        }
    }

    [Fact]
    public async Task Sample_snapshots_render_a_dashboard_without_sql()
    {
        await using var factory = ApiFactory.WithSampleSnapshots();
        using var client = factory.CreateClient();

        var apiResponse = await client.GetAsync("/api/carparks/CP01/predictions");
        apiResponse.EnsureSuccessStatusCode();
        var prediction = await apiResponse.Content.ReadFromJsonAsync<CarParkPredictionResult>(JsonOptions);
        Assert.NotNull(prediction);
        Assert.Equal(6, prediction.Predictions.Count);
        Assert.All(prediction.Predictions, item => Assert.NotNull(item.PredictedOccupancyPercent));

        var home = await client.GetAsync("/");
        home.EnsureSuccessStatusCode();
        var html = await home.Content.ReadAsStringAsync();
        Assert.Contains("data-testid=\"sample-banner\"", html);
        Assert.DoesNotContain("data-testid=\"data-unavailable\"", html);
        Assert.Contains("CP01", html);
        Assert.Contains("CP50", html);
        Assert.Contains(prediction.Method, html);
        foreach (var item in prediction.Predictions)
        {
            Assert.Contains(OccupancyDisplay.FormatPercent(item.PredictedOccupancyPercent), html);
            Assert.Contains($"+{item.HorizonHours}h", html);
        }
    }

    [Fact]
    public async Task Dashboard_charts_follow_sample_predictions_and_latest_snapshots()
    {
        await using var factory = ApiFactory.WithSampleSnapshots();
        using var client = factory.CreateClient();

        var codes = await client.GetFromJsonAsync<CarParkListResponse>("/api/carparks", JsonOptions);
        Assert.NotNull(codes);
        var batchResponse = await client.PostAsJsonAsync(
            "/api/carparks/predictions",
            new BatchPredictionRequest
            {
                CarParkCodes = codes.CarParks.ToList(),
                Horizons = [1, 2, 3, 4, 5, 6]
            },
            JsonOptions);
        batchResponse.EnsureSuccessStatusCode();
        var batch = await batchResponse.Content.ReadFromJsonAsync<BatchPredictionResponse>(JsonOptions);
        Assert.NotNull(batch);

        var inputs = batch.Results.Select(item => new ParkOccupancyInput
        {
            Code = item.CarParkCode,
            Found = item.Found && item.Prediction is not null,
            LatestOccupancyPercent = item.Prediction?.LatestOccupancyPercent,
            LatestSnapshotTime = item.Prediction?.LatestSnapshotTime,
            Predictions = item.Prediction?.Predictions ?? []
        }).ToArray();
        var predicted = HighOccupancyCharts.Predicted(inputs, [1, 2, 3, 4, 5, 6], 80);
        var current = HighOccupancyCharts.Current(inputs, 80);
        Assert.NotEmpty(predicted);
        Assert.NotEmpty(current);
        Assert.Contains(predicted, park => park.HighInEveryHorizon);

        var home = await client.GetAsync("/");
        home.EnsureSuccessStatusCode();
        var html = await home.Content.ReadAsStringAsync();
        Assert.Contains("data-testid=\"prediction-grid\"", html);
        Assert.Contains("data-testid=\"high-occupancy-legend\"", html);
        Assert.Contains("data-testid=\"high-occupancy-threshold\">80%", html);
        Assert.Contains("CarParkData:HighOccupancyThresholdPercent", html);
        Assert.Contains("預測高佔用", html);
        Assert.Contains("Predicted high occupancy", html);
        Assert.Contains("目前高佔用", html);
        Assert.Contains("Currently high occupancy", html);
        Assert.Contains("全部時段", html);
        Assert.Contains("All hours", html);
        Assert.Contains($"data-testid=\"predicted-high-count\">{predicted.Count}", html);
        Assert.Contains($"data-testid=\"current-high-count\">{current.Count}", html);
        foreach (var park in predicted)
        {
            Assert.Contains(
                $"data-testid=\"predicted-high-park\" data-code=\"{park.Code}\" data-span=\"{park.Span}\"",
                html);
        }

        foreach (var park in current)
        {
            Assert.Contains($"data-testid=\"current-high-park\" data-code=\"{park.Code}\"", html);
        }

        var quiet = inputs
            .Where(park => park.Found)
            .Select(park => park.Code)
            .Except(predicted.Select(park => park.Code), StringComparer.OrdinalIgnoreCase)
            .Except(current.Select(park => park.Code), StringComparer.OrdinalIgnoreCase)
            .First();
        Assert.DoesNotContain($"data-code=\"{quiet}\"", html);
        Assert.Contains("occ-high", html);
    }

    [Fact]
    public async Task Dashboard_uses_the_configured_high_occupancy_threshold()
    {
        await using var strict = ApiFactory.WithStore(new FlatHistoryStore(), highOccupancyThreshold: 90);
        using var strictClient = strict.CreateClient();
        var strictHtml = await strictClient.GetStringAsync("/");
        Assert.Contains("data-testid=\"high-occupancy-threshold\">90%", strictHtml);
        Assert.Contains("data-testid=\"predicted-high-empty\"", strictHtml);
        Assert.Contains("data-testid=\"current-high-empty\"", strictHtml);
        Assert.DoesNotContain("data-testid=\"predicted-high-park\"", strictHtml);
        Assert.DoesNotContain("data-testid=\"current-high-park\"", strictHtml);
        Assert.Contains("data-testid=\"prediction-grid\"", strictHtml);
        Assert.Contains("82.0%", strictHtml);
        Assert.DoesNotContain("occ-high", strictHtml);

        await using var loose = ApiFactory.WithStore(new FlatHistoryStore(), highOccupancyThreshold: 30);
        using var looseClient = loose.CreateClient();
        var looseHtml = await looseClient.GetStringAsync("/");
        Assert.Contains("data-testid=\"high-occupancy-threshold\">30%", looseHtml);
        Assert.Contains("data-testid=\"predicted-high-park\" data-code=\"CP001\" data-span=\"all\"", looseHtml);
        Assert.Contains("data-testid=\"predicted-high-park\" data-code=\"CP002\" data-span=\"all\"", looseHtml);
        Assert.Contains("data-testid=\"current-high-park\" data-code=\"CP001\"", looseHtml);
        Assert.Contains("data-testid=\"current-high-park\" data-code=\"CP002\"", looseHtml);
    }

    [Fact]
    public async Task Unknown_car_park_page_returns_404()
    {
        await using var factory = ApiFactory.WithStore(new FlatHistoryStore());
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/carparks/NOPE");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("data-testid=\"not-found\"", html);
    }

    [Fact]
    public async Task Health_and_openapi_stay_available()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var health = await client.GetAsync("/health");
        health.EnsureSuccessStatusCode();
        Assert.Contains("net10.0", await health.Content.ReadAsStringAsync());

        var openApi = await client.GetAsync("/openapi/v1.json");
        openApi.EnsureSuccessStatusCode();
        var document = await openApi.Content.ReadAsStringAsync();
        Assert.Contains("/api/carparks", document);
        Assert.Contains("/health", document);
    }

    private const string HistoricalBaselinePredictorName = "HistoricalBaseline";

    private sealed class FlatHistoryStore : ICarParkReadStore
    {
        private static readonly (string Code, int Occupied)[] Parks =
        [
            ("CP001", 37),
            ("CP002", 82)
        ];

        public Task<IReadOnlyList<string>> GetCarParkCodesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>(Parks.Select(park => park.Code).ToArray());

        public Task<bool> ExistsAsync(string carParkCode, CancellationToken cancellationToken) =>
            Task.FromResult(Parks.Any(park => park.Code.Equals(carParkCode, StringComparison.OrdinalIgnoreCase)));

        public Task<IReadOnlyList<OccupancyObservation>> GetHistoryAsync(
            string carParkCode,
            string? countingCategory,
            DateTimeOffset asOf,
            CancellationToken cancellationToken)
        {
            var match = Parks.FirstOrDefault(item => item.Code.Equals(carParkCode, StringComparison.OrdinalIgnoreCase));
            if (match.Code is null)
            {
                return Task.FromResult<IReadOnlyList<OccupancyObservation>>([]);
            }

            IReadOnlyList<OccupancyObservation> history =
            [
                new()
                {
                    SnapshotTime = asOf.AddHours(-1),
                    Occupied = match.Occupied,
                    Capacity = 100
                },
                new()
                {
                    SnapshotTime = asOf.AddHours(-2),
                    Occupied = match.Occupied,
                    Capacity = 100
                }
            ];
            return Task.FromResult(history);
        }
    }

    private sealed class ApiFactory : WebApplicationFactory<Program>
    {
        private readonly ICarParkReadStore? _store;
        private readonly bool _forceSqlStore;
        private readonly bool _sampleSnapshots;
        private readonly double? _highOccupancyThreshold;

        public ApiFactory()
            : this(store: null, forceSqlStore: true, sampleSnapshots: false, highOccupancyThreshold: null)
        {
        }

        private ApiFactory(
            ICarParkReadStore? store,
            bool forceSqlStore,
            bool sampleSnapshots,
            double? highOccupancyThreshold)
        {
            _store = store;
            _forceSqlStore = forceSqlStore;
            _sampleSnapshots = sampleSnapshots;
            _highOccupancyThreshold = highOccupancyThreshold;
            Environment.SetEnvironmentVariable("Ui__ApiTimeoutSeconds", "15");
            Environment.SetEnvironmentVariable("CarParkData__Source", "Sql");
            Environment.SetEnvironmentVariable(
                "CarParkData__UseSampleSnapshots",
                sampleSnapshots ? "true" : null);
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        }

        public static ApiFactory WithStore(ICarParkReadStore store, double? highOccupancyThreshold = null) =>
            new(store, forceSqlStore: false, sampleSnapshots: false, highOccupancyThreshold);

        public static ApiFactory WithSampleSnapshots() =>
            new(store: null, forceSqlStore: false, sampleSnapshots: true, highOccupancyThreshold: null);

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("CarParkData:Source", "Sql");
            builder.UseSetting("ConnectionStrings:CarParkDb", "");
            builder.UseSetting("CarParkData:UseSampleSnapshots", _sampleSnapshots ? "true" : "false");
            builder.UseSetting("Ui:ApiTimeoutSeconds", "15");
            if (_highOccupancyThreshold is double threshold)
            {
                builder.UseSetting(
                    "CarParkData:HighOccupancyThresholdPercent",
                    threshold.ToString(CultureInfo.InvariantCulture));
            }

            if (_store is not null || _forceSqlStore)
            {
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<ICarParkReadStore>();
                    if (_store is not null)
                    {
                        services.AddSingleton(_store);
                    }
                    else
                    {
                        services.RemoveAll<IOccupancySnapshotSource>();
                        services.RemoveAll<IDbConnectionFactory>();
                        services.RemoveAll<SqlObjectNames>();
                        services.AddSingleton<IDbConnectionFactory, SqlConnectionFactory>();
                        services.AddSingleton(sp => new SqlObjectNames(sp.GetRequiredService<IOptions<CarParkDataOptions>>().Value));
                        services.AddScoped<IOccupancySnapshotSource, SqlServerOccupancySnapshotSource>();
                        services.AddScoped<ICarParkReadStore, OccupancySnapshotReadStore>();
                    }
                });
            }
        }

        protected override void ConfigureClient(HttpClient client)
        {
            var hook = Services.GetRequiredService<CarParkApiHandlerOverride>();
            hook.HandlerFactory ??= () => Server.CreateHandler();
            base.ConfigureClient(client);
        }

        public override async ValueTask DisposeAsync()
        {
            Environment.SetEnvironmentVariable("CarParkData__Source", null);
            if (_sampleSnapshots)
            {
                Environment.SetEnvironmentVariable("CarParkData__UseSampleSnapshots", null);
            }

            await base.DisposeAsync();
        }
    }
}
